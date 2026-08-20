# ChurchReport 效能、Session 隔離與資源生命週期審查報告

本報告針對 ChurchReport 系統中的 Dataverse 連線池（`BoundedClientPool`）、閘道器（`DataverseGateway`）、追蹤機制（`DataverseTrace`）及效能分析器（`RequestProfiler`）進行唯讀架構與程式碼審查。分析重點在於評估系統在高並行環境下的**效能瓶頸**、**Session 隔離安全性**、**資源洩漏風險**及**觀測數據一致性**。

---

## 1. UX Analysis (使用者體驗與安全影響評估)

- **效能延遲與逾時體驗**：
  當系統面臨高並行請求時，若連線池因鎖競爭（Lock Contention）或同步網路 I/O 阻塞而導致 `Acquire` 逾時，前端使用者將直接面臨請求卡頓、加載失敗或收到 `504 Gateway Timeout` 錯誤。這會嚴重損害系統的可用性與使用者信任度。
- **Session 隔離與隱私洩漏**：
  若 Impersonation 狀態（`CallerId`）在連線歸還時未被徹底清理，或在並行請求中因閘道器狀態混用而發生外洩，使用者可能會在介面上看到其他使用者的敏感資料，或以他人身分執行寫入操作。這屬於**最高等級的安全紅線違規**，會導致嚴重的合規性與法律風險。
- **行動端與弱網環境影響**：
  在行動端或網路不穩定的情境下，Dataverse API 的響應時間會拉長。若連線池的健康檢查與連線建立機制不夠彈性，會放大鎖競爭效應，導致行動端用戶體驗極度惡化。

---

## 2. Design Evaluation (設計系統與模式一致性)

- **連線池狀態機一致性**：
  `PooledClient` 定義了明確的狀態機（`Idle → Leased → Faulted/Idle → Disposed`），且在歸還時透過 `TryClearCallerId` 進行 Fail-Closed 的安全防禦，設計模式上符合 Gateway Conformance 規範。
- **生命週期管理不一致**：
  在 `BoundedClientPool` 中，一般連線建立（`CreateClient`）是在鎖外執行網路 I/O，但維持最小連線數（`EnsureMinimum`）卻是在 `lock (subPool.Sync)` 鎖內執行網路 I/O。這種設計模式的不一致，埋下了嚴重的效能隱患。

---

## 3. Technical Considerations (前端與後端架構影響)

- **執行緒安全性（Thread Safety）**：
  `DataverseGateway` 作為 Scoped 服務，其內部成員變數 `_lease` 與 `_depth` 未受執行緒安全保護。在 ASP.NET Core 中，若單一 Request 內使用並行任務（如 `Task.WhenAll`），將直接導致狀態毀損與連線混用。
- **資源生命週期與洩漏**：
  `Timer` 的銷毀與連線池的 `Dispose` 缺乏同步協調，可能導致在物件釋放後，定時清理執行緒仍在存取已釋放的資源，引發未捕獲的異常或資源殘留。

---

## 4. Options (替代方案與權衡)

### 方案 A：維持現狀，僅調整配置參數
- **優點**：無需修改程式碼，無引入新 Bug 的風險。
- **缺點**：無法解決並行請求下的鎖競爭、並行任務下的狀態外洩，以及未來擴充非 `OnPremiseClient` 時的安全盲點。

### 方案 B：重構鎖範圍與閘道器狀態隔離（首選推薦）
- **優點**：
  - 將 I/O 移出鎖外，徹底消除鎖競爭。
  - 使用 `AsyncLocal` 或無狀態設計重構 `DataverseGateway`，確保並行任務下的 Session 隔離。
  - 引入反射或統一介面清理 `CallerId`，消除類型硬編碼的安全盲點。
- **缺點**：需要對核心連線池與閘道器程式碼進行局部重構，並需通過完整的單元與整合測試驗證。

---

## 5. Recommendation (首選方案與理由)

**首選方案 B**。
因為系統的安全隔離（Session Isolation）與高可用性（無鎖競爭阻塞）是系統穩定運行的基石。方案 B 能夠在不改變現有架構層次的前提下，精準修復並行安全漏洞與效能瓶頸，並為未來的 SDK 升級提供良好的擴充性。

