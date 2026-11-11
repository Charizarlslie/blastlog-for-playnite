using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Your picture and name at the top of Settings (the site's Your taste header: Change picture / Add a picture). Change
/// picture picks a file, crops the middle square and saves it at 512px as the site's PictureFramer does (JPEG under
/// 400 KB, PUT /sync/v1/me/picture with the site's body); Remove picture takes an uploaded one away. The site's framer
/// can also move and zoom the crop; here the middle square is it.
/// </summary>
static class SettingsPicture {
  const int Size = 512, MaxBytes = 400_000;
  static readonly Dictionary<string, string> Errors = new Dictionary<string, string> {
    ["picture_too_big"] = "That picture came out too big to save. Try another photo.",
    ["not_an_image"] = "That file isn't a JPEG, PNG or WebP picture.",
    ["invalid_picture"] = "That picture couldn't be read. Try another photo.",
    ["no_picture"] = "There's no uploaded picture to remove.",
  };

  /// <summary>The card. <paramref name="settings"/> null: the server can't change pictures yet, so only the face shows.</summary>
  public static Border Card(BlastlogPlugin plugin, JObject me, JObject settings, Action changed) {
    var name = settings.S("displayName") ?? me?["user"].S("displayName") ?? "You";
    var userId = me?["user"].S("id");
    var hasPicture = settings == null || settings.S("avatarUrl") != null;
    var face = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(36), Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
    face.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game");
    face.Child = T(name.Substring(0, 1).ToUpperInvariant(), "Blastlog.Body", "Blastlog.OnGame", 30, FontWeights.ExtraBold).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; });
    AutomationProperties.SetName(face, name);
    if (hasPicture && userId != null) Load(plugin, userId, face);

    var side = Stack(null, T(name, "Blastlog.CardTitle"));
    var error = new ContentControl();
    if (settings != null) {
      var uploaded = settings.B("uploadedPicture");
      var buttons = Wrap(Kit.Button(settings.S("avatarUrl") != null ? "Change picture…" : "Add a picture…", async () => {
        var file = plugin.Api.Dialogs.SelectFile("Pictures|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif");
        if (string.IsNullOrEmpty(file)) return;
        string image;
        // On the UI thread: WPF drawing wants it, and one 512px picture is quick.
        try { image = Encode(file); } catch (Exception e) { LogManagerWarn(e); error.Content = CollectionUi.Error(Errors["invalid_picture"]); return; }
        if (image == null) { error.Content = CollectionUi.Error(Errors["picture_too_big"]); return; }
        var (value, refusal) = await plugin.TryWrite("me/picture", new { image }, HttpMethod.Put);
        if (value == null) { error.Content = CollectionUi.Error(Words(refusal)); return; }
        changed();
      }, "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 10, 10, 0)));
      if (uploaded) buttons.Children.Add(Kit.Button("Remove picture", async () => {
        var (value, refusal) = await plugin.TryWrite("me/picture", new { }, HttpMethod.Delete);
        if (value == null) { error.Content = CollectionUi.Error(Words(refusal)); return; }
        changed();
      }, "Blastlog.TextButton").Also(b => b.Margin = new Thickness(0, 10, 10, 0)));
      side.Children.Add(buttons);
      side.Children.Add(error);
    }
    var row = new DockPanel();
    DockPanel.SetDock(face, Dock.Left); row.Children.Add(face); row.Children.Add(side);
    return Kit.Card(row).Also(c => c.Margin = new Thickness(0, 0, 0, 18));
  }

  static string Words(string error) => error != null && Errors.TryGetValue(error, out var w) ? w : BlastlogPlugin.RefusalWords(error);
  static void LogManagerWarn(Exception e) => global::Playnite.SDK.LogManager.GetLogger().Warn(e, "Blastlog: picture couldn't be read");

  static async void Load(BlastlogPlugin plugin, string userId, Border face) {
    try {
      var bytes = await plugin.PictureBytes(userId);
      if (bytes == null) return;
      var image = new BitmapImage();
      using (var stream = new MemoryStream(bytes)) {
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.DecodePixelWidth = 144; image.EndInit();
      }
      image.Freeze();
      face.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
      face.Child = null;
      face.Clip = new EllipseGeometry(new Point(36, 36), 36, 36);
    } catch (Exception e) { global::Playnite.SDK.LogManager.GetLogger().Warn(e, "Blastlog: picture didn't show"); }
  }

  /// <summary>
  /// The file's middle square at 512px on the site's dark backing (for transparent pictures), as base64 JPEG under
  /// 400 KB (the site's quality steps 0.88, 0.75, 0.6); null when even the last step is too big.
  /// </summary>
  static string Encode(string path) {
    var source = new BitmapImage();
    source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.UriSource = new Uri(path); source.EndInit(); source.Freeze();
    double w = source.PixelWidth, h = source.PixelHeight, side = Math.Min(w, h);
    var visual = new DrawingVisual();
    using (var g = visual.RenderOpen()) {
      g.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1d)), null, new Rect(0, 0, Size, Size));
      var scale = Size / side;
      g.DrawImage(source, new Rect(-(w - side) / 2 * scale, -(h - side) / 2 * scale, w * scale, h * scale));
    }
    var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    foreach (var quality in new[] { 88, 75, 60 }) {
      var encoder = new JpegBitmapEncoder { QualityLevel = quality };
      encoder.Frames.Add(BitmapFrame.Create(bitmap));
      using (var stream = new MemoryStream()) {
        encoder.Save(stream);
        if (stream.Length <= MaxBytes) return Convert.ToBase64String(stream.ToArray());
      }
    }
    return null;
  }
}
}
