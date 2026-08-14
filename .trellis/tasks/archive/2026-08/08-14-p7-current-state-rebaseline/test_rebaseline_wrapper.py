"""驗證 P7 離線 rebaseline wrapper 的範圍、固定分類與 fail-closed 行為。"""

from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
from pathlib import Path


TASK_ROOT = Path(__file__).resolve().parent
WRAPPER = TASK_ROOT / "Invoke-OfflineRebaseline.ps1"
REPOSITORY_ROOT = TASK_ROOT.parents[2]


class OfflineRebaselineWrapperTests(unittest.TestCase):
    """保護 matrix 產生不接觸 CE，且不會把歷史或 local-only 狀態升格。"""

    def run_wrapper(self, mode: str, matrix_path: Path) -> subprocess.CompletedProcess[str]:
        """在獨立 PowerShell process 執行 wrapper，避免測試共享 process state。"""
        return subprocess.run(
            [
                "powershell.exe",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(WRAPPER),
                "-Mode",
                mode,
                "-MatrixPath",
                str(matrix_path),
            ],
            cwd=TASK_ROOT,
            text=True,
            encoding="utf-8",
            errors="strict",
            capture_output=True,
            check=False,
        )

    def test_build_and_validate_produce_seventy_bounded_rows(self) -> None:
        """建立與驗證必須成功，且只產生 immutable 70-row 分類快照。"""
        with tempfile.TemporaryDirectory(dir=TASK_ROOT) as temporary_directory:
            matrix_path = Path(temporary_directory) / "matrix.json"
            result = self.run_wrapper("Build", matrix_path)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(0, self.run_wrapper("Validate", matrix_path).returncode)
            matrix = json.loads(matrix_path.read_text(encoding="utf-8"))

        self.assertEqual(70, len(matrix["callSites"]))
        self.assertEqual(
            70,
            len({row["callSiteId"] for row in matrix["callSites"]}),
        )

    def test_historical_slice_c_and_local_only_rows_remain_fail_closed(self) -> None:
        """Slice C no-go 與 local-only row 不得因目前靜態 source 而變成可重試或 CE 成功。"""
        with tempfile.TemporaryDirectory(dir=TASK_ROOT) as temporary_directory:
            matrix_path = Path(temporary_directory) / "matrix.json"
            result = self.run_wrapper("Build", matrix_path)
            self.assertEqual(0, result.returncode, result.stderr)
            matrix = json.loads(matrix_path.read_text(encoding="utf-8"))

        slice_c = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00035")
        self.assertEqual("no-go-closed", slice_c["ceEvidence"]["ce91"])
        local_only = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00068")
        self.assertEqual("local-only", local_only["registry"]["status"])
        self.assertEqual("not-executed", local_only["ceEvidence"]["ce91"])
        self.assertEqual("not-migrated", local_only["consumer"]["status"])

    def test_build_writes_a_summary_derived_from_the_matrix(self) -> None:
        """摘要必須由同次 matrix 重新計算，避免手填計數掩蓋 P7.5 gate 狀態。"""
        with tempfile.TemporaryDirectory(dir=TASK_ROOT) as temporary_directory:
            matrix_path = Path(temporary_directory) / "matrix.json"
            summary_path = matrix_path.with_name("matrix-summary.json")
            result = self.run_wrapper("Build", matrix_path)
            self.assertEqual(0, result.returncode, result.stderr)
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(70, summary["callSiteCount"])
        self.assertEqual(67, summary["consumer"]["not-migrated"])
        self.assertEqual(70, summary["temporaryLegacy"]["temporary-legacy"])
        self.assertEqual(1, summary["ce91"]["no-go-closed"])

    def test_wrapper_rejects_output_outside_task_scope(self) -> None:
        """輸出路徑越界必須在 analyzer 啟動前拒絕，避免寫入共享或正式資料。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            outside_path = Path(temporary_directory) / "outside.json"
            result = self.run_wrapper("Build", outside_path)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("task-owned", result.stderr.lower())

    def test_validator_rejects_tampered_slice_c_disposition(self) -> None:
        """故障注入把 historical no-go 改成 pending 時，validator 必須 fail closed。"""
        with tempfile.TemporaryDirectory(dir=TASK_ROOT) as temporary_directory:
            matrix_path = Path(temporary_directory) / "matrix.json"
            result = self.run_wrapper("Build", matrix_path)
            self.assertEqual(0, result.returncode, result.stderr)
            matrix = json.loads(matrix_path.read_text(encoding="utf-8"))
            row = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00035")
            row["ceEvidence"]["ce91"] = "evidence-pending"
            matrix_path.write_text(json.dumps(matrix, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            invalid_result = self.run_wrapper("Validate", matrix_path)

        self.assertNotEqual(0, invalid_result.returncode)
        self.assertIn("P7R-SLICE-C-NO-GO-CLOSED", invalid_result.stdout)

    def test_relative_file_invocation_uses_the_task_owned_default_matrix(self) -> None:
        """從 repository root 以相對 script path 執行時，預設 matrix 仍必須限於 task-owned 目錄。"""
        result = subprocess.run(
            [
                "powershell.exe",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                r".\.trellis\tasks\08-14-p7-current-state-rebaseline\Invoke-OfflineRebaseline.ps1",
                "-Mode",
                "Validate",
            ],
            cwd=REPOSITORY_ROOT,
            text=True,
            encoding="utf-8",
            errors="strict",
            capture_output=True,
            check=False,
        )

        self.assertEqual(0, result.returncode, result.stderr)


if __name__ == "__main__":
    unittest.main()
