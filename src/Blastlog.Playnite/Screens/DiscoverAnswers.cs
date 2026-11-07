using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace Blastlog.Playnite.Screens {
/// <summary>What an answer on Discover did, for the result line: its Undo (or a comfort pick's), and the entry it made.</summary>
sealed class DiscoverAnswered {
  public string Text; public string UndoId; public string EntryId; public bool Played; public string ReplayPickId; public string ReplayTitle;
}

/// <summary>
/// The answers on a Discover card (apps/web DiscoverActions.tsx): Want to play, I own it (with where, optional), Played it
/// (Finished it / Playing now / Stopped, then optional stars) and Not for me, which first asks Why not? (optional; its
/// picks are Fine-tune stances). None of them teach taste except those stances. Each reports back so the page offers Undo.
/// </summary>
sealed class DiscoverAnswers : ContentControl {
  static readonly ILogger Log = LogManager.GetLogger();
  static readonly string[] Stores = { "steam", "gog", "epic", "playstation", "xbox", "nintendo", "physical", "other" };
  static readonly Dictionary<string, string> StoreName = new Dictionary<string, string> { ["steam"] = "Steam", ["gog"] = "GOG", ["epic"] = "Epic Games",
    ["playstation"] = "PlayStation", ["xbox"] = "Xbox", ["nintendo"] = "Nintendo", ["physical"] = "Disc or cartridge", ["other"] = "Somewhere else" };
  readonly BlastlogPlugin plugin; readonly string gameId, title, shelf; readonly bool notOut;
  readonly Func<DiscoverAnswered, Task> onAnswered;
  string ended = "done"; int? rating; readonly HashSet<string> owned = new HashSet<string>();

  public DiscoverAnswers(BlastlogPlugin plugin, string gameId, string title, string shelf, bool notOut, Func<DiscoverAnswered, Task> onAnswered) {
    this.plugin = plugin; this.gameId = gameId; this.title = title; this.shelf = shelf; this.notOut = notOut; this.onAnswered = onAnswered;
    Focusable = false; ShowDefault();
  }

  void ShowDefault() {
    var panel = new StackPanel();
    var want = PnKit.Btn(PnKit.IconWord(PnKit.Heart, "Want to play"), () => Answer("want", new JObject()), "Blastlog.Primary", "Want to play " + title);
    want.HorizontalAlignment = HorizontalAlignment.Stretch; want.Margin = new Thickness(0, 0, 0, 6);
    panel.Children.Add(want);
    var more = new WrapPanel();
    // A game that isn't out yet can't be owned or played.
    if (!notOut) more.Children.Add(Small(Kit.Button("I own it", ShowOwned, "Blastlog.Ghost")));
    if (!notOut) more.Children.Add(Small(PnKit.Btn(PnKit.IconWord(PnKit.Check, "Played it"), () => { ShowPlayed(); return Task.CompletedTask; }, "Blastlog.Ghost", "Played it")));
    more.Children.Add(Small(PnKit.Btn(PnKit.IconWord(PnKit.X, "Not for me"), NotForMe, "Blastlog.TextButton", "Not for me")));
    panel.Children.Add(more);
    Content = panel;
  }
  static Button Small(Button b) { b.Margin = new Thickness(0, 0, 6, 6); b.Padding = new Thickness(14, 0, 14, 0); return b; }

  /// <summary>A step inside the card (how far, stars, where you own it) in the game's tint.</summary>
  void Step(string question, string hint, UIElement middle, params Button[] buttons) {
    var panel = new StackPanel();
    var q = PnKit.Line("Blastlog.Body", PnKit.Run(question, null, System.Windows.FontWeights.SemiBold));
    if (hint != null) q.Inlines.Add(PnKit.Run(" " + hint, "Blastlog.TextMuted"));
    q.FontSize = 15; q.Margin = new Thickness(0, 0, 0, 8); panel.Children.Add(q);
    if (middle != null) panel.Children.Add(middle);
    var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
    foreach (var b in buttons) row.Children.Add(Small(b));
    panel.Children.Add(row);
    Content = new Border { Child = panel, CornerRadius = new CornerRadius(14), Padding = new Thickness(12), BorderThickness = new Thickness(1) }
      .Res(Border.BackgroundProperty, "Blastlog.GameTint2").Res(Border.BorderBrushProperty, "Blastlog.GameEdge");
  }

  void ShowPlayed() => Step("How far did you get?", null, null,
    Kit.Button("Finished it", () => { ended = "done"; ShowStars(); }),
    Kit.Button("Playing now", async () => await Answer("played", new JObject { ["status"] = "playing" }, "playing"), "Blastlog.Ghost"),
    Kit.Button("Stopped", () => { ended = "quit"; ShowStars(); }, "Blastlog.Ghost"),
    Kit.Button("Cancel", ShowDefault, "Blastlog.TextButton"));

  void ShowStars() {
    var stars = new WrapPanel();
    for (var n = 1; n <= 5; n++) {
      var value = n;
      var b = Kit.Button(n + "★", () => { rating = rating == value ? (int?)null : value; ShowStars(); }, rating == n ? "Blastlog.Primary" : "Blastlog.Ghost");
      b.Margin = new Thickness(0, 0, 6, 6); b.Padding = new Thickness(12, 0, 12, 0);
      AutomationProperties.SetItemStatus(b, rating == n ? "Selected" : "");
      stars.Children.Add(b);
    }
    AutomationProperties.SetName(stars, "Stars");
    Step("Stars", "(optional)", stars,
      Kit.Button("Save", async () => { var extra = new JObject { ["status"] = ended }; if (rating != null) extra["rating"] = rating; await Answer("played", extra, ended); }),
      Kit.Button("Back", () => { rating = null; ShowPlayed(); }, "Blastlog.TextButton"));
  }

  void ShowOwned() {
    var stores = new WrapPanel();
    foreach (var store in Stores) {
      var key = store; var on = owned.Contains(store);
      var b = Kit.Button(StoreName[store], () => { if (!owned.Remove(key)) owned.Add(key); ShowOwned(); }, on ? "Blastlog.Primary" : "Blastlog.Ghost");
      b.Margin = new Thickness(0, 0, 6, 6); b.Padding = new Thickness(12, 0, 12, 0); b.MinHeight = 44;
      AutomationProperties.SetItemStatus(b, on ? "Checked" : "Unchecked");
      stores.Children.Add(b);
    }
    Step("Where do you own it?", "(optional)", stores,
      Kit.Button("Add to backlog", async () => await Answer("owned", owned.Count > 0 ? new JObject { ["stores"] = new JArray(Stores.Where(owned.Contains)) } : new JObject())),
      Kit.Button("Cancel", () => { owned.Clear(); ShowDefault(); }, "Blastlog.TextButton"));
  }

  async Task NotForMe() {
    var choices = await DiscoverWhyNot.Ask(plugin, gameId, title);
    if (choices == null) return;
    await Answer("not_for_me", new JObject(), null, choices);
  }

  /// <summary>" (on Steam and somewhere else)", " (somewhere else)", or nothing.</summary>
  static string OwnedWhere(IEnumerable<string> stores) {
    var list = stores.ToList();
    var named = list.Where(s => s != "other").Select(s => StoreName[s]).ToList();
    var parts = list.Contains("other") ? named.Concat(new[] { "somewhere else" }).ToList() : named;
    if (parts.Count == 0) return "";
    var text = parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts.Last();
    return named.Count > 0 ? $" (on {text})" : $" ({text})";
  }

  async Task Answer(string value, JObject extra, string status = null, List<DiscoverWhyNot.Choice> whyNot = null) {
    try {
      var body = new JObject { ["gameId"] = gameId, ["answer"] = value, ["shelf"] = shelf }; body.Merge(extra);
      var saved = await plugin.Write("discover/answers", body);
      if (saved == null) return;
      // Why not? picks are Fine-tune stances; one that fails is counted and reported, not retried (the site's rule).
      var said = new List<string>(); var failed = 0;
      foreach (var c in whyNot ?? new List<DiscoverWhyNot.Choice>()) {
        var ok = await plugin.WriteQuietly("taste/corrections", new JObject { ["characteristicId"] = c.TraitId, ["stance"] = c.Stance, ["note"] = c.Note ?? $"Said on Discover: not for me on {title}" });
        if (ok != null) said.Add($"{c.Label} → {DiscoverWhyNot.StanceLabel(c.Stance)}"); else failed++;
      }
      var stores = (extra["stores"] as JArray)?.Select(s => (string)s) ?? Enumerable.Empty<string>();
      var text = value == "want" ? $"{title} is on your wishlist." : value == "owned" ? $"{title} is in your backlog{OwnedWhere(stores)}."
        : value == "played" ? (status == "playing" ? $"{title} is in Currently playing." : $"{title} is in your library as {(status == "quit" ? "stopped" : "finished")}{(extra["rating"] != null ? $" with {extra["rating"]}★" : "")}.")
        : $"{title} won’t show here again.{(said.Count > 0 ? $" Fine-tune: {string.Join(", ", said)}." : "")}";
      if (failed > 0) text += $" {(failed == 1 ? "One trait" : failed + " traits")} didn’t save. Set {(failed == 1 ? "it" : "them")} in Your Taste → Fine-tune.";
      rating = null; owned.Clear();
      await onAnswered(new DiscoverAnswered { Text = text, UndoId = saved.S("id"), EntryId = saved.S("entryId"), Played = value == "played" && status != "playing" });
    } catch (Exception e) { Log.Error(e, "Blastlog Discover answer"); }
  }
}

/// <summary>
/// Why not? after Not for me (apps/web DiscoverWhyNot.tsx): optional; each of the game's traits can be left, or set to
/// Not for me / Avoid / Never, ordinary Fine-tune stances. Hide it with nothing picked just hides the game.
/// </summary>
static class DiscoverWhyNot {
  static readonly ILogger Log = LogManager.GetLogger();
  public sealed class Choice { public string TraitId, Label, Stance, Note; }
  static readonly string[][] Options = { new[] { "leave", "Leave it" }, new[] { "not_for_me", "Not for me" }, new[] { "avoid", "Avoid" }, new[] { "dealbreaker", "Never" } };
  static readonly HashSet<string> Negative = new HashSet<string> { "not_for_me", "avoid", "dealbreaker" };
  public static string StanceLabel(string stance) => stance == "dealbreaker" ? "Never" : stance == "avoid" ? "Avoid" : stance == "not_for_me" ? "Not for me"
    : stance == "neutral" ? "Doesn’t sway me" : stance == "like" ? "Like" : stance == "love" ? "Love" : stance == "instant_yes" ? "Instant yes" : stance;

  /// <summary>The picked stances (empty: just hide it), or null when cancelled.</summary>
  public static async Task<List<Choice>> Ask(BlastlogPlugin plugin, string gameId, string title) {
    List<Choice> result = null;
    var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = false, ShowMinimizeButton = false });
    window.Title = $"Why not {title}?"; window.Width = 620; window.Height = 640; window.Owner = plugin.Api.Dialogs.GetCurrentAppWindow(); window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    var picked = new Dictionary<string, string>();
    var root = new DockPanel { Margin = new Thickness(24) };
    var page = new Border { Child = root }; page.Resources.MergedDictionaries.Add(Kit.Styles); page.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface");
    var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top);
    top.Children.Add(PnKit.Text($"Why not {title}?", "Blastlog.Section"));
    top.Children.Add(PnKit.Text("Optional. Pick what puts you off and Blastlog counts it against games like this, the same as Your Taste → Fine-tune (change it there any time). Or just hide it.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 0, 0, 14)));
    root.Children.Add(top);
    var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom);
    var traits = new List<JObject>();
    List<Choice> Choices() => traits.Select(t => new { t, pick = picked.TryGetValue(t.S("id"), out var p) ? p : null })
      .Where(x => x.pick != null && x.pick != "leave" && x.pick != x.t.S("stance"))
      .Select(x => new Choice { TraitId = x.t.S("id"), Label = x.t.S("label"), Stance = x.pick, Note = x.t.S("note") }).ToList();
    var hide = Kit.Button("Hide it", () => { result = Choices(); window.Close(); });
    void Relabel() { var n = Choices().Count; hide.Content = n == 0 ? "Hide it" : $"Hide it and save {(n == 1 ? "1 trait" : n + " traits")}"; }
    buttons.Children.Add(hide);
    buttons.Children.Add(Kit.Button("Cancel", () => { result = null; window.Close(); }, "Blastlog.TextButton"));
    root.Children.Add(buttons);
    var list = new StackPanel();
    list.Children.Add(PnKit.Text("Loading its traits…", "Blastlog.Muted"));
    root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
    window.Content = page;
    window.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { result = null; window.Close(); } };

    async void Fill() {
      try {
        var data = (await plugin.Mirror($"discover/games/{gameId}/why-not", fresh: true)).Value;
        list.Children.Clear();
        if (data == null) { list.Children.Add(PnKit.Text("Its traits didn’t load. You can still hide it.", "Blastlog.Body", "Blastlog.Danger")); return; }
        traits = data.Objects("traits");
        if (traits.Count == 0) { list.Children.Add(PnKit.Text("Blastlog doesn’t know enough about this game yet to ask.", "Blastlog.Muted")); return; }
        foreach (var trait in traits) list.Children.Add(Trait(trait, picked, Relabel));
      } catch (Exception e) { Log.Error(e, "Blastlog Why not"); }
    }
    Fill();
    window.ShowDialog();
    await Task.Yield();
    return result;
  }

  static FrameworkElement Trait(JObject trait, Dictionary<string, string> picked, Action changed) {
    var id = trait.S("id"); var stance = trait.S("stance");
    if (!picked.ContainsKey(id)) picked[id] = stance != null && Negative.Contains(stance) ? stance : "leave";
    var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
    var legend = PnKit.Line("Blastlog.Body", PnKit.Run(trait.S("label"), null, FontWeights.Bold));
    if (stance != null && !Negative.Contains(stance)) legend.Inlines.Add(PnKit.Run($" · now {StanceLabel(stance)} in Fine-tune", "Blastlog.TextMuted"));
    panel.Children.Add(legend);
    if (trait.S("definition") != null) panel.Children.Add(PnKit.Text(trait.S("definition"), "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 2, 0, 0)));
    var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
    AutomationProperties.SetName(options, trait.S("label"));
    void Draw() {
      options.Children.Clear();
      foreach (var o in Options) {
        var key = o[0]; var on = picked[id] == key;
        var b = Kit.Button(o[1], () => { picked[id] = key; Draw(); changed(); }, on ? "Blastlog.Primary" : "Blastlog.Ghost");
        b.Margin = new Thickness(0, 0, 6, 6); b.Padding = new Thickness(14, 0, 14, 0);
        AutomationProperties.SetItemStatus(b, on ? "Selected" : "");
        options.Children.Add(b);
      }
    }
    Draw();
    panel.Children.Add(options);
    return panel;
  }
}
}
