using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Blastlog library (sidebar name; Playnite has its own Library) (Blastlog design.md 8 "Library"; web App.tsx Library and library.ts): every Blastlog entry, games that
/// aren't in Playnite included, as a cover grid. Search (titles and reviews), the status chips with their counts, the
/// trait filter, the rating and sort menus and the reverse button work exactly as the site's
/// (Blastlog.Sync.Core.Design.LibraryList, tested against the site's cases). Collections take one tile, with their games
/// under it when opened; wishlist games on sale say so when sale alerts are on. + New entry and Import games as on the
/// site. A game opens through Nav.Entry, a collection through Nav.Collection. The dashboard's library links and its
/// search's Enter open this screen with the site's filters (Nav.Library).
/// </summary>
public sealed class LibraryScreen : IBlastlogScreen {
  public const string SidebarTitle = "Blastlog library";
  public string Title => SidebarTitle;
  public string Icon => Icons.Library;
  public int Order => 20;
  public FrameworkElement Create(BlastlogPlugin plugin) => new LibraryView(plugin, LibraryView.TakeStart());
}

sealed class LibraryView : StackPanel {
  static readonly ILogger Log = LogManager.GetLogger();
  const int Batch = 60;
  static readonly string Download = "<path d=\"M12 4v11M7 10l5 5 5-5M5 20h14\" />";

  // ---- Opening with filters (Nav.Library) ----
  static LibraryFilters pending;
  static WeakReference<LibraryView> current;
  /// <summary>Where a player who opened a game from here left the list: shown again, once, when they come back.</summary>
  static LibraryFilters left; static double leftScroll; static int leftShown; static HashSet<string> leftOpen;

  /// <summary>
  /// Shows the library with these filters (the site's query, e.g. "status=done&amp;rating=4"): applied in place when the
  /// screen is already open (a second sidebar click would close it), else opened from the sidebar. False when Playnite
  /// won't switch (another Playnite version, fullscreen mode), so the caller opens the site.
  /// </summary>
  public static bool Open(string query) {
    var filters = LibraryFilters.Parse(query);
    if (current != null && current.TryGetTarget(out var view) && view.IsLoaded && view.IsVisible) { view.Apply(filters, 0); return true; }
    pending = filters; left = null;
    if (ScreenJump.To(LibraryScreen.SidebarTitle)) return true;
    pending = null;
    return false;
  }
  internal static (LibraryFilters filters, double scroll, int shown, HashSet<string> open) TakeStart() {
    if (pending != null) { var p = pending; pending = null; left = null; return (p, 0, 0, null); }
    if (left != null) { var l = left; left = null; return (l, leftScroll, leftShown, leftOpen); }
    return (new LibraryFilters(), 0, 0, null);
  }

  /// <summary>One tile in the grid: an entry (under its collection when <see cref="In"/> is set) or a collection.</summary>
  sealed class Tile { public Entry Entry; public LibraryItem Collection; public CollectionRef In; }

  readonly BlastlogPlugin plugin;
  LibraryFilters filters;
  List<Entry> all = new List<Entry>(), rows = new List<Entry>();
  List<Tile> tiles = new List<Tile>();
  bool readOnce, readFailed;
  double restoreScroll; int restoreShown;
  ScrollViewer page;
  // Collections (GET /collections), the trait filter (GET /library-traits) and sale prices (GET /sale-prices).
  readonly Dictionary<string, JObject> collections = new Dictionary<string, JObject>();
  readonly HashSet<string> open = new HashSet<string>();
  List<JObject> traits = new List<JObject>();
  string traitFor; HashSet<string> traitGames;
  readonly Dictionary<string, JObject> sales = new Dictionary<string, JObject>();

  readonly TextBlock title = Kit.Text("Your library", "Blastlog.PageTitle"), subtitle = Kit.Text("", "Blastlog.Muted");
  readonly TextBox search = Dash.Field(), traitText = Dash.Field();
  readonly Button clear;
  readonly WrapPanel chips = new WrapPanel();
  readonly StackPanel menus;
  readonly ContentControl traitSlot = new ContentControl { Focusable = false, IsTabStop = false };
  readonly Border traitField;
  readonly Popup traitPopup;
  readonly StackPanel traitList = new StackPanel();
  readonly ComboBox ratingBox = new ComboBox(), sortBox = new ComboBox();
  readonly Button direction;
  readonly CardGrid grid = new CardGrid { MinColumn = 180, Gap = 18 };
  readonly ContentControl below = new ContentControl { Focusable = false, IsTabStop = false };
  readonly DispatcherTimer typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
  bool quiet;

  /// <summary>
  /// A friend's library (Screens/FriendPages.cs, the site's /friends/:id/library) when set: their entries, collections and
  /// traits, read only (no Import, + New entry, sale tags or Play), and games open as their entry (Nav.FriendEntry).
  /// </summary>
  readonly FriendView friend;
  string Path(string read) => friend == null ? read : friend.Path(read);

