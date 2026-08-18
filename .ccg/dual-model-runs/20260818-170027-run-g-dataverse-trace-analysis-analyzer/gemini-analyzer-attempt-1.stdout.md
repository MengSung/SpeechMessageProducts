# Run G Dataverse Trace 實作分析報告

本報告針對當前未提交的 Run G 工作進行程式碼審查與架構分析。本分析嚴格遵守唯讀限制，未對檔案系統進行任何寫入操作，且所有建議均在規定的 Whitelist 範圍內。

---

## 1. UX Analysis (使用者影響評估)

雖然此變更主要發生在後端基礎設施層（Dataverse 連線池與追蹤系統），但它對使用者體驗有間接但顯著的影響：
- **效能與回應時間**：追蹤系統的非阻塞設計（使用背景 `ConcurrentQueue` 與 `Task`）確保了日誌寫入不會阻塞 HTTP 請求執行緒。然而，若在 hot path 上發生不必要的記憶體分配（如 `Encoding.UTF8.GetString`），可能會增加 GC 壓力，進而導致短暫的請求延遲。
- **系統穩定性**：若日誌旋轉時因檔案鎖定導致 `File.Delete` 拋出異常，且未被妥善捕獲，將導致背景日誌執行緒崩潰，進而使整個系統的追蹤功能失效，影響維運人員排查問題的能力。

---

## 2. Design Evaluation (設計評估與一致性)

- **隱私保護**：使用隨機 HMAC salt 生成 `u_` 假名（如 `u_a1b2c3d4`）符合隱私合約，避免了在日誌中洩露真實的使用者名稱或 Session ID。
- **生命週期一致性**：`RequestScope` 和 `LeaseScope` 透過 `IDisposable` 模式在釋放時自動恢復 `AsyncLocal` 的上下文，這與 .NET 的標準資源管理模式一致，確保了非同步上下文的安全性。

---

## 3. Technical Considerations (技術考量與架構影響)

### (1) 編譯與 API 錯誤 (Likely Compile/API Errors)

*   **Critical**: `ToolUtility/Dataverse/DataverseTrace.cs` 缺少 `using System.Diagnostics;`
    *   **證據**：`DataverseTrace.cs` 第 132 行 `Stopwatch.GetTimestamp()` 和第 140 行 `Stopwatch.GetElapsedTime(...)` 使用了 `Stopwatch` 類別，但該檔案的 `using` 宣告中並未包含 `System.Diagnostics`。這將直接導致編譯失敗。
*   **Critical**: `ToolUtility/Dataverse/PooledClient.cs` 缺少 `ClientId` 屬性
    *   **證據**：`PooledClient.cs` 中完全沒有定義 `ClientId` 屬性，但在 `DataverseTrace.cs`（如第 290, 308, 324, 341 行）和 `DataverseTraceTests.cs` 中，都預期 `PooledClient` 擁有 `ClientId` 屬性（且格式為 `c-N`）。這會導致編譯失敗。
*   **Critical**: `ToolUtility/Dataverse/IClientLease.cs` 缺少 `LeaseId` 屬性
    *   **證據**：`IClientLease` 介面未宣告 `LeaseId` 屬性，但 `DataverseGateway` 需要獲取 `LeaseId` 以便呼叫 `PushLease`。這會導致無法直接存取 `LeaseId`。

### (2) 語意或隱私/生命週期缺陷 (Semantic or Privacy/Lifecycle Defects)

*   **Warning**: `ToolUtility/Dataverse/DataverseTrace.cs:521` 檔案刪除未處理異常
    *   **證據**：在 `PruneOldFiles` 方法中，`File.Delete(files[0])` 被直接呼叫。若該日誌檔案正被其他行程（如日誌收集工具）鎖定，將拋出 `IOException`，導致背景執行緒 `WriterLoopAsync` 崩潰並終止，使日誌系統完全失效。
*   **Warning**: `ToolUtility/Dataverse/DataverseTrace.cs:482` 效能缺陷（不必要的字串分配）
    *   **證據**：`_writer.Write(Encoding.UTF8.GetString(buffer.WrittenSpan))` 將 UTF-8 位元組陣列轉換為字串，然後 `StreamWriter` 又將其編碼回位元組寫入檔案。這在 hot path 的背景處理中造成了雙重編碼與額外的記憶體分配。

### (3) 缺失的檢測與確切的安全插入位置 (Missing Instrumentation & Safe Insertion Locations)

當前代碼庫中，多個 whitelist 檔案完全沒有呼叫 `DataverseTrace`。以下是確切的插入位置建議：

*   **Critical**: `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs` 缺少 DI 註冊
    *   **位置**：`ServiceCollectionExtensions.cs` 第 77 行之前。
    *   **建議**：
        ```csharp
        services.TryAddSingleton<DataverseTraceOptions>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            return DataverseTraceOptions.FromConfiguration(configuration);
        });
        services.TryAddSingleton<DataverseTrace>();
        ```
*   **Critical**: `SpeechMessageProducts.ChurchReport/Startup.cs` 缺少 Middleware 註冊
    *   **位置**：`Startup.cs` 第 835 行 `app.UseAuthentication();` 之後。
    *   **建議**：
        ```csharp
        app.UseAuthentication();
        app.UseMiddleware<DataverseTraceMiddleware>();
        ```
*   **Critical**: `appsettings.Development.json` 缺少 Trace 設定
    *   **位置**：`appsettings.Development.json` 第 13 行之後。
    *   **建議**：
        ```json
        "Trace": {
          "Enabled": true,
          "Path": "logs/dataverse-trace.jsonl"
        }
        ```
