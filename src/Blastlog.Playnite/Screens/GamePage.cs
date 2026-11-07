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
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Controls;
using BlastlogGame = Blastlog.Sync.Core.Game;
using Game = Playnite.SDK.Models.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Blastlog's game page inside Playnite's game details (docs/PLAYNITE_DESIGN.md 4.3; design.md 8): theme custom elements
/// the Blastlog theme places in DetailsViewGameOverview and GridViewGameOverview.
/// <list type="bullet">
/// <item><c>Blastlog_GameActions</c>: status and stars in the hero's action row beside Play / Install, editable, with Undo.</item>
/// <item><c>Blastlog_GamePage</c>: the fingerprint, For you, your take, game details, friends, where you own it, the
/// collection, More like this and history.</item>
/// <item>Older names kept for other themes: <c>Blastlog_GamePanel</c> (= GamePage), <c>Blastlog_Fingerprint</c> (the card
/// alone), <c>Blastlog_Status</c> (status and stars, read only).</item>
/// </list>
/// A game that isn't paired shows nothing in the action row and a pairing prompt on the page.
/// </summary>
public static class GamePage {
  public static readonly string[] Elements = { "GameActions", "GamePage", "GamePanel", "Fingerprint", "Status" };
  public static Control Create(BlastlogPlugin plugin, string name) => Elements.Contains(name) ? new GamePageElement(plugin, name == "GamePanel" ? "GamePage" : name) : null;

  internal static readonly string[] Statuses = { "not_started", "started", "playing", "done", "dnf", "quit", "unplayable" };
  internal static readonly string[] Finished = { "done", "dnf", "quit" };
  internal static string StatusWord(string s) => s == "not_started" ? "Not started" : s == "dnf" ? "Didn't finish" : s == "done" ? "Done" : s == "quit" ? "Quit" : s == "started" ? "Started"
    : s == "playing" ? "Playing" : s == "unplayable" ? "Unplayable" : s == "set_aside" ? "Set aside" : s == "wishlist" ? "Wishlist" : (s ?? "").Replace('_', ' ');
  internal static string LevelWord(string level) => level == "main_story" ? "Main story" : level == "completionist" ? "Completionist" : "Main + extras";
  internal static string Today() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>One game page element; Playnite gives it the selected game.</summary>
public sealed class GamePageElement : PluginUserControl {
  static readonly ILogger Log = LogManager.GetLogger();
  readonly BlastlogPlugin plugin; readonly string kind; readonly ContentControl host = new ContentControl();
  int generation; string entryId; JObject page; JObject fit; JArray friends; JArray history;
  // The last change made here, for its Undo (one at a time, as the site's toasts).
  string undoRun, undoLabel; bool busy;

  public GamePageElement(BlastlogPlugin plugin, string kind) {
    this.plugin = plugin; this.kind = kind;
    Content = host; Visibility = Visibility.Collapsed;
    Resources.MergedDictionaries.Add(Kit.Styles);
    plugin.EntryChanged += OnEntryChanged;
    Unloaded += (s, e) => plugin.EntryChanged -= OnEntryChanged;
    Loaded += (s, e) => { plugin.EntryChanged -= OnEntryChanged; plugin.EntryChanged += OnEntryChanged; };
  }

  void OnEntryChanged(string id) { try { if (id != null && id == entryId) _ = Load(GameContext, fresh: true); } catch (Exception e) { Log.Error(e, "Blastlog game page refresh"); } }

  public override void GameContextChanged(Game oldContext, Game newContext) {
    try { if (oldContext?.Id != newContext?.Id) { undoRun = null; undoLabel = null; } _ = Load(newContext, fresh: false); }
    catch (Exception e) { Log.Error(e, "Blastlog game page"); }
  }

  async Task Load(Game game, bool fresh) {
    var mine = ++generation;
    try {
      entryId = game == null ? null : plugin.EntryFor(game.Id);
      if (game == null) { Show(null); return; }
      if (entryId == null) { page = null; Show(kind == "GamePage" ? NotPaired() : null); return; }
      // Hold the space while a game that isn't cached loads, so the sections under it don't jump when it arrives.
      if ((string)page?["entry"]?["id"] != entryId) Show(Placeholder());
      // The cached copy straight away, then the rest as it arrives. More like this is slow, so it comes separately.
      var read = await plugin.Mirror("entries/" + entryId + "?like=0", fresh);
      if (mine != generation) return;
      page = read.Value;
      if (page == null) { Show(kind == "GamePage" ? Unavailable() : kind == "GameActions" || kind == "Status" ? Fallback() : null); return; }
      Render();
      if (kind != "GamePage") return;
      var gameId = (string)page["entry"]?["game"]?["id"];
      var likeRead = plugin.Mirror($"games/{gameId}/like", fresh);
      var reads = new[] { plugin.Mirror($"games/{gameId}/fit", fresh), plugin.Mirror($"games/{gameId}/friends", fresh), plugin.Mirror($"entries/{entryId}/history", fresh) };
      await Task.WhenAll(reads);
      if (mine != generation) return;
      fit = reads[0].Result.Value; friends = reads[1].Result.Value?["items"] as JArray; history = reads[2].Result.Value?["items"] as JArray;
      Render();
      var like = (await likeRead).Value;
      if (mine != generation || like == null || page == null) return;
      page["like"] = like;
      Render();
    } catch (Exception e) { Log.Error(e, "Blastlog game page load"); }
  }

