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
using System.Windows.Threading;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Pieces shared by Your taste and Compare (the site's Chapter, Cover, toggles, Undo toast and charts' colours). Built on
/// the kit's tokens only; nothing here is a raw colour except people's colours, which come from the server.
/// </summary>
static class TasteUi {
  public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
  public const string ProfileIcon = "<circle cx=\"12\" cy=\"8\" r=\"4\" /><path d=\"M4 21a8 8 0 0 1 16 0\" />";
  public const string SearchIcon = "<circle cx=\"11\" cy=\"11\" r=\"8\" /><path d=\"m21 21-4.3-4.3\" />";

  /// <summary>The site's stars text: "4★", "4.5★".</summary>
  public static string Stars(double n) => (Math.Abs(n - Math.Round(n)) < 1e-9 ? Math.Round(n).ToString("0", Inv) : n.ToString("0.0", Inv)) + "★";
  public static string Plural(int n, string one, string many = null) => n + " " + (n == 1 ? one : many ?? one + "s");
  /// <summary>A token at a share of its strength over what's behind it (the site's color-mix(in srgb, var(--token) k, transparent)).</summary>
  public static SolidColorBrush Faded(string token, double share) =>
    new SolidColorBrush(Tint.Alpha((Color)ColorConverter.ConvertFromString(Tokens.For(ThemeBridge.CurrentMode)[token]), share));
  /// <summary>A token's colour as a brush (for tokens the theme doesn't publish as a resource, like ok-text).</summary>
  public static SolidColorBrush TokenBrush(string token) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Tokens.For(ThemeBridge.CurrentMode)[token]));

  /// <summary>Runs an event handler's work, logging instead of letting an exception reach Playnite.</summary>
  public static async void Run(Func<Task> work, string what) {
    try { await work(); } catch (Exception e) { LogManager.GetLogger().Error(e, "Blastlog " + what); }
  }

  public static TextBlock T(string text, string style = "Blastlog.Body", string brush = null, double size = 0, FontWeight? weight = null) {
    var t = Kit.Text(text ?? "", style);
    if (brush != null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
    if (size > 0) t.FontSize = size;
    if (weight != null) t.FontWeight = weight.Value;
    return t;
  }
  /// <summary>A line of mixed runs: (text, bold, brush key) — for "**Love** · your call".</summary>
  public static TextBlock Runs(string style, params (string text, bool bold, string brush)[] parts) {
    var t = Kit.Text("", style);
    foreach (var p in parts) {
      var r = new System.Windows.Documents.Run(p.text);
      if (p.bold) r.FontWeight = FontWeights.Bold;
      if (p.brush != null) r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, p.brush);
      t.Inlines.Add(r);
    }
    return t;
  }
  public static StackPanel Stack(Thickness? margin = null, params UIElement[] children) {
    var s = new StackPanel { Margin = margin ?? new Thickness(0) };
    foreach (var c in children) if (c != null) s.Children.Add(c);
    return s;
  }
  public static StackPanel Row(params UIElement[] children) {
    var s = new StackPanel { Orientation = Orientation.Horizontal };
    foreach (var c in children) if (c != null) s.Children.Add(c);
    return s;
  }
  public static WrapPanel Wrap(params UIElement[] children) {
    var w = new WrapPanel();
    foreach (var c in children) if (c != null) w.Children.Add(c);
    return w;
  }
  public static Border Box(UIElement child, string background, double radius, Thickness padding, string border = null, double borderWidth = 1) {
    var b = new Border { Child = child, CornerRadius = new CornerRadius(radius), Padding = padding };
    if (background != null) b.SetResourceReference(Border.BackgroundProperty, background);
    if (border != null) { b.BorderThickness = new Thickness(borderWidth); b.SetResourceReference(Border.BorderBrushProperty, border); }
    return b;
  }

  /// <summary>A small pill: outlined (Blastlog's read), filled in text colour (You said), or dashed (Early signal).</summary>
  public static Border Pill(string text, bool filled = false, bool dashed = false) {
    var t = T(text, "Blastlog.Small", filled ? "Blastlog.Bg" : dashed ? "Blastlog.TextMuted" : "Blastlog.Text", 0, FontWeights.Bold);
    t.TextWrapping = TextWrapping.NoWrap;
    var b = new Border { Child = t, CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3, 10, 3), BorderThickness = new Thickness(1),
      VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 0) };
    if (filled) { b.SetResourceReference(Border.BackgroundProperty, "Blastlog.Text"); b.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Text"); }
    else if (dashed) {
      // WPF borders can't dash; a dashed rectangle behind the text does the same.
      var grid = new Grid();
      var rect = new System.Windows.Shapes.Rectangle { RadiusX = 13, RadiusY = 13, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } };
      rect.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Blastlog.Edge");
      grid.Children.Add(rect); b.Child = null; t.Margin = new Thickness(10, 3, 10, 3); grid.Children.Add(t);
      b.Child = grid; b.Padding = new Thickness(0); b.BorderThickness = new Thickness(0);
    } else b.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Edge");
    return b;
  }

  /// <summary>A toggle (filter chip, tab, step): filled in the page's game colour when on, outlined when off.</summary>
  public static Button Toggle(string label, bool on, Action click, string accessible = null) {
    var b = Kit.Button(label, click, on ? "Blastlog.Primary" : "Blastlog.Ghost");
    b.Margin = new Thickness(0, 0, 8, 8);
    AutomationProperties.SetName(b, (accessible ?? label) + (on ? ", selected" : ""));
    return b;
  }
  public static Button Link(string label, Action click) { var b = Kit.Button(label, click, "Blastlog.TextButton"); b.Padding = new Thickness(6, 0, 6, 0); return b; }

  /// <summary>A numbered chapter (the site's Chapter): 01, title, one-line note, then its content.</summary>
  public static StackPanel Chapter(string index, string title, string note, UIElement content) {
    var head = new Grid { Margin = new Thickness(0, 44, 0, 18) };
    head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    head.RowDefinitions.Add(new RowDefinition()); head.RowDefinitions.Add(new RowDefinition());
    var i = T(index, "Blastlog.Body", "Blastlog.GameText", 14, FontWeights.ExtraBold); i.Margin = new Thickness(0, 0, 14, 0); i.VerticalAlignment = VerticalAlignment.Center;
    var h = T(title, "Blastlog.Body", null, 28, FontWeights.ExtraBold);
    var n = T(note, "Blastlog.Muted"); n.Margin = new Thickness(0, 6, 0, 0); n.MaxWidth = 680; n.HorizontalAlignment = HorizontalAlignment.Left;
    Grid.SetColumn(h, 1); Grid.SetColumn(n, 1); Grid.SetRow(n, 1);
    head.Children.Add(i); head.Children.Add(h); head.Children.Add(n);
    return Stack(null, head, content);
  }

  /// <summary>A cover from a URL the site sent (IGDB), cached on disk; the title's first letter on a tile until then.</summary>
  public static FrameworkElement Cover(BlastlogPlugin plugin, string url, string title, double width, double radius = 12) {
    var host = new Border { Width = width, Height = Math.Round(width * 4 / 3), CornerRadius = new CornerRadius(radius), ClipToBounds = true };
    host.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
    var letter = T(string.IsNullOrEmpty(title) ? "?" : title.Substring(0, 1), "Blastlog.Body", "Blastlog.TextMuted", Math.Max(14, width / 3), FontWeights.ExtraBold);
    letter.HorizontalAlignment = HorizontalAlignment.Center; letter.VerticalAlignment = VerticalAlignment.Center;
    host.Child = letter;
    AutomationProperties.SetName(host, title ?? "");
    LoadCover(plugin, Artwork.NormalizedLegacyUrl(url, width > 120 ? "cover_big_2x" : "cover_big"), host, radius);
    return host;
  }
  static async void LoadCover(BlastlogPlugin plugin, string url, Border host, double radius) {
    if (url == null || plugin.Theme == null) return;
    try {
      var file = await plugin.Theme.CachedFile(url);
      if (file == null) return;
      var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(file);
      image.DecodePixelWidth = (int)Math.Min(600, host.Width * 2); image.EndInit(); image.Freeze();
      host.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
      host.Child = null;
      host.Clip = new RectangleGeometry(new Rect(0, 0, host.Width, host.Height), radius, radius);
    } catch (Exception) { }
  }

  /// <summary>A button that is a cover and a title (a tile is one button, never a button inside a button).</summary>
  public static Button CoverButton(BlastlogPlugin plugin, string url, string title, double width, Action click, UIElement under = null) {
    var content = Stack(null, Cover(plugin, url, title, width));
    var name = T(title, "Blastlog.Small", "Blastlog.Text", 0, FontWeights.SemiBold); name.MaxWidth = width; name.MaxHeight = 40; name.TextTrimming = TextTrimming.CharacterEllipsis; name.Margin = new Thickness(0, 6, 0, 0);
    content.Children.Add(name);
    if (under != null) content.Children.Add(under);
    return Plain(content, click, title);
  }
  /// <summary>Anything made clickable without a button's chrome (keeps the focus ring and a hand cursor).</summary>
  public static Button Plain(UIElement content, Action click, string name = null) {
    var b = new Button { Content = content, Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
      HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 14),
      FocusVisualStyle = Kit.Style("Blastlog.FocusRing"), Template = PlainTemplate() };
    if (name != null) AutomationProperties.SetName(b, name);
    b.Click += (s, e) => { try { click(); } catch (Exception ex) { LogManager.GetLogger().Error(ex, "Blastlog click " + name); } };
    return b;
  }
  static ControlTemplate plain;
  static ControlTemplate PlainTemplate() {
    if (plain != null) return plain;
    var t = new ControlTemplate(typeof(Button));
    var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
    var border = new FrameworkElementFactory(typeof(Border));
    border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
    border.AppendChild(presenter);
    t.VisualTree = border;
    return plain = t;
  }

  /// <summary>
  /// Lays children out in as many columns as fit (each at least <paramref name="minWidth"/>), row by row, each row as
  /// tall as its tallest card: the site's auto-fit grid.
  /// </summary>
  public static Grid Columns(double minWidth, IEnumerable<UIElement> items, double gap = 16) {
    var grid = new Grid();
    var list = items.ToList();
    foreach (var item in list) { if (item is FrameworkElement f) f.Margin = new Thickness(0, 0, gap, gap); grid.Children.Add(item); }
    int columns = 0;
    void Layout(double width) {
      var n = Math.Max(1, Math.Min(list.Count == 0 ? 1 : list.Count, (int)Math.Floor((width + gap) / (minWidth + gap))));
      if (n == columns) return;
      columns = n;
      grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
      for (var c = 0; c < n; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      for (var r = 0; r < (list.Count + n - 1) / n; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      for (var i = 0; i < list.Count; i++) { Grid.SetRow(list[i], i / n); Grid.SetColumn(list[i], i % n); }
    }
    Layout(minWidth);
    grid.SizeChanged += (s, e) => { if (e.WidthChanged) Layout(e.NewSize.Width); };
    return grid;
  }

  /// <summary>A disclosure row (▸ / ▾, 44px), its content shown on demand.</summary>
  public static StackPanel Disclosure(string label, Func<UIElement> content, bool open = false, Action<bool> changed = null) {
    var panel = new StackPanel();
    Button head = null; UIElement body = null;
    void Set() {
      head.Content = (open ? "▾ " : "▸ ") + label;
      AutomationProperties.SetName(head, label + (open ? ", expanded" : ", collapsed"));
      if (open && body == null) { body = content(); panel.Children.Add(body); }
      if (body != null) body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }
    head = Kit.Button("", () => { open = !open; Set(); changed?.Invoke(open); }, "Blastlog.TextButton");
    head.HorizontalAlignment = HorizontalAlignment.Left; head.Padding = new Thickness(0, 0, 10, 0);
    panel.Children.Add(head);
    Set();
    return panel;
  }

  /// <summary>A search field with the site's icon (44px).</summary>
  public static (Border box, TextBox input) SearchField(string placeholder, double width = 320) {
    var input = new TextBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 16, VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 40 };
    input.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text"); input.SetResourceReference(Control.FontFamilyProperty, "Blastlog.Font");
    input.SetResourceReference(TextBoxBase.CaretBrushProperty, "Blastlog.Text");
    AutomationProperties.SetName(input, placeholder.TrimEnd('…'));
    var hint = T(placeholder, "Blastlog.Muted"); hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(3, 0, 0, 0);
    input.TextChanged += (s, e) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
    var icon = Icons.Make(SearchIcon, 18, (Brush)Application.Current.TryFindResource("Blastlog.TextMuted")); icon.Margin = new Thickness(0, 0, 8, 0); icon.VerticalAlignment = VerticalAlignment.Center;
    var field = new Grid(); field.Children.Add(hint); field.Children.Add(input); Grid.SetColumn(field, 1);
    grid.Children.Add(icon); grid.Children.Add(field);
    var box = Box(grid, "Blastlog.Surface2", 12, new Thickness(12, 2, 12, 2), "Blastlog.Edge");
    box.Width = width; box.HorizontalAlignment = HorizontalAlignment.Left; box.MinHeight = 44;
    return (box, input);
  }
  /// <summary>Calls <paramref name="act"/> once typing pauses for a moment.</summary>
  public static void Debounce(TextBox input, int ms, Action<string> act) {
    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
    timer.Tick += (s, e) => { timer.Stop(); try { act(input.Text); } catch (Exception ex) { LogManager.GetLogger().Error(ex, "Blastlog search"); } };
    input.TextChanged += (s, e) => { timer.Stop(); timer.Start(); };
  }

  /// <summary>An inline question with a confirm and a safer choice beside it (the site's ConfirmBox).</summary>
  public static Border Confirm(string question, string confirmLabel, bool danger, Action confirm, Action cancel, string cancelLabel = "Cancel") {
    var buttons = Wrap(Kit.Button(confirmLabel, confirm, danger ? "Blastlog.DangerButton" : "Blastlog.Primary"), Kit.Button(cancelLabel, cancel, "Blastlog.Ghost"));
    buttons.Margin = new Thickness(0, 10, 0, 0);
    var box = Box(Stack(null, T(question, "Blastlog.Body"), buttons), "Blastlog.Surface2", 14, new Thickness(14), danger ? "Blastlog.Danger" : "Blastlog.Edge");
    box.Margin = new Thickness(0, 10, 0, 0);
    return box;
  }

  public static Brush Brush(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
  public static Color ColorOf(string key) => (Brush(key) as SolidColorBrush)?.Color ?? Colors.Gray;
  public static SolidColorBrush Hex(string hex, double opacity = 1) {
    try { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) { Opacity = opacity }; b.Freeze(); return b; }
    catch (Exception) { return Brushes.Gray; }
  }
  /// <summary>Diagonal stripes in a token's colour (the site's "leans away" bars: never colour alone).</summary>
  public static Brush Stripes(string key) {
    var c = ColorOf(key);
    var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat, StartPoint = new Point(0, 0), EndPoint = new Point(5, 5) };
    brush.GradientStops.Add(new GradientStop(c, 0)); brush.GradientStops.Add(new GradientStop(c, .55));
    brush.GradientStops.Add(new GradientStop(Colors.Transparent, .55)); brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
    brush.Freeze();
    return brush;
  }

  /// <summary>Opens a page on the player's Blastlog (things that stay on the site: pictures, account settings).</summary>
  public static void Site(BlastlogPlugin plugin, string path) => UiKit.Open(plugin.ServerUrl + path);

  /// <summary>A Playnite window holding Blastlog content (Compare's look-up of one game).</summary>
  public static void Dialog(BlastlogPlugin plugin, string title, FrameworkElement content, double width = 980, double height = 720) {
    try {
      var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
      window.Title = title; window.Width = width; window.Height = height;
      window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow();
      window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
      window.Content = Kit.Page(content);
      window.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) window.Close(); };
      window.ShowDialog();
    } catch (Exception e) { LogManager.GetLogger().Error(e, "Blastlog dialog " + title); }
  }

  public static JArray Arr(JToken t) => t as JArray ?? new JArray();
  public static string S(JToken t, string key) => t?[key]?.Type == JTokenType.Null ? null : (string)t?[key];
  public static double? D(JToken t, string key) => t?[key] == null || t[key].Type == JTokenType.Null ? (double?)null : (double)t[key];
  public static int I(JToken t, string key) => t?[key] == null || t[key].Type == JTokenType.Null ? 0 : (int)Math.Round((double)t[key]);

  /// <summary>Every people colour on Compare, nudged until it reads as text on this mode's panels (design.md 2.4).</summary>
  public static string PersonColour(string colour) {
    try {
      var t = Tokens.For(ThemeBridge.CurrentMode);
      return Colour.NudgeToContrast(colour, new[] { t["surface"], t["bg"], t["surface-2"] }, 4.5)?.Colour ?? colour;
    } catch (Exception) { return colour; }
  }
  /// <summary>Near-black or white, whichever reads better on <paramref name="colour"/>.</summary>
  public static string InkOn(string colour) {
    try { return Colour.Contrast(colour, "#ffffff") > Colour.Contrast(colour, "#141418") ? "#ffffff" : "#141418"; } catch (Exception) { return "#141418"; }
  }
}

