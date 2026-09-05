---
name: orchestrate-improvements
description: "Run repository-wide improvement sessions with parallel agents and isolated worktrees when requested. Leave reviewed changes uncommitted by default."
---

# Orchestrate improvements

Run a disciplined codebase improvement exercise. Optimize for verified value, not idea count, and leave the user a reviewable uncommitted worktree unless they explicitly request publication afterward.

## Establish the contract

1. Record the user's target, time window, focus areas, and mutation limits.
2. Treat "do not commit" as also forbidding staging, pushing, tagging, and pull requests.
3. If commits or pull requests are authorized, follow the authorship rule in `AGENTS.md`: never attribute an AI agent as author, co-author, or contributor, and require the same of every implementation agent.
4. Record the original worktree's resolved path, current branch, HEAD, and `git status --short`. This is the integration target; do not let implementation agents work in it.
5. Inspect repository instructions, recent history, and the project structure.
6. Preserve pre-existing changes. Do not assign work that overlaps them. If an improvement depends on uncommitted baseline changes, defer it or obtain direction instead of copying user changes into temporary worktrees.
7. Run `dotnet test --configuration Release` as the baseline and record results and unavailable checks.
8. Define completion from the requested scope and time window. Use a goal or continuation mechanism only when the user requested one.

## Audit in parallel

Partition the repository by stable ownership boundaries such as UI, runtime/integration, and data providers. Give each audit agent a read-only first turn and require:

- no edits and no commits;
- at most ten ranked candidates;
- exact files and symbols;
- reproduction steps or code evidence;
- impact, proposed fix, risk, and likely tests;
- explicit rejection of speculative, broad, or low-value ideas.

Keep the primary agent in the orchestrator role. It should establish baselines, triage evidence, review diffs, route follow-ups, and run final validation rather than independently implementing a parallel feature.

Delegate bounded assignments only when they can advance independently. Give each
agent the relevant paths, constraints, expected evidence, and completion condition;
avoid copying the whole session. Reuse existing agents and results, and stop
dispatching new work when the remaining window is needed for review and integration.

If subagents are unavailable, follow the same phases sequentially and disclose that limitation.

## Triage hard

Classify each candidate:

- **Keep:** evidenced correctness, security, data-safety, resource, accessibility, or concrete UX problem with a bounded fix.
- **Defer:** valuable but architectural, migration-heavy, protocol-sensitive, or too large for the requested window.
- **Kill:** speculative, cosmetic without user value, duplicative, privacy-expanding, unsafe automation, or complexity greater than its benefit.

Treat the requested count as a target, never a quota. Do not manufacture weak changes to reach it. Prefer data-loss and security fixes, then correctness/resource leaks, then accessibility and recurring UX friction, then small features.

## Prepare isolated worktrees

Create a dedicated branch and linked worktree for each accepted implementation assignment. A tightly coupled fix and its tests may share one assignment; unrelated features must not.

1. Create every worktree from the recorded baseline commit, using unique, explicit paths outside the original repository directory and temporary branch names such as `codex/improve-<slug>`.
2. Resolve and record each worktree path, branch, owner, feature scope, and allowed files before dispatching the agent.
3. Assign exactly one implementation agent to each worktree. Tell it to work only there, never in the original worktree, and not to edit another assignment's files. Never let multiple implementation agents share a worktree.
4. Keep audit-only agents read-only. Give reviewers a worktree path to inspect rather than permission to modify it.
5. Do not reuse a worktree after its assignment changes materially; create a new isolated assignment instead.

If linked worktrees are unavailable, use another repository-native isolation mechanism. Do not fall back to several agents editing one checkout concurrently; sequence the work and disclose the limitation.

## Implement in ownership waves

1. Assign accepted items back to agents in their dedicated worktrees with disjoint file ownership.
2. Restate the worktree path, allowed files, and no-commit rule in every implementation task. Agents must not stage, commit, merge, rebase, or publish unless the user explicitly authorized that workflow.
3. Require focused regression tests and exact validation results.
4. Tell agents which attractive but risky ideas are out of scope.
5. Let an agent decline an item when a safe fix requires a broad rewrite.
6. Monitor each worktree's `git status` and diff statistics without modifying an agent's active files.
7. Freeze an assignment when its agent finishes; no further edits may occur while it is under review or integration.

