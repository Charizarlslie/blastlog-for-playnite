# Blastlog in Playnite: design

Status: **draft for Charlie's approval** (Nov 7, 2026). Nothing in it is built yet beyond what "Today" says.

## 1. Goal

Playnite should look and work as close to Blastlog as is physically possible, natively, with no embedded browser.
Playnite stays in charge of what only it can do (install, launch, uninstall, playtime, stores); Blastlog's screens,
colours, type and data come into Playnite around that. Charlie, Nov 7: "as close to Blastlog as is physically possible,
without running it in a janky browser inside Playnite, because having it actually in Playnite is what lets you actually
install/launch/etc."

The authority for how things look is Blastlog's own design system (`design.md` in the Blastlog repo: tokens,
game colour, type, components, screens). This file only says how each part lands in Playnite and what Playnite can't do.

## 2. Today (0.2.3)

- Sync works (status and stars both ways, playtime one way, preview, undo).
- The theme is Playnite's default theme with Blastlog's dark colours, font sizes and corner radius, and a small
  Blastlog panel (status line, fingerprint bars, predicted stars) above each game's description.
- The "B" sidebar view is a plain list: Next up, Currently playing, Play next, with Play buttons.

It doesn't look like Blastlog: no cover heroes, no game colour, no masonry cards, no proper fingerprint card, and Outfit
only if the user installed it.

## 3. How it fits together

Two pieces, shipped together in each release:

1. **The plugin** draws Blastlog's own screens as native Playnite views (WPF, written in XAML from now on; CI already
   builds on Windows), and supplies game colour and Blastlog panels to the theme.
2. **The Blastlog theme** restyles Playnite's own screens (main window, sidebar, library, game details) so they read
   as Blastlog's Library and game page. Full rewrites of those views, not just recolours.

Shared between both: one set of design tokens (the dark and light values from design.md §2.1), the game-colour
builder, and Outfit, bundled (SIL Open Font License allows it), so nobody has to install a font.

### 3.1 Game colour

Every Blastlog page and card wears its game's cover colour (design.md §2.2–2.3). In Playnite:

- The plugin ports `palette.ts` (cover colour) and the game-token builder (`game`, `on-game`, `game-text`, tints, edge,
  glow) to C#, with the worked examples in design.md §2.2 as tests, so the same cover gives the same colours as the site.
- The colour set with "Fix this game" on Blastlog always wins (it comes with the game's data).
- The selected game's tokens are published as theme resources (`Blastlog_Game`, `Blastlog_OnGame`, `Blastlog_GameText`,
  `Blastlog_GameTint`, `Blastlog_Glow` …), so the theme's game page, buttons and sidebar selection change colour with
  the selected game, crossfading over 240ms as on the site (instant with Windows' "show animations" off).
- Unpaired or uncoloured games fall back to Blastlog green `#00e4a8`.

### 3.2 Covers

The site uses IGDB covers. To get the same colours and art, the plugin loads Blastlog's cover for each game from IGDB's
image server (what the site does in the browser) and caches it on disk. For games that are only in Playnite it uses
Playnite's own cover. **This adds a second host** (images.igdb.com) to "the plugin only contacts your Blastlog server";
the README and /privacy would say so. The alternative is Playnite's covers everywhere, which keeps one host but gives
slightly different art and colours from the site.

## 4. Screen by screen

Blastlog's menu is Dashboard, Library, Play next, Discover, Profile, Compare (design.md §6). In Playnite:

| Blastlog | In Playnite | Drawn by |
|---|---|---|
| Sidebar | Playnite's left sidebar, widened with labels: Blastlog mark (game colour), Dashboard, Library, Play next, Discover, Profile, Compare, then Playnite's own items (add-ons, settings) below a line. Selected item filled with `game` / `on-game`. | Theme + plugin sidebar items |
| Dashboard | A plugin view, and Playnite's start view if the user picks it | Plugin |
| Library | Playnite's own library (grid view), restyled as Blastlog's Library | Theme |
| Game page | Playnite's game details, restyled as Blastlog's game page, with Playnite's Install / Play buttons in it | Theme + plugin panels |
| Play next | A plugin view | Plugin |
| Discover | A plugin view | Plugin |
| Profile (Your taste) | A plugin view | Plugin |
| Compare | A plugin view | Plugin |
| Admin, imports, account settings | Stay on the site ("Open in Blastlog") | — |

### 4.1 Dashboard
Everything on design.md's Dashboard: Search and + New entry (Search finds games in your library and friends'
libraries; picking one opens its game page), Rate games I know / New from Steam prompts, the Next up hero (blurred cover
under a scrim, glow in the game's colour, full cover, Recommended / Quick one / Long haul, length, platform and store
chips, why it's up next, watch for, your own quote), Currently playing (with Finished it, Set aside with its two
choices, Still playing check-in, Guides ↗, Reddit ↗, the pager), Your library numbers and How your stars split,
Recently finished with its next steps.
**Playnite adds:** a game you have in Playnite shows **Play** (installed) or **Install** (not installed) as the primary
button on Next up and Currently playing rows. One that isn't in Playnite shows "Not in Playnite" and its stores.
**Built** (`Screens/Dashboard*.cs`): everything above runs natively, including Finished it / Set aside / Still playing /
Start another (mirror writes), the boost's Not now / Undo, and Friends lately with its reactions. Some actions open the
site because they are site flows the plugin has no screen for yet: + New entry and + Add a new game (the add dialog), the
library links (the numbers, the star legend, Enter in
Search) and friends' or catalogue games from Search (Discover game pages). A paired game's View details selects it in
Playnite's library. The earlier view's Sync now / Preview sync / Pair games / Refresh sit in a card at the bottom.

