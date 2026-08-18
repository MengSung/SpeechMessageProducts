# Run G Dataverse Trace 實作分析報告

本報告針對目前儲存庫中未提交的 Run G Dataverse Trace 相關實作進行程式碼審查與架構分析。本分析嚴格遵守唯讀限制，不進行任何檔案修改，並專注於找出潛在的編譯錯誤、語意缺陷、隱私/生命週期問題、遺漏的檢測點（Instrumentation）以及確切的插入位置。

---

## 1. UX Analysis (使用者影響評估)

雖然 Dataverse Trace 屬於後端基礎建設與診斷系統，但其設計與效能表現會間接影響終端使用者的體驗：
- **效能零干擾 (Zero-Overhead on Hot Path)**：當 Trace 停用時（`Enabled = false`），系統僅進行一次布林值讀取與分支判斷，無任何記憶體分配（Zero Allocations）。這確保了在高併發的生產環境中，診斷系統不會對正常請求的響應時間（Latency）造成任何負面影響。
- **隱私安全保障 (Privacy-Safe Pseudonyms)**：系統採用隨機產生的 HMAC Salt 對使用者識別碼（Username/Session ID）進行去識別化，產生的假名（Pseudonym）在每次應用程式重啟時皆不同，且無法被逆向還原。這有效防止了敏感的使用者個人識別資訊（PII）洩漏至日誌檔案中，符合隱私合規要求。
- **非阻塞式寫入 (Non-blocking Overflow)**：日誌寫入採用背景執行緒與 `ConcurrentQueue` 異步處理，當隊列滿載時會自動丟棄舊記錄並記錄 `trace.dropped`，絕不阻塞主執行緒。這保證了即使 I/O 發生瓶頸，使用者的請求也不會因此卡頓或逾時。

---

## 2. Design Evaluation (設計評估與一致性)

- **日誌格式一致性**：採用 JSONL (JSON Lines) 格式，每行代表一個獨立的追蹤事件，便於日誌收集工具（如 ELK、Fluentd）進行結構化解析。事件 Schema 定義清晰，包含時間戳記（`ts`）、事件名稱（`ev`）以及各事件專屬的關聯欄位。
- **生命週期管理**：`DataverseTrace` 實作了 `IDisposable`，在應用程式關閉時會嘗試 Cancel 背景任務並同步等待其完成（Drain Queue），確保日誌不遺失。同時，在 `Dispose` 時使用 `CryptographicOperations.ZeroMemory` 清除記憶體中的 Salt，符合安全設計規範。
- **與 Run F 行為的相容性**：Run F 的連線池管理行為（如歸還時清除 `CallerId`、延遲釋放、最小連線數維持等）保持不變。Run G 的 Trace 僅作為觀察者（Observation Only），不改變任何連線池的語意與狀態機行為。

---

## 3. Technical Considerations (技術與架構考量)

- **編譯與 API 阻礙**：目前定義的 Trace 契約與現有的連線池型別（`PooledClient`、`IClientLease`）存在嚴重的 API 不匹配。`PooledClient` 缺少 `ClientId`，而 `IClientLease` 缺少 `LeaseId`，這導致 Trace 程式碼無法直接與現有架構整合，必須對這些型別進行擴充。
- **I/O 健壯性問題**：背景寫入執行緒在進行檔案旋轉與舊檔刪除時，缺乏對 `IOException` 的捕獲。在 Windows 環境下，若舊日誌檔正被其他行程讀取，刪除操作將會失敗並導致背景執行緒崩潰，進而使整個 Trace 系統失效，甚至在 `Dispose` 時引發應用程式崩潰。
- **雙重編碼開銷**：在寫入日誌時，將 `ArrayBufferWriter<byte>` 轉換為字串再經由 `StreamWriter` 寫入，造成了不必要的記憶體分配與編碼轉換開銷，在啟用 Trace 時會對效能產生一定影響。

---

## 4. Options (替代方案與權衡)

