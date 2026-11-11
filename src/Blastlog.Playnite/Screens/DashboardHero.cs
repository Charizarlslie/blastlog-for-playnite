using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// The dashboard's big card (design.md 8 "Top card"; web GameHero.tsx): the whole cover, sharp, on a blurred copy of
/// itself under a scrim with the game's glow, a label chip with a control to its right, the copy below, the cover on the
/// right with the status on its top-right corner. Shared by Next up and Now playing (which takes the top whenever something
/// is being played; Charlie, 2026-11-08: "now > later") and by friends' dashboards.
/// </summary>
static class DashHero {
  /// <summary>
  /// The whole card. <paramref name="scope"/> puts the game's colours on the card itself (Now playing and friends'
  /// cards; Next up wears the page instead). Clicking anywhere but a button opens the game.
  /// </summary>
  public static Border Frame(BlastlogPlugin plugin, BlastlogGame game, FrameworkElement top, FrameworkElement copy, string status, string coverName, Action open, bool scope) {
    var card = new Border { CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 18), MinHeight = 420 };
    card.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Blastlog.GameEdge");
    if (scope) Kit.GameScope(card, plugin, game);
    var layers = new Grid();
    // Children don't follow a Border's rounded corners: clip the art to them.
    layers.SizeChanged += (s, e) => { try { layers.Clip = new RectangleGeometry(new Rect(e.NewSize), 27, 27); } catch (Exception) { } };
    Backdrop(plugin, layers, game);
    var left = new StackPanel { Children = { top, copy } };
    layers.Children.Add(Content(left, game != null ? Cover(plugin, game, status, coverName) : null));
    card.Child = layers;
    if (open != null) Dash.OnClick(card, open);
    FadeIn(copy);
    return card;
  }

  /// <summary>The copy fading in when the game changes (the site crossfades; skipped when Windows turns animations off).</summary>
  public static void FadeIn(UIElement copy) {
    if (SystemParameters.ClientAreaAnimation) copy.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
  }

  /// <summary>Art, scrim and glow under the card's content.</summary>
  public static void Backdrop(BlastlogPlugin plugin, Grid layers, BlastlogGame game) {
    var art = Artwork.Url(game, ArtworkContext.Backdrop);
    if (art != null) {
      // A rectangle filled with the art takes the card's size instead of setting it (an Image would stretch the card to the cover's shape).
      // The blur comes from decoding the cover tiny and letting it stretch smoothly: a live BlurEffect this size is redrawn on
      // every scroll step and made the page crawl.
      var blurred = new Rectangle { Opacity = 0.9, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1.25, 1.25) };
      RenderOptions.SetBitmapScalingMode(blurred, BitmapScalingMode.Linear);
      Dash.LoadFill(plugin, art, blurred, 24);
      layers.Children.Add(blurred);
    }
    var scrim = new Rectangle { OpacityMask = new LinearGradientBrush(new GradientStopCollection {
      new GradientStop(Color.FromArgb(235, 0, 0, 0), 0), new GradientStop(Color.FromArgb(200, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(120, 0, 0, 0), 1) }, 0) };
    scrim.SetResourceReference(Shape.FillProperty, "Blastlog.Bg");
    layers.Children.Add(scrim);
    // The glow: the game colour fading out from the top left (design.md 2.2: dark 24% fading by 66% of the radius, light 10% by 42%).
    var light = ThemeBridge.CurrentMode == Mode.Light;
    var glow = new Rectangle { Width = 900, Height = 700, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-300, -280, 0, 0),
      IsHitTestVisible = false, OpacityMask = new RadialGradientBrush(new GradientStopCollection {
        new GradientStop(Color.FromArgb((byte)(light ? 26 : 61), 0, 0, 0), 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), light ? 0.42 : 0.66) }) };
    glow.SetResourceReference(Shape.FillProperty, "Blastlog.Game");
    layers.Children.Add(glow);
  }

  /// <summary>The copy on the left, the cover on the right.</summary>
  public static Grid Content(FrameworkElement left, FrameworkElement cover) {
    var content = new Grid { Margin = new Thickness(36, 30, 36, 34) };
    content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    content.Children.Add(left);
    if (cover != null) { Grid.SetColumn(cover, 1); content.Children.Add(cover); }
    return content;
  }

  /// <summary>The full cover with a shadow, its status on its top-right corner (the approved board).</summary>
  public static FrameworkElement Cover(BlastlogPlugin plugin, BlastlogGame game, string status, string name = null) {
    var cover = Kit.Cover(plugin, game, 260, ArtworkContext.Cover, 18);
    cover.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.45, Color = Colors.Black };
    cover.CacheMode = new BitmapCache(); // the shadow is drawn once, not on every scroll step
    System.Windows.Automation.AutomationProperties.SetName(cover, name ?? "Cover of " + game?.Title);
    // Top, not centred: with the start picker open the copy grows taller than the cover, which then stays put.
    var box = new Grid { Margin = new Thickness(32, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, Children = { cover } };
    if (status != null) {
      var pill = Kit.StatusPill(status);
      pill.HorizontalAlignment = HorizontalAlignment.Right; pill.VerticalAlignment = VerticalAlignment.Top; pill.Margin = new Thickness(12);
      box.Children.Add(pill);
    }
    return box;
  }

  /// <summary>The label chip at the top left ("Next up", "Now playing", "Just finished").</summary>
  public static Border Label(string text) => new Border { Style = Kit.Style("Blastlog.NeutralChip"), Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left,
    VerticalAlignment = VerticalAlignment.Center, Child = Dash.Text(text, "Blastlog.Small", "Blastlog.Text", FontWeights.Bold).Also(t => t.TextWrapping = TextWrapping.NoWrap) };

  /// <summary>
  /// The label with its control on the right (Next up's modes, Now playing's covers); when there isn't room for both
  /// beside the cover, the control goes under the label instead of being cut off.
  /// </summary>
  public static FrameworkElement TopRow(FrameworkElement label, FrameworkElement control) {
    var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.Children.Add(label);
    if (control == null) return grid;
    control.VerticalAlignment = VerticalAlignment.Center;
    Grid.SetColumn(control, 2); grid.Children.Add(control);
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.SizeChanged += (s, e) => {
      try {
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var under = e.NewSize.Width < label.DesiredSize.Width + control.DesiredSize.Width + 12;
        Grid.SetColumn(control, under ? 0 : 2); Grid.SetRow(control, under ? 1 : 0); Grid.SetColumnSpan(control, under ? 3 : 1);
        control.HorizontalAlignment = under ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        control.Margin = new Thickness(0, under ? 10 : 0, 0, 0);
      } catch (Exception ex) { global::Playnite.SDK.LogManager.GetLogger().Error(ex, "Blastlog dashboard: top row layout"); }
    };
    return grid;
  }

  public sealed class StripGame { public string Id; public BlastlogGame Game; public string Title; }

  /// <summary>
  /// "Also playing": every playing game as a 44×58 cover, the shown one ringed in its colour, and (own dashboard) a dashed
  /// + to start another. Picking a cover shows that game big.
  /// </summary>
  public static FrameworkElement Strip(BlastlogPlugin plugin, IList<StripGame> games, string shownId, Action<string> pick, Action add) {
    var row = new StackPanel { Orientation = Orientation.Horizontal };
    System.Windows.Automation.AutomationProperties.SetName(row, "Also playing");
    row.Children.Add(Dash.Text("Also playing", "Blastlog.Small", "Blastlog.Text", FontWeights.Bold).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 10, 0); t.TextWrapping = TextWrapping.NoWrap; }));
    foreach (var g in games) {
      var on = g.Id == shownId; var id = g.Id;
      var ring = new Border { Child = Kit.Cover(plugin, g.Game, 44, ArtworkContext.Thumbnail, 8), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), Padding = new Thickness(1) };
      if (on) { Kit.GameScope(ring, plugin, g.Game); ring.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Game"); } else ring.BorderBrush = Brushes.Transparent;
      var b = PnKit.Bare(ring, () => { if (!on) pick(id); }, on ? g.Title + " (shown)" : "Show " + g.Title);
      b.ToolTip = g.Title; b.Margin = new Thickness(0, 0, 4, 0); b.HorizontalAlignment = HorizontalAlignment.Left;
      row.Children.Add(b);
    }
    if (add != null) {
      var dashes = new Rectangle { RadiusX = 8, RadiusY = 8, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 } };
      dashes.SetResourceReference(Shape.StrokeProperty, "Blastlog.Edge");
      var plus = Dash.Text("+", "Blastlog.Body", "Blastlog.Text", FontWeights.Bold, 22).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
      var tile = new Grid { Width = 44, Height = 58, Margin = new Thickness(3), Background = Brushes.Transparent, Children = { dashes, plus } };
      var b = PnKit.Bare(tile, add, "Start another game"); b.ToolTip = "Start another game"; b.HorizontalAlignment = HorizontalAlignment.Left;
      row.Children.Add(b);
    }
    return row;
  }

  /// <summary>A chip with an icon in the hero's meta row (time on the list, length).</summary>
  public static Border Chip(string icon, string text, string tip = null) => Dash.IconChip(icon, Dash.Text(text, "Blastlog.Small", "Blastlog.Text", FontWeights.SemiBold), tip);

  /// <summary>Guides ↗ and Reddit ↗: plain search links, nothing fetched from those sites (docs/COMMERCIAL_READINESS.md).</summary>
  public static FrameworkElement Links(string title) => Dash.Wrap(
    Dash.LinkPill("Guides", "https://www.google.com/search?q=" + Uri.EscapeDataString(title + " guide"), $"Guides for {title} (opens your browser)").Margin(0, 0, 8, 0),
    Dash.LinkPill("Reddit", "https://www.reddit.com/search/?q=" + Uri.EscapeDataString(title), $"Reddit search for {title} (opens your browser)"));
}
}
