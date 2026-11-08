using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>A friend whose pages are open: who they are, and where their reads live on the mirror.</summary>
public sealed class FriendView {
  public string Id { get; }
  public string Name { get; }
  public FriendView(string id, string name) { Id = id; Name = string.IsNullOrWhiteSpace(name) ? "Your friend" : name; }
  /// <summary>
  /// A read about this friend: "taste-profile" → "users/&lt;id&gt;/taste-profile". The friend reads (Blastlog
  /// docs/PLAYNITE_SYNC.md, the site's own /users/:id routes with its friendship checks) all take this shape; if the
  /// server's contract moves, this is the one line to change.
  /// </summary>
  public string Path(string read) => "users/" + Uri.EscapeDataString(Id) + "/" + read;
}

/// <summary>
/// A friend's library as the sync core's entries, so the Library screen's filters and sorts (LibraryList) work on it
/// unchanged. Reads the site's paged library (GET users/:id/library?limit=100&amp;offset=), shaped as the site's library
/// DTO (or the sync library's entry shape, both are understood).
/// </summary>
static class FriendData {
  static readonly ILogger Log = LogManager.GetLogger();
  const int Page = 500, MaxPages = 40;
  /// <summary>GET users/:id/library?limit=&amp;since=: the sync library's pages and cursor (round 4), from the start each time.</summary>
  static string PagePath(FriendView friend, string cursor) =>
    friend.Path($"library?limit={Page}" + (string.IsNullOrEmpty(cursor) ? "" : "&since=" + Uri.EscapeDataString(cursor)));

  /// <summary>The saved copy (the last read's pages), at once; empty when there's none.</summary>
  public static List<Entry> SavedEntries(BlastlogPlugin plugin, FriendView friend) {
    var byId = new Dictionary<string, Entry>();
    try {
      string cursor = null;
      for (var page = 0; page < MaxPages; page++) {
        var saved = plugin.SavedRead(PagePath(friend, cursor));
        if (saved == null) break;
        Take(saved, byId);
        if (!More(saved, out cursor)) break;
      }
    } catch (Exception e) { Log.Warn("Blastlog friend library (saved): " + e.Message); }
    return byId.Values.ToList();
  }

  /// <summary>Every entry, read from the server; null when it couldn't be read (offline, not friends any more).</summary>
  public static async Task<List<Entry>> Entries(BlastlogPlugin plugin, FriendView friend, bool fresh) {
    var byId = new Dictionary<string, Entry>();
    try {
      string cursor = null;
      for (var page = 0; page < MaxPages; page++) {
        var value = (await plugin.Mirror(PagePath(friend, cursor), fresh)).Value;
        if (value == null) return page == 0 ? null : byId.Values.ToList();
        Take(value, byId);
        if (!More(value, out cursor)) break;
      }
    } catch (Exception e) { Log.Warn("Blastlog friend library: " + e.Message); return null; }
    return byId.Values.ToList();
  }

  static void Take(JObject page, Dictionary<string, Entry> byId) {
    foreach (var e in (page["items"] as JArray ?? new JArray()).OfType<JObject>().Select(ToEntry).Where(e => e != null)) byId[e.EntryId] = e;
    foreach (var r in (page["removed"] as JArray ?? new JArray()).OfType<JObject>()) { var id = S(r, "entryId"); if (id != null) byId.Remove(id); }
  }
  static bool More(JObject page, out string cursor) {
    cursor = S(page, "nextCursor");
    return page["hasMore"]?.Type == JTokenType.Boolean && (bool)page["hasMore"] && !string.IsNullOrEmpty(cursor);
  }

  static readonly string[] BacklogStatuses = { "not_started", "started", "playing" };

