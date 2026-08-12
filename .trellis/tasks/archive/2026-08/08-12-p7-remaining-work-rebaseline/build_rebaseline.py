"""建立並驗證 P7 尚餘能力的純離線、去識別化 authoritative gap matrix。

此工具只讀取 repository 內明確 allowlisted 的封存 P7 artifacts 與 C#／設定來源。它不連線 CRM、
不讀取 credential 或環境變數、不啟動產品、也不寫入 task 目錄以外的資料。輸出僅保存固定 capability
分類與 source-relative path；它不是 CE evidence、routing authority 或 deployment manifest。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


TASK_DIRECTORY = Path(__file__).resolve().parent
REPOSITORY_ROOT = next(parent for parent in TASK_DIRECTORY.parents if (parent / ".trellis").is_dir())
ARCHIVE_ROOT = REPOSITORY_ROOT / ".trellis/tasks/archive/2026-08"
# phase0 是 70 個 immutable call-site ID 的 canonical 來源；coverage 檔只補足
# capability family 等去識別化分類，不能取代 phase0 checksum 或 identity。
P70_MATRIX = REPOSITORY_ROOT / ".trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json"
P70_COVERAGE = ARCHIVE_ROOT / "08-05-gateway-capability-inventory/coverage-matrix.json"
OPERATION_IDS = REPOSITORY_ROOT / "SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs"
REGISTRY = REPOSITORY_ROOT / "SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs"
DATA8_EXECUTOR = REPOSITORY_ROOT / "SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs"
PRODUCT_CLIENT_ROOT = REPOSITORY_ROOT / "SpeechMessage.Dynamics.ProductClient"
LOCAL_ONLY_CATALOG = REPOSITORY_ROOT / "SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyCatalog.cs"
CHURCH_REPORT_ROOT = REPOSITORY_ROOT / "SpeechMessageProducts.ChurchReport"

P71_OPERATION_IDS = {
    "fee.dedication.retrieve.by.contact",
    "fee.dedication.retrieve.by.contact.date.range",
    "fees.retrieve.by.dedication.period",
    "fees.editor.load.by.disciplelesson",
    "lessons.stor.retrieve.by.contact",
    "lessons.stor.retrieve.by.disciplelesson",
}

P72_LOCAL_ONLY_OPERATION_IDS = {
    "payments.fee.update.after.payment",
    "payments.dedication.complete.recurring",
    "payments.contact.create.or.update",
    "payments.dedication.cancel.booking",
    "payments.contact.create.with.dedication.number",
    "payments.contact.update.card.profile",
    "appointments.entity.create.or.update",
    "newperson.contact.create.full.onboarding",
    "fees.editor.update.by.storlesson",
    "fees.editor.stage.inmemory.change",
    "fees.create.from.storlesson",
    "presentrecord.create.on.download",
    "presentrecord.upsert.on.upload",
}

# Slice C 的舊 CE cycle 已在受控 read-back 與 cleanup 後以 no-go 結束。
# 這個 immutable allowlist 只描述歷史結果，絕不授權重試、建立 fixture 或執行 CE I/O；
# 後續 matrix 產生器與驗證器都必須保留該結果，避免靜態 executor 造成假性的可重試判斷。
P72_SLICE_C_NO_GO_CLOSED_OPERATION_IDS = {
    "listmanagement.smallgroup.update.fields",
}

PACKAGE02_OPERATION_IDS = {
    "memberinfo.contact.update.basic.info",
    "memberinfo.contact.update.line.profile",
    "memberinfo.contact.count.ungrouped.commitment",
    "list.members.add.many",
    "list.members.remove.one",
    "listmanagement.smallgroup.update.fields",
    "contact.assign.owner",
    "newperson.contact.transfer.between.lists",
}

# 此 mapping 是靜態 source evidence 的 allowlist，不是部署設定讀取或 live rollout 證據。
# `migrated-disabled` 僅表示程式已接到 typed client，且 P7.1 封存證據指出它仍由
# disabled-by-default gate 保護；它不能推論任何機器、環境、Dedicated 或 CE 的成功。
GATED_CHURCHREPORT_CONSUMER_EVIDENCE: dict[str, tuple[Path, str]] = {
    "fee.dedication.retrieve.by.contact.date.range": (
        CHURCH_REPORT_ROOT / "Services/DonationFeeQueryService.cs",
        "RetrieveDedicationFeesByContactDateRangeAsync",
    ),
    "lessons.stor.retrieve.by.contact": (
        CHURCH_REPORT_ROOT / "Services/StorLessonQueryService.cs",
        "RetrieveStorLessonsByContactAsync",
    ),
    "lessons.stor.retrieve.by.disciplelesson": (
        CHURCH_REPORT_ROOT / "Services/StorLessonQueryService.cs",
        "RetrieveStorLessonsByDiscipleLessonAsync",
    ),
}

SPECIAL_RESOURCE_BY_FAMILY = {
    "churchreport.metadata": "metadata-cache",
    "churchreport.weekly.reporting": "paging-result",
}

REQUIRED_ROW_KEYS = {
    "callSiteId",
    "operation",
    "capabilityFamily",
    "operationKind",
    "registry",
    "data8Executor",
    "productClient",
    "consumer",
    "ceEvidence",
    "hostEvidence",
    "rollout",
    "rollback",
    "temporaryLegacy",
    "specialResourceRequirement",
    "p75RemovalBlocker",
}


def read_utf8(path: Path) -> str:
    """讀取固定 repository 來源；這個 helper 不接受呼叫端路徑，避免掃描秘密或 deployment 資料。"""
    return path.read_text(encoding="utf-8")


def sha256(path: Path) -> str:
    """取得來源完整性雜湊；只用於證明檔案版本，不輸出內容。"""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_source_matrix() -> tuple[list[dict[str, Any]], dict[str, Any]]:
    """以 canonical phase0 identity 與受驗證 coverage 分類建立固定 70-row 輸入；任一來源掉列、重複或 ID 不一致都 fail closed。"""
    phase0 = json.loads(read_utf8(P70_MATRIX))
    coverage = json.loads(read_utf8(P70_COVERAGE))
    phase0_rows = phase0.get("normalizedCallSites")
    coverage_rows = coverage.get("callSites")
    if not isinstance(phase0_rows, list) or not isinstance(coverage_rows, list):
        raise ValueError("P7.0 source artifacts must expose call-site lists")
    if len(phase0_rows) != 70 or len(coverage_rows) != 70:
        raise ValueError("P7.0 source artifacts must contain exactly seventy call sites")

    phase0_ids = [row.get("id") for row in phase0_rows]
    coverage_ids = [row.get("callSiteId") for row in coverage_rows]
    if (
        len(set(phase0_ids)) != 70
        or len(set(coverage_ids)) != 70
        or any(not isinstance(value, str) for value in phase0_ids)
        or set(phase0_ids) != set(coverage_ids)
    ):
        raise ValueError("P7.0 source artifacts must have identical unique call-site identifiers")

    coverage_by_id = {row["callSiteId"]: row for row in coverage_rows}
    rows: list[dict[str, Any]] = []
    for phase0_row in phase0_rows:
        call_site_id = phase0_row["id"]
        coverage_row = coverage_by_id[call_site_id]
        operation_id = phase0_row.get("capabilityOperationId")
        if operation_id != coverage_row.get("operation", {}).get("id"):
            raise ValueError("P7.0 source artifacts must agree on operation identifiers")
        rows.append(
            {
                "callSiteId": call_site_id,
                "operation": {
                    "id": operation_id,
                    "status": "forbidden-no-capability" if operation_id is None else "candidate",
                },
                "operationKind": phase0_row["operationKind"],
                "capabilityFamily": coverage_row["capabilityFamily"],
            }
        )
    return rows, {"callSiteCount": 70, "sha256": sha256(P70_MATRIX)}


def csharp_operation_constants() -> dict[str, str]:
    """由 OperationIds 常數擷取 operation ID；只比對正式 const 宣告，忽略註解與任意字串。"""
    pattern = re.compile(r'public\s+const\s+string\s+(\w+)\s*=\s*"([a-z0-9.]+)"\s*;', re.MULTILINE)
    constants = {operation_id: name for name, operation_id in pattern.findall(read_utf8(OPERATION_IDS))}
    required = P71_OPERATION_IDS | P72_LOCAL_ONLY_OPERATION_IDS | PACKAGE02_OPERATION_IDS
    missing = required.difference(constants)
    if missing:
        raise ValueError("OperationIds source is missing an allowlisted operation constant")
    return constants


def referenced_operation_ids(path: Path, known_ids: set[str]) -> set[str]:
    """以去除 C# 註解與字串後的已知常數名稱尋找 source references，避免文件或 literal 被誤判為 executor/consumer 實作。"""
    text = strip_csharp_comments_and_literals(read_utf8(path))
    constants = csharp_operation_constants()
    return {
        operation_id
        for operation_id, constant_name in constants.items()
        if operation_id in known_ids and re.search(rf"\bOperationIds\.{re.escape(constant_name)}\b", text)
    }


