"""建立 P7.5 前置證據的純離線、去識別化與 fail-closed 報告。

此工具只讀取固定 ChurchReport production source 與 immutable P7 matrix。它不接受外部掃描根目錄、
endpoint、profile、credential、connector 或 CE 參數；沒有網路、subprocess、環境秘密或 runtime I/O。
任何 lexical/encoding/path 不確定性都停止報告，而不是產生可能錯誤的 zero-reference 結論。
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as element_tree
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any


TASK_DIRECTORY = Path(__file__).resolve().parent
REPOSITORY_ROOT = next(parent for parent in TASK_DIRECTORY.parents if (parent / ".trellis").is_dir())
PRODUCTION_ROOT = REPOSITORY_ROOT / "SpeechMessageProducts.ChurchReport"
PROJECT_FILE = PRODUCTION_ROOT / "SpeechMessageProducts.ChurchReport.csproj"
MATRIX_FILE = (
    REPOSITORY_ROOT
    / ".trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json"
)
SETTINGS_FILES = tuple(sorted(PRODUCTION_ROOT.glob("appsettings*.json"), key=lambda path: path.name.casefold()))
MAX_SOURCE_BYTES = 4 * 1024 * 1024
EXCLUDED_COMPONENTS = frozenset({".git", "bin", "docs", "logs", "node_modules", "obj", "tests", "wwwroot", "文件"})
SCHEMA_VERSION = "p7.5.prerequisite-evidence.v1"

LEGACY_TOKEN_PATTERNS: tuple[tuple[str, re.Pattern[str]], ...] = (
    ("toolutility-type", re.compile(r"\b(?:IToolUtilityProvider|ToolUtilityClass|ToolUtilityFactory|ToolUtilityFacade)\b")),
    ("toolutility-namespace", re.compile(r"\bToolUtilityNameSpace\b")),
    ("xrm-sdk-namespace", re.compile(r"\bMicrosoft\.(?:Xrm|Crm)\.Sdk(?:\b|\.)")),
    ("organization-service", re.compile(r"\bIOrganizationService\b")),
    (
        "sdk-model",
        re.compile(r"\b(?:AliasedValue|Entity(?:Collection|Reference)?|Organization(?:Request|Response)|QueryBase)\b"),
    ),
    ("dataverse-client-type", re.compile(r"\b(?:CrmServiceClient|ServiceClient)\b|\bMicrosoft\.PowerPlatform\.Dataverse\.Client\b")),
)
PROJECT_DEPENDENCY_MATCHERS = {
    "toolutility-project-reference": lambda element, include: element == "ProjectReference" and include.casefold().endswith("toolutility.csproj"),
    "crm-sdk-reference": lambda element, include: element == "Reference" and include.casefold().startswith("microsoft.crm.sdk"),
    "dataverse-client-package": lambda element, include: element == "PackageReference" and include.casefold() == "microsoft.powerplatform.dataverse.client",
}
LEGACY_SETTINGS_KEY_NAMES = frozenset(
    {
        "crmconnection",
        "crmconnectionstring",
        "crmdomain",
        "crmorganization",
        "crmpassword",
        "crmserviceurl",
        "crmusername",
    }
)
FORBIDDEN_OUTPUT_KEY_PARTS = frozenset(
    {"cookie", "credential", "endpoint", "exception", "ownerid", "password", "secret", "snippet", "sourcepath", "token"}
)


class ScannerInputError(ValueError):
    """表示離線 scanner 無法安全證明輸入可解析；訊息只允許固定分類，禁止承載來源內容。"""


def read_utf8_source(path: Path) -> str:
    """以 UTF-8/UTF-8 BOM 嚴格讀取有界 source；解碼問題不以 replacement 字元掩蓋。"""
    try:
        if path.stat().st_size > MAX_SOURCE_BYTES:
            raise ScannerInputError("source-size-limit")
        with path.open("r", encoding="utf-8-sig", newline="") as source_file:
            return source_file.read()
    except UnicodeDecodeError as error:
        raise ScannerInputError("invalid-utf8") from error
    except OSError as error:
        raise ScannerInputError("source-unreadable") from error


def require_path_within_root(root: Path, candidate: Path) -> Path:
    """確認 candidate 在固定 root 之內且沒有 symbolic link/reparse-point 跳脫掃描邊界。"""
    try:
        resolved_root = root.resolve(strict=False)
        resolved_candidate = candidate.resolve(strict=False)
        resolved_candidate.relative_to(resolved_root)
    except ValueError as error:
        raise ScannerInputError("path-escape") from error
    if candidate.is_symlink():
        raise ScannerInputError("symlink-input")
    return resolved_candidate


def is_excluded_relative_path(relative_path: Path) -> bool:
    """依固定、case-insensitive component allowlist 排除非 production source，避免 log/docs 成為 evidence。"""
    return any(component.casefold() in EXCLUDED_COMPONENTS for component in relative_path.parts)


def iter_production_csharp_files() -> list[Path]:
    """列出固定 production root 的有界 regular C# files；路徑不安全時整個 scanner fail closed。"""
    if not PRODUCTION_ROOT.is_dir() or not PROJECT_FILE.is_file() or not SETTINGS_FILES:
        raise ScannerInputError("production-source-missing")
    files: list[Path] = []
    for candidate in PRODUCTION_ROOT.rglob("*.cs"):
        resolved_candidate = require_path_within_root(PRODUCTION_ROOT, candidate)
        relative_path = resolved_candidate.relative_to(PRODUCTION_ROOT.resolve())
        if is_excluded_relative_path(relative_path):
            continue
        if not candidate.is_file():
            raise ScannerInputError("nonregular-source")
        files.append(candidate)
    if not files:
        raise ScannerInputError("production-csharp-missing")
    return sorted(files, key=lambda path: path.relative_to(PRODUCTION_ROOT).as_posix().casefold())


