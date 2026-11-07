using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// The dashboard's Your library card, Recently finished shelf and Friends lately (design.md 8 Dashboard; web
/// DashboardShelf.tsx and FriendActivity.tsx).
/// </summary>
static class DashboardLibrary {
  static string Pct(double part, double total) => total > 0 ? Math.Round(part / total * 100, MidpointRounding.AwayFromZero) + "%" : "0%";

  // ---- Your library: four numbers (each opens the library filtered), finished vs still to play, How your stars split ----
  public static FrameworkElement LibraryCard(BlastlogPlugin plugin, JObject stats) {
    var panel = new StackPanel();
    var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 14) };
    head.Children.Add(Dash.Text("Your library", "Blastlog.CardTitle").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    var open = Dash.Link("Open library →", () => Dash.Library(plugin, null)); DockPanel.SetDock(open, Dock.Right); head.Children.Add(open);
    panel.Children.Add(head);
    if (stats == null) { panel.Children.Add(Dash.Text("Your library's numbers show here once Blastlog answers.", "Blastlog.Muted")); return Dash.Card(panel); }

    double completed = (double?)stats["completed"] ?? 0, backlog = (double?)stats["backlog"] ?? 0, total = completed + backlog;
    var ratedDone = (int?)stats["ratedDone"] ?? 0; var average = (double?)stats["averageRating"] ?? 0;
    var level = (string)stats["lengthLevel"]; var levelLabel = level != null ? Dash.Level(level) : "main + extras";
    var unknownDone = (int?)stats["lengthUnknown"]?["done"] ?? 0; var noEndDone = (int?)stats["lengthNoEnd"]?["done"] ?? 0;
    var notCounted = new[] { unknownDone > 0 ? $"{unknownDone} with no length yet" : null, noEndDone > 0 ? $"{noEndDone} with no end" : null }.Where(x => x != null).ToList();
    var numbers = new[] {
      new[] { backlog.ToString("N0", CultureInfo.InvariantCulture), "Backlog", "games to play", $"Games to play ({Pct(backlog, total)} of the library)", "status=backlog&sort=length&direction=asc" },
      new[] { completed.ToString("N0", CultureInfo.InvariantCulture), "Finished", "done, quit or put down", $"Games finished or retired: Done, Quit, DNF, Unplayable ({Pct(completed, total)})", "status=completed&sort=finished&direction=desc" },
      new[] { average.ToString("0.0", CultureInfo.InvariantCulture) + "★", "Average", $"over {ratedDone} rated", $"Average rating over {ratedDone} rated games", "sort=rating&direction=desc" },
      new[] { Math.Round((double?)stats["completedMainPlusExtraHours"] ?? 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture) + "h", "Finished time", $"to beat, {levelLabel}",
        $"How long the games finished take to beat ({levelLabel}){(notCounted.Count > 0 ? "; " + string.Join(", ", notCounted) + " not counted" : "")}", "status=done&sort=length&direction=desc" },
    };
    var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 16) };
    foreach (var n in numbers) {
      string value = n[0], label = n[1], hint = n[2], description = n[3], query = n[4];
      var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Children = {
        Dash.Text(value, "Blastlog.Body", weight: FontWeights.ExtraBold, size: 26), Dash.Text(label, "Blastlog.Body", weight: FontWeights.SemiBold), Dash.Text(hint, "Blastlog.Small") } };
      var q = query;
      var tile = new Button { Content = content, Style = Kit.Style("Blastlog.Ghost"), ToolTip = description, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(16, 12, 16, 12), HorizontalContentAlignment = HorizontalAlignment.Left };
      tile.SetResourceReference(Control.BackgroundProperty, "Blastlog.Surface2"); tile.SetResourceReference(Control.BorderBrushProperty, "Blastlog.Line");
      tile.Click += (s, e) => Dash.Library(plugin, q);
      Dash.AutomationName(tile, $"{value} {label}: {description}");
      grid.Children.Add(tile);
    }
    panel.Children.Add(grid);
    if (total > 0) {
      panel.Children.Add(BarLabel(Bold(completed.ToString("N0", CultureInfo.InvariantCulture), " finished"), Bold(backlog.ToString("N0", CultureInfo.InvariantCulture), " still to play")));
      panel.Children.Add(Split(new[] { new Part(completed, 1, "Blastlog.Game"), new Part(backlog, 1, "Blastlog.GameBar") }, null).Margin(0, 6, 0, 18));
    }
    panel.Children.Add(BarLabel(new TextBlock { Style = Kit.Style("Blastlog.Muted"), Text = "How your stars split" }, Bold(average.ToString("0.0", CultureInfo.InvariantCulture) + "★", " average")));
    var dist = stats["ratingDistribution"] as JObject;
    var counts = Enumerable.Range(1, 5).Select(s => (int?)dist?[s.ToString(CultureInfo.InvariantCulture)] ?? 0).ToArray(); var sum = counts.Sum();
    if (sum == 0) panel.Children.Add(Dash.Text("No ratings yet.", "Blastlog.Muted").Margin(0, 6, 0, 0));
    else {
      var shades = Dash.StarShades(counts, out var peak);
      panel.Children.Add(Split(counts.Select((c, i) => new Part(c, shades[i], "Blastlog.Star")).ToArray(), i => Dash.Library(plugin, $"status=done&rating={i + 1}")).Margin(0, 6, 0, 10));
      var legend = new UniformGrid { Columns = 5 };
      for (var i = 0; i < 5; i++) {
        var star = i + 1;
        var content = new StackPanel { Children = { Dash.Text(counts[i].ToString(CultureInfo.InvariantCulture), "Blastlog.Body", weight: FontWeights.ExtraBold).Also(t => t.HorizontalAlignment = HorizontalAlignment.Center),
          Dash.Text($"{star}★ · {Pct(counts[i], sum)}", "Blastlog.Small", star == peak ? "Blastlog.Star" : null).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.TextWrapping = TextWrapping.NoWrap; }) } };
        var b = new Button { Content = content, Style = Kit.Style("Blastlog.TextButton"), Margin = new Thickness(0), Padding = new Thickness(2, 4, 2, 4) };
        b.Click += (s, e) => Dash.Library(plugin, $"status=done&rating={star}");
        Dash.AutomationName(b, $"{counts[i]} games rated {star} stars, {Pct(counts[i], sum)}");
        legend.Children.Add(b);
      }
      panel.Children.Add(legend);
    }
    return Dash.Card(panel);
  }
  static TextBlock Bold(string number, string rest) {
    var t = new TextBlock { Style = Kit.Style("Blastlog.Muted") };
    var b = new Run(number) { FontWeight = FontWeights.Bold }; b.SetResourceReference(TextElement.ForegroundProperty, "Blastlog.Text");
    t.Inlines.Add(b); t.Inlines.Add(new Run(rest)); return t;
  }
  static FrameworkElement BarLabel(TextBlock left, TextBlock right) {
    var d = new DockPanel { LastChildFill = false }; d.Children.Add(left); DockPanel.SetDock(right, Dock.Right); d.Children.Add(right); return d;
  }
  /// <summary>One thin bar split by value (each part a share of the width), optionally clickable per part.</summary>
  sealed class Part { public double Value, Opacity; public string Brush; public Part(double value, double opacity, string brush) { Value = value; Opacity = opacity; Brush = brush; } }
  static FrameworkElement Split(Part[] parts, Action<int> pick) {
    var grid = new Grid { Height = 10 };
    var shown = 0;
    for (var i = 0; i < parts.Length; i++) {
      if (parts[i].Value <= 0) continue;
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(parts[i].Value, GridUnitType.Star) });
      var seg = new Border { CornerRadius = new CornerRadius(999), Margin = new Thickness(shown == 0 ? 0 : 2, 0, 0, 0), Opacity = parts[i].Opacity };
      seg.SetResourceReference(Border.BackgroundProperty, parts[i].Brush);
      if (pick != null) { var index = i; seg.ToolTip = $"{i + 1}★: {parts[i].Value} game{(parts[i].Value == 1 ? "" : "s")}"; Dash.OnClick(seg, () => pick(index)); }
      Grid.SetColumn(seg, shown++); grid.Children.Add(seg);
    }
    return grid;
  }

  // ---- Recently finished ----
  const int WaitingShown = 8, RatedShown = 10;
  static readonly Dictionary<string, string> NeedLabel = new Dictionary<string, string> { ["both"] = "Rate & review", ["review"] = "Add a review", ["stars"] = "Add stars" };
  static bool HasWords(JObject e) => !string.IsNullOrWhiteSpace((string)e["review"]);
  static bool Rated(JObject e) => e["rating"] != null && e["rating"].Type != JTokenType.Null;
  static string NeedOf(JObject e) => !Rated(e) ? HasWords(e) ? "stars" : "both" : "review";
  static string FinishedText(JObject e) {
    var status = (string)e["status"]; var verb = status == "dnf" ? "Didn't finish" : status == "quit" ? "Quit" : "Finished";
    var on = (string)e["finishedOn"]; return on != null ? $"{verb} {Dash.ShortDate(on)}" : verb;
  }

  /// <summary>How many covers fit across the shelf, and how wide each one is (DashboardShelf.tsx fitShelf).</summary>
  sealed class Fit { public int Waiting, Rated; public double Width; public Fit(int waiting, int rated, double width) { Waiting = waiting; Rated = rated; Width = width; } }
  static Fit FitShelf(double width, int waitingCount, int ratedCount) {
    const double gap = 14, groupGap = 22, waitingPad = 26, more = 90, min = 124, max = 160;
    var waiting = Math.Min(waitingCount, WaitingShown);
    for (var round = 0; round < 4; round++) {
      var hasMore = waitingCount > waiting; var groups = (waiting > 0 ? 1 : 0) + (ratedCount > 0 ? 1 : 0);
      var fixedW = (waiting > 0 ? waitingPad : 0) + (hasMore ? more + gap : 0) + (groups == 2 ? groupGap : 0);
      var fits = Math.Max(1, (int)Math.Floor((width - fixedW + groups * gap) / (min + gap)));
      var next = waitingCount > 0 ? Math.Max(1, Math.Min(waiting, fits - Math.Min(ratedCount, 3))) : 0;
      if (next != waiting) { waiting = next; continue; }
      var rated = Math.Min(ratedCount, Math.Min(RatedShown, Math.Max(0, fits - waiting))); var shown = waiting + rated;
      var used = (waiting > 0 ? 1 : 0) + (rated > 0 ? 1 : 0);
      var room = width - (waiting > 0 ? waitingPad : 0) - (hasMore ? more + gap : 0) - (used == 2 ? groupGap : 0) - (shown - used) * gap;
      return new Fit(waiting, rated, Math.Floor(Math.Min(max, Math.Max(min, room / Math.Max(1, shown)))));
    }
    return new Fit(waiting, 0, min);
  }

  public static FrameworkElement RecentlyFinished(BlastlogPlugin plugin, DashboardData data) {
    var panel = new StackPanel();
    var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 14) };
    var titles = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
    titles.Children.Add(Dash.Text("Recently finished", "Blastlog.CardTitle").Margin(0, 0, 14, 0));
    titles.Children.Add(Dash.Text("Your stars and reviews teach Blastlog your taste", "Blastlog.Muted").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 12, 0); }));
    var badge = TasteBadge(data.TasteHelp?["knowledge"] as JObject); if (badge != null) titles.Children.Add(badge);
    head.Children.Add(titles);
    var all = Dash.Link("All suggestions →", () => Dash.Site(plugin, "/recommendations#sharpen")); DockPanel.SetDock(all, Dock.Right); head.Children.Add(all);
    panel.Children.Add(head);

    var shelf = data.Shelf;
    var finished = (int?)shelf?["finished"] ?? 0;
    if (shelf == null || finished == 0) {
      panel.Children.Add(Dash.Text(shelf == null ? "Your finished games show here once Blastlog answers." : "Finish something and it shows up here, ready for your stars and a review.", "Blastlog.Muted"));
      return Dash.Card(panel);
    }
    var waitingAll = Dash.Arr(shelf["waiting"]).OfType<JObject>().ToList(); var waitingTotal = (int?)shelf["waitingTotal"] ?? waitingAll.Count;
    var ratedAll = Dash.Arr(shelf["rated"]).OfType<JObject>().ToList();
    var knowledgeActions = Dash.Arr(data.TasteHelp?["actions"]).OfType<JObject>().Where(a => (string)a["kind"] == "rate")
      .GroupBy(a => (string)a["entryId"]).ToDictionary(g => g.Key ?? "", g => (int?)g.First()["impact"] ?? 0);

    var row = new ContentControl();
    double lastWidth = -1;
    void Fill(double width) {
      if (Math.Abs(width - lastWidth) < 8) return; lastWidth = width;
      var fit = FitShelf(width, waitingTotal, ratedAll.Count);
      var line = new StackPanel { Orientation = Orientation.Horizontal };
      var waiting = waitingAll.Take(fit.Waiting).ToList(); var rated = ratedAll.Take(fit.Rated).ToList();
      if (waiting.Count > 0) {
        var group = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(13, 10, 13, 12), Margin = new Thickness(0, 0, rated.Count > 0 ? 22 : 0, 0) };
        group.SetResourceReference(Border.BackgroundProperty, "Blastlog.GameTint");
        var g = new StackPanel();
        g.Children.Add(Dash.Row(0, Dash.Text("Waiting for stars and a review", "Blastlog.Small", "Blastlog.Text", FontWeights.SemiBold).Also(t => t.VerticalAlignment = VerticalAlignment.Center), Dash.Badge(waitingTotal)).Margin(0, 0, 0, 10));
        var items = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var e in waiting) items.Children.Add(Item(plugin, e, true, fit.Width, knowledgeActions));
        if (waitingTotal > waiting.Count) {
          var moreButton = Kit.Button($"+{waitingTotal - waiting.Count} more", () => Dash.Site(plugin, "/recommendations#sharpen"), "Blastlog.Ghost");
          moreButton.Width = 90; moreButton.Padding = new Thickness(4, 0, 4, 0); moreButton.VerticalAlignment = VerticalAlignment.Top; moreButton.Height = fit.Width * 4 / 3; moreButton.Margin = new Thickness(0);
          items.Children.Add(moreButton);
        }
        g.Children.Add(items); group.Child = g; line.Children.Add(group);
      }
      if (rated.Count > 0) {
        var g = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        g.Children.Add(Dash.Text("Rated lately", "Blastlog.Small", "Blastlog.Text", FontWeights.SemiBold).Margin(0, 0, 0, 10));
        var items = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var e in rated) items.Children.Add(Item(plugin, e, false, fit.Width, knowledgeActions));
        g.Children.Add(items); line.Children.Add(g);
      }
      row.Content = line;
    }
    row.SizeChanged += (s, e) => { try { Fill(e.NewSize.Width); } catch (Exception) { } };
    row.Loaded += (s, e) => { try { if (row.ActualWidth > 0) Fill(row.ActualWidth); } catch (Exception) { } };
    row.MinHeight = 200;
    panel.Children.Add(row);
    return Dash.Card(panel);
  }

  /// <summary>One finish: its cover and title (the game page, or the review for a waiting game), and its next step beside it.</summary>
  static FrameworkElement Item(BlastlogPlugin plugin, JObject entry, bool waiting, double width, Dictionary<string, int> impact) {
    var id = (string)entry["id"]; var game = Dash.Game(entry["game"]); var title = game?.Title ?? "";
    var box = new StackPanel { Width = width, Margin = new Thickness(0, 0, 14, 0) };
    Kit.GameScope(box, plugin, game);
    var tile = new StackPanel();
    tile.Children.Add(Kit.Cover(plugin, game, width, ArtworkContext.Thumbnail, 14));
    tile.Children.Add(Dash.Text(title, "Blastlog.Body", weight: FontWeights.SemiBold).Also(t => { t.Margin = new Thickness(0, 8, 0, 2); t.MaxHeight = 44; t.TextTrimming = TextTrimming.CharacterEllipsis; }));
    if (!waiting) {
      var meta = new WrapPanel();
      meta.Children.Add(Dash.Text("★" + ((double?)entry["rating"])?.ToString("0.#", CultureInfo.InvariantCulture), "Blastlog.Small", "Blastlog.Star", FontWeights.Bold).Margin(0, 0, 8, 0));
      meta.Children.Add(Dash.Text(Dash.ShortDate((string)entry["finishedOn"]), "Blastlog.Small"));
      tile.Children.Add(meta);
    }
    // The hover card: the review, or why it's waiting (design.md 8 Recently finished).
    var need = NeedOf(entry);
    var why = need == "review" ? "Blastlog knows the score, not the why. A few lines teach it what you care about."
      : need == "stars" ? "Your review is in. Stars let Blastlog count it toward your taste."
      : impact.TryGetValue(id, out var helps) && helps > 0 ? $"Rating it could change {helps} of your top 10 picks, and a few words on why help Blastlog even more."
      : "Stars and a few words on why teach Blastlog what you love and what loses you.";
    var tipMeta = waiting ? FinishedText(entry) + " · " + (!Rated(entry) ? HasWords(entry) ? "no stars yet" : "no stars or review yet" : $"★{entry["rating"]} no review yet")
      : $"★{entry["rating"]} · {FinishedText(entry)}";
    var tipBody = waiting ? why : ((string)entry["review"] ?? "").Trim();
    var tip = new StackPanel { MaxWidth = 340, Children = { Dash.Text(title, "Blastlog.CardTitle"), Dash.Text(tipMeta, "Blastlog.Small").Margin(0, 2, 0, 8), Dash.Text(tipBody, "Blastlog.Body"),
      Dash.Text(waiting ? "Click to rate and review" : "Click for the game page", "Blastlog.Small").Margin(0, 8, 0, 0) } };
    tip.Resources.MergedDictionaries.Add(Kit.Styles);
    tile.ToolTip = new ToolTip { Content = tip };
    Dash.OnClick(tile, () => { if (waiting) Dash.Review(plugin, id); else Dash.OpenEntry(plugin, id); });
    Dash.AutomationName(tile, title);
    box.Children.Add(tile);
    if (waiting) {
      var pill = Kit.Button(NeedLabel[need], () => Dash.Review(plugin, id), need == "both" ? "Blastlog.Primary" : "Blastlog.Ghost");
      pill.Margin = new Thickness(0, 6, 0, 0); pill.MinHeight = 44; pill.Padding = new Thickness(8, 0, 8, 0); pill.FontSize = 14; pill.HorizontalAlignment = HorizontalAlignment.Stretch;
      Dash.AutomationName(pill, $"{NeedLabel[need]}: {title}");
      box.Children.Add(pill);
    }
    return box;
  }

  /// <summary>"Taste level 8/12 · guesses 68% right" (SharpenPicks.tsx TasteLevelBadge).</summary>
  static FrameworkElement TasteBadge(JObject knowledge) {
    if (knowledge == null) return null;
    var level = (int?)knowledge["level"] ?? 0;
    var text = new TextBlock { Style = Kit.Style("Blastlog.Small"), VerticalAlignment = VerticalAlignment.Center };
    text.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text");
    string tip;
    if (level == 0) { text.Text = "Taste level: rate a few more games"; tip = "Blastlog needs at least 5 rated games with fingerprints to measure this."; }
    else {
      var pct = Math.Round(((double?)knowledge["accuracy"] ?? 0) * 100, MidpointRounding.AwayFromZero) + "%";
      text.Inlines.Add(new Run("Taste level ")); text.Inlines.Add(new Run(level.ToString(CultureInfo.InvariantCulture)) { FontWeight = FontWeights.ExtraBold });
      text.Inlines.Add(new Run("/" + knowledge["maxLevel"])); text.Inlines.Add(new Run($" · guesses {pct} right"));
      tip = $"Blastlog predicts which of two games you'd rate higher {pct} of the time (level 1 is a coin flip).";
    }
    var b = new Border { Child = text, CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 4, 12, 5), BorderThickness = new Thickness(1), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
    b.SetResourceReference(Border.BackgroundProperty, "Blastlog.GameTint2"); b.SetResourceReference(Border.BorderBrushProperty, "Blastlog.GameEdge");
    return b;
  }

  // ---- Friends lately (your own dashboard only, hidden with no friends; shows once the server sends it) ----
  static readonly Dictionary<string, string> Verbs = new Dictionary<string, string> { ["started"] = "started playing", ["finished"] = "finished", ["quit"] = "quit", ["dnf"] = "put down", ["rated"] = "rated", ["reviewed"] = "reviewed" };
  public static FrameworkElement FriendsLately(BlastlogPlugin plugin, JObject activity) {
    if (activity == null || (bool?)activity["hasFriends"] != true) return null;
    var days = (int?)activity["days"] ?? 30; var items = Dash.Arr(activity["items"]).OfType<JObject>().ToList();
    var panel = new StackPanel();
    var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 14) };
    head.Children.Add(Dash.Row(0, Dash.Text("Friends lately", "Blastlog.CardTitle").Margin(0, 0, 14, 0).Also(t => t.VerticalAlignment = VerticalAlignment.Center), Dash.Text($"Last {days} days", "Blastlog.Muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center)));
    var friends = Dash.Link("Your friends →", () => Dash.Site(plugin, "/friends")); DockPanel.SetDock(friends, Dock.Right); head.Children.Add(friends);
    panel.Children.Add(head);
    if (items.Count == 0) { panel.Children.Add(Dash.Text($"Nothing new from your friends in the last {days} days.", "Blastlog.Muted")); return Dash.Card(panel); }
    var list = new UniformGrid { Columns = 2 };
    var choices = Dash.Arr(activity["reactionChoices"]).Select(c => (string)c).ToList();
    var all = false;
    var more = Dash.Link("", () => { });
    void Fill() {
      list.Children.Clear();
      foreach (var item in all ? items : items.Take(6).ToList()) list.Children.Add(ActivityRow(plugin, item, choices));
      more.Content = all ? "Show fewer" : $"Show {items.Count - 6} more";
    }
    more.Click += (s, e) => { try { all = !all; Fill(); } catch (Exception) { } };
    Fill();
    list.SizeChanged += (s, e) => { try { list.Columns = e.NewSize.Width >= 900 ? 2 : 1; } catch (Exception) { } };
    panel.Children.Add(list);
    more.HorizontalAlignment = HorizontalAlignment.Left;
    if (items.Count > 6) panel.Children.Add(more.Margin(0, 6, 0, 0));
    return Dash.Card(panel);
  }
  static FrameworkElement ActivityRow(BlastlogPlugin plugin, JObject item, List<string> choices) {
    var friend = item["friend"] as JObject; var name = (string)friend?["displayName"] ?? ""; var gameTitle = (string)item["game"]?["title"] ?? "";
    var grid = new Grid { Margin = new Thickness(0, 0, 18, 6), MinHeight = 56 };
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    var face = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 4, 12, 0), VerticalAlignment = VerticalAlignment.Top,
      Child = Dash.Text(name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", "Blastlog.Body", "Blastlog.Text", FontWeights.Bold).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }) };
    face.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2");
    grid.Children.Add(face);
    var body = new StackPanel { Margin = new Thickness(0, 2, 12, 8) };
    var line = new TextBlock { Style = Kit.Style("Blastlog.Body") };
    line.Inlines.Add(new Run(name) { FontWeight = FontWeights.Bold }); line.Inlines.Add(new Run(" " + (Verbs.TryGetValue((string)item["verb"] ?? "", out var v) ? v : (string)item["verb"]) + " "));
    line.Inlines.Add(new Run(gameTitle) { FontWeight = FontWeights.Bold });
    body.Children.Add(line);
    var meta = new WrapPanel();
    var rating = (double?)item["rating"];
    if (rating != null) meta.Children.Add(Dash.Text("★" + rating.Value.ToString("0.#", CultureInfo.InvariantCulture), "Blastlog.Small", "Blastlog.Star", FontWeights.Bold).Margin(0, 0, 10, 0));
    if ((bool?)item["reviewed"] == true && (string)item["verb"] != "reviewed") meta.Children.Add(Dash.Text("Wrote a review", "Blastlog.Small").Margin(0, 0, 10, 0));
    var at = (DateTimeOffset?)item["at"]; if (at != null) meta.Children.Add(Dash.Text(Dash.When(at.Value), "Blastlog.Small"));
    body.Children.Add(meta);
    var excerpt = (string)item["excerpt"];
    if (!string.IsNullOrEmpty(excerpt)) body.Children.Add(Dash.Text("“" + excerpt + "”", "Blastlog.Muted").Also(t => { t.MaxHeight = 44; t.TextTrimming = TextTrimming.WordEllipsis; t.Margin = new Thickness(0, 4, 0, 0); }));
    var entryId = (string)item["entryId"];
    if (choices.Count > 0 && entryId != null) body.Children.Add(Reactions(plugin, entryId, $"{name}'s {gameTitle}", choices, Dash.Arr(item["reactions"]).OfType<JObject>().ToList()));
    Grid.SetColumn(body, 1); grid.Children.Add(body);
    var cover = Kit.Cover(plugin, Dash.GameFromCover((string)item["game"]?["id"], gameTitle, (string)item["game"]?["coverUrl"]), 40, ArtworkContext.Thumbnail, 6);
    cover.VerticalAlignment = VerticalAlignment.Top; cover.Margin = new Thickness(0, 4, 0, 0);
    Grid.SetColumn(cover, 2); grid.Children.Add(cover);
    Dash.OnClick(grid, () => Dash.Site(plugin, "/library/" + entryId));
    grid.Background = Brushes.Transparent; grid.ToolTip = $"{name}'s {gameTitle}";
    return grid;
  }

  /// <summary>The emoji reactions under a friend's line (web Reactions.tsx): each choice a 44px button, yours highlighted.</summary>
  static FrameworkElement Reactions(BlastlogPlugin plugin, string entryId, string about, List<string> choices, List<JObject> reactions) {
    var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
    void Fill() {
      row.Children.Clear();
      foreach (var emoji in choices) {
        var r = reactions.FirstOrDefault(x => (string)x["emoji"] == emoji);
        var count = (int?)r?["count"] ?? 0; var mine = (bool?)r?["mine"] == true;
        var names = Dash.Arr(r?["names"]).Select(n => (string)n).ToList();
        var e = emoji;
        var b = Kit.Button(count > 0 ? $"{emoji} {count}" : emoji, async () => {
          var answer = await plugin.Write($"entries/{entryId}/reactions", new { emoji = e, on = !mine });
          if (answer?["reactions"] is JArray updated) { reactions = updated.OfType<JObject>().ToList(); Fill(); }
        }, "Blastlog.Ghost");
        b.MinHeight = 44; b.MinWidth = 52; b.Padding = new Thickness(12, 0, 12, 0); b.Margin = new Thickness(0, 0, 6, 6); b.FontSize = 15;
        b.SetResourceReference(Control.BorderBrushProperty, mine ? "Blastlog.GameEdge" : "Blastlog.Line");
        if (mine) b.SetResourceReference(Control.BackgroundProperty, "Blastlog.GameTint2");
        if (names.Count > 0) b.ToolTip = string.Join(", ", names);
        Dash.AutomationName(b, $"{(mine ? "Take back" : "React with")} {emoji} on {about}{(count > 0 ? $", {count} so far" : "")}");
        row.Children.Add(b);
      }
    }
    Fill();
    return row;
  }
}
}
