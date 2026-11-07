using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Sharpen your picks (apps/web SharpenPicks.tsx) beside Play next's picks 2 and 3: the taste level with its meter, the
/// games that would help most (stars in place, or Rate &amp; review on Blastlog's review form), and what just happened.
/// </summary>
static class PlayNextSharpen {
  static string message;

  public static FrameworkElement Build(BlastlogPlugin plugin, JObject help, Action reload) {
    if (help == null || help["knowledge"] == null) return null;
    var knowledge = help["knowledge"]; var counts = help["counts"] as JObject;
    var panel = new StackPanel();
    var head = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
    head.Children.Add(PnKit.Text("Sharpen your picks", "Blastlog.Section").Also(t => { t.Margin = new Thickness(0, 0, 12, 0); t.VerticalAlignment = VerticalAlignment.Center; }));
    var waiting = string.Join(" · ", new[] {
      (counts.D("unrated") ?? 0) > 0 ? $"{counts.D("unrated")} finished game{((counts.D("unrated") ?? 0) == 1 ? "" : "s")} without a rating" : null,
      (counts.D("unreviewed") ?? 0) > 0 ? $"{counts.D("unreviewed")} rated without a word" : null }.Where(x => x != null));
    if (waiting.Length > 0) head.Children.Add(PnKit.Text(waiting, "Blastlog.Small").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    panel.Children.Add(head);
    panel.Children.Add(Level(knowledge));
    if (message != null) panel.Children.Add(PnKit.Text(message, "Blastlog.Body", "Blastlog.Ok", FontWeights.SemiBold).Also(t => { t.FontSize = 15; t.Margin = new Thickness(0, 10, 0, 0); }));
    var actions = help.Objects("actions");
    if (actions.Count == 0) panel.Children.Add(PnKit.Text("Nothing to add right now. Finish something and rate it, and it’ll show up here.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 12, 0, 0)));
    foreach (var action in actions) panel.Children.Add(Action(plugin, action, reload));
    var card = Kit.Card(panel); card.Margin = new Thickness(0);
    AutomationProperties.SetName(card, "Sharpen your picks");
    return card;
  }

  static string Pct1(double? v) => v == null ? "—" : (v.Value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

  static FrameworkElement Level(JToken k) {
    var level = (int)(k.D("level") ?? 0); var accuracy = k.D("accuracy"); var next = k.D("nextLevelAt"); var previous = k.D("previousLevel");
    var floor = 0.5 + (level - 1) * 0.025;
    var progress = accuracy == null || next == null ? 1 : Math.Max(0, Math.Min(1, (accuracy.Value - floor) / (next.Value - floor)));
    var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
    var number = new StackPanel { Margin = new Thickness(0, 0, 16, 0), MinWidth = 64 };
    number.Children.Add(PnKit.Text("Level", "Blastlog.Small").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
    number.Children.Add(PnKit.Text(level == 0 ? "–" : level.ToString(CultureInfo.InvariantCulture), "Blastlog.Body", "Blastlog.GameText", FontWeights.ExtraBold).Also(t => { t.FontSize = 34; t.HorizontalAlignment = HorizontalAlignment.Center; }));
    number.Children.Add(PnKit.Text("of " + (int)(k.D("maxLevel") ?? 0), "Blastlog.Small").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
    var box = new Border { Child = number, CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top }
      .Res(Border.BackgroundProperty, "Blastlog.Surface2");
    DockPanel.SetDock(box, Dock.Left); row.Children.Add(box);
    var words = new StackPanel();
    var text = PnKit.Line("Blastlog.Body", PnKit.Run("How well Blastlog knows your taste. ", null, FontWeights.Bold),
      PnKit.Run($"It guesses which of two games you’d rate higher {Pct1(accuracy)} of the time" + (previous != null ? $" ({(previous < level ? "up" : "down")} from level {previous})" : "")
        + ". Level 1 is a coin flip. It only goes up when what you add actually helps."));
    text.FontSize = 15; words.Children.Add(text);
    var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 10, 0, 6) }.Res(Border.BackgroundProperty, "Blastlog.GameBar");
    var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left }.Res(Border.BackgroundProperty, "Blastlog.Game");
    track.Child = fill; track.SizeChanged += (s, e) => fill.Width = Math.Round(track.ActualWidth * progress);
    AutomationProperties.SetName(track, $"Progress to level {level + 1}");
    words.Children.Add(track);
    if (next != null) words.Children.Add(PnKit.Text($"Level {level + 1} at {Pct1(next)}.", "Blastlog.Small"));
    row.Children.Add(words);
    return row;
  }

  static FrameworkElement Action(BlastlogPlugin plugin, JObject action, Action reload) {
    var row = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
    var art = PnKit.Art(plugin, action.S("gameId"), action.S("coverUrl"), new CornerRadius(10), 56); art.Margin = new Thickness(0, 0, 14, 0); art.VerticalAlignment = VerticalAlignment.Top;
    DockPanel.SetDock(art, Dock.Left); row.Children.Add(art);
    var body = new StackPanel();
    var title = action.S("title"); var entryId = action.S("entryId");
    body.Children.Add(PnKit.Text(title, "Blastlog.Body", null, FontWeights.Bold));
    void Review() => UiKit.Open(plugin.ServerUrl + "/library/" + entryId + "/edit");
    if (action.S("kind") == "rate") {
      var impact = (int)(action.D("impact") ?? 0);
      body.Children.Add(PnKit.Text($"{(action.S("status") == "done" ? "Finished" : "Stopped")}, not rated yet.{(impact > 0 ? $" Rating it could change {impact} of your top 10 picks." : " Every rating helps.")}", "Blastlog.Body").Also(t => t.FontSize = 15));
      var stars = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
      for (var n = 1; n <= 5; n++) {
        var rating = n;
        var b = Kit.Button("★", async () => {
          var saved = await plugin.Write("entries/" + entryId, new JObject { ["expectedVersion"] = action["version"], ["rating"] = rating }, new HttpMethod("PATCH"));
          if (saved == null) return;
          message = $"Rated {title} {rating}★. Your picks are updating."; reload();
        }, "Blastlog.TextButton");
        b.SetResourceReference(Control.ForegroundProperty, "Blastlog.Star"); b.FontSize = 22; b.Margin = new Thickness(0); b.Padding = new Thickness(8, 0, 8, 0);
        AutomationProperties.SetName(b, $"{n} star{(n == 1 ? "" : "s")}");
        stars.Children.Add(b);
      }
      stars.Children.Add(Kit.Button("Rate & review ↗", Review, "Blastlog.Ghost").Also(b => b.Margin = new Thickness(8, 0, 0, 0)));
      body.Children.Add(stars);
    } else {
      body.Children.Add(PnKit.Line("Blastlog.Body", PnKit.Run(action.S("prompt") + " "), PnKit.Run("Blastlog knows the score, not the why; a few lines teach it what you care about.", "Blastlog.TextMuted")).Also(t => t.FontSize = 15));
      body.Children.Add(Kit.Button("Rate & review ↗", Review).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 8, 0, 0); }));
    }
    row.Children.Add(body);
    return row;
  }
}
}
