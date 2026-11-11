using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Next up (design.md 8 Dashboard; web App.tsx NextUpCard and Recommendations.tsx NextUpReasons): the whole cover, sharp,
/// on a blurred copy of itself under a scrim with the game's glow; Recommended / Quick one / Long haul; status on the cover; length,
/// platforms and where you own it; Why it's up next and one Watch for; a boost with Not now and Undo; the quote from your
/// own review; Play or Install when it's in Playnite, Start playing (a backlog game you own), View details, Add review
/// (Edit review once written). This is the big card only while nothing is being played. Otherwise Now playing has the top
/// (Charlie, 2026-11-08) and Next up is the medium card beside Your library (web App.tsx NextUpCompact): the modes, cover,
/// title, status and length, the one-line "predicted · because you like", Start playing and View details. In that case
/// the page wears the playing game, not this one.
/// </summary>
sealed class DashboardNextUp : Border {
  internal static string Mode = "recommended";
  sealed class Choice { public string Key, Label, Tip; }
  static readonly Choice[] Modes = {
    new Choice { Key = "recommended", Label = "Recommended" },
    new Choice { Key = "quick", Label = "Quick one", Tip = "Beatable in a weekend: the best match among shorter games" },
    new Choice { Key = "long", Label = "Long haul", Tip = "Something to sink hours into: the best match among long games" },
  };
  readonly BlastlogPlugin plugin; readonly DashboardView view;
  readonly bool compact; readonly Action<JObject> start;
  JObject data; JObject droppedBoost; int switchId;
  readonly Grid layers = new Grid();

  /// <param name="compact">The medium card beside Your library (while something is being played).</param>
  /// <param name="start">Puts a backlog pick on the Playing list (own dashboard); null hides Start playing.</param>
  public DashboardNextUp(BlastlogPlugin plugin, DashboardView view, JObject data, bool compact = false, Action<JObject> start = null) {
    this.plugin = plugin; this.view = view; this.data = data; this.compact = compact; this.start = start;
    if (compact) {
      Style = Kit.Style("Blastlog.Card"); Padding = new Thickness(22);
      Render();
      foreach (var m in Modes.Where(m => m.Key != Mode)) _ = plugin.Mirror($"next-up?mode={m.Key}", fresh: false);
      return;
    }
    CornerRadius = new CornerRadius(28); BorderThickness = new Thickness(1); Margin = new Thickness(0, 0, 0, 18); MinHeight = 420;
    SetResourceReference(BackgroundProperty, "Blastlog.Surface"); SetResourceReference(BorderBrushProperty, "Blastlog.GameEdge");
    Child = layers;
    // Children don't follow a Border's rounded corners: clip the art to them.
    layers.SizeChanged += (s, e) => { try { layers.Clip = new RectangleGeometry(new Rect(e.NewSize), 27, 27); } catch (Exception) { } };
    // The whole card opens the game, except its buttons (web: the article's onClick).
    Dash.OnClick(this, () => { var id = (string)(data?["entry"]?["id"]); if (id != null) Dash.OpenEntry(plugin, id); });
    Render();
    // The other two modes load in the background so switching is quick (the site prefetches them too).
    foreach (var m in Modes.Where(m => m.Key != Mode)) _ = plugin.Mirror($"next-up?mode={m.Key}", fresh: false);
  }

  void Switch(string mode) => Dash.Safe(async () => {
    if (mode == Mode) return;
    Mode = mode; var id = ++switchId;
    Render(); // the toggle moves at once; the game follows
    var r = await plugin.Mirror($"next-up?mode={mode}", fresh: false);
    if (id != switchId) return;
    data = r.Value; droppedBoost = null; Render();
  }, "next up mode");

