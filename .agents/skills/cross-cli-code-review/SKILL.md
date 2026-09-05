---
name: cross-cli-code-review
description: "Get a requested independent code review through Codex, Claude, Grok, or Copilot CLI, verifying model identity and triaging findings. Supports fix-and-rereview requests."
---

# Cross-CLI code review

Obtain an independent review without surrendering control of the worktree. Treat the requested provider, model, and effort as hard requirements.

## Establish the review contract

1. Read repository instructions and capture `git status --short`, the current branch, and the requested review scope.
2. Ask only when the scope or model is materially ambiguous. Otherwise infer the scope from the request and current changes.
3. Record the provider, selectable catalog ID, any reported runtime/backend ID, and reasoning effort. Pass the catalog ID to the CLI. Never replace a rejected catalog ID with a similarly named model, alias, legacy picker entry, automatic selection, or a reported runtime ID that is not itself selectable.
4. Keep review invocations read-only. Do not grant write tools, stage, commit, push, or run tests unless the user separately authorizes them.
5. Include relevant issue or PR discussion in the prompt when available. Require the reviewer to check current code and comments so it does not report already-fixed items.

## Run the review

Prefer `scripts/run_review.py` for Codex, Claude Code, Grok, and Copilot CLI:

```text
python .agents/skills/cross-cli-code-review/scripts/run_review.py \
  --provider copilot \
  --model mai-code-1.1-flash \
  --effort high \
  --scope working-tree \
  --context "Five remaining items from issue #64; read its comments before judging status."
```

Use `--prompt-only` to inspect the generated prompt and command without invoking a provider. Use `--allow-tests` only when the user requested test execution during review. Run the script from the repository being reviewed.

Use `--identity-check` when model availability or identity is uncertain. It sends a minimal prompt, verifies session metadata, and performs no review.

For provider flags, model discovery, and manual fallback commands, read [references/providers.md](references/providers.md). Do not rely on remembered model catalogs because CLI releases and account entitlements change.

## Verify identity and integrity

1. Require the CLI to accept the exact selectable catalog ID. If it rejects that ID, stop. Check the installed version and the provider's current catalog; update the CLI only with user authorization when the update changes software outside the repository.
2. Verify the effective model from structured output or session metadata. A UI label or self-description alone is insufficient when metadata is available. A reported runtime ID that is not in the catalog (for example Grok `grok-4.6` reporting `grok-4.6-build`) is the selected model, not a fallback. A different selectable catalog ID invalidates the run.
3. Confirm the requested effort in the invocation. If the provider exposes effort metadata, verify that too.
4. Compare repository state before and after. If anything changed, report it and inspect the change; never silently revert user work.
5. Discard output from any run that used the wrong model, even if its findings look useful.
6. Accept a review only when the output ends with `REVIEW_VERDICT: findings` or `REVIEW_VERDICT: no_actionable_findings`. Progress narration without that line is not a review. If the reviewer process is still running, wait for that line. Do not start a second review of the same scope, and do not treat a session trace as the verdict.

## Triage findings

Accept only actionable correctness, security, data-safety, SQL/API validity, regression, compatibility, resource-bound, UI-integration, or missing-test findings.

For every claim:

1. Open the cited current lines and relevant surrounding code.
2. Check tests, issue/PR comments, and later edits that may already address it.
3. Reproduce or reason through the failure path.
4. Reject stale, speculative, stylistic, duplicate, or unsupported claims.
5. Report accepted findings by severity with precise file and line references.

If no claims survive verification, say so explicitly. Preserve the external reviewer's original verdict separately from your validation when they differ.

## Fix and review again

When the user asks to address findings, implement only validated claims, add focused regression coverage, run proportionate tests, and invoke a fresh review session with the same exact provider/model/effort unless the user changes them. Do not feed the new reviewer the desired verdict; provide the updated scope and previously raised claims so it can verify them independently.
