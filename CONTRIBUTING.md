# Contributing to Gridlet

Gridlet uses a two-branch release model:

- Open feature and bug-fix pull requests against `dev`.
- `dev` is the protected integration branch and must remain releasable.
- Only a pull request from `dev` may update `main`.
- Every merge to `main` is a public release, so a promotion must increment the
  stable version in `Directory.Build.props`.

## Development

The repository requires the .NET 10 SDK. Before opening a pull request, run:

```pwsh
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
```

Repository agent assets have one canonical home in `.agents`. If you use Codex
or Claude locally, initialize the tool aliases after cloning:

```pwsh
# Windows (creates directory junctions)
pwsh -NoProfile -File ./scripts/Initialize-AgentTooling.ps1
```

```sh
# macOS/Linux (creates symbolic links)
sh ./scripts/Initialize-AgentTooling.sh
```

This setup is optional for developers who do not use coding agents. CI runs
the Linux bootstrap on every build to keep the cross-platform path tested.

Browser tests also require Chromium. CI installs it with:

```pwsh
pwsh tests/Gridlet.BrowserTests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
```

Keep changes focused, add tests for changed behavior, and document public API
changes. Pull requests must pass CI and resolve all review conversations.

## Releases

Maintainers prepare a release on `dev` by changing the shared `<Version>` in
`Directory.Build.props`, then open a `dev` to `main` pull request. After it is
merged, GitHub Actions tests and packs all five packages, publishes them to
NuGet using Trusted Publishing, and creates the corresponding `vX.Y.Z` GitHub
release. Do not create release tags manually.
