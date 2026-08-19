# ChurchReport Trace 兩層保護與三檔綜合分析報告

本報告針對 `ChurchReport` 系統中的 Trace 機制進行深入分析，評估統一追蹤路徑、兩層保護機制（Release 編譯期硬性防線與 Debug 執行期開關）、全域 `Trace.Listeners` 的共用風險，以及 PowerShell 大檔分析器的設計陷阱，並提供具體的架構建議。

---

## 1. UX Analysis (使用者影響評估)

### 使用者體驗與系統效能
- **I/O 負載與響應延遲**：在 Release（生產）環境中，若因設定誤開而啟用詳細追蹤，頻繁的磁碟寫入（特別是包含 Stack Trace 的 legacy 記錄）會大幅增加伺服器 I/O 負載，導致 API 響應時間變慢，直接影響終端使用者的操作流暢度。
- **服務可用性風險**：若 Trace 資源（如 `FileStream`、`TextWriterTraceListener`）未妥善管理，可能導致記憶體洩漏（Memory Leak）或檔案控制代碼耗盡，最終引發系統崩潰（OOM），造成服務中斷。

### 安全性與隱私防護 (Session/Cross-User Leakage)
- **假名化保護**：現有的 `DataverseTrace` 採用隨機 Salt 搭配 HMACSHA256 產生使用者假名（如 `u_a1b2c3d4`），能有效防止使用者識別資訊洩漏。
- **敏感資料外洩風險**：Legacy `Trace.log` 與 `CHURCH_REPORT_TRACE.TXT` 未經過嚴格的欄位遮蔽，可能包含 SQL 查詢、CRM 實體內容或使用者輸入的敏感資訊。若在生產環境中產生這些檔案，將面臨極大的資安合規風險。

---

## 2. Design Evaluation (設計系統評估)

### 一致性與模式 alignment
- **設定集中化**：目前 `EnableTrace`、`Dataverse:Trace:Enabled` 與 `Profiling:Enabled` 分散於不同區段，容易導致維護遺漏。建議統一收攏至單一 `DiagnosticsTrace` 區段：
  ```json
  {
    "DiagnosticsTrace": {
      "Enabled": true,
      "Directory": "D:\\除錯追蹤"
    }
  }
  ```
- **兩層保護機制 (Two-Layer Guard)**：
  - **第一層（編譯期硬性防線）**：在 Release 編譯模式下，透過預處理指令（`#if DEBUG`）或程式碼強制將 `Enabled` 設為 `false`，並註冊 `NullToolUtilityTracer`，確保即使設定檔被誤設為 `true`，也絕對不會產生任何 Trace 檔案。
  - **第二層（執行期開關）**：僅在 Debug 編譯模式下，讀取設定檔的 `Enabled` 狀態來決定是否啟用追蹤。

---

## 3. Technical Considerations (技術考量與前端架構)

### 全域 `Trace.Listeners` 共用風險 (Critical)
- **重複輸出與檔案膨脹**：`Trace.Listeners` 是全域靜態集合。`FileToolUtilityTracer` 與 `TraceLogger` 若將各自的 `TextWriterTraceListener` 加入其中，且兩者皆指向 `CHURCH_REPORT_TRACE.TXT`，會導致任何 `Trace.WriteLine` 的呼叫被重複寫入，且 `Trace.log` 與 `CHURCH_REPORT_TRACE.TXT` 的內容會互相污染。
- **競態條件 (Race Condition)**：多個 `StreamWriter` 同時持有並寫入同一個實體檔案，即使設定了 `FileShare.ReadWrite`，也會因為緩衝區刷新時機不同，導致寫入內容交錯損壞或拋出 `IOException`。
- **記憶體洩漏**：若 DI 生命週期配置不當（例如將 Tracer 註冊為 Scoped 或 Transient），每次建立實例都會向全域 `Trace.Listeners` 新增一個 listener，導致記憶體與 CPU 開銷持續飆升。

