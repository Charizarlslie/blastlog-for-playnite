using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Blastlog.Sync.Core.Design {
/// <summary>An sRGB colour, 0-255 per channel.</summary>
public struct Rgb {
  public int R, G, B;
  public Rgb(int r, int g, int b) { R = r; G = g; B = b; }
}
/// <summary>A colour after a lightness move, and how far it moved (OKLCH lightness).</summary>
public sealed class Nudge {
  public readonly string Colour; public readonly double Move;
  public Nudge(string colour, double move) { Colour = colour; Move = move; }
}
/// <summary>An OKLCH colour: lightness 0-1, chroma, hue in degrees.</summary>
public struct Oklch {
  public double L, C, H;
  public Oklch(double l, double c, double h) { L = l; C = c; H = h; }
}

/// <summary>
/// Colour maths, ported line for line from Blastlog's web design code (apps/web/src/design/colour.ts) so the same seed
/// gives the same colours in Playnite as on the site. Contrast is WCAG 2.x; lightness moves happen in OKLCH. Rounding
/// follows JavaScript (halves round up), which the tests check against fixtures generated from the site's code.
/// </summary>
public static class Colour {
  public static Rgb? ParseHex(string value) {
    if (value == null) return null;
    var v = value.Trim(); if (v.StartsWith("#")) v = v.Substring(1);
    if (v.Length != 6 || !int.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) return null;
    return new Rgb(n >> 16 & 255, n >> 8 & 255, n & 255);
  }
  static Rgb MustParse(string value) => ParseHex(value) ?? throw new ArgumentException("Not a colour: " + value);
  /// <summary>JavaScript's Math.round: halves go up.</summary>
  public static double JsRound(double value) => Math.Floor(value + 0.5);
  static string Hex2(double value) => ((int)JsRound(Math.Min(255, Math.Max(0, value)))).ToString("x2");
  public static string ToHex(Rgb c) => "#" + Hex2(c.R) + Hex2(c.G) + Hex2(c.B);
  /// <summary>Cube root to full precision (netstandard2.0 has no Math.Cbrt): Pow, then one Newton step.</summary>
  public static double Cbrt(double x) {
    if (x == 0) return 0;
    var y = Math.Sign(x) * Math.Pow(Math.Abs(x), 1.0 / 3);
    return y - (y * y * y - x) / (3 * y * y);
  }
  public static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

  public static double ToLinear(double value) { var c = value / 255; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
  static double FromLinear(double value) => 255 * (value <= .0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - .055);

  public static double Luminance(Rgb c) => .2126 * ToLinear(c.R) + .7152 * ToLinear(c.G) + .0722 * ToLinear(c.B);
  public static double Luminance(string colour) => Luminance(MustParse(colour));
  public static double Contrast(string a, string b) {
    var x = Luminance(a); var y = Luminance(b);
    return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
  }

  public static Oklch ToOklch(string colour) => ToOklch(MustParse(colour));
  public static Oklch ToOklch(Rgb c) {
    double R = ToLinear(c.R), G = ToLinear(c.G), B = ToLinear(c.B);
    var l = Cbrt(.4122214708 * R + .5363325363 * G + .0514459929 * B);
    var m = Cbrt(.2119034982 * R + .6806995451 * G + .1073969566 * B);
    var s = Cbrt(.0883024619 * R + .2817188376 * G + .6299787005 * B);
    var L = .2104542553 * l + .793617785 * m - .0040720468 * s;
    var A = 1.9779984951 * l - 2.428592205 * m + .4505937099 * s;
    var Bb = .0259040371 * l + .7827717662 * m - .808675766 * s;
    return new Oklch(L, Hypot(A, Bb), (Math.Atan2(Bb, A) * 180 / Math.PI + 360) % 360);
  }

  /// <summary>OKLCH back to sRGB. Out-of-gamut colours lose chroma (never lightness or hue) until they fit.</summary>
  public static Rgb FromOklch(Oklch c) {
    double[] Convert(double chroma) {
      double A = chroma * Math.Cos(c.H * Math.PI / 180), B = chroma * Math.Sin(c.H * Math.PI / 180);
      double l = Math.Pow(c.L + .3963377774 * A + .2158037573 * B, 3), m = Math.Pow(c.L - .1055613458 * A - .0638541728 * B, 3),
        s = Math.Pow(c.L - .0894841775 * A - 1.291485548 * B, 3);
      return new[] { 4.0767416621 * l - 3.3077115913 * m + .2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - .3413193965 * s,
        -.0041960863 * l - .7034186147 * m + 1.707614701 * s };
    }
    bool Fits(double[] rgb) => rgb.All(v => v >= -.0005 && v <= 1.0005);
    var linear = Convert(c.C);
    if (!Fits(linear)) {
      double lo = 0, hi = c.C;
      for (var i = 0; i < 24; i++) { var mid = (lo + hi) / 2; if (Fits(Convert(mid))) lo = mid; else hi = mid; }
      linear = Convert(lo);
    }
    var o = linear.Select(v => (int)JsRound(FromLinear(Math.Min(1, Math.Max(0, v))))).ToArray();
    return new Rgb(o[0], o[1], o[2]);
  }

  /// <summary>The same colour at another OKLCH lightness, as hex.</summary>
  public static string WithLightness(string colour, double L, double maxChroma = double.PositiveInfinity) {
    var lch = ToOklch(colour);
    return ToHex(FromOklch(new Oklch(Math.Min(1, Math.Max(0, L)), Math.Min(lch.C, maxChroma), lch.H)));
  }

  /// <summary>
  /// The smallest lightness move that makes <paramref name="colour"/> reach <paramref name="target"/> contrast against
  /// every colour in <paramref name="against"/>; the colour unchanged when it already passes; null when nothing works.
  /// </summary>
  public static Nudge NudgeToContrast(string colour, IList<string> against, double target, double maxChroma = double.PositiveInfinity) {
    bool Passes(string candidate) => against.All(bg => Contrast(candidate, bg) >= target);
    var start = ToOklch(colour);
    var capped = start.C > maxChroma ? WithLightness(colour, start.L, maxChroma) : colour.ToLowerInvariant();
    if (Passes(capped)) return new Nudge(capped, 0);
    Nudge best = null;
    foreach (var direction in new[] { 1, -1 }) {
      for (var step = 1; step <= 1000; step++) {
        var L = start.L + direction * step / 1000.0;
        if (L < 0 || L > 1) break;
        var candidate = WithLightness(colour, L, maxChroma);
        if (Passes(candidate)) { if (best == null || step < best.Move * 1000) best = new Nudge(candidate, step / 1000.0); break; }
      }
    }
    return best;
  }

  /// <summary>The site's CSS form, `rgb(r g b / a%)`, kept so tests compare like with like.</summary>
  public static string WithAlpha(string colour, double alpha) {
    var c = MustParse(colour);
    var percent = JsRound(alpha * 1000) / 10;
    return $"rgb({c.R} {c.G} {c.B} / {percent.ToString(CultureInfo.InvariantCulture)}%)";
  }
}
}
