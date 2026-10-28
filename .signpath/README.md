# Code signing setup (SignPath)

Signing follows [CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md). CI signs only when all of these hold; every other
build is unsigned, exactly as before:

- the push is a tag `vX.Y.Z` on a commit that is on `main`,
- `X.Y.Z` matches `Version` in `src/Blastlog.Playnite/extension.yaml` and in `Blastlog.Playnite.csproj`,
- the repository variable `SIGNPATH_PROJECT_SLUG` is set.

## One-time setup, after SignPath accepts the project

In SignPath:

1. Link the project to the GitHub.com trusted build system.
2. Use [artifact-configuration.xml](artifact-configuration.xml) as the project's default artifact configuration (it
   signs `Blastlog.Playnite.dll` only, and only with product name "Blastlog for Playnite" and the tag's version).
3. Make a signing policy that needs manual approval, with the maintainer as approver.
4. Create an API token with submitter rights for that project.

In this repository's **Settings → Secrets and variables → Actions**:

| Kind | Name | Value |
|---|---|---|
| Secret | `SIGNPATH_API_TOKEN` | the API token (never put it in a file, commit or chat) |
| Variable | `SIGNPATH_ORGANIZATION_ID` | the organization id SignPath gives you |
| Variable | `SIGNPATH_PROJECT_SLUG` | the project slug |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | the signing policy slug |

## Making a signed release

1. Bump `Version` in `extension.yaml` and the csproj, merge to `main`.
2. Push the tag `vX.Y.Z` from `main`.
3. Approve the signing request in SignPath within an hour (the build waits that long).
4. CI checks that every DLL in the package has a valid signature and that ours is SignPath Foundation's, then uploads
   the `blastlog-for-playnite-pext-signed` artifact. Attach that `.pext` to the GitHub release.
