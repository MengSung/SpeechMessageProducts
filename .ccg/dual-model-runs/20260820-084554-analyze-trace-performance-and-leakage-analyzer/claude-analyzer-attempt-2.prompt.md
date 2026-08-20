ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: analyze-trace-performance-and-leakage

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# 角色

請以資深 ASP.NET Core／Dataverse 效能、Session 隔離與資源生命週期審查者身分，對現有證據做唯讀分析，不修改任何程式碼。

# 任務

評估 ChurchReport 目前的 Trace 與程式架構，在效能、Session Leakage、Memory／Resource Leakage 及可觀測性方面是否需要改善。請特別檢查：

1. Dataverse connection pool 的 acquire、client creation、return、fault eviction、dispose 是否有逾時、鎖競爭或資源滯留風險。
2. request／lease／CallerId／user scope 是否有跨使用者或跨 request 狀態外洩的證據或盲點。
3. 現有 RequestProfiler、dataverse JSONL、Trace.log 與分析報告的數據是否一致且可信。
4. 是否需要新增 Trace；若需要，請只建議可量化、可關聯、低敏感度且有界的事件或指標，避免大量逐請求文字紀錄。

# 已知資料

- `D:\除錯追蹤\ChurchReport-Trace-Report.md`
- `D:\除錯追蹤\dataverse-trace.jsonl`
- `D:\除錯追蹤\Trace.log`
- `D:\音訊科技產品\系統平台\SpeechMessageProducts\docs\architecture\dataverse-architecture-final-v2.png`
- `ToolUtility/Dataverse/BoundedClientPool.cs`
- `ToolUtility/Dataverse/DataverseGateway.cs`
- `ToolUtility/Dataverse/DataverseConnectionManager.cs`
- `ToolUtility/Dataverse/PooledClient.cs`
- `SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/RequestProfiler.cs`
- `SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1`

# 輸出格式

請輸出 Critical／Warning／Info 分級報告。每項都要包含：觀察證據、根因或證據缺口、風險、建議修正方向。不得把「未觀察到」誤寫成「已證明不存在」。


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