def preserve_newlines(value: str) -> str:
    """以空白遮罩非程式碼 token，僅保留換行，讓 scanner 不保留 literal/comment 資料。"""
    return "".join("\r" if character == "\r" else "\n" if character == "\n" else " " for character in value)


def skip_character_literal(source: str, index: int) -> int:
    """跳過 C# character literal；未關閉或跨行 literal 視為解析不安全。"""
    position = index + 1
    while position < len(source):
        character = source[position]
        if character in "\r\n":
            raise ScannerInputError("unclosed-character-literal")
        if character == "\\":
            if position + 1 >= len(source):
                raise ScannerInputError("unclosed-character-literal")
            position += 2
            continue
        if character == "'":
            return position + 1
        position += 1
    raise ScannerInputError("unclosed-character-literal")


def find_interpolation_end(source: str, opening_brace: int) -> int:
    """找出 interpolated expression 的 matching brace；巢狀 literal/comment 不得使 brace 配對漂移。"""
    depth = 1
    position = opening_brace + 1
    while position < len(source):
        if source.startswith("//", position):
            newline = source.find("\n", position + 2)
            position = len(source) if newline == -1 else newline + 1
            continue
        if source.startswith("/*", position):
            closing = source.find("*/", position + 2)
            if closing == -1:
                raise ScannerInputError("unclosed-block-comment")
            position = closing + 2
            continue
        if source.startswith('"""', position):
            raise ScannerInputError("raw-string-literal")
        if source[position] == "'":
            position = skip_character_literal(source, position)
            continue
        string_start = string_prefix_length(source, position)
        if string_start is not None:
            position = skip_string_literal(source, position, string_start)
            continue
        if source[position] == "{":
            depth += 1
        elif source[position] == "}":
            depth -= 1
            if depth == 0:
                return position
        position += 1
    raise ScannerInputError("unclosed-interpolation")


