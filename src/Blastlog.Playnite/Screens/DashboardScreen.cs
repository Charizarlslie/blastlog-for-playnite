using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Dashboard (Blastlog design.md 8 "Dashboard", PLAYNITE_DESIGN.md 4.1): the header with Search, Find a co-op game and
/// + New entry; the Rate games I know and New from Steam prompts; the Next up hero; Currently playing beside Your library;
/// Recently finished; Friends lately; then the plugin's own sync controls. Games that are in Playnite get Play / Install.
/// The page wears Next up's game. Reads are the site's own answers (mirror reads, cached), so it opens straight away.
/// </summary>
public sealed class DashboardScreen : IBlastlogScreen {
  public string Title => "Dashboard";
  public string Icon => Icons.Dashboard;
  public int Order => 10;
  public FrameworkElement Create(BlastlogPlugin plugin) => new DashboardView(plugin);
}

/// <summary>What the dashboard has read so far (saved copies first, then the server's answers).</summary>
sealed class DashboardData {
  public JObject Me, Dashboard, Next, Shelf, TasteHelp, Backlog, Friends, FriendActivity, Imports;
  /// <summary>Each Currently playing game's page read (entry with game, length and version; stores).</summary>
  public Dictionary<string, JObject> Pages = new Dictionary<string, JObject>();
  public JObject Stats => Dashboard?["stats"] as JObject;
  public JObject NowPlaying => Dashboard?["nowPlaying"] as JObject;
  public IEnumerable<string> PlayingIds => Dash.Arr(NowPlaying?["items"]).Select(i => Dash.Str(i, "entryId")).Where(x => x != null);
  public string DisplayName => Dash.Str(Me?["user"], "displayName");
  public int FriendCount => Dash.Arr(Friends?["friends"]).Count;
}

/// <summary>
/// The page is a column of slots, one per card. Each draws at once from the saved copy and again when its own read
/// lands with something new, so nothing waits on the slowest read and the page doesn't jump while you scroll.
/// </summary>
sealed class DashboardView : StackPanel {
  static readonly ILogger Log = LogManager.GetLogger();
  enum Slot { Header, Prompts, Hero, Playing, Library, Shelf, Friends }
  static readonly string[] Paths = { "me", "friends", "dashboard", "imports", "backlog", "recently-finished", "taste-help", "friend-activity" };
  readonly BlastlogPlugin plugin;
  readonly DashboardData data = new DashboardData();
  readonly Dictionary<Slot, ContentControl> slots = new Dictionary<Slot, ContentControl>();
  readonly ContentControl sync = new ContentControl();
  int renderId; bool built, nextRead, finished;

  public DashboardView(BlastlogPlugin plugin) {
    this.plugin = plugin;
    MaxWidth = 1500;
    foreach (Slot s in Enum.GetValues(typeof(Slot))) slots[s] = new ContentControl { Focusable = false, IsTabStop = false };
    Loaded += (s, e) => Dash.Safe(() => Render(false), "load");
  }

  /// <summary>Reads everything again (after a change, or Refresh) and redraws what changed.</summary>
  public void Refresh(bool fresh = false) => Dash.Safe(() => Render(fresh), "refresh");

  string NextPath => $"next-up?mode={DashboardNextUp.Mode}";

  async Task Render(bool fresh) {
    var id = ++renderId;
    if (!plugin.Configured) {
      Children.Clear(); built = false;
      Children.Add(Header(null));
      Children.Add(Dash.Card(Dash.Text("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to get started. Your dashboard shows up here once it's in.")));
      Children.Add(SyncStrip()); return;
    }
    if (!built) {
      Children.Clear();
      Children.Add(slots[Slot.Header]); Children.Add(slots[Slot.Prompts]); Children.Add(slots[Slot.Hero]);
      Children.Add(Columns(slots[Slot.Playing], slots[Slot.Library]));
      Children.Add(slots[Slot.Shelf]); Children.Add(slots[Slot.Friends]); Children.Add(sync);
      built = true;
    }
    // 1. Saved copies, drawn straight away (a change clears them; then what's on screen stays until the answer).
    foreach (var path in Paths.Concat(new[] { NextPath })) { var saved = plugin.SavedRead(path); if (saved != null) Store(path, saved); }
    foreach (var e in data.PlayingIds) { var saved = plugin.SavedRead("entries/" + e); if (saved != null) data.Pages[e] = saved; }
    finished = false;
    foreach (Slot s in Enum.GetValues(typeof(Slot))) Draw(s);
    sync.Content = SyncStrip();

    // 2. The server's answers, each redrawing only its own cards.
    var reads = Paths.Concat(new[] { NextPath }).Select(path => Fetch(path, fresh, id)).ToList();
    await Task.WhenAll(reads);
    if (id != renderId) return;
    finished = true;
    Draw(Slot.Hero); Draw(Slot.Playing);
    sync.Content = SyncStrip();
  }

