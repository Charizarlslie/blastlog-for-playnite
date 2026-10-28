# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org) (once the project is accepted; until then releases are unsigned).

- **What is signed:** `Blastlog.Playnite.dll`, the only binary this project produces, inside each release's `.pext`.
  Third-party files in the package (Newtonsoft.Json, .NET facades) keep their own publishers' signatures.
- **How:** only builds made by this repository's GitHub Actions workflow from a tagged commit on `main` are signed.
  Nothing built on a developer's machine is ever signed.
- **Who:** committers and reviewers are listed below. Every signing request is approved by an approver, and all team
  members use multi-factor authentication on GitHub and SignPath.
  - Committers and approvers: [Charizarlslie](https://github.com/Charizarlslie)
- **Changes from anyone else** are merged only after review by a committer.
- **Privacy:** the extension contacts only the Blastlog server the user sets in its settings, and sends only the data
  listed in the README's "Privacy and security". No telemetry, analytics or update checks. Uninstall from Playnite's
  Add-ons window; it changes nothing outside Playnite's own database (status, score and "Blastlog…" tags) and its own
  data folder.