  void Render() {
    var entry = data?["entry"] as JObject;
    var showing = (string)data?["mode"] ?? Mode;
    var game = Dash.Game(entry?["game"]);
    if (compact) { Child = Compact(entry, showing, game); return; }
    layers.Children.Clear();
    if (showing == Mode) view.Wear(game);
    DashHero.Backdrop(plugin, layers, game);

    var left = new StackPanel();
    left.Children.Add(DashHero.TopRow(DashHero.Label("Next up"), ModeBox()));
    var copy = new StackPanel();
    var entryId = (string)entry?["id"];
    copy.Children.Add(Dash.Text(entry != null ? (string)entry["game"]?["title"] : showing == "recommended" ? "No backlog game" : "Nothing fits", "Blastlog.HeroTitle").Margin(0, 26, 0, 14));
    if (entry != null) {
      copy.Children.Add(Chips(entry));
      var why = Reasons(entry, showing);
      if (why != null) copy.Children.Add(why);
      copy.Children.Add(Actions(entry));
    } else copy.Children.Add(Empty(showing));
    left.Children.Add(copy);
    layers.Children.Add(DashHero.Content(left, entry != null ? DashHero.Cover(plugin, game, Status(entry)) : null));
    DashHero.FadeIn(copy);
  }
  static string Status(JObject entry) => (string)entry["ownership"] == "wishlist" ? "wishlist" : (string)entry["status"];
  /// <summary>A pick that can go straight onto the Playing list (Charlie, 2026-11-08): a backlog game the player owns.</summary>
  static bool Startable(JObject entry) => (string)entry["ownership"] != "wishlist" && ((string)entry["status"] == "not_started" || (string)entry["status"] == "started");

