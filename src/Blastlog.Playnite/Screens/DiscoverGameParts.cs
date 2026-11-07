using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;

namespace Blastlog.Playnite.Screens {
/// <summary>The Discover game page's answers and cards under the hero (apps/web DiscoverGame.tsx, GameMore.tsx).</summary>
sealed partial class DiscoverGameView {
  static readonly Dictionary<string, string> AnswerText = new Dictionary<string, string> { ["want"] = "on your wishlist", ["played"] = "in your library as played",
    ["owned"] = "in your backlog", ["not_for_me"] = "marked Not for me", ["not_played"] = "marked Haven’t played it" };

  // ---- Answers, Undo and Why not? ----
  async Task Answered(DiscoverAnswered saved) { Changed = true; notice = saved; noticeText = saved.Text; await Load(true); }

  Border NoticeBar() {
    var n = notice;
    var buttons = new List<Button>();
    if (n?.Played == true && n.EntryId != null) buttons.Add(Kit.Button("Rate and review it", () => Nav.Review(plugin, n.EntryId, () => { Changed = true; _ = Load(true); })));
    if (n?.UndoId != null) buttons.Add(Kit.Button("Undo", async () => await Undo(n.UndoId), "Blastlog.Ghost"));
    return PnKit.Notice(noticeText, buttons, () => { notice = null; noticeText = null; Build(); });
  }

  async Task Undo(string answerId) {
    var result = await plugin.Write($"discover/answers/{answerId}/undo", new JObject());
    if (result == null) return;
    Changed = true; notice = null;
    noticeText = result.B("entryKept") ? "Undone. It stays in your library because you changed it since; remove it there if you want." : "Undone.";
    await Load(true);
  }

