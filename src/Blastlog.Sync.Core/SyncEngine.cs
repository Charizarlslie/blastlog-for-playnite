using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
namespace Blastlog.Sync.Core {
/// <summary>The Playnite side, behind an interface so the engine is tested without Playnite (the plugin implements it).</summary>
public interface IPlayniteLibrary {
  IList<LocalGame> Games();
  /// <summary>The user's completion statuses: id → name.</summary>
  IList<KeyValuePair<string,string>> Statuses();
  /// <summary>Writes the mapped fields in one buffered update, never anything else.</summary>
  void Apply(IList<PlayniteWrite> writes);
}

public sealed class LastRun { public string RunId {get;set;} public DateTimeOffset At {get;set;} public PushCounts Counts {get;set;} public int PlayniteWrites {get;set;} public int Conflicts {get;set;} }

/// <summary>The plugin's local state (docs/PLAN.md §4.5): pairs, baselines, the library cursor and cache, the last run. No token.</summary>
public sealed class SyncStore {
  public Dictionary<string,string> Pairs {get;set;} = new Dictionary<string,string>();
  public Dictionary<string,Baseline> Baselines {get;set;} = new Dictionary<string,Baseline>();
  public Dictionary<string,Entry> Entries {get;set;} = new Dictionary<string,Entry>();
  public string Cursor {get;set;} public StatusMap StatusMap {get;set;} public bool FirstSyncDone {get;set;} public LastRun LastRun {get;set;}
  public static SyncStore Load(string path) {
    if (!File.Exists(path)) return new SyncStore();
    try { return JsonConvert.DeserializeObject<SyncStore>(File.ReadAllText(path)) ?? new SyncStore(); }
    catch (JsonException) { File.Copy(path, path + ".broken", true); return new SyncStore(); }
  }
  /// <summary>Written to a temporary file first, so a crash mid-write never leaves half a store.</summary>
  public void Save(string path) {
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
    var temp = path + ".tmp";
    File.WriteAllText(temp, JsonConvert.SerializeObject(this, Formatting.Indented));
    if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
  }
}

public sealed class SyncOutcome { public SyncPlan Plan {get;set;} public PushResponse Response {get;set;} public bool DryRun {get;set;} public int PlayniteWrites {get;set;} public List<string> Notes {get;set;} = new List<string>(); }

/// <summary>
/// One sync, start to end: refresh Blastlog's library (from the cursor), plan, push (or dry-run), write Playnite's side,
/// and move the baselines. A version conflict re-reads the library and plans those games again once. The first real
/// sync must follow a preview (the UI enforces it with <see cref="SyncStore.FirstSyncDone"/>).
/// </summary>
public sealed class SyncEngine {
  readonly BlastlogClient client; readonly IPlayniteLibrary playnite; readonly SyncStore store; readonly ClientInfo info;
  public ConflictRule Rule {get;set;} = ConflictRule.BlastlogWins; public bool IncludeHidden {get;set;}
  public SyncEngine(BlastlogClient client, IPlayniteLibrary playnite, SyncStore store, ClientInfo info) { this.client = client; this.playnite = playnite; this.store = store; this.info = info; }

  StatusMap Map() => store.StatusMap ??= Mapping.FromNames(playnite.Statuses());

  public async Task RefreshAsync(CancellationToken ct = default) {
    var cursor = store.Cursor;
    while (true) {
      var page = (await client.LibraryAsync(cursor, 500, ct).ConfigureAwait(false)).Value;
      foreach (var e in page.Items) store.Entries[e.EntryId] = e;
      foreach (var r in page.Removed) store.Entries.Remove(r.EntryId);
      foreach (var e in page.Items) if (e.Link?.PlayniteGameId != null) store.Pairs[e.Link.PlayniteGameId] = e.EntryId;
      cursor = page.NextCursor ?? cursor;
      if (!page.HasMore) break;
    }
    store.Cursor = cursor;
    // A pair whose entry is gone (removed on the site) is dropped here; nothing in Playnite is touched.
    foreach (var gone in store.Pairs.Where(p => !store.Entries.ContainsKey(p.Value)).Select(p => p.Key).ToList()) { store.Pairs.Remove(gone); store.Baselines.Remove(gone); }
  }