  /// <summary>One entry of the site's library DTO (or the sync library's shape) as the sync core's entry.</summary>
  public static Entry ToEntry(JObject e) {
    var id = S(e, "entryId") ?? S(e, "id");
    if (id == null) return null;
    var gameToken = e["game"] as JObject;
    var game = Dash.Game(gameToken) ?? new Game { Title = "" };
    if (game.ReleaseYear == null && DateTime.TryParse(S(gameToken, "releaseDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var released)) game.ReleaseYear = released.Year;
    var ownership = S(e, "ownership"); var status = S(e, "status"); var review = S(e, "review");
    var length = e["length"] as JObject;
    var noEnd = e["noEnd"]?.Type == JTokenType.Boolean ? (bool)e["noEnd"] : length?["noEnd"]?.Type == JTokenType.Boolean && (bool)length["noEnd"];
    var hours = noEnd ? null : D(length, "hours");
    return new Entry {
      EntryId = id, Version = e["version"]?.Type == JTokenType.Integer ? (int)e["version"] : 1,
      UpdatedAt = e["updatedAt"]?.Type == JTokenType.Date ? new DateTimeOffset((DateTime)e["updatedAt"]) : default(DateTimeOffset),
      Game = game, Ownership = ownership, Status = status, Rating = D(e, "rating"), Review = review,
      HasReview = e["hasReview"]?.Type == JTokenType.Boolean ? (bool)e["hasReview"] : !string.IsNullOrWhiteSpace(review),
      FinishedOn = S(e, "finishedOn"), Platform = S(e, "platform"),
      CollectionId = S(e, "collectionId") ?? S(e["collection"] as JObject, "id"),
      InBacklog = e["inBacklog"]?.Type == JTokenType.Boolean ? (bool)e["inBacklog"] : status != "ignored" && ownership != "wishlist" && BacklogStatuses.Contains(status ?? ""),
      Length = hours == null ? null : new EntryLength { Hours = (int)Math.Round(hours.Value, MidpointRounding.AwayFromZero), Level = S(length, "fromLevel") ?? S(length, "level") },
      NoEnd = noEnd,
    };
  }
}

/// <summary>
/// A friend's pages (the site's /friends/:id/dashboard, /library and /profile, from the menu's friend list): one window
/// with their Dashboard, Library and Taste, read only. The three are the native screens with a viewed friend
/// (FriendDashboard.cs, LibraryView and TasteView take a <see cref="FriendView"/>); games open as their entry
/// (Nav.FriendEntry). Opened through Nav.FriendPage.
/// </summary>
static class FriendPages {
  static readonly ILogger Log = LogManager.GetLogger();
  static readonly (string key, string label)[] Pages = { ("dashboard", "Dashboard"), ("library", "Library"), ("profile", "Taste") };

  /// <summary>"dashboard", "library" or "profile" ("taste" works too); <paramref name="name"/> when the caller knows it.</summary>
  public static async void Open(BlastlogPlugin plugin, string friendId, string page, string name = null) {
    try {
      if (string.IsNullOrEmpty(name)) name = await NameOf(plugin, friendId);
      var friend = new FriendView(friendId, name);
      LinkPages.Show(plugin, friend.Name, close => new Shell(plugin, friend, page), width: 1280, height: 880);
    } catch (Exception e) { Log.Error(e, "Blastlog friend pages"); }
  }

  static async Task<string> NameOf(BlastlogPlugin plugin, string friendId) {
    string Find(JObject data) => (data?["friends"] as JArray ?? new JArray()).OfType<JObject>().Where(f => S(f, "id") == friendId).Select(f => S(f, "displayName")).FirstOrDefault();
    var known = Find(plugin.SavedRead("friends"));
    if (known != null) return known;
    try { return Find((await plugin.Mirror("friends", false)).Value); } catch (Exception) { return null; }
  }

  static string Key(string page) => page == "library" ? "library" : page == "profile" || page == "taste" ? "profile" : "dashboard";

  /// <summary>The window: the friend's name, the three pages as tabs, and the page shown (each kept once built).</summary>
  sealed class Shell : StackPanel {
    readonly BlastlogPlugin plugin; readonly FriendView friend;
    readonly WrapPanel tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 22) };
    readonly ContentControl body = new ContentControl { Focusable = false, IsTabStop = false };
    readonly Dictionary<string, FrameworkElement> built = new Dictionary<string, FrameworkElement>();
    string showing;

    public Shell(BlastlogPlugin plugin, FriendView friend, string page) {
      this.plugin = plugin; this.friend = friend;
      var head = new DockPanel { LastChildFill = true };
      var face = LinkPages.Face(friend.Name, 52); face.Margin = new Thickness(0, 0, 16, 0);
      DockPanel.SetDock(face, Dock.Left); head.Children.Add(face);
      head.Children.Add(LinkPages.Head("Friend", friend.Name, $"{friend.Name}’s Blastlog, as you see it as their friend. Only they can change it."));
      Children.Add(head);
      AutomationProperties.SetName(tabs, friend.Name + "’s pages");
      Children.Add(tabs); Children.Add(body);
      Show(Key(page));
    }

    void Show(string key, string libraryQuery = null) {
      try {
        showing = key;
        tabs.Children.Clear();
        foreach (var (k, label) in Pages) {
          var t = Toggle(label, k == key, () => Show(k), $"{friend.Name}’s {label}");
          t.Margin = new Thickness(0, 0, 8, 8);
          tabs.Children.Add(t);
        }
        if (!built.TryGetValue(key, out var view)) {
          view = key == "library" ? new LibraryView(plugin, (Blastlog.Sync.Core.Design.LibraryFilters.Parse(libraryQuery), 0, 0, null), friend)
            : key == "profile" ? new TasteView(plugin, friend)
            : (FrameworkElement)new FriendDashboardView(plugin, friend, query => Show("library", query ?? ""));
          built[key] = view;
        } else if (key == "library" && libraryQuery != null && view is LibraryView library) library.ApplyQuery(libraryQuery);
        body.Content = view;
        var scroller = PnKit.FindAncestor<ScrollViewer>(this);
        scroller?.ScrollToTop();
      } catch (Exception e) { Log.Error(e, "Blastlog friend pages: showing " + key); }
    }
  }
}
}
