"""保護 P7.5 前置證據 scanner 的離線、去識別化與 fail-closed 契約。"""

from __future__ import annotations

import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


TASK_DIRECTORY = Path(__file__).resolve().parent
ANALYZER = TASK_DIRECTORY / "build_p75_prerequisite_evidence.py"


def load_analyzer_module():
    """以新 module instance 載入 analyzer，確保測試不依賴跨案例 scanner cache 或 retained state。"""
    specification = importlib.util.spec_from_file_location("p75_prerequisite_evidence_test", ANALYZER)
    assert specification is not None and specification.loader is not None
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


class P75PrerequisiteEvidenceContractTests(unittest.TestCase):
    """驗證 static scanner 不會把文件文字或未完成 matrix 偽裝成 P7.5 ready。"""

    def test_comment_literal_and_character_only_legacy_tokens_are_not_source_references(self) -> None:
        """註解、XML 文件、一般/逐字/插值字串與字元 literal 中的 token 必須被忽略，避免 false no-go。"""
        analyzer = load_analyzer_module()
        source = """
// ToolUtilityClass IOrganizationService
/* Microsoft.Xrm.Sdk.Entity */
/// ToolUtilityFactory
var one = \"ToolUtilityClass\";
var two = @\"IOrganizationService\";
var three = $\"Microsoft.Xrm.Sdk\";
var marker = 'x';
"""

        result = analyzer.scan_csharp_source(source)

        self.assertEqual({}, result)

    def test_preprocessor_directive_is_noncode_even_when_its_label_contains_quotes(self) -> None:
        """#region/#pragma 的整行標籤屬於編譯前指示詞，內含引號或 legacy token 皆不可被當成 C# code。"""
        analyzer = load_analyzer_module()
        source = (
            '#region "ToolUtilityClass\\n'
            '#pragma warning disable CS0618 // IOrganizationService\\n'
            'var safe = 1;\\n'
            '#endregion\\n'
        )

        result = analyzer.scan_csharp_source(source)

        self.assertEqual({}, result)

    def test_active_source_token_is_classified_without_emitting_source_context(self) -> None:
        """實際 type/namespace 使用必須落入固定分類，而輸入字串不會出現在 scanner result。"""
        analyzer = load_analyzer_module()
        source = "using Microsoft.Xrm.Sdk;\nIOrganizationService service;\nToolUtilityClass utility;\n"

        result = analyzer.scan_csharp_source(source)

        self.assertEqual(1, result["xrm-sdk-namespace"])
        self.assertEqual(1, result["organization-service"])
        self.assertEqual(1, result["toolutility-type"])
        self.assertNotIn("source", json.dumps(result, ensure_ascii=False).lower())

    def test_raw_string_input_fails_closed_before_a_zero_reference_claim(self) -> None:
        """未支援 C# raw string 時必須拒絕輸入，不能把其中 legacy token 漏掃而宣稱零參照。"""
        analyzer = load_analyzer_module()

        with self.assertRaises(analyzer.ScannerInputError) as captured:
            analyzer.scan_csharp_source('var marker = """ToolUtilityClass""";')

        self.assertEqual("raw-string-literal", str(captured.exception))

    def test_invalid_utf8_is_rejected_without_replacement_decoding(self) -> None:
        """讀取 production candidate 的位元組不是有效 UTF-8 時必須 fail closed，避免 replacement 字元造成漏掃。"""
        analyzer = load_analyzer_module()
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = Path(temporary_directory) / "Invalid.cs"
            path.write_bytes(b"\xff\xfe\x00")

            with self.assertRaises(analyzer.ScannerInputError) as captured:
                analyzer.read_utf8_source(path)

        self.assertEqual("invalid-utf8", str(captured.exception))

    def test_excluded_and_escape_paths_are_never_accepted_as_production_source(self) -> None:
        """Logs/docs/bin/obj 與 root 外候選都必須排除，避免 log value 或外部檔案進入 P7.5 判定。"""
        analyzer = load_analyzer_module()
        root = Path("C:/p75-root")

        self.assertTrue(analyzer.is_excluded_relative_path(Path("Logs/legacy.cs")))
        self.assertTrue(analyzer.is_excluded_relative_path(Path("obj/generated.cs")))
        self.assertTrue(analyzer.is_excluded_relative_path(Path("wwwroot/script.cs")))
        self.assertTrue(analyzer.is_excluded_relative_path(Path("文件/reference.cs")))
        with self.assertRaises(analyzer.ScannerInputError) as captured:
            analyzer.require_path_within_root(root, Path("C:/outside/legacy.cs"))

        self.assertEqual("path-escape", str(captured.exception))

    def test_settings_scanner_counts_keys_without_observing_or_publishing_values(self) -> None:
        """settings 掃描只可分類 key name；credential-like value 不得出現在任何 scan result。"""
        analyzer = load_analyzer_module()
        document = {"DynamicsAccess": {"CrmConnection": "do-not-publish"}}

        result = analyzer.scan_settings_key_names(document)

        rendered = json.dumps(result, ensure_ascii=False)
        self.assertEqual({"legacy-settings-key": 1}, result)
        self.assertNotIn("do-not-publish", rendered)

    def test_json_with_comments_settings_scanner_reads_keys_without_publishing_values(self) -> None:
        """既有 appsettings 的註解是 metadata 格式的一部分；parser 只可遮罩註解後讀取 key，不能洩漏 value。"""
        analyzer = load_analyzer_module()
        source = '''{
            // deployment note: value must remain unread
            "DynamicsAccess": {
                "CrmConnection": "do-not-publish"
            }
        }'''

        result = analyzer.parse_settings_key_document(source)

        rendered = json.dumps(result, ensure_ascii=False)
        self.assertEqual({"legacy-settings-key": 1}, result)
        self.assertNotIn("do-not-publish", rendered)

    def test_key_only_jsonc_scanner_traverses_nested_values_without_materializing_them(self) -> None:
        """JSONC scanner 只可解碼 object key；巢狀 object/array/string value 必須只被結構跳過，且輸出不得含其內容。"""
        analyzer = load_analyzer_module()
        source = '''{
            "Safe": ["https://do-not-publish", {"CrmConnection": "do-not-publish"}],
            "Nested": {"NotLegacy": true}
        }'''

        result = analyzer.scan_settings_key_names_from_jsonc(source)

        rendered = json.dumps(result, ensure_ascii=False)
        self.assertEqual({"legacy-settings-key": 1}, result)
        self.assertNotIn("do-not-publish", rendered)

    def test_key_only_jsonc_scanner_rejects_invalid_value_escape(self) -> None:
        """value 雖不解碼仍須驗證 JSON escape；不合法語法不可產生任何部分 key-only evidence。"""
        analyzer = load_analyzer_module()

        with self.assertRaises(analyzer.ScannerInputError) as captured:
            analyzer.scan_settings_key_names_from_jsonc('{"Safe": "bad\\q"}')

        self.assertEqual("settings-json-invalid", str(captured.exception))

    def test_every_allowlisted_production_csharp_file_has_a_supported_lexical_shape(self) -> None:
        """目前 production source 的每個允許 C# 檔都必須可完整 lexical scan；未知形狀不得被忽略或部分掃描。"""
        analyzer = load_analyzer_module()

        for path in analyzer.iter_production_csharp_files():
            with self.subTest(path=path.relative_to(analyzer.PRODUCTION_ROOT).as_posix()):
                analyzer.scan_csharp_source(analyzer.read_utf8_source(path))

    def test_current_report_is_deterministic_sanitized_and_no_go(self) -> None:
        """現行 repository 必須產生固定 no-go report；不得把 temporary legacy/reference/evidence 缺口誤報 ready。"""
        analyzer = load_analyzer_module()

        first = analyzer.build_report()
        second = analyzer.build_report()
        validation = analyzer.validate_report(first)
        gate = analyzer.evaluate_p75_gate(first)
        rendered = json.dumps(first, ensure_ascii=False, sort_keys=True).lower()

        self.assertEqual(first, second)
        self.assertEqual("valid", validation["outcome"])
        self.assertEqual("no-go", first["readiness"]["state"])
        self.assertEqual("no-go", gate["outcome"])
        self.assertGreater(first["matrix"]["temporaryLegacyCount"], 0)
        self.assertGreater(sum(item["occurrenceCount"] for item in first["productionLegacyReferences"]), 0)
        for forbidden in ("endpoint", "credential", "password", "token", "cookie", "exception", "ownerid", "toolutility.csproj"):
            self.assertNotIn(forbidden, rendered)

    def test_project_settings_and_non_none_matrix_blockers_each_prevent_prerequisite_ready(self) -> None:
        """即使 source 零參照，project/settings 依賴或任一 matrix blocker 仍須獨立阻止 P7.5 prerequisite-ready。"""
        analyzer = load_analyzer_module()
        matrix = {
            "ceOrHostEvidencePendingCount": 0,
            "closedHistoricalWriteFamilyCount": 0,
            "consumerNotMigratedCount": 0,
            "p75BlockerCounts": [{"category": "mixed", "count": 1}],
            "temporaryLegacyCount": 0,
        }

        reasons = analyzer.no_go_reasons(
            matrix,
            [],
            [{"category": "toolutility-project-reference", "count": 1}],
            [{"category": "legacy-settings-key", "count": 1}],
        )

        self.assertEqual(
            ["matrix-p75-blocker", "project-legacy-dependency", "legacy-settings-key"],
            reasons,
        )

    def test_empty_static_no_go_reasons_only_yield_prerequisite_ready(self) -> None:
        """所有靜態前置條件通過僅代表 prerequisite-ready，絕不可被表述成 ToolUtility removal 或 P8 ready。"""
        analyzer = load_analyzer_module()

        state = analyzer.prerequisite_readiness_state([])

        self.assertEqual("prerequisite-ready", state)
        self.assertNotEqual("ready", state)

    def test_report_tamper_cannot_produce_a_ready_p75_gate(self) -> None:
        """竄改 matrix count 或 readiness 後 strict validator 必須拒絕 report，避免 task artifact 被誤用為 removal 授權。"""
        analyzer = load_analyzer_module()
        report = analyzer.build_report()
        tampered = copy.deepcopy(report)
        tampered["matrix"]["temporaryLegacyCount"] = 0
        tampered["readiness"]["state"] = "prerequisite-ready"

        validation = analyzer.validate_report(tampered)
        gate = analyzer.evaluate_p75_gate(tampered)

        self.assertEqual("invalid", validation["outcome"])
        self.assertEqual("invalid-report", gate["outcome"])
        self.assertIn("P75-REPORT-EXPECTED", [error["ruleId"] for error in validation["errors"]])


if __name__ == "__main__":
    unittest.main()
