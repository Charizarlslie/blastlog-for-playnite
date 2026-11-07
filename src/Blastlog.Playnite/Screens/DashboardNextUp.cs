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
/// on a blurred copy of itself under a scrim with the game's glow; Recommended / Quick one / Long haul; status, length,
/// platforms and where you own it; Why it's up next and one Watch for; a boost with Not now and Undo; the quote from your
/// own review; Play or Install when it's in Playnite, View details, Add review (Edit review once written).
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
  JObject data; JObject droppedBoost; int switchId;
  readonly Grid layers = new Grid();

  public DashboardNextUp(BlastlogPlugin plugin, DashboardView view, JObject data) {
    this.plugin = plugin; this.view = view; this.data = data;
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
    layers.Children.Clear();
    var entry = data?["entry"] as JObject;
    var showing = (string)data?["mode"] ?? Mode;
    var game = Dash.Game(entry?["game"]);
    if (showing == Mode) view.Wear(game);
    var art = Artwork.Url(game, ArtworkContext.Backdrop);

    // Art, scrim, glow.
    if (art != null) {
      // A rectangle filled with the art takes the card's size instead of setting it (an Image would stretch the card to the cover's shape).
      // The blur comes from decoding the cover tiny and letting it stretch smoothly: a live BlurEffect this size is redrawn on
      // every scroll step and made the page crawl.
      var blurred = new Rectangle { Opacity = 0.9, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1.25, 1.25) };
      RenderOptions.SetBitmapScalingMode(blurred, BitmapScalingMode.Linear);
      Dash.LoadFill(plugin, art, blurred, 24);
      layers.Children.Add(blurred);
    }
    var scrim = new Rectangle { OpacityMask = new LinearGradientBrush(new GradientStopCollection {
      new GradientStop(Color.FromArgb(235, 0, 0, 0), 0), new GradientStop(Color.FromArgb(200, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(120, 0, 0, 0), 1) }, 0) };
    scrim.SetResourceReference(Shape.FillProperty, "Blastlog.Bg");
    layers.Children.Add(scrim);
    // The glow: the game colour fading out from the top left (design.md 2.2: dark 24% fading by 66% of the radius, light 10% by 42%).
    var light = ThemeBridge.CurrentMode == Blastlog.Sync.Core.Design.Mode.Light;
    var glow = new Rectangle { Width = 900, Height = 700, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-300, -280, 0, 0),
      IsHitTestVisible = false, OpacityMask = new RadialGradientBrush(new GradientStopCollection {
        new GradientStop(Color.FromArgb((byte)(light ? 26 : 61), 0, 0, 0), 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), light ? 0.42 : 0.66) }) };
    glow.SetResourceReference(Shape.FillProperty, "Blastlog.Game");
    layers.Children.Add(glow);

    var content = new Grid { Margin = new Thickness(36, 30, 36, 34) };
    content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    var left = new StackPanel();
    left.Children.Add(TopRow());
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
    content.Children.Add(left);
    if (entry != null) {
      var cover = CoverBox(entry, game);
      Grid.SetColumn(cover, 1); content.Children.Add(cover);
    }
    layers.Children.Add(content);
    if (SystemParameters.ClientAreaAnimation) copy.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
  }

  /// <summary>"Next up" and the three modes (a segmented control; the pressed one in the game's colour).</summary>
  FrameworkElement TopRow() {
    // The chip keeps its own width (Auto, no wrapping); the space between them gives way first.
    var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    var label = new Border { Style = Kit.Style("Blastlog.NeutralChip"), Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
      Child = Dash.Text("Next up", "Blastlog.Small", "Blastlog.Text", FontWeights.Bold).Also(t => t.TextWrapping = TextWrapping.NoWrap) };
    grid.Children.Add(label);
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
    Grid.SetColumn(box, 2); grid.Children.Add(box);
    // Beside the cover there may not be room for both: the modes then go under the chip instead of being cut off.
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.SizeChanged += (s, e) => {
      try {
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var under = e.NewSize.Width < label.DesiredSize.Width + box.DesiredSize.Width + 12;
        Grid.SetColumn(box, under ? 0 : 2); Grid.SetRow(box, under ? 1 : 0); Grid.SetColumnSpan(box, under ? 3 : 1);
        box.HorizontalAlignment = under ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        box.Margin = new Thickness(0, under ? 10 : 0, 0, 0);
      } catch (Exception ex) { global::Playnite.SDK.LogManager.GetLogger().Error(ex, "Blastlog dashboard: next up modes layout"); }
    };
    return grid;
  }

  /// <summary>Status, length, platforms, then where you own it (once the game's page read arrives).</summary>
  FrameworkElement Chips(JObject entry) {
    var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
    var pill = Kit.StatusPill((string)entry["ownership"] == "wishlist" ? "wishlist" : (string)entry["status"]); pill.Margin = new Thickness(0, 0, 8, 8);
    wrap.Children.Add(pill);
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
      var page = (await plugin.Mirror("entries/" + id, fresh: false)).Value;
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
    var details = Kit.Button("View details", () => Dash.OpenEntry(plugin, id), play != null ? "Blastlog.Ghost" : "Blastlog.Primary");
    details.Content = Dash.IconLabel(Dash.Eye, "View details");
    row.Children.Add(details);
    var reviewed = !string.IsNullOrWhiteSpace((string)entry["review"]);
    var review = Kit.Button(reviewed ? "Edit review" : "Add review", () => Dash.Review(plugin, id), "Blastlog.Ghost");
    review.Content = Dash.IconLabel(Dash.Pencil, reviewed ? "Edit review" : "Add review");
    row.Children.Add(review);
    foreach (FrameworkElement b in row.Children) b.Margin = new Thickness(0, 0, 10, 8);
    if (play == null) row.Children.Add(Dash.Text("Not in Playnite", "Blastlog.Muted").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(6, 0, 0, 8); t.ToolTip = "Pair it in Blastlog, Pair games, once it's in your Playnite library"; }));
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

  /// <summary>The full cover with a shadow, its status on it (the approved board).</summary>
  FrameworkElement CoverBox(JObject entry, Blastlog.Sync.Core.Game game) {
    var cover = Kit.Cover(plugin, game, 260, ArtworkContext.Cover, 18);
    cover.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.45, Color = Colors.Black };
    cover.CacheMode = new BitmapCache(); // the shadow is drawn once, not on every scroll step
    System.Windows.Automation.AutomationProperties.SetName(cover, "Cover of " + game?.Title);
    var pill = Kit.StatusPill((string)entry["ownership"] == "wishlist" ? "wishlist" : (string)entry["status"]);
    pill.HorizontalAlignment = HorizontalAlignment.Left; pill.VerticalAlignment = VerticalAlignment.Bottom; pill.Margin = new Thickness(12);
    return new Grid { Margin = new Thickness(32, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { cover, pill } };
  }
}
}
