# ChurchReport Local Gateway 與 Session 資源生命週期：實作前架構分析報告

本報告針對 `ChurchReport` 系統中 Local Gateway 安全切片與 Session 資源生命週期（包含 `HttpClient`、`SemaphoreSlim` 等資源殘留問題）進行架構分析與設計規劃。本分析僅制定架構、TDD 契約與檔案拆分，不修改實際程式碼。

---

## 一、 程式碼審查發現 (Code Review Findings)

### 1. Critical
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Models\DonationPaymentManager.cs`
*   **原理與影響**：
    `DonationPaymentManager` 內部自行建立了 `LineMessagingClient`（其內部持有自行 new 的 `HttpClient`）以及 `SemaphoreSlim _feeRefreshLock`。然而，該類別繼承自 `Controller` 卻**未實作 `IDisposable` 或 `IAsyncDisposable`**。當此 Manager 被放入快取且因過期或登出被淘汰時，底層的 Socket 連線與同步鎖資源將永久殘留在記憶體中，導致嚴重的資源與記憶體洩漏。

### 2. Critical
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Models\InMemoryDataContextSmallGroup.cs`
*   **原理與影響**：
    在 `DonationPaymentManager` 屬性的 `get` 存取器中，將實例以 Session 衍生 Key 存入 `IMemoryCache`。雖然註冊了 `PostEvictionCallbacks`，但其回呼函式僅呼叫了 `localCallbackInvoked.Set()`，**完全沒有對被淘汰的 `subValue`（即 `DonationPaymentManager` 實例）進行 `Dispose()` 釋放**。

### 3. Critical
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\AuthenticationController\AuthenticationController.Session.cs`
*   **原理與影響**：
    `Logout` 方法僅執行了 `HttpContext.Session.Clear()`、`SignOutAsync` 以及清除 Cookie。**並未主動通知快取層（`IMemoryCache`）移除並釋放該 Session 擁有的所有資源物件**。這導致舊的 Session 資源物件會繼續存活在快取中直到 30 分鐘的 TTL 到期，若使用者頻繁登入登出，將導致伺服器資源迅速耗盡。

### 4. Warning
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Services\DonationDynamicsAccessBootstrap.cs`
*   **原理與影響**：
    目前使用靜態的 `ProcessHost` 與獨立的子 `ServiceProvider` 來管理 `ProductClient` 與 `DynamicsAccess`。這種「服務定位器（Service Locator）」反模式繞過了 ASP.NET Core 的主 DI 容器，使得生命週期無法與主程式同步，且無法安全地實作 Fail-Closed 的啟動前置檢查（Preflight）。

### 5. Info
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Startup.cs`
*   **原理與影響**：
    `Package01FeeReadsEnabled` 配置必須保持 `false`。未來啟用時，必須確保主 DI 容器能安全解析相關 Client，並在配置不合規時觸發 Fail-Closed 啟動中斷。

---

## 二、 建議架構與資料／資源生命週期流程

為了徹底解決資源洩漏與並行競爭問題，設計以下生命週期管理架構：

```
[Client Request] ──> [AuthenticationController] ──> [ISessionResourceManager]
                                                            │
                                                   (Manage Session Graph)
                                                            │
                                                            ▼
                                                    [IMemoryCache]
                                                            │
                                                (PostEvictionCallback)
                                                            │
                                                            ▼
                                              [DonationPaymentManager]
                                                (Safe Idempotent Dispose)
                                                ├── Dispose SemaphoreSlim
                                                └── Dispose LineMessagingClient
