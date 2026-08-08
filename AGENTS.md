# Gridlet agent guidance

This file is the shared source of repository instructions for coding agents.
Tool-neutral agent assets live in `.agents/`; do not commit tool-specific
copies under `.codex/` or `.claude/`.

## Local setup

The first non-design-time .NET build initializes the local tool aliases using
the correct script for the operating system. This also applies to contributors
who build the project without using a coding agent.

To initialize or verify the aliases manually, use the platform command below.

Windows:

```pwsh
pwsh -NoProfile -File ./scripts/Initialize-AgentTooling.ps1
```

macOS or Linux:

```sh
sh ./scripts/Initialize-AgentTooling.sh
```

The Windows script creates directory junctions from `.codex` and `.claude` to
`.agents`; the POSIX script creates symbolic links. Both are idempotent and
refuse to overwrite a real directory or a link to another location. CI builds
on Linux, so it continuously validates the automatic POSIX path; the Windows
script uses built-in Windows PowerShell when invoked by MSBuild.

Codex reads this file directly. Claude reads `CLAUDE.md`, which imports this
file, and discovers the same skills through its `.claude` alias.

## Development workflow

- Base feature and bug-fix branches on `dev` and open pull requests into `dev`.
- Keep `dev` releasable. Run `dotnet test --configuration Release` before
  requesting review.
- Only promote `dev` to `main` through a pull request.
- Treat every merge to `main` as a public release. Do not push to `main`, create
  release tags manually, or bypass the promotion policy.
- Preserve unrelated working-tree changes and never commit credentials, local
  runtime state, build output, or tool-specific agent directories.

## Repository skills

- Use `$orchestrate-improvements` for repository-wide parallel improvement
  sessions.
- Use `$release` to prepare and promote a Gridlet release.
