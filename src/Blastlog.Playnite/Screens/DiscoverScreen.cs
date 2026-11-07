using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>Discover (design.md 8 "Discover"; PLAYNITE_DESIGN.md 4.5).</summary>
public sealed class DiscoverScreen : IBlastlogScreen {
  public string Title => "Discover";
  public string Icon => Icons.Discover;
  public int Order => 40;
  public FrameworkElement Create(BlastlogPlugin plugin) => new DiscoverView(plugin);
}

/// <summary>
/// The site's desktop Discover (apps/web Discover.tsx), natively: games from outside the player's list, best fits first,
/// each card wearing its game with where it came from, why it fits, a friend's line, Watch out, Also counts, More from the
/// series, and the answers (Want to play, I own it, Played it, Not for me with Why not?). The result line offers Undo,
/// Why not? and Rate and review it; the comfort pick sits above the picks; Hidden and answered lists every answer with
/// Undo. Covers open the game's Discover page on Blastlog.
/// </summary>
sealed class DiscoverView : UserControl {
  static readonly ILogger Log = LogManager.GetLogger();
  const int First = 24, More = 24;
  readonly BlastlogPlugin plugin; readonly StackPanel body = new StackPanel();
  readonly HashSet<string> seen = new HashSet<string>();
  JObject page, subs; int shown = First; bool loaded;
  DiscoverAnswered notice; string noticeText;

  public DiscoverView(BlastlogPlugin plugin) {
    this.plugin = plugin; Content = body;
    body.Children.Add(PnKit.Text("Picking games for you…", "Blastlog.Muted"));
    Loaded += (s, e) => { try { if (!loaded) { loaded = true; _ = Load(false); } } catch (Exception ex) { Log.Error(ex, "Blastlog Discover"); } };
  }

  async Task Load(bool fresh) {
    try {
      if (!plugin.Configured) { body.Children.Clear(); body.Children.Add(Kit.Card(PnKit.Text("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see Discover."))); return; }
      page = (await plugin.Mirror("discover", fresh)).Value;
      subs = (await plugin.Mirror("subscriptions", fresh)).Value;
      Build();
    } catch (Exception e) { Log.Error(e, "Blastlog Discover"); }
  }

  sealed class Pick { public string Source; public JObject Card; }
  static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
  static bool OutYet(JObject card) { var d = card.S("releaseDate"); return d == null || string.CompareOrdinal(d, Today) <= 0; }
  static string Stars(double r) => (r % 1 == 0 ? r.ToString("0", CultureInfo.InvariantCulture) : r.ToString("0.0", CultureInfo.InvariantCulture)) + "★";
  static string ShortDate(string date) => DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("MMM d", CultureInfo.CurrentCulture) : date;

  /// <summary>The small tag that says where a pick came from, and its detail line.</summary>
  static Tuple<string, string> SourceTag(Pick pick) {
    var card = pick.Card;
    if (pick.Source == "friends") return Tuple.Create("Friends", string.Join(" · ", card.Objects("friends").Select(f => $"{f.S("name")} {Stars(f.D("rating") ?? 0)}")));
    var shelf = card.S("shelf");
    if (shelf == "trending") return Tuple.Create("Trending", "Trending this week");
    if (shelf == "for_you") return Tuple.Create("For you", "Picked for your taste");
    if (shelf == "upcoming") {
      var date = card.S("releaseDate");
      if (date == null) return Tuple.Create("Coming soon", (string)null);
      return OutYet(card) ? Tuple.Create("New", "Came out " + ShortDate(date)) : Tuple.Create("Coming soon", PnKit.ReleaseLabel(date));
    }
    var acclaim = card.D("acclaim");
    return Tuple.Create("Critics", acclaim == null ? null : card.S("acclaimKind") == "critics" ? $"Critics {Math.Round(acclaim.Value)} on IGDB" : $"Rated {Math.Round(acclaim.Value)} on IGDB");
  }
  static string TagBrush(string source) => source == "friends" ? "Blastlog.GameText" : source == "critics" ? "Blastlog.Warn" : source == "trending" ? "Blastlog.Danger" : source == "for_you" ? "Blastlog.Ok" : "Blastlog.Text";