```

### 1. 統一的 Session 資源管理器 (`ISessionResourceManager`)
建立一個全新的服務 `SessionResourceManager`，註冊為主 DI 容器的 Singleton。
*   **職責**：作為所有 Session-owned graph 的唯一 Owner。
*   **統一清理路徑**：提供 `EvictAndDisposeSession(string sessionId)` 冪等方法。
*   **觸發時機**：
    *   **登出 (Logout)**：主動呼叫 `EvictAndDisposeSession`。
    *   **快取過期 (Eviction)**：透過 `IMemoryCache` 的 `PostEvictionCallback` 觸發。
    *   **主機關閉 (Host Shutdown)**：透過註冊 `IHostApplicationLifetime.ApplicationStopping`，遍歷並釋放所有活動中的 Session 資源。

### 2. 冪等釋放與並行防禦
*   `DonationPaymentManager` 實作 `IDisposable`。
*   內部使用 `private int _disposedState = 0;`（0: 未釋放, 1: 釋放中/已釋放）。
*   使用 `Interlocked.CompareExchange(ref _disposedState, 1, 0)` 確保 `Dispose` 邏輯僅執行一次。
*   **釋放順序**：
    1.  將狀態設為已釋放，後續進入 Manager 方法的請求立即拋出 `ObjectDisposedException`。
    2.  呼叫 `_feeRefreshLock.Dispose()` 釋放訊號量。
    3.  呼叫 `m_LineMessagingClient` 的釋放邏輯（若有）。

### 3. 避免 Logout 與活動中 Request 競爭
*   引入 **Active Request Counter (Lease 機制)**：
    *   每個 Session 資源在被 Request 使用時，呼叫 `AcquireLease()`（計數器加 1）。
    *   Request 結束時在 `finally` 區塊呼叫 `ReleaseLease()`（計數器減 1）。
    *   當 Logout 觸發時，將資源標記為 `DisposedPending`。只有當計數器歸零時，才真正執行 `Dispose`。
    *   或者，使用 `ReaderWriterLockSlim`：Request 持有讀取鎖，Logout 清理持有寫入鎖，確保清理時無任何活動中請求。

### 4. 避免快取回呼閉包洩漏 (Closure Leakage)
*   `IMemoryCache` 的 `PostEvictionCallback` 必須註冊為 **`static` 靜態方法**。
*   透過 `PostEvictionCallbackRegistration.State` 僅傳遞最小上下文（例如僅包含 `SessionId` 與需要被釋放的 `IDisposable` 弱引用），**絕對不允許捕獲 `HttpContext`、`Controller` 或整個 DI 容器**。

---

## 三、 精確 RED Test Matrix

在進行任何程式碼修改前，必須先撰寫以下測試案例，並確保其在現有程式碼結構下皆會**失敗 (RED)**：

| 測試案例名稱 | 測試目標與步驟 | 預期失敗原因 (現況缺口) |
| :--- | :--- | :--- |
| `Test_SessionEviction_Should_Dispose_Manager_And_Semaphore` | 1. 模擬寫入快取。<br>2. 手動觸發快取淘汰。<br>3. 驗證 `DonationPaymentManager` 的 `Dispose` 被呼叫，且 `SemaphoreSlim` 已釋放。 | 快取淘汰回呼未呼叫 `Dispose`，資源殘留。 |
| `Test_Logout_Should_Immediately_Evict_And_Dispose_SessionGraph` | 1. 模擬用戶登入並建立快取物件。<br>2. 呼叫 `Logout`。<br>3. 驗證快取中該 Session 的物件已被移除且已執行 `Dispose`。 | `Logout` 僅清除了 ASP.NET Session，快取物件依然存活至 TTL。 |
| `Test_Concurrent_Request_And_Dispose_Should_Not_Throw_ObjectDisposed` | 1. 啟動一個長時間執行的 Manager 請求。<br>2. 在執行中途觸發 `Logout` 清理。<br>3. 驗證活動中請求能安全完成，且隨後資源被正確釋放。 | 缺乏 Lease/Drain 機制，活動中請求會因資源被提前 Dispose 而拋出 `ObjectDisposedException`。 |
| `Test_Double_Dispose_Should_Be_Idempotent` | 1. 取得 Manager 實例。<br>2. 同時在多個執行緒呼叫 `Dispose`。<br>3. 驗證不會拋出任何異常，且底層資源僅被釋放一次。 | 未實作 `Interlocked` 防禦，重複釋放 `SemaphoreSlim` 會拋出異常。 |
| `Test_HostShutdown_Should_Clean_All_Active_Sessions` | 1. 建立多個活動 Session 快取。<br>2. 模擬主機關閉訊號（`ApplicationStopping`）。<br>3. 驗證所有快取實例皆被 Dispose。 | 沒有全域的生命週期監聽與遍歷釋放機制。 |
| `Test_GatewayPreflight_FailClosed_When_ConfigInvalid` | 1. 設定 `Package01FeeReadsEnabled = true`。<br>2. 提供無效的 Dynamics 端點或憑證。<br>3. 啟動 Host。<br>4. 驗證 Host 啟動失敗（Fail-Closed）。 | 目前使用 static bootstrap，無法在 Startup 階段進行阻斷式 Preflight 驗證。 |
| `Test_Gateway_Should_Reject_Spoofed_Headers` | 1. 發送帶有 `X-Principal: spoofed-admin` 的請求。<br>2. 驗證 Gateway 內部的 WhoAmI 依然使用後端安全憑證，不信任該 Header。 | 缺乏明確的信任邊界防禦驗證。 |

---

## 四、 建議檔案變更與平行 Ownership

為支援團隊平行開發，將工作拆分為三個非重疊的 Layer：

### Layer 1A：Session 資源生命週期與釋放機制
*   **負責人員**：開發人員 A
*   **變更檔案**：
    *   `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`：實作 `IDisposable`，加入 `_disposedState` 與 `Interlocked` 冪等防禦。
    *   `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`：重構快取寫入邏輯，改用靜態快取淘汰回呼，並確實執行 `Dispose`。
    *   *新增* `SpeechMessageProducts.ChurchReport/Services/SessionResourceManager.cs`：實作 `ISessionResourceManager` 及其介面。
    *   *新增* `ChurchReport.MemberInfo.Tests/Payments/SessionLifecycleTests.cs`：撰寫快取淘汰、重複釋放、Lease 機制等單元測試。

### Layer 1B：主 DI 容器重構與 Gateway Preflight
*   **負責人員**：開發人員 B
*   **變更檔案**：
    *   `SpeechMessageProducts.ChurchReport/Startup.cs`：將 `DonationDynamicsAccessBootstrap` 納入主 DI 容器，註冊 `ISessionResourceManager`。
    *   `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`：移除靜態 `ProcessHost`，改為依賴注入建構子。
    *   *新增* `SpeechMessageProducts.ChurchReport/Services/GatewayPreflightService.cs`：實作 `IHostedService`，在啟動時執行 `runtime.health.whoami` 驗證，若失敗則阻止啟動。
    *   *新增* `ChurchReport.MemberInfo.Tests/Gateway/GatewayPreflightTests.cs`：驗證 Fail-Closed 啟動阻斷與 Spoof Header 防禦測試。

### Layer 2：登入／登出整合串接 (需等待 Layer 1 完成)
*   **負責人員**：開發人員 A 或 B
*   **變更檔案**：
    *   `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Session.cs`：在 `Logout` 方法中注入並呼叫 `ISessionResourceManager.EvictAndDisposeSession`。
    *   `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs`：確保私有 API 呼叫皆受到 Lease 機制保護。

---

## 五、 相容性與 Rollback 風險

1.  **`Package01FeeReadsEnabled` 保持為 `false`**：
    在所有 Gateway Preflight 測試與 DI 重構完成前，此 Flag 必須在生產環境保持為 `false`。若發生非預期錯誤，可直接將此 Flag 設為 `false` 進行 Rollback，系統將退回不啟用 Gateway 的安全模式。
2.  **`ObjectDisposedException` 風險**：
    若 Lease 機制未正確實作，可能會在用戶登出瞬間，導致其他並行 Request 拋出 `ObjectDisposedException`。必須確保單元測試覆蓋此競爭場景。
3.  **`PowerPlatform.Dataverse.Client` 相容性**：
    重構 DI 時，不得移除現有的 `PowerPlatform.Dataverse.Client` 與 Embedded 依賴，確保舊有 Dynamics 連線邏輯不受影響。

---

## 六、 程式碼 XML 註解規範 (強制品質規則)

所有新增或修改的程式碼，必須包含完整繁體中文 XML 註解。範例如下：

```csharp
/// <summary>
/// 管理 ChurchReport 捐款支付的管理器。
/// </summary>
/// <remarks>
/// 安全邊界與生命週期說明：
/// 1. 本類別持有 <see cref="SemaphoreSlim"/> 與 <see cref="LineMessagingClient"/>（內含 HttpClient），必須由擁有者妥善釋放。
/// 2. 實作 <see cref="IDisposable"/> 以支援快取淘汰時的資源清理。
/// 3. 釋放邏輯為執行緒安全且冪等（Idempotent），使用 Interlocked 確保僅釋放一次。
/// </remarks>
public class DonationPaymentManager : Controller, IDisposable
{
    private int _disposedState = 0;

    /// <summary>
    /// 釋放此管理器所持有的所有受控與非受控資源。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposedState, 1, 0) != 0)
        {
            return; // 已釋放，直接返回以確保冪等性
        }

        // 1. 釋放訊號量，阻斷後續並行請求
        _feeRefreshLock?.Dispose();

        // 2. 釋放 LineMessagingClient 資源
        if (m_LineMessagingClient is IDisposable disposableClient)
        {
            disposableClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
```
