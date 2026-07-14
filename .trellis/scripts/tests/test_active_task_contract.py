import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


SCRIPTS_DIR = Path(__file__).resolve().parents[1]
CLI_PATH = SCRIPTS_DIR / "active_task_cli.py"
sys.path.insert(0, str(SCRIPTS_DIR))

from common.active_task import resolve_active_task


class ActiveTaskContractTests(unittest.TestCase):
    def _create_session(
        self,
        repo_root: Path,
        session_key: str,
        payload: str | dict[str, str],
    ) -> None:
        sessions_directory = repo_root / ".trellis" / ".runtime" / "sessions"
        sessions_directory.mkdir(parents=True, exist_ok=True)
        content = payload if isinstance(payload, str) else json.dumps(payload)
        (sessions_directory / f"{session_key}.json").write_text(
            content,
            encoding="utf-8",
        )

    def _create_task(self, repo_root: Path, task_name: str) -> str:
        task_path = f".trellis/tasks/{task_name}"
        (repo_root / task_path).mkdir(parents=True)
        return task_path

    def test_resolver_fixture_matrix_covers_identity_and_recovery_boundaries(self) -> None:
        cases = (
            ("zero sessions", (), None, False, None, "none", False),
            (
                "one session without identity",
                (("foreign", {"current_task": ".trellis/tasks/foreign"}),),
                None,
                False,
                None,
                "none",
                False,
            ),
            (
                "multiple sessions without identity",
                (
                    ("first", {"current_task": ".trellis/tasks/first"}),
                    ("second", {"current_task": ".trellis/tasks/second"}),
                ),
                None,
                False,
                None,
                "none",
                False,
            ),
            (
                "explicit sole recovery",
                (("foreign", {"current_task": ".trellis/tasks/foreign"}),),
                None,
                True,
                ".trellis/tasks/foreign",
                "session-fallback:foreign",
                False,
            ),
            (
                "explicit identity",
                (("opencode_worker", {"current_task": ".trellis/tasks/owned"}),),
                {"sessionId": "worker"},
                False,
                ".trellis/tasks/owned",
                "session:opencode_worker",
                False,
            ),
            (
                "stale explicit identity",
                (("opencode_worker", {"current_task": ".trellis/tasks/missing"}),),
                {"sessionId": "worker"},
                False,
                ".trellis/tasks/missing",
                "session:opencode_worker",
                True,
            ),
            (
                "malformed session file",
                (("foreign", "{not-json"),),
                None,
                True,
                None,
                "none",
                False,
            ),
        )

        for (
            case_name,
            sessions,
            platform_input,
            use_sole_session,
            expected_task_path,
            expected_source,
            expected_stale,
        ) in cases:
            with self.subTest(case_name), tempfile.TemporaryDirectory() as temp_directory:
                repo_root = Path(temp_directory)
                for session_key, payload in sessions:
                    self._create_session(repo_root, session_key, payload)
                    payload_dict = payload if isinstance(payload, dict) else {}
                    task_ref = payload_dict.get("current_task")
                    if task_ref and not task_ref.endswith("missing"):
                        (repo_root / task_ref).mkdir(parents=True, exist_ok=True)

                with patch.dict(os.environ, {}, clear=True):
                    result = resolve_active_task(
                        repo_root,
                        platform_input=platform_input,
                        platform="opencode" if platform_input else None,
                        allow_sole_session_fallback=use_sole_session,
                    )

                self.assertEqual(expected_task_path, result.task_path)
                self.assertEqual(expected_source, result.source)
                self.assertEqual(expected_stale, result.stale)

    def test_cli_rejects_malformed_platform_json_without_a_task_result(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            result = subprocess.run(
                [
                    sys.executable,
                    str(CLI_PATH),
                    "--repo-root",
                    temp_directory,
                    "--platform-input-json",
                    "{not-json",
                ],
                capture_output=True,
                encoding="utf-8",
                errors="replace",
            )

        self.assertNotEqual(0, result.returncode)
        self.assertEqual("", result.stdout)

    def test_missing_identity_does_not_adopt_the_only_session(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            repo_root = Path(temp_directory)
            task_directory = repo_root / ".trellis" / "tasks" / "foreign"
            task_directory.mkdir(parents=True)
            sessions_directory = repo_root / ".trellis" / ".runtime" / "sessions"
            sessions_directory.mkdir(parents=True)
            (sessions_directory / "foreign.json").write_text(
                json.dumps({"current_task": ".trellis/tasks/foreign"}),
                encoding="utf-8",
            )

            result = resolve_active_task(repo_root)

            self.assertIsNone(result.task_path)
            self.assertEqual("none", result.source_type)

    def test_explicit_recovery_can_select_the_only_session(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            repo_root = Path(temp_directory)
            task_directory = repo_root / ".trellis" / "tasks" / "foreign"
            task_directory.mkdir(parents=True)
            sessions_directory = repo_root / ".trellis" / ".runtime" / "sessions"
            sessions_directory.mkdir(parents=True)
            (sessions_directory / "foreign.json").write_text(
                json.dumps({"current_task": ".trellis/tasks/foreign"}),
                encoding="utf-8",
            )

            result = resolve_active_task(repo_root, allow_sole_session_fallback=True)

            self.assertEqual(".trellis/tasks/foreign", result.task_path)
            self.assertEqual("session-fallback", result.source_type)

    def test_json_cli_requires_explicit_sole_session_recovery(self) -> None:
        with tempfile.TemporaryDirectory() as temp_directory:
            repo_root = Path(temp_directory)
            task_directory = repo_root / ".trellis" / "tasks" / "foreign"
            task_directory.mkdir(parents=True)
            sessions_directory = repo_root / ".trellis" / ".runtime" / "sessions"
            sessions_directory.mkdir(parents=True)
            (sessions_directory / "foreign.json").write_text(
                json.dumps({"current_task": ".trellis/tasks/foreign"}),
                encoding="utf-8",
            )

            blocked = subprocess.run(
                [sys.executable, str(CLI_PATH), "--repo-root", str(repo_root)],
                capture_output=True,
                encoding="utf-8",
                errors="replace",
            )
            recovered = subprocess.run(
                [
                    sys.executable,
                    str(CLI_PATH),
                    "--repo-root",
                    str(repo_root),
                    "--use-sole-session",
                ],
                capture_output=True,
                encoding="utf-8",
                errors="replace",
            )

            self.assertEqual(0, blocked.returncode)
            self.assertEqual(0, recovered.returncode)
            self.assertEqual(
                {"taskPath": None, "source": "none", "stale": False},
                json.loads(blocked.stdout),
            )
            self.assertEqual(
                {
                    "taskPath": ".trellis/tasks/foreign",
                    "source": "session-fallback:foreign",
                    "stale": False,
                },
                json.loads(recovered.stdout),
            )


if __name__ == "__main__":
    unittest.main()
