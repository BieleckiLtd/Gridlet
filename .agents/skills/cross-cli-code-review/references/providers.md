# Provider commands

Model catalogs and flags change. Start every run with `<cli> --version` and `<cli> --help`; treat the installed CLI as authoritative for syntax and the provider service/session metadata as authoritative for the effective model.

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
5. structured output or session metadata that identifies the effective model.

Extend `scripts/run_review.py`, keep argument construction shell-free, and add a smoke test using `--prompt-only` before a live one-line identity check.
