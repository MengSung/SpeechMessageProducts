"""驗證 P7.0 coverage validator 的離線、確定性與 P7.5 依賴門檻契約。"""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


TASK_DIRECTORY = Path(__file__).resolve().parent
VALIDATOR = TASK_DIRECTORY / "validate_coverage.py"


class CoverageValidatorContractTests(unittest.TestCase):
    """保護 P7.0 盤點不讀取 CE，且不同階段使用明確 release gate 的契約。"""

    def run_validator(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        """以獨立子程序執行尚未實作的 validator，避免測試意外共享可變 module 狀態。"""
        return subprocess.run(
            [sys.executable, str(VALIDATOR), *arguments],
            cwd=TASK_DIRECTORY,
            text=True,
            capture_output=True,
            check=False,
        )

    def test_complete_matrix_produces_a_deterministic_green_p7_0_report(self) -> None:
        """完整 70-row matrix 在 P7.0 預設 gate 必須產生固定且可機器解析的 green report。"""
        first = self.run_validator()
        second = self.run_validator()

        self.assertEqual(0, first.returncode, first.stderr)
        self.assertEqual(first.stdout, second.stdout)
        report = json.loads(first.stdout)
        self.assertEqual("valid", report["outcome"])
        self.assertEqual(70, report["summary"]["callSiteCount"])
        self.assertEqual([], report["errors"])

    def test_duplicate_call_site_id_is_rejected_without_reading_external_state(self) -> None:
        """兩筆 row 共用 call-site ID 時必須在純本機資料上 fail closed，不能接觸 CE 或設定祕密。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_path = Path(temporary_directory)
            matrix_path = temporary_path / "coverage-matrix.json"
            shutil.copyfile(TASK_DIRECTORY / "coverage-matrix.json", matrix_path)
            matrix = json.loads(matrix_path.read_text(encoding="utf-8"))
            matrix["callSites"][1]["callSiteId"] = matrix["callSites"][0]["callSiteId"]
            matrix_path.write_text(
                json.dumps(matrix, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
                newline="",
            )

            result = self.run_validator("--matrix", str(matrix_path))

        self.assertNotEqual(0, result.returncode)
        report = json.loads(result.stdout)
        self.assertIn("P70-CALLSITE-DUPLICATE", [error["ruleId"] for error in report["errors"]])

    def test_p7_5_reference_scan_is_advisory_until_explicit_release_enforcement(self) -> None:
        """現存 legacy 依賴在 P7.0 僅供追蹤，但 P7.5 enforcement 必須轉成阻斷錯誤。"""
        baseline = self.run_validator()
        release_gate = self.run_validator("--enforce-p7-5")

        self.assertEqual(0, baseline.returncode, baseline.stderr)
        self.assertNotEqual(0, release_gate.returncode)
        release_report = json.loads(release_gate.stdout)
        self.assertIn(
            "P75-PRODUCTION-LEGACY-DEPENDENCY",
            [error["ruleId"] for error in release_report["errors"]],
        )

    def test_source_manifest_keeps_worker_protocol_allowlist_distinct_from_registry(self) -> None:
        """Worker protocol 只允許兩個 identity 與一個 date-range fee operation，不能因來源註解而誤列 Registry 項目。"""
        result = self.run_validator("--build")

        self.assertEqual(0, result.returncode, result.stderr)
        manifests = json.loads((TASK_DIRECTORY / "source-manifests.json").read_text(encoding="utf-8"))
        self.assertEqual(["runtime.health.whoami"], manifests["data8Executor"]["operationIds"])
        self.assertEqual(
            [
                "fee.dedication.retrieve.by.contact.date.range",
                "runtime.health.whoami",
                "runtime.pool.validate.connection",
            ],
            manifests["officialWorkerProtocol"]["operationIds"],
        )

    def test_generic_entity_capability_is_rejected(self) -> None:
        """任何企圖把 generic entity CRUD 當成遠端 capability 的 row 必須在 P7.0 離線 gate 被拒絕。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            matrix_path = Path(temporary_directory) / "coverage-matrix.json"
            shutil.copyfile(TASK_DIRECTORY / "coverage-matrix.json", matrix_path)
            matrix = json.loads(matrix_path.read_text(encoding="utf-8"))
            matrix["callSites"][0]["operation"]["id"] = "entity.retrieve"
            matrix_path.write_text(
                json.dumps(matrix, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
                newline="",
            )

            result = self.run_validator("--matrix", str(matrix_path))

        self.assertNotEqual(0, result.returncode)
        report = json.loads(result.stdout)
        self.assertIn("P70-GENERIC-CAPABILITY", [error["ruleId"] for error in report["errors"]])

    def test_p7_2_input_includes_non_platform_function_candidates(self) -> None:
        """P7.2 input 必須涵蓋 matrix 的 write、action、function；只有 platform gate 可被排除。"""
        result = self.run_validator("--build")

        self.assertEqual(0, result.returncode, result.stderr)
        activation_input = json.loads((TASK_DIRECTORY / "p7.2-activation-input.json").read_text(encoding="utf-8"))
        candidate_ids = {candidate["callSiteId"] for candidate in activation_input["pendingCandidates"]}
        self.assertIn("ORG-CALL-00024", candidate_ids)
        self.assertNotIn("ORG-CALL-00003", candidate_ids)


if __name__ == "__main__":
    unittest.main()
