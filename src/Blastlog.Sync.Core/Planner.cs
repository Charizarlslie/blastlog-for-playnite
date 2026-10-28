using System;
using System.Collections.Generic;
using System.Linq;
namespace Blastlog.Sync.Core {
/// <summary>What the plugin knows about one Playnite game, read from Playnite's database (no Playnite types here).</summary>
public sealed class LocalGame {
  public string Id {get;set;} public string Name {get;set;} public string StatusId {get;set;} public int? UserScore {get;set;}
  public long PlaytimeSeconds {get;set;} public DateTimeOffset? LastActivity {get;set;} public bool Hidden {get;set;}
  public string Source {get;set;} public string PluginId {get;set;} public string StoreGameId {get;set;} public int? ReleaseYear {get;set;} public string[] Platforms {get;set;} = new string[0];
}

/// <summary>One paired game's state after the last sync: the merge's common ancestor.</summary>
/// <summary>One paired game's state after the last sync, per side: Playnite's fields (mapped) and Blastlog's.</summary>
public sealed class Baseline { public string PlayniteGameId {get;set;} public string EntryId {get;set;} public int Version {get;set;}
  public FieldSet Local {get;set;} = new FieldSet(); public FieldSet Remote {get;set;} = new FieldSet(); public long PlaytimeMinutesSent {get;set;} }

/// <summary>A change to make in Playnite: a status id and/or a 0–100 score (null leaves that field alone).</summary>
public sealed class PlayniteWrite { public string GameId {get;set;} public string StatusId {get;set;} public bool SetScore {get;set;} public int? Score {get;set;} }
public sealed class Conflict { public string PlayniteGameId {get;set;} public string Name {get;set;} public string Field {get;set;} public object Playnite {get;set;} public object Blastlog {get;set;} }

public sealed class SyncPlan {
  public PushRequest Push {get;set;} = new PushRequest();
  public List<PlayniteWrite> PlayniteWrites {get;set;} = new List<PlayniteWrite>();
  public List<Conflict> Conflicts {get;set;} = new List<Conflict>();
  /// <summary>Games (Playnite ids) whose rating Blastlog won't take yet: it needs a finished status first (the site's own
  /// rule). Their rating stays a pending change, so it goes once the game is finished.</summary>
  public List<string> RatingsHeldBack {get;set;} = new List<string>();
  public bool IsEmpty => Push.Count == 0 && PlayniteWrites.Count == 0;
}

/// <summary>
/// Builds one sync from Blastlog's entries, Playnite's games and the baselines (docs/PLAN.md §4.2–4.3). Only paired games
/// sync; ratings are compared as whole stars so 85 in Playnite (4★) is never rewritten as 80; playtime goes one way.
/// </summary>
public static class Planner {
  /// <summary>Statuses Blastlog takes a rating with (done, didn't finish, quit, unplayable).</summary>
  public static readonly HashSet<string> RatableStatuses = new HashSet<string> { "done", "dnf", "quit", "unplayable" };

  public static SyncPlan Plan(IReadOnlyDictionary<string,Entry> entries, IEnumerable<LocalGame> games, IReadOnlyDictionary<string,Baseline> baselines,
    IReadOnlyDictionary<string,string> pairs, StatusMap map, ConflictRule rule, bool includeHidden = false) {
    var plan = new SyncPlan();
    foreach (var game in games) {
      if (game.Hidden && !includeHidden) continue;
      if (!pairs.TryGetValue(game.Id, out var entryId) || !entries.TryGetValue(entryId, out var entry)) continue;
      baselines.TryGetValue(game.Id, out var baseline);
      var remote = new FieldSet { Status = entry.Status, Stars = Mapping.WholeStars(entry.Rating) };
      var local = new FieldSet { Status = map.BlastlogOf(game.StatusId), Stars = Mapping.StarsFromScore(game.UserScore) };
      var outcomes = Merge.Fields(baseline?.Local, baseline?.Remote, remote, local, rule);
      ChangeItem change = null; PlayniteWrite write = null;
      foreach (var outcome in outcomes) {
        if (outcome.Write == Side.Conflict) { plan.Conflicts.Add(new Conflict { PlayniteGameId = game.Id, Name = game.Name, Field = outcome.Field,
          Playnite = outcome.Field == "status" ? (object)local.Status : local.Stars, Blastlog = outcome.Field == "status" ? (object)remote.Status : remote.Stars }); continue; }
        if (outcome.Write == Side.Blastlog) {
          change = change ?? new ChangeItem { EntryId = entry.EntryId, ExpectedVersion = entry.Version };
          if (outcome.Field == "status") change.Status = (string)outcome.Value;
          else { change.SetRating = true; change.RatingValue = (int?)outcome.Value; }
        } else {
          write = write ?? new PlayniteWrite { GameId = game.Id };
          // Already the same meaning (Completed for done) or no Playnite status for it (unplayable): left as it is.
          if (outcome.Field == "status") { var id = map.PlayniteOf((string)outcome.Value); if (id != null && id != game.StatusId && map.BlastlogOf(game.StatusId) != (string)outcome.Value) write.StatusId = id; }
          else {
            var stars = (int?)outcome.Value;
            // Only when the converted value differs: 85 (4★) stays 85 for a 4★ rating.
            if (Mapping.StarsFromScore(game.UserScore) != stars) { write.SetScore = true; write.Score = Mapping.ScoreFromStars(stars); }
          }
        }
      }
      if (change != null && change.SetRating && change.RatingValue != null && !RatableStatuses.Contains(change.Status ?? entry.Status)) {
        change.SetRating = false; change.RatingValue = null; plan.RatingsHeldBack.Add(game.Id);
      }
      if (change != null && (change.Status != null || change.SetRating)) plan.Push.Changes.Add(change);
      if (write != null && (write.StatusId != null || write.SetScore)) plan.PlayniteWrites.Add(write);
      var minutes = game.PlaytimeSeconds / 60;
      if (minutes > 0 && minutes != (baseline?.PlaytimeMinutesSent ?? 0)) plan.Push.Playtime.Add(new PlaytimeItem { EntryId = entry.EntryId, Minutes = minutes, LastPlayedAt = game.LastActivity });
    }
    return plan;
  }

