以下是針對 **unified ChurchReport Trace** 任務的 advisory review 審查報告。

本審查專注於 Release 編譯防線、資源釋放、跨要求隔離、PowerShell 分析器相容性與編碼正確性。

---

# VALIDATION REPORT
=================
User Experience: 12/20 - 統一了設定開關，開發人員只需修改一處即可控制三種 Trace，體驗良好。然而，由於 C# 原始碼檔案編碼損壞導致非法字元寫入，程式在啟動時會直接崩潰，嚴重影響開發體驗。
Visual Consistency: 18/20 - Markdown 報告格式清晰，且對敏感資料（如 GUID、Email、數字）進行了遮罩，符合設計一致性。
Accessibility: 20/20 - 本次變更為純後端診斷與分析工具，不涉及 UI 元素，存取性良好。
Performance: 16/20 - 分析器使用唯讀共享串流逐行讀取，避免大檔案造成記憶體暴漲。然而，`Program.cs` 中存在同步阻塞等待非同步任務結束的設計，有優化空間。
Browser Compatibility: 20/20 - 不涉及瀏覽器相容性。

TOTAL SCORE: 86/100

ISSUES FOUND:
- [Critical] `DiagnosticTraceOptions.cs` 中的預設目錄常數含有非法字元 `?`，導致應用程式在啟動時崩潰。
- [Critical] 多個新增的 C# 檔案編碼損壞，含有大量亂碼。
- [Warning] `Analyze-ChurchReportTraces.ps1` 中 Big5 解碼使用 `ExceptionFallback` 可能導致分析因單一無效字元而中斷。
- [Warning] `Program.cs` 中同步阻塞等待非同步監控任務結束。
- [Info] `FileToolUtilityTracer.cs` 中的時間格式未固定，可能增加分析器解析難度。

RECOMMENDATION: **NEEDS_IMPROVEMENT** (必須修復 Critical 崩潰問題後才能通過)

---

## 1. Summary (整體評估)
本次重構成功將 ChurchReport 的三種診斷輸出（`dataverse-trace.jsonl`、`Trace.log`、`CHURCH_REPORT_TRACE.TXT`）收攏至單一產品層設定區段 `DiagnosticsTrace`，並在 Release 編譯期引入了硬性停用的安全防線，設計方向正確且符合架構規範。
然而，**多個新增的 C# 檔案在寫入時發生了編碼損壞（亂碼），導致預設路徑常數中含有 Windows 非法字元 `?`**。這會導致應用程式在啟動初始化時直接拋出例外並崩潰，屬於阻礙性的 Critical 缺陷，必須優先修復。

---

## 2. Critical Issues (Critical 缺陷)

### 2.1 預設目錄常數含有非法字元，導致應用程式啟動崩潰
* **檔案/符號**：`ToolUtility/Diagnostics/DiagnosticTraceOptions.cs` -> `DefaultDirectory`
* **原因分析**：
  在程式碼中，`DefaultDirectory` 被定義為 `@"D:\?日餈質馱"`（此為「除錯追蹤」的亂碼）。其中問號 `?` 是 Windows 檔案系統中的非法字元。
  當程式呼叫 `DiagnosticTraceOptions.CreateDisabled` 或 `FromConfiguration` 時，會觸發 `Path.GetFullPath(directory.Trim())`。由於路徑中含有 `?`，`Path.GetFullPath` 會拋出 `ArgumentException`（"Illegal characters in path."）。
  這會導致：
  1. **Release 模式下**：`Program.cs` 直接崩潰，應用程式完全無法啟動。
  2. **Debug 模式下**：即使 `FromConfiguration` 的例外被捕獲，`catch` 區塊中呼叫 `CreateDisabled` 仍會再次拋出例外，導致應用程式崩潰。
  3. **測試與 DI 容器**：所有依賴注入測試（如 `ServiceCollectionExtensions` 中的 fallback 註冊）在初始化時都會崩潰。
* **修復建議**：
  將 `DefaultDirectory` 修改為正確的繁體中文字串，並確保檔案以 **UTF-8 without BOM** 編碼保存：
  ```csharp
  public const string DefaultDirectory = @"D:\除錯追蹤";
  ```
  同時，修復該檔案中所有因編碼錯誤產生的亂碼註解與例外訊息（例如第 81 行、第 112 行等）。

### 2.2 新增的 C# 檔案編碼損壞，含有大量亂碼
* **檔案/符號**：
  * `ToolUtility/Diagnostics/DiagnosticTraceOptions.cs`
  * `ToolUtility/Diagnostics/NullToolUtilityTracer.cs`
  * `ToolUtility.Dataverse.Tests/DiagnosticTraceOptionsTests.cs`
