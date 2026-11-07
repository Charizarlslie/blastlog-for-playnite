using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Blastlog.Sync.Core.Design;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Ui {
/// <summary>
/// A Blastlog screen in Playnite's sidebar. Implement it in its own file and it shows up: the plugin finds every
/// IBlastlogScreen in this assembly (no shared list to edit, so screens can be built in parallel). Order follows the
/// site's menu: Dashboard 10, Play next 30, Discover 40, Your taste 50, Compare 60.
/// </summary>
public interface IBlastlogScreen {
  string Title { get; }
  /// <summary>SVG inner elements from the site's Icons.tsx (see <see cref="Icons"/>).</summary>
  string Icon { get; }
  int Order { get; }
  /// <summary>Builds the view when the sidebar item opens. Keep it quick: show the cached copy first, then refresh.</summary>
  FrameworkElement Create(BlastlogPlugin plugin);
}

public static class Screens {
  public static IEnumerable<IBlastlogScreen> All() => typeof(Screens).Assembly.GetTypes()
    .Where(t => typeof(IBlastlogScreen).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
    .Select(t => (IBlastlogScreen)Activator.CreateInstance(t)).OrderBy(s => s.Order);

  public static SidebarItem SidebarItem(IBlastlogScreen screen, BlastlogPlugin plugin) => new SidebarItem {
    Title = screen.Title, Type = SiderbarItemType.View, Icon = Icons.Make(screen.Icon, 18),
    Opened = () => Kit.Page(screen.Create(plugin)),
  };
}

/// <summary>Shared helpers for building Blastlog screens in code (styles live in Kit.xaml).</summary>
public static class Kit {
  static ResourceDictionary styles;
  /// <summary>Kit.xaml, loaded once.</summary>
  public static ResourceDictionary Styles => styles ?? (styles = new ResourceDictionary { Source = new Uri("pack://application:,,,/Blastlog.Playnite;component/Ui/Kit.xaml", UriKind.Absolute) });
  public static Style Style(string key) => (Style)Styles[key];

  /// <summary>A screen's root: Blastlog's page background, Kit styles in scope, the site's page padding, scrolling.</summary>
  public static Control Page(FrameworkElement content) {
    var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(40, 34, 40, 40) };
    var root = new Border { Child = scroll };
    root.Resources.MergedDictionaries.Add(Styles);
    root.SetResourceReference(Border.BackgroundProperty, "Blastlog.Bg");
    return new UserControl { Content = root };
  }

  public static TextBlock Text(string text, string style = "Blastlog.Body") => new TextBlock { Text = text, Style = Style(style) };
  public static Button Button(string label, Action onClick, string style = "Blastlog.Primary") => Button(label, () => { onClick(); return Task.CompletedTask; }, style);
  /// <summary>A button that runs <paramref name="onClick"/> once at a time (disabled while busy).</summary>
  public static Button Button(string label, Func<Task> onClick, string style = "Blastlog.Primary") {
    var b = new Button { Content = label, Style = Style(style) };
    b.Click += async (s, e) => { b.IsEnabled = false; try { await onClick(); } catch (Exception ex) { LogManager.GetLogger().Error(ex, "Blastlog button " + label); } finally { b.IsEnabled = true; } };
    return b;
  }
  public static Border Chip(string text, bool neutral = false) => new Border { Style = Style(neutral ? "Blastlog.NeutralChip" : "Blastlog.Chip"), Child = Text(text, "Blastlog.Small").Also(t => t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text")) };
  public static Border Card(UIElement child, bool game = false) => new Border { Style = Style(game ? "Blastlog.GameCard" : "Blastlog.Card"), Child = child };

  /// <summary>Stars in the star colour plus the number (design.md 7), e.g. "★ 4.5".</summary>
  public static TextBlock Stars(double? rating, double size = 15) {
    var t = new TextBlock { FontSize = size, FontWeight = FontWeights.Bold };
    t.SetResourceReference(TextBlock.FontFamilyProperty, "Blastlog.Font");
    if (rating == null) { t.Text = "No stars yet"; t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.TextMuted"); return t; }
    t.Text = "★ " + (Math.Round(rating.Value * 2) / 2).ToString("0.#", CultureInfo.InvariantCulture);
    t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Star");
    return t;
  }

  static readonly Dictionary<string, string[]> StatusWords = new Dictionary<string, string[]> {
    // status → word, icon (design.md 7: an icon plus a word for every status, fixed colours).
    ["done"] = new[] { "Done", "<path d=\"M20 6 9 17l-5-5\" />" },
    ["playing"] = new[] { "Playing", "<polygon points=\"6 3 20 12 6 21 6 3\" />" },
    ["started"] = new[] { "Started", "<circle cx=\"12\" cy=\"12\" r=\"9\" /><path d=\"M12 7v5l3 2\" />" },
    ["not_started"] = new[] { "Not started", "<circle cx=\"12\" cy=\"12\" r=\"9\" />" },
    ["set_aside"] = new[] { "Set aside", "<rect x=\"6\" y=\"4\" width=\"4\" height=\"16\" rx=\"1\" /><rect x=\"14\" y=\"4\" width=\"4\" height=\"16\" rx=\"1\" />" },
    ["quit"] = new[] { "Quit", "<path d=\"M18 6 6 18\" /><path d=\"m6 6 12 12\" />" },
    ["dnf"] = new[] { "Didn't finish", "<path d=\"M5 12h14\" />" },
    ["unplayable"] = new[] { "Unplayable", "<circle cx=\"12\" cy=\"12\" r=\"9\" /><path d=\"m5 5 14 14\" />" },
    ["wishlist"] = new[] { "Wishlist", "<path d=\"M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z\" />" },
  };
  /// <summary>A status pill: icon plus word in fixed colours, never the game's (design.md 2.3).</summary>
  public static Border StatusPill(string status) {
    StatusWords.TryGetValue(status ?? "", out var w);
    var word = w?[0] ?? (status ?? "").Replace('_', ' ');
    var brushKey = status == "done" ? "Blastlog.Ok" : status == "quit" || status == "dnf" || status == "unplayable" ? "Blastlog.Warn" : "Blastlog.Text";
    var row = new StackPanel { Orientation = Orientation.Horizontal };
    var label = Text(word, "Blastlog.Small"); label.FontWeight = FontWeights.SemiBold; label.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
    if (w != null) { var icon = Icons.Make(w[1], 14, null); icon.Margin = new Thickness(0, 0, 6, 0); icon.VerticalAlignment = VerticalAlignment.Center;
      ((System.Windows.Shapes.Path)((Canvas)((Viewbox)icon).Child).Children[0]).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey); row.Children.Add(icon); }
    row.Children.Add(label);
    var pill = new Border { Child = row, CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 4, 12, 4), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
    pill.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2"); pill.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    return pill;
  }

  /// <summary>Sets <paramref name="element"/>'s own game colours (design.md 2.3 "every card wears its own game").</summary>
  public static void GameScope(FrameworkElement element, string seed) {
    var t = GameColour.For(seed, Mode.Dark);
    void Set(string key, string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); element.Resources[key] = b; }
    Set("Blastlog.Game", t.Game); Set("Blastlog.OnGame", t.OnGame); Set("Blastlog.GameText", t.GameText); Set("Blastlog.GameTint", t.GameTint);
    Set("Blastlog.GameTint2", t.GameTint2); Set("Blastlog.GameEdge", t.GameEdge); Set("Blastlog.GameBar", t.GameBar);
  }
  /// <summary>Wears a Blastlog game's colour once it's known (page colour, else its cover's), Blastlog green meanwhile.</summary>
  public static async void GameScope(FrameworkElement element, BlastlogPlugin plugin, BlastlogGame game) {
    GameScope(element, GameColour.BrandGreen);
    try { var seed = await plugin.Theme.SeedFor(game); GameScope(element, seed); } catch (Exception) { }
  }

  /// <summary>A cover or hero image from Blastlog (IGDB), cached on disk; a text tile with the gamepad if there's none (design.md 9).</summary>
  public static FrameworkElement Cover(BlastlogPlugin plugin, BlastlogGame game, double width, ArtworkContext context = ArtworkContext.Cover, double radius = 16) {
    var height = context == ArtworkContext.Hero || context == ArtworkContext.Backdrop ? width * 9 / 16 : width * 4 / 3;
    var host = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(radius), ClipToBounds = true };
    host.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
    var placeholder = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    placeholder.Children.Add(Icons.Make(Icons.Gamepad, Math.Min(40, width / 3), (Brush)Application.Current.TryFindResource("Blastlog.Brand")));
    host.Child = placeholder;
    Load(plugin, Artwork.Url(game, context), host, radius);
    return host;
  }
  static async void Load(BlastlogPlugin plugin, string url, Border host, double radius) {
    if (url == null) return;
    try {
      var file = await plugin.Theme.CachedFile(url);
      if (file == null) return;
      var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(file);
      image.DecodePixelWidth = (int)Math.Min(1600, host.Width * 2); image.EndInit(); image.Freeze();
      host.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
      host.Child = null;
      host.Clip = new RectangleGeometry(new Rect(0, 0, host.Width, host.Height), radius, radius);
    } catch (Exception) { }
  }

  public static T Also<T>(this T value, Action<T> act) { act(value); return value; }
}
}
