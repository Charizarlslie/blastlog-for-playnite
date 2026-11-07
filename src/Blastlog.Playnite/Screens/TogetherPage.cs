using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Find a co-op game (apps/web Together.tsx, api together.ts): pick who's playing, how and how long, and see the games
/// you can play together, the ones someone already has first. Reached from the dashboard and Compare, not the menu.
/// Reads GET /sync/v1/friends and /sync/v1/compare/together (the site's own answers).
/// </summary>
static class TogetherPage {
  static readonly (string key, string label)[] Modes = { ("any", "Any way"), ("Co-op", "Co-op"), ("Versus", "Versus"), ("Couch", "Couch") };
  static readonly (string key, string label)[] Lengths = { ("any", "Any length"), ("quick", "Quick one"), ("long", "Long haul") };

  public static void Open(BlastlogPlugin plugin, IEnumerable<string> with) =>
    LinkPages.Show(plugin, "Find a co-op game", close => new View(plugin, with, close));

  sealed class View : StackPanel {
    readonly BlastlogPlugin plugin; readonly Action close; readonly List<string> asked;
    readonly StackPanel picker = new StackPanel(), results = new StackPanel();
    List<(string id, string name)> friends; List<string> picked = new List<string>();
    string mode = "any", length = "any"; bool played = true; JObject last; int asking;

    public View(BlastlogPlugin plugin, IEnumerable<string> with, Action close) {
      this.plugin = plugin; this.close = close; asked = with?.Where(id => !string.IsNullOrEmpty(id)).ToList() ?? new List<string>();
      Children.Add(LinkPages.Head("Game night", "Find a co-op game", "Pick who’s playing. Games one of you already has come first, then ones that would suit everyone."));
      Children.Add(picker); Children.Add(results);
      results.Children.Add(T("Loading your friends…", "Blastlog.Muted"));
      Run(Load, "together friends");
    }

    async Task Load() {
      var data = (await plugin.Mirror("friends", fresh: false)).Value;
      friends = (data?["friends"] as JArray ?? new JArray()).OfType<JObject>().Select(f => (S(f, "id"), S(f, "displayName") ?? "Someone")).Where(f => f.Item1 != null).ToList();
      if (data == null) { results.Children.Clear(); results.Children.Add(T("Couldn’t load your friends. Check your connection and try again.", "Blastlog.Muted")); return; }
      // Compare's Play together tab sends its people; with one friend, they're picked already.
      picked = asked.Where(id => friends.Any(f => f.id == id)).Take(7).ToList();
      if (picked.Count == 0 && friends.Count == 1) picked.Add(friends[0].id);
      Picker(); await Ask();
    }

    void Picker() {
      picker.Children.Clear();
      if (friends.Count == 0) {
        results.Children.Clear();
        picker.Children.Add(Row(T("Add a friend first:", "Blastlog.Body").Also(t => t.VerticalAlignment = VerticalAlignment.Center),
          Link("Friends", () => { close(); Application.Current?.Dispatcher.BeginInvoke(new Action(() => Nav.Friends(plugin))); })));
        return;
      }
      var people = Wrap(friends.Select(f => (UIElement)Toggle(f.name, picked.Contains(f.id), () => Pick(f.id))).ToArray());
      if (friends.Count > 1 && friends.Count <= 7 && picked.Count < friends.Count)
        people.Children.Add(Toggle("Everyone", false, () => { picked = friends.Select(f => f.id).ToList(); Changed(); }));
      people.Margin = new Thickness(0, 0, 0, 6);
      picker.Children.Add(people);
      picker.Children.Add(Wrap(Modes.Select(m => (UIElement)Toggle(m.label, mode == m.key, () => { mode = m.key; Changed(); })).ToArray()));
      picker.Children.Add(Wrap(Lengths.Select(l => (UIElement)Toggle(l.label, length == l.key, () => { length = l.key; Changed(); })).ToArray()));
      picker.Children.Add(LinkPages.Check("Include games someone has already played", played, on => { played = on; Changed(); }).Also(c => c.Margin = new Thickness(0, 0, 0, 18)));
    }

    void Pick(string id) {
      if (picked.Contains(id)) picked.Remove(id); else if (picked.Count < 7) picked.Add(id);
      Changed();
    }
    void Changed() { Picker(); Run(Ask, "together results"); }

