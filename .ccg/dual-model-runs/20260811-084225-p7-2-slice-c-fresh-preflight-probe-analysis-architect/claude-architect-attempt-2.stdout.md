# P7.2 Slice C Fresh Preflight Probe — 設計審查

## 現況判讀

`fixture-precondition-failed` 目前是**單一粗粒度分類**，同時代表本地 descriptor 形狀錯誤、五段 list 的靜態驗證失敗，以及（若走到 child）leader/owner/weekly-report 的 CRM 端驗證失敗（`docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1:1193`、`:1300`）。既有唯一的唯讀診斷先例是 `RepairProbe`（`:2078-2180`）與 `ReconcileFixture`（`:1864-1991`）——兩者都已證明「read-only child + fixed-enum probe object + 不授權 mutation」這個模式在此 codebase 是可接受、可稽核的。新 probe 應該複製這個模式，而不是擴充 `ProvisionFresh` 本身的診斷分類。

## 1. 安全整合點與 parameter-set 邊界

- 新增獨立、互斥的 `[Parameter(ParameterSetName = 'FreshPreflightProbe')] [switch] $ProvisionFreshFixturePreflightProbe`，與 `ExecuteFixture`／`ReconcileFixture`／`RepairFixture`／`RepairProbe`／`ProvisionFreshFixture`／`CleanupFreshFixture` 完全互斥（binder 層級拒絕同時指定）。
- **整合點**：放在 `ProvisionFresh` 既有本地驗證之後、fresh ledger/nonce 區塊**之前**（對照 `:2388` 的 `Get-CurrentUserFreshFixtureControlPlaneRoots` 呼叫點）。此 probe 完全不得進入 fresh control-plane 的 ledger／nonce／descriptor-publication 生命週期——它不建立 nonce、不寫 ledger、不呼叫 `Publish-FreshFixtureDescriptorPair`。
- 仍需先過 `Test-Matrix`／`Test-ProfileInput`／`Test-SourceFixtureDescriptor`／`Test-SliceCFixtureDescriptor`（`:880-1150`）等既有本地 gate，並複用（不重寫）這些函式——它們已經是 #1、#2 所需的「descriptor 形狀」與「五段 list 靜態有效性」的證明來源。
- Credential 生命週期沿用 `Test-CredentialTargetPresent` → `Get-P72CredentialPassword`（`:667-878`），與 `RepairProbe` 相同：先確認 target 存在，直到確定要 spawn 唯讀 child 才讀 password，finally 清除。
- 獨立 evidence 檔名與 env var（如 `P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH` / `P72FreshSliceCFixturePreflightProbeEvidence.json`），比照 `:2562-2609` 的互斥 null-out 區塊擴充，避免 child 讀到上一條 lane 殘留的 evidence path。
- 獨立 xUnit 測試過濾器（如 `LivePackage02Data8ListManagementFreshFixtureTests.Preflight_probe_...`），child 端不得呼叫任何既有 mutation-capable 的 `P72ListManagementFixtureBridge` dispatch 方法——建議在 C# 端新增**型別上就唯讀**的 accessor（只暴露 WhoAmI + 兩個 Retrieve/RetrieveMultiple 投影），而非在既有 bridge 上開分支只是「不呼叫」而已，靠型別而非約定保證零 mutation。

## 2. 建議的 sanitized evidence schema

沿用 `New-HandoffResult` 既有的 `-Probe`／`-ProbeStage`／`-ReadOnlyProbeExecuted` 參數（`:208-307`），**不需修改該函式本體**，只需新增一個對照 `Get-StrictSliceCRepairProbeEvidenceFile`（`:2078`）的新 strict parser：

頂層（envelope 與既有一致）：
```
schemaVersion, outcome='no-go'(固定), reason(allowlist),
profileAlias, deploymentProfileAlias, ceVersion, connector,
preflightOnly=true, operationExecuted=false(固定),
readOnlyProbeExecuted, featureFlagChanged=false,
probeStage, probe{...}
```