def string_prefix_length(source: str, index: int) -> int | None:
    """識別 C# regular、verbatim 與 interpolated literal 的 prefix 長度；raw string 由呼叫端拒絕。"""
    if source.startswith('$@"', index) or source.startswith('@$"', index):
        return 3
    if source.startswith('@"', index) or source.startswith('$"', index):
        return 2
    if source.startswith('"', index):
        return 1
    return None


def skip_string_literal(source: str, index: int, prefix_length: int) -> int:
    """跳過一個 literal，並在 interpolation expression 中保留真正的程式碼供外層 scanner 處理。"""
    prefix = source[index : index + prefix_length]
    verbatim = "@" in prefix
    interpolated = "$" in prefix
    position = index + prefix_length
    while position < len(source):
        character = source[position]
        if interpolated and character in "{}":
            if position + 1 < len(source) and source[position + 1] == character:
                position += 2
                continue
            if character == "{":
                position = find_interpolation_end(source, position) + 1
                continue
            raise ScannerInputError("unexpected-interpolation-brace")
        if verbatim:
            if character == '"' and position + 1 < len(source) and source[position + 1] == '"':
                position += 2
                continue
            if character == '"':
                return position + 1
            position += 1
            continue
        if character in "\r\n":
            raise ScannerInputError("unclosed-string-literal")
        if character == "\\":
            if position + 1 >= len(source):
                raise ScannerInputError("unclosed-string-literal")
            position += 2
            continue
        if character == '"':
            return position + 1
        position += 1
    raise ScannerInputError("unclosed-string-literal")


def preprocessor_directive_end(source: str, index: int) -> int | None:
    """辨識 code state 中行首的 C# preprocessor directive，並回傳不含換行的整行結束位置。

    指示詞的 label 不屬於 C# runtime code，常含自然語言、引號或註解式文字。若將它交給
    literal lexer，可能把合法 `#region "...` 誤判成未關閉字串；因此只有行首（允許空白）
    的 `#` 才會整行遮罩。非行首的 `#` 仍保留，讓未知語法維持 fail-closed 路徑。
    """
    if source[index] != "#":
        return None
    line_start = source.rfind("\n", 0, index) + 1
    if any(character not in " \t\r" for character in source[line_start:index]):
        return None
    newline = source.find("\n", index + 1)
    return len(source) if newline == -1 else newline


def strip_csharp_noncode(source: str) -> str:
    """遮罩 comment/literal，保留 interpolated expression 中的 code；未知 C# lexical state 一律拒絕。"""
    result: list[str] = []
    position = 0
    while position < len(source):
        directive_end = preprocessor_directive_end(source, position)
        if directive_end is not None:
            result.append(preserve_newlines(source[position:directive_end]))
            position = directive_end
            continue
        if source.startswith("//", position):
            newline = source.find("\n", position + 2)
            end = len(source) if newline == -1 else newline
            result.append(preserve_newlines(source[position:end]))
            position = end
            continue
        if source.startswith("/*", position):
            end = source.find("*/", position + 2)
            if end == -1:
                raise ScannerInputError("unclosed-block-comment")
            end += 2
            result.append(preserve_newlines(source[position:end]))
            position = end
            continue
        if source.startswith('"""', position):
            raise ScannerInputError("raw-string-literal")
        if source[position] == "'":
            end = skip_character_literal(source, position)
            result.append(preserve_newlines(source[position:end]))
            position = end
            continue
        prefix_length = string_prefix_length(source, position)
        if prefix_length is not None:
            prefix = source[position : position + prefix_length]
            if "$" not in prefix:
                end = skip_string_literal(source, position, prefix_length)
                result.append(preserve_newlines(source[position:end]))
                position = end
                continue
            result.append(preserve_newlines(prefix))
            position += prefix_length
            verbatim = "@" in prefix
            while position < len(source):
                character = source[position]
                if character == '"':
                    if verbatim and position + 1 < len(source) and source[position + 1] == '"':
                        result.append("  ")
                        position += 2
                        continue
                    result.append(" ")
                    position += 1
                    break
                if not verbatim and character in "\r\n":
                    raise ScannerInputError("unclosed-string-literal")
                if not verbatim and character == "\\":
                    if position + 1 >= len(source):
                        raise ScannerInputError("unclosed-string-literal")
                    result.append(preserve_newlines(source[position : position + 2]))
                    position += 2
                    continue
                if character in "{}":
                    if position + 1 < len(source) and source[position + 1] == character:
                        result.append("  ")
                        position += 2
                        continue
                    if character == "}":
                        raise ScannerInputError("unexpected-interpolation-brace")
                    closing = find_interpolation_end(source, position)
                    result.append(" ")
                    result.append(strip_csharp_noncode(source[position + 1 : closing]))
                    result.append(" ")
                    position = closing + 1
                    continue
                result.append(preserve_newlines(character))
                position += 1
            else:
                raise ScannerInputError("unclosed-string-literal")
            continue
        result.append(source[position])
        position += 1
    return "".join(result)


