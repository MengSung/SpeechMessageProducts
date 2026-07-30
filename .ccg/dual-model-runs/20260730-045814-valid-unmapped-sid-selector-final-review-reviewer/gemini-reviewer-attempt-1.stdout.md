# 審查報告 (Review Report)

本報告針對 Dynamics Gateway 的 Windows SID 授權邊界補強與具名 Workload Binding Set 隔離機制進行程式碼審查。

---

## 1. 總體評估 (Summary)
本次變更成功解決了兩個關鍵的安全授權漏洞：
1. **有效但未對應的 SID 回退漏洞**：當已驗證的 Principal 帶有語法有效但未對應的 Windows SID 時，舊實作會錯誤地回退到同名的 Principal Name 授權，這可能導致帳號重建或名稱重用時的權限繼承。新實作確保了有效 SID 的權威性，未命中時立即拒絕。
2. **.NET 設定陣列合併導致的權限繼承**：透過引入具名的 `WorkloadBindingSets` 與單一的 `ActiveWorkloadBindingSet` 選擇器，避免了基底設定與開發設定合併時，因數字索引合併而意外繼承 Central 權限的問題。

所有變更均有對應的 TDD 測試保護，且程式碼中包含詳盡的繁體中文 XML 註解，符合專案的編碼與安全規範。

---

## 2. 審查問題回覆 (Review Questions)

### Q1: 任何有效但未對應的 SID 是否仍能透過其他路徑（包括操作目錄）取得名稱綁定？
**無此風險 (Info)**。
在 `ResolveAuthenticatedBinding` 中，一旦偵測到 `windowsSid` 不為 null，便會直接進行 SID 字典查找並回傳結果。若未命中，則直接回傳 null（拒絕授權），不會進入後續的 `principal.Identity.Name` 查找分支。此邏輯在 `Authorize` 與 `AuthorizeOperationCatalog` 中均被一致採用，確保了所有授權路徑的安全性。

### Q2: 無效或缺失的 SID 處理是否會意外擴大身分授權？
**無此風險 (Info)**。
若 SID 語法無效，`TryGetAuthenticatedWindowsSid` 會回傳 null；若 SID 缺失，則會安全地回退到精確的 `principal.Identity.Name` 查找。此行為僅作為無 SID 環境的相容性路徑，並未擴大任何既有的授權範圍。

### Q3: 選擇器測試是否真實針對各種 Provider 形狀（特別是無子節點的 JSON、僅有純量、純量加子節點）？
**確認通過 (Info)**。
`GatewayWorkloadBoundaryTests.cs` 中已補齊對應的測試案例：
- `Selected_childless_json_workload_binding_set_fails_authorizer_construction` 測試了真實的空 JSON 物件 `{}`。
- `Selected_scalar_workload_binding_set_fails_host_startup` 測試了純量值。
- `Selected_scalar_with_children_workload_binding_set_fails_host_startup` 測試了純量與子節點並存的歧義情況。
這些測試均能確實觸發 `InvalidOperationException` 並使 Host 啟動失敗。

### Q4: 生產環境的變更是否極簡、確定性、並行安全、記憶體分配受限且不影響清理？
**確認通過 (Info)**。
`ConfigurationGatewayOperationAuthorizer` 在建構子中一次性完成設定解析與驗證，並將其凍結為 `FrozenDictionary`。在 Request 熱路徑上僅進行 lock-free 的字典查找，無任何共享可變狀態、計時器、背景工作或資源分配，對效能與記憶體影響極小。

### Q5: 測試是否斷言真實行為而非 Mock，是否有任何測試能在未執行預期安全邊界的情況下通過？
**確認通過 (Info)**。
測試使用 `WebApplicationFactory` 啟動真實的記憶體中 Host，並透過 `HttpClient` 發送真實的 HTTP 請求，斷言其回傳的 HTTP 狀態碼（如 401, 403, 200）以及 executor 的呼叫次數。這些測試直接驗證了安全邊界的行為，而非 Mock 物件的呼叫。

### Q6: SPEC、階段證據、繁體中文說明與 CCG 任務狀態是否一致，且未改寫歷史審查結果或過度宣稱完成？
**確認通過 (Info)**。
所有文件與 SPEC 均保持一致，並明確指出 Phase 4-6 的整體閘門（如真實 CE 8.2/9.1 驗證、OData 投影等）仍然開放，未過度宣稱完成。

### Q7: 註解與編碼是否滿足硬性要求？
**確認通過 (Info)**。
所有新增與修改的程式碼均包含詳盡的繁體中文 XML 註解，且檔案編碼均為 UTF-8 without BOM，換行格式為 CRLF，並以 CRLF 結尾。

---

## 3. 契約驗證 (Contract Verification)

| 契約條款 | 驗證結果 | 說明 |
| :--- | :---: | :--- |
| 1. 有效 Windows SID 具權威性，存在時僅進行 SID 查找，未對應時回傳 `unmapped-principal` 且不回退至名稱。 | **PASS** | 已在 `ResolveAuthenticatedBinding` 中嚴格執行。 |
| 2. 僅在 Principal 完全無可用 SID 時，才允許精確名稱回退。 | **PASS** | 已在 `ResolveAuthenticatedBinding` 中實作。 |
| 3. 拒絕必須發生在建立 executor request、admission permit、secret/token 解析或 outbound transport 之前。 | **PASS** | 授權驗證在 `Program.cs` 的最前端執行，失敗時直接回傳 403。 |
| 4. `ActiveWorkloadBindingSet` 必須精確匹配一個子節點，且不得拼接為設定路徑。 | **PASS** | 透過 `GetChildren()` 遍歷並進行大小寫不敏感的精確比對。 |
| 5. 缺失、空白、前後空白、`*`、`?`、未知、含分隔符（如 `Local:0`）、純量、純量加子節點及空 JSON 集合均須 fail closed。 | **PASS** | 均會在建構子中拋出異常，阻止 Host 啟動。 |
| 6. 熱路徑保持有界、lock-free 凍結查找，不新增可變狀態、快取、計時器、背景工作或資源。 | **PASS** | 僅使用 `FrozenDictionary` 進行唯讀查找。 |
| 7. 所有新增/修改的程式碼必須包含深入的繁體中文 XML 註解，解釋信任邊界、擁有權、並行與效能取捨。 | **PASS** | 所有相關 C# 檔案均已補齊詳盡的繁體中文註解。 |
| 8. 檔案必須為 UTF-8 without BOM、CRLF-only 且以 CRLF 結尾。 | **PASS** | 經檢驗所有 scoped 檔案均符合此格式。 |
| 9. `Package01FeeReadsEnabled=false` 保持不變，保留 Embedded、Data8 與 `PowerPlatform.Dataverse.Client`，不宣稱後續階段已完成。 | **PASS** | 設定與專案結構均保持不變，文件亦未過度宣稱完成。 |

---

## 4. 審查發現分類 (Findings Classification)

### 關鍵缺陷 (Critical)
* **無**。

### 警告事項 (Warning)
* **無**。

### 一般觀察 (Info)
* **mojibake 說明**：在某些終端機環境下，由於解碼器設定不同，UTF-8 without BOM 的繁體中文註解可能會顯示為亂碼（mojibake）。經 strict UTF-8 decoder 驗證，檔案本身的位元組編碼是完全正確的，此為顯示端問題，不影響程式碼品質。

---

## 5. 最終推薦結論 (Final Recommendation)

**PASS**

本增量變更完全符合安全邊界與隔離契約，測試覆蓋完整，文件與註解詳盡，准予通過。