* **原因分析**：
  這些檔案在寫入時可能使用了錯誤的編碼（例如將 UTF-8 的字串用 Big5 寫入，或者相反），導致檔案中的中文字元（包括註解和例外訊息）全部變成亂碼。這違反了 PRD 中「修改的 .cs/.cshtml 為 UTF-8 without BOM」的編碼要求，且會影響程式碼的可讀性與維護性。
* **修復建議**：
  使用支援 UTF-8 without BOM 的編輯器重新儲存這些檔案，並將亂碼部分修復為正確的繁體中文。

---

## 3. Warning Issues (Warning 缺陷)

### 3.1 Big5 解碼使用 `ExceptionFallback` 可能導致分析因單一無效字元而中斷
* **檔案/符號**：`SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1` -> `Analyze-ToolUtilityTrace`
* **原因分析**：
  在分析 legacy `CHURCH_REPORT_TRACE.TXT` 時，解碼器設定了 `[System.Text.DecoderFallback]::ExceptionFallback`。
  如果 legacy 日誌檔案中因為寫入中斷、網路傳輸或歷史原因含有一個無法被 Big5 正確解碼的字元，`StreamReader.ReadLine()` 將會拋出解碼例外。這會導致整個檔案的分析立即中斷，無法繼續分析後續的行，且會將狀態判定為 `FAIL`（exit 2）。
* **修復建議**：
  建議改用 `ReplacementFallback`（例如用 `?` 代替無法解碼的字元），並在分析器中記錄解碼錯誤的次數。這樣既能指出檔案中存在編碼問題（回報 WARN 或 FAIL），又能完整分析完整個檔案的效能與錯誤線索。
  ```powershell
  $big5 = [System.Text.Encoding]::GetEncoding(
      950,
      [System.Text.EncoderFallback]::ReplacementFallback,
      [System.Text.DecoderFallback]::ReplacementFallback)
  ```

### 3.2 同步阻塞等待非同步監控任務結束
* **檔案/符號**：`SpeechMessageProducts.ChurchReport/Program.cs` -> `lifetime.ApplicationStopping.Register`
* **原因分析**：
  在應用程式停止時，程式碼使用 `_gcMonitoringTask?.GetAwaiter().GetResult()` 同步阻塞等待 GC 監控任務結束。雖然 `StartGCMonitoringAsync` 使用了 `ConfigureAwait(false)` 且有傳入 `CancellationToken`，但在 ASP.NET Core 的生命週期事件中同步阻塞非同步任務仍有潛在的延遲關閉風險。
* **修復建議**：
  由於 `StartGCMonitoringAsync` 已經綁定了 `lifetime.ApplicationStopping` 的 `CancellationToken`，當 Host 停止時，該任務會自動被取消並結束。我們不需要在 `Register` 中同步阻塞等待它結束，可以直接讓它在背景自然消亡，或者將 GC 監控實作為一個標準的 `IHostedService`，由 ASP.NET Core 容器優雅地管理其生命週期。

---

## 4. Suggestions (Info 建議)

### 4.1 固定 `FileToolUtilityTracer` 中的時間格式
* **檔案/符號**：`ToolUtility/Diagnostics/FileToolUtilityTracer.cs` -> `Write`
* **原因分析**：
  在寫入 `CHURCH_REPORT_TRACE.TXT` 時，時間欄位使用 `DateTime.Now` 的預設 `ToString()`。這會導致在不同文化特性（Culture）設定的伺服器上，輸出的時間格式不一致（例如 `12-hour` vs `24-hour`，或是不同的日期分隔符號），這會增加 PowerShell 分析器解析時間範圍的難度。
* **修復建議**：
  建議統一使用固定的格式化字串，例如：
  ```csharp
  writer.WriteLine("Time            =" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + Environment.NewLine);
  ```
  這與 `TraceLogger.cs` 中的時間格式化方式一致。

---

## 5. Positive Notes (優秀設計)
1. **PowerShell 避開編碼問題**：`Analyze-ChurchReportTraces.ps1` 中使用 Unicode 字元碼（如 `[char]0x9664`）來串接繁體中文路徑與關鍵字，非常聰明地避開了 PowerShell 腳本在不同語系環境下執行時的編碼解析問題。
2. **唯讀共享讀取**：分析器在讀取檔案時使用了 `FileShare.ReadWrite | FileShare.Delete`，確保在應用程式仍在寫入日誌時，分析器可以安全地讀取快照而不鎖定檔案。
3. **敏感資料遮罩**：分析器在產生 Markdown 報告時，對敏感資料進行了嚴格的正則表達式遮罩與去識別化，且不保存原始匹配行，避免報告成為第二個敏感資料洩漏源。
