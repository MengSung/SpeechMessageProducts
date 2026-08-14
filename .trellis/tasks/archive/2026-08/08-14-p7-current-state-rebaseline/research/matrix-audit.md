# Research: matrix-audit

- Query: 審核 P7 目前狀態 rebaseline 的 matrix 權威來源、必要欄位、70-row 可重建計數，以及不可由本機證據升格的欄位。
- Scope: internal
- Date: 2026-08-14

## Findings

### 1. 權威來源與證據層級

| 層級 | 權威來源 | 可證明的事實 | 不可取代的來源 |
| --- | --- | --- | --- |
| 70-row source inventory | `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json:569` 的 `normalizedCallSites`，及 `.trellis/tasks/archive/2026-08/08-05-gateway-capability-inventory/coverage-matrix.json:2` 的 `callSites` | 70 個 immutable `ORG-CALL-*` ID、operation ID/kind 與 capability family | registry、ProductClient 或單一 child task 都不可自行新增第 71 個 source row。|
| source schema | `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json:7-18,311-334` | source matrix 根節點/row 的封閉欄位契約；`derivedOperationMappings` 只能追溯既有 source row（同檔:103-106） | rebaseline 簡表不能反向改寫 source inventory。|
| current rebaseline 派生輸出 | `.trellis/tasks/08-14-p7-current-state-rebaseline/reference-current-matrix.json:2-3,2665-2669` | 本次 source hash `52327c15e33a62fe64a59ee73c9adf9051a5e6648c41ae903fdb853138c9b503` 下的 70-row offline repository-source snapshot | 它是 source/程式碼掃描的派生物，非 CE、host 或 traffic 證據。|
| 重建演算法 | `.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/build_rebaseline.py:128-168,202-211,279-365` | source join、hash、registry/executor/ProductClient/consumer 狀態與 P7.5 blocker 的可重現規則 | 歷史 JSON 不應手工修補後當作重建結果。|
| 編譯中 local implementation evidence | `SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs:117,164`、`Package01OperationRegistry.cs:384-401,501-515`、`Data8ProfileOperationExecutor.cs:320-365,435-466`、ProductClient 檔案 | operation 常數、closed registry、Data8 allowlist/dispatch、typed client 的本機實作事實 | 不足以宣稱 consumer cutover、CE、Dedicated/Embedded parity 或流量。|
| P7.5 prerequisite snapshot | `.trellis/tasks/archive/2026-08/08-13-p75-prerequisite-evidence-zero-reference-gate/p75-prerequisite-evidence-report.json:137-221` | 該 snapshot 的 deterministic `no-go`、production legacy reference / dependency / settings blockers | 不能作為 2026-08-14 current registry 狀態的來源；其 builder 固定讀取 archived matrix（`build_p75_prerequisite_evidence.py:24-27`）。|

結論：目前 70-row identity 的最高權威是 Phase-0 matrix + coverage matrix 的交集；`reference-current-matrix.json` 是本次應交付的可重建派生 baseline。archived `authoritative-gap-matrix.json` 保留歷史快照意義，但其 `sourceMatrix.sha256` 為 `22d28141b0234f63b5e42e6b85f64e34fc1570fd38731aa93d503a79f5934752`，與 current hash 不同，不能再單獨代表 current state。

### 2. 更新後 matrix 的必要欄位與可重建方式

#### 派生 rebaseline matrix 根節點

必要欄位為：

1. `schemaVersion`：`p7.remaining-work.rebaseline.v1`。
2. `sourceMatrix.callSiteCount`：必須為 `70`。
3. `sourceMatrix.sha256`：必須是目前 Phase-0 matrix 原始 bytes 的 SHA-256；current 值為 `52327c15e33a62fe64a59ee73c9adf9051a5e6648c41ae903fdb853138c9b503`。
4. `analysisScope`：`offline-allowlisted-repository-source-only`，防止把未執行的 CE/host/traffic 推論為已發生。
5. `callSites`：按 `callSiteId` 排序、唯一且與 source baseline 完全一致的 70 rows。

每個派生 row 的必要欄位是 archived builder 的 `REQUIRED_ROW_KEYS`（`build_rebaseline.py:99-115`）：