*   **Warning**: `ToolUtility/Dataverse/BoundedClientPool.cs` 缺少 Trace 呼叫
    *   **位置 1 (Wait/Timeout)**：第 125-130 行。
        ```csharp
        var stopwatch = Stopwatch.StartNew();
        if (!subPool.Slots.Wait(_options.AcquireTimeout, cancellationToken))
        {
            Interlocked.Increment(ref _acquireTimeouts);
            DataverseTrace.Current?.PoolAcquireTimeout();
            throw new TimeoutException(...);
        }
        stopwatch.Stop();
        DataverseTrace.Current?.PoolAcquireWait(stopwatch.ElapsedMilliseconds);
        ```
    *   **位置 2 (Hit)**：第 160 行之前。
        ```csharp
        var leaseId = "l-" + Guid.NewGuid().ToString("N");
        DataverseTrace.Current?.PoolAcquire(leaseId, candidate.ClientId, subPool.Key.ToString(), hit: true);
        ```
    *   **位置 3 (Miss)**：第 166 行之前。
        ```csharp
        var leaseId = "l-" + Guid.NewGuid().ToString("N");
        DataverseTrace.Current?.PoolAcquire(leaseId, created.ClientId, subPool.Key.ToString(), hit: false);
        ```
    *   **位置 4 (Health)**：第 150 行之後。
        ```csharp
        DataverseTrace.Current?.PoolHealth(candidate.ClientId, healthy);
        ```
    *   **位置 5 (Return)**：第 326 行 `Return` 方法開頭。
        ```csharp
        var callerId = (client.Service as OnPremiseClient)?.CallerId.ToString() ?? "";
        DataverseTrace.Current?.PoolReturn(leaseId, client.ClientId, client.State.ToString().ToLowerInvariant(), callerId, heldMs);
        ```
        *(註：需修改 `Return` 簽章以接受 `leaseId` 和 `heldMs`，並在 `ClientLease.Dispose` 中計算傳入)*
    *   **位置 6 (Cleanup)**：第 251 行之後。
        ```csharp
        DataverseTrace.Current?.PoolCleanup(idleBefore, idleAfter, _options.MinSize);
        ```
*   **Warning**: `ToolUtility/Dataverse/DataverseGateway.cs` 缺少 Trace 呼叫
    *   **位置 1 (Enter/Exit)**：第 39 行之後與第 51 行之前。
        ```csharp
        _depth++;
        DataverseTrace.Current?.GatewayExecuteEnter(_depth);
        ...
        finally
        {
            DataverseTrace.Current?.GatewayExecuteExit(_depth);
            _depth--;
            ...
        }
        ```
    *   **位置 2 (PushLease)**：第 38 行之後與第 54 行之後。
        ```csharp
        if (_depth == 0)
        {
            _lease = _manager.Acquire();
            // 使用反射獲取 LeaseId 以避免修改 IClientLease 介面
            var leaseId = _lease.GetType().GetProperty("LeaseId")?.GetValue(_lease) as string ?? "";
            _leaseScope = DataverseTrace.Current?.PushLease(leaseId);
        }
        ...
        finally
        {
            if (_depth == 0)
            {
                _leaseScope?.Dispose();
                _leaseScope = null;
                ...
            }
        }
        ```
*   **Warning**: `ToolUtility/Dataverse/GatewayOrganizationService.cs` 缺少 `crm.op` 呼叫
    *   **位置**：第 22-50 行的所有方法內部。
    *   **範例 (Create)**：
        ```csharp
        public Guid Create(Entity entity) => _gateway.Execute(service => {
            DataverseTrace.Current?.CrmOperation("Create");
            return service.Create(entity);
        });
        ```
*   **Warning**: `ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs` 缺少 `crm.op` 呼叫
    *   **位置**：第 27-51 行的所有方法內部。
    *   **範例 (Create)**：
        ```csharp
        public Guid Create(Entity entity) => Run(service => {
            DataverseTrace.Current?.CrmOperation("Create");
            return service.Create(entity);
        });
        ```

### (4) 範圍違規 (Scope Violations)

*   **Info**: 當前未提交的變更中，沒有超出 whitelist 的檔案修改。
*   **Warning**: 為了實現 `PushLease`，`IClientLease` 介面需要提供 `LeaseId`。由於 `IClientLease.cs` 不在 whitelist 中，直接修改它會造成範圍違規。建議在 `DataverseGateway` 中使用**反射**來獲取 `LeaseId`，以在不修改 `IClientLease.cs` 的前提下完成功能。

---

## 4. Options (替代方案與權衡)

*   **方案 A：使用反射獲取 `LeaseId`（推薦）**
    *   *優點*：完全遵守 whitelist 限制，不需要修改 `IClientLease.cs`。
    *   *缺點*：反射會帶來極微小的效能開銷（僅在每次獲取 lease 時執行一次，非 hot path）。
*   **方案 B：擴充 Whitelist 以修改 `IClientLease.cs`**
    *   *優點*：強型別安全，程式碼更乾淨。
    *   *缺點*：違反了 "Do not suggest expanding scope" 的限制。

---

## 5. Recommendation (建議方案與理由)

- 採用**方案 A**。在 `DataverseGateway` 中透過反射獲取 `IClientLease` 實作類別（即 `ClientLease`）的 `LeaseId` 屬性。這既能滿足 `PushLease` 的功能需求，又能嚴格遵守專案的範圍限制。
- 同時，必須修復 `DataverseTrace.cs` 中的 `using System.Diagnostics;` 缺失，並在 `PooledClient` 中補上 `ClientId` 屬性，以解決編譯錯誤。
