# Building Blastlog screens in Playnite

How to add or restyle a screen. The look is Blastlog's design system (`design.md` in the Blastlog repo); the plan is
[PLAYNITE_DESIGN.md](PLAYNITE_DESIGN.md). Keep every live control the site has (design.md 1: refine, never drop).

## Two kinds of screen

- **Plugin screens** (Dashboard, Blastlog library, Play next, Discover, Your taste, Compare): a class implementing
  `Ui.IBlastlogScreen` in its own file under `src/Blastlog.Playnite/Screens/`. The plugin finds it by reflection and adds
  it to Playnite's sidebar in `Order` (Dashboard 10, Blastlog library 20, Play next 30, Discover 40, Your taste 50, Compare 60). No shared list
  to edit. `Create` returns the content; `Kit.Page` wraps it in the page background, padding, scrolling and Kit styles.
- **Two themes:** Blastlog (dark) and Blastlog Light share `theme/overrides/`; Light only swaps `Constants.xaml`
  (`theme/overrides-light/`), and the plugin builds game colours for whichever is active (`ThemeBridge.CurrentMode`).
  Use tokens only and both work; check both (Settings, Appearance, Theme, or edit `Theme` in the portable copy's
  `config.json` to `Blastlog_Light_Desktop_Theme`).
- **Theme screens** (Library, game details, the main window, controls): whole files under `theme/overrides/`, each a
  copy of the same file from Playnite's default desktop theme (MIT, pinned at 10.63 in `tools/build-theme.ps1`) changed
  to Blastlog's look. The build copies the default theme, then these files over it; an override whose original no longer
  exists fails the build. Keep every `PART_*` element Playnite's file has: Playnite wires behaviour to them.
  Playnite loads each theme file on its own to check it before applying the theme, so a `StaticResource` (or `BasedOn`)
  to a key that only another override file defines makes Playnite fall back to its default theme: use `DynamicResource`
  across files, or define the style in the file that needs it. Shared theme pieces (`Blastlog.FocusRing`,
  `Blastlog.PillFocusRing`, `Blastlog.IconButton`) live in `DefaultControls/Border.xaml`.
- **Library tiles** (`DerivedStyles/GridViewItemTemplate.xaml`): each tile has a `Blastlog_Tile` element that
  `Ui/LibraryTint.cs` fills with the status pill and stars and uses to give the tile its own game colours (worked out
  off the UI thread, remembered per game). The cover box is 3:4 at Playnite's tile width; covers fill and crop.
- **The game page** is both: `theme/overrides/Views/DetailsViewGameOverview.xaml` and `GridViewGameOverview.xaml` lay out
  the hero and place the plugin's elements (`src/Blastlog.Playnite/Screens/GamePage.cs`) by name:
  `<ContentControl x:Name="Blastlog_GameActions" />` (status and stars beside Play / Install) and
  `<ContentControl x:Name="Blastlog_GamePage" />` (fingerprint, For you, your take, friends, where you own it, More like
  this, history). `Blastlog_GamePanel`, `Blastlog_Fingerprint` and `Blastlog_Status` stay for other themes. Status, stars
  and review edits are one panel push each (`plugin.PanelChange`), undoable as a run (`plugin.UndoPanel`).

## The kit (`src/Blastlog.Playnite/Ui/`)

- **Tokens** are resources, always `DynamicResource` / `SetResourceReference`, never raw colours:
  `Blastlog.Bg`, `.Side`, `.Surface`, `.Surface2`, `.Line`, `.Edge`, `.Text`, `.TextMuted`, `.Star`, `.Ok`, `.Warn`,
  `.Danger`, `.Brand`, `.Focus`, the font `Blastlog.Font` (bundled Outfit), and the game colours `Blastlog.Game`,
  `.OnGame`, `.GameText`, `.GameTint`, `.GameTint2`, `.GameEdge`, `.GameBar`, plus `Blastlog.GlowColor` (a Color).
  The Blastlog theme defines them; `ThemeBridge` fills them in under other themes.
