using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using static Blastlog.Playnite.Screens.TasteUi;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// One import (apps/web LibraryImport.tsx ImportBatchPage, /library/import/:id), natively: finding (progress), the add
/// step (Add N games, Sort them first, Discard), the sort step (piles, filter, search, select and move, Steam playtime
/// groups and Quick sort, ratings switch), games that need a look (suggested, options, search, Not a game, Skip), and
/// once it went in: the sort cards (Finished it? yes / no / skip, then stars), the whole list with status changes, and
/// Undo. Same decisions as the site (Blastlog.Sync.Core.Design.ImportSort). Reads GET /sync/v1/imports/:id; writes
/// PATCH imports/:id/rows, PATCH imports/:id, PATCH imports/:id/entries, POST imports/:id/commit | undo | accept-suggested,
/// POST imports/:id/rows/:row/resolve and DELETE imports/:id.
/// </summary>
sealed partial class ImportBatchView : StackPanel {
  static readonly ILogger Log = LogManager.GetLogger();
  static readonly HttpMethod Patch = new HttpMethod("PATCH");
  readonly BlastlogPlugin plugin; readonly string id; readonly Action back, close;
  readonly StackPanel body = new StackPanel();
  readonly DispatcherTimer poll = new DispatcherTimer();
  JObject batch; List<ImportRow> rows = new List<ImportRow>();
  string flash; bool flashOk; string busy;
  string show = "summary"; // after it went in: summary, cards or list
  bool sortFirst, confirmUndo, stopped, loaded;
  Task queue = Task.CompletedTask; int pending;

  public ImportBatchView(BlastlogPlugin plugin, string id, Action back, Action close) {
    this.plugin = plugin; this.id = id; this.back = back; this.close = close;
    handled = ImportPage.Handled.For(plugin, id);
    Children.Add(Fingerprint.LinkButton("← Import", back).Also(b => { b.Margin = new Thickness(-10, 0, 0, 10); b.HorizontalAlignment = HorizontalAlignment.Left; }));
    Children.Add(body);
    body.Children.Add(T("Opening your import…", "Blastlog.Muted"));
    poll.Tick += (s, e) => { poll.Stop(); Run(() => Load(), "import refresh"); };
    Loaded += (s, e) => { if (!loaded) { loaded = true; Run(() => Load(), "import"); } };
    Unloaded += (s, e) => poll.Stop();
  }

  public void Stop() { stopped = true; poll.Stop(); }

  string Path => "imports/" + Uri.EscapeDataString(id);
  string Status => batch?.S("status");
  string Format => batch?.S("format");
  bool Wishlist => ImportSort.IsWishlist(Format);
  string BatchName => batch == null ? "Import" : ImportSort.ImportName(batch.S("door"), Format, batch.S("origin"));

  async Task Load() {
    if (stopped) return;
    if (!plugin.Configured) { Show(T("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see this import.", "Blastlog.Muted")); return; }
    var view = (await plugin.Mirror(Path, fresh: true)).Value;
    if (stopped) return;
    if (!(view?["batch"] is JObject b)) {
      var panel = Stack(null, T("That import isn’t here.", "Blastlog.Body", "Blastlog.Danger"));
      panel.Children.Add(Wrap(Kit.Button("Try again", async () => await Load(), "Blastlog.Ghost"), Kit.Button("Back to import", back, "Blastlog.TextButton")).Also(w => w.Margin = new Thickness(0, 12, 0, 0)));
      Show(panel); return;
    }
    var was = Status;
    batch = b;
    rows = view.Objects("rows").Select(ImportRow.Parse).ToList();
    Render();
    // Matching, adding and undoing update every 1.5 s; new games' fingerprints every 30 s until they're done.
    var s = Status;
    var fp = batch["fingerprints"] as JObject;
    if (s == "matching" || s == "committing" || s == "undoing") { poll.Interval = TimeSpan.FromSeconds(1.5); poll.Start(); }
    else if (fp != null && (fp.D("done") ?? 0) < (fp.D("total") ?? 0)) { poll.Interval = TimeSpan.FromSeconds(30); poll.Start(); }
    if (was != null && was != s && (s == "committed" || s == "undone")) show = "summary";
  }

