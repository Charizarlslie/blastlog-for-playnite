using System;
using System.Collections.Generic;
using System.Linq;

namespace Blastlog.Sync.Core.Design {
public enum Mode { Dark, Light }

/// <summary>Blastlog's base colour tokens (design.md 2.1), ported from apps/web/src/design/tokens.ts.</summary>
public static class Tokens {
  public static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string> {
    ["bg"] = "#0b0b0d", ["side"] = "#0e0e11", ["surface"] = "#151519", ["surface-2"] = "#1e1e24", ["line"] = "#2a2a31", ["edge"] = "#6a6a76",
    ["text"] = "#f4f4f6", ["text-muted"] = "#a6a6b1", ["star"] = "#ffc400", ["ok"] = "#6ee7b0", ["ok-text"] = "#6ee7b0", ["warn"] = "#ffb27a", ["danger"] = "#ff8c95",
    ["brand"] = "#00e4a8", ["brand-text"] = "#00e4a8", ["on-brand"] = "#0b0b0d", ["ink"] = "#0b0b0d", ["focus"] = "#f4f4f6", ["focus-gap"] = "#0b0b0d", ["shade"] = "#000000", ["scrim"] = "#000000",
  };
  public static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string> {
    ["bg"] = "#f3f3f6", ["side"] = "#fbfbfd", ["surface"] = "#ffffff", ["surface-2"] = "#ececf1", ["line"] = "#e0e0e7", ["edge"] = "#8b8b97",
    ["text"] = "#141418", ["text-muted"] = "#55555f", ["star"] = "#9a6b00", ["ok"] = "#1c8a5a", ["ok-text"] = "#13663f", ["warn"] = "#a14f00", ["danger"] = "#c0262f",
    ["brand"] = "#00e4a8", ["brand-text"] = "#016e50", ["on-brand"] = "#0b0b0d", ["ink"] = "#141418", ["focus"] = "#141418", ["focus-gap"] = "#ffffff", ["shade"] = "#000000", ["scrim"] = "#ffffff",
  };
  public static IReadOnlyDictionary<string, string> For(Mode mode) => mode == Mode.Dark ? Dark : Light;
  /// <summary>Fixed per person in Compare (design.md 2.4).</summary>
  public static readonly string[] PeopleDark = { "#f7a35c", "#3fdca7", "#a9a4ff", "#ff8fc7", "#7cc8ff", "#e3d26f" };
  public static readonly string[] PeopleLight = { "#a14f00", "#037555", "#5b52d6", "#b0306f", "#0b6aa8", "#6f6200" };
}

/// <summary>A game's colour tokens (design.md 2.2). Glow is the site's CSS string; <see cref="GlowAlpha"/> gives the alpha for WPF.</summary>
public sealed class GameTokens {
  public string Game, OnGame, GameText, GameTint, GameTint2, GameEdge, GameBar, Glow;
  public double GlowAlpha;
}

/// <summary>
/// Game colour, ported from apps/web/src/design/game-colour.ts: every page and card wears its game's exact cover colour,
/// and the shades around it move lightness only, and only as far as contrast needs.
/// </summary>
public static class GameColour {
  /// <summary>Blastlog green: the mark, and the colour of a game with no usable cover.</summary>
  public const string BrandGreen = "#00e4a8";
  const string White = "#ffffff";

  static (string Game, string OnGame) FillAndInk(string seed, Mode mode) {
    var dark = Tokens.For(mode)["ink"];
    var pick = new[] { dark, White }.Select(ink => (ink, nudged: Colour.NudgeToContrast(seed, new[] { ink }, 4.5).Value))
      .OrderBy(x => x.nudged.Move).ThenByDescending(x => Colour.Contrast(seed, x.ink)).First();
    return (pick.nudged.Colour, pick.ink);
  }

  public static GameTokens For(string seed, Mode mode) {
    var t = Tokens.For(mode);
    var colour = (seed != null && Colour.ParseHex(seed) != null ? seed : BrandGreen).ToLowerInvariant();
    var (game, onGame) = FillAndInk(colour, mode);
    var gameText = Colour.NudgeToContrast(colour, new[] { t["surface"], t["surface-2"] }, 5).Value.Colour;
    double tintL = mode == Mode.Dark ? .25 : .955, tint2L = mode == Mode.Dark ? .30 : .915, tintChroma = mode == Mode.Dark ? .06 : .04;
    var edge = Colour.NudgeToContrast(Colour.WithLightness(colour, mode == Mode.Dark ? .5 : .66, .1), new[] { t["bg"], t["surface"] }, 3).Value.Colour;
    var bar = Colour.NudgeToContrast(Colour.WithLightness(colour, mode == Mode.Dark ? .34 : .88, .08), new[] { t["text"] }, 7).Value.Colour;
    var alpha = mode == Mode.Dark ? .23 : .1;
    return new GameTokens {
      Game = game, OnGame = onGame, GameText = gameText,
      GameTint = Colour.WithLightness(colour, tintL, tintChroma), GameTint2 = Colour.WithLightness(colour, tint2L, tintChroma),
      GameEdge = edge, GameBar = bar, Glow = Colour.WithAlpha(colour, alpha), GlowAlpha = alpha,
    };
  }