---

# 核心審查發現分級報告 (Critical / Warning / Info)

## Critical (關鍵缺陷)

### 1. `DataverseGateway` 非執行緒安全，並行 Request 下存在狀態外洩與連線混用風險
- **觀察證據**：
  在 `DataverseGateway.cs` 中，類別註冊為 Scoped 生命週期，內部維護了成員變數 `private IClientLease _lease;` 與 `private int _depth;`。在 `Execute<T>` 方法中，直接對這些成員變數進行讀寫與修改，且無任何同步鎖保護。
- **根因**：
  Scoped 服務在同一個 HTTP 請求上下文中是單例的。若該請求中存在並行非同步操作（例如使用 `Task.WhenAll` 呼叫 `DataverseGateway.Execute`），多個執行緒會同時存取與修改同一個 `DataverseGateway` 實例的 `_lease` 與 `_depth` 欄位。
- **風險**：
  1. `_lease` 被後續並行呼叫覆蓋，導致先前的租約無法被正確釋放，造成連線洩漏。
  2. 多個執行緒並行使用同一個 `IClientLease`（即同一個底層 Dataverse `OnPremiseClient`），這違反了 Dataverse Client 非執行緒安全的設計，會導致嚴重的並行衝突、資料錯亂或連線崩潰。
  3. `_depth` 計數錯亂，導致租約被提前 `Dispose`，後續操作存取已釋放的連線而拋出 `ObjectDisposedException`。
- **建議修正方向**：
  避免在 Scoped `DataverseGateway` 中使用成員變數來儲存 `_lease` 與 `_depth`。應改用 `AsyncLocal<GatewayContext>` 來隔離同一個請求中不同非同步控制流的狀態，或者將 `Execute` 設計為無狀態，每次呼叫皆獨立獲取與釋放租約。

### 2. `BoundedClientPool.EnsureMinimum` 在鎖內執行網路 I/O 導致嚴重的鎖競爭與逾時風險
- **觀察證據**：
  在 `BoundedClientPool.cs` 的 `EnsureMinimum` 方法中：
  ```csharp
  lock (subPool.Sync)
  {
      var missing = _options.MinSize - subPool.All.Count(client => client.State != PooledClientState.Disposed);
      for (var index = 0; index < missing; index++)
      {
          var client = CreateClientCore(key, cancellationToken); // 鎖內執行 I/O
          subPool.All.Add(client);
          subPool.Idle.Enqueue(client);
      }
  }
  ```
  `CreateClientCore` 會呼叫 `_clientFactory`，進而執行 `CreateOnPremiseClient`，這涉及網路連線與驗證等耗時的同步 I/O 操作。
- **根因**：
  將高延遲的網路 I/O 操作置於 `lock (subPool.Sync)` 區塊內。
- **風險**：
  當連線池需要補充最小連線數（例如初始化或閒置清理後），任何呼叫 `Acquire` 或 `Return` 的執行緒在嘗試獲取 `subPool.Sync` 鎖時都會被長時間阻塞。這會導致 `Acquire` 逾時（`TimeoutException`），並在高並行情況下引發嚴重的執行緒飢餓與鎖競爭，大幅降低系統吞吐量。
- **建議修正方向**：
  將 `CreateClientCore` 移出 `lock (subPool.Sync)` 鎖。可以在鎖內先計算需要建立的數量，在鎖外建立 client 實例，最後再上鎖將建立好的實例加入 `subPool.All` 與 `subPool.Idle`。

---

## Warning (警告事項)

### 1. `TryClearCallerId` 僅針對 `OnPremiseClient` 進行清理，存在未來擴充時的狀態外洩盲點
- **觀察證據**：
  在 `PooledClient.cs` 中：
  ```csharp
  private bool TryClearCallerId()
  {
      if (Service is not OnPremiseClient onPremiseClient)
          return true; // 非 OnPremiseClient 直接跳過清理
      ...
  }
  ```
