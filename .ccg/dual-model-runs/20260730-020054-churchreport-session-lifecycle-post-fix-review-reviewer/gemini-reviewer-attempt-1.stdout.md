# UI/Backend Review Report: ChurchReport Session Lifecycle Post-Fix Review

## 1. 總體評估 (Summary)

**審查結果：`PASS`**

本次審查針對 `SpeechMessageProducts` 專案中的 ChurchReport 本地網關（Local Gateway）生命週期、資源隔離、身份驗證邊界以及主 DI 託管整合進行了深度程式碼與變更審查。

當前 working-tree 的實作已完全修復了先前版本中存在的「Session 作用域在身份重設後發佈」、「過期快取在已移除插槽上發佈」以及「清理失敗導致 Active 計數假歸零」等三大生命週期缺陷。程式碼中新增了詳盡的繁體中文註解，且測試覆蓋率極高（包含針對並行競爭、主機關機、身份重設等邊界條件的單元測試），完全符合核准的架構規格與安全不變式（Invariants）。

---

## 2. 輔助功能問題 (Accessibility Issues)

* **級別：N/A**
* **說明**：本次變更完全屬於後端生命週期、資源治理與網關整合邏輯，不涉及任何前端 UI 介面或 HTML/CSS 變更，故無 a11y 相關問題。

---

## 3. 設計一致性問題 (Design Issues)

* **級別：N/A**
* **說明**：變更檔案完全遵循專案既有的依賴注入（DI）與託管服務（Hosted Service）模式，並將舊有的靜態外觀（Static Facade）安全地橋接至主 DI 容器託管的單例（Singleton）生命週期中，設計一致性良好。

---

## 4. 建議與發現 (Suggestions & Info Findings)

### Info 1: 既有 LINE 用戶端生命週期未處置問題 (LINE Client Lifecycle Debt)
* **檔案路徑**：`Line.Messaging/LineMessagingClient.cs`
* **說明**：在 `LineMessagingClient` 中，許多既有的 HTTP 請求與回應方法（如 `GetMessageQuotaAsync` 等）在建立 `HttpRequestMessage` 或讀取 `HttpResponseMessage` 後，缺乏確定性的 `Dispose` 釋放。本次變更僅對 `MarkAsReadByTokenAsync` 的 XML 註解與部分空白進行了微調，並未新增或擴大此問題。
* **建議**：此問題已正確記錄為獨立的專案級生命週期阻礙器（Zero-Tolerance Lifecycle Blocker），應在後續的獨立任務中透過 TDD 與連線池監控進行完整修復，不應與本次網關生命週期變更混淆。

### Info 2: 部分檔案註解編碼亂碼問題 (Comment Encoding Artifacts)
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Services/Caching/SessionScopedResourceDisposalCoordinator.cs`
  * `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
  * `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
* **說明**：上述檔案在 working-tree 中雖然通過了 strict UTF-8 without BOM 與 CRLF 格式檢查，但部分繁體中文註解在編輯器或工具轉換過程中出現了亂碼（例如 `?嗉祥?桐?甈曉??????蝔????`）。
* **建議**：雖然這不影響程式碼的編譯與執行邏輯，但為了程式碼的可讀性與後續維護，建議在後續的清理提交中，將這些亂碼註解重新整理為正確的繁體中文。

---

## 5. 優秀實作點 (Positive Notes)

1. **嚴謹的 Stripe Lock 實作**：
   `SessionScopedResourceDisposalCoordinator` 透過 `session.Id` 的 SHA-256 雜湊值模 64 實作了分段鎖（Stripe Lock），確保了 Scope 建立、查詢與 Drain 動作在身份重設（Logout/Re-login）時的執行緒安全與原子性，徹底杜絕了舊 Scope 重新發佈的競爭風險。
2. **完善的清理失敗重試機制**：
   當資源 `Dispose` 拋出例外時，Coordinator 會將其狀態標記為 `CleanupFailed` 並保留在 `_failedCleanupEntries` 中，且 `_activeEntryCount` 不會遞減。這保證了系統不會回報假的零基準（False Zero Baseline），並允許後續的 Host 關機程序（`Dispose`）進行序列化重試。
3. **安全的 Preflight 錯誤淨化**：
   `DynamicsGatewayPreflightHostedService` 在執行啟動前置檢查（WhoAmI）時，使用 `CancellationTokenSource` 限制了 15 秒的超時，且在捕獲例外時會將其包裝並淨化為不含敏感資訊（如 CRM 網址、Token 等）的 `InvalidOperationException`，有效防止了敏感資訊洩漏。
4. **並行冪等性處置**：
   `DonationPaymentManager` 與 `DonationFeePaymentProcessor` 的 `Dispose` 實作皆採用了 `Interlocked.Exchange(ref _disposeState, 1)`，確保了在快取收回、登出與主機關機同時觸發時，自建的 LINE 用戶端與訊號量（Semaphore）只會被釋放一次，避免了重複釋放的例外。

---

## 6. 不變式與架構確認 (Explicit Confirmation)

本次審查確認以下關鍵架構約束均被嚴格遵守與保留：

* **`DynamicsAccess:Package01FeeReadsEnabled` 保持為 `false`**：
  在 `SpeechMessageProducts.ChurchReport/appsettings.json` 第 559 行中，該旗標明確為 `false`。在真實 Local Gateway、CE 9.1 驗證與瀏覽器 E2E 測試通過前，此旗標將維持關閉，確保生產環境流量完全走舊有 SOAP 安全路徑。
* **Embedded 模式保留 (Embedded Retention)**：
  `DynamicsAccess:ExecutionMode` 預設為 `"Embedded"`，且相關 Embedded 實作程式碼與專案參考均被完整保留，未被提前移除，符合「Embedded 保持存在但延後啟用」的架構決策。
* **Data8 專案保留 (Data8 Retention)**：
  已簽入的 Data8 `PowerPlatform.Dataverse.Client` 專案與舊有 SDK 依賴均完整保留，未在 Phase 6 移除條件達成前被提前清理。
* **無第二個 Dynamics HTTP/Provider 池**：
  ChurchReport 未在主 DI 託管的 `DonationDynamicsAccessProcessHost` 之外建立任何額外的 Dynamics HTTP 或 Provider 連線池，所有舊有靜態外觀均已安全對齊至該單例主機。

---

## 7. 驗證缺口 (Verification Gaps)

以下為目前尚未完成、仍阻礙真實 Local Gateway 或生產環境啟用的驗證缺口（這些缺口在現有規格文件中已被正確列出，不屬於程式碼缺陷）：

1. **真實 Local Gateway 本地端啟動與瀏覽器 E2E 驗證**：
   目前所有測試均在記憶體與 Mock 環境下完成，仍缺乏在 localhost 實際啟動 Local Gateway 並透過 ChurchReport 瀏覽器進行端到端（E2E）奉獻查詢的真實證據。
2. **CE 8.2 / CE 9.1 真實環境驗證與回滾演練**：
   缺乏連線至真實 CE 8.2（SOAP/WS-Trust）與 CE 9.1（Web API/ADFS OAuth）的 WhoAmI、授權矩陣與容量限制測試。
3. **跨行程容量與效能負載測試 (Soak/Performance Tests)**：
   尚未在多行程並發負載下，驗證 LocalDB 協調器、連線池釋放速度以及高負載下的資源存留基準。