def scan_csharp_source(source: str) -> dict[str, int]:
    """僅從可驗證的 C# code token 建立 fixed category count，不保留或回傳 source context。"""
    sanitized_code = strip_csharp_noncode(source)
    counts = {
        category: len(pattern.findall(sanitized_code))
        for category, pattern in LEGACY_TOKEN_PATTERNS
    }
    return {category: count for category, count in counts.items() if count > 0}


def scan_production_csharp() -> list[dict[str, int | str]]:
    """彙總 production C# fixed category count/file count；輸出不含檔名，避免將 source structure 變成 artifact。"""
    occurrences: Counter[str] = Counter()
    files: Counter[str] = Counter()
    for path in iter_production_csharp_files():
        findings = scan_csharp_source(read_utf8_source(path))
        for category, count in findings.items():
            occurrences[category] += count
            files[category] += 1
    return [
        {"category": category, "fileCount": files[category], "occurrenceCount": occurrences[category]}
        for category in sorted(occurrences)
    ]


def scan_project_dependencies() -> list[dict[str, int | str]]:
    """以 XML attribute 匹配三種 direct legacy dependency；XML 無法解析即無法證明 P7.5 source integrity。"""
    try:
        root = element_tree.parse(PROJECT_FILE).getroot()
    except (OSError, element_tree.ParseError) as error:
        raise ScannerInputError("project-xml-invalid") from error
    counts: Counter[str] = Counter()
    for element in root.iter():
        name = element.tag.rsplit("}", 1)[-1]
        include = element.attrib.get("Include")
        if not include:
            continue
        for category, matcher in PROJECT_DEPENDENCY_MATCHERS.items():
            if matcher(name, include):
                counts[category] += 1
    return [{"category": category, "count": counts[category]} for category in sorted(PROJECT_DEPENDENCY_MATCHERS)]


def scan_settings_key_names(document: Any) -> dict[str, int]:
    """只走 JSON object key name，絕不讀取或回傳 value，避免 task artifact 接觸 settings secret。"""
    counts: Counter[str] = Counter()

    def visit(value: Any) -> None:
        if isinstance(value, dict):
            for key, nested_value in value.items():
                if not isinstance(key, str):
                    raise ScannerInputError("settings-key-invalid")
                if key.casefold() in LEGACY_SETTINGS_KEY_NAMES:
                    counts["legacy-settings-key"] += 1
                visit(nested_value)
        elif isinstance(value, list):
            for nested_value in value:
                visit(nested_value)
        elif value is None or isinstance(value, (bool, int, float, str)):
            return
        else:
            raise ScannerInputError("settings-shape-invalid")

    visit(document)
    return dict(counts)


