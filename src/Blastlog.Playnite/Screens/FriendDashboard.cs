using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// A friend's dashboard (the site's Dashboard for /friends/:id/dashboard, read only): "Maya’s dashboard", a search of
/// their library, the top card (their Now playing while they're playing something, with Next up for them beside their
/// library; otherwise Next up for them, with Currently playing beside it), their library's
/// numbers, and Recently finished. No prompts, + New entry, co-op button or friend activity (the site leaves those to
/// your own dashboard). Every game opens as their entry (Nav.FriendEntry); the numbers and Open library open their
/// Library tab filtered (<c>openLibrary</c>, the site's query).
/// Reads: users/:id/dashboard ({ stats, nowPlaying } as the own /dashboard, or the stats alone), users/:id/next-up?mode=,
/// and their library (FriendData).
/// </summary>
sealed class FriendDashboardView : StackPanel {
  static readonly ILogger Log = LogManager.GetLogger();
  static readonly (string key, string label)[] Modes = { ("recommended", "Recommended"), ("quick", "Quick one"), ("long", "Long haul") };
  readonly BlastlogPlugin plugin; readonly FriendView friend; readonly Action<string> openLibrary;
  readonly ContentControl hero = Slot(), playing = Slot(), library = Slot(), shelf = Slot();
  JObject stats, next; List<Entry> entries; bool nextRead, entriesRead, started;
  string mode = "recommended";

  public FriendDashboardView(BlastlogPlugin plugin, FriendView friend, Action<string> openLibrary) {
    this.plugin = plugin; this.friend = friend; this.openLibrary = openLibrary;
    MaxWidth = 1500;
    Children.Add(Header());
    Children.Add(hero);
    Children.Add(Columns(playing, library));
    Children.Add(shelf);
    Loaded += (s, e) => { if (started) return; started = true; Run(() => Load(false), "friend dashboard"); };
  }

  static ContentControl Slot() => new ContentControl { Focusable = false, IsTabStop = false };
  string NextPath => friend.Path("next-up?mode=" + mode);

  async Task Load(bool fresh) {
    // Saved copies first, then the server's answers.
    UseDashboard(plugin.SavedRead(friend.Path("dashboard"))); next = plugin.SavedRead(NextPath) ?? next;
    var saved = FriendData.SavedEntries(plugin, friend); if (saved.Count > 0) entries = saved;
    DrawAll();
    var dash = plugin.Mirror(friend.Path("dashboard"), fresh); var up = plugin.Mirror(NextPath, fresh); var list = FriendData.Entries(plugin, friend, fresh);
    UseDashboard((await dash).Value);
    next = (await up).Value ?? next; nextRead = true;
    entries = await list ?? entries; entriesRead = true;
    DrawAll();
  }
  void UseDashboard(JObject value) { if (value != null) stats = value["stats"] as JObject ?? (value["total"] != null ? value : stats); }

  async Task SwitchMode(string to) {
    if (mode == to) return;
    mode = to; next = plugin.SavedRead(NextPath); nextRead = false; DrawHero();
    var answer = (await plugin.Mirror(NextPath, false)).Value;
    if (mode != to) return;
    next = answer ?? next; nextRead = true; DrawHero();
  }

  void DrawAll() { DrawHero(); Draw(library, () => DashboardLibrary.LibraryCard(plugin, stats, friend, openLibrary)); Draw(shelf, Shelf); }
  // The top spot (design.md 8 "Top card"): their Now playing while they're playing something, Next up for them beside their
  // library; otherwise Next up on top and their (empty) Currently playing beside it.
  void DrawHero() {
    if (PlayingList().Count > 0) { Draw(hero, NowPlayingHero); Draw(playing, Hero); }
    else { Draw(hero, Hero); Draw(playing, Playing); }
  }
  static void Draw(ContentControl slot, Func<FrameworkElement> build) {
    try { slot.Content = build(); } catch (Exception e) { Log.Error(e, "Blastlog friend dashboard: drawing"); }
  }

  // ---- Header: "Maya’s dashboard" and a search of their library ----
  FrameworkElement Header() {
    var grid = new DockPanel { Margin = new Thickness(0, 0, 0, 22), LastChildFill = true };
    var search = Dash.Field();
    Dash.AutomationName(search, $"Search {friend.Name}’s library");
    var field = Dash.FieldBox(search, "Search any game"); field.Width = 340; field.VerticalAlignment = VerticalAlignment.Center;
    search.PreviewKeyDown += (s, e) => {
      try {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var words = search.Text.Trim();
        openLibrary(words.Length == 0 ? null : "search=" + Uri.EscapeDataString(words));
      } catch (Exception ex) { Log.Error(ex, "Blastlog friend dashboard search"); }
    };
    DockPanel.SetDock(field, Dock.Right); grid.Children.Add(field);
    grid.Children.Add(new StackPanel { Children = { Dash.Text(friend.Name + "’s dashboard", "Blastlog.PageTitle"), Dash.Text(friend.Name + "’s library at a glance", "Blastlog.Muted").Margin(0, 2, 0, 0) } });
    return grid;
  }

