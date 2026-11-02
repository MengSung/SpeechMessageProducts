# 統一 Trace 兩層保護與三檔綜合分析設計

## 設計摘要

本設計將 ChurchReport 的三種檔案追蹤收斂到單一 `DiagnosticsTrace` 組態。第一層是編譯期 fail-closed：Release 建置只能取得停用設定、空追蹤器與未註冊的檔案 listener；第二層是 Debug 執行期設定：只有 `DiagnosticsTrace:Enabled=true` 才建立 writer。分析工具與產品程序分離，採唯讀串流掃描三檔並產生 Markdown 報告。

## 已考慮方案

### 方案 A：只依賴 Debug/Release

- 優點：最少設定，Release 編譯結果容易推論。
- 缺點：Debug 環境無法不重新建置就關閉追蹤；現行 Dataverse Trace 是 runtime singleton，無法自然納入；誤以 Debug 發布正式環境時會全開。
- 結論：不足以滿足「一處設定」與操作彈性。

### 方案 B：只依賴 appsettings

- 優點：可用環境變數覆寫，操作簡單。
- 缺點：Release 若誤設 `Enabled=true` 就會寫檔，無法提供正式上線的硬性安全邊界。
- 結論：不符合使用者選定的兩層保護。

### 方案 C：Release 編譯防線 + Debug 集中設定（採用）

- 優點：Release 無法被部署設定誤開；Debug 只需一處開關與目錄；可讓所有 writer 共用同一不可變設定物件。
- 代價：需要調整 Program、Startup、ToolUtility DI 與測試，並提供 Release 實證。
- 結論：安全性、可操作性與維護成本最佳平衡。

## 組態契約

唯一產品層設定：

```json
{
  "DiagnosticsTrace": {
    "Enabled": true,
    "Directory": "D:\\除錯追蹤"
  }
}
```

- `Enabled`：預設 `false`。只有 Debug 建置會讀取並允許成為 `true`。
- `Directory`：預設為 content root 下的 `Logs`；Development 明確設定為 `D:\除錯追蹤`。
- 檔名固定為程式契約，避免維運人員在多個 key 間同步：
  - `dataverse-trace.jsonl`
  - `Trace.log`
  - `CHURCH_REPORT_TRACE.TXT`
- Production 不需要重複寫一份 `Enabled=false`；Release 程式碼本身會產生 disabled options。若 Production 設定誤含 `Enabled=true`，Release 仍保持關閉。
- 變更組態後需重啟程序；不實作 hot reload，避免 singleton writer 在 request 執行中切換造成競態、漏 flush 或檔案 handle 遺留。

## 元件與責任

### `DiagnosticTraceOptions`

共用、不可變的程序級設定模型，位於 ToolUtility 診斷層。負責驗證可信任的組態、解析 content root/絕對目錄並產生三個完整路徑；不建立目錄、不開檔、不持有 writer。Release 由組合根建立 disabled instance。

### ChurchReport 組合根

- `Program.cs` 在 Debug 且 options enabled 時才建立 `Trace.log` listener；Release 中初始化方法與呼叫維持 `#if DEBUG`。
- `Startup.cs` 只注入同一個 options instance。Debug + enabled 註冊 `FileToolUtilityTracer` 與 `TraceLoggerProvider`，否則註冊 `NullToolUtilityTracer` 並不註冊 provider。
- `ProfilingSwitch.Enabled` 與 master switch 同步；因此三檔與效能剖析由同一開關控制。

### ToolUtility 診斷層

- `AddToolUtility()` 預設註冊 `NullToolUtilityTracer`，讓其他產品未明確選擇檔案追蹤時 fail closed。
- ChurchReport 明確註冊的 tracer 會在 `TryAdd` 前勝出。
- `DataverseTraceOptions` 從同一個 `DiagnosticTraceOptions` 取得 Enabled 與 JSONL 路徑；不再獨立讀 `Dataverse:Trace`。
- `FileToolUtilityTracer` 仍是唯一擁有 legacy stream/writer 的 singleton，停用時不會被建立；它不再向全域 `Trace.Listeners` 加入 listener，而是以自己的 writer 寫入 `CHURCH_REPORT_TRACE.TXT`。Dispose 先 flush 再釋放 writer/stream。
- legacy `TraceLogger` 也不再加入全域 listener，且其 Release 寫檔路徑由編譯期 no-op 保護，避免未來外部呼叫繞過 DI。
- `Program` 是唯一允許擁有 `Trace.log` 全域 listener 的元件；既有大量 `System.Diagnostics.Trace.WriteLine` 呼叫仍集中流向 `Trace.log`，但不會污染 ToolUtility legacy 檔案。

## Release fail-closed 資料流

```text
Release build
  -> Program 建立 disabled DiagnosticTraceOptions
  -> Startup 註冊 NullToolUtilityTracer
  -> TraceLoggerProvider 不編譯/不註冊
  -> DataverseTraceOptions.Enabled = false
  -> DataverseTrace 不啟動 writer task、不建立檔案
  -> Trace.log listener 初始化碼不在 Release 執行路徑
```

任何 appsettings 或環境變數只能影響 Debug 分支，不能逆轉 Release 的 disabled instance。

## Debug 啟用資料流

