using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// A Discover game's own page (apps/web DiscoverGame.tsx), natively, in a Playnite window, laid out as the site's: Back,
/// the hero (cover, title, facts, summary, answers with Undo, Play / Install when the game is in Playnite, the price
/// link), then For you, friends who played it, accessibility, more about the game and More like this. No fingerprint card:
/// the site's Discover page has none (Blastlog is the reference).
/// A game already in the player's library that's paired in Playnite opens Playnite's own game page instead (the site opens its library page).
/// </summary>
static class DiscoverGamePage {
  static readonly ILogger Log = LogManager.GetLogger();

  /// <summary>
  /// Opens the page. <paramref name="shelf"/> is the Discover shelf it was opened from (each shelf's pickups), null when
  /// opened some other way; <paramref name="done"/> runs after the window closes if anything was answered or undone.
  /// </summary>
  public static void Open(BlastlogPlugin plugin, string gameId, string shelf, Action done) {
    try {
      var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
      var owner = plugin.Api.Dialogs.GetCurrentAppWindow();
      window.Title = "Discover";
      window.Width = Math.Max(760, Math.Min(1180, (owner?.ActualWidth ?? 1200) * 0.86));
      window.Height = Math.Max(600, Math.Min(980, (owner?.ActualHeight ?? 900) * 0.9));
      window.Owner = owner; window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
      var view = new DiscoverGameView(plugin, gameId, shelf, window);
      window.Content = Kit.Page(view);
      window.PreviewKeyDown += (s, e) => { try { if (e.Key == Key.Escape) window.Close(); } catch (Exception ex) { Log.Error(ex, "Blastlog Discover game close"); } };
      window.ShowDialog();
      if (view.AfterClose != null) view.AfterClose();
      if (view.Changed) done?.Invoke();
    } catch (Exception e) { Log.Error(e, "Blastlog Discover game page"); }
  }
}

sealed partial class DiscoverGameView : UserControl {
  static readonly ILogger Log = LogManager.GetLogger();
  readonly BlastlogPlugin plugin; readonly Window window;
  // More like this opens the next game in this same window, so these change.
  string gameId, openedFrom;
  readonly StackPanel body = new StackPanel { MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Left };
  JObject data, page, info, subs, like; bool loaded, worn;
  DiscoverAnswered notice; string noticeText;

  /// <summary>Anything answered or undone here (the page that opened it refreshes).</summary>
  public bool Changed { get; private set; }
  /// <summary>What to do once the window is shut (open the game in Playnite's library, start it).</summary>
  public Action AfterClose { get; private set; }

  public DiscoverGameView(BlastlogPlugin plugin, string gameId, string shelf, Window window) {
    this.plugin = plugin; this.gameId = gameId; openedFrom = shelf; this.window = window;
    Content = body;
    body.Children.Add(PnKit.Text("Opening the game…", "Blastlog.Muted"));
    Loaded += (s, e) => { try { if (!loaded) { loaded = true; _ = Load(false); } } catch (Exception ex) { Log.Error(ex, "Blastlog Discover game"); } };
  }

  async Task Load(bool fresh) {
    try {
      if (!plugin.Configured) { Show(Kit.Card(PnKit.Text("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see this game."))); return; }
      var read = plugin.Mirror($"discover/games/{gameId}", fresh); var services = plugin.Mirror("subscriptions", false);
      var similar = plugin.Mirror($"games/{gameId}/like", fresh);
      await Task.WhenAll(read, services, similar);
      data = read.Result.Value; subs = services.Result.Value; like = similar.Result.Value;
      page = data?["page"] as JObject; info = data?["info"] as JObject;
      if (page == null) { Unavailable(); return; }
      // A game that's in the library opens its library page, here Playnite's own game page when it's paired there.
      // Not right after answering here (the site's rule), so the answer and its Undo stay in view.
      var entryId = page.S("entryId");
      var paired = entryId != null ? plugin.GameFor(entryId) : null;
      if (paired != null && !(page["answer"] is JObject) && noticeText == null) { OpenInPlaynite(paired.Id); return; }
      Build();
    } catch (Exception e) { Log.Error(e, "Blastlog Discover game load"); }
  }

