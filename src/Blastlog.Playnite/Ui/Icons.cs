using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Blastlog.Playnite.Ui {
/// <summary>
/// The site's icons (apps/web/src/Icons.tsx: 24x24, stroke 2, round caps and joins, currentColor) drawn in WPF. Paste the
/// SVG's inner elements as they are: path, rect, circle, line, polyline and polygon are understood. The stroke follows
/// the element's Foreground, like currentColor.
/// </summary>
public static class Icons {
  public const string Dashboard = "<rect width=\"7\" height=\"9\" x=\"3\" y=\"3\" rx=\"1\" /><rect width=\"7\" height=\"5\" x=\"14\" y=\"3\" rx=\"1\" /><rect width=\"7\" height=\"9\" x=\"14\" y=\"12\" rx=\"1\" /><rect width=\"7\" height=\"5\" x=\"3\" y=\"16\" rx=\"1\" />";
  public const string Star = "<polygon points=\"12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2\" />";
  public const string Library = "<path d=\"m16 6 4 14\" /><path d=\"M12 6v14\" /><path d=\"M8 8v12\" /><path d=\"M4 4v16\" />";
  public const string Compare = "<circle cx=\"5\" cy=\"6\" r=\"3\" /><path d=\"M12 6h5a2 2 0 0 1 2 2v7\" /><path d=\"m15 9-3-3 3-3\" /><circle cx=\"19\" cy=\"18\" r=\"3\" /><path d=\"M12 18H7a2 2 0 0 1-2-2V9\" /><path d=\"m9 15 3 3-3 3\" />";
  public const string Gamepad = "<line x1=\"6\" x2=\"10\" y1=\"12\" y2=\"12\" /><line x1=\"8\" x2=\"8\" y1=\"10\" y2=\"14\" /><line x1=\"15\" x2=\"15.01\" y1=\"13\" y2=\"13\" /><line x1=\"18\" x2=\"18.01\" y1=\"11\" y2=\"11\" /><rect width=\"20\" height=\"12\" x=\"2\" y=\"6\" rx=\"6\" />";
  public const string Discover ="<circle cx=\"12\" cy=\"12\" r=\"10\" /><polygon points=\"16.24 7.76 14.12 14.12 7.76 16.24 9.88 9.88 16.24 7.76\" />";

  static readonly Regex Element = new Regex(@"<(path|rect|circle|line|polyline|polygon)\b([^>]*)/?>", RegexOptions.IgnoreCase);
  static readonly Regex Attribute = new Regex("([a-zA-Z][a-zA-Z0-9-]*)\\s*=\\s*\"([^\"]*)\"");

  /// <summary>An icon as a 24x24 geometry, for use as a <see cref="Path"/>'s data.</summary>
  public static Geometry Geometry(string svg) {
    var group = new GeometryGroup { FillRule = FillRule.Nonzero };
    foreach (Match m in Element.Matches(svg ?? "")) {
      var a = Attribute.Matches(m.Groups[2].Value).Cast<Match>().ToDictionary(x => x.Groups[1].Value.ToLowerInvariant(), x => x.Groups[2].Value);
      double N(string key, double fallback = 0) => a.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
      switch (m.Groups[1].Value.ToLowerInvariant()) {
        case "path": if (a.TryGetValue("d", out var d1)) group.Children.Add(System.Windows.Media.Geometry.Parse(d1)); break;
        case "rect": { var rx = N("rx", N("ry")); group.Children.Add(new RectangleGeometry(new Rect(N("x"), N("y"), N("width"), N("height")), rx, N("ry", rx))); break; }
        case "circle": group.Children.Add(new EllipseGeometry(new Point(N("cx"), N("cy")), N("r"), N("r"))); break;
        case "line": group.Children.Add(new LineGeometry(new Point(N("x1"), N("y1")), new Point(N("x2"), N("y2")))); break;
        case "polyline": case "polygon": {
          var nums = Regex.Split(a.TryGetValue("points", out var p) ? p.Trim() : "", @"[\s,]+").Where(s => s.Length > 0)
            .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
          if (nums.Length < 4) break;
          var figure = new PathFigure { StartPoint = new Point(nums[0], nums[1]), IsClosed = m.Groups[1].Value.ToLowerInvariant() == "polygon" };
          for (var i = 2; i + 1 < nums.Length; i += 2) figure.Segments.Add(new LineSegment(new Point(nums[i], nums[i + 1]), true));
          group.Children.Add(new PathGeometry(new[] { figure }));
          break;
        }
      }
    }
    group.Freeze();
    return group;
  }

  /// <summary>The icon at <paramref name="size"/> pixels, stroked in its parent's Foreground (or <paramref name="brush"/>).</summary>
  public static FrameworkElement Make(string svg, double size = 18, Brush brush = null) {
    var path = new Path { Data = Geometry(svg), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
      StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.None, SnapsToDevicePixels = false };
    if (brush != null) path.Stroke = brush;
    else path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") {
      RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1) });
    var canvas = new Canvas { Width = 24, Height = 24, Children = { path } };
    return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
  }
}
}
