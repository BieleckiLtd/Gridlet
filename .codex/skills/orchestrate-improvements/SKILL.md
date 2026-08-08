---
name: orchestrate-improvements
description: "Orchestrate a repository-wide improvement session with parallel subagents: establish a test baseline, audit disjoint areas, reject weak ideas, implement a target number of high-value fixes or features without staging or committing by default, peer-review the combined diff, validate it, and produce a decision-ready summary. Use when the user asks Codex to spin off agents to find bugs, improve a codebase overnight, spend an available usage window on autonomous improvements, target many fixes or features, or act only as orchestrator and reviewer while leaving changes for later approval."
---

# Orchestrate improvements

Run a disciplined codebase improvement exercise. Optimize for verified value, not idea count, and leave the user a reviewable uncommitted worktree unless they explicitly request publication afterward.

## Establish the contract

1. Record the user's target, time window, focus areas, and mutation limits.
2. Treat "do not commit" as also forbidding staging, pushing, tagging, and pull requests.
3. Inspect repository instructions, `git status --short`, recent history, and the project structure.
4. Preserve pre-existing changes. Do not assign overlapping files when the worktree is already dirty.
5. Run the broadest practical baseline test suite and record exact counts.
6. If the user requests persistence until a terminal condition, use the available goal or continuation mechanism.

## Audit in parallel

Partition the repository by stable ownership boundaries such as UI, runtime/integration, and data providers. Give each audit agent a read-only first turn and require:

- no edits and no commits;
- at most ten ranked candidates;
- exact files and symbols;
- reproduction steps or code evidence;
- impact, proposed fix, risk, and likely tests;
- explicit rejection of speculative, broad, or low-value ideas.

Keep the primary agent in the orchestrator role. It should establish baselines, triage evidence, review diffs, route follow-ups, and run final validation rather than independently implementing a parallel feature.

If subagents are unavailable, follow the same phases sequentially and disclose that limitation.

## Triage hard

Classify each candidate:

- **Keep:** evidenced correctness, security, data-safety, resource, accessibility, or concrete UX problem with a bounded fix.
- **Defer:** valuable but architectural, migration-heavy, protocol-sensitive, or too large for the requested window.
- **Kill:** speculative, cosmetic without user value, duplicative, privacy-expanding, unsafe automation, or complexity greater than its benefit.

Treat the requested count as a target, never a quota. Do not manufacture weak changes to reach it. Prefer data-loss and security fixes, then correctness/resource leaks, then accessibility and recurring UX friction, then small features.

## Implement in ownership waves

1. Assign accepted items back to agents with disjoint file ownership.
2. Restate the no-commit rule in every implementation task.
3. Require focused regression tests and exact validation results.
4. Tell agents which attractive but risky ideas are out of scope.
5. Let an agent decline an item when a safe fix requires a broad rewrite.
6. Monitor `git status` and diff statistics without modifying an agent's active files.
7. Review each completed area before starting another wave.

Avoid concurrent edits to shared UI bundles, central service registrations, project files, or common fixtures. Sequence those changes when ownership cannot be isolated.

## Review adversarially

After implementation, assign an independent agent to review the entire uncommitted diff, especially code it did not author. Request only actionable P0-P2 findings covering:

- data loss or silent semantic changes;
- injection and sensitive error disclosure;
- process, cancellation, concurrency, and disposal paths;
- compatibility and permission regressions;
- false-positive safety checks;
- missing tests for failure paths.

Route every accepted review finding to the owning agent. Require a regression test and rerun affected suites. Reject review suggestions that merely enlarge scope or restate deferred features.

## Validate and freeze

The orchestrator must independently:

1. Read the important diffs rather than trusting agent summaries.
2. Run all unit and browser/integration tests.
3. Run a Release build when the repository supports one.
4. Run `git diff --check`.
5. Confirm there are no staged changes or commits from the exercise.
6. Record the final modified/untracked file count and compare test counts with baseline.

Do not silently repair a failing area owned by an agent; return it with evidence. Freeze scope once the target is met and the combined patch passes review.

## Deliver the decision summary

Lead with the number of keepers and state clearly that nothing was committed. Group changes by user impact and explain them in plain language. Include:

- the highest-risk bugs prevented;
- runtime and UX improvements;
- exact final test/build results;
- review findings that were corrected;
- killed ideas and credible deferred candidates;
- known tradeoffs or behavior that is now intentionally refused;
- worktree/staging/commit state.

Commit, push, or open a pull request only after a separate explicit user request.
