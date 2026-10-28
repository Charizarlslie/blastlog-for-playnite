using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using Blastlog.Sync.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Game = Playnite.SDK.Models.Game;
using Playnite.SDK.Plugins;

namespace Blastlog.Playnite {
/// <summary>
/// Blastlog for Playnite (docs/PLAN.md). Pairs Playnite games with Blastlog entries, syncs status and stars both ways and
/// playtime one way, never deletes anything, previews before its first real sync, and shows Blastlog's Next up, Play
/// next and game pages inside Playnite. Talks only to the server in its settings, with the token in the header.
/// </summary>
public sealed class BlastlogPlugin : GenericPlugin {
  static readonly ILogger Log = LogManager.GetLogger();
  static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
  readonly BlastlogSettings settings; readonly SemaphoreSlim running = new SemaphoreSlim(1, 1);
  readonly string storePath, cachePath; SyncStore store; Dictionary<string, CachedRead> cache;
  DispatcherTimer timer; DateTime nextAuto = DateTime.MaxValue, lastSyncEnd = DateTime.MinValue; int failures; CancellationTokenSource debounce;
  sealed class CachedRead { public JObject Value {get;set;} public string ETag {get;set;} public DateTimeOffset At {get;set;} }

  public override Guid Id => new Guid("80b79ef3-134e-4e5e-92f2-6baeffbfc8a5");
  public BlastlogPlugin(IPlayniteAPI api) : base(api) {
    settings = new BlastlogSettings(this);
    Properties = new GenericPluginProperties { HasSettings = true };
    storePath = Path.Combine(GetPluginUserDataPath(), "sync.json"); cachePath = Path.Combine(GetPluginUserDataPath(), "mirror-cache.json");
    store = SyncStore.Load(storePath);
    try { cache = File.Exists(cachePath) ? JsonConvert.DeserializeObject<Dictionary<string, CachedRead>>(File.ReadAllText(cachePath)) : null; } catch (Exception) { cache = null; }
    cache = cache ?? new Dictionary<string, CachedRead>();
    AddCustomElementSupport(new AddCustomElementSupportArgs { SourceName = "Blastlog", ElementList = new List<string> { "GamePanel", "Fingerprint", "Status" } });
  }

  /// <summary>Playnite's API for the views (the base class keeps its own protected).</summary>
  public IPlayniteAPI Api => PlayniteApi;
  public string ServerUrl => (settings.Data.ServerUrl ?? "").TrimEnd('/');
  public bool Configured => settings.Token != null && Uri.TryCreate(ServerUrl, UriKind.Absolute, out _);
  BlastlogClient Client() => new BlastlogClient(Http, new Uri(ServerUrl), settings.Token);
  SyncEngine Engine() => new SyncEngine(Client(), new PlayniteLibrary(PlayniteApi), store, new ClientInfo { Version = PlayniteApi.ApplicationInfo.ApplicationVersion.ToString(), Plugin = "0.2.2" })
    { Rule = settings.Data.ConflictRule, IncludeHidden = settings.Data.IncludeHidden };
  void SaveStore() { try { store.Save(storePath); } catch (Exception e) { Log.Error(e, "Blastlog: couldn't save the sync store"); } }

  // ---- Settings ----
  public override ISettings GetSettings(bool firstRunSettings) => settings;
  public override UserControl GetSettingsView(bool firstRunSettings) => new BlastlogSettingsView(settings, this);
  public void SettingsChanged() { cache.Clear(); }
  public async Task<string> TestConnection(string server, string token) {
    if (string.IsNullOrEmpty(token)) return "Paste a token first.";
    try {
      var me = (await new BlastlogClient(Http, new Uri(server.TrimEnd('/')), token).MeAsync()).Value;
      var write = ((JArray)me["token"]?["scopes"])?.Any(s => (string)s == "sync:write") == true;
      return $"Signed in as {(string)me["user"]?["displayName"]}{((bool?)me["server"]?["demo"] == true ? " (demo)" : "")}. {(write ? "Can read and change your library." : "Read only: syncing won't change Blastlog.")}";
    } catch (BlastlogException e) { return Words(e); } catch (Exception e) { return "Couldn't reach the server: " + e.Message; }
  }
  static string Words(BlastlogException e) => e.Error == "sync_token_invalid" ? "Blastlog didn't accept that token (wrong, revoked, expired, or sync is switched off)."
    : e.Error == "supporter_tier_required" ? "Playnite sync comes with Blastlog's $10 supporter tier. You can get it on Blastlog: Your taste, then Your account."
    : e.Error == "rate_limited" ? $"Too many requests just now; try again in {e.RetryAfterSeconds ?? 60} seconds." : e.Error == "sync_scope_required" ? "This token is read only. Make one that can change your library."
    : e.Error == "invalid_request" ? "Blastlog didn't accept what the plugin sent" + (e.Details != null ? $" ({e.Details})" : "") + ". This is a plugin bug: send Claude a screenshot."
    : e.Error == "too_many_items" ? "That was more than Blastlog takes at once. This is a plugin bug: send Claude a screenshot."
    : $"Blastlog said no ({e.Error}).";

