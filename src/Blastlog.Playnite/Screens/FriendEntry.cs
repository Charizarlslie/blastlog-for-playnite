using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// A friend's library entry (the site's GameDetails for someone else's entry, apps/web App.tsx), natively, in a Playnite
/// window, in the site's order: Back, the hero (cover, ownership, title, summary, platforms, status, length, finished,
/// the price link), the fingerprint, For you (your own verdict), their take (stars, taps, review, reactions) beside the
/// game's details, your other friends on it, accessibility, trailers and screenshots, then More like this. Read only, as
/// on the site, apart from reactions. One read for the page (GET entries/:id?like=0); More like this comes separately.
/// </summary>
static class FriendEntryPage {
  static readonly ILogger Log = LogManager.GetLogger();

  /// <summary>Opens the page; <paramref name="done"/> runs after it closes if a reaction changed.</summary>
  public static void Open(BlastlogPlugin plugin, string entryId, string owner, Action done) {
    try {
      var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
      var parent = plugin.Api.Dialogs.GetCurrentAppWindow();
      window.Title = owner != null ? owner + "’s game" : "A friend’s game";
      window.Width = Math.Max(760, Math.Min(1180, (parent?.ActualWidth ?? 1200) * 0.86));
      window.Height = Math.Max(600, Math.Min(980, (parent?.ActualHeight ?? 900) * 0.9));
      window.Owner = parent; window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
      var view = new FriendEntryView(plugin, entryId, owner, window);
      window.Content = Kit.Page(view);
      window.PreviewKeyDown += (s, e) => { try { if (e.Key == Key.Escape) window.Close(); } catch (Exception ex) { Log.Error(ex, "Blastlog friend entry close"); } };
      window.ShowDialog();
      view.AfterClose?.Invoke();
      if (view.Changed) done?.Invoke();
    } catch (Exception e) { Log.Error(e, "Blastlog friend entry"); }
  }
}

sealed class FriendEntryView : UserControl {
  static readonly ILogger Log = LogManager.GetLogger();
  readonly BlastlogPlugin plugin; readonly string entryId; readonly Window window;
  readonly StackPanel body = new StackPanel { MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Left };
  string owner;
  JObject page, picks, reactions, fit, subs, like; JArray friends; bool loaded, worn;

  /// <summary>A reaction was added or taken back here (the dashboard row refreshes).</summary>
  public bool Changed { get; private set; }
  /// <summary>What to do once the window is shut (open a game in Playnite's library).</summary>
  public Action AfterClose { get; private set; }

  public FriendEntryView(BlastlogPlugin plugin, string entryId, string owner, Window window) {
    this.plugin = plugin; this.entryId = entryId; this.owner = owner; this.window = window;
    Content = body;
    body.Children.Add(PnKit.Text("Opening the game…", "Blastlog.Muted"));
    Loaded += (s, e) => { try { if (!loaded) { loaded = true; _ = Load(false); } } catch (Exception ex) { Log.Error(ex, "Blastlog friend entry"); } };
  }

  JObject Entry => page?["entry"] as JObject;
  JObject Info => page?["info"] as JObject;
  string GameId => Entry?["game"].S("id");
  string Title => Entry?["game"].S("title") ?? "this game";
  string Owner => owner ?? "Their";
  BlastlogGame Game => new BlastlogGame { Id = GameId, Title = Entry?["game"].S("title"), CoverUrl = Entry?["game"].S("coverUrl"), Accent = Entry?["game"].S("accent") };

