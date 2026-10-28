using System;
using System.Collections.Generic;
using System.Linq;
namespace Blastlog.Sync.Core {
/// <summary>
/// Field mapping (docs/PLAN.md §4.2). Playnite's completion statuses are the user's own list, so the map is keyed by the
/// status's id; on first run it is filled from the default names, and anything it doesn't know means "no change".
/// </summary>
public static class Mapping {
  /// <summary>Playnite's default status names → Blastlog statuses.</summary>
  public static readonly IReadOnlyDictionary<string,string> DefaultToBlastlog = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
    ["Not Played"]="not_started", ["Plan to Play"]="not_started", ["Played"]="started", ["Playing"]="playing",
    ["Beaten"]="done", ["Completed"]="done", ["On Hold"]="started", ["Abandoned"]="quit" };
  /// <summary>Blastlog statuses → the Playnite status name to set; the rest leave Playnite as it is.</summary>
  public static readonly IReadOnlyDictionary<string,string> DefaultToPlaynite = new Dictionary<string,string> {
    ["not_started"]="Not Played", ["started"]="Played", ["playing"]="Playing", ["done"]="Beaten", ["dnf"]="Abandoned", ["quit"]="Abandoned" };
  public static readonly string[] BlastlogStatuses = { "not_started", "started", "playing", "done", "dnf", "quit", "unplayable", "ignored" };

  /// <summary>The status map for a user's own statuses (id → name): matched by the default names.</summary>
  public static StatusMap FromNames(IEnumerable<KeyValuePair<string,string>> statuses) {
    var map = new StatusMap();
    foreach (var status in statuses) {
      if (DefaultToBlastlog.TryGetValue(status.Value ?? "", out var blastlog)) map.ToBlastlog[status.Key] = blastlog;
      foreach (var pair in DefaultToPlaynite) if (string.Equals(pair.Value, status.Value, StringComparison.OrdinalIgnoreCase) && !map.ToPlaynite.ContainsKey(pair.Key)) map.ToPlaynite[pair.Key] = status.Key;
    }
    return map;
  }

  /// <summary>Playnite's 0–100 user score → whole stars (round half up, 1–5); 0 or none is no rating.</summary>
  public static int? StarsFromScore(int? score) {
    if (score == null || score <= 0) return null;
    // Integer maths: 70 is exactly 3.5 stars and rounds up (0.7 * 5 in floating point can land just under 3.5).
    var stars = (score.Value * 5 + 50) / 100;
    return Math.Max(1, Math.Min(5, stars));
  }
  public static int? ScoreFromStars(double? stars) => stars == null ? (int?)null : (int)Math.Round(stars.Value * 20);
  /// <summary>Whole stars as Blastlog sends them (a 4.5 from the site stays 4.5 for comparing; it's sent as whole stars only).</summary>
  public static int? WholeStars(double? stars) => stars == null ? (int?)null : (int)Math.Floor(stars.Value + 0.5);
}

public sealed class StatusMap {
  /// <summary>Playnite status id → Blastlog status.</summary>
  public Dictionary<string,string> ToBlastlog {get;set;} = new Dictionary<string,string>();
  /// <summary>Blastlog status → Playnite status id.</summary>
  public Dictionary<string,string> ToPlaynite {get;set;} = new Dictionary<string,string>();
  public string BlastlogOf(string playniteStatusId) => playniteStatusId != null && ToBlastlog.TryGetValue(playniteStatusId, out var s) ? s : null;
  public string PlayniteOf(string blastlogStatus) => blastlogStatus != null && ToPlaynite.TryGetValue(blastlogStatus, out var s) ? s : null;
}

/// <summary>What wins when both sides changed the same field to different values since the last sync.</summary>
public enum ConflictRule { BlastlogWins, PlayniteWins, AskMe }

/// <summary>The synced fields of one pair, as they stood after the last sync (the baseline) or as each side has them now.</summary>
public sealed class FieldSet {
  public string Status {get;set;} public int? Stars {get;set;}
  public FieldSet Copy() => new FieldSet { Status = Status, Stars = Stars };
  public override bool Equals(object obj) => obj is FieldSet f && f.Status == Status && f.Stars == Stars;
  public override int GetHashCode() => (Status ?? "").GetHashCode() ^ (Stars ?? -1);
}

public enum Side { None, Blastlog, Playnite, Conflict }
public sealed class FieldOutcome { public string Field {get;set;} public Side Write {get;set;} public object Value {get;set;} public bool Conflict {get;set;} }

/// <summary>
/// Three-way merge per field (docs/PLAN.md §4.3): only Playnite changed → push it; only Blastlog changed → write it to
/// Playnite; both to the same value → nothing; both differently → the conflict rule decides (Ask me leaves both and
/// reports it). Each side is compared with its own baseline (what it had after the last sync), because the mapping is
/// lossy: Blastlog's "unplayable" has no Playnite status, and Playnite's Beaten and Completed are both "done". A null on
/// the Playnite side (a status it can't map) is "no opinion", never a change.
/// </summary>
public static class Merge {
  public static List<FieldOutcome> Fields(FieldSet baseLocal, FieldSet baseRemote, FieldSet blastlog, FieldSet playnite, ConflictRule rule) {
    var outcomes = new List<FieldOutcome>();
    void One(string field, object bl, object br, object local, object remote, bool localKnown, bool first) {
      var localChanged = localKnown && (first ? !Equals(local, remote) : !Equals(local, bl));
      var remoteChanged = first ? !Equals(local, remote) : !Equals(remote, br);
      if (localChanged && remoteChanged && !Equals(local, remote)) {
        var winner = rule == ConflictRule.PlayniteWins ? Side.Blastlog : rule == ConflictRule.BlastlogWins ? Side.Playnite : Side.Conflict;
        outcomes.Add(new FieldOutcome { Field = field, Write = winner, Value = winner == Side.Blastlog ? local : winner == Side.Playnite ? remote : null, Conflict = true });
      } else if (localChanged && !remoteChanged) outcomes.Add(new FieldOutcome { Field = field, Write = Side.Blastlog, Value = local });
      else if (remoteChanged && !localChanged) outcomes.Add(new FieldOutcome { Field = field, Write = Side.Playnite, Value = remote });
    }
    var first = baseLocal == null || baseRemote == null;
    One("status", baseLocal?.Status, baseRemote?.Status, playnite.Status, blastlog.Status, playnite.Status != null, first);
    // A first sync never clears a rating on either side: no score in Playnite isn't a wish to remove Blastlog's.
    if (first && (playnite.Stars == null || blastlog.Stars == null)) {
      if (playnite.Stars == null && blastlog.Stars != null) outcomes.Add(new FieldOutcome { Field = "stars", Write = Side.Playnite, Value = blastlog.Stars });
      else if (blastlog.Stars == null && playnite.Stars != null) outcomes.Add(new FieldOutcome { Field = "stars", Write = Side.Blastlog, Value = playnite.Stars });
    } else One("stars", baseLocal?.Stars, baseRemote?.Stars, playnite.Stars, blastlog.Stars, true, first);
    return outcomes.Where(o => o.Write != Side.None).ToList();
  }
}
}