  /// <summary>The baseline after a sync: each side's fields as they now stand (Playnite read again after its writes).</summary>
  public static Baseline NextBaseline(LocalGame game, Entry entry, StatusMap map, int version, long playtimeMinutesSent) =>
    new Baseline { PlayniteGameId = game.Id, EntryId = entry.EntryId, Version = version, PlaytimeMinutesSent = playtimeMinutesSent,
      Local = new FieldSet { Status = map.BlastlogOf(game.StatusId), Stars = Mapping.StarsFromScore(game.UserScore) },
      Remote = new FieldSet { Status = entry.Status, Stars = Mapping.WholeStars(entry.Rating) } };
}

/// <summary>A row of the pairing screen (docs/PLAN.md §4.1). Exact matches start ticked; everything else is the user's call.</summary>
public sealed class PairingChoice {
  public LocalGame Game {get;set;} public MatchResult Match {get;set;} public bool Ticked {get;set;}
  public string Action => Match.Result == "linked" ? "Already paired" : Match.Result == "entry" ? "Pair with your Blastlog entry"
    : Match.Result == "catalogue" ? "Add to Blastlog" : Match.Result == "skipped" ? "Couldn't send" : "Send to Blastlog's import";
  /// <summary>Why a game couldn't be sent (Match.Result "skipped"), in plain words.</summary>
  public string Problem {get;set;}
}
public static class Pairing {
  public static List<PairingChoice> Choices(IEnumerable<LocalGame> games, IEnumerable<MatchResult> results) {
    var byId = results.ToDictionary(r => r.PlayniteGameId);
    return games.Where(g => byId.ContainsKey(g.Id)).Select(g => { var m = byId[g.Id];
      return new PairingChoice { Game = g, Match = m, Ticked = m.Result == "entry" && m.Confidence == "exact" }; }).ToList();
  }
  /// <summary>What Apply sends: links for paired entries, adds for catalogue games, unmatched for the import.</summary>
  public static PushRequest ToPush(IEnumerable<PairingChoice> choices) {
    var push = new PushRequest();
    foreach (var c in choices.Where(c => c.Ticked)) {
      var g = c.Game;
      if (c.Match.Result == "entry") push.Links.Add(new LinkItem { PlayniteGameId = g.Id, EntryId = c.Match.EntryId });
      else if (c.Match.Result == "catalogue") push.Adds.Add(new AddItem { PlayniteGameId = g.Id, GameId = c.Match.GameId,
        Game = new AddGame { Title = Clean(g.Name, 300), Source = Clean(g.Source, 60), StoreGameId = StoreId(g.StoreGameId), ReleaseYear = Year(g.ReleaseYear), Platforms = PlatformList(g.Platforms) } });
      else if (c.Match.Result == "unknown") push.Unmatched.Add(new UnmatchedItem { PlayniteGameId = g.Id, Title = Clean(g.Name, 300), Source = Clean(g.Source, 60), StoreGameId = StoreId(g.StoreGameId) });
    }
    return push;
  }
  /// <summary>
  /// A game as the server's match takes it, cleaned to its limits (Blastlog checks them strictly and refuses the whole
  /// batch otherwise): title 1–300 characters, source 60, store id 100 (longer ones, like an emulated game's file path,
  /// are left out), release year 1950–2100, up to 20 platforms of 100 characters. Null when the game has no name.
  /// </summary>
  public static MatchGame ToMatch(LocalGame g) {
    var title = Clean(g.Name, 300);
    if (title == null) return null;
    return new MatchGame { PlayniteGameId = g.Id, Title = title, Platforms = PlatformList(g.Platforms), Source = Clean(g.Source, 60),
      PluginId = Clean(g.PluginId, 100), StoreGameId = StoreId(g.StoreGameId), ReleaseYear = Year(g.ReleaseYear) };
  }
  static string Clean(string value, int max) { var t = value?.Trim(); return string.IsNullOrEmpty(t) ? null : t.Length > max ? t.Substring(0, max) : t; }
  static string StoreId(string id) { var t = id?.Trim(); return string.IsNullOrEmpty(t) || t.Length > 100 ? null : t; }
  static int? Year(int? year) => year >= 1950 && year <= 2100 ? year : null;
  static string[] PlatformList(string[] platforms) => (platforms ?? new string[0]).Select(p => Clean(p, 100)).Where(p => p != null).Distinct().Take(20).ToArray();
}
}
