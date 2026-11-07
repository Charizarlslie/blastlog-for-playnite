using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Quick start (apps/web QuickStart.tsx, api quick-start.ts): tap the well-known games you've played and give them
/// stars. Ten gets you picks; after that it stops at every ten to say you're good to go, up to the most quick start
/// takes. A page of covers stays put while you rate; "Show me different games" moves on, and the games skipped there
/// aren't shown again. Each rating is a real library entry (Done, or Didn't finish), changeable or taken back here.
/// Reads GET /sync/v1/quick-start and writes through the site's quick start routes; when the server doesn't have them,
/// <see cref="Open"/> answers false and the caller opens the site's page.
/// </summary>
static class QuickStartPage {
  static readonly ILogger Log = LogManager.GetLogger();

  /// <summary>Opens Quick start in Playnite, or returns false (nothing shown) when it can't be read.</summary>
  public static async Task<bool> Open(BlastlogPlugin plugin, Action done) {
    if (!plugin.Configured) return false;
    var data = (await plugin.Mirror("quick-start", fresh: true)).Value;
    if (data == null || !(data["games"] is JArray)) return false;
    View view = null;
    LinkPages.Show(plugin, "Quick start", close => view = new View(plugin, data, close), () => { if (view?.Changed == true) done?.Invoke(); }, 1100, 820);
    return true;
  }

  sealed class Mine { public string AnswerId; public int Rating; public bool Finished; }

  sealed class View : StackPanel {
    readonly BlastlogPlugin plugin; readonly Action close;
    JObject data; List<JObject> page; readonly Dictionary<string, Mine> mine = new Dictionary<string, Mine>();
    string open, busy, error; bool unfinished, moving; int? checkpoint;
    public bool Changed { get; private set; }

    public View(BlastlogPlugin plugin, JObject data, Action close) {
      this.plugin = plugin; this.data = data; this.close = close;
      page = Games(data["games"]);
      MaxWidth = 1100; HorizontalAlignment = HorizontalAlignment.Left;
      Render();
    }

    static List<JObject> Games(JToken t) => (t as JArray ?? new JArray()).OfType<JObject>().Where(g => S(g, "gameId") != null).ToList();
    int Count => I(data, "count");
    int FirstGoal => Math.Max(1, I(data, "firstGoal"));
    int Round => Math.Max(1, I(data, "round"));
    int Max => Math.Max(1, I(data, "max"));
    int Goal => Count < FirstGoal ? FirstGoal : Math.Min(Max, (Count / Round + 1) * Round);
    bool Full => Count >= Max;

    void Render() {
      Children.Clear();
      Children.Add(LinkPages.Head("Quick start", "Rate games you’ve played",
        "Tap a game you’ve played and give it stars. Ten is enough for your first picks. Skip anything you haven’t played, and stop whenever you like."));
      Children.Add(Progress());
      if (error != null) Children.Add(LinkPages.Notice(error, false));
      if (checkpoint != null) Children.Add(Checkpoint(checkpoint.Value));
      else if (Full) Children.Add(Stop("All done here", $"You’ve rated {Count} games with quick start. Rate more from your library whenever you finish something.", keepGoing: false));
      else if (page.Count == 0) Children.Add(Empty());
      else { Children.Add(Covers()); Children.Add(Foot()); }
      var rated = (data["rated"] as JArray ?? new JArray()).OfType<JObject>().ToList();
      if (rated.Count > 0) Children.Add(Disclosure($"Rated with quick start ({rated.Count})", () => Rated(rated)).Also(d => d.Margin = new Thickness(0, 24, 0, 0)));
    }