/// <summary>
/// What the last change did, with Undo (design.md 7, Toasts): a card floating at the bottom of the screen, gone after 8
/// seconds unless the pointer is on it. Recent changes keeps the same Undo with no time limit.
/// </summary>
sealed class UndoToast {
  readonly Popup popup = new Popup { AllowsTransparency = true, Placement = PlacementMode.Custom, StaysOpen = true, PopupAnimation = PopupAnimation.None };
  readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
  bool held;
  public UndoToast(Panel host) {
    host.Children.Add(popup);
    popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
      new[] { new CustomPopupPlacement(new Point((targetSize.Width - popupSize.Width) / 2, targetSize.Height - popupSize.Height - 28), PopupPrimaryAxis.None) };
    timer.Tick += (s, e) => { if (!held) Close(); };
    host.Unloaded += (s, e) => Close();
    host.Loaded += (s, e) => { popup.PlacementTarget = Viewport(host) ?? host; };
  }
  static FrameworkElement Viewport(DependencyObject from) {
    for (var d = from; d != null; d = VisualTreeHelper.GetParent(d)) if (d is ScrollViewer sv) return sv;
    return null;
  }
  public void Show(string text, Action undo) {
    var row = new StackPanel { Orientation = Orientation.Horizontal };
    var words = TasteUi.T(text, "Blastlog.Body"); words.VerticalAlignment = VerticalAlignment.Center; words.Margin = new Thickness(0, 0, 14, 0); words.MaxWidth = 520;
    row.Children.Add(words);
    if (undo != null) row.Children.Add(Kit.Button("Undo", () => { Close(); undo(); }, "Blastlog.Ghost"));
    var close = Kit.Button("✕", Close, "Blastlog.TextButton"); AutomationProperties.SetName(close, "Dismiss"); row.Children.Add(close);
    var card = TasteUi.Box(row, "Blastlog.Surface2", 16, new Thickness(18, 10, 10, 10), "Blastlog.Edge");
    card.Resources.MergedDictionaries.Add(Kit.Styles);
    card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = .45, Color = Colors.Black };
    card.Margin = new Thickness(20);
    card.MouseEnter += (s, e) => held = true; card.MouseLeave += (s, e) => held = false;
    card.KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
    popup.Child = card;
    popup.IsOpen = false; popup.IsOpen = true;
    timer.Stop(); timer.Start();
  }
  public void Close() { timer.Stop(); popup.IsOpen = false; }
}
}