`probe` 物件（全部 bool 或固定 enum，不含任何 GUID/名稱/日期原值）：
```
descriptorShapeValid: bool                         // 對應需求 1
fixtureListsAggregateValid: bool                    // 對應需求 2（五段 list 靜態驗證彙總）
leaderOwnerKind: 'system-user' | 'team' | 'unavailable'
leaderActive: bool
leaderOwnerMatchesWhoAmI: bool                      // 對應需求 3
leaderProvenanceState: 'task-marked-verified' | 'task-marked-mismatch' | 'unavailable'
transferTargetWeeklyReportCardinality: 'exactly-one' | 'none' | 'multiple' | 'unavailable'  // 對應需求 4
transferTargetWeeklyReportActive: bool
transferTargetWeeklyReportSundayDateProven: bool
preconditionState: 'all-proven' | 'descriptor-invalid' | 'leader-precondition-failed'
                  | 'owner-mismatch' | 'weekly-report-precondition-failed' | 'unavailable'
```

`reason` allowlist（新前綴避免與既有 `ProvisionFresh` diagnosticCategory 混淆）：
`fresh-preflight-preconditions-proven`、`fresh-preflight-precondition-failed`、`baseline-owner-unavailable`、`runtime-failure`、`cleanup-failure`。

第 5 點「最終 go」**不應**外露成獨立 `outcome=go`——比照 `RepairProbe` 的做法：probe 本身永遠是 `outcome='no-go'`（它不是授權），`reason='fresh-preflight-preconditions-proven'` 搭配 `preconditionState='all-proven'` 才是「一切前置條件皆已證明」的訊號，交由操作者自行決定是否接著執行已授權的 `-ProvisionFreshFixture`。這與 `:2158-2163` 對 `readOnlyProbeExecuted`／`reason` 一致性的 fail-closed 檢查是同一原則。

## 3. Abuse/洩漏與生命週期風險

- **權限繞道風險**：此 probe 會讀 password 進 managed memory（child 需要），必須確保它不會變成「不需要 `-ExecuteFixture` 授權就能做等價偵查」的後門。既有 `ReconcileFixture`／`RepairProbe` 已是同等先例，風險可接受，但務必維持相同的 finally 清除保證（`credentialPassword`、`CRM_PASSWORD` env var）。
- **分類混淆風險**：新 `reason` 值絕不可落入既有 `Get-StrictFreshFixtureChildFailureDiagnosticCategory` 的 allowlist（`:1299-1310`），否則可能被誤判為授權下一步 mutation 的訊號。兩份 allowlist 必須嚴格互斥。
- **零 mutation 保證**：不能只靠「沒呼叫」的約定；建議 child 端的唯讀 accessor 與既有 `P72ListManagementFixtureBridge`（可 dispatch add/remove/small-group/owner/transfer）完全分離、不共用可執行 Create/Update/Delete/Assign/Execute/Associate/Disassociate 的程式路徑，讓測試可以用假 OrganizationService 斷言「僅收到 WhoAmI + Retrieve + RetrieveMultiple，其餘方法呼叫次數為 0」。
- **暫存目錄與 cleanup**：沿用 `Remove-OwnedSliceCTemporaryDirectory`（`:2182`）與 `Complete-HandoffResult`（`:2237`）既有保守收斂：無法證明 cleanup 完成一律 `cleanup-failure` + `readOnlyProbeExecuted=false`，不得回傳 Green。
- **跨 user/profile 殘留**：此 probe 完全不進入 fresh ledger 生命週期（不寫 `fresh-slice-c-ledger.json`、不用 nonce），比 `ProvisionFreshFixture` 更單純——沒有跨 session 可殘留的狀態，這點應在測試中明確斷言（執行後 `LOCALAPPDATA` 下無新檔案、無殘留 env var）。

## 4. 具體測試案例與可能受影響檔案

