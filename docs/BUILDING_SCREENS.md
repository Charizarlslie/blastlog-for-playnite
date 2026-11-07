# Building Blastlog screens in Playnite

How to add or restyle a screen. The look is Blastlog's design system (`design.md` in the Blastlog repo); the plan is
[PLAYNITE_DESIGN.md](PLAYNITE_DESIGN.md). Keep every live control the site has (design.md 1: refine, never drop).

## Two kinds of screen

- **Plugin screens** (Dashboard, Play next, Discover, Your taste, Compare): a class implementing
  `Ui.IBlastlogScreen` in its own file under `src/Blastlog.Playnite/Screens/`. The plugin finds it by reflection and adds
  it to Playnite's sidebar in `Order` (Dashboard 10, Play next 30, Discover 40, Your taste 50, Compare 60). No shared list
  to edit. `Create` returns the content; `Kit.Page` wraps it in the page background, padding, scrolling and Kit styles.
- **Two themes:** Blastlog (dark) and Blastlog Light share `theme/overrides/`; Light only swaps `Constants.xaml`
  (`theme/overrides-light/`), and the plugin builds game colours for whichever is active (`ThemeBridge.CurrentMode`).
  Use tokens only and both work; check both (Settings, Appearance, Theme, or edit `Theme` in the portable copy's
  `config.json` to `Blastlog_Light_Desktop_Theme`).
- **Theme screens** (Library, game details, the main window, controls): whole files under `theme/overrides/`, each a
  copy of the same file from Playnite's default desktop theme (MIT, pinned at 10.63 in `tools/build-theme.ps1`) changed
  to Blastlog's look. The build copies the default theme, then these files over it; an override whose original no longer
  exists fails the build. Keep every `PART_*` element Playnite's file has: Playnite wires behaviour to them.

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
- **Data:** `plugin.Mirror("<path>", fresh)` reads `GET /sync/v1/<path>` with a disk cache (show the cached copy, then
  refresh). `plugin.Write("<path>", body)` posts and clears the cache, telling the player in words when it fails. Routes
  and response shapes: `docs/PLAYNITE_SYNC.md` in the Blastlog repo ("Reads for the native screens", "Mirror writes").
  Paired games: `plugin.EntryFor(playniteGameId)`, `plugin.GameFor(entryId)`; Play/Install go through
  `plugin.Api.StartGame(id)` / `plugin.Api.InstallGame(id)`.
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