  void Show(UIElement e) { body.Children.Clear(); body.Children.Add(e); }

  /// <summary>Runs a server action: shows it busy, says what happened (or why not), then reloads.</summary>
  async Task Act(string key, Func<Task<string>> work) {
    busy = key; flash = null; Render();
    try { var text = await work(); if (text != null) { flash = text; flashOk = true; } }
    catch (Exception e) { flash = "Something went wrong."; flashOk = false; Log.Error(e, "Blastlog import: " + key); }
    finally { busy = null; }
    await Load();
  }

  /// <summary>Before it went in: sorting answers at once here and saves in the background.</summary>
  void Sort(IEnumerable<string> ids, PileChange change) {
    var list = ids.ToList(); if (list.Count == 0) return;
    foreach (var r in rows.Where(r => list.Contains(r.Id))) { r.Pile = change.Pile; r.ChosenStatus = change.ChosenStatus; }
    Render();
    Run(async () => {
      var done = await plugin.Write(Path + "/rows", new JObject { ["ids"] = new JArray(list), ["pile"] = change.Pile, ["chosenStatus"] = change.ChosenStatus }, Patch);
      if (done == null) await Load();
    }, "import sort");
  }

  /// <summary>
  /// After it went in: changes to the entries this import made show at once and save in order (a game's Yes and then
  /// its stars must not race).
  /// </summary>
  void Change(IEnumerable<string> ids, string status = null, int? rating = null, bool clearRating = false, bool remove = false) {
    var list = ids.ToList(); if (list.Count == 0) return;
    var after = ImportSort.PileAfter(status, remove);
    if (after != null) foreach (var r in rows.Where(r => list.Contains(r.Id))) { r.Pile = after.Pile; r.ChosenStatus = after.ChosenStatus; }
    var body = new JObject { ["ids"] = new JArray(list) };
    if (remove) body["remove"] = true;
    if (status != null) body["status"] = status;
    if (rating != null) body["rating"] = rating.Value; else if (clearRating) body["rating"] = JValue.CreateNull();
    pending++;
    var ui = Dispatcher;
    queue = queue.ContinueWith(async _ => {
      JObject done = null;
      try { done = await ui.InvokeAsync(() => plugin.Write(Path + "/entries", body, Patch)).Task.Unwrap(); }
      catch (Exception e) { Log.Error(e, "Blastlog import: change entries"); }
      if (done == null) await ui.InvokeAsync(() => Run(() => Load(), "import reload"));
      await ui.InvokeAsync(() => pending--);
    }, TaskScheduler.Default).Unwrap();
  }

  // ---- Rendering ----

