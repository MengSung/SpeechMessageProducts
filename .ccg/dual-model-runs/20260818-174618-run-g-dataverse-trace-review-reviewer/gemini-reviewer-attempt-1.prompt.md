ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: run-g-dataverse-trace-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# Run G Dataverse Trace — final review

Review the uncommitted Run G diff only; do not edit.

The allowed source scope is exactly: `ToolUtility/Dataverse/DataverseTrace.cs` (new), `DataverseTraceMiddleware.cs` (new), `PooledClient.cs` (stable `c-N` ClientId/status reads only), `BoundedClientPool.cs` (observational calls only), `DataverseGateway.cs` (observational calls only), `GatewayOrganizationService.cs` and `AmbientGatewayOrganizationService.cs` (`crm.op` only), `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs`, `SpeechMessageProducts.ChurchReport/Startup.cs` (one fully qualified middleware registration after authentication), `appsettings.Development.json` (Trace section only), `ToolUtility.Dataverse.Tests/DataverseTraceTests.cs` (new), `ToolUtility/ToolUtility.csproj` (ASP.NET Core framework reference and its warning compatibility setting), and the Run G task notes. Controllers and Production config must not change.

Run F semantics are immutable: CallerId clearing and fault eviction, leased disposal deferral, MinSize protection, fail-fast config. Run G must be observability only. Validate exact JSONL schema and all T1–T7 requirements: disabled cost/no allocations, privacy HMAC user pseudonym, 64MB/5-file queue writer/drop behavior, per-request and lease AsyncLocal isolation, `pool.dispose` state timed at the attempt, pre-clear CallerId field, hit/miss/return correlation, nested gateway acquisition, and faulted return. Check resource cleanup, cross-host isolation, concurrency/races, scope, Traditional Chinese docs/CRLF and no DEBUG/Trace.Listeners/AutoFlush.

Important evidence already observed: `dotnet build SpeechMessageProducts.sln -c Debug --no-restore` succeeds with 0 warnings / 0 errors; `ToolUtility.Tests` has 63 passed; `ToolUtility.Dataverse.Tests` has 36 passed; MemberInfo remains 22 failed / 305 passed. Assess the actual current code, not these assertions.

Return Critical / Warning / Info with file:line evidence. A Critical must be a real release-blocking correctness, isolation, lifecycle, scope, or required-contract defect; distinguish any false positive caused by the explicit raw pre-clear CallerId schema requirement.


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