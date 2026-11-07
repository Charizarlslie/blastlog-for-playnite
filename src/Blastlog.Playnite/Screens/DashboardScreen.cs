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

/// <summary>What the dashboard read, all at once.</summary>
sealed class DashboardData {
  public JObject Me, Stats, NowPlaying, Shelf, TasteHelp, Backlog, Friends, FriendActivity, Imports;
  /// <summary>Each Currently playing game's page read (entry with game, length and version; stores).</summary>
  public Dictionary<string, JObject> Pages = new Dictionary<string, JObject>();
  public string DisplayName => Dash.Str(Me?["user"], "displayName");
  public int FriendCount => Dash.Arr(Friends?["friends"]).Count;
}

sealed class DashboardView : StackPanel {
  static readonly ILogger Log = LogManager.GetLogger();
  readonly BlastlogPlugin plugin;
  int renderId;

  public DashboardView(BlastlogPlugin plugin) {
    this.plugin = plugin;
    MaxWidth = 1500;
    Loaded += (s, e) => Dash.Safe(() => Render(false), "load");
  }

  /// <summary>Reads everything again (after a change, or Refresh) and redraws.</summary>
  public void Refresh(bool fresh = false) => Dash.Safe(() => Render(fresh), "refresh");

  async Task Render(bool fresh) {
    var id = ++renderId;
    if (Children.Count == 0) Children.Add(Dash.Text("Loading your dashboard…", "Blastlog.Muted"));
    if (!plugin.Configured) {
      Children.Clear(); Header(null);
      Children.Add(Dash.Card(Dash.Text("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to get started. Your dashboard shows up here once it's in.")));
      Children.Add(SyncStrip()); return;
    }

    Task<JObject> Read(string path) => plugin.Mirror(path, fresh).ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result.Value : null, TaskScheduler.Default);
    var me = Read("me"); var dashboard = Read("dashboard"); var shelf = Read("recently-finished"); var help = Read("taste-help");
    var backlog = Read("backlog"); var friends = Read("friends"); var activity = Read("friend-activity"); var imports = Read("imports");
    var next = Read($"next-up?mode={DashboardNextUp.Mode}");
    await Task.WhenAll(me, dashboard, shelf, help, backlog, friends, activity, imports, next);
    var data = new DashboardData { Me = me.Result, Stats = dashboard.Result?["stats"] as JObject, NowPlaying = dashboard.Result?["nowPlaying"] as JObject, Shelf = shelf.Result,
      TasteHelp = help.Result, Backlog = backlog.Result, Friends = friends.Result, FriendActivity = activity.Result, Imports = imports.Result };
    var playingIds = Dash.Arr(data.NowPlaying?["items"]).Select(i => Dash.Str(i, "entryId")).Where(x => x != null).ToList();
    var pages = playingIds.Select(e => Read("entries/" + e)).ToList();
    await Task.WhenAll(pages);
    for (var i = 0; i < playingIds.Count; i++) if (pages[i].Result != null) data.Pages[playingIds[i]] = pages[i].Result;
    if (id != renderId) return;

    Children.Clear();
    Header(data);
    if (data.Stats == null && next.Result == null) {
      Children.Add(Dash.Card(Dash.Text("Couldn't reach Blastlog, and there's no saved copy yet. Check your connection, then press Refresh.")));
      Children.Add(SyncStrip()); return;
    }
    Prompts(data);
    Children.Add(new DashboardNextUp(plugin, this, next.Result));
    Children.Add(Columns(new DashboardPlaying(plugin, this, data, next.Result?["entry"] as JObject), DashboardLibrary.LibraryCard(plugin, data.Stats)));
    Children.Add(DashboardLibrary.RecentlyFinished(plugin, data));
    var friendsCard = DashboardLibrary.FriendsLately(plugin, data.FriendActivity);
    if (friendsCard != null) Children.Add(friendsCard);
    Children.Add(SyncStrip());
  }

  // ---- Header: title with page help; Search, Find a co-op game, + New entry ----
  void Header(DashboardData data) {
    var grid = new Grid { Margin = new Thickness(0, 0, 0, 22) };
    grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    var name = data?.DisplayName;
    var title = Dash.Text(name != null ? $"{name}'s dashboard" : "Dashboard", "Blastlog.PageTitle"); title.VerticalAlignment = VerticalAlignment.Center;
    var left = new StackPanel { Children = { Dash.Row(0, title, Help()), Dash.Text("Your library at a glance", "Blastlog.Muted").Margin(0, 2, 0, 0) } };
    grid.Children.Add(left);
    if (data != null) {
      var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
      actions.Children.Add(new DashboardSearch(plugin) { Width = 340 }.Margin(0, 0, 12, 0));
      if (data.FriendCount > 0) actions.Children.Add(Kit.Button("Find a co-op game", () => Dash.Site(plugin, "/together"), "Blastlog.Ghost"));
      // Adding a game is the site's search-and-add dialog, so it opens there.
      var add = Kit.Button("+ New entry", () => Dash.Site(plugin, "/dashboard")); add.Margin = new Thickness(0);
      add.ToolTip = "Opens Blastlog to add a game";
      actions.Children.Add(add);
      Grid.SetColumn(actions, 1); grid.Children.Add(actions);
    }
    Children.Add(grid);
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
  void Prompts(DashboardData data) {
    var rated = (int?)data.Stats?["ratedDone"] ?? 0; var total = (int?)data.Stats?["total"] ?? 0;
    if (data.Stats != null && rated < 10)
      Children.Add(Prompt(total > 0 ? "Sharpen your picks in two minutes" : "Get your first picks in two minutes",
        "Tap the well-known games you've played and give them stars. Ten is enough" + (total > 0 ? "." : ", and you can bring in your whole library later."),
        Kit.Button("Rate games I know", () => Dash.Site(plugin, "/quick-start")), total > 0 ? null : Kit.Button("Import my games", () => Dash.Site(plugin, "/library/import"), "Blastlog.Ghost")));
    // Steam sync's daily check found games to sort (only shows once the server sends the imports list).
    var steam = Dash.Arr(data.Imports?["batches"]).OfType<JObject>().Where(b => (string)b["origin"] == "steam_sync" && (string)b["status"] == "ready").ToList();
    if (steam.Count > 0) {
      var count = steam.Sum(b => (int?)b["counts"]?["total"] ?? 0);
      var last = (string)steam[steam.Count - 1]["id"];
      Children.Add(Prompt("New from Steam", $"{count} game{(count == 1 ? "" : "s")} Steam has that Blastlog hasn't seen yet. Sort them into your library or wishlist.",
        Kit.Button("Sort them", () => Dash.Site(plugin, "/library/import/" + last)), null));
    }
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
    Lay(true);
    grid.SizeChanged += (s, e) => { try { Lay(e.NewSize.Width >= 1000); } catch (Exception ex) { Log.Error(ex, "Blastlog dashboard layout"); } };
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
