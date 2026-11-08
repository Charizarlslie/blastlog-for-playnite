using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Blastlog.Playnite.Screens;
using Blastlog.Sync.Core.Design;
using Playnite.SDK;

namespace Blastlog.Playnite.Ui {
/// <summary>
/// The game page's trailers and screenshots, one at a time, in a Playnite window (web MediaViewer.tsx): the count and
/// its label with ✕ on top, the item in the middle, Previous and Next either side. The arrow keys step, Home and End jump
/// to the ends, Escape, ✕ or a click beside the picture closes. A trailer shows a poster whose play button opens it on
/// YouTube in the browser (Playnite has no player); screenshots are IGDB's 1080p pictures, cached like covers, and the
/// neighbours load ahead so stepping shows them straight away.
/// </summary>
public static class MediaViewer {
  static readonly ILogger Log = LogManager.GetLogger();
  const string ChevronLeft = "<path d=\"m15 18-6-6 6-6\" />", ChevronRight = "<path d=\"m9 18 6-6-6-6\" />", Play = "<polygon points=\"6 3 20 12 6 21 6 3\" />";

  /// <summary>
  /// Opens the viewer at <paramref name="start"/>. <paramref name="poster"/> is the picture behind a trailer's play
  /// button (the first screenshot, as on the site), or null.
  /// </summary>
  public static void Open(BlastlogPlugin plugin, string title, List<MediaItem> items, int start, string poster) {
    if (items == null || items.Count == 0) return;
    try {
      var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
      var owner = plugin.Api.Dialogs.GetCurrentAppWindow();
      window.Title = title + ": trailers and screenshots";
      window.Width = Math.Max(760, (owner?.ActualWidth ?? 1400) * 0.92); window.Height = Math.Max(560, (owner?.ActualHeight ?? 900) * 0.92);
      window.Owner = owner; window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
      var view = new View(plugin, title, items, start, poster, window);
      window.Content = view;
      // Playnite's window takes the arrow keys for its own focus moves first, so listen for handled keys too.
      window.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((s, e) => { try { view.Key(e); } catch (Exception ex) { Log.Error(ex, "Blastlog media viewer key"); } }), true);
      window.ShowDialog();
    } catch (Exception e) { Log.Error(e, "Blastlog media viewer"); }
  }

  sealed class View : Border {
    readonly BlastlogPlugin plugin; readonly string title, poster; readonly List<MediaItem> items; readonly Window window;
    readonly TextBlock count = new TextBlock(), label = new TextBlock();
    readonly Border stage = new Border { Background = Brushes.Transparent };
    readonly Button prev, next, close;
    int index; Action fit;

    public View(BlastlogPlugin plugin, string title, List<MediaItem> items, int start, string poster, Window window) {
      this.plugin = plugin; this.title = title; this.items = items; this.poster = poster; this.window = window;
      index = Math.Min(Math.Max(0, start), items.Count - 1);
      Resources.MergedDictionaries.Add(Kit.Styles);
      // The site's scrim at 96% over the page, whatever the window's own background.
      Background = new SolidColorBrush(Tint.Scrim(0.96)).Also(b => b.Freeze());
      Padding = new Thickness(16, 12, 16, 16);

      var grid = new Grid();
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
      Child = grid;

      var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = true };
      close = Round(PnKit.X, "Close", "Close (Esc)", () => window.Close());
      DockPanel.SetDock(close, Dock.Right); top.Children.Add(close);
      var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
      count.Style = Kit.Style("Blastlog.Body"); count.FontWeight = FontWeights.Bold; count.Margin = new Thickness(0, 0, 10, 0);
      label.Style = Kit.Style("Blastlog.Muted"); label.TextTrimming = TextTrimming.CharacterEllipsis;
      words.Children.Add(count); words.Children.Add(label);
      top.Children.Add(words);
      Grid.SetColumnSpan(top, 3); grid.Children.Add(top);

      Grid.SetRow(stage, 1); Grid.SetColumn(stage, 1); grid.Children.Add(stage);
      stage.SizeChanged += (s, e) => { try { fit?.Invoke(); } catch (Exception) { } };
      // A click on the dark space beside the picture closes, as on the site.
      stage.MouseLeftButtonUp += (s, e) => { try { if (e.OriginalSource == stage) window.Close(); } catch (Exception ex) { Log.Error(ex, "Blastlog media viewer close"); } };

      prev = Round(ChevronLeft, "Previous", "Previous (←)", () => Go(index - 1)); prev.Margin = new Thickness(0, 0, 16, 0);
      next = Round(ChevronRight, "Next", "Next (→)", () => Go(index + 1)); next.Margin = new Thickness(16, 0, 0, 0);
      Grid.SetRow(prev, 1); Grid.SetRow(next, 1); Grid.SetColumn(next, 2);
      if (items.Count > 1) { grid.Children.Add(prev); grid.Children.Add(next); }

      Loaded += (s, e) => { try { close.Focus(); } catch (Exception) { } };
      Show();
    }