  void Show(FrameworkElement content) { host.Content = content; Visibility = content == null ? Visibility.Collapsed : Visibility.Visible; }

  /// <summary>An empty block the size of what will come (roughly), so Playnite's own sections don't move when it lands.</summary>
  FrameworkElement Placeholder() {
    var height = kind == "GamePage" ? 900 : kind == "Fingerprint" ? 360 : kind == "GameActions" || kind == "Status" ? 56 : 0;
    if (height == 0) return null;
    var block = new Border { MinHeight = height };
    if (kind == "GamePage") block.Child = Kit.Text("Loading its Blastlog page…", "Blastlog.Muted").Also(t => { t.VerticalAlignment = VerticalAlignment.Top; t.Margin = new Thickness(0, 8, 0, 0); });
    return block;
  }

  void Render() {
    try {
      switch (kind) {
        case "GameActions": Show(Actions(editable: true)); break;
        case "Status": Show(Actions(editable: false)); break;
        case "Fingerprint": Show(Fingerprint.Card(FingerprintData.Parse(page["fingerprint"]), Options())); break;
        default: Show(Page()); break;
      }
    } catch (Exception e) { Log.Error(e, "Blastlog game page render"); }
  }

  FingerprintOptions Options() => new FingerprintOptions { ServerUrl = plugin.ServerUrl, SiteGameUrl = plugin.ServerUrl + "/library/" + entryId };
  JObject Entry => page?["entry"] as JObject;

  // ---- The action row: status and stars, editable in place ----
  FrameworkElement Actions(bool editable) {
    var entry = Entry; var status = (string)entry?["status"]; var rating = (int?)(double?)entry?["rating"];
    var wishlist = (string)entry?["ownership"] == "wishlist";
    var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
    if (!editable) {
      row.Children.Add(Kit.StatusPill(wishlist && status == "not_started" ? "wishlist" : status).Also(p => p.Margin = new Thickness(0, 0, 12, 0)));
      row.Children.Add(Kit.Stars(rating).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
      return row;
    }
    var statusButton = new Button { Style = Kit.Style("Blastlog.Ghost"), Padding = new Thickness(6, 0, 14, 0), ToolTip = "Change its status on Blastlog" };
    var inner = new StackPanel { Orientation = Orientation.Horizontal };
    var pill = Kit.StatusPill(wishlist && status == "not_started" ? "wishlist" : status); pill.BorderThickness = new Thickness(0); pill.Background = Brushes.Transparent; inner.Children.Add(pill);
    var caret = Icons.Make("<path d=\"m6 9 6 6 6-6\" />", 14); caret.Margin = new Thickness(2, 0, 0, 0); caret.VerticalAlignment = VerticalAlignment.Center; inner.Children.Add(caret);
    statusButton.Content = inner;
    AutomationProperties.SetName(statusButton, "Status: " + GamePage.StatusWord(status) + ". Change it");
    var menu = new ContextMenu();
    foreach (var s in GamePage.Statuses) {
      var choice = s;
      var item = new MenuItem { Header = GamePage.StatusWord(s), IsCheckable = true, IsChecked = s == status };
      item.Click += async (o, e) => { try { if (choice != status) await Change(new ChangeItem { Status = choice, FinishedOn = GamePage.Finished.Contains(choice) && (string)entry?["finishedOn"] == null ? GamePage.Today() : null }, "Status set to " + GamePage.StatusWord(choice)); } catch (Exception ex) { Log.Error(ex, "Blastlog status"); } };
      menu.Items.Add(item);
    }
    statusButton.ContextMenu = menu;
    statusButton.Click += (o, e) => { try { menu.PlacementTarget = statusButton; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; } catch (Exception ex) { Log.Error(ex, "Blastlog status menu"); } };
    statusButton.IsEnabled = !busy;
    row.Children.Add(statusButton);

    var canRate = GamePage.Finished.Contains(status);
    var stars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
    for (var i = 1; i <= 5; i++) {
      var value = i;
      var star = new Button { Content = "★", FontSize = 24, Padding = new Thickness(0), MinWidth = 36, MinHeight = 44, Margin = new Thickness(0), Style = Kit.Style("Blastlog.TextButton"), IsEnabled = canRate && !busy };
      star.SetResourceReference(Control.ForegroundProperty, rating != null && i <= rating ? "Blastlog.Star" : "Blastlog.Edge");
      AutomationProperties.SetName(star, rating == value ? $"{value} star{(value == 1 ? "" : "s")}, your rating. Press to clear it" : $"Rate it {value} star{(value == 1 ? "" : "s")}");
      star.Click += async (o, e) => { try { var next = rating == value ? (int?)null : value; await Change(new ChangeItem { SetRating = true, RatingValue = next }, next == null ? "Stars cleared" : $"Rated {next}★"); } catch (Exception ex) { Log.Error(ex, "Blastlog stars"); } };
      stars.Children.Add(star);
    }
    var starsHost = new Border { Child = stars, Background = Brushes.Transparent, ToolTip = canRate ? null : "Stars go on once you've finished it, quit, or didn't finish" };
    ToolTipService.SetShowOnDisabled(starsHost, true);
    row.Children.Add(starsHost);
    if (undoRun != null) {
      row.Children.Add(Kit.Text(undoLabel ?? "Saved", "Blastlog.Small").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(4, 0, 0, 0); }));
      var run = undoRun;
      row.Children.Add(Fingerprint.LinkButton("Undo", async () => { await Undo(run); }, game: true).Also(b => b.VerticalAlignment = VerticalAlignment.Center));
    }
    return row;
  }

