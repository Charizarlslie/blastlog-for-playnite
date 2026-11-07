using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using BlastlogGame = Blastlog.Sync.Core.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// A Discover game's own page (apps/web DiscoverGame.tsx), natively, in a Playnite window: the hero (blurred art under a
/// scrim, cover, title, facts, summary, answers with Undo, Play / Install when the game is in Playnite, the price link),
/// then For you, the fingerprint, friends who played it, accessibility and more about the game. A game already in the
/// player's library that's paired in Playnite opens Playnite's own game page instead (the site opens its library page).
/// </summary>
static class DiscoverGamePage {
  static readonly ILogger Log = LogManager.GetLogger();

  /// <summary>
  /// Opens the page. <paramref name="shelf"/> is the Discover shelf it was opened from (each shelf's pickups), null when
  /// opened some other way; <paramref name="done"/> runs after the window closes if anything was answered or undone.
  /// </summary>
  public static void Open(BlastlogPlugin plugin, string gameId, string shelf, Action done) {
    try {
      var window = plugin.Api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = true, ShowMinimizeButton = false });
      var owner = plugin.Api.Dialogs.GetCurrentAppWindow();
      window.Title = "Discover";
      window.Width = Math.Max(760, Math.Min(1180, (owner?.ActualWidth ?? 1200) * 0.86));
      window.Height = Math.Max(600, Math.Min(980, (owner?.ActualHeight ?? 900) * 0.9));
      window.Owner = owner; window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
      var view = new DiscoverGameView(plugin, gameId, shelf, window);
      window.Content = Kit.Page(view);
      window.PreviewKeyDown += (s, e) => { try { if (e.Key == Key.Escape) window.Close(); } catch (Exception ex) { Log.Error(ex, "Blastlog Discover game close"); } };
      window.ShowDialog();
      if (view.AfterClose != null) view.AfterClose();
      if (view.Changed) done?.Invoke();
    } catch (Exception e) { Log.Error(e, "Blastlog Discover game page"); }
  }
}

sealed partial class DiscoverGameView : UserControl {
  static readonly ILogger Log = LogManager.GetLogger();
  readonly BlastlogPlugin plugin; readonly Window window;
  // More like this opens the next game in this same window, so these change.
  string gameId, openedFrom;
  readonly StackPanel body = new StackPanel { MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Left };
  JObject data, page, info, subs, like; bool loaded, worn;
  DiscoverAnswered notice; string noticeText;

  /// <summary>Anything answered or undone here (the page that opened it refreshes).</summary>
  public bool Changed { get; private set; }
  /// <summary>What to do once the window is shut (open the game in Playnite's library, start it).</summary>
  public Action AfterClose { get; private set; }

  public DiscoverGameView(BlastlogPlugin plugin, string gameId, string shelf, Window window) {
    this.plugin = plugin; this.gameId = gameId; openedFrom = shelf; this.window = window;
    Content = body;
    body.Children.Add(PnKit.Text("Opening the game…", "Blastlog.Muted"));
    Loaded += (s, e) => { try { if (!loaded) { loaded = true; _ = Load(false); } } catch (Exception ex) { Log.Error(ex, "Blastlog Discover game"); } };
  }

  async Task Load(bool fresh) {
    try {
      if (!plugin.Configured) { Show(Kit.Card(PnKit.Text("Add your Blastlog sync token in Add-ons, Extension settings, Blastlog to see this game."))); return; }
      var read = plugin.Mirror($"discover/games/{gameId}", fresh); var services = plugin.Mirror("subscriptions", false);
      var similar = plugin.Mirror($"games/{gameId}/like", fresh);
      await Task.WhenAll(read, services, similar);
      data = read.Result.Value; subs = services.Result.Value; like = similar.Result.Value;
      page = data?["page"] as JObject; info = data?["info"] as JObject;
      if (page == null) { Unavailable(); return; }
      // A game that's in the library opens its library page, here Playnite's own game page when it's paired there.
      // Not right after answering here (the site's rule), so the answer and its Undo stay in view.
      var entryId = page.S("entryId");
      var paired = entryId != null ? plugin.GameFor(entryId) : null;
      if (paired != null && !(page["answer"] is JObject) && noticeText == null) { OpenInPlaynite(paired.Id); return; }
      Build();
    } catch (Exception e) { Log.Error(e, "Blastlog Discover game load"); }
  }