  /// <summary>The medium card: header with the modes, then the pick's cover, title, status and length, why, and buttons.</summary>
  FrameworkElement Compact(JObject entry, string showing, Blastlog.Sync.Core.Game game) {
    var panel = new StackPanel();
    var icon = Icons.Make(Icons.Star, 20, null); icon.Margin = new Thickness(0, 0, 10, 0); icon.VerticalAlignment = VerticalAlignment.Center;
    ((Shape)((Canvas)((Viewbox)icon).Child).Children[0]).SetResourceReference(Shape.StrokeProperty, "Blastlog.GameText");
    var head = Dash.Row(0, icon, Dash.Text("Next up", "Blastlog.CardTitle").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    panel.Children.Add(DashHero.TopRow(head, ModeBox()).Margin(0, 0, 0, 16));
    if (entry == null) { panel.Children.Add(Empty(showing)); return panel; }
    var id = (string)entry["id"]; var title = game?.Title ?? (string)entry["game"]?["title"] ?? "";
    var body = new DockPanel { LastChildFill = true };
    Kit.GameScope(body, plugin, game);
    var cover = PnKit.Bare(Kit.Cover(plugin, game, 88, ArtworkContext.Cover, 12), () => Dash.OpenEntry(plugin, id), "Open " + title);
    cover.Width = 88; cover.VerticalAlignment = VerticalAlignment.Top; cover.Margin = new Thickness(0, 0, 16, 0);
    DockPanel.SetDock(cover, Dock.Left); body.Children.Add(cover);
    var main = new StackPanel();
    var name = Dash.Text(title, "Blastlog.CardTitle"); name.TextWrapping = TextWrapping.Wrap; Dash.OnClick(name, () => Dash.OpenEntry(plugin, id)); name.ToolTip = "Open " + title;
    main.Children.Add(name);
    var meta = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
    meta.Children.Add(Kit.StatusPill(Status(entry)).Also(p => p.Margin = new Thickness(0, 0, 10, 0)));
    var length = Dash.LengthText(entry["length"]); if (length != null) meta.Children.Add(Dash.Text(length, "Blastlog.Muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
    main.Children.Add(meta);
    var why = WhyLine(); if (why != null) main.Children.Add(why.Also(t => t.Margin = new Thickness(0, 10, 0, 0)));
    var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
    var canStart = start != null && Startable(entry);
    if (canStart) actions.Children.Add(Kit.Button("Start playing", () => start(entry)));
    actions.Children.Add(Kit.Button("View details", () => Dash.OpenEntry(plugin, id), canStart ? "Blastlog.Ghost" : "Blastlog.Primary"));
    foreach (var b in actions.Children.OfType<Button>()) { b.Margin = new Thickness(0, 0, 8, 8); b.FontSize = 14; b.MinHeight = 44; b.Padding = new Thickness(16, 0, 16, 0); }
    main.Children.Add(actions);
    body.Children.Add(main);
    panel.Children.Add(body);
    return panel;
  }
  /// <summary>"★ 4.6 predicted · because you like Story-driven" (the one-line why), or null without a reason.</summary>
  TextBlock WhyLine() {
    var explanation = (string)data?["profileSource"] == "fingerprint_v2" ? data["explanation"] as JObject : null;
    var reason = Dash.Arr(explanation?["reasons"]).Select(r => (string)r).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
    if (reason == null) return null;
    var line = new TextBlock { Style = Kit.Style("Blastlog.Body"), TextWrapping = TextWrapping.Wrap };
    var predicted = (double?)explanation["predictedRating"];
    if (predicted != null) {
      var star = new Run("★ " + predicted.Value.ToString("0.0", CultureInfo.InvariantCulture)) { FontWeight = FontWeights.Bold };
      star.SetResourceReference(TextElement.ForegroundProperty, "Blastlog.Star");
      line.Inlines.Add(star); line.Inlines.Add(new Run(" predicted · because you like "));
    } else line.Inlines.Add(new Run("Because you like "));
    line.Inlines.Add(new Run(reason) { FontWeight = FontWeights.Bold });
    return line;
  }

  /// <summary>The three modes (a segmented control; the pressed one in the game's colour).</summary>
  FrameworkElement ModeBox() {
    var toggle = new StackPanel { Orientation = Orientation.Horizontal };
    foreach (var m in Modes) {
      var on = m.Key == Mode;
      var b = Kit.Button(m.Label, () => Switch(m.Key), on ? "Blastlog.Primary" : "Blastlog.TextButton");
      b.Margin = new Thickness(0); b.MinHeight = 44; b.Padding = new Thickness(16, 0, 16, 0);
      if (!on) b.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text");
      if (m.Tip != null) b.ToolTip = m.Tip;
      Dash.AutomationName(b, m.Label + (on ? " (selected)" : ""));
      toggle.Children.Add(b);
    }
    var box = new Border { Child = toggle, CornerRadius = new CornerRadius(14), Padding = new Thickness(3), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    box.SetResourceReference(BackgroundProperty, "Blastlog.Surface2"); box.SetResourceReference(BorderBrushProperty, "Blastlog.Line");
    System.Windows.Automation.AutomationProperties.SetName(box, "What kind of game");
    return box;
  }

  /// <summary>Length, platforms, then where you own it (once the game's page read arrives); the status is on the cover.</summary>
  FrameworkElement Chips(JObject entry) {
    var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
    var length = entry["length"] as JObject; var lengthText = Dash.LengthText(length);
    if (lengthText != null) {
      var hours = Dash.Hours(length);
      wrap.Children.Add(Dash.IconChip(Dash.Clock, Dash.Text(lengthText, "Blastlog.Small", "Blastlog.Text", FontWeights.SemiBold),
        (bool?)length["noEnd"] == true ? "Keeps going: no end to beat" : hours != null ? $"About {Math.Round(hours.Value)} hours to beat ({Dash.Level((string)length["fromLevel"])})" : null));
    }
    var platforms = Dash.Arr(entry["game"]?["releasePlatforms"]).Select(p => (string)p).ToList();
    if (platforms.Count > 0) wrap.Children.Add(Dash.IconChip(Icons.Gamepad, Dash.Text(Dash.PlatformLine(platforms), "Blastlog.Small", "Blastlog.Text", FontWeights.SemiBold),
      "Released on " + string.Join(", ", Dash.OrderedPlatforms(platforms))));
    var id = (string)entry["id"];
    if (id != null) Dash.Safe(async () => {
      var page = (await plugin.Mirror("entries/" + id + "?like=0", fresh: false)).Value;
      var stores = Dash.Arr(page?["stores"]?["stores"]).OfType<JObject>().ToList();
      if (stores.Count == 0) return;
      var line = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
      line.Children.Add(Dash.Text("Owned on ", "Blastlog.Small", "Blastlog.Text", FontWeights.SemiBold));
      for (var i = 0; i < stores.Count; i++) {
        var place = Dash.StorePlace((string)stores[i]["store"], (string)stores[i]["storeId"]);
        if (i > 0) line.Children.Add(Dash.Text(" · ", "Blastlog.Small", "Blastlog.Text"));
        if (place.Url != null) {
          var link = Dash.Text(place.Name, "Blastlog.Small", "Blastlog.GameText", FontWeights.Bold); link.TextDecorations = TextDecorations.Underline; link.ToolTip = place.Where;
          var url = place.Url; Dash.OnClick(link, () => UiKit.Open(url));
          line.Children.Add(link);
        } else { var t = Dash.Text(place.Name, "Blastlog.Small", "Blastlog.Text", FontWeights.SemiBold); t.ToolTip = place.Where; line.Children.Add(t); }
      }
      wrap.Children.Add(Dash.IconChip(Dash.Archive, line, "Where you own it"));
    }, "owned stores");
    return wrap;
  }

  /// <summary>Why it's up next: up to three reasons, one Watch for, the boost, and a quote from your own review.</summary>
  FrameworkElement Reasons(JObject entry, string showing) {
    var explanation = (string)data?["profileSource"] == "fingerprint_v2" ? data["explanation"] as JObject : null;
    var reasons = Dash.Arr(explanation?["reasons"]).Select(r => (string)r).ToList();
    if (reasons.Count == 0) return null;
    var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
    var predicted = (double?)explanation["predictedRating"];
    if (predicted != null) {
      var line = new TextBlock { Style = Kit.Style("Blastlog.Body"), Margin = new Thickness(0, 0, 0, 10) };
      var star = new Run("★ " + predicted.Value.ToString("0.0", CultureInfo.InvariantCulture)) { FontWeight = FontWeights.Bold };
      star.SetResourceReference(TextElement.ForegroundProperty, "Blastlog.Star");
      line.Inlines.Add(star); line.Inlines.Add(new Run(" predicted · because you like "));
      line.Inlines.Add(new Run(reasons[0]) { FontWeight = FontWeights.Bold });
      panel.Children.Add(line);
    }
    panel.Children.Add(Dash.Text("Why it's up next", "Blastlog.Small", "Blastlog.GameText", FontWeights.Bold).Margin(0, 0, 0, 8));
    var chips = new WrapPanel();
    foreach (var r in reasons.Take(3)) chips.Children.Add(Kit.Chip(r));
    foreach (var risk in Dash.Arr(explanation["risks"]).Select(r => (string)r).Take(1)) chips.Children.Add(RiskChip("Watch for: " + risk, "You've rated games with this lower"));
    panel.Children.Add(chips);

    var boost = explanation["boost"] as JObject;
    if (boost != null) {
      var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
      row.Children.Add(Dash.Text((string)boost["text"] + " · ", "Blastlog.Muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
      row.Children.Add(Dash.Link("Not now", () => Dash.Safe(async () => {
        var body = new { kind = (string)boost["kind"], key = (string)boost["key"] };
        if (await plugin.Write("play-next/boost-dismissals", body) == null) return;
        droppedBoost = boost; await Reload();
      }, "boost not now")));
      panel.Children.Add(row);
    } else if (droppedBoost != null) {
      var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
      row.Children.Add(Dash.Text("That boost is off. ", "Blastlog.Muted").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
      var gone = droppedBoost;
      row.Children.Add(Dash.Link("Undo", () => Dash.Safe(async () => {
        if (await plugin.Write("play-next/boost-dismissals", new { kind = (string)gone["kind"], key = (string)gone["key"] }, new System.Net.Http.HttpMethod("DELETE")) == null) return;
        droppedBoost = null; await Reload();
      }, "boost undo")));
      panel.Children.Add(row);
    }

    var quote = Dash.Arr(explanation["quotes"]).OfType<JObject>().FirstOrDefault(q => (string)q["tone"] == "like");
    if (quote != null) {
      var words = (string)quote["quote"] ?? "";
      var text = new TextBlock { Style = Kit.Style("Blastlog.Body"), Margin = new Thickness(0, 6, 0, 0), ToolTip = "About " + (string)quote["label"] };
      text.Inlines.Add(new Run("“" + (words.Length > 0 && char.IsLower(words[0]) ? "…" : "") + words + "”") { FontStyle = FontStyles.Italic });
      var cite = new Run(" — your review of " + (string)quote["gameTitle"]);
      cite.SetResourceReference(TextElement.ForegroundProperty, "Blastlog.TextMuted");
      text.Inlines.Add(cite);
      panel.Children.Add(text);
    }
    return panel;
  }
  async System.Threading.Tasks.Task Reload() { data = (await plugin.Mirror($"next-up?mode={Mode}", fresh: true)).Value ?? data; Render(); }

  /// <summary>A dashed chip in the warning colour (risk chips, design.md 7).</summary>
  static FrameworkElement RiskChip(string text, string tip) {
    var label = Dash.Text(text, "Blastlog.Small", "Blastlog.Warn", FontWeights.SemiBold); label.Margin = new Thickness(12, 5, 12, 6); label.TextWrapping = TextWrapping.NoWrap;
    var edge = new Rectangle { RadiusX = 15, RadiusY = 15, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } };
    edge.SetResourceReference(Shape.StrokeProperty, "Blastlog.Warn");
    return new Grid { Margin = new Thickness(0, 0, 8, 8), ToolTip = tip, Children = { edge, label } };
  }

  FrameworkElement Actions(JObject entry) {
    var id = (string)entry["id"];
    var row = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
    var play = Dash.PlayButton(plugin, id);
    if (play != null) row.Children.Add(play);
    var canStart = start != null && Startable(entry);
    if (canStart) {
      var go = Kit.Button("Start playing", () => start(entry), play != null ? "Blastlog.Ghost" : "Blastlog.Primary");
      go.Content = Dash.IconLabel(Icons.Gamepad, "Start playing");
      row.Children.Add(go);
    }
    var details = Kit.Button("View details", () => Dash.OpenEntry(plugin, id), play != null || canStart ? "Blastlog.Ghost" : "Blastlog.Primary");
    details.Content = Dash.IconLabel(Dash.Eye, "View details");
    row.Children.Add(details);
    var reviewed = !string.IsNullOrWhiteSpace((string)entry["review"]);
    var review = Kit.Button(reviewed ? "Edit review" : "Add review", () => Dash.Review(plugin, id), "Blastlog.Ghost");
    review.Content = Dash.IconLabel(Dash.Pencil, reviewed ? "Edit review" : "Add review");
    row.Children.Add(review);
    foreach (FrameworkElement b in row.Children) b.Margin = new Thickness(0, 0, 10, 8);
    if (play == null && plugin.GameFor(id) == null) row.Children.Add(Dash.Text("Not in Playnite", "Blastlog.Muted").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(6, 0, 0, 8); t.ToolTip = "Pair it in Blastlog, Pair games, once it's in your Playnite library"; }));
    return row;
  }

  /// <summary>Nothing fits: why, and the other modes to try.</summary>
  FrameworkElement Empty(string showing) {
    var panel = new StackPanel();
    var choice = data?["lengthChoice"] as JObject;
    if (showing == "recommended" || choice == null) { panel.Children.Add(Dash.Text("Your actionable backlog is clear.", "Blastlog.Muted")); return panel; }
    var unknown = (int?)choice["unknownLength"] ?? 0;
    var text = (showing == "quick" ? $"Nothing in the backlog takes {(double?)choice["quickMaxHours"]} hours or less" : $"Nothing in the backlog takes {(double?)choice["longMinHours"]} hours or more")
      + $" ({Dash.Level((string)choice["level"])})."
      + (unknown > 0 ? $" {(unknown == 1 ? "1 game has" : unknown + " games have")} no length yet, so {(unknown == 1 ? "it can't" : "they can't")} show here." : "");
    panel.Children.Add(Dash.Text(text, "Blastlog.Muted"));
    var row = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
    foreach (var m in Modes.Where(m => m.Key != showing)) row.Children.Add(Kit.Button("Try " + m.Label, () => Switch(m.Key), "Blastlog.Ghost"));
    panel.Children.Add(row);
    return panel;
  }
}
}
