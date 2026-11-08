using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>Compare (design.md 8 "Compare"; PLAYNITE_DESIGN 4.6).</summary>
public sealed class CompareScreen : IBlastlogScreen {
  public string Title => "Compare";
  public string Icon => Icons.Compare;
  public int Order => 60;
  public FrameworkElement Create(BlastlogPlugin plugin) => new CompareView(plugin);
}

/// <summary>
/// Compare, natively: your matches, side by side for any two or more of you, the taste map, how you play, character
/// sheets, where you split and group favourites, plus Look up a game. Reads only (the site's mirror routes).
/// </summary>
sealed class CompareView : Grid {
  static readonly string[] Abilities = { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
  // The site's ABILITY_DEFINITIONS (packages/domain compare.ts): names from the SRD 5.2 (CC BY 4.0), credited below.
  static readonly Dictionary<string, (string label, string definition)> AbilityText = new Dictionary<string, (string, string)> {
    ["STR"] = ("Strength", "Athletics and raw power: solving things up close and loud — brawls, big swings, boss fights, smashing the scenery."),
    ["DEX"] = ("Dexterity", "Agility, timing and precision: sneaking, platforming, dodging, aiming (bows and guns use Dex) and driving."),
    ["CON"] = ("Constitution", "Stamina and staying power: punishing difficulty, survival and attrition, roguelite runs, 80-hour adventures — and finishing what you start."),
    ["INT"] = ("Intelligence", "Reasoning and study: puzzles, investigation, tactics, builds and economies, and magic you learn rather than are born with."),
    ["WIS"] = ("Wisdom", "Perception and insight: exploring and noticing, living off the land, feeling a story and a place — and reflecting on it in reviews."),
    ["CHA"] = ("Charisma", "Force of personality: playing with others, leading a party, talking your way through, style and self-expression — and rating generously.") };
  static readonly (string key, string left, string right)[] Axes = { ("sleuthSlayer", "Sleuth", "Slayer"), ("scriptSandbox", "Script", "Sandbox"),
    ("hoarderHero", "Hoarder", "Hero"), ("harshGenerous", "Harsh critic", "Generous"), ("samplerFinisher", "Sampler", "Finisher") };
  static readonly (string key, string label)[] Tabs = { ("ratings", "Ratings"), ("tastes", "Tastes"), ("shelves", "Shelves"), ("together", "Play together") };
  static readonly Dictionary<string, string> LookNames = new Dictionary<string, string> { ["classic"] = "Classic", ["ledger"] = "Ledger", ["arcade"] = "Arcade", ["blueprint"] = "Blueprint" };

  readonly BlastlogPlugin plugin;
  readonly StackPanel page = new StackPanel { MaxWidth = 1320 };
  JObject insights, groups, comparison, favourites, detail; string meId; bool loaded;
  List<ComparePerson> people = new List<ComparePerson>();
  string focus; string[] pair; List<string> group; string tab = "ratings", xKey = "sleuthSlayer", yKey = "scriptSandbox", openStat; bool allStrips;
  string lookupText = "";
  FrameworkElement sideBySide;
  StackPanel lookupResults;

  public CompareView(BlastlogPlugin plugin) {
    this.plugin = plugin;
    Children.Add(page);
    Loaded += (s, e) => { if (!loaded) { loaded = true; Run(Load, "compare load"); } };
  }

  async Task Load() {
    if (!plugin.Configured) { Render(); return; }
    await Read(false); Render();
    await Read(true); Render();
  }
  async Task Read(bool fresh) {
    meId = meId ?? S((await plugin.Mirror("me", false)).Value?["user"], "id");
    groups = (await plugin.Mirror("friend-groups", fresh)).Value ?? groups;
    var read = (await plugin.Mirror("compare/insights" + (focus != null ? "?group=" + Uri.EscapeDataString(focus) : ""), fresh)).Value;
    if (read == null && focus != null) { focus = null; read = (await plugin.Mirror("compare/insights", fresh)).Value; } // a group deleted elsewhere: back to everyone
    insights = read ?? insights;
    comparison = (await plugin.Mirror("compare?limit=100&offset=0", fresh)).Value ?? comparison;
    favourites = (await plugin.Mirror("compare/masterpieces?limit=50&offset=0", fresh)).Value ?? favourites;
    people = Arr(insights?["players"]).Select(p => new ComparePerson { Id = S(p, "id"), Name = S(p, "name") ?? "?", Colour = PersonColour(S(p, "color") ?? "#a6a6b1"), J = p }).ToList();
    // Start on you and your best match, in the matches chapter and side by side.
    var me = Me();
    if (pair == null && me != null) {
      var best = Pairs().Where(p => S(p, "a") == me.Id || S(p, "b") == me.Id).OrderByDescending(p => D(p, "score") ?? 0).FirstOrDefault();
      if (best != null) { pair = new[] { me.Id, S(best, "a") == me.Id ? S(best, "b") : S(best, "a") }; group = pair.ToList(); }
    }
    if (group != null) await ReadGroup(fresh);
  }
  async Task ReadGroup(bool fresh) {
    if (group == null || group.Count < 2) { detail = null; return; }
    detail = (await plugin.Mirror("compare/group?ids=" + string.Join(",", group), fresh)).Value ?? detail;
  }
  ComparePerson Me() => people.FirstOrDefault(p => p.Id == meId) ?? people.FirstOrDefault();
  List<JToken> Pairs() => Arr(insights?["pairs"]).ToList();
  ComparePerson Person(string id) => people.FirstOrDefault(p => p.Id == id);
  string Name(string id) => Person(id)?.Name ?? "?";
  Brush BrushOf(string id) => Person(id)?.Brush ?? Brush("Blastlog.Text");

  void ShowGroup(string id) { focus = id; pair = null; group = null; detail = null; Run(async () => { await Read(false); Render(); }, "compare group"); }
  void ChooseGroup(List<string> ids) {
    group = ids; if (ids.Count == 2) pair = new[] { ids[0], ids[1] };
    Run(async () => { Render(); await ReadGroup(false); Render(); }, "compare side by side");
  }
  void Lookup(JToken game) => Showdown(S(game, "gameId"), S(game, "title"), S(game, "coverUrl"));

  // ---- The page ----
  void Render() {
    page.Children.Clear();
    if (!plugin.Configured) { page.Children.Add(Hero("How you stack up", null)); page.Children.Add(Kit.Card(T("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to compare with your friends here."))); return; }
    if (insights == null) { page.Children.Add(Hero("How you all stack up", null)); page.Children.Add(T(loaded ? "Comparing everyone…" : "", "Blastlog.Muted")); return; }
    if (people.Count < 2) {
      page.Children.Add(Hero("How you stack up", null, "Compare puts your taste next to your friends’: who you match best, where you split, and games you’d both enjoy. It fills in once you have a friend on Blastlog."));
      page.Children.Add(Kit.Button("Add a friend ↗", () => Nav.Friends(plugin)).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
      return;
    }
    page.Children.Add(Hero("How you all stack up", GameLookup()));
    var myGroups = Arr(groups?["groups"]).Where(g => Arr(g["memberIds"]).Count > 0).ToList();
    if (myGroups.Count > 0) {
      var row = Wrap(Toggle("Everyone", focus == null, () => ShowGroup(null)));
      foreach (var g in myGroups) { var id = S(g, "id"); row.Children.Add(Toggle(S(g, "name"), focus == id, () => ShowGroup(id))); }
      row.Children.Add(Link("Edit groups ↗", () => Nav.Friends(plugin)));
      row.Margin = new Thickness(0, 18, 0, 0);
      page.Children.Add(row);
    }
    page.Children.Add(Chapter("01", "Your matches", "How alike your tastes are, and how closely you rate the games you’ve both played.", Matches()));
    sideBySide = Chapter("02", "Side by side", "Pick two or more of you: tap a name to add or remove them.", group != null ? SideBySide(focus == null ? myGroups : new List<JToken>()) : new StackPanel());
    page.Children.Add(sideBySide);
    page.Children.Add(Chapter("03", "Taste map", "Where everyone sits between two playstyles. Change the axes to see a different side.", TasteMap()));
    page.Children.Add(Chapter("04", "How you play", "Rating style, finishing, and the pile still waiting.", HowYouPlay()));
    page.Children.Add(Chapter("05", "Character sheets", "Your games as a tabletop character. Tap a stat to see what it’s made of.", CharacterSheets()));
    var splits = Arr(insights["splits"]);
    if (splits.Count > 0) page.Children.Add(Chapter("06", "Where you split", "Games three or more of you rated that ended up furthest apart. Tap one for everyone’s take.", Splits(splits)));
    page.Children.Add(Chapter(splits.Count > 0 ? "07" : "06", "Group favourites", "Loved by two or more of you. Tap one for everyone’s take.", Favourites()));
    page.Children.Add(T(plugin.CacheLine(), "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 30, 0, 0)));
  }

  FrameworkElement Hero(string title, FrameworkElement side, string lede = "Who you match with, where you clash, and what to borrow from each other’s shelves.") {
    var text = Stack(null, T("Compare", "Blastlog.Small", "Blastlog.GameText", 0, FontWeights.Bold), T(title, "Blastlog.PageTitle").Also(t => t.Margin = new Thickness(0, 2, 0, 0)),
      T(lede, "Blastlog.Muted", null, 17).Also(t => { t.Margin = new Thickness(0, 8, 0, 0); t.MaxWidth = 640; t.HorizontalAlignment = HorizontalAlignment.Left; }));
    if (side == null) return text;
    var dock = new DockPanel { LastChildFill = true };
    side.Margin = new Thickness(24, 0, 0, 0); DockPanel.SetDock(side, Dock.Right); dock.Children.Add(side);
    var refresh = Link("Refresh", () => Run(async () => { await Read(true); Render(); }, "compare refresh")); refresh.HorizontalAlignment = HorizontalAlignment.Left; refresh.Margin = new Thickness(-6, 8, 0, 0);
    text.Children.Add(refresh);
    dock.Children.Add(text);
    return dock;
  }

  FrameworkElement GameLookup() {
    var (box, input) = SearchField("Look up a game…", 340);
    input.Text = lookupText;
    lookupResults = new StackPanel { Width = 340, Margin = new Thickness(0, 8, 0, 0) };
    async Task Search(string text) {
      lookupText = text; var q = text.Trim();
      lookupResults.Children.Clear();
      if (q.Length < 2) return;
      var found = (await plugin.Mirror("compare/games?limit=25&offset=0&search=" + Uri.EscapeDataString(q), false)).Value;
      if (lookupText.Trim() != q) return;
      lookupResults.Children.Clear();
      var items = Arr(found?["items"]).Take(8).ToList();
      if (items.Count == 0) { lookupResults.Children.Add(T("No game by that name in anyone’s library.", "Blastlog.Muted")); return; }
      foreach (var g in items) {
        var row = Row(Cover(plugin, S(g, "coverUrl"), S(g, "title"), 36, 6), Stack(new Thickness(10, 0, 0, 0), T(S(g, "title"), "Blastlog.Body", null, 15, FontWeights.SemiBold), T($"{Arr(g["entries"]).Count} of you", "Blastlog.Small")));
        var game = g;
        var b = Plain(row, () => { lookupText = ""; input.Text = ""; lookupResults.Children.Clear(); Lookup(game); }, S(g, "title"));
        b.Margin = new Thickness(0, 0, 0, 6);
        lookupResults.Children.Add(b);
      }
    }
    Debounce(input, 300, text => Run(() => Search(text), "compare lookup"));
    return Stack(null, box, lookupResults);
  }

  // 01: a constellation of everyone beside one selected pair, explained.
  FrameworkElement Matches() {
    var scored = Pairs().Where(p => D(p, "score") != null).OrderByDescending(p => D(p, "score")).ToList();
    bool IsSelected(JToken p) => pair != null && ((S(p, "a") == pair[0] && S(p, "b") == pair[1]) || (S(p, "a") == pair[1] && S(p, "b") == pair[0]));
    var focusPair = scored.FirstOrDefault(IsSelected) ?? scored.FirstOrDefault();
    var web = Stack(null, CompareCharts.Constellation(people, scored, IsSelected, focusPair, p => { pair = new[] { S(p, "a"), S(p, "b") }; Render(); }),
      T("Thicker line = closer match", "Blastlog.Small").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
    web.Margin = new Thickness(0, 0, 28, 0); web.VerticalAlignment = VerticalAlignment.Top;
    var wrap = new WrapPanel();
    wrap.Children.Add(web);
    if (focusPair == null) { wrap.Children.Add(T("Not enough shared ratings or traits yet to score a match.", "Blastlog.Muted")); return wrap; }
    var a = S(focusPair, "a"); var b = S(focusPair, "b"); var score = D(focusPair, "score") ?? 0;
    var f = new StackPanel { MaxWidth = 560, MinWidth = 340 };
    var names = new TextBlock { Style = Kit.Style("Blastlog.Body"), FontSize = 24, FontWeight = FontWeights.ExtraBold };
    names.Inlines.Add(new Run(Name(a)) { Foreground = BrushOf(a) }); names.Inlines.Add(new Run(" and ") { FontWeight = FontWeights.Normal }); names.Inlines.Add(new Run(Name(b)) { Foreground = BrushOf(b) });
    f.Children.Add(names);
    var rank = focusPair == scored[0] ? "the best pair in the group" : focusPair == scored[scored.Count - 1] && scored.Count > 2 ? "the most different pair" : $"{scored.IndexOf(focusPair) + 1} of {scored.Count} pairs";
    f.Children.Add(Runs("Blastlog.Body", ($"{score:0}% match", true, null), ($" · {CompareCharts.MatchWord(score)}", true, "Blastlog.GameText"), ("  " + rank, false, "Blastlog.TextMuted")).Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
    f.Children.Add(CompareCharts.MatchScale(score));
    var taste = D(focusPair, "taste"); var agreement = D(focusPair, "agreement"); var gap = D(focusPair, "meanGap"); var sharedTraits = I(focusPair, "sharedTraits");
    UIElement Part(string label, string big, string small) => Box(Stack(null, T(label, "Blastlog.Small", null, 0, FontWeights.Bold), T(big, "Blastlog.Body", null, 22, FontWeights.ExtraBold), T(small, "Blastlog.Small")),
      "Blastlog.Surface2", 16, new Thickness(16));
    f.Children.Add(Columns(200, new[] {
      Part("Tastes", taste == null ? "Not enough yet" : $"{taste:0}%", (sharedTraits > 0 ? $"From {sharedTraits} traits you both lean on. " : "") + "Leaning the same way on all of them = 100%."),
      Part("Ratings", agreement == null ? "Not enough yet" : gap != null ? $"{Stars(gap.Value)} apart" : $"{agreement:0}%", (agreement != null ? $"{agreement:0}%. " : "") + "Same stars every time = 100%. 3★ or more apart = 0%.") }, 12).Also(g => g.Margin = new Thickness(0, 10, 0, 0)));
    f.Children.Add(T("Match = tastes counted 60%, ratings 40%. Needs 5 shared traits and 3 shared games; otherwise one part is used alone.", "Blastlog.Small"));
    var facts = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
    var loves = Arr(focusPair["loves"]).Select(x => (string)x).ToList(); var splits = Arr(focusPair["splits"]); var sharedRated = I(focusPair, "sharedRated");
    UIElement Fact(string label, string text) => Runs("Blastlog.Body", (label + "  ", true, "Blastlog.TextMuted"), (text, false, null)).Also(t => t.Margin = new Thickness(0, 0, 0, 6));
    if (loves.Count > 0) facts.Children.Add(Fact("Both love", string.Join(", ", loves)));
    if (splits.Count > 0) facts.Children.Add(Fact("Split on", string.Join(", ", splits.Select(s => $"{S(s, "label")} ({Name(S(s, "favours"))})"))));
    if (sharedRated > 0) facts.Children.Add(Fact("Rated together", $"{sharedRated} games{(gap != null ? $", usually {Stars(gap.Value)} apart" : "")}"));
    f.Children.Add(facts);
    f.Children.Add(Kit.Button("See them side by side", () => { ChooseGroup(new List<string> { a, b }); Dispatcher.BeginInvoke(new Action(() => sideBySide?.BringIntoView(new Rect(0, 0, 1, 600)))); })
      .Also(x => { x.HorizontalAlignment = HorizontalAlignment.Left; x.Margin = new Thickness(0, 6, 0, 14); }));
    var pills = new WrapPanel();
    foreach (var p in scored) {
      var captured = p;
      pills.Children.Add(Toggle($"{Name(S(p, "a"))} & {Name(S(p, "b"))}  {D(p, "score"):0}%", IsSelected(p) || (pair == null && p == focusPair), () => { pair = new[] { S(captured, "a"), S(captured, "b") }; Render(); }));
    }
    AutomationProperties.SetName(pills, "All pairs");
    f.Children.Add(pills);
    wrap.Children.Add(f);
    return wrap;
  }

  // 02: two or more players, one view at a time.
  FrameworkElement SideBySide(List<JToken> myGroups) {
    var panel = new StackPanel();
    var who = new WrapPanel();
    var inGroup = Pairs().Where(p => group.Contains(S(p, "a")) && group.Contains(S(p, "b"))).ToList();
    var scored = inGroup.Where(p => D(p, "score") != null).ToList();
    int? score = scored.Count > 0 ? (int)Math.Round(scored.Average(p => D(p, "score").Value)) : (int?)null;
    foreach (var p in people) {
      var on = group.Contains(p.Id);
      var content = Row(CompareCharts.Avatar(p, 24), T(p.Name, "Blastlog.Body", null, 15, FontWeights.Bold).Also(t => { t.Margin = new Thickness(8, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center; }));
      var chip = Box(content, on ? null : "Blastlog.Surface2", 999, new Thickness(6, 6, 14, 6), on ? null : "Blastlog.Line");
      if (on) { chip.BorderThickness = new Thickness(2); chip.BorderBrush = p.Brush; chip.Background = Hex(p.Colour, .16); }
      chip.MinHeight = 44;
      var id = p.Id;
      var b = Plain(chip, () => { if (group.Contains(id)) { if (group.Count > 2) ChooseGroup(group.Where(x => x != id).ToList()); } else ChooseGroup(group.Concat(new[] { id }).ToList()); },
        p.Name + (on ? ", selected" : ""));
      b.Margin = new Thickness(0, 0, 8, 8);
      if (on && group.Count <= 2) b.ToolTip = "Side by side needs two: add someone else first";
      who.Children.Add(b);
    }
    if (people.Count > 2 && group.Count < people.Count) who.Children.Add(Toggle("Everyone", false, () => ChooseGroup(people.Select(p => p.Id).ToList())));
    foreach (var g in myGroups) {
      var members = people.Where(p => p.Id == meId || Arr(g["memberIds"]).Any(m => (string)m == p.Id)).Select(p => p.Id).ToList();
      if (members.Count >= 2) who.Children.Add(Toggle(S(g, "name"), false, () => ChooseGroup(members)));
    }
    if (score != null) who.Children.Add(T(group.Count > 2 ? $"Group match {score}%" : $"{score}% match", "Blastlog.Body", "Blastlog.GameText", 18, FontWeights.ExtraBold)
      .Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(8, 0, 0, 8); if (group.Count > 2) t.ToolTip = "The average of every pair’s match in this group"; }));
    AutomationProperties.SetName(who, "Who to compare");
    panel.Children.Add(who);
    var tabs = Wrap(); tabs.Margin = new Thickness(0, 10, 0, 6);
    foreach (var t in Tabs) { var key = t.key; tabs.Children.Add(Toggle(t.label, tab == key, () => { tab = key; Render(); })); }
    panel.Children.Add(tabs);
    var body = new StackPanel();
    var detailIds = Arr(detail?["ids"]).Select(x => (string)x).ToList();
    var shown = detailIds.Select(Person).Where(p => p != null).ToList();
    if (detail == null) body.Children.Add(T("Lining up your shelves…", "Blastlog.Muted"));
    else if (tab == "ratings") body.Children.Add(RatingsTab(shown, inGroup.Where(p => detailIds.Contains(S(p, "a")) && detailIds.Contains(S(p, "b"))).ToList()));
    else if (tab == "tastes") body.Children.Add(Columns(380, new UIElement[] {
        Kit.Card(CompareCharts.Dumbbells(Arr(detail["tug"]?["splits"]), shown, "Where you split", "No big splits — your tastes line up.")),
        Kit.Card(CompareCharts.Dumbbells(Arr(detail["tug"]?["shared"]), shown, shown.Count > 2 ? "What you all love" : "What you both love", "No shared loves yet.")) }));
    else if (tab == "shelves") {
      body.Children.Add(Columns(380, Arr(detail["steal"]).Select(s => (UIElement)Kit.Card(Steal(s, shown)))));
      var both = Arr(detail["bothWaiting"]);
      if (both.Count > 0) body.Children.Add(Kit.Card(Stack(null, T(shown.Count == 2 ? "On both your lists" : "On two or more of your lists", "Blastlog.CardTitle"),
        T($"Single-player games {(shown.Count == 2 ? "you both have" : "more than one of you has")} and haven’t played, best fit first.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 4, 0, 12)),
        Waiting(both, shown))));
    } else {
      var together = Arr(detail["together"]); var two = shown.Count == 2;
      if (together.Count == 0) body.Children.Add(T(two ? "No co-op or multiplayer game is waiting on both your lists yet." : "No co-op or multiplayer game is waiting on two or more of your lists yet.", "Blastlog.Muted"));
      else body.Children.Add(Kit.Card(Stack(null, T(two ? "Co-op and multiplayer games you both have and haven’t played, best fit for both of you first. Online modes that have shut down don’t count."
        : "Co-op and multiplayer games waiting on two or more of your lists, the most of you first, then the best fit for everyone who has it. Online modes that have shut down don’t count.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 0, 0, 12)),
        Waiting(together, shown))));
      body.Children.Add(Row(Link("Find a co-op game ↗", () => Nav.Together(plugin, detailIds)),
        T($"for {(shown.Count > 2 ? "all of you" : "you two")}, including ones nobody has yet.", "Blastlog.Muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center)));
    }
    panel.Children.Add(body);
    return panel;
  }

  FrameworkElement RatingsTab(List<ComparePerson> shownPeople, List<JToken> pairs) {
    var shared = Arr(detail["shared"]).ToList(); var agreements = Arr(detail["agreements"]).ToList();
    var grid = new WrapPanel();
    if (shownPeople.Count == 2) {
      var a = shownPeople[0]; var b = shownPeople[1];
      if (shared.Count == 0) return T($"{a.Name} and {b.Name} haven’t rated any of the same games yet.", "Blastlog.Muted");
      (JToken game, double a, double b) Duel(JToken g) => (g, (double?)g["ratings"]?[a.Id] ?? 0, (double?)g["ratings"]?[b.Id] ?? 0);
      var left = Stack(new Thickness(0, 0, 32, 0), T($"{shared.Count} games you’ve both rated. Dots on the line, you agree; above it {b.Name} liked it more, below it {a.Name} did.", "Blastlog.Muted")
        .Also(t => { t.MaxWidth = 520; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 0, 0, 10); }),
        CompareCharts.Scatter(shared.Select(Duel).ToList(), a, b, Lookup));
      var side = new StackPanel { Width = 340, Margin = new Thickness(0, 8, 0, 24) };
      side.Children.Add(T("Biggest disagreements", "Blastlog.CardTitle").Also(t => t.Margin = new Thickness(0, 0, 0, 10)));
      var dis = Arr(detail["disagreements"]).Take(4).Select(Duel).ToList();
      if (dis.Count == 0) side.Children.Add(T("None — you rate the games you share about the same.", "Blastlog.Muted"));
      foreach (var d in dis) {
        var duel = new TextBlock { Style = Kit.Style("Blastlog.Body"), FontWeight = FontWeights.Bold };
        duel.Inlines.Add(new Run(Stars(d.a)) { Foreground = a.Brush }); duel.Inlines.Add(new Run("  vs  ") { FontWeight = FontWeights.Normal, Foreground = Brush("Blastlog.TextMuted") }); duel.Inlines.Add(new Run(Stars(d.b)) { Foreground = b.Brush });
        var row = Row(Cover(plugin, S(d.game, "coverUrl"), S(d.game, "title"), 44, 6), Stack(new Thickness(12, 0, 0, 0), T(S(d.game, "title"), "Blastlog.Body", null, 15, FontWeights.SemiBold), duel));
        var game = d.game; side.Children.Add(Plain(row, () => Lookup(game), S(d.game, "title")).Also(x => x.Margin = new Thickness(0, 0, 0, 10)));
      }
      if (agreements.Count > 0) { side.Children.Add(T("You both loved", "Blastlog.CardTitle").Also(t => t.Margin = new Thickness(0, 12, 0, 10))); side.Children.Add(MiniCovers(agreements.Take(5), g => $"{S(g, "title")}: {a.Name} {Stars(Duel(g).a)}, {b.Name} {Stars(Duel(g).b)}")); }
      grid.Children.Add(left); grid.Children.Add(side);
      return grid;
    }
    if (shared.Count == 0) return T("No two of you have rated the same game yet.", "Blastlog.Muted");
    IEnumerable<double> Values(JToken g) => ((g["ratings"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()).Select(p => (double)p.Value);
    double Spread(JToken g) { var v = Values(g).ToList(); return v.Count == 0 ? 0 : v.Max() - v.Min(); }
    var games = shared.OrderByDescending(Spread).ThenByDescending(g => Values(g).Count()).ThenBy(g => S(g, "title")).ToList();
    var gaps = pairs.Where(p => D(p, "meanGap") != null && I(p, "sharedRated") > 0).OrderBy(p => D(p, "meanGap")).ToList();
    var main = Stack(new Thickness(0, 0, 32, 0), T($"{games.Count} games two or more of you rated, furthest apart first. Each dot is one person’s stars; tap a game for everyone’s take.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 0, 0, 10)),
      CompareCharts.Strips(plugin, games, shownPeople, allStrips, () => { allStrips = !allStrips; Render(); }, Lookup));
    main.Width = 600;
    var right = new StackPanel { Width = 340, Margin = new Thickness(0, 8, 0, 24) };
    right.Children.Add(T("How closely each pair rates", "Blastlog.CardTitle").Also(t => t.Margin = new Thickness(0, 0, 0, 10)));
    if (gaps.Count == 0) right.Children.Add(T("No pair has rated the same game yet.", "Blastlog.Muted"));
    foreach (var p in gaps) {
      var line = new TextBlock { Style = Kit.Style("Blastlog.Body"), FontWeight = FontWeights.Bold };
      line.Inlines.Add(new Run(Name(S(p, "a"))) { Foreground = BrushOf(S(p, "a")) }); line.Inlines.Add(new Run(" & ") { FontWeight = FontWeights.Normal }); line.Inlines.Add(new Run(Name(S(p, "b"))) { Foreground = BrushOf(S(p, "b")) });
      right.Children.Add(Stack(new Thickness(0, 0, 0, 10), line, T($"{Stars(D(p, "meanGap").Value)} apart on average, {Plural(I(p, "sharedRated"), "game")}", "Blastlog.Small")));
    }
    if (agreements.Count > 0) {
      string Label(JToken g) => S(g, "title") + ": " + string.Join(", ", shownPeople.Where(x => g["ratings"]?[x.Id] != null).Select(x => $"{x.Name} {Stars((double)g["ratings"][x.Id])}"));
      right.Children.Add(T("Loved by everyone who rated it", "Blastlog.CardTitle").Also(t => t.Margin = new Thickness(0, 12, 0, 10)));
      right.Children.Add(MiniCovers(agreements.Take(6), Label));
    }
    grid.Children.Add(main); grid.Children.Add(right);
    return grid;
  }
  FrameworkElement MiniCovers(IEnumerable<JToken> games, Func<JToken, string> title) {
    var w = new WrapPanel();
    foreach (var g in games) { var game = g; var b = Plain(Cover(plugin, S(g, "coverUrl"), S(g, "title"), 56, 8), () => Lookup(game), title(g)); b.ToolTip = title(g); b.Margin = new Thickness(0, 0, 8, 8); w.Children.Add(b); }
    return w;
  }

  FrameworkElement Steal(JToken s, List<ComparePerson> shownPeople) {
    var taker = Person(S(s, "taker")); var others = shownPeople.Where(p => p.Id != S(s, "taker")).ToList(); var one = others.Count == 1 ? others[0] : null;
    var panel = new StackPanel();
    var title = new TextBlock { Style = Kit.Style("Blastlog.CardTitle") };
    title.Inlines.Add(new Run(taker?.Name ?? "?") { Foreground = taker?.Brush ?? Brush("Blastlog.Text") }); title.Inlines.Add(new Run(", from " + (one != null ? $"{one.Name}’s shelf" : "the others’ shelves")));
    panel.Children.Add(title);
    panel.Children.Add(T($"{(one != null ? $"{one.Name}’s favourites" : "The others’ favourites")} {taker?.Name} doesn’t have, with {taker?.Name}’s predicted rating.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 4, 0, 12)));
    var games = Arr(s["games"]).Take(5).ToList();
    if (games.Count == 0) panel.Children.Add(T($"Nothing left — {taker?.Name} already has all of {(one != null ? $"{one.Name}’s" : "the others’")} favourites.", "Blastlog.Muted"));
    foreach (var g in games) {
      var text = Stack(new Thickness(12, 0, 0, 0), T(S(g, "title"), "Blastlog.Body", null, 15, FontWeights.SemiBold));
      if (one == null) {
        var givers = new TextBlock { Style = Kit.Style("Blastlog.Small"), FontWeight = FontWeights.Bold };
        foreach (var v in Arr(g["givers"])) givers.Inlines.Add(new Run($"{Name(S(v, "userId"))} {Stars(D(v, "rating") ?? 0)}  ") { Foreground = BrushOf(S(v, "userId")) });
        text.Children.Add(givers);
      }
      var reasons = Arr(g["reasons"]).Select(x => (string)x).ToList();
      if (reasons.Count > 0) text.Children.Add(T(string.Join(", ", reasons), "Blastlog.Small"));
      var row = new DockPanel { LastChildFill = true };
      var predicted = D(g, "predicted");
      if (predicted != null) {
        var p = T("~" + Stars(predicted.Value), "Blastlog.Body", null, 16, FontWeights.ExtraBold); p.Foreground = taker?.Brush ?? Brush("Blastlog.Text"); p.VerticalAlignment = VerticalAlignment.Center; p.Margin = new Thickness(10, 0, 0, 0);
        p.ToolTip = string.Join(", ", Arr(g["givers"]).Select(v => $"{Name(S(v, "userId"))} gave it {Stars(D(v, "rating") ?? 0)}"));
        DockPanel.SetDock(p, Dock.Right); row.Children.Add(p);
      }
      row.Children.Add(Row(Cover(plugin, S(g, "coverUrl"), S(g, "title"), 44, 6), text));
      var game = g;
      var b = Plain(row, () => Lookup(game), S(g, "title")); b.HorizontalContentAlignment = HorizontalAlignment.Stretch; b.Margin = new Thickness(0, 0, 0, 10);
      panel.Children.Add(b);
    }
    return panel;
  }

  FrameworkElement Waiting(JArray games, List<ComparePerson> shownPeople) => Columns(300, games.Select(g => {
    var owners = Arr(g["owners"]).Select(x => (string)x).ToList();
    var missing = shownPeople.Where(p => !owners.Contains(p.Id)).Select(p => p.Name + "’s").ToList();
    var text = Stack(new Thickness(12, 0, 0, 0), T(S(g, "title"), "Blastlog.Body", null, 15, FontWeights.Bold));
    var modes = Arr(g["modes"]).Select(x => (string)x).ToList();
    if (modes.Count > 0) text.Children.Add(Wrap(modes.Select(m => (UIElement)Kit.Chip(m, neutral: true)).ToArray()).Also(w => w.Margin = new Thickness(0, 4, 0, 0)));
    var predicted = new TextBlock { Style = Kit.Style("Blastlog.Small"), FontWeight = FontWeights.Bold };
    foreach (var p in shownPeople.Where(p => owners.Contains(p.Id))) predicted.Inlines.Add(new Run($"{p.Name} ~{Stars(D(g["predicted"], p.Id) ?? 0)}  ") { Foreground = p.Brush });
    text.Children.Add(predicted);
    if (missing.Count > 0) text.Children.Add(T("Not on " + (missing.Count < 2 ? missing[0] : string.Join(", ", missing.Take(missing.Count - 1)) + " or " + missing[missing.Count - 1]) + " list", "Blastlog.Small"));
    var game = g;
    return (UIElement)Plain(Row(Cover(plugin, S(g, "coverUrl"), S(g, "title"), 56, 8), text), () => Lookup(game), S(g, "title"));
  }), 8);

  // 03
  FrameworkElement TasteMap() {
    var pickers = new StackPanel { Width = 320, Margin = new Thickness(28, 0, 0, 0) };
    ComboBox Picker(string current, string other, Action<string> set) {
      var box = new ComboBox { MinHeight = 44, FontSize = 15, Margin = new Thickness(0, 4, 0, 14) };
      foreach (var a in Axes) box.Items.Add(new ComboBoxItem { Content = $"{a.left} ↔ {a.right}", Tag = a.key, IsEnabled = a.key != other, IsSelected = a.key == current });
      box.SelectionChanged += (s, e) => { try { if (box.SelectedItem is ComboBoxItem i && (string)i.Tag != current) { set((string)i.Tag); Render(); } } catch (Exception) { } };
      return box;
    }
    pickers.Children.Add(T("Left ↔ right", "Blastlog.Small", null, 0, FontWeights.Bold));
    pickers.Children.Add(Picker(xKey, yKey, k => xKey = k).Also(b => AutomationProperties.SetName(b, "Left to right")));
    pickers.Children.Add(T("Bottom ↔ top", "Blastlog.Small", null, 0, FontWeights.Bold));
    pickers.Children.Add(Picker(yKey, xKey, k => yKey = k).Also(b => AutomationProperties.SetName(b, "Bottom to top")));
    pickers.Children.Add(T("Sleuths go for mysteries and puzzles, Slayers for fights. Script fans like a told story, Sandbox fans their own. Hoarders collect and craft, Heroes chase the plot.", "Blastlog.Muted"));
    return Wrap(CompareCharts.TasteMap(people, xKey, yKey, Axes), pickers);
  }

  // 04: rating style and habits, one card each, one shared scale.
  FrameworkElement HowYouPlay() {
    double H(ComparePerson p, string key) => D(p.J["habits"], key) ?? 0;
    string Top(string key, bool low = false) => (low ? people.OrderBy(p => H(p, key)) : people.OrderByDescending(p => H(p, key))).First().Id;
    var titles = new Dictionary<string, string>();
    foreach (var (id, label) in new[] { (Top("avgRating", true), "Toughest critic"), (Top("avgRating"), "Most generous"), (Top("done"), "Most finished"),
      (Top("backlog"), "Biggest backlog"), (Top("completionRate"), "Sees it through"), (Top("reviewRate"), "The reviewer") }) if (!titles.ContainsKey(id)) titles[id] = label;
    int[] Histogram(ComparePerson p) => Arr(p.J["habits"]?["histogram"]).Select(x => (int)Math.Round((double)x)).Concat(Enumerable.Repeat(0, 10)).Take(10).ToArray();
    var halves = people.Any(p => Histogram(p).Where((n, i) => i % 2 == 0 && n > 0).Any());
    int[] Bars(int[] h) => halves ? h : Enumerable.Range(0, 5).Select(i => h[i * 2 + 1] + (i > 0 ? h[i * 2] : h[0])).ToArray();
    var peak = Math.Max(1, people.SelectMany(p => Bars(Histogram(p))).DefaultIfEmpty(0).Max());
    var hours = Arr(comparison?["items"]).ToDictionary(i => S(i["user"], "id") ?? "", i => D(i, "completedAllStatusHours") ?? 0);
    return Columns(260, people.Select(p => {
      var s = new StackPanel();
      var head = Row(CompareCharts.Avatar(p, 32), Stack(new Thickness(10, 0, 0, 0), T(p.Name, "Blastlog.Body", null, 0, FontWeights.Bold), titles.TryGetValue(p.Id, out var t) ? T(t, "Blastlog.Small") : null));
      s.Children.Add(head);
      s.Children.Add(CompareCharts.Histogram(p, Bars(Histogram(p)), peak, halves));
      UIElement Stat(string label, string value) => Stack(new Thickness(0, 0, 14, 8), T(label, "Blastlog.Small"), T(value, "Blastlog.Body", null, 18, FontWeights.ExtraBold));
      var stats = Wrap(Stat("Average", Stars(H(p, "avgRating"))), Stat("Finished", H(p, "done").ToString("0")), Stat("Finish rate", H(p, "completionRate").ToString("0") + "%"), Stat("Backlog", H(p, "backlog").ToString("0")));
      if (hours.TryGetValue(p.Id, out var h) && h > 0) {
        var stat = Stat("Game time finished", Math.Round(h).ToString("N0") + "h");
        ((FrameworkElement)stat).ToolTip = "How long the games they finished take to beat, in the length level they chose: not hours actually played";
        stats.Children.Add(stat);
      }
      s.Children.Add(stats);
      var card = Kit.Card(s); card.Margin = new Thickness(0);
      return (UIElement)card;
    }), 14);
  }

  // 05: ability scores and the best-fitting class, in the owner's chosen look.
  FrameworkElement CharacterSheets() {
    var sheets = people.Where(p => p.J["sheet"] is JObject).Select(p => {
      var sheet = (JObject)p.J["sheet"]; var classes = Arr(sheet["classes"]).ToList(); var best = classes.FirstOrDefault(); var look = S(sheet, "style") ?? "classic";
      string ClassName(JToken k) => S(k, "name") + (S(k, "build") != null ? " · " + S(k, "build") : "");
      var s = new StackPanel();
      var head = new DockPanel { LastChildFill = true };
      if (best != null) { var m = T($"{I(best, "match")}%", "Blastlog.Body", null, 18, FontWeights.ExtraBold); m.Foreground = p.Brush; m.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(m, Dock.Right); head.Children.Add(m); }
      head.Children.Add(Row(CompareCharts.Avatar(p, 34), Stack(new Thickness(10, 0, 0, 0), T(p.Name, "Blastlog.Small"), T(best != null ? ClassName(best) : "Adventurer", "Blastlog.CardTitle"))));
      s.Children.Add(head);
      var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 14, 0, 6) };
      foreach (var key in Abilities) {
        var score = I(sheet["scores"], key); var id = p.Id + ":" + key; var open = openStat == id;
        var cell = Stack(null, T(key, "Blastlog.Small", null, 0, FontWeights.Bold), T(score.ToString(), "Blastlog.Body", null, 24, FontWeights.ExtraBold));
        var bar = new Grid { Height = 6, Margin = new Thickness(0, 4, 0, 0) };
        bar.Children.Add(Box(null, "Blastlog.Line", 3, new Thickness(0)));
        var fill = new Border { CornerRadius = new CornerRadius(look == "arcade" ? 0 : 3), Background = p.Brush, HorizontalAlignment = HorizontalAlignment.Left };
        if (look == "arcade") fill.OpacityMask = Segments();
        bar.Children.Add(fill); bar.SizeChanged += (o, e) => fill.Width = e.NewSize.Width * Math.Max(0, score - 3) / 17.0;
        cell.Children.Add(bar);
        var box = Box(cell, open ? "Blastlog.Surface" : null, look == "arcade" ? 0 : 12, new Thickness(10, 8, 10, 8), open ? "Blastlog.Edge" : null);
        var b = Plain(box, () => { openStat = open ? null : id; Render(); }, $"{AbilityText[key].label} {score}" + (open ? ", expanded" : ""));
        b.Margin = new Thickness(0, 0, 6, 6); b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        stats.Children.Add(b);
      }
      s.Children.Add(stats);
      foreach (var key in Abilities) {
        if (openStat != p.Id + ":" + key) continue;
        var contributors = Arr(sheet["contributors"]?[key]).Select(x => (string)x).ToList();
        s.Children.Add(Stack(new Thickness(0, 4, 0, 8), Runs("Blastlog.Body", (AbilityText[key].label + ". ", true, null), (AbilityText[key].definition, false, null)),
          T(contributors.Count > 0 ? $"From {p.Name}’s games: {string.Join(", ", contributors)}" : "Nothing here stands out above the group.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 4, 0, 0))));
      }
      if (classes.Count > 1) s.Children.Add(T("Also a fit: " + string.Join(", ", classes.Skip(1).Select(ClassName)), "Blastlog.Small"));
      // Your own sheet gets the look picker (the site's "Look" select); friends see the owner's choice.
      if (p.Id == meId) s.Children.Add(LookPicker(look));
      return (UIElement)SheetCard(s, p, look);
    }).ToList();
    var about = Disclosure("How the sheets work", () => {
      var credit = new TextBlock { Style = Kit.Style("Blastlog.Small"), Margin = new Thickness(0, 8, 0, 0) };
      credit.Inlines.Add(new Run("Class and ability names from the "));
      credit.Inlines.Add(Hyper("System Reference Document 5.2", "https://www.dndbeyond.com/srd"));
      credit.Inlines.Add(new Run(" by Wizards of the Coast LLC, licensed under "));
      credit.Inlines.Add(Hyper("CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/legalcode"));
      credit.Inlines.Add(new Run("."));
      return Stack(null, T("Each stat is built from the game traits that belong to it: how much more of them you play than the group, and how much you like them (ratings, reviews, taste answers). 10–11 is the group’s average, 3–20 the range. Constitution, Wisdom and Charisma also count finishing games, writing reviews and rating generously. Your class is the one whose ideal stats best match the shape of your sheet, using RPGBot’s class guides; the % is how close the match is.", "Blastlog.Muted")
        .Also(t => t.MaxWidth = 760), credit);
    });
    about.Margin = new Thickness(0, 8, 0, 0);
    return Stack(null, Columns(300, sheets), about);
  }
  /// <summary>Automation name of the look picker; Nav.SheetLook scrolls to it.</summary>
  internal const string LookPickerName = "Your character sheet’s look";
  static readonly string[] Looks = { "classic", "ledger", "arcade", "blueprint" };
  bool savingLook;
  /// <summary>
  /// "Look" and a select of the four looks (web Compare.tsx CharacterSheets): picking one saves it at once
  /// (PUT me/sheet-style), then Compare reloads so the sheet wears it; disabled while it saves.
  /// </summary>
  FrameworkElement LookPicker(string look) {
    var box = new ComboBox { MinHeight = 44, MinWidth = 180, FontSize = 15, IsEnabled = !savingLook, VerticalAlignment = VerticalAlignment.Center };
    foreach (var key in Looks) box.Items.Add(new ComboBoxItem { Content = LookNames[key], Tag = key, IsSelected = key == look });
    AutomationProperties.SetName(box, LookPickerName);
    box.SelectionChanged += (s, e) => {
      try {
        if (!(box.SelectedItem is ComboBoxItem i) || (string)i.Tag == look || savingLook) return;
        var style = (string)i.Tag;
        savingLook = true; box.IsEnabled = false;
        Run(async () => {
          try { await plugin.Write("me/sheet-style", new { style }, System.Net.Http.HttpMethod.Put); await Read(true); }
          finally { savingLook = false; Render(); }
        }, "sheet look");
      } catch (Exception ex) { global::Playnite.SDK.LogManager.GetLogger().Error(ex, "Blastlog sheet look"); }
    };
    var label = T("Look", "Blastlog.Small", null, 0, FontWeights.Bold).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 10, 0); });
    return Row(label, box).Also(r => r.Margin = new Thickness(0, 10, 0, 0));
  }
  static Hyperlink Hyper(string text, string url) {
    var h = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url) };
    h.SetResourceReference(TextElement.ForegroundProperty, "Blastlog.GameText");
    h.RequestNavigate += (s, e) => { UiKit.Open(url); e.Handled = true; };
    return h;
  }
  /// <summary>Every look is built from tokens plus the owner's colour; text stays in text tokens on near-surface backgrounds.</summary>
  static Border SheetCard(UIElement content, ComparePerson p, string look) {
    var card = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(22), Child = content };
    var surface = ColorOf("Blastlog.Surface2"); var tint = ((SolidColorBrush)p.Brush).Color;
    switch (look) {
      case "ledger": {
        // Ruled paper with a double rule in the owner's colour.
        var lines = new DrawingBrush(new GeometryDrawing(null, new Pen(Brush("Blastlog.Line"), 1), new LineGeometry(new Point(0, 27), new Point(10, 27))))
          { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, 28), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 10, 28), ViewboxUnits = BrushMappingMode.Absolute };
        var grid = new Grid(); grid.Children.Add(new Rectangle { Fill = lines, Opacity = .7, IsHitTestVisible = false });
        var rule = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-22, -22, -22, 0) };
        rule.Children.Add(new Rectangle { Height = 3, Fill = p.Brush }); rule.Children.Add(new Rectangle { Height = 3, Fill = p.Brush, Margin = new Thickness(0, 3, 0, 0) });
        grid.Children.Add(rule); card.Child = null; grid.Children.Add(content); card.Child = grid;
        card.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface"); card.CornerRadius = new CornerRadius(6); card.ClipToBounds = true;
        break;
      }
      case "arcade": {
        // Square, notched feel with scanlines; bars are segmented above.
        var scan = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 0, 4, 2))))
          { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 4), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 4, 4), ViewboxUnits = BrushMappingMode.Absolute };
        var grid = new Grid(); grid.Children.Add(new Rectangle { Fill = scan, IsHitTestVisible = false }); card.Child = null; grid.Children.Add(content); card.Child = grid;
        card.CornerRadius = new CornerRadius(0); card.BorderThickness = new Thickness(2); card.BorderBrush = p.Brush;
        card.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
        break;
      }
      case "blueprint": {
        // Grid paper and a dashed edge.
        var gridBrush = new DrawingBrush(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(70, tint.R, tint.G, tint.B)), .6), new RectangleGeometry(new Rect(0, 0, 18, 18))))
          { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 18, 18), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 18, 18), ViewboxUnits = BrushMappingMode.Absolute };
        var grid = new Grid();
        grid.Children.Add(new Rectangle { Fill = gridBrush, IsHitTestVisible = false, Margin = new Thickness(-22) });
        grid.Children.Add(new Rectangle { Stroke = p.Brush, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 5, 4 }, RadiusX = 16, RadiusY = 16, Margin = new Thickness(-14), IsHitTestVisible = false });
        card.Child = null; grid.Children.Add(content); card.Child = grid;
        card.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface"); card.ClipToBounds = true;
        break;
      }
      default: {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(.4, 1) };
        g.GradientStops.Add(new GradientStop(Blend(surface, tint, .15), 0)); g.GradientStops.Add(new GradientStop(surface, .6));
        card.Background = g;
        break;
      }
    }
    return card;
  }
  static Color Blend(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
  static Brush Segments() => new DrawingBrush(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, 8, 6))))
    { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, 6), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 10, 6), ViewboxUnits = BrushMappingMode.Absolute };

  // 06
  FrameworkElement Splits(JArray splits) => Columns(380, splits.Take(6).Select(s => {
    var breakaway = S(s, "breakaway");
    var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
    foreach (var r in Arr(s["ratings"])) {
      var id = S(r, "userId"); var away = breakaway == id;
      var t = T($"{Name(id)} {Stars(D(r, "rating") ?? 0)}", "Blastlog.Small", null, 0, FontWeights.Bold); t.Foreground = BrushOf(id);
      var chip = new Border { Child = t, CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(away ? 2 : 1), BorderBrush = BrushOf(id) };
      if (!away) chip.BorderBrush = new SolidColorBrush(((SolidColorBrush)BrushOf(id)).Color) { Opacity = .35 };
      chips.Children.Add(chip);
    }
    var text = Stack(new Thickness(12, 0, 0, 0), T(S(s, "title"), "Blastlog.Body", null, 16, FontWeights.Bold), chips);
    if (breakaway != null) text.Children.Add(T($"{Name(breakaway)} broke from the group", "Blastlog.Small"));
    var row = new DockPanel { LastChildFill = true };
    var gap = T($"{Stars(D(s, "spread") ?? 0)} apart", "Blastlog.Body", "Blastlog.Warn", 15, FontWeights.ExtraBold); gap.VerticalAlignment = VerticalAlignment.Top; gap.Margin = new Thickness(10, 0, 0, 0);
    gap.ToolTip = "How far apart the highest and lowest ratings are"; DockPanel.SetDock(gap, Dock.Right); row.Children.Add(gap);
    row.Children.Add(Row(Cover(plugin, S(s, "coverUrl"), S(s, "title"), 60, 8), text));
    var game = s;
    var card = Kit.Card(row); card.Margin = new Thickness(0);
    return (UIElement)Plain(card, () => Lookup(game), S(s, "title")).Also(b => { b.Margin = new Thickness(0); b.HorizontalContentAlignment = HorizontalAlignment.Stretch; });
  }), 14);

  // 07
  FrameworkElement Favourites() {
    var ids = new HashSet<string>(people.Select(p => p.Id));
    var w = new WrapPanel();
    foreach (var item in Arr(favourites?["items"]).Where(i => focus == null || Arr(i["lovers"]).Count(l => ids.Contains(S(l, "userId"))) >= 2).Take(12)) {
      var dots = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
      foreach (var l in Arr(item["lovers"])) {
        var dot = new Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 4, 0), Fill = BrushOf(S(l, "userId")) };
        dot.ToolTip = $"{S(l, "displayName")} {Stars(D(l, "rating") ?? 0)}"; dots.Children.Add(dot);
      }
      var game = item;
      w.Children.Add(CoverButton(plugin, S(item, "coverUrl"), S(item, "title"), 132, () => Lookup(game), dots));
    }
    if (w.Children.Count == 0) return T("Nothing two of you both loved yet.", "Blastlog.Muted");
    return w;
  }

  /// <summary>Everyone's rating and review for one game, side by side (a Playnite window; Escape closes).</summary>
  void Showdown(string gameId, string title, string coverUrl) => Run(async () => {
    var found = (await plugin.Mirror("compare/games?limit=25&offset=0&search=" + Uri.EscapeDataString(title ?? ""), false)).Value;
    // Searched by title, matched by ID: a same-titled game (a remake, a reboot) never stands in for the one picked.
    var match = Arr(found?["items"]).FirstOrDefault(g => S(g, "gameId") == gameId);
    var content = new StackPanel();
    content.Children.Add(Row(Cover(plugin, coverUrl, title, 72, 10), T(title, "Blastlog.PageTitle").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(18, 0, 0, 0); })));
    var takes = people.Select(p => new { p, entry = Arr(match?["entries"]).FirstOrDefault(e => S(e["user"], "id") == p.Id) }).Where(t => t.entry != null).ToList();
    var missing = people.Where(p => takes.All(t => t.p.Id != p.Id)).ToList();
    var cards = takes.Select(t => {
      var s = new StackPanel();
      var head = Wrap(T(t.p.Name, "Blastlog.CardTitle").Also(x => { x.Foreground = t.p.Brush; x.Margin = new Thickness(0, 0, 10, 0); }));
      var status = S(t.entry, "status");
      if (status != null) head.Children.Add(Kit.StatusPill(status).Also(x => x.Margin = new Thickness(0, 0, 10, 0)));
      var rating = D(t.entry, "rating");
      if (rating != null && rating > 0) head.Children.Add(Kit.Stars(rating).Also(x => x.VerticalAlignment = VerticalAlignment.Center));
      s.Children.Add(head);
      var review = S(t.entry, "review");
      s.Children.Add(string.IsNullOrWhiteSpace(review) ? T("No review written.", "Blastlog.Muted").Also(x => x.Margin = new Thickness(0, 10, 0, 0)) : T("“" + review + "”", "Blastlog.Body").Also(x => x.Margin = new Thickness(0, 10, 0, 0)));
      var card = Kit.Card(s); card.Margin = new Thickness(0); card.BorderThickness = new Thickness(0, 4, 0, 0); card.BorderBrush = t.p.Brush;
      return (UIElement)card;
    }).ToList();
    content.Children.Add(Columns(260, cards).Also(g => g.Margin = new Thickness(0, 24, 0, 0)));
    if (takes.Count == 0) content.Children.Add(T("Nobody has this one yet.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 20, 0, 0)));
    if (takes.Count > 0 && missing.Count > 0) content.Children.Add(T($"Not in {string.Join(" or ", missing.Select(p => p.Name))}’s library.", "Blastlog.Muted"));
    Dialog(plugin, title ?? "Compare", content);
  }, "compare look-up");
}
}
