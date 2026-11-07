using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Currently playing (design.md 8 Dashboard; web NowPlaying.tsx): three games a page with a pager; each row has its
/// cover, title, time on the list and length, Guides ↗ and Reddit ↗, Play or Install when it's in Playnite, Still playing
/// when the check-in is due, Finished it, and Set aside with its two choices. Finishing opens the rate-and-review card
/// (Later, Oops, still playing). + Start another and the empty state's Pick from your backlog open the start picker.
/// </summary>
sealed class DashboardPlaying : Border {
  const int Limit = 3;
  sealed class Finishing { public JObject Entry; public string Status; public string PreviousFinishedOn; }
  // Kept across the dashboard's redraws (a change re-reads everything), like the site's component state.
  static readonly List<Finishing> finishing = new List<Finishing>();
  static string message; static int page; static string asideFor; static bool picking;

  readonly BlastlogPlugin plugin; readonly DashboardView view; readonly DashboardData data; readonly JObject suggestion;
  readonly StackPanel body = new StackPanel();
  string busy;

  public DashboardPlaying(BlastlogPlugin plugin, DashboardView view, DashboardData data, JObject suggestion) {
    this.plugin = plugin; this.view = view; this.data = data; this.suggestion = suggestion;
    Style = Kit.Style("Blastlog.Card");
    Child = body;
    Render();
  }

  List<JObject> Playing() {
    var done = new HashSet<string>(finishing.Select(f => (string)f.Entry["id"]));
    return Dash.Arr(data.NowPlaying?["items"]).OfType<JObject>().Where(i => !done.Contains((string)i["entryId"]) && data.Pages.ContainsKey((string)i["entryId"]))
      .Where(i => (string)data.Pages[(string)i["entryId"]]["entry"]?["status"] == "playing").ToList();
  }

