"""P7.0 的離線能力盤點產生器與確定性 coverage validator。

此工具的唯一輸入是版本控制中的 Phase 0 matrix、來源碼與 task-local JSON。它不讀取
環境變數、產品執行設定、Credential Manager、網路、CE、token、cookie 或 connection string。
產生器把來源碼的可驗證現況投影為 task-local artifact；validator 只驗證這些 artifact，
因此可在未連線 Lenovo、Dedicated Gateway 或 Central Gateway 的情況下重現相同結果。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from collections import Counter
from pathlib import Path
from typing import Any


TASK_DIRECTORY = Path(__file__).resolve().parent
REPOSITORY_ROOT = TASK_DIRECTORY.parents[2]
SOURCE_MATRIX = REPOSITORY_ROOT / ".trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json"
PRELIMINARY_INVENTORY = TASK_DIRECTORY / "preliminary-capability-inventory.json"
SOURCE_MANIFESTS = TASK_DIRECTORY / "source-manifests.json"
COVERAGE_MATRIX = TASK_DIRECTORY / "coverage-matrix.json"
P72_ACTIVATION_INPUT = TASK_DIRECTORY / "p7.2-activation-input.json"
REFERENCE_SCAN_BASELINE = TASK_DIRECTORY / "reference-scan-baseline.json"

OPERATION_ID_PATTERN = re.compile(r"^[a-z0-9]+(?:\.[a-z0-9]+)+$")
GENERIC_CAPABILITY_PREFIXES = ("entity.", "querybase.", "fetchxml.")
REQUIRED_ROW_PATHS = (
    "callSiteId",
    "source.file",
    "source.symbol",
    "legacyBehavior",
    "businessUseCase",
    "capabilityFamily",
    "operationKind",
    "operation.id",
    "productClient.owner",
    "requestDto.owner",
    "responseDto.owner",
    "registry.status",
    "data8Executor.status",
    "officialWorkerExecutor.status",
    "consumer.status",
    "realCeEvidence.ce82",
    "realCeEvidence.ce91",
    "featureGate",
    "authorization",
    "profileBoundary",
    "workloadBoundary",
    "rollout.owner",
    "rollback.owner",
    "temporaryLegacy.status",
    "toolUtilityRemovalGate",
    "lifecycleRisk.resources",
    "lifecycleRisk.owner",
    "lifecycleRisk.releasePath",
    "lifecycleRisk.isolationBoundary",
)


def load_json(path: Path) -> dict[str, Any]:
    """讀取 UTF-8 JSON，並將無法解析的 task-local input 視為 fail-closed 錯誤。"""
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, value: dict[str, Any]) -> None:
    """以 UTF-8 無 BOM、CRLF 與結尾 CRLF 寫入可版本控制的確定性 JSON artifact。"""
    text = json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    path.write_text(text, encoding="utf-8", newline="\r\n")


def sha256_file(path: Path) -> str:
    """回傳檔案位元組雜湊，使盤點能偵測來源碼或基線遭到修改。"""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def relative_path(path: Path) -> str:
    """將來源檔案投影為 repository-relative 路徑，避免將電腦帳號或絕對環境路徑寫入 artifact。"""
    return path.relative_to(REPOSITORY_ROOT).as_posix()


def extract_operation_ids(operation_ids_path: Path) -> dict[str, str]:
    """擷取 OperationIds 的常數名稱與 capability ID，作為 Registry/Client scan 的唯一字典。"""
    text = operation_ids_path.read_text(encoding="utf-8")
    return dict(
        re.findall(
            r"public const string\s+(\w+)\s*=\s*\"([a-z0-9]+(?:\.[a-z0-9]+)+)\";",
            text,
        )
    )


def operation_ids_referenced_by(path: Path, operation_ids: dict[str, str]) -> list[str]:
    """從明確的 OperationIds 常數引用取得 allowlist，避免靠敘述文件手寫數字。"""
    text = path.read_text(encoding="utf-8")
    names = re.findall(r"OperationIds\.(\w+)", text)
    return sorted({operation_ids[name] for name in names if name in operation_ids})


def source_file_manifest(path: Path) -> dict[str, str]:
    """保存非敏感來源檔案位置與雜湊，讓資料層 coverage 可追溯且不複製來源內容。"""
    return {"path": relative_path(path), "sha256": sha256_file(path)}


def build_source_manifests() -> dict[str, Any]:
    """掃描固定、可審核的來源，分開記錄 registry、Data8、Worker、consumer 與 CE evidence 狀態。"""
    operation_ids_path = REPOSITORY_ROOT / "SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs"
    registry_path = REPOSITORY_ROOT / "SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs"
    data8_path = REPOSITORY_ROOT / "SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs"
    worker_operations_path = REPOSITORY_ROOT / "SpeechMessage.Dynamics.WorkerHost/OfficialWorkerOperations.cs"
    worker_contract_path = REPOSITORY_ROOT / "SpeechMessage.Dynamics.WorkerProtocol/Package01FeeWorkerContract.cs"
    router_path = REPOSITORY_ROOT / "SpeechMessage.Dynamics.ControlPlane/Connectors/OfficialWorkerConnectorRouter.cs"
    product_client_path = REPOSITORY_ROOT / "SpeechMessage.Dynamics.ProductClient/FeeReads/Package01FeeReadClient.cs"
    product_client_contract_path = REPOSITORY_ROOT / "SpeechMessage.Dynamics.ProductClient/FeeReads/IPackage01FeeReadClient.cs"
    church_report_settings = REPOSITORY_ROOT / "SpeechMessageProducts.ChurchReport/appsettings.json"
    church_report_development_settings = REPOSITORY_ROOT / "SpeechMessageProducts.ChurchReport/appsettings.Development.json"

    operation_ids = extract_operation_ids(operation_ids_path)
    registry_ids = operation_ids_referenced_by(registry_path, operation_ids)
    data8_text = data8_path.read_text(encoding="utf-8")
    data8_ids = [operation_ids["RuntimeHealthWhoAmI"]] if "OperationIds.RuntimeHealthWhoAmI" in data8_text else []
    worker_operations_text = worker_operations_path.read_text(encoding="utf-8")
    worker_contract_text = worker_contract_path.read_text(encoding="utf-8")
    worker_identity_names = re.findall(
        r"public const string\s+(Runtime(?:HealthWhoAmI|PoolValidateConnection))\s*=\s*\"",
        worker_operations_text,
    )
    worker_contract_ids = re.findall(
        r"CapabilityOperationId\s*=\s*\"([a-z0-9]+(?:\.[a-z0-9]+)+)\"",
        worker_contract_text,
    )
    worker_ids = sorted(
        {operation_ids[name] for name in worker_identity_names if name in operation_ids}.union(worker_contract_ids)
    )
    product_client_ids = operation_ids_referenced_by(product_client_path, operation_ids)
    router_text = router_path.read_text(encoding="utf-8")
    settings_text = church_report_settings.read_text(encoding="utf-8")
    development_settings_text = church_report_development_settings.read_text(encoding="utf-8")
    source_matrix = load_json(SOURCE_MATRIX)
    rows = source_matrix["normalizedCallSites"]

    return {
        "schemaVersion": "p7.0.source-manifests.v1",
        "sourceMatrix": {
            "path": relative_path(SOURCE_MATRIX),
            "sha256": sha256_file(SOURCE_MATRIX),
            "normalizedCallSiteIds": sorted(row["id"] for row in rows),
            "normalizedCallSiteCount": len(rows),
        },
        "registry": {
            "status": "declared",
            "operationIds": registry_ids,
            "sources": [source_file_manifest(operation_ids_path), source_file_manifest(registry_path)],
        },
        "data8Executor": {
            "status": "implemented",
            "operationIds": data8_ids,
            "sources": [source_file_manifest(data8_path)],
        },
        "officialWorkerProtocol": {
            "status": "protocol-allowlisted",
            "operationIds": worker_ids,
            "sources": [source_file_manifest(worker_operations_path), source_file_manifest(worker_contract_path)],
        },
        "officialWorkerRouter": {
            "status": "implemented-offline-p6.1",
            "sources": [source_file_manifest(router_path)],
            "hasExplicitConnectorKinds": all(
                token in router_text for token in ("OfficialCrm82Worker", "OfficialCrm91Worker")
            ),
        },
        "productClient": {
            "status": "declared",
            "operationIds": product_client_ids,
            "owner": "SpeechMessage.Dynamics.ProductClient.IPackage01FeeReadClient",
            "sources": [source_file_manifest(product_client_path), source_file_manifest(product_client_contract_path)],
        },
        "consumer": {
            "status": "disabled",
            "featureGate": "Package01FeeReadsEnabled",
            "baseValue": "true" if re.search(r'"Package01FeeReadsEnabled"\s*:\s*true', settings_text) else "false",
            "developmentValue": "true" if re.search(r'"Package01FeeReadsEnabled"\s*:\s*true', development_settings_text) else "false",
            "sources": [source_file_manifest(church_report_settings), source_file_manifest(church_report_development_settings)],
        },
        "realCeEvidence": {
            "ce82": "metadata-only",
            "ce91": "metadata-only",
            "smoke": "not-started",
            "status": "evidence-pending",
        },
    }


def capability_groups(preliminary: dict[str, Any]) -> dict[str, str]:
    """建立 call-site 到唯一業務 capability family 的映射，重複或遺漏留給產生器 fail closed。"""
    result: dict[str, str] = {}
    for group in preliminary["capabilityGroups"]:
        for call_site_id in group["callSiteIds"]:
            if call_site_id in result:
                raise ValueError(f"重複 capability grouping: {call_site_id}")
            result[call_site_id] = group["family"]
    return result


def sanitize_business_use_case(row: dict[str, Any]) -> str:
    """僅保留 operation kind 與 entity/action 名稱，避免將 legacy raw request 或 credential 描述帶入 artifact。"""
    entity_or_action = str(row.get("entityOrAction", "unspecified")).strip().lower()
    entity_or_action = re.sub(r"[^a-z0-9._-]+", "-", entity_or_action).strip("-")
    return f"{row['operationKind']}:{entity_or_action or 'unspecified'}"


def lifecycle_risk(operation_kind: str) -> dict[str, Any]:
    """宣告所有後續 child task 都不可繞過的 profile-scoped 資源所有權與釋放邊界。"""
    resources = ["profile-scoped connector lease", "request cancellation"]
    if operation_kind in {"read", "metadata"}:
        resources.append("bounded response or paging state")
    if operation_kind in {"write", "action", "function"}:
        resources.append("idempotency and ambiguous-timeout reconciliation")
    return {
        "resources": resources,
        "owner": "deployment profile runtime and request scope",
        "releasePath": "stop admission, drain request, dispose lease, then release profile-owned resources",
        "isolationBoundary": "profile alias plus immutable generation; no request may supply connector, credential, endpoint, or organization",
    }


def row_route(capability_family: str, operation_kind: str) -> str:
    """回傳規劃用途的 P7 child slice，不建立 child task，也不把它誤當成執行授權。"""
    if capability_family in {"platform.shared.runtime", "platform.legacy.blocked"}:
        return "P7.5-removal-gate"
    if operation_kind == "metadata":
        return "P7.3-special-resource"
    if operation_kind in {"write", "action", "function"}:
        return "P7.2-write-action-function"
    return "P7.1-read"


def coverage_statuses(
    operation_id: str,
    capability_family: str,
    source_row: dict[str, Any],
    manifests: dict[str, Any],
) -> dict[str, Any]:
    """依 manifest 計算彼此獨立的 registry、executor、consumer 與 CE 證據，禁止互相推論。"""
    blocked = capability_family == "platform.legacy.blocked"
    registry_ids = set(manifests["registry"]["operationIds"])
    data8_ids = set(manifests["data8Executor"]["operationIds"])
    worker_ids = set(manifests["officialWorkerProtocol"]["operationIds"])
    product_client_ids = set(manifests["productClient"]["operationIds"])
    temporary_legacy = source_row["migrationStatus"] == "temporary-legacy"
    temporary_owner = source_row.get("temporaryLegacyOwner") or "p7-owner-unassigned"

    return {
        "operation": {
            "id": operation_id,
            "status": "forbidden-no-capability" if blocked else "candidate",
        },
        "productClient": {
            "owner": manifests["productClient"]["owner"] if operation_id in product_client_ids else "p7-child-task-required",
        },
        "requestDto": {"owner": "p7-child-task-required"},
        "responseDto": {"owner": "p7-child-task-required"},
        "registry": {"status": "declared" if operation_id in registry_ids else "not-declared"},
        "data8Executor": {"status": "implemented" if operation_id in data8_ids else "not-implemented"},
        "officialWorkerExecutor": {
            "status": "protocol-allowlisted" if operation_id in worker_ids else "not-implemented",
        },
        "consumer": {"status": manifests["consumer"]["status"]},
        "realCeEvidence": {
            "ce82": manifests["realCeEvidence"]["ce82"],
            "ce91": manifests["realCeEvidence"]["ce91"],
        },
        "connectorSupport": {
            "data8": "implemented" if operation_id in data8_ids else "evidence-pending",
            "officialWorker": "evidence-pending" if operation_id in worker_ids else "not-selected",
        },
        "ceSupport": {"ce82": "evidence-pending", "ce91": "evidence-pending"},
        "featureGate": manifests["consumer"]["featureGate"] if operation_id in product_client_ids else "not-yet-created",
        "authorization": "server-owned-workload-policy-required",
        "profileBoundary": "deployment-owned profile alias and immutable generation",
        "workloadBoundary": "server-owned workload-to-profile authorization required",
        "rollout": {"owner": temporary_owner if temporary_legacy else "p7-child-task-required"},
        "rollback": {"owner": temporary_owner if temporary_legacy else "p7-child-task-required"},
        "temporaryLegacy": {
            "status": source_row["migrationStatus"],
            "owner": temporary_owner if temporary_legacy else "not-applicable",
        },
        "toolUtilityRemovalGate": "p7.5-required",
    }


def build_coverage_matrix(manifests: dict[str, Any]) -> dict[str, Any]:
    """從 Phase 0 row 與 preliminary grouping 建立完整 70-row matrix，不複製敏感 legacy request 文字。"""
    source_matrix = load_json(SOURCE_MATRIX)
    preliminary = load_json(PRELIMINARY_INVENTORY)
    rows = source_matrix["normalizedCallSites"]
    group_by_call_site = capability_groups(preliminary)
    source_ids = {row["id"] for row in rows}
    if source_ids != set(group_by_call_site):
        raise ValueError("Phase 0 call-site IDs 與 preliminary grouping 不一致")

    call_sites: list[dict[str, Any]] = []
    for source_row in sorted(rows, key=lambda item: item["id"]):
        call_site_id = source_row["id"]
        family = group_by_call_site[call_site_id]
        operation_id = source_row["capabilityOperationId"]
        states = coverage_statuses(operation_id, family, source_row, manifests)
        call_sites.append(
            {
                "callSiteId": call_site_id,
                "source": {"file": source_row["file"], "symbol": source_row["member"]},
                "legacyBehavior": f"{source_row['operationKind']}-legacy-dynamics-access",
                "businessUseCase": sanitize_business_use_case(source_row),
                "capabilityFamily": family,
                "operationKind": source_row["operationKind"],
                "nextP7Slice": row_route(family, source_row["operationKind"]),
                "lifecycleRisk": lifecycle_risk(source_row["operationKind"]),
                **states,
            }
        )

    return {
        "schemaVersion": "p7.0.coverage-matrix.v1",
        "sourceMatrix": manifests["sourceMatrix"],
        "callSites": call_sites,
        "coverageInterpretation": "Registry, executor, consumer and real CE evidence are independent states; none implies another.",
    }


def build_p72_activation_input(matrix: dict[str, Any]) -> dict[str, Any]:
    """輸出 P7.2 建立前要補齊的 mutation family 清單，避免把隔離 CE 9.1 誤當作無限制寫入授權。"""
    candidates = [
        {
            "callSiteId": row["callSiteId"],
            "capabilityFamily": row["capabilityFamily"],
            "operationId": row["operation"]["id"],
            "operationKind": row["operationKind"],
            "requiredBeforeActivation": [
                "unique fixture owner",
                "allowed mutations",
                "precondition",
                "cleanup and reconciliation",
                "ambiguous-timeout policy",
                "CE support decision",
            ],
        }
        for row in matrix["callSites"]
        if row["nextP7Slice"] == "P7.2-write-action-function"
    ]
    return {
        "schemaVersion": "p7.0.p7.2-activation-input.v1",
        "sourceMatrixSha256": matrix["sourceMatrix"]["sha256"],
        "outcome": "not-approved-p7.0-inventory-only",
        "ce91FixtureEnvironment": "sunnyvalechback-isolated-development-organization",
        "requiredFamilies": [],
        "pendingCandidates": candidates,
    }


def scan_production_legacy_dependencies() -> dict[str, Any]:
    """建立 P7.5 前的 production reference 基線；它只掃 source 文字，不載入組態或執行產品。"""
    church_report_directory = REPOSITORY_ROOT / "SpeechMessageProducts.ChurchReport"
    patterns = {
        "ToolUtility": re.compile(r"\bToolUtility\b"),
        "Microsoft.Xrm.Sdk": re.compile(r"\bMicrosoft\.Xrm\.Sdk\b"),
        "DataverseClient": re.compile(r"\bMicrosoft\.PowerPlatform\.Dataverse\.Client\b"),
        "IOrganizationService": re.compile(r"\bIOrganizationService\b"),
    }
    findings: list[dict[str, Any]] = []
    for path in sorted(church_report_directory.rglob("*")):
        if not path.is_file() or path.suffix.lower() not in {".cs", ".csproj"}:
            continue
        if any(part.lower() in {"bin", "obj"} for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        counts = {name: len(pattern.findall(text)) for name, pattern in patterns.items()}
        counts = {name: count for name, count in counts.items() if count}
        if counts:
            findings.append({"path": relative_path(path), "counts": counts})
    remaining_count = sum(sum(finding["counts"].values()) for finding in findings)
    return {
        "schemaVersion": "p7.0.reference-scan-baseline.v1",
        "scope": "SpeechMessageProducts.ChurchReport production .cs and .csproj files only",
        "releaseBlockingAt": "P7.5",
        "remainingReferenceCount": remaining_count,
        "findings": findings,
    }


def build_artifacts() -> None:
    """依固定來源順序產生所有 P7.0 task-local artifact，並保留 P7.5 enforcement 的未來輸入。"""
    manifests = build_source_manifests()
    matrix = build_coverage_matrix(manifests)
    activation_input = build_p72_activation_input(matrix)
    reference_scan = scan_production_legacy_dependencies()
    write_json(SOURCE_MANIFESTS, manifests)
    write_json(COVERAGE_MATRIX, matrix)
    write_json(P72_ACTIVATION_INPUT, activation_input)
    write_json(REFERENCE_SCAN_BASELINE, reference_scan)


def get_path(value: dict[str, Any], path: str) -> Any:
    """讀取 dotted JSON path；缺欄位回傳 None，以便 validator 收斂為單一確定性 error 格式。"""
    current: Any = value
    for segment in path.split("."):
        if not isinstance(current, dict) or segment not in current:
            return None
        current = current[segment]
    return current


def add_error(errors: list[dict[str, str]], rule_id: str, call_site_id: str, message: str) -> None:
    """以固定欄位收集 fail-closed error，並避免回顯來源內容或任何部署資料。"""
    errors.append({"ruleId": rule_id, "callSiteId": call_site_id, "message": message})


def validate(matrix_path: Path, manifest_path: Path, reference_scan_path: Path, enforce_p7_5: bool) -> dict[str, Any]:
    """驗證 matrix 對 source manifest 的完整性；P7.5 legacy zero-reference 僅在明確 enforcement 時阻斷。"""
    matrix = load_json(matrix_path)
    manifests = load_json(manifest_path)
    reference_scan = load_json(reference_scan_path)
    errors: list[dict[str, str]] = []
    warnings: list[dict[str, str]] = []
    call_sites = matrix.get("callSites", [])
    expected_ids = manifests["sourceMatrix"]["normalizedCallSiteIds"]
    actual_ids = [row.get("callSiteId", "") for row in call_sites]

    if len(call_sites) != len(expected_ids):
        add_error(errors, "P70-ROW-COUNT", "", "coverage matrix row count does not match the Phase 0 baseline")
    duplicate_ids = sorted(call_site_id for call_site_id, count in Counter(actual_ids).items() if call_site_id and count > 1)
    for call_site_id in duplicate_ids:
        add_error(errors, "P70-CALLSITE-DUPLICATE", call_site_id, "call-site ID appears more than once")
    if sorted(set(actual_ids)) != expected_ids:
        add_error(errors, "P70-CALLSITE-BASELINE", "", "matrix call-site IDs do not equal the source manifest baseline")
    if matrix.get("sourceMatrix", {}).get("sha256") != manifests["sourceMatrix"]["sha256"]:
        add_error(errors, "P70-SOURCE-HASH", "", "matrix source hash does not match source manifest")

    registry_ids = set(manifests["registry"]["operationIds"])
    data8_ids = set(manifests["data8Executor"]["operationIds"])
    worker_ids = set(manifests["officialWorkerProtocol"]["operationIds"])
    allowed_statuses = {
        "registry": {"declared", "not-declared"},
        "data8Executor": {"implemented", "not-implemented"},
        "officialWorkerExecutor": {"protocol-allowlisted", "not-implemented"},
        "consumer": {"disabled", "enabled"},
        "ce": {"metadata-only", "real-evidence"},
    }
    for row in sorted(call_sites, key=lambda item: item.get("callSiteId", "")):
        call_site_id = row.get("callSiteId", "")
        for field_path in REQUIRED_ROW_PATHS:
            if get_path(row, field_path) in (None, "", []):
                add_error(errors, "P70-REQUIRED-FIELD", call_site_id, f"missing required field: {field_path}")
        operation_id = get_path(row, "operation.id")
        operation_status = get_path(row, "operation.status")
        if operation_status != "forbidden-no-capability" and (
            not isinstance(operation_id, str) or not OPERATION_ID_PATTERN.fullmatch(operation_id)
        ):
            add_error(errors, "P70-OPERATION-ID", call_site_id, "operation ID is not a closed capability identifier")
        if operation_status != "forbidden-no-capability" and isinstance(operation_id, str) and operation_id.startswith(GENERIC_CAPABILITY_PREFIXES):
            add_error(errors, "P70-GENERIC-CAPABILITY", call_site_id, "generic entity, QueryBase, or FetchXML capability is forbidden")
        if operation_status == "forbidden-no-capability" and row.get("capabilityFamily") != "platform.legacy.blocked":
            add_error(errors, "P70-FORBIDDEN-CAPABILITY", call_site_id, "only platform legacy-blocked rows may reject capability creation")
        for state_name, allowed in allowed_statuses.items():
            value = get_path(row, f"{state_name}.status") if state_name not in {"ce"} else None
            if state_name == "ce":
                for version in ("ce82", "ce91"):
                    if get_path(row, f"realCeEvidence.{version}") not in allowed:
                        add_error(errors, "P70-CE-STATUS", call_site_id, f"unknown CE evidence status: {version}")
            elif value not in allowed:
                add_error(errors, "P70-COVERAGE-STATUS", call_site_id, f"unknown {state_name} status")
        if operation_status != "forbidden-no-capability":
            if get_path(row, "registry.status") == "declared" and operation_id not in registry_ids:
                add_error(errors, "P70-REGISTRY-MANIFEST", call_site_id, "declared registry operation is absent from source manifest")
            if get_path(row, "data8Executor.status") == "implemented" and operation_id not in data8_ids:
                add_error(errors, "P70-DATA8-MANIFEST", call_site_id, "implemented Data8 operation is absent from source manifest")
            if get_path(row, "officialWorkerExecutor.status") == "protocol-allowlisted" and operation_id not in worker_ids:
                add_error(errors, "P70-WORKER-PROTOCOL-MANIFEST", call_site_id, "Worker protocol operation is absent from source manifest")
        if get_path(row, "temporaryLegacy.status") == "temporary-legacy":
            if get_path(row, "rollout.owner") in (None, "", "p7-owner-unassigned") or get_path(row, "rollback.owner") in (None, "", "p7-owner-unassigned"):
                add_error(errors, "P70-TEMPORARY-LEGACY-OWNER", call_site_id, "temporary legacy requires rollout and rollback owners")

    remaining_reference_count = reference_scan.get("remainingReferenceCount", 0)
    legacy_result = {
        "ruleId": "P75-PRODUCTION-LEGACY-DEPENDENCY",
        "callSiteId": "",
        "message": "ChurchReport production legacy dependency references remain",
    }
    if remaining_reference_count:
        if enforce_p7_5:
            errors.append(legacy_result)
        else:
            warnings.append(legacy_result)

    errors.sort(key=lambda item: (item["ruleId"], item["callSiteId"], item["message"]))
    warnings.sort(key=lambda item: (item["ruleId"], item["callSiteId"], item["message"]))
    return {
        "schemaVersion": "p7.0.coverage-validation-report.v1",
        "outcome": "valid" if not errors else "invalid",
        "summary": {
            "callSiteCount": len(call_sites),
            "errorCount": len(errors),
            "warningCount": len(warnings),
            "p7_5Enforced": enforce_p7_5,
        },
        "errors": errors,
        "warnings": warnings,
    }


def parse_arguments() -> argparse.Namespace:
    """解析純本機檔案參數；不提供 endpoint、credential 或任何 CE 操作選項。"""
    parser = argparse.ArgumentParser(description="Validate P7.0 task-local coverage artifacts offline.")
    parser.add_argument("--build", action="store_true", help="regenerate task-local artifacts from approved repository sources")
    parser.add_argument("--matrix", type=Path, default=COVERAGE_MATRIX)
    parser.add_argument("--manifests", type=Path, default=SOURCE_MANIFESTS)
    parser.add_argument("--reference-scan", type=Path, default=REFERENCE_SCAN_BASELINE)
    parser.add_argument("--enforce-p7-5", action="store_true")
    return parser.parse_args()


def main() -> int:
    """執行產生或驗證，輸出固定 JSON 並以非零 exit code 表達 fail-closed 結果。"""
    arguments = parse_arguments()
    if arguments.build:
        build_artifacts()
    report = validate(arguments.matrix, arguments.manifests, arguments.reference_scan, arguments.enforce_p7_5)
    sys.stdout.write(json.dumps(report, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n")
    return 0 if report["outcome"] == "valid" else 1


if __name__ == "__main__":
    raise SystemExit(main())