### 4.2 Library (Playnite's grid, restyled)
Blastlog's cover tiles (3:4, big, rounded 14–18, game-tinted card, status pill with icon and word, stars), title under
the cover, two lines then cut off. Filters use Playnite's own filter panel and the `Blastlog: …` tags, styled as
Blastlog's filter chips. Installed games get a small "Installed" mark; Playnite's sort and group stay.

### 4.3 Game page (Playnite's details, restyled)
The game page hero: blurred background art under a scrim, glow, cover, title, and the action row: **Play / Install**
(Playnite's), then Blastlog's status and stars, editable in place (they sync at once as a panel push, with Undo).
Below: the fingerprint card exactly as design.md §8 "Game page fingerprint" (caps label, watermark, glow, bars against a
typical game with names inside the fill, "+N more", tap a group for every trait, "Why it's here"), how much you'd like
it, length at your level, reasons and watch-outs, friends' stars and reviews, your review (edit in place, or Rate & review), where you own
it, series, More like this as a row of small covers, and history with Undo. Playnite's own extras (playtime,
achievements from other add-ons, description, links) sit underneath in the same style.

### 4.4 Play next
Picks first with #1's status buttons, the You're playing strip, Tends to love / dislike chips, length and ownership
filters, the masonry cards (rank, status, predicted stars, length, Instant yes / Never, because you like, your quote,
watch out, early fingerprint), Show more, Coming up. **Playnite adds** Play / Install on cards for games you have.

### 4.5 Discover
The desktop Discover cards as on the site, with Want to play, I own it, Played it (status and optional stars), Not for
me with Undo and Why not?. Games aren't in Playnite yet, so the action that matters most is Want to play, plus the
store links Blastlog already shows.

### 4.6 Profile and Compare
Profile (01–08) and Compare (01–07) as on the site, read mostly. Profile's corrections (Correct this, That's not me,
Fine-tune stances) and Compare's look-up work in place. Character sheets keep the owner's chosen look.

## 5. What Playnite can't do, and the answer

- **Live light/dark switching:** a Playnite theme is one set of colours until restart. Ship **Blastlog Dark** first
  (the site's dark board), then **Blastlog Light** as a second theme; the plugin's views follow whichever is active.
- **Hover cards:** WPF tooltips and popups do it, so they stay; no phone layouts (Playnite is desktop only, so design.md's
  phone and tablet rules don't apply; 200% Windows scaling still has to work).
- **The site's routing and back button:** Playnite's back/forward goes between views; links inside plugin views open
  the matching view, or the site for admin things.
- **Fullscreen mode** (the controller UI) is a separate kind of theme. Out of scope until desktop is done.
- Exact pixel match isn't possible (different text rendering, WPF layout); the aim is the same layout, colours, type
  scale, radii, spacing and behaviour, checked side by side with screenshots of the site.

## 6. What Blastlog's server needs

Mostly reads it already has, plus a few new routes, all behind the plugin's token:
- Read: Discover's whole page, Compare, Profile's full page, dashboard search (own + friends), friends' stars on a
  game. The existing "mirror" pattern makes each a one-line route.
- Write (needs "Let it change my library"): Discover answers (want, not for me, why not, undo), Profile corrections,
  review text, Set aside / Finished it, Not now. Each goes through the same mutation functions as the site, so History
  and Undo work as they do now.
- No migration expected.

## 7. Build order

Each phase ships as a release you can install; the theme and plugin always go out together.

1. **Foundation:** XAML views, shared tokens, bundled Outfit, the C# game-colour builder with the site's test vectors,
   cover loading and cache, Blastlog Dark theme's main window and sidebar.
2. **Game page and Library** (the screens you'll see most, and where Install/Play live).
3. **Dashboard** (start view).
4. **Play next and Discover**, with the server's new routes.
5. **Profile and Compare.**
6. **Blastlog Light.**

## 8. Checking it

There's no Playnite on the Blastlog server PC, and CI can't run Playnite. To compare against the site screen by screen
I'd run a **portable Playnite** (its own folder, nothing installed, no effect on anything else) on the server PC with a
handful of test games, take screenshots next to the site's, and fix what's off before each release reaches your gaming
PC. That needs the .NET 8 SDK in a user folder for local builds (also no system install). Your gaming PC stays the final
check.

## 9. Decisions for Charlie

1. **Covers from IGDB** (same art and colours as the site, adds images.igdb.com as a second host) or **Playnite's
   covers** (one host, slightly different look). Recommended: IGDB.
2. **Portable Playnite and a user-folder .NET SDK on the server PC** for screenshot checks. Recommended: yes.
3. **Order:** game page and Library first (recommended), or Dashboard first.
4. **Dark first, Light later** (recommended), or both from the start.