### 建議的 DI 架構
- 移除非必要的複雜度，預設註冊 `NullToolUtilityTracer`（Fail-Closed 原則）。
- 僅在 `DEBUG` 且 `DiagnosticsTrace:Enabled == true` 時，才註冊實體的 `FileToolUtilityTracer`。

---

## 4. Options (替代方案與權衡)

### 方案 A：維持現有全域 `Trace.Listeners` 機制，僅調整註冊邏輯
- **優點**：修改幅度小，現有程式碼相依性不變。
- **缺點**：無法徹底解決全域污染與重複輸出的問題，多個 Tracer 之間的競態風險依然存在。

### 方案 B：去全域化，Tracer 獨立管理私有 `StreamWriter` (推薦)
- **優點**：
  - 徹底移除 `Trace.Listeners.Add` 的呼叫。
  - `FileToolUtilityTracer` 與 `TraceLogger` 直接透過私有的 `StreamWriter` 寫入檔案，互不干擾。
  - 避免全域 `Trace.WriteLine` 的內容被意外寫入診斷檔案中。
- **缺點**：需要重構 Tracer 的寫入邏輯，使其不再依賴 `System.Diagnostics.Trace` 的全域輸出。

---

## 5. Recommendation (架構師推薦方案)

採用 **方案 B**，並結合**兩層保護機制**與**集中化設定**。

### 理由
1. **安全性**：確保 Release 環境下絕對不產生任何 Trace 檔案，達到零洩漏風險。
2. **穩定性**：避免全域 `Trace.Listeners` 造成的記憶體洩漏與檔案寫入衝突。
3. **可維護性**：設定單一化，且透過 DI 容器明確管理生命週期。

---

## 6. 針對 7 個分析問題的具體回覆

### ① Critical / Warning / Info 風險分級

| 風險等級 | 影響範疇 | 具體風險描述 |
| :--- | :--- | :--- |
| **Critical** | 記憶體與效能 | `FileToolUtilityTracer` 與 `TraceLogger` 重複向全域 `Trace.Listeners` 註冊，導致記憶體洩漏與重複寫入。 |
| **Critical** | 安全性 | Release 環境下若誤開 Trace，敏感資料（如 CRM 查詢、Token）會以明文寫入 `Trace.log` 與 `CHURCH_REPORT_TRACE.TXT`。 |
| **Warning** | 架構維護 | 設定檔（`EnableTrace`、`Dataverse:Trace`）分散，容易導致 Production 環境設定不一致。 |
| **Warning** | 診斷工具 | 現有 PowerShell 腳本使用 `Get-Content` 一次性載入，面對 60MB 以上的大檔時會導致記憶體溢出（OOM）。 |
| **Info** | 程式碼冗餘 | `Program.cs` 中的 `InitializeTraceListener` 在 `Main` 中被重複呼叫了兩次（第 57 行與第 73 行）。 |

### ② 是否真的能保證 Release 設定誤開仍不產生三檔？
**現況無法保證**。目前 `Startup.cs` 無條件註冊了 `FileToolUtilityTracer`，且其建構子預設會建立 `D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT`。
**改善方案**：必須在 `Startup.cs` 中使用 `#if DEBUG` 進行編譯期隔離，Release 模式下強制註冊 `NullToolUtilityTracer`，且不讀取任何啟用設定。

### ③ `System.Diagnostics.Trace.Listeners` 共用造成的重複輸出、資源所有權或競態風險
- **重複輸出**：全域 `Trace.WriteLine` 會同時觸發所有已註冊的 listener，導致 `Trace.log` 與 `CHURCH_REPORT_TRACE.TXT` 內容互相混雜。
- **競態風險**：多個 `StreamWriter` 同時寫入同一個檔案，會導致緩衝區衝突與內容損壞。
- **解決方案**：重構 `FileToolUtilityTracer`，移除 `Trace.Listeners.Add`，改為直接呼叫私有 `StreamWriter.WriteLine`。

