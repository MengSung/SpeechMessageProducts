import io
import subprocess
import sys
import tempfile
import time
import unittest
from contextlib import redirect_stderr
from pathlib import Path
from unittest.mock import patch


SCRIPTS_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS_DIR))

from common.config import get_hooks
from common.task_utils import run_task_hooks


class TaskHookTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.repo_root = Path(self.temporary_directory.name)
        self.task_json = self.repo_root / "task.json"
        self.task_json.write_text("{}", encoding="utf-8")

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_timeout_blocks_and_does_not_log_command_arguments(self) -> None:
        secret_argument = "fixture-secret-argument"
        hook = {
            "command": [sys.executable, "-c", "import time; time.sleep(2)", secret_argument],
            "timeout_seconds": 0.01,
            "failure_policy": "block",
        }

        with patch("common.config.get_hooks", return_value=[hook]):
            output = io.StringIO()
            with redirect_stderr(output):
                with self.assertRaisesRegex(RuntimeError, "timed out"):
                    run_task_hooks("after_start", self.task_json, self.repo_root)

        self.assertNotIn(secret_argument, output.getvalue())

    def test_warn_policy_reports_only_the_command_name(self) -> None:
        secret_argument = "fixture-secret-argument"
        hook = {
            "command": [sys.executable, "-c", "raise SystemExit(7)", secret_argument],
            "timeout_seconds": 1,
            "failure_policy": "warn",
        }

        with patch("common.config.get_hooks", return_value=[hook]):
            output = io.StringIO()
            with redirect_stderr(output):
                run_task_hooks("after_start", self.task_json, self.repo_root)

        self.assertIn("python", output.getvalue().lower())
        self.assertNotIn(secret_argument, output.getvalue())

    def test_successful_hook_completes_without_a_warning(self) -> None:
        hook = {
            "command": [sys.executable, "-c", "raise SystemExit(0)"],
            "timeout_seconds": 1,
            "failure_policy": "block",
        }

        with patch("common.config.get_hooks", return_value=[hook]):
            output = io.StringIO()
            with redirect_stderr(output):
                run_task_hooks("after_start", self.task_json, self.repo_root)

        self.assertEqual("", output.getvalue())

    def test_nonzero_exit_blocks_when_policy_is_block(self) -> None:
        hook = {
            "command": [sys.executable, "-c", "raise SystemExit(7)"],
            "timeout_seconds": 1,
            "failure_policy": "block",
        }

        with patch("common.config.get_hooks", return_value=[hook]):
            with self.assertRaisesRegex(RuntimeError, "exited with status 7"):
                run_task_hooks("after_start", self.task_json, self.repo_root)

    def test_ignore_policy_suppresses_failure_output(self) -> None:
        hook = {
            "command": [sys.executable, "-c", "raise SystemExit(7)"],
            "timeout_seconds": 1,
            "failure_policy": "ignore",
        }

        with patch("common.config.get_hooks", return_value=[hook]):
            output = io.StringIO()
            with redirect_stderr(output):
                run_task_hooks("after_start", self.task_json, self.repo_root)

        self.assertEqual("", output.getvalue())

    def test_timeout_terminates_the_child_process(self) -> None:
        child_pid_file = self.repo_root / "child.pid"
        child_code = (
            "import subprocess, sys, time; "
            "child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)']); "
            f"open({str(child_pid_file)!r}, 'w', encoding='utf-8').write(str(child.pid)); "
            "time.sleep(60)"
        )
        hook = {
            "command": [sys.executable, "-c", child_code],
            "timeout_seconds": 0.25,
            "failure_policy": "block",
        }

        with patch("common.config.get_hooks", return_value=[hook]):
            with self.assertRaisesRegex(RuntimeError, "timed out"):
                run_task_hooks("after_start", self.task_json, self.repo_root)

        child_pid = int(child_pid_file.read_text(encoding="utf-8"))
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            process_list = subprocess.run(
                ["tasklist", "/FI", f"PID eq {child_pid}", "/FO", "CSV", "/NH"],
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                check=False,
            )
            if str(child_pid) not in process_list.stdout:
                break
            time.sleep(0.05)
        else:
            self.fail(f"Hook child process {child_pid} is still alive after timeout")

    def test_config_normalizes_legacy_and_structured_hooks(self) -> None:
        config_dir = self.repo_root / ".trellis"
        config_dir.mkdir()
        (config_dir / "config.yaml").write_text(
            """hooks:
  default_timeout_seconds: 42
  default_failure_policy: ignore
  after_start:
    - \"echo legacy\"
    - command: \"echo structured\"
      timeout_seconds: 5
      failure_policy: block
  after_finish:
    - command: \"echo default structured\"
""",
            encoding="utf-8",
        )

        self.assertEqual(
            [
                {
                    "command": "echo legacy",
                    "timeout_seconds": 42.0,
                    "failure_policy": "warn",
                },
                {
                    "command": "echo structured",
                    "timeout_seconds": 5.0,
                    "failure_policy": "block",
                },
            ],
            get_hooks("after_start", self.repo_root),
        )
        self.assertEqual(
            [
                {
                    "command": "echo default structured",
                    "timeout_seconds": 42.0,
                    "failure_policy": "ignore",
                }
            ],
            get_hooks("after_finish", self.repo_root),
        )


if __name__ == "__main__":
    unittest.main()
