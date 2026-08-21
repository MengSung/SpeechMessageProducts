ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: perf-crm-attribution-and-switch-decouple-final

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
請審查目前工作樹中本任務的完整實作與未提交修正，重點包括：

1. `AmbientGatewayOrganizationService` 是否在有 request 時解析目前 scope 的完整 `IOrganizationService` 裝飾鏈，而不是繞過 decorator 直接取得 gateway。
2. 無 request 的 fallback scope 是否有明確唯一 owner、成功／例外都會 deterministic Dispose，且不保存 HttpContext、scope、lease、raw client、identity 或 tenant state。
3. `Startup` 的 Debug DI decorator、`TimedOrganizationService`、`RequestProfiler` 的資料流是否仍正確，Release 是否不編譯或註冊診斷型別。
4. 相關 regression tests 是否測到真實 DI 組合與 legacy Factory 路徑，測試替身是否忠實反映正式 DI 圖且沒有跨測試／跨 request 狀態洩漏。
5. 所有本次修改的 `.cs` 是否有完整可維護的繁體中文註解，並維持 UTF-8 無 BOM、CRLF、final CRLF。

審查範圍：
- 目前工作樹未提交 diff：`git diff`
- 本任務已在目前 HEAD 中的產品／測試實作（請排除 `.ccg/dual-model-runs/` 暫存審查產物）：
  `ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
  `ToolUtility.Dataverse.Tests/GatewayArchitectureTests.cs`
  `ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs`
  `SpeechMessageProducts.ChurchReport/Startup.cs`
  `SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/TimedOrganizationService.cs`
  `SpeechMessageProducts.ChurchReport/Diagnostics/SessionDiagnosticsSwitch.cs`
  `ToolUtility/Diagnostics/DiagnosticTraceOptions.cs`
  `ToolUtility.Dataverse.Tests/DiagnosticTraceOptionsTests.cs`
  `ToolUtility.Dataverse.Tests/StartupOrganizationServiceProfilingTests.cs`
  `SpeechMessageProducts.ChurchReport/appsettings.json`
  以及已刪除的 `SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/TimedToolUtilityProvider.cs`

請先讀取 `AGENTS.md`、`.trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/{prd,design,implement}.md`，再輸出 `Critical/Warning/Info` 分級報告。每一項 finding 都必須以實際程式碼資料流為依據，不要把任務明確排除的 `Line.Messaging/LineMessagingClient.cs:840` 當成本任務問題。


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