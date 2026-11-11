using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Pieces shared by the Play next and Discover screens: the cover-first game card (design.md 7 "Cards and tiles"), the
/// overlay tags and rank, segmented controls, taste chips, the result line with Undo, and Play / Install for games the
/// player has in Playnite. Every colour is a Blastlog.* token; a card wears its own game (design.md 2.3).
/// </summary>
static class PnKit {
  static readonly ILogger Log = LogManager.GetLogger();

  public static T Res<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement { element.SetResourceReference(property, key); return element; }
  public static string S(this JToken token, string key) => token?[key]?.Type == JTokenType.String ? (string)token[key] : token?[key]?.Type > JTokenType.None && token[key].Type != JTokenType.Null ? token[key].ToString() : null;
  public static double? D(this JToken token, string key) => token?[key] == null || token[key].Type == JTokenType.Null ? (double?)null : (double)token[key];
  public static bool B(this JToken token, string key) => token?[key]?.Type == JTokenType.Boolean && (bool)token[key];
  public static List<string> Strings(this JToken token, string key) => (token?[key] as JArray)?.Select(v => v.Type == JTokenType.String ? (string)v : null).Where(v => !string.IsNullOrEmpty(v)).ToList() ?? new List<string>();
  public static List<JObject> Objects(this JToken token, string key) => (token?[key] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();

  public static TextBlock Text(string text, string style = "Blastlog.Body", string brush = null, FontWeight? weight = null) {
    var t = Kit.Text(text, style);
    if (brush != null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
    if (weight != null) t.FontWeight = weight.Value;
    return t;
  }
  public static Run Run(string text, string brush = null, FontWeight? weight = null, bool italic = false) {
    var r = new Run(text);
    if (brush != null) r.SetResourceReference(TextElement.ForegroundProperty, brush);
    if (weight != null) r.FontWeight = weight.Value;
    if (italic) r.FontStyle = FontStyles.Italic;
    return r;
  }
  public static TextBlock Line(string style, params Inline[] inlines) { var t = Kit.Text("", style); t.Inlines.Clear(); t.Inlines.AddRange(inlines); return t; }
  /// <summary>Cuts text after <paramref name="lines"/> lines with an ellipsis (the site's line clamp).</summary>
  public static TextBlock Clamp(this TextBlock t, int lines) {
    var height = Math.Round(t.FontSize * 1.42);
    t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; t.LineHeight = height; t.MaxHeight = height * lines; t.TextTrimming = TextTrimming.CharacterEllipsis;
    return t;
  }

  /// <summary>A button whose look is its content (a cover, a title): keyboard focus shows Blastlog's ring, 44px or more.</summary>
  public static Button Bare(UIElement content, Action onClick, string name) {
    var b = new Button { Content = content, Template = BareTemplate, Cursor = System.Windows.Input.Cursors.Hand, FocusVisualStyle = Kit.Style("Blastlog.FocusRing"),
      HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    AutomationProperties.SetName(b, name);
    b.Click += (s, e) => { try { onClick(); } catch (Exception ex) { Log.Error(ex, "Blastlog: " + name); } };
    return b;
  }
  static ControlTemplate bare;
  static ControlTemplate BareTemplate { get {
    if (bare != null) return bare;
    var t = new ControlTemplate(typeof(Button));
    var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
    var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
    border.AppendChild(presenter); t.VisualTree = border; t.Seal();
    return bare = t;
  } }

  /// <summary>A Kit-styled button with any content (an icon and a word), busy while its task runs.</summary>
  public static Button Btn(object content, Func<Task> onClick, string style = "Blastlog.Primary", string name = null) {
    var b = new Button { Content = content, Style = Kit.Style(style) };
    if (name != null) AutomationProperties.SetName(b, name);
    b.Click += async (s, e) => { b.IsEnabled = false; try { await onClick(); } catch (Exception ex) { Log.Error(ex, "Blastlog button " + (name ?? content?.ToString())); } finally { b.IsEnabled = true; } };
    return b;
  }
  public static Button Btn(string label, Func<Task> onClick, string style = "Blastlog.Primary") => Btn((object)label, onClick, style, label);
  public static object IconWord(string svg, string word, double size = 16) {
    var row = new StackPanel { Orientation = Orientation.Horizontal };
    var icon = Icons.Make(svg, size); icon.Margin = new Thickness(0, 0, 7, 0); icon.VerticalAlignment = VerticalAlignment.Center;
    row.Children.Add(icon); row.Children.Add(new TextBlock { Text = word, VerticalAlignment = VerticalAlignment.Center });
    return row;
  }
  public const string Heart = "<path d=\"M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z\" />";
  public const string Check = "<path d=\"M20 6 9 17l-5-5\" />";
  public const string X = "<path d=\"M18 6 6 18\" /><path d=\"m6 6 12 12\" />";

  /// <summary>
  /// A cover that fills its column at 3:4 (or a fixed width), loaded from Blastlog's art (IGDB) and cached on disk; the
  /// gamepad tile when there's none (design.md 9).
  /// </summary>
  public static Border Art(BlastlogPlugin plugin, string gameId, string coverUrl, CornerRadius radius, double? width = null) {
    var host = new CoverBox { CornerRadius = radius, ClipToBounds = true };
    host.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
    if (width != null) { host.Width = width.Value; host.Height = width.Value * 4 / 3; }
    var size = Math.Min(44, (width ?? 160) * 0.3);
    var icon = Icons.Make(Icons.Gamepad, size, new SolidColorBrush()); icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
    ((Shape)((Canvas)((Viewbox)icon).Child).Children[0]).SetResourceReference(Shape.StrokeProperty, "Blastlog.GameText");
    host.Child = icon;
    LoadArt(plugin, Artwork.Url(new BlastlogGame { Id = gameId, CoverUrl = coverUrl }, width != null && width < 120 ? ArtworkContext.Thumbnail : ArtworkContext.Cover), host, width);
    return host;
  }

  // A page asks for a hundred or so pictures at once (each card's cover, and the same cover again for its colour).
  // .NET allows only two connections per host, so all at once most of them hit the download timeout and the cards stay
  // blank and green. Each picture is fetched once, at most four at a time, top of the page first; a failed one is tried
  // again the next time it's asked for.
  static readonly SemaphoreSlim Downloads = new SemaphoreSlim(4, 4);
  static readonly Dictionary<string, Task<string>> Fetches = new Dictionary<string, Task<string>>();
  public static Task<string> Picture(BlastlogPlugin plugin, string url) {
    if (url == null || plugin.Theme == null) return Task.FromResult<string>(null);
    lock (Fetches) {
      if (Fetches.TryGetValue(url, out var known) && !(known.IsCompleted && known.Result == null)) return known;
      return Fetches[url] = Fetch(plugin, url);
    }
  }
  static async Task<string> Fetch(BlastlogPlugin plugin, string url) {
    await Downloads.WaitAsync().ConfigureAwait(false);
    try { return await plugin.Theme.CachedFile(url).ConfigureAwait(false); }
    catch (Exception e) { Log.Warn("Blastlog: picture didn't load: " + e.Message); return null; }
    finally { Downloads.Release(); }
  }

  static async void LoadArt(BlastlogPlugin plugin, string url, Border host, double? width) {
    if (url == null || plugin.Theme == null) return;
    try {
      var file = await Picture(plugin, url);
      if (file == null) return;
      var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(file);
      image.DecodePixelWidth = (int)Math.Min(800, (width ?? 300) * 2); image.EndInit(); image.Freeze();
      host.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
      host.Child = null;
    } catch (Exception e) { Log.Warn("Blastlog: cover didn't load: " + e.Message); }
  }

  /// <summary>A game-tinted card wearing its own game (Discover and Play next cards, design.md 8).</summary>
  public static Border GameCard(BlastlogPlugin plugin, string gameId, string coverUrl, UIElement child, double radius = 18) {
    var card = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), Child = child, SnapsToDevicePixels = true };
    card.Res(Border.BackgroundProperty, "Blastlog.GameTint").Res(Border.BorderBrushProperty, "Blastlog.GameEdge");
    Wear(card, plugin, new BlastlogGame { Id = gameId, CoverUrl = coverUrl });
    return card;
  }
  /// <summary>The card's own game colour once its cover is on disk (through the shared queue above), neutral meanwhile.</summary>
  static async void Wear(FrameworkElement card, BlastlogPlugin plugin, BlastlogGame game) {
    LibraryTile.Neutral(card);
    try {
      await Picture(plugin, Artwork.Url(game, ArtworkContext.Cover));
      Kit.GameScope(card, plugin, game);
    } catch (Exception e) { Log.Warn("Blastlog: card colour: " + e.Message); }
  }

  /// <summary>A small pill over cover art: the page colour under it, mostly opaque, so it reads on any cover.</summary>
  public static FrameworkElement OverPill(string text, string brush, HorizontalAlignment h, VerticalAlignment v) {
    var grid = new Grid { HorizontalAlignment = h, VerticalAlignment = v, Margin = new Thickness(10), IsHitTestVisible = false };
    grid.Children.Add(new Border { CornerRadius = new CornerRadius(999), Opacity = 0.84 }.Res(Border.BackgroundProperty, "Blastlog.Bg"));
    var label = Text(text, "Blastlog.Small", brush ?? "Blastlog.Text", FontWeights.ExtraBold);
    grid.Children.Add(new Border { CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(1), Padding = new Thickness(10, 3, 10, 4), Child = label }.Res(Border.BorderBrushProperty, "Blastlog.Line"));
    return grid;
  }
  /// <summary>The rank on a Play next cover, in the card's game colour.</summary>
  public static FrameworkElement Rank(int rank) {
    var t = Text(rank.ToString(CultureInfo.InvariantCulture), "Blastlog.Body", "Blastlog.OnGame", FontWeights.ExtraBold);
    t.FontSize = 15; t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
    return new Border { MinWidth = 34, Height = 34, Padding = new Thickness(9, 0, 9, 0), CornerRadius = new CornerRadius(17), Child = t, Margin = new Thickness(10),
      HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false }.Res(Border.BackgroundProperty, "Blastlog.Game");
  }

  /// <summary>A segmented control (design.md 7): the picked option filled with the page's game colour.</summary>
  public static FrameworkElement Segmented(string label, IEnumerable<Tuple<string, string, string>> options, string current, Action<string> pick) {
    var row = new StackPanel { Orientation = Orientation.Horizontal };
    foreach (var o in options) {
      var active = o.Item1 == current; var key = o.Item1;
      var b = Kit.Button(o.Item2, () => pick(key), active ? "Blastlog.Primary" : "Blastlog.TextButton");
      b.MinHeight = 40; b.Margin = new Thickness(0, 0, 4, 0); b.Padding = new Thickness(14, 0, 14, 0);
      if (!active) b.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text");
      if (o.Item3 != null) b.ToolTip = o.Item3;
      AutomationProperties.SetItemStatus(b, active ? "Selected" : "");
      row.Children.Add(b);
    }
    var box = new Border { Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(4, 4, 0, 4), Margin = new Thickness(0, 0, 10, 10), HorizontalAlignment = HorizontalAlignment.Left }
      .Res(Border.BackgroundProperty, "Blastlog.Surface2");
    AutomationProperties.SetName(box, label);
    return box;
  }

  /// <summary>Tends to love chips sit in the page's tint; Tends to dislike chips are dashed (the approved board).</summary>
  public static FrameworkElement TasteChip(string text, bool love, string tip) {
    var label = Text(text, "Blastlog.Small", "Blastlog.Text", FontWeights.Bold);
    if (love) { var chip = Kit.Chip(text); ((TextBlock)chip.Child).FontWeight = FontWeights.Bold; chip.ToolTip = tip; return chip; }
    var grid = new Grid { Margin = new Thickness(0, 0, 8, 8), ToolTip = tip, HorizontalAlignment = HorizontalAlignment.Left };
    grid.Children.Add(new Rectangle { RadiusX = 15, RadiusY = 15, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } }
      .Res(Shape.StrokeProperty, "Blastlog.Edge").Res(Shape.FillProperty, "Blastlog.Surface2"));
    label.Margin = new Thickness(12, 6, 12, 7); grid.Children.Add(label);
    return grid;
  }

