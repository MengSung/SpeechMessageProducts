ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: run-g-dataverse-trace-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# Run G Dataverse Trace — implementation analysis

Review the currently uncommitted Run G work in this repository. Do not edit files.

Scope whitelist: `ToolUtility/Dataverse/DataverseTrace.cs` (new), `DataverseTraceMiddleware.cs` (new), `PooledClient.cs` (ClientId/state reads only), `BoundedClientPool.cs` (trace calls only), `DataverseGateway.cs` (trace calls only), `GatewayOrganizationService.cs` and `AmbientGatewayOrganizationService.cs` (`crm.op` calls only), `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs`, `SpeechMessageProducts.ChurchReport/Startup.cs` (one middleware registration line), `appsettings.Development.json`, `ToolUtility.Dataverse.Tests/DataverseTraceTests.cs` (new), and `ToolUtility/ToolUtility.csproj` (FrameworkReference only). No architecture document or production appsettings change is allowed.

Run F behavior is immutable: clearing CallerId before healthy return with fail-closed eviction, leased-state deferred disposal, cleanup MinSize preservation, and fail-fast missing server/user config. Run G is observation only; it must not alter semantic behavior.

Required contract: disabled trace one bool read/branch and no allocations; UTF-8-no-BOM private StreamWriter with background ConcurrentQueue, rotation <=64MB and retain <=5, nonblocking overflow with `trace.dropped`, no Trace.Listeners or AutoFlush; in-memory random HMAC salt and privacy-safe `u_` pseudonyms; exact JSONL event schema stated in DataverseTraceTests / current task; stable PooledClient `c-N`; AsyncLocal request/lease correlation restored on scope dispose; middleware immediately after UseAuthentication; all docs Traditional Chinese; Run F 29 tests remain green and T1–T7 validate actual wiring.

Analyze source and tests critically. Report (1) likely compile/API errors, (2) semantic or privacy/lifecycle defects, (3) missing instrumentation / exact safe insertion locations, (4) scope violations. Categorize Critical / Warning / Info with file:line evidence. Do not suggest expanding scope.


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