  async Task Fetch(string path, bool fresh, int id) {
    var value = (await plugin.Mirror(path, fresh)).Value;
    if (id != renderId) return;
    if (path.StartsWith("next-up")) nextRead = true;
    if (value != null && Store(path, value)) foreach (var s in SlotsFor(path)) Draw(s);
    else if (path.StartsWith("next-up")) Draw(Slot.Hero);
    if (path == "dashboard") {
      // One page read per playing game, in parallel; the card fills in as they land.
      var ids = data.PlayingIds.ToList(); var changed = false;
      await Task.WhenAll(ids.Select(async e => {
        var page = (await plugin.Mirror("entries/" + e, fresh)).Value;
        if (id != renderId || page == null) return;
        if (!data.Pages.TryGetValue(e, out var old) || !JToken.DeepEquals(old, page)) { data.Pages[e] = page; changed = true; }
      }));
      if (changed && id == renderId) Draw(Slot.Playing); // once, so the rows don't flicker through their colours per read
    }
  }

  /// <summary>Keeps a read; true when it's new or different from what's shown.</summary>
  bool Store(string path, JObject value) {
    JObject Swap(ref JObject field) { var old = field; field = value; return old; }
    JObject old;
    switch (path) {
      case "me": old = Swap(ref data.Me); break;
      case "friends": old = Swap(ref data.Friends); break;
      case "dashboard": old = Swap(ref data.Dashboard); break;
      case "imports": old = Swap(ref data.Imports); break;
      case "backlog": old = Swap(ref data.Backlog); break;
      case "recently-finished": old = Swap(ref data.Shelf); break;
      case "taste-help": old = Swap(ref data.TasteHelp); break;
      case "friend-activity": old = Swap(ref data.FriendActivity); break;
      default:
        if (path != NextPath) return false; // a mode the hero has since switched away from
        old = Swap(ref data.Next); break;
    }
    return old == null || !JToken.DeepEquals(old, value);
  }
  static IEnumerable<Slot> SlotsFor(string path) {
    switch (path) {
      case "me": case "friends": return new[] { Slot.Header };
      case "dashboard": return new[] { Slot.Prompts, Slot.Playing, Slot.Library };
      case "imports": return new[] { Slot.Prompts };
      case "backlog": return new[] { Slot.Playing };
      case "recently-finished": case "taste-help": return new[] { Slot.Shelf };
      case "friend-activity": return new[] { Slot.Friends };
      default: return new[] { Slot.Hero, Slot.Playing };
    }
  }