    /// <summary>The site's round 48px buttons: surface 2, an edge line, the icon in text colour.</summary>
    static Button Round(string svg, string name, string tip, Action onClick) {
      var face = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1) }.Res(Border.BackgroundProperty, "Blastlog.Surface2").Res(Border.BorderBrushProperty, "Blastlog.Edge");
      var icon = Icons.Make(svg, 24); icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
      face.Child = icon;
      var b = PnKit.Bare(face, onClick, name);
      b.Width = 48; b.Height = 48; b.VerticalAlignment = VerticalAlignment.Center; b.HorizontalAlignment = HorizontalAlignment.Center; b.ToolTip = tip;
      b.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text");
      return b;
    }

    public void Key(KeyEventArgs e) {
      if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; window.Close(); return; }
      if (Keyboard.Modifiers != ModifierKeys.None) return;
      var name = e.Key == System.Windows.Input.Key.Right ? "ArrowRight" : e.Key == System.Windows.Input.Key.Left ? "ArrowLeft" : e.Key == System.Windows.Input.Key.Home ? "Home" : e.Key == System.Windows.Input.Key.End ? "End" : null;
      var target = name != null ? MediaSequence.KeyTarget(name, index, items.Count) : null;
      if (target != null) { e.Handled = true; Go(target.Value); }
    }

    void Go(int target) {
      target = Math.Min(Math.Max(0, target), items.Count - 1);
      if (target == index) return;
      index = target; Show();
    }

    void Show() {
      var item = items[index]; var words = MediaSequence.Label(items, index);
      count.Text = items.Count > 1 ? $"{index + 1} of {items.Count}" : ""; count.Visibility = items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
      label.Text = words;
      prev.Opacity = index == 0 ? 0.4 : 1; next.Opacity = index == items.Count - 1 ? 0.4 : 1;
      prev.IsEnabled = index > 0; next.IsEnabled = index < items.Count - 1;
      fit = null;
      stage.Child = item.IsVideo ? Poster(item, words) : Shot(item, words);
      // The neighbouring screenshots load ahead.
      foreach (var near in new[] { index - 1, index + 1 }) if (near >= 0 && near < items.Count && !items[near].IsVideo) _ = PnKit.Picture(plugin, Full(items[near].Url));
    }

    static string Full(string url) => Artwork.NormalizedLegacyUrl(url, "1080p");

    FrameworkElement Shot(MediaItem item, string words) {
      var image = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
      AutomationProperties.SetName(image, $"{title}, {words.ToLowerInvariant()}");
      var frame = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = image };
      var holder = new Grid { Background = Brushes.Transparent };
      var waiting = PnKit.Text("Loading the picture…", "Blastlog.Muted").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
      holder.Children.Add(waiting); holder.Children.Add(frame);
      // Clicks on the picture don't close; the space around it does.
      holder.MouseLeftButtonUp += (s, e) => { try { if (e.OriginalSource == holder || e.OriginalSource == waiting) window.Close(); } catch (Exception) { } };
      Load(Full(item.Url), image, waiting, frame);
      return holder;
    }

    async void Load(string url, Image image, TextBlock waiting, Border frame) {
      try {
        var file = await PnKit.Picture(plugin, url);
        if (file == null) { waiting.Text = "This picture didn’t load."; return; }
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(file); bitmap.EndInit(); bitmap.Freeze();
        image.Source = bitmap;
        // Rounded corners hug the picture, not the window: size the frame to the picture as it fits.
        void Fit() {
          var w = stage.ActualWidth; var h = stage.ActualHeight; if (w <= 0 || h <= 0) return;
          var k = Math.Min(w / bitmap.PixelWidth, h / bitmap.PixelHeight);
          frame.Width = bitmap.PixelWidth * k; frame.Height = bitmap.PixelHeight * k;
          frame.Clip = new RectangleGeometry(new Rect(0, 0, frame.Width, frame.Height), 12, 12);
        }
        if (!ReferenceEquals(stage.Child, frame.Parent)) return; // stepped on while it loaded
        fit = Fit; Fit();
        waiting.Visibility = Visibility.Collapsed;
      } catch (Exception e) { Log.Warn("Blastlog: screenshot didn't load: " + e.Message); waiting.Text = "This picture didn’t load."; }
    }

    /// <summary>A trailer: the poster with the game-coloured play button; it opens on YouTube in the browser.</summary>
    FrameworkElement Poster(MediaItem item, string words) {
      var box = new Grid();
      var back = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true }.Res(Border.BackgroundProperty, "Blastlog.Bg");
      box.Children.Add(back);
      if (poster != null) LoadPoster(back);
      var circle = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(36), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, "Blastlog.Game");
      var icon = Icons.Make(Play, 30); icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center; icon.Margin = new Thickness(4, 0, 0, 0);
      circle.Child = icon; box.Children.Add(circle);
      var pill = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 10),
        Background = new SolidColorBrush(Tint.Scrim(0.72)).Also(b => b.Freeze()),
        Child = PnKit.Text(words + " · plays on YouTube ↗", "Blastlog.Small", "Blastlog.Text", FontWeights.Bold) };
      box.Children.Add(pill);
      var url = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(item.YoutubeId);
      var button = PnKit.Bare(box, () => UiKit.Open(url), "Play " + words + " on YouTube");
      button.SetResourceReference(Control.ForegroundProperty, "Blastlog.OnGame");
      button.HorizontalAlignment = HorizontalAlignment.Center; button.VerticalAlignment = VerticalAlignment.Center;
      // 16:9, as wide as fits.
      void Fit() { var w = stage.ActualWidth; var h = stage.ActualHeight; if (w <= 0 || h <= 0) return; var k = Math.Min(w / 16, h / 9); button.Width = 16 * k; button.Height = 9 * k; }
      fit = Fit; Fit();
      return button;
    }

    async void LoadPoster(Border back) {
      try {
        var file = await PnKit.Picture(plugin, Full(poster));
        if (file == null) return;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(file); bitmap.DecodePixelWidth = 1280; bitmap.EndInit(); bitmap.Freeze();
        back.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
      } catch (Exception e) { Log.Warn("Blastlog: trailer poster didn't load: " + e.Message); }
    }
  }
}
}