```text
callSiteId
operation { id, kind }
capabilityFamily
operationKind
registry { status }
data8Executor { status }
productClient { status }
consumer { status }
ceEvidence { ce82, ce91 }
hostEvidence { embedded, dedicated }
rollout { owner }
rollback { owner }
temporaryLegacy
specialResourceRequirement
p75RemovalBlocker
```

來源 Phase-0 row 另外必須符合封閉 schema 的 20 個 `normalizedCallSite.required` 欄位：`id`、`product`、`file`、`member`、`legacyEntryPoint`、`currentRequestShape`、`entityOrAction`、`operationKind`、`dataClassification`、`capabilityOperationId`、`serverOwnedTemplate`、`typedParameters`、`encodingContexts`、`versionEvidence`、`smokeEvidence`、`auditRequirement`、`idempotencyClass`、`migrationStatus`、`temporaryLegacyOwner`、`temporaryLegacyRemovalDeadline`（schema:314-334）。已具 compiled registry 的 source row 還需 response kind 與各 response page/byte/item 上限（schema:531-607）。

可重建程序（保持 offline；本研究未寫入或執行它）：

1. UTF-8 讀取 Phase-0 `normalizedCallSites` 與 archived coverage `callSites`。兩方各必須 70、ID 唯一且集合相同；相同 ID 的 operation ID 必須相同（`build_rebaseline.py:128-168`）。
2. 對目前 Phase-0 matrix 的原始 bytes 計算 SHA-256，寫入 `sourceMatrix`；此舉讓任何 source inventory 漂移可被偵測。
3. 以 `OperationIds.cs` 常數為 canonical operation token；去除 C# 註解/字串後，分別掃描 `Package01OperationRegistry.cs`、`Data8ProfileOperationExecutor.cs` 與 `SpeechMessage.Dynamics.ProductClient/**/*.cs` 的 `OperationIds.<constant>` 引用（`build_rebaseline.py:171-211`）。comment/literal-only 引用不得計入實作證據（test:81-96）。
4. 依規則寫入 `registry`/`data8Executor`/`productClient`：無 capability 為 `not-*`；P7.2 local-only allowlist 為 `local-only` / `local-only-rejected` / `not-implemented`；其餘只在該 layer 有上述實際 source reference 時為 `declared`/`implemented`（`build_rebaseline.py:303-314`）。
5. consumer 只能由三個明列的 ChurchReport gated method + `_package01Enabled` 證實，並僅為 `migrated-disabled`；有 ProductClient 本身不可升為 consumer migration（`build_rebaseline.py:79-92,214-222`；test:116-126）。
6. CE/host 只能採 builder 固定 allowlist：P7.1 six reads 可保留 CE 9.1 / Embedded `succeeded`；local-only 是 `not-executed`；Slice C closed operation 必須維持 CE 9.1 `no-go-closed`；其餘為 `evidence-pending`（`build_rebaseline.py:256-276`）。
7. 依 resource family、consumer、legacy dependency 與 CE 9.1 結果計算 `specialResourceRequirement`、`temporaryLegacy`、`p75RemovalBlocker`（`build_rebaseline.py:244-253,320-356`），以 sorted IDs 輸出 UTF-8 no-BOM/CRLF/final CRLF（同檔:415-418）。
8. 驗證 70 count、unique/sorted/source ID equality/source hash/required fields；再套用 local-only、Slice-C、enabled-consumer 與 removal-state fail-closed gates（同檔:368-412）。任何 future build 必須輸出到 current task 所擁有的 path，不能覆寫 archived matrix。

### 3. 70 rows 的可驗證計數（current reference matrix）

下列數字由 `.trellis/tasks/08-14-p7-current-state-rebaseline/reference-current-matrix.json` 的 70 rows 直接 group-by，並與 archived P7.5 report 的 shared matrix aggregates 交叉比對。

