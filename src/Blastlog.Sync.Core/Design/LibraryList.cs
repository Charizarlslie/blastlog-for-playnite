using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Blastlog.Sync.Core.Design {
/// <summary>
/// The Library page's filters, as the site keeps them in its address (web library.ts LibraryFilters): search, status,
/// rating, sort, direction, trait. Status is "all", "backlog", "completed", "wishlist" or one status; rating "all" or 1–5;
/// sort "title", "rating", "finished" or "length"; direction "asc" or "desc"; trait a trait id or "".
/// </summary>
public sealed class LibraryFilters {
  public static readonly string[] Statuses = { "not_started", "started", "playing", "done", "dnf", "quit", "unplayable", "ignored", "unknown" };
  public static readonly HashSet<string> Terminal = new HashSet<string> { "done", "dnf", "quit", "unplayable" };
  static readonly string[] Groups = { "all", "backlog", "completed", "wishlist" };
  static readonly string[] Sorts = { "title", "rating", "finished", "length" };
  static readonly Regex TraitId = new Regex("^[0-9a-f-]{36}$", RegexOptions.IgnoreCase);

  public string Search { get; set; } = "";
  public string Status { get; set; } = "all";
  public string Rating { get; set; } = "all";
  public string Sort { get; set; } = "title";
  public string Direction { get; set; } = "asc";
  public string Trait { get; set; } = "";

  public LibraryFilters With(Action<LibraryFilters> change) { var copy = (LibraryFilters)MemberwiseClone(); change(copy); return copy; }

  /// <summary>Filters from a query string ("status=done&amp;rating=4"), unknown values falling back as the site's filtersFromSearch does.</summary>
  public static LibraryFilters Parse(string query) {
    var values = new Dictionary<string, string>();
    foreach (var part in (query ?? "").TrimStart('?').Split('&')) {
      if (part.Length == 0) continue;
      var at = part.IndexOf('=');
      var key = Uri.UnescapeDataString((at < 0 ? part : part.Substring(0, at)).Replace('+', ' '));
      var value = at < 0 ? "" : Uri.UnescapeDataString(part.Substring(at + 1).Replace('+', ' '));
      if (!values.ContainsKey(key)) values[key] = value;
    }
    string Get(string key) => values.TryGetValue(key, out var v) ? v : null;
    var f = new LibraryFilters();
    f.Search = Get("search") ?? "";
    var status = Get("status"); if (status != null && (Groups.Contains(status) || Statuses.Contains(status))) f.Status = status;
    var rating = Get("rating"); if (rating != null && (rating == "all" || (rating.Length == 1 && rating[0] >= '1' && rating[0] <= '5'))) f.Rating = rating;
    // "hltb" is the length sort's old name (bookmarks from before 2026-10-18).
    var sort = Get("sort"); f.Sort = sort == "hltb" ? "length" : sort != null && Sorts.Contains(sort) ? sort : "title";
    f.Direction = Get("direction") == "desc" ? "desc" : "asc";
    var trait = Get("trait"); f.Trait = trait != null && TraitId.IsMatch(trait) ? trait : "";
    return f;
  }

  /// <summary>The query string for these filters, defaults left out (the site's filtersToSearch).</summary>
  public string ToQuery() {
    var parts = new List<string>();
    void Add(string key, string value, string fallback) { if (!string.IsNullOrEmpty(value) && value != fallback) parts.Add(key + "=" + Uri.EscapeDataString(value)); }
    Add("search", Search, ""); Add("status", Status, "all"); Add("rating", Rating, "all"); Add("sort", Sort, "title"); Add("direction", Direction, "asc"); Add("trait", Trait, "");
    return string.Join("&", parts);
  }
}

/// <summary>
/// The site's search matching (contracts search-text.ts): accents and case set aside, and a second try on the words run
/// together with trademark signs, apostrophes and punctuation dropped, "&amp;" as "and" and Roman numerals I–XX as numbers,
/// so "pokemon" finds Pokémon Gold, "hades 2" finds Hades II and "spiderman" finds Spider-Man.
/// </summary>
public static class SearchText {
  static readonly Dictionary<string, string> Roman = new Dictionary<string, string> {
    ["i"] = "1", ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10",
    ["xi"] = "11", ["xii"] = "12", ["xiii"] = "13", ["xiv"] = "14", ["xv"] = "15", ["xvi"] = "16", ["xvii"] = "17", ["xviii"] = "18", ["xix"] = "19", ["xx"] = "20" };
  static string DropMarks(string text) {
    var sb = new StringBuilder(text.Length);
    foreach (var c in text) if (c < '̀' || c > 'ͯ') sb.Append(c);
    return sb.ToString();
  }
  /// <summary>Accents and case set aside (foldText).</summary>
  public static string Fold(string text) => DropMarks((text ?? "").Normalize(NormalizationForm.FormD)).ToLowerInvariant();
  /// <summary>The words run together (searchKey).</summary>
  public static string Key(string text) {
    var t = Regex.Replace(text ?? "", "[™®©℠]", "");
    t = DropMarks(t.Normalize(NormalizationForm.FormKD)).ToLowerInvariant().Replace("&", " and ");
    t = Regex.Replace(t, "['’‘ʼ`]", "");
    return string.Concat(Regex.Split(t, "[^a-z0-9]+").Where(w => w.Length > 0).Select(w => Roman.TryGetValue(w, out var n) ? n : w));
  }
  /// <summary>Whether <paramref name="text"/> matches <paramref name="query"/> (already folded): titleMatches.</summary>
  public static bool Matches(string text, string query) {
    if (string.IsNullOrEmpty(text)) return false;
    if (Fold(text).Contains(query)) return true;
    var key = Key(query);
    return key.Length > 0 && Key(text).Contains(key);
  }
}

/// <summary>The Library page's list: filter and sort as the site's filterLibrary, and the status chips' counts.</summary>
public static class LibraryList {
  /// <summary>The status chips, in the site's order, with their labels.</summary>
  public static readonly (string Value, string Label)[] Chips = {
    ("all", "All records"), ("backlog", "Backlog"), ("completed", "Completed"), ("wishlist", "Wishlist"), ("playing", "Playing"), ("started", "Started"), ("done", "Done") };
  static readonly StringComparer Titles = StringComparer.Create(CultureInfo.GetCultureInfo("en-US"), false);

  /// <summary>Whether an entry passes the status and rating filters (no search, no trait).</summary>
  public static bool PassesStatus(Entry e, string status) {
    switch (status) {
      case "all": return true;
      case "backlog": return (e.Status == "not_started" || e.Status == "started" || e.Status == "playing") && e.Ownership != "wishlist";
      case "completed": return LibraryFilters.Terminal.Contains(e.Status ?? "");
      case "wishlist": return e.Ownership == "wishlist";
      default: return e.Status == status;
    }
  }

  /// <summary>
  /// The entries that pass, sorted. Search looks at the title (and <paramref name="reviewOf"/>'s text when given: the
  /// site searches reviews too). <paramref name="hasTrait"/>: the trait filter's games, when one is picked.
  /// </summary>
  public static List<Entry> Filter(IEnumerable<Entry> entries, LibraryFilters f, Func<Entry, string> reviewOf = null, Func<Entry, bool> hasTrait = null) {
    var query = SearchText.Fold((f.Search ?? "").Trim());
    var rows = entries.Where(e => e?.Game != null).Where(e => {
      if (query.Length > 0 && !SearchText.Matches(e.Game.Title + " " + (reviewOf?.Invoke(e) ?? ""), query)) return false;
      if (!PassesStatus(e, f.Status)) return false;
      if (f.Rating != "all" && (e.Rating == null || Math.Abs(e.Rating.Value - double.Parse(f.Rating, CultureInfo.InvariantCulture)) > 0.001)) return false;
      if (hasTrait != null && !hasTrait(e)) return false;
      return true;
    }).ToList();
    rows.Sort((a, b) => Compare(a, b, f.Sort, f.Direction == "desc" ? -1 : 1));
    return rows;
  }

  static int Compare(Entry a, Entry b, string sort, int factor) {
    IComparable left, right;
    switch (sort) {
      case "rating": left = a.Rating; right = b.Rating; break;
      case "finished": left = a.FinishedOn; right = b.FinishedOn; break;
      // Unknown lengths go last either way (docs/GAME_LENGTH.md D6).
      case "length": left = a.Length != null ? (IComparable)(double)a.Length.Hours : null; right = b.Length != null ? (IComparable)(double)b.Length.Hours : null; break;
      default: left = a.Game.Title; right = b.Game.Title; break;
    }
    if (Equals(left, right)) return Titles.Compare(a.Game.Title, b.Game.Title);
    if (left == null) return 1; if (right == null) return -1;
    return (left is string ls ? (sort == "title" ? Titles.Compare(ls, (string)right) : string.CompareOrdinal(ls, (string)right)) : left.CompareTo(right)) * factor;
  }

  /// <summary>How many games each status chip holds, over the whole library (the approved board's counts).</summary>
  public static Dictionary<string, int> Counts(IEnumerable<Entry> entries) {
    var list = entries.Where(e => e?.Game != null).ToList();
    return Chips.ToDictionary(c => c.Value, c => list.Count(e => PassesStatus(e, c.Value)));
  }
}
}