  // ---- Next up for them: cover, title, the predicted stars and why it fits them ----
  FrameworkElement Hero() {
    var panel = new StackPanel();
    var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 16) };
    head.Children.Add(Dash.Text("Next up for " + friend.Name, "Blastlog.CardTitle").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    var modes = PnKit.Segmented("Next up", Modes.Select(m => Tuple.Create(m.key, m.label, (string)null)), mode, key => Run(() => SwitchMode(key), "friend next up mode"));
    DockPanel.SetDock(modes, Dock.Right); head.Children.Add(modes);
    panel.Children.Add(head);

    var entry = next?["entry"] as JObject;
    if (entry == null) {
      panel.Children.Add(Dash.Text(!nextRead && next == null ? "Loading…"
        : mode == "quick" ? $"Nothing in {friend.Name}’s backlog is short enough for a quick one."
        : mode == "long" ? $"Nothing in {friend.Name}’s backlog is long enough for a long haul."
        : $"{friend.Name} has nothing in their backlog to pick from.", "Blastlog.Muted"));
      return Kit.Card(panel, game: true).Also(c => c.Margin = new Thickness(0, 0, 0, 18));
    }
    var game = Dash.Game(entry["game"]); var title = game?.Title ?? ""; var id = S(entry, "id") ?? S(entry, "entryId");
    var row = new DockPanel { LastChildFill = true };
    var cover = Kit.Cover(plugin, game, 150, ArtworkContext.Cover, 14);
    var coverButton = PnKit.Bare(cover, () => Nav.FriendEntry(plugin, id, friend.Name), $"{friend.Name}’s {title}");
    coverButton.Margin = new Thickness(0, 0, 22, 0); coverButton.VerticalAlignment = VerticalAlignment.Top;
    DockPanel.SetDock(coverButton, Dock.Left); row.Children.Add(coverButton);

    var body = new StackPanel();
    body.Children.Add(Dash.Text(title, "Blastlog.HeroTitle").Also(t => t.TextWrapping = TextWrapping.Wrap));
    var facts = new List<string>();
    var length = Dash.LengthText(entry["length"]); if (length != null) facts.Add(length);
    var status = S(entry, "status"); if (status != null) facts.Add(Dash.StatusWord(status));
    var platform = S(entry, "platform"); if (!string.IsNullOrEmpty(platform)) facts.Add(platform);
    if (facts.Count > 0) body.Children.Add(Dash.Text(string.Join(" · ", facts), "Blastlog.Muted").Margin(0, 4, 0, 0));
    var explanation = next["explanation"] as JObject;
    var predicted = D(explanation, "predictedRating");
    if (predicted != null) body.Children.Add(Runs("Blastlog.Body", ("Blastlog thinks they’d give it ", false, null), (Stars(Math.Round(predicted.Value * 10) / 10), true, "Blastlog.Star")).Also(t => t.Margin = new Thickness(0, 12, 0, 0)));
    var reasons = Arr(explanation?["reasons"]).Select(r => (string)r).Where(r => !string.IsNullOrWhiteSpace(r)).Take(3).ToList();
    var risks = Arr(explanation?["risks"]).Select(r => (string)r).Where(r => !string.IsNullOrWhiteSpace(r)).Take(2).ToList();
    if (reasons.Count > 0) body.Children.Add(Bullets("Why it fits them", reasons, "Blastlog.Ok"));
    if (risks.Count > 0) body.Children.Add(Bullets("Watch out", risks, "Blastlog.Warn"));
    body.Children.Add(Link("See their entry →", () => Nav.FriendEntry(plugin, id, friend.Name)).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(-6, 12, 0, 0); }));
    row.Children.Add(body);
    panel.Children.Add(row);
    var card = Kit.Card(panel, game: true); card.Margin = new Thickness(0, 0, 0, 18);
    Kit.GameScope(card, plugin, game);
    return card;
  }
  static FrameworkElement Bullets(string head, List<string> lines, string brush) {
    var s = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
    s.Children.Add(T(head, "Blastlog.Small", "Blastlog.Text", 0, FontWeights.Bold));
    foreach (var line in lines) s.Children.Add(Runs("Blastlog.Body", ("● ", false, brush), (line, false, null)).Also(t => t.Margin = new Thickness(0, 4, 0, 0)));
    return s;
  }

  // ---- Now playing for them: the same top card as your own, without the buttons (web PlayingHero, friend view) ----
  string shownId;
  /// <summary>Their Playing games in their library's order (newest change first; their own order and pick aren't shared).</summary>
  List<Entry> PlayingList() => (entries ?? new List<Entry>()).Where(e => e.Status == "playing")
    .OrderByDescending(e => e.UpdatedAt).ThenBy(e => e.Game?.Title, StringComparer.CurrentCulture).ToList();

  FrameworkElement NowPlayingHero() {
    var list = PlayingList();
    var shown = list.FirstOrDefault(e => e.EntryId == shownId) ?? list[0];
    var title = shown.Game?.Title ?? ""; var id = shown.EntryId;
    // Covers to look through; nothing is saved for a friend's card.
    var strip = list.Count > 1 ? DashHero.Strip(plugin, list.Select(e => new DashHero.StripGame { Id = e.EntryId, Game = e.Game, Title = e.Game?.Title ?? "" }).ToList(),
      id, pick => { shownId = pick; Draw(hero, NowPlayingHero); }, null) : null;
    var copy = new StackPanel();
    copy.Children.Add(Dash.Text(title, "Blastlog.HeroTitle").Also(t => t.TextWrapping = TextWrapping.Wrap).Margin(0, 26, 0, 14));
    var length = shown.NoEnd ? "No end" : shown.Length != null ? "~" + shown.Length.Hours.ToString(CultureInfo.InvariantCulture) + "h" : null;
    if (length != null) copy.Children.Add(Dash.Wrap(DashHero.Chip(Dash.Clock, length, "How long it takes to beat")).Margin(0, 0, 0, 6));
    copy.Children.Add(DashHero.Links(title).Margin(0, 12, 0, 0));
    var details = Kit.Button("View details", () => Nav.FriendEntry(plugin, id, friend.Name));
    details.Content = Dash.IconLabel(Dash.Eye, "View details"); details.Margin = new Thickness(0, 18, 0, 0); details.HorizontalAlignment = HorizontalAlignment.Left;
    Dash.AutomationName(details, $"View {friend.Name}’s {title}");
    copy.Children.Add(details);
    return DashHero.Frame(plugin, shown.Game, DashHero.TopRow(DashHero.Label("Now playing"), strip), copy, "playing", $"Cover of {title}",
      () => Nav.FriendEntry(plugin, id, friend.Name), scope: true);
  }

  // ---- Currently playing: their Playing games, newest first ----
  FrameworkElement Playing() {
    var panel = new StackPanel();
    panel.Children.Add(Dash.Text("Currently playing", "Blastlog.CardTitle").Margin(0, 0, 0, 14));
    var list = (entries ?? new List<Entry>()).Where(e => e.Status == "playing").OrderByDescending(e => e.UpdatedAt).ThenBy(e => e.Game?.Title, StringComparer.CurrentCulture).ToList();
    if (entries == null) panel.Children.Add(Dash.Text(entriesRead ? $"Couldn’t load {friend.Name}’s library." : "Loading…", "Blastlog.Muted"));
    else if (list.Count == 0) panel.Children.Add(Dash.Text($"{friend.Name} isn’t playing anything right now.", "Blastlog.Muted"));
    foreach (var e in list.Take(6)) panel.Children.Add(PlayingRow(e));
    if (list.Count > 6) panel.Children.Add(Link($"All {list.Count} →", () => openLibrary("status=playing")).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(-6, 4, 0, 0); }));
    return Dash.Card(panel).Also(c => c.Margin = new Thickness(0, 0, 0, 18));
  }
  FrameworkElement PlayingRow(Entry e) {
    var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10), MinHeight = 56 };
    var cover = Kit.Cover(plugin, e.Game, 44, ArtworkContext.Thumbnail, 8); cover.Margin = new Thickness(0, 0, 14, 0); cover.VerticalAlignment = VerticalAlignment.Center;
    DockPanel.SetDock(cover, Dock.Left); row.Children.Add(cover);
    var facts = new[] { e.NoEnd ? "No end" : e.Length != null ? "~" + e.Length.Hours.ToString(CultureInfo.InvariantCulture) + "h" : null, e.Platform }.Where(x => !string.IsNullOrEmpty(x));
    row.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = {
      Dash.Text(e.Game?.Title ?? "", "Blastlog.Body", weight: FontWeights.SemiBold).Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis),
      Dash.Text(string.Join(" · ", facts), "Blastlog.Small") } });
    return PnKit.Bare(row, () => Nav.FriendEntry(plugin, e.EntryId, friend.Name), $"{friend.Name}’s {e.Game?.Title}").Also(b => b.HorizontalContentAlignment = HorizontalAlignment.Stretch);
  }

  // ---- Recently finished: their latest finishes with stars; See all opens their library by finish date ----
  FrameworkElement Shelf() {
    var panel = new StackPanel();
    var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 14) };
    head.Children.Add(Dash.Text("Recently finished", "Blastlog.CardTitle").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    var all = Dash.Link("See all →", () => openLibrary("status=completed&sort=finished&direction=desc")); DockPanel.SetDock(all, Dock.Right); head.Children.Add(all);
    panel.Children.Add(head);
    var finished = (entries ?? new List<Entry>()).Where(e => LibraryFilters.Terminal.Contains(e.Status ?? "") && !string.IsNullOrEmpty(e.FinishedOn))
      .OrderByDescending(e => e.FinishedOn, StringComparer.Ordinal).Take(12).ToList();
    if (entries == null) panel.Children.Add(Dash.Text(entriesRead ? $"Couldn’t load {friend.Name}’s library." : "Loading…", "Blastlog.Muted"));
    else if (finished.Count == 0) panel.Children.Add(Dash.Text($"{friend.Name} hasn’t finished anything yet.", "Blastlog.Muted"));
    else {
      var line = new WrapPanel();
      foreach (var e in finished) line.Children.Add(FinishedTile(e));
      panel.Children.Add(line);
    }
    return Dash.Card(panel);
  }
  FrameworkElement FinishedTile(Entry e) {
    const double width = 132;
    var tile = new StackPanel { Width = width };
    tile.Children.Add(Kit.Cover(plugin, e.Game, width, ArtworkContext.Thumbnail, 14));
    tile.Children.Add(Dash.Text(e.Game?.Title ?? "", "Blastlog.Body", weight: FontWeights.SemiBold).Also(t => { t.Margin = new Thickness(0, 8, 0, 2); t.MaxHeight = 44; t.TextTrimming = TextTrimming.CharacterEllipsis; }));
    var meta = new WrapPanel();
    if (e.Rating != null) meta.Children.Add(Dash.Text("★" + e.Rating.Value.ToString("0.#", CultureInfo.InvariantCulture), "Blastlog.Small", "Blastlog.Star", FontWeights.Bold).Margin(0, 0, 8, 0));
    meta.Children.Add(Dash.Text(Dash.ShortDate(e.FinishedOn), "Blastlog.Small"));
    tile.Children.Add(meta);
    if (!string.IsNullOrWhiteSpace(e.Review)) tile.ToolTip = new ToolTip { Content = new TextBlock { Text = e.Review.Trim(), MaxWidth = 340, TextWrapping = TextWrapping.Wrap } };
    var button = PnKit.Bare(tile, () => Nav.FriendEntry(plugin, e.EntryId, friend.Name), $"{friend.Name}’s {e.Game?.Title}{(e.Rating != null ? $", {e.Rating} stars" : "")}");
    button.Margin = new Thickness(0, 0, 14, 14); button.VerticalAlignment = VerticalAlignment.Top;
    Kit.GameScope(tile, plugin, e.Game);
    return button;
  }

  /// <summary>Two cards side by side on a wide window (3:2), one above the other on a narrow one (as the own dashboard).</summary>
  static FrameworkElement Columns(FrameworkElement left, FrameworkElement right) {
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.Children.Add(left); grid.Children.Add(right);
    void Lay(bool wide) {
      Grid.SetColumn(left, 0); Grid.SetRow(left, 0); Grid.SetColumnSpan(left, wide ? 1 : 2);
      Grid.SetColumn(right, wide ? 1 : 0); Grid.SetRow(right, wide ? 0 : 1); Grid.SetColumnSpan(right, wide ? 1 : 2);
      left.Margin = new Thickness(0, 0, wide ? 18 : 0, 0);
    }
    var isWide = true; Lay(true);
    grid.SizeChanged += (s, e) => { try { var w = e.NewSize.Width >= 1000; if (w != isWide) { isWide = w; Lay(w); } } catch (Exception ex) { Log.Error(ex, "Blastlog friend dashboard layout"); } };
    return grid;
  }
}
}
