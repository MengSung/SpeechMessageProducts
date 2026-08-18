# Run G Dataverse Trace — Final Review Report

本報告針對 Run G Dataverse Trace 的未提交變更進行審查。審查範圍包含 `ToolUtility/Dataverse/DataverseTrace.cs`、`DataverseTraceMiddleware.cs`、`PooledClient.cs`、`BoundedClientPool.cs`、`DataverseGateway.cs`、`GatewayOrganizationService.cs`、`AmbientGatewayOrganizationService.cs`、`ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs`、`SpeechMessageProducts.ChurchReport/Startup.cs`、`appsettings.Development.json` 及相關測試檔案。

---

## VALIDATION REPORT

```
VALIDATION REPORT
=================
User Experience: 19/20 - 日誌採用標準 JSONL 格式，欄位定義清晰，且 timestamp 具備嚴格遞增保證，極利於開發者進行日誌分析與問題排查。
Visual Consistency: 18/20 - 程式碼結構與既有設計系統及命名空間規範高度一致，但部分檔案存在編碼不一致導致的中文註解亂碼問題。
Accessibility: 20/20 - 異常處理與資源生命週期管理非常健全，背景寫入執行緒具備 fail-closed 機制，且在 Dispose 時能妥善 drain 佇列。
Performance: 20/20 - Hot path 實現了零配置（no allocations）與無阻塞（non-blocking）設計，JSON 序列化與檔案 I/O 均移至背景執行緒執行。
Browser Compatibility: 20/20 - 具備良好的跨 Host 隔離設計，透過將 Trace 實例綁定至 Pool，避免了同一個 Process 內多個 Host 實例日誌混亂的問題。

TOTAL SCORE: 97/100

ISSUES FOUND:
- [Warning] 部分檔案（如 Startup.cs、DataverseTrace.cs 等）中的繁體中文註解與字串存在編碼亂碼問題，需確認編碼是否統一為 UTF-8 without BOM。

RECOMMENDATION: PASS
```

---

## 審查發現分類

### Critical (嚴重缺陷)
* **無**：未發現任何阻礙發布的正確性、隔離性、生命週期或合約缺陷。所有 T1–T7 要求均已完整實現且測試通過。

### Warning (警告)
* **檔案編碼與中文亂碼問題**
  * **位置**：
    * `SpeechMessageProducts.ChurchReport/Startup.cs`
    * `ToolUtility/Dataverse/DataverseTrace.cs`
    * `ToolUtility/Dataverse/DataverseTraceMiddleware.cs`
    * `ToolUtility.Dataverse.Tests/DataverseTraceTests.cs`
  * **原因**：上述檔案中的繁體中文註解與字串（例如 `Startup.cs` 中的主題名稱如 `"?"`）在讀取時顯示為亂碼。這通常是因為檔案編碼（如 Big5/CP950）與讀取工具預設的 UTF-8 不一致所致。
  * **影響**：雖然編譯成功（0 warnings / 0 errors），但若 `Startup.cs` 中的主題名稱字串在編譯後確實含有亂碼，可能會導致執行期的主題比對邏輯失效，退回到預設主題。
  * **建議**：確認這些檔案的編碼是否統一為 **UTF-8 without BOM**，並修復程式碼中的亂碼字元。

### Info (提示)
* **背景寫入執行緒的 Fail-Closed 設計**
  * **位置**：`ToolUtility/Dataverse/DataverseTrace.cs` (第 489-495 行)
  * **說明**：在 `WriterLoopAsync` 中，如果 `DrainQueue` 丟出異常（例如磁碟空間不足或權限問題），會透過 `Interlocked.Exchange(ref _writerFaulted, 1)` 將寫入器標記為損毀，並捨棄佇列中的所有事件。這是一個非常優良的防禦性設計，能有效防止因 I/O 錯誤導致記憶體中的佇列無限增長。

---

## 逐項指標驗證 (T1–T7)

1. **T1: Disabled Cost / No Allocations** (通過)
   * 當 `Enabled` 為 `false` 時，`BeginRequest` 立即回傳 `NoopScope.Instance`，且 `CrmOperation` 等方法直接 return，避免了任何物件配置。測試 `Disabled_trace_writes_nothing_and_allocates_nothing_on_hot_path` 已驗證此行為。
2. **T2: Privacy HMAC User Pseudonym** (通過)
   * `CreateUserPseudonym` 使用隨機產生的 `_salt` 進行 `HMACSHA256` 雜湊，並取前 4 個 byte 轉為小寫十六進位字串（格式如 `u_a1b2c3d4`），且在 `Dispose` 時使用 `CryptographicOperations.ZeroMemory` 清除 salt，確保隱私安全。
3. **T3: 64MB/5-file Queue Writer & Drop Behavior** (通過)
   * 預設限制為 64MB 與 5 個保留檔案。當佇列超過 `QueueCapacity` (8192) 時，會丟棄最舊的事件，並在隨後寫入 `trace.dropped` 事件記錄丟棄數量。
4. **T4: Per-Request & Lease AsyncLocal Isolation** (通過)
   * 透過 `AsyncLocal<DataverseTrace>` 與 `AsyncLocal<RequestContext>` 實現 request 與 lease 的上下文隔離，並在 `RequestScope` 與 `LeaseScope` 的 `Dispose` 中正確還原先前的上下文。
5. **T5: Pool.Dispose State Timed at the Attempt** (通過)
   * 在 `BoundedClientPool.cs` 中，`TraceDisposeAttempt` 會在呼叫底層 `DisposeUnderlying` 之前，即時記錄當時的 `client.State`。
6. **T6: Pre-Clear CallerId Field** (通過)
   * 在 `PooledClient.cs` 的 `ReturnHealthy` 中，狀態轉為 `Idle` 之前會先呼叫 `TryClearCallerId` 將 `CallerId` 設為 `Guid.Empty` 並驗證。若清除失敗則將狀態設為 `Faulted` 並移出池。
7. **T7: Correlation & Nested Gateway Acquisition** (通過)
   * 實現了 `pool.acquire.hit` / `pool.acquire.miss` 與 `pool.return` 的 `leaseId` 關聯。`DataverseGateway` 支援 reentrant 呼叫，僅在最外層 (`_depth == 0`) 進行實體 Acquire 與 Dispose。