  /// <summary>The result of an action, with Undo when it can be undone (design.md 7 "Toasts"), and ✕ to put it away.</summary>
  public static Border Notice(string text, IEnumerable<Button> buttons, Action dismiss) {
    var row = new DockPanel();
    var close = Kit.Button("✕", dismiss, "Blastlog.TextButton"); AutomationProperties.SetName(close, "Dismiss"); close.Margin = new Thickness(0);
    DockPanel.SetDock(close, Dock.Right); row.Children.Add(close);
    var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    foreach (var b in buttons.Where(b => b != null)) { b.MinHeight = 40; actions.Children.Add(b); }
    DockPanel.SetDock(actions, Dock.Right); row.Children.Add(actions);
    var words = Text(text, "Blastlog.Body"); words.FontSize = 15; words.VerticalAlignment = VerticalAlignment.Center; words.Margin = new Thickness(0, 0, 12, 0);
    row.Children.Add(words);
    return new Border { Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 6, 6, 6), Margin = new Thickness(0, 0, 0, 16), BorderThickness = new Thickness(1) }
      .Res(Border.BackgroundProperty, "Blastlog.GameTint").Res(Border.BorderBrushProperty, "Blastlog.GameEdge");
  }

  /// <summary>Play (installed) or Install for a game the player has in Playnite; null when it isn't in Playnite.</summary>
  public static Button PlayOrInstall(BlastlogPlugin plugin, string entryId, bool primary = true) {
    var game = string.IsNullOrEmpty(entryId) ? null : plugin.GameFor(entryId);
    if (game == null || !Dash.CanLaunch(plugin, game)) return null; // nothing this PC can launch or install
    var id = game.Id;
    return game.IsInstalled ? Kit.Button("Play", () => plugin.Api.StartGame(id), primary ? "Blastlog.Primary" : "Blastlog.Ghost")
      : Kit.Button("Install", () => plugin.Api.InstallGame(id), primary ? "Blastlog.Primary" : "Blastlog.Ghost");
  }

  /// <summary>A Blastlog entry's page: Playnite's own game page when it's paired, else Blastlog's.</summary>
  public static void OpenEntry(BlastlogPlugin plugin, string entryId) => Nav.Entry(plugin, entryId);

  /// <summary>"~8h", "~8h main story" from another level, "~8h (estimate)", "No end" (domain lengthText).</summary>
  public static string LengthText(JToken length) {
    if (length == null || length.Type != JTokenType.Object) return null;
    if (length.B("noEnd")) return "No end";
    var raw = length.D("hours"); if (raw == null) return null;
    var hours = raw < 10 ? Math.Round(raw.Value * 2) / 2 : Math.Round(raw.Value);
    var text = hours == 0 ? "<0.5h" : "~" + hours.ToString("0.#", CultureInfo.InvariantCulture) + "h";
    var level = length.S("level"); var from = length.S("fromLevel");
    if (from != null && from != level) text += " " + LevelLabel(from);
    return length.B("estimate") ? text + " (estimate)" : text;
  }
  public static string LevelLabel(string level) => level == "main_story" ? "main story" : level == "completionist" ? "completionist" : "main + extras";

  /// <summary>"Out Oct 23 (in 10 days)" when it's close, "Out Jun 2027" when it's far off (the site's releaseLabel).</summary>
  public static string ReleaseLabel(string date) {
    if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var release)) return "Not out yet";
    var days = (int)Math.Round((release.AddHours(12) - DateTime.Now).TotalDays);
    if (days <= 0) return "Out now";
    if (days <= 60) return $"Out {release.ToString("MMM d", CultureInfo.CurrentCulture)} (in {(days == 1 ? "1 day" : days + " days")})";
    return $"Out {release.ToString("MMM yyyy", CultureInfo.CurrentCulture)}";
  }
  /// <summary>Review lines are spans of a longer review; one that starts mid-sentence gets a leading ellipsis.</summary>
  public static string Quoted(string text) => "“" + (text.Length > 0 && char.IsLower(text[0]) ? "…" : "") + text + "”";

  /// <summary>A quote with the site's left rule in the card's colour.</summary>
  public static FrameworkElement Quote(string quote, string cite, int lines = 0) {
    var t = Line("Blastlog.Body", Run(Quoted(quote), italic: true), Run(" — " + cite, "Blastlog.TextMuted"));
    t.FontSize = 14; if (lines > 0) t.Clamp(lines);
    return new Border { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2), Child = t }.Res(Border.BorderBrushProperty, "Blastlog.GameEdge");
  }

  /// <summary>A ▸ / ▾ disclosure row (design.md 7), 44px.</summary>
  public static FrameworkElement Disclosure(string label, Func<UIElement> body, bool open = false) {
    var panel = new StackPanel();
    var head = Text("", "Blastlog.Body", null, FontWeights.SemiBold); head.FontSize = 15;
    var button = Bare(head, () => { }, label); button.MinHeight = 44;
    var content = new ContentControl { Visibility = Visibility.Collapsed };
    void Set(bool o) { open = o; head.Text = (o ? "▾ " : "▸ ") + label; content.Visibility = o ? Visibility.Visible : Visibility.Collapsed; AutomationProperties.SetItemStatus(button, o ? "Expanded" : "Collapsed"); if (o && content.Content == null) content.Content = body(); }
    button.Click += (s, e) => { try { Set(!open); } catch (Exception ex) { Log.Error(ex, "Blastlog disclosure"); } };
    Set(open);
    panel.Children.Add(button); panel.Children.Add(content);
    return panel;
  }

  public static T FindAncestor<T>(DependencyObject start) where T : DependencyObject {
    for (var d = start == null ? null : VisualTreeHelper.GetParent(start); d != null; d = VisualTreeHelper.GetParent(d)) if (d is T t) return t;
    return null;
  }
}

