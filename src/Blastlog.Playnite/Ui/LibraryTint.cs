using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Blastlog.Sync.Core.Design;
using Playnite.SDK;
using Playnite.SDK.Controls;
using Game = Playnite.SDK.Models.Game;

namespace Blastlog.Playnite.Ui {
/// <summary>
/// The Library tile's Blastlog part (docs/PLAYNITE_DESIGN.md 4.2): the theme puts a <c>Blastlog_Tile</c> element under
/// each grid tile's title, and this fills it with the game's status pill and stars (paired games only), and gives the
/// whole tile its own game colours (design.md 2.3 "every card wears its own game"), so the card's tint, edge and Play
/// button follow that game's cover instead of the selected one. Colours are worked out off the UI thread, a few at a
/// time, and remembered, so scrolling back is instant. Until a tile's colour is known it wears neutral surface tokens.
/// </summary>
public sealed class LibraryTile : PluginUserControl {
  public const string ElementName = "Tile";
  static readonly ILogger Log = LogManager.GetLogger();
  static readonly ConcurrentDictionary<string, string> Seeds = new ConcurrentDictionary<string, string>();
  static readonly SemaphoreSlim Working = new SemaphoreSlim(3, 3);
  readonly BlastlogPlugin plugin; readonly WrapPanel row = new WrapPanel { Orientation = Orientation.Horizontal };
  int version;

  public LibraryTile(BlastlogPlugin plugin) { this.plugin = plugin; Content = row; Focusable = false; }

  public override async void GameContextChanged(Game oldContext, Game newContext) {
    var mine = ++version;
    try {
      row.Children.Clear();
      var tile = (Parent as FrameworkElement)?.TemplatedParent as FrameworkElement;
      if (newContext == null) { if (tile != null) Neutral(tile); return; }
      ShowInfo(newContext);
      if (tile == null || plugin.Theme == null || !ThemeBridge.BlastlogThemeActive) return;
      var key = SeedKey(newContext);
      if (Seeds.TryGetValue(key, out var known)) { Kit.GameScope(tile, known); return; }
      Neutral(tile);
      await Working.WaitAsync();
      string seed;
      try {
        if (mine != version) return;
        seed = await Task.Run(() => plugin.Theme.SeedFor(newContext));
      } finally { Working.Release(); }
      Seeds[key] = seed;
      if (mine == version) Kit.GameScope(tile, seed);
    } catch (Exception e) { Log.Error(e, "Blastlog: library tile for " + newContext?.Name); }
  }

  /// <summary>Status pill (icon plus word, fixed colours) and stars, from the paired entry as of the last sync.</summary>
  void ShowInfo(Game game) {
    var entryId = plugin.EntryFor(game.Id);
    var entry = entryId != null ? plugin.CachedEntry(entryId) : null;
    if (entry == null) { Visibility = Visibility.Collapsed; return; }
    var status = entry.Ownership == "wishlist" && (entry.Status == null || entry.Status == "not_started") ? "wishlist" : entry.Status;
    var pill = Kit.StatusPill(status); pill.Margin = new Thickness(0, 0, 8, 4); row.Children.Add(pill);
    if (entry.Rating != null) { var stars = Kit.Stars(entry.Rating, 15); stars.VerticalAlignment = VerticalAlignment.Center; stars.Margin = new Thickness(0, 0, 0, 4); row.Children.Add(stars); }
    Visibility = Visibility.Visible;
  }

  /// <summary>The seed changes when the Blastlog page colour or the cover changes, so both are part of the key.</summary>
  string SeedKey(Game game) {
    var entryId = plugin.EntryFor(game.Id);
    var accent = entryId != null ? plugin.CachedEntry(entryId)?.Game?.Accent : null;
    return game.Id + "|" + game.CoverImage + "|" + accent + "|" + ThemeBridge.CurrentMode;
  }

  /// <summary>
  /// A tile whose colour isn't known yet: Blastlog green for its buttons (with the checked ink for this board), and
  /// surface and line for the card, not the selected game's colours.
  /// </summary>
  static void Neutral(FrameworkElement tile) {
    Kit.GameScope(tile, GameColour.BrandGreen);
    void Use(string key, string token) { if (Application.Current?.TryFindResource(token) is Brush b) tile.Resources[key] = b; }
    Use("Blastlog.GameTint", "Blastlog.Surface"); Use("Blastlog.GameTint2", "Blastlog.Surface2"); Use("Blastlog.GameEdge", "Blastlog.Line");
    Use("Blastlog.GameBar", "Blastlog.Surface2"); Use("Blastlog.GameText", "Blastlog.Text");
  }
}
}