  async Task Change(ChangeItem change, string label) {
    if (busy || entryId == null) return;
    busy = true; Render();
    try {
      change.EntryId = entryId; change.ExpectedVersion = (int?)Entry?["version"] ?? 1;
      var outcome = await plugin.PanelChange(change);
      if (outcome == null) return;
      if (outcome.Applied) { undoRun = outcome.RunId; undoLabel = label; }
      else if (outcome.Result == "conflict") plugin.Api.Dialogs.ShowMessage("It changed on Blastlog since this page loaded. The page shows the latest now; try again.", "Blastlog");
      else if (outcome.Result == "rejected") plugin.Api.Dialogs.ShowMessage(outcome.Reason == "rate_when_done" ? "Stars and reviews go on once you've finished it, quit, or didn't finish. Set its status first." : $"Blastlog didn't take that ({outcome.Reason}).", "Blastlog");
    } finally { busy = false; await Load(GameContext, fresh: true); }
  }
  async Task Undo(string run) {
    if (busy) return;
    busy = true; Render();
    try { if (await plugin.UndoPanel(run, entryId)) { undoRun = null; undoLabel = null; } }
    finally { busy = false; await Load(GameContext, fresh: true); }
  }

  // ---- The page under the hero ----
  FrameworkElement Page() {
    var entry = Entry; var title = (string)entry?["game"]?["title"] ?? "this game";
    var stack = new StackPanel();
    stack.Children.Add(Fingerprint.Card(FingerprintData.Parse(page["fingerprint"]), Options()));
    var forYou = ForYou(); if (forYou != null) stack.Children.Add(forYou);
    stack.Children.Add(TwoUp(Take(entry), Facts(entry)));
    stack.Children.Add(Friends());
    var own = OwnedAndCollection(entry); if (own != null) stack.Children.Add(own);
    var like = MoreLikeThis(title); if (like != null) stack.Children.Add(like);
    stack.Children.Add(History());
    stack.Children.Add(Fingerprint.LinkButton("Open on Blastlog ↗", () => UiKit.Open(plugin.ServerUrl + "/library/" + entryId), game: true).Also(b => b.Margin = new Thickness(-10, 0, 0, 18)));
    return stack;
  }

  /// <summary>Two cards side by side on a wide page, stacked on a narrow one (Playnite's grid view side panel).</summary>
  static FrameworkElement TwoUp(FrameworkElement a, FrameworkElement b) {
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.Children.Add(a); grid.Children.Add(b);
    void Lay(bool wide) {
      Grid.SetColumn(a, 0); Grid.SetRow(a, 0); Grid.SetColumnSpan(a, wide ? 1 : 3);
      Grid.SetColumn(b, wide ? 2 : 0); Grid.SetRow(b, wide ? 0 : 1); Grid.SetColumnSpan(b, wide ? 1 : 3);
    }
    Lay(true);
    grid.SizeChanged += (s, e) => { try { Lay(e.NewSize.Width >= 760); } catch (Exception) { } };
    return grid;
  }

  static Border Card(string heading, out StackPanel body, bool game = false) {
    body = new StackPanel();
    if (heading != null) body.Children.Add(Kit.Text(heading, "Blastlog.CardTitle").Also(t => { t.FontSize = 20; t.FontWeight = FontWeights.ExtraBold; t.Margin = new Thickness(0, 0, 0, 12); }));
    return Kit.Card(body, game).Also(c => c.VerticalAlignment = VerticalAlignment.Stretch);
  }
  static TextBlock Eyebrow(string text) => Kit.Text(text, "Blastlog.Small").Also(t => { t.FontWeight = FontWeights.Bold; t.Margin = new Thickness(0, 0, 0, 6); });

