# 統一診斷追蹤與分析器審查報告 (Unified Trace Guard and Analyzer Review)

本報告針對 `SpeechMessageProducts` 專案中新增與修改的統一診斷追蹤（Unified Trace）機制及 PowerShell 分析腳本進行審查。審查重點在於 Release 模式下的 Fail-closed 安全防禦、資源釋放與生命週期管理、編碼相容性、記憶體邊界以及測試覆蓋率。

---

## VALIDATION REPORT

```
VALIDATION REPORT
=================
User Experience: 12/20 - 統一了設定開關，開發人員只需修改一個地方即可控制三種 Trace，體驗良好。然而，由於 C# 原始碼檔案編碼損壞產生亂碼，導致應用程式在啟動時會直接拋出例外並崩潰，嚴重影響開發與部署體驗。
Visual Consistency: 18/20 - PowerShell 分析報告的 Markdown 格式清晰，且成功遮罩了敏感資料（如身分識別碼與路徑），符合設計要求。
Accessibility: 20/20 - 此變更主要為後端診斷與分析工具，不涉及 UI 元素，存取性良好。
Performance: 16/20 - 分析器使用唯讀串流逐行讀取，避免大檔案記憶體暴漲。但在 Program.cs 中存在同步阻塞等待非同步監控任務結束的設計，有優化空間。
Browser Compatibility: 20/20 - 不涉及瀏覽器相容性。

TOTAL SCORE: 76/100

ISSUES FOUND:
- DiagnosticTraceOptions.cs 中的預設目錄常數含有非法字元與亂碼，導致應用程式在啟動時崩潰。
- 多個新增的 C# 檔案編碼損壞，含有大量亂碼，違反 UTF-8 without BOM 規範。
- Analyze-ChurchReportTraces.ps1 中 Big5 解碼使用 ExceptionFallback 可能導致分析因單一無效字元而中斷。
- Program.cs 中同步阻塞等待非同步監控任務結束。

RECOMMENDATION: NEEDS_IMPROVEMENT
```

---

## 1. Critical 發現

### 1.1 `DiagnosticTraceOptions.cs` 中的預設目錄常數含有非法字元與亂碼，導致應用程式啟動崩潰
- **檔案/符號**：`ToolUtility/Diagnostics/DiagnosticTraceOptions.cs` -> `DefaultDirectory`
- **原因分析**：
  在程式碼中，`DefaultDirectory` 被定義為：
  ```csharp
  public const string DefaultDirectory = @"D:\?日餈質馱";
  ```
  其中 `?` 是 Windows 檔案系統中的非法字元。當程式呼叫 `DiagnosticTraceOptions.CreateDisabled` 或 `FromConfiguration` 時，會觸發 `Path.GetFullPath(directory.Trim())`。由於路徑中含有 `?`，`Path.GetFullPath` 會拋出 `ArgumentException`（"Illegal characters in path."）。
  這會導致以下嚴重後果：
  1. **Release 模式崩潰**：在 Release 模式下，`Program.cs` 會直接呼叫 `CreateDisabled`，導致應用程式啟動時立即崩潰。
  2. **Debug 模式崩潰**：在 Debug 模式下，即使 `FromConfiguration` 的例外被捕獲，`catch` 區塊中呼叫 `CreateDisabled` 仍會再次拋出例外，導致應用程式崩潰。
  3. **測試與 DI 初始化失敗**：所有依賴注入測試（如 `ServiceCollectionExtensions` 中的 fallback 註冊）在初始化時都會崩潰。
- **修復建議**：
  將 `DefaultDirectory` 修改為正確的繁體中文字串，並確保檔案以 **UTF-8 without BOM** 編碼保存：
  ```csharp
  public const string DefaultDirectory = @"D:\除錯追蹤";
  ```

### 1.2 多個新增的 C# 檔案編碼損壞，含有大量亂碼
- **檔案/符號**：
  - `ToolUtility/Diagnostics/DiagnosticTraceOptions.cs`
  - `ToolUtility/Diagnostics/NullToolUtilityTracer.cs`
  - `ToolUtility.Dataverse.Tests/DiagnosticTraceOptionsTests.cs`
- **原因分析**：
  這些檔案在寫入時可能使用了錯誤的編碼（例如將 UTF-8 的字串用 Big5 寫入，或者相反），導致檔案中的中文字元（包括註解和例外訊息）全部變成亂碼。這違反了 PRD 中「修改的 .cs/.cshtml 為 UTF-8 without BOM」的編碼要求，且會影響程式碼的可讀性與維護性。
  *例如 `DiagnosticTraceOptions.cs` 第 84 行：*
  ```csharp
  throw new ArgumentException("閮箸?桅?敹??臬閫????獢頂蝯梯楝敺€?, nameof(directory));
  ```
- **修復建議**：
  使用支援 UTF-8 without BOM 的編輯器重新儲存這些檔案，並將亂碼部分修復為正確的繁體中文。例如：
  ```csharp
  throw new ArgumentException("診斷目錄必須是可解析的絕對或相對路徑。", nameof(directory));
  ```

