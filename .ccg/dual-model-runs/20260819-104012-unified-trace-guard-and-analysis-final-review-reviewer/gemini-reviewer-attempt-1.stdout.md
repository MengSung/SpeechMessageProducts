# ChurchReport Trace 統一收斂與安全強化最終審查報告

本報告針對 `ChurchReport` 系統中的 Trace 保護機制（Trace Guard）與 PowerShell 綜合分析器進行唯讀架構審查，評估其在 Release 安全防線、資源釋放、隱私隔離以及腳本相容性等維度的合規性。

---

## Critical 發現

### 1. `DataverseTrace` 缺乏編譯期硬性防線 (Fail-Closed)
* **檔案路徑**：`ToolUtility\Dataverse\DataverseTrace.cs`
* **符號**：`DataverseTrace` 建構子 / `DataverseTraceOptions`
* **風險說明**：
  雖然 `FileToolUtilityTracer` 與 `TraceLogger` 都實作了編譯期隔離（透過 `IsCompileTimeTraceEnabled()` 檢查 `#if DEBUG`），但 `DataverseTrace` 僅依賴 `DataverseTraceOptions.Enabled` 屬性。如果 Release 環境的配置檔（如 `appsettings.json`）因人為疏失或環境變數將 `DiagnosticsTrace:Enabled` 設為 `true`，`DataverseTrace` 仍會啟動背景 Task (`WriterLoopAsync`) 並在生產環境寫入 `dataverse-trace.jsonl`。這違反了「Release 必須 fail closed，即使配置或環境變數為 true 亦然」的硬性安全防線。
* **修復建議**：
  在 `DataverseTrace` 的建構子或 `DataverseTraceOptions.FromDiagnosticOptions` 中，強制加上編譯期檢查。若非 `DEBUG` 模式，強制將 `Enabled` 設為 `false`：
  ```csharp
  public DataverseTrace(DataverseTraceOptions options)
  {
      _options = options ?? throw new ArgumentNullException(nameof(options));
      _options.Validate();
      
      #if DEBUG
      Enabled = _options.Enabled;
      #else
      Enabled = false; // Release 模式下強制關閉，不接受任何配置啟用
      #endif

      if (Enabled)
      {
          _salt = RandomNumberGenerator.GetBytes(32);
          _writerTask = Task.Run(WriterLoopAsync);
      }
  }
  ```

---

## Warning 發現

### 1. PowerShell 7+ 環境下的 Big5 編碼相容性風險
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Tools\Analyze-ChurchReportTraces.ps1`
* **符號**：`Analyze-ToolUtilityTrace` 函數
* **風險說明**：
  分析器腳本在讀取 `CHURCH_REPORT_TRACE.TXT` 時，使用 `[System.Text.Encoding]::GetEncoding(950)`。在以 .NET Core 為基礎的 PowerShell 7+ (Core) 環境中，預設不支援 Big5 (950) 編碼，必須先註冊 `CodePagesEncodingProvider`，否則會拋出 `ArgumentException` 異常，導致分析中斷並回傳 Exit Code 1。
* **修復建議**：
  在腳本初始化階段，加入對 PowerShell 版本的判斷，若為 PowerShell 6+ (Core)，則動態註冊 CodePages 提供者：
  ```powershell
  if ($PSVersionTable.PSVersion.Major -ge 6) {
      [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
  }
  ```

### 2. `DataverseTrace` 佇列溢出時的鎖定競爭
* **檔案路徑**：`ToolUtility\Dataverse\DataverseTrace.cs`
* **符號**：`Enqueue` 方法
* **風險說明**：
  當佇列長度超過 `QueueCapacity` 時，`Enqueue` 會在 `lock (_queueSync)` 區塊內執行 `while` 迴圈進行 `TryDequeue`。在高併發的環境下，這會導致呼叫端執行緒（如 ASP.NET Core 請求執行緒）在鎖定競爭中被阻塞，進而影響 API 吞吐量與回應時間。
* **修復建議**：
  考慮將丟棄邏輯移出呼叫端執行緒，或者使用 `ConcurrentQueue` 的無鎖特性，僅在背景寫入執行緒中進行長度調節，避免在 `Enqueue` 時使用重度鎖定。

---

## Info 發現

### 1. 原始碼檔案標頭註解亂碼
* **檔案路徑**：
  * `ToolUtility\Diagnostics\DiagnosticTraceOptions.cs`
  * `ToolUtility\Diagnostics\NullToolUtilityTracer.cs`
  * `ToolUtility\Diagnostics\IToolUtilityTracer.cs`
  * `ToolUtility\Diagnostics\FileToolUtilityTracer.cs`
  * `ToolUtility\Diagnostics\TraceLogger.cs`
* **風險說明**：
  上述檔案的標頭註解存在 Big5/UTF-8 轉換導致的亂碼（例如 `// AI-蝜?銝剜?瑼?閮餉圾`）。雖然不影響編譯與執行，但影響程式碼可讀性與後續維護。
* **修復建議**：
  將所有原始碼檔案統一轉換為 UTF-8 without BOM 編碼，並修復損壞的中文註解。

### 2. 測試覆蓋完整性確認
* **檔案路徑**：`ToolUtility.Dataverse.Tests` 專案
* **說明**：
  現有的單元測試（如 `DataverseTraceTests.cs` 與 `FileToolUtilityTracerTests.cs`）設計非常嚴密，成功驗證了無鎖/有界佇列、假名化雜湊穩定性、以及全域 `Trace.Listeners` 未被污染等關鍵指標。建議將這些測試納入 CI/CD 流程中，作為防止迴歸的防線。