  void Render() {
    body.Children.Clear();
    var playing = Playing();
    var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14), LastChildFill = false };
    var icon = Icons.Make(Icons.Gamepad, 20, null); icon.Margin = new Thickness(0, 0, 10, 0); icon.VerticalAlignment = VerticalAlignment.Center;
    ((System.Windows.Shapes.Shape)((Canvas)((Viewbox)icon).Child).Children[0]).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Blastlog.GameText");
    var title = Dash.Text("Currently playing", "Blastlog.CardTitle"); title.VerticalAlignment = VerticalAlignment.Center;
    head.Children.Add(Dash.Row(0, icon, title, playing.Count > 0 ? Dash.Badge(playing.Count) : null));
    body.Children.Add(head);
    if (message != null) body.Children.Add(Dash.Text(message, "Blastlog.Body", "Blastlog.GameText").Margin(0, 0, 0, 12));

    foreach (var f in finishing.ToList()) body.Children.Add(FinishCard(f));

    var pages = Math.Max(1, (int)Math.Ceiling(playing.Count / (double)Limit)); page = Math.Min(page, pages - 1);
    var shown = playing.Skip(page * Limit).Take(Limit).ToList();
    foreach (var item in shown) body.Children.Add(GameRow(item, (JObject)data.Pages[(string)item["entryId"]]["entry"]));

    if (playing.Count > 0 && (pages > 1 || !picking)) {
      var foot = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = false };
      if (!picking) foot.Children.Add(Dash.Link("+ Start another", () => { picking = true; Render(); }));
      if (pages > 1) {
        var pager = new StackPanel { Orientation = Orientation.Horizontal };
        pager.Children.Add(Dash.Text($"{page * Limit + 1}–{page * Limit + shown.Count} of {playing.Count}", "Blastlog.Small").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 10, 0); }));
        var prev = Kit.Button("‹", () => { page--; Render(); }, "Blastlog.Ghost"); prev.IsEnabled = page > 0; Dash.AutomationName(prev, "Previous games");
        var next = Kit.Button("›", () => { page++; Render(); }, "Blastlog.Ghost"); next.IsEnabled = page < pages - 1; Dash.AutomationName(next, "More games");
        foreach (var b in new[] { prev, next }) { b.Padding = new Thickness(0); b.Width = 44; b.Margin = new Thickness(0, 0, 6, 0); pager.Children.Add(b); }
        DockPanel.SetDock(pager, Dock.Right); foot.Children.Add(pager);
      }
      body.Children.Add(foot);
    }

    if (playing.Count == 0 && finishing.Count == 0) {
      var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 18) };
      empty.Children.Add(Dash.Text("Nothing on the go right now.", "Blastlog.Muted").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
      if (!picking) empty.Children.Add(Dash.Row(0,
        Kit.Button("Pick from your backlog", () => { picking = true; Render(); }),
        Kit.Button("+ Add a new game", () => Nav.AddGame(plugin, "playing"), "Blastlog.Ghost")).Margin(0, 14, 0, 0));
      body.Children.Add(empty);
    }
    if (picking) body.Children.Add(StartPicker());
  }

  // ---- One game ----
  FrameworkElement GameRow(JObject item, JObject entry) {
    var id = (string)entry["id"]; var game = Dash.Game(entry["game"]); var title = game?.Title ?? "";
    var due = (bool?)item["checkInDue"] == true; var working = busy == id;
    var row = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1) };
    row.SetResourceReference(BackgroundProperty, "Blastlog.GameTint"); row.SetResourceReference(BorderBrushProperty, due ? "Blastlog.GameEdge" : "Blastlog.GameTint2");
    Kit.GameScope(row, plugin, game);
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    var cover = Kit.Cover(plugin, game, 66, ArtworkContext.Thumbnail, 12); cover.VerticalAlignment = VerticalAlignment.Top; cover.Margin = new Thickness(0, 0, 14, 0);
    Dash.OnClick(cover, () => Dash.OpenEntry(plugin, id)); Dash.AutomationName(cover, "Open " + title); cover.Focusable = true;
    Grid.SetRowSpan(cover, 2); grid.Children.Add(cover);

    var main = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
    var name = Dash.Text(title, "Blastlog.CardTitle"); Dash.OnClick(name, () => Dash.OpenEntry(plugin, id)); name.ToolTip = "Open " + title;
    main.Children.Add(name);
    var facts = new List<string>(); var hours = Dash.LengthText(entry["length"] as JObject);
    facts.Add(OnListText(item)); if (hours != null) facts.Add(hours);
    main.Children.Add(Dash.Text(string.Join(" · ", facts), "Blastlog.Muted").Margin(0, 2, 0, 8));
    main.Children.Add(Dash.Wrap(
      Dash.LinkPill("Guides", "https://www.google.com/search?q=" + Uri.EscapeDataString(title + " guide"), $"Guides for {title} (opens your browser)").Margin(0, 0, 8, 0),
      Dash.LinkPill("Reddit", "https://www.reddit.com/search/?q=" + Uri.EscapeDataString(title), $"Reddit search for {title} (opens your browser)")));
    Grid.SetColumn(main, 1); grid.Children.Add(main);

    var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
    var tail = new StackPanel();
    if (asideFor == id) {
      // design.md 8: the same question and answers for every game, with an end or without.
      tail.Children.Add(Dash.Text("Back to your backlog to pick up later, or done with it?", "Blastlog.Body").Margin(0, 10, 0, 8));
      actions.Children.Add(Kit.Button("Back to backlog", () => SetAside(entry), "Blastlog.Ghost"));
      actions.Children.Add(Kit.Button("Done with it", () => Finish(entry, "dnf"), "Blastlog.Ghost"));
      actions.Children.Add(Kit.Button("Cancel", () => { asideFor = null; Render(); }, "Blastlog.TextButton"));
      tail.Children.Add(actions);
    } else {
      var play = Dash.PlayButton(plugin, id); if (play != null) actions.Children.Add(play);
      if (due) actions.Children.Add(Kit.Button("Still playing", () => StillPlaying(entry), play != null ? "Blastlog.Ghost" : "Blastlog.Primary"));
      actions.Children.Add(Kit.Button("Finished it", () => Finish(entry, "done"), "Blastlog.Ghost"));
      actions.Children.Add(Kit.Button("Set aside", () => { asideFor = id; Render(); }, "Blastlog.Ghost"));
      if (play == null) actions.Children.Add(Dash.Text("Not in Playnite", "Blastlog.Small").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(4, 0, 0, 8); }));
    }
    foreach (var b in actions.Children.OfType<Button>()) { b.Margin = new Thickness(0, 0, 8, 8); b.IsEnabled = !working; }
    if (asideFor != id) tail.Children.Add(actions);
    if (asideFor != id && due) tail.Children.Add(Dash.Text(AskText(item, entry), "Blastlog.Muted").Margin(0, 2, 0, 0));
    grid.Children.Add(tail);
    // Buttons on the right on a wide card, under the title on a narrow one.
    void Lay(bool wide) {
      Grid.SetRow(tail, wide ? 0 : 1); Grid.SetColumn(tail, wide ? 2 : 1); tail.Margin = new Thickness(wide ? 14 : 0, wide ? 0 : 10, 0, 0);
      tail.MaxWidth = wide ? 380 : double.PositiveInfinity; tail.VerticalAlignment = VerticalAlignment.Center;
    }
    var wideNow = false; Lay(false);
    grid.SizeChanged += (s, e) => { try { var w = e.NewSize.Width >= 640; if (w != wideNow) { wideNow = w; Lay(w); } } catch (Exception) { } };
    row.Child = grid;
    return row;
  }

  static string OnListText(JObject item) {
    var days = (int?)item["daysOnList"] ?? 0;
    return item["playingSince"]?.Type == JTokenType.Null || item["playingSince"] == null ? "Since before Blastlog" : days < 1 ? "Started today" : $"{Dash.Span(days)} in";
  }
  static readonly Dictionary<string, string> FinishWhat = new Dictionary<string, string> { ["main_story"] = "the main story", ["main_plus_extra"] = "the main story and extras", ["completionist"] = "everything" };
  /// <summary>"2 months in. Still playing? Most people finish the main story in about 30h." (NowPlaying.tsx askText)</summary>
  static string AskText(JObject item, JObject entry) {
    string lead;
    var last = (DateTimeOffset?)item["lastCheckIn"];
    if (last != null) lead = $"You last said so {Dash.Span((int)Math.Floor((DateTimeOffset.Now - last.Value).TotalDays))} ago.";
    else if (item["playingSince"] == null || item["playingSince"].Type == JTokenType.Null) lead = "On your list since before Blastlog.";
    else { var s = Dash.Span((int?)item["daysOnList"] ?? 0); lead = char.ToUpperInvariant(s[0]) + s.Substring(1) + " in."; }
    var length = entry["length"] as JObject; var h = Dash.Hours(length);
    var tailText = "";
    if (h != null) {
      var hours = h.Value < 10 ? Math.Round(h.Value * 2, MidpointRounding.AwayFromZero) / 2 : Math.Round(h.Value, MidpointRounding.AwayFromZero);
      FinishWhat.TryGetValue((string)length["fromLevel"] ?? "", out var what);
      tailText = $" Most people finish {what ?? "it"} in about {hours.ToString("0.#", CultureInfo.InvariantCulture)}h.";
    }
    return lead + " Still playing?" + tailText;
  }

  // ---- Changes (the site's own write routes, through the token) ----
  /// <summary>PATCH /entries/:id with the entry's current version (read fresh first). Null after telling the player why.</summary>
  async Task<JObject> Patch(string entryId, JObject changes) {
    var current = (await plugin.Mirror("entries/" + entryId, fresh: true)).Value;
    var version = (int?)current?["entry"]?["version"];
    if (version == null) { plugin.Api.Dialogs.ShowErrorMessage("Couldn't read that game from Blastlog just now. Try again in a moment.", "Blastlog"); return null; }
    changes["expectedVersion"] = version.Value;
    return await plugin.Write("entries/" + entryId, changes, new HttpMethod("PATCH"));
  }
  async Task Run(string entryId, Func<Task> work) {
    busy = entryId; message = null; Render();
    try { await work(); } finally { busy = null; }
    view.Refresh();
  }
  Task Finish(JObject entry, string status) => Run((string)entry["id"], async () => {
    var previous = (string)entry["finishedOn"];
    var updated = await Patch((string)entry["id"], new JObject { ["status"] = status, ["finishedOn"] = Dash.Today });
    if (updated == null) return;
    finishing.RemoveAll(f => (string)f.Entry["id"] == (string)entry["id"]);
    finishing.Add(new Finishing { Entry = updated.Type == JTokenType.Object && updated["id"] != null ? updated : entry, Status = status, PreviousFinishedOn = previous });
    asideFor = null;
  });
  Task SetAside(JObject entry) => Run((string)entry["id"], async () => {
    if (await Patch((string)entry["id"], new JObject { ["status"] = "started" }) == null) return;
    asideFor = null; message = $"{(string)entry["game"]?["title"]} is back in your backlog as Started.";
  });
  Task StillPlaying(JObject entry) => Run((string)entry["id"], async () => {
    if (await plugin.Write($"entries/{(string)entry["id"]}/still-playing", new { }) == null) return;
    message = $"Got it. Enjoy {(string)entry["game"]?["title"]}.";
  });
  Task Start(string entryId, string title) => Run(entryId, async () => {
    if (await Patch(entryId, new JObject { ["status"] = "playing" }) == null) return;
    picking = false; message = $"{title} is on your Playing list. Have fun.";
  });

  /// <summary>Just finished: the way into the full review, whose questions teach Blastlog; Oops puts it back.</summary>
  FrameworkElement FinishCard(Finishing item) {
    var entry = item.Entry; var id = (string)entry["id"]; var game = Dash.Game(entry["game"]);
    var judged = entry["rating"] != null && entry["rating"].Type != JTokenType.Null || !string.IsNullOrWhiteSpace((string)entry["review"]);
    var card = new Border { Style = Kit.Style("Blastlog.GameCard"), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 12) };
    Kit.GameScope(card, plugin, game);
    var panel = new StackPanel();
    var cover = Kit.Cover(plugin, game, 48, ArtworkContext.Thumbnail, 10); cover.Margin = new Thickness(0, 0, 12, 0);
    var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Dash.Text(game?.Title ?? "", "Blastlog.CardTitle"),
      Dash.Text(item.Status == "done" ? "Marked finished" : "Marked didn't finish", "Blastlog.Small") } };
    panel.Children.Add(Dash.Row(0, cover, names));
    panel.Children.Add(Dash.Text(item.Status == "done" ? "Nice! How was it?" : "How was it, as far as you got?", "Blastlog.Body", weight: FontWeights.SemiBold).Margin(0, 12, 0, 2));
    panel.Children.Add(Dash.Text("A few quick questions help Blastlog learn what you love and what loses you.", "Blastlog.Muted"));
    var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
    buttons.Children.Add(Kit.Button(judged ? "Update your review" : "Rate and review it", () => Dash.Review(plugin, id)));
    buttons.Children.Add(Kit.Button("Later", () => { finishing.Remove(item); Render(); }, "Blastlog.Ghost"));
    buttons.Children.Add(Kit.Button("Oops, still playing", () => Run(id, async () => {
      if (await Patch(id, new JObject { ["status"] = "playing", ["finishedOn"] = item.PreviousFinishedOn == null ? JValue.CreateNull() : (JToken)item.PreviousFinishedOn }) == null) return;
      finishing.Remove(item); message = $"{game?.Title} is back on your Playing list.";
    }), "Blastlog.TextButton"));
    foreach (FrameworkElement b in buttons.Children) b.Margin = new Thickness(0, 0, 8, 8);
    panel.Children.Add(buttons);
    card.Child = panel;
    return card;
  }

  /// <summary>Start something: your Next up pick first, then your backlog (games you've started first), or add a new one.</summary>
  FrameworkElement StartPicker() {
    var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
    var backlog = Dash.Arr(data.Backlog?["items"]).OfType<JObject>()
      .Where(e => ((string)e["status"] == "not_started" || (string)e["status"] == "started") && (string)e["ownership"] != "wishlist").ToList();
    var pickId = (string)suggestion?["id"];
    var pick = pickId != null ? backlog.FirstOrDefault(e => (string)e["entryId"] == pickId) : null;
    var suggestBox = new StackPanel();
    var list = new StackPanel();
    var box = Dash.Field();
    void Fill() {
      suggestBox.Children.Clear(); list.Children.Clear();
      var query = Fold(box.Text.Trim());
      if (query.Length == 0 && pick != null) {
        var t = (string)pick["title"];
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Dash.Text("Your Next up pick", "Blastlog.Small", "Blastlog.GameText", FontWeights.Bold), Dash.Text(t, "Blastlog.CardTitle") } });
        var start = Kit.Button("Start it", () => Start(pickId, t)); start.IsEnabled = busy == null; Grid.SetColumn(start, 1); row.Children.Add(start);
        suggestBox.Children.Add(row);
      }
      var matches = backlog.Where(e => (query.Length > 0 || (string)e["entryId"] != (string)pick?["entryId"]) && (query.Length == 0 || Fold((string)e["title"]).Contains(query)))
        .OrderByDescending(e => (string)e["status"] == "started").ThenBy(e => (string)e["title"], StringComparer.CurrentCultureIgnoreCase).Take(6).ToList();
      if (matches.Count == 0) list.Children.Add(Dash.Text(query.Length > 0 ? "Not in your backlog. Add it as a new game?" : "Your backlog is empty.", "Blastlog.Muted").Margin(0, 8, 0, 8));
      foreach (var e in matches) {
        var t = (string)e["title"]; var eid = (string)e["entryId"];
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Dash.Text(t, "Blastlog.Body", weight: FontWeights.SemiBold), Dash.Text(Dash.StatusWord((string)e["status"]), "Blastlog.Small") } });
        var start = Kit.Button("Start", () => Start(eid, t), "Blastlog.Ghost"); start.Margin = new Thickness(0); start.IsEnabled = busy == null;
        Grid.SetColumn(start, 1); row.Children.Add(start);
        list.Children.Add(row);
      }
    }
    box.TextChanged += (s, e) => { try { Fill(); } catch (Exception) { } };
    Fill();
    panel.Children.Add(suggestBox);
    panel.Children.Add(Dash.FieldBox(box, "Find a game in your backlog…").Margin(0, 0, 0, 8));
    Dash.AutomationName(box, "Find a game in your backlog");
    panel.Children.Add(list);
    panel.Children.Add(Dash.Row(0, Kit.Button("+ Add a new game", () => Nav.AddGame(plugin, "playing"), "Blastlog.Ghost"),
      Kit.Button("Cancel", () => { picking = false; Render(); }, "Blastlog.TextButton")).Margin(0, 10, 0, 0));
    return panel;
  }
  /// <summary>Case- and accent-insensitive text for matching titles (web search-text.ts fold).</summary>
  static string Fold(string text) {
    var d = (text ?? "").Normalize(System.Text.NormalizationForm.FormD);
    return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
  }
}
}
