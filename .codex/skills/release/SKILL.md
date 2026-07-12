---
name: release
description: Cut and publish a Gridlet release by reviewing release contents, bumping the shared version, testing, committing, tagging, pushing to GitHub, and monitoring the NuGet publication workflow. Use for requests such as release, publish, cut a version, bump and publish, or ship Gridlet.
---

# Gridlet release

Publish Gridlet through GitHub Actions. Pushing a `v*` tag triggers `.github/workflows/publish.yml`, which tests, packs, authenticates to nuget.org with Trusted Publishing, and publishes all four packages.

## Repository facts

- Read and change the single `<Version>` in `Directory.Build.props`; never version individual projects.
- Publish `Gridlet.Core`, `Gridlet.SqlServer`, `Gridlet.Sqlite`, and `Gridlet.AspNetCore`.
- Use `origin` (`BieleckiLtd/Gridlet`) and release from `main`.
- Treat `patch` as the default bump; treat a feature release as `minor`.
- Do not look for or use a local NuGet API key. CI uses OIDC Trusted Publishing.

## Workflow

1. Confirm the branch is `main`, inspect `git status --short`, review the full diff, and read the current version.
2. If the tree contains work intended for the release, include it in the release commit. Preserve unrelated user changes. The tree must be clean before tagging.
3. Run `dotnet test --configuration Release`. Stop if any test fails.
4. Compute the semantic version bump and edit only `<Version>` in `Directory.Build.props`.
5. Re-run release tests when the version or release contents changed after the first run.
6. Stage the intended release contents, inspect the staged diff, and commit with a concise message that describes the shipped work. Use `Release Gridlet <version>` only for a version-only release commit.
7. Confirm the tree is clean and create annotated release tag `v<version>`.
8. Before pushing the tag, state that it irreversibly publishes the four public NuGet packages and obtain explicit confirmation unless the user already explicitly requested both push and publish in the current turn.
9. Push `main`, then push `v<version>`.
10. Find and watch the `Publish` workflow with `gh run list --workflow=publish.yml --limit 1` and `gh run watch <run-id> --exit-status`.
11. On failure, inspect and report the failing logs. Never delete or move a published tag blindly.
12. On success, report the commit, version, tag, workflow URL, and package URLs:
    - `https://www.nuget.org/packages/Gridlet.Core/<version>`
    - `https://www.nuget.org/packages/Gridlet.SqlServer/<version>`
    - `https://www.nuget.org/packages/Gridlet.Sqlite/<version>`
    - `https://www.nuget.org/packages/Gridlet.AspNetCore/<version>`

## Guardrails

- Never tag a dirty tree or a commit that does not contain everything intended to ship.
- Never publish from a non-`main` branch or after failed tests.
- Check that the tag and NuGet version do not already exist before creating the tag.
- NuGet publication is irreversible; an existing version cannot be overwritten.
- A transient workflow failure may be rerun because publishing uses `--skip-duplicate`, but inspect the failure first.
