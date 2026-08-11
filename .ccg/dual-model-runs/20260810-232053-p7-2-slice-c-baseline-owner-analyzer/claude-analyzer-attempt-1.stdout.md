# P7.2 Slice C baseline-owner 前置條件分析

## 方法說明
本分析基於實際原始碼審查（非僅信任 Trellis 記錄）：通讀 `P72FreshSliceCFixtureProvisioner.cs`（Provision/Cleanup 全流程）、`P72FreshSliceCFixtureFileLedger.cs`（含本次未提交的 diff）、`Invoke-Package02Data8ListManagementEvidence.ps1`（parent 控制面 diff）、`P72FreshSliceCFixtureLiveEvidence.cs`，並實際執行 `dotnet test --filter FullyQualifiedName~P72FreshSliceC`，結果為 **23 通過 / 2 略過（本機缺 SeCreateSymbolicLinkPrivilege）/ 0 失敗**，與 Trellis 記錄相符。未對 CE/CRM 做任何連線或變更。

---

## Critical

無。目前程式碼路徑與提議的決策閘門未發現會導致 CRM mutation、retry 或狀態外洩的釋出阻斷缺陷。

---

## Warning

**W1｜「baseline-owner 是否非 Data8 WhoAmI」目前沒有獨立的唯讀 probe 入口**
`TryResolveActiveBaselineOwner`（`P72FreshSliceCFixtureProvisioner.cs:579-615`）雖然正確地「先讀後判斷、失敗即停」，但它只存在於 `Provision()` 內部（`:309`, `:314-318`），必須透過完整的 `-ProvisionFreshFixture -ReplaceStaleDescriptor` 呼叫才能觸發，且一次失敗即消耗掉一次「operator explicitly authorizes one new independent Slice C cycle」的授權額度。相較之下，repair lane 已有專屬 `-RepairProbe` 唯讀診斷模式。若沒有等效的 owner-only probe，操作者只能用手動 CE 查詢（人工核對既有 task-marked leader 的 `ownerid` 與該 systemuser 的 `isdisabled`）在場外先行驗證，這是可行但無自動化保護的路徑，容易在下一輪授權時仍打到相同的 `baseline-owner-unavailable`。
建議（非阻斷）：日後可考慮抽出一個唯讀 probe 模式重用 `TryResolveActiveBaselineOwner` 邏輯；但不需要在啟動下一輪授權週期前完成。

**W2｜Cross-process ledger replacement 邏輯較新且尚無獨立第二人覆核**
`ValidateExistingLedgerForReplacement` / `ValidateReplacementStageTransition`（`P72FreshSliceCFixtureFileLedger.cs`）是本次未提交 diff 中最複雜的部分：它要求既有 ledger 的 `stage` 索引必須等於候選值或候選值−1（即只能同階重播或前進一格），且 profile/CE/connector/owner/nonce/original baseline leader 全部相符才允許 atomic replace。經審查，任何比對失敗都導致 `throw`（fail-closed），方向是安全的；但這段邏輯目前只由本專案內部測試覆蓋（`P72FreshSliceCFixtureFileLedgerTests.cs`），建議在正式合併前有第二人覆核 `AllowedStages` 陣列順序與 provision/cleanup 兩條 stage 序列是否完全吻合，避免未來新增 stage 時漏改。

---

## Info

**1️⃣ 現有程式碼與決策閘門是否維持 isolation / cleanup / no-retry 不變量**

維持。具體證據：