### ④ 最小且可測試的集中設定/DI 設計
1. **定義統一設定類別**：
   ```csharp
   public sealed class DiagnosticTraceOptions
   {
       public bool Enabled { get; set; }
       public string Directory { get; set; } = @"D:\除錯追蹤";
       public string DataverseTracePath => Path.Combine(Directory, "dataverse-trace.jsonl");
       public string TraceLogPath => Path.Combine(Directory, "Trace.log");
       public string ChurchReportTracePath => Path.Combine(Directory, "CHURCH_REPORT_TRACE.TXT");
   }
   ```
2. **DI 註冊 (Startup.cs)**：
   ```csharp
   var traceOptions = new DiagnosticTraceOptions();
   #if DEBUG
   Configuration.GetSection("DiagnosticsTrace").Bind(traceOptions);
   #else
   traceOptions.Enabled = false; // Release 強制關閉
   #endif

   services.AddSingleton(traceOptions);

   if (traceOptions.Enabled)
   {
       services.AddSingleton<IToolUtilityTracer, FileToolUtilityTracer>();
   }
   else
   {
       services.AddSingleton<IToolUtilityTracer, NullToolUtilityTracer>();
   }
   ```

### ⑤ PowerShell 大檔串流、Big5/UTF-8、正在 append、資料配對與敏感資訊報告的陷阱
- **大檔串流與鎖定**：必須使用 `[System.IO.File]::ReadLines()` 或 `StreamReader` 搭配 `FileShare.ReadWrite` 進行逐行讀取，避免鎖定正在寫入的檔案。
- **編碼處理**：`Trace.log` 與 `CHURCH_REPORT_TRACE.TXT` 為 **Big5** 編碼，`dataverse-trace.jsonl` 為 **UTF-8**。PowerShell 讀取時必須明確指定編碼，否則會產生亂碼。
- **資料配對限制**：配對 `request.begin` 與 `request.end` 時，應使用 Bounded Set（限制快取大小，例如最多 10,000 筆未配對記錄），防止分析器本身記憶體溢出。
- **敏感資訊遮蔽**：報告中僅記錄「第 X 行發現敏感模式」，絕對不輸出匹配到的原始敏感字串。

### ⑥ 必須先寫的測試與 Release 實證命令
- **單元測試**：
  1. 驗證 `DiagnosticTraceOptions` 在 Release 編譯下，其 `Enabled` 屬性恆為 `false`。
  2. 驗證 `NullToolUtilityTracer` 呼叫 `Write` 時不會建立任何實體檔案。
  3. 驗證 `FileToolUtilityTracer` 在 `Dispose` 後，底層的 `FileStream` 已被正確關閉。
- **Release 實證命令**：
  ```bash
  # 1. 以 Release 模式發行
  dotnet publish -c Release -o ./publish

  # 2. 修改發行目錄下的 appsettings.json，將 DiagnosticsTrace:Enabled 設為 true
  # 3. 執行網站並進行 CRM 操作
  # 4. 驗證 D:\除錯追蹤 目錄下「沒有」產生任何 Trace 檔案
  ```

### ⑦ 對 `.trellis/tasks/08-19-unified-trace-guard-and-analysis/design.md` 的具體修訂建議
1. **明確禁止全域註冊**：在設計文件中加入「禁止將實體 Tracer 的 listener 加入全域 `Trace.Listeners`」的硬性約束。
2. **補充編碼規範**：明確指出 `CHURCH_REPORT_TRACE.TXT` 與 `Trace.log` 必須使用 Big5 寫入與讀取，而 `dataverse-trace.jsonl` 必須使用 UTF-8。
3. **完善 PowerShell 串流設計**：在分析器設計章節中，將 `Get-Content` 改為 `StreamReader` 逐行讀取與 `FileShare.ReadWrite` 的具體實作範例。
