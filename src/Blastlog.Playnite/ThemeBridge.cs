using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Blastlog.Sync.Core;
using Blastlog.Sync.Core.Design;
using Playnite.SDK;
using Game = Playnite.SDK.Models.Game;

namespace Blastlog.Playnite {
/// <summary>
/// Connects the plugin to the Blastlog theme (docs/PLAYNITE_DESIGN.md §3.1): bundled Outfit, and the selected game's
/// colours as theme resources (Blastlog.Game, Blastlog.OnGame, Blastlog.GameText, tints, edge, bar, glow), worked out
/// exactly as the site does and crossfaded over 240ms. Does nothing under other themes, which don't define Blastlog.Game.
/// </summary>
public sealed class ThemeBridge {
  static readonly ILogger Log = LogManager.GetLogger();
  static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
  static readonly string[] Keys = { "Blastlog.Game", "Blastlog.OnGame", "Blastlog.GameText", "Blastlog.GameTint", "Blastlog.GameTint2", "Blastlog.GameEdge", "Blastlog.GameBar" };
  readonly BlastlogPlugin plugin; readonly string coverCache;
  readonly ConcurrentDictionary<string, string> seeds = new ConcurrentDictionary<string, string>();
  Guid? selected, lastSelected; bool installed, themed;

  public ThemeBridge(BlastlogPlugin plugin) {
    this.plugin = plugin;
    coverCache = Path.Combine(plugin.GetPluginUserDataPath(), "covers");
  }

  public static bool BlastlogThemeActive => Application.Current?.TryFindResource("Blastlog.Game") != null;
  public static FontFamily Outfit { get; private set; }
  /// <summary>Which board game colours are built for: Blastlog Light says Light (its Blastlog.Mode); anything else is dark.</summary>
  public static Mode CurrentMode => Application.Current?.TryFindResource("Blastlog.Mode") as string == "Light" ? Mode.Light : Mode.Dark;