  void Draw(Slot slot) {
    try {
      var host = slots[slot];
      switch (slot) {
        case Slot.Header: host.Content = Header(data); break;
        case Slot.Prompts: host.Content = Prompts(data); break;
        case Slot.Hero:
          if (data.Next != null) host.Content = new DashboardNextUp(plugin, this, data.Next);
          else if (finished && data.Stats == null) host.Content = Dash.Card(Dash.Text("Couldn't reach Blastlog, and there's no saved copy yet. Check your connection, then press Refresh."));
          else if (nextRead && finished) host.Content = new DashboardNextUp(plugin, this, null);
          else host.Content = Dash.Card(Dash.Text("Loading your dashboard…", "Blastlog.Muted"));
          break;
        case Slot.Playing: host.Content = data.Dashboard == null ? null : new DashboardPlaying(plugin, this, data, data.Next?["entry"] as JObject); break;
        case Slot.Library: host.Content = data.Dashboard == null ? null : DashboardLibrary.LibraryCard(plugin, data.Stats); break;
        case Slot.Shelf: host.Content = data.Shelf == null ? null : DashboardLibrary.RecentlyFinished(plugin, data); break;
        case Slot.Friends: host.Content = DashboardLibrary.FriendsLately(plugin, data.FriendActivity); break;
      }
    } catch (Exception e) { Log.Error(e, "Blastlog dashboard: drawing " + slot); }
  }
  // ---- Header: title with page help; Search, Find a co-op game, + New entry ----
  FrameworkElement Header(DashboardData data) {
    var grid = new Grid { Margin = new Thickness(0, 0, 0, 22) };
    grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    var name = data?.DisplayName;
    var title = Dash.Text(name != null ? $"{name}'s dashboard" : "Dashboard", "Blastlog.PageTitle"); title.VerticalAlignment = VerticalAlignment.Center;
    var left = new StackPanel { Children = { Dash.Row(0, title, Help()), Dash.Text("Your library at a glance", "Blastlog.Muted").Margin(0, 2, 0, 0) } };
    grid.Children.Add(left);
    if (data != null) {
      var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
      actions.Children.Add(new DashboardSearch(plugin) { Width = 340 }.Margin(0, 0, 12, 0));
      if (data.FriendCount > 0) actions.Children.Add(Kit.Button("Find a co-op game", () => Nav.Together(plugin), "Blastlog.Ghost"));
      // Adding a game is the native search-and-add window (AddGameWindow); the dashboard refreshes after each add or undo.
      var add = Kit.Button("+ New entry", () => Nav.AddGame(plugin, done: () => Refresh(true))); add.Margin = new Thickness(0);
      add.ToolTip = "Search for a game and add it to your Blastlog library";
      actions.Children.Add(add);
      Grid.SetColumn(actions, 1); grid.Children.Add(actions);
      // Narrow pages put the actions on their own line under the title instead of squeezing the title into a column
      // (the site's header wraps the same way).
      grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      grid.SizeChanged += (s, e) => {
        try {
          actions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); left.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
          var stack = e.NewSize.Width < actions.DesiredSize.Width + Math.Max(520, left.DesiredSize.Width) + 24;
          Grid.SetColumn(actions, stack ? 0 : 1); Grid.SetRow(actions, stack ? 1 : 0);
          Grid.SetColumnSpan(left, stack ? 2 : 1);
          actions.HorizontalAlignment = stack ? HorizontalAlignment.Left : HorizontalAlignment.Right;
          actions.Margin = new Thickness(0, stack ? 16 : 0, 0, 0);
        } catch (Exception ex) { Log.Error(ex, "Blastlog dashboard: header layout"); }
      };
    }
    return grid;
  }

  /// <summary>The page help: the site's "Show me around this page" steps, in a small panel.</summary>
  FrameworkElement Help() {
    var button = new Button { Content = "?", Style = Kit.Style("Blastlog.Ghost"), MinWidth = 34, MinHeight = 34, Width = 34, Height = 34, Padding = new Thickness(0), Margin = new Thickness(14, 6, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    Dash.AutomationName(button, "Show me around this page"); button.ToolTip = "Show me around this page";
    var steps = new[] {
      new[] { "Your next game", "One pick from your own backlog, with the reasons it fits you. The page takes its cover's colour." },
      new[] { "Short on time?", "Quick one picks something short, Long haul something to sink into." },
      new[] { "What you're playing", "Games on Playing live here. When you're done with one, press Finished it and give it stars and a line." },
      new[] { "Rate what you finish", "Games you finished that still need stars or a few words wait here first. Nothing teaches Blastlog your taste faster." },
      new[] { "Play and install", "Games that are in Playnite get Play or Install right on the dashboard." },
    };
    var list = new StackPanel();
    foreach (var step in steps) list.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 0, 12), Children = { Dash.Text(step[0], "Blastlog.CardTitle"), Dash.Text(step[1], "Blastlog.Muted") } });
    var panel = new Border { Child = list, Width = 380, Padding = new Thickness(20, 18, 20, 6), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
    panel.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2"); panel.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    panel.Resources.MergedDictionaries.Add(Kit.Styles);
    var popup = new Popup { Child = panel, PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 8 };
    button.Click += (s, e) => { try { popup.IsOpen = !popup.IsOpen; } catch (Exception ex) { Log.Error(ex, "Blastlog help"); } };
    return new Grid { Children = { button, popup } };
  }

  // ---- Prompts: Rate games I know (fewer than 10 rated), New from Steam ----
  FrameworkElement Prompts(DashboardData data) {
    var list = new StackPanel();
    var rated = (int?)data.Stats?["ratedDone"] ?? 0; var total = (int?)data.Stats?["total"] ?? 0;
    if (data.Stats != null && rated < 10)
      list.Children.Add(Prompt(total > 0 ? "Sharpen your picks in two minutes" : "Get your first picks in two minutes",
        "Tap the well-known games you've played and give them stars. Ten is enough" + (total > 0 ? "." : ", and you can bring in your whole library later."),
        Kit.Button("Rate games I know", () => Nav.QuickStart(plugin)), total > 0 ? null : Kit.Button("Import my games", () => Dash.Site(plugin, "/library/import"), "Blastlog.Ghost")));
    // Steam sync's daily check found games to sort (only shows once the server sends the imports list).
    var steam = Dash.Arr(data.Imports?["batches"]).OfType<JObject>().Where(b => (string)b["origin"] == "steam_sync" && (string)b["status"] == "ready").ToList();
    if (steam.Count > 0) {
      var count = steam.Sum(b => (int?)b["counts"]?["total"] ?? 0);
      var last = (string)steam[steam.Count - 1]["id"];
      list.Children.Add(Prompt("New from Steam", $"{count} game{(count == 1 ? "" : "s")} Steam has that Blastlog hasn't seen yet. Sort them into your library or wishlist.",
        Kit.Button("Sort them", () => Dash.Site(plugin, "/library/import/" + last)), null));
    }
    return list;
  }
  static Border Prompt(string head, string body, Button primary, Button second) {
    var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Dash.Text(head, "Blastlog.CardTitle"), Dash.Text(body, "Blastlog.Muted").Margin(0, 4, 0, 0) } });
    var buttons = Dash.Row(0, primary, second); buttons.VerticalAlignment = VerticalAlignment.Center; buttons.Margin = new Thickness(20, 0, 0, 0);
    Grid.SetColumn(buttons, 1); grid.Children.Add(buttons);
    return Kit.Card(grid, game: true);
  }

  /// <summary>Two cards side by side on a wide window (3:2), one above the other on a narrow one.</summary>
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
    var wide = true; Lay(true);
    // Only when it crosses the line: re-laying on every size change makes WPF measure the page again mid-scroll.
    grid.SizeChanged += (s, e) => { try { var w = e.NewSize.Width >= 1000; if (w != wide) { wide = w; Lay(w); } } catch (Exception ex) { Log.Error(ex, "Blastlog dashboard layout"); } };
    return grid;
  }

  // ---- The plugin's own controls (the earlier Blastlog view's sync buttons, kept) ----
  FrameworkElement SyncStrip() {
    var buttons = Dash.Wrap(
      Kit.Button("Sync now", async () => { await plugin.SyncNow(interactive: true); Refresh(); }, "Blastlog.Ghost"),
      Kit.Button("Preview sync", async () => { await plugin.PreviewSync(); Refresh(); }, "Blastlog.Ghost"),
      Kit.Button("Pair games", async () => { await plugin.PairGames(); Refresh(); }, "Blastlog.Ghost"),
      Kit.Button("Refresh", () => Refresh(fresh: true), "Blastlog.Ghost"),
      Kit.Button("Open Blastlog", () => Dash.Site(plugin, "/dashboard"), "Blastlog.TextButton"));
    foreach (FrameworkElement b in buttons.Children) b.Margin = new Thickness(0, 0, 10, 10);
    var panel = new StackPanel { Children = { Dash.Text("Playnite sync", "Blastlog.CardTitle").Margin(0, 0, 0, 10), buttons } };
    var sync = plugin.LastSyncLine();
    if (!string.IsNullOrEmpty(sync)) panel.Children.Add(Dash.Text(sync, "Blastlog.Small"));
    var updated = UpdatedAt();
    if (updated != null) panel.Children.Add(Dash.Text("Last updated " + Dash.Clock12(updated.Value) + " Pacific. Saved copies show when you're offline.", "Blastlog.Small"));
    return Dash.Card(panel);
  }
  /// <summary>When the newest saved read was taken (the plugin's "Last updated …" line, in this PC's time).</summary>
  DateTimeOffset? UpdatedAt() {
    var line = plugin.CacheLine();
    if (!line.StartsWith("Last updated ")) return null;
    return DateTime.TryParse(line.Substring(13).TrimEnd('.'), out var local) ? new DateTimeOffset(local) : (DateTimeOffset?)null;
  }

  /// <summary>Next up chose another game: the page wears it (design.md 2.3).</summary>
  internal void Wear(Blastlog.Sync.Core.Game game) => Dash.Safe(async () => {
    if (plugin.Theme == null) return;
    plugin.Theme.WearPage(game == null ? null : await plugin.Theme.SeedFor(game));
  }, "wear");
}
}