class JsoncKeyOnlyScanner:
    """驗證 JSONC 結構並只解碼 object key 的 bounded scanner。

    此 scanner 的唯一 retained state 是本次 source 的 cursor 與固定 category counter。value 的
    string、number、boolean、null、array 與 object 都只做語法跳過；它們不會轉換為 Python value、
    cache、log 或 report。這讓 checked-in settings 的 key-only boundary 不會因完整反序列化而
    接觸 credential-like value。
    """

    def __init__(self, source: str) -> None:
        """建立一次性 parser；沒有 module cache、跨檔案 state 或 background resource。"""
        self._source = source
        self._position = 0
        self._counts: Counter[str] = Counter()

    def scan(self) -> dict[str, int]:
        """驗證 root object 並輸出固定 key category count；不回傳 input 內容或 value。"""
        self._skip_trivia()
        if not self._consume("{"):
            raise ScannerInputError("settings-root-invalid")
        self._parse_object_body()
        self._skip_trivia()
        if self._position != len(self._source):
            raise ScannerInputError("settings-json-invalid")
        return dict(self._counts)

    def _skip_trivia(self) -> None:
        """略過 JSON whitespace 與既有 JSONC 註解；未封閉 block comment 維持 fail closed。"""
        while self._position < len(self._source):
            if self._source[self._position] in " \t\r\n":
                self._position += 1
                continue
            if self._source.startswith("//", self._position):
                newline = self._source.find("\n", self._position + 2)
                self._position = len(self._source) if newline == -1 else newline + 1
                continue
            if self._source.startswith("/*", self._position):
                end = self._source.find("*/", self._position + 2)
                if end == -1:
                    raise ScannerInputError("settings-comment-invalid")
                self._position = end + 2
                continue
            return

    def _consume(self, expected: str) -> bool:
        """在略過 JSONC trivia 後消費唯一 syntax token，不允許寬鬆的部分比對。"""
        self._skip_trivia()
        if self._source.startswith(expected, self._position):
            self._position += len(expected)
            return True
        return False

    def _parse_object_body(self) -> None:
        """掃描 object entries；僅 key string 會被解碼和分類，value 交由純 syntax skip。"""
        if self._consume("}"):
            return
        while True:
            key = self._parse_string(decode=True)
            if not isinstance(key, str):
                raise ScannerInputError("settings-key-invalid")
            if key.casefold() in LEGACY_SETTINGS_KEY_NAMES:
                self._counts["legacy-settings-key"] += 1
            if not self._consume(":"):
                raise ScannerInputError("settings-json-invalid")
            self._skip_value()
            if self._consume("}"):
                return
            if not self._consume(","):
                raise ScannerInputError("settings-json-invalid")
            self._skip_trivia()
            if self._source.startswith("}", self._position):
                raise ScannerInputError("settings-json-invalid")

    def _skip_value(self) -> None:
        """只驗證 value 語法並前進 cursor；永不解碼、儲存、分類或輸出 value。"""
        self._skip_trivia()
        if self._consume("{"):
            self._parse_object_body()
            return
        if self._consume("["):
            self._parse_array_body()
            return
        if self._position < len(self._source) and self._source[self._position] == '"':
            self._parse_string(decode=False)
            return
        for literal in ("true", "false", "null"):
            if self._source.startswith(literal, self._position):
                self._position += len(literal)
                return
        number = re.match(r"-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?", self._source[self._position :])
        if number is not None:
            self._position += len(number.group(0))
            return
        raise ScannerInputError("settings-json-invalid")

    def _parse_array_body(self) -> None:
        """遞迴驗證 array value；array members 仍只做 skip，不會取得任何 value。"""
        if self._consume("]"):
            return
        while True:
            self._skip_value()
            if self._consume("]"):
                return
            if not self._consume(","):
                raise ScannerInputError("settings-json-invalid")
            self._skip_trivia()
            if self._source.startswith("]", self._position):
                raise ScannerInputError("settings-json-invalid")

    def _parse_string(self, decode: bool) -> str | None:
        """驗證 JSON string，僅在它是 object key 時以標準 JSON 規則解碼，不解碼 value string。"""
        self._skip_trivia()
        if self._position >= len(self._source) or self._source[self._position] != '"':
            raise ScannerInputError("settings-json-invalid")
        start = self._position
        self._position += 1
        while self._position < len(self._source):
            character = self._source[self._position]
            if character == "\\":
                if self._position + 1 >= len(self._source):
                    raise ScannerInputError("settings-json-invalid")
                escaped = self._source[self._position + 1]
                if escaped in '"\\/bfnrt':
                    self._position += 2
                    continue
                if escaped != "u" or self._position + 5 >= len(self._source):
                    raise ScannerInputError("settings-json-invalid")
                unicode_digits = self._source[self._position + 2 : self._position + 6]
                if len(unicode_digits) != 4 or any(digit not in "0123456789abcdefABCDEF" for digit in unicode_digits):
                    raise ScannerInputError("settings-json-invalid")
                self._position += 6
                continue
            if character == '"':
                self._position += 1
                if not decode:
                    return None
                try:
                    decoded = json.loads(self._source[start : self._position])
                except json.JSONDecodeError as error:
                    raise ScannerInputError("settings-key-invalid") from error
                return decoded if isinstance(decoded, str) else None
            if ord(character) < 0x20:
                raise ScannerInputError("settings-json-invalid")
            self._position += 1
        raise ScannerInputError("settings-json-invalid")


