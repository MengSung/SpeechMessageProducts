# ChurchReport Trace 效能、Session 隔離與資源生命週期審查報告

本報告針對 ChurchReport 系統中的 Dataverse 連線池（`BoundedClientPool`）、閘道器（`DataverseGateway`）、追蹤機制（`DataverseTrace`）及效能分析器（`RequestProfiler`）進行唯讀架構與程式碼審查。分析重點在於評估系統在高並行環境下的**效能瓶頸**、**Session 隔離安全性**、**資源洩漏風險**及**觀測數據一致性**。

---

## 1. UX Analysis (使用者影響評估)

- **效能與延遲影響**：Dataverse 連線池的獲取（Acquire）與健康檢查機制直接影響前端請求的響應時間。若連線池在高負載下發生鎖競爭（Lock Contention）或健康檢查（WhoAmI 查詢）超時，將導致使用者請求排隊，進而引發前端 UI 卡頓或 HTTP 504 閘道器超時。
- **Session 隔離與隱私安全**：系統採用 HMAC 雜湊技術將使用者識別碼（Username/Session ID）去識別化為虛擬識別碼（Pseudonym，如 `u_xxxx`），這能有效保護使用者隱私，避免敏感個資寫入日誌。然而，若連線歸還時未能徹底清除模擬身分（Impersonation State），將導致 A 使用者的請求執行在 B 使用者的權限上下文中，造成嚴重的跨使用者數據外洩，直接破壞使用者信任。
- **非阻塞寫入設計**：`DataverseTrace` 採用背景執行緒與 `ConcurrentQueue` 進行非同步日誌寫入，並在佇列溢出時採取丟棄策略（`trace.dropped`），這確保了即使日誌 I/O 發生瓶頸，也不會阻塞使用者的正常操作，維持了良好的使用者體驗。

---

## 2. Design Evaluation (設計系統與模式評估)

- **日誌格式一致性**：`DataverseTrace` 輸出標準的 JSONL 格式，便於與現代日誌分析工具（如 ELK、Fluentd）整合。然而，現有的 `RequestProfiler` 輸出格式為自訂的文字行（如 `[Perf]`、`[Perf-Phase]`），兩者在格式與語意上存在不一致，增加了自動化解析與關聯分析的複雜度。
- **生命週期管理**：連線池與 Trace 服務皆實作了 `IDisposable`。連線池在處置時會釋放所有閒置連線，並將租借中的連線標記為待處置；Trace 服務在處置時會嘗試將佇列中的剩餘事件寫入磁碟，並使用 `ZeroMemory` 清除記憶體中的 HMAC Salt，符合安全設計規範。

---

## 3. Technical Considerations (技術架構考量)

- **條件編譯的副作用**：`RequestProfiler` 依賴 `#if DEBUG` 條件編譯。在 Release（生產環境）建置中，該剖析器完全不參與編譯，導致生產環境的 `Trace.log` 缺乏效能數據，無法與 `dataverse-trace.jsonl` 進行交叉比對。
- **I/O 異常與執行緒安全**：背景日誌寫入執行緒在進行檔案輪轉與舊檔清理時，若遭遇檔案鎖定或權限問題，異常若未被妥善捕獲，將導致背景寫入執行緒永久終止，使系統失去觀測能力。
- **記憶體與 CPU 開銷**：在日誌寫入的 Hot Path 上，將已序列化的位元組陣列重新轉換為字串再寫入 `StreamWriter`，會產生不必要的記憶體分配與 GC 壓力。

---

## 4. Options (替代方案評估)

- **方案 A：維持現狀，僅修補異常安全性與效能瓶頸**
  - *優點*：改動最小，風險低。
  - *缺點*：生產環境依然缺乏 `RequestProfiler` 的細粒度效能數據，且無法解決跨重啟的使用者識別碼關聯問題。
- **方案 B：將效能剖析指標整合至 `DataverseTrace`**
  - *優點*：消除 `#if DEBUG` 的限制，使生產環境也能透過 JSONL 取得請求耗時、CRM 呼叫次數及 Gap 耗時等量化指標，達成觀測數據的一致性。
  - *缺點*：需要微調 `DataverseTrace` 的事件 Schema，並在 `RequestScope` 結束時收集這些指標。

---

## 5. Recommendation (建議做法)

**採用方案 B**。建議將 `RequestProfiler` 的核心量化指標（如 Action 耗時、CRM 呼叫次數與總耗時）整合至 `DataverseTrace` 的 `request.end` 事件中，統一輸出至 JSONL。這樣既能避免在生產環境中引入繁重的文字日誌，又能確保觀測數據的一致性與可信度。同時，應針對下述的 Critical 與 Warning 項目進行架構補強。

---

## 6. Detailed Findings (詳細審查結果)

### Critical (嚴重缺陷)

#### 1. `RequestProfiler` 條件編譯導致生產環境觀測數據缺失與不一致
- **觀察證據**：`RequestProfiler.cs` 整個類別被包裹在 `#if DEBUG` 條件編譯中。
- **根因/證據缺口**：在 Release 建置中，`RequestProfiler` 相關程式碼不會被編譯，因此 `Trace.log` 中不會產生任何 `[Perf]` 相關的效能日誌。
- **風險**：生產環境無法輸出任何效能剖析日誌，導致 `Analyze-ChurchReportTraces.ps1` 在生產環境執行時，會因為缺乏 `Trace.log` 中的效能數據而產生數據不一致或分析盲點，無法評估真實環境下的 N+1 查詢或慢速呼叫。
- **建議修正方向**：將核心的效能指標（如 Action 耗時、CRM 呼叫次數與總耗時）整合至 `DataverseTrace` 的 `request.end` 事件中，統一輸出至 JSONL，避免依賴條件編譯的 `Trace.log`。

