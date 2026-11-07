using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
      var pill = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
        Child = Dash.Text(badge, "Blastlog.Small", "Blastlog.Ok", FontWeights.Bold) };
      pill.SetResourceReference(Border.BackgroundProperty, "Blastlog.Surface");
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

  /// <summary>The chosen row wears the accent edge and tint (web .game-results button.active).</summary>
  public static void Mark(Border frame, bool on) {
    frame.SetResourceReference(Border.BorderBrushProperty, on ? "Blastlog.Game" : "Blastlog.Line");
    frame.SetResourceReference(Border.BackgroundProperty, on ? "Blastlog.GameTint2" : "Blastlog.Surface2");
    frame.BorderThickness = new Thickness(on ? 2 : 1);
    frame.Padding = new Thickness(on ? 8 : 9);
  }

  /// <summary>A 44px pill that can be on or off (ownership and Where you own it; web .owned-choices label.on).</summary>
  public static Button Pill(string label, Action click) {
    var b = new Button { Content = label, Style = Kit.Style("Blastlog.Ghost"), MinHeight = 44, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 8), FontSize = 14 };
    b.Click += (s, e) => { try { click(); } catch (Exception ex) { Log.Error(ex, "Blastlog add a game: " + label); } };
    return b;
  }
  public static void SetPill(Button b, bool on) {
    b.SetResourceReference(Control.BorderBrushProperty, on ? "Blastlog.GameEdge" : "Blastlog.Line");
    b.SetResourceReference(Control.BackgroundProperty, on ? "Blastlog.GameTint2" : "Blastlog.Surface");
    b.SetResourceReference(Control.ForegroundProperty, on ? "Blastlog.GameText" : "Blastlog.Text");
    b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
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