  void Show(UIElement content) { body.Children.Clear(); body.Children.Add(content); }

  void Unavailable() {
    var panel = new StackPanel();
    panel.Children.Add(PnKit.Text("This game couldn’t be opened.", "Blastlog.Body", "Blastlog.Danger"));
    panel.Children.Add(Kit.Button("Try again", async () => await Load(true), "Blastlog.Ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 12, 0, 0); }));
    Show(panel);
  }

  void OpenInPlaynite(Guid id) {
    AfterClose = () => { try { plugin.Api.MainView.SwitchToLibraryView(); plugin.Api.MainView.SelectGame(id); } catch (Exception e) { Log.Error(e, "Blastlog open in Playnite"); } };
    window.Close();
  }

  BlastlogGame Game => new BlastlogGame { Id = page?["game"].S("id") ?? gameId, Title = page?["game"].S("title"), CoverUrl = page?["game"].S("coverUrl"), Accent = page?["game"].S("accent") };
  string Title => page?["game"].S("title") ?? "this game";
  bool NotOut { get { var d = page?["game"].S("releaseDate"); return d != null && string.CompareOrdinal(d, GamePage.Today()) > 0; } }
  /// <summary>The shelf it was opened from, or the site's best guess when opened some other way.</summary>
  string Shelf => openedFrom ?? (page.Objects("friends").Count > 0 ? "friends" : page["catalogue"].D("trendingRank") > 0 ? "trending" : NotOut ? "upcoming" : "critics");

  void Build() {
    try {
      body.Children.Clear();
      window.Title = Title;
      // The page wears its game: the page colour set on Blastlog, else its cover's (design.md 2.3).
      if (!worn) { worn = true; Kit.GameScope(body, plugin, Game); }
      body.Children.Add(Fingerprint.LinkButton(openedFrom != null ? "← Back to Discover" : "← Back", () => window.Close()).Also(b => { b.Margin = new Thickness(-10, 0, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; }));
      body.Children.Add(Hero());
      var fit = FitCard(); if (fit != null) body.Children.Add(fit);
      var friends = FriendsCard(); if (friends != null) body.Children.Add(friends);
      var access = AccessibilityCard(); if (access != null) body.Children.Add(access);
      var more = MoreCard(); if (more != null) body.Children.Add(more);
      var similar = MoreLikeThis(); if (similar != null) body.Children.Add(similar);
      body.Children.Add(Fingerprint.LinkButton("Open on Blastlog ↗", () => UiKit.Open(plugin.ServerUrl + "/discover/game/" + Game.Id), game: true).Also(b => { b.Margin = new Thickness(-10, 0, 0, 6); b.HorizontalAlignment = HorizontalAlignment.Left; }));
      body.Children.Add(PnKit.Text(plugin.CacheLine(), "Blastlog.Small"));
    } catch (Exception e) { Log.Error(e, "Blastlog Discover game build"); }
  }

  // ---- The hero ----
  FrameworkElement Hero() {
    var game = page["game"] as JObject; var fit = page["fit"] as JObject; var catalogue = page["catalogue"] as JObject;
    var entryId = page.S("entryId"); var answer = page["answer"] as JObject;

    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    var cover = Kit.Cover(plugin, Game, 260, ArtworkContext.Cover, 18);
    cover.VerticalAlignment = VerticalAlignment.Top;
    AutomationProperties.SetName(cover, Title + " cover");
    grid.Children.Add(cover);

    var text = new StackPanel(); Grid.SetColumn(text, 2); grid.Children.Add(text);
    text.Children.Add(PnKit.Text("Discover", "Blastlog.Body", "Blastlog.GameText", FontWeights.Bold));
    text.Children.Add(PnKit.Text(Title, "Blastlog.PageTitle").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 2, 0, 6); }));
    var date = game.S("releaseDate"); var predicted = fit.D("predictedRating"); var acclaim = catalogue.D("acclaim");
    var facts = new[] {
      date != null ? (NotOut ? PnKit.ReleaseLabel(date) : game.S("year")) : null,
      predicted != null ? $"predicted {predicted.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}★" : null,
      fit.B("earlyRead") ? "early read" : null,
      acclaim != null ? $"{(catalogue.S("acclaimKind") == "critics" ? "Critics" : "Rated")} {Math.Round(acclaim.Value)} on IGDB" : null,
      catalogue.D("trendingRank") > 0 ? "Trending this week" : null }.Where(x => !string.IsNullOrEmpty(x)).ToList();
    if (facts.Count > 0) text.Children.Add(PnKit.Text(string.Join(" · ", facts), "Blastlog.Muted"));
    var chip = PlayNextSubscriptions.Chip(subs, Game.Id); if (chip != null) text.Children.Add(chip.Also(c => c.Margin = new Thickness(0, 8, 0, 0)));
    var summary = info.S("summary");
    if (!string.IsNullOrEmpty(summary)) text.Children.Add(Summary(summary));
    if (noticeText != null) text.Children.Add(NoticeBar().Also(n => n.Margin = new Thickness(0, 12, 0, 0)));

    if (entryId != null) {
      var line = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
      line.Children.Add(PnKit.Text("It’s in your library.", "Blastlog.Body").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.FontSize = 15; }));
      var paired = plugin.GameFor(entryId);
      line.Children.Add(Fingerprint.LinkButton(paired != null ? "Open it in Playnite →" : "Open it there ↗", () => { if (paired != null) OpenInPlaynite(paired.Id); else UiKit.Open(plugin.ServerUrl + "/library/" + entryId); }, game: true));
      text.Children.Add(line);
    } else if (answer != null) text.Children.Add(AnsweredLine(answer));

    // One row of buttons: Play / Install (Playnite's), the answers, then the price check.
    var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
    var play = PlayOrInstall(entryId); if (play != null) buttons.Children.Add(play);
    var deal = info.S("dealUrl");
    if (deal != null) buttons.Children.Add(Kit.Button("Compare prices on GG.deals ↗", () => UiKit.Open(deal), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
    if (entryId == null && answer == null) {
      var answers = new DiscoverAnswers(plugin, Game.Id, Title, Shelf, NotOut, Answered) { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
      text.Children.Add(answers);
      if (buttons.Children.Count > 0) buttons.Margin = new Thickness(0, 6, 0, 0);
    }
    if (buttons.Children.Count > 0) text.Children.Add(buttons);

    grid.Margin = new Thickness(0, 12, 0, 22);
    return grid;
  }

  /// <summary>The summary, whole, as on the site.</summary>
  static FrameworkElement Summary(string summary) {
    var t = PnKit.Text(summary, "Blastlog.Body").Also(x => { x.FontSize = 16; x.TextWrapping = TextWrapping.Wrap; x.MaxWidth = 820; x.HorizontalAlignment = HorizontalAlignment.Left; x.Margin = new Thickness(0, 12, 0, 12); });
    t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; t.LineHeight = 25;
    return t;
  }
  /// <summary>Pictures come from IGDB's image server only (the plugin's one other host, README).</summary>
  internal static bool IsIgdb(string url) => Uri.TryCreate(url?.StartsWith("//") == true ? "https:" + url : url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "images.igdb.com";

  /// <summary>Play (installed) or Install for a game the player has in Playnite; the window shuts first.</summary>
  Button PlayOrInstall(string entryId) {
    var game = entryId != null ? plugin.GameFor(entryId) : null;
    if (game == null) return null;
    var id = game.Id; var installed = game.IsInstalled;
    var b = PnKit.Btn(PnKit.IconWord(installed ? "<polygon points=\"6 3 20 12 6 21 6 3\" />" : "<path d=\"M12 15V3\" /><path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4\" /><path d=\"m7 10 5 5 5-5\" />", installed ? "Play" : "Install"),
      () => { AfterClose = () => { try { if (installed) plugin.Api.StartGame(id); else plugin.Api.InstallGame(id); } catch (Exception e) { Log.Error(e, "Blastlog play"); } }; window.Close(); return Task.CompletedTask; },
      "Blastlog.Primary", (installed ? "Play " : "Install ") + Title);
    b.Margin = new Thickness(0, 0, 8, 8);
    return b;
  }
}
}
