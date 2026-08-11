# P7.2 Slice C Fresh Preflight Probe — 設計審查

## 結論先行

**Go**，此能力可安全新增。四個必要的判定（request shape、五個 operational list 的 task-owned 驗證、task-marked leader 的 owner/active 判定、weekly-report cardinality/Sunday-date 判定）在既有 `P72FreshSliceCFixtureProvisioner.Provision()`（`ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureProvisioner.cs:294-331`）中**已經**是純讀取（`Retrieve`/`RetrieveMultiple`），並且**已經**在任何 `Create` 之前執行完畢才回傳 `fixture-precondition-failed`。新探測器不需要新寫任何 CRM 存取邏輯，只需要把這段既有前置驗證抽出、獨立掛一條新的唯讀 lane。

---

## 1. 最安全的整合點與 parameter-set 邊界

- **PS1 (`docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`)**：新增一個獨立 `ParameterSetName = 'FreshPreflightProbe'`（例如 `-ProbeFreshFixturePreflight`），與 `ExecuteFixture` / `ReconcileFixture` / `RepairFixture` / `RepairProbe` / `ProvisionFreshFixture` / `CleanupFreshFixture` **互斥**（binder 層級即拒絕併用，同 `-ExecuteFixture`/`-ReconcileFixture` 現有互斥模式）。
  - **不**接受 `-ReplaceStaleDescriptor` / `-ConfirmFreshFixtureCleanup`：這兩者是 mutation 的明確授權旗標，探測器沒有 mutation，強制不能出現在同一個 parameter set，避免操作者誤以為探測也需要「確認破壞性動作」。
  - 仍沿用 `RepositoryPath` / `ProfileInputPath` / `SourceFixtureDescriptorPath` / `FixtureDescriptorPath`，並沿用既有 local-descriptor gate（`Test-SourceFixtureDescriptor` / `Test-SliceCFixtureDescriptor`，PS1:2353-2361）——這一步在讀 Credential Manager 之前，失敗即 fail-closed，零 CE I/O。
  - **關鍵邊界**：探測器**絕不**讀取或寫入 `fresh-slice-c-ledger.json`（PS1:2398-2438 的 pending-ledger 分支必須整段跳過）。因為它不是 provisioning stage machine 的一部分，若讓它去讀/寫 ledger，會製造「哪個 lane 寫了 ledger」的新歧義，破壞現有 `Get-StrictFreshFixtureLedger` 的單一 writer 假設。這也是「no cross-user/profile state retention」最乾淨的做法——沒有 ledger 接觸，天然滿足。
  - 仍需 `Test-CredentialTargetPresent` + `Get-P72CredentialPassword`（PS1:2441-2468）：探測必須做 WhoAmI 以取得 `Data8ServiceUserId` 來比對 leader owner，因此仍需認證，但這只是讀取，不是規格禁止的動作。

- **Child dispatch**：比照既有模式（PS1:2573-2614），新增一條 `dotnet test --filter FullyQualifiedName=...` 指向新的 `[Fact]`，並用新的、加入 allowlist 的環境變數閘門（如 `SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE`，補進 PS1:134-150 的 `$freshSliceCControlPlaneEnvironmentNames`，使其被 snapshot/清空/finally 還原邏輯正確納管）。

- **C# 層**：把 `P72FreshSliceCFixtureProvisioner` 內的四個私有判定方法（`IsRequestShapeValid`、`AreOperationalListsTaskOwned`、`TryResolveActiveBaselineOwner`、`TryResolveExactlyOneActiveWeeklyReport`，檔案:537-653）抽成一個 `internal` 方法（例如 `Preflight(request)`），回傳四個 bool 的 record。`Provision()` 內部呼叫同一個方法（取代目前 294-331 行內聯的 `if`），確保探測器與正式 provision 的判定邏輯**永遠同步**，不會出現「探測說 go，正式 provision 卻 no-go」的邏輯漂移。新的 xunit `[Fact]` 只呼叫 `Preflight()`，全程不呼叫 `Create`/`Update`/`Execute(Assign/Associate/Disassociate...)`。

---

## 2. 建議的 sanitized evidence schema 與允許值

