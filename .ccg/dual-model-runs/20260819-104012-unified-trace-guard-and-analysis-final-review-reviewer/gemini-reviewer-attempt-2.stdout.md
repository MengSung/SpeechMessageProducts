本 Advisory Review 報告針對 ChurchReport 系統的統一追蹤機制（Trace Guard）與 PowerShell 整合診斷分析器進行安全與架構審查。

---

### Critical (嚴重風險)

#### 1. PowerShell 7+ 相容性與 Big5 編碼解析失敗風險
* **檔案/符號**: `SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1`
* **說明**: 
  分析器腳本在讀取 legacy 追蹤檔案 `CHURCH_REPORT_TRACE.TXT` 時，直接呼叫了 `[System.Text.Encoding]::GetEncoding(950, ...)`。
  在基於 .NET Core 的 PowerShell 7+ 環境中，預設並不支援 Code Page 950 (Big5)，必須先註冊 `CodePagesEncodingProvider`。若未註冊，此呼叫將會拋出 `ArgumentException` 異常，導致分析器在 PowerShell 7+ 執行時崩潰，並回傳 Exit Code 1，無法順利產出診斷報告。
* **建議與修復**:
  在腳本初始化階段或呼叫 `GetEncoding` 之前，加入針對 PowerShell Core (6+) 的編碼提供者註冊邏輯：
  ```powershell
  if ($PSVersionTable.PSVersion.Major -ge 6) {
      [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
  }
  ```

---

### Warning (警告)

#### 1. `DataverseTrace` 釋放時的 `CancellationTokenSource` 處置順序與潛在 Race Condition
* **檔案/符號**: `ToolUtility/Dataverse/DataverseTrace.cs` -> `Dispose()`
* **說明**:
  在 `DataverseTrace.Dispose()` 中，呼叫 `_writerWakeup.Cancel()` 後立即透過 `_writerTask.GetAwaiter().GetResult()` 同步等待背景寫入任務結束，隨後便處置 `_writerWakeup`。
  雖然等待任務結束能降低大部分競態，但若 `WriterLoopAsync` 在被取消時，其內部的 `Task.Delay` 拋出 `OperationCanceledException` 且尚未完全退出 `finally` 區塊，此時併發呼叫 `_writerWakeup.Dispose()` 仍有微小機率導致未預期的資源釋放衝突。
* **建議與修復**:
  確保 `_writerTask` 完整結束後再處置 `_writerWakeup`，或在 `WriterLoopAsync` 的 `finally` 區塊中，確保不依賴任何可能已被處置的外部資源。

#### 2. `RequestScope` 未被妥善處置時的 `AsyncLocal` 上下文洩漏風險
* **檔案/符號**: `ToolUtility/Dataverse/DataverseTrace.cs` -> `BeginRequest`
* **說明**:
  `BeginRequest` 回傳的 `RequestScope` 負責在 `Dispose` 時將 `AsyncLocal` 的 `RequestContext` 與 `s_current` 還原為先前的值。如果呼叫端（例如 Controller 或 Middleware）沒有使用 `using` 語法，或在異常路徑中漏掉 `Dispose()`，將導致執行緒或非同步內容中的上下文殘留，進而污染後續的請求，造成跨請求/跨使用者的追蹤資料混淆。
* **建議與修復**:
  在程式碼審查與靜態分析中，強制要求所有 `BeginRequest` 的呼叫必須搭配 `using` 宣告或 `try...finally` 區塊，以確保還原機制必定執行。

---

### Info (提示)

#### 1. Release 模式 Fail-Closed 機制驗證成功
* **檔案/符號**: `ToolUtility/Diagnostics/DiagnosticTraceOptions.cs`, `SpeechMessageProducts.ChurchReport/Program.cs`
* **說明**:
  經審查，Release 編譯模式下強制將 `DiagnosticTraceOptions` 設為停用（`CreateDisabled`），且 DI 容器強制注入 `NullToolUtilityTracer`，且 `FileToolUtilityTracer` 內部亦有 `#if DEBUG` 的編譯期防線。這完全阻斷了 Release 環境下因設定檔誤開而產生 Trace 檔案的可能性，符合安全設計要求。

#### 2. 全域 `Trace.Listeners` 污染防範有效
* **檔案/符號**: `ToolUtility/Diagnostics/FileToolUtilityTracer.cs`, `ToolUtility/Diagnostics/TraceLogger.cs`
* **說明**:
  重構後的 `FileToolUtilityTracer` 與 legacy `TraceLogger` 皆已改為直接持有並寫入私有的 `StreamWriter`，不再向全域 `System.Diagnostics.Trace.Listeners` 註冊自身。這有效避免了全域日誌重複輸出、檔案無限膨脹與資源寫入競態的問題。
