---
name: ship-issue-item
description: >
  Ship the next remaining item from a ranked Gridlet backlog issue (default #64)
  by implementing one slice, reviewing it through a different coding CLI
  (Codex, Claude Code, or Grok), fixing until host and reviewer agree, then
  opening a PR into dev, waiting for CI, and merging. Use when the user asks
  to work the next issue 64 item, drain the SQL coverage backlog, ship one
  backlog item, close the next coverage gap, or runs /ship-issue-item.
---

# Ship one issue item

Implement exactly one remaining backlog item, get an independent review from a
different coding CLI, and merge it to `dev`. Do not start a second item in the
same run unless the user asked to keep going after the first merge.

## Target

- Default issue: `BieleckiLtd/Gridlet#64`.
- Use another issue only when the user names it.
- One item per pull request. If the next item has ordered steps, ship only the
  next self-contained step.

## Inventory

1. Confirm a clean worktree on latest `origin/dev`. Preserve unrelated local
   changes; do not mix them into this branch.
2. Fetch the issue body and every comment with `gh`.
3. Collect numbered items from the issue body.
4. Mark an item done when a later comment reports it shipped, completed, or
   delivered.
5. Pick the lowest remaining number, or the item the user named if it is still
   open. Stop if nothing remains.

## Implement

1. Branch from `origin/dev` as `issue-<n>-item-<id>-<slug>`.
2. Implement the smallest complete slice, matching existing code and tests.
3. Run `dotnet test --configuration Release`. Stop on failure.
4. Record exact test counts for the review prompt.

## Independent review

The host agent must not review its own work through its own CLI. Follow
`$cross-cli-code-review` for invocation, identity checks, and triage.

1. If the user named a reviewer CLI, use that unless it is the host.
2. Otherwise pick the first installed CLI that is not the host, in this order:
   `codex`, `claude`, `grok`.
3. Stop if no other CLI is installed.
4. Discover that CLI's current default model from its own help, config, or
   catalog. Honor an explicit user model or effort. Default effort is `medium`.
5. Run `scripts/run_review.py` with `--scope working-tree`. Pass the issue URL,
   item number, intended behavior, and known test counts via `--context` and
   `--known-tests`.
6. Fix only validated claims, add focused tests, re-run the affected suite, and
   review again with the same provider, model, and effort.
7. Repeat until the reviewer reports no actionable findings and you
   independently agree. Do not tell the next reviewer what verdict to reach.

## Publish

1. Commit in the repository voice. Follow the authorship rules in `AGENTS.md`.
2. Open a pull request into `dev`.
3. Wait for GitHub Actions with `gh run watch <id> --exit-status`.
4. On green CI, merge with a merge commit and delete the branch.
5. Comment on the issue with what shipped, the PR number, and the validation
   result.
6. Stop, unless the user asked to keep going. Then fetch `origin/dev` and start
   the next remaining item.
