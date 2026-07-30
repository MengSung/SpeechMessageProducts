# 最終程式審查報告 (Gateway HTTP & Canonical Queue Final Review)

本報告針對 `SpeechMessage.Dynamics.Gateway` 與 `SpeechMessage.Dynamics.WebApi` 相關的 HTTP 邊界防禦、Canonical 佇列序列化、資源生命週期管理及記憶體防洩漏設計進行完整審查。

## 驗證評分 (VALIDATION REPORT)
=================
* **使用者體驗 (User Experience)**: 20/20 - 邊界防禦設計優異，未授權要求回傳 403，無效媒體型別回傳 415，超限要求回傳 413，格式錯誤回傳 400。所有錯誤回應均為受控狀態，不洩漏內部 principal、token 或 session 資訊。
* **視覺與設計一致性 (Visual Consistency)**: 20/20 - 程式碼結構清晰，命名規範高度一致，且與既有的組織容量管理（Admission Plan）無縫對齊。
* **無障礙與安全性 (Accessibility / Security)**: 20/20 - 實作了 fail-closed 的 JSON-only 媒體型別檢查，且授權驗證（AuthN/AuthZ）嚴格先於任何 Body I/O 與 Buffer 租借，徹底杜絕了利用 Content-Type 探測 Body 契約的 Oracle 攻擊。
* **效能與資源治理 (Performance)**: 20/20 - 採用 `ArrayPool<byte>` 進行有界緩衝區租借，並在排隊（Admission Wait）前將原始 Request、JsonDocument 等大物件圖（Object Graph）完全斷開並釋放，極大地降低了 GC 壓力和記憶體殘留風險。
* **相容性 (Compatibility)**: 20/20 - Kestrel、IIS 與 Application Reader 完美共用同一個部署上限設定，且 `EstimatedEnvelopeBytes` 與 `CanonicalEnvelopeBytes` 的相容性設計確保了既有測試與 Admission 機制不受影響。

**總分 (TOTAL SCORE): 100/100**

**建議 (RECOMMENDATION): PASS (通過)**

---

## 審查發現 (FINDINGS)

### Critical Issues
* **無 (None)**：未發現任何 Critical 級別的安全性、正確性或資源洩漏風險。所有記憶體租借、取消路徑、異常處理均有嚴格的清零（Zero-Memory）與歸還機制。

### Warning Issues
* **無 (None)**：未發現任何會導致執行期錯誤或不符合規格的 Warning 級別問題。

### Info Issues

#### 1. Enum 型別的 Canonical 序列化限制
* **檔案位置**：`SpeechMessage.Dynamics.WebApi/Runtime/OperationDispatchPreparer.cs` (第 325-327 行)
* **情境說明**：當 Registry 定義的參數型別為 `enum` 時，`TryNormalizeValue` 會呼叫 `TryGetString` 將其視為字串處理。如果呼叫端傳入的是整數數值（例如 `JsonValueKind.Number`），則會因為無法轉換為字串而導致驗證失敗（回傳 `InvalidParameter`）。
* **影響**：這是一個非常嚴格的契約限制。在 Dynamics Web API 中，enum 參數通常以字串形式傳輸，因此此設計符合預期，但開發人員需注意呼叫端不可傳入整數值。
* **建議**：維持現狀。此嚴格限制有助於確保 Canonical 雜湊的 deterministic 特性。

#### 2. 嚴格的 JSON 格式限制
* **檔案位置**：`SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationRequestBodyReader.cs` (第 93-97 行)
* **情境說明**：`DefaultDocumentOptions` 設定了 `AllowTrailingCommas = false` 與 `CommentHandling = JsonCommentHandling.Disallow`。
* **影響**：任何包含尾隨逗號或 JSON 註解的要求都會被視為 `InvalidJson` 並回傳 400。
* **建議**：維持現狀。這符合 fail-closed JSON-only 的安全防禦原則。

---

## 關鍵設計與優點 (Positive Notes)

1. **授權優先於媒體型別驗證**：
   在 `Program.cs` 中，`operationAuthorizer.Authorize` 確實先於 `bodyReader.ReadAsync` 執行。未授權的要求會直接回傳 403，不會觸發任何 Body 讀取或 Buffer 租借，有效防止了未授權使用者探測 API 內部契約。
   
2. **精確的 UTF-8 Wire Bytes 計算與有界讀取**：
   `GatewayOperationRequestBodyReader` 在讀取未宣告長度（Chunked）的要求時，最多只會租借 `MaxRequestBodyBytes + 1` 的緩衝區，且一旦讀取位元組數超過上限便立即中斷並回傳 413，有效防止了阻斷服務攻擊（DoS）。

3. **徹底的記憶體清理（Zero-Tolerance Leakage）**：
   `PreparedOperationDispatch.Dispose` 與 `GatewayOperationRequestBodyReader.ReadAsync` 的 `finally` 區塊中，均使用了 `CryptographicOperations.ZeroMemory` 將整個租借的陣列清零後才歸還給 `ArrayPool`，確保敏感資料不會殘留在記憶體池中。

4. **排隊期間的記憶體隔離**：
   `ControlledOperationExecutor.ExecuteAsync` 在第一個 `await` 懸置點前，便透過 `TryPrepare` 完成了所有參數的正規化與實體型別轉換，並將原始的 `OperationExecutionRequest`、`HttpContext`、`ClaimsPrincipal` 等大物件完全釋放，避免了排隊等待期間將這些物件提升至二代 GC（Gen 2）的風險。

5. **詳盡的繁體中文註解**：
   所有新增與修改的程式碼均包含了極為詳盡的繁體中文 XML 註解，清楚說明了信任邊界、生命週期、Disposal 順序與記憶體取捨，完全符合專案的編碼規範。
