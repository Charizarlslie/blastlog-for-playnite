using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Blastlog.Sync.Core;
using FakeBlastlog;
using Xunit;

public class ClientTests {
  [Fact] public async Task ReadsOverRealLoopbackHttp() {
    var state = new FakeState(); state.Add("Sample game");
    await using var server = Server.Create(state); await server.StartAsync();
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    var client = new BlastlogClient(http, new Uri(server.Urls.Single()), Server.Token);
    var me = await client.MeAsync(); Assert.Equal("Sample player", (string)me.Value["user"]["displayName"]);
    var library = await client.LibraryAsync(); Assert.Single(library.Value.Items); Assert.Equal("Sample game", library.Value.Items[0].Game.Title); Assert.False(library.Value.HasMore);
    await server.StopAsync();
  }
  [Fact] public async Task ReportsUnauthorizedWithoutLeakingToken() {
    await using var server = Server.Create(); await server.StartAsync();
    using var http = new HttpClient(); var client = new BlastlogClient(http, new Uri(server.Urls.Single()), "deliberately-invalid");
    var error = await Assert.ThrowsAsync<BlastlogException>(() => client.MeAsync());
    Assert.Equal(401, error.StatusCode); Assert.Equal("sync_token_invalid", error.Error); Assert.DoesNotContain("deliberately-invalid", error.ToString());
    await server.StopAsync();
  }
  [Fact] public void RejectsInsecureRemoteServer() { using var http = new HttpClient(); Assert.Throws<ArgumentException>(() => new BlastlogClient(http, new Uri("http://example.com"), "fake")); }
  [Fact] public void SendsARatingClearOnlyWhenAsked() {
    var json = Newtonsoft.Json.JsonConvert.SerializeObject(new ChangeItem { EntryId = "e", ExpectedVersion = 2, Status = "done" }, new Newtonsoft.Json.JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(), NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore });
    Assert.DoesNotContain("rating", json);
    var clear = Newtonsoft.Json.JsonConvert.SerializeObject(new ChangeItem { EntryId = "e", ExpectedVersion = 2, SetRating = true, RatingValue = null }, new Newtonsoft.Json.JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(), NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore });
    Assert.Contains("\"rating\":null", clear);
  }
}

public class MappingTests {
  [Theory] [InlineData(85, 4)] [InlineData(90, 5)] [InlineData(10, 1)] [InlineData(1, 1)] [InlineData(100, 5)] [InlineData(50, 3)] [InlineData(69, 3)] [InlineData(70, 4)]
  public void ConvertsScoresToWholeStars(int score, int stars) => Assert.Equal(stars, Mapping.StarsFromScore(score));
  [Fact] public void NoScoreIsNoRating() { Assert.Null(Mapping.StarsFromScore(0)); Assert.Null(Mapping.StarsFromScore(null)); Assert.Equal(80, Mapping.ScoreFromStars(4)); }
  [Fact] public void MapsTheUsersOwnStatusesByTheDefaultNames() {
    var map = Mapping.FromNames(new[] { KV("a", "Beaten"), KV("b", "Completed"), KV("c", "Playing"), KV("d", "My own status"), KV("e", "Abandoned") });
    Assert.Equal("done", map.BlastlogOf("a")); Assert.Equal("done", map.BlastlogOf("b")); Assert.Null(map.BlastlogOf("d"));
    Assert.Equal("a", map.PlayniteOf("done")); Assert.Equal("e", map.PlayniteOf("quit")); Assert.Equal("e", map.PlayniteOf("dnf")); Assert.Null(map.PlayniteOf("unplayable"));
  }
  internal static KeyValuePair<string,string> KV(string k, string v) => new KeyValuePair<string,string>(k, v);
}

