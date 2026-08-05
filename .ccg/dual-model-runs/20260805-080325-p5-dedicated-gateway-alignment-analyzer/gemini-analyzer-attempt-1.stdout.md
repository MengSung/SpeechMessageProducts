# P5 Dedicated Gateway Alignment 設計與現況分析報告

本報告針對目前未提交的 P5 變更進行設計與現況分析。目標是讓 `ChurchReport` 在 Visual Studio Multiple Startup Projects 以 `DedicatedGateway` 模式經 `https://localhost:7244/` 存取 Data8 runtime，同時保留 Development 預設的 Embedded F5 體驗。

---

## 1. UX Analysis (使用者影響評估)

*   **開發者體驗 (DX) 提升**：
    *   預設的 `appsettings.Development.json` 仍維持 `ConnectionMode: Embedded`，這確保了一般開發者在 Visual Studio 按下 F5 啟動 `ChurchReport` 時，不需要額外啟動 Gateway 進程，即可直接存取 Data8 runtime。這避免了不必要的 HTTP 網路開銷與複雜的進程管理。
    *   新增的 `DedicatedGateway` 啟動設定檔（launchSettings profile）允許開發者在需要驗證 HTTP 序列化、授權邊界或 RequestGuard 時，一鍵切換至 Dedicated Gateway 模式，提供極佳的彈性。
*   **安全性與隔離性**：
    *   Dedicated Gateway 模式下，產品端（`ChurchReport`）僅持有 `ConnectionMode`、`ProfileAlias` 與 Gateway 端點，完全不接觸 CRM 憑證或組織 ID。這符合最小權限原則，防止敏感資訊在外圍系統洩漏。

---

## 2. Design Evaluation (設計系統評估)

*   **共用 Runtime 核心**：
    *   `Data8ProfileRuntime` 作為共用的生命週期擁有者，同時服務於 Embedded 與 Dedicated Gateway 模式。這確保了兩種模式下的連線池（Pool）、容量限制（Admission）與解析器（Resolver）邏輯完全一致，達成了「開發測到的行為 ＝ 正式跑的行為」的設計目標。
*   **配置一致性**：
    *   `launchSettings.json` 中使用環境變數（如 `DynamicsAccess__ConnectionMode`）來覆寫預設配置，而不是修改實體 `appsettings.json` 檔案。這避免了開發人員意外將本機測試的 Dedicated 模式提交至 Git 儲存庫。

---

## 3. Technical Considerations (技術考量與架構影響)

*   **生命週期與資源釋放**：
    *   `Data8ProfileRuntime` 實作了 `IAsyncDisposable`，在釋放時會先排空連線池（drain pool），再釋放容量管理器（admission manager），確保資源釋放的決定性（deterministic disposal）。
    *   在 Dedicated Gateway 中，透過 `DedicatedData8RuntimeHostedService` 在 Host 停止時明確 await 釋放，避免遺留背景工作或未關閉的連線。
*   **安全防護網 (RequestGuard)**：
    *   `RequestGuard` 保持無狀態，僅檢查請求的合法性，不持有任何 HttpContext 或 Token。在 Dedicated 模式下，POST 呼叫明確傳入 `RequestOrigin.DedicatedGateway`，確保安全審查路徑的語意正確。

---

## 4. Options (替代方案評估)

*   **方案 A：共用同一個 `Data8ProfileRuntime` 類別，但各自 Host 獨立實例化（目前實作）**
    *   *優點*：程式碼重用率高，且 Embedded 與 Dedicated 模式的行為完全一致。各自 Host 擁有獨立的 DI 容器與實例，絕不跨模式或跨組織洩漏連線與 Permit。
    *   *缺點*：需要小心處理 DI 容器釋放時的生命週期管理。
*   **方案 B：為 Embedded 與 Dedicated 實作兩套不同的 Runtime 類別**
    *   *優點*：可以針對各自模式進行極致的優化。
    *   *缺點*：違反「治理層行為一致」的原則，容易導致開發環境（Embedded）與測試環境（Dedicated）行為不一致，增加維護成本。

---

## 5. Recommendation (建議方案)

