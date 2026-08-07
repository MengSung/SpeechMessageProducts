# P7.1 Package01 Data8 Read 代碼審查報告

本審查針對 P7.1 擁有的變更進行安全性、設計一致性、輸入驗證、資源釋放與 Required behavior 的合規性評估。

---

## 1. 總體評估 (Summary)
本次變更完整實作了 P7.1 所要求的六項固定、型別化且列入白名單的 Data8 唯讀操作，並嚴格限制於 `sunnyvalechback` CE 9.1 `Embedded + Data8` 模式。
- **功能合規性**：`Package01FeeReadsEnabled` 保持為 `false`，未啟用產品流量或變更既有行為。
- **安全性與隔離性**：PowerShell 遞交腳本（Handoff）採用 Windows Generic Credential 讀取憑證，秘密僅在短暫的子進程環境變數中傳遞，並在 `finally` 中確實清除與恢復環境，無洩漏風險。
- **架構設計**：`Package01Data8ReadOperations.cs` 集中管理查詢與投影邏輯，拒絕任何外部傳入的 FetchXML 或通用 CRUD，且對回應大小與頁數進行了嚴格的 checked 算術限制，防止記憶體無界增長。
- **歸檔相容性**：`validate_coverage.py` 改用結構性錨點 `.trellis/tasks` 尋找儲存庫根目錄，解決了任務歸檔後目錄深度改變導致的定位失敗問題。

---

## 2. 輔助功能與 UI 評估 (Accessibility & UX)
*註：本變更主要為後端 API、測試與自動化腳本，無直接前端 UI 元件。以下針對腳本的 CLI 互動與錯誤回饋（UX）進行評估：*
- **錯誤回饋 (UX)**：PowerShell 腳本在輸入驗證失敗或憑證不可用時，能立即 fail closed 並輸出結構化的 JSON 錯誤訊息（如 `repository-invalid`、`fixture-input-invalid`、`credential-unavailable`），不洩漏敏感資訊，便於自動化工具解析。

---

## 3. 設計與代碼一致性 (Design & Code Quality)

### **Warning**
* **檔案路徑**：`docs/scripts/Invoke-Package01Data8ReadEvidence.ps1` (第 470 行)
  * **主旨**：`finally` 區塊中的 `Remove-Item` 異常可能中斷後續的變數清理。
  * **說明**：腳本設定了 `$ErrorActionPreference = 'Stop'`。在 `finally` 區塊中，如果 `Remove-Item -LiteralPath $temporaryDirectory -Force -Recurse` 因為檔案被鎖定等原因拋出終止錯誤，將會中斷 `finally` 區塊的執行，導致後續的變數清理（如 `$credentialPassword = $null`）無法執行。
  * **建議**：將 `Remove-Item` 包裹在子 `try/catch` 中，或使用 `-ErrorAction SilentlyContinue`，以確保所有清理步驟（特別是敏感變數的釋放）必定執行。

### **Info**
* **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs` (第 606 行)
  * **主旨**：`StrictUtf8.GetByteCount` 異常捕獲範圍建議放寬。
  * **說明**：在 `ReadRequiredBoundedString` 方法中，`StrictUtf8.GetByteCount` 在遇到無效的 surrogate pair 時，在某些 .NET 執行期版本中可能會拋出 `ArgumentException`（其為 `EncoderFallbackException` 的基底類別）。
  * **建議**：建議同時捕獲 `ArgumentException`，以確保在所有 .NET 執行環境下均能正確 fail closed。

---

## 4. 建議改善事項 (Suggestions)
1. **PowerShell 臨時目錄清理防禦**：
   在 `Invoke-Package01Data8ReadEvidence.ps1` 中，將臨時目錄清理改為：
   ```powershell
   if ($null -ne $temporaryDirectory -and (Test-Path -LiteralPath $temporaryDirectory)) {
       try { Remove-Item -LiteralPath $temporaryDirectory -Force -Recurse -ErrorAction Stop } catch {}
   }
   ```
2. **字串驗證異常處理**：
   在 `Package01Data8ReadOperations.cs` 中，將 `catch (EncoderFallbackException)` 改為 `catch (ArgumentException)`，以涵蓋所有編碼回退與無效字元引發的參數異常。

---

## 5. 優秀實作亮點 (Positive Notes)
1. **嚴格的記憶體與大小限制**：`Package01Data8ReadOperations.cs` 中使用 `checked` 關鍵字累積回應的 byte 數，並在超限時立即中斷，有效防止 CRM 惡意或異常大數據導致的拒絕服務（DoS）風險。
2. **無 SDK 洩漏的投影設計**：所有 CRM 實體（Entity、EntityReference、AliasedValue）均在 Connector 內部完成投影，對外僅曝露純值 DTO，確保了邊界隔離性。
3. **結構性儲存庫定位**：`validate_coverage.py` 引入的 `find_repository_root` 透過尋找 `.trellis/tasks` 錨點定位根目錄，極大地提升了腳本在歸檔路徑下的健壯性與可重現性。
