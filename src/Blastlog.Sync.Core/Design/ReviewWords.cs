using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Blastlog.Sync.Core.Design {
/// <summary>One tap on the Rate &amp; review form: a trait or a fixed chip, on the "worked" or "held_back" side.</summary>
public sealed class Pick {
  public string Side; public string CharacteristicId; public string Chip;
  public Pick(string side, string characteristicId, string chip) { Side = side; CharacteristicId = characteristicId; Chip = chip; }
  public string Key => Side + ":" + (CharacteristicId ?? Chip);
}

/// <summary>A trait's name for the starter line; <see cref="Family"/> is null for a saved pick the game no longer offers.</summary>
public sealed class TraitName {
  public string Label; public string Family;
  public TraitName(string label, string family = null) { Label = label; Family = family; }
}

/// <summary>A fixed chip: its key, words and the sides it goes on.</summary>
public sealed class PickChip {
  public string Key; public string Label; public string[] Sides;
  public PickChip(string key, string label, params string[] sides) { Key = key; Label = label; Sides = sides; }
}

/// <summary>A question beside the review box.</summary>
public sealed class ReviewQuestion {
  public string Ask; public string Hint;
  public ReviewQuestion(string ask, string hint) { Ask = ask; Hint = hint; }
}

/// <summary>
/// The site's Rate &amp; review words, ported exactly: the starter line taps write into an empty review box
/// (web review-picks-api.ts starterLine / traitWords), the fixed chips (FALLBACK_CHIPS), the questions beside the review
/// (review-questions.ts) and which statuses can be rated (domain canRateOrReview, web library.ts).
/// </summary>
public static class ReviewWords {
  public static readonly string[] Statuses = { "not_started", "started", "playing", "done", "dnf", "quit", "unplayable", "ignored", "unknown" };
  public static string StatusLabel(string status) {
    switch (status) {
      case "not_started": return "Not started"; case "dnf": return "Didn't finish"; case "done": return "Done"; case "quit": return "Quit";
      case "started": return "Started"; case "playing": return "Playing"; case "unplayable": return "Unplayable"; case "ignored": return "Ignored";
      case "unknown": return "Unknown"; default: return status;
    }
  }
  static readonly HashSet<string> Terminal = new HashSet<string> { "done", "dnf", "quit", "unplayable" };
  /// <summary>Stars, taps and words wait until the player is done with the game (the same four statuses on the site).</summary>
  public static bool CanRateOrReview(string status) => status != null && Terminal.Contains(status);
  public static bool IsTerminal(string status) => status != null && Terminal.Contains(status);
  /// <summary>The completion date fills in with today when a game enters a finished status and has none.</summary>
  public static string CompletionDate(string status, string current, string today) => IsTerminal(status) && string.IsNullOrEmpty(current) ? today : current;

  /// <summary>Only used when the game's own chips can't be loaded; must match PICK_CHIPS on the server.</summary>
  public static readonly PickChip[] FallbackChips = {
    new PickChip("looks_sound", "Looks & sound", "worked", "held_back"),
    new PickChip("right_length", "Right length", "worked"),
    new PickChip("too_long", "Too long", "held_back"),
    new PickChip("too_short", "Too short", "held_back"),
    new PickChip("grind", "Grindy", "held_back"),
    new PickChip("slow_start", "Slow start", "held_back"),
    new PickChip("repetitive", "Felt samey", "held_back"),
    new PickChip("bugs", "Bugs & crashes", "held_back"),
    new PickChip("nothing", "Nothing, loved it", "held_back"),
  };

  public static List<ReviewQuestion> Questions(string status) {
    var stopped = status == "dnf" || status == "quit";
    return new List<ReviewQuestion> {
      new ReviewQuestion("What hooked you?", "The parts you’d want more of: the combat, puzzles, exploring, building, the people you played with."),
      stopped ? new ReviewQuestion("What made you stop?", "What dragged, annoyed you or lost you, and where.")
        : new ReviewQuestion("What wore on you?", "What dragged or annoyed you, even in a game you loved."),
      new ReviewQuestion("Is this your kind of game?", "Do you usually love, avoid or not care about games like this? Did this one change your mind?"),
      new ReviewQuestion("Did the story and world matter?", "The characters, setting and writing: did they pull you in, or were they just there?"),
      new ReviewQuestion("Was it the right size and challenge?", "Too long, too short, too easy, too punishing?"),
    };
  }
  public const string Tip = "Say what your stars are mostly about (the gameplay, the story, the looks and sound, or bugs), and whether something was done badly here or just isn’t your thing.";
  public const string UnplayableNote = "Unplayable means it wouldn’t run, so there’s nothing to judge. A note on what went wrong is plenty.";

  // ---- Tapping: the site's TapReview toggles ----
  public static bool Has(IEnumerable<Pick> picks, string side, string characteristicId, string chip) =>
    picks.Any(p => p.Side == side && (characteristicId != null ? p.CharacteristicId == characteristicId : p.Chip == chip));

  /// <summary>A trait sits on one side only, and picking a downside means it wasn't "nothing".</summary>
  public static List<Pick> ToggleTrait(IList<Pick> picks, string side, string id) => Has(picks, side, id, null)
    ? picks.Where(p => !(p.Side == side && p.CharacteristicId == id)).ToList()
    : picks.Where(p => p.CharacteristicId != id && !(side == "held_back" && p.Chip == "nothing")).Concat(new[] { new Pick(side, id, null) }).ToList();

  /// <summary>"Nothing, loved it" clears that side; any other downside clears "nothing".</summary>
  public static List<Pick> ToggleChip(IList<Pick> picks, string side, string chip) => Has(picks, side, null, chip)
    ? picks.Where(p => !(p.Side == side && p.Chip == chip)).ToList()
    : chip == "nothing" ? picks.Where(p => p.Side != side).Concat(new[] { new Pick(side, null, chip) }).ToList()
      : picks.Where(p => !(side == "held_back" && p.Chip == "nothing")).Concat(new[] { new Pick(side, null, chip) }).ToList();

  public static bool SamePicks(IEnumerable<Pick> a, IEnumerable<Pick> b) {
    var x = a.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList(); var y = b.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
    return x.SequenceEqual(y);
  }

  // ---- The starter line ----
  static readonly Dictionary<string, string> WorkedChip = new Dictionary<string, string> { ["looks_sound"] = "the look and sound" };
  static readonly Dictionary<string, string> HeldChip = new Dictionary<string, string> {
    ["looks_sound"] = "the look and sound", ["grind"] = "the grind", ["slow_start"] = "a slow start", ["repetitive"] = "the repetition", ["bugs"] = "bugs" };
  static readonly Dictionary<string, string> LengthLine = new Dictionary<string, string> {
    ["right_length"] = "It was the right length.", ["too_long"] = "It ran too long.", ["too_short"] = "It was over too soon." };
  static readonly Dictionary<string, string> Fixed = new Dictionary<string, string> {
    ["story-driven"] = "the story", ["grief and loss"] = "the themes of grief and loss", ["religion and faith"] = "the themes of religion and faith",
    ["revenge"] = "the revenge story", ["medieval"] = "the medieval setting", ["noir"] = "the noir feel", ["dark or grim tone"] = "the grim tone",
    ["high challenge"] = "the challenge", ["point-and-click"] = "the point-and-click gameplay", ["player versus environment"] = "the PvE",
    ["puzzle"] = "the puzzles", ["platformer"] = "the platforming", ["2d platformer"] = "the 2D platforming", ["3d platformer"] = "the 3D platforming",
    ["shooter"] = "the shooting", ["racing"] = "the racing", ["fighting"] = "the fighting", ["2d fighting"] = "the 2D fighting", ["action"] = "the action",
    ["role-playing"] = "the role-playing", ["dungeon crawling"] = "the dungeon crawling", ["beat 'em up"] = "the beat 'em up action",
    ["active time battle"] = "the active time battle system", ["hand-drawn 2d"] = "the hand-drawn 2D art", ["escape room"] = "the escape-room puzzles",
  };
  static readonly string[] Proper = { "Japanese", "Norse", "Lovecraftian", "Vikings", "Cold War", "Art Deco", "New Game Plus" };

  /// <summary>"Open world" → "open world", "&amp;" → "and"; LGBTQ+, JRPG, 2D and a lone capital (Zero-G) keep their case.</summary>
  static string LowerName(string label) {
    bool Keep(string part) => (part.Length > 1 && part.Substring(1).Any(char.IsUpper) && !part.StartsWith("'", StringComparison.Ordinal)) || Regex.IsMatch(part, "^[A-Z]$");
    var name = string.Join(" ", Regex.Replace(label.Trim(), @"\s*&\s*", " and ").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
      .Select(word => string.Join("-", word.Split('-').Select(part => Keep(part) ? part : part.ToLowerInvariant()))));
    foreach (var proper in Proper) name = Regex.Replace(name, @"\b" + Regex.Escape(proper) + @"\b", proper, RegexOptions.IgnoreCase);
    return name;
  }

  /// <summary>A trait as words in a sentence: "the open world", "the story", "the JRPG elements".</summary>
  public static string TraitWords(TraitName trait) {
    if (Fixed.TryGetValue(trait.Label.Trim().ToLowerInvariant(), out var words)) return words;
    var name = LowerName(trait.Label);
    if (Regex.IsMatch(name, "^[a-z]+-(driven|based|focused|heavy|paced)$")) return $"the {name} design";
    if (trait.Family == "genre" && !Regex.IsMatch(name, @"\b(combat|gameplay|play|action)$")) return $"the {name} elements";
    return $"the {name}";
  }

  /// <summary>"a", "a and b", "a, b, and c"; an item with its own "and" goes last, and two of those take "plus".</summary>
  static string List(IList<string> items) {
    bool HasAnd(string item) => item.Contains(" and ");
    var sorted = items.Where(i => !HasAnd(i)).Concat(items.Where(HasAnd)).ToList();
    if (sorted.Count <= 1) return string.Join("", sorted);
    if (sorted.Count == 2) return $"{sorted[0]}{(HasAnd(sorted[0]) ? ", plus" : " and")} {sorted[1]}";
    return $"{string.Join(", ", sorted.Take(sorted.Count - 1))}, and {sorted[sorted.Count - 1]}";
  }

  public static string StarterLine(IList<Pick> picks, Func<Pick, TraitName> nameOf, int? rating) {
    List<string> Words(string side, Dictionary<string, string> chips) => picks.Where(p => p.Side == side)
      .Select(p => {
        if (p.Chip != null) return chips.TryGetValue(p.Chip, out var w) ? w : null;
        var name = nameOf(p); return name != null ? TraitWords(name) : null;
      }).Where(x => x != null).Distinct().ToList();
    IEnumerable<string> Lengths(string side) => picks.Where(p => p.Side == side && p.Chip != null && LengthLine.ContainsKey(p.Chip)).Select(p => LengthLine[p.Chip]);
    var worked = Words("worked", WorkedChip); var held = Words("held_back", HeldChip);
    var nothing = picks.Any(p => p.Side == "held_back" && p.Chip == "nothing");
    var parts = new List<string> { worked.Count > 0 ? $"{(rating != null && rating <= 3 ? "Liked" : "Loved")} {List(worked)}." : "" };
    parts.AddRange(Lengths("worked"));
    parts.Add(nothing ? "Nothing held it back." : held.Count > 0 ? $"Held back by {List(held)}." : "");
    parts.AddRange(Lengths("held_back"));
    return string.Join(" ", parts.Where(p => p.Length > 0));
  }
}
}