| 維度 | 計數 | 合計/檢查 |
| --- | --- | --- |
| `callSiteId` | `ORG-CALL-00001` 至 `ORG-CALL-00070`、70 unique | 70 |
| operation kind | read 35、write 23、action 4、connection-runtime 5、function 2、metadata 1 | 70 |
| registry | declared 28、local-only 13、not-declared 29 | 70 |
| Data8 executor | implemented 27、local-only-rejected 13、not-implemented 30 | 70 |
| ProductClient | implemented 26、not-implemented 44 | 70 |
| consumer | migrated-disabled 3、not-migrated 67 | 70 |
| CE 8.2 | evidence-pending 56、not-executed 14 | 70 |
| CE 9.1 | succeeded 6、evidence-pending 50、not-executed 13、no-go-closed 1 | 70 |
| Embedded host | succeeded 6、evidence-pending 50、not-executed 14 | 70 |
| Dedicated host | evidence-pending 56、not-executed 14 | 70 |
| P7.5 blocker | consumer-not-migrated 49、legacy-sdk-dependency 3、mixed 13、special-resource-pending 5 | 70 |
| temporary legacy | temporary-legacy 70 | 70 |

P7.5 report 的可交叉驗證 aggregate 為：`callSiteCount=70`、`temporaryLegacyCount=70`、`consumerNotMigratedCount=67`、`ceOrHostEvidencePendingCount=70`、`closedHistoricalWriteFamilyCount=1`，且 blocker 分布相同（`p75-prerequisite-evidence-report.json:137-160`）。能力 family 的 row count 亦為 appointments 1、attendance 3、authentication 2、contact.onboarding 1、donation.lifecycle 9、fee.lessons 10、list.membership 23、member.profile 8、metadata 1、weekly.reporting 1、platform.legacy.blocked 5、platform.shared.runtime 6（同檔:3-135），總和 70。

本次 source-hash 更新造成的 current-state 差異只有兩 rows：

| row | operation | archived -> current local implementation 差異 | 仍保持 |
| --- | --- | --- | --- |
| ORG-CALL-00026 | `memberinfo.present.retrieve.by.contact` | registry `not-declared -> declared`、Data8 `not-implemented -> implemented`、ProductClient `not-implemented -> implemented`、rollout/rollback owner `pending -> p7.4-capability-owner` | consumer `not-migrated`、CE 8.2/9.1 `evidence-pending`、Embedded/Dedicated `evidence-pending`、temporary legacy。|
| ORG-CALL-00057 | `list.membership.retrieve.appnamed.by.contact` | 同上四項 local implementation/owner 變更 | consumer、CE/host、temporary legacy 亦全未升格。|

這兩列的 local contract 可由 registry（`Package01OperationRegistry.cs:384-401,501-515`）、Data8 allowlist（`Data8ProfileOperationExecutor.cs:344-347,448-454`）與兩個 typed client 的 source-contract tests（`SpeechMessage.Dynamics.Tests/AppNamedMembershipReadRegistryTests.cs:35-96`、`MemberInfoPresentRecordReadRegistryTests.cs:34-120`）追溯。

### 4. 不可由 local evidence 升格的欄位

1. `registry=declared`、`data8Executor=implemented`、`productClient=implemented` 只證明 checked-in local capability layer；它們不能升格 `consumer.status`。現行測試明確規定 client-only 仍是 `not-migrated`（`test_rebaseline.py:116-126`）。
2. `consumer=migrated-disabled` 只能表示 checked-in gate 的 local route；它不能升格 Dedicated、CE 8.2、traffic 或完整 consumer cutover。`migrated-enabled` 必須同時有 CE 9.1 和 Dedicated `succeeded`（`build_rebaseline.py:401-403`；test:205-220）。
3. Data8/unit/source-contract test、registry response contract 與 ProductClient DTO defensive-copy test 都不是 CE execution evidence、real CRM data correctness、host parity、soak/drain 或 production traffic evidence。這些欄位必須有各自的執行/host/traffic artifact，不能由 source scan 推論。
4. local-only row 不得升格 executor、ProductClient、consumer 或任何 CE success（`build_rebaseline.py:391-397`；test:142-156）。Slice C `listmanagement.smallgroup.update.fields` 的 CE 9.1 `no-go-closed` 也不能改成 pending retry（同檔:398-400；test:158-186）。
5. `rollout.owner` / `rollback.owner` 的 `p7.4-capability-owner` 是責任歸屬 metadata，並非已驗證 rollback drill、切流或 traffic ownership 證據。
6. 任何 local status 不能移除 `temporaryLegacy` 或把 P7.5/P8 升格 ready。P7.5 report 仍是 `no-go`，因為 70 temporary-legacy rows、consumer/CE-host/P7.5 blockers、production legacy references、project dependencies 與 legacy settings key 都尚在（report:162-221；gate logic:621-699）。
7. P7.5 archived report 不能反向證實 current 00026/00057 的 implementation；它的 scanner input 固定為 archived matrix（`build_p75_prerequisite_evidence.py:24-27,554-571`）。雖然本次 two-row implementation 變動不改變它所列的 shared no-go aggregate，若要聲稱「current deterministic P7.5 report」，必須另建 task-owned successor，改以 current matrix 重建並重新驗證，不能手改 archived report。

