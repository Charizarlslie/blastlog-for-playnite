using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>Play next (design.md 8 "Play next"; PLAYNITE_DESIGN.md 4.4).</summary>
public sealed class PlayNextScreen : IBlastlogScreen {
  public string Title => "Play next";
  public string Icon => Icons.Star;
  public int Order => 30;
  public FrameworkElement Create(BlastlogPlugin plugin) => new PlayNextView(plugin);
}

/// <summary>
/// The site's Play next (apps/web Recommendations.tsx), natively: the length and ownership filters, a line that jumps to
/// Coming up, Tends to love / dislike, the You're playing strips, then the picks as masonry cards that each wear their
/// game (rank, status, predicted stars, length, Instant yes / Never, boosts with Not now, Because you like, a line from
/// your review, Watch out, early fingerprint). #1 is the wide card with Start playing / Not now, with Sharpen your picks
/// under it beside picks 2 and 3. 48 picks at a time, the next batch loading as you near the end. Playnite adds Play or
/// Install on every pick you have in Playnite.
/// </summary>
sealed class PlayNextView : UserControl {
  static readonly ILogger Log = LogManager.GetLogger();
  const int Batch = 48;
  /// <summary>The length choice lasts while Playnite runs (the site keeps it for the browser session).</summary>
  static string choice = "any";
  readonly BlastlogPlugin plugin; readonly StackPanel body = new StackPanel();
  JObject data, help, subs; int limit = Batch; bool autoLoad = true, loaded;
  string doneText; Func<Task> doneUndo; Button more; FrameworkElement comingShelf; ScrollViewer viewer;

  public PlayNextView(BlastlogPlugin plugin) {
    this.plugin = plugin; Content = body;
    body.Children.Add(PnKit.Text("Matching games to your taste…", "Blastlog.Muted"));
    Loaded += (s, e) => {
      try {
        if (viewer == null) { viewer = PnKit.FindAncestor<ScrollViewer>(this); if (viewer != null) viewer.ScrollChanged += (o, a) => NearEnd(); }
        if (!loaded) { loaded = true; _ = Load(false); }
      } catch (Exception ex) { Log.Error(ex, "Blastlog Play next"); }
    };
  }

  async Task Load(bool fresh) {
    try {
      if (!plugin.Configured) { body.Children.Clear(); body.Children.Add(Kit.Card(PnKit.Text("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see your picks."))); return; }
      data = (await plugin.Mirror("play-next", fresh)).Value;
      help = (await plugin.Mirror("taste-help", fresh)).Value;
      subs = (await plugin.Mirror("subscriptions", fresh)).Value;
      Build();
      if (help.B("refreshing")) RefreshHelpSoon(0);
    } catch (Exception e) { Log.Error(e, "Blastlog Play next"); }
  }
  /// <summary>Sharpen your picks sends its last result while a fresh one is worked out; ask again shortly (the site polls every 3 s).</summary>
  void RefreshHelpSoon(int tries) {
    if (tries >= 5) return;
    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
    timer.Tick += async (s, e) => {
      timer.Stop();
      try { help = (await plugin.Mirror("taste-help", true)).Value; Build(); if (help.B("refreshing")) RefreshHelpSoon(tries + 1); } catch (Exception ex) { Log.Error(ex, "Blastlog taste help"); }
    };
    timer.Start();
  }

