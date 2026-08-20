ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: unified-trace-guard-and-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# 雙模型架構分析請求：ChurchReport Trace 兩層保護與三檔綜合分析

## 角色

請以資深 .NET 10 / ASP.NET Core 效能、資源生命週期、安全隔離與 PowerShell 診斷工具架構師身分分析。只做唯讀分析，不修改檔案。

## 使用者需求

1. 三種 Trace 統一建立在 `D:\除錯追蹤`：
   - `dataverse-trace.jsonl`
   - `Trace.log`
   - `CHURCH_REPORT_TRACE.TXT`
2. 採兩層保護：Release 是不能被設定誤開的硬性防線；Debug 再用單一設定區段日常啟停。
3. 建立一個 PowerShell 分析器，同時分析三檔的效能、Dataverse request/lease/pool 管理、legacy ToolUtility trace，並產生完整 Markdown 報告。
4. 零容忍 Session/cross-user leakage、memory/resource leakage；大檔分析不得無界載入記憶體。

## 已確認現況

- `SpeechMessageProducts.ChurchReport/Program.cs` 的 `Trace.log` listener 在 `#if DEBUG`，但目前 Debug 無條件建立。
- `Startup.cs` 使用獨立 `EnableTrace` 註冊 `TraceLoggerProvider`，並無條件註冊 `FileToolUtilityTracer` singleton。
- `ToolUtility/Dataverse/DataverseTrace.cs` 從 `Dataverse:Trace` 讀 Enabled/Path；enabled 時啟動 singleton background writer。
- `FileToolUtilityTracer` 預設硬編碼 `D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT`，lazy 建立 stream/writer/global listener。
- legacy `ToolUtility/Diagnostics/TraceLogger.cs` 也有同一路徑且可直接建立 writer。
- `appsettings.Development.json` 目前 `Dataverse:Trace:Enabled=true` 且指向 `D:\dataverse-trace`；`Profiling:Enabled=false`。
- `appsettings.json` 與 Production 使用 `EnableTrace=false`，控制來源分散。
- 現有 `Diagnose-DataverseTrace.ps1` 只分析 JSONL，現有 `parse-perf-log.ps1` 只分析 `[Perf]`。
- 真實 legacy trace 約 60MB，分析器需串流與 bounded aggregation。

## 建議設計

- 單一 `DiagnosticsTrace`：`Enabled` + `Directory`；檔名固定。
- `DiagnosticTraceOptions` 為不可變程序級設定；Release 組合根強制建立 disabled instance，不讀取 Enabled=true。
- `Program.cs` Debug + enabled 才建立 `Trace.log` listener。
- `Startup.cs` Debug + enabled 才註冊 `FileToolUtilityTracer`、`TraceLoggerProvider`、Profiling；否則 `NullToolUtilityTracer`。
- `AddToolUtility()` 預設 `NullToolUtilityTracer`，讓未明確 opt-in 的其他產品 fail closed。
- `DataverseTraceOptions` 從集中 options 取得 enabled/path，不再直接讀 `Dataverse:Trace`。
- legacy `TraceLogger` Release 路徑編譯期 no-op，避免直接 new 繞過 DI。
- PowerShell 使用 FileShare.ReadWrite、逐行讀取、敏感資料遮蔽、bounded top-N/配對集合，產出 PASS/WARN/FAIL Markdown。

## 請分析

請各自輸出：

1. Critical / Warning / Info 分級風險。
2. 是否真的能保證 Release 設定誤開仍不產生三檔。
3. `System.Diagnostics.Trace.Listeners` 共用造成的重複輸出、資源所有權或競態風險。
4. 最小且可測試的集中設定/DI 設計，指出不必要複雜度。
5. PowerShell 大檔串流、Big5/UTF-8、正在 append、資料配對與敏感資訊報告的陷阱。
6. 必須先寫的測試與 Release 實證命令。
7. 對 `.trellis/tasks/08-19-unified-trace-guard-and-analysis/design.md` 的具體修訂建議。

OUTPUT: Traditional Chinese. Provide actionable findings with exact file/symbol references where possible. Do not output repository secrets or raw trace contents.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.