### 審查總結 (Executive Summary)

針對 `line-actionable-exceptions` 任務的變更進行 UI/系統介面與異常診斷架構審查。本次變更實作了全系統的例外狀況攔截機制，將所有 Actionable Exceptions 寫入並 Flush 至 `Logs/Exception.log`（包含 Debug 與 Release 模式），並在寫入磁碟後透過 Channel 異步傳送 LINE 異常通知。

程式碼架構良好，具備完整的敏感資料脫敏（Sanitization）、Task/AppDomain 未捕捉例外攔截、以及 Channel 防佇列爆滿（Bounded Channel, `DropOldest`）設計。然而，在**日誌滾動（Log Rotation）磁碟作業**與**跨 Session Mutex 鎖定**等極端邊界條件下存在死鎖或永久失效隱患，需進行修正。

---

### 評分報告 (Validation Report)

```
VALIDATION REPORT
=================
User Experience & Reliability: 17/20 - 異常通知格式簡明且無敏感資料洩漏，但在檔案被鎖定時可能中斷通知。
Visual & Log Consistency: 19/20 - JSONL 結構化日誌格式一致，脫敏邏輯符合作業規範。
Accessibility & Error Resilience: 16/20 - 捕捉全域與 Task 例外，但 Log Rotation 失敗會導致後續所有紀錄永久失靈。
Performance & Multi-threading: 18/20 - 採用 Channel 無鎖/低鎖異步佇列，避免阻塞 HTTP 請求通道。
Browser & System Compatibility: 18/20 - 相容 Windows 多行程架構，唯 Session 0 / Session 1 邊界 Mutex 需留意。

TOTAL SCORE: 88/100

ISSUES FOUND:
- [Critical] Log Rotation 失敗時未捕捉例外，導致後續 ExceptionLog 永久無法寫入與傳送 LINE。
- [Warning] Windows 跨 Session (Service vs Desktop) 的 Named Mutex 命名空間未考慮 `Global\` 前綴。
- [Warning] `LineExceptionSender` 生命週期處置（Dispose）與動態 Token 更新邏輯需改善。

RECOMMENDATION: NEEDS_IMPROVEMENT
```

---

### 關鍵檢查點驗證 (Checklist Verification)

1. **"LOG MUST BE WRITTEN AND FLUSHED BEFORE LINE IS QUEUED/SENT" 順序保證**:
   - **驗證通過**：在 `ExceptionDiagnostics.Report` (ToolUtility/Diagnostics/ExceptionDiagnostics.cs) 中：
     1. 先執行 `Write(record)`。在 `Write` 方法內以 `stream.Flush(flushToDisk: true)` 實體刷入磁碟。
     2. 僅在 `Write(record)` 成功返回 `true` 後，才呼叫 `_notifications.Writer.TryWrite(record)` 推入 LINE 通知佇列。
     3. 順序嚴格符合要求。

2. **敏感資訊脫敏 (Sensitive Data Redaction)**:
   - **驗證通過**：`Symbol` 與 `StackSymbols` 僅輸出類別名稱、方法名稱、檔案名稱與行號，完全未包含 `Exception.Message` 或 `Exception.Data`，避免密碼、Token 或 PII 洩漏至日誌或 LINE 訊息。

---

### 程式碼審查發現 (Actionable Findings)

#### 🔴 Critical (嚴重問題)

##### 1. Log Rotation 檔案鎖定失敗導致 Exception Log 永久失效
- **位置**: `ToolUtility/Diagnostics/ExceptionDiagnostics.cs` (第 146-157 行)
- **問題描述**:
  當 `Exception.log` 大小超過 `_maximumFileBytes` (5MB) 時，`Write` 方法會執行日誌滾動 (`File.Move`)。若 `Exception.log` 或 `Exception.1.log` 正被外部程序（如 Log Viewer、備份軟體、PowerShell 等）以讀取鎖定開啟，`File.Move` 將拋出 `IOException`。
  此例外會被 `Write` 外層的 `catch` 捕捉並返回 `false`。**關鍵在於：`Exception.log` 檔案並未被清空或搬移，其檔案大小仍超過上限**。
  導致隨後所有的 `ExceptionDiagnostics.Report(...)` 呼叫都會進入滾動邏輯、觸發 `File.Move` 失敗並返回 `false`。**全系統的例外記錄與 LINE 通知將永久停止運作**，直到重新啟動應用程式或手動刪除檔案。
- **重現程式碼區段**:
  ```csharp
  if (info.Exists && info.Length + bytes.Length > _maximumFileBytes)
  {
      RotateLogsLocked(path); // 若此處 File.Move 失敗拋出 IOException
  }
  ```
- **建議修復方式**:
  將 `RotateLogsLocked` 包裹在獨立的 `try-catch` 區塊中。若滾動失敗（如檔案被鎖定），應印出警示並退回到 **Truncate（截斷舊日誌）** 或 **強制 Append 並暫緩滾動** 的安全備援模式，切勿直接 abort 寫入作業。

