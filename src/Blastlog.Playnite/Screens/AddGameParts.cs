using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using BlastlogGame = Blastlog.Sync.Core.Game;
using PlayniteGame = Playnite.SDK.Models.Game;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Pieces of the Add a game window (web App.tsx AddKnownGame): result rows, the ownership and store pills, and finding
/// the same game in Playnite so it can be paired after it's added.
/// </summary>
static class AddGameParts {
  static readonly ILogger Log = LogManager.GetLogger();

  /// <summary>The stores a player can tick by hand (domain owned-stores.ts PICKABLE_STORES, STORE_NAME).</summary>
  public static readonly KeyValuePair<string, string>[] Stores = {
    new KeyValuePair<string, string>("steam", "Steam"), new KeyValuePair<string, string>("gog", "GOG"),
    new KeyValuePair<string, string>("epic", "Epic Games"), new KeyValuePair<string, string>("playstation", "PlayStation"),
    new KeyValuePair<string, string>("xbox", "Xbox"), new KeyValuePair<string, string>("nintendo", "Nintendo"),
    new KeyValuePair<string, string>("physical", "Disc or cartridge"), new KeyValuePair<string, string>("other", "Somewhere else"),
  };

  /// <summary>An IGDB search result as the sync core's Game, for its cover.</summary>
  public static BlastlogGame CatalogCover(JObject item) => new BlastlogGame {
    Id = "igdb:" + (string)item["externalId"], Title = (string)item["title"],
    Artwork = new GameArtwork { CoverImageId = (string)item["artwork"]?["coverImageId"] },
  };

  /// <summary>"2019 · PC · PlayStation" for an IGDB result, or "Details unavailable" (web: year + platformLine(…, 3)).</summary>
  public static string CatalogLine(JObject item) {
    var year = ((string)item["releaseDate"])?.Length >= 4 ? ((string)item["releaseDate"]).Substring(0, 4) : null;
    var platforms = Dash.PlatformLine(Dash.Arr(item["platforms"]).Select(p => (string)p), 3);
    var line = string.Join(" · ", new[] { year, platforms }.Where(x => !string.IsNullOrEmpty(x)));
    return line.Length > 0 ? line : "Details unavailable";
  }