  /// <summary>"You said: on your wishlist. Undo" (Why not? comes with Not for me itself, as on the site).</summary>
  FrameworkElement AnsweredLine(JObject answer) {
    var line = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
    var kind = answer.S("answer"); var id = answer.S("id");
    line.Children.Add(PnKit.Text($"You said: {(AnswerText.TryGetValue(kind ?? "", out var words) ? words : kind)}.", "Blastlog.Body").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.FontSize = 15; }));
    if (id != null) line.Children.Add(Fingerprint.LinkButton("Undo", async () => { try { await Undo(id); } catch (Exception e) { Log.Error(e, "Blastlog Discover undo"); } }, game: true)
      .Also(b => AutomationProperties.SetName(b, "Undo your answer on " + Title)));
    return line;
  }

  // ---- Cards ----
  static Border Card(string heading, out StackPanel body, bool game = false) {
    body = new StackPanel();
    if (heading != null) body.Children.Add(Kit.Text(heading, "Blastlog.CardTitle").Also(t => { t.FontSize = 20; t.FontWeight = FontWeights.ExtraBold; t.Margin = new Thickness(0, 0, 0, 12); }));
    // The site's panel: surface, a 1px line, radius 26, padding 26.
    return Kit.Card(body, game).Also(c => { c.CornerRadius = new CornerRadius(26); c.Padding = new Thickness(26); c.BorderThickness = new Thickness(1); c.Res(Border.BorderBrushProperty, "Blastlog.Line"); });
  }
  static TextBlock Eyebrow(string text) => Kit.Text(text, "Blastlog.Small").Also(t => { t.FontWeight = FontWeights.Bold; t.Margin = new Thickness(0, 0, 0, 6); });

  /// <summary>
  /// Why it fits: "Why it’s on your list" when opened from a Discover shelf, "For you" otherwise (the site, 2026-11-05):
  /// Because you like chips, Watch out risk chips, the early note.
  /// </summary>
  FrameworkElement FitCard() {
    var fit = page["fit"] as JObject;
    var reasons = fit.Strings("reasons"); var watch = fit.Strings("watchOuts"); var ready = fit.B("ready");
    if (reasons.Count == 0 && watch.Count == 0 && ready) return null;
    var card = Card(openedFrom != null ? "Why it’s on your list" : "For you", out var body);
    if (!ready) { body.Children.Add(PnKit.Text("Rate a few more games you’ve finished and Blastlog can say how this one fits your taste.", "Blastlog.Muted")); return card; }
    if (reasons.Count > 0) {
      body.Children.Add(Eyebrow("Because you like"));
      var w = new WrapPanel(); foreach (var r in reasons) w.Children.Add(Kit.Chip(r).Also(c => c.Margin = new Thickness(0, 0, 8, 8))); body.Children.Add(w);
    }
    if (watch.Count > 0) {
      body.Children.Add(Eyebrow("Watch out").Also(t => t.Margin = new Thickness(0, reasons.Count > 0 ? 8 : 0, 0, 6)));
      var w = new WrapPanel(); foreach (var r in watch) w.Children.Add(RiskChip(r)); body.Children.Add(w);
    }
    if (fit.B("early")) body.Children.Add(PnKit.Text(fit.B("upcoming") ? "It isn’t out yet, so this comes from previews and counts for a little less." : "Fingerprinting in progress, confirming data after release.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
    return card;
  }
  /// <summary>A risk chip: dashed, in the danger colour (the site's .discover-chips.risk; design.md 7).</summary>
  static FrameworkElement RiskChip(string text) {
    var chip = Kit.Chip(text, neutral: true); chip.BorderThickness = new Thickness(0); chip.Margin = new Thickness(0);
    if (chip.Child is TextBlock t) t.SetResourceReference(TextBlock.ForegroundProperty, "Blastlog.Danger");
    var dashed = new Rectangle { RadiusX = 14, RadiusY = 14, StrokeDashArray = new DoubleCollection { 4, 3 }, StrokeThickness = 1, IsHitTestVisible = false };
    dashed.SetResourceReference(Shape.StrokeProperty, "Blastlog.Danger");
    var g = new Grid { Margin = new Thickness(0, 0, 8, 8) }; g.Children.Add(chip); g.Children.Add(dashed);
    return g;
  }

  /// <summary>Friends who played it: name and stars, then their review (two lines, opens in full) or a line from it.</summary>
  FrameworkElement FriendsCard() {
    var friends = page.Objects("friends");
    if (friends.Count == 0) return null;
    var card = Card("Friends who played it", out var body);
    foreach (var f in friends) {
      var name = f.S("name") ?? "A friend"; var rating = f.D("rating"); var review = f.S("review")?.Trim(); var line = f.S("line");
      var row = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
      var head = PnKit.Line("Blastlog.Body", PnKit.Run(name + " ", null, FontWeights.Bold));
      if (rating != null) head.Inlines.Add(PnKit.Run(Stars(rating.Value), "Blastlog.Star", FontWeights.Bold));
      row.Children.Add(head);
      if (!string.IsNullOrEmpty(review)) row.Children.Add(Review(review, name));
      else if (!string.IsNullOrEmpty(line)) row.Children.Add(new Border { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2), Margin = new Thickness(0, 6, 0, 0),
        Child = PnKit.Line("Blastlog.Body", PnKit.Run(PnKit.Quoted(line), italic: true)).Also(t => t.TextWrapping = TextWrapping.Wrap) }.Res(Border.BorderBrushProperty, "Blastlog.GameEdge"));
      body.Children.Add(row);
    }
    return card;
  }
  static string Stars(double r) => "★ " + (r % 1 == 0 ? r.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : r.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
  static FrameworkElement Review(string review, string name) {
    var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
    var quote = Kit.Text(review).Also(t => { t.FontStyle = FontStyles.Italic; t.TextWrapping = TextWrapping.Wrap; t.LineHeight = 24; t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; t.MaxHeight = 48; t.TextTrimming = TextTrimming.WordEllipsis; });
    panel.Children.Add(quote);
    if (review.Length > 160) {
      Button toggle = null;
      toggle = Fingerprint.LinkButton("Read the full review ▾", () => { var open = quote.MaxHeight < 1e5; quote.MaxHeight = open ? double.PositiveInfinity : 48; toggle.Content = open ? "Show less ▴" : "Read the full review ▾"; });
      toggle.Margin = new Thickness(-10, 0, 0, 0); toggle.HorizontalAlignment = HorizontalAlignment.Left; AutomationProperties.SetName(toggle, $"Read {name}’s full review");
      panel.Children.Add(toggle);
    }
    return panel;
  }

  /// <summary>Accessibility: the features the developer lists on the Steam store page, as plain chips; hidden when none.</summary>
  FrameworkElement AccessibilityCard() {
    var features = info.Strings("accessibility");
    if (features.Count == 0) return null;
    var card = Card("Accessibility", out var body);
    var w = new WrapPanel(); foreach (var f in features) w.Children.Add(Kit.Chip(f, neutral: true).Also(c => c.Margin = new Thickness(0, 0, 8, 8)));
    AutomationProperties.SetName(w, "Accessibility features listed for " + Title);
    body.Children.Add(w);
    body.Children.Add(PnKit.Text("From the Steam store, as listed by the developer.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 4, 0, 0)));
    return card;
  }

  /// <summary>
  /// More about the game (the site's GameMore): platforms and genres, the trailers (they play on YouTube, in the browser,
  /// only when picked) and screenshots (open larger in the browser), with IGDB's credit.
  /// </summary>
  FrameworkElement MoreCard() {
    var platforms = info.Strings("platforms"); var genres = info.Strings("genres");
    var shots = info.Strings("screenshots").Where(IsIgdb).ToList(); var videos = info.Objects("videos").Where(v => !string.IsNullOrEmpty(v.S("youtubeId"))).ToList();
    if (videos.Count == 0 && shots.Count == 0 && platforms.Count == 0) return null;
    var card = Card(videos.Count > 0 || shots.Count > 0 ? "Trailers and screenshots" : "Where to play it", out var body);
    if (platforms.Count > 0) body.Children.Add(PnKit.Line("Blastlog.Body", PnKit.Run("On ", "Blastlog.TextMuted"), PnKit.Run(string.Join(", ", platforms))).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 4); }));
    if (genres.Count > 0) body.Children.Add(PnKit.Line("Blastlog.Body", PnKit.Run("Genres ", "Blastlog.TextMuted"), PnKit.Run(string.Join(", ", genres))).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 4); }));
    if (videos.Count > 0) {
      var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
      for (var i = 0; i < videos.Count; i++) {
        var v = videos[i]; var url = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(v.S("youtubeId"));
        var label = i == 0 ? $"▶ {v.S("name") ?? "Trailer"} · plays on YouTube ↗" : (v.S("name") ?? "Video") + " ↗";
        row.Children.Add(Kit.Button(label, () => UiKit.Open(url), i == 0 ? "Blastlog.Primary" : "Blastlog.Ghost").Also(b => { b.Margin = new Thickness(0, 0, 8, 8); AutomationProperties.SetName(b, "Play " + (v.S("name") ?? "the trailer") + " on YouTube"); }));
      }
      body.Children.Add(row);
    }
    if (shots.Count > 0) {
      const int shown = 6;
      var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
      for (var i = 0; i < Math.Min(shown, shots.Count); i++) {
        var url = shots[i]; var more = i == shown - 1 ? shots.Count - shown : 0; var n = i + 1;
        row.Children.Add(Shot(url, more, n));
      }
      body.Children.Add(row);
    }
    body.Children.Add(PnKit.Text("Game details, screenshots and trailers from IGDB.com.", "Blastlog.Small").Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
    return card;
  }
  FrameworkElement Shot(string url, int more, int n) {
    var full = Artwork.NormalizedLegacyUrl(url, "1080p");
    var box = new Border { Width = 220, Height = 124, CornerRadius = new CornerRadius(14), ClipToBounds = true }.Res(Border.BackgroundProperty, "Blastlog.Surface2");
    var layers = new Grid(); layers.Children.Add(box);
    if (more > 0) layers.Children.Add(new Border { CornerRadius = new CornerRadius(14), Child = PnKit.Text("+" + more, "Blastlog.Section").Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0); }) }
      .Also(b => b.Background = new SolidColorBrush(Tint.Scrim(0.55)).Also(x => x.Freeze())));
    var button = PnKit.Bare(layers, () => UiKit.Open(full), more > 0 ? $"Show screenshot {n} larger, {more} more after it" : $"Show screenshot {n} larger");
    button.Width = 220; button.Margin = new Thickness(0, 0, 10, 10); button.HorizontalAlignment = HorizontalAlignment.Left; button.Cursor = Cursors.Hand;
    LoadShot(Artwork.NormalizedLegacyUrl(url, "screenshot_med"), box);
    return button;
  }
  /// <summary>
  /// More like this (the site's MoreLikeThis): up to 12 unplayed games most like it, six at first, each wearing its own
  /// game. One in Playnite opens Playnite's game page, one in the library but not Playnite opens on Blastlog, any other
  /// opens its own Discover page here.
  /// </summary>
  FrameworkElement MoreLikeThis() {
    var items = like.Objects("items");
    if (items.Count == 0) return null;
    var card = Card("More like " + Title, out var body);
    body.Children.Add(PnKit.Text("Games you haven’t played yet that play the most like it, from your library, your friends’ games and Discover. Your taste breaks close calls.", "Blastlog.Muted")
      .Also(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, -6, 0, 12); }));
    var row = new WrapPanel();
    var shown = 6;
    void Fill() {
      row.Children.Clear();
      foreach (var item in items.Take(shown)) row.Children.Add(LikeTile(item));
      if (items.Count > shown) row.Children.Add(Fingerprint.LinkButton($"Show {items.Count - shown} more", () => { shown = items.Count; Fill(); }, game: true).Also(b => b.VerticalAlignment = VerticalAlignment.Center));
    }
    Fill();
    body.Children.Add(row);
    return card;
  }
  FrameworkElement LikeTile(JObject item) {
    var game = new Blastlog.Sync.Core.Game { Id = item.S("gameId"), Title = item.S("title"), CoverUrl = item.S("coverUrl") };
    var entryId = item.S("entryId"); var inPlaynite = entryId != null ? plugin.GameFor(entryId) : null;
    var stack = new StackPanel { Width = 132 };
    stack.Children.Add(Kit.Cover(plugin, game, 132, ArtworkContext.Thumbnail, 14));
    stack.Children.Add(PnKit.Text(game.Title ?? "", "Blastlog.Small", "Blastlog.Text", FontWeights.Bold).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxHeight = 40; t.TextTrimming = TextTrimming.CharacterEllipsis; t.Margin = new Thickness(0, 8, 0, 0); }));
    var predicted = item.D("predictedRating");
    var source = item.S("source") == "library" ? "Your library" : item.S("source") == "friends" ? "Friends" : "Discover";
    stack.Children.Add(PnKit.Text(predicted != null ? $"{source} · ★ {predicted.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}" : source, "Blastlog.Small"));
    var button = PnKit.Bare(stack, () => {
      if (inPlaynite != null) OpenInPlaynite(inPlaynite.Id);
      else if (entryId != null) UiKit.Open(plugin.ServerUrl + "/library/" + entryId);
      else OpenGame(game.Id);
    }, "Open " + game.Title);
    button.Width = 132; button.Margin = new Thickness(0, 0, 14, 14); button.VerticalAlignment = VerticalAlignment.Top; button.HorizontalAlignment = HorizontalAlignment.Left;
    var reasons = item.Strings("reasons"); var watch = item.S("watchOut"); var shared = item.Strings("shared");
    var tip = string.Join("\n", new[] { shared.Count > 0 ? "Shares: " + string.Join(", ", shared) : null, reasons.Count > 0 ? "Because you like " + string.Join(", ", reasons) : null,
      watch != null ? "Watch out: " + watch : null, inPlaynite != null ? "In your Playnite library" : null }.Where(x => x != null));
    if (tip.Length > 0) button.ToolTip = tip;
    Kit.GameScope(button, plugin, game);
    return button;
  }
  /// <summary>Another game's Discover page in this window, from the top; Back returns to this one (the site's origin).</summary>
  void OpenGame(string nextGameId) {
    trail.Push(Tuple.Create(gameId, openedFrom, Title));
    Go(nextGameId, null);
  }
  /// <summary>Back: the game this one was opened from in this window, else shut the window (back where it was opened).</summary>
  void Back() {
    if (trail.Count == 0) { window.Close(); return; }
    var previous = trail.Pop();
    Go(previous.Item1, previous.Item2);
  }
  string BackLabel => trail.Count > 0 ? "← Back to " + trail.Peek().Item3 : openedFrom != null ? "← Back to Discover" : "← Back";
  readonly Stack<Tuple<string, string, string>> trail = new Stack<Tuple<string, string, string>>();
  void Go(string nextGameId, string shelf) {
    gameId = nextGameId; openedFrom = shelf; notice = null; noticeText = null; worn = false;
    body.Children.Clear(); body.Children.Add(PnKit.Text("Opening the game…", "Blastlog.Muted"));
    PnKit.FindAncestor<ScrollViewer>(this)?.ScrollToTop();
    _ = Load(false);
  }

  async void LoadShot(string url, Border box) {
    try {
      var file = await PnKit.Picture(plugin, url);
      if (file == null) return;
      var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(file); image.DecodePixelWidth = 440; image.EndInit(); image.Freeze();
      box.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
    } catch (Exception e) { Log.Warn("Blastlog: screenshot didn't load: " + e.Message); }
  }
}
}
