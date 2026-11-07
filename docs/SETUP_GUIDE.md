# Blastlog for Playnite: setup guide

About ten minutes. You need [Playnite](https://playnite.link) 10 on Windows and a Blastlog account.

## 1. Install the plugin (free)

1. Download `Blastlog_for_Playnite_<version>.pext`.
2. Double-click it (or drag it onto Playnite), then restart Playnite when it asks.
3. In Playnite: **Add-ons → Extension settings → Blastlog**.

## 2. Connect it to Blastlog

1. On Blastlog: **Your taste → Account settings → Playnite sync → Make a token**. Tick **Let it change my library** if
   you want Playnite's statuses and stars to go to Blastlog. Leave it unticked for a read-only connection.
2. Copy the token. Blastlog shows it once.
3. In Playnite's Blastlog settings, paste it into **Sync token** and press **Test connection**. It should say
   "Signed in as …". Then **Save**.

The token is stored encrypted for your Windows account and sent only to the server in the settings. To stop a PC
syncing, revoke its token on Blastlog. It stops working at once.

## 3. Pair your games, preview, sync

1. **Blastlog → Pair games…** (Playnite's main menu). Exact matches are ticked. Check the rest:
   - **Pair with your Blastlog entry**: the game is already in your Blastlog library.
   - **Add to Blastlog**: Blastlog knows the game, but you don't have it there yet.
   - **Send to Blastlog's import**: Blastlog couldn't tell which game it is. These go to Blastlog → Library → Import
     games for you to sort, and nothing is added until you finish that import.
2. Press **Apply**.
3. **Blastlog → Preview sync** shows what would change on each side. Press **Sync these** to do it.

After the first sync, the plugin syncs on its own: at start-up, after a library update, after you play, and every
30 minutes (Settings → "Sync on its own").

## What syncs

| | Playnite → Blastlog | Blastlog → Playnite |
|---|---|---|
| Status | Not Played / Plan to Play → not started, Played and On Hold → started, Playing, Beaten / Completed → done, Abandoned → quit | not started → Not Played, started → Played, playing → Playing, done → Beaten, quit / didn't finish → Abandoned |
| Stars | Your 0–100 user score as whole stars (85 → 4★) | Stars × 20, but only when the stars differ (your 85 stays 85 for 4★) |
| Playtime | Sent as a note on the game | Not sent back |

Reviews, notes, platforms, covers, favourites and tags stay where they are. A rating goes to Blastlog once the game
is finished there (Blastlog's own rule). If both sides changed the same game differently, **Blastlog wins** by default
(Settings: Blastlog wins / Playnite wins / Ask me).

**Nothing is ever deleted** by a sync. Every sync can be undone: on Blastlog (Account settings → Playnite sync → Undo), or
with **Blastlog → Undo the last sync** in Playnite.

## 4. Blastlog inside Playnite

- **The Blastlog sidebar item** shows Next up (Recommended / Quick one / Long haul, with Play), Currently playing (with
  "Still playing"), and Play next in Blastlog's order. It opens instantly from its last copy and works offline.
- **Tags**: `Blastlog: Backlog`, `Blastlog: Next up`, `Blastlog: Currently playing`, `Blastlog: Wishlist`, and one
  `Blastlog trait: …` per trait, so Playnite's filter panel works as your Blastlog library. To keep a view, filter by a
  tag and use Playnite's **Save filter preset**. **Blastlog → Remove Blastlog tags** takes them all off; your own tags
  are never touched.
- **A game's menu → Blastlog**: open its Blastlog page, or unpair it.

## 5. The Blastlog theme and settings pack

Both come with every release, next to the plugin:

1. `Blastlog_Desktop_Theme_<version>.pthm`: double-click it, then **Settings → Appearance → Theme → Blastlog**. It's
   Playnite's own default theme in Blastlog's colours, with Blastlog's status line and fingerprint panel on each game's
   details. For the intended look, install the free [Outfit font](https://fonts.google.com/specimen/Outfit) first
   (otherwise it uses Segoe UI).
2. `Blastlog settings pack.json`: in the plugin's settings press **Load a settings pack…** and pick it, then **Save**.
   It sets the sync choices this guide recommends (auto-sync on, Blastlog wins, tags on). It never touches your server
   or token.

## 6. Other plugins that work well with it (optional, not included)

These are other people's plugins. Install them from Playnite's add-on browser (**Add-ons → Browse**) if you want
them; Blastlog doesn't ship or support them.

- Playnite's built-in library integrations (Steam, GOG, Epic, Xbox, EA, Ubisoft and more): **Add-ons → Libraries**.
  The more stores are connected, the more games pair by store id.
- [Duplicate Hider](https://github.com/felixkmh/DuplicateHider): hides the same game owned on several stores, so
  pairing sees each game once.
- [Extra Metadata Loader](https://github.com/darklinkpower/PlayniteExtensionsCollection): logos and trailers for the
  game details.
- [Success Story](https://github.com/Lacro59/playnite-successstory-plugin): achievements in Playnite.

## If something's off

- "Blastlog didn't accept that token": it's wrong, revoked or expired, or sync is switched off on Blastlog. Make a new one.
- Preview lists a game under "Changed on both sides": change it on one side, then sync again.
- To start over: revoke the token on Blastlog, then in Playnite **Remove Blastlog tags** and delete the plugin's data
  folder (Playnite → Add-ons → Extension settings → Blastlog, or `%AppData%\Playnite\ExtensionsData\80b79ef3-134e-4e5e-92f2-6baeffbfc8a5`).
