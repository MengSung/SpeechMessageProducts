# 雙模型架構分析報告：ChurchReport Trace 兩層保護與三檔綜合分析

本報告針對 `SpeechMessageProducts.ChurchReport` 專案中的追蹤機制（Trace Guard）與整合診斷分析器進行深度架構分析。

---

## 1. UX Analysis (使用者影響評估)

### 1.1 使用者體驗與維運便利性
- **日誌路徑統一**：將三種不同來源的追蹤檔案（`dataverse-trace.jsonl`、`Trace.log`、`CHURCH_REPORT_TRACE.TXT`）統一建立於 `D:\除錯追蹤`，能大幅降低維運人員在排查系統問題時尋找日誌的認知負載。
- **自動化診斷報告**：透過 PowerShell 分析器產生的 Markdown 報告，維運人員能一目了然地掌握系統效能瓶頸（如 N+1 查詢、慢速 API、Pool 租約洩漏等），縮短平均修復時間（MTTR）。

### 1.2 隱私與安全防護（User Journey Implications）
- **假名化保護**：現有的 `DataverseTrace` 採用隨機 Salt 搭配 HMAC-SHA256 產生使用者假名（如 `u_a1b2`），能有效防止使用者識別資訊（PII）洩漏至日誌中。
- **敏感資料遮蔽**：PowerShell 分析器在掃描日誌時，若偵測到明文的 Email、密碼、Token 等敏感資訊，僅在報告中標記行號與風險等級，**絕對不輸出敏感內容原文**，避免二次洩漏。

### 1.3 行動端與無障礙考量
- **結構化報告**：產出的 Markdown 報告應具備良好的標題層級與表格結構，便於螢幕閱讀器（Accessibility）讀取，且在行動端瀏覽時不易變形。

---

## 2. Design System Evaluation (設計系統評估)

### 2.1 一致性與模式（Consistency with Existing Patterns）
- **集中化設定**：引入統一的 `DiagnosticsTrace` 設定區段（包含 `Enabled` 與 `Directory`），取代原本分散在 `Dataverse:Trace`、`EnableTrace`、`Profiling:Enabled` 的設定，符合單一來源原則（Single Source of Truth）。
- **空對象模式（Null Object Pattern）**：註冊 `NullToolUtilityTracer` 作為 `IToolUtilityTracer` 的預設實作。當追蹤停用時，DI 容器注入空實作，避免了程式碼中出現大量的 `if (tracer != null)` 判斷，保持程式碼的簡潔與一致性。

### 2.2 元件重用性與命名規範
- 統一使用 `DiagnosticsTrace` 作為設定名稱，與現有的 `PaymentDebugLog` 等命名風格保持一致。
- 檔名固定為 `dataverse-trace.jsonl`、`Trace.log`、`CHURCH_REPORT_TRACE.TXT`，避免因設定錯誤導致檔名混亂。

---

## 3. Technical Considerations (技術考量)

### 3.1 關鍵風險評估 (Critical / Warning / Info)

| 風險等級 | 影響範疇 | 具體說明與檔案引用 |
| :--- | :--- | :--- |
| **Critical** | 效能與記憶體 | **全域 `Trace.Listeners` 污染**：`FileToolUtilityTracer` (`FileToolUtilityTracer.cs:87`) 與 legacy `TraceLogger` (`TraceLogger.cs:87`) 都會將自己註冊到全域的 `Trace.Listeners`。這會導致任何呼叫 `Trace.WriteLine` 的地方（包括 `Program.cs` 的 GC 監控、ASP.NET Core 內部日誌等）都會被重複寫入到 `CHURCH_REPORT_TRACE.TXT` 與 `Trace.log` 中，造成嚴重的磁碟 I/O 負擔與檔案膨脹。 |
| **Critical** | 資源洩漏 | **檔案鎖定與未釋放資源**：若 `FileToolUtilityTracer` 或 `TraceLogger` 未被正確 Dispose，底層的 `FileStream` 會一直保持開啟狀態，導致檔案被鎖定，其他進程（包括 PowerShell 分析器）無法讀取，甚至在 IIS 重啟時發生衝突。 |
| **Warning** | 效能與記憶體 | **PowerShell 大檔分析記憶體溢出**：若 PowerShell 分析器使用 `Get-Content` 或 `[System.IO.File]::ReadAllLines` 一次性載入 60MB 的日誌檔，會導致 PowerShell 進程記憶體飆升。 |
| **Warning** | 編碼相容性 | **編碼亂碼問題**：`FileToolUtilityTracer` 與 `TraceLogger` 預設使用 `big5` 編碼寫入，而 `DataverseTrace` 使用 `UTF-8`。PowerShell 分析器若未正確處理編碼，會導致中文字元解析失敗。 |
| **Info** | 程式碼冗餘 | `Program.cs` 中 `InitializeTraceListener` 被重複呼叫兩次（Main 中的第 57 行與第 73 行），雖然有 lock 保護，但仍屬冗餘代碼。 |

### 3.2 Release 安全防線驗證
目前**無法保證** Release 設定誤開時不產生檔案。因為 `Startup.cs` 中無條件註冊了 `FileToolUtilityTracer`，且其建構子預設會建立 `D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT`。
必須透過編譯條件（`#if DEBUG`）在 Release 模式下強制注入 `NullToolUtilityTracer`，且不讀取設定檔中的 `Enabled` 欄位，才能達成硬性防線。