def scan_settings_key_names_from_jsonc(source: str) -> dict[str, int]:
    """以 key-only JSONC scanner 解析 settings；任何結構問題都不產生部分 evidence。"""
    return JsoncKeyOnlyScanner(source).scan()


def parse_settings_key_document(source: str) -> dict[str, int]:
    """相容 task contract 的 key-only settings 入口；不執行完整 JSON value materialization。"""
    return scan_settings_key_names_from_jsonc(source)


def scan_settings() -> list[dict[str, int | str]]:
    """解析固定 checked-in settings JSON；任何 invalid encoding/JSON 都不以部分結果繼續。"""
    counts: Counter[str] = Counter()
    for path in SETTINGS_FILES:
        counts.update(parse_settings_key_document(read_utf8_source(path)))
    return [{"category": category, "count": counts[category]} for category in sorted(counts)]


def read_matrix() -> list[dict[str, Any]]:
    """讀取 immutable matrix 並驗證必要 row 關係；不回傳 call-site ID 至 report。"""
    try:
        matrix = json.loads(read_utf8_source(MATRIX_FILE))
    except json.JSONDecodeError as error:
        raise ScannerInputError("matrix-json-invalid") from error
    rows = matrix.get("callSites")
    if (
        not isinstance(rows, list)
        or len(rows) != 70
        or matrix.get("sourceMatrix", {}).get("callSiteCount") != 70
        or len({row.get("callSiteId") for row in rows if isinstance(row, dict)}) != 70
    ):
        raise ScannerInputError("matrix-baseline-invalid")
    required = {"capabilityFamily", "consumer", "ceEvidence", "hostEvidence", "p75RemovalBlocker", "registry", "specialResourceRequirement", "temporaryLegacy"}
    if any(not isinstance(row, dict) or required.difference(row) for row in rows):
        raise ScannerInputError("matrix-schema-invalid")
    return rows


