using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core;
using Blastlog.Sync.Core.Design;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Your library (Blastlog design.md 8 "Library"; web App.tsx Library and library.ts): every Blastlog entry, games that
/// aren't in Playnite included, as a cover grid. Search, the status chips with their counts, the rating and sort menus and
/// the reverse button work exactly as the site's (Blastlog.Sync.Core.Design.LibraryList, tested against the site's
/// cases); + New entry and Import games as on the site. A game opens through Nav.Entry. The dashboard's library links and
/// its search's Enter open this screen with the site's filters (Nav.Library).
/// </summary>
public sealed class LibraryScreen : IBlastlogScreen {
  public string Title => "Your library";
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
  static LibraryFilters left; static double leftScroll; static int leftShown;

  /// <summary>
  /// Shows the library with these filters (the site's query, e.g. "status=done&amp;rating=4"): applied in place when the
  /// screen is already open (a second sidebar click would close it), else opened from the sidebar. False when Playnite
  /// won't switch (another Playnite version, fullscreen mode), so the caller opens the site.
  /// </summary>
  public static bool Open(string query) {
    var filters = LibraryFilters.Parse(query);
    if (current != null && current.TryGetTarget(out var view) && view.IsLoaded && view.IsVisible) { view.Apply(filters, 0); return true; }
    pending = filters; left = null;
    if (ScreenJump.To("Your library")) return true;
    pending = null;
    return false;
  }
  internal static (LibraryFilters filters, double scroll, int shown) TakeStart() {
    if (pending != null) { var p = pending; pending = null; left = null; return (p, 0, 0); }
    if (left != null) { var l = left; left = null; return (l, leftScroll, leftShown); }
    return (new LibraryFilters(), 0, 0);
  }

  readonly BlastlogPlugin plugin;
  LibraryFilters filters;
  List<Entry> all = new List<Entry>(), rows = new List<Entry>();
  bool readOnce, readFailed;
  double restoreScroll; int restoreShown;
  ScrollViewer page;

  readonly TextBlock title = Kit.Text("Your library", "Blastlog.PageTitle"), subtitle = Kit.Text("", "Blastlog.Muted");
  readonly TextBox search = Dash.Field();
  readonly Button clear;
  readonly WrapPanel chips = new WrapPanel();
  readonly ComboBox ratingBox = new ComboBox(), sortBox = new ComboBox();
  readonly Button direction;
  readonly CardGrid grid = new CardGrid { MinColumn = 180, Gap = 18 };
  readonly ContentControl below = new ContentControl { Focusable = false, IsTabStop = false };
  readonly DispatcherTimer typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
  bool quiet;

