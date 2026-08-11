# P7.2 Slice C Fresh Preflight Probe — 架構設計審查

（唯讀分析，未變更任何檔案。已讀取 `Invoke-Package02Data8ListManagementEvidence.ps1`、`P72FreshSliceCFixtureProvisioner.cs`、`P72FreshSliceCFixtureLiveEvidence.cs`、`LivePackage02Data8ListManagementFreshFixtureTests.cs` 與既有 `RepairProbe` lane 的完整實作作為基準。）

## 1. 最安全的整合點與 parameter-set 邊界

新增獨立的 `ParameterSetName = 'ProvisionFreshProbe'`，例如 `-ProvisionFreshFixturePreflightProbe`，**不可**掛在既有 `'ProvisionFresh'` set 或共用 `-ReplaceStaleDescriptor`：

- `-ReplaceStaleDescriptor` 目前語意是「明確授權以新 fixture 取代本機 descriptor」，`Invoke-...ps1:2366-2377` 會把它與 `$isFreshProvisionMode` 綁死互斥檢查（`fresh-fixture-confirmation-misused` / `fresh-fixture-confirmation-required`）。probe 永遠不應該持有這個授權語意，讓它落在獨立 parameter set 可以讓 PowerShell binder 在讀 credential、建立 child 之前就天然拒絕 `-ProvisionFreshFixturePreflightProbe -ReplaceStaleDescriptor` 的組合。
- probe 必須**略過** ledger-pending 閘門（`2400-2407`：既有 pending ledger 會擋下新 provision）與 `freshOriginalTargetLeaderContactId` 的擷取（`2411`，這是給 cleanup 用的 immutable baseline）——probe 不寫 ledger，不需要、也不該觸發這兩條屬於 mutation lane 的邏輯。
- `$operationMayHaveExecuted`（`96`）需比照 `$isRepairProbeMode` 加入新旗標 `$isFreshProvisionProbeMode`，固定為 `false`。
- Child dispatch 需在 `2528`（`elseif ($isRepairProbeMode)`）旁新增平行分支：專屬 `SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE=1`（與 `..._FRESH_PROVISION`、`..._REPAIR_PROBE` 等互斥、逐一清空），專屬 evidence 檔名（如 `P72FreshSliceCFixtureProvisionProbeEvidence.json`）與專屬環境變數 `P7_2_SLICE_C_FRESH_PROVISION_PROBE_EVIDENCE_PATH`，並在 `2562-2608` 那組「先清空所有 evidence path 再指定自己」的樣式中補上這一支。
- probe 仍需 Credential Manager 密碼（WhoAmI/Retrieve/RetrieveMultiple 需要已驗證連線），因此仍走 `Get-P72CredentialPassword` 與既有 `CRM_PASSWORD` 短生命週期注入／finally 清除流程，只是 test filter 指向新的唯讀 xUnit Fact。

## 2. Sanitized evidence schema 與允許值

沿用 `Get-StrictSliceCRepairProbeEvidenceFile`（`2078-2180`）的嚴格 exact-property-set 慣例：

```
schemaVersion = 1
outcome       = 'no-go'                         // 固定值，即使全部通過也不得為 'go'
reason        ∈ { 'fresh-preflight-preconditions-proven',
                   'fresh-preflight-precondition-failed',
                   'cleanup-failure' }
profileAlias / deploymentProfileAlias / ceVersion='9.1' / connector='Data8'
preflightOnly = false
operationExecuted = false                        // 永遠 false：probe 不觸發任何 mutation
readOnlyProbeExecuted : bool
probe:
  requestShapeValid                 : bool   // 對應需求 #1
  operationalListsTaskOwned         : bool   // 對應需求 #2，五個 list 聚合為單一布林（刻意不逐一揭露，避免可被二分搜出哪一個 list 壞掉）
  leaderMarkerValid                 : bool   // 對應需求 #3
  leaderOwnerKind      ∈ { 'systemuser', 'other', 'unresolved' }
  leaderOwnerActive                 : bool
  leaderOwnerMatchesServiceUser     : bool   // 與已驗證 WhoAmI user 是否相同
  weeklyReportCardinalityState ∈ { 'none', 'exactly-one', 'multiple', 'unresolved' }  // 對應需求 #4
  weeklyReportActiveAndSundayProven : bool
  preconditionState ∈ { 'all-preconditions-proven', 'request-shape-invalid',
                         'operational-lists-unproven', 'leader-provenance-invalid',
                         'leader-owner-matches-service-user', 'weekly-report-unproven',
                         'unavailable' }        // 對應需求 #5：「最終 go」訊號收斂在這裡
```