  /// <summary>A cover colour too grey to wear (design.md 2.2 fallbacks).</summary>
  public static bool IsGrey(string colour) => Colour.ToOklch(colour).C < .04;
  /// <summary>The first usable, non-grey candidate, else Blastlog green.</summary>
  public static string PickSeed(IEnumerable<string> candidates) =>
    candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && Colour.ParseHex(c) != null && !IsGrey(c)) ?? BrandGreen;
}

/// <summary>
/// A cover's main colours, ported from apps/web/src/palette.ts `coverColours`: each hue counts by how much of the cover
/// it covers, vivid colour more than muted (chroma squared), dark, near-white and grey areas set aside, all in OKLab.
/// Pixels are the cover shrunk to 36x48, opaque ones only (the site's canvas step; <c>CoverPixels</c> in the plugin).
/// </summary>
public static class CoverColours {
  const int Bins = 24; const double Bin = 360.0 / Bins;
  struct Lab { public double L, A, B; }
  struct Px { public Lab Lab; public double Chroma, Hue, Weight; }

  static double Encoded(double value) => 255 * Math.Min(1, Math.Max(0, value <= .0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - .055));
  static Lab ToOklab(Rgb p) {
    double R = Colour.ToLinear(p.R), G = Colour.ToLinear(p.G), B = Colour.ToLinear(p.B);
    var l = Colour.Cbrt(.4122214708 * R + .5363325363 * G + .0514459929 * B);
    var m = Colour.Cbrt(.2119034982 * R + .6806995451 * G + .1073969566 * B);
    var s = Colour.Cbrt(.0883024619 * R + .2817188376 * G + .6299787005 * B);
    return new Lab { L = .2104542553 * l + .793617785 * m - .0040720468 * s, A = 1.9779984951 * l - 2.428592205 * m + .4505937099 * s,
      B = .0259040371 * l + .7827717662 * m - .808675766 * s };
  }
  static string FromOklabHex(Lab c) {
    double l = Math.Pow(c.L + .3963377774 * c.A + .2158037573 * c.B, 3), m = Math.Pow(c.L - .1055613458 * c.A - .0638541728 * c.B, 3),
      s = Math.Pow(c.L - .0894841775 * c.A - 1.291485548 * c.B, 3);
    string H(double v) => ((int)Colour.JsRound(v)).ToString("x2");
    return "#" + H(Encoded(4.0767416621 * l - 3.3077115913 * m + .2309699292 * s)) + H(Encoded(-1.2684380046 * l + 2.6097574011 * m - .3413193965 * s))
      + H(Encoded(-.0041960863 * l - .7034186147 * m + 1.707614701 * s));
  }

  /// <summary>Up to <paramref name="limit"/> distinct hues, the accent first; empty when nothing is colourful enough.</summary>
  public static List<string> Of(IList<Rgb> pixels, int limit = 1) {
    foreach (var minChroma in new[] { .08, .045 }) {
      var hist = new double[Bins];
      var colourful = new List<Px>();
      foreach (var pixel in pixels) {
        var lab = ToOklab(pixel); var chroma = Colour.Hypot(lab.A, lab.B);
        if (chroma < minChroma || lab.L < .3 || lab.L > .97) continue;
        var hue = (Math.Atan2(lab.B, lab.A) * 180 / Math.PI + 360) % 360; var weight = chroma * chroma;
        colourful.Add(new Px { Lab = lab, Chroma = chroma, Hue = hue, Weight = weight });
        hist[(int)Math.Floor(hue / Bin) % Bins] += weight;
      }
      if (colourful.Count < Math.Max(1, pixels.Count * .015)) continue;
      var smoothed = Enumerable.Range(0, Bins).Select(i => hist[(i + Bins - 1) % Bins] + 2 * hist[i] + hist[(i + 1) % Bins]).ToArray();
      var peaks = new List<int>();
      while (peaks.Count < limit) {
        var peak = -1;
        for (var i = 0; i < Bins; i++) {
          if (peaks.Any(p => Math.Min(Math.Abs(p - i), Bins - Math.Abs(p - i)) < 3)) continue;
          if (peak < 0 || smoothed[i] > smoothed[peak]) peak = i;
        }
        if (peak < 0 || smoothed[peak] == 0 || (peaks.Count > 0 && smoothed[peak] < smoothed[peaks[0]] * .08)) break;
        peaks.Add(peak);
      }
      return peaks.Select(peak => {
        var centre = (peak + .5) * Bin;
        // The vivid half of the pixels within a bin and a half of the peak, averaged. Stable sort, as JS's is.
        var cluster = colourful.Where(p => { var d = Math.Abs(p.Hue - centre); return Math.Min(d, 360 - d) <= Bin * 1.5; })
          .OrderByDescending(p => p.Chroma).ToList();
        var vivid = cluster.Take(Math.Max(1, (int)Math.Ceiling(cluster.Count / 2.0))).ToList();
        var total = vivid.Sum(p => p.Weight);
        double L = 0, A = 0, B = 0;
        foreach (var p in vivid) { L += p.Lab.L * p.Weight / total; A += p.Lab.A * p.Weight / total; B += p.Lab.B * p.Weight / total; }
        return FromOklabHex(new Lab { L = Math.Max(L, .62), A = A, B = B });
      }).ToList();
    }
    return new List<string>();
  }
}
}
