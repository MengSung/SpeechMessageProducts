# P7.2 Slice C Baseline-Owner 前置條件分析報告

本報告針對 `sunnyvalechback` / `crm91` / CE 9.1 / Data8 設定檔下，Slice C 執行 `ProvisionFreshFixture` 時出現的 `baseline-owner-unavailable` 診斷結果進行前置條件與架構安全性分析。

---

## 1. UX Analysis (使用者與營運體驗評估)

- **營運人員體驗影響**：
  當前置條件不滿足（即預期的 Leader 擁有者與當前 Data8 WhoAmI 服務使用者相同）時，系統能立即且安全地中斷（Fail-Closed），並在日誌中提供明確的診斷類別（`baseline-owner-unavailable`）。這避免了營運人員在不知情的情況下重複嘗試，或造成 CRM 系統中的資料污染。
- **使用者旅程暗示**：
  此安全閘確保了測試生命週期的完整性。營運人員不需要通讀複雜的 WCF 錯誤日誌，即可透過 `diagnosticCategory` 快速定位是「環境測試資料設定問題」而非「產品程式碼缺陷」。
- **無障礙與行動端考量**：
  不適用（此為後端測試控制面與腳本工具）。

---

## 2. Design Evaluation (設計與模式評估)

- **一致性與模式**：
  - 診斷檔案 `P72FreshSliceCFixtureDiagnostic.json` 的寫入與讀取模式與現有的 `P72FreshSliceCFixtureLiveEvidence.cs` 和 `Invoke-Package02Data8ListManagementEvidence.ps1` 保持高度一致。
  - 腳本嚴格限制診斷檔案大小為 1024 bytes (1 KiB) 且必須為 CRLF 格式，這符合專案對 JSON 驗證的嚴格規範。
- **組件重用性**：
  - `TryResolveActiveBaselineOwner` 方法重用了 `IOrganizationService` 的唯讀查詢能力，且不引入額外的狀態，保持了無狀態設計。
- **視覺與互動設計**：
  - 營運人員在 CLI 或日誌中能看到明確的 `diagnosticCategory=baseline-owner-unavailable`，這有助於快速定位問題。

---

## 3. Technical Considerations (技術與架構考量)

### 隔離性與清理不變量
- **檔案路徑**：`ChurchReport.MemberInfo.Tests\P72FreshSliceCFixtureProvisioner.cs`
  - 程式碼在 `PersistLedger` 與任何 `Create` 變更之前即進行 `baselineOwnerId == request.Data8ServiceUserId` 檢查，確保了「零 CRM 異動」與「零 Ledger 殘留」。
- **檔案路徑**：`docs\scripts\Invoke-Package02Data8ListManagementEvidence.ps1`
  - 腳本在 child process 失敗時，會正確捕獲診斷類別，並將 `safeToRetry` 設為 `false`，防止自動重試。

### 測試考量
- **檔案路徑**：`ChurchReport.MemberInfo.Tests\P72Data8ListManagementFreshFixtureProvisionerTests.cs`
  - 單元測試 `Provision_rejects_service_user_as_the_only_baseline_owner_before_any_mutation` 完整驗證了此安全閘，確保重構或後續變更不會破壞此不變量。

---

## 4. Options (替代方案與權衡)

| 方案 | 優點 | 缺點 | 結論 |
| :--- | :--- | :--- | :--- |
| **方案 A（現行方案）**<br>嚴格 Fail-Closed，不自動尋找替代 owner，不接受外部傳入 owner，不弱化 Assign。 | 極高的安全性，防止測試資料污染與未授權的 CRM 變更。 | 需要手動介入修正 CE 端的測試資料。 | **首選**。符合安全第一原則。 |
| **方案 B**<br>自動掃描並選擇另一個 active `systemuser` 作為 owner。 | 自動化程度高，測試可能自動通過。 | 違反「非妥協邊界」，可能引入不可預測的 owner 變更，且在多用戶環境下可能導致權限或資料隔離失效。 | 排除。 |
| **方案 C**<br>弱化 Assign，如果 owner 相同則跳過 Assign。 | 避免 `baseline-owner-unavailable` 錯誤。 | 無法測試真實的 Assign 邏輯，降低了測試的覆蓋品質與真實性。 | 排除。 |

---

## 5. Recommendation (建議方案與原由)

- **首選方案**：**方案 A**。
- **原由**：在分散式系統與 CRM 整合測試中，資料的一致性與隔離性至關重要。自動選擇或弱化檢查會引入隱性風險。現有的 Fail-Closed 設計是最佳實踐。

---

## 6. Concrete Findings (具體發現分類)

### 🔴 Critical (關鍵)
1. **環境資料不一致 (Environment Data Mismatch)**
   - **檔案路徑**：`ChurchReport.MemberInfo.Tests\P72FreshSliceCFixtureProvisioner.cs` (第 314-319 行)
   - **說明**：當前 CE 9.1 環境中的 task-marked leader 聯絡人被指派給了與 Data8 WhoAmI 相同的 `systemuser`。這是一個發行阻礙（Release-blocking）的環境問題，必須在執行下一次 fresh cycle 之前手動修正。
   - **處置**：在未修正 CE 端資料前，絕對不能修改程式碼以繞過此檢查，或執行 `-ExecuteFixture`。

2. **最小權威證據要求 (Minimum Authoritative Evidence)**
   - **說明**：在執行下一次 fresh cycle 之前，必須透過唯讀查詢證明以下三點：
     1. 存在一個 task-marked 的 leader 聯絡人（其 `fullname` 以 `P7.2-SC-` 開頭）。
     2. 該 leader 聯絡人擁有一個 active 的 `systemuser` 作為 owner（即 `isdisabled` 為 `false`）。
     3. 該 owner 的 `systemuserid` **不等於** 當前 Data8 `WhoAmI` 使用者的 ID。

### 🟡 Warning (警告)
1. **UI 查詢錯誤 (UI Query Failure)**
   - **說明**：在 CE UI 中搜尋 task-marker list 時出現的 `0x80044150 SQL Server error` 應被視為平台/查詢故障，不能作為 owner 選擇的依據，亦不能視為有效的測試結果。

### 🟢 Info (資訊)
1. **測試覆蓋完整 (Test Coverage)**
   - **檔案路徑**：`ChurchReport.MemberInfo.Tests\P72Data8ListManagementFreshFixtureProvisionerTests.cs` (第 112-135 行)
   - **說明**：單元測試已完整覆蓋此邊界條件，證明程式碼在 `baseline-owner-unavailable` 情況下能確保 `MutationAttemptCount` 為 0 且 ledger 為空，邏輯安全無虞。