沿用既有 envelope 慣例（`schemaVersion`/`lane`/`outcome`/`reason`/`operationExecuted`/`descriptorPublicationReady`/`featureFlagChanged`，見 `Get-StrictFreshFixtureEvidenceFile`，PS1:1178-1186），但新增一個封閉的 `preflight` 巢狀物件，把「哪一段前置證明失敗」以布林/enum 攤平，取代目前單一 `fixture-precondition-failed` 的黑箱：

```jsonc
{
  "schemaVersion": 1,
  "lane": "preflight-probe",
  "outcome": "go" | "no-go",
  "reason": "fresh-preflight-probed",
  "operationExecuted": false,          // 恆為 false，強制驗證
  "descriptorPublicationReady": false, // 恆為 false，強制驗證
  "featureFlagChanged": false,         // 恆為 false，強制驗證
  "preflight": {
    "requestShapeValid": true|false,
    "operationalListsTaskOwned": true|false,
    "leaderProvenanceValid": true|false,        // marker + owner reference 型別正確
    "leaderOwnerActive": true|false,             // systemuser.isdisabled = false
    "leaderOwnerDiffersFromServiceUser": true|false, // 只在上兩項都 true 時才有意義
    "weeklyReportExactlyOneActiveSundayMatch": true|false,
    "go": true|false                             // 五項全 true 時才 true，等同 outcome
  }
}
```

- **允許值收斂**：`reason` 固定只有一個字串（例如 `fresh-preflight-probed`），因為「哪裡失敗」已經由 `preflight.*` 布林攤平，不需要再疊加一組新的 reason vocabulary 去重複表達同一件事（避免兩套分類互相矛盾）。
- `outcome`/`Test-StrictPropertyNames` 沿用既有 `-cnotin` 嚴格 allowlist 比對模式（PS1:1220-1230），任何未知欄位 = fail closed。
- **明確排除**：leader contact ID、owner GUID、list GUID/name、weekly report ID、WhoAmI user 的 domain login/GUID、任何時間戳記、raw exception、raw CRM response——全部不得出現，這點與現有 `New-HandoffResult` 的「不接受 path/descriptor/GUID/identity/credential/exception 作為欄位」原則一致（PS1:213-216）。

---

## 3. 濫用/洩漏與生命週期風險

- **判定顆粒度即資訊揭露量**：五個布林已經是規格要求的最大允許顆粒度（provenance / active / diff-from-service-user / cardinality / final go）。不應再往下拆（例如區分「leader 不存在」vs「leader 存在但缺 marker」vs「owner 欄位型別錯誤」），因為那會把 CRM schema 內部細節（field 存在性、型別）當成 oracle 洩漏出去，超出規格授權範圍。
- **重複呼叫的認證讀取 oracle**：探測器等同一個「已知 ID 集合上的唯讀狀態機」，威脅模型與現有 `ReconcileFixture`（WhoAmI + Retrieve + RetrieveMultiple 的唯讀 lane）完全相同——都需要本機 descriptor + Credential Manager entry + 目前 Windows identity 才能執行，不構成新的攻擊面擴大。不需要額外 rate limiting。
- **ledger 隔離是最大的生命週期風險點**：如果實作不慎讓探測器讀取或寫入 `fresh-slice-c-ledger.json`，會破壞「單一 writer、單一 stage machine」不變量，且可能讓一次單純的診斷呼叫意外阻擋（或誤判為滿足）後續真正的 `-ProvisionFreshFixture -ReplaceStaleDescriptor` pending-ledger 檢查（PS1:2403-2407）。設計上必須讓探測 lane 完全繞過這兩行分支。
- **temp evidence 檔案與 process 生命週期**：沿用既有 parent-owned temp directory + finally 整目錄刪除模式（`New-TemporaryCleanupFailureResult`，PS1:320-333 附近的邏輯），任何 cleanup 無法證明成功時一律 No-Go，不得輸出已計算的 go 結果。
- **例外路徑必須同樣零 mutation**：C# 測試需覆蓋「WhoAmI 逾時」「Retrieve 拋例外」等路徑，此時 fake `IOrganizationService` 的 `Create`/`Update`/`Execute`(非 WhoAmI)/`Assign`/`Associate`/`Disassociate` 必須全部標記為「呼叫即失敗」（沿用 `ThrowUnexpectedMutation<T>()` 模式，見 `P72Data8ListManagementFreshFixtureProvisionerTests.cs:854/1007/1251`），確保例外分支也不會意外觸發 mutation。

