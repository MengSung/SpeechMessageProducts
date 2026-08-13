# P7.4 ORG-CALL-00024 最終審查報告 (Final Review Report)

本審查針對 `ORG-CALL-00024` 的 ChurchReport 非空未分組承諾累計計數（ungrouped commitment aggregate counts）本地端修正進行驗證。

---

## 審查結論 (Decision)
**通過 (Passed)**：變更完全符合設計規範，所有安全閘門（Gates）預設關閉，且無任何 CE 寫入、流量啟用或 P7.5/P8 部署宣告。

---

## 審查發現 (Findings)

### 1. 閘門控制與 Fail-Closed 驗證
* **分類**：Info
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/appsettings.json`
  * `SpeechMessageProducts.ChurchReport/appsettings.Development.json`
  * `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs` (Lines 218-229)
* **說明**：
  * 簽入的設定檔中 `Package02UngroupedCommitmentReadEnabled` 均維持 `false`。
  * `IsPackage02UngroupedCommitmentReadEnabled` 實作中優先檢查了 Base Gate (`IsPackage02ContactProfileOperationsEnabled`)，任一閘門關閉即回傳 `false`，符合 Fail-Closed 原則。

### 2. ProfileAlias 預先驗證
* **分類**：Info
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs` (Lines 267-287)
* **說明**：
  * `TryCreatePackage02UngroupedCommitmentReadClient` 在進行任何 Process Host、Provider 或 Pool 解析前，先透過 `EnsureNonEmptyProductProfile` 驗證 `ProfileAlias` 是否為非空值。若為空則拋出 `InvalidOperationException`，防止未授權或未設定的連線建立。

### 3. 類型化計數與異常傳播 (No Fallback / No Retry)
* **分類**：Info
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Services/MemberInfo/Package02UngroupedCommitmentReadService.cs` (Lines 76-105)
  * `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs` (Lines 2126-2153)
* **說明**：
  * `RetrieveAsync` 使用固定的 `WorkloadSubjectId` 與 `ProfileAlias`，不接受呼叫端控制路由。
  * `LoadUngroupedCommitmentCountsAsync` 中的 Typed 分支沒有任何 `try-catch` 或重試機制，一旦發生異常會直接向上傳播，不會降級（Fallback）至舊有的 `CountUngroupedCommitmentValues` 查詢，確保錯誤邊界清晰。

### 4. 舊有快取旁路 (Cache Bypass)
* **分類**：Info
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs` (Lines 409-413, 1765-1802)
* **說明**：
  * 當啟用 Typed 計數且使用承諾排序時，`GetChurchGroupedCurrentIds` 的 `bypassCache` 參數會被設為 `true`。
  * 此設計旁路了 3 分鐘的 `IMemoryCache`，直接向 CRM 查詢最新的群組成員，避免了新舊計數因時間差產生數據不一致（Data Freshness Gap）的問題，且未引入任何快取或資源洩漏。

### 5. 防禦性複製與 DTO 驗證
* **分類**：Info
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Services/MemberInfo/Package02UngroupedCommitmentReadService.cs` (Lines 95-102, 125-136, 143-144)
* **說明**：
  * `RetrieveAsync` 內對上游 DTO 進行了嚴格驗證，若出現 `null`、重複的 Key 或負數計數，將直接拋出異常。
  * `Package02UngroupedCommitmentReadResult` 在建構子中對傳入的字典進行了防禦性複製（Defensive Copy），且 `GetCounts()` 回傳唯讀包裝的複本，防止外部惡意修改影響 Request-Local 狀態。

### 6. 邊界文件與測試覆蓋
* **分類**：Info
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs` (Lines 380-390, 2111-2115)
  * `ChurchReport.MemberInfo.Tests/Services/Package02UngroupedCommitmentReadServiceTests.cs`
  * `ChurchReport.MemberInfo.Tests/Controllers/MemberInfoControllerPackage02UngroupedCommitmentContractTests.cs`
* **說明**：
  * 公開 Action 與測試案例皆撰寫了詳盡的繁體中文邊界文件說明（XML 註解），明確標示此變更為本地端候選方案（local-only candidate），不涉及 CE 割接或 P7.5/P8 宣告。
  * 測試案例中完整覆蓋了 A/B 隔離性、取消權杖傳播、無舊有 Fallback 等關鍵路徑。