public class MergeTests {
  static FieldSet F(string status, int? stars) => new FieldSet { Status = status, Stars = stars };
  [Fact] public void OnlyPlayniteChangedPushes() {
    var o = Merge.Fields(F("playing", 4), F("playing", 4), F("playing", 4), F("done", 4), ConflictRule.BlastlogWins);
    Assert.Single(o); Assert.Equal(Side.Blastlog, o[0].Write); Assert.Equal("done", o[0].Value);
  }
  [Fact] public void OnlyBlastlogChangedWritesPlaynite() {
    var o = Merge.Fields(F("playing", 4), F("playing", 4), F("playing", 5), F("playing", 4), ConflictRule.BlastlogWins);
    Assert.Single(o); Assert.Equal(Side.Playnite, o[0].Write); Assert.Equal(5, o[0].Value);
  }
  [Fact] public void BothToTheSameValueIsQuiet() => Assert.Empty(Merge.Fields(F("playing", 4), F("playing", 4), F("done", 4), F("done", 4), ConflictRule.AskMe));
  [Theory] [InlineData(ConflictRule.BlastlogWins, Side.Playnite, "quit")] [InlineData(ConflictRule.PlayniteWins, Side.Blastlog, "done")] [InlineData(ConflictRule.AskMe, Side.Conflict, null)]
  public void BothDifferentFollowsTheRule(ConflictRule rule, Side side, string value) {
    var o = Merge.Fields(F("playing", 4), F("playing", 4), F("quit", 4), F("done", 4), rule);
    Assert.Single(o); Assert.Equal(side, o[0].Write); Assert.Equal(value, o[0].Value); Assert.True(o[0].Conflict);
  }
  [Fact] public void AnUnmappedPlayniteStatusIsNoOpinion() => Assert.Empty(Merge.Fields(F("started", null), F("unplayable", null), F("unplayable", null), F(null, null), ConflictRule.PlayniteWins));
  [Fact] public void AFirstSyncNeverClearsARating() {
    var o = Merge.Fields(null, null, F("done", 4), F("done", null), ConflictRule.PlayniteWins);
    Assert.Single(o); Assert.Equal(Side.Playnite, o[0].Write); Assert.Equal(4, o[0].Value);
    var back = Merge.Fields(null, null, F("done", null), F("done", 3), ConflictRule.BlastlogWins);
    Assert.Single(back); Assert.Equal(Side.Blastlog, back[0].Write); Assert.Equal(3, back[0].Value);
  }
}

public class PlannerTests {
  static readonly StatusMap Map = Mapping.FromNames(new[] { MappingTests.KV("np", "Not Played"), MappingTests.KV("pl", "Playing"), MappingTests.KV("bt", "Beaten"), MappingTests.KV("cp", "Completed"), MappingTests.KV("ab", "Abandoned") });
  static Entry E(string id, string status, double? rating, int version = 3) => new Entry { EntryId = id, Status = status, Rating = rating, Version = version, Game = new Game { Title = id } };
  static LocalGame G(string id, string status, int? score, long playtime = 0, bool hidden = false) => new LocalGame { Id = id, Name = id, StatusId = status, UserScore = score, PlaytimeSeconds = playtime, Hidden = hidden };
  static SyncPlan Plan(Entry[] entries, LocalGame[] games, Dictionary<string,Baseline> baselines = null, ConflictRule rule = ConflictRule.BlastlogWins) =>
    Planner.Plan(entries.ToDictionary(e => e.EntryId), games, baselines ?? new Dictionary<string,Baseline>(), games.ToDictionary(g => g.Id, g => "e-" + g.Id), Map, rule);