Worktrees isolate files on disk but do not eliminate merge conflicts. Avoid concurrent edits to shared UI bundles, central service registrations, project files, or common fixtures. Sequence dependent changes from the latest integrated baseline when ownership cannot be isolated.

## Review adversarially

After implementation, assign an independent agent to review each frozen worktree, especially code it did not author. Request only actionable P0-P2 findings covering:

- data loss or silent semantic changes;
- injection and sensitive error disclosure;
- process, cancellation, concurrency, and disposal paths;
- compatibility and permission regressions;
- false-positive safety checks;
- missing tests for failure paths.

Route every accepted review finding to the owning agent. Require a regression test and rerun affected suites. Reject review suggestions that merely enlarge scope or restate deferred features.

Do not integrate an assignment until its focused tests pass and its review findings are resolved. After integration, review the combined diff on the original branch for cross-feature interactions.

## Integrate onto the original branch

The orchestrator alone integrates accepted work. Return to the recorded original worktree and confirm its branch, HEAD ancestry, and pre-existing status before every integration.

1. Integrate reviewed assignments sequentially in dependency order.
2. Under the default no-commit contract, transfer the reviewed tracked diff and every explicitly inventoried untracked file from the feature worktree into the original worktree. Do not stage or create temporary commits. Verify the integrated paths against the source worktree before proceeding.
3. If commits were explicitly authorized, prefer focused commits on the feature branches and cherry-pick them onto the original branch. Resolve conflicts only in the original worktree, then rerun the affected tests.
4. Never merge or copy an entire worktree blindly. Exclude agent artifacts, build output, caches, credentials, and unrelated files.
5. If an assignment conflicts with user changes or another accepted feature, stop integrating that assignment, keep its worktree intact, and report the exact conflict. Do not overwrite either side.
6. After each transfer, inspect the original worktree's diff and run the smallest relevant validation before integrating the next assignment.

## Validate and freeze

The orchestrator must independently:

1. Read the important combined diffs rather than trusting agent summaries.
2. Run `dotnet test --configuration Release` on the combined change, including relevant browser/integration coverage. Report skipped or unavailable checks.
3. Reuse the build performed by the test command; run a separate build only for configurations it does not cover. Repeat passing checks only after relevant changes or new evidence.
4. Run `git diff --check`.
5. Confirm the original branch is still checked out and there are no staged changes or commits from the exercise unless explicitly authorized.
6. Record the final modified/untracked file count and compare test counts with baseline.

Do not silently repair a failing area owned by an agent; return it with evidence. Freeze scope once the target is met and the combined patch passes review.

## Clean temporary worktrees

Clean isolation resources only after the combined patch passes validation.

1. For every assignment, compare its reviewed diff and untracked-file inventory with the integrated result. Keep any worktree whose changes are missing, conflicted, or still under investigation.
2. Confirm the exact resolved path belongs to the recorded temporary worktree set before removing it. Never run broad recursive cleanup against a workspace root or parent directory.
3. Remove each fully integrated linked worktree with Git's worktree command. A dirty worktree may be force-removed only after the comparison proves all intended changes exist in the original worktree.
4. Delete its temporary branch only when it has no unintegrated commits. Prefer safe branch deletion after cherry-picking; use force deletion only for a verified no-commit temporary branch.
5. Run `git worktree list` and `git worktree prune`, then confirm no exercise worktrees or temporary branches remain.

If cleanup cannot be proven safe, leave the affected worktree intact and include its path and reason in the decision summary.

## Deliver the decision summary

Lead with the number of keepers and state clearly that nothing was committed. Group changes by user impact and explain them in plain language. Include:

- the highest-risk bugs prevented;
- runtime and UX improvements;
- exact final test/build results;
- review findings that were corrected;
- killed ideas and credible deferred candidates;
- known tradeoffs or behavior that is now intentionally refused;
- original worktree/staging/commit state;
- temporary worktree and branch cleanup state, including any intentionally retained path.

Commit, push, or open a pull request only after a separate explicit user request.