### 方案 A：直接擴充現有型別（推薦）
- **做法**：在 `IClientLease` 介面中新增 `string LeaseId { get; }` 屬性；在 `PooledClient` 類別中新增 `string ClientId { get; }` 屬性（格式為 `c-N`）。
- **優點**：能完美實現 Trace 所需的精確關聯，程式碼結構最為乾淨，符合原設計意圖。
- **缺點**：需要修改 `IClientLease.cs` 與 `PooledClient.cs`，但這兩個檔案在 whitelist 中有嚴格的讀取/修改限制。

### 方案 B：使用物件雜湊值或動態轉型（轉圜方案）
- **做法**：若嚴格限制不可修改 `IClientLease` 介面與 `PooledClient` 的公開 API：
  - 在 `BoundedClientPool` 內部，將 `PooledClient` 轉型為內部型別或使用 `RuntimeHelpers.GetHashCode` 作為臨時 `ClientId`。
  - 在 `DataverseGateway` 中，將 `IClientLease` 強制轉型為具體的 `ClientLease` 實作類別以獲取 `LeaseId`。
- **優點**：不改變公開介面契約。
- **缺點**：轉型操作脆弱，且無法保證 `c-N` 的穩定識別碼格式，違反任務契約。

---

## 5. Recommendation (推薦方案)

**採用方案 A**。雖然需要對 `IClientLease` 與 `PooledClient` 進行微幅擴充，但這是實現「穩定 PooledClient `c-N`」與「AsyncLocal 租約關聯」的唯一正確途徑。建議在後續開發階段中，將這兩個屬性的新增納入允許的修改範圍，以確保系統的類型安全與架構一致性。

---

## 6. Detailed Findings (詳細發現與程式碼證據)

### Critical (嚴重缺陷)

#### 1. `PooledClient` 缺少 `ClientId` 屬性導致編譯錯誤
- **檔案位置**：`ToolUtility/Dataverse/PooledClient.cs`
- **說明**：`DataverseTrace.cs` 中多個核心方法（如 `PoolAcquire`、`PoolHealth`、`PoolReturn`、`PoolDispose`）皆要求傳入 `clientId`。然而，`PooledClient` 類別中並未定義 `ClientId` 屬性或欄位，這會導致在 `BoundedClientPool` 中呼叫這些方法時發生編譯錯誤，也無法滿足「stable PooledClient `c-N`」的契約要求。
- **程式碼證據**：
  `PooledClient.cs` 中僅有 `Service`、`LastValidatedUtc`、`LastUsedUtc`、`State` 等屬性，完全無 `ClientId` 定義。

#### 2. `IClientLease` 介面缺少 `LeaseId` 屬性導致無法關聯租約
- **檔案位置**：`ToolUtility/Dataverse/IClientLease.cs`
- **說明**：`DataverseTrace.cs` 的 `PushLease(string leaseId)` 用於將租約 ID 關聯至當前請求上下文。然而，`IClientLease` 介面並未定義 `LeaseId` 屬性。在 `DataverseGateway.cs` 中，`Execute` 方法僅持有 `IClientLease` 介面，無法獲取具體的租約 ID，導致無法呼叫 `PushLease`，這違反了「AsyncLocal request/lease correlation restored on scope dispose」的設計要求。
- **程式碼證據**：
  `IClientLease.cs` 僅定義了 `IOrganizationService Service { get; }` 與 `void MarkFaulted();`。

#### 3. 背景寫入執行緒異常未捕獲導致 `Dispose` 崩潰
- **檔案位置**：`ToolUtility/Dataverse/DataverseTrace.cs:392`
- **說明**：在 `DataverseTrace.Dispose` 中，呼叫了 `_writerTask.GetAwaiter().GetResult()` 來同步等待背景寫入任務結束。如果背景任務 `WriterLoopAsync` 在執行過程中因為 I/O 異常（例如磁碟空間不足、寫入權限問題或檔案旋轉失敗）而終止，`GetResult()` 會重新拋出該異常，導致 `Dispose` 崩潰。這會影響 DI 容器的資源釋放流程，甚至導致應用程式異常終止。
- **程式碼證據**：
  ```csharp
  _writerWakeup.Cancel();
  _writerTask.GetAwaiter().GetResult(); // 若任務 Faulted，此處會拋出異常
  ```

---

### Warning (警告級別缺陷)

