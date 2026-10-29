# Blastlog for Playnite

A [Playnite](https://playnite.link) extension that keeps your Playnite library and your [Blastlog](https://blastlog.games)
library in step, and brings Blastlog's Next up and Play next into Playnite.

- **Pairs** your Playnite games with your Blastlog entries (by store id, then by title, as Blastlog's own import does).
- **Syncs** play status and stars both ways, and playtime from Playnite to Blastlog. A preview comes first, and nothing
  is ever deleted on either side. Each sync can be undone on Blastlog.
- **Shows Blastlog in Playnite:** a Blastlog sidebar view (Next up, Currently playing, Play next), Blastlog tags for
  Playnite's filters, and theme elements (`Blastlog_GamePanel`, `Blastlog_Fingerprint`, `Blastlog_Status`).

Playnite sync is part of Blastlog's $10 supporter tier: the server only syncs for accounts with it. This extension is
free and open source (MIT).

## Install

1. Download the latest `.pext` from [Releases](../../releases) and double-click it. Playnite installs it and restarts.
2. On Blastlog: Your taste → Your account → Playnite sync → **Make a token** (tick "Let it change my library" for
   two-way sync). Copy it; it's shown once.
3. In Playnite: Add-ons → Extension settings → Blastlog → paste the token → **Test connection** → Save.
4. Main menu → **Blastlog → Pair games…** → Apply, then **Blastlog → Preview sync** → Sync these.

## Code signing

This project uses the [SignPath Foundation](https://signpath.org) for code signing: free code signing provided by
[SignPath.io](https://about.signpath.io), certificate by SignPath Foundation. The application is pending, so current
releases are **unsigned**, and Windows Smart App Control blocks unsigned DLLs; signed releases will say so in their
notes. Policy: [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

## Privacy and security

- The token is encrypted with Windows' per-user protection (DPAPI) and only ever sent in the `Authorization` header to
  the server in your settings. Nothing else is contacted: no telemetry, no analytics, no update checks.
- What it sends: your Playnite games' names, store ids, release years, platforms, completion status, user score and
  playtime. What it changes in Playnite: only completion status, user score, and tags whose names start with "Blastlog".
- Blastlog's privacy policy: https://blastlog.games/privacy

## Build

.NET SDK 8 (see `global.json`). `src/Blastlog.Sync.Core` (netstandard2.0) holds the client, mapping, merge and planner,
and `tests/` runs against `FakeBlastlog`, an in-memory server. `src/Blastlog.Playnite` (net462) is the extension; its UI
is built in code. CI (GitHub Actions) runs the tests on Linux, then builds and packs the `.pext` on Windows.

```sh
dotnet test tests/Blastlog.Sync.Core.Tests -c Release
dotnet build src/Blastlog.Playnite -c Release
```

## Licence and credits

MIT (see `LICENSE`). Uses the [Playnite SDK](https://github.com/JosefNemec/Playnite) (MIT) and
[Newtonsoft.Json](https://www.newtonsoft.com/json) (MIT). Code signing policy: [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).
