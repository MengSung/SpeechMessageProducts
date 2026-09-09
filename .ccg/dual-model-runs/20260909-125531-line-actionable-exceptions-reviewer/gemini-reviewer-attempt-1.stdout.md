# 程式碼審查報告：LINE 可行動例外日誌與通知機制 (line-actionable-exceptions)

## 總體評估 (Summary)

本變更針對 `SpeechMessageProducts.ChurchReport` 與 `ToolUtility` 導入了強固且統一的例外診斷與 LINE 管理員通知機制 (`ExceptionDiagnostics`, `ExceptionReporting`, `LineExceptionSender`, `UnhandledExceptionLineNotificationMiddleware`, `ExceptionLoggerProvider`)。

**核心優勢**：
1. **嚴格遵循寫入順序契約**：`ExceptionDiagnostics.Report` 確保日誌必須先寫入 `Logs/Exception.log` 並執行 `stream.Flush(flushToDisk: true)`，確認成功後才推入 LINE 發送佇列。寫入失敗時不發送 LINE。
2. **完全去敏感化與無 PII 外洩**：`Symbol()` 與 `StackSymbols()` 僅記錄類別名稱、方法名稱、HResult 與堆疊檔號，省略 `Exception.Message` 與 `Exception.Data`，確保不保留 Access Token、Session、Cookie 或使用者個資。
3. **無效應佇列與平滑退避**：LINE 發送服務採用獨立 Task 與 Channel 佇列，且 `SendAsync` 設有 5 秒逾時與取消控制，不阻塞 HTTP 請求線程。
4. **Debug 與 Release 一致運作**：`Logs/Exception.log` 寫入在 Debug 與 Release 均生效，確保生產環境發生關鍵例外時可完整稽核。

---

## 驗證報告與評分 (Validation Report)

```
VALIDATION REPORT
=================
User Experience: 18/20 - 發生未處理例外時能平滑處理並發送管理員 LINE 通知，前端錯誤呈現清晰。
Visual Consistency: 19/20 - 日誌格式為標準 JSONL，輸出規範統一。
Accessibility: 20/20 - 本次變更為後端例外處理機制，不影響前端 a11y。
Performance: 17/20 - 使用 Channel 佇列與獨立 Task 進行非同步 LINE 發送，但 File.Move 輪轉邏輯在檔案鎖定時有存取風險。
Browser Compatibility: 20/20 - 不影響前端瀏覽器相容性。

TOTAL SCORE: 94/100

ISSUES FOUND:
- Critical: ExceptionDiagnostics 檔案輪轉 (Log Rotation) 遇讀取鎖定時拋出 Exception，導致後續所有 Log 寫入與 LINE 通知永久失效。
- Warning: Mutex 命名未指定 Global\ 字頭，跨 Windows Session / IIS 處理程序可能無法完全跨程序互斥。
- Warning: LineExceptionSender 之 Access Token 於建構時一次性載入，缺乏動態刷新機制。

RECOMMENDATION: NEEDS_IMPROVEMENT (修復 Log Rotation 鎖定防護後即可 PASS)
```

---

## 關鍵發現與改進建議 (Actionable Findings)

### 🔴 Critical (嚴重)

#### 1. 檔案輪轉 (Log Rotation) 遇讀取鎖定拋出例外，將導致 Log 與 LINE 通知永久癱瘓
- **位置**：`ToolUtility/Diagnostics/ExceptionDiagnostics.cs` (第 146-157 行)
- **原因分析**：
  ```csharp
  if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > _maximumFileBytes)
  {
      for (var i = 5; i >= 1; i--)
      {
          var target = Path.Combine(_directory, $"Exception.{i}.log");
          var source = i == 1 ? path : Path.Combine(_directory, $"Exception.{i - 1}.log");
          if (File.Exists(source)) File.Move(source, target, true);
      }
  }
  ```
  當 `Exception.log` 達到 `_maximumFileBytes` (預設 5MB) 時，會觸發 `File.Move` 進行輪轉。若此時有外部程式 (例如 PowerShell 稽核腳本 `Analyze-ChurchReportTraces.ps1`、記錄收集器、文字編輯器或另一個處理程序) 正開著 `Exception.log` 或 `Exception.1.log` (具 `FileShare.Read`)：
  1. `File.Move` 在 Windows 上會拋出 `IOException` ("The process cannot access the file because it is being used by another process.")。
  2. `Write` 的外層 `catch` 會捕獲該例外並回傳 `false`。
  3. 由於 `Exception.log` 搬移失敗且未被截斷，檔案大小持續維持在 `> _maximumFileBytes`。
  4. 此後**每一次**呼叫 `Report(...)` 都會重新進入 `if` 條件、重新執行 `File.Move` 再次拋出例外並回傳 `false`。
- **影響**：所有後續例外寫入與 LINE 通知被**永久阻斷**，直到重新啟動應用程式或手動刪除檔案。
- **修復建議**：
  1. `Write` 內開檔改用 `FileShare.ReadWrite` 減少共享鎖定衝突。
  2. 將輪轉 `for` 迴圈包覆於獨立的 `try-catch` 中。若輪轉失敗，退回至清空 `path` 或繼續 append 並紀錄輪轉警告，切勿讓輪轉 failure 導致整套 `Write` 回傳 `false`：
  ```csharp
  try
  {
      for (var i = 5; i >= 1; i--) { ... File.Move(source, target, true); }
  }
  catch (Exception ex)
  {
      Emergency("ExceptionLogRotationFailed: " + ex.Message);
      // 輪轉失敗時之退避防護：避免長久卡死在 > _maximumFileBytes
  }
  ```