#### 1. 檔案旋轉時刪除舊檔案未處理 I/O 異常
- **檔案位置**：`ToolUtility/Dataverse/DataverseTrace.cs:521`
- **說明**：在 `PruneOldFiles` 方法中，呼叫了 `File.Delete(files[0])` 來刪除超出保留數量的舊日誌檔案。如果該檔案正被其他行程（如日誌收集工具、防毒軟體或系統管理員）鎖定，`File.Delete` 會拋出 `IOException`。由於此處沒有任何異常處理，該異常會直接導致背景寫入執行緒 `WriterLoopAsync` 崩潰終止。
- **程式碼證據**：
  ```csharp
  while (files.Count >= _options.MaxRetainedFiles)
  {
      File.Delete(files[0]); // 未使用 try-catch 保護，易因檔案鎖定崩潰
      files.RemoveAt(0);
  }
  ```

#### 2. `DataverseTraceOptions.FromConfiguration` 未讀取完整設定
- **檔案位置**：`ToolUtility/Dataverse/DataverseTrace.cs:42-53`
- **說明**：`FromConfiguration` 方法僅從設定中讀取了 `Enabled` 和 `Path` 屬性，而忽略了 `MaxFileBytes`、`MaxRetainedFiles`、`QueueCapacity` 和 `FlushInterval`。這導致這些關鍵的效能與容量參數始終只能使用硬編碼的預設值，無法透過 `appsettings.json` 進行調整。
- **程式碼證據**：
  ```csharp
  var section = configuration.GetSection("Dataverse:Trace");
  return new DataverseTraceOptions
  {
      Enabled = section.GetValue("Enabled", false),
      Path = section["Path"] ?? "logs/dataverse-trace.jsonl"
      // 缺少 MaxFileBytes, MaxRetainedFiles, QueueCapacity, FlushInterval 的讀取
  };
  ```

#### 3. 日誌寫入時存在不必要的字串分配與雙重編碼
- **檔案位置**：`ToolUtility/Dataverse/DataverseTrace.cs:482`
- **說明**：在 `WriteEntry` 中，`buffer` 已經是 UTF-8 編碼的位元組陣列（`ArrayBufferWriter<byte>`），但程式碼卻使用 `Encoding.UTF8.GetString(buffer.WrittenSpan)` 將其轉換為字串，再寫入 `StreamWriter`。`StreamWriter` 內部又會將該字串重新編碼為位元組寫入底層的 `FileStream`。這在 hot path 上造成了大量的字串分配與重複編碼開銷。
- **程式碼證據**：
  ```csharp
  _writer.Write(Encoding.UTF8.GetString(buffer.WrittenSpan)); // 雙重編碼與字串分配
  _writer.WriteLine();
  ```

---

### Info (建議與提示)

#### 1. 全域事件在無 HTTP 請求上下文時無法記錄
- **檔案位置**：`ToolUtility/Dataverse/DataverseTrace.cs:272, 280, 296, 314`
- **說明**：`PoolAcquireWait`、`PoolAcquire`、`PoolAcquireTimeout` 和 `PoolReturn` 等方法內部使用了 `TryGetRequest` 進行攔截。如果這些操作發生在背景任務（如 Hangfire、Queue 處理器或 Startup 預熱）中，此時沒有當前的 HTTP 請求上下文，這些重要的 pool 生命週期事件將會被直接忽略而不做任何記錄。
- **建議**：在無請求上下文時，仍記錄事件但將 `traceId` 與 `user` 設為空值。

#### 2. 匿名用戶的 Pseudonym 無法區分
- **檔案位置**：`ToolUtility/Dataverse/DataverseTrace.cs:240-253`
- **說明**：當 `identityName` 和 `sessionId` 皆為空時，`CreateUserPseudonym` 會使用 `"anon"` 作為輸入源。這會導致所有未登入用戶在同一次執行期中產生完全相同的 pseudonym。

---

## 7. Missing Instrumentation & Exact Insertion Locations (遺漏的檢測點與確切插入位置)

目前 `BoundedClientPool.cs`、`DataverseGateway.cs`、`GatewayOrganizationService.cs`、`AmbientGatewayOrganizationService.cs` 中完全沒有任何 `DataverseTrace` 的呼叫。以下為確切的插入位置建議：