def matrix_aggregate(rows: list[dict[str, Any]]) -> tuple[dict[str, Any], list[dict[str, int | str]]]:
    """將 immutable rows 收斂為固定 totals/backlog，不讓 ID、operation 或 owner 離開 task boundary。"""
    blockers = Counter(str(row["p75RemovalBlocker"]) for row in rows)
    family_rows: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        family = row["capabilityFamily"]
        if not isinstance(family, str):
            raise ScannerInputError("matrix-family-invalid")
        family_rows[family].append(row)

    backlog: list[dict[str, int | str]] = []
    pending_evidence_count = 0
    for family in sorted(family_rows):
        family_items = family_rows[family]
        pending = sum(
            1
            for row in family_items
            if any(value != "succeeded" for value in row["ceEvidence"].values())
            or any(value != "succeeded" for value in row["hostEvidence"].values())
        )
        pending_evidence_count += pending
        backlog.append(
            {
                "capabilityFamily": family,
                "callSiteCount": len(family_items),
                "consumerNotMigratedCount": sum(row["consumer"].get("status") == "not-migrated" for row in family_items),
                "legacyDependencyBlockerCount": sum(row["p75RemovalBlocker"] == "legacy-sdk-dependency" for row in family_items),
                "localOnlyCount": sum(row["registry"].get("status") == "local-only" for row in family_items),
                "mixedBlockerCount": sum(row["p75RemovalBlocker"] == "mixed" for row in family_items),
                "pendingEvidenceCount": pending,
                "specialResourceCount": sum(row["specialResourceRequirement"] != "none" for row in family_items),
                "temporaryLegacyCount": sum(row["temporaryLegacy"] == "temporary-legacy" for row in family_items),
            }
        )
    summary = {
        "callSiteCount": len(rows),
        "ceOrHostEvidencePendingCount": pending_evidence_count,
        "closedHistoricalWriteFamilyCount": sum(
            row["ceEvidence"].get("ce91") == "no-go-closed" for row in rows
        ),
        "consumerNotMigratedCount": sum(row["consumer"].get("status") == "not-migrated" for row in rows),
        "p75BlockerCounts": [{"category": category, "count": blockers[category]} for category in sorted(blockers)],
        "temporaryLegacyCount": sum(row["temporaryLegacy"] == "temporary-legacy" for row in rows),
    }
    return summary, backlog


def no_go_reasons(
    matrix: dict[str, Any],
    references: list[dict[str, int | str]],
    dependencies: list[dict[str, int | str]],
    settings: list[dict[str, int | str]],
) -> list[str]:
    """依獨立 evidence 建立 fixed no-go classification；不以任一單一 static result 推導 ready。"""
    reasons: list[str] = []
    if matrix["temporaryLegacyCount"]:
        reasons.append("matrix-temporary-legacy")
    if matrix["consumerNotMigratedCount"]:
        reasons.append("consumer-migration-pending")
    if matrix["ceOrHostEvidencePendingCount"]:
        reasons.append("ce-or-host-evidence-pending")
    if matrix["closedHistoricalWriteFamilyCount"]:
        reasons.append("historical-write-family-closed")
    if any(item["category"] != "none" and item["count"] for item in matrix["p75BlockerCounts"]):
        reasons.append("matrix-p75-blocker")
    if any(item["occurrenceCount"] for item in references):
        reasons.append("production-legacy-reference")
    if any(item["count"] for item in dependencies):
        reasons.append("project-legacy-dependency")
    if any(item["count"] for item in settings):
        reasons.append("legacy-settings-key")
    return reasons


def prerequisite_readiness_state(reasons: list[str]) -> str:
    """將靜態 evidence 結果限制為 no-go 或 prerequisite-ready，絕不替代 P7.5 removal／P8 授權。"""
    return "no-go" if reasons else "prerequisite-ready"


def build_report() -> dict[str, Any]:
    """建立 deterministic、去識別化 P7.5 prerequisite report；任何 input 不確定性由呼叫端 fail closed。"""
    rows = read_matrix()
    matrix, backlog = matrix_aggregate(rows)
    references = scan_production_csharp()
    dependencies = scan_project_dependencies()
    settings = scan_settings()
    reasons = no_go_reasons(matrix, references, dependencies, settings)
    return {
        "analysisScope": "offline-allowlisted-production-source-only",
        "capabilityFamilyBacklog": backlog,
        "matrix": matrix,
        "productionLegacyReferences": references,
        "projectDependencies": dependencies,
        "readiness": {"noGoReasons": reasons, "state": prerequisite_readiness_state(reasons)},
        "schemaVersion": SCHEMA_VERSION,
        "settingsKeyReferences": settings,
    }