**受影響檔案**：
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`：新 ParameterSet、新 evidence path 常數、新 dispatch 分支、新 `Get-StrictSliceCFreshPreflightProbeEvidenceFile` parser。
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`：新 parser 的 schema 違規測試。
- `ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementFreshFixtureTests.cs`（目前已在 git status 中被修改，顯示 Slice C fresh fixture 基礎設施正在演進中）：新增唯讀 probe 測試方法與對應 `FactAttribute` gate。
- 新增一個唯讀 CRM accessor 類別（WhoAmI + Retrieve + RetrieveMultiple only）。

**測試案例**：
1. 全部前置條件成立 → `reason='fresh-preflight-preconditions-proven'`、`preconditionState='all-proven'`、`readOnlyProbeExecuted=true`。
2. descriptor 形狀不合法（本地檢查）→ 直接 no-go，**不啟動 child**（零 CRM I/O）。
3. leader owner 非目前 WhoAmI 使用者 → `leaderOwnerMatchesWhoAmI=false`、`preconditionState='owner-mismatch'`。
4. leader 非 active → `leaderActive=false`。
5. transfer target weekly report cardinality ≠ 1（0 筆或多筆）→ 對應 enum ≠ `'exactly-one'`。
6. weekly report 非 active 或非週日日期 → 對應 bool=false。
7. child timeout / 非零 exit → 沿用既有 `test-timeout`／`child-process-failed` 路徑，`operationExecuted` 固定 false（因為 `isFreshPreflightProbeMode` 應歸入 `operationMayHaveExecuted=false` 群組，比照 `:96` 的 reconcile/repair-probe 分類）。
8. cleanup 無法證明完成 → `reason='cleanup-failure'`、`readOnlyProbeExecuted=false`。
9. 零 mutation 斷言：fake organization service 記錄呼叫，斷言 Create/Update/Delete/Assign/Execute/Associate/Disassociate 呼叫數皆為 0。
10. 跨 user/profile 隔離：以不同 identity 連續執行兩次，斷言無殘留 env var、無殘留暫存檔、無 ledger 寫入。
11. 既有 `Get-StrictFreshFixtureChildFailureDiagnosticCategory` allowlist 與新 probe 的 `reason` allowlist 互斥性測試（防止分類串接誤用）。

## 5. 是否可重用既有 repair/reconcile mode

**不能直接重用同一個 ParameterSet 或 evidence schema**，但**架構模式應完全比照**：

- `RepairProbe` 的 domain（relationship-list 的 4 個欄位）與本需求（leader 全域 provenance + weekly-report cardinality）完全不同，不能共用同一組 `probe` allowlist。
- `ReconcileFixture` 的 `states`／`ownerBinding`／`probeStage` 欄位設計哲學可借鏡，但它綁定五段 mutation operation 的 baseline 語意，與 fresh provisioning 的前置條件語意不同，也不宜直接複用。
- **應重用的是通用 helper**：`Read-StrictJsonFile`／`Test-StrictPropertyNames`／`New-HandoffResult` 的 `-Probe`/`-ProbeStage`/`-ReadOnlyProbeExecuted` 參數／`Get-P72CredentialPassword`／`Test-CredentialTargetPresent`／`Remove-OwnedSliceCTemporaryDirectory`——這些都是與 domain 無關的安全邊界，理應共用。
- **不應重用**的是 child 端可能與 `RepairProbe` 共用的唯讀 CRM accessor（若該 accessor 目前寫死了 repair 專屬的 entity/attribute 集合）——應另建一份新的唯讀 accessor，以單一職責與 blast-radius 隔離優先於 DRY，避免未來修改 repair 路徑時意外影響這個新 probe 的唯讀保證。

## 結論

無阻塞問題；此設計可行且與既有 codebase 的安全模式（fail-closed strict parser、固定 allowlist、read-only child lane、finally-owned 資源生命週期）完全一致。建議下一步：先定案 `probe` schema 與 `reason` allowlist（本審查已給出草案），再依 `RepairProbe` 的實作結構複製 ParameterSet/parser/dispatch 三段式改動。

---
SESSION_ID: c5414927-048c-4441-8df0-3b9921eda09d
