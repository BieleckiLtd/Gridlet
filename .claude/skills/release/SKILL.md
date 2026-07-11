---
name: release
description: Cut a Gridlet release — bump the version, commit, tag, and push to GitHub. Pushing the tag triggers the Publish workflow, which packs and publishes the three NuGet packages to nuget.org via Trusted Publishing. Trigger on "release", "publish", "cut a version", "bump and publish", "ship it".
---

# Gridlet release

End-to-end release of the Gridlet packages. One version number drives all three packages; it lives in a single place. **Publishing to nuget.org happens in CI, not locally** — this skill's job is to get a correct version tag onto GitHub. Pushing a `v*` tag fires `.github/workflows/publish.yml`, which packs and pushes to nuget.org using Trusted Publishing (OIDC — no API key).

## Facts about this repo

- **Version source of truth**: `<Version>` in `Directory.Build.props`. Nothing else holds a version — do not edit csproj files.
- **Packable projects** (produce `.nupkg`): `src/Gridlet.Core`, `src/Gridlet.SqlServer`, `src/Gridlet.AspNetCore`. Everything else has `IsPackable=false`.
- **Publish mechanism**: `.github/workflows/publish.yml`, triggered by pushing a tag matching `v*`. It runs `dotnet test`, `dotnet pack`, then `NuGet/login@v1` (Trusted Publishing) + `dotnet nuget push`. The nuget.org Trusted Publishing policy is bound to owner `BieleckiLtd`, repo `BieleckiLtd/Gridlet`, workflow `publish.yml`. **No API key or `NUGET_API_KEY` is used anywhere.**
- **Remote**: `origin` → `https://github.com/BieleckiLtd/Gridlet.git`, default branch `main`.
- **SDK**: .NET 10 (`dotnet --version` ≈ 10.0.x).

## Inputs

- Version bump: an explicit version like `1.2.0`, or one of `patch` / `minor` / `major` (default: `patch`). A "feature" release is a `minor` bump.

## Steps

### 1. Preflight
- Confirm the branch is `main` and the working tree is clean: `git status --porcelain` and `git rev-parse --abbrev-ref HEAD`. If there are uncommitted changes or you're not on `main`, stop and report — the packages CI builds come from the tagged commit, so anything uncommitted would ship in the packages but be absent from the tag. Get the tree clean first (commit or stash) before continuing.
- Read the current `<Version>` from `Directory.Build.props`.

### 2. Build & test locally (fast feedback; CI runs them again)
```
dotnet test --configuration Release
```
If tests fail, stop and report. Do not release. (CI will also fail the publish job if tests fail, but catch it here first.)

### 3. Bump the version
- Compute the new version from the input (semantic bump of the current value, or the explicit version given).
- Edit **only** the `<Version>` element in `Directory.Build.props`.
- Show the user the old → new version.

### 4. Commit, tag, push — **confirm before pushing**
```
git add Directory.Build.props
git commit -m "Release Gridlet <new-version>"
git tag v<new-version>
```
Pushing is the point of no return: the tag push triggers the public nuget.org publish. State clearly that pushing `v<new-version>` will publish the three packages to nuget.org, and get an explicit yes. Then:
```
git push origin main
git push origin v<new-version>
```

### 5. Watch the publish and report
- The `Publish` workflow starts on the tag push. Follow it with `gh run watch` (or link the user to the Actions tab): `gh run list --workflow=publish.yml --limit 1` then `gh run watch <run-id>`.
- On success, report the version, the tag, and links to `https://www.nuget.org/packages/Gridlet.Core/<version>` (and SqlServer, AspNetCore). Note nuget.org indexing can lag a few minutes before the version is listed.
- If the run fails, surface the failing step's log — do not re-tag blindly.

## Guardrails
- Never push a release tag from a dirty tree or a non-`main` branch — CI packs the tagged commit, so the tag must fully represent what ships.
- Never publish if local tests failed at step 2.
- The publish is irreversible: a nuget.org version cannot be re-uploaded once listed. The confirmation at step 4 is the gate — do not skip it.
- Publishing requires no secrets on this machine. If someone asks where the API key is, the answer is there isn't one — it's Trusted Publishing via OIDC in `publish.yml`.
- If a tag already exists on nuget.org and you need to re-run (e.g. transient CI failure), re-running the workflow is safe: `dotnet nuget push --skip-duplicate` skips already-published packages.