---

#### 🟡 Warning (警告與潛在風險)

##### 1. 跨 Windows Session (服務 vs 互動桌面) Named Mutex 隔離問題
- **位置**: `ToolUtility/Diagnostics/ExceptionDiagnostics.cs` (第 39-40 行)
- **問題描述**:
  `_fileMutex = new Mutex(false, "ExceptionLog-" + identity);` 使用了預設的 Session 區域名稱空間 (`Local\`)。
  若 `ChurchReport` 執行於 IIS 或 Windows Service (Session 0)，而背景 CLI 工具或排程執行於使用者登入 Session (Session 1)，兩者建立的 Mutex 將無法跨 Session 互斥。
- **建議修復方式**:
  若需要在不同 Session 的程序間共用 Mutex，請加上 `Global\` 前綴（如 `"Global\\ExceptionLog-" + identity`），並配置適當的 `MutexSecurity` 存取權限。

##### 2. `LineExceptionSender` 的 Token 生命週期與重新初始化限制
- **位置**: `SpeechMessageProducts.ChurchReport/Services/LineExceptionSender.cs` (第 18-33 行, 第 63 行)
- **問題描述**:
  `LineExceptionSender` 在建構子讀取 `LINE_CHANNEL_ACCESS_TOKEN`，並在 `Dispose()` 時將 `_token` 設定為 `null`。若 Dependency Injection 容器重新初始化或變更設定，單例物件無法動態更新 Token。
- **建議修復方式**:
  建議搭配 `IOptionsMonitor<LineOptions>` 或在傳送時動態取得設定，提升動態變更設定檔時的強健性。

---

#### 🔵 Info (資訊與良善實作)

##### 1. `ExceptionLoggerProvider` 篩選層級正確
- **位置**: `SpeechMessageProducts.ChurchReport/Program.cs` (第 208-209 行)
- **說明**: 透過 `AddFilter<ExceptionLoggerProvider>(null, LogLevel.Error);` 正確過濾，確保僅有 `LogLevel.Error` 與 `LogLevel.Critical` 會發送 LINE 通知，避免一般 `Information` 或 `Warning` 造成 Notification Fatigue。

##### 2. 異步 Background Channel 設計優良
- **位置**: `ToolUtility/Diagnostics/ExceptionDiagnostics.cs` (第 41-47 行)
- **說明**: `Channel.CreateBounded<string>` 設定 `Capacity = 200` 並搭配 `BoundedChannelFullMode.DropOldest`，有效防止在 LINE API 網路延遲或斷線時引發 Memory Leak 或 HTTP 執行緒阻塞。

---

### Catch Audit 審查建議 (`catch-audit.json` 分析)

針對 `.ccg/tasks/line-actionable-exceptions/catch-audit.json` 所列出的 Terminal Catch 區塊分析：

1. **必須回報至 Exception.log / LINE 的 Actionable Exceptions (Actionable Failures)**:
   - **ASP.NET Core 全域未捕捉 HTTP 例外**: 由 `UnhandledExceptionLineNotificationMiddleware` 正確攔截並回報。
   - **BaseChurchController 靜態與 Controller 錯誤處置**: `BaseChurchController.HandleError` 中的例外捕捉，若導致 API 回應 500 或業務流程中斷者，均已寫入 Diagnostics。
   - **TaskScheduler.UnobservedTaskException 與 AppDomain.UnhandledException**: 系統全域背景 Task 崩潰，已正確綁定回報。

2. **允許排除的復原/預期例外 (Recovered / Expected Exclusions)**:
   - **`OperationCanceledException` / `TaskCanceledException`**: 使用者中途取消 HTTP 請求或 Client 斷線，`ExceptionDiagnostics.Report` 中已建立 `IsCancellation` 過濾，**不應發送 LINE 通知**（符合預期）。
   - **`Program.cs` 啟動階段 Trace Listener 備援捕捉**: 啟動時設定日誌輸出的嘗試性 try-catch，屬於 Graceful Fallback，**不需要回報**。
   - **靜態資源或快取釋放 (Dispose / Cleanup)**: 解構子或 `Dispose` 中的靜態資源釋放 `catch {}`，無實質業務影響，**不需要回報**。

---

### 結論與後續建議

1. **修正 Critical 項目**: 優先修復 `ExceptionDiagnostics.cs` 中 Log Rotation 失敗時的 Fallback 處理，確保在檔案遭獨占鎖定時仍可寫入 Exception 並發送 LINE。
2. **單元測試補充**: 建議針對 `ExceptionDiagnostics` 新增「模擬 Log 檔案遭 Lock 時寫入」的單元測試，驗證系統在極端檔案鎖定下的復原能力。