  [Fact] public void EightyFiveStaysEightyFiveForFourStars() {
    var plan = Plan(new[] { E("e-a", "done", 4) }, new[] { G("a", "bt", 85) });
    Assert.True(plan.IsEmpty);
  }
  [Fact] public void CompletedIsAlreadyDoneSoPlayniteIsLeftAlone() => Assert.True(Plan(new[] { E("e-a", "done", null) }, new[] { G("a", "cp", null) }).IsEmpty);
  [Fact] public void HoldsBackARatingUntilTheGameIsFinished() {
    var b = new Dictionary<string,Baseline> { ["a"] = new Baseline { Local = new FieldSet { Status = "playing" }, Remote = new FieldSet { Status = "playing" } } };
    var plan = Plan(new[] { E("e-a", "playing", null) }, new[] { G("a", "pl", 80) }, b);
    Assert.Empty(plan.Push.Changes); Assert.Equal(new[] { "a" }, plan.RatingsHeldBack);
    var finished = Plan(new[] { E("e-a", "playing", null) }, new[] { G("a", "bt", 80) }, b);
    var change = Assert.Single(finished.Push.Changes); Assert.Equal("done", change.Status); Assert.True(change.SetRating); Assert.Equal(4, change.RatingValue); Assert.Equal(3, change.ExpectedVersion);
  }
  [Fact] public void WritesBlastlogsChangesToPlaynite() {
    var b = new Dictionary<string,Baseline> { ["a"] = new Baseline { Local = new FieldSet { Status = "not_started" }, Remote = new FieldSet { Status = "not_started" } } };
    var write = Assert.Single(Plan(new[] { E("e-a", "done", 5) }, new[] { G("a", "np", null) }, b).PlayniteWrites);
    Assert.Equal("bt", write.StatusId); Assert.True(write.SetScore); Assert.Equal(100, write.Score);
  }
  [Fact] public void SendsPlaytimeOnlyWhenItMovedAndSkipsHiddenGames() {
    var b = new Dictionary<string,Baseline> { ["a"] = new Baseline { Local = new FieldSet { Status = "playing" }, Remote = new FieldSet { Status = "playing" }, PlaytimeMinutesSent = 60 } };
    Assert.Empty(Plan(new[] { E("e-a", "playing", null) }, new[] { G("a", "pl", null, 3600) }, b).Push.Playtime);
    Assert.Equal(90, Assert.Single(Plan(new[] { E("e-a", "playing", null) }, new[] { G("a", "pl", null, 5400) }, b).Push.Playtime).Minutes);
    Assert.True(Plan(new[] { E("e-a", "done", 5) }, new[] { G("a", "np", null, 0, hidden: true) }).IsEmpty);
  }
  [Fact] public void AskMeReportsTheConflictAndChangesNothing() {
    var b = new Dictionary<string,Baseline> { ["a"] = new Baseline { Local = new FieldSet { Status = "playing" }, Remote = new FieldSet { Status = "playing" } } };
    var plan = Plan(new[] { E("e-a", "quit", null) }, new[] { G("a", "bt", null) }, b, ConflictRule.AskMe);
    Assert.True(plan.IsEmpty); var c = Assert.Single(plan.Conflicts); Assert.Equal("status", c.Field); Assert.Equal("done", c.Playnite); Assert.Equal("quit", c.Blastlog);
  }
}

/// <summary>A Playnite stand-in: games in a list, writes applied to them.</summary>
sealed class FakePlaynite : IPlayniteLibrary {
  public readonly List<LocalGame> List = new(); public readonly List<PlayniteWrite> Written = new();
  public IList<LocalGame> Games() => List.Select(g => new LocalGame { Id = g.Id, Name = g.Name, StatusId = g.StatusId, UserScore = g.UserScore, PlaytimeSeconds = g.PlaytimeSeconds, Hidden = g.Hidden, Source = g.Source, StoreGameId = g.StoreGameId }).ToList();
  public IList<KeyValuePair<string,string>> Statuses() => new[] { MappingTests.KV("np", "Not Played"), MappingTests.KV("pl", "Playing"), MappingTests.KV("bt", "Beaten"), MappingTests.KV("ab", "Abandoned") };
  public void Apply(IList<PlayniteWrite> writes) { foreach (var w in writes) { var g = List.First(x => x.Id == w.GameId); if (w.StatusId != null) g.StatusId = w.StatusId; if (w.SetScore) g.UserScore = w.Score; Written.Add(w); } }
}

public class EngineTests {
  static async Task<(FakeState state, FakePlaynite playnite, SyncEngine engine, SyncStore store, Func<Task> stop)> Setup() {
    var state = new FakeState(); var server = Server.Create(state); await server.StartAsync();
    var client = new BlastlogClient(new HttpClient(), new Uri(server.Urls.Single()), Server.Token);
    var playnite = new FakePlaynite(); var store = new SyncStore();
    return (state, playnite, new SyncEngine(client, playnite, store, new ClientInfo { Version = "10.63", Plugin = "test" }), store, async () => await server.DisposeAsync());
  }

