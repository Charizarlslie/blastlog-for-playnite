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
  /// <summary>Rate and review a library entry: stars, taps, review and its questions.</summary>
  public static void Review(BlastlogPlugin plugin, string entryId, Action done = null) {
    if (entryId == null) return;
    Site(plugin, "/library/" + entryId + "/edit");
  }

  /// <summary>Add a game to the Blastlog library. <paramref name="status"/> "playing" when added from Currently playing.</summary>
  public static void AddGame(BlastlogPlugin plugin, string status = null, Action done = null) => Site(plugin, "/dashboard");

  /// <summary>A catalogue game's page (Discover's game page): for games that aren't in the library, or aren't in Playnite.</summary>
  public static void DiscoverGame(BlastlogPlugin plugin, string gameId, Action done = null) {
    if (gameId == null) return;
    Site(plugin, "/discover/game/" + gameId);
  }

  /// <summary>Quick start: tap the well-known games you've played and give them stars.</summary>
  public static void QuickStart(BlastlogPlugin plugin, Action done = null) => Site(plugin, "/quick-start");

  /// <summary>Find a co-op game with friends (the site's Together page), optionally with these friends picked.</summary>
  public static void Together(BlastlogPlugin plugin, IEnumerable<string> friendIds = null) {
    var ids = friendIds?.Where(id => !string.IsNullOrEmpty(id)).ToList();
    Site(plugin, "/together" + (ids != null && ids.Count > 0 ? "?with=" + string.Join(",", ids) : ""));
  }

  /// <summary>The player's friends and friend groups.</summary>
  public static void Friends(BlastlogPlugin plugin) => Site(plugin, "/friends");

  /// <summary>Your taste (the native Taste screen once in-app jumps between screens work).</summary>
  public static void Taste(BlastlogPlugin plugin) => Site(plugin, "/profile");

  /// <summary>Sharpen your picks: the full list of games worth rating or reviewing.</summary>
  public static void Sharpen(BlastlogPlugin plugin) => Site(plugin, "/recommendations#sharpen");

  /// <summary>A page that stays on the site (import sorting, account settings, the picture).</summary>
  public static void Site(BlastlogPlugin plugin, string path) => UiKit.Open(plugin.ServerUrl + path);
}
}