  // ---- Menus, sidebar and theme elements ----
  public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args) {
    const string section = "@Blastlog";
    yield return new MainMenuItem { MenuSection = section, Description = "Sync now", Action = a => _ = SyncNow(interactive: true) };
    yield return new MainMenuItem { MenuSection = section, Description = "Preview sync", Action = a => _ = PreviewSync() };
    yield return new MainMenuItem { MenuSection = section, Description = "Pair games…", Action = a => _ = PairGames() };
    yield return new MainMenuItem { MenuSection = section, Description = "Undo the last sync", Action = a => _ = UndoLast() };
    yield return new MainMenuItem { MenuSection = section, Description = "Open Blastlog", Action = a => UiKit.Open(ServerUrl + "/dashboard") };
    yield return new MainMenuItem { MenuSection = section, Description = "Remove Blastlog tags", Action = a => {
      var n = BlastlogTags.RemoveAll(PlayniteApi); settings.Data.BlastlogTags = false; settings.Data.TraitTags = false; SavePluginSettings(settings.Data);
      PlayniteApi.Dialogs.ShowMessage($"Removed Blastlog tags from {n} game(s), and switched them off in settings. Your own tags are untouched."); } };
  }
  public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args) {
    if (args.Games.Count != 1) yield break;
    var game = args.Games[0]; var entryId = EntryFor(game.Id);
    if (entryId == null) yield break;
    yield return new GameMenuItem { MenuSection = "Blastlog", Description = "Open Blastlog page", Action = a => UiKit.Open(ServerUrl + "/library/" + entryId) };
    yield return new GameMenuItem { MenuSection = "Blastlog", Description = "Unpair from Blastlog", Action = a => _ = Unpair(game) };
  }
  public override IEnumerable<SidebarItem> GetSidebarItems() {
    yield return new SidebarItem { Title = "Blastlog", Type = SiderbarItemType.View, Icon = new TextBlock { Text = "B", FontSize = 20, FontWeight = System.Windows.FontWeights.Bold }, Opened = () => new DashboardView(this) };
  }
  public override Control GetGameViewControl(GetGameViewControlArgs args) => args.Name == "GamePanel" || args.Name == "Fingerprint" || args.Name == "Status" ? new GameElement(this, args.Name) : null;

  // ---- Events: auto-sync (after the first real sync only) ----
  public override void OnApplicationStarted(OnApplicationStartedEventArgs args) {
    timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
    timer.Tick += (s, e) => { if (DateTime.Now >= nextAuto) _ = AutoSync(); };
    timer.Start();
    ScheduleAuto(TimeSpan.FromSeconds(20));
    // The user's own edits sync too (debounced); the sync's own writes, just after it, don't start another.
    PlayniteApi.Database.Games.ItemUpdated += (s, e) => { if (DateTime.Now - lastSyncEnd > TimeSpan.FromSeconds(15) && running.CurrentCount == 1) Debounced(); };
  }
  public override void OnApplicationStopped(OnApplicationStoppedEventArgs args) { timer?.Stop(); SaveStore(); }
  public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) => Debounced();
  public override void OnGameStopped(OnGameStoppedEventArgs args) => Debounced();
  void ScheduleAuto(TimeSpan after) => nextAuto = DateTime.Now + after;
  /// <summary>Edits and game sessions sync ten seconds after the last one, not once each.</summary>
  void Debounced() {
    debounce?.Cancel(); debounce = new CancellationTokenSource(); var token = debounce.Token;
    Task.Delay(TimeSpan.FromSeconds(10), token).ContinueWith(t => { if (!t.IsCanceled) _ = AutoSync(); }, TaskScheduler.Default);
  }
  async Task AutoSync() {
    ScheduleAuto(TimeSpan.FromMinutes(30));
    if (!settings.Data.AutoSync || !Configured || !store.FirstSyncDone) return;
    try { await RunSync(dryRun: false); failures = 0; }
    catch (Exception e) {
      // Offline or the server is down: try again after 1, 5, 15, then 60 minutes. Never on Playnite's UI thread.
      var waits = new[] { 1, 5, 15, 60 }; ScheduleAuto(TimeSpan.FromMinutes(waits[Math.Min(failures++, waits.Length - 1)]));
      Log.Warn("Blastlog auto-sync failed: " + (e is BlastlogException b ? b.Error : e.Message));
    }
  }

  // ---- Sync, preview, pairing, undo ----
  async Task<SyncOutcome> RunSync(bool dryRun) {
    if (!await running.WaitAsync(0)) return null;
    try {
      var outcome = await Task.Run(() => Engine().SyncAsync(dryRun));
      if (!dryRun) { SaveStore(); await RefreshTags(); cache.Remove("next-up?mode=recommended"); }
      if (!dryRun && outcome.Plan.Conflicts.Count > 0) PlayniteApi.Notifications.Add(new NotificationMessage("blastlog-conflicts",
        $"Blastlog: {outcome.Plan.Conflicts.Count} game(s) changed differently on both sides; they were left as they are. Preview sync lists them.", NotificationType.Info));
      return outcome;
    } finally { lastSyncEnd = DateTime.Now; running.Release(); }
  }

  public async Task SyncNow(bool interactive) {
    if (!Configured) { PlayniteApi.Dialogs.ShowMessage("Add your Blastlog sync token first: Add-ons → Extension settings → Blastlog."); return; }
    if (!store.FirstSyncDone) { await PreviewSync(); return; }
    try { var o = await RunSync(false); if (interactive && o != null) PlayniteApi.Dialogs.ShowMessage(Summary(o)); }
    catch (BlastlogException e) { PlayniteApi.Dialogs.ShowErrorMessage("Syncing: " + Words(e), "Blastlog"); }
    catch (Exception e) { PlayniteApi.Dialogs.ShowErrorMessage("Couldn't sync: " + e.Message, "Blastlog"); }
  }
  /// <summary>A dry run, shown; "Sync these" then runs it for real. The first real sync always comes through here.</summary>
  public async Task PreviewSync() {
    if (!Configured) { PlayniteApi.Dialogs.ShowMessage("Add your Blastlog sync token first: Add-ons → Extension settings → Blastlog."); return; }
    if (store.Pairs.Count == 0) { PlayniteApi.Dialogs.ShowMessage("Pair your games first: Blastlog → Pair games."); return; }
    try {
      var preview = await RunSync(true); if (preview == null) return;
      if (PreviewWindow.Show(this, preview, EntryName) == true) { var o = await RunSync(false); if (o != null) PlayniteApi.Dialogs.ShowMessage(Summary(o)); }
    } catch (BlastlogException e) { PlayniteApi.Dialogs.ShowErrorMessage("Preview sync: " + Words(e), "Blastlog"); }
    catch (Exception e) { PlayniteApi.Dialogs.ShowErrorMessage("Couldn't sync: " + e.Message, "Blastlog"); }
  }
  public async Task PairGames() {
    if (!Configured) { PlayniteApi.Dialogs.ShowMessage("Add your Blastlog sync token first: Add-ons → Extension settings → Blastlog."); return; }
    try {
      var engine = Engine(); await Task.Run(() => engine.RefreshAsync());
      var choices = await Task.Run(() => engine.PairingAsync());
      if (PairingWindow.Show(this, choices) != true) return;
      var result = await Task.Run(() => engine.ApplyPairingAsync(choices)); SaveStore();
      PlayniteApi.Dialogs.ShowMessage($"Paired: {result.Counts.Applied}.{(choices.Any(c => c.Match.Result == "skipped") ? $" {choices.Count(c => c.Match.Result == "skipped")} couldn't be sent (listed in the pairing window)." : "")} {(result.Counts.Rejected > 0 ? result.Counts.Rejected + " couldn't be (they're listed in Blastlog's import or already there). " : "")}Games sent to the import wait for you on Blastlog → Library → Import games. Next: Preview sync.");
    } catch (BlastlogException e) { PlayniteApi.Dialogs.ShowErrorMessage("Pairing: " + Words(e), "Blastlog"); }
    catch (Exception e) { PlayniteApi.Dialogs.ShowErrorMessage("Couldn't pair: " + e.Message, "Blastlog"); }
  }
  async Task UndoLast() {
    var run = store.LastRun?.RunId;
    if (run == null) { PlayniteApi.Dialogs.ShowMessage("No sync to undo yet."); return; }
    try {
      var r = (await Task.Run(() => Client().UndoAsync(run))).Value;
      PlayniteApi.Dialogs.ShowMessage($"Undone on Blastlog: {r.Undone} change(s).{(r.Kept.Count > 0 ? " Kept (changed since): " + string.Join(", ", r.Kept.Select(k => (string)k["title"])) : "")} Playnite's own copies stay as they are.");
      store.LastRun.RunId = null; SaveStore();
    } catch (BlastlogException e) { PlayniteApi.Dialogs.ShowErrorMessage(e.Error == "nothing_to_undo" ? "That sync is already undone." : Words(e), "Blastlog"); }
  }
  async Task Unpair(Game game) {
    try { await Task.Run(() => Engine().UnpairAsync(game.Id.ToString())); SaveStore(); }
    catch (BlastlogException e) { PlayniteApi.Dialogs.ShowErrorMessage(Words(e), "Blastlog"); }
  }
  public async Task StillPlaying(string entryId) {
    var push = new PushRequest { Origin = "panel", Client = new ClientInfo { Plugin = "0.2.2" } };
    push.CheckIns.Add(new JObject { ["entryId"] = entryId, ["answer"] = "still_playing" });
    try { await Task.Run(() => Client().PushAsync(push)); cache.Remove("now-playing"); } catch (BlastlogException e) { PlayniteApi.Dialogs.ShowErrorMessage(Words(e), "Blastlog"); }
  }
  static string Summary(SyncOutcome o) {
    var c = o.Response?.Counts ?? new PushCounts();
    return $"Synced: {c.Applied} change(s) to Blastlog, {o.PlayniteWrites} in Playnite." + (o.Plan.Conflicts.Count > 0 ? $" {o.Plan.Conflicts.Count} changed on both sides (left as they are)." : "")
      + (c.Rejected > 0 ? $" {c.Rejected} weren't taken." : "") + (o.Notes.Count > 0 ? " " + string.Join(" ", o.Notes) : "") + " You can undo it on Blastlog or with Blastlog → Undo the last sync.";
  }

  // ---- Tags (docs/PLAN.md §4.6) ----
  async Task RefreshTags() {
    if (!settings.Data.BlastlogTags && !settings.Data.TraitTags) return;
    var nextUp = (string)(await Mirror("next-up?mode=recommended", fresh: true)).Value?["entry"]?["id"];
    var traits = settings.Data.TraitTags ? await TraitsByEntry() : new Dictionary<string, List<string>>();
    var wanted = new Dictionary<string, HashSet<string>>();
    foreach (var pair in store.Pairs) {
      if (!store.Entries.TryGetValue(pair.Value, out var e)) continue;
      var tags = new HashSet<string>();
      if (settings.Data.BlastlogTags) {
        if (e.InBacklog) tags.Add(BlastlogTags.Backlog);
        if (e.Ownership == "wishlist") tags.Add(BlastlogTags.Wishlist);
        if (e.Status == "playing") tags.Add(BlastlogTags.Playing);
        if (e.EntryId == nextUp) tags.Add(BlastlogTags.NextUp);
      }
      if (traits.TryGetValue(e.EntryId, out var names)) foreach (var n in names.Take(12)) tags.Add(BlastlogTags.TraitPrefix + n);
      wanted[pair.Key] = tags;
    }
    BlastlogTags.Apply(PlayniteApi, wanted);
  }
  /// <summary>Trait names per paired entry, from the bulk fingerprints read (100 at a time), refreshed at most daily.</summary>
  async Task<Dictionary<string, List<string>>> TraitsByEntry() {
    var result = new Dictionary<string, List<string>>(); var ids = store.Pairs.Values.Distinct().ToList();
    for (var i = 0; i < ids.Count; i += 100) {
      var batch = ids.Skip(i).Take(100).ToArray(); var key = "fingerprints:" + string.Join(",", batch).GetHashCode();
      if (!cache.TryGetValue(key, out var hit) || DateTimeOffset.Now - hit.At > TimeSpan.FromDays(1)) {
        try { cache[key] = hit = new CachedRead { Value = (await Task.Run(() => Client().FingerprintsAsync(batch))).Value, At = DateTimeOffset.Now }; } catch (Exception) { if (hit == null) continue; }
      }
      foreach (var item in (hit.Value?["items"] as JArray ?? new JArray()).OfType<JObject>())
        result[(string)item["entryId"]] = (item["fingerprint"]?.SelectTokens("$..traits[*]") ?? Enumerable.Empty<JToken>()).OfType<JObject>()
          .Select(t => (string)(t["label"] ?? t["name"])).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
    }
    SaveCache();
    return result;
  }

  // ---- Mirror reads with a local cache (works offline, "last updated") ----
  public async Task<Response<JObject>> Mirror(string path, bool fresh) {
    cache.TryGetValue(path, out var hit);
    if (!Configured) return new Response<JObject> { Value = hit?.Value };
    if (!fresh && hit != null && DateTimeOffset.Now - hit.At < TimeSpan.FromMinutes(10)) return new Response<JObject> { Value = hit.Value };
    try {
      var r = await Task.Run(() => Client().GetAsync(path, hit?.ETag));
      if (r.NotModified && hit != null) hit.At = DateTimeOffset.Now; else cache[path] = hit = new CachedRead { Value = r.Value, ETag = r.ETag, At = DateTimeOffset.Now };
      SaveCache();
    } catch (Exception e) { Log.Warn("Blastlog read failed (" + path + "): " + (e is BlastlogException b ? b.Error : e.Message)); }
    return new Response<JObject> { Value = hit?.Value };
  }
  void SaveCache() { try { File.WriteAllText(cachePath, JsonConvert.SerializeObject(cache)); } catch (Exception) { } }
  public string CacheLine() { var at = cache.Values.Select(c => (DateTimeOffset?)c.At).Max(); return at == null ? "" : $"Last updated {at.Value.LocalDateTime:g}."; }
  public string LastSyncLine() => store.LastRun == null ? (store.FirstSyncDone ? "" : "Not synced yet: Pair games, then Preview sync.")
    : $"Last sync {store.LastRun.At.LocalDateTime:g}: {store.LastRun.Counts?.Applied ?? 0} to Blastlog, {store.LastRun.PlayniteWrites} in Playnite{(store.LastRun.Conflicts > 0 ? $", {store.LastRun.Conflicts} changed on both sides" : "")}.";

  // ---- Lookups for the views ----
  public string EntryFor(Guid gameId) => store.Pairs.TryGetValue(gameId.ToString(), out var e) ? e : null;
  public Entry CachedEntry(string entryId) => store.Entries.TryGetValue(entryId, out var e) ? e : null;
  public Game GameFor(string entryId) { var id = store.Pairs.FirstOrDefault(p => p.Value == entryId).Key; return id != null && Guid.TryParse(id, out var g) ? PlayniteApi.Database.Games.Get(g) : null; }
  public string GameName(string gameId) => Guid.TryParse(gameId, out var g) ? PlayniteApi.Database.Games.Get(g)?.Name ?? gameId : gameId;
  public string StatusName(string statusId) => Guid.TryParse(statusId, out var s) ? PlayniteApi.Database.CompletionStatuses.Get(s)?.Name ?? statusId : statusId;
  string EntryName(string entryId) => store.Entries.TryGetValue(entryId, out var e) ? e.Game?.Title ?? entryId : entryId;
}
}
