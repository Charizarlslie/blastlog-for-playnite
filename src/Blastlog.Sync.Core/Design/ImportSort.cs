using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Blastlog.Sync.Core.Design {
/// <summary>A pile or status choice: the id the player picks and its words.</summary>
public sealed class ImportChoice {
  public string Id; public string Label;
  public ImportChoice(string id, string label) { Id = id; Label = label; }
}

/// <summary>A pile change: the row's pile and its extra status (null for the pile's own).</summary>
public sealed class PileChange {
  public string Pile; public string ChosenStatus;
  public PileChange(string pile, string chosenStatus) { Pile = pile; ChosenStatus = chosenStatus; }
}

/// <summary>One match option for a game that needs a look (the site's ImportOption).</summary>
public sealed class ImportOption {
  public string IgdbId, GameId, Title, CoverImageId, CoverUrl, Note, Detail; public int? Year; public bool Suggested;
  public static ImportOption Parse(JObject o) => new ImportOption {
    IgdbId = Str(o, "igdbId"), GameId = Str(o, "gameId"), Title = Str(o, "title") ?? "", CoverImageId = Str(o, "coverImageId"), CoverUrl = Str(o, "coverUrl"),
    Note = Str(o, "note"), Detail = Str(o, "detail"), Year = Int(o, "year"), Suggested = o.Value<bool?>("suggested") == true };
  /// <summary>What picking it sends: the stored game when it's one Blastlog (or the library) already has, else the IGDB record.</summary>
  public JObject Choice() => GameId != null && (IgdbId == null || Note == "in_blastlog" || Note == "in_library")
    ? new JObject { ["gameId"] = GameId } : new JObject { ["igdbId"] = IgdbId };
  /// <summary>The line under its name: year, where it is, what sets it apart, Suggested.</summary>
  public string Line() => string.Join(" · ", new[] { Year?.ToString(CultureInfo.InvariantCulture),
    Note == "in_library" ? "In your library" : Note == "in_blastlog" ? "Already in Blastlog" : Note == "edition" ? "Edition" : null,
    Detail, Suggested ? "Suggested" : null }.Where(s => !string.IsNullOrEmpty(s)));
  internal static string Str(JObject o, string key) => o?[key]?.Type == JTokenType.String ? (string)o[key] : null;
  internal static int? Int(JObject o, string key) => o?[key] == null || o[key].Type == JTokenType.Null ? (int?)null : (int)(double)o[key];
}

/// <summary>Steam's playtime for a row (Connect Steam / Steam sync).</summary>
public sealed class ImportPlay {
  public string Group; public double Hours; public double? BeatHours; public DateTimeOffset? LastPlayedAt;
}

/// <summary>One game of an import (the site's ImportRow), read from GET imports/:id.</summary>
public sealed class ImportRow {
  public string Id, RawTitle, Title, CoverImageId, CoverUrl, State, Reason, Pile, ChosenStatus, Outcome, EntryId, GameId;
  public int Position; public int? Year, Rating, SourceRating; public bool NewToBlastlog, HasReview, Declared;
  public List<ImportOption> Options = new List<ImportOption>();
  public ImportPlay Play;
  public string Name => Title ?? RawTitle;

  public static ImportRow Parse(JObject o) {
    var row = new ImportRow {
      Id = ImportOption.Str(o, "id"), RawTitle = ImportOption.Str(o, "rawTitle") ?? "", Title = ImportOption.Str(o, "title"),
      CoverImageId = ImportOption.Str(o, "coverImageId"), CoverUrl = ImportOption.Str(o, "coverUrl"), State = ImportOption.Str(o, "state"),
      Reason = ImportOption.Str(o, "reason"), Pile = ImportOption.Str(o, "pile") ?? "backlog", ChosenStatus = ImportOption.Str(o, "chosenStatus"),
      Outcome = ImportOption.Str(o, "outcome"), EntryId = ImportOption.Str(o, "entryId"), GameId = ImportOption.Str(o, "gameId"),
      Position = ImportOption.Int(o, "position") ?? 0, Year = ImportOption.Int(o, "year"), Rating = ImportOption.Int(o, "rating"),
      SourceRating = ImportOption.Int(o, "sourceRating"), NewToBlastlog = o.Value<bool?>("newToBlastlog") == true,
      HasReview = o.Value<bool?>("hasReview") == true, Declared = o.Value<bool?>("declared") == true };
    if (o["options"] is JArray options) row.Options = options.OfType<JObject>().Select(ImportOption.Parse).ToList();
    if (o["play"] is JObject play) {
      var last = ImportOption.Str(play, "lastPlayedAt");
      row.Play = new ImportPlay { Group = ImportOption.Str(play, "group"), Hours = play["hours"]?.Type == JTokenType.Null ? 0 : (double?)play["hours"] ?? 0,
        BeatHours = play["beatHours"] == null || play["beatHours"].Type == JTokenType.Null ? (double?)null : (double)play["beatHours"],
        LastPlayedAt = last != null && DateTimeOffset.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : (DateTimeOffset?)null };
    }
    return row;
  }
}

/// <summary>
/// The import page's rules (apps/web LibraryImport.tsx): piles and statuses, what a row's pile means once the import went
/// in, which games the sort cards ask about and in what order, the playtime line, and the words for each import and each
/// game that needs a look. Kept here so the Playnite screen makes exactly the site's decisions.
/// </summary>
public static class ImportSort {
  public static readonly IReadOnlyList<ImportChoice> Piles = new[] { new ImportChoice("finished", "Finished"), new ImportChoice("playing", "Playing"),
    new ImportChoice("backlog", "Backlog"), new ImportChoice("not_interested", "Not interested"), new ImportChoice("skip", "Don’t import") };
  /// <summary>A wishlist's games go on the wishlist (stored as the Backlog pile) or stay out.</summary>
  public static readonly IReadOnlyList<ImportChoice> WishlistPiles = new[] { new ImportChoice("backlog", "Wishlist"), new ImportChoice("skip", "Don’t import") };
  public static readonly IReadOnlyList<ImportChoice> Extra = new[] { new ImportChoice("started", "Started (backlog)"), new ImportChoice("dnf", "Didn’t finish"),
    new ImportChoice("quit", "Quit"), new ImportChoice("unplayable", "Unplayable") };

  public static bool IsWishlist(string format) => format == "steam_wishlist";
  public static IReadOnlyList<ImportChoice> PilesFor(string format) => IsWishlist(format) ? WishlistPiles : Piles;

  /// <summary>A pile select's value (a pile or an extra status) as the change it makes: Started is a Backlog game, the rest Finished.</summary>
  public static PileChange ChangeFor(string value) {
    var extra = Extra.FirstOrDefault(e => e.Id == value);
    return extra != null ? new PileChange(extra.Id == "started" ? "backlog" : "finished", extra.Id) : new PileChange(value, null);
  }
  /// <summary>A row's value in a pile select.</summary>
  public static string ValueOf(string pile, string chosenStatus) => chosenStatus ?? pile;
  public static string PileLabel(string pile, string chosenStatus) =>
    (chosenStatus != null ? Extra.FirstOrDefault(e => e.Id == chosenStatus) : Piles.FirstOrDefault(p => p.Id == pile))?.Label ?? pile;
  public static bool FinishedPile(string pile) => pile == "finished";

  /// <summary>What each library status looks like as a pile (after the import went in).</summary>
  static readonly (string status, string pile, string chosen)[] RowFor = {
    ("done", "finished", null), ("dnf", "finished", "dnf"), ("quit", "finished", "quit"), ("unplayable", "finished", "unplayable"),
    ("playing", "playing", null), ("started", "backlog", "started"), ("not_started", "backlog", null), ("ignored", "not_interested", null) };
  /// <summary>The library status a row's pile stands for; null when it was removed (the skip pile).</summary>
  public static string StatusOf(string pile, string chosenStatus) => RowFor.FirstOrDefault(r => r.pile == pile && r.chosen == chosenStatus).status;
  /// <summary>The pile a change to the import's entries leaves a row on: removed is the skip pile; a rating alone changes nothing.</summary>
  public static PileChange PileAfter(string status, bool remove) {
    if (remove) return new PileChange("skip", null);
    if (status == null) return null;
    var r = RowFor.FirstOrDefault(x => x.status == status);
    return r.status == null ? null : new PileChange(r.pile, r.chosen);
  }

  /// <summary>
  /// The games the sort cards ask about: the ones this import added that you've played (Steam's never-played stay
  /// Backlog) or, from a list with no playtime, all of them, unless the file gave one its own status. Most played first.
  /// </summary>
  public static List<ImportRow> CardRows(IEnumerable<ImportRow> rows) => rows
    .Where(r => r.Outcome == "committed" && !r.Declared && r.Play?.Group != "never")
    .OrderByDescending(r => r.Play?.Hours ?? 0).ThenBy(r => r.Position).ToList();

  /// <summary>"Played some" in Quick sort, most played first.</summary>
  public static List<ImportRow> QuickRows(IEnumerable<ImportRow> rows) => rows.Where(r => r.Play?.Group == "some").OrderByDescending(r => r.Play.Hours).ToList();

  public static readonly IReadOnlyList<(string id, string title, string note)> PlayGroups = new[] {
    ("never", "Never played on Steam", "Nothing on Steam's clock."),
    ("finished", "Played long enough to finish", "At least 80% of how long the game takes to beat."),
    ("playing", "Played in the last 2 weeks", "Still going, probably."),
    ("some", "Played some", "Only you know these. They stay Backlog until you sort them.") };

  /// <summary>Quick sort's choices, in the order of their number keys.</summary>
  public static readonly IReadOnlyList<(string label, PileChange change)> Quick = new[] {
    ("Finished", new PileChange("finished", null)), ("Didn’t finish", new PileChange("finished", "dnf")), ("Started", new PileChange("backlog", "started")),
    ("Playing", new PileChange("playing", null)), ("Quit", new PileChange("finished", "quit")), ("Backlog", new PileChange("backlog", null)),
    ("Not interested", new PileChange("not_interested", null)), ("Don’t import", new PileChange("skip", null)) };

  /// <summary>"No" on a sort card: where the game is instead (status null with remove: take it out).</summary>
  public static readonly IReadOnlyList<(string label, string status, bool remove)> NotFinished = new[] {
    ("Still playing", "playing", false), ("Will go back to it", "started", false), ("Didn’t finish", "dnf", false), ("Quit", "quit", false),
    ("Unplayable", "unplayable", false), ("Haven’t started", "not_started", false), ("Not interested", "ignored", false), ("Remove it", (string)null, true) };

  /// <summary>What a sort card says the game is on now.</summary>
  public static string Now(string status) {
    switch (status) {
      case "done": return "Finished"; case "playing": return "Playing"; case "started": return "Started"; case "dnf": return "Didn’t finish";
      case "quit": return "Quit"; case "unplayable": return "Unplayable"; case "ignored": return "Not interested"; default: return "Backlog";
    }
  }

  /// <summary>After it went in: the list's filter piles, the status choices, and the More ones.</summary>
  public static readonly IReadOnlyList<ImportChoice> EntryPiles = new[] { new ImportChoice("finished", "Finished"), new ImportChoice("playing", "Playing"),
    new ImportChoice("backlog", "Backlog"), new ImportChoice("not_interested", "Not interested"), new ImportChoice("skip", "Removed") };
  public static readonly IReadOnlyList<ImportChoice> EntryChoices = new[] { new ImportChoice("done", "Finished"), new ImportChoice("playing", "Playing"),
    new ImportChoice("not_started", "Backlog"), new ImportChoice("ignored", "Not interested") };
  public static readonly IReadOnlyList<ImportChoice> EntryMore = new[] { new ImportChoice("started", "Started (backlog)"), new ImportChoice("dnf", "Didn’t finish"),
    new ImportChoice("quit", "Quit"), new ImportChoice("unplayable", "Unplayable") };

  static string HoursText(double hours) => hours < 1 ? $"{Math.Round(hours * 60, MidpointRounding.AwayFromZero)} min"
    : (hours >= 10 ? Math.Round(hours, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) : hours.ToString(CultureInfo.InvariantCulture)) + " h";
  /// <summary>"12 h played · beats in ~20 h · last played Mar 2025" (the site's playLine).</summary>
  public static string PlayLine(ImportPlay play) => string.Join(" · ", new[] {
    play.Hours > 0 ? HoursText(play.Hours) + " played" : "Never played",
    play.BeatHours > 0 ? $"beats in ~{Math.Round(play.BeatHours.Value, MidpointRounding.AwayFromZero)} h" : null,
    play.LastPlayedAt != null ? "last played " + play.LastPlayedAt.Value.ToLocalTime().ToString("MMM yyyy", CultureInfo.InvariantCulture) : null }.Where(s => s != null));
  /// <summary>How far through the game the playtime is (0–1), when Blastlog knows how long it takes.</summary>
  public static double? Share(ImportPlay play) => play?.BeatHours > 0 ? Math.Min(1, play.Hours / play.BeatHours.Value) : (double?)null;

  static readonly Dictionary<string, string> Doors = new Dictionary<string, string> {
    ["steam"] = "Steam", ["playstation"] = "PlayStation", ["xbox"] = "Xbox", ["nintendo"] = "Nintendo Switch", ["gog"] = "GOG", ["epic"] = "Epic Games", ["list"] = "Any list or file" };
  static readonly Dictionary<string, string> Formats = new Dictionary<string, string> { ["grouvee_csv"] = "Grouvee", ["backloggery_csv"] = "Backloggery", ["backloggd_page"] = "Backloggd" };
  public static string DoorName(string door) => door != null && Doors.TryGetValue(door, out var n) ? n : "List";
  /// <summary>"Steam wishlist", "Grouvee", "Steam (daily check)".</summary>
  public static string ImportName(string door, string format, string origin) =>
    (IsWishlist(format) ? DoorName(door) + " wishlist" : format != null && Formats.TryGetValue(format, out var f) ? f : DoorName(door))
    + (origin == "steam_sync" ? " (daily check)" : "");

  /// <summary>The badge on an import in the list.</summary>
  public static string StatusText(string status, int needsLook) {
    switch (status) {
      case "matching": return "Finding games…"; case "ready": return "Ready to sort"; case "committing": return "Adding…";
      case "committed": return needsLook > 0 ? $"Added · {needsLook} to look at" : "Added";
      case "undoing": return "Undoing…"; case "undone": return "Undone"; case "failed": return "Didn’t finish"; default: return status ?? "";
    }
  }

  static readonly Dictionary<string, string> LookReasons = new Dictionary<string, string> {
    ["edition"] = "This looks like an edition. Blastlog keeps the game itself.", ["same_name"] = "More than one game has this name. Which one?",
    ["close"] = "No exact match. Is it one of these?", ["maybe_same"] = "Blastlog already has a game with this name. Is it the same one?",
    ["maybe_in_library"] = "You already have a game with this name in your library. Same one, or a different game?",
    ["bundle"] = "This is a bundle. Pick the game you mean, or skip it.", ["addon"] = "This is an add-on (an expansion or episode). Keep it, or skip it.",
    ["several"] = "This store id matches more than one game.", ["could_not_add"] = "This one couldn’t be added. Pick it again.",
    ["not_found"] = "Couldn’t find this one.", ["no_game"] = "Couldn’t find a game by this name.", ["store_id_unknown"] = "Couldn’t find this one." };

  /// <summary>"Same as yours?" when the two are years apart, said plainly; null otherwise.</summary>
  public static string YearsApart(ImportRow row) {
    if (row.Reason != "maybe_in_library" && row.Reason != "maybe_same") return null;
    var known = row.Options.FirstOrDefault(o => o.Note == "in_library" || o.Note == "in_blastlog");
    var found = row.Options.FirstOrDefault(o => o != known && o.Year != null);
    if (known?.Year == null || found?.Year == null || Math.Abs(known.Year.Value - found.Year.Value) < 5) return null;
    var whose = known.Note == "in_library" ? "Your library’s" : "Blastlog’s";
    return $"{whose} “{known.Title}” is from {known.Year}; this one is from {found.Year}, so they’re probably different games. Pick yours only if it’s really the same.";
  }
  /// <summary>Why a game needs a look (or was set aside as not a game).</summary>
  public static string LookReason(ImportRow row) {
    if (row.State == "not_a_game") return row.Reason == "player" ? "You said this isn’t a game."
      : row.Reason == "extra" ? "Looks like an extra that comes with a game (a soundtrack, artbook, tool or demo). Wrong? Search for the game."
      : "The game catalog lists this as an add-on, not a game. Wrong? Search for the game.";
    return YearsApart(row) ?? (row.Reason != null && LookReasons.TryGetValue(row.Reason, out var r) ? r : "Pick the right game.");
  }
  public static bool IsSplitBundle(ImportRow row) => row.State == "not_a_game" && row.Reason == "bundle_split";
  /// <summary>A wishlist row Steam named only by its id ("Steam app 123"): its Steam id, so the store page can say what it is.</summary>
  public static string SteamIdOnly(string rawTitle) {
    if (rawTitle == null) return null;
    var m = System.Text.RegularExpressions.Regex.Match(rawTitle, @"^Steam app (\d{1,10})$");
    return m.Success ? m.Groups[1].Value : null;
  }
}
}
