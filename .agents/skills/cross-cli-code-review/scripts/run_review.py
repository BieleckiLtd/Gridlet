#!/usr/bin/env python3
"""Run a model-verified, read-only review through Claude Code or Copilot CLI."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--provider", choices=("claude", "copilot"), required=True)
    parser.add_argument("--model", required=True, help="Exact backend model ID; aliases are discouraged.")
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


def run_git(*args: str) -> str:
    result = subprocess.run(
        ["git", *args], check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE
    )
    return result.stdout


def scope_text(args: argparse.Namespace) -> str:
    if args.scope == "staged":
        return "Review only the staged changes (`git diff --cached`) and their relevant surrounding code."
    if args.scope == "branch":
        return f"Review the branch diff from the merge base with `{args.base}` through HEAD and relevant surrounding code."
    return "Review every current change not in HEAD, including staged, unstaged, and untracked files, plus relevant surrounding code."


def build_prompt(args: argparse.Namespace, marker: str) -> str:
    if args.identity_check:
        return f"Identity check marker: {marker}\nReply with exactly: MODEL_OK"
    sections = [
        f"Review marker: {marker}",
        scope_text(args),
        "This is a read-only review. Do not modify files, stage, commit, push, or create repository artifacts.",
        "Report only actionable correctness, security, data-safety, SQL/API validity, regression, compatibility, resource-bound, UI-integration, or missing-test findings.",
        "For each finding, cite the current file and line, explain the concrete failure path, and check surrounding code, tests, and supplied discussion so already-fixed items are not reported.",
        "Order findings by severity. Omit style preferences and speculation. If no actionable findings remain, say so explicitly.",
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


def build_command(args: argparse.Namespace, prompt: str) -> list[str]:
    if args.provider == "copilot":
        return [
            "copilot", "-p", prompt, "--model", args.model, "--effort", args.effort,
            "--allow-tool=shell(git diff)", "--allow-tool=shell(git status)",
            "--allow-tool=shell(git log)", "--allow-tool=shell(git show)",
            "--deny-tool=write", "--no-ask-user", "--silent", "--stream", "off",
        ]
    return [
        "claude", "-p", prompt, "--model", args.model, "--effort", args.effort,
        "--permission-mode", "plan", "--tools", "Read,Grep,Glob,Bash", "--output-format", "json",
    ]


def collect_models(value: object) -> set[str]:
    models: set[str] = set()
    if isinstance(value, dict):
        for key, child in value.items():
            if key == "model" and isinstance(child, str):
                models.add(child)
            elif key == "modelUsage" and isinstance(child, dict):
                models.update(str(name) for name in child)
            models.update(collect_models(child))
    elif isinstance(value, list):
        for child in value:
            models.update(collect_models(child))
    return models


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
    return set()


def verify_model(args: argparse.Namespace, stdout: str, marker: str, started: float) -> set[str]:
    models: set[str] = set()
    try:
        models.update(collect_models(json.loads(stdout)))
    except json.JSONDecodeError:
        pass
    home = Path.home()
    session_root = home / (".copilot/session-state" if args.provider == "copilot" else ".claude/projects")
    models.update(models_from_jsonl(session_root, marker, started))
    if args.model not in models:
        observed = ", ".join(sorted(models)) or "none"
        raise RuntimeError(f"effective model could not be verified as {args.model!r}; observed: {observed}")
    return models


def display_result(provider: str, stdout: str) -> None:
    if provider == "claude":
        try:
            payload = json.loads(stdout)
            result = payload.get("result")
            if isinstance(result, str):
                print(result)
                return
        except json.JSONDecodeError:
            pass
    print(stdout.rstrip())


def main() -> int:
    args = parse_args()
    try:
        root = Path(run_git("rev-parse", "--show-toplevel").strip())
        before = run_git("status", "--porcelain=v2", "--untracked-files=all")
    except (OSError, subprocess.CalledProcessError) as error:
        print(f"error: run this script inside a Git repository: {error}", file=sys.stderr)
        return 2

    executable = shutil.which(args.provider)
    if executable is None:
        print(f"error: {args.provider} CLI is not installed or is not on PATH", file=sys.stderr)
        return 2

    marker = f"cross-cli-review-{uuid.uuid4()}"
    prompt = build_prompt(args, marker)
    command = build_command(args, prompt)
    if args.prompt_only:
        print(prompt)
        print("\nArgument vector:\n" + json.dumps(command, indent=2))
        return 0

    started = time.time()
    completed = subprocess.run(command, cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if completed.returncode != 0:
        sys.stderr.write(completed.stderr or completed.stdout)
        print("error: review failed; no fallback model was used", file=sys.stderr)
        return completed.returncode or 1

    try:
        observed = verify_model(args, completed.stdout, marker, started)
    except RuntimeError as error:
        print(f"error: {error}; discard this review output", file=sys.stderr)
        return 3

    after = run_git("status", "--porcelain=v2", "--untracked-files=all")
    if after != before:
        print("warning: repository status changed during the review; inspect it before trusting the result", file=sys.stderr)
        return 4

    print(f"Verified provider={args.provider} model={args.model} effort={args.effort}", file=sys.stderr)
    display_result(args.provider, completed.stdout)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
