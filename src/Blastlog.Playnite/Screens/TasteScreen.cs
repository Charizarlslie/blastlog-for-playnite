using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>Your taste (the site's Profile, design.md 8 "Your taste (Profile)"; PLAYNITE_DESIGN 4.6).</summary>
public sealed class TasteScreen : IBlastlogScreen {
  public string Title => "Your taste";
  public string Icon => ProfileIcon;
  public int Order => 50;
  public FrameworkElement Create(BlastlogPlugin plugin) => new TasteView(plugin);
}

/// <summary>One trait from GET /taste-profile (the site's TasteTrait).</summary>
sealed class TasteTrait {
  public string Id, Label, Family, Definition, Stance, Correction, Note, SetBy; public double Value, Count; public double? Inferred; public int GamesWithTrait;
  public JArray Readings, RatedGames, Sources; public JObject Pattern, Exceptions;
  public static TasteTrait From(JToken j) => new TasteTrait {
    Id = S(j, "id"), Label = S(j, "label") ?? "", Family = S(j, "family") ?? "other", Definition = S(j, "definition"), Stance = S(j, "stance") ?? "mixed",
    Correction = S(j, "correction"), Note = S(j, "note"), SetBy = S(j, "setBy"), Value = D(j, "value") ?? 0, Count = D(j, "count") ?? 0, Inferred = D(j, "inferred"),
    GamesWithTrait = I(j, "gamesWithTrait"), Readings = Arr(j["readings"]), RatedGames = Arr(j["ratedGames"]), Sources = Arr(j["sources"]),
    Pattern = j["pattern"] as JObject, Exceptions = j["exceptions"] as JObject };
}

/// <summary>
/// Your taste, natively: the readable part first (what hooks you, what loses you, area by area, numbers, sweet spots, your
/// own words, must-haves), then Fine-tune with every trait and the controls to correct them. Corrections go through the
/// site's own routes (sync mirror writes), so History, Recent changes and Undo work as on the site.
/// </summary>
sealed class TasteView : Grid {
  static readonly string[] Positive = { "like", "love", "instant_yes" }, Negative = { "not_for_me", "avoid", "dealbreaker" };
  static readonly (string key, string label, double value, string help)[] Scale = {
    ("dealbreaker", "Never", -1, "Dealbreaker: a game that clearly has this sinks to the bottom, no exceptions"),
    ("avoid", "Avoid", -.7, "A strong strike against a game"), ("not_for_me", "Not for me", -.35, "Counts a little against a game"),
    ("neutral", "Doesn’t sway me", 0, "Ignored either way, whatever your ratings suggested"), ("like", "Like", .35, "Counts a little for a game"),
    ("love", "Love", .7, "A strong pull toward a game"), ("instant_yes", "Instant yes", 1, "If a game clearly has this, it jumps up on its own") };
  static readonly Dictionary<string, string> Derived = new Dictionary<string, string> { ["loves"] = "Loves", ["likes"] = "Likes", ["mixed"] = "Mixed", ["dislikes"] = "Not keen", ["avoids"] = "Avoids" };
  static readonly (string key, string label, string derived)[] CardSteps = { ("avoid", "Avoids", "avoids"), ("not_for_me", "Not keen", "dislikes"), ("neutral", "Mixed", "mixed"), ("like", "Likes", "likes"), ("love", "Loves", "loves") };
  static readonly Dictionary<string, string> Sentiment = new Dictionary<string, string> { ["2"] = "loved", ["1"] = "liked", ["0"] = "mixed", ["-1"] = "disliked", ["-2"] = "hated" };
  static readonly Dictionary<string, string> SourceWord = new Dictionary<string, string> { ["ratings"] = "from ratings", ["reviews"] = "from reviews", ["you"] = "you said" };
  static readonly string[] FamilyOrder = { "gameplay", "genre", "structure", "progression", "narrative", "setting", "theme", "tone", "presentation", "mode", "challenge", "other", "franchise" };
  static readonly Dictionary<string, string> FamilyLabel = new Dictionary<string, string> { ["gameplay"] = "Gameplay & mechanics", ["genre"] = "Genre", ["structure"] = "Structure & world",
    ["progression"] = "Progression", ["narrative"] = "Story & narrative", ["setting"] = "Setting", ["theme"] = "Themes", ["tone"] = "Tone & mood", ["presentation"] = "Presentation & visuals",
    ["mode"] = "Play modes", ["challenge"] = "Challenge", ["franchise"] = "Franchises", ["other"] = "Other" };
  const int GlanceMinGames = 8;

  readonly BlastlogPlugin plugin;
  readonly StackPanel page = new StackPanel { MaxWidth = 1320, HorizontalAlignment = HorizontalAlignment.Stretch };
  readonly UndoToast toast;
  JObject data, help, changes; string myName; string error;
  bool busy, loaded, changesOpen, addOpen;
  List<TasteTrait> traits = new List<TasteTrait>();
  // Cards keep the place they had when the page opened (the site's settle): a tap changes a card, never where it is.
  readonly Dictionary<string, List<string>> order = new Dictionary<string, List<string>>();
  readonly HashSet<string> openWhy = new HashSet<string>(), fullDefinition = new HashSet<string>();
  string fineTab = "loves", fineSearch = "", asking, askingFor, addSearch = "", confirmingQuestion; int shown = 12;
  (string id, string label)? addPicked;
  FrameworkElement fineTune;
  StackPanel fineResults, addResults;

  /// <summary>
  /// A friend's taste (Screens/FriendPages.cs, the site's /friends/:id/profile) when set: their profile, read only (no
  /// corrections, Sharpen your picks, picture or settings), worded about them.
  /// </summary>
  readonly FriendView friend;
  bool Own => friend == null;
  /// <summary>The line about the player (<paramref name="you"/>) or about the friend (<paramref name="them"/>).</summary>
  string Say(string you, string them) => Own ? you : them;

  public TasteView(BlastlogPlugin plugin, FriendView friend = null) {
    this.plugin = plugin; this.friend = friend;
    Children.Add(page);
    toast = new UndoToast(this);
    Loaded += (s, e) => { if (!loaded) { loaded = true; Run(Load, "taste load"); } };
  }

  async Task Load() {
    if (!plugin.Configured) { Render(); return; }
    // The cached copy straight away, then a fresh one.
    await Read(false); Render();
    await Read(true); Render();
  }
  async Task Read(bool fresh) {
    if (!Own) {
      data = (await plugin.Mirror(friend.Path("taste-profile"), fresh)).Value ?? data;
      myName = friend.Name;
      traits = Arr(data?["traits"]).Select(TasteTrait.From).Where(t => t.Id != null).ToList();
      return;
    }
    data = (await plugin.Mirror("taste-profile", fresh)).Value ?? data;
    help = (await plugin.Mirror("taste-help", fresh)).Value ?? help;
    changes = (await plugin.Mirror("taste-changes", fresh)).Value ?? changes;
    myName = myName ?? S((await plugin.Mirror("me", false)).Value?["user"], "displayName");
    traits = Arr(data?["traits"]).Select(TasteTrait.From).Where(t => t.Id != null).ToList();
  }

  // ---- Writes ----
  async Task SetStance(string id, string stance, string label, string before, bool undo) {
    if (busy || !Own) return;
    busy = true; Render();
    try {
      var body = new JObject { ["characteristicId"] = id, ["stance"] = stance == null ? JValue.CreateNull() : (JToken)stance };
      var ok = await plugin.Write("taste/corrections", body);
      if (ok != null) {
        error = null;
        var text = undo ? $"Undone. {label} is back to {LeanWord(stance)}." : stance != null ? $"{label} set to {LeanWord(stance)}." : $"{label} is back to Blastlog’s read.";
        toast.Show(text, undo ? (Action)null : () => Run(() => SetStance(id, before, label, stance, true), "taste undo"));
        await Read(true);
      }
    } finally { busy = false; Render(); }
  }
  void OnStance(string id, string stance, string label = null) {
    var trait = traits.FirstOrDefault(t => t.Id == id); var before = trait?.Correction;
    if (trait != null && stance == before) return;
    Run(() => SetStance(id, stance, trait?.Label ?? label ?? "That trait", before, false), "taste stance");
  }
  void OnReading(string id, string action) => Run(async () => {
    if (busy || !Own) return; busy = true; Render();
    try { if (await plugin.Write($"taste/readings/{id}/correction", new JObject { ["action"] = action == null ? JValue.CreateNull() : (JToken)action }) != null) await Read(true); }
    finally { busy = false; Render(); }
  }, "taste reading");
  void RemoveQuestion(string id) => Run(async () => {
    if (busy || !Own) return; busy = true; Render();
    try { if (await plugin.Write($"taste/questions/{id}/remove", new JObject()) != null) await Read(true); }
    finally { busy = false; Render(); }
  }, "taste question");

  // ---- Helpers (the site's TasteProfile.tsx rules) ----
  static string LeanWord(string s) => s == null ? "Blastlog’s read" : Scale.FirstOrDefault(x => x.key == s).label ?? s;
  static string StanceLabel(string s) => Scale.FirstOrDefault(x => x.key == s).label ?? s;
  static bool IsLove(TasteTrait t) => t.Correction != null && t.Correction != "neutral" ? Positive.Contains(t.Correction)
    : t.Correction == "neutral" ? (t.Inferred ?? 0) >= 0 : t.Value > 0;
  static double Weight(TasteTrait t) => Math.Abs(t.Value) * Math.Log(1 + t.Count, 2) + (t.Correction != null ? 1 : 0);
  static string NearestStance(double value) => Scale.Where(s => s.key != "dealbreaker" && s.key != "instant_yes").OrderBy(s => Math.Abs(s.value - value)).First().key;
  static string Quoted(string text) { var t = (text ?? "").Trim(); return "“" + (t.Length > 0 && char.IsLower(t[0]) && t[0] <= 'z' ? "…" : "") + t + "”"; }
  static JToken BestQuote(TasteTrait t, bool love) => t.Readings.Where(r => S(r, "correction") != "dismiss" && (love ? (D(r, "sentiment") ?? 0) > 0 : (D(r, "sentiment") ?? 0) < 0)
      && (S(r, "quote") ?? "").Trim().Length >= 25 && (S(r, "quote") ?? "").Trim().Length <= 200)
    .OrderByDescending(r => S(r, "target") == "preference").ThenByDescending(r => S(r, "clarity") == "clear").ThenByDescending(r => Math.Abs(D(r, "sentiment") ?? 0)).FirstOrDefault();
  List<TasteTrait> Settle(string key, List<TasteTrait> fresh, string[] group = null) {
    group = group ?? new string[0];
    if (!order.TryGetValue(key, out var kept)) { order[key] = fresh.Select(t => t.Id).ToList(); return fresh; }
    var placed = new HashSet<string>(group.SelectMany(k => order.TryGetValue(k, out var l) ? l : new List<string>()));
    var ids = kept.Concat(fresh.Where(t => group.Length > 0 && !placed.Contains(t.Id)).Select(t => t.Id)).ToList();
    order[key] = ids;
    var byId = traits.ToDictionary(t => t.Id);
    return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
  }

  // ---- The page ----
  void Render() {
    page.Children.Clear();
    page.Children.Add(Header());
    if (!plugin.Configured) { page.Children.Add(Kit.Card(T("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see your taste here."))); return; }
    if (data == null) { page.Children.Add(T(loaded ? Say("Loading your taste…", $"Loading {friend?.Name}’s taste…") : "", "Blastlog.Muted")); return; }
    if (error != null) page.Children.Add(T(error, "Blastlog.Body", "Blastlog.Danger"));
    var items = Arr(changes?["items"]);
    if (items.Count > 0) page.Children.Add(RecentChanges(items));
    if (I(data, "ratedTotal") < 5) page.Children.Add(T("Rate a few more games and this page fills in.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 16, 0, 0)));

    var chapter = 0; string Next() => (++chapter).ToString("00", Inv);
    var loves = Settle("loves", traits.Where(t => IsLove(t) && t.Correction != "neutral").OrderByDescending(Weight).Take(6).ToList());
    var avoids = Settle("avoids", traits.Where(t => t.Correction != "neutral" && (!IsLove(t) && t.Value < 0 || Negative.Contains(t.Correction))).OrderByDescending(Weight).Take(4).ToList());
    var fine = new[] { "fine-loves", "fine-avoids" };
    var fineLoves = Settle("fine-loves", traits.Where(IsLove).ToList(), fine);
    var fineAvoids = Settle("fine-avoids", traits.Where(t => !IsLove(t)).ToList(), fine);
    var yours = Settle("fine-yours", traits.Where(t => t.Correction != null).OrderBy(t => t.Label, StringComparer.CurrentCulture).ToList(), new[] { "fine-yours" });
    var questions = Arr(data["tasteQuestions"]).Where(q => (bool?)q["used"] == true).ToList();
    var sweet = SweetGroups();
    var inOwnWords = Arr(data["inOwnWords"]);

    if (loves.Count > 0) page.Children.Add(Chapter(Next(), Say("What hooks you", "What hooks them"), Say("The things that most reliably make a game land for you.", "The things that most reliably make a game land for them."),
      Columns(380, loves.Select(t => (UIElement)Spotlight(t, true)))));
    if (avoids.Count > 0) page.Children.Add(Chapter(Next(), Say("What loses you", "What loses them"), Say("What tends to make you check out, and the exceptions that still win you over.", "What tends to make them check out, and the exceptions that still win them over."),
      Columns(340, avoids.Select(t => (UIElement)Spotlight(t, false)))));
    if (traits.Count >= 6) page.Children.Add(Chapter(Next(), "Area by area",
      Say("Where you lean", "Where they lean") + " in each part of a game. Solid leans toward, striped leans away; faded bars are early signals (fewer than 6 games).", Areas()));
    page.Children.Add(Chapter(Next(), Say("Your numbers", "Their numbers"), Say("Your ratings at a glance.", "Their ratings at a glance."), Glance()));
    if (sweet.Count > 0) page.Children.Add(Chapter(Next(), "Sweet spots", Say("How you rate", "How they rate") + " games, on average, by how long they take to beat and when they came out.", SweetSpots(sweet)));
    if (inOwnWords.Count > 0) page.Children.Add(Chapter(Next(), Say("In your own words", "In their own words"), "Things said about games in general, pulled from reviews.", QuoteWall(inOwnWords)));
    if (questions.Count > 0) page.Children.Add(Chapter(Next(), "Must-haves & dealbreakers", Say("The sharpest wants and dealbreakers in your reviews.", "The sharpest wants and dealbreakers in their reviews.") + " Every game is checked for them.", MustHaves(questions)));
    fineTune = Chapter(Next(), "Fine-tune", Say("Everything Blastlog has worked out, and how to correct it. Your corrections always win, and change Play next and Next up right away.",
        $"Everything Blastlog has worked out about {friend?.Name}’s taste. Only they can correct it."),
      FineTune(fineLoves, fineAvoids, yours));
    page.Children.Add(fineTune);
    page.Children.Add(T(plugin.CacheLine(), "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 30, 0, 0)));
  }

  FrameworkElement Header() {
    var who = Row();
    var name = myName ?? "You";
    var face = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), Margin = new Thickness(0, 0, 14, 0),
      Child = T(name.Substring(0, 1), "Blastlog.Body", "Blastlog.OnGame", 24, FontWeights.ExtraBold).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }) };
    face.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game");
    AutomationProperties.SetName(face, name);
    who.Children.Add(face);
    // The picture and account settings are the native Settings window (Screens/SettingsWindow.cs); a change there
    // (the length level, the picture) shows here once it closes.
    if (Own) {
      void Settings() => Nav.Settings(plugin, () => Run(async () => { await Read(true); Render(); }, "taste after settings"));
      who.Children.Add(Link("Change picture", Settings).Also(b => b.VerticalAlignment = VerticalAlignment.Center));
      who.Children.Add(Link("Account settings", Settings).Also(b => b.VerticalAlignment = VerticalAlignment.Center));
    }
    who.Children.Add(Link("Refresh", () => Run(async () => { await Read(true); Render(); }, "taste refresh")).Also(b => b.VerticalAlignment = VerticalAlignment.Center));
    var head = Stack(null, who);
    head.Children.Add(T("Taste", "Blastlog.Small", "Blastlog.GameText", 0, FontWeights.Bold).Also(t => t.Margin = new Thickness(0, 18, 0, 2)));
    head.Children.Add(T(Say("Your taste", friend?.Name + "’s taste"), "Blastlog.PageTitle"));
    if (data != null) {
      var read = I(data["analysis"], "read");
      head.Children.Add(T($"{Say("What you love, what you skip", "What they love, what they skip")}, and why: from {I(data, "ratedTotal")} ratings{(read > 0 ? $" and {read} reviews" : "")}.", "Blastlog.Muted", null, 17).Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
    }
    var k = Own ? help?["knowledge"] : null;
    if (k != null) {
      var level = D(k, "level");
      var badge = level == null || level == 0
        ? Box(T("Taste level: rate a few more games", "Blastlog.Small"), null, 999, new Thickness(12, 4, 12, 4), "Blastlog.Line")
        : Box(Runs("Blastlog.Small", ("Taste level ", false, null), (level.Value.ToString("0", Inv), true, "Blastlog.GameText"), ("/" + I(k, "maxLevel"), false, null),
            (" · guesses " + Math.Round((D(k, "accuracy") ?? 0) * 100).ToString("0", Inv) + "% right", false, null)), null, 999, new Thickness(12, 4, 12, 4), "Blastlog.GameEdge");
      badge.VerticalAlignment = VerticalAlignment.Center; badge.Margin = new Thickness(0, 0, 10, 0);
      var row = Row(badge, Link("Sharpen your picks →", () => Nav.Sharpen(plugin)));
      row.Margin = new Thickness(0, 12, 0, 0);
      head.Children.Add(row);
    }
    var key = Stack(new Thickness(0, 14, 0, 0),
      Row(Pill(Say("You said", "They said"), filled: true), T(Say("you set it, and it always wins", "they set it, and it always wins"), "Blastlog.Small").Also(t => t.VerticalAlignment = VerticalAlignment.Center)),
      Row(Pill("Blastlog’s read"), T(Say("worked out from your ratings and reviews; correct it any time", "worked out from their ratings and reviews"), "Blastlog.Small").Also(t => t.VerticalAlignment = VerticalAlignment.Center)),
      Row(Pill("Early signal", dashed: true), T("fewer than 6 games behind it", "Blastlog.Small").Also(t => t.VerticalAlignment = VerticalAlignment.Center)));
    foreach (UIElement r in key.Children) ((FrameworkElement)r).Margin = new Thickness(0, 0, 0, 6);
    head.Children.Add(key);
    return head;
  }

  FrameworkElement RecentChanges(JArray items) => Disclosure($"Recent changes  {items.Count}", () => {
    var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
    foreach (var c in items) {
      var at = DateTimeOffset.TryParse(S(c, "at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var when) ? when.ToLocalTime() : (DateTimeOffset?)null;
      var time = at == null ? "" : at.Value.Date == DateTime.Today ? at.Value.ToString("h:mm tt", CultureInfo.CurrentCulture) : at.Value.ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture);
      var label = S(c, "label") ?? ""; var before = S(c, "before"); var after = S(c, "after");
      var text = Stack(null, Runs("Blastlog.Body", (label, true, null), ("  " + LeanWord(before) + " → " + LeanWord(after), false, "Blastlog.TextMuted")),
        T(time + (S(c, "setBy") != null ? $" · {S(c, "setBy")} set this" : ""), "Blastlog.Small"));
      var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true };
      if ((bool?)c["latest"] == true) {
        var undo = Kit.Button("Undo", () => Run(() => SetStance(S(c, "characteristicId"), before, label, after, true), "taste undo"), "Blastlog.Ghost");
        undo.IsEnabled = !busy; AutomationProperties.SetName(undo, $"Undo: {label} back to {LeanWord(before)}");
        DockPanel.SetDock(undo, Dock.Right); row.Children.Add(undo);
      }
      row.Children.Add(text);
      list.Children.Add(row);
    }
    return Kit.Card(list).Also(b => b.MaxWidth = 760);
  }, changesOpen, open => changesOpen = open).Also(p => p.Margin = new Thickness(0, 14, 0, 0));

  // A hook or a loss: what you told Blastlog, what your ratings show, what your reviews say, the lean on five steps.
  FrameworkElement Spotlight(TasteTrait t, bool love) {
    var games = (love ? t.RatedGames.OrderByDescending(g => D(g, "rating") ?? 0) : t.RatedGames.OrderBy(g => D(g, "rating") ?? 0)).Take(4).ToList();
    var quote = BestQuote(t, love);
    var gameCount = (int)Math.Round(t.Count);
    var ratings = t.RatedGames.Select(g => D(g, "rating") ?? 0).ToList();
    double? average = ratings.Count > 0 ? ratings.Average() : (double?)null;
    var reviews = t.Readings.Count(r => S(r, "correction") != "dismiss");
    var current = t.Correction != null ? CardSteps.FirstOrDefault(s => s.key == t.Correction).key : CardSteps.FirstOrDefault(s => s.derived == t.Stance).key;

    var card = new StackPanel();
    var head = new DockPanel { LastChildFill = true };
    var source = Pill(t.Correction != null ? Say("You said", "They said") : "Blastlog’s read", filled: t.Correction != null); source.Margin = new Thickness(10, 0, 0, 0);
    DockPanel.SetDock(source, Dock.Right); head.Children.Add(source);
    head.Children.Add(T(t.Label, "Blastlog.CardTitle"));
    card.Children.Add(head);

    // Five across when they fit, otherwise two rows (the site wraps them the same way on narrow cards).
    var steps = new UniformGrid { Columns = CardSteps.Length };
    steps.SizeChanged += (o, e) => { var n = e.NewSize.Width >= CardSteps.Length * 76 ? CardSteps.Length : 3; if (steps.Columns != n) steps.Columns = n; };
    foreach (var s in CardSteps) {
      var on = current == s.key;
      var b = Kit.Button(s.label, () => OnStance(t.Id, s.key), on ? "Blastlog.Primary" : "Blastlog.TextButton");
      b.Margin = new Thickness(1); b.MinHeight = 40; b.Padding = new Thickness(4, 0, 4, 0); b.FontSize = 14; b.IsEnabled = !busy && Own;
      AutomationProperties.SetName(b, s.label + (on ? ", selected" : ""));
      AutomationProperties.SetHelpText(b, $"How much {t.Label} matters to {Say("you", "them")}");
      steps.Children.Add(b);
    }
    card.Children.Add(Box(steps, "Blastlog.Surface2", 12, new Thickness(3)).Also(x => x.Margin = new Thickness(0, 14, 0, 14)));

    UIElement Evidence(string label, UIElement value) => Stack(new Thickness(0, 0, 0, 12), T(label, "Blastlog.Small", null, 0, FontWeights.Bold), value);
    card.Children.Add(Evidence(Say("What you told Blastlog", "What they told Blastlog"), T(t.Correction != null ? StanceLabel(t.Correction) : "Nothing yet.")));
    var ratingsShow = Stack(null, T(gameCount > 0 ? $"{Plural(gameCount, "rated game")} with it{(average != null ? $", average {Stars(Math.Round(average.Value * 10) / 10)}" : "")}" : "No rated games with it yet."));
    if (games.Count > 0) {
      var covers = Row();
      foreach (var g in games) {
        var c = Stack(new Thickness(0, 8, 8, 0), Cover(plugin, S(g, "coverUrl"), S(g, "title"), 52, 8), T(Stars(D(g, "rating") ?? 0), "Blastlog.Small", "Blastlog.Star", 0, FontWeights.Bold));
        c.ToolTip = $"{S(g, "title")} · {Stars(D(g, "rating") ?? 0)}";
        covers.Children.Add(c);
      }
      ratingsShow.Children.Add(covers);
    }
    card.Children.Add(Evidence(Say("What your ratings show", "What their ratings show"), ratingsShow));
    card.Children.Add(Evidence(Say("What your reviews say", "What their reviews say"), quote != null
      ? Runs("Blastlog.Body", (Quoted(S(quote, "quote")) + " ", false, null), ("— " + S(quote, "gameTitle"), false, "Blastlog.TextMuted"))
      : T("No review mentions it.", "Blastlog.Muted")));
    var exceptionGames = Arr(t.Exceptions?["games"]);
    if (!love && t.Correction != "dealbreaker" && exceptionGames.Count > 0) {
      var shared = Arr(t.Exceptions["sharedTraits"]).Select(x => (string)x).Take(3).ToList();
      card.Children.Add(Runs("Blastlog.Body", ("Unless", true, null), (" it’s like " + string.Join(", ", exceptionGames.Take(3).Select(g => S(g, "title")))
        + (shared.Count > 0 ? " (" + string.Join(", ", shared).ToLowerInvariant() + ")" : "") + ".", false, null)).Also(x => x.Margin = new Thickness(0, 0, 0, 12)));
    }
    var correct = Kit.Button("Correct this", () => fineTune?.BringIntoView(new Rect(0, 0, 1, 400)), "Blastlog.Ghost");
    var lean = Row(gameCount < 6 ? (UIElement)Pill("Early signal", dashed: true) : T("Clear lean", "Blastlog.Small"),
      T($" · {Plural(gameCount, "game")}{(reviews > 0 ? ", " + Plural(reviews, "review") : "")}", "Blastlog.Small").Also(x => x.VerticalAlignment = VerticalAlignment.Center));
    lean.VerticalAlignment = VerticalAlignment.Center; lean.Margin = new Thickness(0, 0, 14, 8);
    correct.Margin = new Thickness(0);
    card.Children.Add(Own ? Wrap(lean, correct) : Wrap(lean));

    var border = Kit.Card(card);
    border.Margin = new Thickness(0);
    // The site's .tp-spot top: ok (or danger) at 40%, not full strength.
    border.BorderThickness = new Thickness(0, 3, 0, 0);
    border.BorderBrush = Faded(love ? "ok" : "danger", 0.4);
    return border;
  }

  // Each part of a game: a bar from the middle, solid toward and striped away, faded under 6 games, + / − chips.
  FrameworkElement Areas() {
    var areas = FamilyOrder.Select(family => {
      var own = traits.Where(t => t.Family == family).ToList();
      if (own.Count < 2) return null;
      var mass = own.Sum(t => Math.Min(t.Count, 10));
      var lean = mass > 0 ? own.Sum(t => t.Value * Math.Min(t.Count, 10)) / mass : 0;
      var games = own.SelectMany(t => t.RatedGames.Select(g => S(g, "gameId"))).Distinct().Count();
      return new { family, lean, games, loves = own.Where(IsLove).OrderByDescending(Weight).Take(2).ToList(), avoids = own.Where(t => !IsLove(t)).OrderByDescending(Weight).Take(2).ToList() };
    }).Where(a => a != null).ToList();
    var strongest = Math.Max(.05, areas.Select(a => Math.Abs(a.lean)).DefaultIfEmpty(0).Max());
    var list = new StackPanel();
    const double W = 240;
    foreach (var a in areas) {
      var size = Math.Abs(a.lean) / strongest; var neutral = size < .12; var early = a.games < 6;
      var word = neutral ? "Neutral" : a.lean >= 0 ? "Leans toward" : "Leans away";
      var summary = $"{word} · {Plural(a.games, "game")}{(early ? " · early" : "")}";
      var label = FamilyLabel.TryGetValue(a.family, out var l) ? l : a.family;
      var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
      foreach (var w in new[] { 200.0, W + 24 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
      row.ColumnDefinitions.Add(new ColumnDefinition());
      row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      var name = T(label, "Blastlog.Body", null, 0, FontWeights.SemiBold); name.VerticalAlignment = VerticalAlignment.Center;
      var track = new Grid { Width = W, Height = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
      track.Children.Add(Box(null, "Blastlog.Surface2", 7, new Thickness(0)));
      var mid = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Center }; mid.SetResourceReference(Border.BackgroundProperty, "Blastlog.Edge"); track.Children.Add(mid);
      Border fill;
      if (neutral) { fill = new Border { Width = 12, CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Center }; fill.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game"); }
      else if (a.lean >= 0) { fill = new Border { Width = Math.Max(.04, size * .5) * W, CornerRadius = new CornerRadius(0, 7, 7, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(W / 2, 0, 0, 0) }; fill.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game"); }
      else { fill = new Border { Width = Math.Max(.04, size * .5) * W, CornerRadius = new CornerRadius(7, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, W / 2, 0),
          Background = Stripes("Blastlog.Danger"), BorderThickness = new Thickness(1.5) }; fill.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Danger"); }
      if (early) fill.Opacity = .45;
      track.Children.Add(fill);
      AutomationProperties.SetName(track, $"{label}: {summary}");
      var wordText = T(summary, "Blastlog.Small", early ? "Blastlog.TextMuted" : "Blastlog.Text"); wordText.VerticalAlignment = VerticalAlignment.Center;
      // The strongest pulls either way sit on their own line under the bar, so they wrap at any width.
      var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
      foreach (var t in a.loves) chips.Children.Add(Kit.Chip("+ " + t.Label));
      foreach (var t in a.avoids) chips.Children.Add(Kit.Chip("− " + t.Label, neutral: true));
      Grid.SetColumn(track, 1); Grid.SetColumn(wordText, 2); Grid.SetRow(chips, 1); Grid.SetColumn(chips, 1); Grid.SetColumnSpan(chips, 2);
      row.Children.Add(name); row.Children.Add(track); row.Children.Add(wordText); row.Children.Add(chips);
      list.Children.Add(row);
    }
    return Kit.Card(list);
  }

  // Four plain facts.
  FrameworkElement Glance() {
    var h = data["highlights"];
    List<JToken> Solid(JToken list) => Arr(list).Where(b => I(b, "games") >= GlanceMinGames).ToList();
    JToken Best(JToken list) => Solid(list).OrderByDescending(b => D(b, "avg") ?? 0).FirstOrDefault();
    JToken Worst(JToken list) => Solid(list).OrderBy(b => D(b, "avg") ?? 0).FirstOrDefault();
    var length = Best(h?["sweetSpots"]?["length"]); var shortest = Worst(h?["sweetSpots"]?["length"]); var era = Best(h?["sweetSpots"]?["decades"]);
    var average = D(h?["finished"], "mean") ?? D(data, "meanRating") ?? 0; var finished = h?["finished"]?["games"] != null ? I(h["finished"], "games") : (int?)null;
    var group = D(h, "groupMeanRating"); var diff = group == null ? 0 : average - group.Value;
    var verdict = group == null || I(h, "groupPlayers") < 2 ? null : Math.Abs(diff) < .15 ? $"About the same as the group ({Stars(group.Value)})"
      : diff < 0 ? $"Tougher than the group ({Stars(group.Value)})" : $"More generous than the group ({Stars(group.Value)})";
    var stopped = h?["stopped"]; var stoppedLine = stopped != null && I(stopped, "games") > 0 && D(stopped, "mean") != null ? $"{I(stopped, "games")} quit or unfinished · {Stars(D(stopped, "mean").Value)}" : null;
    UIElement Tile(string big, string label, params string[] small) {
      var s = Stack(null, T(big, "Blastlog.Body", null, 26, FontWeights.ExtraBold), T(label, "Blastlog.Small"));
      foreach (var x in small) if (x != null) s.Children.Add(T(x, "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 4, 0, 0)));
      return Box(s, "Blastlog.Surface", 20, new Thickness(22, 20, 22, 20));
    }
    var tiles = new List<UIElement>();
    var read = I(data["analysis"], "read"); var rated = I(data, "ratedTotal");
    tiles.Add(Tile(rated.ToString(Inv), "games rated", read > 0 ? $"{read} reviews read" : null));
    if (rated > 0) tiles.Add(Tile(Stars(Math.Round(average * 10) / 10), "your average" + (finished != null ? $" on {finished} finished games" : " rating"), verdict, stoppedLine));
    if (length != null) tiles.Add(Tile(S(length, "label"), "sweet spot for length",
      $"{Stars(D(length, "avg") ?? 0)} on average{(shortest != null && S(shortest, "key") != S(length, "key") ? $" · {S(shortest, "label").ToLowerInvariant()} {Stars(D(shortest, "avg") ?? 0)}" : "")}"));
    if (era != null) tiles.Add(Tile(S(era, "label"), "favourite release era", $"{Stars(D(era, "avg") ?? 0)} across {I(era, "games")} games"));
    return Columns(210, tiles, 14);
  }

  List<(string title, List<JToken> list)> SweetGroups() {
    var s = data["highlights"]?["sweetSpots"];
    return new[] { ("By length", Arr(s?["length"])), ("By release era", Arr(s?["decades"])) }
      .Select(g => (g.Item1, g.Item2.Where(b => (bool?)b["enough"] == true).ToList())).Where(g => g.Item2.Count >= 2).ToList();
  }
  FrameworkElement SweetSpots(List<(string title, List<JToken> list)> groups) {
    var cards = groups.Select(g => {
      var solid = g.list.Where(b => I(b, "games") >= GlanceMinGames).ToList();
      var top = (solid.Count > 0 ? solid : g.list).Max(b => D(b, "avg") ?? 0);
      var rows = Stack(null, T(g.title, "Blastlog.CardTitle").Also(t => t.Margin = new Thickness(0, 0, 0, 12)));
      foreach (var b in g.list) {
        var avg = D(b, "avg") ?? 0; var best = Math.Abs(avg - top) < 1e-9 && (I(b, "games") >= GlanceMinGames || solid.Count == 0);
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var w in new[] { 130.0, 0, 56, 80 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        var bar = new Grid { Height = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        bar.Children.Add(Box(null, "Blastlog.Surface2", 5, new Thickness(0)));
        var fill = new Border { CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left }; fill.SetResourceReference(Border.BackgroundProperty, best ? "Blastlog.Game" : "Blastlog.GameBar");
        bar.Children.Add(fill);
        bar.SizeChanged += (s, e) => fill.Width = e.NewSize.Width * avg / 5;
        var label = T(S(b, "label"), "Blastlog.Body", null, 15, best ? FontWeights.Bold : FontWeights.Normal);
        var stars = T(Stars(avg), "Blastlog.Body", "Blastlog.Star", 15, FontWeights.Bold);
        var count = T($"{I(b, "games")} games", "Blastlog.Small"); count.ToolTip = $"{I(b, "games")} rated games";
        foreach (var x in new FrameworkElement[] { label, stars, count }) x.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(bar, 1); Grid.SetColumn(stars, 2); Grid.SetColumn(count, 3);
        row.Children.Add(label); row.Children.Add(bar); row.Children.Add(stars); row.Children.Add(count);
        rows.Children.Add(best ? (UIElement)Box(row, "Blastlog.GameTint", 10, new Thickness(8, 4, 8, 4)) : Box(row, null, 10, new Thickness(8, 4, 8, 4)));
      }
      return (UIElement)Kit.Card(rows).Also(c => c.Margin = new Thickness(0));
    });
    return Columns(380, cards);
  }

  FrameworkElement QuoteWall(JArray readings) {
    var seen = new HashSet<string>();
    var picks = readings.Where(r => S(r, "correction") != "dismiss" && (S(r, "quote") ?? "").Trim().Length >= 25 && (S(r, "quote") ?? "").Trim().Length <= 220)
      .OrderByDescending(r => S(r, "clarity") == "clear").ThenByDescending(r => Math.Abs(D(r, "sentiment") ?? 0))
      .Where(r => seen.Add(S(r, "gameTitle") ?? "")).Take(6);
    return Columns(300, picks.Select(r => {
      var neg = (D(r, "sentiment") ?? 0) < 0;
      var b = Box(Stack(null, T(Quoted(S(r, "quote")), "Blastlog.Body", null, 17), T(S(r, "gameTitle"), "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 10, 0, 0))),
        "Blastlog.Surface", 18, new Thickness(22, 20, 22, 20));
      // The site's .tp-wall blockquote: a 3px left edge, ok (or danger) at 33%.
      b.BorderThickness = new Thickness(3, 0, 0, 0); b.BorderBrush = Faded(neg ? "danger" : "ok", 0.333);
      return (UIElement)b;
    }), 14);
  }

  FrameworkElement MustHaves(List<JToken> questions) {
    var cols = new List<UIElement>();
    foreach (var p in new[] { "want", "avoid" }) {
      var list = questions.Where(q => S(q, "polarity") == p).OrderByDescending(q => D(q, "strength") ?? 0).ToList();
      if (list.Count == 0) continue;
      // The site's .tp-must: no coloured edge; the title is in ok-text (or danger).
      var title = T(p == "want" ? "Must-haves" : "Dealbreakers", "Blastlog.CardTitle").Also(t => t.Margin = new Thickness(0, 0, 0, 10));
      if (p == "want") title.Foreground = TokenBrush("ok-text"); else title.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Danger");
      var s = Stack(null, title);
      foreach (var q in list) s.Children.Add(Stack(new Thickness(0, 0, 0, 10), T(S(q, "label"), "Blastlog.Body", null, 0, FontWeights.Bold), T(S(q, "question"), "Blastlog.Muted")));
      var card = Kit.Card(s); card.Margin = new Thickness(0);
      cols.Add(card);
    }
    return Columns(340, cols);
  }

  // ---- Fine-tune ----
  FrameworkElement FineTune(List<TasteTrait> loves, List<TasteTrait> avoids, List<TasteTrait> yours) {
    var panel = new StackPanel();
    var a = data["analysis"];
    if (I(a, "withText") > 0 && I(a, "read") < I(a, "withText"))
      panel.Children.Add(T($"{(I(a, "pending") > 0 ? $"Reading {I(a, "pending")} reviews now… " : "")}{I(a, "read")} of {I(a, "withText")} reviews read so far.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 0, 0, 12)));
    if (Own) panel.Children.Add(AddStance());
    var questions = Arr(data["tasteQuestions"]); var words = Arr(data["inOwnWords"]); var lengthNotes = Arr(data["lengthNotes"]);
    var tabs = new[] { ("loves", "Loves & likes", loves.Count), ("avoids", "Avoids & dislikes", avoids.Count), ("yours", Say("You set", "They set"), yours.Count),
      ("questions", "Wants & dealbreakers", questions.Count), ("words", "Own words", words.Count), ("length", "Length & pacing", lengthNotes.Count) }.Where(t => t.Item3 > 0).ToList();
    if (tabs.Count > 0 && !tabs.Any(t => t.Item1 == fineTab)) fineTab = tabs[0].Item1;
    var bar = Wrap(); bar.Margin = new Thickness(0, 16, 0, 8);
    foreach (var tab in tabs) bar.Children.Add(Toggle($"{tab.Item2}  {tab.Item3}", fineTab == tab.Item1, () => { fineTab = tab.Item1; shown = 12; fineSearch = ""; Render(); }, tab.Item2));
    panel.Children.Add(bar);
    if (fineTab == "loves" || fineTab == "avoids" || fineTab == "yours") {
      var list = fineTab == "loves" ? loves : fineTab == "yours" ? yours : avoids;
      var (box, input) = SearchField("Find a trait…");
      input.Text = fineSearch;
      var legend = Row(Legend(true, Say("Your call", "Their call")), Legend(false, Say("Blastlog’s read from your ratings and reviews", "Blastlog’s read from their ratings and reviews")));
      legend.Margin = new Thickness(18, 0, 0, 0); legend.VerticalAlignment = VerticalAlignment.Center;
      panel.Children.Add(Wrap(box, legend));
      fineResults = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
      panel.Children.Add(fineResults);
      void Results() {
        fineResults.Children.Clear();
        var q = fineSearch.Trim();
        var matching = list.Where(t => q.Length == 0 || Fold(t.Label).Contains(Fold(q))).ToList();
        fineResults.Children.Add(Columns(360, matching.Take(shown).Select(t => (UIElement)TraitCard(t))));
        if (matching.Count == 0) fineResults.Children.Add(T("Nothing matches.", "Blastlog.Muted"));
        if (matching.Count > shown) fineResults.Children.Add(Kit.Button($"Show more ({matching.Count - shown} left)", () => { shown += 24; Results(); }, "Blastlog.Ghost").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
      }
      Debounce(input, 200, text => { fineSearch = text; shown = 12; Results(); });
      Results();
    } else if (fineTab == "questions") panel.Children.Add(TasteQuestions(questions));
    else if (fineTab == "words") panel.Children.Add(Readings(words));
    else if (fineTab == "length") {
      panel.Children.Add(T("What you say about how long games are and how they pace themselves. These lines don’t change picks on their own; how long games take shows in Sweet spots, and Quick one / Long haul on Next up and Play next pick by length.", "Blastlog.Muted").Also(t => { t.Margin = new Thickness(0, 0, 0, 12); t.MaxWidth = 760; t.HorizontalAlignment = HorizontalAlignment.Left; }));
      panel.Children.Add(Readings(lengthNotes));
    }
    return panel;
  }
  static string Fold(string s) => new string((s ?? "").Normalize(System.Text.NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
  static UIElement Legend(bool yours, string text) {
    var mark = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    if (yours) mark.SetResourceReference(Border.BackgroundProperty, "Blastlog.Ok");
    else { mark.BorderThickness = new Thickness(2); mark.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Text"); }
    return Row(mark, T(text, "Blastlog.Small").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 16, 0); }));
  }

  FrameworkElement TraitCard(TasteTrait t) {
    var stance = t.Correction != null ? StanceLabel(t.Correction) : Derived.TryGetValue(t.Stance, out var d) ? d : t.Stance;
    var tone = t.Correction != null ? (Positive.Contains(t.Correction) ? "Blastlog.Ok" : Negative.Contains(t.Correction) ? "Blastlog.Danger" : "Blastlog.TextMuted") : t.Value >= 0 ? "Blastlog.Ok" : "Blastlog.Danger";
    var s = new StackPanel();
    var head = new DockPanel { LastChildFill = true };
    var tag = T(stance, "Blastlog.Small", tone, 0, FontWeights.Bold); tag.Margin = new Thickness(10, 0, 0, 0); tag.VerticalAlignment = VerticalAlignment.Center;
    DockPanel.SetDock(tag, Dock.Right); head.Children.Add(tag);
    head.Children.Add(T(t.Label, "Blastlog.CardTitle"));
    s.Children.Add(head);
    if (!string.IsNullOrEmpty(t.Definition)) {
      var full = fullDefinition.Contains(t.Id);
      var def = T(t.Definition, "Blastlog.Muted"); def.Margin = new Thickness(0, 8, 0, 0);
      if (!full) { def.MaxHeight = 66; def.TextTrimming = TextTrimming.WordEllipsis; }
      var b = Plain(def, () => { if (!fullDefinition.Add(t.Id)) fullDefinition.Remove(t.Id); Render(); }, full ? t.Definition : "Read it all: " + t.Label);
      b.Margin = new Thickness(0); b.ToolTip = full ? null : "Tap to read it all";
      s.Children.Add(b);
    }
    var exceptionGames = Arr(t.Exceptions?["games"]);
    if (t.Exceptions != null && t.Correction != "dealbreaker" && exceptionGames.Count > 0) {
      var shared = Arr(t.Exceptions["sharedTraits"]).Select(x => (string)x).ToList();
      s.Children.Add(Runs("Blastlog.Muted", ("Except games like " + string.Join(", ", exceptionGames.Take(3).Select(g => S(g, "title"))), false, null),
        (shared.Count > 0 ? " — they share " : "", false, null), (string.Join(", ", shared), true, null), (".", false, null)).Also(x => x.Margin = new Thickness(0, 8, 0, 0)));
    }
    if (t.Note != null) s.Children.Add(T("Note: " + t.Note, "Blastlog.Muted").Also(x => x.Margin = new Thickness(0, 8, 0, 0)));
    s.Children.Add(StanceScale(t.Id, t.Label, t.SetBy, t.Correction, t.Correction != null ? t.Inferred : t.Value, st => OnStance(t.Id, st)));
    var sources = string.Join(" · ", t.Sources.Select(x => SourceWord.TryGetValue((string)x, out var w) ? w : (string)x));
    if (t.Pattern != null) {
      var kind = S(t.Pattern, "kind"); var praised = I(t.Pattern, "praised"); var criticised = I(t.Pattern, "criticised");
      sources += (sources.Length > 0 ? " · " : "") + (kind == "likes" ? $"praised in {praised} reviews" : kind == "dislikes" ? $"criticised in {criticised} reviews" : $"picky: {praised} praised, {criticised} criticised");
    }
    var open = openWhy.Contains(t.Id);
    var why = Link(open ? "Hide why" : $"Why? {Plural(t.GamesWithTrait, "game")}{(t.Readings.Count > 0 ? ", " + Plural(t.Readings.Count, "quote") : "")}", () => { if (!openWhy.Add(t.Id)) openWhy.Remove(t.Id); Render(); });
    AutomationProperties.SetName(why, (open ? "Hide why: " : "Why: ") + t.Label);
    var meta = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
    DockPanel.SetDock(why, Dock.Right); meta.Children.Add(why);
    meta.Children.Add(T(sources, "Blastlog.Small").Also(x => x.VerticalAlignment = VerticalAlignment.Center));
    s.Children.Add(meta);
    if (open) {
      var ev = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
      foreach (var g in t.RatedGames) {
        var counted = D(g, "counted");
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var stars = T(new string('★', (int)Math.Round(D(g, "rating") ?? 0)), "Blastlog.Body", "Blastlog.Star"); DockPanel.SetDock(stars, Dock.Right);
        AutomationProperties.SetName(stars, Stars(D(g, "rating") ?? 0));
        row.Children.Add(stars);
        var title = Runs("Blastlog.Body", (S(g, "title"), false, null), (counted != null && counted < .95 ? $" · counts {Math.Round(counted.Value * 100)}%" : "", false, "Blastlog.TextMuted"));
        if (counted != null && counted < .95) title.ToolTip = "The review said this rating was mostly about how well the game was made (craft, bugs, pacing), not what kind of game it is";
        row.Children.Add(title);
        ev.Children.Add(row);
      }
      if (t.Readings.Count > 0) ev.Children.Add(Readings(t.Readings));
      s.Children.Add(ev);
    }
    var card = Kit.Card(s); card.Margin = new Thickness(0);
    return card;
  }

  /// <summary>
  /// One scale from Never to Instant yes: seven bars, a gentle step taller toward the ends, red through grey to green.
  /// Your pick lifts; Blastlog's read keeps its outline; Reset (or your pick again) hands it back to Blastlog. The two
  /// ends ask first and say what they do.
  /// </summary>
  FrameworkElement StanceScale(string id, string label, string setBy, string chosen, double? guess, Action<string> pick) {
    var guessed = guess == null ? null : NearestStance(guess.Value);
    var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
    var bars = new UniformGrid { Rows = 1, Columns = Scale.Length, Height = 44 };
    var heights = new[] { 1.0, .86, .72, .6, .72, .86, 1.0 };
    var colours = new[] { "Blastlog.Danger", "Blastlog.Warn", "Blastlog.Warn", "Blastlog.TextMuted", "Blastlog.Ok", "Blastlog.Ok", "Blastlog.Ok" };
    for (var i = 0; i < Scale.Length; i++) {
      var step = Scale[i]; var on = chosen == step.key; var read = guessed == step.key;
      var fill = new Border { CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Bottom, Height = 36 * heights[i], Opacity = on ? 1 : .42,
        Margin = new Thickness(0, 0, 0, on ? 3 : 0) };
      fill.SetResourceReference(Border.BackgroundProperty, colours[i]);
      var ring = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(read ? 2 : 0), Padding = new Thickness(2), Child = fill, VerticalAlignment = VerticalAlignment.Bottom };
      ring.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Text");
      var b = Plain(ring, () => {
        if (chosen == step.key) { pick(null); return; }
        if (step.key == "instant_yes" || step.key == "dealbreaker") { asking = step.key; askingFor = id; Render(); return; }
        pick(step.key);
      }, step.label + (read ? ", Blastlog’s read" : "") + (on ? ", selected" : ""));
      b.Margin = new Thickness(3, 0, 3, 0); b.IsEnabled = !busy && Own; b.VerticalContentAlignment = VerticalAlignment.Bottom; b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
      b.ToolTip = $"{step.label}: {step.help}{(read ? " (Blastlog’s read)" : "")}";
      bars.Children.Add(b);
    }
    AutomationProperties.SetName(bars, $"How much {label} matters to you");
    panel.Children.Add(bars);
    var ends = new DockPanel(); var right = T("Instant yes", "Blastlog.Small"); DockPanel.SetDock(right, Dock.Right); ends.Children.Add(right); ends.Children.Add(T("Never", "Blastlog.Small"));
    panel.Children.Add(ends);
    if (askingFor == id && asking != null) {
      var key = asking;
      panel.Children.Add(Confirm(key == "instant_yes"
          ? $"Instant yes moves any game that clearly has {label} up your picks, even when the rest of it isn’t much like what you love. If you just like {label} a lot, Love is usually the better fit. Set Instant yes?"
          : $"Never drops every game that clearly has {label} to the bottom of your picks, and keeps it out of Next up and Discover. Set Never?",
        key == "instant_yes" ? "Set Instant yes" : "Set Never", key == "dealbreaker",
        () => { asking = askingFor = null; pick(key); }, () => { asking = askingFor = null; Render(); }));
    }
    var now = chosen ?? guessed;
    var foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = true };
    var reset = Kit.Button("Reset", () => pick(null), "Blastlog.TextButton");
    reset.IsEnabled = !busy && chosen != null && Own; reset.Visibility = chosen != null && Own ? Visibility.Visible : Visibility.Hidden;
    reset.ToolTip = guessed != null ? $"Back to Blastlog’s read ({StanceLabel(guessed)})" : "Let Blastlog decide";
    DockPanel.SetDock(reset, Dock.Right); foot.Children.Add(reset);
    foot.Children.Add((now != null ? Runs("Blastlog.Small", (StanceLabel(now), true, "Blastlog.Text"), (" · " + (chosen != null ? (setBy != null && setBy != myName ? $"{setBy} set this" : Say("your call", "their call")) : "Blastlog’s read"), false, null))
      : T(Say("Pick how much it matters", "Not set"), "Blastlog.Small")).Also(x => x.VerticalAlignment = VerticalAlignment.Center));
    panel.Children.Add(foot);
    return panel;
  }

  FrameworkElement Readings(JArray readings) {
    var list = new StackPanel();
    foreach (var r in readings) list.Children.Add(ReadingItem(r));
    return list;
  }
  FrameworkElement ReadingItem(JToken r) {
    var id = S(r, "id"); var correction = S(r, "correction"); var preference = S(r, "target") == "preference";
    var s = new StackPanel();
    var head = Wrap(T(S(r, "aspect"), "Blastlog.Body", null, 0, FontWeights.Bold).Also(t => { t.Margin = new Thickness(0, 0, 10, 0); t.VerticalAlignment = VerticalAlignment.Center; }));
    var sentiment = ((int)Math.Round(D(r, "sentiment") ?? 0)).ToString(Inv);
    head.Children.Add(T(Sentiment.TryGetValue(sentiment, out var w) ? w : sentiment, "Blastlog.Small", (D(r, "sentiment") ?? 0) < 0 ? "Blastlog.Warn" : (D(r, "sentiment") ?? 0) > 0 ? "Blastlog.Ok" : "Blastlog.TextMuted", 0, FontWeights.Bold)
      .Also(t => { t.Margin = new Thickness(0, 0, 10, 0); t.VerticalAlignment = VerticalAlignment.Center; }));
    head.Children.Add(Pill(preference ? "general taste" : $"about {S(r, "gameTitle")}"));
    if (S(r, "clarity") != "clear" && correction == null) head.Children.Add(Pill(S(r, "clarity") == "somewhat" ? "probably" : "unclear", dashed: true));
    if (correction != null) head.Children.Add(Pill(correction == "dismiss" ? "you said: not what I meant" : correction == "general" ? "you confirmed: general taste" : "you said: about this game only", filled: true));
    s.Children.Add(head);
    s.Children.Add(Runs("Blastlog.Body", ("“" + S(r, "quote") + "”", false, null), (preference ? " — review of " + S(r, "gameTitle") : "", false, "Blastlog.TextMuted")).Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
    var characteristic = S(r, "characteristic");
    s.Children.Add(characteristic != null
      ? Runs("Blastlog.Small", ("→ counts toward ", false, null), (characteristic, true, "Blastlog.Text"), (S(r, "linkFit") == "part_of" ? " (as an example of it)" : "", false, null))
      : T("→ explanation only (no matching characteristic)", "Blastlog.Small"));
    var actions = Wrap();
    if (!Own) { }
    else if (correction != null) actions.Children.Add(Link("Undo", () => OnReading(id, null)));
    else {
      actions.Children.Add(preference ? Link("That was about this game", () => OnReading(id, "game_only")) : Link("That’s my general taste", () => OnReading(id, "general")));
      actions.Children.Add(Link("Not what I meant", () => OnReading(id, "dismiss")));
    }
    foreach (UIElement a in actions.Children) a.IsEnabled = !busy;
    s.Children.Add(actions);
    var box = Box(s, "Blastlog.Surface2", 14, new Thickness(14, 12, 14, 6)); box.Margin = new Thickness(0, 0, 0, 10);
    if (correction == "dismiss") box.Opacity = .6;
    return box;
  }

  FrameworkElement TasteQuestions(JArray questions) {
    var used = questions.Where(q => (bool?)q["used"] == true).ToList(); var unused = questions.Where(q => (bool?)q["used"] != true).ToList();
    var s = new StackPanel();
    s.Children.Add(Runs("Blastlog.Section", ("Your taste questions  ", false, null), (used.Count.ToString(Inv), false, "Blastlog.TextMuted")));
    s.Children.Add(T("The sharpest wants and dealbreakers in your own reviews. Every game in the library is checked for them, and they count for or against games just for you. Dealbreakers only count when a game clearly has the thing, not an optional side mode.", "Blastlog.Muted")
      .Also(t => { t.MaxWidth = 760; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 14); }));
    s.Children.Add(Columns(360, used.Select(q => (UIElement)Question(q))));
    if (unused.Count > 0) s.Children.Add(Disclosure($"{unused.Count} more that don’t tell games apart", () => Columns(360, unused.Select(q => (UIElement)Question(q)))));
    return s;
  }
  FrameworkElement Question(JToken q) {
    var id = S(q, "id"); var want = S(q, "polarity") == "want"; var used = (bool?)q["used"] == true; var strength = I(q, "strength");
    var s = new StackPanel();
    var head = Wrap(Pill(want ? "Want" : "Dealbreaker", filled: !want), T(S(q, "label"), "Blastlog.Body", null, 0, FontWeights.Bold).Also(t => { t.Margin = new Thickness(0, 0, 10, 0); t.VerticalAlignment = VerticalAlignment.Center; }));
    var dots = T(new string('●', strength) + new string('○', Math.Max(0, 3 - strength)), "Blastlog.Small", "Blastlog.GameText"); dots.VerticalAlignment = VerticalAlignment.Center;
    AutomationProperties.SetName(dots, $"strength {strength} of 3"); head.Children.Add(dots);
    s.Children.Add(head);
    s.Children.Add(T(S(q, "question"), "Blastlog.Body").Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
    var quote = Arr(q["quotes"]).FirstOrDefault();
    if (quote != null) s.Children.Add(Runs("Blastlog.Muted", ("“" + S(quote, "quote") + "”", false, null), (" — " + S(quote, "game"), false, null)).Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
    string note;
    if (used) {
      var avg = D(q, "avgRatingWithIt"); var weight = D(q, "weight");
      note = $"{I(q, "ratedWithIt")} rated game{(I(q, "ratedWithIt") == 1 ? "" : "s")} have it{(avg != null ? $", averaging {avg.Value.ToString("0.0", Inv)}★" : "")}"
        + (weight != null ? $" · counts {(weight > 0 ? "for" : "against")} games ({(weight > 0 ? "+" : "")}{weight.Value.ToString("0.00", Inv)})" : "");
    } else {
      var share = D(q, "matchShare");
      note = $"Not used: {S(q, "notUsedReason")}{(share != null ? $" ({Math.Round(share.Value * 100)}% of your games have it)" : "")}.";
    }
    s.Children.Add(T(note, "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
    if (confirmingQuestion == id) {
      var row = Wrap(Kit.Button("Remove it", () => { confirmingQuestion = null; RemoveQuestion(id); }, "Blastlog.DangerButton"), Kit.Button("Keep", () => { confirmingQuestion = null; Render(); }, "Blastlog.Ghost"));
      row.Margin = new Thickness(0, 10, 0, 0); s.Children.Add(row);
    } else if (Own) s.Children.Add(Link("That’s not me", () => { confirmingQuestion = id; Render(); }).Also(b => { b.IsEnabled = !busy; b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(-6, 6, 0, 0); }));
    var card = Kit.Card(s); card.Margin = new Thickness(0); if (!used) card.Opacity = .75;
    return card;
  }

  // Tell Blastlog something directly: find any trait and set how much it matters.
  FrameworkElement AddStance() => Disclosure("Tell Blastlog something directly (“horror is a dealbreaker”, “farming games are an instant yes”)", () => {
    var s = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
    var (box, input) = SearchField("Search characteristics…", 420);
    input.Text = addSearch;
    s.Children.Add(box);
    addResults = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
    s.Children.Add(addResults);
    async Task Search(string text) {
      addSearch = text; addPicked = null;
      var q = text.Trim();
      addResults.Children.Clear();
      if (q.Length < 2) return;
      var found = (await plugin.Mirror("taste/characteristics?search=" + Uri.EscapeDataString(q.Length > 80 ? q.Substring(0, 80) : q), false)).Value;
      if (addSearch.Trim() != q) return;
      addResults.Children.Clear();
      var items = Arr(found?["items"]);
      if (items.Count == 0) { addResults.Children.Add(T("No matching characteristics.", "Blastlog.Muted")); return; }
      foreach (var item in items) {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left, LastChildFill = true };
        var choose = Kit.Button("Choose", () => { addPicked = (S(item, "id"), S(item, "label")); ShowPicked(); }, "Blastlog.Ghost"); choose.IsEnabled = !busy;
        AutomationProperties.SetName(choose, "Choose " + S(item, "label"));
        DockPanel.SetDock(choose, Dock.Right); row.Children.Add(choose);
        row.Children.Add(T(S(item, "label")).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        addResults.Children.Add(row);
      }
    }
    void ShowPicked() {
      addResults.Children.Clear();
      if (addPicked == null) return;
      var picked = addPicked.Value;
      addResults.Children.Add(Runs("Blastlog.Body", (picked.label, true, null), (": how much does it matter to you?", false, null)));
      addResults.Children.Add(Box(StanceScale("add:" + picked.id, picked.label, null, null, null, st => {
        if (st == null) return;
        addPicked = null; addSearch = "";
        OnStance(picked.id, st, picked.label);
      }), null, 0, new Thickness(0)).Also(b => b.MaxWidth = 520).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
    }
    Debounce(input, 300, text => Run(() => Search(text), "taste characteristic search"));
    if (addPicked != null) ShowPicked(); else if (addSearch.Trim().Length >= 2) Run(() => Search(addSearch), "taste characteristic search");
    return s;
  }, addOpen, open => addOpen = open);
}
}
