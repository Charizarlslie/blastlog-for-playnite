using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Blastlog.Playnite.Ui;
using Blastlog.Sync.Core.Design;
using Newtonsoft.Json.Linq;
using static Blastlog.Playnite.Screens.TasteUi;

namespace Blastlog.Playnite.Screens {
/// <summary>
/// Settings, natively: everything a player sets in the site's Settings menu (App.tsx AccountMenu: appearance, guided
/// start, linked sign-ins, log out) and Account settings (Account.tsx: game lengths, subscriptions, Playnite sync, comfort
/// picks, sale alerts, notifications, your data), in one window opened from the sidebar's Settings and Your taste.
/// Changes save as they're made through the site's own routes (docs/PLAYNITE_SYNC.md "Round 4" in the Blastlog repo), and
/// a card whose read Blastlog doesn't answer is left out, as the site hides a card that's switched off. What needs the
/// browser (making and revoking tokens, downloading your data, deleting the account, Google and Apple sign-ins) links to
/// the site. Your picture is <see cref="SettingsPicture"/>.
/// </summary>
static class SettingsWindow {
  /// <summary>Opens the window; <paramref name="done"/> runs after it closes (a screen refreshes then).</summary>
  public static void Open(BlastlogPlugin plugin, Action done = null) =>
    LinkPages.Show(plugin, "Settings", close => new View(plugin, close), done, width: 940, height: 900);

  static readonly Tuple<string, string, string>[] Appearances = {
    Tuple.Create("device", "System", "Matches your device's light or dark setting"), Tuple.Create("light", "Light", (string)null), Tuple.Create("dark", "Dark", (string)null) };
  static readonly Tuple<string, string, string>[] Levels = {
    Tuple.Create("main_story", "Main story", (string)null), Tuple.Create("main_plus_extra", "Main + extras", (string)null), Tuple.Create("completionist", "Completionist", (string)null) };

  sealed class View : StackPanel {
    readonly BlastlogPlugin plugin; readonly Action close;
    readonly StackPanel body = new StackPanel();
    JObject me, settings, subscriptions, replay, sales, notifications, tokens;
    string lengthLevel; bool loaded;

    public View(BlastlogPlugin plugin, Action close) {
      this.plugin = plugin; this.close = close;
      MaxWidth = 860; HorizontalAlignment = HorizontalAlignment.Left;
      CollectionUi.WearUsual(this, plugin);
      Children.Add(LinkPages.Head("Blastlog", "Settings", "Your Blastlog account and settings. Changes save as you make them, here and on the site."));
      Children.Add(body);
      body.Children.Add(T("Loading your settings…", "Blastlog.Muted"));
      Loaded += (s, e) => { if (!loaded) { loaded = true; Run(Load, "settings"); } };
    }

    async Task Load() {
      if (!plugin.Configured) { NotSignedIn(); return; }
      Task<JObject> Read(string path) => plugin.Mirror(path, true).ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result.Value : null);
      var reads = new[] { "me", "settings", "subscriptions", "replay-picks", "sale-prices", "notifications", "tokens" }.Select(Read).ToArray();
      await Task.WhenAll(reads);
      me = reads[0].Result; settings = reads[1].Result; subscriptions = reads[2].Result; replay = reads[3].Result;
      sales = reads[4].Result; notifications = reads[5].Result; tokens = reads[6].Result;
      lengthLevel = settings.S("lengthLevel");
      // Before the server sends settings, the length level is on the taste profile (as the site's Your taste reads it).
      if (lengthLevel == null) lengthLevel = (await plugin.Mirror("taste-profile", false)).Value.S("lengthLevel");
      if (me == null && settings == null) { Unavailable(); return; }
      Build();
    }

    void NotSignedIn() {
      body.Children.Clear();
      body.Children.Add(LinkPages.Card("Not signed in", "Playnite signs in to Blastlog with a sync token. Make one in Account settings on the site, then paste it in the plugin's settings.",
        Wrap(Kit.Button("Paste a token", () => { close(); plugin.OpenSyncSettings(); }).Also(b => b.Margin = new Thickness(0, 0, 10, 0)),
          Kit.Button("Make a token on Blastlog  ↗", () => Nav.Site(plugin, "/profile?settings=1"), "Blastlog.Ghost"))));
    }

