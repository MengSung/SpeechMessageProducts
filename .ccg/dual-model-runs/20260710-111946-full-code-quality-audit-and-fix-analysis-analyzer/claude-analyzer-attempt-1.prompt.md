ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: full-code-quality-audit-and-fix-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.0.Initialization.Worktree

## Request
# Full Code Quality Audit And Fix - Analysis Request

## User Objective

完整仔細深入研究全部程式有什麼缺點，例如 Session Leak、速度慢、Memory Leak 等等，然後修復到好為止。

## Repository Context

- Worktree: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.0.Initialization.Worktree`
- Branch: `1.0.0.0.Initialization.Worktree`
- Solution: `SpeechMessageProducts.sln`
- Scale: 18 solution projects, 827 C# files
- Main stack: .NET 10 solution with ASP.NET Core ChurchReport app, LINE Messaging SDK/processor, RichMenus, Payments, ToolUtility, Dataverse client, Trace library, and multiple xUnit test projects.
- Existing docs already mention memory/performance/session issues under:
  - `SpeechMessageProducts.ChurchReport/文件/記憶體優化/`
  - `SpeechMessageProducts.ChurchReport/文件/效能優化計畫/`
  - `docs/superpowers/plans/2026-07-06-session-leakage-complete-fix.md`

## Initial Local Signals

- `Startup.cs` heavily configures session, cookies, cache, auth, no-cache headers, session validation, session monitoring, identity audit, CRM connection pooling, and many scoped/singleton services.
- `SessionAttribute.cs` appears to store `SessionId` as an instance field in an action filter type, which may be unsafe depending on filter lifetime.
- `SessionValidationMiddleware.ClearSessionAndRedirectToLogin` calls `context.Session.CommitAsync().GetAwaiter().GetResult()`, creating sync-over-async risk.
- Some production code still has sync-over-async or blocking patterns, including:
  - `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs`
  - `ToolUtility/PushUtility.cs`
- `Line.Messaging/LineMessagingClient.cs` and `Line.Messaging/Liff/LiffClient.cs` contain internal `new HttpClient()` paths. Some are documented as compatibility paths, but need validation for leak/socket exhaustion risk.
- `InMemoryDataContextSmallGroup.cs` has many per-session `IMemoryCache` entries and explicit comments about previous Session Bleeding fixes; verify cache lifetime, key isolation, eviction, and bounded growth.
- Existing memory/performance docs appear partly aspirational and partly completed; verify current code instead of trusting docs.

## Required Analysis Output

Please perform read-only analysis. Do not modify files.

Return a concise but concrete report with:

1. Top 10 highest-risk defect clusters likely to cause Session Leak, Memory Leak, slow requests, deadlocks, socket exhaustion, unbounded cache growth, or cross-user data leakage.
2. For each cluster, cite specific files/classes/methods and explain the evidence.
3. Distinguish verified issues from hypotheses that need runtime profiling or load testing.
4. Recommend a phased implementation strategy that can be safely executed in this repo:
   - Phase A: deterministic static/code fixes with focused tests
   - Phase B: broader refactors requiring integration tests
   - Phase C: runtime profiling/load validation requiring app execution
5. Identify the first narrow fix batch that offers the best risk reduction with manageable blast radius.
6. List validation commands and test cases needed before claiming improvement.
7. Call out false-positive traps: places where existing comments/docs say an issue was already intentionally handled.

Use Critical / Warning / Info severity. Prefer actionable findings over generic advice.


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