- **根因**：
  硬編碼僅對 `OnPremiseClient` 進行類型檢查與 `CallerId` 清理。
- **風險**：
  如果未來 Dataverse SDK 升級或架構調整，改用 `ServiceClient` 或其他自訂的 `IOrganizationService` 裝飾器（Decorator），此處的類型檢查會直接返回 `true`，導致 Impersonation 狀態（`CallerId`）未被清除就將連線歸還至池中。下一個請求取得該連線時，將會以先前使用者的身分執行操作，造成嚴重的跨使用者資料外洩（Session Leakage）。
- **建議修正方向**：
  應透過反射動態檢查 `Service` 實體是否存在 `CallerId` 屬性，或者定義一個統一的介面（例如 `IImpersonateableService`），由連線實作，並在歸還時統一清理。若無法確定，應在偵測到未知的 `IOrganizationService` 實作且無法確認其 Impersonation 狀態時，採取 Fail-Closed 策略，拒絕將其歸還至 Idle 池。

### 2. `BoundedClientPool.Dispose` 與 `CleanupTimer` 缺乏同步協調，可能導致釋放後存取與資源滯留
- **觀察證據**：
  在 `BoundedClientPool.cs` 中，`_cleanupTimer` 在 `Dispose` 時被釋放：
  ```csharp
  _cleanupTimer.Dispose();
  ```
  但 `CleanupTimerCallback` 仍在執行：
  ```csharp
  private void CleanupTimerCallback(object state)
  {
      try { CleanupIdleClients(); } catch { }
  }
  ```
- **根因**：
  `Timer.Dispose()` 並不保證已觸發的回呼函數執行完畢。在 `Dispose` 釋放了 `_subPools` 並呼叫 `subPool.Slots.Dispose()` 後，併發的 `CleanupIdleClients` 可能仍在執行，並嘗試存取已釋放的 `subPool.Slots` 或 `subPool.All`。
- **風險**：
  引發 `ObjectDisposedException` 或未預期的 Null 參考異常，甚至可能導致部分連線在 `Dispose` 過程中未被正確關閉，造成資源滯留（Resource Leakage）。
- **建議修正方向**：
  使用 `Timer.Dispose(WaitHandle)` 重載版本，同步等待所有回呼執行完畢後，再進行後續的資源釋放與清理。

---

## Info (提示資訊)

### 1. Release 環境下 `RequestProfiler` 缺失導致的數據不一致
- **觀察證據**：
  `RequestProfiler.cs` 整個類別被包裹在 `#if DEBUG` 條件編譯中。
- **根因**：
  在 Release 建置中，`RequestProfiler` 相關程式碼不會被編譯，因此 `Trace.log` 中不會產生任何 `[Perf]` 相關的效能日誌。
- **風險**：
  當運維人員在生產環境（Release）執行 `Analyze-ChurchReportTraces.ps1` 時，由於缺乏 `Trace.log` 中的效能數據，分析腳本會因為「三檔證據集合不完整」而發出 `WARN` 或 `FAIL`，且無法提供端到端的效能關聯分析。
- **建議修正方向**：
  評估是否將 `RequestProfiler` 的核心量化指標（如 Action 耗時、CRM 呼叫次數與總耗時）整合至 `DataverseTrace` 的 `request.end` 事件中，統一輸出至 JSONL，避免依賴條件編譯的 `Trace.log`。

### 2. 建議新增之可量化、低敏感度 Trace 指標
- **觀察證據**：
  目前 `DataverseTrace` 缺乏對連線池飽和度與連線建立失敗的細粒度觀測。
- **建議修正方向**：
  1. **SubPool 飽和度與等待計數**：在 `pool.acquire.wait` 事件中，新增記錄當時的 `waiting` 執行緒數與 `leased` 連線數，這有助於評估是否需要調整 `MaxN`。
  2. **連線建立失敗率**：在 `CreateClientCore` 拋出異常時，新增 `pool.client.create.failed` 事件，記錄失敗原因（去敏感化），以便區分是 Dataverse 伺服器拒絕連線，還是連線池本身的問題。
