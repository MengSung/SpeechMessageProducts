"""Deterministic, offline P7.2 coverage gate.

The validator intentionally reads only version-controlled files.  It never
opens a browser, reads Credential Manager, contacts CE, or consumes a secret.
It separates implementation coverage from live evidence so a pending fixture
cannot be mistaken for a completed capability.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any, Iterable


def find_repository_root(start: Path) -> Path:
    for candidate in (start, *start.parents):
        if (candidate / ".trellis" / "tasks").is_dir():
            return candidate
    raise RuntimeError("repository-root-not-found")


TASK_DIR = Path(__file__).resolve().parent
REPOSITORY_ROOT = find_repository_root(TASK_DIR)
DEFAULT_MATRIX = TASK_DIR / "p7.2-fixture-activation-matrix.json"
SOURCE_MATRIX = (
    REPOSITORY_ROOT
    / ".trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json"
)

OPERATION_PATTERN = re.compile(r"^[a-z0-9]+(?:\.[a-z0-9]+)+$")
REQUIRED_STATUS = "required-for-activation"
SLICE_C_LIVE_FIXTURE_PATH = (
    "ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementEvidenceTests.cs"
)
SMALL_GROUP_OPERATION_ID = "listmanagement.smallgroup.update.fields"

OPERATION_CONTRACTS: dict[str, dict[str, Any]] = {
    "memberinfo.contact.update.basic.info": {
        "constant": "MemberInfoContactUpdateBasicInfo",
        "dto": ["ContactBasicInfoUpdateRequest", "ContactBasicInfoUpdateResult"],
        "client": ["IPackage02ContactBasicInfoUpdateClient", "Package02ContactBasicInfoUpdateClient"],
        "fixture": "ChurchReport.MemberInfo.Tests/P72ContactBasicInfoFixtureBridge.cs",
    },
    "memberinfo.contact.update.line.profile": {
        "constant": "MemberInfoContactUpdateLineProfile",
        "dto": ["ContactLineProfileUpdateRequest", "ContactLineProfileUpdateResult"],
        "client": ["IPackage02ContactProfileClient", "Package02ContactProfileClient"],
        "fixture": "ChurchReport.MemberInfo.Tests/P72ContactProfileFixtureBridge.cs",
    },
    "memberinfo.contact.count.ungrouped.commitment": {
        "constant": "MemberInfoContactCountUngroupedCommitment",
        "dto": ["UngroupedCommitmentCountRequest", "UngroupedCommitmentCountResult"],
        "client": ["IPackage02ContactProfileClient", "Package02ContactProfileClient"],
        "fixture": "ChurchReport.MemberInfo.Tests/P72ContactProfileFixtureBridge.cs",
    },
    "list.members.add.many": {
        "constant": "ListMembersAddMany",
        "dto": ["StaticListMembersAddRequest", "StaticListMembershipMutationResult"],
        "client": ["IPackage02ListManagementClient", "Package02ListManagementClient"],
        "fixture": "ChurchReport.MemberInfo.Tests/P72ListManagementFixtureBridge.cs",
    },
    "list.members.remove.one": {
        "constant": "ListMembersRemoveOne",
        "dto": ["StaticListMemberRemoveRequest", "StaticListMembershipMutationResult"],
        "client": ["IPackage02ListManagementClient", "Package02ListManagementClient"],
        "fixture": "ChurchReport.MemberInfo.Tests/P72ListManagementFixtureBridge.cs",
    },
    "listmanagement.smallgroup.update.fields": {
        "constant": "ListManagementSmallGroupUpdateFields",
        "dto": ["SmallGroupFixedFieldsUpdateRequest", "SmallGroupFixedFieldsMutationResult"],
        "client": ["IPackage02ListManagementClient", "Package02ListManagementClient"],
        "fixture": "ChurchReport.MemberInfo.Tests/P72ListManagementFixtureBridge.cs",
    },
    "contact.assign.owner": {
        "constant": "ContactAssignOwner",
        "dto": ["ContactOwnerAssignmentRequest", "ContactOwnerAssignmentResult"],
        "client": ["IPackage02ListManagementClient", "Package02ListManagementClient"],
        "fixture": "ChurchReport.MemberInfo.Tests/P72ListManagementFixtureBridge.cs",
    },
    "newperson.contact.transfer.between.lists": {
        "constant": "NewPersonContactTransferBetweenLists",
        "dto": ["ContactListTransferRequest", "ContactListTransferResult"],
        "client": ["IPackage02ListManagementClient", "Package02ListManagementClient"],
        "fixture": "ChurchReport.MemberInfo.Tests/P72ListManagementFixtureBridge.cs",
    },
}


def load_json(path: Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"invalid-json:{path}") from exc


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        raise ValueError(f"unreadable-source:{path}") from exc


def path_for(relative: str) -> Path:
    return REPOSITORY_ROOT / relative.replace("/", "\\")


def add_error(
    errors: list[dict[str, str]],
    rule_id: str,
    slice_id: str,
    operation_id: str,
    detail: str,
) -> None:
    errors.append(
        {
            "ruleId": rule_id,
            "sliceId": slice_id,
            "operationId": operation_id,
            "detail": detail,
        }
    )


def validate_slice_c_relationship_mode(errors: list[dict[str, str]]) -> None:
    """驗證 Slice C 實機路徑確實使用 relationship-bound area-leader 語意。

    smallGroupExpectedRelationshipListId 不是單純的 preflight scalar：它提供的 area
    leader/name 必須同時參與 TryProveFixtureGraph 與一次 bounded bridge dispatch。因此
    這個離線 gate 只讀取固定 C# 測試 source，確認兩個固定 call-site 都選擇
    ChangeAreaLeader 並傳遞同一個 descriptor field；若 source 不存在、讀取失敗或任一
    call-site 漂移，加入 sanitized no-go rule。它不建立 CRM/Data8 client、credential、
    process、cache 或跨驗證可變狀態。
    """
    source_path = path_for(SLICE_C_LIVE_FIXTURE_PATH)
    try:
        source = read_text(source_path)
    except ValueError:
        add_error(
            errors,
            "P72-SMALL-GROUP-RELATIONSHIP-MODE",
            "small-group-fixed-fields",
            SMALL_GROUP_OPERATION_ID,
            "live fixture source is unreadable",
        )
        return

    dispatch_uses_area_mode = re.search(
        r"ExecuteSmallGroupFieldsAsync\(\s*client,\s*store,\s*fixture\.SmallGroupListId,\s*"
        r"SmallGroupFixedFieldsUpdateMode\.ChangeAreaLeader,\s*"
        r"fixture\.SmallGroupTargetLeaderContactId,\s*"
        r"fixture\.SmallGroupExpectedRelationshipListId,",
        source,
    )
    preflight_uses_area_mode = re.search(
        r"ResolveSmallGroupExpected\(\s*fixture\.SmallGroupListId,\s*"
        r"SmallGroupFixedFieldsUpdateMode\.ChangeAreaLeader,\s*"
        r"fixture\.SmallGroupTargetLeaderContactId,\s*"
        r"fixture\.SmallGroupExpectedRelationshipListId\)",
        source,
    )
    if dispatch_uses_area_mode is None or preflight_uses_area_mode is None:
        add_error(
            errors,
            "P72-SMALL-GROUP-RELATIONSHIP-MODE",
            "small-group-fixed-fields",
            SMALL_GROUP_OPERATION_ID,
            "live preflight and dispatch must use ChangeAreaLeader with the descriptor relationship list",
        )


def operation_ids_in_artifact(value: Any) -> set[str]:
    """Collect operation IDs from the fixed, sanitized evidence shapes."""
    found: set[str] = set()
    if isinstance(value, dict):
        for key, item in value.items():
            if key in {"operationId", "operationIds"}:
                if isinstance(item, str):
                    found.add(item)
                elif isinstance(item, list):
                    found.update(x for x in item if isinstance(x, str))
            else:
                found.update(operation_ids_in_artifact(item))
    elif isinstance(value, list):
        for item in value:
            found.update(operation_ids_in_artifact(item))
    return found


def evidence_is_complete(path: Path, expected_operations: set[str]) -> tuple[bool, str]:
    if not path.is_file():
        return False, "evidence-artifact-missing"
    try:
        artifact = load_json(path)
    except ValueError:
        return False, "evidence-artifact-invalid"
    if artifact.get("schemaVersion") != "p7.2.live-evidence.v1":
        return False, "evidence-schema-mismatch"
    if artifact.get("connector") != "Data8" or artifact.get("ceVersion") != "9.1":
        return False, "evidence-profile-mismatch"
    if artifact.get("profileAlias") != "sunnyvalechback":
        return False, "evidence-organization-mismatch"
    if artifact.get("sensitiveDataIncluded") is not False:
        return False, "evidence-sensitive-data-flag"
    preflight = artifact.get("preflight")
    execution = artifact.get("execution")
    if not isinstance(preflight, dict) or preflight.get("outcome") != "go":
        return False, "evidence-preflight-not-go"
    if not isinstance(execution, dict) or execution.get("outcome") != "go":
        return False, "evidence-execution-not-go"
    if execution.get("operationExecuted") is not True:
        return False, "evidence-operation-not-executed"
    if execution.get("featureFlagChanged") is not False:
        return False, "evidence-feature-flag-changed"
    operation_ids = operation_ids_in_artifact(artifact)
    if not expected_operations.issubset(operation_ids):
        return False, "evidence-operation-missing"
    # Every mutation evidence must prove cleanup; read-only aggregate evidence
    # proves parity instead.  The fixed nested keys avoid accepting arbitrary
    # caller-supplied JSON as proof.
    for key, branch in execution.items():
        if not isinstance(branch, dict) or key in {"preflight", "execution"}:
            continue
        if branch.get("outcome") == "go" and "cleanupState" in branch:
            if branch.get("cleanupState") != "restored":
                return False, "evidence-cleanup-not-restored"
        if branch.get("outcome") == "go" and "parityState" in branch:
            if branch.get("parityState") != "confirmed":
                return False, "evidence-parity-not-confirmed"
    if "cleanupState" in execution and execution.get("cleanupState") != "restored":
        return False, "evidence-cleanup-not-restored"
    return True, ""


def validate(matrix_path: Path) -> dict[str, Any]:
    errors: list[dict[str, str]] = []
    try:
        matrix = load_json(matrix_path)
        source = load_json(SOURCE_MATRIX)
    except ValueError as exc:
        return {
            "schemaVersion": "p7.2.coverage-report.v1",
            "outcome": "error",
            "summary": {"requiredSliceCount": 0, "operationCount": 0, "completeSliceCount": 0, "pendingSliceCount": 0, "errorCount": 1},
            "slices": [],
            "errors": [{"ruleId": "P72-INPUT", "sliceId": "", "operationId": "", "detail": str(exc)}],
        }

    if matrix.get("schemaVersion") != "p7.2.fixture-activation.v1":
        errors.append({"ruleId": "P72-MATRIX-SCHEMA", "sliceId": "", "operationId": "", "detail": "schemaVersion mismatch"})
    if matrix.get("defaultDispatch") != "fail-closed" or matrix.get("allowedConnector") != "Data8":
        errors.append({"ruleId": "P72-FAIL-CLOSED", "sliceId": "", "operationId": "", "detail": "matrix must select Data8 and fail-closed dispatch"})
    validate_slice_c_relationship_mode(errors)
    source_rows = source.get("normalizedCallSites", [])
    source_by_operation: dict[str, list[dict[str, Any]]] = {}
    for row in source_rows:
        operation_id = row.get("capabilityOperationId")
        if isinstance(operation_id, str):
            source_by_operation.setdefault(operation_id, []).append(row)

    required_slices = [s for s in matrix.get("slices", []) if s.get("status") == REQUIRED_STATUS]
    seen_operations: set[str] = set()
    reports: list[dict[str, Any]] = []
    complete_count = 0

    for slice_data in sorted(required_slices, key=lambda item: str(item.get("id", ""))):
        slice_id = str(slice_data.get("id", ""))
        operation_ids = [str(x) for x in slice_data.get("operationIds", [])]
        missing: list[str] = []
        for operation_id in operation_ids:
            contract = OPERATION_CONTRACTS.get(operation_id)
            if not OPERATION_PATTERN.fullmatch(operation_id):
                add_error(errors, "P72-OPERATION-ID", slice_id, operation_id, "operation ID is not canonical")
            if operation_id in seen_operations:
                add_error(errors, "P72-OPERATION-DUPLICATE", slice_id, operation_id, "operation is assigned to more than one required slice")
            seen_operations.add(operation_id)
            if contract is None:
                add_error(errors, "P72-OPERATION-UNKNOWN", slice_id, operation_id, "operation is not in the P7.2 contract allowlist")
                missing.append("contract")
                continue
            if not source_by_operation.get(operation_id):
                add_error(errors, "P72-SOURCE-MATRIX", slice_id, operation_id, "operation is absent from the Phase 0 source matrix")
                missing.append("source")
            constant = contract["constant"]
            operation_ids_file = path_for("SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs")
            registry_file = path_for("SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs")
            executor_file = path_for("SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs")
            connector_file = path_for("SpeechMessage.Dynamics.Connectors.Data8/Package02Data8ListManagementOperations.cs")
            operation_ids_text = read_text(operation_ids_file) if operation_ids_file.is_file() else ""
            operation_constant_pattern = re.compile(
                rf"public\s+const\s+string\s+{re.escape(constant)}\s*=\s*(?:\r?\n\s*)?\"{re.escape(operation_id)}\""
            )
            for label, file_path, marker in (
                ("registry", registry_file, f"OperationIds.{constant}"),
                ("executor", executor_file, f"OperationIds.{constant}"),
            ):
                if not file_path.is_file() or marker not in read_text(file_path):
                    add_error(errors, f"P72-{label.upper()}", slice_id, operation_id, f"{label} implementation marker is missing")
                    missing.append(label)
            if not operation_constant_pattern.search(operation_ids_text):
                add_error(errors, "P72-OPERATION-CONSTANT", slice_id, operation_id, "operation constant implementation marker is missing")
                missing.append("operation-constant")
            # The Package02 list-management connector owns Slice C.  The
            # earlier A/B slices intentionally use their own connector files.
            if operation_id in {
                "list.members.add.many",
                "list.members.remove.one",
                "listmanagement.smallgroup.update.fields",
                "contact.assign.owner",
                "newperson.contact.transfer.between.lists",
            }:
                connector_text = read_text(connector_file) if connector_file.is_file() else ""
                if operation_id not in connector_text and f"OperationIds.{constant}" not in connector_text:
                    add_error(errors, "P72-CONNECTOR", slice_id, operation_id, "Data8 connector implementation marker is missing")
                    missing.append("executor")
            dto_file = path_for("SpeechMessage.Dynamics.ProductClient/ListManagement/IPackage02ListManagementClient.cs")
            client_file = path_for("SpeechMessage.Dynamics.ProductClient/ListManagement/Package02ListManagementClient.cs")
            # A/B slices use their established MemberInfo files; Slice C uses
            # the new list-management ProductClient.
            if operation_id.startswith("memberinfo.contact.update.basic"):
                dto_file = path_for("SpeechMessage.Dynamics.ProductClient/MemberInfo/IPackage02ContactBasicInfoUpdateClient.cs")
                client_file = path_for("SpeechMessage.Dynamics.ProductClient/MemberInfo/Package02ContactBasicInfoUpdateClient.cs")
            elif operation_id.startswith("memberinfo.contact.update.line") or operation_id.startswith("memberinfo.contact.count"):
                dto_file = path_for("SpeechMessage.Dynamics.ProductClient/MemberInfo/IPackage02ContactProfileClient.cs")
                client_file = path_for("SpeechMessage.Dynamics.ProductClient/MemberInfo/Package02ContactProfileClient.cs")
            dto_text = read_text(dto_file) if dto_file.is_file() else ""
            client_text = read_text(client_file) if client_file.is_file() else ""
            for marker in contract["dto"]:
                if marker not in dto_text:
                    add_error(errors, "P72-DTO", slice_id, operation_id, f"DTO marker missing: {marker}")
                    missing.append("dto")
            for marker in contract["client"]:
                if marker not in dto_text and marker not in client_text:
                    add_error(errors, "P72-CONSUMER", slice_id, operation_id, f"typed consumer marker missing: {marker}")
                    missing.append("consumer")

        for field in ("fixtureOwner", "rollbackOwner", "lifecycleOwner", "cleanup", "reconciliation", "ambiguousTimeoutPolicy"):
            value = slice_data.get(field)
            if not isinstance(value, str) or not value.strip():
                add_error(errors, "P72-BOUNDARY", slice_id, "|".join(operation_ids), f"required boundary field missing: {field}")
                missing.append(field)
        lifecycle_owner = str(slice_data.get("lifecycleOwner", ""))
        if lifecycle_owner and not all(token in lifecycle_owner.lower() for token in ("request", "data8", "lease")):
            add_error(errors, "P72-LIFECYCLE-OWNER", slice_id, "|".join(operation_ids), "lifecycle owner must name request-scoped Data8 lease ownership")
            missing.append("lifecycleOwner")

        fixture_path = path_for(OPERATION_CONTRACTS.get(operation_ids[0], {}).get("fixture", "")) if operation_ids else Path("__missing__")
        if not fixture_path.is_file():
            add_error(errors, "P72-FIXTURE", slice_id, "|".join(operation_ids), f"fixture bridge missing: {fixture_path.relative_to(REPOSITORY_ROOT) if fixture_path.is_absolute() and fixture_path.exists() else fixture_path}")
            missing.append("fixture")

        evidence = slice_data.get("realCeEvidence") or {}
        artifact_value = evidence.get("artifact") if isinstance(evidence, dict) else None
        evidence_ok = False
        evidence_reason = "evidence-artifact-missing"
        if evidence.get("ce82") not in {"unsupported", "not-supported", "pending"}:
            add_error(errors, "P72-CE82", slice_id, "|".join(operation_ids), "CE 8.2 must remain unsupported/fail-closed for this Data8 slice")
            missing.append("ce82-policy")
        if evidence.get("ce91") != "complete" or not isinstance(artifact_value, str):
            evidence_reason = "matrix-evidence-pending"
        else:
            evidence_ok, evidence_reason = evidence_is_complete(path_for(artifact_value), set(operation_ids))
        if not evidence_ok:
            add_error(errors, "P72-LIVE-EVIDENCE", slice_id, "|".join(operation_ids), evidence_reason)
            missing.append("evidence")

        missing = sorted(set(missing))
        if not missing:
            complete_count += 1
        reports.append(
            {
                "sliceId": slice_id,
                "operationIds": sorted(operation_ids),
                "status": "complete" if not missing else "pending",
                "missing": missing,
            }
        )

    errors.sort(key=lambda item: (item["ruleId"], item["sliceId"], item["operationId"], item["detail"]))
    reports.sort(key=lambda item: item["sliceId"])
    operation_count = sum(len(report["operationIds"]) for report in reports)
    return {
        "schemaVersion": "p7.2.coverage-report.v1",
        "outcome": "go" if not errors else "no-go",
        "sourceMatrixSha256": sha256(SOURCE_MATRIX) if SOURCE_MATRIX.is_file() else None,
        "summary": {
            "requiredSliceCount": len(reports),
            "operationCount": operation_count,
            "completeSliceCount": complete_count,
            "pendingSliceCount": len(reports) - complete_count,
            "errorCount": len(errors),
        },
        "slices": reports,
        "errors": errors,
    }


def main(argv: Iterable[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate P7.2 Data8-first coverage without external access.")
    parser.add_argument("--matrix", type=Path, default=DEFAULT_MATRIX)
    args = parser.parse_args(list(argv) if argv is not None else None)
    try:
        report = validate(args.matrix.resolve())
    except Exception as exc:  # malformed local input is an explicit error, never a pass
        report = {
            "schemaVersion": "p7.2.coverage-report.v1",
            "outcome": "error",
            "summary": {"requiredSliceCount": 0, "operationCount": 0, "completeSliceCount": 0, "pendingSliceCount": 0, "errorCount": 1},
            "slices": [],
            "errors": [{"ruleId": "P72-VALIDATOR", "sliceId": "", "operationId": "", "detail": str(exc)}],
        }
    print(json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True))
    return 0 if report["outcome"] == "go" else 2 if report["outcome"] == "no-go" else 1


if __name__ == "__main__":
    raise SystemExit(main())