  public LibraryView(BlastlogPlugin plugin, (LibraryFilters filters, double scroll, int shown) start) {
    this.plugin = plugin; filters = start.filters; restoreScroll = start.scroll; restoreShown = start.shown;
    MaxWidth = 1500;
    current = new WeakReference<LibraryView>(this);

    // Header: the title and how many show, Import games and + New entry beside it (the approved board).
    var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 24) };
    var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
    var import = PnKit.Btn(PnKit.IconWord(Download, "Import games", 18), () => { Nav.Site(plugin, "/library/import"); return Task.CompletedTask; }, "Blastlog.Ghost", "Import games");
    import.ToolTip = "Bring in games from Steam, PlayStation, Xbox, Nintendo, GOG, Epic or any list (on Blastlog)";
    var add = Kit.Button("+ New entry", () => Nav.AddGame(plugin, null, () => Safe(() => Refresh())));
    add.ToolTip = "Search for a game and add it to your Blastlog library"; add.Margin = new Thickness(0);
    actions.Children.Add(import); actions.Children.Add(add);
    DockPanel.SetDock(actions, Dock.Right); head.Children.Add(actions);
    subtitle.Margin = new Thickness(0, 6, 0, 0);
    head.Children.Add(new StackPanel { Children = { title, subtitle } });
    Children.Add(head);

    // The panel (the approved board): search with the menus beside it, then the status chips.
    Dash.AutomationName(search, "Search library");
    var field = Dash.FieldBox(search, "Search titles…");
    search.Text = filters.Search; // after FieldBox, so its placeholder hides for a restored search
    clear = Kit.Button("✕", () => { search.Text = ""; search.Focus(); }, "Blastlog.TextButton");
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

    // Search with the rating and sort menus beside it, then the status chips the whole width (they wrap as a row, not a column).
    var menus = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Children = { ratingBox, sortBox, direction } };
    var searchRow = new DockPanel();
    DockPanel.SetDock(menus, Dock.Right); searchRow.Children.Add(menus); searchRow.Children.Add(field);
    chips.Margin = new Thickness(0, 14, 0, -8);
    var panel = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 20), Child = new StackPanel { Children = { searchRow, chips } } };
    panel.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface");
    AutomationProperties.SetName(panel, "Library filters");
    Children.Add(panel);
    Children.Add(grid);
    Children.Add(below);

    Loaded += (s, e) => Safe(() => Start());
    Unloaded += (s, e) => { try { typing.Stop(); if (page != null) page.ScrollChanged -= Scrolled; } catch (Exception) { } };
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
    // The saved copy at once (the last sync's read), then what changed since.
    all = plugin.LibraryEntries(); readOnce = all.Count > 0;
    var restore = restoreShown; Apply(filters, Math.Max(Batch, restore));
    if (restore > 0 && page != null) { var to = restoreScroll; restoreScroll = 0; restoreShown = 0; Dispatcher.BeginInvoke(new Action(() => { try { page.ScrollToVerticalOffset(to); } catch (Exception) { } }), DispatcherPriority.Loaded); }
    ShowName();
    await Refresh();
  }

  async void ShowName() {
    try {
      void Use(Newtonsoft.Json.Linq.JObject me) { var name = Dash.Str(me?["user"], "displayName"); if (!string.IsNullOrEmpty(name)) title.Text = name + "’s library"; }
      Use(plugin.SavedRead("me"));
      Use((await plugin.Mirror("me", false)).Value);
    } catch (Exception e) { Log.Warn("Blastlog library: name: " + e.Message); }
  }

  /// <summary>Reads what changed on Blastlog and redraws when anything did, keeping how far the list was shown.</summary>
  public async Task Refresh() {
    var ok = await plugin.RefreshLibrary();
    var fresh = plugin.LibraryEntries();
    readFailed = !ok; readOnce |= ok;
    if (Same(all, fresh) && grid.Children.Count > 0) return;
    all = fresh;
    Apply(filters, Math.Max(Batch, grid.Children.Count));
  }
  static bool Same(List<Entry> a, List<Entry> b) {
    if (a.Count != b.Count) return false;
    var versions = a.ToDictionary(e => e.EntryId, e => e.Version);
    return b.All(e => versions.TryGetValue(e.EntryId, out var v) && v == e.Version);
  }

  void ShowControls(bool on) { foreach (var c in Children.OfType<Border>()) c.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }

  /// <summary>Puts these filters on and redraws: the chips, menus and the first <paramref name="show"/> covers.</summary>
  void Apply(LibraryFilters next, int show) {
    var sameList = filters.ToQuery() == next.ToQuery() && grid.Children.Count > 0 && show == 0;
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
    DrawChips();
    rows = LibraryList.Filter(all, filters);
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
    if (filters.Trait.Length > 0) {
      // The trait filter needs the site's library-traits read, which the sync API doesn't offer yet: say so, offer to drop it.
      var off = Chip("With a trait (not here yet)", -1, true, () => Apply(filters.With(f => f.Trait = ""), 0));
      off.ToolTip = "The trait filter works on Blastlog for now. Click to clear it."; chips.Children.Add(off);
    }
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

  /// <summary>Adds the next covers (the list draws in batches, more as you scroll near the end).</summary>
  void More(int count) {
    var start = grid.Children.Count;
    foreach (var e in rows.Skip(start).Take(count)) grid.Children.Add(Card(e));
    var remaining = rows.Count - grid.Children.Count;
    if (rows.Count == 0) below.Content = Empty();
    else if (remaining > 0) {
      var more = Kit.Button($"Show more ({remaining.ToString("N0", CultureInfo.InvariantCulture)} left)", () => More(Batch), "Blastlog.Ghost");
      more.HorizontalAlignment = HorizontalAlignment.Center; more.Margin = new Thickness(0, 24, 0, 0);
      below.Content = more;
    } else below.Content = null;
  }

  void Scrolled(object sender, ScrollChangedEventArgs e) {
    try {
      if (!IsVisible || page == null || grid.Children.Count >= rows.Count) return;
      if (page.VerticalOffset + page.ViewportHeight > page.ExtentHeight - 900) More(Batch);
    } catch (Exception ex) { Log.Error(ex, "Blastlog library scroll"); }
  }

  FrameworkElement Empty() {
    if (all.Count == 0 && !readOnce) return Dash.Card(Dash.Text(readFailed ? "Couldn't reach Blastlog. Your library shows here once it answers." : "Loading your library…", "Blastlog.Muted"));
    if (all.Count == 0) {
      var body = new StackPanel();
      body.Children.Add(Kit.Text("Your library is empty", "Blastlog.CardTitle"));
      body.Children.Add(Kit.Text("Import your games from Steam, PlayStation, Xbox, Nintendo, GOG, Epic or any list, or add them one at a time with + New entry.", "Blastlog.Muted").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 8, 0, 16); }));
      body.Children.Add(Kit.Button("Import my games", () => Nav.Site(plugin, "/library/import")).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
      return Dash.Card(body);
    }
    return Dash.Card(Dash.Text("No titles match these filters.", "Blastlog.Muted"));
  }

  // ---- The cover tile (the Library tile's look, DerivedStyles/GridViewItemTemplate.xaml; the site's row's facts) ----
  FrameworkElement Card(Entry e) {
    var game = e.Game;
    var cover = new CoverBox { CornerRadius = new CornerRadius(14), ClipToBounds = true, Margin = new Thickness(10, 10, 10, 0) };
    cover.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
    var pad = Icons.Make(Icons.Gamepad, 40, new SolidColorBrush()); pad.HorizontalAlignment = HorizontalAlignment.Center; pad.VerticalAlignment = VerticalAlignment.Center;
    ((Shape)((Canvas)((Viewbox)pad).Child).Children[0]).SetResourceReference(Shape.StrokeProperty, "Blastlog.GameText");
    cover.Child = pad;

    var name = new TextBlock { Text = game.Title, FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 10, 12, 0) }.Clamp(2);
    name.SetResourceReference(TextBlock.FontFamilyProperty, "Blastlog.Font"); name.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text");
    name.Height = name.MaxHeight;
    var facts = new[] { game.ReleaseYear?.ToString(CultureInfo.InvariantCulture), e.Length != null ? "~" + e.Length.Hours.ToString(CultureInfo.InvariantCulture) + "h" : null, e.HasReview ? "Has review" : null }.Where(x => x != null).ToList();
    var meta = Kit.Text(facts.Count > 0 ? string.Join(" · ", facts) : "—", "Blastlog.Small");
    meta.Margin = new Thickness(12, 4, 12, 0); meta.TextTrimming = TextTrimming.CharacterEllipsis; meta.TextWrapping = TextWrapping.NoWrap;

    var info = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 12), Height = 30, ClipToBounds = true };
    var status = e.Ownership == "wishlist" && (e.Status == null || e.Status == "not_started") ? "wishlist" : e.Status;
    var pill = Kit.StatusPill(status); pill.Margin = new Thickness(0, 0, 8, 0); pill.VerticalAlignment = VerticalAlignment.Center; info.Children.Add(pill);
    if (e.Rating != null) { var stars = Kit.Stars(e.Rating, 15); stars.VerticalAlignment = VerticalAlignment.Center; info.Children.Add(stars); }

    var body = new StackPanel { Children = { cover, name, meta, info } };
    var card = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Child = body, SnapsToDevicePixels = true };
    card.SetResourceReference(Border.BackgroundProperty, "Blastlog.GameTint"); card.SetResourceReference(Border.BorderBrushProperty, "Blastlog.GameEdge");

    var played = plugin.GameFor(e.EntryId);
    var whereText = played != null ? (played.IsInstalled ? "installed in Playnite" : "in Playnite") : "not in Playnite";
    if (played?.IsInstalled == true) info.Children.Insert(0, Installed());
    var label = $"{game.Title}, {Dash.StatusWord(status == "wishlist" ? "not_started" : status)}{(status == "wishlist" ? ", wishlist" : "")}{(e.Rating != null ? $", {e.Rating} stars" : "")}, {whereText}";
    var id = e.EntryId;
    var button = PnKit.Bare(card, () => { Remember(); Nav.Entry(plugin, id, () => Safe(() => Refresh())); }, label);
    button.ToolTip = game.Title;
    Wear(card, cover, game);
    return button;
  }

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

  /// <summary>Opening a game: coming back to the screen shows the same filters, scrolled to the same place.</summary>
  void Remember() { left = filters; leftScroll = page?.VerticalOffset ?? 0; leftShown = grid.Children.Count; }
}
}