  [Fact] public async Task PairsThenSyncsBothWaysWithAPreviewThatWritesNothing() {
    var (state, playnite, engine, store, stop) = await Setup();
    try {
      var hades = state.Add("Hades", "done", 5); var celeste = state.Add("Celeste", "playing"); var tunic = state.Add("Tunic", steamAppId: "553420");
      state.Catalogue["ow"] = ("cat-ow", "Outer Wilds", 2019);
      playnite.List.AddRange(new[] { new LocalGame { Id = "p-hades", Name = "Hades", StatusId = "pl" }, new LocalGame { Id = "p-celeste", Name = "Celeste", StatusId = "bt", UserScore = 90, PlaytimeSeconds = 7200 },
        new LocalGame { Id = "p-tunic", Name = "TUNIC (2022)", Source = "Steam", StoreGameId = "553420", StatusId = "np" }, new LocalGame { Id = "p-ow", Name = "Outer Wilds", StatusId = "np" },
        new LocalGame { Id = "p-none", Name = "Something Unknown" } });
      var choices = await engine.PairingAsync();
      Assert.Equal(new[] { "entry", "entry", "entry", "catalogue", "unknown" }, choices.Select(c => c.Match.Result));
      Assert.Equal(new[] { true, true, true, false, false }, choices.Select(c => c.Ticked));
      foreach (var c in choices) c.Ticked = true;
      var paired = await engine.ApplyPairingAsync(choices);
      Assert.Equal(0, paired.Counts.Rejected);
      Assert.Equal(4, store.Pairs.Count); Assert.Equal(new[] { "Something Unknown" }, state.Unmatched);

      // The preview: nothing changes on either side.
      var versions = state.Entries.Select(e => e.Version).ToList();
      var preview = await engine.SyncAsync(dryRun: true);
      Assert.Equal(versions, state.Entries.Select(e => e.Version).ToList()); Assert.Empty(playnite.Written); Assert.False(store.FirstSyncDone);
      Assert.Contains(preview.Plan.Push.Playtime, p => p.EntryId == celeste.Id && p.Minutes == 120);
      // Celeste's 5★ from Playnite waits: Blastlog takes a rating once the game is finished.
      Assert.Contains("p-celeste", preview.Plan.RatingsHeldBack);

      var first = await engine.SyncAsync(dryRun: false);
      // Blastlog wins a first-sync disagreement: Hades is done (5★) on the site, so Playnite gets Beaten and 100.
      Assert.Equal("bt", playnite.List.First(g => g.Id == "p-hades").StatusId); Assert.Equal(100, playnite.List.First(g => g.Id == "p-hades").UserScore);
      // Celeste: the site said playing, Playnite Beaten 90; Blastlog wins the status, Playnite's rating fills the empty one only once it's finished.
      Assert.Equal("pl", playnite.List.First(g => g.Id == "p-celeste").StatusId);
      Assert.Equal(new[] { 120L }, state.Playtime.Where(p => p.entryId == celeste.Id).Select(p => p.minutes));
      Assert.True(store.FirstSyncDone);

      // Later: Celeste finished in Playnite, Tunic started on the site. Each side gets the other's change.
      playnite.List.First(g => g.Id == "p-celeste").StatusId = "bt";
      state.SiteChange(tunic, e => e.Status = "playing");
      var second = await engine.SyncAsync(dryRun: false);
      Assert.Equal("done", celeste.Status); Assert.Equal(5, celeste.Rating);
      Assert.Equal("pl", playnite.List.First(g => g.Id == "p-tunic").StatusId);
      // And a third sync has nothing to do: no flip-flop.
      var third = await engine.SyncAsync(dryRun: false);
      Assert.True(third.Plan.IsEmpty, string.Join(",", third.Plan.Push.Changes.Select(c => c.EntryId + ":" + c.Status)));
    } finally { await stop(); }
  }