    void Unavailable() {
      body.Children.Clear();
      body.Children.Add(T("Blastlog didn't answer. Check your connection and try again.", "Blastlog.Body", "Blastlog.Danger"));
      body.Children.Add(Kit.Button("Try again", Load, "Blastlog.Ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 12, 0, 0); }));
    }

    void Build() {
      body.Children.Clear();
      body.Children.Add(SettingsPicture.Card(plugin, me, settings, () => Run(Load, "settings reload")));
      body.Children.Add(Section(AppearanceCard));
      if (lengthLevel != null) body.Children.Add(Section(LengthCard));
      if (subscriptions != null && subscriptions.B("available") && subscriptions.Objects("services").Count > 0) body.Children.Add(Section(SubscriptionsCard));
      if (replay != null && replay.B("available")) body.Children.Add(Section(ReplayCard));
      if (sales != null && sales.B("available")) body.Children.Add(Section(SalesCard));
      if (notifications != null && (notifications.D("devices") > 0 || notifications.Objects("email").Count > 0)) body.Children.Add(Section(NotificationsCard));
      body.Children.Add(SyncCard());
      body.Children.Add(ExtrasCard());
      body.Children.Add(DataCard());
      body.Children.Add(SignOutCard());
    }

    /// <summary>A card that redraws itself after a save: <paramref name="build"/> gets the host and an error line to show.</summary>
    static ContentControl Section(Func<Action<string>, Border> build) {
      var host = new ContentControl();
      void Draw(string error) {
        var card = build(e => Draw(e));
        if (error != null && card.Child is StackPanel inner) inner.Children.Add(CollectionUi.Error(error));
        host.Content = card;
      }
      Draw(null);
      return host;
    }

    /// <summary>Saves through the site's route, then redraws the card with the answer (or the refusal in words).</summary>
    async Task Save(string path, object payload, Action<JObject> took, Action<string> redraw, Func<string, string> words = null) {
      var (value, error) = await plugin.TryWrite(path, payload, HttpMethod.Put);
      if (value != null) { took(value); redraw(null); return; }
      redraw(words?.Invoke(error) ?? BlastlogPlugin.RefusalWords(error));
    }

    Border AppearanceCard(Action<string> redraw) {
      var current = settings.S("appearance") ?? "device";
      var content = Stack(null, PnKit.Segmented("Appearance", Appearances, current, pick => Run(() =>
        Save("me/appearance", new { appearance = pick }, v => { if (settings != null) settings["appearance"] = v.S("appearance") ?? pick; }, redraw), "appearance")));
      var playnite = ThemeBridge.CurrentMode == Mode.Light ? "Blastlog Light" : "Blastlog";
      content.Children.Add(T($"This is how the site looks for you. Playnite's own look is its theme, {playnite} right now: pick Blastlog or Blastlog Light in Playnite's Settings, Appearance, Theme.", "Blastlog.Muted")
        .Also(t => { t.FontSize = 14; t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 720; t.HorizontalAlignment = HorizontalAlignment.Left; }));
      return LinkPages.Card("Appearance", null, content);
    }

    Border LengthCard(Action<string> redraw) => LinkPages.Card("Game lengths",
      "How you play: every length you see, and what counts as a Quick one or a Long haul, uses this.",
      PnKit.Segmented("Game lengths", Levels, lengthLevel, pick => Run(() =>
        Save("taste/length-level", new { level = pick }, v => lengthLevel = v.S("level") ?? pick, redraw), "length level")));

    Border SubscriptionsCard(Action<string> redraw) {
      var services = subscriptions.Objects("services");
      var on = services.Where(s => s.B("on")).Select(s => s.S("key")).ToList();
      var checks = Stack(null);
      foreach (var service in services) {
        var key = service.S("key");
        checks.Children.Add(LinkPages.Check(service.S("name") ?? key, service.B("on"), value => Run(() =>
          Save("subscriptions", new { services = value ? on.Concat(new[] { key }).Distinct().ToArray() : on.Where(k => k != key).ToArray() },
            v => subscriptions = v, redraw, _ => "That didn't save. Try again."), "subscriptions")));
      }
      return LinkPages.Card("Subscriptions", "Games included with yours get a tag in Play next, Discover and on game pages.", checks);
    }

    Border ReplayCard(Action<string> redraw) => LinkPages.Card("Discover",
      "When a few new games in a row miss, Discover can suggest one of your old favorites to play again.",
      LinkPages.Check("Suggest a replay when new picks keep missing", replay.B("on"), value => Run(() =>
        Save("replay-picks", new { on = value }, v => replay = v, redraw), "replay picks")));

    Border SalesCard(Action<string> redraw) {
      var checks = Stack(null,
        LinkPages.Check("Tell me when a game hits its lowest price ever", sales.B("lowestAlerts"), value => Run(() =>
          Save("sale-alerts", new { lowestAlerts = value }, v => sales = v, redraw), "sale alerts")),
        LinkPages.Check("Count key-shop prices (often cheaper, sold by resellers)", sales.B("keyshops"), value => Run(() =>
          Save("sale-alerts", new { keyshops = value }, v => sales = v, redraw), "sale alerts")));
      return LinkPages.Card("Sale alerts", "Wishlist prices from GG.deals (US stores). “On sale” shows in your wishlist" +
        (sales.B("alertsOn") ? "; alerts come to the iPhone app" : "") + ". Set your own price on a game's page on the site.", checks);
    }

    Border NotificationsCard(Action<string> redraw) {
      var content = Stack(null);
      UIElement Switches(IEnumerable<JObject> items) {
        var list = Stack(null);
        foreach (var item in items) {
          var kind = item.S("kind");
          list.Children.Add(LinkPages.Check(item.S("label") ?? kind, item.B("enabled"), value => Run(() =>
            Save("notifications", new { kind, enabled = value }, v => notifications = v, redraw), "notifications")));
        }
        return list;
      }
      UIElement Note(string text) => T(text, "Blastlog.Muted").Also(t => { t.FontSize = 14; t.Margin = new Thickness(0, 10, 0, 2); });
      if (notifications.D("devices") > 0) {
        var phone = notifications.Objects("settings");
        content.Children.Add(Note("What Blastlog tells you on your iPhone."));
        content.Children.Add(Switches(phone.Where(i => !i.B("admin"))));
        var admin = phone.Where(i => i.B("admin")).ToList();
        if (admin.Count > 0) { content.Children.Add(Note("For you as admin")); content.Children.Add(Switches(admin)); }
      }
      var email = notifications.Objects("email");
      if (email.Count > 0) { content.Children.Add(Note("By email, to the address you sign in with.")); content.Children.Add(Switches(email)); }
      return LinkPages.Card("Notifications", null, content);
    }

    /// <summary>Playnite sync: the token this PC uses, every live token and when it was last used, and the plugin's own settings.</summary>
    Border SyncCard() {
      var content = Stack(null);
      var mine = me?["token"] as JObject;
      if (mine != null) {
        var write = mine.Strings("scopes").Contains("sync:write");
        content.Children.Add(PnKit.Line("Blastlog.Body", PnKit.Run("This PC uses "), PnKit.Run(mine.S("name") ?? "a token", null, FontWeights.Bold),
          PnKit.Run(write ? ", which can change your library." : ", which is read only: Playnite can show Blastlog but not change it.", "Blastlog.TextMuted")).Also(t => t.TextWrapping = TextWrapping.Wrap));
      }
      var live = tokens.Objects("items").Where(t => t.S("revokedAt") == null && !(DateTimeOffset.TryParse(t.S("expiresAt"), out var x) && x < DateTimeOffset.Now)).ToList();
      if (live.Count > 0) {
        var list = Stack(new Thickness(0, 12, 0, 0));
        foreach (var t in live) {
          var used = DateTimeOffset.TryParse(t.S("lastUsedAt"), out var at) ? "last used " + at.LocalDateTime.ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture) : "never used";
          var here = mine != null && t.S("id") == mine.S("id");
          var scopes = t.Strings("scopes").Contains("sync:write") ? "can change your library" : "read only";
          list.Children.Add(PnKit.Line("Blastlog.Body", PnKit.Run(t.S("name") ?? "Token", null, FontWeights.SemiBold), PnKit.Run(here ? " (this PC)" : "", "Blastlog.GameText", FontWeights.SemiBold),
            PnKit.Run($"  {t.S("prefix")}… · {scopes} · {used}", "Blastlog.TextMuted")).Also(x => { x.FontSize = 15; x.TextWrapping = TextWrapping.Wrap; x.Margin = new Thickness(0, 0, 0, 6); }));
        }
        content.Children.Add(list);
      }
      var last = plugin.LastSyncLine();
      if (!string.IsNullOrEmpty(last)) content.Children.Add(T(last, "Blastlog.Muted").Also(t => { t.FontSize = 14; t.Margin = new Thickness(0, 8, 0, 0); t.TextWrapping = TextWrapping.Wrap; }));
      content.Children.Add(Wrap(
        Kit.Button("Sync settings", () => { close(); plugin.OpenSyncSettings(); }, "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 14, 10, 0)),
        Kit.Button("Make or revoke tokens on Blastlog  ↗", () => Nav.Site(plugin, "/profile?settings=1"), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 14, 10, 0))));
      return LinkPages.Card("Playnite sync", "Connects this plugin to your Blastlog. Tokens are made and revoked on the site; each is shown once.", content);
    }

    /// <summary>The site's Your extras and the Settings menu's Guided start.</summary>
    Border ExtrasCard() {
      var year = DateTime.Now.Year;
      var links = Stack(null,
        Link("Your year in games  →", () => { close(); Nav.Year(plugin, year); }),
        Link("Pick your character-sheet look on Compare  →", () => { close(); Nav.SheetLook(plugin); }),
        Link("Taste history  ↗", () => Nav.Site(plugin, "/profile/history")),
        Link("Guided start  ↗", () => Nav.Site(plugin, "/guided-start")));
      foreach (var b in links.Children.OfType<Button>()) { b.HorizontalAlignment = HorizontalAlignment.Left; b.MinHeight = 44; }
      return LinkPages.Card("Your extras", "All free, for everyone.", links);
    }

    /// <summary>Your data and privacy: downloads and deleting need the browser, so they open the site's Account settings.</summary>
    Border DataCard() {
      var content = Stack(null,
        T("Download everything Blastlog keeps about you (your account, games, ratings, reviews, taste and friends) as a file or your library as a spreadsheet, or delete your account, in Account settings on the site.", "Blastlog.Muted")
          .Also(t => { t.FontSize = 15; t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 720; t.HorizontalAlignment = HorizontalAlignment.Left; }),
        Wrap(Kit.Button("Download or delete on Blastlog  ↗", () => Nav.Site(plugin, "/profile?settings=1"), "Blastlog.Ghost").Also(b => b.Margin = new Thickness(0, 14, 10, 0))),
        Wrap(Link("Privacy policy  ↗", () => Nav.Site(plugin, "/privacy")), Link("Terms  ↗", () => Nav.Site(plugin, "/terms")), Link("About  ↗", () => Nav.Site(plugin, "/about")))
          .Also(w => w.Margin = new Thickness(-6, 10, 0, 0)));
      return LinkPages.Card("Your data and privacy", null, content);
    }

    /// <summary>
    /// Signing out: Playnite holds a token, not a site sign-in, so signing out here forgets the token on this PC (it keeps
    /// working on Blastlog until it's revoked there), and logging out of the site leaves Playnite signed in.
    /// </summary>
    Border SignOutCard() {
      var content = Stack(null);
      content.Children.Add(T("Playnite isn't signed in with Google or Apple: it uses the sync token above. Logging out of the site doesn't sign Playnite out. To sign Playnite out, forget the token on this PC; to stop it everywhere, revoke it on Blastlog too. Google and Apple sign-ins are linked on the site.", "Blastlog.Muted")
        .Also(t => { t.FontSize = 15; t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 720; t.HorizontalAlignment = HorizontalAlignment.Left; }));
      var actions = new ContentControl { Margin = new Thickness(0, 14, 0, 0) };
      void Ask() {
        actions.Content = Confirm("Forget the token on this PC? Sync and the Blastlog screens stop until you paste a token again.", "Forget the token", true, () => {
          plugin.ForgetToken(); close();
          plugin.Api.Dialogs.ShowMessage("Playnite is signed out of Blastlog on this PC. The token still works on Blastlog until you revoke it in Account settings on the site.", "Blastlog");
        }, Plain);
      }
      void Plain() { actions.Content = Kit.Button("Sign Playnite out…", Ask, "Blastlog.DangerButton").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left); }
      Plain();
      content.Children.Add(actions);
      return LinkPages.Card("Sign out", null, content);
    }
  }
}
}