- `baselineOwnerId == request.Data8ServiceUserId` 分支（`:314-318`）在 `PersistLedger`（`:331`，第一次 ledger 寫入）與 `operationExecuted = true`（`:333`，第一次 Create）**之前**就 return，因此 `baseline-owner-unavailable` 分支確定零 ledger 寫入、零 CRM mutation，與 Trellis 記錄「the provisioner correctly stops before any ledger persistence or CRM mutation」一致。
- Owner 解析只讀取 `request.ExistingTargetLeaderContactId` 這一個既定 leader（`:579`），沒有任何 `systemuser` 掃描/清單查詢/caller-supplied owner 分支——符合「不得自動掃描替代 owner、不得接受呼叫端提供的 owner」的邊界。
- `AssignRequest`（`:382-386`）的 `Assignee` 恆為 `baselineOwnerId`（非 Data8 WhoAmI user，因為前面已擋掉相等情形），不存在「弱化為 self-assignment」的路徑。
- Cleanup 的 owner provenance 只從 **strict ledger** 的 `OriginalTargetLeaderContactId` 取得（`:457`），不會信任 request 或已被覆寫的 descriptor（`:63` 註解已明確標註此點），因此不會出現 cleanup 用到「已發布 fresh descriptor」的錯誤基準。
- PS parent 層新增的 `diagnosticCategory` 僅為固定 allowlist（`New-HandoffResult` 中 `-cnotin` 白名單驗證，否則 `throw`），且不影響 `safeToRetry`、不觸發 cleanup dispatch、不發布 descriptor——與非授權、僅供分類用途的設計意圖一致。
- No-retry：`-ProvisionFreshFixture` 需要顯式 `-ReplaceStaleDescriptor`；已存在未消化的 ledger 會回傳 `fresh-fixture-ledger-pending` 而非自動重試或覆蓋（`:2404` 附近邏輯）。

**2️⃣ 啟動下一輪 fresh cycle 前所需的最小、具權威性證據**

僅需以下**兩筆唯讀 CRM 查詢結果同時成立**（與 `TryResolveActiveBaselineOwner` 的判斷完全一致，可視為規格）：

1. 既有 task-marked leader（`ExistingTargetLeaderContactId`）的 `fullname` 仍保有 `P7.2-SC-` 前綴，且其 `ownerid` 是一筆合法 `systemuser` reference（非 team、非空）。
2. 該 `systemuser` 的 `isdisabled = false`（active），且其 ID **不等於**同一 crm91/sunnyvalechback profile 下 Data8 WhoAmI 解析出的 `UserId`。

此證據必須在啟動 `-ProvisionFreshFixture -ReplaceStaleDescriptor` **之前**、以場外唯讀方式確認（因為目前架構中這個判斷只發生在 child 內部，見 W1）。除此之外不需要其他前置證據——五個 operational list 的 task-owned 驗證（`AreOperationalListsTaskOwned`）與 weekly report 的唯一性解析都是既有、已證明穩定的既存前置條件，非本次待證項目。

**3️⃣ 執行本輪 cycle 前是否有源碼變更是必要（justified）**

不需要。`baseline-owner-unavailable` 是 Trellis 記錄與程式碼都確認的**CRM 資料狀態問題**（既有 leader 與 Data8 WhoAmI 是同一 active systemuser），不是程式邏輯缺陷；`TryResolveActiveBaselineOwner` 的 fail-closed 行為本身是正確、也是設計要求（不得自動選代替 owner）。本次未提交的 diff（ledger cross-process replacement 驗證、CRLF standalone-CR 解析修正、`diagnosticCategory` 管線、JSON duplicate-key 防護）屬於獨立的本機品質強化，測試已通過（23/25，2 略過為本機權限限制而非邏輯缺口），**不是**解除 `baseline-owner-unavailable` 的必要條件，也不需要在下一輪授權週期前額外修改。真正的前置動作是 CE 端資料層級的決定：指派/選用一個 owner 為 active、非 Data8-WhoAmI 使用者的既有 task-marked leader，這超出程式碼變更範疇，屬於「separately scoped and explicitly authorized CE test-setup decision」。

**4️⃣ 釋出阻斷缺陷**

未發現。CRLF standalone-CR 解析漏洞的修正（JSON 允許獨立 CR 作為 whitespace，舊版只擋 bare LF）本身是一個真實修好的邊界缺陷，但屬於本地檔案信任邊界強化而非會導致 CRM mutation 或機密外洩的問題，且已有回歸測試先重現再驗證修復。

## 結論
程式碼與決策閘門本身可以安全支撐「一次獨立、已授權的 Provision→graph validation→evidence→Cleanup」週期；唯一還沒滿足的前置條件是 CE 端資料狀態（需要一個 owner 非 Data8 WhoAmI 的 active systemuser 的 task-marked leader），這需要場外人工/CE 端決定，而非程式碼修改。建議在啟動下一輪前，先以唯讀方式核對第 2️⃣ 點的兩個條件（可考慮日後補一個等同 RepairProbe 的唯讀 owner probe，見 W1），以避免再次消耗一次授權週期。

---
SESSION_ID: 727f7e0c-4309-4206-be1e-8601d5f9da94