⚠️ **需求第 5 點「a final `go` only when...」的語意風險**：字面上可解讀為頂層 `outcome` 應允許輸出 `'go'`。但既有 `RepairProbe` 的明確設計原則是「即使所有 proof 成立，child 仍回傳 no-go」（`2083-2084` 註解），目的是讓唯讀診斷在 outcome 層永遠與「已授權 mutation」的 lane 結構性可區分，避免下游任何邏輯誤把 probe 的 `go` 當成執行授權。建議維持 `outcome` 恆為 `'no-go'`，把「全部條件成立」的訊號放在 `reason='fresh-preflight-preconditions-proven'` 與 `probe.preconditionState='all-preconditions-proven'`——這仍滿足「只有全部前提成立才會出現該分類」的要求，但不破壞既有 lane 的安全不變量。這點建議在實作前與需求方確認。

`readOnlyProbeExecuted` 與 `reason` 之間應比照 `2158-2163` 的雙向一致性檢查（proven ⇔ executed=true；非 proven ⇔ executed=false）。

## 3. 濫用／外洩與生命週期風險

- **列舉/側通道**：五個 list 只給聚合布林（已如上設計），但 `AreOperationalListsTaskOwned` 目前用 `&&` 短路（`Provisioner.cs:564-569`）。因為最終只回吐一次性 sanitized JSON、無逐次計時或逐段輸出，短路不構成可觀察的側通道，但若之後任何監控工具量測 child 執行時間，仍建議 probe 版本改為不短路、跑滿五次 Retrieve 再聚合，避免時間差成為未來新增觀測面時的隱性外洩源（Info 等級）。
- **憑證/連線生命週期**：probe 仍需短生命週期取得密碼並建立 `OnPremiseClient`／`EmbeddedData8Runtime`，必須完整重用 `DisposeService` → `DisposeRuntimeAsync` → `DisposeLogger` 的反向釋放順序與 finally 邏輯（`115-130`），任一 cleanup 失敗一律降級為 `reason='cleanup-failure'` 且 `readOnlyProbeExecuted=false`，不可保留連線或憑證痕跡。
- **跨 session 殘留**：新增的 `SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE` 與其 evidence-path 環境變數必須同時納入 `legacySliceCEnvironmentPrefixes` / snapshot / finally-restore 三條路徑（`98-120` 及對應 restore 區塊），否則會重現註解中警告的「只更新其中一條 lifecycle path 而跨 session 洩漏」問題。
- **ledger 隔離**：probe 絕不可持有 `IP72FreshSliceCFixtureLedger` 實例，天然排除了 ledger 洩漏或誤寫的整類風險——這是選擇不重用 mutation 路徑的直接好處。
- **診斷欄位**：不需要（也不應該）像 provision lane 一樣有獨立 `TryWriteDiagnostic`／`diagnosticCategory`（`281-298` 允許清單、`2091-2104` 的 strict schema 都刻意不含此欄位）；probe 的 no-go 分類本身就是主要輸出通道，額外診斷檔只會多一個要保證清除的暫存檔。

## 4. 具體測試案例與可能受影響檔案

**受影響檔案**
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`：新 parameter set／switch、`$isFreshProvisionProbeMode`、env 分支、evidence path 分支、`Get-StrictSliceCFreshProvisionProbeEvidenceFile`（仿 `2078-2180`）。
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`：binder 互斥測試、strict parser 拒收測試。
- `docs/scripts/Invoke-Package02Data8ListManagementFreshFixture.Tests.ps1`：probe 模式的 ledger/descriptor 不變性、env 隔離測試。
- `ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementFreshFixtureTests.cs`：新 xUnit Fact `Probe_fresh_package02_data8_list_management_preconditions_emits_sanitized_evidence`。
- `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureProvisioner.cs`：把 `IsRequestShapeValid` / `AreOperationalListsTaskOwned` / `TryResolveActiveBaselineOwner` / `TryResolveExactlyOneActiveWeeklyReport`（`537-653`）抽出給新的唯讀 `ProbePreconditions(request)` 共用，避免 probe 與真正 `Provision()` 的前置條件邏輯漂移。
- 新增類似 `P72Data8SliceCRepairProbeFactAttribute`（`LivePackage02Data8ListManagementEvidenceTests.cs:1378-`）的專屬 Fact gate。
- `P72FreshSliceCFixtureFileLedger.cs`：**不應變更**（probe 不觸碰 ledger 是設計重點）。

