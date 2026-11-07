using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>A player on Compare (the site's ComparePlayer) with their colour already checked for this mode.</summary>
sealed class ComparePerson {
  public string Id, Name, Colour; public JToken J;
  public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1);
  public Brush Brush => Hex(Colour);
}

/// <summary>Compare's charts, drawn on WPF canvases from the site's own numbers (Compare.tsx).</summary>
static class CompareCharts {
  public static string MatchWord(double v) => v >= 75 ? "Very alike" : v >= 50 ? "Alike" : v >= 25 ? "Different" : "Very different";

  /// <summary>A person's mark: their colour, their initial (never colour alone).</summary>
  public static Border Avatar(ComparePerson p, double size = 28) {
    var t = new TextBlock { Text = p.Initial, FontSize = Math.Max(14, size * .42), FontWeight = FontWeights.ExtraBold, Foreground = Hex(InkOn(p.Colour)),
      HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    t.SetResourceReference(TextBlock.FontFamilyProperty, "Blastlog.Font");
    var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = p.Brush, Child = t, VerticalAlignment = VerticalAlignment.Center };
    AutomationProperties.SetName(b, p.Name);
    return b;
  }
  static TextBlock Label(string text, double size, string brush = "Blastlog.Text", FontWeight? weight = null) {
    var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.SemiBold };
    t.SetResourceReference(TextBlock.FontFamilyProperty, "Blastlog.Font");
    if (brush != null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
    return t;
  }
  static void Place(Canvas c, UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); c.Children.Add(e); }
  /// <summary>Puts text centred (anchor middle), starting (start) or ending (end) at x, vertically centred on y.</summary>
  static void Text(Canvas c, TextBlock t, double x, double y, string anchor = "middle") {
    t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    var w = t.DesiredSize.Width; var h = t.DesiredSize.Height;
    Place(c, t, anchor == "middle" ? x - w / 2 : anchor == "end" ? x - w : x, y - h / 2);
  }
  static Line Ln(double x1, double y1, double x2, double y2, string brush, double width = 1, double opacity = 1) {
    var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = width, Opacity = opacity, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    l.SetResourceReference(Shape.StrokeProperty, brush);
    return l;
  }

  /// <summary>Everyone in a ring, a line per pair (thicker = closer match); click a line to explain that pair.</summary>
  public static FrameworkElement Constellation(List<ComparePerson> people, List<JToken> scored, Func<JToken, bool> isSelected, JToken focus, Action<JToken> select) {
    const double size = 360, centre = size / 2, radius = size / 2 - 54;
    var c = new Canvas { Width = size, Height = size };
    var spots = people.Select((p, i) => { var a = -Math.PI / 2 + i * 2 * Math.PI / people.Count; return new { p.Id, x = centre + radius * Math.Cos(a), y = centre + radius * Math.Sin(a) }; }).ToDictionary(s => s.Id);
    var scores = scored.Select(p => D(p, "score") ?? 0).ToList();
    var min = scores.DefaultIfEmpty(0).Min(); var max = scores.DefaultIfEmpty(0).Max();
    double Strength(double s) => max > min ? (s - min) / (max - min) : .5;
    var anySelected = scored.Any(isSelected);
    foreach (var pair in scored) {
      if (!spots.TryGetValue(S(pair, "a"), out var a) || !spots.TryGetValue(S(pair, "b"), out var b)) continue;
      var on = isSelected(pair); var st = Strength(D(pair, "score") ?? 0);
      var line = Ln(a.x, a.y, b.x, b.y, on ? "Blastlog.Text" : "Blastlog.Game", 2 + st * 9, on ? .95 : anySelected ? .18 + st * .3 : .3 + st * .5);
      var hit = new Line { X1 = a.x, Y1 = a.y, X2 = b.x, Y2 = b.y, Stroke = Brushes.Transparent, StrokeThickness = 22, Cursor = Cursors.Hand };
      var captured = pair;
      hit.MouseLeftButtonUp += (s, e) => { try { select(captured); } catch (Exception) { } };
      c.Children.Add(line); c.Children.Add(hit);
    }
    foreach (var p in people) {
      var at = spots[p.Id]; var dim = focus != null && p.Id != S(focus, "a") && p.Id != S(focus, "b");
      var dot = new Ellipse { Width = 52, Height = 52, Fill = p.Brush, StrokeThickness = 5, Opacity = dim ? .45 : 1, IsHitTestVisible = false };
      dot.SetResourceReference(Shape.StrokeProperty, "Blastlog.Bg");
      Place(c, dot, at.x - 26, at.y - 26);
      var initial = new TextBlock { Text = p.Initial, FontSize = 20, FontWeight = FontWeights.ExtraBold, Foreground = Hex(InkOn(p.Colour)), Opacity = dim ? .45 : 1, IsHitTestVisible = false };
      initial.SetResourceReference(TextBlock.FontFamilyProperty, "Blastlog.Font");
      Text(c, initial, at.x, at.y);
      var name = Label(p.Name, 15); name.Opacity = dim ? .45 : 1; name.IsHitTestVisible = false;
      Text(c, name, at.x, at.y + (at.y > centre + 1 ? 46 : at.y < centre - 1 ? -42 : 46));
    }
    AutomationProperties.SetName(c, "Matches between players");
    return c;
  }

  /// <summary>The match on one bar: four zones of 25%, deeper toward Very alike, a marker at the match.</summary>
  public static FrameworkElement MatchScale(double value) {
    var steps = new[] { "Very different", "Different", "Alike", "Very alike" };
    var grid = new Grid { Margin = new Thickness(0, 12, 0, 6) };
    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    var bar = new Grid(); var labels = new Grid { Margin = new Thickness(0, 6, 0, 0) };
    for (var i = 0; i < 4; i++) {
      bar.ColumnDefinitions.Add(new ColumnDefinition()); labels.ColumnDefinitions.Add(new ColumnDefinition());
      var zone = new Border { Opacity = .12 + i * .2, Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0), CornerRadius = new CornerRadius(i == 0 ? 7 : 0, i == 3 ? 7 : 0, i == 3 ? 7 : 0, i == 0 ? 7 : 0) };
      zone.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game"); Grid.SetColumn(zone, i); bar.Children.Add(zone);
      var l = T(steps[i], "Blastlog.Small"); l.HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == 3 ? HorizontalAlignment.Right : HorizontalAlignment.Center; Grid.SetColumn(l, i); labels.Children.Add(l);
    }
    var mark = new Border { Width = 4, Height = 22, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, -4, 0, -4) };
    mark.SetResourceReference(Border.BackgroundProperty, "Blastlog.Text");
    bar.Children.Add(mark); Grid.SetColumnSpan(mark, 4);
    bar.SizeChanged += (s, e) => mark.Margin = new Thickness(Math.Max(0, Math.Min(100, value)) / 100 * e.NewSize.Width - 2, -4, 0, -4);
    Grid.SetRow(labels, 1);
    grid.Children.Add(bar); grid.Children.Add(labels);
    AutomationProperties.SetName(grid, $"Match {value:0}%, {MatchWord(value)}");
    return grid;
  }

  /// <summary>Two players' stars on one chart: on the line you agree; shape and colour say who rated it higher.</summary>
  public static FrameworkElement Scatter(List<(JToken game, double a, double b)> games, ComparePerson a, ComparePerson b, Action<JToken> lookup) {
    const double w = 520, h = 336, pad = 36, padB = 54;
    double X(double r) => pad + (r - .5) / 4.5 * (w - pad * 2);
    double Y(double r) => h - padB - (r - .5) / 4.5 * (h - pad - padB);
    var c = new Canvas { Width = w, Height = h };
    var band = new Polygon { Points = new PointCollection { new Point(X(.5), Y(1.5)), new Point(X(.5), Y(.5)), new Point(X(1.5), Y(.5)), new Point(X(5), Y(4)), new Point(X(5), Y(5)), new Point(X(4), Y(5)) } };
    band.SetResourceReference(Shape.FillProperty, "Blastlog.GameTint"); c.Children.Add(band);
    for (var r = 1; r <= 5; r++) {
      c.Children.Add(Ln(X(r), Y(.5), X(r), Y(5), "Blastlog.Line")); c.Children.Add(Ln(X(.5), Y(r), X(5), Y(r), "Blastlog.Line"));
      Text(c, Label(r.ToString(), 14, "Blastlog.TextMuted"), X(r), h - padB + 22); Text(c, Label(r.ToString(), 14, "Blastlog.TextMuted"), 14, Y(r));
    }
    var diagonal = Ln(X(.5), Y(.5), X(5), Y(5), "Blastlog.TextMuted", 1.5); diagonal.StrokeDashArray = new DoubleCollection { 4, 4 }; c.Children.Add(diagonal);
    var ax = Label(a.Name + "’s rating →", 14, null, FontWeights.Bold); ax.Foreground = a.Brush; Text(c, ax, w - pad, h - 12, "end");
    var bx = Label("↑ " + b.Name + "’s rating", 14, null, FontWeights.Bold); bx.Foreground = b.Brush; Text(c, bx, 4, pad - 18, "start");
    var note = T("Hover or tap a mark for its games.", "Blastlog.Muted");
    var noteGames = new WrapPanel();
    void Show(List<(JToken game, double a, double b)> group, bool open) {
      var first = group[0];
      note.Inlines.Clear();
      note.Inlines.Add(new System.Windows.Documents.Run(group.Count == 1 ? S(first.game, "title") : group.Count + " games") { FontWeight = FontWeights.Bold });
      note.Inlines.Add(new System.Windows.Documents.Run(" · "));
      note.Inlines.Add(new System.Windows.Documents.Run($"{a.Name} {Stars(first.a)}") { Foreground = a.Brush });
      note.Inlines.Add(new System.Windows.Documents.Run(" · "));
      note.Inlines.Add(new System.Windows.Documents.Run($"{b.Name} {Stars(first.b)}") { Foreground = b.Brush });
      if (!open) return;
      noteGames.Children.Clear();
      if (group.Count == 1) lookup(first.game);
      else foreach (var g in group) { var game = g.game; noteGames.Children.Add(Link(S(game, "title"), () => lookup(game))); }
    }
    var spots = games.GroupBy(g => $"{g.a}:{g.b}").Select(x => x.ToList());
    foreach (var group in spots) {
      var g = group[0]; var gap = Math.Abs(g.a - g.b); var n = group.Count;
      var r = n > 1 ? 9 + Math.Sqrt(n) * 3 : 6;
      Shape mark;
      if (gap < .75) { mark = new Rectangle { Width = r * 1.6, Height = r * 1.6, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(45) }; mark.SetResourceReference(Shape.FillProperty, "Blastlog.Text"); }
      else if (g.a > g.b) mark = new Ellipse { Width = r * 2, Height = r * 2, Fill = a.Brush };
      else mark = new Rectangle { Width = r * 1.8, Height = r * 1.8, RadiusX = 3, RadiusY = 3, Fill = b.Brush };
      mark.Cursor = Cursors.Hand; mark.Opacity = .9;
      var captured = group;
      mark.MouseEnter += (s, e) => { try { Show(captured, false); mark.Opacity = 1; } catch (Exception) { } };
      mark.MouseLeave += (s, e) => mark.Opacity = .9;
      mark.MouseLeftButtonUp += (s, e) => { try { Show(captured, true); } catch (Exception) { } };
      Place(c, mark, X(g.a) - mark.Width / 2, Y(g.b) - mark.Height / 2);
      if (n > 1) { var count = Label(n.ToString(), 14, null, FontWeights.ExtraBold); count.Foreground = Hex(InkOn(gap < .75 ? "#f4f4f6" : g.a > g.b ? a.Colour : b.Colour)); count.IsHitTestVisible = false; Text(c, count, X(g.a), Y(g.b)); }
    }
    var within = games.Count(g => Math.Abs(g.a - g.b) <= 1);
    AutomationProperties.SetName(c, $"Ratings by {a.Name} and {b.Name}: {games.Count} games, {within} within 1★");
    var key = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
    UIElement Key(Shape s, string text, string small) { s.Width = 12; s.Height = 12; s.Margin = new Thickness(0, 0, 8, 0); s.VerticalAlignment = VerticalAlignment.Center;
      return Row(s, Runs("Blastlog.Small", (text, false, "Blastlog.Text"), (small == null ? "" : " " + small, false, null))).Also(x => x.Margin = new Thickness(0, 0, 0, 4)); }
    var same = new Rectangle { RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(45) }; same.SetResourceReference(Shape.FillProperty, "Blastlog.Text");
    key.Children.Add(Key(same, "Same rating", "(on the dashed line)"));
    key.Children.Add(Key(new Ellipse { Fill = a.Brush }, $"{a.Name} rated it higher", "(below the line)"));
    key.Children.Add(Key(new Rectangle { Fill = b.Brush, RadiusX = 2, RadiusY = 2 }, $"{b.Name} rated it higher", "(above the line)"));
    var bandKey = new Rectangle(); bandKey.SetResourceReference(Shape.FillProperty, "Blastlog.GameTint");
    key.Children.Add(Key(bandKey, "Shaded band: within 1★, close enough to count as agreeing", null));
    key.Children.Add(Runs("Blastlog.Small", ("3", true, "Blastlog.Text"), ("  A number and a bigger mark: that many games at the same spot", false, null)));
    var view = new Viewbox { Child = c, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = w };
    return Stack(null, view, note, noteGames, key);
  }

  /// <summary>One row per game: a ½–5★ line with a dot per person who rated it.</summary>
  public static FrameworkElement Strips(BlastlogPlugin plugin, List<JToken> games, List<ComparePerson> people, bool all, Action toggleAll, Action<JToken> lookup) {
    var panel = new StackPanel();
    double Pos(double r) => (r - .5) / 4.5;
    foreach (var g in all ? games : games.Take(10).ToList()) {
      var ratings = g["ratings"] as JObject ?? new JObject();
      var raters = people.Where(p => ratings[p.Id] != null).ToList(); var values = raters.Select(p => (double)ratings[p.Id]).ToList();
      if (values.Count == 0) continue;
      var lo = values.Min(); var hi = values.Max();
      var row = new Grid();
      row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); row.ColumnDefinitions.Add(new ColumnDefinition());
      var cover = Cover(plugin, S(g, "coverUrl"), S(g, "title"), 40, 6); cover.VerticalAlignment = VerticalAlignment.Center;
      var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
      body.Children.Add(Runs("Blastlog.Body", (S(g, "title"), true, null), (hi - lo > 0 ? "  " + Stars(hi - lo) + " apart" : "", false, "Blastlog.TextMuted")));
      var track = new Grid { Height = 26, Margin = new Thickness(0, 6, 0, 0) };
      var canvas = new Canvas();
      track.Children.Add(canvas);
      track.SizeChanged += (s, e) => {
        canvas.Children.Clear(); var W = e.NewSize.Width - 20;
        for (var r = 1; r <= 5; r++) Place(canvas, new Rectangle { Width = 1, Height = 12, Fill = Brush("Blastlog.Line") }, 10 + Pos(r) * W, 7);
        var span = new Rectangle { Width = Math.Max(0, (Pos(hi) - Pos(lo)) * W), Height = 6, RadiusX = 3, RadiusY = 3, Fill = Brush("Blastlog.Edge") }; Place(canvas, span, 10 + Pos(lo) * W, 10);
        foreach (var p in raters) {
          var v = (double)ratings[p.Id]; var sameStars = raters.Where(o => (double)ratings[o.Id] == v).ToList(); var k = sameStars.IndexOf(p);
          var dot = Avatar(p, 22); Place(canvas, dot, 10 + Pos(v) * W + (k - (sameStars.Count - 1) / 2.0) * 14 - 11, 2);
        }
      };
      body.Children.Add(track);
      Grid.SetColumn(body, 1); row.Children.Add(cover); row.Children.Add(body);
      var b = Plain(row, () => lookup(g), S(g, "title") + ": " + string.Join(", ", raters.Select(p => $"{p.Name} {Stars((double)ratings[p.Id])}")));
      b.HorizontalContentAlignment = HorizontalAlignment.Stretch; b.Margin = new Thickness(0, 0, 0, 10);
      b.ToolTip = string.Join(", ", raters.Select(p => $"{p.Name} {Stars((double)ratings[p.Id])}"));
      panel.Children.Add(b);
    }
    var scale = new Grid { Margin = new Thickness(52, 0, 0, 6), Height = 20 };
    var sc = new Canvas(); scale.Children.Add(sc);
    scale.SizeChanged += (s, e) => { sc.Children.Clear(); var W = e.NewSize.Width - 20; for (var r = 1; r <= 5; r++) Text(sc, Label(r + "★", 14, "Blastlog.TextMuted"), 10 + Pos(r) * W, 10); };
    panel.Children.Insert(0, scale);
    if (games.Count > 10) panel.Children.Add(Kit.Button(all ? "Show the top 10" : $"Show all {games.Count}", toggleAll, "Blastlog.Ghost").Also(x => x.HorizontalAlignment = HorizontalAlignment.Left));
    return panel;
  }

  /// <summary>One line per trait, a dot per person: the bar shows how far apart they are.</summary>
  public static FrameworkElement Dumbbells(JArray rows, List<ComparePerson> people, string title, string empty) {
    var panel = new StackPanel();
    panel.Children.Add(T(title, "Blastlog.CardTitle").Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
    var ends = new DockPanel { Margin = new Thickness(170, 0, 0, 4) }; var right = T("love it", "Blastlog.Small"); DockPanel.SetDock(right, Dock.Right); ends.Children.Add(right); ends.Children.Add(T("not for them", "Blastlog.Small"));
    panel.Children.Add(ends);
    if (rows.Count == 0) { panel.Children.Add(T(empty, "Blastlog.Muted")); return panel; }
    var reach = Math.Max(.4, rows.SelectMany(r => ((r["values"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()).Select(p => Math.Abs((double)p.Value))).DefaultIfEmpty(0).Max()) * 1.1;
    double Pos(double v) => (Math.Max(-reach, Math.Min(reach, v)) + reach) / (2 * reach);
    foreach (var r in rows) {
      var values = r["values"] as JObject ?? new JObject();
      double V(ComparePerson p) => values[p.Id] == null ? 0 : (double)values[p.Id];
      var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
      row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); row.ColumnDefinitions.Add(new ColumnDefinition());
      var label = T(S(r, "label"), "Blastlog.Body", null, 15); label.VerticalAlignment = VerticalAlignment.Center; label.TextTrimming = TextTrimming.CharacterEllipsis; label.TextWrapping = TextWrapping.NoWrap;
      var track = new Grid { Height = 26 }; var canvas = new Canvas(); track.Children.Add(canvas);
      track.SizeChanged += (s, e) => {
        canvas.Children.Clear(); var W = e.NewSize.Width - 24;
        Place(canvas, new Rectangle { Width = 1, Height = 18, Fill = Brush("Blastlog.Edge") }, 12 + Pos(0) * W, 4);
        var at = people.Select(p => Pos(V(p))).ToList(); var lo = at.Min(); var hi = at.Max();
        Place(canvas, new Rectangle { Width = Math.Max(0, (hi - lo) * W), Height = 6, RadiusX = 3, RadiusY = 3, Fill = Brush("Blastlog.GameBar") }, 12 + lo * W, 10);
        for (var i = 0; i < people.Count; i++) Place(canvas, Avatar(people[i], 22), 12 + at[i] * W - 11, 2);
      };
      track.ToolTip = string.Join(", ", people.Select(p => $"{p.Name} {(V(p) > 0 ? "+" : "")}{V(p):0.##}"));
      AutomationProperties.SetName(track, S(r, "label") + ": " + track.ToolTip);
      Grid.SetColumn(track, 1); row.Children.Add(label); row.Children.Add(track);
      panel.Children.Add(row);
    }
    return panel;
  }

  /// <summary>Everyone on two chosen playstyle axes, zoomed to the group; names placed where they clear the dots.</summary>
  public static FrameworkElement TasteMap(List<ComparePerson> people, string xKey, string yKey, (string key, string left, string right)[] axes) {
    const double s = 420, pad = 44;
    var xa = axes.First(a => a.key == xKey); var ya = axes.First(a => a.key == yKey);
    double Axis(ComparePerson p, string key) => D(p.J["axes"], key) ?? 0;
    double Reach(string key) => Math.Max(.15, people.Select(p => Math.Abs(Axis(p, key))).DefaultIfEmpty(0).Max()) * 1.2;
    double Spot(double v, string key) => pad + (v / Reach(key) + 1) / 2 * (s - pad * 2);
    var c = new Canvas { Width = s, Height = s };
    var frame = new Rectangle { Width = s - pad * 2, Height = s - pad * 2, RadiusX = 18, RadiusY = 18, StrokeThickness = 1 };
    frame.SetResourceReference(Shape.FillProperty, "Blastlog.Surface2"); frame.SetResourceReference(Shape.StrokeProperty, "Blastlog.Line");
    Place(c, frame, pad, pad);
    c.Children.Add(Ln(s / 2, pad, s / 2, s - pad, "Blastlog.Line")); c.Children.Add(Ln(pad, s / 2, s - pad, s / 2, "Blastlog.Line"));
    Text(c, Label("← " + xa.left, 14, "Blastlog.TextMuted"), pad, s - pad + 24, "start"); Text(c, Label(xa.right + " →", 14, "Blastlog.TextMuted"), s - pad, s - pad + 24, "end");
    Text(c, Label("↑ " + ya.right, 14, "Blastlog.TextMuted"), s / 2, pad - 16); Text(c, Label("↓ " + ya.left, 14, "Blastlog.TextMuted"), s / 2, s - pad + 24);
    var dots = people.Select(p => new { p, cx = Spot(Axis(p, xKey), xKey), cy = s - Spot(Axis(p, yKey), yKey) }).ToList();
    // The axis names count as taken space, so a player's name never sits on top of one.
    var boxes = new List<Rect> { new Rect(pad, s - pad + 14, s - pad * 2, 20), new Rect(s / 2 - 70, pad - 26, 140, 20) };
    bool Hits(Rect r) => boxes.Any(o => o.IntersectsWith(r)) || dots.Any(d => new Rect(d.cx - 17, d.cy - 17, 34, 34).IntersectsWith(r));
    foreach (var d in dots) {
      var dot = new Ellipse { Width = 34, Height = 34, Fill = d.p.Brush, StrokeThickness = 4 }; dot.SetResourceReference(Shape.StrokeProperty, "Blastlog.Bg");
      Place(c, dot, d.cx - 17, d.cy - 17);
      var initial = Label(d.p.Initial, 15, null, FontWeights.ExtraBold); initial.Foreground = Hex(InkOn(d.p.Colour)); Text(c, initial, d.cx, d.cy);
    }
    foreach (var d in dots) {
      var name = Label(d.p.Name, 15, "Blastlog.Text", FontWeights.Bold);
      name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
      var wide = name.DesiredSize.Width + 4; var high = name.DesiredSize.Height;
      var options = new[] { (x: 0.0, y: 34.0, anchor: "middle"), (x: 0.0, y: -28.0, anchor: "middle"), (x: 24.0, y: 0.0, anchor: "start"), (x: -24.0, y: 0.0, anchor: "end") };
      Rect Box((double x, double y, string anchor) o) { var left = d.cx + o.x - (o.anchor == "middle" ? wide / 2 : o.anchor == "end" ? wide : 0); return new Rect(left, d.cy + o.y - high / 2, wide, high); }
      var pick = options.FirstOrDefault(o => !Hits(Box(o))); if (pick.anchor == null) pick = options[0];
      boxes.Add(Box(pick));
      Text(c, name, d.cx + pick.x, d.cy + pick.y, pick.anchor);
    }
    AutomationProperties.SetName(c, $"Taste map: {xa.left} to {xa.right} across, {ya.left} to {ya.right} up");
    return new Viewbox { Child = c, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = s, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
  }

  /// <summary>A person's ratings as bars on one shared scale, with a count on every bar.</summary>
  public static FrameworkElement Histogram(ComparePerson p, int[] bars, int peak, bool halves) {
    var grid = new Grid { Height = 120, Margin = new Thickness(0, 14, 0, 10) };
    var words = new List<string>();
    for (var i = 0; i < bars.Length; i++) {
      grid.ColumnDefinitions.Add(new ColumnDefinition());
      var n = bars[i]; var label = (halves ? (i + 1) / 2.0 : i + 1) + "★";
      words.Add($"{label} {Plural(n, "game")}");
      var col = new Grid { Margin = new Thickness(2, 0, 2, 0) };
      col.RowDefinitions.Add(new RowDefinition()); col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      var bar = new Border { VerticalAlignment = VerticalAlignment.Bottom, Height = Math.Max(4, (double)n / peak * 86), CornerRadius = new CornerRadius(5, 5, 2, 2), Background = p.Brush, Opacity = n > 0 ? .9 : .15 };
      var count = Label(n.ToString(), 14, null, FontWeights.Bold); count.Foreground = Hex(InkOn(p.Colour)); count.HorizontalAlignment = HorizontalAlignment.Center; count.VerticalAlignment = VerticalAlignment.Top;
      if (n > 0 && bar.Height >= 20) bar.Child = count;
      var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
      if (n > 0 && bar.Height < 20) { var above = Label(n.ToString(), 14, "Blastlog.Text", FontWeights.Bold); above.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(above); }
      stack.Children.Add(bar);
      var tick = T(label, "Blastlog.Small"); tick.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(tick, 1);
      col.Children.Add(stack); col.Children.Add(tick);
      Grid.SetColumn(col, i); grid.Children.Add(col);
    }
    AutomationProperties.SetName(grid, $"{p.Name}'s ratings: " + string.Join(", ", words));
    return grid;
  }
}
}
