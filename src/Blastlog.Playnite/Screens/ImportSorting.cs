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
using System.Windows.Threading;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// The rest of one import's page (Screens/ImportBatch.cs): games that need a look, Quick sort, what happened once it went
/// in (with Undo), the sort cards and the whole list, and their keys (as the site's: Y / N / S, 1–5 or the No list,
/// Backspace for Back, Escape to stop; Quick sort's 1–8).
/// </summary>
sealed partial class ImportBatchView {
  readonly HashSet<string> handled;

  // ---- Keys ----

  /// <summary>A key for the open cards or Quick sort; true when it was theirs. Typing in a field or a select is left alone.</summary>
  public bool Key(KeyEventArgs e) {
    if (Keyboard.Modifiers != ModifierKeys.None && Keyboard.Modifiers != ModifierKeys.Shift) return false;
    if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox || Keyboard.FocusedElement is ComboBoxItem) return false;
    var n = Number(e.Key);
    if (quickOpen) {
      if (n >= 1 && n <= ImportSort.Quick.Count) { QuickChoose(ImportSort.Quick[n - 1].change); return true; }
      if (e.Key == System.Windows.Input.Key.Back) { QuickBack(); return true; }
      if (e.Key == System.Windows.Input.Key.Escape) { quickOpen = false; Render(); return true; }
      return false;
    }
    if (Status != "committed" || show != "cards") return false;
    if (e.Key == System.Windows.Input.Key.Back) { CardBack(); return true; }
    if (e.Key == System.Windows.Input.Key.Escape) { CardsDone(); return true; }
    var row = CardRow;
    if (row == null) return false;
    if (stage == "ask") {
      if (e.Key == System.Windows.Input.Key.Y) { Answer("done", false, "rate"); return true; }
      if (e.Key == System.Windows.Input.Key.N) { stage = "no"; Render(); return true; }
      if (e.Key == System.Windows.Input.Key.S) { Answer(null, false); return true; }
    } else if (stage == "rate") {
      if (n >= 1 && n <= 5) { Rate(n); return true; }
      if (e.Key == System.Windows.Input.Key.S) { NextCard(); return true; }
    } else if (stage == "no" && n >= 1 && n <= ImportSort.NotFinished.Count) {
      var pick = ImportSort.NotFinished[n - 1]; Answer(pick.status, pick.remove); return true;
    }
    return false;
  }
  static int Number(System.Windows.Input.Key k) => k >= System.Windows.Input.Key.D0 && k <= System.Windows.Input.Key.D9 ? k - System.Windows.Input.Key.D0
    : k >= System.Windows.Input.Key.NumPad0 && k <= System.Windows.Input.Key.NumPad9 ? k - System.Windows.Input.Key.NumPad0 : -1;

  static FrameworkElement KeyHint(string label, string key) => Row(T(label, "Blastlog.Body", null, 0, FontWeights.Bold),
    Kit.Chip(key, neutral: true).Also(c => { c.Margin = new Thickness(8, 0, 0, 0); c.VerticalAlignment = VerticalAlignment.Center; }));
  static Button Choice(string label, string key, Action click, string style = "Blastlog.Ghost") {
    var b = new Button { Content = KeyHint(label, key), Style = Kit.Style(style), MinHeight = 48, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(16, 6, 16, 6) };
    AutomationProperties.SetName(b, label); AutomationProperties.SetAcceleratorKey(b, key);
    b.Click += (s, e) => { try { click(); } catch (Exception ex) { Log.Error(ex, "Blastlog import: " + label); } };
    return b;
  }

  // ---- Quick sort (Played some, before it went in) ----

  bool quickOpen; List<string> quickOrder = new List<string>(); int quickAt;
  readonly List<(string id, PileChange before)> quickHistory = new List<(string, PileChange)>();

  void OpenQuick() {
    quickOrder = ImportSort.QuickRows(rows.Where(r => r.State == "confident")).Select(r => r.Id).ToList();
    quickAt = 0; quickHistory.Clear(); quickOpen = true; Render();
  }
  ImportRow QuickRow => quickAt < quickOrder.Count ? rows.FirstOrDefault(r => r.Id == quickOrder[quickAt]) : null;
  void QuickChoose(PileChange change) {
    var row = QuickRow; if (row == null) return;
    quickHistory.Add((row.Id, new PileChange(row.Pile, row.ChosenStatus)));
    quickAt++; Sort(new[] { row.Id }, change);
  }
  void QuickBack() {
    if (quickHistory.Count == 0) return;
    var last = quickHistory[quickHistory.Count - 1]; quickHistory.RemoveAt(quickHistory.Count - 1);
    quickAt = Math.Max(0, quickAt - 1); Sort(new[] { last.id }, last.before);
  }

  FrameworkElement QuickSort() {
    var row = QuickRow;
    var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
    var done = Kit.Button("Done", () => { quickOpen = false; Render(); }, "Blastlog.TextButton"); DockPanel.SetDock(done, Dock.Right); top.Children.Add(done);
    top.Children.Add(T(row != null ? $"{quickAt + 1} of {quickOrder.Count} · most played first" : $"All {quickOrder.Count} sorted", "Blastlog.Muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    var panel = Stack(null, top);
    var backButton = Kit.Button("Back", QuickBack, "Blastlog.TextButton"); backButton.IsEnabled = quickHistory.Count > 0;
    if (row != null) {
      panel.Children.Add(CardGame(row, row.Play != null ? ImportSort.PlayLine(row.Play) : ""));
      var choices = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
      for (var i = 0; i < ImportSort.Quick.Count; i++) { var q = ImportSort.Quick[i]; choices.Children.Add(Choice(q.label, (i + 1).ToString(CultureInfo.InvariantCulture), () => QuickChoose(q.change))); }
      panel.Children.Add(choices);
      panel.Children.Add(Wrap(T("Tap, or press the number. ", "Blastlog.Muted"), backButton, T(" undoes the last one. Stop any time: the rest stay Backlog.", "Blastlog.Muted")));
    } else panel.Children.Add(Wrap(T("That’s all of them. ", "Blastlog.Body"), backButton, T(" to change the last one, or Done to import.", "Blastlog.Body")));
    var card = Kit.Card(panel, game: row != null); card.Margin = new Thickness(0, 0, 0, 18);
    if (row != null) Kit.GameScope(card, plugin, new BlastlogGame { Id = row.GameId ?? row.Id, Title = row.Name, CoverUrl = CoverUrl(row) });
    AutomationProperties.SetName(card, "Quick sort");
    return card;
  }

  static string CoverUrl(ImportRow row) => row.CoverUrl ?? (row.CoverImageId != null ? Artwork.IgdbImageUrl(row.CoverImageId, "cover_big") : null);

  /// <summary>A card's game: big cover, name, its line and how far through the game the playtime is.</summary>
  FrameworkElement CardGame(ImportRow row, string line, string prefix = null) {
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
    var cover = Kit.Cover(plugin, new BlastlogGame { Id = row.GameId ?? row.Id, Title = row.Name, CoverUrl = CoverUrl(row) }, 120, ArtworkContext.Cover, 12);
    cover.Margin = new Thickness(0, 0, 20, 0); cover.VerticalAlignment = VerticalAlignment.Top; grid.Children.Add(cover);
    var title = T(row.Name, "Blastlog.PageTitle").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Focusable = true; });
    AutomationProperties.SetName(title, (prefix ?? "") + row.Name);
    cardTitle = title;
    var words = Stack(null, title, T(line, "Blastlog.Muted").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 6, 0, 0); }));
    var share = ImportSort.Share(row.Play);
    if (share != null) {
      var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = share.Value, Height = 8, Margin = new Thickness(0, 10, 0, 0), MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, Width = 360 };
      bar.SetResourceReference(Control.ForegroundProperty, "Blastlog.Game"); bar.SetResourceReference(Control.BackgroundProperty, "Blastlog.Surface2");
      AutomationProperties.SetName(bar, $"Played {Math.Round(share.Value * 100)}% of how long it takes to beat");
      words.Children.Add(bar);
    }
    Grid.SetColumn(words, 1); grid.Children.Add(words);
    return grid;
  }
  TextBlock cardTitle;

  // ---- Needs a look ----

  bool showNotGames, showNotFound;
  sealed class LookSearch { public string Query; public JObject Results; public bool Busy; }
  readonly Dictionary<string, LookSearch> searches = new Dictionary<string, LookSearch>();

  FrameworkElement LookSection() {
    var look = rows.Where(r => r.State == "needs_look" && r.Outcome == null && r.Pile != "skip").ToList();
    var notGames = rows.Where(r => r.State == "not_a_game" && r.Outcome == null && !ImportSort.IsSplitBundle(r)).ToList();
    var notFound = rows.Where(r => r.State == "unmatched" && r.Outcome == null).ToList();
    if (look.Count == 0 && notGames.Count == 0 && notFound.Count == 0) return null;
    var suggested = look.Count(r => r.Options.Any(o => o.Suggested));
    var parts = new List<UIElement>();
    if (suggested > 0) {
      var use = Kit.Button(busy == "suggested" ? "Working…" : $"Use the suggested ones ({suggested})", () => Act("suggested", async () => {
        var r = await plugin.Write(Path + "/accept-suggested", new JObject());
        if (r == null) return null;
        var resolved = (int?)r.D("resolved") ?? 0; var asking = (int?)r.D("asking") ?? 0;
        return $"Used the suggested game for {Plural(resolved, "one")}." + (asking > 0 ? $" {Plural(asking, "more needs", "more need")} a look: you may already have {(asking == 1 ? "it" : "them")} under another name." : "");
      }));
      use.IsEnabled = busy == null; use.HorizontalAlignment = HorizontalAlignment.Left; use.Margin = new Thickness(0, 0, 0, 14);
      parts.Add(use);
    }
    foreach (var r in look) parts.Add(LookCard(r));
    if (notGames.Count > 0) parts.Add(Fold($"Not games, skipped ({notGames.Count}): soundtracks, artbooks, tools, demos, add-ons", showNotGames, v => showNotGames = v, notGames));
    if (notFound.Count > 0) parts.Add(Fold($"Not found ({notFound.Count})", showNotFound, v => showNotFound = v, notFound));
    return Card(look.Count > 0 ? $"Needs a look ({look.Count})" : "Not found",
      look.Count > 0 ? "Blastlog wasn’t sure about these. Pick the right game, or skip it. They never hold the rest of the import back." : null, parts.ToArray());
  }

  FrameworkElement Fold(string title, bool open, Action<bool> set, List<ImportRow> list) {
    var panel = Stack(new Thickness(0, 8, 0, 0));
    var head = Kit.Button($"{(open ? "▾" : "▸")}  {title}", () => { set(!open); Render(); }, "Blastlog.TextButton");
    head.HorizontalAlignment = HorizontalAlignment.Left; AutomationProperties.SetItemStatus(head, open ? "Expanded" : "Collapsed");
    panel.Children.Add(head);
    if (open) foreach (var r in list) panel.Children.Add(LookCard(r));
    return panel;
  }

  void Resolve(ImportRow row, JObject choice) => Run(() => Act("row-" + row.Id, async () => {
    var result = await plugin.Write($"{Path}/rows/{Uri.EscapeDataString(row.Id)}/resolve", choice);
    searches.Remove(row.Id);
    return Status == "committed" && result?.S("outcome") != null ? Wishlist ? "Added to your wishlist." : "Added to your library." : null;
  }), "import resolve");

  FrameworkElement LookCard(ImportRow row) {
    var panel = Stack(new Thickness(0, 0, 0, 0));
    var steamId = ImportSort.SteamIdOnly(row.RawTitle);
    panel.Children.Add(T($"“{row.RawTitle}”", "Blastlog.Body", null, 0, FontWeights.Bold).Also(t => t.TextWrapping = TextWrapping.Wrap));
    var reason = Wrap(T(ImportSort.LookReason(row), "Blastlog.Small").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 760; }));
    if (steamId != null) { reason.Children.Add(Fingerprint.LinkButton(" See it on Steam ↗", () => UiKit.Open("https://store.steampowered.com/app/" + steamId + "/"), game: true)); reason.Children.Add(T(", then search for it by name.", "Blastlog.Small")); }
    panel.Children.Add(reason);
    if (row.Options.Count > 0) {
      var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
      foreach (var o in row.Options) { var opt = o; options.Children.Add(OptionButton(opt.Title, opt.Line(), opt.CoverUrl, opt.CoverImageId, opt.Suggested, () => Resolve(row, opt.Choice()))); }
      panel.Children.Add(options);
    }
    searches.TryGetValue(row.Id, out var search);
    if (search != null) panel.Children.Add(SearchForm(row, search));
    var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
    var enabled = busy == null;
    if (search == null) actions.Children.Add(Kit.Button("Search…", () => { searches[row.Id] = new LookSearch { Query = row.RawTitle }; Render(); }, "Blastlog.Ghost").Also(b => { b.Margin = new Thickness(0, 0, 8, 6); b.IsEnabled = enabled; }));
    if (row.State != "not_a_game") actions.Children.Add(Kit.Button("Not a game", () => Resolve(row, new JObject { ["notAGame"] = true }), "Blastlog.Ghost").Also(b => { b.Margin = new Thickness(0, 0, 8, 6); b.IsEnabled = enabled; }));
    if (row.State == "needs_look") actions.Children.Add(Kit.Button("Skip", () => Resolve(row, new JObject { ["skip"] = true }), "Blastlog.Ghost").Also(b => { b.Margin = new Thickness(0, 0, 8, 6); b.IsEnabled = enabled; }));
    if (row.State == "needs_look" && !Wishlist) {
      actions.Children.Add(T("When it goes in", "Blastlog.Small").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(6, 0, 8, 6); }));
      actions.Children.Add(PileBox(ImportSort.ValueOf(row.Pile, row.ChosenStatus), "When it goes in for " + row.RawTitle, c => Sort(new[] { row.Id }, c)).Also(b => b.Margin = new Thickness(0, 0, 0, 6)));
    }
    panel.Children.Add(actions);
    var box = new Border { Child = panel, Padding = new Thickness(14), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
    box.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line"); box.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
    return box;
  }

  FrameworkElement SearchForm(ImportRow row, LookSearch search) {
    var panel = Stack(new Thickness(0, 10, 0, 0));
    var (box, input) = SearchField("Search for " + row.RawTitle, 360);
    input.Text = search.Query ?? "";
    async Task Go() {
      search.Query = input.Text.Trim(); if (search.Query.Length < 2) return;
      search.Busy = true; Render();
      var q = Uri.EscapeDataString(search.Query.Length > 100 ? search.Query.Substring(0, 100) : search.Query);
      search.Results = (await plugin.Mirror("catalog/search?q=" + q, fresh: false)).Value ?? new JObject();
      search.Busy = false; Render();
    }
    input.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; Run(Go, "import search"); } };
    panel.Children.Add(Row(box, Kit.Button("Search", Go, "Blastlog.Ghost").Also(b => b.Margin = new Thickness(8, 0, 0, 0))));
    if (search.Results == null && !search.Busy) Dispatcher.BeginInvoke(new Action(() => { try { input.Focus(); input.SelectAll(); } catch (Exception) { } }), DispatcherPriority.Input);
    if (search.Busy) panel.Children.Add(T("Searching…", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
    else if (search.Results != null) {
      var items = search.Results.Objects("items").Take(6).ToList();
      var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
      foreach (var item in items) {
        var ext = item.S("externalId"); var date = item.S("releaseDate");
        options.Children.Add(OptionButton(item.S("title") ?? "", date?.Length >= 4 ? date.Substring(0, 4) : "", null, item["artwork"]?.S("coverImageId"), false,
          () => Resolve(row, new JObject { ["igdbId"] = ext })));
      }
      if (items.Count == 0) options.Children.Add(T(search.Results["items"] == null ? "IGDB search is temporarily unavailable." : "Nothing found. Try fewer words.", "Blastlog.Muted"));
      panel.Children.Add(options);
    }
    return panel;
  }

  /// <summary>A match option (the site's imp-option): cover, name, its line; the suggested one marked.</summary>
  Button OptionButton(string title, string line, string coverUrl, string imageId, bool suggested, Action pick) {
    var grid = new Grid { Width = 300 };
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
    grid.Children.Add(CoverOf(null, title, coverUrl, imageId, 40));
    var words = Stack(null, T(title, "Blastlog.Body", null, 0, FontWeights.Bold).Also(t => t.TextWrapping = TextWrapping.Wrap));
    if (!string.IsNullOrEmpty(line)) words.Children.Add(T(line, "Blastlog.Small").Also(t => t.TextWrapping = TextWrapping.Wrap));
    words.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(words, 1); grid.Children.Add(words);
    var frame = new Border { Child = grid, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(suggested ? 2 : 1) };
    frame.SetResourceReference(Border.BorderBrushProperty, suggested ? "Blastlog.Game" : "Blastlog.Edge"); frame.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface");
    var b = new Button { Content = frame, Style = Kit.Style("Blastlog.TextButton"), Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 8), MinHeight = 44, IsEnabled = busy == null };
    AutomationProperties.SetName(b, title + (string.IsNullOrEmpty(line) ? "" : ", " + line));
    b.Click += (s, e) => { try { pick(); } catch (Exception ex) { Log.Error(ex, "Blastlog import: pick"); } };
    return b;
  }

  // ---- After it went in ----

  const int CardSeconds = 4;

  FrameworkElement Added() {
    var added = rows.Count(r => r.Outcome == "committed"); var linked = rows.Count(r => r.Outcome == "linked");
    var newGames = rows.Count(r => r.Outcome == "committed" && r.NewToBlastlog);
    var result = batch["result"] as JObject;
    if (Status == "undone") {
      var kept = (int?)result?.D("kept") ?? 0;
      return Card(null, null, Runs("Blastlog.Body", ("Undone. ", true, null), ($"Removed {Plural((int?)result?.D("undone") ?? 0, "game")} this import had added." +
          (kept > 0 ? $" Kept {Plural(kept, "game")} you’d changed since (or that another import also brought in)." : "") + " Games that were already in your library weren’t touched.", false, null)).Also(t => t.TextWrapping = TextWrapping.Wrap),
        Actions(Kit.Button("Import again on Blastlog ↗", () => Nav.Site(plugin, "/library/import")), Kit.Button("Go to your library", () => { close(); Nav.Library(plugin); }, "Blastlog.TextButton")));
    }
    var never = rows.Count(r => r.Outcome == "committed" && r.Play?.Group == "never");
    var cards = Wishlist ? new List<ImportRow>() : ImportSort.CardRows(rows);
    var left = cards.Count(r => !handled.Contains(r.Id));
    var notes = string.Join(" ", new[] { never > 0 ? $"{Plural(never, "game")} you’ve never played went to Backlog." : null,
      linked > 0 ? $"{Plural(linked, "was", "were")} already in your library and stayed as {(linked == 1 ? "it was" : "they were")}." : null }.Where(n => n != null));
    var parts = new List<UIElement>();
    if (notes.Length > 0) parts.Add(T(notes, "Blastlog.Muted").Also(t => t.TextWrapping = TextWrapping.Wrap));
    if (cards.Count > 0) {
      parts.Add(T(left == 0 ? "All sorted." : left < cards.Count ? $"{Plural(left, "game")} left to sort."
        : cards.Any(r => r.Play != null) ? $"{Plural(left, "game")} you’ve played. Which did you finish?" : "Which ones did you finish?", "Blastlog.Section").Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
      if (left > 0) parts.Add(Kit.Button($"{(left < cards.Count ? "Keep sorting" : "Sort them")} · about {Math.Max(1, (int)Math.Round(left * CardSeconds / 60.0))} min", OpenCards)
        .Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 10, 0, 0); }));
    }
    var library = Kit.Button("Go to your library", () => { close(); Nav.Library(plugin); }, left > 0 ? "Blastlog.Ghost" : "Blastlog.Primary");
    var list = added > 0 ? Kit.Button(show == "list" ? "Hide the list" : $"See all {added:N0}", () => { show = show == "list" ? "summary" : "list"; Render(); }, "Blastlog.TextButton") : null;
    if (list != null) AutomationProperties.SetItemStatus(list, show == "list" ? "Expanded" : "Collapsed");
    var elsewhere = Kit.Button("Import from somewhere else", back, "Blastlog.TextButton");
    var undo = confirmUndo ? null : Kit.Button(busy == "undo" ? "Undoing…" : "Undo this import", () => { confirmUndo = true; Render(); }, "Blastlog.TextButton");
    if (undo != null) undo.IsEnabled = busy == null;
    parts.Add(Actions(library, list, elsewhere, undo));
    if (batch["fingerprints"] is JObject fp && (fp.D("total") ?? 0) > 0) parts.Add(FingerprintProgress((int)fp.D("total"), (int)(fp.D("done") ?? 0), (int)(fp.D("building") ?? 0)));
    else if (newGames > 0) parts.Add(T($"{Plural(newGames, "game")} {(newGames == 1 ? "is" : "are")} new to Blastlog and get{(newGames == 1 ? "s its" : " their")} traits over the next few days. Picks get better as they fill in.", "Blastlog.Muted").Also(t => t.TextWrapping = TextWrapping.Wrap));
    if (confirmUndo) {
      var yes = Kit.Button("Undo this import", () => { confirmUndo = false; return Act("undo", async () => { await plugin.Write(Path + "/undo", new JObject()); return null; }); }, "Blastlog.DangerButton");
      parts.Add(Stack(new Thickness(0, 12, 0, 0), T("Undo this import? Games it added that you haven’t changed since are removed. Games that were already in your library stay.", "Blastlog.Body").Also(t => t.TextWrapping = TextWrapping.Wrap),
        Actions(yes, Kit.Button("Cancel", () => { confirmUndo = false; Render(); }, "Blastlog.Ghost"))));
    }
    return Card(null, null, parts.ToArray());
  }

  static FrameworkElement FingerprintProgress(int total, int done, int building) {
    var waiting = total - done - building;
    if (done >= total) return T($"All {Plural(total, "game")} new to Blastlog {(total == 1 ? "is" : "are")} fingerprinted. Their traits are on each game’s page.", "Blastlog.Muted").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 10, 0, 0); });
    var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, total), Value = done, Height = 8, Margin = new Thickness(0, 8, 0, 6) };
    bar.SetResourceReference(Control.ForegroundProperty, "Blastlog.Game"); bar.SetResourceReference(Control.BackgroundProperty, "Blastlog.Surface2");
    var label = $"{Plural(total, "game")} new to Blastlog: {done} fingerprinted, {building} in progress{(waiting > 0 ? $", {waiting} waiting their turn" : "")}";
    AutomationProperties.SetName(bar, label);
    return Stack(new Thickness(0, 12, 0, 0), T(label, "Blastlog.Body", null, 0, FontWeights.SemiBold).Also(t => t.TextWrapping = TextWrapping.Wrap), bar,
      T("Each game’s traits fill in by themselves, usually within the hour. Picks get better as they do. This updates by itself.", "Blastlog.Muted").Also(t => t.TextWrapping = TextWrapping.Wrap));
  }

  // ---- The sort cards ----

  List<string> order = new List<string>(); int at; string stage = "ask";
  sealed class CardStep { public string Id; public int At; public string Before; public bool Changed, Rated; }
  readonly List<CardStep> history = new List<CardStep>();

  void OpenCards() {
    order = ImportSort.CardRows(rows).Select(r => r.Id).ToList();
    var first = order.FindIndex(x => !handled.Contains(x)); at = first < 0 ? order.Count : first;
    stage = "ask"; history.Clear(); show = "cards"; flash = null; Render(); FocusCard();
  }
  void CardsDone() { show = "summary"; Render(); }
  ImportRow CardRow => show == "cards" && at < order.Count ? rows.FirstOrDefault(r => r.Id == order[at]) : null;
  void Mark(string rowId, bool on) { if (on) handled.Add(rowId); else handled.Remove(rowId); ImportPage.Handled.Save(plugin); }
  void FocusCard() => Dispatcher.BeginInvoke(new Action(() => { try { (stage == "ask" ? (UIElement)cardTitle : stageFirst)?.Focus(); } catch (Exception) { } }), DispatcherPriority.Input);
  UIElement stageFirst;

  void NextCard() { var row = CardRow; if (row != null) Mark(row.Id, true); at++; stage = "ask"; Render(); FocusCard(); }
  void Answer(string status, bool remove, string then = "next") {
    var row = CardRow; if (row == null) return;
    var before = ImportSort.StatusOf(row.Pile, row.ChosenStatus);
    var changed = remove || (status != null && status != before);
    if (changed) Change(new[] { row.Id }, status, remove: remove);
    history.Add(new CardStep { Id = row.Id, At = at, Before = before, Changed = changed });
    if (then == "rate") { stage = "rate"; Render(); FocusCard(); } else NextCard();
  }
  void Rate(int stars) {
    var row = CardRow; if (row == null) return;
    Change(new[] { row.Id }, rating: stars);
    if (history.Count > 0) history[history.Count - 1].Rated = true;
    NextCard();
  }
  void CardBack() {
    if (stage == "no") { stage = "ask"; Render(); FocusCard(); return; }
    if (history.Count == 0) return;
    var last = history[history.Count - 1];
    if (last.Changed || last.Rated) Change(new[] { last.Id }, last.Before ?? "not_started", clearRating: last.Rated);
    history.RemoveAt(history.Count - 1); Mark(last.Id, false); at = last.At; stage = "ask"; Render(); FocusCard();
  }

  FrameworkElement SortCards() {
    var row = CardRow;
    if (row == null) {
      var change = history.Count > 0 ? Kit.Button("Change the last one", CardBack, "Blastlog.TextButton") : null;
      return Card(null, null, T(order.Count > 0 ? "All sorted." : "Nothing to sort here.", "Blastlog.Section"),
        Actions(Kit.Button("Go to your library", () => { close(); Nav.Library(plugin); }), Kit.Button("Back to the import", CardsDone, "Blastlog.TextButton"), change));
    }
    var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
    var done = Kit.Button("Done for now", CardsDone, "Blastlog.TextButton"); DockPanel.SetDock(done, Dock.Right); top.Children.Add(done);
    top.Children.Add(T($"{at + 1} of {order.Count}", "Blastlog.Muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    var panel = Stack(null, top);
    var now = ImportSort.Now(ImportSort.StatusOf(row.Pile, row.ChosenStatus) ?? "not_started");
    var line = string.Join(" · ", new[] { row.Play != null ? ImportSort.PlayLine(row.Play) : row.Year?.ToString(CultureInfo.InvariantCulture), "Now: " + now }.Where(x => !string.IsNullOrEmpty(x)));
    panel.Children.Add(CardGame(row, line, $"{at + 1} of {order.Count}: "));
    var ask = Stack(new Thickness(0, 18, 0, 0));
    stageFirst = null;
    if (stage == "ask") {
      ask.Children.Add(T("Finished it?", "Blastlog.Section").Also(t => t.Margin = new Thickness(0, 0, 0, 10)));
      ask.Children.Add(Wrap(Choice("Yes", "Y", () => Answer("done", false, "rate"), "Blastlog.Primary"), Choice("No", "N", () => { stage = "no"; Render(); FocusCard(); }), Choice("Skip", "S", () => Answer(null, false), "Blastlog.TextButton")));
    } else if (stage == "rate") {
      ask.Children.Add(T("Rate it?", "Blastlog.Section").Also(t => t.Margin = new Thickness(0, 0, 0, 10)));
      var stars = new WrapPanel();
      for (var i = 1; i <= 5; i++) {
        var n = i;
        var b = new Button { Content = T("★", "Blastlog.Body", "Blastlog.Star", 30), Style = Kit.Style("Blastlog.Ghost"), MinWidth = 52, MinHeight = 52, Margin = new Thickness(0, 0, 8, 8) };
        AutomationProperties.SetName(b, $"{n} star{(n > 1 ? "s" : "")}"); AutomationProperties.SetAcceleratorKey(b, n.ToString(CultureInfo.InvariantCulture));
        b.Click += (s, e) => { try { Rate(n); } catch (Exception ex) { Log.Error(ex, "Blastlog import: rate"); } };
        if (stageFirst == null) stageFirst = b;
        stars.Children.Add(b);
      }
      ask.Children.Add(stars);
      ask.Children.Add(Kit.Button("Skip", NextCard, "Blastlog.TextButton").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
    } else {
      ask.Children.Add(T("Where are you with it?", "Blastlog.Section").Also(t => t.Margin = new Thickness(0, 0, 0, 10)));
      var more = new WrapPanel();
      for (var i = 0; i < ImportSort.NotFinished.Count; i++) {
        var pick = ImportSort.NotFinished[i];
        var b = Choice(pick.label, (i + 1).ToString(CultureInfo.InvariantCulture), () => Answer(pick.status, pick.remove), pick.remove ? "Blastlog.DangerButton" : "Blastlog.Ghost");
        if (stageFirst == null) stageFirst = b;
        more.Children.Add(b);
      }
      ask.Children.Add(more);
    }
    panel.Children.Add(ask);
    var backButton = Kit.Button("Back", CardBack, "Blastlog.TextButton"); backButton.IsEnabled = history.Count > 0 || stage == "no";
    panel.Children.Add(Wrap(backButton, T("  Keys: Y, N, S, 1–5, Backspace for Back, Escape to stop.", "Blastlog.Small").Also(t => t.VerticalAlignment = VerticalAlignment.Center)).Also(w => w.Margin = new Thickness(0, 8, 0, 0)));
    var card = Kit.Card(panel, game: true); card.Margin = new Thickness(0, 0, 0, 18);
    Kit.GameScope(card, plugin, new BlastlogGame { Id = row.GameId ?? row.Id, Title = row.Name, CoverUrl = CoverUrl(row) });
    AutomationProperties.SetName(card, "Sort your games");
    return card;
  }

  // ---- The whole list, after it went in ----

  string listFilter = "all"; readonly HashSet<string> listSelected = new HashSet<string>(); bool listAll;

  /// <summary>The site's status select once it went in: Finished, Playing, Backlog, Not interested, More, Removed (a wishlist: Wishlist, Removed).</summary>
  ComboBox EntryBox(ImportRow row) {
    var box = new ComboBox { MinHeight = 44, MinWidth = 180, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center };
    if (Wishlist) box.Items.Add(new ComboBoxItem { Content = "Wishlist", Tag = "not_started" });
    else {
      foreach (var c in ImportSort.EntryChoices) box.Items.Add(new ComboBoxItem { Content = c.Label, Tag = c.Id });
      box.Items.Add(new ComboBoxItem { Content = "More", IsEnabled = false, FontWeight = FontWeights.Bold, Tag = null });
      foreach (var c in ImportSort.EntryMore) box.Items.Add(new ComboBoxItem { Content = "   " + c.Label, Tag = c.Id });
    }
    box.Items.Add(new ComboBoxItem { Content = "Removed", Tag = "remove" });
    var value = ImportSort.StatusOf(row.Pile, row.ChosenStatus) ?? "remove";
    box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value);
    AutomationProperties.SetName(box, "Status of " + row.Name);
    box.SelectionChanged += (s, e) => {
      try { if (box.SelectedItem is ComboBoxItem i && i.Tag is string v && v != value) { if (v == "remove") Change(new[] { row.Id }, remove: true); else Change(new[] { row.Id }, v); Render(); } }
      catch (Exception ex) { Log.Error(ex, "Blastlog import: status"); }
    };
    return box;
  }

  void AddedList() {
    var mine = rows.Where(r => r.Outcome == "committed").ToList();
    var piles = Wishlist ? ImportSort.EntryPiles.Where(p => p.Id == "backlog" || p.Id == "skip").Select(p => p.Id == "backlog" ? new ImportChoice("backlog", "Wishlist") : p).ToList() : ImportSort.EntryPiles.ToList();
    var shown = mine.Where(r => (listFilter == "all" || r.Pile == listFilter) && Matches(r)).ToList();
    var chips = new WrapPanel();
    chips.Children.Add(Chip("All", mine.Count, listFilter == "all", () => { listFilter = "all"; Render(); }));
    foreach (var p in piles) { var pid = p.Id; chips.Children.Add(Chip(p.Label, mine.Count(r => r.Pile == pid), listFilter == pid, () => { listFilter = pid; Render(); })); }
    var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
    var sb = SearchBox(); DockPanel.SetDock(sb, Dock.Right); sb.VerticalAlignment = VerticalAlignment.Top; tools.Children.Add(sb); tools.Children.Add(chips);
    var allOn = shown.Count > 0 && shown.All(r => listSelected.Contains(r.Id));
    var selectAll = LinkPages.Check($"Select all {(shown.Count == mine.Count ? "" : "shown ")}({shown.Count:N0})", allOn, v => {
      if (v) foreach (var r in shown) listSelected.Add(r.Id); else foreach (var r in shown) listSelected.Remove(r.Id);
      Render();
    });
    FrameworkElement bulk = null;
    if (listSelected.Count > 0) {
      var bar = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
      bar.Children.Add(T($"{listSelected.Count:N0} selected", "Blastlog.Body", null, 0, FontWeights.Bold).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 12, 8); }));
      void Move(string value) { var ids = listSelected.ToList(); listSelected.Clear(); if (value == "remove") Change(ids, remove: true); else Change(ids, value); Render(); }
      if (!Wishlist) foreach (var c in ImportSort.EntryChoices) { var cid = c.Id; bar.Children.Add(Kit.Button(c.Label, () => Move(cid), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 8, 8))); }
      bar.Children.Add(Kit.Button("Remove", () => Move("remove"), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
      bar.Children.Add(Kit.Button("Clear", () => { listSelected.Clear(); Render(); }, "Blastlog.TextButton").Also(b => b.Margin = new Thickness(0, 0, 0, 8)));
      AutomationProperties.SetName(bar, "Change the selected games");
      bulk = bar;
    }
    var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
    foreach (var r in listAll ? shown : shown.Take(120)) {
      var row = r;
      var line = string.Join(" · ", new[] { row.Year?.ToString(CultureInfo.InvariantCulture), row.NewToBlastlog ? "New to Blastlog" : null,
        row.Play != null && !Wishlist ? ImportSort.PlayLine(row.Play) : null }.Where(x => x != null));
      var tick = Tick(listSelected.Contains(row.Id), "Select " + row.Name, v => { if (v) listSelected.Add(row.Id); else listSelected.Remove(row.Id); Render(); });
      list.Children.Add(GameRow(row, line, EntryBox(row), tick));
    }
    if (!listAll && shown.Count > 120) list.Children.Add(Kit.Button($"Show all {shown.Count:N0}", () => { listAll = true; Render(); }, "Blastlog.Ghost").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
    if (shown.Count == 0) list.Children.Add(T("Nothing here.", "Blastlog.Muted"));
    body.Children.Add(Card(null, null, tools, selectAll, bulk, list));
  }
}
}
