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

  /// <summary>
  /// A friend's Dashboard, Library or Taste (the site's /friends/:id/dashboard, /library and /profile): one native window
  /// with the three as tabs, read only (Screens/FriendPages.cs). <paramref name="page"/>: "dashboard", "library" or
  /// "profile" ("taste" works too); <paramref name="name"/> when the caller knows it. Not set up yet: the site's page.
  /// </summary>
  public static void FriendPage(BlastlogPlugin plugin, string friendId, string page = "dashboard", string name = null) {
    if (string.IsNullOrEmpty(friendId)) return;
    var key = page == "library" ? "library" : page == "profile" || page == "taste" ? "profile" : "dashboard";
    if (!plugin.Configured) { Site(plugin, $"/friends/{friendId}/{key}"); return; }
    Blastlog.Playnite.Screens.FriendPages.Open(plugin, friendId, key, name);
  }

  /// <summary>The player's friends and friend groups: links, remove and restore, groups, and adding someone by their link (native).</summary>
  public static void Friends(BlastlogPlugin plugin) { if (plugin.Configured) Blastlog.Playnite.Screens.FriendsPage.Open(plugin); else Site(plugin, "/friends"); }

  /// <summary>Your taste: the native screen in Playnite's sidebar (Ui/ScreenJump.cs), the site if Playnite won't switch.</summary>
  public static void Taste(BlastlogPlugin plugin) { if (!ScreenJump.To("Your taste")) Site(plugin, "/profile"); }

  /// <summary>Play next, at the top (the site's recommendations page).</summary>
  public static void PlayNext(BlastlogPlugin plugin) { if (!ScreenJump.To("Play next")) Site(plugin, "/recommendations"); }

  /// <summary>Sharpen your picks: the full list of games worth rating or reviewing.</summary>
  public static void Sharpen(BlastlogPlugin plugin) { if (!ScreenJump.To("Play next", "Sharpen your picks")) Site(plugin, "/recommendations#sharpen"); }

  /// <summary>The sidebar's Settings: the plugin's settings until the native Blastlog settings land.</summary>
  public static void Settings(BlastlogPlugin plugin) {
    try { plugin.OpenSettingsView(); } catch (Exception e) { global::Playnite.SDK.LogManager.GetLogger().Error(e, "Blastlog settings"); }
  }

  /// <summary>Send feedback: an idea or a bug, with the screen it came from (native, Screens/FeedbackDialog.cs).</summary>
  public static void Feedback(BlastlogPlugin plugin, string page = null) => Blastlog.Playnite.Screens.FeedbackDialog.Open(plugin, page);

  /// <summary>Playnite's own library (installing and launching), for when its menu items are hidden.</summary>
  public static void PlayniteLibrary(BlastlogPlugin plugin) {
    try { plugin.Api.MainView.SwitchToLibraryView(); } catch (Exception e) { global::Playnite.SDK.LogManager.GetLogger().Error(e, "Blastlog: Playnite library"); }
  }

  /// <summary>
  /// The Import page (the site's /library/import): Steam sync's finds and "Your imports", each import's sorting, commit
  /// and Undo, natively (Screens/ImportPage.cs, ImportBatch.cs). Bringing new games in (the store doors) stays on the site.
  /// <paramref name="batchId"/> opens one import. Not set up yet: the site's page.
  /// </summary>
  public static void Import(BlastlogPlugin plugin, string batchId = null, Action done = null) {
    if (!plugin.Configured) { SiteOnly(plugin, "/library/import" + (batchId != null ? "/" + batchId : "")); return; }
    global::Blastlog.Playnite.Screens.ImportPage.Open(plugin, batchId, done);
  }

  /// <summary>
  /// A page that stays on the site (account settings, the picture), unless Playnite has it: /library/import and
  /// /library/import/:id open natively (Import), so screens that still pass those paths get the native page.
  /// </summary>
  public static void Site(BlastlogPlugin plugin, string path) {
    var m = System.Text.RegularExpressions.Regex.Match(path ?? "", @"^/library/import(?:/([0-9a-fA-F-]{8,}))?/?$");
    if (m.Success && plugin.Configured) { Import(plugin, m.Groups[1].Success ? m.Groups[1].Value : null); return; }
    SiteOnly(plugin, path);
  }

  /// <summary>Always the site, even where Playnite has its own page (Import games, whose doors are on the site).</summary>
  public static void SiteOnly(BlastlogPlugin plugin, string path) => UiKit.Open(plugin.ServerUrl + path);
}
}
