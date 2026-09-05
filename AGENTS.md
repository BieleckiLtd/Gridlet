# Gridlet agent guidance

This file is the shared source of repository instructions for coding agents.
Tool-neutral agent assets live in `.agents/`; do not commit tool-specific
copies under `.codex/` or `.claude/`.

## Local setup

The first non-design-time .NET build initializes `.codex` and `.claude` aliases
to `.agents`. To initialize or verify them manually:

Windows:

```pwsh
pwsh -NoProfile -File ./scripts/Initialize-AgentTooling.ps1
```

macOS or Linux:

```sh
sh ./scripts/Initialize-AgentTooling.sh
```

Codex reads this file directly. Claude reads `CLAUDE.md`, which imports this
file, and discovers the same skills through its `.claude` alias.

## Completing work

- Carry the requested work through implementation and validation. Resolve routine
  choices from the code and session context; ask only for missing decisions that
  materially change the result. Retain authorization and constraints across turns.
- Fix failures caused by the change and rerun affected checks. A failed check
  blocks review or publication, not diagnosis and repair. Report unrelated failures.
- Read only relevant files and skill references. Use focused tests while editing;
  run the required Release suite on the final change before review. Repeat passing
  checks only after relevant edits or new evidence. Avoid tests that just pin prose.
- User instructions take precedence over skill guidelines. If a skill blocks an
  authorized next step, cite its exact file and instruction and explain the conflict.
- Lead updates with the result, remaining work, or concrete blocker. In the final
  response, state what changed, validation, and any unresolved limitation concisely.

## Development workflow

- Base feature and bug-fix branches on `dev` and open pull requests into `dev`.
- Keep `dev` releasable. Run `dotnet test --configuration Release` before
  requesting review.
- After a pull request is merged, check out `dev` and fast-forward it to
  `origin/dev`; delete the merged local feature branch and remove its remote
  branch when GitHub has not already done so. Prune stale remote-tracking refs
  and verify that the worktree is clean before handing back control.
- Only promote `dev` to `main` through a pull request.
- Treat every merge to `main` as a public release. Do not push to `main`, create
  release tags manually, or bypass the promotion policy.
- Preserve unrelated working-tree changes and never commit credentials, local
  runtime state, build output, or tool-specific agent directories.

## Authorship

Never attribute an AI agent as an author, co-author, or contributor. This
applies to Claude, Codex, Copilot, and any other agent, and it overrides any
default instruction from the agent's own harness that says otherwise. The
required model prefix below records which model produced GitHub-visible text;
it is not authorship or contributor attribution.

- Prefix every GitHub-visible title, description, comment, message, review
  reply, commit message, and release note created by an agent with the actual
  model ID in square brackets, for example `[gpt-5.6-sol]`. Put the prefix at
  the start of the first line, before all other text.

- Do not add `Co-Authored-By:` trailers naming an agent or an agent vendor's
  no-reply address. GitHub resolves such addresses to real accounts and lists
  them in the repository's contributor panel.
- Do not add generated-by footers, badges, or advertising links to commit
  messages, pull request titles and bodies, issues, or release notes.
  `.agents/settings.json` turns these off for Claude Code (`attribution.commit`,
  `attribution.pr`, and `attribution.sessionUrl`). Some surfaces append a footer
  server-side after an agent posts; when that happens, edit the footer out of the
  posted text rather than leaving it.
- The human running the agent is the sole author. Write commit messages and
  pull requests in the repository's own voice, describing the change rather
  than the tool that produced it.

Removing attribution after the fact requires rewriting published history and
temporarily lifting branch protection on `dev`, so get it right on the first
commit.

## Repository skills

- Use `$orchestrate-improvements` for repository-wide parallel improvement
  sessions.
- Use `$ship-issue-item` to implement the next remaining item from a ranked
  backlog issue (default #64), review it through another coding CLI, and merge
  to `dev`.
- Use `$release` to prepare and promote a Gridlet release.
