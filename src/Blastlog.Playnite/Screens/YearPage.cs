using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Blastlog.Playnite.Ui;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Year in games (apps/web YearInGames.tsx, /year/:year), natively and read only: the year's card (finished, average,
/// top three, the trait that defined it) with Save the card as a picture, then Month by month, Your top five, How your
/// taste moved and Highlights. Reads GET /sync/v1/year/:year (the site's GET /me/year/:year). Only dated finishes count,
/// and lengths are Blastlog's own, as on the site.
/// </summary>
static class YearPage {
  static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

  public static void Open(BlastlogPlugin plugin, int year) =>
    LinkPages.Show(plugin, "Year in games", close => new View(plugin, year, close), width: 1040, height: 900);

  static string StarText(double? n) => n == null ? "unrated" : Stars(n.Value);
  static string Day(string at) => DateTimeOffset.TryParse(at, out var d) ? d.LocalDateTime.ToString("MMMM d", CultureInfo.CurrentCulture) : "";

  sealed class View : StackPanel {
    readonly BlastlogPlugin plugin; readonly Action close; int year;
    readonly StackPanel body = new StackPanel();
    bool loaded;

    public View(BlastlogPlugin plugin, int year, Action close) {
      this.plugin = plugin; this.year = year; this.close = close;
      MaxWidth = 960; HorizontalAlignment = HorizontalAlignment.Left;
      CollectionUi.WearUsual(this, plugin);
      Children.Add(body);
      body.Children.Add(T("Looking back at your year…", "Blastlog.Muted"));
      Loaded += (s, e) => { if (!loaded) { loaded = true; Run(Load, "year in games"); } };
    }