  public SyncPlan Plan() => Planner.Plan(store.Entries, playnite.Games(), store.Baselines, store.Pairs, Map(), Rule, IncludeHidden);

  public async Task<SyncOutcome> SyncAsync(bool dryRun, CancellationToken ct = default) {
    await RefreshAsync(ct).ConfigureAwait(false);
    var plan = Plan();
    var outcome = new SyncOutcome { Plan = plan, DryRun = dryRun };
    plan.Push.DryRun = dryRun; plan.Push.Client = info;
    if (plan.Push.Count > 0) outcome.Response = await PushChunked(plan.Push, ct).ConfigureAwait(false);
    if (dryRun) return outcome;
    // Conflicts: the entry moved on the site since the last read. Read again and plan those games once more.
    if (outcome.Response != null && outcome.Response.Results.Any(r => r.Result == "conflict")) {
      await RefreshAsync(ct).ConfigureAwait(false);
      var conflicted = new HashSet<string>(outcome.Response.Results.Where(r => r.Result == "conflict").Select(r => r.Ref));
      var retry = Plan();
      retry.Push.Changes = retry.Push.Changes.Where(c => conflicted.Contains(c.EntryId)).ToList(); retry.Push.Playtime.Clear(); retry.Push.Client = info;
      if (retry.Push.Changes.Count > 0) {
        var second = await PushChunked(retry.Push, ct).ConfigureAwait(false);
        outcome.Notes.Add(second.Counts.Conflict > 0 ? $"{second.Counts.Conflict} game(s) changed on Blastlog while syncing; they'll sync next time." : "Re-read changed games and synced them.");
      }
    }
    playnite.Apply(plan.PlayniteWrites);
    outcome.PlayniteWrites = plan.PlayniteWrites.Count;
    await RefreshAsync(ct).ConfigureAwait(false);
    var games = playnite.Games().ToDictionary(g => g.Id);
    var sentPlaytime = plan.Push.Playtime.ToDictionary(p => p.EntryId, p => p.Minutes);
    var asked = new HashSet<string>(plan.Conflicts.Select(c => c.PlayniteGameId));
    foreach (var pair in store.Pairs) {
      if (asked.Contains(pair.Key) || !games.TryGetValue(pair.Key, out var game) || !store.Entries.TryGetValue(pair.Value, out var entry)) continue;
      store.Baselines.TryGetValue(pair.Key, out var old);
      var minutes = sentPlaytime.TryGetValue(entry.EntryId, out var m) ? m : old?.PlaytimeMinutesSent ?? 0;
      var next = Planner.NextBaseline(game, entry, Map(), entry.Version, minutes);
      // A rating held back stays a change for next time (the baseline keeps what Playnite had before it).
      if (plan.RatingsHeldBack.Contains(pair.Key)) next.Local.Stars = old?.Local?.Stars;
      store.Baselines[pair.Key] = next;
    }
    store.FirstSyncDone = true;
    store.LastRun = new LastRun { RunId = outcome.Response?.RunId, At = DateTimeOffset.Now, Counts = outcome.Response?.Counts ?? new PushCounts(), PlayniteWrites = outcome.PlayniteWrites, Conflicts = plan.Conflicts.Count };
    return outcome;
  }

