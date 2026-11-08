using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Add a game, natively (web App.tsx AddKnownGame, the dashboard's "+ New entry" and Currently playing's "+ Add a new
/// game"): search IGDB and the games Blastlog already has, pick one, say whether you own it or want it (and where you
/// own it), and add it. From Currently playing it's added as playing. A game already in your library opens instead.
/// After adding: Undo (the site's restorable remove), open its page, and pair it when the same game is in Playnite.
/// Needs the server's catalogue and library mirror routes (docs/PLAYNITE_SYNC.md); without them it says so and offers
/// the site.
/// </summary>
public static class AddGameWindow {
  static readonly ILogger Log = LogManager.GetLogger();
  /// <summary>Opens the window. <paramref name="status"/> "playing" adds it as playing; <paramref name="done"/> runs after each add or undo.</summary>
  public static async void Show(BlastlogPlugin plugin, string status = null, Action done = null) {
    if (opening) return; // a second click while the search status loads
    opening = true;
    try { await new AddGameFlow(plugin, status == "playing", done).Open(() => opening = false); }
    catch (Exception e) { Log.Error(e, "Blastlog add a game"); try { Nav.Site(plugin, "/dashboard"); } catch (Exception) { } }
    finally { opening = false; }
  }
  static bool opening;
}

sealed class AddGameFlow {
  static readonly ILogger Log = LogManager.GetLogger();
  const string SitePath = "/dashboard";
  readonly BlastlogPlugin plugin;
  readonly bool playing;
  readonly Action done;
  Window window;
  readonly DockPanel root = new DockPanel();
  readonly DispatcherTimer debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
  // Rebuilt each time the search view opens ("Add another" comes back to a fresh one).
  TextBox box;
  StackPanel catalogPanel, storedPanel;
  TextBlock error;
  readonly List<KeyValuePair<string, Border>> rows = new List<KeyValuePair<string, Border>>();
  readonly HashSet<string> stores = new HashSet<string>();
  readonly Dictionary<string, Button> storePills = new Dictionary<string, Button>();
  Button addButton, inLibrary, wishlist;
  FrameworkElement storesBlock;
  Choice selected;
  string ownership = "library";
  bool igdbOn, everything;
  int askId;

  sealed class Choice {
    public string ExternalId; public string GameId; public string Title;
    public string Key => ExternalId != null ? "igdb:" + ExternalId : "game:" + GameId;
  }

  public AddGameFlow(BlastlogPlugin plugin, bool playing, Action done) { this.plugin = plugin; this.playing = playing; this.done = done; }

  /// <summary>Reads whether IGDB search is on (cached), then shows the window; <paramref name="shown"/> runs as it appears.</summary>
  public async Task Open(Action shown) {
    string message = null, siteButton = null;
    if (!plugin.Configured) message = "Add your Blastlog sync token first: Add-ons, Extension settings, Blastlog.";
    else {
      var status = (await plugin.Mirror("catalog/status", fresh: false)).Value;
      if (status == null) { message = "Playnite can’t reach Blastlog’s game search right now. You can add the game on Blastlog instead."; siteButton = "Add it on Blastlog"; }
      else igdbOn = (bool?)status["igdb"]?["configured"] == true;
    }
    window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = false, ShowMinimizeButton = false });
    window.Title = playing ? "Add a game you’re playing" : "Add a game"; window.Width = 680; window.Height = message != null ? 300 : 820; window.MinWidth = 520; window.MinHeight = 260;
    window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow(); window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    // Never taller than Playnite's own window, so the Add button stays on screen.
    if (window.Owner != null && window.Owner.ActualHeight > 300) window.Height = Math.Min(window.Height, window.Owner.ActualHeight - 24);
    root.Resources.MergedDictionaries.Add(Kit.Styles);
    root.SetResourceReference(Panel.BackgroundProperty, "Blastlog.Surface");
    // The window's accent is Blastlog's own green (the site's modal wears the page colour; a dialog has no page).
    Kit.GameScope(root, GameColour.BrandGreen);
    window.Content = root;
    window.PreviewKeyDown += (s, e) => { try { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; } } catch (Exception ex) { Log.Error(ex, "Blastlog add a game: keys"); } };
    window.Closed += (s, e) => { try { debounce.Stop(); } catch (Exception) { } };
    debounce.Tick += (s, e) => { debounce.Stop(); Dash.Safe(Ask, "add a game: search"); };
    if (message != null) ShowMessage(message, siteButton); else ShowSearch();
    shown();
    window.ShowDialog();
  }

  // ---- The search view ----

  /// <summary>The unavailable state: a sentence, and (when it helps) a button that opens the site's Add a game.</summary>
  void ShowMessage(string text, string siteButton) {
    var body = new StackPanel { Margin = new Thickness(28, 26, 28, 26) };
    body.Children.Add(Dash.Text(text, "Blastlog.Body").Also(t => t.TextWrapping = TextWrapping.Wrap));
    var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
    if (siteButton != null) buttons.Children.Add(Kit.Button(siteButton + "  ↗", () => { window.Close(); Nav.Site(plugin, SitePath); }).Also(b => b.Margin = new Thickness(0, 0, 10, 0)));
    buttons.Children.Add(Kit.Button(siteButton != null ? "Cancel" : "Close", () => window.Close(), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0)));
    body.Children.Add(buttons);
    root.Children.Clear(); root.Children.Add(body);
  }

  void ShowSearch() {
    rows.Clear(); selected = null; everything = false; askId++; addButton = null; stores.Clear(); ownership = "library";
    box = Dash.Field(); catalogPanel = new StackPanel(); storedPanel = new StackPanel();
    error = Dash.Text("", "Blastlog.Small", "Blastlog.Danger"); error.Visibility = Visibility.Collapsed; error.TextWrapping = TextWrapping.Wrap;
    var body = new StackPanel { Margin = new Thickness(28, 22, 28, 16) };
    body.Children.Add(Dash.Text(igdbOn ? "Search for the game you want to add." : "Searching new games isn’t available right now; games already in Blastlog can still be found.", "Blastlog.Muted")
      .Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 14); }));
    body.Children.Add(Dash.Text("Search games", "Blastlog.Body", weight: FontWeights.SemiBold).Margin(0, 0, 0, 8));
    var field = Dash.FieldBox(box, "Type at least two letters");
    Dash.AutomationName(box, "Search games");
    body.Children.Add(field);
    body.Children.Add(catalogPanel.Margin(0, 6, 0, 0));
    body.Children.Add(storedPanel);

    body.Children.Add(Dash.Text("Blastlog records the game, not the hardware you played it on.", "Blastlog.Muted").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 16, 0, 10); }));
    body.Children.Add(Dash.Text("Ownership", "Blastlog.Body", weight: FontWeights.SemiBold).Margin(0, 0, 0, 8));
    inLibrary = AddGameParts.Pill("In library", () => SetOwnership("library"));
    wishlist = AddGameParts.Pill("Wishlist", () => SetOwnership("wishlist"));
    var owning = new WrapPanel { Children = { inLibrary, wishlist } };
    Dash.AutomationName(owning, "Ownership");
    body.Children.Add(owning);
    var storeBlock = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
    storeBlock.Children.Add(Dash.Text("Where you own it (optional)", "Blastlog.Muted").Margin(0, 0, 0, 8));
    var storeWrap = new WrapPanel(); Dash.AutomationName(storeWrap, "Where you own it");
    storePills.Clear();
    foreach (var s in AddGameParts.Stores) {
      var key = s.Key; var pill = AddGameParts.Pill(s.Value, () => { if (!stores.Remove(key)) stores.Add(key); AddGameParts.SetPill(storePills[key], stores.Contains(key)); }, box: true);
      AddGameParts.SetPill(pill, stores.Contains(key)); storePills[key] = pill; storeWrap.Children.Add(pill);
    }
    storeBlock.Children.Add(storeWrap);
    storesBlock = storeBlock;
    body.Children.Add(storeBlock);
    body.Children.Add(error.Margin(0, 10, 0, 0));
    SetOwnership(ownership);

    // The site's .modal .actions: Add is .primary (16px, 800, 44px tall, 18px sides); Cancel is plain muted words, no edge.
    addButton = new Button { Content = AddLabel, Style = Kit.Style("Blastlog.Primary"), IsEnabled = false, FontSize = 16, FontWeight = FontWeights.ExtraBold, MinHeight = 44, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0) };
    addButton.Click += (s, e) => Dash.Safe(Add, "add a game: add");
    var cancel = Kit.Button("Cancel", () => window.Close(), "Blastlog.TextButton").Also(b => { b.Margin = new Thickness(0, 0, 10, 0); b.FontSize = 16; b.FontWeight = FontWeights.Normal; b.MinHeight = 44; b.Padding = new Thickness(11, 0, 11, 0); });
    var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, addButton } };
    var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(28, 12, 28, 18), Child = buttons };
    footer.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");

    root.Children.Clear();
    DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
    root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

    box.TextChanged += Typed;
    box.PreviewKeyDown += FieldKeys;
    window.Dispatcher.BeginInvoke(new Action(() => { try { box.Focus(); Keyboard.Focus(box); } catch (Exception) { } }), DispatcherPriority.Input);
  }
  string AddLabel => playing ? "Add and start playing" : "Add game";

  void Typed(object sender, TextChangedEventArgs e) {
    try { Choose(null); debounce.Stop(); debounce.Start(); } catch (Exception ex) { Log.Error(ex, "Blastlog add a game: typing"); }
  }
  void FieldKeys(object sender, KeyEventArgs e) {
    try {
      if (e.Key == Key.Down) {
        var first = catalogPanel.Children.OfType<FrameworkElement>().Concat(storedPanel.Children.OfType<FrameworkElement>()).SelectMany(Buttons).FirstOrDefault();
        if (first != null) { first.Focus(); e.Handled = true; }
      } else if (e.Key == Key.Enter) { debounce.Stop(); Dash.Safe(Ask, "add a game: search"); e.Handled = true; }
    } catch (Exception ex) { Log.Error(ex, "Blastlog add a game: field keys"); }
  }
  static IEnumerable<Button> Buttons(FrameworkElement e) => e is Button b ? new[] { b } : e is Panel p ? p.Children.OfType<FrameworkElement>().SelectMany(Buttons) : Enumerable.Empty<Button>();

  void SetOwnership(string value) {
    ownership = value;
    AddGameParts.SetPill(inLibrary, value == "library"); AddGameParts.SetPill(wishlist, value == "wishlist");
    if (storesBlock != null) storesBlock.Visibility = value == "library" ? Visibility.Visible : Visibility.Collapsed;
  }

  void Choose(Choice choice) {
    selected = choice;
    foreach (var r in rows) AddGameParts.Mark(r.Value, choice != null && r.Key == choice.Key);
    if (addButton != null) addButton.IsEnabled = choice != null;
    error.Visibility = Visibility.Collapsed;
  }

  /// <summary>Searches as the site does: games Blastlog has, and IGDB (when it's on), with "Show everything" for DLC and editions.</summary>
  async Task Ask() {
    var words = box.Text.Trim(); var id = ++askId;
    rows.Clear(); catalogPanel.Children.Clear(); storedPanel.Children.Clear();
    if (words.Length < 2) return;
    var q = Uri.EscapeDataString(words.Length > 100 ? words.Substring(0, 100) : words);
    var catalogRead = igdbOn ? plugin.Mirror($"catalog/search?q={q}{(everything ? "&all=1" : "")}", fresh: false) : null;
    var storedRead = plugin.Mirror($"games?limit=50&offset=0&search={q}", fresh: false);
    var mineRead = plugin.Mirror($"search?q={q}", fresh: false);
    if (catalogRead != null) catalogPanel.Children.Add(Dash.Text("Searching IGDB…", "Blastlog.Muted").Margin(0, 12, 0, 0));

    // Games already in Blastlog first (they come back quicker), less any already in your library.
    var stored = (await storedRead).Value; var mine = (await mineRead).Value;
    if (id != askId) return;
    var owned = new HashSet<string>(Dash.Arr(mine?["mine"]).Select(m => Dash.Str(m, "gameId")).Where(x => x != null));
    var catalog = catalogRead != null ? (await catalogRead).Value : null;
    if (id != askId) return;
    foreach (var item in Dash.Arr(catalog?["items"]).OfType<JObject>().Where(i => Dash.Arr(i["libraryEntryIds"]).Count > 0))
      if (Dash.Str(item, "canonicalGameId") is string g) owned.Add(g);
    RenderStored(Dash.Arr(stored?["items"]).OfType<JObject>().Where(g => Dash.Str(g, "id") != null && !owned.Contains(Dash.Str(g, "id"))).Take(8).ToList());
    if (catalogRead != null) RenderCatalog(catalog, words);
  }

  void RenderCatalog(JObject catalog, string words) {
    catalogPanel.Children.Clear();
    if (catalog == null) {
      catalogPanel.Children.Add(Dash.Text("IGDB search is temporarily unavailable. Stored results are still shown.", "Blastlog.Small", "Blastlog.Danger").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 12, 0, 0); }));
      return;
    }
    var items = Dash.Arr(catalog["items"]).OfType<JObject>().ToList();
    catalogPanel.Children.Add(Label("Results from IGDB"));
    var list = new StackPanel();
    catalogPanel.Children.Add(list);
    foreach (var item in items) {
      var entryIds = Dash.Arr(item["libraryEntryIds"]).Select(x => (string)x).Where(x => x != null).ToList();
      var choice = new Choice { ExternalId = (string)item["externalId"], Title = (string)item["title"] };
      var row = AddGameParts.Row(plugin, list, AddGameParts.CatalogCover(item), choice.Title, AddGameParts.CatalogLine(item),
        entryIds.Count > 0 ? "Already in library · View entry" : null, () => { if (entryIds.Count > 0) OpenOwned(entryIds[0]); else Choose(choice); }, out var frame);
      rows.Add(new KeyValuePair<string, Border>(choice.Key, frame)); list.Children.Add(row);
    }
    if (items.Count == 0) catalogPanel.Children.Add(Dash.Text("No IGDB matches.", "Blastlog.Muted").Margin(0, 4, 0, 0));
    var more = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
    more.Children.Add(Dash.Text(everything ? "Showing everything IGDB has, including DLC, packs and editions." : "Not seeing it?", "Blastlog.Muted").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 4, 0); }));
    more.Children.Add(Dash.Link(everything ? "Back to games only" : "Show everything", () => { everything = !everything; Dash.Safe(Ask, "add a game: show everything"); }).Also(b => b.MinHeight = 44));
    if (!everything) more.Children.Add(Dash.Text("(DLC, packs, editions)", "Blastlog.Muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    catalogPanel.Children.Add(more);
  }

  void RenderStored(List<JObject> games) {
    storedPanel.Children.Clear();
    if (games.Count == 0) return;
    storedPanel.Children.Add(Label("Already in Blastlog"));
    var list = new StackPanel();
    storedPanel.Children.Add(list);
    foreach (var game in games) {
      var choice = new Choice { GameId = (string)game["id"], Title = (string)game["title"] };
      var year = ((string)game["releaseDate"])?.Length >= 4 ? ((string)game["releaseDate"]).Substring(0, 4) : null;
      var row = AddGameParts.Row(plugin, list, Dash.Game(game), choice.Title, year, null, () => Choose(choice), out var frame);
      rows.Add(new KeyValuePair<string, Border>(choice.Key, frame)); list.Children.Add(row);
    }
    if (selected != null) Choose(rows.Any(r => r.Key == selected.Key) ? selected : null);
  }

  static TextBlock Label(string text) => Dash.Text(text, "Blastlog.Small", "Blastlog.TextMuted", FontWeights.Bold).Margin(0, 18, 0, 8);

  /// <summary>A game already in your library opens its page instead (web chooseCatalog): Playnite's when paired, else Blastlog's.</summary>
  void OpenOwned(string entryId) { window.Close(); Dash.OpenEntry(plugin, entryId); }

  // ---- Adding ----
  async Task Add() {
    var choice = selected; if (choice == null) return;
    addButton.IsEnabled = false; addButton.Content = "Adding…"; error.Visibility = Visibility.Collapsed;
    var finished = false;
    try {
      string gameId = choice.GameId, title = choice.Title;
      if (choice.ExternalId != null) {
        var game = await plugin.Write("catalog/games/resolve", new JObject { ["provider"] = "igdb", ["externalId"] = choice.ExternalId });
        if (game == null) return;
        gameId = (string)game["id"]; title = (string)game["title"] ?? title;
      }
      if (gameId == null) { Fail("Blastlog didn’t send that game back. Try again."); return; }
      // The site hands back the existing entry when the game is already in the library; knowing which entries were there
      // first keeps Undo from removing one this window didn't make.
      var before = await MyEntryIds(title);
      var body = new JObject { ["gameId"] = gameId, ["ownership"] = ownership };
      if (playing) body["status"] = "playing";
      var entry = await plugin.Write("library", body);
      if (entry == null) return;
      var entryId = (string)entry["id"];
      var isNew = entryId != null && !before.Contains(entryId) && (int?)entry["version"] == 1;
      // The game is added either way; the stores are a nicety that can be ticked on its page later (the site does the same).
      if (isNew && ownership == "library" && stores.Count > 0)
        await plugin.WriteQuietly($"entries/{entryId}/stores", new JObject { ["stores"] = new JArray(AddGameParts.Stores.Select(s => s.Key).Where(stores.Contains)) }, HttpMethod.Put);
      finished = true;
      Done();
      ShowAdded(entry, title, isNew);
    } finally {
      if (!finished && addButton != null) { addButton.Content = AddLabel; addButton.IsEnabled = selected != null; }
    }
  }
  void Fail(string text) { error.Text = text; error.Visibility = Visibility.Visible; }
  void Done() { try { done?.Invoke(); } catch (Exception e) { Log.Error(e, "Blastlog add a game: refresh"); } }

  /// <summary>The player's entries whose titles match (the dashboard search's "mine"), read fresh.</summary>
  async Task<HashSet<string>> MyEntryIds(string title) {
    var words = (title ?? "").Trim();
    if (words.Length < 2) return new HashSet<string>();
    var mine = (await plugin.Mirror("search?q=" + Uri.EscapeDataString(words.Length > 100 ? words.Substring(0, 100) : words), fresh: true)).Value;
    return new HashSet<string>(Dash.Arr(mine?["mine"]).Select(m => Dash.Str(m, "entryId")).Where(x => x != null));
  }

  // ---- After adding ----
  void ShowAdded(JObject entry, string title, bool isNew) {
    var entryId = (string)entry["id"]; var game = Dash.Game(entry["game"]);
    title = (string)entry["game"]?["title"] ?? title;
    var body = new StackPanel { Margin = new Thickness(28, 26, 28, 20) };
    var cover = Kit.Cover(plugin, game, 96, ArtworkContext.Cover, 12); cover.Margin = new Thickness(0, 0, 18, 0); cover.VerticalAlignment = VerticalAlignment.Top;
    var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
    words.Children.Add(Dash.Text(title, "Blastlog.CardTitle").Also(t => t.TextWrapping = TextWrapping.Wrap));
    var said = !isNew ? "It was already in your library, so nothing was added."
      : (string)entry["ownership"] == "wishlist" ? "Added to your wishlist."
      : (string)entry["status"] == "playing" ? "Added to your library as playing." : "Added to your library.";
    words.Children.Add(Dash.Text(said, "Blastlog.Body").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 6, 0, 0); }));
    var head = new DockPanel { Children = { cover, words } }; DockPanel.SetDock(cover, Dock.Left);
    body.Children.Add(head);

    // Pairing makes its status and stars sync and gives Blastlog's screens Play and Install for it.
    var paired = entryId != null ? plugin.GameFor(entryId) : null;
    if (paired != null) body.Children.Add(Dash.Text($"It’s paired with {AddGameParts.Where(paired)} in Playnite.", "Blastlog.Muted").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 18, 0, 0); }));
    else {
      var inPlaynite = AddGameParts.UnpairedInPlaynite(plugin, title);
      if (inPlaynite.Count > 0) {
        var card = new StackPanel();
        card.Children.Add(Dash.Text("It’s in Playnite too", "Blastlog.Body", "Blastlog.Text", FontWeights.SemiBold));
        var names = string.Join(", ", inPlaynite.Take(3).Select(AddGameParts.Where)) + (inPlaynite.Count > 3 ? $" and {inPlaynite.Count - 3} more" : "");
        card.Children.Add(Dash.Text($"Pair it with {names} in Playnite so its status and stars sync both ways and Blastlog’s screens can show Play.", "Blastlog.Muted").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 4, 0, 12); }));
        card.Children.Add(Kit.Button("Pair games…", async () => { window.Close(); await plugin.PairGames(); Done(); }).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0); }));
        body.Children.Add(Dash.Card(card, 18).Margin(0, 20, 0, 0));
      }
    }

    var actions = new WrapPanel { Margin = new Thickness(0, 22, 0, 0) };
    if (entryId != null) actions.Children.Add(Kit.Button("Open its page", () => { window.Close(); Dash.OpenEntry(plugin, entryId); }, "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 10, 10)));
    if (isNew && entryId != null) actions.Children.Add(Kit.Button("Undo", () => Undo(entryId, (int?)entry["version"] ?? 1, title), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 10, 10)));
    actions.Children.Add(Kit.Button("Add another", () => ShowSearch(), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 10, 10)));
    actions.Children.Add(Kit.Button("Done", () => window.Close()).Also(b => b.Margin = new Thickness(0, 0, 0, 10)));
    body.Children.Add(actions);

    root.Children.Clear();
    root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
  }

  /// <summary>Takes the add back: the site's restorable remove, at the entry's current version.</summary>
  async Task Undo(string entryId, int version, string title) {
    var fresh = (await plugin.Mirror("entries/" + entryId + "?like=0", fresh: true)).Value?["entry"];
    var current = (int?)fresh?["version"] ?? version;
    if (await plugin.Write("entries/" + entryId, new JObject { ["expectedVersion"] = current }, HttpMethod.Delete) == null) return;
    Done();
    var body = new StackPanel { Margin = new Thickness(28, 26, 28, 20) };
    body.Children.Add(Dash.Text($"Took {title} back out of your library.", "Blastlog.Body").Also(t => t.TextWrapping = TextWrapping.Wrap));
    var actions = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) };
    actions.Children.Add(Kit.Button("Add another", () => ShowSearch(), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 10, 10)));
    actions.Children.Add(Kit.Button("Done", () => window.Close()).Also(b => b.Margin = new Thickness(0, 0, 0, 10)));
    body.Children.Add(actions);
    root.Children.Clear(); root.Children.Add(body);
  }
}
}
