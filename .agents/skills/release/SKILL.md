---
name: release
description: "Cut and publish a requested Gridlet package release through a version bump, dev-to-main PR, and NuGet workflow."
---

# Gridlet release

Publish Gridlet through GitHub Actions. Merging the protected `dev` branch into `main` triggers `.github/workflows/publish.yml`, which tests, packs, authenticates to nuget.org with Trusted Publishing, publishes all seven packages, and creates the matching GitHub release and tag.

## Repository facts

- Read and change the single `<Version>` in `Directory.Build.props`; never version individual projects.
- Publish stable `Gridlet.Core`, `Gridlet.SqlServer`, `Gridlet.Sqlite`, `Gridlet.AspNetCore`, and `Gridlet.Voice` packages plus `Gridlet.AgentFramework` and `Gridlet.Components` at `<Version>-preview.1`.
- Use `origin` (`BieleckiLtd/Gridlet`), prepare releases on `dev`, and promote them to `main`.
- Treat `patch` as the default bump; treat a feature release as `minor`.
- Do not look for or use a local NuGet API key. CI uses OIDC Trusted Publishing.

## Workflow

1. Confirm the branch is `dev`, inspect `git status --short`, review the full diff, and read the current version.
2. If the tree contains work intended for the release, include it in the release commit. Preserve unrelated user changes. The tree must be clean before promotion.
3. Review release contents and resolve the intended version before final validation.
4. Compute the semantic version bump and edit only `<Version>` in `Directory.Build.props`.
5. Run `dotnet test --configuration Release` on the final version and contents.
   Diagnose failures and repair release-related defects; failing tests block
   promotion, not repair. Rerun after relevant changes, not after unchanged checks.
6. Stage the intended release contents, inspect the staged diff, and commit with a concise message that describes the shipped work. Use `Prepare Gridlet <version>` only for a version-only release commit. Follow the authorship rule in `AGENTS.md`: never attribute an AI agent as author, co-author, or contributor anywhere in the commit, pull request, or release notes.
7. Push `dev` and open a `dev` to `main` pull request. Never push release changes or tags directly to `main`.
8. Before merging, verify that the user authorized public package publication
   in this session. An explicit request to release or publish Gridlet includes
   the required promotion merge; a version-bump-only request does not. When
   authorization is missing, present the prepared PR and explain that merging
   publishes public NuGet packages before asking for confirmation.
9. Wait for the required CI and promotion-policy checks, then merge using a merge commit so `main` remains a descendant of `dev`.
10. Find and watch the `Publish` workflow with `gh run list --workflow=publish.yml --limit 1` and `gh run watch <run-id> --exit-status`.
11. On failure, inspect and report the failing logs. Never delete or move a published tag blindly.
12. On success, report the commit, version, tag, workflow URL, and seven package URLs:
    - `https://www.nuget.org/packages/Gridlet.Core/<version>`
    - `https://www.nuget.org/packages/Gridlet.SqlServer/<version>`
    - `https://www.nuget.org/packages/Gridlet.Sqlite/<version>`
    - `https://www.nuget.org/packages/Gridlet.AspNetCore/<version>`
    - `https://www.nuget.org/packages/Gridlet.Voice/<version>`
    - `https://www.nuget.org/packages/Gridlet.AgentFramework/<version>-preview.1`
    - `https://www.nuget.org/packages/Gridlet.Components/<version>-preview.1`

## Guardrails

- Never manually tag or push directly to `main`; the publish workflow creates the tag after package publication succeeds.
- Never merge a release after failed tests.
- Check that the intended tag and NuGet version do not already exist before promotion; the workflow owns tag creation.
- NuGet publication is irreversible; an existing version cannot be overwritten.
- A transient workflow failure may be rerun because publishing uses `--skip-duplicate`, but inspect the failure first.
