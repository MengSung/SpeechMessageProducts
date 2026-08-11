我已閱讀相關程式碼（`Invoke-Package02Data8ListManagementEvidence.ps1` 的 RepairProbe/ProvisionFresh 路徑、`P72FreshSliceCFixtureProvisioner.cs` 的既有 preflight 鏈、`P72FreshSliceCFixtureFileLedger.cs`）與 `.trellis` 進度文件，以下是設計審查。這是純分析／審查任務（TASK 明確要求「設計審查」而非程式碼變更），未修改任何檔案。

## 1. 最安全的整合點與 parameter-set 邊界

**新增獨立 ParameterSetName `FreshPreflightProbe`（例如開關 `-FreshPreflightProbe`）**，掛在既有 `[CmdletBinding(DefaultParameterSetName = 'Preflight')]` 之下（`Invoke-Package02Data8ListManagementEvidence.ps1:31-70`）。PowerShell binder 會自動保證它與 `-ExecuteFixture` / `-ReconcileFixture` / `-RepairFixture` / `-RepairProbe` / `-ProvisionFreshFixture` / `-CleanupFreshFixture` 互斥，不需額外程式碼——這與現有 `-ExecuteFixture`/`-ReconcileFixture` 互斥的做法一致（見檔頭 22 行註解）。

**不要掛在 `RepairProbe` 或 `ReconcileFixture` 底下**，理由見第 5 節。應該掛在 **`ProvisionFresh` 家族**（`isFreshProvisionMode`/`isFreshCleanupMode` 的同一組 branch，`:2480-2515`），因為它需要的是 fresh-fixture descriptor 純量（`addListId`/`removeListId`/`smallGroupListId`/`existingTargetLeaderContactId`/`transferSourceListId`/`transferTargetListId`/`transferWeekStartUtc`），而非 generic Slice C fixture 環境變數（`:2541-2551`，執行/reconcile/repair/repairProbe 共用）。

具體改動點：
- `:2480` 的條件需擴充為 `if ($isFreshProvisionMode -or $isFreshCleanupMode -or $isFreshPreflightProbeMode)`，但**新增一個排除分支**：probe 模式必須跳過 `P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION`（此值目前只用於 `replace-stale-descriptor` / `cleanup-fresh-fixture` 兩種授權語意，probe 不應該持有任何確認語意）與所有 `P7_2_SLICE_C_FRESH_LEDGER_ROOT`/`P7_2_SLICE_C_FRESH_LEDGER_PATH`（`:2497-2498`）——probe 不寫 ledger。
- Dispatch 區塊（`:2534-2610`）比照 `isRepairProbeMode` 分支新增一支，evidence 檔名建議 `P72FreshSliceCFixturePreflightProbeEvidence.json`，對應新的環境變數 `P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH`，且必須先清空其餘四種 evidence path 變數（沿用現有模式，`:2596-2601`）。
- `New-HandoffResult` 呼叫端新增一支 `elseif ($isFreshPreflightProbeMode)`，輸出 `-ProbeStage 'fresh-preflight'` + `-Probe $strictEvidence.probe`（比照 `:2702-2711` RepairProbe 的作法）。

C# 側：**不要**在 `P72FreshSliceCFixtureProvisioner.Provision()`（`P72FreshSliceCFixtureProvisioner.cs:294-331`）內加旗標分岔——那個方法本體就是 mutation path，任何 `if (probeOnly) return` 都要人工證明所有 `_service.Create` 呼叫確實在該 return 之後，屬於高風險維護點。應該把既有的三個 private 判斷式：
- `IsRequestShapeValid`
- `AreOperationalListsTaskOwned`
- `TryResolveActiveBaselineOwner(...)`（含 `:314-319` 的 owner==WhoAmI 比對）
- `TryResolveExactlyOneActiveWeeklyReport(...)`

抽成 `internal` 共用方法（同一 class 或抽出 `P72FreshSliceCFixturePreflightEvaluator`），讓 `Provision()` 呼叫它們（邏輯零漂移），並讓新的 `internal P72FreshSliceCFixturePreflightProbeResult Probe(request)` 唯讀方法也呼叫同一組方法後直接組裝結果、**physically 不引用** `_service.Create/Update/Delete/Assign/Associate/Disassociate` 任何一個 symbol，這樣「零 mutation」是編譯期可審查的事實，不只是執行期行為。

## 2. 建議的 sanitized evidence schema

沿用 `Get-StrictSliceCRepairProbeEvidenceFile`（`:2078-2180`）的兩層結構（固定 top-level envelope + 白名單 `probe` 子物件），但欄位對應 Provision 既有的四段 gate（`P72FreshSliceCFixtureProvisioner.cs:307-327`）：