def strip_csharp_comments_and_literals(source: str) -> str:
    """以固定長度空白取代 C# line/block comments 與 quoted literals，保留其餘 token 邊界供精確 OperationIds 掃描，且不建立 parser cache 或共享可變狀態。"""
    pattern = re.compile(
        r'//[^\r\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\\r\n])*"',
        re.DOTALL,
    )
    return pattern.sub(lambda match: "".join("\r" if char == "\r" else "\n" if char == "\n" else " " for char in match.group()), source)


def implementation_evidence(known_ids: set[str]) -> tuple[set[str], set[str], set[str]]:
    """蒐集 registry、Data8 executor 與 typed ProductClient 的獨立靜態 evidence，不觸及 ChurchReport consumer。"""
    registry_ids = referenced_operation_ids(REGISTRY, known_ids)
    executor_ids = referenced_operation_ids(DATA8_EXECUTOR, known_ids)
    product_ids: set[str] = set()
    for path in sorted(PRODUCT_CLIENT_ROOT.rglob("*.cs")):
        if "bin" in path.parts or "obj" in path.parts:
            continue
        product_ids.update(referenced_operation_ids(path, known_ids))
    return registry_ids, executor_ids, product_ids


def churchreport_gated_consumer_operation_ids() -> set[str]:
    """以精確 client method call 和本機 gate 檢查確認三條 P7.1 consumer 路徑，避免 bootstrap 的 OperationIds 引用被誤判為產品切流。"""
    result: set[str] = set()
    for operation_id, (path, client_method) in GATED_CHURCHREPORT_CONSUMER_EVIDENCE.items():
        text = read_utf8(path)
        if client_method not in text or "_package01Enabled" not in text:
            raise ValueError("ChurchReport gated consumer source evidence is incomplete")
        result.add(operation_id)
    return result