**推薦採用方案 A（目前實作）**。該方案在維持程式碼高重用性的同時，透過獨立的 DI 容器實例化達成了嚴格的資源隔離。以下針對目前的實作程式碼進行詳細的審查與發現分類。

---

## 6. Code Review Findings (程式碼審查發現)

### ⚠️ Warning: `Data8ProfileRuntime` 與 `EmbeddedData8Runtime` 未實作 `IDisposable`

*   **具體檔案與行數**：
    *   `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileRuntime.cs` (第 20 行)
    *   `SpeechMessageProducts.ChurchReport/Services/EmbeddedData8Runtime.cs` (第 20 行)
*   **說明**：
    這兩個類別都只實作了 `IAsyncDisposable`，但沒有實作 `IDisposable`。雖然 ASP.NET Core 的 `ServiceProvider` 原生支援 `IAsyncDisposable`，但在某些僅支援 `IDisposable` 的第三方 DI 容器或同步釋放情境中，可能會導致資源無法被釋放。
*   **建議**：
    建議同時實作 `IDisposable`。由於內部的 `_poolRegistry` 與 `_admissionManager` 都支援同步的 `Dispose()`，因此在 `Data8ProfileRuntime` 中實作 `IDisposable` 是安全且可行的：
    ```csharp
    public void Dispose()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
            {
                _disposeTask.GetAwaiter().GetResult();
                return;
            }
            try { _poolRegistry.Dispose(); }
            finally { _admissionManager.Dispose(); }
        }
    }
    ```

---

### ℹ️ Info: `DedicatedData8RuntimeHostedService` 與 DI 容器重複釋放

*   **具體檔案與行數**：
    *   `SpeechMessage.Dynamics.Gateway/DedicatedData8RuntimeHostedService.cs` (第 23 行)
    *   `SpeechMessage.Dynamics.Gateway/Program.cs` (第 147-157 行)
*   **說明**：
    `Data8ProfileRuntime` 在 `Program.cs` 中被註冊為 Singleton，這意味著當 DI 容器釋放時，容器會自動呼叫其 `DisposeAsync`。然而，`DedicatedData8RuntimeHostedService` 在 `StopAsync` 中也明確呼叫了 `_runtime.DisposeAsync()`。這會導致該 Runtime 被釋放兩次。
*   **分析**：
    幸好 `Data8ProfileRuntime.DisposeAsync` 內部使用了 `_disposeTask ??= DisposeCoreAsync()` 進行冪等性保護，因此重複呼叫是安全的，不會引發例外或二次釋放問題。

---

### ℹ️ Info: `RequestGuard` 忽略了 `RequestOrigin` 參數

*   **具體檔案與行數**：
    *   `SpeechMessage.Dynamics.ControlPlane/Guard/RequestGuard.cs` (第 46 行)
*   **說明**：
    在 `RequestGuard.Inspect` 方法中，傳入的 `RequestOrigin origin` 參數被以 `_ = origin;` 忽略。目前這不會造成功能問題，因為 `RequestGuard` 的主要職責是檢查 `ProfileAlias`、`Parameters` 和 `CapabilityOperationId` 是否合法。但這是一個設計上的簡化，未來若需針對不同來源（如 Dedicated vs Central）進行差異化安全檢查，可在此處擴充。

---

### ℹ️ Info: Dedicated Gateway 啟動對環境變數 `CRM_PASSWORD` 的依賴

*   **具體檔案與行數**：
    *   `SpeechMessage.Dynamics.Gateway/Properties/launchSettings.json` (第 30 行)
    *   `SpeechMessage.Dynamics.Gateway/DedicatedGatewayData8Configuration.cs` (第 79-83 行)
*   **說明**：
    `launchSettings.json` 中的 `DedicatedGateway__Data8__Password` 預設值為 `"REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT"`。當以 `DedicatedGateway` 模式啟動時，系統會嘗試從環境變數 `CRM_PASSWORD` 讀取真實密碼。
*   **分析**：
    這是一個正確的安全實作，避免了將敏感密碼提交至 Git。開發人員在本機進行 Dedicated Gateway 測試時，必須確保本機環境變數 `CRM_PASSWORD` 已正確設定，否則啟動將會失敗。
