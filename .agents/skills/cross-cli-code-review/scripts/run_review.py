#!/usr/bin/env python3
"""Run a model-verified, read-only review through an external coding CLI."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import hashlib
import uuid

PROVIDERS = ("claude", "codex", "copilot", "grok")
SESSION_ROOTS = {
    "claude": Path.home() / ".claude/projects",
    "codex": Path.home() / ".codex/sessions",
    "copilot": Path.home() / ".copilot/session-state",
    "grok": Path.home() / ".grok/sessions",
}
MODEL_ID_KEYS = {"model", "model_id", "current_model_id"}
# Runtime implementation suffixes reported in usage/session metadata that are
# not themselves selectable catalog IDs. Distinct picker IDs such as
# Copilot's `-picker` suffix are not listed here.
RUNTIME_SUFFIXES = ("-build",)
VERDICT_RE = re.compile(r"(?m)^REVIEW_VERDICT:\s*(findings|no_actionable_findings)\s*$")
TURN_LIMIT_MARKERS = ("max_turns", "max_turn_requests", "error_max_turns")
IDENTITY_MAX_TURNS = 2
REVIEW_MAX_TURNS = 80
VERDICT_MAX_TURNS = 3
VERDICT_ONLY_PROMPT = (
    "You already inspected the current code in this session. "
    "Do not call tools. Write the review from what you already saw. "
    "End with exactly one of these lines:\n"
    "REVIEW_VERDICT: findings\n"
    "REVIEW_VERDICT: no_actionable_findings"
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--provider", choices=PROVIDERS, required=True)
    parser.add_argument(
        "--model",
        required=True,
        help="Selectable catalog model ID. Do not pass a reported runtime ID such as grok-4.6-build.",
    )
    parser.add_argument("--effort", required=True, choices=("low", "medium", "high", "xhigh", "max"))
    parser.add_argument("--scope", choices=("working-tree", "staged", "branch"), default="working-tree")
    parser.add_argument("--base", default="dev", help="Base branch for --scope branch.")
    parser.add_argument("--context", action="append", default=[], help="Issue, PR, or review context; repeatable.")
    parser.add_argument("--focus", action="append", default=[], help="Extra review focus; repeatable.")
    parser.add_argument("--known-tests", help="Existing validation result to give the reviewer.")
    parser.add_argument("--allow-tests", action="store_true", help="Allow the reviewer to run tests.")
    parser.add_argument(
        "--identity-check", action="store_true",
        help="Verify provider/model metadata with a minimal prompt instead of reviewing.",
    )
    parser.add_argument("--prompt-only", action="store_true", help="Print the prompt and command without running it.")
    return parser.parse_args()


def run_git(*args: str, cwd: Path | None = None) -> str:
    result = subprocess.run(
        ["git", *args], check=True, text=True, cwd=cwd,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    return result.stdout


def has_head(root: Path) -> bool:
    check = subprocess.run(
        ["git", "rev-parse", "--verify", "-q", "HEAD"], cwd=root, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    return check.returncode == 0


def repo_content_state(root: Path) -> str:
    # Contents, not stat: a watcher touching files without changing them must not read as a
    # change, while edited, deleted, or staged files still show up. The tracked side is the full
    # patch against HEAD (content, not names) and each untracked file is hashed, so even a dirty
    # file that gets edited and restored is caught. Everything is anchored to the repository root,
    # because ls-files answers for the current directory subtree when asked anywhere else, and a
    # repository with no commits yet has no diff to take but its untracked files still count.
    parts = []
    if has_head(root):
        parts.append(run_git("diff", "HEAD", cwd=root))
        # A binary file's diff is only a sentence, so an already-dirty binary rewritten during the
        # review would go unnoticed by the patch alone; the dirty files are hashed as well.
        for rel in run_git("diff", "--name-only", "HEAD", cwd=root).splitlines():
            path = root / rel
            try:
                digest = hashlib.sha1(path.read_bytes()).hexdigest()
            except OSError:
                digest = "unreadable"
            parts.append(f"dirty {rel} {digest}")
    for rel in run_git("ls-files", "--others", "--exclude-standard", cwd=root).splitlines():
        path = root / rel
        try:
            digest = hashlib.sha1(path.read_bytes()).hexdigest()
        except OSError:
            digest = "unreadable"
        parts.append(f"{rel} {digest}")
    return "\n".join(parts)


def scope_text(args: argparse.Namespace) -> str:
    if args.scope == "staged":
        return "Review only the staged changes (`git diff --cached`) and their relevant surrounding code."
    if args.scope == "branch":
        return f"Review the branch diff from the merge base with `{args.base}` through HEAD and relevant surrounding code."
    return "Review every current change not in HEAD, including staged, unstaged, and untracked files, plus relevant surrounding code."


def build_prompt(args: argparse.Namespace, marker: str) -> str:
    if args.identity_check:
        return f"Identity check marker: {marker}\nReply with exactly this single token and nothing else: MODEL_OK"
    sections = [
        # The trace id rides in the first line on purpose: codex logs only that line of the prompt,
        # and the logged id is what ties model verification to exactly this session.
        f"You are the reviewer yourself (trace id: {marker}). Do not read, load, or follow any SKILL.md or skill, "
        "including cross-cli-code-review, and do not search for any provider transcript, session "
        "state, or prior review output: there is nothing to look up. Inspect the code in this "
        "working tree directly with your own file and search tools, then write the findings "
        "yourself.",
        "Use read_file for file contents, including untracked files. Run only one simple git command per shell call, such as `git status --short` or `git diff HEAD -- path`. Do not chain commands, fetch the web, or inspect another review session.",
        scope_text(args),
        "This is a read-only review. Do not modify files, stage, commit, push, or create repository artifacts.",
        "Report only actionable correctness, security, data-safety, SQL/API validity, regression, compatibility, resource-bound, UI-integration, or missing-test findings.",
        "For each finding, cite the current file and line, explain the concrete failure path, and check surrounding code, tests, and supplied discussion so already-fixed items are not reported.",
        "Order findings by severity. Omit style preferences, speculation, and progress narration. If no actionable findings remain, say so explicitly.",
        "Prefer fewer tool rounds. As soon as you can judge the scoped files, stop inspecting and emit REVIEW_VERDICT.",
        "End the final reply with exactly one of these lines:\nREVIEW_VERDICT: findings\nREVIEW_VERDICT: no_actionable_findings",
    ]
    if not args.allow_tests:
        sections.append("Do not run tests or builds.")
    if args.known_tests:
        sections.append(f"Known validation: {args.known_tests}")
    if args.context:
        sections.append("Context to verify:\n- " + "\n- ".join(args.context))
    if args.focus:
        sections.append("Review focus:\n- " + "\n- ".join(args.focus))
    return "\n\n".join(sections)


def build_command(args: argparse.Namespace, prompt: str, session_id: str | None = None) -> list[str]:
    if args.provider == "copilot":
        return [
            "copilot", "-p", prompt, "--model", args.model, "--effort", args.effort,
            "--allow-tool=shell(git diff)", "--allow-tool=shell(git status)",
            "--allow-tool=shell(git log)", "--allow-tool=shell(git show)",
            "--deny-tool=write", "--no-ask-user", "--silent", "--stream", "off",
        ]
    if args.provider == "claude":
        return [
            "claude", "-p", prompt, "--model", args.model, "--effort", args.effort,
            "--permission-mode", "plan", "--tools", "Read,Grep,Glob,Bash", "--output-format", "json",
        ]
    if args.provider == "codex":
        # `codex exec review --uncommitted` cannot carry a custom prompt, and this script's marker,
        # scope and context travel in the prompt, so the review runs as a read-only exec session
        # instead of the bespoke review command.
        return [
            "codex", "-a", "never", "-c", f'model_reasoning_effort="{args.effort}"',
            "exec", "-s", "read-only", "--color", "never", "-m", args.model, "--json",
            prompt,
        ]
    command = [
        "grok", "-p", prompt, "-m", args.model, "--reasoning-effort", args.effort,
        "--permission-mode", "dontAsk", "--sandbox", "read-only", "--output-format", "json",
        "--verbatim", "--no-plan", "--no-subagents", "--disable-web-search",
        "--tools", "read_file,grep,list_dir,run_terminal_cmd",
        "--disallowed-tools", "web_search,web_fetch,search_replace,Agent",
        "--max-turns", str(IDENTITY_MAX_TURNS if getattr(args, "identity_check", False) else REVIEW_MAX_TURNS),
    ]
    if session_id:
        command.extend(["--session-id", session_id])
    return command


def grok_verdict_command(args: argparse.Namespace, session_id: str) -> list[str]:
    return [
        "grok", "-p", VERDICT_ONLY_PROMPT, "-m", args.model, "--reasoning-effort", args.effort,
        "--resume", session_id, "--permission-mode", "dontAsk", "--sandbox", "read-only",
        "--output-format", "json", "--verbatim", "--no-plan", "--no-subagents",
        "--disable-web-search", "--max-turns", str(VERDICT_MAX_TURNS),
    ]


def hit_turn_limit(*blobs: str) -> bool:
    combined = "\n".join(blobs).lower()
    return any(marker in combined for marker in TURN_LIMIT_MARKERS)


def collect_models(value: object) -> set[str]:
    models: set[str] = set()
    if isinstance(value, dict):
        for key, child in value.items():
            if key in MODEL_ID_KEYS and isinstance(child, str):
                models.add(child)
            elif key == "modelUsage" and isinstance(child, dict):
                models.update(str(name) for name in child)
            models.update(collect_models(child))
    elif isinstance(value, list):
        for child in value:
            models.update(collect_models(child))
    return models


def accepted_model_ids(requested: str) -> set[str]:
    accepted = {requested}
    for suffix in RUNTIME_SUFFIXES:
        accepted.add(f"{requested}{suffix}")
    return accepted


def model_is_verified(requested: str, observed: set[str]) -> bool:
    return bool(observed & accepted_model_ids(requested))


def models_from_jsonl(root: Path, marker: str, started: float) -> set[str]:
    if not root.exists():
        return set()
    for path in sorted(root.rglob("*.jsonl"), key=lambda item: item.stat().st_mtime, reverse=True):
        try:
            if path.stat().st_mtime + 1 < started:
                break
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        if marker not in text:
            continue
        models: set[str] = set()
        for line in text.splitlines():
            try:
                models.update(collect_models(json.loads(line)))
            except json.JSONDecodeError:
                continue
        return models
    # No session correlates with this run: say so rather than guessing from sessions of other
    # reviews, which could vouch for a model that never ran here.
    return set()


def models_from_text(text: str) -> set[str]:
    return {match.group(1) for match in re.finditer(r"(?m)^model:\s+(\S+)", text)}


def verify_model(args: argparse.Namespace, stdout: str, marker: str, started: float) -> set[str]:
    models: set[str] = set()
    try:
        models.update(collect_models(json.loads(stdout)))
    except json.JSONDecodeError:
        for line in stdout.splitlines():
            try:
                models.update(collect_models(json.loads(line)))
            except json.JSONDecodeError:
                continue
    models.update(models_from_text(stdout))
    models.update(models_from_jsonl(SESSION_ROOTS[args.provider], marker, started))
    if not model_is_verified(args.model, models):
        observed = ", ".join(sorted(models)) or "none"
        raise RuntimeError(f"effective model could not be verified as {args.model!r}; observed: {observed}")
    return models


def extract_output_text(provider: str, stdout: str) -> str:
    if provider == "codex":
        # The --json stream carries the reply inside item.completed events, so the verdict has to
        # be read out of the decoded text rather than looked for in raw JSONL. A stream with no
        # reply decodes to nothing: the raw stream must never count as the reply, or an echoed
        # prompt would pass the identity and verdict gates.
        texts = []
        for line in stdout.splitlines():
            try:
                event = json.loads(line)
            except json.JSONDecodeError:
                continue
            item = event.get("item") if isinstance(event, dict) else None
            if (event.get("type") == "item.completed" and isinstance(item, dict)
                    and item.get("type") == "agent_message"
                    and isinstance(item.get("text"), str)):
                texts.append(item["text"])
        return "\n".join(texts)
    if provider in {"claude", "grok"}:
        try:
            payload = json.loads(stdout)
            for key in ("text", "result", "message"):
                value = payload.get(key)
                if isinstance(value, str) and value.strip():
                    return value
        except json.JSONDecodeError:
            pass
    return stdout


def has_required_closing(identity_check: bool, text: str, marker: str = "",
                         provider: str = "") -> bool:
    if identity_check:
        # Some models acknowledge the marker conversationally instead of parroting the token. An
        # accepted acknowledgement has to repeat this run's unique trace id, so a generic refusal
        # or any reply to a different prompt cannot pass; the effective model is verified
        # separately from session metadata. The echo is only trusted where the text is known to
        # be a decoded reply - codex's decoder returns nothing when there is no reply - because
        # claude and grok fall back to raw stdout, which contains the echoed prompt itself.
        marker_ok = marker != "" and marker in text and provider == "codex"
        return "MODEL_OK" in text or marker_ok
    return VERDICT_RE.search(text) is not None


def find_grok_session(session_id: str) -> Path | None:
    root = SESSION_ROOTS["grok"]
    if not session_id or not root.exists():
        return None
    matches = [path for path in root.rglob(session_id) if path.is_dir()]
    return matches[0] if len(matches) == 1 else None


def grok_turn_complete(session_dir: Path) -> bool:
    events = session_dir / "events.jsonl"
    if not events.exists():
        return False
    complete = False
    for line in events.read_text(encoding="utf-8", errors="replace").splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        kind = event.get("type")
        if kind == "turn_started":
            complete = False
        elif kind == "turn_ended":
            complete = True
    return complete


def grok_last_assistant_text(session_dir: Path) -> str:
    history = session_dir / "chat_history.jsonl"
    if not history.exists():
        return ""
    last = ""
    for line in history.read_text(encoding="utf-8", errors="replace").splitlines():
        try:
            payload = json.loads(line)
        except json.JSONDecodeError:
            continue
        if payload.get("type") != "assistant":
            continue
        content = payload.get("content")
        if isinstance(content, str) and content.strip():
            last = content
    return last


def wait_for_grok_session_text(session_id: str, timeout_sec: float = 900) -> str:
    deadline = time.time() + timeout_sec
    session_dir: Path | None = None
    while time.time() < deadline:
        if session_dir is None:
            session_dir = find_grok_session(session_id)
        if session_dir is not None and grok_turn_complete(session_dir):
            return grok_last_assistant_text(session_dir)
        time.sleep(1)
    return grok_last_assistant_text(session_dir) if session_dir is not None else ""


def main() -> int:
    args = parse_args()
    try:
        root = Path(run_git("rev-parse", "--show-toplevel").strip())
        before = repo_content_state(root)
    except (OSError, subprocess.CalledProcessError) as error:
        print(f"error: run this script inside a Git repository: {error}", file=sys.stderr)
        return 2

    executable = shutil.which(args.provider)
    if executable is None:
        print(f"error: {args.provider} CLI is not installed or is not on PATH", file=sys.stderr)
        return 2

    session_id = str(uuid.uuid4())
    marker = f"review-session-{session_id}"
    prompt = build_prompt(args, marker)
    if args.provider == "codex":
        # codex-cli delivers only the first paragraph of an argv prompt and silently drops the
        # rest, so the review contract travels as one block of single-broken lines.
        prompt = prompt.replace("\n\n", "\n")
    command = build_command(args, prompt, session_id=session_id)
    # The command names the CLI bare, but Windows cannot launch the .cmd shims the npm installs
    # leave behind without the resolved path, so the search result leads the argument vector.
    command[0] = executable
    if args.prompt_only:
        print(prompt)
        print("\nArgument vector:\n" + json.dumps(command, indent=2))
        return 0

    started = time.time()
    completed = subprocess.run(command, cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    output_text = extract_output_text(args.provider, completed.stdout)
    if args.provider == "grok" and not has_required_closing(args.identity_check, output_text, marker, args.provider):
        session_text = wait_for_grok_session_text(session_id)
        if session_text.strip():
            output_text = session_text
    if (
        args.provider == "grok"
        and not args.identity_check
        and not has_required_closing(False, output_text)
        and hit_turn_limit(completed.stdout, completed.stderr)
    ):
        follow = subprocess.run(
            [executable, *grok_verdict_command(args, session_id)[1:]],
            cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        )
        follow_text = extract_output_text("grok", follow.stdout)
        if not has_required_closing(False, follow_text):
            session_text = wait_for_grok_session_text(session_id, timeout_sec=180)
            if session_text.strip():
                follow_text = session_text
        if has_required_closing(False, follow_text):
            output_text = follow_text
            completed = follow

    if completed.returncode != 0 and not has_required_closing(args.identity_check, output_text, marker, args.provider):
        sys.stderr.write(completed.stderr or completed.stdout)
        print("error: review failed; no fallback model was used", file=sys.stderr)
        return completed.returncode or 1

    try:
        observed = verify_model(args, completed.stdout, marker, started)
    except RuntimeError as error:
        print(f"error: {error}; discard this review output", file=sys.stderr)
        return 3

    after = repo_content_state(root)
    if after != before:
        print("warning: repository status changed during the review; inspect it before trusting the result", file=sys.stderr)
        return 4

    if not has_required_closing(args.identity_check, output_text, marker, args.provider):
        expected = "MODEL_OK" if args.identity_check else "REVIEW_VERDICT"
        print(f"error: output lacked {expected}; discard this review output", file=sys.stderr)
        return 5

    print(f"Verified provider={args.provider} model={args.model} effort={args.effort}", file=sys.stderr)
    print(output_text.rstrip())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
