using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Blastlog.Sync.Core.Design;
using Playnite.SDK;

namespace Blastlog.Playnite.Ui {
/// <summary>Options for <see cref="Fingerprint.Card"/>.</summary>
public sealed class FingerprintOptions {
  /// <summary>Where "Flag it" and "Suggest a trait" go on the site (the game's page there); null hides them.</summary>
  public string SiteGameUrl;
  /// <summary>The site's address, for "See games with …" (traits/:id).</summary>
  public string ServerUrl;
}

/// <summary>
/// The game page fingerprint card exactly as design.md 8 "Game page fingerprint" (the site's GameFingerprint.tsx, player
/// view): caps FINGERPRINT label with its icon, watermark and glow, summary, What defines it, the bars against a typical game
/// with trait names only inside the fill and "+N more", tap a group for every trait, tap a trait for "Why it's here",
/// series, the legend and the disclosures. The maths live in Blastlog.Sync.Core.Design.FingerprintBars (tested against the
/// site). Other screens reuse <see cref="Bars"/> for an early fingerprint on a card.
/// </summary>
public static class Fingerprint {
  static readonly ILogger Log = LogManager.GetLogger();
  public const string Glyph = "<path d=\"M12 10a2 2 0 0 0-2 2c0 1.6-.3 3.4-1 5\" /><path d=\"M14 12.5c0 2.1-.3 4.4-1 6.5\" /><path d=\"M7.5 7.5A6 6 0 0 1 18 12c0 1.3-.1 2.6-.4 3.9\" /><path d=\"M6 12a6 6 0 0 1 .3-1.9M6.2 15.5c-.1-.5-.2-1-.2-1.5\" /><path d=\"M4.5 4.9A10 10 0 0 1 21.6 9.5M2.5 15A10 10 0 0 1 2 12a10 10 0 0 1 .7-3.6\" />";
  const string Caret = "<path d=\"m6 9 6 6 6-6\" />";
  static readonly Dictionary<string, string> ReasonLabel = new Dictionary<string, string> { ["recheck"] = "Re-check", ["interpretation"] = "Blastlog’s read",
    ["you"] = "You", ["ruling"] = "Your ruling", ["example"] = "Example", ["players"] = "Players", ["fix"] = "Fix it", ["other"] = "Merge", ["source"] = "Source" };
  static readonly Dictionary<string, string> OnlineHow = new Dictionary<string, string> { ["servers"] = "servers shut down", ["dropped"] = "not in the newest edition", ["ghost_town"] = "lobbies are empty" };