    async Task Load() {
      if (!plugin.Configured) { Show(Hero("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see your year.", null)); return; }
      var view = (await plugin.Mirror("year/" + year.ToString(CultureInfo.InvariantCulture), false)).Value;
      if (!(view?["card"] is JObject)) {
        var hero = Hero("Your year couldn't be opened. Check your connection and try again.", null);
        hero.Children.Add(Kit.Button("Try again", async () => { var v = await plugin.Mirror("year/" + year.ToString(CultureInfo.InvariantCulture), true); if (v.Value?["card"] is JObject) Build(v.Value); }, "Blastlog.Ghost")
          .Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 12, 0, 0); }));
        Show(hero); return;
      }
      Build(view);
    }

    void Show(UIElement content) { body.Children.Clear(); body.Children.Add(content); }

    StackPanel Hero(string lede, List<int> years) {
      var hero = Stack(new Thickness(0, 0, 0, 22),
        T("Year in games", "Blastlog.Small", "Blastlog.GameText", 0, FontWeights.Bold),
        T($"Your {year} in games", "Blastlog.PageTitle").Also(t => t.Margin = new Thickness(0, 2, 0, 0)),
        T(lede, "Blastlog.Muted", null, 17).Also(t => { t.Margin = new Thickness(0, 8, 0, 0); t.MaxWidth = 720; t.HorizontalAlignment = HorizontalAlignment.Left; t.TextWrapping = TextWrapping.Wrap; }));
      if (years != null && years.Count > 1) {
        var pick = new ComboBox { MinWidth = 120, MinHeight = 44, Margin = new Thickness(10, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var y in years) pick.Items.Add(y);
        pick.SelectedItem = year;
        pick.SelectionChanged += (s, e) => { if (pick.SelectedItem is int y && y != year) { year = y; Run(Load, "year pick"); } };
        hero.Children.Add(Row(T("Year", "Blastlog.Body").Also(t => t.VerticalAlignment = VerticalAlignment.Center), pick).Also(r => r.Margin = new Thickness(0, 14, 0, 0)));
      }
      return hero;
    }

    void Build(JObject v) {
      body.Children.Clear();
      var card = (JObject)v["card"];
      var finished = I(card, "finished"); var average = D(card, "averageRating");
      var lede = finished > 0 ? $"{Plural(finished, "game")} finished{(average != null ? $", rated {StarText(average)} on average" : "")}."
        : "No finished games with a date this year yet. Finishes count once they have a finish date.";
      var years = Arr(v["years"]).Select(y => (int)y).ToList();
      body.Children.Add(Hero(lede, years));
      body.Children.Add(ShareCard(card));
      var d = v["details"] as JObject;
      if (d == null) return;
      var chapter = 0;
      string Next() => (++chapter).ToString("00", Inv);
      body.Children.Add(Chapter(Next(), "Month by month", "Games you finished each month, and how many you rated.", MonthBars(Arr(d["byMonth"]))));
      body.Children.Add(Chapter(Next(), "Your top five", "Your highest-rated finishes of the year.", Picks(Arr(d["top"]))));
      var taste = d["taste"] as JObject; var from = S(taste, "from"); var to = S(taste, "to");
      body.Children.Add(Chapter(Next(), "How your taste moved", from != null && to != null && from != to
          ? $"From your first taste snapshot of the year ({Day(from)}) to your latest ({Day(to)})."
          : "Blastlog writes your taste down every week; this fills in once there are two snapshots this year.",
        Stack(null, Moves(Arr(taste?["rose"]), Arr(taste?["fell"])), Link("Taste history  ↗", () => Nav.Site(plugin, "/profile/history")).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(-6, 12, 0, 0); }))));
      body.Children.Add(Chapter(Next(), "Highlights", "Your best match, Blastlog's biggest miss, and your longest and shortest finishes.", Highlights(d)));
    }

    /// <summary>The share card (the site's .yr-card) and Save the card: the card drawn to a PNG at twice its size.</summary>
    UIElement ShareCard(JObject card) {
      var inner = Stack(new Thickness(36, 30, 36, 30));
      inner.Children.Add(T($"My {I(card, "year")} in games", "Blastlog.Body", null, 30, FontWeights.ExtraBold));
      var big = PnKit.Line("Blastlog.Body", PnKit.Run(I(card, "finished").ToString(Inv), null, FontWeights.ExtraBold).Also(r => r.FontSize = 44), PnKit.Run(" finished", null, FontWeights.Bold));
      if (D(card, "averageRating") != null) big.Inlines.Add(PnKit.Run($" · {StarText(D(card, "averageRating"))} average", "Blastlog.TextMuted"));
      big.FontSize = 22; big.Margin = new Thickness(0, 14, 0, 0);
      inner.Children.Add(big);
      var top = Arr(card["top"]);
      if (top.Count > 0) {
        inner.Children.Add(T("Top three", "Blastlog.Small", "Blastlog.TextMuted", 15, FontWeights.Bold).Also(t => t.Margin = new Thickness(0, 18, 0, 6)));
        var i = 0;
        foreach (var p in top) inner.Children.Add(PnKit.Line("Blastlog.Body", PnKit.Run($"{++i}. "), PnKit.Run(S(p, "title") ?? "", null, FontWeights.Bold), PnKit.Run("  " + StarText(D(p, "rating")), "Blastlog.Star")).Also(t => { t.FontSize = 18; t.Margin = new Thickness(0, 2, 0, 0); }));
      }
      var trait = card["definingTrait"] as JObject;
      if (trait != null) {
        inner.Children.Add(T("The trait that defined my year", "Blastlog.Small", "Blastlog.TextMuted", 15, FontWeights.Bold).Also(t => t.Margin = new Thickness(0, 18, 0, 4)));
        inner.Children.Add(T(S(trait, "label"), "Blastlog.Body", "Blastlog.GameText", 26, FontWeights.ExtraBold));
      }
      inner.Children.Add(T("blastlog.games", "Blastlog.Body", "Blastlog.Brand", 16, FontWeights.Bold).Also(t => t.Margin = new Thickness(0, 22, 0, 0)));
      var bar = new Border { Height = 8, CornerRadius = new CornerRadius(18, 18, 0, 0) };
      bar.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game");
      var stack = Stack(null, bar, inner);
      var shape = Box(stack, "Blastlog.Surface", 18, new Thickness(0), "Blastlog.Line");
      shape.Width = 480; shape.HorizontalAlignment = HorizontalAlignment.Left;

      var note = new ContentControl();
      var save = Kit.Button("Save the card", () => {
        try {
          var file = plugin.Api.Dialogs.SaveFile("PNG picture|*.png");
          if (string.IsNullOrEmpty(file)) return;
          var scale = 2.0;
          var bitmap = new RenderTargetBitmap((int)(shape.ActualWidth * scale), (int)(shape.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
          bitmap.Render(shape);
          var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
          using (var stream = File.Create(file)) encoder.Save(stream);
          note.Content = T("Saved.", "Blastlog.Muted").Also(t => t.Margin = new Thickness(0, 8, 0, 0));
        } catch (Exception e) {
          global::Playnite.SDK.LogManager.GetLogger().Warn(e, "Blastlog: save the year card");
          note.Content = CollectionUi.Error("That didn't work. Try again.");
        }
      });
      save.HorizontalAlignment = HorizontalAlignment.Left; save.Margin = new Thickness(0, 14, 0, 0);
      return Stack(new Thickness(0, 0, 0, 8), shape, save, note);
    }

    /// <summary>One bar per month with its count on top; hovering a month gives its numbers (the site's .yr-months).</summary>
    static UIElement MonthBars(JArray months) {
      var max = Math.Max(1, months.Select(m => I(m, "finished")).DefaultIfEmpty(0).Max());
      var grid = new Grid { Height = 220, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
      for (var i = 0; i < 12; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      grid.Width = 860;
      foreach (var m in months) {
        var index = I(m, "month") - 1; if (index < 0 || index > 11) continue;
        var finished = I(m, "finished"); var rated = I(m, "rated");
        var column = new DockPanel { Margin = new Thickness(4, 0, 4, 0), Background = Brushes.Transparent, ToolTip = $"{Months[index]}: {finished} finished, {rated} rated" };
        var label = T(Months[index], "Blastlog.Small", "Blastlog.TextMuted", 14).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 6, 0, 0); });
        DockPanel.SetDock(label, Dock.Bottom); column.Children.Add(label);
        var slot = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        if (finished > 0) {
          slot.Children.Add(T(finished.ToString(Inv), "Blastlog.Body", null, 14, FontWeights.Bold).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 0, 0, 4); }));
          var bar = new Border { Height = Math.Max(4, 160.0 * finished / max), CornerRadius = new CornerRadius(8, 8, 3, 3) };
          bar.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game");
          slot.Children.Add(bar);
        }
        column.Children.Add(slot);
        Grid.SetColumn(column, index); grid.Children.Add(column);
      }
      var described = months.Where(m => I(m, "finished") > 0).Select(m => $"{Months[Math.Max(0, Math.Min(11, I(m, "month") - 1))]}: {I(m, "finished")} finished, {I(m, "rated")} rated").ToList();
      System.Windows.Automation.AutomationProperties.SetName(grid, described.Count > 0 ? string.Join("; ", described) : "No finishes this year.");
      return grid;
    }

    UIElement Picks(JArray picks) {
      if (picks.Count == 0) return T("Nothing finished yet.", "Blastlog.Muted");
      var row = new WrapPanel();
      var i = 0;
      foreach (var p in picks) {
        var tile = Stack(new Thickness(0, 0, 18, 18), Cover(plugin, S(p, "coverUrl"), S(p, "title"), 132),
          T($"{++i}. {S(p, "title")}", "Blastlog.Body", null, 15, FontWeights.Bold).Also(t => { t.MaxWidth = 132; t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 8, 0, 0); }),
          T(StarText(D(p, "rating")), "Blastlog.Body", "Blastlog.Star", 14, FontWeights.Bold));
        row.Children.Add(tile);
      }
      return row;
    }

    /// <summary>Traits that rose and fell, each on a −1 (dislike) to 1 (like) track: hollow where it was, filled where it is.</summary>
    static UIElement Moves(JArray rose, JArray fell) {
      if (rose.Count == 0 && fell.Count == 0) return T("Nothing moved much yet.", "Blastlog.Muted");
      var wrap = new WrapPanel();
      void Group(string title, JArray moves) {
        if (moves.Count == 0) return;
        var group = Stack(new Thickness(0, 0, 40, 10), T(title, "Blastlog.CardTitle").Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
        foreach (var m in moves) {
          double from = D(m, "from") ?? 0, to = D(m, "to") ?? 0;
          var label = T(S(m, "label"), "Blastlog.Body", null, 15).Also(t => { t.Width = 200; t.TextTrimming = TextTrimming.CharacterEllipsis; t.VerticalAlignment = VerticalAlignment.Center; });
          var track = new Canvas { Width = 220, Height = 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
          var line = new Rectangle { Width = 220, Height = 4, RadiusX = 2, RadiusY = 2 }; line.SetResourceReference(Shape.FillProperty, "Blastlog.Surface2");
          Canvas.SetTop(line, 9); track.Children.Add(line);
          var mid = new Rectangle { Width = 2, Height = 14 }; mid.SetResourceReference(Shape.FillProperty, "Blastlog.Line");
          Canvas.SetLeft(mid, 109); Canvas.SetTop(mid, 4); track.Children.Add(mid);
          var was = new Ellipse { Width = 14, Height = 14, StrokeThickness = 2 }; was.SetResourceReference(Shape.StrokeProperty, "Blastlog.TextMuted");
          Canvas.SetLeft(was, (from + 1) / 2 * 206); Canvas.SetTop(was, 4); track.Children.Add(was);
          var now = new Ellipse { Width = 14, Height = 14 }; now.SetResourceReference(Shape.FillProperty, "Blastlog.Game");
          Canvas.SetLeft(now, (to + 1) / 2 * 206); Canvas.SetTop(now, 4); track.Children.Add(now);
          var row = Row(label, track).Also(r => r.Margin = new Thickness(0, 0, 0, 8));
          System.Windows.Automation.AutomationProperties.SetName(row, $"{S(m, "label")}: from {from.ToString("0.00", Inv)} to {to.ToString("0.00", Inv)}, on a scale from −1 (dislike) to 1 (like)");
          group.Children.Add(row);
        }
        wrap.Children.Add(group);
      }
      Group("Rose", rose); Group("Fell", fell);
      return wrap;
    }

    static UIElement Highlights(JObject d) {
      var list = Stack(null);
      void Item(string label, string value) {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        var l = T(label, "Blastlog.Muted").Also(t => { t.Width = 220; t.FontSize = 15; });
        DockPanel.SetDock(l, Dock.Left); row.Children.Add(l);
        row.Children.Add(T(value, "Blastlog.Body", null, 16, FontWeights.Bold).Also(t => t.TextWrapping = TextWrapping.Wrap));
        list.Children.Add(row);
      }
      var best = d["bestMatch"] as JObject;
      Item("Best match", best != null ? $"{S(best, "name")}, {I(best, "score")}%" : "Add a friend to find out");
      var wrong = d["mostWrong"] as JObject;
      Item("Blastlog's biggest miss", wrong != null ? $"{S(wrong, "title")}: guessed {StarText(D(wrong, "predicted"))}, you gave it {StarText(D(wrong, "rating"))}" : "No real misses this year");
      if (d["longest"] is JObject longest) Item("Longest finish", $"{S(longest, "title")}, about {I(longest, "hours")} hours");
      if (d["shortest"] is JObject shortest) Item("Shortest finish", $"{S(shortest, "title")}, about {I(shortest, "hours")} hours");
      return list;
    }
  }
}
}