```
top-level（沿用固定 envelope，schemaVersion/profileAlias/ceVersion/connector/preflightOnly=false/operationExecuted=false 恆定）：
  outcome            = 'no-go'                         # 恆定；probe 本身永不是授權
  reason ∈ {'fresh-preflight-proven', 'fresh-preflight-precondition-failed', 'cleanup-failure', 'runtime-failure'}
  readOnlyProbeExecuted : bool
  probe:
    requestShapeValid                : bool   # 對應 IsRequestShapeValid
    operationalListsTaskOwned        : bool   # 對應 AreOperationalListsTaskOwned（五份清單聚合，不逐一投影）
    leaderMarkerValid                : bool   # task-marked 前綴驗證
    leaderOwnerKind ∈ {'active-systemuser','inactive-systemuser','non-user','missing','unreadable'}
    leaderOwnerDiffersFromData8User  : bool   # 對應 baselineOwnerId == request.Data8ServiceUserId 比對
    weeklyReportState ∈ {'exactly-one-active','none','multiple','date-invalid','unreadable'}
    preflightState ∈ {'go','request-shape-invalid','operational-lists-invalid',
                       'leader-provenance-invalid','leader-owner-same-as-data8-user',
                       'weekly-report-invalid','unavailable'}
```

`preflightState = 'go'` 只在上述五個布林/列舉全部通過時才允許出現，且必須與 `reason = 'fresh-preflight-proven'` 一起驗證（比照 `:2158-2163` 的雙向蘊含檢查，防止 child 只送 `go` 卻不送對應 reason，或反之）。**這個結構直接解決 TASK 描述的核心問題**——目前 `fixture-precondition-failed` 把「request shape / 五清單 / leader 血緣 / weekly report」四種原因壓成同一個字串（`:281-297`、`:1190-1206`），這個新 schema 讓 operator 知道是哪一段聚合條件不成立，但仍不洩漏是哪一筆清單、哪一個 GUID、owner 的真實 identity 或 baseline 值。

## 3. 濫用／洩漏與生命週期風險

- **最大風險是 `leaderOwnerKind` 變成側信道**：如果它意外帶出真實 `systemuser` 的 domain/UPN 字串而非固定列舉，等同洩漏 identity。必須強制走 allowlist 轉換（比照 `expectedRelationshipFieldsState` 的 `-cnotin $allowedFieldStates` 模式，`:2153`），C# 端也要回傳 enum 而非原始 attribute 值。
- **`weeklyReportState` 的 `'multiple'` 分支本身就是資訊量**：目前 provision 用 `TopCount=2` 是為了偵測「非恰好一筆」而不需要真正枚舉，probe 必須維持同樣做法（只問 count 是 0/1/>1，不回傳任何 report ID）。
- **與 diagnosticCategory 機制的疊加風險**：`New-HandoffResult` 目前有 `DiagnosticCategory` 白名單（`:281-293`）專門處理「child 非零 exit」情境；新 probe 是「child 正常結束、outcome=no-go」情境，兩者必須用不同欄位（`probe` vs `diagnosticCategory`），否則兩條完全不同信任等級的資訊（一個是 parent 對非零 exit 的保守猜測、一個是 child 已證明的讀取結果）會被合併解讀，破壏現有「diagnosticCategory 絕不是 child evidence」的不變量（`:278-280` 註解）。
- **跨 profile/session 殘留**：probe 不寫 ledger、不建立 descriptor pair，所以理論上没有殘留面；但仍需比照現有 fresh lane 做法，在 dispatch 前清空所有非本模式的 legacy 環境變數（`:2484-2486`），並在 `finally` 走既有 `Remove-OwnedSliceCTemporaryDirectory`（`:2182`）刪除整個 nonce directory，即使結果已經是 no-go。
- **重試放大風險**：probe 本身零成本（純讀），但若 operator 把 `preflightState=go` 誤當「已授權」而略過下一次明確 `-ProvisionFreshFixture` 確認，等於繞過既有的顯式 opt-in 政策。建議在 PowerShell 端輸出文字（非 JSON 欄位，只是給人看的 stderr/host 提示）強調「此結果不授權任何 mutation」，並保持 `outcome` 恆為 `'no-go'`、`safeToRetry` 恆為 `false`（沿用 `:252-259` 的自動附加邏輯，probe 模式也要納入該 `Get-Variable` 判斷式）。

## 4. 具體測試案例與可能受影響檔案

**PowerShell contract（`docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1` 或 `...FreshFixture.Tests.ps1`）：**
- `-FreshPreflightProbe` 與其他五個 live 開關同時指定 → binder 在建立 credential/child 前即拒絕（沿用既有 `-ExecuteFixture`+`-ReconcileFixture` 互斥測試模式）。
- 新 strict parser（`Get-StrictSliceCFreshPreflightProbeEvidenceFile`）：欄位數量不符、多餘欄位、`probe` 內任何值不在 allowlist、`preflightState='go'` 但某個布林為 false、`reason` 與 `readOnlyProbeExecuted`/`preflightState` 不蘊含一致 → 全部 fail closed 為 `evidence-result-unavailable`（比照 `:2136-2163` 全部案例）。
- child 非零 exit 時，probe 模式必須落入既有 `child-process-failed` 分支（`:2655-2680`），且 `operationExecuted` 固定 `false`（不像 `$operationMayHaveExecuted` 對 execute lane 保守回報 `true`——因為 probe 從定義上永不可能執行 mutation，這點需要在 `:96` 的 `$operationMayHaveExecuted` 計算式中把 probe 模式也排除）。
- UTF-8 no-BOM / CRLF-only / final-CRLF：沿用既有 `Read-StrictFinalCrLfJsonFile` 測試矩陣（bare LF、bare CR、embedded BOM、缺 final CRLF 全部要 fail closed）。

