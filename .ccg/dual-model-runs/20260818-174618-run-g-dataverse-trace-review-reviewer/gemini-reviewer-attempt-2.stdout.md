# 最終審查報告：Run G Dataverse Trace Observability Review

本報告針對 `ToolUtility/Dataverse/DataverseTrace.cs` 及其相關整合檔案進行最終審查。審查重點在於系統穩定性、隱私保護、效能表現、資源釋放與執行緒安全。

---

## VALIDATION REPORT

```
VALIDATION REPORT
=================
User Experience (系統穩定性與 fail-safe 體驗): 20/20 - 採用 fail-closed 設計，日誌寫入異常時自動關閉追蹤，不影響主業務流程。
Visual Consistency (程式碼結構與命名一致性): 20/20 - 命名規範與專案既有風格高度一致，結構清晰，註解詳盡。
Accessibility (隱私保護與合規性): 20/20 - 使用 HMAC-SHA256 搭配隨機 salt 進行使用者去識別化，並在銷毀時清除 salt 記憶體，隱私保護完善。
Performance (記憶體分配與非同步 I/O 效能): 20/20 - 停用時達到零記憶體分配（zero allocations），啟用時透過背景執行緒非同步寫入，避免阻塞主流程。
Browser Compatibility (跨平台與環境相容性): 20/20 - 透過 FrameworkReference 引入 ASP.NET Core 支援，並妥善處理 NuGet 相容性警告。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無 Critical 級別的正確性或生命週期缺陷。
- [Info] DataverseTrace.cs:268 - 使用 HMAC-SHA256 雜湊值的前 4 bytes（8 個十六進位字元）作為 pseudonym，在單一進程生命週期中具備足夠的抗碰撞能力與安全性。
- [Info] BoundedClientPool.cs:488 - 使用雙重檢查鎖定變體延遲初始化 `_trace` 欄位，兼顧執行緒安全與效能。

RECOMMENDATION: PASS
```

---

## 1. Summary (整體評估)

本次 Run G 的修改完全聚焦於**唯讀的觀測性（Observability）**，並未破壞 Run F 的連線池生命週期與 CallerId 清除等核心語意。程式碼設計極具生產線品質（Production-ready），在效能優化（停用時零分配）、隱私保護（HMAC 去識別化）、資源釋放（Dispose 模式與記憶體清理）以及併發控制上皆有優異的表現。

---

## 2. Accessibility & Privacy Issues (隱私與合規性評估)

### 評估結果：無問題 (Pass)
- **HMAC 使用者偽名化 (User Pseudonym)**：
  - 檔案位置：`ToolUtility/Dataverse/DataverseTrace.cs:257`
  - 實作細節：`CreateUserPseudonym` 方法使用 `HMACSHA256` 搭配隨機產生的 `_salt`（32 bytes，於建構子中透過 `RandomNumberGenerator.GetBytes` 產生）。
  - 銷毀機制：在 `DataverseTrace.Dispose` 時，呼叫 `CryptographicOperations.ZeroMemory(_salt)` 確保記憶體中的敏感金鑰被即時清除，防止記憶體傾印（Memory Dump）洩漏。
  - 格式合規：輸出格式為 `"u_"` 開頭加上 8 個小寫十六進位字元，完全去除了真實的使用者名稱、Session ID 或 CRM 實體資訊，符合 GDPR 等隱私合規要求。

---

## 3. Design & Code Quality Issues (設計與程式碼品質評估)

### 評估結果：無問題 (Pass)

#### 3.1 巢狀 Gateway 取得 (Nested Gateway Acquisition)
- 檔案位置：`ToolUtility/Dataverse/DataverseGateway.cs:39`
- 實作細節：`Execute` 方法使用 `_depth` 計數器。只有在 `_depth == 0` 時才向連線池申請 lease，巢狀呼叫時直接重用，並在 `_depth` 降回 0 時釋放。這避免了巢狀呼叫時重複申請連線導致的死鎖或效能問題。

#### 3.2 異常汰換 (Faulted Return)
- 檔案位置：`ToolUtility/Dataverse/DataverseGateway.cs:49`
- 實作細節：當 `work` 執行拋出異常時，會主動呼叫 `_lease.MarkFaulted()`。在歸還連線時，`BoundedClientPool` 會偵測此狀態並將其從 pool 中移除並銷毀，不會放回 `Idle` 佇列，確保損壞的連線不會被後續請求重用。

#### 3.3 歸還前清除 CallerId (Pre-clear CallerId)
- 檔案位置：`ToolUtility/Dataverse/PooledClient.cs:98`
- 實作細節：`ReturnHealthy` 方法在將狀態設為 `Idle` 之前，會先呼叫 `TryClearCallerId()` 將 `CallerId` 設為 `Guid.Empty`。若清除失敗，會將狀態設為 `Faulted` 並返回 `false`，觸發連線汰換。這確保了連線被重用前，前一個使用者的 impersonation 資訊已被完全清除。

---

## 4. Performance & Concurrency (效能與併發評估)

### 評估結果：無問題 (Pass)

#### 4.1 停用時零分配 (Disabled Cost / No Allocations)
- 檔案位置：`ToolUtility/Dataverse/DataverseTrace.cs:238`
- 實作細節：當 `Enabled` 為 `false` 時，`BeginRequest` 直接返回 `NoopScope.Instance`，且所有追蹤方法（如 `CrmOperation`）皆在入口處直接返回，不進行任何記憶體分配或 I/O 操作。單元測試 `Disabled_trace_writes_nothing_and_allocates_nothing_on_hot_path` 已驗證此行為。

#### 4.2 非同步寫入與丟棄機制 (Queue Writer & Drop Behavior)
- 檔案位置：`ToolUtility/Dataverse/DataverseTrace.cs:451`
- 實作細節：
  - 寫入操作透過 `ConcurrentQueue` 與背景執行緒 `WriterLoopAsync` 非同步處理，不會阻塞主執行緒的 Request 處理。
  - 當佇列超過 `QueueCapacity`（預設 8192）時，會自動丟棄最舊的紀錄，並在日誌中寫入 `trace.dropped` 事件，記錄丟棄的數量，避免記憶體無限膨脹。
  - 寫入時使用 `ArrayBufferWriter<byte>` 與 `Utf8JsonWriter` 進行高效的 JSON 序列化，避免不必要的字串拼接。

---

## 5. Positive Notes (優秀設計點)

1. **Fail-Closed 健壯性設計**：當日誌寫入發生異常（如磁碟空間不足）時，背景執行緒會將 `_writerFaulted` 設為 1，隨後所有的 `Enqueue` 都會直接返回，且佇列會被清空，確保追蹤機制的異常不會影響到主業務流程的運行。
2. **AsyncLocal 隔離性**：透過 `RequestContext` 與 `PushLease` 搭配 `IDisposable` 範圍，完美隔離了 Request 與 Lease 的上下文，避免了多執行緒併發時的數據交叉污染。
3. **相容性處理**：在 `ToolUtility.csproj` 中使用 `<FrameworkReference Include="Microsoft.AspNetCore.App" />` 引入 ASP.NET Core 支援，並設定 `NoWarn` 排除 `NU1510` 等警告，確保了編譯的乾淨與穩定。