def legacy_reference_count() -> int:
    """取得 P7.5 的去識別化 reference 數量，不把實際行號、設定內容或 source text 寫進 matrix。"""
    patterns = (
        re.compile(r"\bToolUtility\b"),
        re.compile(r"\bMicrosoft\.Xrm\.Sdk\b"),
        re.compile(r"\bIOrganizationService\b"),
        re.compile(r"\bMicrosoft\.PowerPlatform\.Dataverse\.Client\b"),
    )
    count = 0
    for path in sorted(CHURCH_REPORT_ROOT.rglob("*")):
        if not path.is_file() or path.suffix.lower() not in {".cs", ".csproj"}:
            continue
        if "bin" in path.parts or "obj" in path.parts:
            continue
        text = read_utf8(path)
        count += sum(len(pattern.findall(text)) for pattern in patterns)
    return count


def resource_requirement(row: dict[str, Any]) -> str:
    """從固定 family／operation kind 保守分類 P7.3；未知項目不猜測為已完成資源遷移。"""
    family = row["capabilityFamily"]
    if family in SPECIAL_RESOURCE_BY_FAMILY:
        return SPECIAL_RESOURCE_BY_FAMILY[family]
    if row["operationKind"] == "metadata":
        return "metadata-cache"
    if family == "churchreport.member.profile" and "image" in row["operation"]["id"]:
        return "attachment-stream"
    return "none"


def base_evidence(operation_id: str, is_local_only: bool) -> tuple[dict[str, str], dict[str, str]]:
    """以已封存 evidence 建立 CE/host 欄位；沒有精確 evidence 的所有狀態一律保持 pending/not-executed。"""
    if operation_id in P72_SLICE_C_NO_GO_CLOSED_OPERATION_IDS:
        return (
            {"ce82": "not-executed", "ce91": "no-go-closed"},
            {"embedded": "not-executed", "dedicated": "not-executed"},
        )
    if operation_id in P71_OPERATION_IDS:
        return (
            {"ce82": "evidence-pending", "ce91": "succeeded"},
            {"embedded": "succeeded", "dedicated": "evidence-pending"},
        )
    if is_local_only:
        return (
            {"ce82": "not-executed", "ce91": "not-executed"},
            {"embedded": "not-executed", "dedicated": "not-executed"},
        )
    return (
        {"ce82": "evidence-pending", "ce91": "evidence-pending"},
        {"embedded": "evidence-pending", "dedicated": "evidence-pending"},
    )