```text
appsettings.Development.json DiagnosticsTrace
  -> Program 建立及驗證單一 options instance
  -> Program 唯一 Trace.log listener（Debug + Enabled）
  -> Startup FileToolUtilityTracer 私有 writer（CHURCH_REPORT_TRACE.TXT）
  -> DataverseTraceOptions（dataverse-trace.jsonl）
  -> ProfilingSwitch + TraceLoggerProvider
```

目錄建立採 writer 首次開啟前一次性建立。組態驗證失敗時 fail closed，記錄到 console 後不啟用檔案追蹤；不得讓診斷功能阻止正式主流程啟動。

## PowerShell 分析器

### 介面

```powershell
Analyze-ChurchReportTraces.ps1 `
  -TraceDirectory 'D:\除錯追蹤' `
  -ReportPath 'D:\除錯追蹤\ChurchReport-Trace-Report.md' `
  -Top 20
```

也允許用 `-DataverseTracePath`、`-ApplicationTracePath`、`-ToolUtilityTracePath` 個別覆寫，方便分析封存檔；正常使用只需 `-TraceDirectory`。

### 分析策略

- 所有輸入以 `FileShare.ReadWrite` 開啟，允許分析執行中的 append-only 檔案。
- JSONL 逐行解析，不使用 `ReadAllText`；記錄解析錯誤行號但不輸出原文。
- `Trace.log` 與 legacy Big5 檔逐行掃描；只保存聚合計數、固定 Top N 候選與必要分位數資料。若資料量超過安全上限，使用 bounded reservoir/直方圖而非保存所有事件。
- 路徑、GUID、長數字、email/token/password 模式在進入報告前遮蔽；報告不重印完整 stack trace 或原始使用者訊息。

### 報告章節

1. 執行摘要與總結狀態。
2. 三檔 inventory、時間範圍、大小、讀取/解析狀態。
3. Dataverse request、gateway、lease、pool、timeout/fault/dispose/cleanup、敏感資料與丟棄事件。
4. `Trace.log` 效能端點摘要、慢請求、CRM N+1、gap、startup 與 warning/error。
5. `CHURCH_REPORT_TRACE.TXT` legacy 呼叫、例外/錯誤線索、常見來源與格式完整性。
6. 跨檔結論：資料時間窗是否重疊、是否有 Trace 缺口、是否足以作為本次診斷證據。
7. 建議與本工具不能單獨證明的補測。

### 狀態規則

- `FAIL`：檔案存在但無法解析、request/lease 明確不成對、敏感資料命中、pool 狀態違規或 writer 資源驗證失敗。
- `WARN`：某檔不存在、沒有足夠事件、時間窗不重疊、發現 dropped/timeout/slow/N+1 或只看到部分格式。
- `PASS`：該章節有足夠證據且沒有明確違規。三檔缺任一檔時總結不可為完整 PASS。

## 錯誤處理與生命週期

- Trace 設定錯誤不得退回硬編碼使用者路徑並偷偷寫檔；應關閉檔案追蹤並留下 console 診斷。
- 分析器對每個檔案獨立捕捉錯誤，單檔失敗仍產生含失敗原因的總報告。
- 所有 `StreamReader`、`FileStream`、writer、listener、CTS 與 background task 都由單一 owner 持有並在 `finally`/`Dispose` 確定釋放。
- 不使用 unbounded dictionary 保存每個訊息、traceId 或 leaseId。Dataverse 配對集合有明確最大容量與超限 WARN；一般文字只保留 bounded 聚合。

## 測試與證據

- `DiagnosticTraceOptions`：預設停用、目錄解析、固定檔名、無效路徑 fail closed。
- `DataverseTraceOptions`：集中 options 停用時不建立 task/file；Debug enabled 時使用集中路徑。
- `NullToolUtilityTracer`：任何寫入均無檔案與 listener 副作用。
- `FileToolUtilityTracer`：enabled Debug 寫入、不增加全域 listener、低層級不開檔、Dispose 冪等。
- PowerShell fixture：三檔正常、缺檔、壞 JSON、lease 缺 return、Perf/N+1/gap 與 Big5 legacy error。
- Debug build + enabled/disabled 實跑；Release build + 惡意 `Enabled=true` 實跑；Release DLL/原始碼靜態掃描。
- 真實三檔分析產生報告，並監看程序停止後檔案大小不再增加、handle 可重新獨佔開啟。

## 相容性與遷移

- Development 的 `Dataverse:Trace`、`EnableTrace`、`Profiling:Enabled` 會移除，避免多來源真相。
- 檔案格式維持不變，既有 Dataverse JSONL 與效能解析規則仍可用。
- 既有 `Diagnose-DataverseTrace.ps1` 保留作 Dataverse 專項工具；新的整合分析器可重用其規則，但不修改該未提交檔案，以避免覆寫使用者工作。
- 不移動或刪除 `D:\dataverse-trace` 舊資料；新啟動後只寫 `D:\除錯追蹤`。

## 回復策略

- 若集中設定接線造成啟動問題，先將 Debug `DiagnosticsTrace:Enabled=false`，主流程應仍可啟動。
- 程式回退時不刪除任何 Trace 或報告檔；所有輸出均為可保留的診斷證據。