- **Game colour:** the page wears one game (`plugin.Theme.WearPage(seed)`, e.g. the dashboard wears Next up's game;
  the selected Playnite game is automatic). A card wears its own game with `Kit.GameScope(card, plugin, blastlogGame)`.
  Seeds come from `plugin.Theme.SeedFor(game)`: the page colour set on Blastlog, else the cover's colour, computed
  exactly as the site does (`Blastlog.Sync.Core.Design`, tested against the site's own code).
- **Styles** in `Kit.xaml`: text `Blastlog.Body`, `.Muted`, `.Small`, `.CardTitle`, `.Section`, `.PageTitle`,
  `.HeroTitle`, `.Label` (caps, FINGERPRINT only), `.GameWords`; borders `Blastlog.Card`, `.GameCard`, `.Tile`, `.Chip`,
  `.NeutralChip`; buttons `Blastlog.Primary`, `.Ghost`, `.TextButton`, `.DangerButton`. Keyboard focus shows Blastlog's ring.
- **Helpers** in `Kit.cs`: `Kit.Text`, `Kit.Button` (busy while its task runs), `Kit.Chip`, `Kit.Card`, `Kit.Stars`,
  `Kit.StatusPill` (icon + word, fixed colours), `Kit.Cover(plugin, game, width, context)` (IGDB art, cached, with the
  gamepad tile when there's none), `Kit.GameScope`. `Icons.Make(svg)` draws the site's icons: paste the inner SVG
  elements from `apps/web/src/Icons.tsx`.
- **Fingerprint:** `Ui.Fingerprint.Card(FingerprintData.Parse(json["fingerprint"]), options)` is the game page card;
  `Ui.Fingerprint.Bars(data)` is an early fingerprint for a card. The maths (bars against a typical game, trait order,
  What defines it) are `Blastlog.Sync.Core.Design.FingerprintBars`, tested against the site (`FingerprintTests`).
  `Ui.Tint.OnGame(element, (game, line, surface) => ...)` does the site's `color-mix()` game tints.
- **Data:** `plugin.Mirror("<path>", fresh)` reads `GET /sync/v1/<path>` with a disk cache (show the cached copy, then
  refresh). `plugin.Write("<path>", body)` posts (or `plugin.Write(path, body, method)` for PATCH and DELETE; net462 has no `HttpMethod.Patch`, so use `new HttpMethod("PATCH")`) and clears the cache, telling the player in words when it fails. Routes
  and response shapes: `docs/PLAYNITE_SYNC.md` in the Blastlog repo ("Reads for the native screens", "Mirror writes").
  Paired games: `plugin.EntryFor(playniteGameId)`, `plugin.GameFor(entryId)`; Play/Install go through
  `plugin.Api.StartGame(id)` / `plugin.Api.InstallGame(id)`.
- **Links to other pages** go through `Ui.Nav` (`Nav.Review`, `Nav.AddGame`, `Nav.Library(query)`, `Nav.DiscoverGame`, `Nav.Entry`,
  `Nav.FriendEntry`, `Nav.FriendPage`, `Nav.Media`, `Nav.Trait`, `Nav.Collection`, `Nav.QuickStart`, `Nav.Together`, `Nav.Friends`,
  `Nav.Taste`, `Nav.PlayNext`, `Nav.Sharpen`, `Nav.Settings`, `Nav.Year`, `Nav.Feedback`, `Nav.PlayniteLibrary`, and `Nav.Site(path)` for pages that stay on the site).
  Each opens the site until its native version lands; a native dialog replaces that one method's body, so screens
  never hard-code a site URL for something Playnite can show.
- **A friend's pages** (`Nav.FriendPage(plugin, friendId, "dashboard" | "library" | "profile")`, Screens/FriendPages.cs)
  are native: one window per friend with their Dashboard, Library and Taste as tabs, read only, as the site's
  /friends/:id/... pages. Library and Taste are the own screens with a viewed friend (`LibraryView(..., friend)`,
  `TasteView(plugin, friend)`: own-only controls hidden, wording about them); the Dashboard is its own variant
  (Screens/FriendDashboard.cs: Next up for them, Currently playing, their library's numbers, Recently finished; no
  prompts, + New entry or friend activity). Reads go through `FriendView.Path` (`users/:id/...`, Blastlog
  docs/PLAYNITE_SYNC.md "Round 4"); their library is the sync library's cursor pages (`FriendData`). Games open as their
  entry (`Nav.FriendEntry`). Friends (Nav.Friends) links each friend's three pages and adds a friend from a pasted link
  (`GET friends/code/:code`, `POST friends`); the site has no friend requests.
- **A friend's entry** (`Nav.FriendEntry`, Screens/FriendEntry.cs) is native: the site's game page for someone else's
  entry (hero, fingerprint, For you, their take with taps, review and your reactions, game details, your other friends,
  accessibility, trailers and screenshots, More like this), read only apart from reactions. Friend activity on the
  Dashboard opens it.
- **Trailers and screenshots** (`Screens/GameMore.cs`, shared by every game page window) open in the media viewer
  (`Nav.Media`, Ui/MediaViewer.cs): the site's MediaViewer, stepping through the trailers then every screenshot with the
  arrows, Home and End (order and labels are Core `MediaSequence`, tested against the site). Trailers play on YouTube in
  the browser (Playnite has no player).
- **The character sheet's look** is native: a "Look" select on your own sheet on Compare saves with
  `PUT me/sheet-style` and reloads Compare; `Nav.SheetLook` opens Compare scrolled to it.
- **The sidebar** matches the site's (App.tsx Brand, Navigation, FriendsNav, SidebarCompletion, FeedbackButton,
  AccountMenu, SiteCredit). `theme/overrides/Views/Sidebar.xaml` lays it out with empty hosts by name
  (`Blastlog_SideName`, `Blastlog_SideFriends`, `Blastlog_SideCompletion`, `Blastlog_SideFooter`) and the "My Blastlog"
  label; `Ui/SidebarChrome.cs` finds them in the main window and fills them from the `me`, `friends` and `dashboard`
  reads (friend links go through `Nav.FriendPage`, "Add or manage friends" `Nav.Friends`, Send feedback `Nav.Feedback`
  → Screens/FeedbackDialog.cs posting `feedback`, Settings `Nav.Settings`). Playnite's own items (Library, Statistics,
  other add-ons' views) sit after Blastlog's (Ui/SidebarOrder.cs) in one collapsible "Playnite" group under a divider,
  closed by default and remembered in the plugin's `sidebar.json`; a selected Playnite item stays visible while it's
  closed. The plugin setting "Hide Playnite's own menu items" hides the group's buttons only (the items stay in
  Playnite's list, so `Nav.Entry` still switches to the Library view) and adds "Playnite library" to the footer.
  Playnite redraws every sidebar button when its item list changes, so the group is re-applied by a 1-second check.
- **Settings** (`Nav.Settings`, Screens/SettingsWindow.cs) is native: the site's Settings menu and Account settings in
  one window. Your picture (Screens/SettingsPicture.cs: pick a file, middle square at 512px, JPEG under 400 KB,
  `PUT`/`DELETE me/picture`), appearance (`PUT me/appearance`, the site's look; Playnite's look is its theme), game
  lengths (`PUT taste/length-level`), subscriptions, comfort picks, sale alerts, notifications (`PUT` on `subscriptions`,
  `replay-picks`, `sale-alerts`, `notifications`), the token this PC uses (`GET me`), your extras, your data and signing
  Playnite out (`plugin.ForgetToken()`). Every card is the site's own read (`GET settings`, `notifications`, ...); a
  card whose read fails is left out, as the site hides switched-off cards. Saves use `plugin.TryWrite` (PluginAccount.cs),
  which answers in words on the card instead of a dialog. Making and revoking tokens (a token can't list tokens: 401),
  downloading your data, deleting the account, linked sign-ins, taste history and guided start link to the site.
  Your taste's "Change picture" and "Account settings" and the sidebar's Settings open it.
- **Year in games** (`Nav.Year(year)`, Screens/YearPage.cs) is native and read only: `GET year/:year` (the site's
  YearInGames: the card with Save the card as a PNG, Month by month, top five, how your taste moved, highlights). Settings'
  Your extras links it.
- **Still on the site** because the mirror has no route for them yet: reporting a review.
- **Rate & review** (Nav.Review, Screens/ReviewDialog.cs) is native: the site's pop-up (ownership, status, completion
  date, stars, taps, review, the questions). It saves with PATCH entries/:id (expectedVersion; a ersion_conflict loads
  the latest and keeps the player's edits), then PUT entries/:id/picks; the taps hide if the picks can't be read. The
  starter line and taps are Blastlog.Sync.Core.Design.ReviewWords, tested against the site's cases.
- Text never under 14px, targets 44px, sentence case, no AI mentions, no admin data (design.md 7, 10, 12).

## Checking it locally (Windows)

```powershell
# once per checkout: a portable Playnite 10.63 with 15 test games (-From copies an already set-up one, skipping
# Playnite's first-run wizard, which a brand-new library shows once)
powershell -File tools/dev/playnite.ps1 setup -Dir <folder> [-From <set-up folder>]
# each change: build plugin + theme, install, restart Playnite (waits if another checkout is using Playnite)
powershell -File tools/dev/playnite.ps1 deploy -Dir <folder>
powershell -File tools/dev/shot.ps1 -Out <png>          # window at 0,0, 1600x1000 physical pixels
powershell -File tools/dev/click.ps1 -X <x> -Y <y>       # click at screenshot coordinates
powershell -File tools/dev/playnite.ps1 stop             # always, so the next checkout can use Playnite
```

Only one Playnite runs per Windows user, so `deploy` holds a lock until `stop` (15 minutes counts as abandoned). Set
`BLASTLOG_DOTNET` to a .NET 8 SDK folder if `dotnet` isn't on PATH. The test games aren't paired with Blastlog; to see Blastlog
data, paste a token for the demo (`https://demo.blastlog.games`, Try the demo, Account settings, Playnite sync) in the
plugin's settings, never a real account's. Errors land in `<dir>\playnite.log`; a plugin exception that escapes takes
Playnite down, so catch and log in every event handler and async void.

The final check is a real Playnite with a real library, through a release.