  /// <summary>
  /// Call once Playnite's window and theme are up. Under the Blastlog theme, Outfit becomes Playnite's font; under any
  /// theme, the Blastlog.* tokens the plugin's screens use are filled in where the theme doesn't define them.
  /// </summary>
  public void Install() {
    if (installed) return;
    installed = true;
    try {
      var resources = Application.Current.Resources;
      themed = BlastlogThemeActive;
      var fonts = Path.Combine(Path.GetDirectoryName(typeof(ThemeBridge).Assembly.Location), "Fonts");
      Outfit = File.Exists(Path.Combine(fonts, "Outfit-Regular.ttf")) ? new FontFamily(new Uri(fonts + Path.DirectorySeparatorChar), "./#Outfit, Segoe UI") : new FontFamily("Segoe UI");
      resources["Blastlog.Font"] = Outfit;
      if (themed) resources["FontFamily"] = Outfit;
      else foreach (var token in BaseTokens) if (Application.Current.TryFindResource(token.Key) == null) {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Tokens.Dark[token.Value])); b.Freeze(); resources[token.Key] = b;
      }
      // App-level keys win over the theme's defaults in its merged dictionaries.
      Apply(GameColour.BrandGreen, animate: false);
    } catch (Exception e) { Log.Error(e, "Blastlog: couldn't set up the theme resources"); }
  }
  static readonly Dictionary<string, string> BaseTokens = new Dictionary<string, string> {
    ["Blastlog.Bg"] = "bg", ["Blastlog.Side"] = "side", ["Blastlog.Surface"] = "surface", ["Blastlog.Surface2"] = "surface-2", ["Blastlog.Line"] = "line",
    ["Blastlog.Edge"] = "edge", ["Blastlog.Text"] = "text", ["Blastlog.TextMuted"] = "text-muted", ["Blastlog.Star"] = "star", ["Blastlog.Ok"] = "ok",
    ["Blastlog.Warn"] = "warn", ["Blastlog.Danger"] = "danger", ["Blastlog.Brand"] = "brand", ["Blastlog.Focus"] = "focus",
  };
  /// <summary>
  /// A screen about one game wears that game (design.md 2.3: the dashboard wears Next up's game, a game page its game).
  /// Call it with the seed from <see cref="SeedFor(Blastlog.Sync.Core.Game)"/>; the sidebar, mark and buttons follow.
  /// </summary>
  public void WearPage(string seed) { if (installed) { selected = null; Apply(seed ?? GameColour.BrandGreen, true); } }

  /// <summary>Back from a Blastlog screen to Playnite's library: wear the selected game again (or Blastlog green).</summary>
  public void RestoreSelection() {
    try {
      var games = plugin.Api.MainView.SelectedGames?.ToList();
      // Playnite drops the selection while a sidebar screen is open; put the last selected game back, so the library
      // shows it and wears its colour again.
      if ((games == null || games.Count == 0) && lastSelected != null && plugin.Api.Database.Games.Get(lastSelected.Value) != null) {
        plugin.Api.MainView.SelectGame(lastSelected.Value);
        Select(plugin.Api.Database.Games.Get(lastSelected.Value)); // the view reports the selection a moment later
        return;
      }
      Select(games != null && games.Count == 1 ? games[0] : null);
    } catch (Exception e) { Log.Warn(e, "Blastlog: couldn't restore the selected game's colour"); }
  }

  /// <summary>The selected game changed: wear its colour (Blastlog green until it's known).</summary>
  public void Select(Game game) {
    if (!installed) return;
    selected = game?.Id; if (game != null) lastSelected = game.Id;
    if (game == null) { Apply(GameColour.BrandGreen, true); return; }
    if (seeds.TryGetValue(game.Id.ToString(), out var known)) { Apply(known, true); return; }
    _ = Task.Run(async () => {
      var seed = await SeedFor(game).ConfigureAwait(false);
      seeds[game.Id.ToString()] = seed;
      Application.Current?.Dispatcher.BeginInvoke(new Action(() => { if (selected == game.Id) Apply(seed, true); }));
    });
  }

  /// <summary>The game's seed colour: the Blastlog page colour when set, else its Blastlog cover's, else its Playnite cover's.</summary>
  public async Task<string> SeedFor(Game game) {
    try {
      var entryId = plugin.EntryFor(game.Id);
      var entry = entryId != null ? plugin.CachedEntry(entryId) : null;
      if (entry?.Game?.Accent != null && Colour.ParseHex(entry.Game.Accent) != null) return entry.Game.Accent.ToLowerInvariant();
      var url = Artwork.Url(entry?.Game, ArtworkContext.Cover);
      var file = url != null ? await CachedFile(url).ConfigureAwait(false) : null;
      if (file == null && !string.IsNullOrEmpty(game.CoverImage)) file = plugin.Api.Database.GetFullFilePath(game.CoverImage);
      if (file == null || !File.Exists(file)) return GameColour.BrandGreen;
      var pixels = CoverPixels(file);
      return pixels == null ? GameColour.BrandGreen : CoverColours.Of(pixels).FirstOrDefault() ?? GameColour.BrandGreen;
    } catch (Exception e) { Log.Warn(e, "Blastlog: no colour for " + game.Name); return GameColour.BrandGreen; }
  }

  /// <summary>A Blastlog game's seed colour (for cards): its page colour when set, else its cover's, else Blastlog green.</summary>
  public async Task<string> SeedFor(Blastlog.Sync.Core.Game game) {
    if (game == null) return GameColour.BrandGreen;
    if (game.Accent != null && Colour.ParseHex(game.Accent) != null) return game.Accent.ToLowerInvariant();
    var url = Artwork.Url(game, ArtworkContext.Cover);
    if (url == null) return GameColour.BrandGreen;
    if (seeds.TryGetValue(url, out var known)) return known;
    var file = await CachedFile(url).ConfigureAwait(false);
    var pixels = file != null ? await Task.Run(() => CoverPixels(file)).ConfigureAwait(false) : null;
    var seed = pixels == null ? GameColour.BrandGreen : CoverColours.Of(pixels).FirstOrDefault() ?? GameColour.BrandGreen;
    seeds[url] = seed;
    return seed;
  }

  /// <summary>A Blastlog image, downloaded once and kept in the plugin's data folder.</summary>
  public Task<string> CachedFile(string url) {
    if (string.IsNullOrEmpty(url)) return Task.FromResult<string>(null);
    // One download per picture however many ask (a card's cover and its colour), at most six at a time: .NET Framework
    // allows two connections per host by default, so a page asking for a hundred covers at once timed most of them out.
    lock (fetches) {
      if (fetches.TryGetValue(url, out var running)) return running;
      var task = Download(url);
      fetches[url] = task;
      task.ContinueWith(_ => { lock (fetches) fetches.Remove(url); }, TaskScheduler.Default);
      return task;
    }
  }
  readonly Dictionary<string, Task<string>> fetches = new Dictionary<string, Task<string>>();
  static readonly System.Threading.SemaphoreSlim Downloads = new System.Threading.SemaphoreSlim(6, 6);
  static ThemeBridge() { try { System.Net.ServicePointManager.FindServicePoint(new Uri("https://images.igdb.com/")).ConnectionLimit = 8; } catch (Exception) { } }
  async Task<string> Download(string url) {
    Directory.CreateDirectory(coverCache);
    string name; using (var sha = SHA256.Create()) name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "").Substring(0, 32) + ".jpg";
    var path = Path.Combine(coverCache, name);
    if (File.Exists(path)) return path;
    await Downloads.WaitAsync().ConfigureAwait(false);
    try {
      if (File.Exists(path)) return path;
      var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
      // Two callers can fetch the same picture at once (a card's colour and its cover): each writes its own temp file,
      // and whoever finishes second keeps the first one's copy.
      var temp = path + "." + Guid.NewGuid().ToString("N") + ".part";
      File.WriteAllBytes(temp, bytes);
      try { if (!File.Exists(path)) File.Move(temp, path); } catch (IOException) when (File.Exists(path)) { }
      try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
      return path;
    } catch (Exception e) { Log.Warn(e, "Blastlog: couldn't download " + url); return null; }
    finally { Downloads.Release(); }
  }

  /// <summary>The cover shrunk to 36x48 (covers are 3:4), opaque pixels only: the site's canvas step.</summary>
  public static List<Rgb> CoverPixels(string file) {
    try {
      var source = new BitmapImage();
      source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.UriSource = new Uri(file); source.EndInit(); source.Freeze();
      var scaled = new TransformedBitmap(source, new ScaleTransform(36.0 / source.PixelWidth, 48.0 / source.PixelHeight));
      var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
      int w = bgra.PixelWidth, h = bgra.PixelHeight; var data = new byte[w * h * 4];
      bgra.CopyPixels(data, w * 4, 0);
      var pixels = new List<Rgb>(w * h);
      for (var i = 0; i < data.Length; i += 4) if (data[i + 3] > 180) pixels.Add(new Rgb(data[i + 2], data[i + 1], data[i]));
      return pixels;
    } catch (Exception) { return null; }
  }

  // The game colours live on one holder object; the brushes in the resources are bound to it, so they can't be frozen
  // and are placed once. A colour change animates the holder (design.md 11: 240ms; Windows' "Show animations" off means
  // an instant switch) and never replaces resources, which would make WPF re-resolve every resource in the window on
  // each frame (and, on Charlie's PC, coincided with panels that stopped closing).
  readonly ColourHolder holder = new ColourHolder();
  bool placed;
  void Place() {
    if (placed) return; placed = true;
    var resources = Application.Current.Resources;
    SolidColorBrush Bound(int index, byte alpha = 255) {
      var b = new SolidColorBrush();
      BindingOperations.SetBinding(b, SolidColorBrush.ColorProperty, new Binding(ColourHolder.Names[index]) { Source = holder,
        Converter = alpha == 255 ? null : new AlphaConverter(alpha) });
      return b;
    }
    for (var i = 0; i < Keys.Length; i++) resources[Keys[i]] = Bound(i);
    // Under the Blastlog themes, Playnite's own accent (the Play button, selection borders, links, highlights) is the
    // game colour too, with its ink on fills, so nothing stays the default theme's teal.
    if (themed) {
      resources["GlyphBrush"] = Bound(0); resources["HighlightGlyphBrush"] = Bound(0, 0x33); resources["TextBrushDark"] = Bound(1);
    }
  }
  void Apply(string seed, bool animate) {
    try {
      var t = GameColour.For(seed, CurrentMode);
      var target = new[] { t.Game, t.OnGame, t.GameText, t.GameTint, t.GameTint2, t.GameEdge, t.GameBar }
        .Select(v => (Color)ColorConverter.ConvertFromString(v)).ToArray();
      var first = !placed;
      Place();
      var duration = animate && !first && SystemParameters.ClientAreaAnimation ? new Duration(TimeSpan.FromMilliseconds(240)) : new Duration(TimeSpan.Zero);
      for (var i = 0; i < target.Length; i++) {
        if (duration.TimeSpan == TimeSpan.Zero) { holder.BeginAnimation(ColourHolder.Properties[i], null); holder.SetValue(ColourHolder.Properties[i], target[i]); }
        else holder.BeginAnimation(ColourHolder.Properties[i], new ColorAnimation(target[i], duration) { EasingFunction = new QuadraticEase() });
      }
      var glow = target[0]; var glowColour = Color.FromArgb((byte)Math.Round(t.GlowAlpha * 255), glow.R, glow.G, glow.B);
      var resources = Application.Current.Resources;
      if (!(resources["Blastlog.GlowColor"] is Color old) || old != glowColour) {
        resources["Blastlog.GlowColor"] = glowColour; resources["Blastlog.GlowClearColor"] = Color.FromArgb(0, glow.R, glow.G, glow.B);
      }
    } catch (Exception e) { Log.Error(e, "Blastlog: couldn't apply the game colour"); }
  }

  /// <summary>The page's game colours as animatable properties (Game, OnGame, GameText, GameTint, GameTint2, GameEdge, GameBar).</summary>
  sealed class ColourHolder : Animatable {
    protected override Freezable CreateInstanceCore() => new ColourHolder();
    public static readonly string[] Names = { "Game", "OnGame", "GameText", "GameTint", "GameTint2", "GameEdge", "GameBar" };
    public static readonly DependencyProperty[] Properties = Names.Select(n => DependencyProperty.Register(n, typeof(Color), typeof(ColourHolder), new PropertyMetadata(Colors.Transparent))).ToArray();
  }
  sealed class AlphaConverter : IValueConverter {
    readonly byte alpha; public AlphaConverter(byte alpha) { this.alpha = alpha; }
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) { var col = (Color)value; return Color.FromArgb(alpha, col.R, col.G, col.B); }
    public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
  }
}
}