#### 2. `TryClearCallerId` 僅支援 `OnPremiseClient` 導致未來升級時的 Session Leakage 隱患
- **觀察證據**：`PooledClient.cs` 第 184-200 行的 `TryClearCallerId` 方法中，僅對 `Service is OnPremiseClient` 進行 `CallerId` 清理，其餘類型直接回傳 `true`。
- **根因/證據缺口**：缺乏對其他 `IOrganizationService` 實作（如 `ServiceClient`）的 CallerId 清理邏輯。
- **風險**：若未來將 Dataverse SDK 升級至 `Microsoft.PowerPlatform.Dataverse.Client` 並使用 `ServiceClient`，該客戶端同樣支援 Impersonation（透過 `CallerId` 或 `CallerObjectId`），但由於 `TryClearCallerId` 會直接回傳 `true` 而不作任何清理，將導致嚴重的跨使用者 Session 狀態外洩（Session Leakage）。
- **建議修正方向**：在 `TryClearCallerId` 中加入對 `ServiceClient` 的型別檢查與清理邏輯，或透過反射動態尋找並清除 `CallerId` / `CallerObjectId` 屬性，確保 fail-closed 機制對所有潛在的 client 實作皆有效。

#### 3. `DataverseTrace.Dispose` 中同步等待背景工作可能導致應用程式關閉時死鎖或崩潰
- **觀察證據**：`DataverseTrace.cs` 第 456 行：`_writerTask.GetAwaiter().GetResult();`。
- **根因/證據缺口**：在 `Dispose` 同步方法中，使用 `GetResult()` 強制同步等待非同步的背景寫入任務。
- **風險**：若背景任務 `WriterLoopAsync` 在寫入過程中拋出異常（例如磁碟空間不足、檔案鎖定），`GetResult()` 會將該異常重新拋出，導致 DI 容器釋放或 ASP.NET Core 主機關閉時發生未預期的崩潰。此外，在特定同步上下文中，這可能引發死鎖。
- **建議修正方向**：在 `Dispose` 中使用 `try-catch` 包裹 `GetResult()`，或改用非同步的處置模式（如實作 `IAsyncDisposable`），安全地記錄異常而不中斷關閉流程。

---

### Warning (警告項目)

#### 1. `PruneOldFiles` 刪除檔案未處理異常導致 Trace 寫入執行緒 Silent Failure
- **觀察證據**：`DataverseTrace.cs` 第 638 行：`File.Delete(files[0]);`，該方法未被 `try-catch` 保護。
- **根因/證據缺口**：`PruneOldFiles` 在執行舊日誌檔案刪除時，若檔案被其他處理程序（如日誌收集器、防毒軟體）鎖定，會拋出 `IOException`。
- **風險**：異常會向上傳播至 `RotateWriter`，進而導致 `WriterLoopAsync` 異常終止。此時 `_writerFaulted` 被設為 1，後續所有的 Trace 事件都會被靜默丟棄（Silent Failure），系統失去觀測能力。
- **建議修正方向**：在 `PruneOldFiles` 的 `File.Delete` 呼叫加上 `try-catch` 保護，若刪除失敗應記錄警告（或忽略）並繼續執行，不應讓清理失敗影響主寫入流程。

#### 2. `WriteEntry` 中不必要的字串轉換造成 Hot Path 效能損耗
- **觀察證據**：`DataverseTrace.cs` 第 592 行：`_writer.Write(Encoding.UTF8.GetString(buffer.WrittenSpan));`。
- **根因/證據缺口**：`ArrayBufferWriter<byte>` 已經將 JSON 序列化為 UTF-8 位元組，但程式碼卻將其轉回字串，再經由 `StreamWriter` 寫入。
- **風險**：在每秒數千次請求的高並行環境下，頻繁的 `GetString` 轉換會產生大量暫時性字串，增加 GC 壓力與 CPU 耗損，降低整體吞吐量。
- **建議修正方向**：直接將 `buffer.WrittenSpan` 寫入底層的 `FileStream`，避免中間的字串解碼與二次編碼過程。

---

### Info (提示項目)

#### 1. 隨機 HMAC Salt 導致跨重啟邊界的使用者識別碼無法關聯
- **觀察證據**：`DataverseTrace.cs` 第 230 行：`_salt = RandomNumberGenerator.GetBytes(32);`。
- **根因/證據缺口**：Salt 是在每次 `DataverseTrace` 實例化時隨機產生的，並未持久化。
- **風險**：當應用程式重啟（如部署、回收）後，同一個使用者產生的 `user` 虛擬識別碼（Pseudonym）會完全改變，導致無法跨重啟邊界進行長期的使用者行為或效能關聯分析。
- **建議修正方向**：若有跨重啟關聯的需求，可考慮將 Salt 儲存於安全的配置中（如 Azure Key Vault 或加密的設定檔），或使用基於機器/部署週期的固定 Salt，同時維持足夠的雜湊強度。

#### 2. 非 HTTP Request 上下文下的 Trace 盲點
- **觀察證據**：`DataverseTrace.cs` 中多處呼叫 `TryGetRequest`，若無當前 Request 則不記錄或記錄為空。
- **根因/證據缺口**：背景工作（如排程任務、Queue 處理器）執行 Dataverse 操作時，由於沒有 HTTP 上下文，`traceId` 與 `user` 會缺失。
- **風險**：無法有效追蹤背景工作所引發的 Dataverse 連線池狀態與效能瓶頸。
- **建議修正方向**：為背景工作建立專屬的 `RequestScope`（例如在背景任務啟動時手動呼叫 `BeginRequest` 並給予特定的 `traceId` 如 `bg-task-xxx`），以確保觀測完整性。
