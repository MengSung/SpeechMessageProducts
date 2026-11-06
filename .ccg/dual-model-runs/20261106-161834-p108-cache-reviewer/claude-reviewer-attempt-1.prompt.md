ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p108-cache

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
唯讀審查P1-08正式計畫與本次實作（目前已存在本地commit b74a311d，不能只看git diff空差異；請直接讀檔，不修改、不spawn）。正式02的P1-08及AGENTS.md為依據。
範圍：SpeechMessage.Localization/{IChurchTextOverrideSource,ChurchTextOverrideValidator,NamedTextFormatter}.cs、ToolUtility.Core/Localization/D365ChurchTextOverrideSource.cs、ToolUtility/Localization/DataverseChurchTextOverrideServiceFactory.cs、SpeechMessage.Tenancy/{ChurchTextOverrideCache,OverrideServiceCollectionExtensions}.cs及對應新增Tests（含ChurchReport.MemberInfo.Tests/LineSharedWorkflow/OverrideCacheExceptionTests.cs）。
核對：完整organization/product/church隔離、已授權TextContext、正式ID、完整分頁、整候選驗證後發布、錯候選保留舊、UTC stamp/generation、同scope single-flight、2000容量/30分閒置/10分重讀/64 flight、caller取消隔離、Disable及Dispose/drain、timer/registration/lease生命週期、錯誤傳真Exception至共用入口不洩密、雙目標net462/net10。
重要已修故障：壞cancel callback會拋AggregateException；deadline timer改安全callback並DisposeAsync drain；Lazy與timer抑制caller ExecutionContext。共享工作Task.Yield會擷取caller SynchronizationContext造成百scope弱參照48/200仍保留，新增排程計數真紅0期望/2實際後改CompletedTask.ConfigureAwait(ForceYielding)；現75/75與兩案五輪10/10通過。未放寬原弱參照斷言。source/adapter24案、validator20每宿主，全方案17TRX exit0，coverage正在獨立收集（不得猜測未讀結果）。Mock僅離線契約，本包沒有D365實機操作。
輸出具體檔案位置的Critical/High/Warning/Info，避免把caller提供任意ID視為已授權；組合根必須先授權。不要把P2輪詢Hosting未實作誤作本包缺失。

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