## Files Found

- `.trellis/tasks/08-14-p7-current-state-rebaseline/reference-current-matrix.json` — current 70-row derived snapshot；含目前 source hash 與兩列更新狀態。
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json` — canonical normalized source inventory。
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json` — source inventory 的封閉 JSON schema。
- `.trellis/tasks/archive/2026-08/08-05-gateway-capability-inventory/coverage-matrix.json` — capability family join source，並明定 layer evidence 互不推論（line 5179）。
- `.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/build_rebaseline.py` — archived but reusable offline rebaseline builder/validator contract。
- `.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/test_rebaseline.py` — row count、checksum、encoding 與 no-promotion regression tests。
- `.trellis/tasks/archive/2026-08/08-13-p75-prerequisite-evidence-zero-reference-gate/p75-prerequisite-evidence-report.json` — historical deterministic P7.5 no-go snapshot。
- `.trellis/tasks/archive/2026-08/08-13-p75-prerequisite-evidence-zero-reference-gate/build_p75_prerequisite_evidence.py` — P7.5 report input/strict-equality/gate rules。
- `SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs`、`Package01OperationRegistry.cs` — compiled operation identities and registry policy。
- `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs` — Data8 server-owned dispatch allowlist。
- `SpeechMessage.Dynamics.ProductClient/MemberInfo/MemberInfoPresentRecordReadClient.cs`、`ListCatalog/AppNamedMembershipReadClient.cs` — rows 00026/00057 typed ProductClient evidence。

## Code Patterns

- Source identity derives from a two-matrix join and fails closed on count, uniqueness, ID-set or operation-ID disagreement: `build_rebaseline.py:128-168`.
- Layer status is computed independently from registry/executor/ProductClient source references, while consumer is a separate explicit gated scan: `build_rebaseline.py:202-222,303-318`.
- Data8 requires a workload ID, successful registry lookup and an allowlisted operation before it creates a connector operation: `Data8ProfileOperationExecutor.cs:320-365`.
- Registry operations are fixed server-owned templates with closed response/page/byte/item policies and SHA-256 template hash: `Package01OperationRegistry.cs:612-678`.
- P7.5 is a fail-closed, strict-equality report/gate; a hand-edited `ready` report becomes `invalid-report`: `build_p75_prerequisite_evidence.py:673-699` and `test_p75_prerequisite_evidence.py:216-229`.

## External References

- 無。本研究限定 offline repository source；未執行 CE、未呼叫外部模型或網路資源。

## Related Specs

- `.trellis/spec/backend/cross-user-isolation-and-performance.md` — local capability/registry 不得跨越 server-validated isolation boundary，並要求 deterministic resource ownership。
- `.trellis/spec/guides/cross-user-isolation-and-performance-review.md` — 審查時不可將 local implementation 與 cross-user isolation、host/traffic 或 lifecycle proof 混為一談。

## Caveats / Not Found

- 未執行 archived builder、validator、tests、CE、feature gate、host 或 traffic；本研究只做靜態讀取與 JSON aggregate。故不宣稱 current matrix 已通過 runtime validation。
- `reference-current-matrix.json` 與 archived matrix 的 source hash 不同，且 archived P7.5 builder 仍固定讀 archived matrix。P7.5 report 的 `no-go` 判定可保留為歷史 blocker evidence，但不能標示為「current-source-hash 已重建」的 report。
- `Package01OperationRegistry.All` 的 29 個 compiled definitions 不等於 29 個 source rows；至少一個是 `derivedOperationMappings` 的 local contract，source inventory 仍必須維持 70 rows。