/// <summary>
/// A 3:4 cover that takes its height from the width it's given in the same layout pass. Setting the height afterwards
/// (on SizeChanged) left every new card flat for one pass, which shrank the page and threw the scroll position back up.
/// </summary>
sealed class CoverBox : Border {
  protected override Size MeasureOverride(Size constraint) {
    if (!double.IsNaN(Width) || double.IsInfinity(constraint.Width)) return base.MeasureOverride(constraint);
    var size = new Size(constraint.Width, constraint.Width * 4 / 3);
    base.MeasureOverride(size);
    return size;
  }
}

/// <summary>
/// The card grid (design.md 8): columns between a minimum and a maximum width, as many as fit. Masonry stacks each card
/// under the shortest column, so a short card's neighbour moves up (Play next); otherwise cards sit in rows of even
/// height (Discover). A Wide card (Play next's #1 and Sharpen your picks) takes two columns, three from seven up, the
/// whole width on a narrow page, and stacks down the left.
/// </summary>
public sealed class CardGrid : Panel {
  public double MinColumn { get; set; } = 220;
  /// <summary>Play next: columns grow with the page, 170 to 280px (the site's clamp(170px, 10vw, 280px)).</summary>
  public bool Scaled { get; set; }
  public double Gap { get; set; } = 16;
  public bool Masonry { get; set; }
  /// <summary>Fewer cards than columns stretch to fill the row (CSS auto-fit), like the site's You're playing strips.</summary>
  public bool Fit { get; set; }
  public static readonly DependencyProperty WideProperty = DependencyProperty.RegisterAttached("Wide", typeof(bool), typeof(CardGrid),
    new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
  public static void SetWide(UIElement element, bool value) => element.SetValue(WideProperty, value);
  public static bool GetWide(UIElement element) => (bool)element.GetValue(WideProperty);

  Rect[] rects = new Rect[0]; double laidWidth = -1, height;
  public int Columns { get; private set; } = 1;

  protected override Size MeasureOverride(Size available) {
    var w = double.IsInfinity(available.Width) ? 1200 : available.Width;
    Layout(w, true);
    return new Size(w, height);
  }
  protected override Size ArrangeOverride(Size final) {
    if (Math.Abs(final.Width - laidWidth) > 0.5 || rects.Length != InternalChildren.Count) Layout(final.Width, true);
    for (var i = 0; i < InternalChildren.Count; i++) InternalChildren[i].Arrange(rects[i]);
    return new Size(final.Width, height);
  }

  void Layout(double w, bool measure) {
    laidWidth = w;
    var min = Scaled ? Math.Max(170, Math.Min(280, w * 0.125)) : MinColumn;
    var cols = Math.Max(1, (int)Math.Floor((w + Gap) / (min + Gap)));
    if (Fit) cols = Math.Max(1, Math.Min(cols, InternalChildren.Count));
    Columns = cols;
    var cw = (w - Gap * (cols - 1)) / cols;
    var span = w < 780 ? cols : Math.Min(cols, cols >= 7 ? 3 : 2);
    var count = InternalChildren.Count; rects = new Rect[count];
    double Width(int s) => cw * s + Gap * (s - 1);
    if (Masonry) {
      var tops = new double[cols];
      for (var i = 0; i < count; i++) {
        var child = InternalChildren[i]; var wide = GetWide(child); var s = wide ? span : 1;
        var width = Width(s);
        if (measure) child.Measure(new Size(width, double.PositiveInfinity));
        var h = child.DesiredSize.Height;
        int col; double top;
        if (wide) { col = 0; top = tops.Take(s).Max(); }
        else { col = 0; for (var c = 1; c < cols; c++) if (tops[c] < tops[col] - 0.5) col = c; top = tops[col]; }
        rects[i] = new Rect(col * (cw + Gap), top, width, h);
        for (var c = col; c < col + s; c++) tops[c] = top + h + Gap;
      }
      height = count == 0 ? 0 : Math.Max(0, tops.Max() - Gap);
      return;
    }
    var row = new List<int>(); double rowTop = 0, rowHeight = 0; var colAt = 0;
    void Close() { foreach (var j in row) rects[j].Height = rowHeight; rowTop += rowHeight + Gap; rowHeight = 0; colAt = 0; row.Clear(); }
    for (var i = 0; i < count; i++) {
      var child = InternalChildren[i]; var s = GetWide(child) ? span : 1;
      if (colAt + s > cols && row.Count > 0) Close();
      var width = Width(s);
      if (measure) child.Measure(new Size(width, double.PositiveInfinity));
      rects[i] = new Rect(colAt * (cw + Gap), rowTop, width, child.DesiredSize.Height);
      rowHeight = Math.Max(rowHeight, child.DesiredSize.Height); colAt += s; row.Add(i);
    }
    if (row.Count > 0) Close();
    height = Math.Max(0, rowTop - Gap);
  }
}
}