  void Show(UIElement content) { body.Children.Clear(); body.Children.Add(content); }

  void Unavailable() {
    var panel = new StackPanel();
    panel.Children.Add(PnKit.Text("This game couldn’t be opened.", "Blastlog.Body", "Blastlog.Danger"));
    panel.Children.Add(Kit.Button("Try again", async () => await Load(true), "Blastlog.Ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 12, 0, 0); }));
    Show(panel);
  }

  void OpenInPlaynite(Guid id) {
    AfterClose = () => { try { plugin.Api.MainView.SwitchToLibraryView(); plugin.Api.MainView.SelectGame(id); } catch (Exception e) { Log.Error(e, "Blastlog open in Playnite"); } };
    window.Close();
  }

  BlastlogGame Game => new BlastlogGame { Id = page?["game"].S("id") ?? gameId, Title = page?["game"].S("title"), CoverUrl = page?["game"].S("coverUrl"), Accent = page?["game"].S("accent") };
  string Title => page?["game"].S("title") ?? "this game";
  bool NotOut { get { var d = page?["game"].S("releaseDate"); return d != null && string.CompareOrdinal(d, GamePage.Today()) > 0; } }
  /// <summary>The shelf it was opened from, or the site's best guess when opened some other way.</summary>
  string Shelf => openedFrom ?? (page.Objects("friends").Count > 0 ? "friends" : page["catalogue"].D("trendingRank") > 0 ? "trending" : NotOut ? "upcoming" : "critics");

  void Build() {
    try {
      body.Children.Clear();
      window.Title = Title;
      // The page wears its game: the page colour set on Blastlog, else its cover's (design.md 2.3).
      if (!worn) { worn = true; Kit.GameScope(body, plugin, Game); }
      body.Children.Add(Hero());
      var fit = FitCard(); if (fit != null) body.Children.Add(fit);
      if (data["fingerprint"] is JObject fingerprint)
        // No Flag it / Suggest a trait here: the site has them on library pages only (SiteGameUrl null hides them).
        body.Children.Add(Fingerprint.Card(FingerprintData.Parse(fingerprint), new FingerprintOptions { ServerUrl = plugin.ServerUrl }));
      var friends = FriendsCard(); if (friends != null) body.Children.Add(friends);
      var access = AccessibilityCard(); if (access != null) body.Children.Add(access);
      var more = MoreCard(); if (more != null) body.Children.Add(more);
      var similar = MoreLikeThis(); if (similar != null) body.Children.Add(similar);
      body.Children.Add(Fingerprint.LinkButton("Open on Blastlog ↗", () => UiKit.Open(plugin.ServerUrl + "/discover/game/" + Game.Id), game: true).Also(b => { b.Margin = new Thickness(-10, 0, 0, 6); b.HorizontalAlignment = HorizontalAlignment.Left; }));
      body.Children.Add(PnKit.Text(plugin.CacheLine(), "Blastlog.Small"));
    } catch (Exception e) { Log.Error(e, "Blastlog Discover game build"); }
  }

  // ---- The hero ----
  FrameworkElement Hero() {
    var game = page["game"] as JObject; var fit = page["fit"] as JObject; var catalogue = page["catalogue"] as JObject;
    var entryId = page.S("entryId"); var answer = page["answer"] as JObject;

    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    var cover = Kit.Cover(plugin, Game, 240, ArtworkContext.Cover, 18);
    cover.VerticalAlignment = VerticalAlignment.Top;
    AutomationProperties.SetName(cover, Title + " cover");
    grid.Children.Add(cover);

    var text = new StackPanel(); Grid.SetColumn(text, 2); grid.Children.Add(text);
    text.Children.Add(PnKit.Text("Discover", "Blastlog.Body", "Blastlog.GameText", FontWeights.Bold));
    text.Children.Add(PnKit.Text(Title, "Blastlog.HeroTitle").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.FontSize = 40; t.Margin = new Thickness(0, 2, 0, 6); }));
    var date = game.S("releaseDate"); var predicted = fit.D("predictedRating"); var acclaim = catalogue.D("acclaim");
    var facts = new[] {
      date != null ? (NotOut ? PnKit.ReleaseLabel(date) : game.S("year")) : null,
      predicted != null ? $"predicted {predicted.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}★" : null,
      fit.B("earlyRead") ? "early read" : null,
      acclaim != null ? $"{(catalogue.S("acclaimKind") == "critics" ? "Critics" : "Rated")} {Math.Round(acclaim.Value)} on IGDB" : null,
      catalogue.D("trendingRank") > 0 ? "Trending this week" : null }.Where(x => !string.IsNullOrEmpty(x)).ToList();
    if (facts.Count > 0) text.Children.Add(PnKit.Text(string.Join(" · ", facts), "Blastlog.Muted"));
    var chip = PlayNextSubscriptions.Chip(subs, Game.Id); if (chip != null) text.Children.Add(chip.Also(c => c.Margin = new Thickness(0, 8, 0, 0)));
    var summary = info.S("summary");
    if (!string.IsNullOrEmpty(summary)) text.Children.Add(Summary(summary));
    if (noticeText != null) text.Children.Add(NoticeBar().Also(n => n.Margin = new Thickness(0, 12, 0, 0)));

    if (entryId != null) {
      var line = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
      line.Children.Add(PnKit.Text("It’s in your library.", "Blastlog.Body").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.FontSize = 15; }));
      var paired = plugin.GameFor(entryId);
      line.Children.Add(Fingerprint.LinkButton(paired != null ? "Open it in Playnite →" : "Open it there ↗", () => { if (paired != null) OpenInPlaynite(paired.Id); else UiKit.Open(plugin.ServerUrl + "/library/" + entryId); }, game: true));
      text.Children.Add(line);
    } else if (answer != null) text.Children.Add(AnsweredLine(answer));

    // One row of buttons: Play / Install (Playnite's), the answers, then the price check.
    var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
    var play = PlayOrInstall(entryId); if (play != null) buttons.Children.Add(play);
    var deal = info.S("dealUrl");
    if (deal != null) buttons.Children.Add(Kit.Button("Compare prices on GG.deals ↗", () => UiKit.Open(deal), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
    if (entryId == null && answer == null) {
      var answers = new DiscoverAnswers(plugin, Game.Id, Title, Shelf, NotOut, Answered) { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
      text.Children.Add(answers);
      if (buttons.Children.Count > 0) buttons.Margin = new Thickness(0, 6, 0, 0);
    }
    if (buttons.Children.Count > 0) text.Children.Add(buttons);

    return Backdrop(grid);
  }

  /// <summary>The summary, cut after six lines with Read more (the site shows it whole; a long one would push everything down).</summary>
  static FrameworkElement Summary(string summary) {
    var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
    var t = PnKit.Text(summary, "Blastlog.Body").Also(x => { x.FontSize = 15; x.TextWrapping = TextWrapping.Wrap; });
    t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; t.LineHeight = 23; t.TextTrimming = TextTrimming.WordEllipsis;
    panel.Children.Add(t);
    const double clamp = 23 * 6;
    Button toggle = null;
    toggle = Fingerprint.LinkButton("Read more ▾", () => { var open = t.MaxHeight < 1e5; t.MaxHeight = open ? double.PositiveInfinity : clamp; toggle.Content = open ? "Show less ▴" : "Read more ▾"; }, game: true);
    toggle.Margin = new Thickness(-10, 0, 0, 0); toggle.HorizontalAlignment = HorizontalAlignment.Left; toggle.Visibility = Visibility.Collapsed;
    panel.Children.Add(toggle);
    // Cut only when it really runs past six lines at the width it gets (measured once, on its first layout).
    var measured = false;
    t.SizeChanged += (s, e) => {
      try {
        if (measured || e.NewSize.Width <= 0) return;
        measured = true;
        if (e.NewSize.Height > clamp + 1) { t.MaxHeight = clamp; toggle.Visibility = Visibility.Visible; }
      } catch (Exception ex) { Log.Error(ex, "Blastlog summary"); }
    };
    return panel;
  }

  /// <summary>The hero card: the game's art blurred under a scrim, the game's glow from the top right, then the content.</summary>
  FrameworkElement Backdrop(FrameworkElement content) {
    var card = new Border { CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 18), ClipToBounds = true };
    card.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Blastlog.GameEdge");
    var layers = new Grid();
    var art = new Border { Opacity = 0.42, IsHitTestVisible = false, Effect = new BlurEffect { Radius = 36, KernelType = KernelType.Gaussian }, Margin = new Thickness(-60) };
    layers.Children.Add(art);
    var scrim = new Border { IsHitTestVisible = false };
    Tint.OnGame(scrim, (game, line, surface) => {
      var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
      b.GradientStops.Add(new GradientStop(Tint.Alpha(Tint.Mix(game, surface, 0.16), 0.55), 0));
      b.GradientStops.Add(new GradientStop(Tint.Alpha(surface, 0.86), 0.55));
      b.GradientStops.Add(new GradientStop(Tint.Alpha(surface, 0.94), 1));
      b.Freeze(); scrim.Background = b;
    });
    layers.Children.Add(scrim);
    layers.Children.Add(new Border { Child = content, Padding = new Thickness(28) });
    card.Child = layers;
    // Rounded corners for the blurred layer too (ClipToBounds clips to the square box).
    card.SizeChanged += (s, e) => { try { layers.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width - 2, e.NewSize.Height - 2), 25, 25); } catch (Exception ex) { Log.Error(ex, "Blastlog hero clip"); } };
    LoadBackdrop(art);
    return card;
  }
  async void LoadBackdrop(Border art) {
    try {
      // A wide screenshot when IGDB has one (the site's hero art), else the cover.
      var shot = info.Strings("screenshots").FirstOrDefault(IsIgdb);
      var url = shot != null ? Artwork.NormalizedLegacyUrl(shot, "screenshot_big") : Artwork.Url(Game, ArtworkContext.Cover);
      var file = await PnKit.Picture(plugin, url);
      if (file == null) return;
      var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(file); image.DecodePixelWidth = 640; image.EndInit(); image.Freeze();
      art.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
    } catch (Exception e) { Log.Warn("Blastlog: hero art didn't load: " + e.Message); }
  }
  /// <summary>Pictures come from IGDB's image server only (the plugin's one other host, README).</summary>
  internal static bool IsIgdb(string url) => Uri.TryCreate(url?.StartsWith("//") == true ? "https:" + url : url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "images.igdb.com";

  /// <summary>Play (installed) or Install for a game the player has in Playnite; the window shuts first.</summary>
  Button PlayOrInstall(string entryId) {
    var game = entryId != null ? plugin.GameFor(entryId) : null;
    if (game == null) return null;
    var id = game.Id; var installed = game.IsInstalled;
    var b = PnKit.Btn(PnKit.IconWord(installed ? "<polygon points=\"6 3 20 12 6 21 6 3\" />" : "<path d=\"M12 15V3\" /><path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4\" /><path d=\"m7 10 5 5 5-5\" />", installed ? "Play" : "Install"),
      () => { AfterClose = () => { try { if (installed) plugin.Api.StartGame(id); else plugin.Api.InstallGame(id); } catch (Exception e) { Log.Error(e, "Blastlog play"); } }; window.Close(); return Task.CompletedTask; },
      "Blastlog.Primary", (installed ? "Play " : "Install ") + Title);
    b.Margin = new Thickness(0, 0, 8, 8);
    return b;
  }
}
}
