# 統一 Trace 兩層保護與三檔綜合分析

## Goal

以 Release 編譯防線與集中設定開關統一控制三種 Trace，並提供同時分析 dataverse-trace.jsonl、Trace.log 與 CHURCH_REPORT_TRACE.TXT 的完整 PowerShell 報告工具。

## Goal

讓 ChurchReport 的三種診斷輸出受同一個設定區段管理，同時由 Release 編譯條件提供不可被部署設定繞過的第二層停用防線；開發人員只需控制一個檔案，即可在 Debug 除錯時開啟或關閉所有 Trace，正式 Release 不建立任何三種 Trace writer。

## Confirmed facts

- `dataverse-trace.jsonl` 由 `DataverseTraceOptions` 的 `Dataverse:Trace:Enabled` 與 `Path` 控制；目前 Development 設定仍指向 `D:\dataverse-trace`。
- `Trace.log` 的主要 `TextWriterTraceListener` 位於 `Program.cs` 的 `#if DEBUG`，但 `EnableTrace` 仍決定 `TraceLoggerProvider` 是否註冊。
- `CHURCH_REPORT_TRACE.TXT` 由 `FileToolUtilityTracer`／legacy `TraceLogger` 透過程序級 `System.Diagnostics.Trace` 寫入；目前共用追蹤資源的控制不完整，不能只依賴 Release/Debug 推論。
- 既有未提交工作目錄變更屬於本次架構工作的先前成果，不能覆寫或還原。
- 目前已有 Dataverse-only `Diagnose-DataverseTrace.ps1`；本次應擴充為三檔唯讀分析器，而不是再建立互相重複的第二個 Dataverse 腳本。

## Requirements

### Unified configuration

1. 以單一 `DiagnosticsTrace` 設定區段提供 `Enabled`、`Directory` 與三個檔名的集中預設；正式/預設組態必須是停用。
2. Debug 組態只有在 `DiagnosticsTrace:Enabled=true` 時才建立三種檔案的 writer；停用時不得開啟、建立或追加任何檔案。
3. Release 組態無論外部 appsettings、環境變數或其他設定是否誤設 `Enabled=true`，都必須強制停用三種檔案輸出並移除或不註冊任何 Debug-only listener/provider。
4. 三個輸出檔案預設位於 `D:\除錯追蹤`：`dataverse-trace.jsonl`、`Trace.log`、`CHURCH_REPORT_TRACE.TXT`；目錄需可由單一設定覆寫。
5. 相對目錄只能解析到應用程式 content root；絕不從 request、Session、使用者輸入或租戶輸入取得輸出路徑。
6. 停止/Dispose 必須先停止接受事件，再完成背景佇列 drain/flush，最後移除 listener、釋放 writer/stream；不得留下 timer、task、subscription 或檔案 handle。

### Unified analysis script

7. 新增一個 PowerShell `.ps1`，一次接受三個檔案（或一個共用目錄）並以唯讀、串流方式分析：檔案存在性、大小/時間、編碼/可讀性、JSONL 解析、事件統計、request/lease 成對、pool 健康/故障/回收、使用者假名與敏感資料表面掃描。
8. `Trace.log` 分析效能事件（`[Perf]`、`[Perf-N+1]`、`[Perf-Gap]`、`[Perf-Startup]`）的命中數、端點平均/最大耗時、CRM 次數/耗時、gap 熱點與慢端點。
9. `CHURCH_REPORT_TRACE.TXT` 分析 legacy ToolUtility 呼叫量、錯誤/例外線索、時間範圍、常見訊息與疑似敏感資料；不在主控台或報告重印敏感原文。
10. 最後輸出完整 Markdown 報告，包含檔案摘要、三檔個別結果、跨檔關聯結論、PASS/WARN/FAIL、限制與建議；報告寫入指定路徑且使用 UTF-8 without BOM。
11. 腳本錯誤碼：可證明的資料錯誤/解析錯誤為非零；資料不足但無直接違規可為 WARN 並以明確狀態輸出；不可把「沒有事件」誤報成 PASS。
12. 腳本不可刪除、清空、鎖定、旋轉或修改任何 Trace 原檔；必須能讀取正在被 append 的檔案而不依賴獨佔鎖。

## Acceptance Criteria

- [ ] `DiagnosticsTrace` 是三種 Trace 唯一的產品層設定入口，Development 可一處開啟、Production 預設關閉。
- [ ] Debug + Enabled=false：三個檔案不存在時，執行基本啟動/診斷路徑不會建立檔案。
- [ ] Debug + Enabled=true：三個檔案由同一目錄設定建立，且三種既有輸出仍可寫入。
- [ ] Release + 任意設定 Enabled=true：建置產物不建立三種 Trace writer；靜態與執行驗證均證明不寫入。
- [ ] 追蹤資源在正常停止、取消、writer 例外與佇列滿載路徑均有確定性釋放，且無跨 request/user/tenant 狀態留存。
- [ ] PowerShell 腳本可對三個真實 Trace 檔一次執行，產生完整 Markdown 報告，且不修改原檔、不因大檔案而無界增長記憶體。
- [ ] 腳本可正確辨識已知的 request begin/end 與 lease acquire/return 配對、Trace.log 效能摘要及 legacy Trace 的錯誤線索。
- [ ] 既有 Dataverse Trace 單元測試、ToolUtility 測試、ChurchReport Debug/Release build 與 Release 無痕驗證通過。
- [ ] 修改的 `.cs`/`.cshtml` 為 UTF-8 without BOM、CRLF、最終 CRLF；PowerShell/JSON/Markdown 也通過 UTF-8 without BOM 與換行檢查。

## Out of scope

- 不改 Dataverse pool 的連線、租約或 session isolation 演算法；只接入其既有 Trace 開關與分析證據。
- 不把 Trace 報告上傳外部服務、不新增網路連線、不自動刪除舊檔案。
- 不讓正式環境透過 runtime admin endpoint 動態開啟 Trace；需要除錯時由受控部署設定與重啟進入 Debug/診斷流程。

## Open questions

- None blocking. The requested policy is: Release is a hard fail-closed boundary; one `DiagnosticsTrace` section is the normal operator switch.

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