  /// <summary>The whole card. Returns a card saying so when there's no fingerprint yet.</summary>
  public static FrameworkElement Card(FingerprintData data, FingerprintOptions options = null) {
    options = options ?? new FingerprintOptions();
    var card = new Border { CornerRadius = new CornerRadius(28), Padding = new Thickness(28), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 18), ClipToBounds = true };
    card.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface");
    Tint.OnGame(card, (game, line, surface) => {
      card.BorderBrush = Frozen(new SolidColorBrush(Tint.Mix(game, line, 0.24)));
      // The site's radial glow: game at 14% from the top-right corner, gone by 60%.
      var glow = new RadialGradientBrush { Center = new Point(1, 0), GradientOrigin = new Point(1, 0), RadiusX = 1.1, RadiusY = 0.8 };
      glow.GradientStops.Add(new GradientStop(Tint.Mix(game, surface, 0.14), 0)); glow.GradientStops.Add(new GradientStop(surface, 0.6));
      card.Background = Frozen(glow);
    });
    var layers = new Grid();
    // The watermark: the fingerprint glyph, big and faint in the game's words colour, off the top-right corner.
    var mark = Icons.Make(Glyph, 260); mark.Opacity = 0.1; mark.IsHitTestVisible = false; mark.HorizontalAlignment = HorizontalAlignment.Right; mark.VerticalAlignment = VerticalAlignment.Top;
    mark.Margin = new Thickness(0, -74, -74, 0); ThinStroke(mark, 1.1, "Blastlog.GameText");
    layers.Children.Add(mark);
    var body = new StackPanel();
    layers.Children.Add(body);
    card.Child = layers;
    if (data == null) { body.Children.Add(Header(null, null, null)); body.Children.Add(Kit.Text("Not fingerprinted yet.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 14, 0, 0))); return card; }

    var areas = FingerprintBars.Areas(data);
    var expanded = new HashSet<string>(); var showAll = false;
    var traitCount = FingerprintBars.TraitCount(data);
    var count = traitCount == 0 ? null : $"{traitCount} trait{(traitCount == 1 ? "" : "s")}{(areas.Count > 1 ? $" in {areas.Count} areas" : "")}";
    var bars = new StackPanel();
    Button showAllButton = null;
    if (areas.Count > 0) showAllButton = SmallButton("Show every trait", () => {
      showAll = !showAll; expanded.Clear(); showAllButton.Content = showAll ? "Show less" : "Show every trait";
      Render(bars, data, areas, expanded, showAll, options);
    });
    body.Children.Add(Header(count, FingerprintBars.StandOut(areas), showAllButton, data.PendingCount));

    if (data.Building) body.Children.Add(Note("Fingerprint still building. Its traits show up here when it’s done, usually within the hour."));
    if (!string.IsNullOrEmpty(data.Summary)) body.Children.Add(Kit.Text(data.Summary).Also(t => { t.FontSize = 15; t.LineHeight = 24; t.MaxWidth = 900; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0, 14, 0, 4); }));
    else if (data.Active.Count == 0 && !data.Building) body.Children.Add(Kit.Text("Not fingerprinted yet.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 14, 0, 0)));

    var defining = FingerprintBars.Defining(data);
    if (defining.Count > 0) {
      var block = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
      var h = new TextBlock { FontSize = 14, FontWeight = FontWeights.ExtraBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
      h.SetResourceReference(TextBlock.FontFamilyProperty, "Blastlog.Font"); h.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.GameText");
      h.Inlines.Add(new System.Windows.Documents.Run("What defines it"));
      var note = new System.Windows.Documents.Run(" · strong here, rare elsewhere") { FontWeight = FontWeights.SemiBold }; note.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.TextMuted");
      h.Inlines.Add(note); block.Children.Add(h);
      block.Children.Add(Chips(defining, options, big: true));
      body.Children.Add(block);
    }

    if (areas.Count > 0) {
      bars.Margin = new Thickness(0, defining.Count > 0 ? 22 : 18, 0, 0);
      Render(bars, data, areas, expanded, showAll, options);
      body.Children.Add(bars);
      if (areas.Any(a => a.Fill != null)) body.Children.Add(Kit.Text("A typical game fills each bar halfway. Longer means more of it than most games.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 12, 0, 0)));
    }
    var series = FingerprintBars.Series(data);
    if (series.Count > 0) {
      var line = new TextBlock { FontSize = 14, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
      line.SetResourceReference(TextBlock.FontFamilyProperty, "Blastlog.Font"); line.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text");
      var label = new System.Windows.Documents.Run("Series "); label.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.TextMuted");
      line.Inlines.Add(label); line.Inlines.Add(new System.Windows.Documents.Run(string.Join(", ", series)));
      body.Children.Add(line);
    }
    if (data.OnlineEnded.Count > 0) body.Children.Add(OnlineNote(data.OnlineEnded));
    if (data.Active.Count > 0) body.Children.Add(Legend());
    if (options.SiteGameUrl != null) body.Children.Add(LinkButton("Missing something? Suggest a trait on Blastlog ↗", () => UiKit.Open(options.SiteGameUrl)).Also(b => b.Margin = new Thickness(-10, 8, 0, 0)));
    if (data.Pending.Count > 0) body.Children.Add(Disclosure($"Discoveries awaiting review ({data.Pending.Count})", () => {
      var list = new StackPanel();
      list.Children.Add(Note("Kept on this game now; they start counting once they’re approved."));
      foreach (var p in data.Pending) list.Children.Add(NoteRow(p));
      return list;
    }));
    if (data.Suppressed.Count > 0) body.Children.Add(Disclosure($"Set aside ({data.Suppressed.Count})", () => {
      var list = new StackPanel(); foreach (var s in data.Suppressed) list.Children.Add(NoteRow(s)); return list;
    }));
    return card;
  }

  /// <summary>
  /// Just the bars (no header, no taps), for a card that wants an early fingerprint (Play next, Dashboard). Wear the card's
  /// game around it with <see cref="Kit.GameScope(FrameworkElement, string)"/>; names inside the fill, "+N more" beside it.
  /// </summary>
  public static FrameworkElement Bars(FingerprintData data, int maxBars = 4, bool compact = true) {
    var panel = new StackPanel();
    if (data == null) return panel;
    var areas = FingerprintBars.Areas(data);
    foreach (var a in areas.Take(maxBars)) {
      var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
      var name = Kit.Text(a.Name, "Blastlog.Small"); name.FontWeight = FontWeights.Bold; name.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text"); name.Margin = new Thickness(0, 0, 0, 4);
      row.Children.Add(name);
      row.Children.Add(new FillTrack { Traits = a.Traits, Fill = FingerprintBars.DrawnFill(a, areas), Height = compact ? 34 : 40 });
      AutomationProperties.SetName(row, a.Sentence);
      panel.Children.Add(row);
    }
    return panel;
  }

  // ---- Pieces ----
  static FrameworkElement Header(string count, string standOut, Button showAll, int pending = 0) {
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    var left = new StackPanel();
    var eyebrow = new StackPanel { Orientation = Orientation.Horizontal };
    var icon = Icons.Make(Glyph, 15); ThinStroke(icon, 2.2, "Blastlog.GameText"); icon.Margin = new Thickness(0, 0, 7, 0); icon.VerticalAlignment = VerticalAlignment.Center;
    eyebrow.Children.Add(icon); eyebrow.Children.Add(Kit.Text("FINGERPRINT", "Blastlog.Label"));
    left.Children.Add(eyebrow);
    left.Children.Add(Kit.Text("What this game is like").Also(t => { t.FontSize = 20; t.FontWeight = FontWeights.ExtraBold; t.Margin = new Thickness(0, 2, 0, 0); }));
    if (count != null) {
      var line = Kit.Text(count, "Blastlog.Small"); line.Margin = new Thickness(0, 4, 0, 0);
      if (standOut != null) { line.Inlines.Add(new System.Windows.Documents.Run(" · stands out for ")); line.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(standOut)).Also(b => b.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.Text"))); }
      left.Children.Add(line);
    }
    grid.Children.Add(left);
    var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
    if (showAll != null) right.Children.Add(showAll);
    if (pending > 0) {
      var p = Kit.Text("", "Blastlog.Small"); p.VerticalAlignment = VerticalAlignment.Center; p.Margin = new Thickness(12, 0, 0, 0); p.ToolTip = "Discovered traits waiting for review";
      p.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(pending.ToString(CultureInfo.InvariantCulture))).Also(b => b.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.Text")));
      p.Inlines.Add(new System.Windows.Documents.Run(" awaiting review")); right.Children.Add(p);
    }
    Grid.SetColumn(right, 1); grid.Children.Add(right);
    // A narrow card puts the buttons under the title instead of squeezing it.
    grid.SizeChanged += (s, e) => { try { var narrow = e.NewSize.Width < 520; Grid.SetColumn(right, narrow ? 0 : 1); Grid.SetRow(right, narrow ? 1 : 0);
      if (grid.RowDefinitions.Count == 0) { grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
      right.Margin = new Thickness(0, narrow ? 10 : 0, 0, 0); } catch (Exception ex) { Log.Error(ex, "Blastlog fingerprint header"); } };
    return grid;
  }

  static void Render(StackPanel bars, FingerprintData data, List<FingerprintArea> areas, HashSet<string> expanded, bool showAll, FingerprintOptions options) {
    bars.Children.Clear();
    var byFamily = FingerprintBars.ByFamily(data);
    foreach (var area in areas) {
      var open = showAll || expanded.Contains(area.Name);
      var item = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
      var bar = BarButton(area, FingerprintBars.DrawnFill(area, areas), open);
      bar.Click += (s, e) => { try { if (!expanded.Remove(area.Name)) expanded.Add(area.Name); Render(bars, data, areas, expanded, showAll, options); } catch (Exception ex) { Log.Error(ex, "Blastlog fingerprint bar"); } };
      item.Children.Add(bar);
      if (open) {
        var detail = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16), BorderThickness = new Thickness(1), Margin = new Thickness(0, 10, 0, 0) };
        detail.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
        detail.Background = Frozen(new SolidColorBrush(Tint.Scrim(0.55)));
        var groups = new StackPanel();
        foreach (var family in byFamily.Where(f => area.Families.Contains(f.Key))) {
          var head = new DockPanel { Margin = new Thickness(0, groups.Children.Count == 0 ? 0 : 14, 0, 10) };
          var countText = Kit.Text(family.Value.Count.ToString(CultureInfo.InvariantCulture), "Blastlog.Small"); countText.FontWeight = FontWeights.ExtraBold; DockPanel.SetDock(countText, Dock.Right);
          head.Children.Add(countText);
          head.Children.Add(Kit.Text(FingerprintBars.FamilyLabels.TryGetValue(family.Key, out var l) ? l : family.Key, "Blastlog.Small").Also(t => t.FontWeight = FontWeights.ExtraBold));
          groups.Children.Add(head);
          groups.Children.Add(Chips(family.Value, options, big: false));
        }
        detail.Child = groups;
        item.Children.Add(detail);
      }
      bars.Children.Add(item);
    }
  }

  /// <summary>One bar: name and "N traits" on the label, the track, the caret. Below 560px the name sits above the track.</summary>
  static Button BarButton(FingerprintArea area, double fill, bool open) {
    var grid = new Grid();
    var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
    var title = Kit.Text(area.Name); title.FontSize = 15; title.FontWeight = FontWeights.Bold; name.Children.Add(title);
    var traits = Kit.Text($"{area.Traits.Count} trait{(area.Traits.Count == 1 ? "" : "s")}", "Blastlog.Small"); traits.FontWeight = FontWeights.SemiBold; name.Children.Add(traits);
    var track = new FillTrack { Traits = area.Traits, Fill = fill, Height = 40 };
    var caret = Icons.Make(Caret, 16); caret.VerticalAlignment = VerticalAlignment.Center; ThinStroke(caret, 2, "Blastlog.TextMuted");
    if (open) caret.RenderTransform = new RotateTransform(180, 8, 8);
    grid.Children.Add(name); grid.Children.Add(track); grid.Children.Add(caret);
    void Lay(bool narrow) {
      grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
      if (!narrow) {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        Grid.SetColumn(name, 0); Grid.SetColumn(track, 1); Grid.SetColumn(caret, 2); Grid.SetRow(name, 0); Grid.SetRow(track, 0); Grid.SetRow(caret, 0); Grid.SetColumnSpan(track, 1);
        name.Orientation = Orientation.Vertical; traits.Margin = new Thickness(0, 1, 0, 0); track.Margin = new Thickness(12, 0, 0, 0); caret.HorizontalAlignment = HorizontalAlignment.Right;
      } else {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(name, 0); Grid.SetRow(name, 0); Grid.SetColumn(caret, 1); Grid.SetRow(caret, 0); Grid.SetColumn(track, 0); Grid.SetRow(track, 1); Grid.SetColumnSpan(track, 2);
        name.Orientation = Orientation.Horizontal; traits.Margin = new Thickness(8, 1, 0, 0); traits.VerticalAlignment = VerticalAlignment.Center; track.Margin = new Thickness(0, 6, 0, 0);
      }
    }
    Lay(false);
    var narrowNow = false;
    grid.SizeChanged += (s, e) => { try { var narrow = e.NewSize.Width < 560; if (narrow != narrowNow) { narrowNow = narrow; Lay(narrow); } } catch (Exception ex) { Log.Error(ex, "Blastlog fingerprint bar layout"); } };
    var button = new Button { Content = grid, Style = Kit.Style("Blastlog.TextButton"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), Margin = new Thickness(0), MinHeight = 44 };
    button.Template = PlainTemplate();
    AutomationProperties.SetName(button, area.Sentence);
    AutomationProperties.SetHelpText(button, open ? "Expanded. Press to hide its traits." : "Collapsed. Press to see every trait.");
    return button;
  }

  /// <summary>Trait chips; each opens "Why it's here" under the row (one at a time).</summary>
  static FrameworkElement Chips(List<FingerprintTrait> traits, FingerprintOptions options, bool big) {
    var host = new StackPanel();
    var wrap = new WrapPanel(); host.Children.Add(wrap);
    var why = new ContentControl(); host.Children.Add(why);
    FingerprintTrait openTrait = null;
    var buttons = new List<Button>();
    foreach (var t in traits) {
      var trait = t;
      var b = Chip(trait, big);
      b.Click += (s, e) => {
        try {
          openTrait = openTrait == trait ? null : trait;
          foreach (var other in buttons) SetChipOpen(other, ReferenceEquals(other.Tag, openTrait));
          why.Content = openTrait == null ? null : WhyPanel(openTrait, options, () => { openTrait = null; why.Content = null; foreach (var other in buttons) SetChipOpen(other, false); });
        } catch (Exception ex) { Log.Error(ex, "Blastlog trait chip"); }
      };
      buttons.Add(b); wrap.Children.Add(b);
    }
    return host;
  }

  static Button Chip(FingerprintTrait trait, bool big) {
    var text = new TextBlock { Text = trait.Label, FontSize = 14, FontWeight = big ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
    text.SetResourceReference(TextBlock.FontFamilyProperty, "Blastlog.Font"); text.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text");
    if (trait.Role == "context") { var ctx = new System.Windows.Documents.Run(" (context)"); ctx.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.TextMuted"); text.Inlines.Add(ctx); }
    var pill = new Border { CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(1), Padding = big ? new Thickness(15, 8, 15, 8) : new Thickness(10, 5, 10, 5), Child = text };
    var ring = new Border { CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(2), Margin = new Thickness(-3), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    ring.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Game");
    var holder = new Grid(); holder.Children.Add(pill); holder.Children.Add(ring);
    Tint.OnGame(pill, (game, line, surface) => {
      var textColour = (pill.TryFindResource("Blastlog.Text") as SolidColorBrush)?.Color ?? Colors.White;
      if (trait.Band == "high") { pill.BorderBrush = Frozen(new SolidColorBrush(Tint.Alpha(game, 0.7))); pill.Background = Frozen(new SolidColorBrush(Tint.Alpha(game, 0.16))); }
      else if (trait.Band == "mid") { pill.BorderBrush = Frozen(new SolidColorBrush(Tint.Alpha(game, 0.35))); pill.Background = Frozen(new SolidColorBrush(Tint.Alpha(textColour, 0.04))); }
      else { pill.BorderBrush = Frozen(new SolidColorBrush(line)); pill.Background = Frozen(new SolidColorBrush(Tint.Alpha(textColour, 0.04))); }
    });
    if (trait.Band == "low") holder.Opacity = 0.72;
    var b = new Button { Content = holder, Tag = trait, Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, MinHeight = 32, FocusVisualStyle = (Style)Kit.Styles["Blastlog.FocusRing"] };
    b.Template = PlainTemplate();
    b.ToolTip = trait.Reasons.FirstOrDefault(r => r.Kind != "source") is TraitReason first ? "Why: " + first.Text : $"{trait.Label}: {trait.StrengthWord} evidence";
    AutomationProperties.SetName(b, $"{trait.Label}, {trait.StrengthWord} evidence. Why it’s here");
    return b;
  }
  static void SetChipOpen(Button chip, bool open) { if (chip.Content is Grid g && g.Children.Count > 1) g.Children[1].Visibility = open ? Visibility.Visible : Visibility.Collapsed; }

  /// <summary>Why a trait is on this game: Blastlog's own words (players never see store labels), See games with →, Flag it.</summary>
  static FrameworkElement WhyPanel(FingerprintTrait trait, FingerprintOptions options, Action close) {
    var box = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8) };
    box.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2"); box.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    AutomationProperties.SetName(box, $"Why “{trait.Label}” is here");
    var stack = new StackPanel();
    var head = new DockPanel();
    var x = LinkButton("✕", close); x.Padding = new Thickness(10, 0, 10, 0); AutomationProperties.SetName(x, "Close"); DockPanel.SetDock(x, Dock.Right); head.Children.Add(x);
    var title = Kit.Text($"Why “{trait.Label}” is here", "Blastlog.Small"); title.FontWeight = FontWeights.Bold; title.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text"); title.VerticalAlignment = VerticalAlignment.Center;
    title.Inlines.Add(new System.Windows.Documents.Run($" · {trait.StrengthWord} evidence") { FontWeight = FontWeights.Normal }.Also(r => r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.TextMuted")));
    head.Children.Add(title); stack.Children.Add(head);
    var said = trait.Reasons.Where(r => r.Kind != "source").ToList();
    if (said.Count == 0) stack.Children.Add(Kit.Text("Blastlog read it from what’s written about this game.", "Blastlog.Small"));
    foreach (var r in said) {
      var line = new WrapPanel { Margin = new Thickness(0, 3, 0, 3) };
      var kind = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
      Tint.OnGame(kind, (g, l, s) => kind.Background = Frozen(new SolidColorBrush(Tint.Alpha((kind.TryFindResource("Blastlog.Text") as SolidColorBrush)?.Color ?? Colors.White, 0.07))));
      kind.Child = Kit.Text(ReasonLabel.TryGetValue(r.Kind ?? "", out var k) ? k : "Blastlog", "Blastlog.Small");
      var row = new DockPanel(); DockPanel.SetDock(kind, Dock.Left); row.Children.Add(kind);
      row.Children.Add(Kit.Text(r.Text, "Blastlog.Small").Also(t => t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text")));
      stack.Children.Add(row.Also(d => d.Margin = new Thickness(0, 4, 0, 2)));
    }
    var links = new WrapPanel { Margin = new Thickness(-10, 4, 0, 0) };
    if (options.ServerUrl != null && trait.Id != null) links.Children.Add(LinkButton($"See games with “{trait.Label}” →", () => UiKit.Open(options.ServerUrl + "/traits/" + trait.Id), game: true));
    if (options.SiteGameUrl != null) links.Children.Add(LinkButton("Flag it on Blastlog ↗", () => UiKit.Open(options.SiteGameUrl)));
    stack.Children.Add(links);
    box.Child = stack;
    return box;
  }

  static FrameworkElement Legend() {
    var wrap = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
    foreach (var pair in new[] { new[] { "high", "Strong evidence" }, new[] { "mid", "Moderate" }, new[] { "low", "Light" } }) {
      var band = pair[0]; var word = pair[1];
      var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 4) };
      var dot = new StrengthDot { Band = band, Width = 6, Height = 6, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }; p.Children.Add(dot);
      p.Children.Add(Kit.Text(word, "Blastlog.Small")); wrap.Children.Add(p);
    }
    wrap.Children.Add(Kit.Text("Tap a group to see every trait, then a trait to see why it’s here or flag it.", "Blastlog.Small"));
    return wrap;
  }

  static FrameworkElement OnlineNote(List<OnlineEnded> items) {
    var box = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 10, 0, 10) };
    box.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line");
    var stack = new StackPanel();
    var ended = items.Where(i => !i.Upcoming).ToList(); var ending = items.Where(i => i.Upcoming).ToList();
    if (ended.Count > 0) {
      var when = When(ended.FirstOrDefault(i => i.EndedOn != null)?.EndedOn);
      var t = Kit.Text("", "Blastlog.Small"); t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text");
      t.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run($"Online play ended{(when != null ? $" ({when})" : "")}:")));
      t.Inlines.Add(new System.Windows.Documents.Run($" {string.Join(", ", ended.Select(i => i.Label))} no longer count for this game ({(OnlineHow.TryGetValue(ended[0].How ?? "", out var how) ? how : ended[0].How)})."));
      stack.Children.Add(t);
    }
    if (ending.Count > 0) {
      var t = Kit.Text("", "Blastlog.Small"); t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text");
      t.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run($"Online play ends {Day(ending[0].EffectiveFrom)}:")));
      t.Inlines.Add(new System.Windows.Documents.Run($" {string.Join(", ", ending.Select(i => i.Label))} stop counting then."));
      stack.Children.Add(t);
    }
    var reason = items.FirstOrDefault(i => !string.IsNullOrEmpty(i.Reason))?.Reason;
    if (reason != null) stack.Children.Add(Kit.Text(reason, "Blastlog.Small"));
    box.Child = stack; return box;
  }
  static string When(string date) => DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? (date.EndsWith("-01-01") ? d.ToString("yyyy", CultureInfo.CurrentCulture) : d.ToString("MMM yyyy", CultureInfo.CurrentCulture)) : null;
  static string Day(string date) => DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) : date;

  static FrameworkElement Disclosure(string label, Func<FrameworkElement> content) {
    var stack = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
    var host = new ContentControl();
    Button button = null;
    button = LinkButton("▸ " + label, () => { var open = host.Content == null; host.Content = open ? content() : null; button.Content = (open ? "▾ " : "▸ ") + label; });
    button.Margin = new Thickness(-10, 0, 0, 0);
    button.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text");
    stack.Children.Add(button); stack.Children.Add(host);
    return stack;
  }
  static FrameworkElement NoteRow(FingerprintNote n) {
    var s = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
    var t = Kit.Text(n.Label ?? "", "Blastlog.Small"); t.FontWeight = FontWeights.Bold; t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Text");
    if (!string.IsNullOrEmpty(n.Detail)) t.Inlines.Add(new System.Windows.Documents.Run(" · " + n.Detail) { FontWeight = FontWeights.Normal }.Also(r => r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Blastlog.TextMuted")));
    s.Children.Add(t);
    if (!string.IsNullOrEmpty(n.Description)) s.Children.Add(Kit.Text(n.Description, "Blastlog.Small"));
    return s;
  }
  static TextBlock Note(string text) => Kit.Text(text, "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 10, 0, 0));

  // ---- Small shared bits (public so other screens can match the fingerprint's look) ----
  /// <summary>A muted text button (44px), optionally in the game's words colour.</summary>
  public static Button LinkButton(string label, Action onClick, bool game = false) {
    var b = new Button { Content = label, Style = Kit.Style("Blastlog.TextButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0) };
    if (game) b.SetResourceReference(Control.ForegroundProperty, "Blastlog.GameText");
    b.Click += (s, e) => { try { onClick(); } catch (Exception ex) { Log.Error(ex, "Blastlog link " + label); } };
    return b;
  }
  /// <summary>A small ghost button for card headers.</summary>
  public static Button SmallButton(string label, Action onClick) {
    var b = new Button { Content = label, Style = Kit.Style("Blastlog.Ghost"), MinHeight = 44, FontSize = 14, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0) };
    b.Click += (s, e) => { try { onClick(); } catch (Exception ex) { Log.Error(ex, "Blastlog button " + label); } };
    return b;
  }
  static ControlTemplate plain;
  /// <summary>A button with no chrome of its own (its content draws everything), keeping the focus ring.</summary>
  public static ControlTemplate PlainTemplate() {
    if (plain != null) return plain;
    var factory = new FrameworkElementFactory(typeof(Border)); factory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
    var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
    factory.AppendChild(content);
    plain = new ControlTemplate(typeof(Button)) { VisualTree = factory }; plain.Seal();
    return plain;
  }
  static void ThinStroke(FrameworkElement icon, double thickness, string brushKey) {
    if (icon is Viewbox v && v.Child is Canvas c && c.Children.Count > 0 && c.Children[0] is System.Windows.Shapes.Path p) {
      BindingOperationsClear(p); p.StrokeThickness = thickness; p.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey);
    }
  }
  static void BindingOperationsClear(DependencyObject d) => System.Windows.Data.BindingOperations.ClearBinding(d, System.Windows.Shapes.Shape.StrokeProperty);
  static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

/// <summary>
/// Game-colour mixing for the bits the site draws with color-mix(): calls back with the element's current Blastlog.Game,
/// Blastlog.Line and Blastlog.Surface colours now and whenever they change (the selected game, a card's GameScope, the theme).
/// </summary>
public static class Tint {
  static readonly DependencyProperty WatchProperty = DependencyProperty.RegisterAttached("Watch", typeof(object), typeof(Tint), new PropertyMetadata(null, Changed));
  static readonly DependencyProperty CallbackProperty = DependencyProperty.RegisterAttached("Callback", typeof(Action<Color, Color, Color>), typeof(Tint));
  public static void OnGame(FrameworkElement element, Action<Color, Color, Color> apply) {
    element.SetValue(CallbackProperty, apply);
    element.SetResourceReference(WatchProperty, "Blastlog.Game");
    // Line and surface rarely change, but the theme can swap them; re-run once the element is in the tree too.
    element.Loaded += (s, e) => Run(element);
  }
  static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) { if (d is FrameworkElement f) Run(f); }
  static void Run(FrameworkElement f) {
    try {
      var apply = f.GetValue(CallbackProperty) as Action<Color, Color, Color>; if (apply == null) return;
      var game = (f.GetValue(WatchProperty) as SolidColorBrush)?.Color ?? (Color)ColorConverter.ConvertFromString(GameColour.BrandGreen);
      var line = (f.TryFindResource("Blastlog.Line") as SolidColorBrush)?.Color ?? Color.FromRgb(0x2a, 0x2a, 0x31);
      var surface = (f.TryFindResource("Blastlog.Surface") as SolidColorBrush)?.Color ?? Color.FromRgb(0x15, 0x15, 0x19);
      apply(game, line, surface);
    } catch (Exception ex) { LogManager.GetLogger().Error(ex, "Blastlog tint"); }
  }
  /// <summary>color-mix(in srgb, a k, b).</summary>
  public static Color Mix(Color a, Color b, double k) => Color.FromRgb((byte)Math.Round(a.R * k + b.R * (1 - k)), (byte)Math.Round(a.G * k + b.G * (1 - k)), (byte)Math.Round(a.B * k + b.B * (1 - k)));
  /// <summary>color-mix(in srgb, c k, transparent).</summary>
  public static Color Alpha(Color c, double k) => Color.FromArgb((byte)Math.Round(255 * k), c.R, c.G, c.B);
  /// <summary>The scrim token (black in dark, white in light) at <paramref name="k"/>.</summary>
  public static Color Scrim(double k) => Alpha((Color)ColorConverter.ConvertFromString(Tokens.For(ThemeBridge.CurrentMode)["scrim"]), k);
}

/// <summary>A strength dot (design.md 8): solid strong, ring moderate, faint ring light.</summary>
public sealed class StrengthDot : FrameworkElement {
  public string Band { get; set; } = "high";
  public static readonly DependencyProperty BrushProperty = DependencyProperty.Register("Brush", typeof(Brush), typeof(StrengthDot), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
  public StrengthDot() { SetResourceReference(BrushProperty, "Blastlog.Text"); }
  protected override void OnRender(DrawingContext dc) => FillTrack.Dot(dc, new Point(ActualWidth / 2, ActualHeight / 2), Math.Min(ActualWidth, ActualHeight) / 2, Band, (GetValue(BrushProperty) as SolidColorBrush)?.Color ?? Colors.White);
}

/// <summary>
/// A fingerprint bar's track (design.md 8; the site's FillTrack): the fill's length is how the area compares with a typical
/// game (half = typical, full = twice typical or more; a 6px stub at least) and names never change it. The strongest traits
/// are written inside the coloured part, as many as fully fit, one line; a bar too short for even one puts them just past
/// its end. "+N more" sits at the far right. Drawn, not laid out, so a resize just redraws.
/// </summary>
public sealed class FillTrack : FrameworkElement {
  public IList<FingerprintTrait> Traits { get; set; } = new List<FingerprintTrait>();
  public double Fill { get; set; }
  public static readonly DependencyProperty GameProperty = DependencyProperty.Register("Game", typeof(Brush), typeof(FillTrack), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
  public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(Brush), typeof(FillTrack), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
  public static readonly DependencyProperty MutedProperty = DependencyProperty.Register("Muted", typeof(Brush), typeof(FillTrack), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
  public static readonly DependencyProperty FontProperty = DependencyProperty.Register("Font", typeof(FontFamily), typeof(FillTrack), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
  const double Pad = 13, Gap = 14, DotSize = 7, DotGap = 7, FillEnd = 12, FontSize = 14;
  public FillTrack() {
    SetResourceReference(GameProperty, "Blastlog.Game"); SetResourceReference(TextProperty, "Blastlog.Text"); SetResourceReference(MutedProperty, "Blastlog.TextMuted"); SetResourceReference(FontProperty, "Blastlog.Font");
    SnapsToDevicePixels = true; MinHeight = 34;
  }
  protected override Size MeasureOverride(Size available) => new Size(double.IsInfinity(available.Width) ? 300 : available.Width, double.IsNaN(Height) ? 40 : Height);

  protected override void OnRender(DrawingContext dc) {
    try {
      double w = ActualWidth, h = ActualHeight; if (w <= 0 || h <= 0) return;
      var game = (GetValue(GameProperty) as SolidColorBrush)?.Color ?? Colors.MediumAquamarine;
      var text = (GetValue(TextProperty) as SolidColorBrush)?.Color ?? Colors.White;
      var muted = GetValue(MutedProperty) as Brush ?? Brushes.Gray;
      var face = new Typeface((GetValue(FontProperty) as FontFamily) ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
      var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
      var shape = new RectangleGeometry(new Rect(0, 0, w, h), 12, 12);
      dc.PushClip(shape);
      dc.DrawRectangle(new SolidColorBrush(Tint.Alpha(text, 0.05)), null, new Rect(0, 0, w, h));
      var fillWidth = Math.Max(6, Math.Max(0, Math.Min(1, Fill)) * w);
      var gradient = new LinearGradientBrush(Tint.Alpha(game, 0.52), Tint.Alpha(game, 0.16), 0);
      dc.DrawRoundedRectangle(gradient, null, new Rect(0, 0, fillWidth, h), 12, 12);

      var names = Traits.Take(14).Select(t => new KeyValuePair<FingerprintTrait, FormattedText>(t, new FormattedText(t.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, new SolidColorBrush(text), dpi))).ToList();
      FormattedText MoreText(int shown) => new FormattedText($"+{Traits.Count - shown} more", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, muted, dpi);
      int Fit(double start, double room) { var x = start; var n = 0; foreach (var pair in names) { var f = pair.Value; var right = x + DotSize + DotGap + f.WidthIncludingTrailingWhitespace; if (right > room) break; n++; x = right + Gap; } return n; }
      var startX = Pad; var shownCount = Fit(startX, fillWidth - FillEnd);
      if (shownCount == 0 && names.Count > 0) {
        // Too short for a name inside: write them just past the fill's end, up to "+N more".
        startX = fillWidth + Pad;
        var worstMore = MoreText(0);
        shownCount = Fit(startX, w - Pad - worstMore.Width - FillEnd);
      }
      var x0 = startX; var mid = h / 2;
      for (var i = 0; i < shownCount; i++) {
        var t = names[i].Key; var f = names[i].Value;
        Dot(dc, new Point(x0 + DotSize / 2, mid), DotSize / 2, t.Band, text);
        dc.DrawText(f, new Point(x0 + DotSize + DotGap, mid - f.Height / 2));
        x0 += DotSize + DotGap + f.WidthIncludingTrailingWhitespace + Gap;
      }
      if (Traits.Count > shownCount) { var more = MoreText(shownCount); dc.DrawText(more, new Point(w - Pad - more.Width, mid - more.Height / 2)); }
      dc.Pop();
    } catch (Exception ex) { LogManager.GetLogger().Error(ex, "Blastlog fill track"); }
  }

  /// <summary>Solid (strong), a ring at 75% (moderate) or at 35% (light), as the site's .fp-dot.</summary>
  public static void Dot(DrawingContext dc, Point centre, double r, string band, Color text) {
    if (band == "high") { dc.DrawEllipse(new SolidColorBrush(text), null, centre, r, r); return; }
    var pen = new Pen(new SolidColorBrush(Tint.Alpha(text, band == "mid" ? 0.75 : 0.35)), 1.5);
    dc.DrawEllipse(null, pen, centre, r - 0.75, r - 0.75);
  }
}
}