### 3.3 `System.Diagnostics.Trace.Listeners` 共用風險
- **重複輸出**：`Trace.Listeners` 是全域靜態集合。當多個組件都向其添加 `TextWriterTraceListener` 時，任何 `Trace.WriteLine` 呼叫都會分發給所有 Listener，導致日誌內容交叉污染。
- **解決方案**：`FileToolUtilityTracer` 與 `TraceLogger` 應改為直接持有並寫入私有的 `StreamWriter`，不透過全域 `Trace` 轉發。

### 3.4 PowerShell 大檔串流與敏感資訊報告陷阱
- **大檔串流**：避免使用 `Get-Content`，改用 `[System.IO.File]::ReadLines()`（.NET 逐行延遲讀取）或 `StreamReader`。
- **正在 Append 的處理**：使用 `FileShare.ReadWrite` 開啟檔案，避免因為網站正在寫入而導致 PowerShell 讀取失敗（IOException: File in use）。
- **資料配對與敏感資訊**：使用 `Hashtable` 進行 `traceId` 與 `leaseId` 的配對，但必須限制配對集合的最大容量（例如最多保留 10000 個未配對的 key），防止記憶體無界增長。

---

## 4. Options (替代方案)

### 方案 A：完全依賴編譯條件（`#if DEBUG`）
- **優點**：Release 模式下絕對安全，程式碼會被編譯器移除。
- **缺點**：無法在測試環境（Staging/UAT，通常是 Release 建置）中臨時開啟追蹤進行排錯。

### 方案 B：完全依賴 appsettings 設定
- **優點**：極具彈性，可在任何環境下透過修改設定檔啟停追蹤。
- **缺點**：若 Production 設定檔不慎將 `Enabled` 設為 `true`，會導致敏感資料洩漏與效能下降。

### 方案 C：兩層保護（建議方案）
- **設計**：Release 建置時，組合根（Composition Root）強制注入 `NullToolUtilityTracer` 且不啟動 `DataverseTrace` 的背景工作，完全忽略設定檔中的 `Enabled=true`；Debug 建置時，才讀取設定檔決定是否啟停。
- **優點**：兼顧 Production 的絕對安全（硬性防線）與 Development/Debug 的日常便利。

---

## 5. Recommendation (建議)

建議採用 **方案 C（兩層保護）**。

### 5.1 最小且可測試的集中設定/DI 設計
1. **定義統一的 `DiagnosticTraceOptions`**：
   ```csharp
   public class DiagnosticTraceOptions
   {
       public bool Enabled { get; set; }
       public string Directory { get; set; } = @"D:\除錯追蹤";
       public string DataverseTracePath => Path.Combine(Directory, "dataverse-trace.jsonl");
       public string ApplicationTracePath => Path.Combine(Directory, "Trace.log");
       public string ToolUtilityTracePath => Path.Combine(Directory, "CHURCH_REPORT_TRACE.TXT");
   }
   ```

2. **在 `Startup.cs` 中的 DI 註冊**：
   ```csharp
   #if DEBUG
       var traceOptions = Configuration.GetSection("DiagnosticsTrace").Get<DiagnosticTraceOptions>() ?? new DiagnosticTraceOptions();
   #else
       var traceOptions = new DiagnosticTraceOptions { Enabled = false }; // Release 強制關閉
   #endif

   services.AddSingleton(traceOptions);

   #if DEBUG
       if (traceOptions.Enabled)
       {
           services.AddSingleton<IToolUtilityTracer>(sp => new FileToolUtilityTracer(traceOptions.ToolUtilityTracePath));
           services.AddSingleton<DataverseTraceOptions>(sp => new DataverseTraceOptions 
           { 
               Enabled = true, 
               Path = traceOptions.DataverseTracePath 
           });
       }
       else
       {
           services.AddSingleton<IToolUtilityTracer, NullToolUtilityTracer>();
           services.AddSingleton<DataverseTraceOptions>(new DataverseTraceOptions { Enabled = false });
       }
   #else
       services.AddSingleton<IToolUtilityTracer, NullToolUtilityTracer>();
       services.AddSingleton<DataverseTraceOptions>(new DataverseTraceOptions { Enabled = false });
   #endif
   ```

### 5.2 必須先寫的測試與 Release 實證命令
- **單元測試**：
  - 驗證 `NullToolUtilityTracer` 呼叫 `Write` 時不會產生任何 I/O 操作。
  - 驗證 `FileToolUtilityTracer` 在 `Dispose` 後，底層的檔案控制代碼已被釋放。
  - 驗證 `DataverseTrace` 在 `Enabled = false` 時，不會啟動背景 Task。
- **Release 實證命令**：
  1. 建置 Release 版本：
     ```bash
     dotnet publish -c Release -o ./publish
     ```
  2. 修改 `./publish/appsettings.json`，手動加入：
     ```json
     "DiagnosticsTrace": {
       "Enabled": true,
       "Directory": "D:\\除錯追蹤"
     }
     ```
  3. 啟動網站並進行操作。
  4. 驗證 `D:\除錯追蹤` 目錄下**沒有**產生任何日誌檔案。

### 5.3 對 `design.md` 的具體修訂建議
- 明確指出 `FileToolUtilityTracer` 與 `TraceLogger` 必須移除對 `Trace.Listeners` 的依賴，改為直接寫入私有 `StreamWriter`。
- 明確定義 `NullToolUtilityTracer` 的類別結構。
- 在設計中加入 PowerShell 分析器處理 Big5 與 UTF-8 混雜編碼的具體實作細節（如使用 `FileShare.ReadWrite` 與指定編碼的 `StreamReader`）。