  [Fact] public async Task AConflictOnTheServerIsReadAgainAndRetriedOnce() {
    var (state, playnite, engine, store, stop) = await Setup();
    try {
      var hades = state.Add("Hades", "playing");
      playnite.List.Add(new LocalGame { Id = "p-hades", Name = "Hades", StatusId = "pl" });
      await engine.ApplyPairingAsync(await engine.PairingAsync());
      await engine.SyncAsync(false);
      // Playnite finishes it while the site changes the entry in the same moment: the first push conflicts.
      playnite.List[0].StatusId = "bt"; playnite.List[0].UserScore = 80;
      state.Race(hades);
      var outcome = await engine.SyncAsync(false);
      Assert.Equal(1, outcome.Response.Counts.Conflict);
      Assert.Equal("done", hades.Status); Assert.Equal(4, hades.Rating);
      Assert.Contains(outcome.Notes, n => n.StartsWith("Re-read"));
    } finally { await stop(); }
  }

  [Fact] public async Task OneGameTheServerRefusesNeverSinksItsBatch() {
    var (state, playnite, engine, store, stop) = await Setup();
    try {
      state.Add("Hades"); state.RefuseTitles.Add("Cursed Name");
      playnite.List.AddRange(new[] { new LocalGame { Id = "a", Name = "Hades" }, new LocalGame { Id = "b", Name = "Cursed Name" }, new LocalGame { Id = "c", Name = "Celeste" },
        new LocalGame { Id = "d", Name = "  " }, new LocalGame { Id = "e", Name = "Rom Game", StoreGameId = new string('x', 300), ReleaseYear = 1900 } });
      var choices = await engine.PairingAsync();
      Assert.Equal(new[] { "entry", "skipped", "unknown", "skipped", "unknown" }, choices.Select(c => c.Match.Result));
      Assert.Equal("it has no name in Playnite", choices.First(c => c.Game.Id == "d").Problem);
      Assert.StartsWith("Blastlog couldn't read its details", choices.First(c => c.Game.Id == "b").Problem);
      // Skipped games never go in the Apply push.
      foreach (var c in choices) c.Ticked = true;
      var push = Pairing.ToPush(choices);
      Assert.DoesNotContain(push.Unmatched, u => u.PlayniteGameId == "b" || u.PlayniteGameId == "d");
      // The long store id and the impossible year were cleaned, not sent.
      Assert.Null(Assert.Single(push.Unmatched, u => u.PlayniteGameId == "e").StoreGameId);
    } finally { await stop(); }
  }
  [Fact] public void CleansGamesToTheServersLimits() {
    var m = Pairing.ToMatch(new LocalGame { Id = "g", Name = "  " + new string('a', 400) + " ", Source = new string('s', 80), StoreGameId = new string('9', 101), ReleaseYear = 2200,
      Platforms = Enumerable.Range(0, 30).Select(i => "P" + i).Concat(new[] { "", null, "P1" }).ToArray() });
    Assert.Equal(300, m.Title.Length); Assert.Equal(60, m.Source.Length); Assert.Null(m.StoreGameId); Assert.Null(m.ReleaseYear); Assert.Equal(20, m.Platforms.Length);
    Assert.Null(Pairing.ToMatch(new LocalGame { Id = "x", Name = null }));
    Assert.Equal(1985, Pairing.ToMatch(new LocalGame { Id = "y", Name = "Old", ReleaseYear = 1985, StoreGameId = "553420" }).ReleaseYear);
  }
  [Fact] public void KeepsTheStoreInAFileAndSurvivesABrokenOne() {
    var dir = Path.Combine(Path.GetTempPath(), "blastlog-store-" + Guid.NewGuid()); var path = Path.Combine(dir, "sync.json");
    var store = new SyncStore { Cursor = "c1" }; store.Pairs["p"] = "e"; store.Save(path); store.Save(path);
    Assert.Equal("e", SyncStore.Load(path).Pairs["p"]);
    File.WriteAllText(path, "{ not json"); Assert.Empty(SyncStore.Load(path).Pairs); Assert.True(File.Exists(path + ".broken"));
    Directory.Delete(dir, true);
  }
}
