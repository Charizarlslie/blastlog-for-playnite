using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Rate &amp; review, natively (the site's Rate &amp; review pop-up: App.tsx ReviewModal / EditEntry "wide", TapReview.tsx,
/// review-picks-api.ts). Left: ownership, status and completion date, the stars, the taps, the review; right: the review
/// questions. Save sends the entry (PATCH with expectedVersion) and then the taps (PUT), as the site does. A version
/// conflict loads the latest copy and keeps what the player changed. Opened through <see cref="Nav.Review"/>.
/// </summary>
public sealed class ReviewDialog {
  static readonly ILogger Log = LogManager.GetLogger();
  readonly BlastlogPlugin plugin; readonly string entryId; readonly Action done;
  Window window; Border page; ContentControl content;
  bool anySaved, saving;

  // The entry as Blastlog last sent it, and the form over it.
  JObject entry;
  string ownership, status, finishedOn, review; int? rating;
  // Taps: null `picks` = untouched (the saved ones show). `tapsReady` false = the picks couldn't be read, so no taps.
  bool tapsReady; List<Pick> saved = new List<Pick>(); List<Pick> picks;
  List<JObject> traits = new List<JObject>(); List<PickChip> chips = ReviewWords.FallbackChips.ToList();
  readonly Dictionary<string, TraitName> traitNames = new Dictionary<string, TraitName>();
  readonly Dictionary<string, bool> more = new Dictionary<string, bool> { ["worked"] = false, ["held_back"] = false };
  string error, pickError;

  // Controls that live as long as the form (re-rendering them would lose the caret).
  ComboBox ownershipBox, statusBox; TextBox dateBox, reviewBox; TextBlock reviewHint; Button saveButton;
  readonly ContentControl ratingHost = new ContentControl(), noteHost = new ContentControl(), tapHost = new ContentControl(),
    sideHost = new ContentControl(), errorHost = new ContentControl();
  readonly List<(ToggleButton button, string side, string trait, string chip)> chipButtons = new List<(ToggleButton, string, string, string)>();
  bool quiet;

  ReviewDialog(BlastlogPlugin plugin, string entryId, Action done) { this.plugin = plugin; this.entryId = entryId; this.done = done; }

  /// <summary>Opens the dialog for one library entry; <paramref name="done"/> runs after it closes if anything saved.</summary>
  public static void Open(BlastlogPlugin plugin, string entryId, Action done) {
    try { new ReviewDialog(plugin, entryId, done).Show(); } catch (Exception e) { Log.Error(e, "Blastlog Rate & review"); }
  }

  void Show() {
    window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
    window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow(); window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    // The site's pop-up: up to 1180 wide and 94% of the screen tall; here up to 94% of Playnite's window.
    var room = window.Owner != null && window.Owner.ActualHeight > 0 ? new Size(window.Owner.ActualWidth, window.Owner.ActualHeight) : new Size(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
    window.Title = "Rate and review"; window.MinWidth = 560; window.MinHeight = 480;
    window.Width = Math.Max(window.MinWidth, Math.Min(1180, room.Width * 0.94)); window.Height = Math.Max(window.MinHeight, Math.Min(900, room.Height * 0.94));
    content = new ContentControl();
    page = new Border { Child = content, Padding = new Thickness(28, 22, 28, 22) };
    page.Resources.MergedDictionaries.Add(Kit.Styles);
    page.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface");
    Kit.GameScope(page, GameColour.BrandGreen);
    window.Content = page;
    window.PreviewKeyDown += (s, e) => { try { if (e.Key == Key.Escape && !saving) window.Close(); } catch (Exception ex) { Log.Error(ex, "Blastlog Rate & review key"); } };
    window.Closed += (s, e) => { try { if (anySaved) done?.Invoke(); } catch (Exception ex) { Log.Error(ex, "Blastlog Rate & review done"); } };
    Loading("Opening the review…");
    Load();
    window.ShowDialog();
  }

  void Loading(string words) => content.Content = Kit.Text(words, "Blastlog.Muted");

  async void Load() {
    try {
      var entryRead = plugin.Mirror("entries/" + entryId + "?like=0", fresh: true); var picksRead = plugin.Mirror("entries/" + entryId + "/picks", fresh: true);
      await Task.WhenAll(entryRead, picksRead);
      entry = entryRead.Result.Value?["entry"] as JObject;
      if (entry == null) { Failed(); return; }
      ReadPicks(picksRead.Result.Value);
      FromEntry(entry);
      var title = (string)entry["game"]?["title"];
      window.Title = title != null ? "Rate and review " + title : "Rate and review";
      Wear();
      Build(title);
    } catch (Exception e) { Log.Error(e, "Blastlog Rate & review load"); Failed(); }
  }

  void Failed() {
    var stack = new StackPanel();
    stack.Children.Add(Kit.Text("The review didn’t open. Check the connection and try again.", "Blastlog.Body"));
    stack.Children.Add(Kit.Button("Try again", () => { Loading("Opening the review…"); Load(); }, "Blastlog.Ghost").Also(b => { b.Margin = new Thickness(0, 14, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; }));
    content.Content = stack;
  }

  async void Wear() {
    try {
      var g = entry["game"] as JObject; if (g == null) return;
      BlastlogGame game; try { game = g.ToObject<BlastlogGame>(); } catch (Exception) { game = new BlastlogGame { Id = (string)g["id"], Title = (string)g["title"], CoverUrl = (string)g["coverUrl"] }; }
      Kit.GameScope(page, await plugin.Theme.SeedFor(game));
    } catch (Exception e) { Log.Warn(e, "Blastlog Rate & review colour"); }
  }

  void ReadPicks(JObject data) {
    tapsReady = data != null;
    if (data == null) return;
    saved = (data["picks"] as JArray ?? new JArray()).OfType<JObject>().Select(p => new Pick((string)p["side"], (string)p["characteristicId"], (string)p["chip"])).ToList();
    picks = null;
    traitNames.Clear();
    foreach (var p in (data["picks"] as JArray ?? new JArray()).OfType<JObject>()) if ((string)p["characteristicId"] != null) traitNames[(string)p["characteristicId"]] = new TraitName((string)p["label"] ?? "");
    var options = data["options"] as JObject;
    traits = (options?["traits"] as JArray ?? new JArray()).OfType<JObject>().ToList();
    foreach (var t in traits) if ((string)t["id"] != null) traitNames[(string)t["id"]] = new TraitName((string)t["label"] ?? "", (string)t["family"]);
    var given = (options?["chips"] as JArray ?? new JArray()).OfType<JObject>()
      .Select(c => new PickChip((string)c["key"], (string)c["label"], (c["sides"] as JArray ?? new JArray()).Select(s => (string)s).ToArray())).Where(c => c.Key != null).ToList();
    chips = given.Count > 0 ? given : ReviewWords.FallbackChips.ToList();
  }

  void FromEntry(JObject e) {
    ownership = (string)e["ownership"] ?? "library"; status = (string)e["status"] ?? "not_started"; finishedOn = (string)e["finishedOn"] ?? "";
    rating = Rating(e); review = (string)e["review"] ?? "";
  }
  static int? Rating(JObject e) => e?["rating"] == null || e["rating"].Type == JTokenType.Null ? (int?)null : (int)Math.Round((double)e["rating"]);
  static string Today() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

  // ---- State the site derives (EditEntry) ----
  bool Judgeable => ReviewWords.CanRateOrReview(status);
  string SavedReview => ((string)entry?["review"] ?? "").Trim();
  bool OnHold => !Judgeable && (Rating(entry) != null || SavedReview.Length > 0);
  bool Blocked => !Judgeable && ((rating != null && rating != Rating(entry)) || (review.Trim().Length > 0 && review.Trim() != SavedReview));
  List<Pick> Current => picks ?? saved;
  TraitName NameOf(Pick p) => p.CharacteristicId != null && traitNames.TryGetValue(p.CharacteristicId, out var n) ? n : null;
  string Starter(IList<Pick> list, int? stars) => ReviewWords.StarterLine(list, NameOf, stars);
  bool Untouched(string text, IList<Pick> list, int? stars) => text.Trim().Length == 0 || text == Starter(list, stars);

  // ---- Layout ----
  void Build(string title) {
    var root = new DockPanel();
    var heading = Kit.Text(title != null ? "Rate and review " + title : "Rate and review", "Blastlog.Section"); heading.Margin = new Thickness(0, 0, 0, 18);
    DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

    var main = new StackPanel();
    main.Children.Add(Fields());
    main.Children.Add(Kit.Text("Completion date is filled when entering Done, Quit, DNF, or Unplayable. Existing dates are retained if a game is reopened and may be corrected manually.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
    main.Children.Add(Label("Rating").Also(t => t.Margin = new Thickness(0, 18, 0, 0)));
    main.Children.Add(ratingHost);
    main.Children.Add(noteHost);
    main.Children.Add(tapHost);
    main.Children.Add(Label("Review").Also(t => t.Margin = new Thickness(0, 18, 0, 0)));
    main.Children.Add(ReviewBox());
    // Errors and Save sit in a footer pinned under the form, so they never fall below the fold on a short window.
    errorHost.Margin = new Thickness(0, 0, 0, 10);
    saveButton = Kit.Button("Save changes", Save); saveButton.Margin = new Thickness(0); saveButton.MinWidth = 180;
    var cancel = Kit.Button("Cancel", () => { if (!saving) window.Close(); }, "Blastlog.Ghost"); cancel.Margin = new Thickness(0, 0, 10, 0);
    var footer = new StackPanel { Margin = new Thickness(0, 14, 8, 0) };
    footer.Children.Add(errorHost);
    footer.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, saveButton } });
    var footerFrame = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = footer, Margin = new Thickness(0, 8, 0, 0) };
    footerFrame.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    DockPanel.SetDock(footerFrame, Dock.Bottom); root.Children.Add(footerFrame);

    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.Children.Add(main); grid.Children.Add(sideHost);
    // Two columns on a wide window; on a narrow one the questions go under the form.
    void Arrange() {
      var wide = grid.ActualWidth == 0 || grid.ActualWidth >= 820;
      var hasSide = sideHost.Content != null;
      Grid.SetColumn(main, 0); Grid.SetRow(main, 0); Grid.SetColumnSpan(main, wide && hasSide ? 1 : 3);
      Grid.SetColumn(sideHost, wide ? 2 : 0); Grid.SetRow(sideHost, wide ? 0 : 1); Grid.SetColumnSpan(sideHost, wide ? 1 : 3);
      sideHost.Margin = wide ? new Thickness(0) : new Thickness(0, 18, 0, 0);
      main.MaxWidth = hasSide || !wide ? double.PositiveInfinity : 680; main.HorizontalAlignment = HorizontalAlignment.Left;
      if (hasSide || !wide) main.HorizontalAlignment = HorizontalAlignment.Stretch;
    }
    grid.SizeChanged += (s, e) => { try { Arrange(); } catch (Exception ex) { Log.Error(ex, "Blastlog Rate & review layout"); } };
    arrange = Arrange;
    root.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) });
    content.Content = root;
    Refresh();
  }
  Action arrange;

  static TextBlock Label(string words) => Kit.Text(words, "Blastlog.Small").Also(t => { t.FontWeight = FontWeights.Bold; t.Margin = new Thickness(0, 0, 0, 8); });

  FrameworkElement Fields() {
    var grid = new Grid();
    for (var i = 0; i < 5; i++) grid.ColumnDefinitions.Add(i % 2 == 1 ? new ColumnDefinition { Width = new GridLength(12) } : new ColumnDefinition());
    ownershipBox = Combo(new[] { ("library", "In library"), ("wishlist", "Wishlist"), ("unknown", "Unknown") }, ownership, "Ownership", v => { ownership = v; });
    statusBox = Combo(ReviewWords.Statuses.Select(s => (s, ReviewWords.StatusLabel(s))).ToArray(), status, "Status", v => {
      status = v; finishedOn = ReviewWords.CompletionDate(v, dateBox.Text.Trim(), Today()); quiet = true; dateBox.Text = finishedOn; quiet = false; Refresh();
    });
    dateBox = Dash.Field(); dateBox.Text = finishedOn; dateBox.MaxLength = 10;
    dateBox.TextChanged += (s, e) => { if (!quiet) finishedOn = dateBox.Text.Trim(); };
    AutomationProperties.SetName(dateBox, "Completion date, as year-month-day");
    var dateFrame = new Border { Child = dateBox, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 0, 12, 0), MinHeight = 44, Margin = new Thickness(0) };
    dateFrame.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2"); dateFrame.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Edge");
    var cells = new (string label, FrameworkElement control)[] { ("Ownership", ownershipBox), ("Status", statusBox), ("Completion date", dateFrame) };
    for (var i = 0; i < cells.Length; i++) {
      var cell = new StackPanel(); cell.Children.Add(Label(cells[i].label)); cell.Children.Add(cells[i].control);
      Grid.SetColumn(cell, i * 2); grid.Children.Add(cell);
    }
    return grid;
  }

  ComboBox Combo((string value, string label)[] options, string current, string name, Action<string> changed) {
    var box = new ComboBox { MinHeight = 44, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center };
    foreach (var o in options) box.Items.Add(new ComboBoxItem { Content = o.label, Tag = o.value, IsSelected = o.value == current });
    if (box.SelectedItem == null) box.Items.Add(new ComboBoxItem { Content = current, Tag = current, IsSelected = true });
    AutomationProperties.SetName(box, name);
    box.SelectionChanged += (s, e) => { try { if (!quiet && box.SelectedItem is ComboBoxItem i) changed((string)i.Tag); } catch (Exception ex) { Log.Error(ex, "Blastlog Rate & review " + name); } };
    return box;
  }
  void Select(ComboBox box, string value) {
    quiet = true;
    try { foreach (ComboBoxItem i in box.Items) if ((string)i.Tag == value) { box.SelectedItem = i; break; } } finally { quiet = false; }
  }

  FrameworkElement ReviewBox() {
    reviewBox = new TextBox { Text = review, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 190, MaxLength = 10000, Padding = new Thickness(12, 10, 12, 10), FontSize = 16, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    reviewBox.SetResourceReference(Control.FontFamilyProperty, "Blastlog.Font"); reviewBox.SetResourceReference(Control.BackgroundProperty, "Blastlog.Surface2");
    reviewBox.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text"); reviewBox.SetResourceReference(Control.BorderBrushProperty, "Blastlog.Edge");
    reviewBox.SetResourceReference(TextBoxBase.CaretBrushProperty, "Blastlog.Text");
    AutomationProperties.SetName(reviewBox, "Review");
    reviewHint = Kit.Text("", "Blastlog.Muted"); reviewHint.IsHitTestVisible = false; reviewHint.Margin = new Thickness(15, 11, 15, 0); reviewHint.VerticalAlignment = VerticalAlignment.Top;
    reviewBox.TextChanged += (s, e) => { try { if (!quiet) review = reviewBox.Text; Hint(); UpdateSave(); } catch (Exception ex) { Log.Error(ex, "Blastlog Rate & review text"); } };
    var grid = new Grid(); grid.Children.Add(reviewBox); grid.Children.Add(reviewHint);
    return grid;
  }
  void Hint() {
    reviewHint.Text = ReviewWords.IsTerminal(status) ? "Start anywhere. A few honest sentences beat a perfect essay." : "Write what worked, what didn’t, and what you’ll remember…";
    reviewHint.Visibility = string.IsNullOrEmpty(reviewBox.Text) ? Visibility.Visible : Visibility.Collapsed;
  }
  void SetReview(string text) { review = text; quiet = true; try { reviewBox.Text = text; } finally { quiet = false; } Hint(); }

  /// <summary>Redraws everything that follows the status, stars and taps.</summary>
  void Refresh() {
    ratingHost.Content = Stars();
    noteHost.Content = Note();
    tapHost.Content = Judgeable && status != "unplayable" && tapsReady ? Taps() : null;
    sideHost.Content = Side();
    reviewBox.IsEnabled = Judgeable && !saving;
    Hint(); Errors(); UpdateSave();
    arrange?.Invoke();
  }

  void UpdateSave() {
    if (saveButton == null) return;
    saveButton.IsEnabled = !saving && !Blocked;
    saveButton.Content = saving ? "Saving…" : "Save changes";
  }

  FrameworkElement Stars() {
    var row = new StackPanel { Orientation = Orientation.Horizontal };
    for (var i = 1; i <= 5; i++) {
      var value = i; var on = (rating ?? 0) >= value;
      var b = new Button { Content = "★", Style = Kit.Style("Blastlog.Primary"), FontSize = 25, Width = 49, Height = 45, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), IsEnabled = Judgeable && !saving };
      b.SetResourceReference(Control.BackgroundProperty, on ? "Blastlog.Game" : "Blastlog.Surface2");
      b.SetResourceReference(Control.ForegroundProperty, on ? "Blastlog.OnGame" : "Blastlog.Edge");
      AutomationProperties.SetName(b, $"{value} star{(value == 1 ? "" : "s")}");
      if (rating == value) AutomationProperties.SetItemStatus(b, "Selected");
      b.Click += (s, e) => { try { ChangeRating(rating == value ? (int?)null : value); } catch (Exception ex) { Log.Error(ex, "Blastlog Rate & review stars"); } };
      row.Children.Add(b);
    }
    return row;
  }

  void ChangeRating(int? next) {
    var auto = review.Trim().Length > 0 && Untouched(review, Current, rating);
    rating = next;
    if (auto) SetReview(Starter(Current, next));
    Refresh();
  }

  FrameworkElement Note() {
    if (Judgeable) return null;
    var t = Kit.Text("", "Blastlog.Small"); t.Margin = new Thickness(0, 10, 0, 0);
    if (Blocked) {
      t.Inlines.Add("Ratings and reviews are for games you’re done with: Done, DNF, Quit or Unplayable. Pick one of those, or ");
      var undo = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("undo the change")) { Focusable = true };
      undo.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.GameText");
      undo.Click += (s, e) => { try { rating = Rating(entry); SetReview((string)entry?["review"] ?? ""); Refresh(); } catch (Exception ex) { Log.Error(ex, "Blastlog Rate & review undo"); } };
      t.Inlines.Add(undo); t.Inlines.Add(".");
    } else t.Text = OnHold ? "On hold: your stars and review count once you mark it Done, DNF, Quit or Unplayable." : "Rate and review it once you’re done playing: Done, DNF, Quit or Unplayable.";
    return t;
  }

  // ---- Taps (TapReview.tsx) ----
  static readonly (string side, string ask)[] Sides = { ("worked", "What worked?"), ("held_back", "What held it back?") };

  FrameworkElement Taps() {
    chipButtons.Clear();
    var box = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Padding = new Thickness(18, 16, 18, 10), Margin = new Thickness(0, 18, 0, 0) };
    box.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    var stack = new StackPanel();
    stack.Children.Add(Kit.Text("Tap what fits", "Blastlog.Body").Also(t => t.FontWeight = FontWeights.Bold));
    stack.Children.Add(Kit.Text("Skip either side. Writing is optional too.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 2, 0, 0)));
    foreach (var (side, ask) in Sides) {
      stack.Children.Add(Kit.Text(ask, "Blastlog.Body").Also(t => { t.FontWeight = FontWeights.Bold; t.Margin = new Thickness(0, 14, 0, 8); }));
      var wrap = new WrapPanel(); AutomationProperties.SetName(wrap, ask);
      var shown = traits.Where(t => more[side] || (bool?)t["top"] == true || ReviewWords.Has(Current, side, (string)t["id"], null)).ToList();
      var hidden = traits.Count - shown.Count;
      var sideChips = chips.Where(c => c.Sides.Contains(side)).ToList();
      var nothing = sideChips.FirstOrDefault(c => c.Key == "nothing");
      if (nothing != null) wrap.Children.Add(Chip(nothing.Label, side, null, nothing.Key));
      foreach (var t in shown) wrap.Children.Add(Chip((string)t["label"], side, (string)t["id"], null));
      foreach (var c in sideChips.Where(c => c.Key != "nothing")) wrap.Children.Add(Chip(c.Label, side, null, c.Key));
      if (hidden > 0) {
        var s = side;
        wrap.Children.Add(MoreChip($"{hidden} more", () => { more[s] = true; tapHost.Content = Taps(); }));
      }
      stack.Children.Add(wrap);
    }
    box.Child = stack;
    Restyle();
    return box;
  }

  const string ChipTemplate = "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ButtonBase\"><Grid>"
    + "<Border x:Name=\"Fill\" Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" CornerRadius=\"22\" />"
    + "<Rectangle x:Name=\"Dash\" RadiusX=\"22\" RadiusY=\"22\" Stroke=\"{TemplateBinding BorderBrush}\" StrokeThickness=\"1\" StrokeDashArray=\"4 3\" Visibility=\"{TemplateBinding Tag}\" />"
    + "<ContentPresenter Margin=\"{TemplateBinding Padding}\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" /></Grid>"
    + "<ControlTemplate.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"Fill\" Property=\"Opacity\" Value=\"0.88\" /></Trigger>"
    + "<Trigger Property=\"IsEnabled\" Value=\"False\"><Setter Property=\"Opacity\" Value=\"0.5\" /></Trigger></ControlTemplate.Triggers></ControlTemplate>";
  static ControlTemplate chipTemplate;
  static ControlTemplate Template() => chipTemplate ?? (chipTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(ChipTemplate));

  static TextBlock ChipWords(string words) => Kit.Text(words, "Blastlog.Body").Also(t => { t.FontSize = 15; t.FontWeight = FontWeights.SemiBold; t.TextAlignment = TextAlignment.Center; t.Foreground = null; });

  ToggleButton Chip(string label, string side, string trait, string chip) {
    var words = ChipWords(label ?? "");
    var b = new ToggleButton { Content = words, Template = Template(), MinHeight = 44, MinWidth = 44, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 8),
      Cursor = Cursors.Hand, FocusVisualStyle = Kit.Style("Blastlog.FocusRing"), Tag = Visibility.Collapsed, IsEnabled = !saving };
    words.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = b });
    AutomationProperties.SetName(b, label ?? "");
    b.Click += (s, e) => {
      try { ChangePicks(trait != null ? ReviewWords.ToggleTrait(Current, side, trait) : ReviewWords.ToggleChip(Current, side, chip)); }
      catch (Exception ex) { Log.Error(ex, "Blastlog Rate & review tap"); }
    };
    chipButtons.Add((b, side, trait, chip));
    return b;
  }

  Button MoreChip(string label, Action click) {
    var words = ChipWords(label);
    var b = new Button { Content = words, Template = Template(), MinHeight = 44, MinWidth = 44, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 8),
      Cursor = Cursors.Hand, FocusVisualStyle = Kit.Style("Blastlog.FocusRing"), Tag = Visibility.Visible, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    b.SetResourceReference(Control.BorderBrushProperty, "Blastlog.Edge"); b.SetResourceReference(Control.ForegroundProperty, "Blastlog.TextMuted");
    words.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = b });
    b.Click += (s, e) => { try { click(); } catch (Exception ex) { Log.Error(ex, "Blastlog Rate & review more"); } };
    return b;
  }

  /// <summary>Pressed chips in the game's colour (aria-pressed on the site); the rest on the raised surface.</summary>
  void Restyle() {
    foreach (var (b, side, trait, chip) in chipButtons) {
      var on = ReviewWords.Has(Current, side, trait, chip);
      b.IsChecked = on;
      b.SetResourceReference(Control.BackgroundProperty, on ? "Blastlog.Game" : "Blastlog.Surface2");
      b.SetResourceReference(Control.BorderBrushProperty, on ? "Blastlog.Text" : "Blastlog.Edge");
      b.SetResourceReference(Control.ForegroundProperty, on ? "Blastlog.OnGame" : "Blastlog.Text");
      b.BorderThickness = new Thickness(on ? 2 : 1);
      b.IsEnabled = !saving;
    }
  }

  /// <summary>Taps write a starter line into an empty review and keep it in step while it's untouched (review-picks-api.ts).</summary>
  void ChangePicks(List<Pick> next) {
    if (Untouched(review, Current, rating)) SetReview(Starter(next, rating));
    picks = next;
    pickError = null;
    Restyle(); Errors(); UpdateSave();
  }

  // ---- The questions (review-questions.ts) ----
  FrameworkElement Side() {
    if (status == "unplayable") return Kit.Text(ReviewWords.UnplayableNote, "Blastlog.Small");
    if (!ReviewWords.IsTerminal(status)) return null;
    var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Padding = new Thickness(18) };
    card.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    AutomationProperties.SetName(card, "Questions worth answering");
    var stack = new StackPanel();
    stack.Children.Add(Kit.Text("Questions worth answering", "Blastlog.Label"));
    stack.Children.Add(Kit.Text("Answer any of them, in any order. Blastlog reads your review to learn what you love and what loses you.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 8, 0, 4)));
    foreach (var q in ReviewWords.Questions(status)) {
      var item = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(13, 10, 13, 10), Margin = new Thickness(0, 8, 0, 0) };
      item.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
      var words = new StackPanel();
      words.Children.Add(Kit.Text(q.Ask, "Blastlog.Body").Also(t => { t.FontSize = 14; t.FontWeight = FontWeights.Bold; t.Margin = new Thickness(0, 0, 0, 3); }));
      words.Children.Add(Kit.Text(q.Hint, "Blastlog.Small").Also(t => t.LineHeight = 20));
      item.Child = words;
      stack.Children.Add(item);
    }
    stack.Children.Add(Kit.Text(ReviewWords.Tip, "Blastlog.Small").Also(t => { t.Margin = new Thickness(0, 12, 0, 0); t.LineHeight = 20; }));
    card.Child = stack;
    return card;
  }

  void Errors() {
    var lines = new[] { error, pickError }.Where(x => x != null).ToList();
    if (lines.Count == 0) { errorHost.Content = null; return; }
    var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
    foreach (var l in lines) stack.Children.Add(Kit.Text(l, "Blastlog.Small").Also(t => t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Danger")));
    errorHost.Content = stack;
  }

  // ---- Saving: the entry, then the taps, as the site does ----
  async Task Save() {
    if (saving || entry == null || Blocked) return;
    var date = ReviewWords.CompletionDate(status, finishedOn.Trim(), Today());
    if (date.Length > 0 && !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
      error = "Write the completion date as year-month-day, like " + Today() + "."; pickError = null; Errors(); return;
    }
    saving = true; error = null; pickError = null; Refresh();
    try {
      var body = new JObject {
        ["expectedVersion"] = entry["version"], ["ownership"] = ownership, ["status"] = status,
        ["finishedOn"] = date.Length > 0 ? (JToken)date : JValue.CreateNull(), ["rating"] = rating != null ? (JToken)rating.Value : JValue.CreateNull(),
        ["review"] = review.Trim().Length > 0 ? (JToken)review.Trim() : JValue.CreateNull(),
      };
      var r = await plugin.ReviewSend(new HttpMethod("PATCH"), "entries/" + entryId, body);
      if (!r.Ok) {
        if (r.Error == "version_conflict") { await Merge(); error = "This game changed on Blastlog while this was open. The latest is loaded with your edits kept; check it and save again."; }
        else error = r.Error == "rate_when_done" ? "Stars and reviews go on once you’re done with it: Done, DNF, Quit or Unplayable." : r.Words;
        return;
      }
      anySaved = true;
      var updated = r.Value?["version"] != null ? r.Value : null;
      if (updated != null) entry = updated; else entry["version"] = (int?)entry["version"] + 1;
      var updatedStatus = (string)entry["status"] ?? status;
      if (tapsReady && picks != null && !ReviewWords.SamePicks(picks, saved) && ReviewWords.CanRateOrReview(updatedStatus)) {
        var body2 = new JObject { ["picks"] = new JArray(picks.Select(p => new JObject {
          ["side"] = p.Side, ["characteristicId"] = p.CharacteristicId != null ? (JToken)p.CharacteristicId : JValue.CreateNull(), ["chip"] = p.Chip != null ? (JToken)p.Chip : JValue.CreateNull() })) };
        var p2 = await plugin.ReviewSend(HttpMethod.Put, "entries/" + entryId + "/picks", body2);
        if (!p2.Ok) { pickError = "Your taps didn’t save. Press Save again."; plugin.ReviewSaved(entryId); return; }
        ReadSavedPicks(p2.Value);
      }
      plugin.ReviewSaved(entryId);
      saving = false;
      window.Close();
    } catch (Exception e) { Log.Error(e, "Blastlog Rate & review save"); error = "That didn’t save. Try again."; }
    finally { if (saving) { saving = false; Refresh(); } }
  }

  void ReadSavedPicks(JObject answer) {
    var list = answer?["picks"] as JArray;
    if (list == null) { saved = picks ?? saved; picks = null; return; }
    saved = list.OfType<JObject>().Select(p => new Pick((string)p["side"], (string)p["characteristicId"], (string)p["chip"])).ToList();
    foreach (var p in list.OfType<JObject>()) if ((string)p["characteristicId"] != null && !traitNames.ContainsKey((string)p["characteristicId"])) traitNames[(string)p["characteristicId"]] = new TraitName((string)p["label"] ?? "");
    picks = null;
  }

  /// <summary>
  /// A version conflict: load Blastlog's latest copy, take its value for every field the player didn't change here, and
  /// keep the ones they did, so saving again neither loses their edits nor quietly undoes the other change.
  /// </summary>
  async Task Merge() {
    var latest = (await plugin.Mirror("entries/" + entryId + "?like=0", fresh: true)).Value?["entry"] as JObject;
    if (latest == null) return;
    var before = entry; entry = latest;
    if (ownership == ((string)before["ownership"] ?? "library")) ownership = (string)latest["ownership"] ?? ownership;
    if (status == ((string)before["status"] ?? "not_started")) status = (string)latest["status"] ?? status;
    if (finishedOn == ((string)before["finishedOn"] ?? "")) finishedOn = (string)latest["finishedOn"] ?? "";
    if (rating == Rating(before)) rating = Rating(latest);
    if (review == ((string)before["review"] ?? "")) SetReview((string)latest["review"] ?? "");
    Select(ownershipBox, ownership); Select(statusBox, status);
    quiet = true; try { dateBox.Text = finishedOn; } finally { quiet = false; }
  }
}
}
