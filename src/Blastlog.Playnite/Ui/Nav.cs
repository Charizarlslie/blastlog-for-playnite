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

  /// <summary>
  /// A friend's library entry (the site's /library/:id for someone else's entry): their stars, taps, review, status and
  /// reactions, with the game's fingerprint and details, natively (Screens/FriendEntry.cs). <paramref name="owner"/> is
  /// the friend's name when the caller knows it. Not set up yet: the site's page.
  /// </summary>
  public static void FriendEntry(BlastlogPlugin plugin, string entryId, string owner = null, Action done = null) {
    if (entryId == null) return;
    if (!plugin.Configured) { Site(plugin, "/library/" + entryId); return; }
    global::Blastlog.Playnite.Screens.FriendEntryPage.Open(plugin, entryId, owner, done);
  }

  /// <summary>
  /// A game's trailers and screenshots, one at a time, starting at <paramref name="start"/> (native, Ui/MediaViewer.cs).
  /// Trailers open on YouTube in the browser from there.
  /// </summary>
  public static void Media(BlastlogPlugin plugin, string title, List<global::Blastlog.Sync.Core.Design.MediaItem> items, int start, string poster = null) =>
    MediaViewer.Open(plugin, title, items, start, poster);

  /// <summary>
  /// The look of your character sheet: the native Compare screen, scrolled to the Look picker on your sheet
  /// (Screens/CompareScreen.cs); the site's Compare if Playnite won't switch.
  /// </summary>
  public static void SheetLook(BlastlogPlugin plugin) {
    if (!plugin.Configured || !ScreenJump.To("Compare", global::Blastlog.Playnite.Screens.CompareView.LookPickerName)) Site(plugin, "/compare#sheets");
  }

  /// <summary>
  /// Every game with one trait (the site's /traits/:id, "See games with …"): native (Screens/TraitPage.cs).
  /// <paramref name="from"/>: what it was opened from ("Hades"), for its Back button.
  /// </summary>
  public static void Trait(BlastlogPlugin plugin, string traitId, string from = null) {
    if (traitId == null) return;
    if (plugin.Configured) { Blastlog.Playnite.Screens.TraitPage.Open(plugin, traitId, from); return; }
    Site(plugin, "/traits/" + traitId);
  }

  /// <summary>
  /// One of the player's collections (the site's /library/collections/:id): its games and the overall take, native
  /// (Screens/CollectionPage.cs). <paramref name="done"/> runs after the window closes.
  /// </summary>
  public static void Collection(BlastlogPlugin plugin, string collectionId, Action done = null) {
    if (collectionId == null) return;
    if (plugin.Configured) { Blastlog.Playnite.Screens.CollectionPage.Open(plugin, collectionId, done); return; }
    Site(plugin, "/library/collections/" + collectionId);
  }

  /// <summary>
  /// The player's whole Blastlog library (native, Screens/LibraryScreen.cs), with the site's filters as a query
  /// ("status=done&amp;rating=4", "search=hades"); the site's Library page if Playnite won't switch.
  /// </summary>
  public static void Library(BlastlogPlugin plugin, string query = null) {
    if (plugin.Configured && Blastlog.Playnite.Screens.LibraryView.Open(query)) return;
    Site(plugin, "/library" + (string.IsNullOrEmpty(query) ? "" : "?" + query));
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