    UIElement Progress() {
      var row = new DockPanel { Margin = new Thickness(0, -6, 0, 22), LastChildFill = false };
      var words = Count < FirstGoal ? $"{Count} of {FirstGoal}" : Full ? $"{Count} rated" : $"{Count} rated · next stop {Goal}";
      var label = T(words, "Blastlog.Body", null, 15, FontWeights.Bold); label.Margin = new Thickness(14, 0, 0, 0); label.VerticalAlignment = VerticalAlignment.Center;
      var right = Row(label);
      if (Count >= FirstGoal) right.Children.Add(Link("See my picks", SeePicks).Also(b => b.Margin = new Thickness(8, 0, 0, 0)));
      var share = Math.Min(1, Count / (double)Goal);
      var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Width = 360 };
      DockPanel.SetDock(track, Dock.Left); DockPanel.SetDock(right, Dock.Left);
      track.SetResourceReference(Border.BackgroundProperty, "Blastlog.GameBar");
      var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
      fill.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game");
      track.Child = fill; track.SizeChanged += (s, e) => fill.Width = Math.Round(track.ActualWidth * share);
      AutomationProperties.SetName(track, words);
      row.Children.Add(track); row.Children.Add(right);
      return row;
    }

    UIElement Checkpoint(int at) {
      var title = at == FirstGoal ? $"That’s {at}. You’re good to go." : at >= Max ? $"That’s {at}. Plenty." : $"{at} rated. Nice.";
      var text = at == FirstGoal ? "Blastlog knows enough to start picking for you. Every game you add from here makes the picks a little sharper, but you can stop right now."
        : at >= Max ? "That’s as far as quick start goes. Your picks have plenty to work with; rate more from your library whenever you finish something."
        : "Your picks keep getting sharper with each one. Stop whenever you like; nothing’s lost if you leave now.";
      return Stop(title, text, keepGoing: at < Max);
    }