  /// <summary>
  /// One result: cover, title, a line under it and an optional "Already in library" badge, in a 44px+ row the width of
  /// <paramref name="host"/>. <paramref name="frame"/> is the row's border, so the window can mark the chosen one.
  /// </summary>
  public static Button Row(BlastlogPlugin plugin, FrameworkElement host, BlastlogGame cover, string title, string line, string badge, Action pick, out Border frame) {
    var image = Kit.Cover(plugin, cover, 36, ArtworkContext.Thumbnail, 7); image.Margin = new Thickness(0, 0, 12, 0); image.VerticalAlignment = VerticalAlignment.Center;
    var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
    text.Children.Add(Dash.Text(title, "Blastlog.Body", "Blastlog.Text", FontWeights.SemiBold).Also(t => t.TextWrapping = TextWrapping.Wrap));
    if (!string.IsNullOrEmpty(line)) text.Children.Add(Dash.Text(line, "Blastlog.Small").Also(t => { t.Margin = new Thickness(0, 3, 0, 0); t.TextWrapping = TextWrapping.Wrap; }));
    if (badge != null) {
      // web .library-badge: ok-text on light-dark(#cef6e8, #12352b), 800 weight.
      var light = ThemeBridge.CurrentMode == Mode.Light;
      var pill = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
        Child = Dash.Text(badge, "Blastlog.Small", null, FontWeights.ExtraBold).Also(t => t.Foreground = new SolidColorBrush(Token("ok-text"))),
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#cef6e8" : "#12352b")) };
      text.Children.Add(pill);
    }
    var content = new DockPanel { Children = { image, text } }; DockPanel.SetDock(image, Dock.Left);
    frame = new Border { Child = content, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Padding = new Thickness(9), MinHeight = 48 };
    frame.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.ActualWidth)) { Source = host });
    var b = new Button { Content = frame, Style = Kit.Style("Blastlog.TextButton"), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 7), MinHeight = 44,
      HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    b.SetResourceReference(Control.ForegroundProperty, "Blastlog.Text");
    b.Click += (s, e) => { try { pick(); } catch (Exception ex) { Log.Error(ex, "Blastlog add a game: pick"); } };
    Dash.AutomationName(b, title + (string.IsNullOrEmpty(line) ? "" : ", " + line) + (badge != null ? ", " + badge : ""));
    Mark(frame, false);
    return b;
  }

  // The window wears Blastlog green (AddGameWindow), as the site's modal does; its mixes are worked out from the tokens.
  static Color Token(string key) => (Color)ColorConverter.ConvertFromString(Tokens.For(ThemeBridge.CurrentMode)[key]);
  static Color Accent => (Color)ColorConverter.ConvertFromString(GameColour.For(GameColour.BrandGreen, ThemeBridge.CurrentMode).Game);

  /// <summary>
  /// The chosen row (web .game-results button.active): a 1px accent edge and the accent 10% into surface-2.
  /// </summary>
  public static void Mark(Border frame, bool on) {
    if (on) { frame.BorderBrush = new SolidColorBrush(Accent); frame.Background = new SolidColorBrush(Tint.Mix(Accent, Token("surface-2"), 0.10)); }
    else { frame.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line"); frame.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface2"); }
  }

  /// <summary>
  /// A 44px pill that can be on or off (web .owned-choices label): muted words in 800 weight, fully rounded. Store pills
  /// carry the site's checkbox (<paramref name="box"/>); the ownership pair stands in for the site's select, so it has none.
  /// </summary>
  public static Button Pill(string label, Action click, bool box = false) {
    var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
    if (box) {
      var tick = new Path { Data = Geometry.Parse("M3.5 8.5 6.5 11.5 12.5 4.5"), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round, Width = 16, Height = 16, Stretch = Stretch.None };
      tick.SetResourceReference(Shape.StrokeProperty, "Blastlog.OnGame");
      var square = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 7, 0), Child = tick };
      DockPanel.SetDock(square, Dock.Left); row.Children.Add(square);
    }
    row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
    var pill = new Border { Child = row, CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(1), Padding = new Thickness(13, 6, 13, 6), MinHeight = 44 };
    var b = new Button { Content = pill, Style = Kit.Style("Blastlog.TextButton"), MinHeight = 44, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6), FontSize = 14 };
    b.Click += (s, e) => { try { click(); } catch (Exception ex) { Log.Error(ex, "Blastlog add a game: " + label); } };
    Dash.AutomationName(b, label);
    SetPill(b, false);
    return b;
  }
  /// <summary>On: the edge is the accent 60% into the line, the fill the accent 14% into the surface, the box ticked.</summary>
  public static void SetPill(Button b, bool on) {
    if (!(b.Content is Border pill)) return;
    if (on) { pill.BorderBrush = new SolidColorBrush(Tint.Mix(Accent, Token("line"), 0.6)); pill.Background = new SolidColorBrush(Tint.Mix(Accent, Token("surface"), 0.14)); }
    else { pill.SetResourceReference(Border.BorderBrushProperty, "Blastlog.Line"); pill.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface"); }
    b.SetResourceReference(Control.ForegroundProperty, "Blastlog.TextMuted");
    b.FontWeight = FontWeights.ExtraBold;
    if (pill.Child is DockPanel row && row.Children.Count > 1 && row.Children[0] is Border square) {
      square.SetResourceReference(Border.BorderBrushProperty, on ? "Blastlog.Game" : "Blastlog.Edge");
      if (on) square.SetResourceReference(Border.BackgroundProperty, "Blastlog.Game"); else square.Background = Brushes.Transparent;
      if (square.Child != null) square.Child.Visibility = on ? Visibility.Visible : Visibility.Hidden;
    }
    System.Windows.Automation.AutomationProperties.SetItemStatus(b, on ? "On" : "Off");
  }

  /// <summary>
  /// Playnite games with the same name as <paramref name="title"/> that aren't paired yet (letters and digits only,
  /// case ignored), so the window can offer to pair the game it just added.
  /// </summary>
  public static List<PlayniteGame> UnpairedInPlaynite(BlastlogPlugin plugin, string title) {
    var key = Key(title);
    if (key.Length == 0) return new List<PlayniteGame>();
    try {
      return plugin.Api.Database.Games.Where(g => g != null && Key(g.Name) == key && plugin.EntryFor(g.Id) == null).OrderBy(g => g.Name).ToList();
    } catch (Exception e) { Log.Warn(e, "Blastlog add a game: Playnite lookup"); return new List<PlayniteGame>(); }
  }
  static string Key(string name) {
    if (string.IsNullOrEmpty(name)) return "";
    var sb = new StringBuilder(name.Length);
    foreach (var c in name.Normalize(NormalizationForm.FormKD)) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
    return sb.ToString();
  }
  public static string Where(PlayniteGame game) => string.IsNullOrEmpty(game.Source?.Name) ? game.Name : $"{game.Name} ({game.Source.Name})";
}
}