  // ---- Filters, remembered like the site (owned-only per player on this device) ----
  string SettingsFile => Path.Combine(plugin.GetPluginUserDataPath(), "play-next.json");
  JObject Saved() { try { return File.Exists(SettingsFile) ? JObject.Parse(File.ReadAllText(SettingsFile)) : new JObject(); } catch (Exception) { return new JObject(); } }
  void Save(string key, bool value) { try { var o = Saved(); o[key] = value; File.WriteAllText(SettingsFile, o.ToString(Formatting.None)); } catch (Exception) { } }
  bool OwnedOnly { get => Saved().B("ownedOnly"); set => Save("ownedOnly", value); }
  /// <summary>"On my subscriptions" (Blastlog docs/SUBSCRIPTIONS.md): only games the player's services include.</summary>
  bool ServicesOnly { get => Saved().B("servicesOnly"); set => Save("servicesOnly", value); }
  static bool Owned(JObject item) => item.S("ownership") != "wishlist";
  static bool Fits(JObject item, string choice, double quickMax, double longMin, string level) {
    if (choice == "any") return true;
    var length = item["length"] as JObject;
    if (length == null) return false;
    if (length.B("noEnd")) return choice == "long";
    var hours = length.D("hours"); if (hours == null) return false;
    var levels = new[] { "main_story", "main_plus_extra", "completionist" };
    int shown = Array.IndexOf(levels, length.S("fromLevel")), wanted = Array.IndexOf(levels, length.S("level") ?? level);
    return choice == "quick" ? hours <= quickMax && shown >= wanted : hours >= longMin && shown <= wanted;
  }

