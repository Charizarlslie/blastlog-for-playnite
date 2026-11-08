using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// The cards every game page shares under the take (web GameMore.tsx): Accessibility, and "Trailers and screenshots"
/// (platforms, genres, the trailers, six screenshots, IGDB's credit). Screenshots open in the media viewer (Nav.Media),
/// which steps through the trailers and every screenshot as the site's does; a trailer plays on YouTube in the browser,
/// only when picked.
/// <paramref name="info"/> is the game's info as the mirror sends it (games/:id/info).
/// </summary>
static class GameMore {
  static readonly ILogger Log = LogManager.GetLogger();
  const int ShotsShown = 6;

  /// <summary>Accessibility: the features the developer lists on the Steam store page, as plain chips; null when none.</summary>
  public static FrameworkElement Accessibility(JObject info, string title) {
    var features = info.Strings("accessibility");
    if (features.Count == 0) return null;
    var card = DiscoverGameView.Card("Accessibility", out var body);
    var w = new WrapPanel(); foreach (var f in features) w.Children.Add(Kit.Chip(f, neutral: true).Also(c => c.Margin = new Thickness(0, 0, 8, 8)));
    AutomationProperties.SetName(w, "Accessibility features listed for " + title);
    body.Children.Add(w);
    body.Children.Add(PnKit.Text("From the Steam store, as listed by the developer.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 4, 0, 0)));
    return card;
  }

  /// <summary>Platforms and genres, the trailers and screenshots; null when there's none of it.</summary>
  public static FrameworkElement Media(BlastlogPlugin plugin, JObject info, string title) {
    var platforms = info.Strings("platforms"); var genres = info.Strings("genres");
    var shots = info.Strings("screenshots").Where(DiscoverGameView.IsIgdb).ToList();
    var videos = info.Objects("videos").Where(v => !string.IsNullOrEmpty(v.S("youtubeId"))).ToList();
    if (videos.Count == 0 && shots.Count == 0 && platforms.Count == 0) return null;
    var items = MediaSequence.Items(videos.Select(v => (v.S("youtubeId"), v.S("name"))), shots);
    var poster = shots.FirstOrDefault();
    void View(int start) => Nav.Media(plugin, title, items, start, poster);

    var card = DiscoverGameView.Card(videos.Count > 0 || shots.Count > 0 ? "Trailers and screenshots" : "Where to play it", out var body);
    if (platforms.Count > 0) body.Children.Add(PnKit.Line("Blastlog.Body", PnKit.Run("On ", "Blastlog.TextMuted"), PnKit.Run(string.Join(", ", platforms))).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 4); }));
    if (genres.Count > 0) body.Children.Add(PnKit.Line("Blastlog.Body", PnKit.Run("Genres ", "Blastlog.TextMuted"), PnKit.Run(string.Join(", ", genres))).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 4); }));
    if (videos.Count > 0) {
      var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
      for (var i = 0; i < videos.Count; i++) {
        // Playnite has no video player, so a trailer goes straight to YouTube in the browser; the viewer still steps
        // through them (with the same link) between screenshots.
        var v = videos[i]; var url = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(v.S("youtubeId"));
        var label = i == 0 ? $"▶ {v.S("name") ?? "Trailer"} · plays on YouTube ↗" : (v.S("name") ?? "Video") + " ↗";
        row.Children.Add(Kit.Button(label, () => UiKit.Open(url), i == 0 ? "Blastlog.Primary" : "Blastlog.Ghost").Also(b => { b.Margin = new Thickness(0, 0, 8, 8); AutomationProperties.SetName(b, "Play " + (v.S("name") ?? "the trailer") + " on YouTube"); }));
      }
      body.Children.Add(row);
    }
    if (shots.Count > 0) {
      var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
      for (var i = 0; i < Math.Min(ShotsShown, shots.Count); i++) {
        var more = i == ShotsShown - 1 ? shots.Count - ShotsShown : 0; var at = videos.Count + i;
        row.Children.Add(Shot(plugin, shots[i], more, i + 1, () => View(at)));
      }
      body.Children.Add(row);
    }
    body.Children.Add(PnKit.Text("Game details, screenshots and trailers from IGDB.com.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
    return card;
  }

  static FrameworkElement Shot(BlastlogPlugin plugin, string url, int more, int n, Action open) {
    var box = new Border { Width = 220, Height = 124, CornerRadius = new CornerRadius(14), ClipToBounds = true }.Res(Border.BackgroundProperty, "Blastlog.Surface2");
    var layers = new Grid(); layers.Children.Add(box);
    if (more > 0) layers.Children.Add(new Border { CornerRadius = new CornerRadius(14), Child = PnKit.Text("+" + more, "Blastlog.Section").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0); }) }
      .Also(b => b.Background = new SolidColorBrush(Tint.Scrim(0.55)).Also(x => x.Freeze())));
    var button = PnKit.Bare(layers, open, more > 0 ? $"Show screenshot {n} larger, {more} more after it" : $"Show screenshot {n} larger");
    button.Width = 220; button.Margin = new Thickness(0, 0, 10, 10); button.HorizontalAlignment = HorizontalAlignment.Left; button.Cursor = Cursors.Hand;
    LoadShot(plugin, Artwork.NormalizedLegacyUrl(url, "screenshot_med"), box);
    return button;
  }

  static async void LoadShot(BlastlogPlugin plugin, string url, Border box) {
    try {
      var file = await PnKit.Picture(plugin, url);
      if (file == null) return;
      var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(file); image.DecodePixelWidth = 440; image.EndInit(); image.Freeze();
      box.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
    } catch (Exception e) { Log.Warn("Blastlog: screenshot didn't load: " + e.Message); }
  }
}
}