  public LibraryView(BlastlogPlugin plugin, (LibraryFilters filters, double scroll, int shown, HashSet<string> open) start, FriendView friend = null) {
    this.plugin = plugin; this.friend = friend; filters = start.filters; restoreScroll = start.scroll; restoreShown = start.shown;
    if (start.open != null) open.UnionWith(start.open);
    MaxWidth = 1500;
    if (friend == null) current = new WeakReference<LibraryView>(this);
    else title.Text = friend.Name + "’s library";

    // Header: the title and how many show, Import games and + New entry beside it (the approved board).
    var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 24) };
    var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
    var import = PnKit.Btn(PnKit.IconWord(Download, "Import games", 18), () => { Nav.Site(plugin, "/library/import"); return Task.CompletedTask; }, "Blastlog.Ghost", "Import games");
    import.ToolTip = "Bring in games from Steam, PlayStation, Xbox, Nintendo, GOG, Epic or any list (on Blastlog)";
    var add = Kit.Button("+ New entry", () => Nav.AddGame(plugin, null, () => Safe(() => Refresh())));
    add.ToolTip = "Search for a game and add it to your Blastlog library"; add.Margin = new Thickness(0);
    if (friend == null) { actions.Children.Add(import); actions.Children.Add(add); }
    DockPanel.SetDock(actions, Dock.Right); head.Children.Add(actions);
    subtitle.Margin = new Thickness(0, 6, 0, 0);
    head.Children.Add(new StackPanel { Children = { title, subtitle } });
    Children.Add(head);

    // Search (titles and reviews).
    Dash.AutomationName(search, "Search library");
    var field = Dash.FieldBox(search, "Search title or review…");
    search.Text = filters.Search; // after FieldBox, so its placeholder hides for a restored search
    // Clears at once (not after the typing pause), and on the press, so nothing between press and release can swallow it.
    clear = Kit.Button("✕", () => { }, "Blastlog.TextButton");
    clear.PreviewMouseLeftButtonDown += (s, e) => { try { ClearSearch(); e.Handled = true; } catch (Exception ex) { Log.Error(ex, "Blastlog library clear"); } };
    clear.Click += (s, e) => Safe(ClearSearch); // the keyboard (Enter or Space on the focused ✕)
    clear.Margin = new Thickness(0); clear.MinHeight = 40; clear.Padding = new Thickness(10, 0, 10, 0); clear.ToolTip = "Clear search"; Dash.AutomationName(clear, "Clear search");
    clear.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Collapsed : Visibility.Visible;
    var fieldGrid = (Grid)field.Child;
    fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    Grid.SetColumn(clear, 2); fieldGrid.Children.Add(clear);
    search.TextChanged += (s, e) => { try { clear.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Collapsed : Visibility.Visible; if (!quiet) { typing.Stop(); typing.Start(); } } catch (Exception ex) { Log.Error(ex, "Blastlog library search"); } };
    typing.Tick += (s, e) => { typing.Stop(); Safe(() => Apply(filters.With(f => f.Search = search.Text), 0)); };
    search.PreviewKeyDown += (s, e) => {
      try {
        if (e.Key == Key.Escape && search.Text.Length > 0) { search.Text = ""; e.Handled = true; }
        else if (e.Key == Key.Enter) { typing.Stop(); Apply(filters.With(f => f.Search = search.Text), 0); e.Handled = true; }
      } catch (Exception ex) { Log.Error(ex, "Blastlog library search keys"); }
    };

    // The trait filter ("Has trait…", the site's datalist): type, pick from the list, or type a trait's whole name.
    Dash.AutomationName(traitText, "Filter by trait");
    traitField = Dash.FieldBox(traitText, "Has trait…");
    var listBox = new Border { Child = new ScrollViewer { Content = traitList, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Padding = new Thickness(6),
      CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), MinWidth = 300 };
    listBox.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface"); listBox.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    listBox.Resources.MergedDictionaries.Add(Kit.Styles);
    traitPopup = new Popup { Child = listBox, PlacementTarget = traitField, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 6 };
    traitText.TextChanged += (s, e) => Safe(() => TraitMatches(true));
    traitText.GotKeyboardFocus += (s, e) => Safe(() => TraitMatches(false));
    traitText.PreviewKeyDown += (s, e) => {
      try {
        if (e.Key == Key.Escape) { traitPopup.IsOpen = false; e.Handled = true; }
        else if (e.Key == Key.Enter) { var first = Matching().FirstOrDefault(); if (first != null) PickTrait(first.S("id")); e.Handled = true; }
        else if (e.Key == Key.Down && traitPopup.IsOpen) { traitList.Children.OfType<Button>().FirstOrDefault()?.Focus(); e.Handled = true; }
      } catch (Exception ex) { Log.Error(ex, "Blastlog library trait keys"); }
    };

    foreach (var (value, label) in new[] { ("all", "All ratings"), ("5", "5 stars"), ("4", "4 stars"), ("3", "3 stars"), ("2", "2 stars"), ("1", "1 star") })
      ratingBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
    foreach (var (value, label) in new[] { ("title", "Title"), ("rating", "Rating"), ("finished", "Completion date"), ("length", "Length") })
      sortBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
    foreach (var box in new[] { ratingBox, sortBox }) { box.MinHeight = 46; box.FontSize = 15; box.MinWidth = 160; box.VerticalContentAlignment = VerticalAlignment.Center; box.Margin = new Thickness(0, 0, 8, 0); }
    AutomationProperties.SetName(ratingBox, "Rating filter"); AutomationProperties.SetName(sortBox, "Sort library");
    ratingBox.SelectionChanged += (s, e) => { if (!quiet && ratingBox.SelectedItem is ComboBoxItem i) Safe(() => Apply(filters.With(f => f.Rating = (string)i.Tag), 0)); };
    sortBox.SelectionChanged += (s, e) => { if (!quiet && sortBox.SelectedItem is ComboBoxItem i) Safe(() => Apply(filters.With(f => f.Sort = (string)i.Tag), 0)); };
    direction = Kit.Button("↑", () => Apply(filters.With(f => f.Direction = f.Direction == "asc" ? "desc" : "asc"), 0), "Blastlog.Ghost");
    direction.ToolTip = "Reverse sort"; direction.Margin = new Thickness(0); direction.MinHeight = 44; direction.MinWidth = 44; direction.Padding = new Thickness(14, 0, 14, 0); direction.FontSize = 17;
    menus = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 8), Children = { ratingBox, sortBox, direction } };

    // The panel (the approved board): search with the trait filter beside it, then the status chips and the menus.
    traitSlot.Width = 290; traitSlot.Margin = new Thickness(12, 0, 0, 0); traitSlot.VerticalAlignment = VerticalAlignment.Top;
    var searchRow = new DockPanel();
    field.VerticalAlignment = VerticalAlignment.Top; // stays put when the trait slot grows (With … and its link)
    DockPanel.SetDock(traitSlot, Dock.Right); searchRow.Children.Add(traitSlot); searchRow.Children.Add(field);
    chips.Margin = new Thickness(0, 14, 0, -8);
    var panel = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 20), Child = new StackPanel { Children = { searchRow, chips } } };
    panel.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface");
    AutomationProperties.SetName(panel, "Library filters");
    Children.Add(panel);
    Children.Add(grid);
    Children.Add(below);

    Loaded += (s, e) => Safe(() => Start());
    Unloaded += (s, e) => { try { typing.Stop(); traitPopup.IsOpen = false; if (page != null) page.ScrollChanged -= Scrolled; } catch (Exception) { } };
  }

  void ClearSearch() {
    typing.Stop();
    quiet = true; try { search.Text = ""; } finally { quiet = false; }
    search.Focus();
    Apply(filters.With(f => f.Search = ""), 0);
  }

  static async void Safe(Func<Task> work) { try { await work(); } catch (Exception e) { Log.Error(e, "Blastlog library"); } }
  static void Safe(Action work) { try { work(); } catch (Exception e) { Log.Error(e, "Blastlog library"); } }

  async Task Start() {
    page = PnKit.FindAncestor<ScrollViewer>(this);
    if (page != null) { page.ScrollChanged -= Scrolled; page.ScrollChanged += Scrolled; }
    if (!plugin.Configured) {
      grid.Children.Clear(); ShowControls(false);
      below.Content = Dash.Card(Dash.Text("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to get started. Your library shows up here once it's in."));
      return;
    }
    // The saved copies at once (the last sync's read, the last collections, traits and prices), then what changed since.
    all = friend != null ? FriendData.SavedEntries(plugin, friend) : plugin.LibraryEntries(); readOnce = all.Count > 0;
    UseCollections(plugin.SavedRead(Path("collections"))); UseTraits(plugin.SavedRead(Path("library-traits"))); if (friend == null) UseSales(plugin.SavedRead("sale-prices"));
    var restore = restoreShown; Apply(filters, Math.Max(Batch, restore));
    if (restore > 0 && page != null) { var to = restoreScroll; restoreScroll = 0; restoreShown = 0; Dispatcher.BeginInvoke(new Action(() => { try { page.ScrollToVerticalOffset(to); } catch (Exception) { } }), DispatcherPriority.Loaded); }
    ShowName();
    ReadExtras();
    await Refresh();
  }

  async void ShowName() {
    if (friend != null) return;
    try {
      void Use(JObject me) { var name = Dash.Str(me?["user"], "displayName"); if (!string.IsNullOrEmpty(name)) title.Text = name + "’s library"; }
      Use(plugin.SavedRead("me"));
      Use((await plugin.Mirror("me", false)).Value);
    } catch (Exception e) { Log.Warn("Blastlog library: name: " + e.Message); }
  }

  /// <summary>Collections, the trait list and sale prices from the server; each redraws only when it changed.</summary>
  async void ReadExtras() {
    try {
      var reads = (friend == null ? new[] { "collections", "library-traits", "sale-prices" } : new[] { "collections", "library-traits" })
        .Select(async path => (path, value: (await plugin.Mirror(Path(path), false)).Value)).ToList();
      var changed = false;
      foreach (var read in reads) {
        var (path, value) = await read;
        if (value == null) continue;
        changed |= path == "collections" ? UseCollections(value) : path == "library-traits" ? UseTraits(value) : UseSales(value);
      }
      if (changed && IsLoaded) Redraw();
    } catch (Exception e) { Log.Warn("Blastlog library: extras: " + e.Message); }
  }
  bool UseCollections(JObject value) {
    if (value == null) return false;
    var next = value.Objects("items").Where(c => c.S("id") != null).ToDictionary(c => c.S("id"));
    if (next.Count == collections.Count && next.All(kv => collections.TryGetValue(kv.Key, out var old) && JToken.DeepEquals(old, kv.Value))) return false;
    collections.Clear(); foreach (var kv in next) collections[kv.Key] = kv.Value;
    return true;
  }
  bool UseTraits(JObject value) {
    if (value == null) return false;
    var next = value.Objects("traits");
    var same = next.Count == traits.Count && next.Zip(traits, (a, b) => JToken.DeepEquals(a, b)).All(x => x);
    traits = next;
    return !same && filters.Trait.Length > 0; // only the chip's name depends on the list
  }
  bool UseSales(JObject value) {
    if (value == null) return false;
    var next = value.B("available") ? value.Objects("games").Where(g => g.B("onSale") && g.D("priceCents") != null && g.S("gameId") != null).GroupBy(g => g.S("gameId")).ToDictionary(g => g.Key, g => g.First())
      : new Dictionary<string, JObject>();
    if (next.Count == sales.Count && next.All(kv => sales.TryGetValue(kv.Key, out var old) && JToken.DeepEquals(old, kv.Value))) return false;
    sales.Clear(); foreach (var kv in next) sales[kv.Key] = kv.Value;
    return true;
  }

  /// <summary>Reads what changed on Blastlog and redraws when anything did, keeping how far the list was shown.</summary>
  public async Task Refresh() {
    var read = friend != null ? await FriendData.Entries(plugin, friend, true) : null;
    var ok = friend != null ? read != null : await plugin.RefreshLibrary();
    var fresh = friend != null ? read ?? all : plugin.LibraryEntries();
    readFailed = !ok; readOnce |= ok;
    if (Same(all, fresh) && grid.Children.Count > 0) return;
    all = fresh;
    Redraw();
  }
  static bool Same(List<Entry> a, List<Entry> b) {
    if (a.Count != b.Count) return false;
    var versions = a.ToDictionary(e => e.EntryId, e => (e.Version, e.CollectionId));
    return b.All(e => versions.TryGetValue(e.EntryId, out var v) && v.Version == e.Version && v.CollectionId == e.CollectionId);
  }

  void ShowControls(bool on) { foreach (var c in Children.OfType<Border>()) c.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }

  /// <summary>Draws the list again with the same filters, as far as it was shown.</summary>
  void Redraw() => Apply(filters, Math.Max(Batch, grid.Children.Count), force: true);

  /// <summary>Puts these filters on and redraws: the chips, menus and the first <paramref name="show"/> tiles.</summary>
  void Apply(LibraryFilters next, int show, bool force = false) {
    var sameList = !force && filters.ToQuery() == next.ToQuery() && grid.Children.Count > 0 && show == 0;
    filters = next;
    quiet = true;
    try {
      if (search.Text != filters.Search) search.Text = filters.Search;
      ratingBox.SelectedItem = ratingBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == filters.Rating);
      sortBox.SelectedItem = sortBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == filters.Sort);
    } finally { quiet = false; }
    direction.Content = filters.Direction == "asc" ? "↑" : "↓";
    Dash.AutomationName(direction, "Reverse sort, now " + (filters.Direction == "asc" ? "ascending" : "descending"));
    if (sameList) return;
    ShowControls(true);
    DrawChips(); DrawTraitSlot();
    if (filters.Trait.Length > 0 && traitFor != filters.Trait) ReadTraitGames(filters.Trait);
    var trait = filters.Trait.Length > 0 ? (traitFor == filters.Trait ? traitGames : null) ?? new HashSet<string>() : null;
    rows = LibraryList.Filter(all, filters, trait == null ? (Func<Entry, bool>)null : e => trait.Contains(e.Game.Id));
    var items = LibraryList.Group(rows, all, collections.Values.Select(c => new CollectionRef { Id = c.S("id"), Title = c.S("title") }), filters);
    tiles = new List<Tile>();
    foreach (var item in items) {
      if (item.Entry != null) { tiles.Add(new Tile { Entry = item.Entry }); continue; }
      tiles.Add(new Tile { Collection = item });
      if (open.Contains(item.Collection.Id)) foreach (var m in item.Members) tiles.Add(new Tile { Entry = m, In = item.Collection });
    }
    subtitle.Text = $"{rows.Count.ToString("N0", CultureInfo.InvariantCulture)} of {all.Count.ToString("N0", CultureInfo.InvariantCulture)} titles";
    grid.Children.Clear();
    if (show == 0 && page != null) page.ScrollToVerticalOffset(Math.Min(page.VerticalOffset, OffsetOfGrid()));
    More(Math.Max(Batch, show));
  }

  double OffsetOfGrid() { try { return grid.TransformToAncestor(page).Transform(new Point(0, 0)).Y + page.VerticalOffset - 16; } catch (Exception) { return 0; } }

  void DrawChips() {
    chips.Children.Clear();
    var counts = LibraryList.Counts(all);
    foreach (var (value, label) in LibraryList.Chips) {
      var v = value;
      chips.Children.Add(Chip(label, counts[value], filters.Status == value, () => Apply(filters.With(f => f.Status = v), 0)));
    }
    chips.Children.Add(menus);
  }

  // ---- The trait filter ----
  /// <summary>The "Has trait…" field, or, with a trait picked, "With …" (✕ clears it) and the link to its page.</summary>
  void DrawTraitSlot() {
    var holder = new Grid();
    if (filters.Trait.Length == 0) {
      traitPopup.IsOpen = false;
      if (traitField.Parent is Grid g) g.Children.Clear();
      if (traitPopup.Parent is Grid p) p.Children.Clear();
      holder.Children.Add(traitField); holder.Children.Add(traitPopup);
      traitSlot.Content = holder;
      return;
    }
    var id = filters.Trait;
    var name = traits.FirstOrDefault(t => t.S("id") == id)?.S("label");
    var label = Kit.Text("With " + (name ?? "a trait"), "Blastlog.Body"); label.FontWeight = FontWeights.Bold; label.VerticalAlignment = VerticalAlignment.Center;
    label.TextTrimming = TextTrimming.CharacterEllipsis; label.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.GameText");
    var x = Kit.Button("✕", () => { traitText.Text = ""; Apply(filters.With(f => f.Trait = ""), 0); }, "Blastlog.TextButton");
    x.Margin = new Thickness(4, 0, 0, 0); x.MinHeight = 40; x.Padding = new Thickness(10, 0, 10, 0); x.SetResourceReference(Control.ForegroundProperty, "Blastlog.GameText");
    Dash.AutomationName(x, "Clear trait filter"); x.ToolTip = "Clear trait filter";
    var row = new DockPanel(); DockPanel.SetDock(x, Dock.Right); row.Children.Add(x); row.Children.Add(label);
    var chip = new Border { Child = row, CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(1), Padding = new Thickness(14, 2, 4, 2), MinHeight = 46 };
    chip.SetResourceReference(Border.BackgroundProperty, "Blastlog.GameTint2"); chip.SetResourceReference(Border.BorderBrushProperty, "Blastlog.GameEdge");
    var more = Dash.Link("Friends’ and Discover games with it →", () => Nav.Trait(plugin, id, "Library"));
    more.HorizontalAlignment = HorizontalAlignment.Left; more.MinHeight = 40; more.Margin = new Thickness(4, 4, 0, 0); more.FontSize = 14;
    traitSlot.Content = new StackPanel { Children = { chip, more } };
  }

  IEnumerable<JObject> Matching() {
    var q = SearchText.Fold(traitText.Text.Trim());
    return traits.Where(t => q.Length == 0 || SearchText.Fold(t.S("label")).Contains(q));
  }

  /// <summary>The traits that match what's typed; typing a trait's whole name picks it, as the site's list does.</summary>
  void TraitMatches(bool typed) {
    if (!traitText.IsKeyboardFocusWithin && !typed) return;
    var text = traitText.Text.Trim();
    var exact = traits.FirstOrDefault(t => string.Equals(t.S("label"), text, StringComparison.CurrentCultureIgnoreCase));
    if (typed && exact != null) { PickTrait(exact.S("id")); return; }
    traitList.Children.Clear();
    var found = Matching().Take(40).ToList();
    if (traits.Count == 0) traitList.Children.Add(Dash.Text("The traits in your library show here once Blastlog answers.", "Blastlog.Muted").Margin(10, 8, 10, 8));
    else if (found.Count == 0) traitList.Children.Add(Dash.Text("No trait in your library matches.", "Blastlog.Muted").Margin(10, 8, 10, 8));
    foreach (var t in found) {
      var games = (int)(t.D("games") ?? 0);
      var count = Dash.Text($"{games} game{(games == 1 ? "" : "s")}", "Blastlog.Small"); count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new Thickness(12, 0, 0, 0);
      var word = Dash.Text(t.S("label"), "Blastlog.Body"); word.VerticalAlignment = VerticalAlignment.Center; word.TextTrimming = TextTrimming.CharacterEllipsis;
      var line = new DockPanel(); DockPanel.SetDock(count, Dock.Right); line.Children.Add(count); line.Children.Add(word);
      var id = t.S("id");
      // A full-width row (the kit's buttons centre their content): the label left, the count right, a tint on hover.
      var row = new Border { Child = line, MinHeight = 42, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(10), Background = Brushes.Transparent };
      row.MouseEnter += (s, e) => row.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
      row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
      var b = PnKit.Bare(row, () => PickTrait(id), $"{t.S("label")}, {games} games");
      b.GotKeyboardFocus += (s, e) => row.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
      b.LostKeyboardFocus += (s, e) => row.Background = Brushes.Transparent;
      traitList.Children.Add(b);
    }
    ((Border)traitPopup.Child).Width = Math.Max(300, traitField.ActualWidth);
    traitPopup.IsOpen = true;
  }

  void PickTrait(string id) {
    if (id == null) return;
    traitPopup.IsOpen = false;
    quiet = true; try { traitText.Text = ""; } finally { quiet = false; }
    Apply(filters.With(f => f.Trait = id), 0);
  }

  /// <summary>Which games carry the picked trait (GET /library-traits?trait=); the list redraws when it lands.</summary>
  async void ReadTraitGames(string id) {
    traitFor = id; traitGames = null;
    try {
      var path = Path("library-traits?trait=" + Uri.EscapeDataString(id));
      var saved = plugin.SavedRead(path);
      if (saved != null) { traitGames = new HashSet<string>(saved.Strings("gameIds")); }
      var value = (await plugin.Mirror(path, false)).Value;
      if (traitFor != id) return;
      if (value != null) { UseTraits(value); traitGames = new HashSet<string>(value.Strings("gameIds")); }
      else if (traitGames == null) traitGames = new HashSet<string>();
      if (IsLoaded) Redraw();
    } catch (Exception e) { Log.Warn("Blastlog library: trait games: " + e.Message); }
  }

  /// <summary>A status chip (the site's .filter-row button): outlined pill with its count; the picked one filled with the game colour.</summary>
  static Button Chip(string label, int count, bool on, Action pick) {
    var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    words.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
    if (count >= 0) words.Children.Add(new TextBlock { Text = count.ToString("N0", CultureInfo.InvariantCulture), FontWeight = FontWeights.SemiBold, Opacity = 0.8,
      Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
    var pill = new Border { Child = words, CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(1), Padding = new Thickness(15, 9, 15, 9), MinHeight = 44 };
    var b = new Button { Content = pill, Style = Kit.Style("Blastlog.TextButton"), MinHeight = 44, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 8), FontSize = 14 };
    if (on) {
      pill.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game"); pill.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Game");
      b.SetResourceReference(Control.ForegroundProperty, "Blastlog.OnGame");
    } else {
      pill.Background = Brushes.Transparent; pill.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Edge");
      b.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text");
    }
    AutomationProperties.SetName(b, count >= 0 ? $"{label}, {count}" : label);
    AutomationProperties.SetItemStatus(b, on ? "Selected" : "");
    b.Click += (s, e) => Safe(pick);
    return b;
  }

  /// <summary>Adds the next tiles (the list draws in batches, more as you scroll near the end).</summary>
  void More(int count) {
    var start = grid.Children.Count;
    foreach (var t in tiles.Skip(start).Take(count)) grid.Children.Add(t.Entry != null ? Card(t.Entry, t.In) : CollectionCard(t.Collection));
    var remaining = tiles.Count - grid.Children.Count;
    if (tiles.Count == 0) below.Content = Empty();
    else if (remaining > 0) {
      var more = Kit.Button($"Show more ({remaining.ToString("N0", CultureInfo.InvariantCulture)} left)", () => More(Batch), "Blastlog.Ghost");
      more.HorizontalAlignment = HorizontalAlignment.Center; more.Margin = new Thickness(0, 24, 0, 0);
      below.Content = more;
    } else below.Content = null;
  }

  void Scrolled(object sender, ScrollChangedEventArgs e) {
    try {
      if (!IsVisible || page == null || grid.Children.Count >= tiles.Count) return;
      if (page.VerticalOffset + page.ViewportHeight > page.ExtentHeight - 900) More(Batch);
    } catch (Exception ex) { Log.Error(ex, "Blastlog library scroll"); }
  }

  FrameworkElement Empty() {
    if (all.Count == 0 && !readOnce) return Dash.Card(Dash.Text(readFailed ? "Couldn't reach Blastlog. Your library shows here once it answers." : "Loading your library…", "Blastlog.Muted"));
    if (all.Count == 0 && friend != null) return Dash.Card(Dash.Text(friend.Name + "’s library is empty.", "Blastlog.Muted"));
    if (all.Count == 0) {
      var body = new StackPanel();
      body.Children.Add(Kit.Text("Your library is empty", "Blastlog.CardTitle"));
      body.Children.Add(Kit.Text("Import your games from Steam, PlayStation, Xbox, Nintendo, GOG, Epic or any list, or add them one at a time with + New entry.", "Blastlog.Muted").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 8, 0, 16); }));
      body.Children.Add(Kit.Button("Import my games", () => Nav.Site(plugin, "/library/import")).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
      return Dash.Card(body);
    }
    if (filters.Trait.Length > 0 && (traitFor != filters.Trait || traitGames == null)) return Dash.Card(Dash.Text("Finding the games with that trait…", "Blastlog.Muted"));
    return Dash.Card(Dash.Text("No titles match these filters.", "Blastlog.Muted"));
  }

  // ---- Tiles (the Library tile's look, DerivedStyles/GridViewItemTemplate.xaml; the site's row's facts) ----
  /// <summary>The cover box with its gamepad placeholder, in a grid so a pill can sit over it.</summary>
  static (Grid host, Border cover) CoverHost() {
    var cover = new CoverBox { CornerRadius = new CornerRadius(14), ClipToBounds = true };
    cover.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
    var pad = Icons.Make(Icons.Gamepad, 40, new SolidColorBrush()); pad.HorizontalAlignment = HorizontalAlignment.Center; pad.VerticalAlignment = VerticalAlignment.Center;
    ((Shape)((Canvas)((Viewbox)pad).Child).Children[0]).SetResourceReference(Shape.StrokeProperty, "Blastlog.GameText");
    cover.Child = pad;
    return (new Grid { Margin = new Thickness(10, 10, 10, 0), Children = { cover } }, cover);
  }
  static TextBlock TileTitle(string text) {
    var name = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 10, 12, 0) }.Clamp(2);
    name.SetResourceReference(TextBlock.FontFamilyProperty, "Blastlog.Font"); name.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text");
    name.Height = name.MaxHeight;
    return name;
  }
  static TextBlock Meta(IEnumerable<string> facts) {
    var list = facts.Where(x => !string.IsNullOrEmpty(x)).ToList();
    var meta = Kit.Text(list.Count > 0 ? string.Join(" · ", list) : "—", "Blastlog.Small");
    meta.Margin = new Thickness(12, 4, 12, 0); meta.TextTrimming = TextTrimming.CharacterEllipsis; meta.TextWrapping = TextWrapping.NoWrap;
    if (list.Count > 0) meta.ToolTip = string.Join(" · ", list);
    return meta;
  }
  static Border Frame(UIElement body) {
    var card = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Child = body, SnapsToDevicePixels = true };
    card.SetResourceReference(Border.BackgroundProperty, "Blastlog.GameTint"); card.SetResourceReference(Border.BorderBrushProperty, "Blastlog.GameEdge");
    return card;
  }
  /// <summary>"~9h", "No end" or null (the site's length cell, without the tilde-free table style).</summary>
  static string LengthWord(Entry e) => e.NoEnd ? "No end" : e.Length != null ? "~" + e.Length.Hours.ToString(CultureInfo.InvariantCulture) + "h" : null;

  FrameworkElement Card(Entry e, CollectionRef inCollection) {
    var game = e.Game;
    var (host, cover) = CoverHost();
    // On sale (SaleAlerts.tsx SaleTag): your own wishlisted game, only while sale alerts are on.
    if (e.Ownership == "wishlist" && sales.TryGetValue(game.Id ?? "", out var sale)) {
      var cents = sale.D("priceCents") ?? 0;
      host.Children.Add(PnKit.OverPill("On sale $" + (cents / 100).ToString("0.00", CultureInfo.InvariantCulture), "Blastlog.Ok", HorizontalAlignment.Left, VerticalAlignment.Top));
    }
    var facts = new[] { inCollection != null ? "In " + inCollection.Title : null, game.ReleaseYear?.ToString(CultureInfo.InvariantCulture), LengthWord(e), e.HasReview ? "Has review" : null };
    // No year, length or review yet: the site's ownership word (library.ts ownershipLabel) rather than a bare dash.
    if (facts.All(f => f == null)) facts = new[] { e.Ownership == "wishlist" ? "Wishlist" : e.Ownership == "library" ? "In library" : "Ownership unknown" };
    var meta = Meta(facts);

    var info = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 12), Height = 30, ClipToBounds = true };
    var status = e.Ownership == "wishlist" && (e.Status == null || e.Status == "not_started") ? "wishlist" : e.Status;
    var pill = Kit.StatusPill(status); pill.Margin = new Thickness(0, 0, 8, 0); pill.VerticalAlignment = VerticalAlignment.Center; info.Children.Add(pill);
    if (e.Rating != null) { var stars = Kit.Stars(e.Rating, 15); stars.VerticalAlignment = VerticalAlignment.Center; info.Children.Add(stars); }

    var card = Frame(new StackPanel { Children = { host, TileTitle(game.Title), meta, info } });
    var played = friend == null ? plugin.GameFor(e.EntryId) : null;
    var whereText = friend != null ? friend.Name + "’s entry" : played != null ? (played.IsInstalled ? "installed in Playnite" : "in Playnite") : "not in Playnite";
    if (played?.IsInstalled == true) info.Children.Insert(0, Installed());
    var label = $"{game.Title}, {Dash.StatusWord(status == "wishlist" ? "not_started" : status)}{(status == "wishlist" ? ", wishlist" : "")}{(e.Rating != null ? $", {e.Rating} stars" : "")}, {whereText}"
      + (inCollection != null ? ", in " + inCollection.Title : "");
    var id = e.EntryId;
    var button = PnKit.Bare(card, () => {
      if (friend != null) { Nav.FriendEntry(plugin, id, friend.Name); return; }
      Remember(); Nav.Entry(plugin, id, () => Safe(() => Refresh()));
    }, label);
    button.ToolTip = game.Title;
    Wear(card, cover, game);
    return button;
  }

  /// <summary>
  /// A collection's tile (LibraryCollections.tsx CollectionRow): its cover, title, "Collection · N games", how many are
  /// finished, its overall stars (not a rating of its games) and its games' lengths added up. The tile opens its page;
  /// "Show its N games" puts its games that pass the filters under it.
  /// </summary>
  FrameworkElement CollectionCard(LibraryItem item) {
    var c = item.Collection; collections.TryGetValue(c.Id, out var data);
    var games = data?.Objects("games") ?? new List<JObject>();
    var total = games.Count; var finished = games.Count(g => LibraryFilters.Terminal.Contains(g.S("status") ?? ""));
    var hasReview = !string.IsNullOrWhiteSpace(data?.S("review"));
    var hours = all.Where(e => e.CollectionId == c.Id && e.Length != null).Sum(e => (double)e.Length.Hours);
    var (host, cover) = CoverHost();
    var meta = Meta(new[] { "Collection", $"{total} game{(total == 1 ? "" : "s")}", data?.S("releaseYear"), hours > 0 ? Math.Round(hours).ToString(CultureInfo.InvariantCulture) + "h" : null, hasReview ? "Has review" : null });

    var info = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 0), Height = 30, ClipToBounds = true };
    var done = Kit.Text($"{finished}/{total} finished", "Blastlog.Small"); done.FontWeight = FontWeights.SemiBold; done.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text");
    var pill = new Border { Child = done, CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 4, 12, 4), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    pill.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2"); pill.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    info.Children.Add(pill);
    var rating = data?.D("rating");
    if (rating != null) { var stars = Kit.Stars(rating, 15); stars.VerticalAlignment = VerticalAlignment.Center; stars.ToolTip = "Overall, for the collection: not a rating of its games"; info.Children.Add(stars); }

    var isOpen = open.Contains(c.Id);
    // It sits inside the tile's own button: the press is taken here, so the tile doesn't also open the collection.
    var cid = c.Id;
    var toggle = Kit.Button(isOpen ? "▾ Hide its games" : $"▸ Show its {total} games", () => Toggle(cid), "Blastlog.TextButton");
    toggle.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = true; Safe(() => Toggle(cid)); };
    toggle.HorizontalAlignment = HorizontalAlignment.Left; toggle.MinHeight = 40; toggle.Margin = new Thickness(4, 2, 0, 8); toggle.Padding = new Thickness(8, 0, 8, 0); toggle.FontSize = 14;
    toggle.SetResourceReference(Control.ForegroundProperty, "Blastlog.GameText");
    AutomationProperties.SetItemStatus(toggle, isOpen ? "Expanded" : "Collapsed");

    var card = Frame(new StackPanel { Children = { host, TileTitle(c.Title), meta, info, toggle } });
    var id = c.Id;
    var button = PnKit.Bare(card, () => { Remember(); Nav.Collection(plugin, id, () => Safe(() => { ReadExtras(); return Refresh(); })); },
      $"{c.Title}, collection of {total} games, {finished} finished{(rating != null ? $", {rating} stars overall" : "")}");
    button.ToolTip = c.Title;
    var coverId = data?.S("coverImageId");
    Wear(card, cover, new Game { Id = "collection:" + id, Title = c.Title, Artwork = coverId != null ? new GameArtwork { CoverImageId = coverId } : null });
    return button;
  }

  /// <summary>Shows or hides a collection's games under it, keeping the list where it is.</summary>
  void Toggle(string id) { if (!open.Remove(id)) open.Add(id); Redraw(); }

  /// <summary>The Installed mark from the Library tile, icon only (the pill and stars need the room).</summary>
  static FrameworkElement Installed() {
    var icon = Icons.Make("<path d=\"M12 4v11M7 10l5 5 5-5M5 20h14\" />", 14, null);
    ((Shape)((Canvas)((Viewbox)icon).Child).Children[0]).SetResourceReference(Shape.StrokeProperty, "Blastlog.Ok");
    var mark = new Border { Child = icon, CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 6, 8, 6), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0),
      VerticalAlignment = VerticalAlignment.Center, ToolTip = "Installed" };
    mark.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2"); mark.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    AutomationProperties.SetName(mark, "Installed");
    return mark;
  }

  /// <summary>The cover, then the card's own game colour (its page colour on Blastlog, else the cover's), through the shared picture queue.</summary>
  async void Wear(Border card, Border cover, Game game) {
    Kit.GameScope(card, GameColour.BrandGreen);
    try {
      var url = Artwork.Url(game, ArtworkContext.Cover);
      var file = url != null ? await PnKit.Picture(plugin, url) : null;
      if (file != null) {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(file);
        image.DecodePixelWidth = 500; image.EndInit(); image.Freeze();
        cover.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill }; cover.Child = null;
      }
      Kit.GameScope(card, await plugin.Theme.SeedFor(game));
    } catch (Exception ex) { Log.Warn("Blastlog library: cover for " + game?.Title + ": " + ex.Message); }
  }

  /// <summary>Opening a game or collection: coming back to the screen shows the same filters, opened collections and place.</summary>
  /// <summary>Puts the site's filters on in place ("status=done&amp;rating=4"); a friend's dashboard links use it.</summary>
  internal void ApplyQuery(string query) => Apply(LibraryFilters.Parse(query), 0);

  void Remember() { if (friend != null) return; left = filters; leftScroll = page?.VerticalOffset ?? 0; leftShown = grid.Children.Count; leftOpen = new HashSet<string>(open); }
}
}