  void Build() {
    body.Children.Clear(); more = null; comingShelf = null;
    if (data == null) { body.Children.Add(PnKit.Text("Recommendations are unavailable right now.", "Blastlog.Body", "Blastlog.Danger")); return; }
    var ready = data.B("ready"); var ownedOnly = OwnedOnly;
    var hasServices = subs.Objects("services").Any(s => s.B("on")); var servicesOnly = ServicesOnly && hasServices;
    var thresholds = data["lengthChoice"] as JObject;
    var quickMax = thresholds.D("quickMaxHours") ?? 12; var longMin = thresholds.D("longMinHours") ?? 40; var level = thresholds.S("level") ?? "main_plus_extra";
    var items = data.Objects("items");
    var pool = (ownedOnly ? items.Where(Owned) : items).Where(i => !servicesOnly || PlayNextSubscriptions.OnMine(subs, i.S("gameId"))).ToList();
    var fitting = pool.Where(i => Fits(i, choice, quickMax, longMin, level)).ToList();
    // A game you're already playing sits in its own strip, never at #1 (design.md 8).
    var playing = fitting.Where(i => i.S("status") == "playing").ToList();
    var picks = fitting.Where(i => i.S("status") != "playing").ToList();
    // "Not now": the best pick that isn't snoozed goes first; the rest keep their order.
    var topIndex = picks.FindIndex(i => i.S("snoozedUntil") == null);
    var shown = topIndex > 0 ? new[] { picks[topIndex] }.Concat(picks.Take(topIndex)).Concat(picks.Skip(topIndex + 1)).ToList() : picks;
    var unknown = pool.Count(i => i["length"] == null || i["length"].Type == JTokenType.Null);
    var comingUp = data.Objects("comingUp").Where(i => !ownedOnly || Owned(i)).ToList();
    var comingHidden = data.Objects("comingUp").Count - comingUp.Count;
    var wishlisted = items.Count(i => !Owned(i));

    // Header
    body.Children.Add(PnKit.Text("For you", "Blastlog.Body", "Blastlog.GameText", FontWeights.Bold));
    body.Children.Add(PnKit.Text("What to play next", "Blastlog.PageTitle").Also(t => t.FontSize = 32));
    var ranked = PnKit.Line("Blastlog.Muted", PnKit.Run($"Ranked from {(int)(data.D("ratedWithFingerprints") ?? 0)} rated games. "));
    var taste = new System.Windows.Documents.Hyperlink(PnKit.Run("See your taste", "Blastlog.GameText", FontWeights.SemiBold)) { TextDecorations = null };
    taste.Click += (s, e) => UiKit.Open(plugin.ServerUrl + "/profile");
    ranked.Inlines.Add(taste); ranked.Margin = new Thickness(0, 4, 0, 14);
    body.Children.Add(ranked);

    // Filters
    var filters = new WrapPanel();
    filters.Children.Add(PnKit.Segmented("How long a game", new[] {
      Tuple.Create("any", "Any length", (string)null),
      Tuple.Create("quick", "Quick one", $"Beatable in about {quickMax:0.#} hours or less ({PnKit.LevelLabel(level)})"),
      Tuple.Create("long", "Long haul", $"{longMin:0.#} hours or more ({PnKit.LevelLabel(level)}), or no end") }, choice, c => { choice = c; limit = Batch; Build(); }));
    filters.Children.Add(PnKit.Segmented("Which games", new[] {
      Tuple.Create("all", "Wishlist too", (string)null),
      Tuple.Create("owned", "Only what I own", wishlisted > 0 ? $"Leaves out {wishlisted} wishlist game{(wishlisted == 1 ? "" : "s")}" : null) },
      ownedOnly ? "owned" : "all", c => { OwnedOnly = c == "owned"; limit = Batch; Build(); }));
    if (hasServices) filters.Children.Add(PnKit.Segmented("Subscriptions", new[] { Tuple.Create("on", "On my subscriptions", (string)null) },
      servicesOnly ? "on" : "", c => { ServicesOnly = !servicesOnly; limit = Batch; Build(); }));
    body.Children.Add(filters);

    // Coming up is after the picks, a long scroll away; one line says it's there and jumps to it.
    if (comingUp.Count > 0) {
      // One line of text, so the link and the rest share a baseline.
      var first = comingUp[0];
      var go = new System.Windows.Documents.Hyperlink(PnKit.Run($"Coming up: {(comingUp.Count == 1 ? "1 game" : comingUp.Count + " games")} not out yet ↓", "Blastlog.GameText", FontWeights.SemiBold)) { TextDecorations = null };
      go.Click += (s, e) => { try { autoLoad = false; comingShelf?.BringIntoView(); comingShelf?.Focus(); } catch (Exception ex) { Log.Error(ex, "Blastlog Coming up"); } };
      var next = first.S("releaseDate") != null ? ", " + PnKit.ReleaseLabel(first.S("releaseDate")).Replace("Out", "out") : "";
      var jump = PnKit.Line("Blastlog.Muted", go, PnKit.Run($"  Next is {first.S("title")}{next}."));
      jump.MinHeight = 44; jump.Padding = new Thickness(0, 12, 0, 12); jump.Margin = new Thickness(0, 0, 0, 2);
      body.Children.Add(jump);
    } else if (comingHidden > 0) body.Children.Add(PnKit.Text($"{(comingHidden == 1 ? "1 wishlist game is" : comingHidden + " wishlist games are")} coming up; “Wishlist too” shows {(comingHidden == 1 ? "it" : "them")}.", "Blastlog.Muted"));

    if (!ready) body.Children.Add(Note("Not enough rated games have fingerprints yet — rankings will appear once at least 5 do.", warn: true));
    if (ready) {
      var tastes = new WrapPanel { Margin = new Thickness(0, 14, 0, 6) };
      tastes.Children.Add(TasteGroup("Tends to love", data.Objects("likes"), true));
      tastes.Children.Add(TasteGroup("Tends to dislike", data.Objects("dislikes"), false));
      body.Children.Add(tastes);
    }
    if (choice != "any" && shown.Count == 0)
      body.Children.Add(Note((choice == "quick" ? $"Nothing here takes {quickMax:0.#} hours or less" : $"Nothing here takes {longMin:0.#} hours or more") + $" ({PnKit.LevelLabel(level)})."
        + (unknown > 0 ? $" {(unknown == 1 ? "1 game has" : unknown + " games have")} no length yet, so {(unknown == 1 ? "it can’t" : "they can’t")} show here." : "")));
    if (choice == "any" && servicesOnly && shown.Count == 0) body.Children.Add(Note("Nothing here is on your subscriptions right now."));
    if (choice == "any" && ownedOnly && !servicesOnly && shown.Count == 0) body.Children.Add(Note("Nothing you own is waiting to be played. Switch to “Wishlist too” to see the rest."));

    if (playing.Count > 0) {
      var strips = new CardGrid { MinColumn = 420, Gap = 12, Fit = true, Margin = new Thickness(0, 4, 0, 18) };
      foreach (var item in playing) strips.Children.Add(PlayingStrip(item));
      body.Children.Add(strips);
    }
    if (doneText != null) {
      var undo = doneUndo;
      body.Children.Add(PnKit.Notice(doneText, new[] { undo == null ? null : Kit.Button("Undo", async () => await Run(async () => { await undo(); return "Undone."; }, null), "Blastlog.Ghost") },
        () => { doneText = null; doneUndo = null; Build(); }));
    }

    var grid = new CardGrid { Scaled = true, Masonry = true, Margin = new Thickness(0, 4, 0, 16) };
    for (var i = 0; i < Math.Min(limit, shown.Count); i++) {
      var card = Pick(shown[i], i, ready);
      if (i == 0) CardGrid.SetWide(card, true);
      grid.Children.Add(card);
      if (i == 0 && help != null) { var sharpen = PlayNextSharpen.Build(plugin, help, () => _ = Load(true)); if (sharpen != null) { CardGrid.SetWide(sharpen, true); grid.Children.Add(sharpen); } }
    }
    body.Children.Add(grid);
    if (shown.Count > limit) {
      more = Kit.Button($"Show more ({shown.Count - limit} left)", () => { autoLoad = true; limit += Batch; Build(); }, "Blastlog.Ghost");
      more.HorizontalAlignment = HorizontalAlignment.Left; more.Margin = new Thickness(0, 0, 0, 16);
      body.Children.Add(more);
    }
    if (shown.Count > Batch && limit >= shown.Count) body.Children.Add(PnKit.Text($"That’s all {shown.Count} picks.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 0, 0, 16)));
    if (comingUp.Count > 0) body.Children.Add(comingShelf = ComingUp(comingUp, ready));
    body.Children.Add(PnKit.Text(plugin.CacheLine(), "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 12, 0, 0)));
  }

  /// <summary>Presses Show more when it comes within a screen of view (the site's automatic batches).</summary>
  void NearEnd() {
    try {
      if (!autoLoad || more == null || viewer == null || !more.IsVisible) return;
      var y = more.TranslatePoint(new Point(0, 0), viewer).Y;
      if (y < viewer.ViewportHeight * 2) { more = null; limit += Batch; Dispatcher.BeginInvoke(new Action(() => { try { Build(); } catch (Exception e) { Log.Error(e, "Blastlog Play next"); } })); }
    } catch (Exception e) { Log.Error(e, "Blastlog Play next scroll"); }
  }

  static FrameworkElement Note(string text, bool warn = false) {
    var t = PnKit.Text(text, "Blastlog.Body", warn ? "Blastlog.Warn" : "Blastlog.Text"); t.FontSize = 15;
    return new Border { Child = t, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 12) }.Res(Border.BackgroundProperty, "Blastlog.Surface2");
  }

  static FrameworkElement TasteGroup(string title, List<JObject> items, bool love) {
    var panel = new StackPanel { Margin = new Thickness(0, 0, 32, 4), MaxWidth = 720 };
    panel.Children.Add(PnKit.Text(title, "Blastlog.Body", null, FontWeights.Bold).Also(t => { t.FontSize = 15; t.Margin = new Thickness(0, 0, 0, 10); }));
    var chips = new WrapPanel();
    foreach (var item in items) chips.Children.Add(PnKit.TasteChip(item.S("label"), love, $"{(int)(item.D("count") ?? 0)} rated games"));
    if (items.Count == 0) chips.Children.Add(PnKit.Text("Nothing yet.", "Blastlog.Small"));
    panel.Children.Add(chips);
    return panel;
  }

  /// <summary>One strip per game you're playing, in its own tint, with Open (the approved board) and Play / Install.</summary>
  FrameworkElement PlayingStrip(JObject item) {
    var row = new DockPanel();
    var art = PnKit.Art(plugin, item.S("gameId"), item.S("coverUrl"), new CornerRadius(8), 44); art.Margin = new Thickness(0, 0, 14, 0);
    DockPanel.SetDock(art, Dock.Left); row.Children.Add(art);
    var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    var play = PnKit.PlayOrInstall(plugin, item.S("entryId")); if (play != null) buttons.Children.Add(play);
    var open = Kit.Button("Open", () => PnKit.OpenEntry(plugin, item.S("entryId")), "Blastlog.Ghost"); open.Margin = new Thickness(0);
    System.Windows.Automation.AutomationProperties.SetName(open, "Open " + item.S("title"));
    buttons.Children.Add(open);
    DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
    var length = PnKit.LengthText(item["length"]);
    if (length != null) { var meta = PnKit.Text(length + " to beat", "Blastlog.Small"); meta.VerticalAlignment = VerticalAlignment.Center; meta.Margin = new Thickness(10, 0, 14, 0); DockPanel.SetDock(meta, Dock.Right); row.Children.Add(meta); }
    var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
    words.Children.Add(PnKit.Text("You’re playing", "Blastlog.Small", "Blastlog.GameText", FontWeights.Bold));
    words.Children.Add(PnKit.Text(item.S("title"), "Blastlog.Body", null, FontWeights.ExtraBold).Also(t => { t.FontSize = 17; t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; t.ToolTip = t.Text; }));
    row.Children.Add(words);
    var strip = PnKit.GameCard(plugin, item.S("gameId"), item.S("coverUrl"), row);
    strip.Padding = new Thickness(12, 12, 16, 12);
    return strip;
  }

  /// <summary>A pick's card (the site's pn-tile): cover with rank and status, then everything the site shows.</summary>
  FrameworkElement Pick(JObject item, int index, bool ready) {
    var first = index == 0; var entryId = item.S("entryId"); var title = item.S("title");
    var artGrid = new Grid();
    artGrid.Children.Add(PnKit.Art(plugin, item.S("gameId"), item.S("coverUrl"), first ? new CornerRadius(17, 0, 0, 17) : new CornerRadius(17, 17, 0, 0)));
    artGrid.Children.Add(PnKit.Rank(index + 1));
    artGrid.Children.Add(PnKit.OverPill(item.S("ownership") == "wishlist" ? "Wishlist" : StatusWord(item.S("status")), null, HorizontalAlignment.Right, VerticalAlignment.Top));
    var art = PnKit.Bare(artGrid, () => PnKit.OpenEntry(plugin, entryId), $"{index + 1}. {title}");
    art.VerticalAlignment = VerticalAlignment.Top;

    var text = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
    if (first) text.Children.Add(PnKit.Text("Your best bet right now", "Blastlog.Small", "Blastlog.GameText", FontWeights.Bold).Also(t => t.Margin = new Thickness(0, 0, 0, 4)));
    var name = PnKit.Bare(PnKit.Text(title, first ? "Blastlog.CardTitle" : "Blastlog.Body", null, FontWeights.Bold).Also(t => { if (!first) t.FontSize = 15; }), () => PnKit.OpenEntry(plugin, entryId), title);
    text.Children.Add(name);
    var facts = PnKit.Line("Blastlog.Small", PnKit.Run(string.Join(" · ", new[] { ready ? $"predicted {item.D("predictedRating") ?? 0:0.0}★" : "not scored", PnKit.LengthText(item["length"]),
      (item.D("totalTraits") ?? 0) == 0 ? "not fingerprinted yet" : null }.Where(x => x != null))));
    if (item.B("earlyFingerprint") && (item.D("totalTraits") ?? 0) > 0) {
      facts.Inlines.Add(PnKit.Run(" · early fingerprint"));
      facts.ToolTip = "Fingerprinted before it came out, from previews and store pages. It counts a little less until it's refreshed after release.";
    }
    facts.Margin = new Thickness(0, 2, 0, 6); text.Children.Add(facts);
    var chip = PlayNextSubscriptions.Chip(subs, item.S("gameId")); if (chip != null) text.Children.Add(chip);
    var mustHaves = item.Strings("mustHaves"); var dealbreakers = item.Strings("dealbreakers");
    if (mustHaves.Count > 0) text.Children.Add(Para(PnKit.Text("Instant yes: " + string.Join(", ", mustHaves), "Blastlog.Body", "Blastlog.Ok", FontWeights.Bold)));
    if (dealbreakers.Count > 0) text.Children.Add(Para(PnKit.Line("Blastlog.Body", PnKit.Run("Never: ", "Blastlog.Danger", FontWeights.Bold), PnKit.Run(string.Join(", ", dealbreakers), "Blastlog.Danger"))));
    var boost = item["boost"] as JObject;
    if (boost != null) {
      var line = new WrapPanel();
      line.Children.Add(PnKit.Text(boost.S("text"), "Blastlog.Body").Also(t => { t.FontSize = 14; t.VerticalAlignment = VerticalAlignment.Center; }));
      line.Children.Add(Kit.Button("Not now", async () => await DropBoost(boost), "Blastlog.TextButton").Also(b => { b.MinHeight = 44; b.Margin = new Thickness(4, 0, 0, 0); }));
      text.Children.Add(line);
    }
    var reasons = item.Strings("reasons");
    if (reasons.Count > 0) text.Children.Add(Para(PnKit.Line("Blastlog.Body", PnKit.Run("Because you like ", "Blastlog.TextMuted"), PnKit.Run(string.Join(", ", reasons)))));
    var like = item.Objects("quotes").FirstOrDefault(q => q.S("tone") == "like");
    if (like != null) text.Children.Add(PnKit.Quote(like.S("quote"), "your review of " + like.S("gameTitle"), 3).Also(q => { q.Margin = new Thickness(0, 2, 0, 6); q.ToolTip = "About " + like.S("label"); }));
    var watch = item.Strings("dislikes").Concat(item.Strings("risks")).Distinct().Where(l => !dealbreakers.Contains(l)).ToList();
    if (watch.Count > 0) text.Children.Add(Para(PnKit.Text("Watch out: " + string.Join(", ", watch), "Blastlog.Body", "Blastlog.Danger")));

    var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
    var startable = first && Owned(item) && (item.S("status") == "not_started" || item.S("status") == "started");
    if (first) {
      if (startable) actions.Children.Add(Kit.Button("Start playing", async () => await StartPlaying(item)));
      actions.Children.Add(Kit.Button("Not now", async () => await NotNow(item), "Blastlog.Ghost"));
    }
    var play = PnKit.PlayOrInstall(plugin, entryId, primary: !startable);
    if (play != null) actions.Children.Add(play);
    foreach (UIElement b in actions.Children) ((FrameworkElement)b).Margin = new Thickness(0, 0, 8, 8);
    if (actions.Children.Count > 0) text.Children.Add(actions);

    UIElement content;
    if (first) {
      var g = new Grid();
      g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
      g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
      Grid.SetColumn(text, 1); g.Children.Add(art); g.Children.Add(text);
      content = g;
    } else content = new StackPanel { Children = { art, text } };
    return PnKit.GameCard(plugin, item.S("gameId"), item.S("coverUrl"), content);
  }
  static FrameworkElement Para(TextBlock t) { t.FontSize = 14; t.Margin = new Thickness(0, 0, 0, 6); return t; }
  static string StatusWord(string status) => status == "not_started" ? "Not started" : status == "started" ? "Started" : status == "playing" ? "Playing" : status == "done" ? "Done"
    : status == "set_aside" ? "Set aside" : status == "quit" ? "Quit" : status == "dnf" ? "Didn’t finish" : status == "unplayable" ? "Unplayable" : (status ?? "").Replace('_', ' ');

  /// <summary>Games on the list that aren't out yet, soonest first, with early guesses.</summary>
  FrameworkElement ComingUp(List<JObject> items, bool ready) {
    var section = new StackPanel { Margin = new Thickness(0, 18, 0, 0), Focusable = true, FocusVisualStyle = null };
    System.Windows.Automation.AutomationProperties.SetName(section, "Coming up");
    section.Children.Add(PnKit.Text("Coming up", "Blastlog.Section").Also(t => t.Margin = new Thickness(0, 0, 0, 4)));
    section.Children.Add(PnKit.Text("On the list but not out yet, soonest first. Ratings are early guesses from previews; they firm up after release.", "Blastlog.Muted"));
    var list = new CardGrid { MinColumn = 320, Gap = 12, Margin = new Thickness(0, 12, 0, 0) };
    foreach (var item in items) {
      var row = new DockPanel();
      var art = PnKit.Art(plugin, item.S("gameId"), item.S("coverUrl"), new CornerRadius(8), 44); art.Margin = new Thickness(0, 0, 14, 0); art.VerticalAlignment = VerticalAlignment.Top;
      DockPanel.SetDock(art, Dock.Left); row.Children.Add(art);
      var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
      words.Children.Add(PnKit.Text(item.S("title"), "Blastlog.Body", null, FontWeights.Bold));
      words.Children.Add(PnKit.Text(string.Join(" · ", new[] { item.S("releaseDate") != null ? PnKit.ReleaseLabel(item.S("releaseDate")) : "Not out yet",
        ready && (item.D("totalTraits") ?? 0) > 0 ? $"early guess {item.D("predictedRating") ?? 0:0.0}★" : null }.Where(x => x != null)), "Blastlog.Small"));
      var reasons = item.Strings("reasons"); if (reasons.Count > 0) words.Children.Add(PnKit.Text("Looks like " + string.Join(", ", reasons.Take(3)), "Blastlog.Small"));
      var risks = item.Strings("dislikes").Concat(item.Strings("risks")).Distinct().Take(2).ToList();
      if (risks.Count > 0) words.Children.Add(PnKit.Text("Might have: " + string.Join(", ", risks), "Blastlog.Small", "Blastlog.Danger"));
      row.Children.Add(words);
      var tile = new Border { Child = row, CornerRadius = new CornerRadius(16), Padding = new Thickness(12) }.Res(Border.BackgroundProperty, "Blastlog.Surface");
      var entryId = item.S("entryId");
      list.Children.Add(PnKit.Bare(tile, () => PnKit.OpenEntry(plugin, entryId), item.S("title")));
    }
    section.Children.Add(list);
    return section;
  }

  // ---- #1's buttons and boosts: what happened, with Undo (design.md 8) ----
  async Task Run(Func<Task<string>> work, Func<Task> undo) {
    string text;
    try { text = await work(); } catch (Exception e) { Log.Error(e, "Blastlog Play next action"); text = "That didn’t save. Try again."; undo = null; }
    if (text == null) return; // the plugin already said what went wrong
    doneText = text; doneUndo = undo;
    await Load(true);
  }
  async Task<JObject> SetStatus(string entryId, string status) {
    var entry = (await plugin.Mirror("entries/" + entryId, true)).Value?["entry"];
    var version = (int?)entry?["version"];
    if (version == null) { plugin.Api.Dialogs.ShowErrorMessage("Couldn't read that game from Blastlog. Try again.", "Blastlog"); return null; }
    return await plugin.Write("entries/" + entryId, new JObject { ["expectedVersion"] = version, ["status"] = status }, new HttpMethod("PATCH"));
  }
  Task StartPlaying(JObject item) {
    var id = item.S("entryId"); var before = item.S("status"); var title = item.S("title");
    return Run(async () => await SetStatus(id, "playing") == null ? null : $"{title} is in Currently playing.", async () => { await SetStatus(id, before); });
  }
  Task NotNow(JObject item) {
    var id = item.S("entryId"); var title = item.S("title");
    return Run(async () => await plugin.Write("play-next/snoozes", new JObject { ["entryId"] = id }) == null ? null : $"{title} won’t be your top pick for two weeks.",
      async () => { await plugin.Write("play-next/snoozes/" + id, new JObject(), HttpMethod.Delete); });
  }
  Task DropBoost(JObject boost) {
    var body = new JObject { ["kind"] = boost.S("kind"), ["key"] = boost.S("key") };
    return Run(async () => await plugin.Write("play-next/boost-dismissals", body) == null ? null : "That boost is off. Your taste is untouched.",
      async () => { await plugin.Write("play-next/boost-dismissals", body, HttpMethod.Delete); });
  }
}
}
