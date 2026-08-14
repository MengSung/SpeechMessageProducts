### 總結 (Summary)
本次審查針對 P7.4 `MemberInfo` 伺服器端指派憑證（server-owned assignment evidence）的本地實現進行評估。整體架構設計符合安全合約，具備完善的 Request-Local 隔離、取消權杖傳遞、Fail-Closed 異常處理與 Registry 限制。然而，源碼檔案存在嚴重的**編碼損毀問題**，導致關鍵的安全過濾字串常數遺失並被替換為問號（`?`），這將直接導致授權邏輯失效。

---

### 嚴重問題 (Critical Issues)
* **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Package02Data8MemberInfoAuthorizationAssignmentOperations.cs`
  * **行號**：第 41 行、第 211-213 行
  * **問題描述**：該檔案在保存或轉換過程中經歷了破壞性的編碼轉換（例如 UTF-8 轉為 Big5/ANSI），導致字串常數中的中文字元損毀，並被替換為無法還原的實體問號 `?`（例如 `"?批葦?喲?"`、`"?折?銝颱遙"`）以及亂碼（`"撠??"`）。
  * **安全影響**：`IsChurchWideJobTitle` 與 `SmallGroupPurpose` 的比對邏輯依賴這些字串常數。由於 `?` 是遺失字元的替代符號，運行時與資料庫中的真實中文職稱（如「主任牧師」、「區長」）或目的（如「小組聚會」）比對將永遠失敗，導致 Church-wide 權限無法正確解析，或無法正確篩選小組，違反了 Fail-Closed 的安全防禦原則。必須立即使用正確的 UTF-8 編碼重新寫入這些中文字串。

---

### 警告事項 (Warning Issues)
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Security/MemberInfoServerAssignmentEvidenceSource.cs`
  * `SpeechMessage.Dynamics.ProductClient/MemberInfo/IMemberInfoAuthorizationAssignmentReadClient.cs`
  * `SpeechMessage.Dynamics.ProductClient/MemberInfo/MemberInfoAuthorizationAssignmentReadClient.cs`
  * `SpeechMessage.Dynamics.Connectors.Data8/Package02Data8MemberInfoAuthorizationAssignmentOperations.cs`
  * `ChurchReport.MemberInfo.Tests/Security/MemberInfoServerAssignmentEvidenceSourceTests.cs`
  * 以及其他 Dynamics 測試與註冊表檔案。
  * **問題描述**：上述檔案的中文註解均出現嚴重的亂碼（Garbled Comments），這是因為檔案採用了無 BOM 的 UTF-8 編碼，在特定 IDE 或工具鏈環境下被誤判為 Big5/CP950 編碼所致。
  * **建議**：應將所有專案源碼檔案統一轉換並儲存為 **UTF-8 with BOM** 格式，以確保跨平台與不同開發環境下編碼識別的一致性，避免後續再次發生破壞性編碼轉換。

---

### 資訊與優點 (Info / Positive Notes)
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Security/MemberInfoServerAssignmentEvidenceSource.cs`
  * **優點**：`ResolveAsync` 實現了良好的 Fail-Closed 機制。當底層 `ProductClient` 拋出非取消異常時，會捕獲並返回 `null` 憑證，進而由 Resolver 判定為 `SourceUnavailable` 拒絕存取，且未洩漏任何敏感的 CRM 錯誤細節。
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient/MemberInfo/MemberInfoAuthorizationAssignmentReadClient.cs`
  * **優點**：`ResolveBySubjectAsync` 嚴格驗證了回傳的 `SubjectContactId` 是否與請求一致，並對 `AssignedListIds` 進行了去重與唯讀封裝（`ReadOnlyCollection`），有效防止了多租戶/跨用戶的資料污染與篡改。
* **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Package02Data8MemberInfoAuthorizationAssignmentOperations.cs`
  * **優點**：查詢限制（`TopCount = 513`）與 `page.Entities.Count > 512` 的溢出檢查邏輯正確，能有效防止大數據量查詢造成的記憶體過載，符合 Bounded-Read 規範。

---

### 建議 (Suggestions)
1. **修復字串常數**：重新以 UTF-8 編碼編輯 `Package02Data8MemberInfoAuthorizationAssignmentOperations.cs`，將 `SmallGroupPurpose` 與 `IsChurchWideJobTitle` 中的比對字串修復為正確的繁體中文字元（如 "小組聚會"、"主任牧師"、"區長" 等）。
2. **統一編碼格式**：使用腳本或 IDE 批量將所有新增的 `.cs` 檔案轉換為 `UTF-8 with BOM` 編碼，並在 `.editorconfig` 中加入 `charset = utf-8-bom` 規範，防止未來再次發生破壞性編碼轉換。
