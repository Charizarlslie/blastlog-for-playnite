using System;
using System.Collections.Generic;
using System.Linq;

namespace Blastlog.Playnite.Ui {
/// <summary>
/// Every place a screen sends the player somewhere else goes through here, so a native replacement for a site page is a
/// change to one method, not to every screen that links to it. Each method opens the site until its native version
/// lands. <paramref name="done"/> callbacks run after a native dialog saved something (screens use them to refresh).
/// </summary>
public static class Nav {
  /// <summary>Rate and review a library entry: stars, taps, review and its questions (native, Screens/ReviewDialog.cs).</summary>
  public static void Review(BlastlogPlugin plugin, string entryId, Action done = null) {
    if (entryId == null) return;
    global::Blastlog.Playnite.Screens.ReviewDialog.Open(plugin, entryId, done);
  }

  /// <summary>Add a game to the Blastlog library. <paramref name="status"/> "playing" when added from Currently playing.</summary>
  public static void AddGame(BlastlogPlugin plugin, string status = null, Action done = null) => global::Blastlog.Playnite.Screens.AddGameWindow.Show(plugin, status, done);

  /// <summary>
  /// A catalogue game's page (Discover's game page): for games that aren't in the library, or aren't in Playnite. Native
  /// (Screens/DiscoverGame.cs). <paramref name="shelf"/>: the Discover shelf it was opened from, when it was.
  /// </summary>
  public static void DiscoverGame(BlastlogPlugin plugin, string gameId, Action done = null, string shelf = null) {
    if (gameId == null) return;
    Blastlog.Playnite.Screens.DiscoverGamePage.Open(plugin, gameId, shelf, done);
  }

  /// <summary>
  /// One of the player's own entries: Playnite's game page when it's paired, else the native game page for its game,
  /// else (not synced yet) the site's.
  /// </summary>
  public static void Entry(BlastlogPlugin plugin, string entryId, Action done = null) {
    if (entryId == null) return;
    try {
      var game = plugin.GameFor(entryId);
      if (game != null) { plugin.Api.MainView.SwitchToLibraryView(); plugin.Api.MainView.SelectGame(game.Id); return; }
      var gameId = plugin.BlastlogGameIdFor(entryId);
      if (gameId != null) { DiscoverGame(plugin, gameId, done); return; }
    } catch (Exception e) { global::Playnite.SDK.LogManager.GetLogger().Warn(e, "Blastlog: couldn't open the game in Playnite"); }
    Site(plugin, "/library/" + entryId);
  }

  /// <summary>Quick start: tap the well-known games you've played and give them stars.</summary>
  public static async void QuickStart(BlastlogPlugin plugin, Action done = null) {
    // Native once the server has the quick start routes; the site's page until then (or offline).
    try { if (await Blastlog.Playnite.Screens.QuickStartPage.Open(plugin, done)) return; } catch (Exception e) { global::Playnite.SDK.LogManager.GetLogger().Error(e, "Blastlog quick start"); }
    Site(plugin, "/quick-start");
  }

  /// <summary>Find a co-op game with friends (the site's Together page), optionally with these friends picked.</summary>
  public static void Together(BlastlogPlugin plugin, IEnumerable<string> friendIds = null) {
    var ids = friendIds?.Where(id => !string.IsNullOrEmpty(id)).ToList();
    if (plugin.Configured) { Blastlog.Playnite.Screens.TogetherPage.Open(plugin, ids); return; }
    Site(plugin, "/together" + (ids != null && ids.Count > 0 ? "?with=" + string.Join(",", ids) : ""));
  }

  /// <summary>The player's friends and friend groups.</summary>
  public static void Friends(BlastlogPlugin plugin) { if (plugin.Configured) Blastlog.Playnite.Screens.FriendsPage.Open(plugin); else Site(plugin, "/friends"); }

  /// <summary>Your taste: the native screen in Playnite's sidebar (Ui/ScreenJump.cs), the site if Playnite won't switch.</summary>
  public static void Taste(BlastlogPlugin plugin) { if (!ScreenJump.To("Your taste")) Site(plugin, "/profile"); }

  /// <summary>Play next, at the top (the site's recommendations page).</summary>
  public static void PlayNext(BlastlogPlugin plugin) { if (!ScreenJump.To("Play next")) Site(plugin, "/recommendations"); }

  /// <summary>Sharpen your picks: the full list of games worth rating or reviewing.</summary>
  public static void Sharpen(BlastlogPlugin plugin) { if (!ScreenJump.To("Play next", "Sharpen your picks")) Site(plugin, "/recommendations#sharpen"); }

  /// <summary>A page that stays on the site (import sorting, account settings, the picture).</summary>
  public static void Site(BlastlogPlugin plugin, string path) => UiKit.Open(plugin.ServerUrl + path);
}
}