---

### 🟡 Warning (警告)

#### 1. Mutex 命名未指定 `Global\` 前綴 (跨 Session / IIS 隔離問題)
- **位置**：`ToolUtility/Diagnostics/ExceptionDiagnostics.cs` (第 39-40 行)
- **原因分析**：
  ```csharp
  var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_directory.ToUpperInvariant())));
  _fileMutex = new Mutex(false, "ExceptionLog-" + identity);
  ```
  Windows 系統中，未加 `Global\` 前綴的具名 Mutex 預設建立於 Session Local 命名空間 (`Local\`)。若 ChurchReport 在 IIS (`Session 0`, 如 `w3wp.exe`) 運行，而開發人員在互動式 Session (`Session 1`) 執行命令列或工具，兩者會建立各自獨立的 Mutex。
- **影響**：無法在跨 Session / 跨 IIS 工作進程間發揮互斥寫入防護作用。
- **修復建議**：若預期支援跨 Session 同步，應使用 `"Global\\ExceptionLog-" + identity` 並設置合適的 `MutexSecurity` ACL；若僅防護單一 Session 內的進程，應於 XML 文件中明確標註。

#### 2. `LineExceptionSender` 之 Channel Access Token 為單次載入
- **位置**：`SpeechMessageProducts.ChurchReport/Services/LineExceptionSender.cs` (第 30-33 行, 第 63 行)
- **原因分析**：`LineExceptionSender` 於建構時從 `IConfiguration` 讀取 Token 並儲存於 `_token`。當 `Dispose()` 被呼叫時 `_token` 會被清空。然而若組態檔動態更新 (ReloadOnChange)，`LineExceptionSender` 無法自動更新 Token。
- **修復建議**：可改為注入 `IOptionsMonitor<LineMessagingOptions>` 或於每次發送時動態解析 Token，確保憑證輪轉時無需重啟 Service。

---

### 🔵 Info (資訊與良善實作)

#### 1. `ExceptionLoggerProvider` 過濾器設定正確
- **位置**：`SpeechMessageProducts.ChurchReport/Program.cs` (第 208-209 行)
- **優點**：`builder.Logging.AddFilter<ExceptionLoggerProvider>(null, LogLevel.Error);` 確保僅有 `LogLevel.Error` 與 `LogLevel.Critical` 會進入 LINE 通知與 Exception.log，防止過多的 Debug/Info 訊息引發通知風暴。

#### 2. 順序與生命週期管理嚴謹
- **位置**：`SpeechMessageProducts.ChurchReport/Program.cs` (第 76-81 行)
- **優點**：`Program.Main` 的 `finally` 區塊中，依序執行 `registration.Dispose()` -> `diagnostics.DisposeAsync()` -> `sender.Dispose()`。`diagnostics` 在關閉時會先耗盡 Channel 佇列中剩餘的訊息並等待 `sender` 完成發送，最後才釋放 `sender` 的 `HttpClient`，生命週期無 Memory / Resource Leak 隱憂。

---

## Catch 稽核分析 (`catch-audit.json`)

對 `.ccg/tasks/line-actionable-exceptions/catch-audit.json` 的終端 catch 區塊稽核結論：

1. **應上報之 Actionable Exceptions (需紀錄 Log 並通知 LINE)**：
   - HTTP 管道未處理例外：由 `UnhandledExceptionLineNotificationMiddleware` 截獲並呼叫 `Report(exception, "Http.UnhandledException")`（**已涵蓋**）。
   - Controller 內部 capture 後 fail-closed 之例外：`BaseChurchController.HandleError` 呼叫 `ExceptionReporting.Report(exception, methodName)`（**已涵蓋**）。
   - 背景工作與 Task unobserved 例外：`TaskScheduler.UnobservedTaskException` 與 `AppDomain.UnhandledException` 由 `ExceptionReporting.Attach` 綁定（**已涵蓋**）。

2. **允許排除/不發送 LINE 之 Recovered / Expected 區塊**：
   - **`OperationCanceledException`**：當 Client 中斷連線或 CancellationToken 觸發取消時，屬正常 HTTP 流量終止，`ExceptionDiagnostics.Report` (第 66-71 行) 已正確將其標記並忽略，**不發送 LINE**。
   - **清理/Dispose 內部之 catch 區塊**：如 `Program.cs` 關閉 Trace Listener 時的 `try { candidate.Dispose(); } catch { }`，屬安全釋放資源，**不應發送 LINE**。
   - **Trace 初始化的降級備援**：`Program.cs` 第 101-107 行對 Trace 選項解析失敗時，會降級至 `CreateDisabled`，此為啟動時的 Fail-Closed 設計，Console 已有輸出，**無須發送 LINE**。

---

## 結論與建議處置 (Recommendation)

1. **立即修正 `ExceptionDiagnostics.cs` 中的檔案輪轉防護**（將 `File.Move` 加上專屬 try-catch 降級處理），防止因檔案讀取鎖定造成 Log/LINE 功能永久失效。
2. 調整完成後再次執行測試，確認所有單元測試通過。
