#!/usr/bin/env python3
"""Unit tests for catalog-vs-runtime model identity checks."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from argparse import Namespace
from pathlib import Path

from run_review import (
    REVIEW_MAX_TURNS,
    accepted_model_ids,
    build_command,
    build_prompt,
    collect_models,
    extract_output_text,
    grok_last_assistant_text,
    grok_turn_complete,
    grok_verdict_command,
    has_required_closing,
    hit_turn_limit,
    model_is_verified,
    repo_content_state,
)


class RepoContentStateTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        subprocess.run(["git", "init", "-q"], cwd=self.root, check=True, capture_output=True)
        subprocess.run(["git", "config", "user.email", "test@example.com"],
                       cwd=self.root, check=True, capture_output=True)
        subprocess.run(["git", "config", "user.name", "Test"],
                       cwd=self.root, check=True, capture_output=True)

    def git(self, *args: str) -> None:
        subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True, text=True)

    def write(self, rel: str, text: str) -> None:
        (self.root / rel).write_text(text, encoding="utf-8")

    def test_untracked_files_count_even_without_a_commit(self) -> None:
        self.write("a.txt", "one")
        before = repo_content_state(self.root)
        self.write("a.txt", "two")
        self.assertNotEqual(before, repo_content_state(self.root))

    def test_content_changes_count_and_restored_edits_do_not(self) -> None:
        self.write("tracked.txt", "v1")
        self.git("add", "tracked.txt")
        self.git("commit", "-q", "-m", "init")
        self.write("new.txt", "u1")
        before = repo_content_state(self.root)
        # A watcher rewriting the same bytes changes the stat but not the state.
        self.write("new.txt", "u1")
        self.assertEqual(before, repo_content_state(self.root))
        # Editing an untracked file counts, and so does editing a tracked one.
        self.write("new.txt", "u2")
        edited = repo_content_state(self.root)
        self.assertNotEqual(before, edited)
        self.write("tracked.txt", "v2")
        self.assertNotEqual(edited, repo_content_state(self.root))
        # Restoring the tracked edit returns the state to what it was.
        self.write("tracked.txt", "v1")
        self.assertEqual(edited, repo_content_state(self.root))


class AcceptedModelIdsTests(unittest.TestCase):
    def test_catalog_id_includes_runtime_build_suffix(self) -> None:
        self.assertEqual(accepted_model_ids("grok-4.6"), {"grok-4.6", "grok-4.6-build"})

    def test_picker_suffix_is_not_a_runtime_alias(self) -> None:
        self.assertEqual(
            accepted_model_ids("mai-code-1.1-flash"),
            {"mai-code-1.1-flash", "mai-code-1.1-flash-build"},
        )
        self.assertNotIn("mai-code-1.1-flash-picker", accepted_model_ids("mai-code-1.1-flash"))


class ModelIsVerifiedTests(unittest.TestCase):
    def test_grok_runtime_backend_verifies_catalog_id(self) -> None:
        self.assertTrue(model_is_verified("grok-4.6", {"grok-4.6-build"}))

    def test_catalog_id_verifies_itself(self) -> None:
        self.assertTrue(model_is_verified("grok-4.6", {"grok-4.6"}))

    def test_different_catalog_id_is_rejected(self) -> None:
        self.assertFalse(model_is_verified("grok-4.6", {"grok-4.5"}))
        self.assertFalse(model_is_verified("grok-4.6", {"grok-4.5-build"}))

    def test_runtime_id_is_not_selected_as_a_catalog_id(self) -> None:
        self.assertFalse(model_is_verified("grok-4.6-build", {"grok-4.6"}))

    def test_empty_observation_fails(self) -> None:
        self.assertFalse(model_is_verified("grok-4.6", set()))


class CollectModelsTests(unittest.TestCase):
    def test_collects_model_id_and_usage_keys(self) -> None:
        payload = {
            "current_model_id": "grok-4.6",
            "messages": [{"model_id": "grok-4.6-build"}],
            "usage": {"modelUsage": {"grok-4.6-build": {"inputTokens": 1}}},
        }
        self.assertEqual(collect_models(payload), {"grok-4.6", "grok-4.6-build"})


class ReviewGateTests(unittest.TestCase):
    def test_prompt_requires_verdict_and_forbids_skill_follow(self) -> None:
        args = Namespace(
            identity_check=False,
            scope="working-tree",
            allow_tests=False,
            known_tests=None,
            context=[],
            focus=[],
        )
        prompt = build_prompt(args, "review-session-test")
        self.assertIn("You are the reviewer yourself", prompt)
        self.assertIn("Do not read, load, or follow any SKILL.md or skill", prompt)
        self.assertIn("one simple git command", prompt)
        self.assertIn("REVIEW_VERDICT: findings", prompt)
        self.assertNotIn("Review marker: cross-cli-review", prompt)

    def test_grok_command_is_reviewer_not_planner(self) -> None:
        args = Namespace(provider="grok", model="grok-4.6", effort="medium", identity_check=False)
        command = build_command(args, "review", session_id="11111111-1111-1111-1111-111111111111")
        self.assertIn("--no-plan", command)
        self.assertIn("--session-id", command)
        self.assertIn("web_fetch", command[command.index("--disallowed-tools") + 1])
        self.assertEqual(command[command.index("--max-turns") + 1], str(REVIEW_MAX_TURNS))
        self.assertGreaterEqual(REVIEW_MAX_TURNS, 40)
        self.assertEqual(command[command.index("--permission-mode") + 1], "dontAsk")

    def test_turn_limit_triggers_same_session_verdict_pass(self) -> None:
        self.assertTrue(hit_turn_limit('{"stopReason":"max_turn_requests"}'))
        args = Namespace(model="grok-4.6", effort="medium")
        command = grok_verdict_command(args, "11111111-1111-1111-1111-111111111111")
        self.assertEqual(command[command.index("--resume") + 1], "11111111-1111-1111-1111-111111111111")
        self.assertEqual(command[command.index("--max-turns") + 1], "3")
        self.assertNotIn("--session-id", command)

    def test_progress_narration_is_not_a_verdict(self) -> None:
        narration = "I'll start by loading the review skill and then inspect the tree."
        self.assertFalse(has_required_closing(False, narration))
        self.assertTrue(has_required_closing(False, "No issues.\nREVIEW_VERDICT: no_actionable_findings\n"))

    def test_identity_closing_needs_the_token_or_this_runs_marker(self) -> None:
        marker = "review-session-11111111-1111-1111-1111-111111111111"
        self.assertTrue(has_required_closing(True, "MODEL_OK", marker, "codex"))
        # The marker echo is only trusted on the provider whose decoder guarantees it is a reply.
        self.assertTrue(
            has_required_closing(True, f"Identity check marker received: `{marker}`.", marker, "codex"))
        self.assertFalse(
            has_required_closing(True, f"Identity check marker received: `{marker}`.", marker, "claude"))
        # A reply that neither parrots the token nor echoes this run's unique trace id proves
        # nothing about having heard this prompt.
        self.assertFalse(has_required_closing(
            True, "Identity check marker received: `review-session-22222222-2222-2222-2222-222222222222`.",
            marker, "codex"))
        self.assertFalse(has_required_closing(True, "I cannot help with that.", marker, "codex"))

    def test_codex_jsonl_events_are_decoded_before_verdict_check(self) -> None:
        stdout = "\n".join([
            '{"type":"thread.started","thread_id":"t"}',
            '{"type":"item.completed","item":{"id":"i","type":"reasoning","text":"thinking"}}',
            '{"type":"item.completed","item":{"id":"j","type":"agent_message",'
            '"text":"No actionable findings.\\nREVIEW_VERDICT: no_actionable_findings"}}',
        ])
        text = extract_output_text("codex", stdout)
        self.assertIn("REVIEW_VERDICT: no_actionable_findings", text)
        self.assertTrue(has_required_closing(False, text))

    def test_codex_stream_without_a_reply_is_not_the_reply_itself(self) -> None:
        # A crashed or refused run still echoes the prompt, and the prompt carries the trace id;
        # an undecoded stream must read as silence so neither echo can pass a gate.
        marker = "review-session-33333333-3333-3333-3333-333333333333"
        stdout = json.dumps({
            "type": "item.completed",
            "item": {"id": "i", "type": "user_message", "text": f"trace id: {marker}"},
        })
        self.assertEqual(extract_output_text("codex", stdout), "")
        self.assertFalse(has_required_closing(True, extract_output_text("codex", stdout), marker))
        self.assertFalse(has_required_closing(False, extract_output_text("codex", stdout)))

    def test_grok_json_text_is_preferred_over_other_fields(self) -> None:
        stdout = json.dumps({
            "result": "I'll start by loading the skill.",
            "text": "No actionable findings remain.\nREVIEW_VERDICT: no_actionable_findings\n",
        })
        text = extract_output_text("grok", stdout)
        self.assertIn("REVIEW_VERDICT: no_actionable_findings", text)
        self.assertTrue(has_required_closing(False, text))

    def test_session_trace_is_complete_only_after_turn_ended(self) -> None:
        root = Path(self.id().replace(".", "_"))
        self.addCleanup(lambda: shutil.rmtree(root, ignore_errors=True))
        root.mkdir(parents=True)
        (root / "events.jsonl").write_text(
            '{"type":"turn_started"}\n{"type":"turn_ended","outcome":"completed"}\n',
            encoding="utf-8",
        )
        (root / "chat_history.jsonl").write_text(
            '{"type":"assistant","content":"Looking next."}\n'
            '{"type":"assistant","content":"No issues.\\nREVIEW_VERDICT: no_actionable_findings\\n"}\n',
            encoding="utf-8",
        )
        self.assertTrue(grok_turn_complete(root))
        self.assertIn("REVIEW_VERDICT: no_actionable_findings", grok_last_assistant_text(root))


if __name__ == "__main__":
    raise SystemExit(unittest.main())