  async Task Load(bool fresh) {
    try {
      var read = await plugin.Mirror($"entries/{entryId}?like=0", fresh);
      page = read.Value;
      if (Entry == null || GameId == null) { Unavailable(); return; }
      Build();
      // The rest as it arrives: their taps, reactions, your verdict, your other friends, subscriptions, then More like this (slow).
      var gameId = GameId; var userId = Entry.S("userId");
      var likeRead = plugin.Mirror($"games/{gameId}/like", fresh);
      var reads = new[] { plugin.Mirror($"entries/{entryId}/picks", fresh), plugin.Mirror($"entries/{entryId}/reactions", fresh), plugin.Mirror($"games/{gameId}/fit", fresh),
        plugin.Mirror($"games/{gameId}/friends", fresh), plugin.Mirror("subscriptions", false) };
      var names = owner == null ? plugin.Mirror("friends", false) : null;
      await Task.WhenAll(reads);
      picks = reads[0].Result.Value; reactions = reads[1].Result.Value; fit = reads[2].Result.Value; subs = reads[4].Result.Value;
      // Your other friends on it: everyone but the entry's owner (the site's FriendsOnGame).
      friends = new JArray((reads[3].Result.Value?["items"] as JArray ?? new JArray()).OfType<JObject>().Where(f => f.S("userId") != userId));
      if (names != null) {
        var known = ((await names).Value?["friends"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(f => (f.S("id") ?? f.S("userId")) == userId);
        owner = known?.S("displayName") ?? owner;
      }
      Build();
      like = (await likeRead).Value;
      if (like != null) Build();
    } catch (Exception e) { Log.Error(e, "Blastlog friend entry load"); }
  }

  void Unavailable() {
    var panel = new StackPanel();
    panel.Children.Add(PnKit.Text("This game couldn’t be opened. It may have been removed, or you’re no longer friends.", "Blastlog.Body", "Blastlog.Danger").Also(t => t.TextWrapping = TextWrapping.Wrap));
    panel.Children.Add(Kit.Button("Try again", async () => await Load(true), "Blastlog.Ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 12, 0, 0); }));
    body.Children.Clear(); body.Children.Add(panel);
  }

  void Build() {
    try {
      body.Children.Clear();
      window.Title = $"{Owner}’s {Title}";
      // The page wears its game (design.md 2.3).
      if (!worn) { worn = true; Kit.GameScope(body, plugin, Game); }
      body.Children.Add(Fingerprint.LinkButton("← Back", () => window.Close()).Also(b => { b.Margin = new Thickness(-10, 0, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; }));
      body.Children.Add(Hero());
      body.Children.Add(Fingerprint.Card(FingerprintData.Parse(page["fingerprint"]), new FingerprintOptions { ServerUrl = plugin.ServerUrl, SiteGameUrl = plugin.ServerUrl + "/library/" + entryId,
        OpenTrait = t => Nav.Trait(plugin, t.Id, $"{Owner}’s {Title}") }));
      var forYou = GamePageElement.ForYou(fit); if (forYou != null) body.Children.Add(forYou);
      body.Children.Add(GamePageElement.TwoUp(Take(), GamePageElement.Facts(Entry, Info)));
      if (friends != null && friends.Count > 0) body.Children.Add(GamePageElement.Friends(friends));
      var access = GameMore.Accessibility(Info, Title); if (access != null) body.Children.Add(access);
      var more = GameMore.Media(plugin, Info, Title); if (more != null) body.Children.Add(more);
      var similar = MoreLikeThis(); if (similar != null) body.Children.Add(similar);
      body.Children.Add(Fingerprint.LinkButton("Open on Blastlog ↗", () => UiKit.Open(plugin.ServerUrl + "/library/" + entryId), game: true).Also(b => { b.Margin = new Thickness(-10, 0, 0, 6); b.HorizontalAlignment = HorizontalAlignment.Left; }));
      body.Children.Add(PnKit.Text(plugin.CacheLine(), "Blastlog.Small"));
    } catch (Exception e) { Log.Error(e, "Blastlog friend entry build"); }
  }

  // ---- The hero ----
  FrameworkElement Hero() {
    var entry = Entry; var game = entry["game"] as JObject;
    var grid = new Grid { Margin = new Thickness(0, 12, 0, 22) };
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    var cover = Kit.Cover(plugin, Game, 260, ArtworkContext.Cover, 18);
    cover.VerticalAlignment = VerticalAlignment.Top;
    AutomationProperties.SetName(cover, Title + " cover");
    grid.Children.Add(cover);

    var text = new StackPanel(); Grid.SetColumn(text, 2); grid.Children.Add(text);
    var ownership = entry.S("ownership");
    text.Children.Add(PnKit.Text(ownership == "wishlist" ? "Wishlist" : ownership == "library" ? "In library" : "Unknown", "Blastlog.Body", "Blastlog.GameText", FontWeights.Bold));
    text.Children.Add(PnKit.Text(Title, "Blastlog.PageTitle").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 2, 0, 6); }));
    var summary = game.S("summary") ?? Info.S("summary") ?? "A game in the Blastlog library.";
    text.Children.Add(PnKit.Text(summary, "Blastlog.Body").Also(t => { t.FontSize = 16; t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 820; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 6, 0, 12);
      t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; t.LineHeight = 25; }));

    // The site's meta chips: platforms, status, length, when they finished it, the subscription it's on.
    var chips = new WrapPanel();
    var platforms = game.Strings("releasePlatforms"); if (platforms.Count == 0) platforms = Info.Strings("platforms");
    chips.Children.Add(Chip(platforms.Count == 0 ? "Platforms not available" : string.Join(", ", platforms.Take(4)) + (platforms.Count > 4 ? $" +{platforms.Count - 4}" : ""), platforms.Count > 4 ? string.Join(", ", platforms) : null));
    var status = entry.S("status");
    chips.Children.Add(Chip(GamePage.StatusWord(ownership == "wishlist" && status == "not_started" ? "wishlist" : status)));
    chips.Children.Add(Chip(LengthChip(entry["length"] as JObject)));
    var finished = entry.S("finishedOn");
    if (!string.IsNullOrEmpty(finished)) chips.Children.Add(Chip("Finished " + (DateTime.TryParse(finished, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) : finished)));
    var sub = PlayNextSubscriptions.Chip(subs, GameId); if (sub != null) chips.Children.Add(sub.Also(c => c.Margin = new Thickness(0, 0, 8, 8)));
    text.Children.Add(chips);

    var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
    var deal = Info.S("dealUrl");
    if (deal != null) buttons.Children.Add(Kit.Button("Compare prices on GG.deals ↗", () => UiKit.Open(deal), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
    if (buttons.Children.Count > 0) text.Children.Add(buttons);
    return grid;
  }
  static FrameworkElement Chip(string words, string tip = null) => Kit.Chip(words, neutral: true).Also(c => { c.Margin = new Thickness(0, 0, 8, 8); if (tip != null) c.ToolTip = tip; });
  /// <summary>The site's length chip: "About 30 hours Main + extras", "No end", or "Length unknown".</summary>
  static string LengthChip(JObject length) {
    if (length == null) return "Length unknown";
    if (length.B("noEnd")) return "No end";
    var hours = length.D("hours"); if (hours == null) return "Length unknown";
    var h = hours.Value < 10 ? Math.Round(hours.Value * 2, MidpointRounding.AwayFromZero) / 2 : Math.Round(hours.Value, MidpointRounding.AwayFromZero);
    var words = $"About {h.ToString("0.#", CultureInfo.InvariantCulture)} hours";
    return length.S("fromLevel") != length.S("level") ? words : words + " " + GamePage.LevelWord(length.S("level"));
  }

  // ---- Their take ----
  /// <summary>
  /// "{Name}’s take": their stars, the taps (Worked / Held back), their review, a report link (on the site), and your
  /// reactions (web review-panel: PicksSummary, ReportReview, EntryReactions).
  /// </summary>
  FrameworkElement Take() {
    var entry = Entry;
    var stack = new StackPanel();
    stack.Children.Add(Kit.Text(Owner + "’s take", "Blastlog.Small").Also(t => { t.FontWeight = FontWeights.Bold; t.Margin = new Thickness(0, 0, 0, 6); }));
    var rating = (int?)entry.D("rating"); var review = entry.S("review")?.Trim();
    var stars = Kit.Text(rating == null ? "Not rated yet" : new string('★', rating.Value) + new string('☆', 5 - rating.Value));
    if (rating != null) { stars.FontSize = 26; stars.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Star"); AutomationProperties.SetName(stars, $"{rating} of 5 stars"); }
    else stars.Style = Kit.Style("Blastlog.Muted");
    stack.Children.Add(stars);
    var taps = picks.Objects("picks");
    foreach (var side in new[] { ("worked", "Worked"), ("held_back", "Held back") }) {
      var list = taps.Where(p => p.S("side") == side.Item1 && !string.IsNullOrEmpty(p.S("label"))).ToList();
      if (list.Count == 0) continue;
      stack.Children.Add(Kit.Text(side.Item2, "Blastlog.Small").Also(t => { t.FontWeight = FontWeights.Bold; t.Margin = new Thickness(0, 10, 0, 6); }));
      var w = new WrapPanel(); foreach (var p in list) w.Children.Add(Kit.Chip(p.S("label"), neutral: side.Item1 != "worked").Also(c => c.Margin = new Thickness(0, 0, 8, 8)));
      stack.Children.Add(w);
    }
    if (!string.IsNullOrEmpty(review)) {
      stack.Children.Add(Kit.Text("“" + review + "”").Also(t => { t.FontStyle = FontStyles.Italic; t.TextWrapping = TextWrapping.Wrap; t.LineHeight = 25; t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; t.Margin = new Thickness(0, 10, 0, 0); }));
      // Reporting goes through the site (the mirror has no write for it).
      stack.Children.Add(Fingerprint.LinkButton("Report ↗", () => UiKit.Open(plugin.ServerUrl + "/library/" + entryId)).Also(b => { b.Margin = new Thickness(-10, 4, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; AutomationProperties.SetName(b, $"Report {Owner}’s review on Blastlog"); }));
    } else if (taps.Count == 0) stack.Children.Add(Kit.Text("No written review yet.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 10, 0, 0)));
    var choices = reactions.Strings("choices");
    if (choices.Count > 0 && reactions.B("canReact"))
      stack.Children.Add(DashboardLibrary.Reactions(plugin, entryId, $"{Owner}’s {Title}", choices, reactions.Objects("reactions"), () => Changed = true));
    return Kit.Card(stack).Also(c => c.VerticalAlignment = VerticalAlignment.Stretch);
  }

  // ---- More like this ----
  /// <summary>
  /// More like this (the site's MoreLikeThis, under every game page): up to 12, six at first. One in Playnite opens
  /// Playnite's game page, one in your library opens there, any other its Discover page.
  /// </summary>
  FrameworkElement MoreLikeThis() {
    var items = like.Objects("items");
    if (items.Count == 0) return null;
    var card = DiscoverGameView.Card("More like " + Title, out var cardBody);
    cardBody.Children.Add(PnKit.Text("Games you haven’t played yet that play the most like it, from your library, your friends’ games and Discover. Your taste breaks close calls.", "Blastlog.Muted")
      .Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, -6, 0, 12); }));
    var row = new WrapPanel(); var shown = 6;
    void Fill() {
      row.Children.Clear();
      foreach (var item in items.Take(shown)) row.Children.Add(Tile(item));
      if (items.Count > shown) row.Children.Add(Fingerprint.LinkButton($"Show {items.Count - shown} more", () => { shown = items.Count; Fill(); }, game: true).Also(b => b.VerticalAlignment = VerticalAlignment.Center));
    }
    Fill();
    cardBody.Children.Add(row);
    return card;
  }
  FrameworkElement Tile(JObject item) {
    var game = new BlastlogGame { Id = item.S("gameId"), Title = item.S("title"), CoverUrl = item.S("coverUrl") };
    var mine = item.S("entryId"); var inPlaynite = mine != null ? plugin.GameFor(mine) : null;
    var stack = new StackPanel { Width = 132 };
    stack.Children.Add(Kit.Cover(plugin, game, 132, ArtworkContext.Thumbnail, 14));
    stack.Children.Add(PnKit.Text(game.Title ?? "", "Blastlog.Small", "Blastlog.Text", FontWeights.Bold).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxHeight = 40; t.TextTrimming = TextTrimming.CharacterEllipsis; t.Margin = new Thickness(0, 8, 0, 0); }));
    var predicted = item.D("predictedRating");
    var source = item.S("source") == "library" ? "Your library" : item.S("source") == "friends" ? "Friends" : "Discover";
    stack.Children.Add(PnKit.Text(predicted != null ? $"{source} · ★ {predicted.Value.ToString("0.0", CultureInfo.InvariantCulture)}" : source, "Blastlog.Small"));
    var button = PnKit.Bare(stack, () => {
      if (inPlaynite != null) { var id = inPlaynite.Id; AfterClose = () => { try { plugin.Api.MainView.SwitchToLibraryView(); plugin.Api.MainView.SelectGame(id); } catch (Exception e) { Log.Error(e, "Blastlog open in Playnite"); } }; window.Close(); }
      else if (mine != null) Nav.Entry(plugin, mine);
      else Nav.DiscoverGame(plugin, game.Id);
    }, "Open " + game.Title);
    button.Width = 132; button.Margin = new Thickness(0, 0, 14, 14); button.VerticalAlignment = VerticalAlignment.Top; button.HorizontalAlignment = HorizontalAlignment.Left;
    var reasons = item.Strings("reasons"); var watch = item.S("watchOut"); var shared = item.Strings("shared");
    var tip = string.Join("\n", new[] { shared.Count > 0 ? "Shares: " + string.Join(", ", shared) : null, reasons.Count > 0 ? "Because you like " + string.Join(", ", reasons) : null,
      watch != null ? "Watch out: " + watch : null, inPlaynite != null ? "In your Playnite library" : null }.Where(x => x != null));
    if (tip.Length > 0) button.ToolTip = tip;
    Kit.GameScope(button, plugin, game);
    return button;
  }
}
}