  void Build() {
    body.Children.Clear();
    if (page == null) { body.Children.Add(PnKit.Text("Discover is unavailable right now.", "Blastlog.Body", "Blastlog.Danger")); return; }
    var friends = page["friends"] as JObject; var catalog = page["catalog"] as JObject;
    var picks = friends.Objects("cards").Select(c => new Pick { Source = "friends", Card = c })
      .Concat(new[] { "critics", "upcoming", "trending", "for_you" }.SelectMany(k => catalog.Objects(k)).Select(c => new Pick { Source = c.S("shelf"), Card = c }))
      .OrderByDescending(p => p.Card.D("rank") ?? 0).ThenBy(p => p.Card.S("title"), StringComparer.CurrentCulture).ToList();
    var anyEarly = picks.Any(p => p.Source != "friends" && p.Card.B("earlyRead"));

    body.Children.Add(PnKit.Text("Discover", "Blastlog.Body", "Blastlog.GameText", FontWeights.Bold));
    body.Children.Add(PnKit.Text("Games you don’t have yet", "Blastlog.PageTitle").Also(t => t.FontSize = 32));
    body.Children.Add(PnKit.Text($"{picks.Count} picks from outside your list, best fits first. Open any cover for why it fits you. Answers here don’t change your taste, except a trait you pick when Not for me asks why."
      + (!page.B("starsShown") && friends.B("ready") ? " Predicted stars show once Blastlog knows your taste a little better (Sharpen your picks on Play next)." : "")
      + (anyEarly ? " “Early read” means Blastlog’s first, lighter look at a game nobody here has, so it shows no predicted stars yet." : ""), "Blastlog.Muted").Also(t => { t.Margin = new Thickness(0, 4, 0, 16); t.MaxWidth = 980; t.HorizontalAlignment = HorizontalAlignment.Left; }));

    if (noticeText != null) body.Children.Add(NoticeBar());
    if (page["replay"] is JObject replay) body.Children.Add(ComfortPick(replay));

    if (picks.Count == 0) body.Children.Add(PnKit.Text("Nothing new to show right now." + ((friends.D("friends") ?? 0) == 0 ? " Add friends from the Friends page and their favourites show up here too." : ""), "Blastlog.Body"));
    else {
      var grid = new CardGrid { MinColumn = 220, Gap = 16, Margin = new Thickness(0, 0, 0, 16) };
      foreach (var pick in picks.Take(shown)) grid.Children.Add(Card(pick));
      body.Children.Add(grid);
      if (picks.Count > shown) {
        var more = Kit.Button($"Show more ({picks.Count - shown} left)", () => { shown += More; Build(); }, "Blastlog.Ghost");
        more.HorizontalAlignment = HorizontalAlignment.Left; more.Margin = new Thickness(0, 0, 0, 16);
        body.Children.Add(more);
      }
      ReportSeen(picks.Take(shown));
    }
    var answered = page.Objects("answered");
    if (answered.Count > 0) body.Children.Add(PnKit.Disclosure($"Hidden and answered ({answered.Count})", () => Answered(answered)));
    body.Children.Add(PnKit.Text(plugin.CacheLine(), "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 12, 0, 0)));
  }

  /// <summary>Tells Blastlog which picks were on screen, for each shelf's pickup rate (the site's ReportSeen). Each card once per visit.</summary>
  void ReportSeen(IEnumerable<Pick> visible) {
    var fresh = visible.Where(p => seen.Add(p.Source + ":" + p.Card.S("gameId"))).Select(p => new JObject { ["gameId"] = p.Card.S("gameId"), ["shelf"] = p.Source }).ToList();
    for (var i = 0; i < fresh.Count; i += 200) _ = plugin.WriteQuietly("discover/seen", new JObject { ["cards"] = new JArray(fresh.Skip(i).Take(200)) });
  }

  FrameworkElement Card(Pick pick) {
    var card = pick.Card; var gameId = card.S("gameId"); var title = card.S("title"); var tag = SourceTag(pick);
    var early = pick.Source != "friends" && card.B("earlyRead");
    var notOut = pick.Source != "friends" && !OutYet(card);
    void Open() => Nav.DiscoverGame(plugin, gameId);
    var artGrid = new Grid();
    artGrid.Children.Add(PnKit.Art(plugin, gameId, card.S("coverUrl"), new CornerRadius(17, 17, 0, 0)));
    artGrid.Children.Add(PnKit.OverPill(tag.Item1, TagBrush(pick.Source), HorizontalAlignment.Left, VerticalAlignment.Top));
    var art = PnKit.Bare(artGrid, Open, "More about " + title);

    var dock = new DockPanel { Margin = new Thickness(14, 12, 14, 14), LastChildFill = false };
    var answers = new DiscoverAnswers(plugin, gameId, title, pick.Source, notOut, Answered);
    answers.Margin = new Thickness(0, 8, 0, 0);
    DockPanel.SetDock(answers, Dock.Bottom); dock.Children.Add(answers);
    var text = new StackPanel(); DockPanel.SetDock(text, Dock.Top);
    text.Children.Add(PnKit.Bare(PnKit.Text(title, "Blastlog.Body", null, FontWeights.Bold).Also(t => t.FontSize = 15), Open, title));
    var predicted = card.D("predictedRating");
    var facts = string.Join(" · ", new[] { card.S("year"), predicted != null ? $"predicted {predicted:0.0}★" : null, early ? "early read" : null }.Where(x => x != null));
    if (facts.Length > 0) text.Children.Add(Para(PnKit.Text(facts, "Blastlog.Small")));
    var chip = PlayNextSubscriptions.Chip(subs, gameId); if (chip != null) text.Children.Add(chip);
    if (!string.IsNullOrEmpty(tag.Item2)) text.Children.Add(Para(PnKit.Text(tag.Item2, "Blastlog.Body")));
    var reasons = card.Strings("reasons");
    if (reasons.Count > 0) text.Children.Add(Para(PnKit.Line("Blastlog.Body", PnKit.Run("Because you like ", "Blastlog.TextMuted"), PnKit.Run(string.Join(", ", reasons)))));
    if (card.S("watchOut") != null) text.Children.Add(Para(PnKit.Text("Watch out: " + card.S("watchOut"), "Blastlog.Body", "Blastlog.Warn")));
    if (card["line"] is JObject line) text.Children.Add(PnKit.Quote(line.S("quote"), line.S("name")).Also(q => q.Margin = new Thickness(0, 0, 0, 6)));
    var versions = card.Strings("versions");
    if (versions.Count > 0) text.Children.Add(Para(PnKit.Text("Also counts: " + string.Join(", ", versions), "Blastlog.Small")));
    var series = card.Objects("moreFromSeries");
    if (series.Count > 0) text.Children.Add(PnKit.Disclosure($"More from {card.S("series") ?? "this series"} ({series.Count})", () => {
      var list = new StackPanel { Margin = new Thickness(14, 0, 0, 4) };
      foreach (var g in series) list.Children.Add(PnKit.Text(g.S("title") + (g.S("year") != null ? $" ({g.S("year")})" : ""), "Blastlog.Body").Also(t => t.FontSize = 14));
      return list;
    }));
    dock.Children.Add(text);
    return PnKit.GameCard(plugin, gameId, card.S("coverUrl"), new DockPanel { Children = { art.Also(a => DockPanel.SetDock(a, Dock.Top)), dock } });
  }
  static FrameworkElement Para(TextBlock t) { t.FontSize = 14; t.Margin = new Thickness(0, 0, 0, 6); return t; }

  async Task Answered(DiscoverAnswered saved) { notice = saved; noticeText = saved.Text; await Load(true); }

  FrameworkElement NoticeBar() {
    var n = notice;
    var buttons = new List<Button>();
    if (n?.Played == true && n.EntryId != null) buttons.Add(Kit.Button("Rate and review it", () => Nav.Review(plugin, n.EntryId)));
    if (n?.UndoId != null) buttons.Add(Kit.Button("Undo", async () => await Undo(n.UndoId, null), "Blastlog.Ghost"));
    if (n?.ReplayPickId != null) buttons.Add(Kit.Button("Undo", async () => await UndoReplay(n.ReplayPickId, n.ReplayTitle), "Blastlog.Ghost"));
    return PnKit.Notice(noticeText, buttons, () => { notice = null; noticeText = null; Build(); });
  }

  async Task Undo(string answerId, string title) {
    var result = await plugin.Write($"discover/answers/{answerId}/undo", new JObject());
    if (result == null) return;
    notice = null;
    noticeText = result.B("entryKept") ? $"Undone. {title ?? "The game"} stays in your library because you changed it since; remove it there if you want." : $"Undone. {title ?? "The game"} can show up here again.";
    await Load(true);
  }

  /// <summary>The comfort pick: one old favorite after a run of low ratings, apart from the picks, with Play it again and Not now.</summary>
  FrameworkElement ComfortPick(JObject pick) {
    var pickId = pick.S("pickId"); var title = pick.S("title"); var gameId = pick.S("gameId");
    void Open() => Nav.DiscoverGame(plugin, gameId);
    var row = new DockPanel();
    var art = PnKit.Bare(PnKit.Art(plugin, gameId, pick.S("coverUrl"), new CornerRadius(14), 120), Open, title);
    art.Margin = new Thickness(0, 0, 20, 0); art.VerticalAlignment = VerticalAlignment.Top; art.HorizontalAlignment = HorizontalAlignment.Left;
    DockPanel.SetDock(art, Dock.Left); row.Children.Add(art);
    var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
    words.Children.Add(PnKit.Text("Comfort pick", "Blastlog.Body", "Blastlog.GameText", FontWeights.Bold));
    words.Children.Add(PnKit.Bare(PnKit.Text(title, "Blastlog.Section").Also(t => t.Margin = new Thickness(0, 2, 0, 4)), Open, title));
    words.Children.Add(PnKit.Text("Your last few picks missed. Here’s an old favorite.", "Blastlog.Body"));
    var reasons = pick.Strings("reasons");
    if (reasons.Count > 0) words.Children.Add(PnKit.Text(string.Join(" · ", reasons), "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 4, 0, 0)));
    var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
    var again = Kit.Button("Play it again", async () => await AnswerReplay(pickId, title, "replay")); again.IsEnabled = pickId != null;
    var later = Kit.Button("Not now", async () => await AnswerReplay(pickId, title, "not-now"), "Blastlog.Ghost"); later.IsEnabled = pickId != null;
    buttons.Children.Add(again); buttons.Children.Add(later);
    var play = PnKit.PlayOrInstall(plugin, pick.S("entryId"), primary: false); if (play != null) buttons.Children.Add(play);
    words.Children.Add(buttons);
    row.Children.Add(words);
    var card = PnKit.GameCard(plugin, gameId, pick.S("coverUrl"), row, 22);
    card.Padding = new Thickness(20); card.Margin = new Thickness(0, 0, 0, 18);
    return card;
  }
  async Task AnswerReplay(string pickId, string title, string answer) {
    if (pickId == null) return;
    if (await plugin.Write($"discover/replay/{pickId}/{answer}", new JObject()) == null) return;
    notice = new DiscoverAnswered { ReplayPickId = pickId, ReplayTitle = title };
    noticeText = answer == "replay" ? $"{title} is back in Currently playing. Enjoy." : $"Okay. {title} is put away for now.";
    await Load(true);
  }
  async Task UndoReplay(string pickId, string title) {
    var result = await plugin.Write($"discover/replay/{pickId}/undo", new JObject());
    if (result == null) return;
    notice = null;
    noticeText = result.B("entryKept") ? $"Undone. {title} stays as you left it in your library, since you changed it after." : $"Undone. {title} is back as it was.";
    await Load(true);
  }

  static readonly Dictionary<string, string> AnswerLabel = new Dictionary<string, string> { ["want"] = "Want to play", ["played"] = "Played it", ["not_for_me"] = "Not for me", ["not_played"] = "Haven’t played it", ["owned"] = "I own it" };
  UIElement Answered(List<JObject> answered) {
    var list = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
    foreach (var item in answered) {
      var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
      var id = item.S("id"); var title = item.S("title");
      var undo = Kit.Button("Undo", async () => await Undo(id, title), "Blastlog.Ghost"); undo.Margin = new Thickness(12, 0, 0, 0);
      System.Windows.Automation.AutomationProperties.SetName(undo, "Undo " + title);
      DockPanel.SetDock(undo, Dock.Right); row.Children.Add(undo);
      var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
      words.Children.Add(PnKit.Text(title, "Blastlog.Body", null, FontWeights.Bold));
      var when = DateTimeOffset.TryParse(item.S("createdAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at.LocalDateTime.ToString("d", CultureInfo.CurrentCulture) : "";
      words.Children.Add(PnKit.Text($"{(AnswerLabel.TryGetValue(item.S("answer") ?? "", out var l) ? l : item.S("answer"))} · {when}", "Blastlog.Small"));
      row.Children.Add(words);
      list.Children.Add(new Border { Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 8, 8, 8), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left }
        .Res(Border.BackgroundProperty, "Blastlog.Surface").Also(b => b.MinWidth = 420));
    }
    return list;
  }
}
}
