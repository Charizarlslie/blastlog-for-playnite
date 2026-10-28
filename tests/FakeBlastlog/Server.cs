using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
namespace FakeBlastlog {
/// <summary>
/// An in-memory stand-in for Blastlog's /api/sync/v1 (the server's docs/PLAYNITE_SYNC.md): bearer token check, library
/// with a cursor, match, push (dry run, version checks, links, adds, playtime, unmatched), undo and unlink. Enough of the
/// real rules that the engine's tests mean something; no network beyond loopback.
/// </summary>
public sealed class FakeState {
  public sealed class FakeEntry { public string Id = Guid.NewGuid().ToString(); public int Version = 1; public long Clock; public string GameId; public string Title; public int? Year;
    public string Status = "not_started"; public int? Rating; public string Ownership = "library"; public string? FinishedOn; public bool Deleted; public string? SteamAppId;
    /// <summary>Changed on the site after the plugin's last read: shows in the library only once a push has hit the conflict.</summary>
    public bool Racing; }
  public readonly List<FakeEntry> Entries = new(); public readonly Dictionary<string,string> Links = new();
  public readonly Dictionary<string,(string id,string title,int? year)> Catalogue = new();
  public readonly List<JsonObject> Pushes = new(); public readonly Dictionary<string,JsonObject> Runs = new(); public readonly List<(string entryId,long minutes)> Playtime = new();
  public readonly List<string> Unmatched = new(); public long Clock = 1;
  /// <summary>Titles the fake refuses as the real server refuses a bad field: the whole batch answers 400.</summary>
  public readonly HashSet<string> RefuseTitles = new(); public int MatchCalls;
  public FakeEntry Add(string title, string status = "not_started", int? rating = null, int? year = null, string? steamAppId = null) {
    var e = new FakeEntry { GameId = Guid.NewGuid().ToString(), Title = title, Status = status, Rating = rating, Year = year, Clock = Clock++, SteamAppId = steamAppId };
    Entries.Add(e); return e; }
  /// <summary>A change made on the site (bumps the version, as the web's mutations do).</summary>
  public void SiteChange(FakeEntry e, Action<FakeEntry> change) { change(e); e.Version++; e.Clock = Clock++; }
  /// <summary>A site change racing the plugin's sync: the version moves now, the library shows it after the conflict.</summary>
  public void Race(FakeEntry e) { e.Version++; e.Racing = true; }
}

public static class Server {
  public const string Token = "fake-test-token";
  static readonly HashSet<string> Ratable = new() { "done", "dnf", "quit", "unplayable" };
  public static WebApplication Create() => Create(new FakeState());
  public static WebApplication Create(FakeState state) {
    var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); var app = builder.Build();
    app.Use(async (context, next) => { if (context.Request.Headers.Authorization != "Bearer " + Token) { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "sync_token_invalid" }); return; } await next(); });
    object Dto(FakeState.FakeEntry e) => new { entryId = e.Id, version = e.Version, updatedAt = DateTimeOffset.UtcNow, game = new { id = e.GameId, title = e.Title, releaseYear = e.Year, steamAppIds = e.SteamAppId == null ? new string[0] : new[] { e.SteamAppId }, gogIds = new string[0] },
      ownership = e.Ownership, status = e.Status, rating = e.Rating, hasReview = false, finishedOn = e.FinishedOn, inBacklog = e.Status is "not_started" or "started" or "playing",
      link = state.Links.Where(l => l.Value == e.Id).Select(l => new { playniteGameId = l.Key }).FirstOrDefault() };
    app.MapGet("/api/sync/v1/me", () => Results.Json(new { user = new { id = "demo", displayName = "Sample player" }, token = new { id = "test", name = "Fake", scopes = new[] { "sync:read", "sync:write" } }, server = new { version = 1, demo = true }, limits = new { maxPushItems = 200 } }));
    app.MapGet("/api/sync/v1/library", (string? since, int? limit) => {
      var from = since == null ? 0 : long.Parse(since);
      var rows = state.Entries.Where(e => e.Clock > from && (since != null || !e.Deleted)).OrderBy(e => e.Clock).Take(limit ?? 500).ToList();
      var more = state.Entries.Count(e => e.Clock > from && (since != null || !e.Deleted)) > rows.Count;
      return Results.Json(new { items = rows.Where(e => !e.Deleted).Select(Dto), removed = rows.Where(e => e.Deleted).Select(e => new { entryId = e.Id, removedAt = DateTimeOffset.UtcNow }),
        nextCursor = (rows.LastOrDefault()?.Clock ?? from).ToString(), hasMore = more }); });
    app.MapPost("/api/sync/v1/match", async (HttpRequest request) => {
      var body = JsonNode.Parse(await new System.IO.StreamReader(request.Body).ReadToEndAsync())!;
      var games = body["games"]!.AsArray(); if (games.Count > 200) return Results.Json(new { error = "too_many_items" }, statusCode: 413);
      state.MatchCalls++;
      // The real server's limits (strict: one bad game refuses the batch).
      bool Bad(JsonNode g) { var t = (string?)g["title"]; var s = (string?)g["storeGameId"]; var y = (int?)g["releaseYear"];
        return string.IsNullOrWhiteSpace(t) || t.Length > 300 || (s != null && s.Length > 100) || (y != null && (y < 1950 || y > 2100)) || state.RefuseTitles.Contains(t!); }
      if (games.Any(g => Bad(g!))) return Results.Json(new { error = "invalid_request", issues = new[] { new { path = new object[] { "games", 0, "title" }, code = "custom" } } }, statusCode: 400);
      return Results.Json(new { results = games.Select(g => { var id = (string)g!["playniteGameId"]!; var title = (string)g["title"]!;
        if (state.Links.TryGetValue(id, out var linked)) return (object)new { playniteGameId = id, result = "linked", entryId = linked };
        var store = (string?)g["storeGameId"]; var bySteam = store == null ? null : state.Entries.FirstOrDefault(e => !e.Deleted && e.SteamAppId == store);
        if (bySteam != null) return new { playniteGameId = id, result = "entry", entryId = bySteam.Id, confidence = "exact" };
        var mine = state.Entries.FirstOrDefault(e => !e.Deleted && string.Equals(e.Title, title, StringComparison.OrdinalIgnoreCase));
        if (mine != null) return new { playniteGameId = id, result = "entry", entryId = mine.Id, confidence = "exact" };
        var cat = state.Catalogue.Values.FirstOrDefault(c => string.Equals(c.title, title, StringComparison.OrdinalIgnoreCase));
        if (cat.id != null) return new { playniteGameId = id, result = "catalogue", gameId = cat.id, title = cat.title, confidence = "exact" };
        return new { playniteGameId = id, result = "unknown" }; }) }); });
    app.MapPost("/api/sync/v1/push", async (HttpRequest request) => {
      var body = JsonNode.Parse(await new System.IO.StreamReader(request.Body).ReadToEndAsync())!.AsObject();
      var dry = (bool?)body["dryRun"] ?? false; var runId = Guid.NewGuid().ToString(); var results = new List<object>(); state.Pushes.Add(body);
      var undo = new JsonArray();
      foreach (var l in body["links"]?.AsArray() ?? new JsonArray()) { var pid = (string)l!["playniteGameId"]!; var eid = (string)l["entryId"]!;
        if (!dry) state.Links[pid] = eid; results.Add(new { kind = "link", @ref = pid, result = "applied" }); }
      foreach (var c in body["changes"]?.AsArray() ?? new JsonArray()) { var eid = (string)c!["entryId"]!; var e = state.Entries.FirstOrDefault(x => x.Id == eid && !x.Deleted);
        if (e == null) { results.Add(new { kind = "change", @ref = eid, result = "rejected", reason = "library_entry_not_found" }); continue; }
        if ((int)c["expectedVersion"]! != e.Version) { if (e.Racing) { e.Racing = false; e.Clock = state.Clock++; } results.Add(new { kind = "change", @ref = eid, result = "conflict", currentVersion = e.Version }); continue; }
        var status = (string?)c["status"] ?? e.Status; var hasRating = c.AsObject().ContainsKey("rating"); var rating = hasRating ? (int?)c["rating"] : e.Rating;
        if (hasRating && rating != null && rating != e.Rating && !Ratable.Contains(status)) { results.Add(new { kind = "change", @ref = eid, result = "rejected", reason = "rate_when_done" }); continue; }
        if (!dry) { undo.Add(new JsonObject { ["entryId"] = e.Id, ["status"] = e.Status, ["rating"] = e.Rating, ["version"] = e.Version + 1 }); e.Status = status; e.Rating = rating; e.Version++; e.Clock = state.Clock++; }
        results.Add(new { kind = "change", @ref = eid, result = "applied" }); }
      foreach (var a in body["adds"]?.AsArray() ?? new JsonArray()) { var pid = (string)a!["playniteGameId"]!; var gid = (string)a["gameId"]!; var title = (string?)a["game"]?["title"];
        var cat = state.Catalogue.Values.FirstOrDefault(x => x.id == gid);
        if (cat.id == null || !string.Equals(cat.title, title, StringComparison.OrdinalIgnoreCase)) { results.Add(new { kind = "add", @ref = pid, result = "rejected", reason = "not_the_matched_game" }); continue; }
        if (!dry) { var e = state.Add(cat.title, (string?)a["status"] ?? "not_started", null, cat.year); e.GameId = gid; state.Links[pid] = e.Id; }
        results.Add(new { kind = "add", @ref = pid, result = "applied" }); }
      foreach (var p in body["playtime"]?.AsArray() ?? new JsonArray()) { if (!dry) state.Playtime.Add(((string)p!["entryId"]!, (long)p["minutes"]!)); results.Add(new { kind = "playtime", @ref = (string)p!["entryId"]!, result = "applied" }); }
      foreach (var u in body["unmatched"]?.AsArray() ?? new JsonArray()) { if (!dry) state.Unmatched.Add((string)u!["title"]!); results.Add(new { kind = "unmatched", @ref = (string)u!["playniteGameId"]!, result = "applied" }); }
      var json = JsonSerializer.SerializeToNode(results)!.AsArray();
      int Count(string r) => json.Count(x => (string)x!["result"]! == r);
      if (!dry) state.Runs[runId] = new JsonObject { ["undo"] = undo, ["state"] = "applied" };
      return Results.Json(new { runId = dry ? null : runId, dryRun = dry, results = json, counts = new { applied = Count("applied"), unchanged = Count("unchanged"), conflict = Count("conflict"), rejected = Count("rejected") } }); });
    app.MapPost("/api/sync/v1/runs/{id}/undo", (string id) => {
      if (!state.Runs.TryGetValue(id, out var run) || (string)run["state"]! != "applied") return Results.Json(new { error = "nothing_to_undo" }, statusCode: 409);
      var undone = 0; var kept = new List<object>();
      foreach (var u in run["undo"]!.AsArray()) { var e = state.Entries.First(x => x.Id == (string)u!["entryId"]!);
        if (e.Version != (int)u!["version"]!) { kept.Add(new { entryId = e.Id, title = e.Title, why = "changed after the sync" }); continue; }
        e.Status = (string)u["status"]!; e.Rating = (int?)u["rating"]; e.Version++; e.Clock = state.Clock++; undone++; }
      run["state"] = kept.Count > 0 ? "partly_undone" : "undone";
      return Results.Json(new { undone, kept }); });
    app.MapPost("/api/sync/v1/links/{id}/unlink", (string id) => state.Links.Remove(id) ? Results.Json(new { unlinked = true }) : Results.Json(new { error = "link_not_found" }, statusCode: 404));
    return app;
  }
}
}