**測試案例**
1. 五個 list 皆 task-owned、leader active 且非 service user、weekly report 恰一筆且 Sunday 相符 → `reason='fresh-preflight-preconditions-proven'`，`preconditionState='all-preconditions-proven'`，全程零 mutation call。
2. 任一 list 未通過 task-owned/type 檢查 → `operationalListsTaskOwned=false`，零 mutation。
3. leader marker 缺失／owner 非 active／owner 是 team 非 systemuser → `leaderMarkerValid`/`leaderOwnerActive`=false。
4. leader owner 恰等於已驗證 WhoAmI service user（對齊既有 `baseline-owner-unavailable` 情境）→ `leaderOwnerMatchesServiceUser=true`，`preconditionState` 反映但不外洩 ID。
5. weekly report 0 筆 / 2 筆以上（含 `MoreRecords`）/ 日期或 statecode 不符 → `weeklyReportCardinalityState` 對應 `none`/`multiple`。
6. WhoAmI 解析本身失敗（`ResolveFixtureTargetOwnerIdAsync` 回 null）→ 全欄位 fail-closed 為 `unavailable`/`false`，`reason='fresh-preflight-precondition-failed'`。
7. runtime/store/logger cleanup 失敗 → `reason` 降級為 `cleanup-failure`、`readOnlyProbeExecuted=false`（比照 `FinalizeReconciliationEvidence` 優先序邏輯）。
8. PowerShell 層：`-ProvisionFreshFixturePreflightProbe -ReplaceStaleDescriptor` 應在 binder 階段即失敗，零次 `Get-P72CredentialPassword` 呼叫。
9. Strict parser 拒收：欄位多/少、`outcome != 'no-go'`、`operationExecuted=true`、`readOnlyProbeExecuted` 與 `reason` 不一致。
10. env 隔離：呼叫後所有 `..._FRESH_PROVISION_PROBE*` 變數應回復呼叫前狀態（`null`）。
11. **貫穿所有分支**：以 fake `IOrganizationService`（重用 `P72Data8ListManagementFreshFixtureProvisionerTests.cs` 既有 test double 樣式）斷言 `Create`/`Update`/`Delete`/`Execute`（Assign/Add/Remove List Members）/`Associate`/`Disassociate` 呼叫次數恆為 0，覆蓋案例 1–7 全部結果，直接對應需求「每個結果都要證明零 mutation call」。

## 5. 能否重用既有 repair/reconcile 模式

**`RepairProbe`（`RepairProbe` param set / `Get-StrictSliceCRepairProbeEvidenceFile` / `P72Data8SliceCRepairProbeFactAttribute`）是最接近的既有樣式**，兩者共享同一套安全骨架：專屬唯讀 parameter set、恆定 `outcome='no-go'`、bounded `probe` 物件、專屬 env 旗標與專屬 Fact gate、與所有 mutation lane 互斥。

但**不建議直接掛在 `RepairProbe` 之上**，理由：

- **欄位網域完全不同**：`RepairProbe` 描述既有 relationship-list 的 repair 前提（`sourceContactMarkerValid`、`expectedRelationshipFieldsState` 等），fresh 流程要證明的是五個 list 的聚合擁有權、leader-vs-WhoAmI 比對、weekly-report 基數——若共用同一 schema，會違反 `2106-2108` 那套「屬性集合必須精確相符」的 strict parser 不變量，被迫做成聯集 schema，反而讓兩種 lane 互相洩漏彼此用不到的欄位。
- **輸入前置條件不同**：fresh 流程綁定 `-ReplaceStaleDescriptor` 語意、nonce、ledger-root 存在性檢查（`2385-2412`），`RepairProbe` 完全不觸碰這些；硬要共用會被迫繞過或弱化 fresh lane 既有的「ledger pending 擋下重新 provision」不變量。
- **稽核邊界**：每個 lane 現在都有自己專屬的 `SPEECHMESSAGE_P7_2_SLICE_C_*` 旗標與 Fact gate，殘留變數只可能誤啟用「自己那條」唯讀或 mutation 測試。借用 `SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE` 會讓「這份 evidence 到底出自哪條 lane」的稽核軌跡變模糊。

**建議做法**：複製 `RepairProbe` 的*模式*而非*程式碼*——新建結構平行的 `ProvisionFreshProbe` lane（獨立 param set／env 旗標／strict parser／Fact attribute），並讓它與真正的 `Provision()` 共用 `P72FreshSliceCFixtureProvisioner.cs` 裡同一組前置條件方法（見第 4 節），確保 probe 回報的「已證明」與 `Provision()` 實際判斷的閘門永遠一致，不會出現 probe 說綠燈但正式 provision 又擋下的落差。

---
SESSION_ID: 0bbb75d1-4137-4012-a591-3c1a6ccba2cd
