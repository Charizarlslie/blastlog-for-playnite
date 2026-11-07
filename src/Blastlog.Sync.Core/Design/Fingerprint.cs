using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Blastlog.Sync.Core.Design {
/// <summary>One reason a trait is on a game ("Why it's here"): its kind (interpretation, recheck, you, ...) and Blastlog's words.</summary>
public sealed class TraitReason { public string Kind; public string Text; }

/// <summary>A trait on a game (the site's ActiveCharacteristic, player view).</summary>
public sealed class FingerprintTrait {
  public string Id, Label, Family, Role; public double Strength; public int Carriers; public List<TraitReason> Reasons = new List<TraitReason>();
  /// <summary>Strength dots (design.md 8): solid strong (0.8+), half moderate (0.5+), ring light.</summary>
  public string Band => Strength >= 0.8 ? "high" : Strength >= 0.5 ? "mid" : "low";
  public string StrengthWord => Strength >= 0.8 ? "strong" : Strength >= 0.5 ? "moderate" : "weak";
}

/// <summary>A discovery waiting for review, or a set-aside source label (read only for players).</summary>
public sealed class FingerprintNote { public string Label, Detail, Description; }

/// <summary>Online play that ended for this game (docs/ONLINE_MODES.md): the traits that left the fingerprint, and why.</summary>
public sealed class OnlineEnded { public string Label, How, EndedOn, EffectiveFrom, Reason; public bool Upcoming; }

/// <summary>
/// A game's fingerprint as the site's GET /games/:id/fingerprint returns it (the plugin reads it through the
/// /sync/v1/entries/:id mirror). Read loosely: a field the server leaves out reads as empty.
/// </summary>
public sealed class FingerprintData {
  public string GameId, GameTitle, Summary; public int ScopeGames; public bool Building;
  public Dictionary<string, double> TypicalShares = new Dictionary<string, double>();
  public List<FingerprintTrait> Active = new List<FingerprintTrait>();
  public List<FingerprintNote> Pending = new List<FingerprintNote>(), Suppressed = new List<FingerprintNote>();
  public List<OnlineEnded> OnlineEnded = new List<OnlineEnded>();
  public int PendingCount;

  public static FingerprintData Parse(JToken token) {
    if (!(token is JObject o)) return null;
    var d = new FingerprintData { GameId = (string)o["game"]?["id"], GameTitle = (string)o["game"]?["title"], Summary = (string)o["summary"],
      ScopeGames = (int?)o["scopeGames"] ?? 0, Building = (bool?)o["building"] ?? false };
    if (o["typicalShares"] is JObject shares) foreach (var p in shares.Properties()) { var v = (double?)p.Value; if (v != null) d.TypicalShares[p.Name] = v.Value; }
    foreach (var t in (o["active"] as JArray ?? new JArray()).OfType<JObject>()) {
      var label = (string)t["label"]; if (string.IsNullOrEmpty(label)) continue;
      d.Active.Add(new FingerprintTrait { Id = (string)t["characteristicId"], Label = label, Family = (string)t["family"] ?? "other", Role = (string)t["role"] ?? "taste",
        Strength = (double?)t["strength"] ?? 0, Carriers = (int?)t["carriers"] ?? 0,
        Reasons = (t["reasons"] as JArray ?? new JArray()).OfType<JObject>().Select(r => new TraitReason { Kind = (string)r["kind"], Text = (string)r["text"] })
          .Where(r => !string.IsNullOrEmpty(r.Text)).ToList() });
    }
    // Discoveries can repeat per source; the site shows one row per wording.
    foreach (var p in (o["pending"] as JArray ?? new JArray()).OfType<JObject>().GroupBy(p => (string)p["termId"] ?? (string)p["term"]).Select(g => g.First()))
      d.Pending.Add(new FingerprintNote { Label = (string)p["term"], Description = (string)p["description"],
        Detail = ((int?)p["termGames"] ?? 0) > 1 ? $"also on {(int)p["termGames"] - 1} other game{((int)p["termGames"] == 2 ? "" : "s")}" : "only on this game" });
    foreach (var s in (o["suppressed"] as JArray ?? new JArray()).OfType<JObject>())
      d.Suppressed.Add(new FingerprintNote { Label = (string)s["rawLabel"], Description = (string)s["description"], Detail = SuppressionReason(s) });
    foreach (var e in (o["onlineEnded"] as JArray ?? new JArray()).OfType<JObject>())
      d.OnlineEnded.Add(new OnlineEnded { Label = (string)e["label"], How = (string)e["how"], EndedOn = (string)e["endedOn"], EffectiveFrom = (string)e["effectiveFrom"],
        Reason = (string)e["reason"], Upcoming = (bool?)e["upcoming"] ?? false });
    d.PendingCount = (int?)o["counts"]?["pending"] ?? d.Pending.Count;
    return d;
  }

  static string SuppressionReason(JObject item) {
    var inactive = (string)item["inactiveReason"]; var dispute = (string)item["disputeReason"];
    if (inactive == "retracted_by_admin") return "hidden by Blastlog";
    if (inactive == "superseded_by_new_source_record") return "no longer listed by the source";
    if (!string.IsNullOrEmpty(dispute)) return "disputed: " + System.Text.RegularExpressions.Regex.Replace(dispute, @"^(misleading|not_about_gameplay):\s*", "");
    if ((string)item["support"] == "unverified") return "not supported by a source yet";
    var status = (string)item["characteristicStatus"];
    if ((string)item["resolution"] == "mapped" && status != null && status != "approved") return $"its trait “{(string)item["characteristicLabel"] ?? (string)item["term"]}” was retired";
    return "removed by a decision";
  }
}

/// <summary>One bar on the game page fingerprint (design.md 8): an area, its traits strongest first, and how it compares with a typical game.</summary>
public sealed class FingerprintArea {
  public string Name; public string[] Families; public List<FingerprintTrait> Traits; public double Weight, Share, Typical;
  /// <summary>0–1, half = typical, full = twice typical or more; null when there's no typical to compare with.</summary>
  public double? Fill;
  /// <summary>What a screen reader hears (the numbers live here, never on screen).</summary>
  public string Sentence;
}

/// <summary>
/// The site's game page fingerprint maths (apps/web/src/GameFingerprint.tsx, fingerprint-bars.ts, fingerprint-api.ts),
/// so the plugin's bars, order and "What defines it" match the site exactly.
/// </summary>
public static class FingerprintBars {
  /// <summary>The card's four bars: plain-words groups of the trait families (Charlie picked option E, 2026-10-19).</summary>
  public static readonly KeyValuePair<string, string[]>[] Groups = {
    new KeyValuePair<string, string[]>("How it plays", new[] { "genre", "gameplay", "structure", "progression", "other" }),
    new KeyValuePair<string, string[]>("What it feels like", new[] { "setting", "theme", "tone", "presentation" }),
    new KeyValuePair<string, string[]>("The story", new[] { "narrative" }),
    new KeyValuePair<string, string[]>("How you play it", new[] { "mode", "challenge" }),
  };
  public static readonly Dictionary<string, string> StandsOut = new Dictionary<string, string> {
    ["How it plays"] = "how it plays", ["What it feels like"] = "how it feels", ["The story"] = "its story", ["How you play it"] = "how you play it" };
  public static readonly string[] FamilyOrder = { "gameplay", "genre", "structure", "progression", "narrative", "setting", "theme", "tone", "presentation", "mode", "challenge", "other", "franchise" };
  public static readonly Dictionary<string, string> FamilyLabels = new Dictionary<string, string> {
    ["gameplay"] = "Gameplay & mechanics", ["genre"] = "Genre", ["structure"] = "Structure & world", ["progression"] = "Progression", ["narrative"] = "Story & narrative",
    ["setting"] = "Setting", ["theme"] = "Themes", ["tone"] = "Tone & mood", ["presentation"] = "Presentation & visuals", ["mode"] = "Play modes",
    ["challenge"] = "Challenge", ["franchise"] = "Franchises", ["other"] = "Other" };
  /// <summary>"What defines it" only earns its place once the full list is long.</summary>
  public const int DefiningFrom = 12;

  /// <summary>fill = min(share ÷ typical, 2) ÷ 2; null when there's no typical to compare with.</summary>
  public static double? BarFill(double share, double typical) => typical > 0 ? Math.Min(share / typical, 2) / 2 : (double?)null;

  /// <summary>Strongest evidence first; among equally strong traits (to 0.05) the rarer one first, then by name.</summary>
  public static int ByStrength(FingerprintTrait a, FingerprintTrait b) {
    var s = (int)Math.Round(b.Strength * 20, MidpointRounding.AwayFromZero) - (int)Math.Round(a.Strength * 20, MidpointRounding.AwayFromZero);
    if (s != 0) return s;
    var c = a.Carriers - b.Carriers; if (c != 0) return c;
    return CultureInfo.InvariantCulture.CompareInfo.Compare(a.Label ?? "", b.Label ?? "", CompareOptions.None);
  }
  public static List<FingerprintTrait> Sorted(IEnumerable<FingerprintTrait> traits) { var list = traits.ToList(); list.Sort(ByStrength); return list; }

  /// <summary>The traits that say the most about a game: strength leads, rarity breaks ties; no context-only traits, franchises, or traits on over half the library.</summary>
  public static List<FingerprintTrait> Defining(FingerprintData d, int count = 8) {
    if (d == null || d.ScopeGames <= 0 || d.Active.Count <= DefiningFrom) return new List<FingerprintTrait>();
    return Sorted(d.Active.Where(t => t.Family != "franchise" && t.Role != "context" && t.Strength >= 0.5 && (double)t.Carriers / d.ScopeGames <= 0.5)).Take(count).ToList();
  }

  /// <summary>Traits by family in the site's order (franchises included, last), strongest first inside each.</summary>
  public static List<KeyValuePair<string, List<FingerprintTrait>>> ByFamily(FingerprintData d) =>
    FamilyOrder.Select(f => new KeyValuePair<string, List<FingerprintTrait>>(f, Sorted(d.Active.Where(t => t.Family == f)))).Where(p => p.Value.Count > 0).ToList();

  /// <summary>The bars, empty areas left out, each compared with a typical game.</summary>
  public static List<FingerprintArea> Areas(FingerprintData d) {
    var areas = Groups.Select(g => {
      var traits = Sorted(d.Active.Where(t => g.Value.Contains(t.Family)));
      return new FingerprintArea { Name = g.Key, Families = g.Value, Traits = traits, Weight = traits.Sum(t => t.Strength) };
    }).Where(a => a.Traits.Count > 0).ToList();
    var total = areas.Sum(a => a.Weight);
    foreach (var a in areas) {
      a.Share = total > 0 ? a.Weight / total : 0;
      a.Typical = a.Families.Sum(f => d.TypicalShares.TryGetValue(f, out var v) ? v : 0);
      a.Fill = BarFill(a.Share, a.Typical);
      var count = $"{a.Traits.Count} trait{(a.Traits.Count == 1 ? "" : "s")}";
      var strongest = string.Join(", ", a.Traits.Take(3).Select(t => t.Label));
      a.Sentence = a.Fill == null ? $"{a.Name}: {count}; strongest: {strongest}."
        : $"{a.Name}: {Verdict(a.Share / a.Typical)}. {Percent(a.Share)} of this game against {Percent(a.Typical)} for a typical game. {count}; strongest: {strongest}.";
    }
    return areas;
  }
  /// <summary>The bar's drawn fill: compared with a typical game when possible, else its weight against the heaviest bar (halved), as the site does.</summary>
  public static double DrawnFill(FingerprintArea area, IEnumerable<FingerprintArea> all) => area.Fill ?? area.Weight / Math.Max(1, all.Max(a => a.Weight)) / 2;
  /// <summary>The header's "stands out for …": the area at least 1.5x a typical game, furthest above it.</summary>
  public static string StandOut(List<FingerprintArea> areas) {
    var top = areas.Where(a => (a.Fill ?? 0) >= 0.75).OrderByDescending(a => a.Fill ?? 0).FirstOrDefault();
    return top == null ? null : StandsOut.TryGetValue(top.Name, out var words) ? words : top.Name.ToLowerInvariant();
  }
  /// <summary>Series names from the franchise traits ("Franchise: Silent Hill" → "Silent Hill").</summary>
  public static List<string> Series(FingerprintData d) => Sorted(d.Active.Where(t => t.Family == "franchise"))
    .Select(t => System.Text.RegularExpressions.Regex.Replace(t.Label, @"^Franchise:\s*", "")).ToList();
  public static int TraitCount(FingerprintData d) => d.Active.Count(t => t.Family != "franchise");

  static string Percent(double v) => Math.Round(v * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%";
  static string Verdict(double ratio) => ratio >= 1.75 ? "far more than most games" : ratio >= 1.25 ? "more than most games" : ratio > 0.8 ? "about typical"
    : ratio > 0.5 ? "less than most games" : "far less than most games";
}
}