  void Render() {
    if (batch == null) return;
    var focusSearch = searchInput?.IsKeyboardFocusWithin == true;
    body.Children.Clear();
    var s = Status;
    var window = Window.GetWindow(this); if (window != null) window.Title = BatchName;
    body.Children.Add(Header());
    if (flash != null) body.Children.Add(LinkPages.Notice(flash, flashOk));

    if (s == "matching") body.Children.Add(Progress((int)Math.Round((batch.D("progress") ?? 0) * Total), Total, "Finding your games",
      "Matching each one to the game catalog. Big libraries take a few minutes; you can close this and come back."));
    else if (s == "committing") body.Children.Add(Progress(rows.Count(r => r.Outcome != null),
      rows.Count(r => (r.State == "confident" || r.State == "in_library") && r.Pile != "skip"), "Adding your games", null));
    else if (s == "undoing") body.Children.Add(Kit.Card(T("Undoing this import…", "Blastlog.Muted")));
    else if (s == "failed") body.Children.Add(Kit.Card(Stack(null,
      T($"This import didn’t finish ({batch.S("error") ?? "unknown error"}). Nothing was added.", "Blastlog.Body", "Blastlog.Danger").Also(t => t.TextWrapping = TextWrapping.Wrap),
      Fingerprint.LinkButton("Start again on Blastlog ↗", () => Nav.Site(plugin, "/library/import"), game: true).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left))).Also(c => c.Margin = new Thickness(0, 0, 0, 18)));
    else if (s == "ready") { if (sortFirst) SortStep(); else AddStep(); }
    else if (s == "committed" && show == "cards") body.Children.Add(SortCards());
    else if (s == "committed" || s == "undone") body.Children.Add(Added());
    if (s == "committed" && show == "list") AddedList();
    if (s == "committed" && show != "cards") { var look = LookSection(); if (look != null) body.Children.Add(look); }
    if (s == "committed" && show == "summary" && !Wishlist)
      body.Children.Add(ImportPage.Sentence("Blastlog.Body", "Next: ", ("Fill in your library ↗", (Action)(() => Nav.Site(plugin, "/library/import/fill-in"))),
        ": the rest of your series, and games your friends played.").Also(w => w.Margin = new Thickness(0, 4, 0, 0)));
    if (focusSearch && searchInput != null) Dispatcher.BeginInvoke(new Action(() => { try { searchInput.Focus(); searchInput.CaretIndex = searchInput.Text.Length; } catch (Exception) { } }), DispatcherPriority.Input);
  }

  int Total => (int?)batch?["counts"]?.D("total") ?? 0;

  FrameworkElement Header() {
    var s = Status;
    var added = rows.Count(r => r.Outcome == "committed"); var found = rows.Count(r => r.State == "confident");
    var title = s == "ready" ? $"Found {Plural(found, Wishlist ? "wishlist game" : "game")}"
      : s == "committed" ? added > 0 ? $"Added {Plural(added, "game")}{(Wishlist ? " to your wishlist" : "")}" : "Your games are in"
      : s == "undone" ? "Import undone" : "Your import";
    var created = batch.S("createdAt");
    var when = created != null && DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.ToLocalTime().ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture) : "";
    return LinkPages.Head("Import · " + BatchName, title, $"{when} · {Plural(Total, "game")} read");
  }

  static FrameworkElement Progress(int value, int total, string label, string detail) {
    var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, total), Value = Math.Min(value, Math.Max(1, total)), Height = 10, Margin = new Thickness(0, 10, 0, 8) };
    bar.SetResourceReference(Control.ForegroundProperty, "Blastlog.Game"); bar.SetResourceReference(Control.BackgroundProperty, "Blastlog.Surface2");
    AutomationProperties.SetName(bar, label);
    var panel = Stack(null, T($"{label}: {value:N0} of {total:N0}", "Blastlog.Body", null, 0, FontWeights.SemiBold), bar);
    if (detail != null) panel.Children.Add(T(detail, "Blastlog.Muted").Also(t => t.TextWrapping = TextWrapping.Wrap));
    return Kit.Card(panel).Also(c => c.Margin = new Thickness(0, 0, 0, 18));
  }

  // ---- Shared pieces ----

  FrameworkElement Cover(ImportRow row, double width = 56) => CoverOf(row.Id, row.Name, row.CoverUrl, row.CoverImageId, width);
  FrameworkElement CoverOf(string key, string title, string url, string imageId, double width) {
    var c = Kit.Cover(plugin, new BlastlogGame { Id = key ?? title, Title = title, CoverUrl = url ?? (imageId != null ? Artwork.IgdbImageUrl(imageId, "cover_big") : null) }, width, ArtworkContext.Thumbnail, 8);
    c.VerticalAlignment = VerticalAlignment.Center; c.Margin = new Thickness(0, 0, 12, 0);
    return c;
  }

  /// <summary>The site's pile select: the piles, then More (Started, Didn't finish, Quit, Unplayable). A wishlist has Wishlist and Don't import.</summary>
  ComboBox PileBox(string value, string name, Action<PileChange> changed, bool wishlist = false, bool mixed = false) {
    var box = new ComboBox { MinHeight = 44, MinWidth = 180, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center };
    if (mixed) box.Items.Add(new ComboBoxItem { Content = "Mixed", Tag = "" });
    foreach (var p in wishlist ? ImportSort.WishlistPiles : ImportSort.Piles) box.Items.Add(new ComboBoxItem { Content = p.Label, Tag = p.Id });
    if (!wishlist) {
      box.Items.Add(new ComboBoxItem { Content = "More", IsEnabled = false, FontWeight = FontWeights.Bold, Tag = null });
      foreach (var e in ImportSort.Extra) box.Items.Add(new ComboBoxItem { Content = "   " + e.Label, Tag = e.Id });
    }
    box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (mixed ? "" : value));
    AutomationProperties.SetName(box, name);
    box.SelectionChanged += (s, e) => {
      try { if (box.SelectedItem is ComboBoxItem i && i.Tag is string v && v != "" && v != value) changed(ImportSort.ChangeFor(v)); }
      catch (Exception ex) { Log.Error(ex, "Blastlog import: pile"); }
    };
    return box;
  }

  /// <summary>A filter chip with its count (the site's imp-chips), the picked one filled with the game colour.</summary>
  static Button Chip(string label, int count, bool on, Action pick) {
    var b = AddGameParts.Pill($"{label} {count:N0}", pick);
    AddGameParts.SetPill(b, on);
    AutomationProperties.SetItemStatus(b, on ? "Selected" : "");
    return b;
  }

  static CheckBox Tick(bool on, string name, Action<bool> changed) {
    var c = new CheckBox { IsChecked = on, MinHeight = 44, MinWidth = 32, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    AutomationProperties.SetName(c, name);
    c.Click += (s, e) => { try { changed(c.IsChecked == true); } catch (Exception ex) { Log.Error(ex, "Blastlog import: tick"); } };
    return c;
  }

  /// <summary>A game row: cover, name and its line, then whatever control it gets (a pile or status select).</summary>
  FrameworkElement GameRow(ImportRow row, string line, UIElement control, CheckBox tick = null, UIElement extra = null) {
    var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    if (tick != null) grid.Children.Add(tick);
    var cover = Cover(row, 48); Grid.SetColumn(cover, 1); grid.Children.Add(cover);
    var words = Stack(null, T(row.Name, "Blastlog.Body", null, 0, FontWeights.Bold).Also(t => t.TextWrapping = TextWrapping.Wrap));
    if (!string.IsNullOrEmpty(line)) words.Children.Add(T(line, "Blastlog.Small").Also(t => t.TextWrapping = TextWrapping.Wrap));
    if (extra != null) words.Children.Add(extra);
    words.VerticalAlignment = VerticalAlignment.Center; words.Margin = new Thickness(0, 0, 12, 0);
    Grid.SetColumn(words, 2); grid.Children.Add(words);
    if (control is FrameworkElement f) { f.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(f, 3); grid.Children.Add(f); }
    return grid;
  }

  static Border Card(string title, string note, params UIElement[] content) {
    var panel = Stack(null);
    if (title != null) panel.Children.Add(T(title, "Blastlog.Section"));
    if (note != null) panel.Children.Add(T(note, "Blastlog.Muted").Also(t => { t.Margin = new Thickness(0, 4, 0, 10); t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 760; t.HorizontalAlignment = HorizontalAlignment.Left; }));
    foreach (var c in content) if (c != null) panel.Children.Add(c);
    return Kit.Card(panel).Also(c => c.Margin = new Thickness(0, 0, 0, 18));
  }

  static FrameworkElement Actions(params UIElement[] items) {
    var w = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
    foreach (var i in items) if (i is FrameworkElement f) { f.Margin = new Thickness(0, 0, 12, 8); w.Children.Add(f); }
    return w;
  }

  // ---- Before it went in ----

  /// <summary>The file's own ratings and reviews (only onto games sorted as finished).</summary>
  FrameworkElement RatingsSwitch() {
    var scale = (int?)batch.D("ratingScale") ?? 5;
    var on = batch.B("bringRatings");
    var note = $"From your file, only onto games you sort as finished (Finished, Didn’t finish, Quit, Unplayable). Your scores out of {scale} become stars: " +
      (scale == 100 ? "90 → 5★, 70 → 4★, 50 → 3★" : scale == 10 ? "9 → 5★, 7 → 4★, 5 → 3★" : "as they are") + ". Off means none come along.";
    return Stack(new Thickness(0, 12, 0, 0),
      LinkPages.Check("Bring my ratings and reviews", on, v => Run(() => Act("ratings", async () => { await plugin.Write(Path, new JObject { ["bringRatings"] = v }, Patch); return null; }), "import ratings")),
      T(note, "Blastlog.Small").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 760; t.HorizontalAlignment = HorizontalAlignment.Left; }));
  }

  Task Commit() => Act("commit", async () => { await plugin.Write(Path + "/commit", new JObject()); return null; });
  void Discard() => Run(async () => {
    busy = "discard"; Render();
    var done = await plugin.Write(Path, new JObject(), HttpMethod.Delete);
    busy = null;
    if (done != null) back(); else await Load();
  }, "import discard");

  /// <summary>Found but not added yet (Steam sync's daily finds, older imports): one button adds them; sorting comes after.</summary>
  void AddStep() {
    var going = rows.Count(r => r.State == "confident" && r.Pile != "skip");
    var inLibrary = rows.Count(r => r.State == "in_library"); var look = rows.Count(r => r.State == "needs_look" && r.Outcome == null);
    var notes = string.Join(" ", new[] { inLibrary > 0 ? $"{Plural(inLibrary, "is", "are")} already in your library." : null,
      look > 0 ? $"{Plural(look, "needs", "need")} a look after." : null }.Where(n => n != null));
    var add = Kit.Button(busy == "commit" ? "Adding…" : going > 0 ? $"Add {Plural(going, "game")}{(Wishlist ? " to your wishlist" : "")}" : "Finish", () => Commit());
    add.IsEnabled = (going > 0 || inLibrary > 0) && busy == null;
    var first = !Wishlist && going > 0 ? Kit.Button("Sort them first", () => { sortFirst = true; Render(); }, "Blastlog.TextButton") : null;
    var discard = Kit.Button("Discard", Discard, "Blastlog.TextButton"); discard.IsEnabled = busy == null;
    body.Children.Add(Card(null, null, notes.Length > 0 ? T(notes, "Blastlog.Muted").Also(t => t.TextWrapping = TextWrapping.Wrap) : null,
      batch.B("hasRatings") ? RatingsSwitch() : null, Actions(add, first, discard)));
  }

  // The sort step's filter, search and selection survive re-renders.
  string filter = "all"; HashSet<string> selected = new HashSet<string>(); bool showAll;
  Border searchBox; TextBox searchInput; string search = "";

  /// <summary>The site's search field, kept across renders so typing isn't interrupted.</summary>
  Border SearchBox() {
    if (searchBox == null) {
      var (box, input) = SearchField("Search…", 280);
      searchBox = box; searchInput = input;
      var typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
      typing.Tick += (s, e) => { typing.Stop(); search = input.Text; Render(); };
      input.TextChanged += (s, e) => { typing.Stop(); typing.Start(); };
      AutomationProperties.SetName(input, "Search these games");
    }
    (searchBox.Parent as Panel)?.Children.Remove(searchBox);
    return searchBox;
  }
  bool Matches(ImportRow r) {
    var term = search.Trim(); if (term.Length == 0) return true;
    return r.Name.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0 || r.RawTitle.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0;
  }

  void SortStep() {
    var ready = rows.Where(r => r.State == "confident").ToList();
    var inLibrary = rows.Where(r => r.State == "in_library").ToList();
    var newToBlastlog = ready.Count(r => r.NewToBlastlog);
    var bundles = rows.Count(ImportSort.IsSplitBundle);
    int Count(string state) => rows.Count(r => r.State == state && !ImportSort.IsSplitBundle(r));
    var going = ready.Where(r => r.Pile != "skip").ToList(); var goingNew = going.Count(r => r.NewToBlastlog);
    var piles = ImportSort.PilesFor(Format);
    int PileCount(string pile) => ready.Count(r => r.Pile == pile);

    if (quickOpen) { body.Children.Add(QuickSort()); return; }

    var summary = string.Join("", new[] {
      ready.Count > 0 ? $"{Plural(ready.Count, "game")} to add{(newToBlastlog > 0 ? $" ({newToBlastlog:N0} new to Blastlog)" : "")}" : "Nothing new to add",
      inLibrary.Count > 0 ? $" · {inLibrary.Count:N0} already in your library, left exactly as they are" : "",
      Count("needs_look") > 0 ? $" · {Count("needs_look")} need a look" : "",
      Count("not_a_game") + Count("unmatched") > 0 ? $" · {Count("not_a_game") + Count("unmatched")} not games or not found" : "",
      Count("duplicate") > 0 ? $" · {Count("duplicate")} listed twice" : "",
      bundles > 0 ? $" · {Plural(bundles, "bundle")} split into {(bundles == 1 ? "its" : "their")} games" : "" });
    var head = Runs("Blastlog.Body", ($"{Plural(rows.Count - bundles, "game")} read. ", true, null), (summary, false, null)).Also(t => t.TextWrapping = TextWrapping.Wrap);
    var parts = new List<UIElement> { head };
    if (inLibrary.Count > 0) {
      var exp = new Expander { Header = T($"See the {Plural(inLibrary.Count, "game")} already in your library", "Blastlog.Body"), Margin = new Thickness(0, 8, 0, 0) };
      exp.Content = Stack(new Thickness(18, 6, 0, 0), inLibrary.Select(r => (UIElement)T(r.RawTitle + (r.Title != null && Same(r.Title) != Same(r.RawTitle) ? " → your " + r.Title : ""), "Blastlog.Small")).ToArray());
      parts.Add(exp);
    }
    foreach (var w in (batch["completeness"]?["warnings"] as JArray)?.Select(x => (string)x) ?? Enumerable.Empty<string>())
      if (Warnings.TryGetValue(w ?? "", out var words)) parts.Add(LinkPages.Notice(words, false).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 10, 0, 0); }));
    parts.Add(T(Wishlist ? "These go on your wishlist. Don’t want one there anymore? Set it to Don’t import."
      : "Put each game on a pile. Anything you don’t sort goes in as Backlog. Quickest start: tick the ones you’ve finished and move them to Finished. Ratings and reviews are for finished games only.",
      "Blastlog.Muted").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 10, 0, 0); }));
    if (batch.B("hasRatings")) parts.Add(RatingsSwitch());
    body.Children.Add(Card(null, null, parts.ToArray()));

    var playRows = Wishlist ? new List<ImportRow>() : ready.Where(r => r.Play != null).ToList();
    if (playRows.Count > 0) body.Children.Add(PlayGroups(playRows));

    if (ready.Count > 0) {
      var shown = ready.Where(r => (filter == "all" || r.Pile == filter) && Matches(r)).ToList();
      var chips = new WrapPanel();
      chips.Children.Add(Chip("All", ready.Count, filter == "all", () => { filter = "all"; Render(); }));
      foreach (var p in piles) { var pid = p.Id; chips.Children.Add(Chip(p.Label, PileCount(pid), filter == pid, () => { filter = pid; Render(); })); }
      var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
      var sb = SearchBox(); DockPanel.SetDock(sb, Dock.Right); sb.VerticalAlignment = VerticalAlignment.Top; tools.Children.Add(sb); tools.Children.Add(chips);
      var allOn = shown.Count > 0 && shown.All(r => selected.Contains(r.Id));
      var selectAll = LinkPages.Check($"Select all {(shown.Count == ready.Count ? "" : "shown ")}({shown.Count:N0})", allOn, v => {
        if (v) foreach (var r in shown) selected.Add(r.Id); else foreach (var r in shown) selected.Remove(r.Id);
        Render();
      });
      var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
      var scale = (int?)batch.D("ratingScale") ?? 5;
      foreach (var r in showAll ? shown : shown.Take(120)) {
        var row = r;
        var line = string.Join(" · ", new[] { row.Year?.ToString(CultureInfo.InvariantCulture), row.NewToBlastlog ? "New to Blastlog" : null,
          row.Title != null && row.Title != row.RawTitle ? $"“{row.RawTitle}”" : null, row.Play != null && !Wishlist ? ImportSort.PlayLine(row.Play) : null }.Where(x => x != null));
        UIElement rating = null;
        if (batch.B("bringRatings") && (row.Rating != null || row.HasReview)) {
          var comes = ImportSort.FinishedPile(row.Pile);
          rating = T((row.Rating > 0 ? $"{new string('★', row.Rating.Value)} ({row.SourceRating}/{scale})" : "") + (row.HasReview ? " + review" : "") + (comes ? "" : "  (only finished games take ratings and reviews)"),
            "Blastlog.Small", comes ? "Blastlog.Star" : "Blastlog.TextMuted");
        }
        var tick = Tick(selected.Contains(row.Id), "Select " + row.Name, v => { if (v) selected.Add(row.Id); else selected.Remove(row.Id); Render(); });
        list.Children.Add(GameRow(row, line, PileBox(ImportSort.ValueOf(row.Pile, row.ChosenStatus), "Pile for " + row.Name, c => Sort(new[] { row.Id }, c), Wishlist), tick, rating));
      }
      if (!showAll && shown.Count > 120) list.Children.Add(Kit.Button($"Show all {shown.Count:N0}", () => { showAll = true; Render(); }, "Blastlog.Ghost").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
      if (shown.Count == 0) list.Children.Add(T("Nothing on this pile.", "Blastlog.Muted"));
      body.Children.Add(Card(null, null, tools, selectAll, Bulk(piles), list));
    }

    var look = LookSection(); if (look != null) body.Children.Add(look);

    var commitWords = going.Count > 0
      ? $"{Plural(going.Count, "game")} will go {(Wishlist ? "on your wishlist." : "into your library: " + string.Join(", ", ImportSort.Piles.Take(4).Select(p => $"{PileCount(p.Id)} {p.Label.ToLowerInvariant()}")) + ".")}"
        + (goingNew > 0 ? $" {goingNew:N0} of them {(goingNew == 1 ? "is" : "are")} new to Blastlog and get their traits over the next few days." : "")
      : inLibrary.Count > 0 ? "Nothing new to add." : "Nothing is set to go in yet.";
    if (inLibrary.Count > 0) commitWords += $" The {Plural(inLibrary.Count, "game")} already in your library stay exactly as they are{(Wishlist ? "." : $"; Blastlog just notes that you own them on {ImportSort.DoorName(batch.S("door"))}.")}";
    var go = Kit.Button(busy == "commit" ? "Starting…" : going.Count > 0 ? Wishlist ? $"Add {Plural(going.Count, "game")} to your wishlist" : $"Import {Plural(going.Count, "game")}" : "Finish", () => Commit());
    go.IsEnabled = (going.Count > 0 || inLibrary.Count > 0) && busy == null;
    var discard = Kit.Button("Discard this import", Discard, "Blastlog.TextButton"); discard.IsEnabled = busy == null;
    body.Children.Add(Card(null, null, T(commitWords, "Blastlog.Body").Also(t => t.TextWrapping = TextWrapping.Wrap),
      T("You can undo the whole import later. Games still waiting for a look can be added after.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 6, 0, 0)), Actions(go, discard)));
  }

  /// <summary>The site's bulk bar: "N selected", a button per pile, Clear (shown while any are ticked).</summary>
  FrameworkElement Bulk(IReadOnlyList<ImportChoice> piles) {
    if (selected.Count == 0) return null;
    var bar = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
    bar.Children.Add(T($"{selected.Count:N0} selected", "Blastlog.Body", null, 0, FontWeights.Bold).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 12, 8); }));
    foreach (var p in piles) { var pid = p.Id; bar.Children.Add(Kit.Button(p.Label, () => { var ids = selected.ToList(); selected.Clear(); Sort(ids, new PileChange(pid, null)); }, "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 8, 8))); }
    bar.Children.Add(Kit.Button("Clear", () => { selected.Clear(); Render(); }, "Blastlog.TextButton").Also(b => b.Margin = new Thickness(0, 0, 0, 8)));
    AutomationProperties.SetName(bar, "Move the selected games");
    return bar;
  }

  static string Same(string title) => new string((title ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

  static readonly Dictionary<string, string> Warnings = new Dictionary<string, string> {
    ["wishlist_partial"] = "Steam only copies the wishlist games it has on screen. On a computer, copy the page’s code instead (Ctrl + U on the wishlist) to get them all at once. On a phone, scroll to the games that aren’t in yet, copy again and paste under this; nothing gets added twice.",
    ["fewer_than_stated"] = "The page says you have more games than came through. Scroll all the way down before copying, then copy again.",
    ["steam_page_partial"] = "Steam only copies what it has drawn on screen. If this is fewer games than Steam shows, scroll to the very bottom of the list and copy again.",
    ["page_code"] = "That’s the page’s code, not the page, and your games aren’t in it. Go back to the page itself, select everything and copy that instead.",
    ["ends_early"] = "This list stops partway through the alphabet. If it got cut off, copy it again.",
    ["more_pages"] = "This is only one page of your list. Go to the next page, copy it the same way and paste it under this one (or make the page show more games first).",
    ["nothing_found"] = "No games found in that. Check you copied the list itself." };

  /// <summary>Steam's playtime groups: change a whole group at once, open one to change a game; Played some goes through Quick sort.</summary>
  readonly HashSet<string> openGroups = new HashSet<string>();
  FrameworkElement PlayGroups(List<ImportRow> playRows) {
    var list = new StackPanel();
    foreach (var g in ImportSort.PlayGroups) {
      var group = playRows.Where(r => r.Play.Group == g.id).ToList();
      if (group.Count == 0) continue;
      var gid = g.id;
      var values = new HashSet<string>(group.Select(r => ImportSort.ValueOf(r.Pile, r.ChosenStatus)));
      var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
      var select = PileBox(values.Count == 1 ? values.First() : "", "Pile for " + g.title, c => Sort(group.Select(r => r.Id), c), mixed: values.Count != 1);
      DockPanel.SetDock(select, Dock.Right); head.Children.Add(select);
      if (gid == "some") { var quick = Kit.Button("Quick sort", () => OpenQuick()); quick.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(quick, Dock.Right); head.Children.Add(quick); }
      var open = openGroups.Contains(gid);
      var name = new Button { Style = Kit.Style("Blastlog.TextButton"), HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 44,
        Content = Stack(null, T($"{(open ? "▾" : "▸")}  {g.title} · {group.Count}", "Blastlog.Body", null, 0, FontWeights.Bold), T(g.note, "Blastlog.Small")) };
      AutomationProperties.SetName(name, $"{g.title}, {group.Count}"); AutomationProperties.SetItemStatus(name, open ? "Expanded" : "Collapsed");
      name.Click += (s, e) => { if (!openGroups.Remove(gid)) openGroups.Add(gid); Render(); };
      head.Children.Add(name);
      list.Children.Add(head);
      if (open) {
        var games = new StackPanel { Margin = new Thickness(24, 0, 0, 12) };
        foreach (var r in group) { var row = r; games.Children.Add(GameRow(row, ImportSort.PlayLine(row.Play), PileBox(ImportSort.ValueOf(row.Pile, row.ChosenStatus), "Pile for " + row.Name, c => Sort(new[] { row.Id }, c)))); }
        list.Children.Add(games);
      }
    }
    return Card("Sorted by your Steam playtime", "Change a whole group at once, or open it to change one game. Nothing goes in until you press Import.", list);
  }
}
}