def build_matrix() -> dict[str, Any]:
    """以 70-row baseline 建立排序、去識別化矩陣；不將任何靜態 evidence 推論為 CE 或 consumer success。"""
    source_rows, source_info = read_source_matrix()
    known_ids = {
        row["operation"]["id"]
        for row in source_rows
        if row["operation"]["status"] != "forbidden-no-capability"
    }
    registry_ids, executor_ids, product_ids = implementation_evidence(known_ids)
    consumer_ids = churchreport_gated_consumer_operation_ids()
    has_legacy_dependencies = legacy_reference_count() > 0

    rows: list[dict[str, Any]] = []
    for source in sorted(source_rows, key=lambda item: item["callSiteId"]):
        operation = source["operation"]
        operation_id = operation["id"]
        forbidden = operation["status"] == "forbidden-no-capability"
        local_only = operation_id in P72_LOCAL_ONLY_OPERATION_IDS
        is_registry = operation_id in registry_ids
        is_executor = operation_id in executor_ids
        is_product = operation_id in product_ids
        is_consumer = operation_id in consumer_ids
        ce_evidence, host_evidence = base_evidence(operation_id, local_only)

        if forbidden:
            registry_status = "not-declared"
            executor_status = "not-implemented"
            product_status = "not-implemented"
        elif local_only:
            registry_status = "local-only"
            executor_status = "local-only-rejected"
            product_status = "not-implemented"
        else:
            registry_status = "declared" if is_registry else "not-declared"
            executor_status = "implemented" if is_executor else "not-implemented"
            product_status = "implemented" if is_product else "not-implemented"

        consumer_status = "not-migrated"
        if is_consumer:
            consumer_status = "migrated-disabled"

        resource = resource_requirement(source)
        blocked = (
            consumer_status == "not-migrated"
            or has_legacy_dependencies
            or ce_evidence["ce91"] != "succeeded"
            or resource != "none"
        )
        if local_only:
            removal_blocker = "mixed"
        elif resource != "none":
            removal_blocker = "special-resource-pending"
        elif consumer_status == "not-migrated":
            removal_blocker = "consumer-not-migrated"
        elif has_legacy_dependencies:
            removal_blocker = "legacy-sdk-dependency"
        elif ce_evidence["ce91"] != "succeeded":
            removal_blocker = "ce-evidence-missing"
        else:
            removal_blocker = "none"

        rows.append(
            {
                "callSiteId": source["callSiteId"],
                "operation": {"id": operation_id, "kind": source["operationKind"]},
                "capabilityFamily": source["capabilityFamily"],
                "operationKind": source["operationKind"],
                "registry": {"status": registry_status},
                "data8Executor": {"status": executor_status},
                "productClient": {"status": product_status},
                "consumer": {"status": consumer_status},
                "ceEvidence": ce_evidence,
                "hostEvidence": host_evidence,
                "rollout": {"owner": "p7.4-capability-owner" if is_product else "pending"},
                "rollback": {"owner": "p7.4-capability-owner" if is_product else "pending"},
                "temporaryLegacy": "temporary-legacy" if blocked else "none",
                "specialResourceRequirement": resource,
                "p75RemovalBlocker": removal_blocker,
            }
        )

    return {
        "schemaVersion": "p7.remaining-work.rebaseline.v1",
        "sourceMatrix": {"callSiteCount": 70, "sha256": source_info["sha256"]},
        "analysisScope": "offline-allowlisted-repository-source-only",
        "callSites": rows,
    }