---

## 2. Warning 發現

### 2.1 `Analyze-ChurchReportTraces.ps1` 中 Big5 解碼使用 `ExceptionFallback` 可能導致分析因單一無效字元而中斷
- **檔案/符號**：`SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1` -> `Analyze-ToolUtilityTrace`
- **原因分析**：
  在分析 legacy `CHURCH_REPORT_TRACE.TXT` 時，解碼器設定了 `[System.Text.DecoderFallback]::ExceptionFallback`：
  ```powershell
  $big5 = [System.Text.Encoding]::GetEncoding(
      950,
      [System.Text.EncoderFallback]::ExceptionFallback,
      [System.Text.DecoderFallback]::ExceptionFallback)
  ```
  如果 legacy 日誌檔案中因為寫入中斷、網路傳輸或歷史原因含有一個無法被 Big5 正確解碼的字元，`StreamReader.ReadLine()` 將會拋出解碼例外。這會導致整個檔案的分析立即中斷，無法繼續分析後續的行，且會將狀態判定為 `FAIL`（exit 2）。
- **修復建議**：
  建議改用 `ReplacementFallback`（例如用 `?` 代替無法解碼的字元），並在分析器中記錄解碼錯誤的次數。這樣既能指出檔案中存在編碼問題（回報 WARN 或 FAIL），又能完整分析完整個檔案的效能與錯誤線索。
  ```powershell
  $big5 = [System.Text.Encoding]::GetEncoding(
      950,
      [System.Text.EncoderFallback]::ReplacementFallback,
      [System.Text.DecoderFallback]::ReplacementFallback)
  ```

### 2.2 `Program.cs` 中同步阻塞等待非同步監控任務結束
- **檔案/符號**：`SpeechMessageProducts.ChurchReport/Program.cs` -> `lifetime.ApplicationStopping.Register`
- **原因分析**：
  在應用程式停止時，程式碼使用 `_gcMonitoringTask?.GetAwaiter().GetResult()` 同步阻塞等待 GC 監控任務結束。雖然 `StartGCMonitoringAsync` 使用了 `ConfigureAwait(false)` 且有傳入 `CancellationToken`，但在 ASP.NET Core 的生命週期事件中同步阻塞非同步任務仍有潛在的延遲關閉風險。
- **修復建議**：
  由於 `StartGCMonitoringAsync` 已經綁定了 `lifetime.ApplicationStopping` 的 `CancellationToken`，當 Host 停止時，該任務會自動被取消並結束。我們不需要在 `Register` 中同步阻塞等待它結束，可以直接讓它在背景自然消亡，或者將 GC 監控實作為一個標準的 `IHostedService`，由 ASP.NET Core 容器優雅地管理其生命週期。

---

## 3. Info 發現

### 3.1 `FileToolUtilityTracer.cs` 中的時間格式未固定
- **檔案/符號**：`ToolUtility/Diagnostics/FileToolUtilityTracer.cs` -> `Write`
- **原因分析**：
  在寫入 `CHURCH_REPORT_TRACE.TXT` 時，時間欄位使用 `DateTime.Now` 的預設 `ToString()`。這會導致在不同文化特性（Culture）設定的伺服器上，輸出的時間格式不一致（例如 `12-hour` vs `24-hour`，或是不同的日期分隔符號），這會增加 PowerShell 分析器解析時間範圍的難度。
- **修復建議**：
  建議統一使用固定的格式化字串，例如：
  ```csharp
  writer.WriteLine("Time            =" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + Environment.NewLine);
  ```
  這與 `TraceLogger.cs` 中的時間格式化方式一致。

---

## 4. Positive Notes (做得好的地方)

1. **Fail-closed 安全設計**：
   在 Release 模式下，系統強制註冊 `NullToolUtilityTracer`，且不包含任何依設定分支建立檔案 tracer 的路徑。這確保了診斷功能在生產環境中絕對不會被意外啟用，符合安全規範。
2. **不污染全域 `Trace.Listeners`**：
   `FileToolUtilityTracer` 與 `TraceLogger` 均改為直接寫入 `StreamWriter`，不再向全域 `Trace.Listeners` 註冊，避免了多個實例重複註冊導致的記憶體洩漏與效能下降。
3. **PowerShell 腳本編碼防禦**：
   `Analyze-ChurchReportTraces.ps1` 聰明地使用 Unicode 字元碼（如 `([char]0x9664) + ([char]0x932F)`）來串接中文字串，成功避開了 PowerShell 腳本在不同語系環境下執行時可能產生的亂碼問題。
4. **檔案共用讀取模式**：
   寫入器與分析器均使用了 `FileShare.ReadWrite | FileShare.Delete`，這允許分析器在不鎖定檔案的情況下讀取正在被寫入的日誌，且支援日誌輪轉與刪除。
