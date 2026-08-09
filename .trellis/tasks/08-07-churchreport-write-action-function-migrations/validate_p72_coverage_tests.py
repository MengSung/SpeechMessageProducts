"""Contract tests for the offline P7.2 coverage gate."""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import validate_p72_coverage


TASK_DIR = Path(__file__).resolve().parent
VALIDATOR = TASK_DIR / "validate_p72_coverage.py"


class P72CoverageValidatorTests(unittest.TestCase):
    def run_validator(self, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(VALIDATOR), *args],
            cwd=TASK_DIR,
            text=True,
            capture_output=True,
            check=False,
        )

    def test_current_matrix_is_deterministic_and_reports_only_pending_slice_c(self) -> None:
        first = self.run_validator()
        second = self.run_validator()

        self.assertEqual(2, first.returncode)
        self.assertEqual(first.stdout, second.stdout)
        report = json.loads(first.stdout)
        self.assertEqual("no-go", report["outcome"])
        self.assertEqual(7, report["summary"]["requiredSliceCount"])
        self.assertEqual(3, report["summary"]["completeSliceCount"])
        pending = {item["sliceId"] for item in report["slices"] if item["status"] == "pending"}
        self.assertEqual(
            {
                "list-membership-association",
                "small-group-fixed-fields",
                "contact-owner-assignment",
                "contact-list-transfer-composite",
            },
            pending,
        )

    def test_required_slice_without_lifecycle_owner_is_rejected(self) -> None:
        matrix = json.loads((TASK_DIR / "p7.2-fixture-activation-matrix.json").read_text(encoding="utf-8"))
        matrix["slices"][0].pop("lifecycleOwner")
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "matrix.json"
            path.write_text(json.dumps(matrix, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            result = self.run_validator("--matrix", str(path))
        report = json.loads(result.stdout)
        self.assertIn("P72-BOUNDARY", {item["ruleId"] for item in report["errors"]})

    def test_duplicate_required_operation_is_rejected(self) -> None:
        matrix = json.loads((TASK_DIR / "p7.2-fixture-activation-matrix.json").read_text(encoding="utf-8"))
        matrix["slices"][4]["operationIds"].append("list.members.add.many")
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "matrix.json"
            path.write_text(json.dumps(matrix, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            result = self.run_validator("--matrix", str(path))
        report = json.loads(result.stdout)
        self.assertIn("P72-OPERATION-DUPLICATE", {item["ruleId"] for item in report["errors"]})

    def test_evidence_parser_rejects_feature_flag_changes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "evidence.json"
            path.write_text(
                json.dumps(
                    {
                        "schemaVersion": "p7.2.live-evidence.v1",
                        "operationId": "list.members.add.many",
                        "profileAlias": "sunnyvalechback",
                        "ceVersion": "9.1",
                        "connector": "Data8",
                        "sensitiveDataIncluded": False,
                        "preflight": {"outcome": "go"},
                        "execution": {
                            "outcome": "go",
                            "operationExecuted": True,
                            "featureFlagChanged": True,
                        },
                    }
                ),
                encoding="utf-8",
            )
            ok, reason = validate_p72_coverage.evidence_is_complete(path, {"list.members.add.many"})
        self.assertFalse(ok)
        self.assertEqual("evidence-feature-flag-changed", reason)

    def test_validator_rejects_change_race_leader_for_relationship_bound_live_fixture(self) -> None:
        """保護 Slice C 關係名單的實機語意。

        故障注入把 live fixture source 中的 ChangeAreaLeader 全部替換為舊的
        ChangeRaceLeader；coverage gate 必須回報固定規則，而不能只看 descriptor
        有 GUID 就把 area leader/name relationship 視為已參與 mutation。決定性
        assertion 是 no-go report 含有該規則，整個測試僅讀取工作區文字，不建立
        CRM、Data8、credential、process 或跨測試可變狀態。
        """
        live_fixture_path = validate_p72_coverage.path_for(
            "ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementEvidenceTests.cs"
        )
        original_read_text = validate_p72_coverage.read_text

        def read_text_with_race_leader(path: Path) -> str:
            text = original_read_text(path)
            if path == live_fixture_path:
                return text.replace(
                    "SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader",
                    "SmallGroupFixedFieldsUpdateMode.ChangeRaceLeader",
                )
            return text

        with mock.patch.object(validate_p72_coverage, "read_text", side_effect=read_text_with_race_leader):
            report = validate_p72_coverage.validate(
                TASK_DIR / "p7.2-fixture-activation-matrix.json"
            )

        self.assertIn(
            "P72-SMALL-GROUP-RELATIONSHIP-MODE",
            {item["ruleId"] for item in report["errors"]},
        )

    def test_current_live_fixture_uses_area_leader_for_descriptor_relationship(self) -> None:
        """保護已核准的 Slice C 實機語意不退回 race-only mutation。

        此回歸以目前 version-controlled live fixture source 為輸入；若 preflight 或 dispatch
        任一處改回 ChangeRaceLeader，coverage report 會出現固定 relationship-mode rule，這個
        assertion 隨即失敗。它只執行 deterministic offline validator，不取得憑證、不接觸 CE，
        也不開啟 feature flag 或 background resource。
        """
        report = validate_p72_coverage.validate(
            TASK_DIR / "p7.2-fixture-activation-matrix.json"
        )

        self.assertNotIn(
            "P72-SMALL-GROUP-RELATIONSHIP-MODE",
            {item["ruleId"] for item in report["errors"]},
        )


if __name__ == "__main__":
    unittest.main()