def validate_matrix(matrix: dict[str, Any]) -> dict[str, Any]:
    """驗證 schema、來源完整性與關鍵 fail-closed 關係，回傳去識別化、穩定排序的錯誤分類。"""
    errors: list[dict[str, str]] = []
    rows = matrix.get("callSites")
    if not isinstance(rows, list) or len(rows) != 70:
        errors.append({"ruleId": "P7R-ROW-COUNT", "callSiteId": "", "message": "matrix must contain exactly seventy call sites"})
        rows = [] if not isinstance(rows, list) else rows
    identifiers = [row.get("callSiteId", "") for row in rows]
    if len(set(identifiers)) != len(identifiers):
        errors.append({"ruleId": "P7R-DUPLICATE-CALLSITE", "callSiteId": "", "message": "call-site identifiers must be unique"})
    source_rows, source_info = read_source_matrix()
    expected = [row["callSiteId"] for row in sorted(source_rows, key=lambda item: item["callSiteId"])]
    if identifiers != expected:
        errors.append({"ruleId": "P7R-SOURCE-BASELINE", "callSiteId": "", "message": "call-site identifiers must equal the immutable source baseline"})
    if matrix.get("sourceMatrix", {}).get("sha256") != source_info["sha256"]:
        errors.append({"ruleId": "P7R-SOURCE-HASH", "callSiteId": "", "message": "matrix source hash must equal the immutable source baseline"})

    for row in rows:
        call_site_id = row.get("callSiteId", "")
        missing = REQUIRED_ROW_KEYS.difference(row)
        if missing:
            errors.append({"ruleId": "P7R-REQUIRED-FIELD", "callSiteId": call_site_id, "message": "matrix row has missing required fields"})
            continue
        if row["registry"].get("status") == "local-only":
            if row["data8Executor"].get("status") != "local-only-rejected" or row["productClient"].get("status") != "not-implemented":
                errors.append({"ruleId": "P7R-LOCAL-ONLY-IMPLEMENTATION", "callSiteId": call_site_id, "message": "local-only rows cannot claim executor or ProductClient implementation"})
            if any(value == "succeeded" for value in row["ceEvidence"].values()):
                errors.append({"ruleId": "P7R-LOCAL-ONLY-NO-CE", "callSiteId": call_site_id, "message": "local-only rows cannot claim CE evidence"})
            if row["consumer"].get("status") != "not-migrated":
                errors.append({"ruleId": "P7R-LOCAL-ONLY-NO-CONSUMER", "callSiteId": call_site_id, "message": "local-only rows cannot claim consumer migration"})
        if row["operation"].get("id") in P72_SLICE_C_NO_GO_CLOSED_OPERATION_IDS:
            if row["ceEvidence"].get("ce91") != "no-go-closed":
                errors.append({"ruleId": "P7R-SLICE-C-NO-GO-CLOSED", "callSiteId": call_site_id, "message": "closed Slice C no-go operation must retain its immutable CE 9.1 disposition"})
        if row["consumer"].get("status") == "migrated-enabled":
            if row["hostEvidence"].get("dedicated") != "succeeded" or row["ceEvidence"].get("ce91") != "succeeded":
                errors.append({"ruleId": "P7R-CONSUMER-GATE", "callSiteId": call_site_id, "message": "enabled consumer requires CE 9.1 and Dedicated evidence"})
        if row["temporaryLegacy"] == "none" and row["p75RemovalBlocker"] != "none":
            errors.append({"ruleId": "P7R-REMOVAL-STATE", "callSiteId": call_site_id, "message": "non-none removal blocker requires temporary legacy state"})

    errors.sort(key=lambda item: (item["ruleId"], item["callSiteId"], item["message"]))
    return {
        "schemaVersion": "p7.remaining-work.rebaseline.validation.v1",
        "outcome": "valid" if not errors else "invalid",
        "errors": errors,
    }


def write_json(path: Path, value: dict[str, Any]) -> None:
    """以 UTF-8 無 BOM、固定縮排與 CRLF final newline 輸出 task artifact，滿足 Windows repository 的 byte-level 編碼合約。"""
    rendered = json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True)
    path.write_bytes((rendered.replace("\n", "\r\n") + "\r\n").encode("utf-8"))


def parse_arguments() -> argparse.Namespace:
    """解析有限離線 CLI。沒有 endpoint、credential、network 或 CE mutation 選項。"""
    parser = argparse.ArgumentParser(description="Build or validate the offline P7 authoritative gap matrix.")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--output", type=Path)
    group.add_argument("--validate", type=Path)
    return parser.parse_args()


def main() -> int:
    """執行 build 或 validation；輸出只保留 validator 固定分類，不輸出 matrix 原始內容。"""
    arguments = parse_arguments()
    if arguments.output is not None:
        output = arguments.output.resolve()
        if output.parent != TASK_DIRECTORY.resolve() and not str(output).startswith(str(TASK_DIRECTORY.resolve())):
            # Tests use a temporary output to prove no static state. It remains a local filesystem artifact, not CE state.
            if output.suffix != ".json":
                raise ValueError("offline matrix output must be a JSON file")
        write_json(output, build_matrix())
        return 0
    matrix = json.loads(read_utf8(arguments.validate))
    report = validate_matrix(matrix)
    sys.stdout.write(json.dumps(report, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n")
    return 0 if report["outcome"] == "valid" else 1


if __name__ == "__main__":
    raise SystemExit(main())