    /// <summary>A stopping point: See my picks, Fill in your library, and Keep going while there's more.</summary>
    UIElement Stop(string title, string text, bool keepGoing) {
      var buttons = Wrap(Kit.Button("See my picks", SeePicks).Also(b => b.Margin = new Thickness(0, 0, 10, 10)),
        Kit.Button("Fill in your library ↗", () => Nav.Site(plugin, "/library/import/fill-in"), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 10, 10)));
      if (keepGoing) buttons.Children.Add(Kit.Button("Keep going", () => { checkpoint = null; Render(); }, "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 10, 10)));
      return Panel(title, text, buttons);
    }

    UIElement Empty() {
      var again = Kit.Button("Show the skipped ones again", StartOver, "Blastlog.Ghost"); again.IsEnabled = !moving; again.Margin = new Thickness(0, 0, 10, 10);
      return Panel("That’s every game for now",
        "You’ve been through all the well-known games quick start has. Add others with + New entry on your dashboard, or bring in your library.",
        Wrap(Kit.Button("Import my games ↗", () => Nav.Site(plugin, "/library/import")).Also(b => b.Margin = new Thickness(0, 0, 10, 10)), again));
    }

    static UIElement Panel(string title, string text, UIElement buttons) {
      var body = Stack(null, T(title, "Blastlog.Section"), T(text, "Blastlog.Body").Also(t => { t.Margin = new Thickness(0, 8, 0, 16); t.MaxWidth = 680; t.HorizontalAlignment = HorizontalAlignment.Left; }), buttons);
      var card = Kit.Card(body, game: true); card.Margin = new Thickness(0, 0, 0, 18); card.MaxWidth = 760; card.HorizontalAlignment = HorizontalAlignment.Left;
      return card;
    }

    UIElement Covers() {
      var wrap = new WrapPanel();
      foreach (var game in page) wrap.Children.Add(Card(game));
      return wrap;
    }

    UIElement Card(JObject game) {
      var id = S(game, "gameId"); var title = S(game, "title") ?? ""; var year = I(game, "year");
      mine.TryGetValue(id, out var rated); var isOpen = open == id;
      var art = new Grid();
      art.Children.Add(Cover(plugin, S(game, "coverUrl"), title, 150, 14));
      if (rated != null) art.Children.Add(PnKit.OverPill(new string('★', rated.Rating) + (rated.Finished ? "" : " · didn’t finish"), "Blastlog.Star", HorizontalAlignment.Left, VerticalAlignment.Bottom));
      var coverButton = Plain(art, () => { if (busy != null && busy != id) return; open = isOpen ? null : id; unfinished = rated != null && !rated.Finished; Render(); },
        rated != null ? $"{title}: {rated.Rating} stars. Change" : $"Rate {title}");
      coverButton.Margin = new Thickness(0); coverButton.HorizontalAlignment = HorizontalAlignment.Left;
      coverButton.IsEnabled = busy == null || busy == id;
      var name = T(title, "Blastlog.Small", "Blastlog.Text", 0, FontWeights.SemiBold); name.Inlines.Clear(); name.Inlines.Add(new System.Windows.Documents.Run(title));
      if (year > 0) name.Inlines.Add(new System.Windows.Documents.Run(" " + year.ToString(CultureInfo.InvariantCulture)).Also(r => r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.TextMuted")));
      name.MaxWidth = 150; name.Margin = new Thickness(0, 8, 0, 0); name.HorizontalAlignment = HorizontalAlignment.Left;
      var card = Stack(new Thickness(0, 0, 18, 22), coverButton, name);
      card.Width = isOpen ? 236 : 150;
      if (isOpen) card.Children.Add(Rate(game, rated));
      return card;
    }

    /// <summary>The open card's stars, Didn't finish it, and Take it back or Haven't played it.</summary>
    UIElement Rate(JObject game, Mine rated) {
      var id = S(game, "gameId"); var title = S(game, "title") ?? "";
      var stars = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
      for (var n = 1; n <= 5; n++) {
        var star = n; var on = rated != null && star <= rated.Rating;
        var b = Kit.Button("★", () => Rate(game, star), "Blastlog.TextButton");
        b.FontSize = 26; b.MinWidth = 44; b.MinHeight = 44; b.Padding = new Thickness(0); b.Margin = new Thickness(0, 0, 2, 0);
        b.SetResourceReference(Control.ForegroundProperty, on ? "Blastlog.Star" : "Blastlog.TextMuted");
        b.IsEnabled = busy != id;
        AutomationProperties.SetName(b, $"{star} star{(star > 1 ? "s" : "")}");
        stars.Children.Add(b);
      }
      AutomationProperties.SetName(stars, $"How was {title}?");
      var dnf = LinkPages.Check("Didn’t finish it", unfinished, v => unfinished = v);
      var after = rated != null ? Link("Take it back", () => Run(() => TakeBack(id, rated.AnswerId), "quick start take back")).Also(b => b.IsEnabled = busy != id)
        : Link("Haven’t played it", () => { open = null; Render(); });
      after.HorizontalAlignment = HorizontalAlignment.Left; after.Margin = new Thickness(-6, 0, 0, 0);
      return Box(Stack(null, stars, dnf, after), "Blastlog.Surface2", 14, new Thickness(10, 2, 10, 6), "Blastlog.Line").Also(b => b.Margin = new Thickness(0, 8, 0, 0));
    }

    UIElement Foot() {
      var row = Wrap();
      var more = Kit.Button(moving ? "Finding more…" : "Show me different games", NextPage, "Blastlog.Ghost"); more.IsEnabled = !moving; more.Margin = new Thickness(0, 0, 12, 10);
      row.Children.Add(more);
      if (Count > 0) row.Children.Add(Kit.Button(Count >= FirstGoal ? "I’m done, see my picks" : "I’m done for now", SeePicks, Count >= FirstGoal ? "Blastlog.Primary" : "Blastlog.TextButton").Also(b => b.Margin = new Thickness(0, 0, 12, 10)));
      row.Margin = new Thickness(0, 4, 0, 0);
      return row;
    }

    UIElement Rated(List<JObject> rated) {
      var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
      foreach (var item in rated) {
        var entryId = S(item, "entryId"); var rating = I(item, "rating");
        var stars = (rating > 0 ? new string('★', rating) : "") + (S(item, "status") == "dnf" ? " · didn’t finish" : "");
        var row = Row(Link(S(item, "title") ?? "", () => OpenEntry(entryId)), T(stars, "Blastlog.Small", "Blastlog.Star").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(6, 0, 0, 0); }));
        list.Children.Add(row);
      }
      list.Children.Add(T("Each one is in your library now. Open it to add a line about why, or to change anything.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 8, 0, 0)));
      return list;
    }

    /// <summary>A game in Playnite opens its page there; one that isn't opens it on Blastlog.</summary>
    void OpenEntry(string entryId) {
      if (entryId == null) return;
      var game = plugin.GameFor(entryId);
      if (game != null) { close(); plugin.Api.MainView.SwitchToLibraryView(); plugin.Api.MainView.SelectGame(game.Id); return; }
      Nav.Site(plugin, "/library/" + entryId);
    }

    void SeePicks() { close(); if (!ScreenJump.To("Discover")) Nav.Site(plugin, "/discover"); }

    /// <summary>Reads the counts and the rated list again; the page of covers stays put.</summary>
    async Task Reload(bool newPage) {
      var fresh = (await plugin.Mirror("quick-start", fresh: true)).Value;
      if (fresh == null) return;
      data = fresh;
      if (newPage) { page = Games(fresh["games"]); mine.Clear(); }
    }

    async Task Rate(JObject game, int rating) {
      var id = S(game, "gameId"); busy = id; error = null; Render();
      try {
        var before = Count; mine.TryGetValue(id, out var existing);
        if (existing != null) {
          var r = await plugin.Write("quick-start/answers/" + existing.AnswerId, new { rating, finished = !unfinished }, new HttpMethod("PATCH"));
          if (r == null) return;
          existing.Rating = rating; existing.Finished = !unfinished;
        } else {
          var r = await plugin.Write("quick-start/rate", new { gameId = id, rating, finished = !unfinished });
          if (r == null || S(r, "answerId") == null) return;
          mine[id] = new Mine { AnswerId = S(r, "answerId"), Rating = rating, Finished = !unfinished };
          var after = before + 1;
          // Every ten (and the last one): stop and say they can stop.
          if (after % Round == 0 || after == Max) checkpoint = after;
        }
        Changed = true; open = null; unfinished = false;
        await Reload(newPage: false);
      } catch (Exception e) { Log.Error(e, "Blastlog quick start rate"); error = "That didn’t save. Try again."; }
      finally { busy = null; Render(); }
    }

    async Task TakeBack(string gameId, string answerId) {
      busy = gameId; error = null; Render();
      try {
        var r = await plugin.Write("quick-start/answers/" + answerId, new { }, HttpMethod.Delete);
        if (r == null) return;
        mine.Remove(gameId); Changed = true; open = null;
        if (r["entryKept"]?.Type == JTokenType.Boolean && (bool)r["entryKept"]) error = "You’d changed that game since, so it stays in your library.";
        await Reload(newPage: false);
      } catch (Exception e) { Log.Error(e, "Blastlog quick start take back"); error = "That didn’t work. Try again."; }
      finally { busy = null; Render(); }
    }

    // Move on: the games left unrated on this page aren't offered again, and a fresh page comes.
    async Task NextPage() {
      moving = true; error = null; open = null; Render();
      try {
        var skipped = page.Select(g => S(g, "gameId")).Where(id => !mine.ContainsKey(id)).ToList();
        if (skipped.Count > 0 && await plugin.Write("quick-start/pass", new { gameIds = skipped }) == null) return;
        await Reload(newPage: true);
        Top();
      } catch (Exception e) { Log.Error(e, "Blastlog quick start next page"); error = "Couldn’t find more games. Try again."; }
      finally { moving = false; Render(); }
    }

    async Task StartOver() {
      moving = true; error = null; Render();
      try {
        if (await plugin.Write("quick-start/start-over", new { }) == null) return;
        await Reload(newPage: true);
      } catch (Exception e) { Log.Error(e, "Blastlog quick start start over"); error = "That didn’t work. Try again."; }
      finally { moving = false; Render(); }
    }

    void Top() {
      for (DependencyObject d = this; d != null; d = VisualTreeHelper.GetParent(d)) if (d is ScrollViewer s) { s.ScrollToTop(); return; }
    }
  }
}
}