  /// <summary>The server takes at most 200 items per list; bigger pushes go as several runs.</summary>
  async Task<PushResponse> PushChunked(PushRequest push, CancellationToken ct) {
    const int max = 200;
    if (push.Changes.Count <= max && push.Playtime.Count <= max && push.Links.Count <= max && push.Adds.Count <= max && push.Unmatched.Count <= max)
      return (await client.PushAsync(push, ct).ConfigureAwait(false)).Value;
    var total = new PushResponse { DryRun = push.DryRun };
    for (var i = 0; ; i++) {
      var part = new PushRequest { DryRun = push.DryRun, Client = push.Client, Origin = push.Origin,
        Links = push.Links.Skip(i * max).Take(max).ToList(), Changes = push.Changes.Skip(i * max).Take(max).ToList(), Adds = push.Adds.Skip(i * max).Take(max).ToList(),
        Playtime = push.Playtime.Skip(i * max).Take(max).ToList(), Unmatched = push.Unmatched.Skip(i * max).Take(max).ToList() };
      if (part.Count == 0) break;
      var r = (await client.PushAsync(part, ct).ConfigureAwait(false)).Value;
      total.RunId = total.RunId ?? r.RunId; total.Results.AddRange(r.Results);
      total.Counts.Applied += r.Counts.Applied; total.Counts.Unchanged += r.Counts.Unchanged; total.Counts.Conflict += r.Counts.Conflict; total.Counts.Rejected += r.Counts.Rejected;
    }
    return total;
  }

  /// <summary>
  /// Matches every (visible) Playnite game in batches of 200. Reads only. One game the server can't read never sinks
  /// its batch: a refused batch is split in halves until that game is found, and it comes back as "Couldn't send".
  /// </summary>
  public async Task<List<PairingChoice>> PairingAsync(CancellationToken ct = default) {
    var games = playnite.Games().Where(g => IncludeHidden || !g.Hidden).ToList();
    var results = new List<MatchResult>(); var problems = new Dictionary<string,string>();
    var sendable = new List<MatchGame>();
    foreach (var g in games) { var m = Pairing.ToMatch(g); if (m == null) problems[g.Id] = "it has no name in Playnite"; else sendable.Add(m); }
    for (var i = 0; i < sendable.Count; i += 200) await MatchBatch(sendable.Skip(i).Take(200).ToList(), results, problems, ct).ConfigureAwait(false);
    foreach (var p in problems) results.Add(new MatchResult { PlayniteGameId = p.Key, Result = "skipped" });
    var choices = Pairing.Choices(games, results);
    foreach (var c in choices) if (problems.TryGetValue(c.Game.Id, out var why)) c.Problem = why;
    return choices;
  }
  async Task MatchBatch(List<MatchGame> batch, List<MatchResult> results, Dictionary<string,string> problems, CancellationToken ct) {
    try { results.AddRange((await client.MatchAsync(batch, ct).ConfigureAwait(false)).Value.Results); }
    catch (BlastlogException e) when (e.StatusCode == 400) {
      if (batch.Count == 1) { problems[batch[0].PlayniteGameId] = "Blastlog couldn't read its details" + (e.Details != null ? " (" + e.Details + ")" : ""); return; }
      var half = batch.Count / 2;
      await MatchBatch(batch.Take(half).ToList(), results, problems, ct).ConfigureAwait(false);
      await MatchBatch(batch.Skip(half).ToList(), results, problems, ct).ConfigureAwait(false);
    }
  }

  /// <summary>Apply on the pairing screen: one real push of the ticked rows, then the new pairs are read back.</summary>
  public async Task<PushResponse> ApplyPairingAsync(IEnumerable<PairingChoice> choices, CancellationToken ct = default) {
    var push = Pairing.ToPush(choices); push.Client = info;
    if (push.Count == 0) return new PushResponse();
    var response = await PushChunked(push, ct).ConfigureAwait(false);
    await RefreshAsync(ct).ConfigureAwait(false);
    return response;
  }

  /// <summary>Unpairs one game here and on the server; its entry and the Playnite game are untouched.</summary>
  public async Task UnpairAsync(string playniteGameId, CancellationToken ct = default) {
    try { await client.UnlinkAsync(playniteGameId, ct).ConfigureAwait(false); } catch (BlastlogException e) when (e.StatusCode == 404) { }
    store.Pairs.Remove(playniteGameId); store.Baselines.Remove(playniteGameId);
  }
}
}
