# Provider commands

Model catalogs and flags change. Start every run with `<cli> --version` and `<cli> --help`; treat the installed CLI as authoritative for syntax and the provider service/session metadata as authoritative for the effective model.

## Codex CLI

Non-interactive pattern:

```text
codex -a never -c model_reasoning_effort="<level>" \
  exec -s read-only --color never --model <exact-id> --json \
  review --uncommitted "<prompt>"
```

- `codex review --uncommitted` cannot take a custom prompt; use `codex exec review`.
- Global flags such as `-a` belong before `exec`. Confirm `--help` on the installed CLI.
- Verify the model from `--json` events, the `model:` banner, or `~/.codex/sessions`.

## Grok CLI

Non-interactive pattern:

```text
grok -p "<prompt>" --model <exact-id> --reasoning-effort <level> \
  --permission-mode dontAsk --sandbox read-only --output-format json \
  --verbatim --no-plan --no-subagents --disable-web-search \
  --tools read_file,grep,list_dir,run_terminal_cmd
```

- Keep the sandbox read-only. Do not use plan permission mode; it selects the plan agent and yields progress narration instead of a verdict.
- The invoked process is the reviewer. Do not let it load `$cross-cli-code-review`, fetch the web, chain shell commands, or write a review file.
- If the Grok client exits before `REVIEW_VERDICT`, wait for the session's `turn_ended` event. Do not launch a duplicate review.
- A review may need more than a short headless turn budget. If the first run stops on `max_turn_requests` after inspecting code, resume that same session once with a no-tool prompt that only asks for `REVIEW_VERDICT`.
- Pass a catalog ID from `grok models` (for example `grok-4.6`). Session JSON and `modelUsage` often report a runtime ID such as `grok-4.6-build` in `model_id`. That runtime ID is not selectable; treat it as verification of the catalog ID, not as a different model.
- Verify the model from JSON output or `~/.grok/sessions`. Require `REVIEW_VERDICT` in the `text` field.

## GitHub Copilot CLI

Non-interactive pattern:

```text
copilot -p "<prompt>" --model <exact-id> --effort <level> \
  --allow-tool="shell(git diff)" \
  --allow-tool="shell(git status)" \
  --allow-tool="shell(git log)" \
  --allow-tool="shell(git show)" \
  --deny-tool=write --no-ask-user --silent --stream off
```

- Use `/model` interactively to discover display names, but do not infer the backend ID from the label.
- Check `~/.copilot/session-state/*/events.jsonl` for the review's unique marker and `data.model` value.
- Treat IDs with suffixes such as `-picker` as distinct models unless metadata proves otherwise.
- An unavailable-model error invalidates the run. Do not fall back to `auto`.

## Claude Code

Non-interactive pattern:

```text
claude -p "<prompt>" --model <exact-id> --effort <level> \
  --permission-mode plan --tools "Read,Grep,Glob,Bash" --output-format json
```

- Do not pass `--fallback-model`; overload or unavailability must remain visible.
- Use plan permission mode and explicitly prohibit edits and tests in the prompt.
- Verify the model in JSON output when present. Otherwise locate the review's unique marker in Claude session JSONL and inspect adjacent model metadata.
- Aliases such as `opus` can move over time. Prefer a full model name when the user requests a specific release.

## Adding another provider

Add a provider only when it can satisfy all of these properties:

1. non-interactive prompt execution;
2. exact model selection with no silent fallback;
3. configurable reasoning effort when requested;
4. a read-only or plan permission mode;
5. structured output or session metadata that identifies the effective model, including any runtime ID that is not itself selectable.

Extend `scripts/run_review.py`, keep argument construction shell-free, and add a smoke test using `--prompt-only` before a live one-line identity check.
