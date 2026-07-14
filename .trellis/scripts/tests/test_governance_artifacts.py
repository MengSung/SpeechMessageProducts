import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPTS_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS_DIR))

from governance_artifacts import build_fixture_report, check_index


class GovernanceArtifactTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.repo_root = Path(self.temporary_directory.name)
        subprocess.run(
            ["git", "init", "--quiet", str(self.repo_root)],
            check=True,
            capture_output=True,
        )

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_index_scan_rejects_staged_raw_runs_and_cache_paths(self) -> None:
        raw_run = self.repo_root / ".ccg" / "dual-model-runs" / "run" / "stdout.md"
        cache = self.repo_root / ".serena" / "cache" / "session.pkl"
        raw_run.parent.mkdir(parents=True)
        cache.parent.mkdir(parents=True)
        raw_run.write_text("raw model output", encoding="utf-8")
        cache.write_bytes(b"cache")
        subprocess.run(
            ["git", "-C", str(self.repo_root), "add", "."],
            check=True,
            capture_output=True,
        )

        result = check_index(self.repo_root)

        self.assertEqual(1, result.ccg_raw_paths)
        self.assertEqual(1, result.serena_cache_paths)
        self.assertFalse(result.is_clean)

    def test_index_scan_allows_untracked_local_artifacts_after_index_removal(self) -> None:
        raw_run = self.repo_root / ".ccg" / "dual-model-runs" / "run" / "stdout.md"
        cache = self.repo_root / ".serena" / "cache" / "session.pkl"
        raw_run.parent.mkdir(parents=True)
        cache.parent.mkdir(parents=True)
        raw_run.write_text("local model output", encoding="utf-8")
        cache.write_bytes(b"cache")

        self.assertTrue(check_index(self.repo_root).is_clean)
        self.assertTrue(raw_run.exists())
        self.assertTrue(cache.exists())

    def test_synthetic_bearer_fixture_is_redacted_from_durable_report(self) -> None:
        fake_bearer = "Bearer " + ("A" * 172)

        report = build_fixture_report(fake_bearer)
        durable_output = json.dumps(report, sort_keys=True)

        self.assertEqual(0, report["durable_token_matches"])
        self.assertNotIn(fake_bearer, durable_output)
        self.assertNotIn("A" * 172, durable_output)


if __name__ == "__main__":
    unittest.main()