**C#（`ChurchReport.MemberInfo.Tests/P72Data8ListManagementFreshFixtureProvisionerTests.cs` 或新檔）：**
- 對每一段 gate 分別造出「該段失敗、其餘段通過」的假 `IOrganizationService`，斷言 `preflightState` 精準對應且**其餘布林仍然是各自獨立的真實值**（不能一失敗就全部塞 false）。
- 全部通過時 `preflightState='go'`、`reason='fresh-preflight-proven'`。
- **零 mutation 斷言**：對 fake service 加计数器，斷言 `Create`/`Update`/`Delete`/`Assign`/`Associate`/`Disassociate` 呼叫次數在所有分支（含全通過）都是 0——這是 TASK 要求的「zero mutation calls for every outcome」最直接的證明方式。
- 跨呼叫/跨 profile 無殘留：兩次連續呼叫使用不同 fake WhoAmI user，斷言第二次呼叫結果不受第一次呼叫任何 instance 欄位影響（因為方法應為 stateless / 每次重新建構）。

**受影響檔案清單（與目前 git status 已變動的檔案高度重疊，顯示這正是同一批工作的延伸）：**
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`（新 param/env/dispatch/parser）
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`、`docs/scripts/Invoke-Package02Data8ListManagementFreshFixture.Tests.ps1`（contract 測試）
- `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureProvisioner.cs`（抽出共用 preflight 方法 + 新 `Probe` 入口）
- `ChurchReport.MemberInfo.Tests/LivePackage02Data8Data8ListManagementFreshFixtureTests.cs`（新 xUnit live test，供 runner `--filter` 使用）
- 若 evidence 檔名/temp 目錄有集中式白名單守門（`P72FreshSliceCFixtureFileLedger.cs`／`P72FreshSliceCFixtureLiveEvidence.cs`/`...LiveGateTests.cs`，這幾個檔案目前已在 working tree 變動中），需檢查是否有「合法 evidence 檔名」列舉需要加入新檔名。

## 5. 能否重用既有 repair/reconcile 模式

**建議重用 RepairProbe 的「架構模式」，但不重用 ReconcileFixture 或 RepairFixture 本身，也不能把新邏輯塞進 RepairProbe。**

- **`ReconcileFixture`**（`:17-22`、`Get-StrictSliceCReconciliationEvidenceFile`）讀的是**已建立完成**的 Slice C graph（`baseline-unprovable`/`small-group-read` 等 probeStage），對象是 execute 後的既有 fixture，不是「建立前」的 descriptor/leader/weekly-report 條件。語意不符。
- **`RepairFixture`/`RepairProbe`**（`Get-StrictSliceCRepairProbeEvidenceFile`, `:2078-2180`）鎖定的是 relationship-list **修復**場景的欄位（`sourceContactMarkerValid`/`smallGroupListValid`/`expectedRelationshipListValid`/`targetLeaderMarkerValid`/`expectedRelationshipRaceLeaderMatches`/`expectedRelationshipFieldsState`），這是**既有** relationship list 的欄位完整性，而 TASK 要的是 **fresh provision 前**的四段完全不同的 gate（request shape、五份清單、leader owner 血緣+WhoAmI 比對、weekly-report 存在性）。把兩者混在同一個 evidence schema 會讓 allowlist 同時混用兩種語意，違反「bounded schema with fixed deidentified vocabulary」的要求，也會讓 `RepairFixture` 系列的既有測試矩陣被迫涵蓋不相關分支。
- 唯一該重用的是**結構性模式**：獨立 ParameterSetName、獨立環境變數旗標、獨立 evidence 檔名/路徑、`outcome` 恆為 `'no-go'`、頂層+巢狀 `probe` 兩層固定 allowlist、`reason`↔巢狀狀態雙向蘊含檢查、child 非零 exit 一律不信任 evidence 內容。這套模式在程式碼裡已經被驗證兩次（Reconcile、RepairProbe），第三次套用到 Fresh Preflight 是低風險的既定慣例，而不是新發明。

**結論／blocker：** 沒有無法完成的部分——這是設計審查，已產出可執行的整合點、schema 與測試計畫。若要進入實作階段，需要先確認：(a) 新增的 `preflightState`/`leaderOwnerKind`/`weeklyReportState` 三個列舉值清單是否需要與現有 P7.2 命名慣例（英文 kebab-case、無 CRM 術語外洩）做最終審閱；(b) 是否要在同一個 PR 內順便把 `Provisioner.Provision()` 的四個 private 判斷式抽成共用方法（這是唯一會觸及既有 mutation path 的變更，需要格外小心的 review）。

---
SESSION_ID: d29d6510-5260-425e-ae16-ec4fdb5283d6