---

## 4. 具體測試案例與可能受影響的檔案

**受影響檔案**
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`：新增 ParameterSet、env var allowlist、child dispatch 分支（比照 2573-2614）、新的嚴格 evidence reader（比照 `Get-StrictSliceCRepairProbeEvidenceFile`/`Get-StrictFreshFixtureEvidenceFile`）、最終 console 投影分支（比照 2702-2761）。
- `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureProvisioner.cs`：抽出共用 `Preflight()` 方法。
- `ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementFreshFixtureTests.cs`（或新檔）：新增 `[Fact]`，env var 閘門化，寫出 sanitized evidence。
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`、`Invoke-Package02Data8ListManagementFreshFixture.Tests.ps1`：Pester 測試。
- 可能新增 `P72FreshSliceCFixturePreflightProbeTests.cs`（單元測試，比照 `P72Data8ListManagementFreshFixtureProvisionerTests.cs` 的 fake service 結構）。

**具體測試案例**
1. 五個布林各自獨立失敗（request shape 缺一個 GUID / 非 Sunday；某個 list 的 `createdfromcode`≠2；leader 缺 marker；owner `isdisabled=true`；owner == Data8ServiceUserId；weekly report 0 筆/2 筆/日期不符）→ 對應布林為 `false`，`outcome=no-go`，其餘布林不受污染。
2. 五個布林全 true → `outcome=go`，`preflight.go=true`。
3. 全流程中 fake service 記錄的 `Create`/`Update`/`Execute`(mutation request)/`Assign`/`Associate`/`Disassociate` 呼叫次數在**每一種**上述結果（含 CRM 逾時/例外路徑）下都是 0。
4. `-ProbeFreshFixturePreflight` 與 `-ExecuteFixture`/`-ReconcileFixture`/`-RepairFixture`/`-RepairProbe`/`-ProvisionFreshFixture`/`-CleanupFreshFixture`/`-ReplaceStaleDescriptor`/`-ConfirmFreshFixtureCleanup` 併用時被 binder 拒絕。
5. 探測前後 `fresh-slice-c-ledger.json`（存在或不存在時）內容/mtime 完全不變（讀寫皆未發生）。
6. 探測結果 JSON 通過既有 `Test-StrictPropertyNames` 嚴格比對，任何未知欄位 fail closed；UTF-8 no-BOM、CRLF-only、final CRLF 驗證。
7. 新 env var 只允許 parent 顯式建立的 binding 傳給 child；未知同 namespace 變數在 snapshot/clear/finally-restore 生命週期中正確處理（沿用既有 legacy inventory 機制）。
8. 跨兩次不同 identity 的探測呼叫互不留下可觀察狀態（無 process/env/temp 殘留）。

---

## 5. 是否可重用既有 repair/reconcile mode

**不建議直接重用，應仿其模式新開一條 sibling lane。**

- **`RepairProbe`**：其 evidence 契約（`probeStage`/`ownerBinding`/`states`）與允許值是為「既有 relationship-list 修復」領域打造，不涉及 fresh-fixture 的五個 operational list、weekly-report cardinality/Sunday-date 這些欄位。要塞入這些欄位就必須放寬其封閉 `expectedNames`/`allowedReasons` allowlist，等於把兩個不同生命週期的證據契約混在一起，違反目前「每條 lane 各自封閉 schema」的不變量。
- **`ReconcileFixture`**：結構上最接近（同樣是 WhoAmI + Retrieve + RetrieveMultiple 的唯讀 lane），但其語意固定綁在「既有／生產 fixture」的 baseline-unprovable/cleanup-failure 分類，同樣沒有承載 fresh descriptor 的五個 list ID 與 `TransferWeekStartUtc` 的位置。
- 正確做法是**比照 `ProvisionFreshFixture`/`CleanupFreshFixture` 的既有模式**（同樣的 local descriptor 驗證、control-plane-root 驗證、env var allowlist 手法）新開一條 `FreshPreflightProbe` lane，但**跳過**兩者共有的 ledger 讀寫分支，並用全新、自成一體的 `preflight` schema。這樣既重用了「四個唯讀判定」的既有實作（透過抽出 `Preflight()` 共用方法），又不破壞任何現有 lane 的封閉契約。

---
SESSION_ID: fbe0a946-815f-4bb5-b95c-7ce055811f9e
