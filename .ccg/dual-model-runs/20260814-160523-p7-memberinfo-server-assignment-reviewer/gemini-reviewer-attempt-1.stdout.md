# P7.4 MemberInfo 伺服器端指派憑證審查報告 (MemberInfo Server-Owned Assignment Evidence Review)

## 1. 總體評估 (Summary)
本次審查針對 P7.4 未提交的子實作進行程式碼品質、設計一致性與安全性評估。該功能定位為 **Local-only**，不影響 Controller、Feature Gate、CE 流量或 P7.5/P8 的範疇。

整體架構設計符合 request-local 隔離、取消控制與 512 筆邊界限制的要求。然而，**偵測到嚴重的檔案編碼毀損（Mojibake/亂碼）問題**，這直接影響了字串常數與查詢條件的正確性，將導致執行階段授權邏輯完全失效。此問題被列為 **Critical** 級別，必須在合併前予以修復。

---

## 2. 關鍵發現與分類 (Findings & Classifications)

### 【Critical】檔案編碼毀損導致字串常數與查詢條件失效 (Mojibake in String Literals & Query Constants)
* **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Package02Data8MemberInfoAuthorizationAssignmentOperations.cs`
* **關鍵行數**：第 41 行、第 211-213 行
* **問題說明**：
  由於檔案編碼不一致（例如 UTF-8 檔案被以 ANSI/Big5 載入或儲存），導致程式碼中的中文實體字串常數與比對邏輯發生亂碼：
  1. 第 41 行：`private const string SmallGroupPurpose = "撠??";`（預期應為「小組名單」或相關定義）。此常數用於 CRM 查詢條件，將導致無法正確篩選出對應的 List。
  2. 第 211-213 行：
     ```csharp
     jobTitle?.Contains("?批葦?喲?", StringComparison.Ordinal) == true ||
     jobTitle?.Contains("?折?銝颱遙", StringComparison.Ordinal) == true ||
     jobTitle?.Contains("瑼Ｚ??冽????閮?, StringComparison.Ordinal) == true
     ```
     此處用於判斷是否為全教會職稱（Church-wide Job Title）的字串比對完全毀損。執行階段將無法正確識別「牧師」、「區長」等職稱，進而導致權限判定錯誤，退回至清單查詢或直接拒絕存取（Fail-closed）。
* **修復建議**：將該檔案重新以 **UTF-8 with BOM** 編碼儲存，並還原正確的中文常數與職稱字串。

### 【Critical】原始碼註解與文件編碼毀損 (Mojibake in Comments & Headers)
* **檔案路徑**：
  * `SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs`
  * `SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs`
  * `SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs`
  * `SpeechMessage.Dynamics.Connectors.Data8/Package02Data8MemberInfoAuthorizationAssignmentOperations.cs`
  * `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs`
* **問題說明**：
  上述所有檔案的標頭與 XML 註解均出現嚴重的亂碼（例如 `// 瑼?嚗peechMessage.Dynamics.Abstractions/Operations/OperationIds.cs`）。這不僅違反程式碼品質規範，也使得後續維護與審查極為困難，且有編譯器誤判字元集之風險。
* **修復建議**：統一將專案內所有受影響的 `.cs` 檔案轉換為標準的 UTF-8 編碼，確保註解可讀性。

### 【Info】邊界限制與重複項偵測實作符合預期 (Bounded 512-list Evidence Path & Duplicate Detection)
* **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Package02Data8MemberInfoAuthorizationAssignmentOperations.cs`
* **關鍵行數**：第 107-125 行
* **說明**：
  程式碼正確實作了 512 筆清單的邊界限制。透過設定 `TopCount = 513`（`OverflowSentinelTopCount`），並在偵測到 `page.MoreRecords` 或數量大於 512 時拋出 `InvalidOperationException`，有效防止 Unbounded Query 帶來的記憶體與效能風險。同時，利用 `HashSet<Guid>` 進行重複 ID 篩選，確保資料唯一性。

### 【Info】Request-Local 取消控制與資源隔離 (Request-Local Cancellation & Resource Isolation)
* **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Package02Data8MemberInfoAuthorizationAssignmentOperations.cs`
* **說明**：
  在每次進行 CRM SDK 呼叫（`Retrieve` 與 `RetrieveMultiple`）前後，皆有確實檢查 `cancellationToken.ThrowIfCancellationRequested()`。此設計符合 request-local 隔離原則，無靜態狀態殘留或跨用戶快取污染風險。

---

## 3. 評分報告 (Scoring Report for /ccg:bugfix validation)

```
VALIDATION REPORT
=================
User Experience: 18/20 - [後端授權邏輯設計符合 Fail-closed 安全原則，但亂碼會導致合法用戶無法通過驗證]
Visual Consistency: N/A - [此為後端 API 與 Connector 變更，無前端 UI 視覺呈現]
Accessibility: N/A - [無前端 UI，不適用 a11y 評估]
Performance: 19/20 - [嚴格限制 TopCount 為 513 並禁止分頁，有效防止大數據查詢造成的效能瓶頸]
Browser Compatibility: N/A - [後端服務，不涉及瀏覽器相容性]

TOTAL SCORE: 37/100 (因 Critical 編碼問題導致核心功能失效，予以扣分)

ISSUES FOUND:
- [Critical] Package02Data8MemberInfoAuthorizationAssignmentOperations.cs 中 SmallGroupPurpose 與 IsChurchWideJobTitle 的字串常數因編碼問題毀損（Mojibake），導致執行階段 Dynamics CRM 查詢與職稱比對失效。
- [Critical] 多個核心檔案（OperationIds.cs, OperationResponseData.cs, Package01OperationRegistry.cs 等）的標頭與 XML 註解出現亂碼，影響程式碼維護性。

RECOMMENDATION: NEEDS_IMPROVEMENT
```

---

## 4. 改善建議 (Suggestions)
1. **編碼標準化**：在 CI/CD 流程或 Git Hook 中加入編碼檢查，強制所有 `.cs` 檔案必須使用 `UTF-8` 或 `UTF-8 with BOM` 編碼，防止後續開發人員因編輯器設定不同而引入亂碼。
2. **單元測試覆蓋**：確保 `MemberInfoAuthorizationAssignmentData8Tests.cs` 中有針對 `IsChurchWideJobTitle` 的邊界測試，並使用真實的中文職稱（如 "牧師"）進行驗證，以利在編譯或測試階段及早攔截編碼問題。

---

## 5. 肯定之處 (Positive Notes)
1. **防禦性程式設計**：在 `OperationResponseData` 中實作了嚴格的 `ValidateSingleSafeBranch`，確保回傳的 DTO 結構單一且安全，避免多餘的欄位洩漏。
2. **資源生命週期管理**：Data8 執行器採用 `await using` 搭配 `AcquireAsync` 取得連線租約（lease），並在異常時呼叫 `lease.MarkFaulted()`，資源釋放與連線池管理機制健全。