  /// <summary>"For you" (design.md 8): predicted stars with a plain verdict, Because you like, Watch out. Hidden once rated or before the game is read.</summary>
  FrameworkElement ForYou() {
    if (fit == null || fit["rated"]?.Type != JTokenType.Null && fit["rated"] != null || (bool?)fit["read"] != true) return null;
    var card = Card("For you", out var body, game: true);
    if ((bool?)fit["ready"] != true) { body.Children.Add(Kit.Text("Rate a few more games you’ve finished and Blastlog can say how this one fits your taste.", "Blastlog.Muted")); return card; }
    var predicted = (double?)fit["predictedRating"];
    var reasons = (fit["reasons"] as JArray ?? new JArray()).Select(r => (string)r).Where(r => !string.IsNullOrEmpty(r)).ToList();
    var watch = (fit["watchOuts"] as JArray ?? new JArray()).Select(r => (string)r).Where(r => !string.IsNullOrEmpty(r)).ToList();
    if (predicted == null && reasons.Count == 0 && watch.Count == 0) return null;
    if (predicted != null) {
      var line = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
      line.Children.Add(Kit.Stars(predicted, 22).Also(t => t.Text = "★ " + predicted.Value.ToString("0.0", CultureInfo.InvariantCulture)));
      var verdict = predicted >= 4.2 ? "Looks like one you’d love" : predicted >= 3.6 ? "Likely a good fit for you" : predicted >= 3.0 ? "Could go either way for you" : "Probably not your kind of game";
      line.Children.Add(Kit.Text("predicted · " + verdict, "Blastlog.Muted").Also(t => { t.Margin = new Thickness(10, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center; }));
      body.Children.Add(line);
    }
    if (reasons.Count > 0) { body.Children.Add(Eyebrow("Because you like")); var w = new WrapPanel(); foreach (var r in reasons) w.Children.Add(Kit.Chip(r)); body.Children.Add(w); }
    if (watch.Count > 0) {
      body.Children.Add(Eyebrow("Watch out").Also(t => t.Margin = new Thickness(0, 8, 0, 6)));
      var w = new WrapPanel();
      foreach (var r in watch) {
        var chip = Kit.Chip(r, neutral: true);
        // Risk chips are dashed (design.md 7), in the warning colour.
        chip.BorderThickness = new Thickness(0);
        var dashed = new System.Windows.Shapes.Rectangle { RadiusX = 14, RadiusY = 14, StrokeDashArray = new DoubleCollection { 4, 3 }, StrokeThickness = 1, IsHitTestVisible = false };
        dashed.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Blastlog.Warn");
        var g = new Grid { Margin = chip.Margin }; chip.Margin = new Thickness(0); g.Children.Add(chip); g.Children.Add(dashed); w.Children.Add(g.Also(x => x.Margin = new Thickness(0, 0, 8, 8)));
      }
      body.Children.Add(w);
    }
    if ((bool?)fit["early"] == true) body.Children.Add(Kit.Text((bool?)fit["upcoming"] == true ? "It isn’t out yet, so this comes from previews and counts for a little less." : "Fingerprinting in progress, confirming data after release.", "Blastlog.Small"));
    else if (predicted == null && (bool?)fit["earlyRead"] == true) body.Children.Add(Kit.Text("From Blastlog’s first, lighter look at this game, so no predicted stars yet.", "Blastlog.Small"));
    else if (predicted == null) body.Children.Add(Kit.Text("Predicted stars show once Blastlog knows your taste a little better (Sharpen your picks on Play next).", "Blastlog.Small"));
    return card;
  }

  /// <summary>Your take: stars and your review, the review editable in place.</summary>
  FrameworkElement Take(JObject entry) {
    var card = Card(null, out var body);
    body.Children.Add(Eyebrow("Your take"));
    var rating = (int?)(double?)entry?["rating"]; var review = ((string)entry?["review"])?.Trim();
    var stars = Kit.Text(rating == null ? "Not rated yet" : new string('★', rating.Value) + new string('☆', 5 - rating.Value));
    if (rating != null) { stars.FontSize = 26; stars.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Star"); AutomationProperties.SetName(stars, $"{rating} of 5 stars"); }
    else stars.Style = Kit.Style("Blastlog.Muted");
    body.Children.Add(stars);
    var canReview = GamePage.Finished.Contains((string)entry?["status"]);
    var reviewHost = new ContentControl { Margin = new Thickness(0, 10, 0, 0) };
    void ShowReview() {
      var s = new StackPanel();
      if (!string.IsNullOrEmpty(review)) s.Children.Add(Kit.Text("“" + review + "”").Also(t => { t.FontStyle = FontStyles.Italic; t.LineHeight = 25; }));
      else s.Children.Add(Kit.Text("No written review yet.", "Blastlog.Muted"));
      if (!canReview) s.Children.Add(Kit.Text("You can review it once you’ve finished it, quit, or didn’t finish.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
      // Rate & review: the full form (status, stars, taps, the review questions), which can also mark it finished first.
      var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
      actions.Children.Add(Kit.Button("Rate & review", () => Nav.Review(plugin, entryId), "Blastlog.Primary").Also(b => { b.Margin = new Thickness(0, 0, 10, 8); b.IsEnabled = !busy; }));
      if (canReview) actions.Children.Add(Kit.Button(string.IsNullOrEmpty(review) ? "Add review" : "Edit review", () => Edit(), "Blastlog.Ghost").Also(b => { b.Margin = new Thickness(0, 0, 10, 8); b.IsEnabled = !busy; }));
      s.Children.Add(actions);
      reviewHost.Content = s;
    }
    void Edit() {
      var s = new StackPanel();
      var box = new TextBox { Text = review ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, MaxLength = 20000, Padding = new Thickness(10), FontSize = 16, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
      box.SetResourceReference(Control.FontFamilyProperty, "Blastlog.Font"); box.SetResourceReference(Control.BackgroundProperty, "Blastlog.Surface2");
      box.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text"); box.SetResourceReference(Control.BorderBrushProperty, "Blastlog.Edge"); box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Blastlog.Text");
      AutomationProperties.SetName(box, "Your review");
      s.Children.Add(box);
      var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
      buttons.Children.Add(Kit.Button("Save", async () => { await Change(new ChangeItem { Review = box.Text.Trim() }, string.IsNullOrWhiteSpace(box.Text) ? "Review removed" : "Review saved"); }));
      buttons.Children.Add(Kit.Button("Cancel", () => ShowReview(), "Blastlog.TextButton"));
      s.Children.Add(buttons);
      reviewHost.Content = s;
      box.Loaded += (o, e) => { try { box.Focus(); box.CaretIndex = box.Text.Length; } catch (Exception) { } };
    }
    ShowReview();
    body.Children.Add(reviewHost);
    if (undoRun != null && undoLabel != null && undoLabel.StartsWith("Review", StringComparison.Ordinal)) {
      var run = undoRun;
      body.Children.Add(Fingerprint.LinkButton("Undo: " + undoLabel.ToLowerInvariant(), async () => await Undo(run), game: true).Also(b => b.Margin = new Thickness(-10, 6, 0, 0)));
    }
    return card;
  }

  /// <summary>Game details: status, ownership, release platforms, length at your level, when you finished it.</summary>
  FrameworkElement Facts(JObject entry) {
    var card = Card("Game details", out var body);
    var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    void Row(string label, UIElement value) {
      grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); var r = grid.RowDefinitions.Count - 1;
      var l = Kit.Text(label, "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 6, 18, 6)); Grid.SetRow(l, r); grid.Children.Add(l);
      if (value is FrameworkElement f) { f.Margin = new Thickness(0, 6, 0, 6); f.VerticalAlignment = VerticalAlignment.Center; }
      Grid.SetRow(value, r); Grid.SetColumn(value, 1); grid.Children.Add(value);
    }
    var status = (string)entry?["status"]; var wishlist = (string)entry?["ownership"] == "wishlist";
    Row("Status", Kit.StatusPill(wishlist && status == "not_started" ? "wishlist" : status));
    Row("Ownership", Kit.Text(wishlist ? "Wishlist" : (string)entry?["ownership"] == "library" ? "In library" : "Unknown"));
    var platforms = (page["info"]?["platforms"] as JArray ?? entry?["game"]?["releasePlatforms"] as JArray ?? new JArray()).Select(p => (string)p).Where(p => !string.IsNullOrEmpty(p)).ToList();
    Row("Release platforms", Kit.Text(platforms.Count > 0 ? string.Join(", ", platforms) : "Not available"));
    var length = entry?["length"] as JObject;
    var level = (string)length?["level"];
    Row(length != null && (bool?)length["noEnd"] != true ? $"Length ({GamePage.LevelWord(level)})" : "Length", Kit.Text(LengthWords(length)));
    var finished = (string)entry?["finishedOn"];
    if (!string.IsNullOrEmpty(finished)) Row("Completed", Kit.Text(DateTime.TryParse(finished, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) : finished));
    body.Children.Add(grid);
    return card;
  }
  static string LengthWords(JObject length) {
    if (length == null) return "Length unknown";
    if ((bool?)length["noEnd"] == true) return "No end: it keeps going";
    var hours = (double?)length["hours"]; if (hours == null) return "Length unknown";
    var h = hours.Value < 10 ? Math.Round(hours.Value * 2, MidpointRounding.AwayFromZero) / 2 : Math.Round(hours.Value, MidpointRounding.AwayFromZero);
    var words = $"About {h.ToString("0.#", CultureInfo.InvariantCulture)} hours";
    if ((string)length["fromLevel"] != (string)length["level"]) words += $" ({GamePage.LevelWord((string)length["fromLevel"])})";
    if ((bool?)length["estimate"] == true) words += ", an estimate";
    return words;
  }

  /// <summary>Friends on this game: one row per friend with stars; a review gets a two-line preview that opens in full.</summary>
  FrameworkElement Friends() {
    var card = Card("Friends on this game", out var body);
    if (friends == null) { body.Children.Add(Kit.Text("Checking your friends…", "Blastlog.Muted")); return card; }
    if (friends.Count == 0) { body.Children.Add(Kit.Text("None of your friends has rated it yet.", "Blastlog.Muted")); return card; }
    foreach (var f in friends.OfType<JObject>()) {
      var name = (string)f["name"] ?? "A friend"; var rating = (double?)f["rating"]; var review = ((string)f["review"])?.Trim();
      var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
      var face = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
      face.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
      face.Child = Kit.Text(name.Substring(0, 1).ToUpperInvariant()).Also(t => { t.FontWeight = FontWeights.ExtraBold; t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
      DockPanel.SetDock(face, Dock.Left); row.Children.Add(face);
      var stars = Kit.Stars(rating); DockPanel.SetDock(stars, Dock.Right); stars.VerticalAlignment = VerticalAlignment.Top; stars.Margin = new Thickness(12, 9, 0, 0); row.Children.Add(stars);
      var text = new StackPanel();
      text.Children.Add(Kit.Text(name).Also(t => { t.FontWeight = FontWeights.Bold; t.Margin = new Thickness(0, 8, 0, 0); }));
      var line = (string)f["line"]; if (!string.IsNullOrEmpty(line)) text.Children.Add(Kit.Text(line, "Blastlog.Small"));
      if (!string.IsNullOrEmpty(review)) {
        var quote = Kit.Text(review).Also(t => { t.FontStyle = FontStyles.Italic; t.MaxHeight = 50; t.TextTrimming = TextTrimming.WordEllipsis; t.Margin = new Thickness(0, 6, 0, 0); t.LineHeight = 24; t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; });
        text.Children.Add(quote);
        if (review.Length > 120) {
          Button toggle = null;
          toggle = Fingerprint.LinkButton("Read the full review ▾", () => { var open = quote.MaxHeight < 1e5; quote.MaxHeight = open ? double.PositiveInfinity : 50; toggle.Content = open ? "Show less ▴" : "Read the full review ▾"; });
          toggle.Margin = new Thickness(-10, 0, 0, 0); AutomationProperties.SetName(toggle, $"Read {name}’s full review");
          text.Children.Add(toggle);
        }
      }
      row.Children.Add(text);
      body.Children.Add(row);
    }
    return card;
  }

  static readonly Dictionary<string, string[]> Stores = new Dictionary<string, string[]> {
    ["steam"] = new[] { "Steam", "It’s in your Steam library." }, ["gog"] = new[] { "GOG", "Download it from your GOG library, or install it in GOG Galaxy." },
    ["epic"] = new[] { "Epic Games", "Install it from Library in the Epic Games Launcher." }, ["playstation"] = new[] { "PlayStation", "Download it from the Game Library on your PlayStation." },
    ["xbox"] = new[] { "Xbox", "Install it from My games & apps on your Xbox, or the Xbox app on a PC." }, ["nintendo"] = new[] { "Nintendo", "Redownload it from the Nintendo eShop on your Switch." },
    ["physical"] = new[] { "Disc or cartridge", "You have it on a disc or cartridge." }, ["other"] = new[] { "Somewhere else", "You own it somewhere else." },
    ["playnite"] = new[] { "Playnite", "It’s in your Playnite library." } };

  /// <summary>Where you own it (the site's stores), and the collection it's part of.</summary>
  FrameworkElement OwnedAndCollection(JObject entry) {
    var rows = (page["stores"]?["stores"] as JArray ?? new JArray()).OfType<JObject>().ToList();
    var collection = entry?["collection"] as JObject;
    var card = Card(null, out var body);
    body.Children.Add(Eyebrow("Where you own it"));
    if (rows.Count == 0) body.Children.Add(Kit.Text("Not known yet. Importing a store’s list on Blastlog fills this in, or tick it on the game’s page there.", "Blastlog.Muted"));
    foreach (var r in rows) {
      var store = (string)r["store"]; var storeId = (string)r["storeId"];
      var words = Stores.TryGetValue(store ?? "", out var w) ? w : new[] { store, "" };
      var url = store == "steam" ? (storeId != null ? $"https://store.steampowered.com/app/{Uri.EscapeDataString(storeId)}/" : "https://steamcommunity.com/my/games/?tab=all") : store == "gog" ? "https://www.gog.com/en/account" : null;
      var where = store == "steam" && storeId != null ? "Play or install it from its Steam page." : words[1];
      var line = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
      if (url != null) line.Children.Add(Fingerprint.LinkButton(words[0] + " ↗", () => UiKit.Open(url), game: true).Also(b => b.Margin = new Thickness(-10, 0, 0, 0)));
      else line.Children.Add(Kit.Text(words[0]).Also(t => { t.FontWeight = FontWeights.Bold; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 10, 0); }));
      line.Children.Add(Kit.Text(where, "Blastlog.Muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
      body.Children.Add(line);
    }
    body.Children.Add(Fingerprint.LinkButton(rows.Count > 0 ? "Change on Blastlog ↗" : "Add where you own it on Blastlog ↗", () => UiKit.Open(plugin.ServerUrl + "/library/" + entryId)).Also(b => b.Margin = new Thickness(-10, 4, 0, 0)));
    if (collection != null) {
      body.Children.Add(Eyebrow("Part of a collection").Also(t => t.Margin = new Thickness(0, 14, 0, 6)));
      var id = (string)collection["id"];
      body.Children.Add(Fingerprint.LinkButton(((string)collection["title"] ?? "Collection") + " ↗", () => UiKit.Open(plugin.ServerUrl + "/library/collections/" + id), game: true).Also(b => b.Margin = new Thickness(-10, 0, 0, 0)));
    }
    return card;
  }

  /// <summary>More like this: one row of small covers, each wearing its own game; one in Playnite opens there, the rest on Blastlog.</summary>
  FrameworkElement MoreLikeThis(string title) {
    var items = (page["like"]?["items"] as JArray ?? new JArray()).OfType<JObject>().ToList();
    if (items.Count == 0) return null;
    var card = Card("More like " + title, out var body);
    body.Children.Add(Kit.Text("Games you haven’t played yet that play the most like it, from your library, your friends’ games and Discover.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, -6, 0, 12)));
    var row = new WrapPanel();
    var shown = 6;
    void Fill() {
      row.Children.Clear();
      foreach (var item in items.Take(shown)) row.Children.Add(Tile(item));
      if (items.Count > shown) row.Children.Add(Fingerprint.LinkButton($"Show {items.Count - shown} more", () => { shown = items.Count; Fill(); }, game: true).Also(b => b.VerticalAlignment = VerticalAlignment.Center));
    }
    Fill();
    body.Children.Add(row);
    return card;
  }
  FrameworkElement Tile(JObject item) {
    var game = new BlastlogGame { Id = (string)item["gameId"], Title = (string)item["title"], CoverUrl = (string)item["coverUrl"] };
    var entry = (string)item["entryId"]; var inPlaynite = entry != null ? plugin.GameFor(entry) : null;
    var stack = new StackPanel { Width = 120 };
    stack.Children.Add(Kit.Cover(plugin, game, 120, ArtworkContext.Thumbnail, 14));
    stack.Children.Add(Kit.Text(game.Title ?? "", "Blastlog.Small").Also(t => { t.FontWeight = FontWeights.Bold; t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text"); t.MaxHeight = 40; t.TextTrimming = TextTrimming.CharacterEllipsis; t.Margin = new Thickness(0, 8, 0, 0); }));
    var predicted = (double?)item["predictedRating"];
    var source = (string)item["source"] == "library" ? "Your library" : (string)item["source"] == "friends" ? "Friends" : "Discover";
    stack.Children.Add(Kit.Text(predicted != null ? $"{source} · ★ {predicted.Value.ToString("0.0", CultureInfo.InvariantCulture)}" : source, "Blastlog.Small"));
    var button = new Button { Content = stack, Template = Fingerprint.PlainTemplate(), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 14, 14), FocusVisualStyle = (Style)Kit.Styles["Blastlog.FocusRing"], VerticalAlignment = VerticalAlignment.Top };
    var reasons = (item["reasons"] as JArray ?? new JArray()).Select(r => (string)r).ToList(); var watch = (string)item["watchOut"];
    button.ToolTip = string.Join("\n", new[] { reasons.Count > 0 ? "Because you like " + string.Join(", ", reasons) : null, watch != null ? "Watch out: " + watch : null, inPlaynite != null ? "In your Playnite library" : null }.Where(x => x != null)) is string tip && tip.Length > 0 ? tip : null;
    AutomationProperties.SetName(button, "Open " + game.Title);
    button.Click += (s, e) => {
      try {
        if (inPlaynite != null) plugin.Api.MainView.SelectGame(inPlaynite.Id);
        else if (entry != null) UiKit.Open(plugin.ServerUrl + "/library/" + entry); else Nav.DiscoverGame(plugin, game.Id);
      } catch (Exception ex) { Log.Error(ex, "Blastlog more like this"); }
    };
    Kit.GameScope(button, plugin, game);
    return button;
  }

  /// <summary>History, newest first; the newest status or stars change can be undone from here.</summary>
  FrameworkElement History() {
    var card = Card("History", out var body);
    if (history == null) { body.Children.Add(Kit.Text("Loading…", "Blastlog.Muted")); return card; }
    if (history.Count == 0) { body.Children.Add(Kit.Text("No changes yet.", "Blastlog.Muted")); return card; }
    var first = true;
    foreach (var e in history.OfType<JObject>().Take(12)) {
      var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
      var when = DateTimeOffset.TryParse((string)e["createdAt"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.ToLocalTime().ToString("MMM d, yyyy, h:mm tt", CultureInfo.CurrentCulture) : "";
      var undo = first ? UndoFor(e) : null; first = false;
      if (undo != null) { DockPanel.SetDock(undo, Dock.Right); row.Children.Add(undo); }
      var text = new StackPanel();
      text.Children.Add(Kit.Text(Describe(e)));
      text.Children.Add(Kit.Text($"{when} · {Where((string)e["origin"])}", "Blastlog.Small"));
      row.Children.Add(text);
      body.Children.Add(row);
    }
    return card;
  }
  static string Where(string origin) => origin == null ? "Blastlog" : origin.StartsWith("playnite", StringComparison.Ordinal) ? "from Playnite" : origin == "blastlog_user" ? "on Blastlog"
    : origin.Contains("import") ? "from an import" : "by Blastlog";
  static string Describe(JObject e) {
    var action = (string)e["action"]; var before = e["before"] as JObject; var after = e["after"] as JObject;
    if (action == "library.add") return "Added to your library" + (after?["status"] != null ? $" as {GamePage.StatusWord((string)after["status"]).ToLowerInvariant()}" : "");
    if (action == "library.remove") return "Removed from your library";
    if (action == "library.restore") return "Put back in your library";
    var parts = new List<string>();
    string S(JObject o, string k) => (string)o?[k];
    if (S(before, "status") != S(after, "status") && after?["status"] != null) parts.Add($"Status {GamePage.StatusWord(S(before, "status")).ToLowerInvariant()} → {GamePage.StatusWord(S(after, "status")).ToLowerInvariant()}");
    var rb = (double?)before?["review"]?["rating"]; var ra = (double?)after?["review"]?["rating"];
    if (rb != ra) parts.Add(ra == null ? "Stars cleared" : rb == null ? $"Rated {ra}★" : $"Stars {rb}★ → {ra}★");
    var hb = (string)before?["review"]?["bodyHash"]; var ha = (string)after?["review"]?["bodyHash"];
    if (hb != ha) parts.Add(ha == null ? "Review removed" : hb == null ? "Review written" : "Review edited");
    if (S(before, "ownership") != S(after, "ownership") && after?["ownership"] != null) parts.Add(S(after, "ownership") == "wishlist" ? "Moved to your wishlist" : "Moved to your library");
    if (S(before, "finishedOn") != S(after, "finishedOn") && S(after, "finishedOn") != null) parts.Add("Finished on " + S(after, "finishedOn"));
    return parts.Count > 0 ? string.Join(" · ", parts) : "Details changed";
  }
  /// <summary>Undo for the newest change when it moved status or stars: puts the before values back as a new change (itself undoable).</summary>
  FrameworkElement UndoFor(JObject e) {
    if ((string)e["action"] != "library.change" || !(e["before"] is JObject before) || !(e["after"] is JObject after)) return null;
    var change = new ChangeItem();
    if ((string)before["status"] != (string)after["status"] && before["status"] != null) change.Status = (string)before["status"];
    var rb = (double?)before["review"]?["rating"]; var ra = (double?)after["review"]?["rating"];
    if (rb != ra) { change.SetRating = true; change.RatingValue = rb == null ? (int?)null : (int)rb.Value; }
    if (change.Status == null && !change.SetRating) return null;
    if (change.Status != null && (string)before["finishedOn"] != (string)after["finishedOn"]) change.FinishedOn = (string)before["finishedOn"];
    var b = Fingerprint.LinkButton("Undo", async () => await Change(change, "Undone"), game: true);
    b.VerticalAlignment = VerticalAlignment.Top; b.IsEnabled = !busy;
    AutomationProperties.SetName(b, "Undo this change");
    return b;
  }

  // ---- No data ----
  FrameworkElement NotPaired() {
    var card = Card(null, out var body);
    body.Children.Add(Eyebrow("Blastlog"));
    if (!plugin.Configured) { body.Children.Add(Kit.Text("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see this game’s fingerprint, your take and your friends here.", "Blastlog.Muted")); return card; }
    body.Children.Add(Kit.Text("This game isn’t paired with Blastlog yet.", "Blastlog.Muted"));
    body.Children.Add(Kit.Button("Pair games", async () => { await plugin.PairGames(); await Load(GameContext, fresh: true); }, "Blastlog.Ghost").Also(b => { b.Margin = new Thickness(0, 12, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; }));
    return card;
  }
  FrameworkElement Unavailable() {
    var card = Card(null, out var body);
    body.Children.Add(Eyebrow("Blastlog"));
    body.Children.Add(Kit.Text("Couldn’t reach Blastlog for this game just now. It shows up here once Blastlog answers.", "Blastlog.Muted"));
    body.Children.Add(Kit.Button("Try again", async () => await Load(GameContext, fresh: true), "Blastlog.Ghost").Also(b => { b.Margin = new Thickness(0, 12, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; }));
    return card;
  }
  /// <summary>Offline with nothing cached: the sync's own copy of status and stars, read only.</summary>
  FrameworkElement Fallback() {
    var cached = entryId != null ? plugin.CachedEntry(entryId) : null; if (cached == null) return null;
    var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
    row.Children.Add(Kit.StatusPill(cached.Status).Also(p => p.Margin = new Thickness(0, 0, 12, 0)));
    row.Children.Add(Kit.Stars(cached.Rating).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    return row;
  }
}
}
