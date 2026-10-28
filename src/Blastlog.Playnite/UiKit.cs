using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Blastlog.Playnite {
/// <summary>
/// Small builders for the code-only WPF views. Colours and fonts come from the current Playnite theme's resources
/// (TextBrush, TextBrushDarker, …), so the views fit any theme; Blastlog green is only the fallback accent
/// (docs/design.md). Text never goes under 14px, buttons are 44px tall.
/// </summary>
public static class UiKit {
  public static readonly Brush Brand = new SolidColorBrush(Color.FromRgb(0x00, 0xE4, 0xA8));
  public static TextBlock Text(string text, bool muted = false, double size = 14, bool bold = false) {
    var t = new TextBlock { Text = text, FontSize = Math.Max(14, size), TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0, 2, 0, 2) };
    t.SetResourceReference(TextBlock.ForegroundProperty, muted ? "TextBrushDarker" : "TextBrush");
    return t;
  }
  public static void Heading(Panel panel, string text, double size = 18) { var h = Text(text, size: size, bold: true); h.Margin = new Thickness(0, 12, 0, 4); panel.Children.Add(h); }
  public static Button Button(string label, Action onClick) => Button(label, () => { onClick(); return Task.CompletedTask; });
  public static Button Button(string label, Func<Task> onClick) {
    var b = new Button { Content = label, MinHeight = 44, MinWidth = 44, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 8, 4), FontSize = 14 };
    b.Click += async (s, e) => { b.IsEnabled = false; try { await onClick(); } finally { b.IsEnabled = true; } };
    return b;
  }
  public static CheckBox Check(string label, bool value, Action<bool> changed) {
    var c = new CheckBox { Content = Text(label), IsChecked = value, Margin = new Thickness(0, 4, 0, 4), MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
    c.Checked += (s, e) => changed(true); c.Unchecked += (s, e) => changed(false);
    return c;
  }
  /// <summary>A card: a bordered panel on the theme's grid item background.</summary>
  public static Border Card(UIElement child) {
    var b = new Border { Child = child, Padding = new Thickness(16), Margin = new Thickness(0, 6, 0, 10), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
    b.SetResourceReference(Border.BackgroundProperty, "GridItemBackgroundBrush");
    b.SetResourceReference(Border.BorderBrushProperty, "NormalBorderBrush");
    return b;
  }
  /// <summary>A labelled share bar (the fingerprint card's bars): solid in the accent, its number beside it in words.</summary>
  public static UIElement ShareBar(string label, double share, Brush accent) {
    var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    var name = Text(label); Grid.SetColumn(name, 0); grid.Children.Add(name);
    var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
    track.SetResourceReference(Border.BackgroundProperty, "PanelSeparatorBrush");
    var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    var holder = new Grid(); holder.Children.Add(track); holder.Children.Add(bar);
    holder.SizeChanged += (s, e) => bar.Width = Math.Max(4, e.NewSize.Width * Math.Max(0, Math.Min(1, share)));
    Grid.SetColumn(holder, 1); grid.Children.Add(holder);
    return grid;
  }
  public static string Stars(double? rating) => rating == null ? "no rating" : (Math.Round(rating.Value * 2) / 2).ToString("0.#") + "★";
  public static void Open(string url) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { } }
}
}
