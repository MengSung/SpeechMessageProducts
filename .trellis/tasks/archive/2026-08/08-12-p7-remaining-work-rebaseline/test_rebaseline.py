"""保護 P7 尚餘能力重新基準化工具的離線、去識別化與 fail-closed 契約。"""

from __future__ import annotations

import json
import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


TASK_DIRECTORY = Path(__file__).resolve().parent
ANALYZER = TASK_DIRECTORY / "build_rebaseline.py"


def load_analyzer_module():
    """以獨立 module instance 載入 analyzer，讓 comment-only evidence 測試不保留跨案例的可變掃描狀態。"""
    specification = importlib.util.spec_from_file_location("p7_rebaseline_analyzer_test", ANALYZER)
    assert specification is not None and specification.loader is not None
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


class P7RebaselineContractTests(unittest.TestCase):
    """驗證 matrix 絕不把靜態存在或 local-only 狀態誤宣稱為完成遷移或 CE 成功。"""

    def run_analyzer(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        """在獨立程序執行純本機 analyzer，避免測試留存跨案例可變 matrix 或檔案 handle。"""
        return subprocess.run(
            [sys.executable, str(ANALYZER), *arguments],
            cwd=TASK_DIRECTORY,
            text=True,
            capture_output=True,
            check=False,
        )

    def test_build_writes_exactly_the_immutable_seventy_call_site_snapshot(self) -> None:
        """來源 baseline 的 70 筆 call site 必須恰好一次出現在輸出，防止掉列或重複列造成假性完成。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "authoritative-gap-matrix.json"
            result = self.run_analyzer("--output", str(output))

            self.assertEqual(0, result.returncode, result.stderr)
            matrix = json.loads(output.read_text(encoding="utf-8"))

        self.assertEqual(70, len(matrix["callSites"]))
        call_site_ids = [row["callSiteId"] for row in matrix["callSites"]]
        self.assertEqual(70, len(set(call_site_ids)))
        self.assertEqual(sorted(call_site_ids), call_site_ids)

    def test_build_records_the_current_canonical_phase0_checksum(self) -> None:
        """輸出必須綁定目前可解析的 phase0 原始 70-row 基準，不能只重送封存 coverage 文件內的舊 hash 文字。"""
        analyzer = load_analyzer_module()
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "authoritative-gap-matrix.json"
            result = self.run_analyzer("--output", str(output))
            self.assertEqual(0, result.returncode, result.stderr)
            matrix = json.loads(output.read_text(encoding="utf-8"))

        self.assertEqual(70, matrix["sourceMatrix"]["callSiteCount"])
        self.assertEqual(
            analyzer.sha256(analyzer.P70_MATRIX),
            matrix["sourceMatrix"]["sha256"],
        )

    def test_generated_artifact_is_utf8_without_bom_and_uses_crlf(self) -> None:
        """產出必須符合 repository 的 UTF-8 無 BOM、全 CRLF 與 final CRLF 合約，避免 Windows task artifact 被混合行尾污染。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "authoritative-gap-matrix.json"
            result = self.run_analyzer("--output", str(output))
            self.assertEqual(0, result.returncode, result.stderr)
            content = output.read_bytes()

        self.assertFalse(content.startswith(b"\xef\xbb\xbf"))
        self.assertTrue(content.endswith(b"\r\n"))
        self.assertNotIn(b"\n", content.replace(b"\r\n", b""))

    def test_comment_or_literal_only_operation_reference_is_not_static_implementation_evidence(self) -> None:
        """註解與字串中的 OperationIds 不是 executor/consumer 實作；掃描器必須忽略它們，避免文件文字誤升格 migration。"""
        analyzer = load_analyzer_module()
        known_ids = {"fee.dedication.retrieve.by.contact"}
        with tempfile.TemporaryDirectory() as temporary_directory:
            source = Path(temporary_directory) / "CommentOnly.cs"
            source.write_text(
                "// OperationIds.FeeDedicationRetrieveByContact\n"
                "/* OperationIds.FeeDedicationRetrieveByContact */\n"
                "var text = \"OperationIds.FeeDedicationRetrieveByContact\";\n",
                encoding="utf-8",
            )

            evidence = analyzer.referenced_operation_ids(source, known_ids)

        self.assertEqual(set(), evidence)

    def test_gated_package01_consumer_is_not_reported_as_dedicated_success(self) -> None:
        """已接上 Package01 typed client、但旗標預設關閉的費用讀取，不能被升格為 Dedicated 或 CE 8.2 成功。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "authoritative-gap-matrix.json"
            result = self.run_analyzer("--output", str(output))
            self.assertEqual(0, result.returncode, result.stderr)
            matrix = json.loads(output.read_text(encoding="utf-8"))

        row = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00006")
        self.assertEqual("declared", row["registry"]["status"])
        self.assertEqual("implemented", row["data8Executor"]["status"])
        self.assertEqual("implemented", row["productClient"]["status"])
        self.assertEqual("migrated-disabled", row["consumer"]["status"])
        self.assertEqual("succeeded", row["ceEvidence"]["ce91"])
        self.assertEqual("evidence-pending", row["ceEvidence"]["ce82"])
        self.assertEqual("succeeded", row["hostEvidence"]["embedded"])
        self.assertEqual("evidence-pending", row["hostEvidence"]["dedicated"])

    def test_client_only_package01_operation_is_not_reported_as_a_consumer_migration(self) -> None:
        """僅有 registry、executor 與 typed client 的能力，若 ChurchReport 沒有精確呼叫點，必須保留 not-migrated。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "authoritative-gap-matrix.json"
            result = self.run_analyzer("--output", str(output))
            self.assertEqual(0, result.returncode, result.stderr)
            matrix = json.loads(output.read_text(encoding="utf-8"))

        row = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00005")
        self.assertEqual("implemented", row["productClient"]["status"])
        self.assertEqual("not-migrated", row["consumer"]["status"])

    def test_multiline_package02_operation_constant_is_detected_across_all_implementation_layers(self) -> None:
        """多行 C# const 宣告不得讓已實作的 Package02 operation 假性降級為未宣告或未實作。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "authoritative-gap-matrix.json"
            result = self.run_analyzer("--output", str(output))
            self.assertEqual(0, result.returncode, result.stderr)
            matrix = json.loads(output.read_text(encoding="utf-8"))

        row = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00024")
        self.assertEqual("declared", row["registry"]["status"])
        self.assertEqual("implemented", row["data8Executor"]["status"])
        self.assertEqual("implemented", row["productClient"]["status"])
        self.assertEqual("not-migrated", row["consumer"]["status"])

    def test_local_only_slice_h_cannot_be_promoted_to_executor_consumer_or_ce_evidence(self) -> None:
        """Slice H reducer/plan 的本機綠燈必須維持 local-only，避免未執行的出席寫入進入切流或 removal gate。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "authoritative-gap-matrix.json"
            result = self.run_analyzer("--output", str(output))
            self.assertEqual(0, result.returncode, result.stderr)
            matrix = json.loads(output.read_text(encoding="utf-8"))

        row = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00068")
        self.assertEqual("local-only", row["registry"]["status"])
        self.assertEqual("local-only-rejected", row["data8Executor"]["status"])
        self.assertEqual("not-implemented", row["productClient"]["status"])
        self.assertEqual("not-migrated", row["consumer"]["status"])
        self.assertEqual("not-executed", row["ceEvidence"]["ce91"])
        self.assertNotEqual("none", row["p75RemovalBlocker"])

    def test_slice_c_no_go_closed_operation_cannot_be_reclassified_as_pending_evidence(self) -> None:
        """保護已清理的 Slice C CE 9.1 no-go：歷史寫入家族不得因靜態 executor 存在而被當成可重試的 pending 證據。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "authoritative-gap-matrix.json"
            result = self.run_analyzer("--output", str(output))
            self.assertEqual(0, result.returncode, result.stderr)
            matrix = json.loads(output.read_text(encoding="utf-8"))

        row = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00035")
        self.assertEqual("listmanagement.smallgroup.update.fields", row["operation"]["id"])
        self.assertEqual("no-go-closed", row["ceEvidence"]["ce91"])
        self.assertNotEqual("none", row["p75RemovalBlocker"])

    def test_validator_rejects_a_slice_c_no_go_row_claiming_pending_retry(self) -> None:
        """注入將已關閉 Slice C operation 改為 pending 的錯誤矩陣，驗證器必須 fail closed，避免後續工作誤啟動舊寫入家族。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            matrix_path = Path(temporary_directory) / "matrix.json"
            build_result = self.run_analyzer("--output", str(matrix_path))
            self.assertEqual(0, build_result.returncode, build_result.stderr)
            matrix = json.loads(matrix_path.read_text(encoding="utf-8"))
            row = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00035")
            row["ceEvidence"]["ce91"] = "evidence-pending"
            matrix_path.write_text(json.dumps(matrix, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

            result = self.run_analyzer("--validate", str(matrix_path))

        self.assertNotEqual(0, result.returncode)
        report = json.loads(result.stdout)
        self.assertIn("P7R-SLICE-C-NO-GO-CLOSED", [error["ruleId"] for error in report["errors"]])

    def test_validator_rejects_a_local_only_row_claiming_ce_success(self) -> None:
        """故障注入把 local-only row 偽造成 CE 成功；validator 必須純離線 fail closed 並產生固定 rule ID。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            matrix_path = Path(temporary_directory) / "matrix.json"
            build_result = self.run_analyzer("--output", str(matrix_path))
            self.assertEqual(0, build_result.returncode, build_result.stderr)
            matrix = json.loads(matrix_path.read_text(encoding="utf-8"))
            row = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00068")
            row["ceEvidence"]["ce91"] = "succeeded"
            matrix_path.write_text(json.dumps(matrix, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

            result = self.run_analyzer("--validate", str(matrix_path))

        self.assertNotEqual(0, result.returncode)
        report = json.loads(result.stdout)
        self.assertIn("P7R-LOCAL-ONLY-NO-CE", [error["ruleId"] for error in report["errors"]])

    def test_validator_rejects_a_disabled_consumer_claiming_enabled_cutover(self) -> None:
        """故障注入將 Package01 disabled gate 標成 enabled；validator 必須阻止沒有 rollout evidence 的假性 cutover。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            matrix_path = Path(temporary_directory) / "matrix.json"
            build_result = self.run_analyzer("--output", str(matrix_path))
            self.assertEqual(0, build_result.returncode, build_result.stderr)
            matrix = json.loads(matrix_path.read_text(encoding="utf-8"))
            row = next(row for row in matrix["callSites"] if row["callSiteId"] == "ORG-CALL-00006")
            row["consumer"]["status"] = "migrated-enabled"
            matrix_path.write_text(json.dumps(matrix, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

            result = self.run_analyzer("--validate", str(matrix_path))

        self.assertNotEqual(0, result.returncode)
        report = json.loads(result.stdout)
        self.assertIn("P7R-CONSUMER-GATE", [error["ruleId"] for error in report["errors"]])

    def test_output_never_contains_disallowed_deployment_or_identity_field_names(self) -> None:
        """輸出只保留去識別化分類；CRM ID、帳號、endpoint、secret/token/cookie 與原始 exception 欄位一律禁止。"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "authoritative-gap-matrix.json"
            result = self.run_analyzer("--output", str(output))
            self.assertEqual(0, result.returncode, result.stderr)
            rendered = output.read_text(encoding="utf-8").lower()

        for forbidden in ("organizationid", "endpoint", "credential", "password", "token", "cookie", "exception", "ownerid"):
            self.assertNotIn(forbidden, rendered)


if __name__ == "__main__":
    unittest.main()