### 1. `BoundedClientPool.cs` 插入點
- **等待信號量時**：在 `Acquire` 方法中（約第 125 行），在 `subPool.Slots.Wait` 之前啟動 `Stopwatch`。
  - 若 `Wait` 逾時，在拋出 `TimeoutException` 之前呼叫：
    ```csharp
    DataverseTrace.Current?.PoolAcquireTimeout();
    ```
  - 若 `Wait` 成功，在進入 `try` 區塊後呼叫：
    ```csharp
    DataverseTrace.Current?.PoolAcquireWait(stopwatch.ElapsedMilliseconds);
    ```
- **取得 Client 時 (Hit/Miss)**：
  - 在 `subPool.Idle.TryDequeue` 成功且 `TryLease` 成功後（約第 160 行），呼叫：
    ```csharp
    DataverseTrace.Current?.PoolAcquire(leaseId, client.ClientId, poolKey, hit: true);
    ```
  - 在建立新 client 並 `TryLease` 成功後（約第 167 行），呼叫：
    ```csharp
    DataverseTrace.Current?.PoolAcquire(leaseId, client.ClientId, poolKey, hit: false);
    ```
- **健康檢查時**：在 `_healthCheck` 執行後（約第 150 行），呼叫：
    ```csharp
    DataverseTrace.Current?.PoolHealth(client.ClientId, healthy);
    ```
- **歸還 Client 時**：在 `Return` 方法中（約第 342 行），呼叫：
    ```csharp
    DataverseTrace.Current?.PoolReturn(leaseId, client.ClientId, client.State.ToString(), callerIdAtReturn, heldMs);
    ```
- **釋放 Client 時**：在 `RemoveAndDispose` 方法中（約第 363 行）以及 `Dispose` 方法中（約第 276 行），呼叫：
    ```csharp
    DataverseTrace.Current?.PoolDispose(client.ClientId, client.State.ToString(), reason);
    ```
- **清理閒置 Client 時**：在 `CleanupIdleClients` 方法結束前（約第 251 行），呼叫：
    ```csharp
    DataverseTrace.Current?.PoolCleanup(idleBefore, idleAfter, _options.MinSize);
    ```

### 2. `DataverseGateway.cs` 插入點
- **進入與離開執行時**：
  - 在 `Execute<T>` 方法入口處（約第 37 行），呼叫：
    ```csharp
    DataverseTrace.Current?.GatewayExecuteEnter(_depth + 1);
    ```
  - 在 `finally` 區塊中（約第 51 行），呼叫：
    ```csharp
    DataverseTrace.Current?.GatewayExecuteExit(_depth);
    ```
- **關聯租約時**：
  - 在取得租約後（約第 38 行），呼叫：
    ```csharp
    var leaseScope = DataverseTrace.Current?.PushLease(_lease.LeaseId);
    ```
    並在 `finally` 區塊中釋放該 `leaseScope`。

### 3. `GatewayOrganizationService.cs` 和 `AmbientGatewayOrganizationService.cs` 插入點
- 在每個 `IOrganizationService` 介面實作方法（如 `Create`、`Retrieve`、`Execute` 等）的入口處，呼叫：
  ```csharp
  DataverseTrace.Current?.CrmOperation(nameof(MethodName));
  ```

### 4. `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs` 插入點
- 在 `AddToolUtility` 方法中（約第 50 行之前），加入：
  ```csharp
  services.TryAddSingleton<DataverseTraceOptions>(sp =>
  {
      var configuration = sp.GetRequiredService<IConfiguration>();
      return DataverseTraceOptions.FromConfiguration(configuration);
  });
  services.TryAddSingleton<DataverseTrace>();
  ```

### 5. `SpeechMessageProducts.ChurchReport/Startup.cs` 插入點
- 在 `Configure` 方法中，在 `app.UseAuthentication();`（第 835 行）之後，立即加入：
  ```csharp
  app.UseMiddleware<DataverseTraceMiddleware>();
  ```

### 6. `appsettings.Development.json` 插入點
- 加入 `Dataverse:Trace` 設定區段：
  ```json
  "Dataverse": {
    "Trace": {
      "Enabled": true,
      "Path": "logs/dataverse-trace.jsonl"
    },
    ...
  }
  ```