    string Query() => "compare/together?ids=" + string.Join(",", picked) + "&mode=" + Uri.EscapeDataString(mode) + "&length=" + length + "&played=" + (played ? 1 : 0);

    async Task Ask() {
      if (friends == null || friends.Count == 0) return;
      if (picked.Count == 0) { last = null; results.Children.Clear(); results.Children.Add(T("Pick at least one friend.", "Blastlog.Muted")); return; }
      var mine = ++asking;
      // The last answer stays up, dimmed, until the new one comes (the site's keepPreviousData).
      if (last != null) results.Opacity = 0.6;
      else { results.Children.Clear(); results.Children.Add(T("Finding games you can play together…", "Blastlog.Muted")); }
      var data = (await plugin.Mirror(Query(), fresh: false)).Value;
      if (mine != asking) return;
      results.Opacity = 1;
      results.Children.Clear();
      if (data == null) { results.Children.Add(T("Couldn’t find games just now. Check your connection and try again.", "Blastlog.Muted")); return; }
      last = data;
      var games = (data["games"] as JArray ?? new JArray()).OfType<JObject>().ToList();
      if (games.Count == 0) { results.Children.Add(T("Nothing fits all of that. Try any way to play, any length, or games you’ve played.", "Blastlog.Muted")); return; }
      var players = (data["players"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(p => S(p, "id") ?? "", p => S(p, "displayName"));
      // The player is the one in the answer who isn't a picked friend: "You".
      string Name(string id) => id == null ? "Someone"
        : friends.Any(f => f.id == id) ? (players.TryGetValue(id, out var n) && n != null ? n : friends.First(f => f.id == id).name)
        : players.ContainsKey(id) ? "You" : "Someone";
      results.Children.Add(Columns(380, games.Select(g => Card(g, Name))));
    }

    UIElement Card(JObject game, Func<string, string> name) {
      var title = S(game, "title") ?? "";
      var text = Stack(new Thickness(14, 0, 0, 0), T(title, "Blastlog.Body", null, 16, FontWeights.Bold));
      var tags = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };
      foreach (var m in Strings(game["modes"])) tags.Children.Add(Kit.Chip(m, neutral: true));
      var len = PnKit.LengthText(game["length"]);
      if (len != null) tags.Children.Add(Kit.Chip(len, neutral: true));
      if (tags.Children.Count > 0) text.Children.Add(tags);
      var owners = Strings(game["owners"]); var needs = Strings(game["needs"]); var before = Strings(game["played"]);
      string Who(List<string> ids) => LinkPages.And(ids.Select(name).ToList());
      string Verb(List<string> ids, string one, string many) => ids.Count == 1 && name(ids[0]) != "You" ? one : many;
      var have = owners.Count > 0 ? $"{Who(owners)} {Verb(owners, "has", "have")} it" : "Nobody has it yet";
      if (owners.Count > 0 && needs.Count > 0) have += $" · {Who(needs)} {Verb(needs, "needs", "need")} it";
      text.Children.Add(T(have, "Blastlog.Small", "Blastlog.Text"));
      if (before.Count > 0) text.Children.Add(T($"{Who(before)} played it before", "Blastlog.Small"));
      var weakest = game["weakest"] as JObject;
      if (weakest != null && D(weakest, "predicted") != null) text.Children.Add(T($"Weakest fit: {name(S(weakest, "userId"))} ~{Stars(D(weakest, "predicted").Value)}", "Blastlog.Small", "Blastlog.Text"));
      var row = new DockPanel();
      var cover = Cover(plugin, S(game, "coverUrl"), title, 72, 10); cover.VerticalAlignment = VerticalAlignment.Top;
      DockPanel.SetDock(cover, Dock.Left); row.Children.Add(cover); row.Children.Add(text);
      var card = Box(row, "Blastlog.Surface", 16, new Thickness(12), "Blastlog.Line");
      var gameId = S(game, "gameId");
      var button = Plain(card, () => Nav.DiscoverGame(plugin, gameId), title);
      button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0);
      return button;
    }

    static List<string> Strings(JToken token) => (token as JArray)?.Select(v => v.Type == JTokenType.String ? (string)v : null).Where(v => !string.IsNullOrEmpty(v)).ToList() ?? new List<string>();
  }
}
}
