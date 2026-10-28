using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Blastlog.Sync.Core {
// Shapes of the Blastlog sync API (docs/API_SPEC.md; the server's docs/PLAYNITE_SYNC.md wins where they differ).
public sealed class Game { public string Id {get;set;} public string Title {get;set;} public int? ReleaseYear {get;set;} public string CoverUrl {get;set;} public string IgdbId {get;set;} public string[] SteamAppIds {get;set;} public string[] GogIds {get;set;} }
public sealed class EntryLength { public int Hours {get;set;} public string Level {get;set;} }
public sealed class EntryLink { public string PlayniteGameId {get;set;} }
public sealed class Entry { public string EntryId {get;set;} public int Version {get;set;} public DateTimeOffset UpdatedAt {get;set;} public Game Game {get;set;} public string Ownership {get;set;} public string Status {get;set;} public double? Rating {get;set;} public bool HasReview {get;set;} public string FinishedOn {get;set;} public string Platform {get;set;} public bool InBacklog {get;set;} public EntryLength Length {get;set;} public EntryLink Link {get;set;} }
public sealed class RemovedEntry { public string EntryId {get;set;} public DateTimeOffset RemovedAt {get;set;} }
public sealed class LibraryPage { public List<Entry> Items {get;set;} = new List<Entry>(); public List<RemovedEntry> Removed {get;set;} = new List<RemovedEntry>(); public string NextCursor {get;set;} public bool HasMore {get;set;} }

/// <summary>One Playnite game as POST /match takes it.</summary>
public sealed class MatchGame { public string PlayniteGameId {get;set;} public string Title {get;set;} public string[] Platforms {get;set;} = new string[0]; public string Source {get;set;} public string PluginId {get;set;} public string StoreGameId {get;set;} public int? ReleaseYear {get;set;} }
/// <summary>result: linked | entry | catalogue | unknown; confidence: exact | likely.</summary>
public sealed class MatchResult { public string PlayniteGameId {get;set;} public string Result {get;set;} public string EntryId {get;set;} public string GameId {get;set;} public string Title {get;set;} public string Confidence {get;set;} }
public sealed class MatchResponse { public List<MatchResult> Results {get;set;} = new List<MatchResult>(); }

public sealed class LinkItem { public string PlayniteGameId {get;set;} public string EntryId {get;set;} }
/// <summary>Only the fields that are set are sent, so only they change (null rating is sent as an explicit clear).</summary>
public sealed class ChangeItem {
  public string EntryId {get;set;} public int ExpectedVersion {get;set;}
  [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public string Status {get;set;}
  [JsonIgnore] public bool SetRating {get;set;} [JsonIgnore] public int? RatingValue {get;set;}
  [JsonProperty("rating", NullValueHandling=NullValueHandling.Include)] public int? Rating => RatingValue;
  public bool ShouldSerializeRating() => SetRating;
  [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public string FinishedOn {get;set;}
  [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public string Ownership {get;set;}
}
public sealed class AddGame { public string Title {get;set;} public string Source {get;set;} public string StoreGameId {get;set;} public int? ReleaseYear {get;set;} public string[] Platforms {get;set;} = new string[0]; }
public sealed class AddItem { public string PlayniteGameId {get;set;} public string GameId {get;set;} public AddGame Game {get;set;} public string Status {get;set;} = "not_started"; public string Ownership {get;set;} = "library"; }
public sealed class PlaytimeItem { public string EntryId {get;set;} public long Minutes {get;set;} public DateTimeOffset? LastPlayedAt {get;set;} }
public sealed class UnmatchedItem { public string PlayniteGameId {get;set;} public string Title {get;set;} public string Source {get;set;} public string StoreGameId {get;set;} }
public sealed class ClientInfo { public string Name {get;set;} = "Playnite"; public string Version {get;set;} public string Plugin {get;set;} }

public sealed class PushRequest {
  public bool DryRun {get;set;} public string ClientRunId {get;set;} = Guid.NewGuid().ToString(); public ClientInfo Client {get;set;} public string Origin {get;set;} = "sync";
  public List<LinkItem> Links {get;set;} = new List<LinkItem>(); public List<ChangeItem> Changes {get;set;} = new List<ChangeItem>(); public List<AddItem> Adds {get;set;} = new List<AddItem>();
  public List<PlaytimeItem> Playtime {get;set;} = new List<PlaytimeItem>(); public List<UnmatchedItem> Unmatched {get;set;} = new List<UnmatchedItem>(); public JArray CheckIns {get;set;} = new JArray();
  [JsonIgnore] public int Count => Links.Count + Changes.Count + Adds.Count + Playtime.Count + Unmatched.Count + CheckIns.Count;
}
/// <summary>kind: link | change | add | playtime | unmatched | checkIn; result: applied | unchanged | conflict | rejected.</summary>
public sealed class PushResult { public string Kind {get;set;} public string Ref {get;set;} public string Result {get;set;} public string Reason {get;set;} public int? CurrentVersion {get;set;} public JObject Entry {get;set;} }
public sealed class PushCounts { public int Applied {get;set;} public int Unchanged {get;set;} public int Conflict {get;set;} public int Rejected {get;set;} }
public sealed class PushResponse { public string RunId {get;set;} public bool DryRun {get;set;} public List<PushResult> Results {get;set;} = new List<PushResult>(); public PushCounts Counts {get;set;} = new PushCounts(); }
public sealed class UndoResponse { public int Undone {get;set;} public List<JObject> Kept {get;set;} = new List<JObject>(); }

/// <summary>A refused request. Details: the server's "issues" (field paths and codes) when it sent them, never request data.</summary>
public sealed class BlastlogException : Exception { public int StatusCode {get;} public string Error {get;} public int? RetryAfterSeconds {get;} public string Details {get;}
  public BlastlogException(int status,string error,int? retry,string details=null):base("Blastlog request failed: " + status + " " + error) {StatusCode=status;Error=error;RetryAfterSeconds=retry;Details=details;} }
public sealed class Response<T> { public T Value {get;set;} public string ETag {get;set;} public bool NotModified {get;set;} }
}