def validate_report(report: Any) -> dict[str, Any]:
    """重建 expected report 進行 strict equality，拒絕手改計數、敏感欄位或將 no-go 升格 ready。"""
    errors: list[dict[str, str]] = []
    if not isinstance(report, dict):
        return {"errors": [{"ruleId": "P75-REPORT-SHAPE"}], "outcome": "invalid", "schemaVersion": SCHEMA_VERSION}
    rendered_keys = json.dumps(report, ensure_ascii=True, sort_keys=True).casefold()
    if any(forbidden in rendered_keys for forbidden in FORBIDDEN_OUTPUT_KEY_PARTS):
        errors.append({"ruleId": "P75-REPORT-SENSITIVE-KEY"})
    try:
        expected = build_report()
    except ScannerInputError:
        errors.append({"ruleId": "P75-SCANNER-INPUT-INVALID"})
        expected = None
    if expected is not None and report != expected:
        errors.append({"ruleId": "P75-REPORT-EXPECTED"})
    return {"errors": errors, "outcome": "valid" if not errors else "invalid", "schemaVersion": SCHEMA_VERSION}


def evaluate_p75_gate(report: Any) -> dict[str, Any]:
    """評估 P7.5 removal prerequisite；report 無法驗證或仍有任一 no-go 均不授權移除。"""
    validation = validate_report(report)
    if validation["outcome"] != "valid":
        return {"outcome": "invalid-report", "schemaVersion": SCHEMA_VERSION}
    readiness = report["readiness"]
    if readiness["state"] != "prerequisite-ready":
        return {"outcome": "no-go", "reasons": readiness["noGoReasons"], "schemaVersion": SCHEMA_VERSION}
    return {"outcome": "prerequisite-ready", "schemaVersion": SCHEMA_VERSION}


def write_report(path: Path, report: dict[str, Any]) -> None:
    """以 UTF-8 無 BOM、CRLF 和 final CRLF 寫入唯一 task-owned report，不允許輸出到任意位置。"""
    resolved = path.resolve()
    task_root = TASK_DIRECTORY.resolve()
    try:
        resolved.relative_to(task_root)
    except ValueError as error:
        raise ScannerInputError("report-path-invalid") from error
    rendered = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True).replace("\n", "\r\n") + "\r\n"
    resolved.write_bytes(rendered.encode("utf-8"))


def parse_arguments() -> argparse.Namespace:
    """解析有限 CLI；不提供 scan root、network、credential 或 CE mutation options。"""
    parser = argparse.ArgumentParser(description="Build, validate, or enforce offline P7.5 prerequisite evidence.")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--report", type=Path)
    group.add_argument("--validate", type=Path)
    group.add_argument("--enforce-p75", action="store_true")
    return parser.parse_args()


def write_console(value: dict[str, Any]) -> None:
    """輸出固定 JSON 結果；不輸出 path、原始 exception 或 source/settings 內容。"""
    sys.stdout.write(json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True) + "\n")


def main() -> int:
    """執行 report/validate/enforce；所有 scanner fault 皆以固定 invalid-input 分類結束。"""
    arguments = parse_arguments()
    try:
        if arguments.report is not None:
            report = build_report()
            write_report(arguments.report, report)
            write_console({"outcome": "reported", "schemaVersion": SCHEMA_VERSION})
            return 0
        if arguments.validate is not None:
            report = json.loads(read_utf8_source(arguments.validate))
            validation = validate_report(report)
            write_console(validation)
            return 0 if validation["outcome"] == "valid" else 1
        report = build_report()
        gate = evaluate_p75_gate(report)
        write_console(gate)
        return 0 if gate["outcome"] == "prerequisite-ready" else 1
    except (ScannerInputError, json.JSONDecodeError):
        write_console({"classification": "scanner-input-invalid", "outcome": "invalid-input", "schemaVersion": SCHEMA_VERSION})
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
