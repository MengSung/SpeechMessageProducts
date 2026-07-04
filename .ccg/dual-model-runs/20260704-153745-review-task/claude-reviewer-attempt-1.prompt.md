ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# LINE RichMenu Shared Orchestrator Post-Fix Review

Review the current git diff in this worktree after the latest RichMenu fixes.

## Scope

- Branch/worktree: `Jesus_5.1.7.WorktreeRefactorRichMenu`
- Main shared project: `LineMessagingProcessor.RichMenus`
- Test project: `LineMessagingProcessor.RichMenus.Tests`
- ASP.NET Core registration project: `LineMessagingProcessor.AspNetCore`
- Product project: `ChurchReport`

## Architecture intent

The goal is to extract reusable LINE RichMenu behavior for future ASP.NET Core products.
The shared RichMenu core must stay product-neutral.
ChurchReport-specific CRM, Controller, DbContext, IActionResult, payment, and notification flows must remain outside `LineMessagingProcessor.RichMenus`.

## Key fixes already made

1. `LineRichMenuProvisioningWorkflow` no longer reopens the PNG stream and no longer uses sync-over-async.
2. `LineRichMenuFingerprint.BuildName(...)` now receives already-read bytes or a precomputed fingerprint.
3. `RichMenuOrchestrator` now has one public constructor.
4. Text-trigger behavior now goes through `LineRichMenuTextTriggerPolicy : IRichMenuPolicy`.
5. Removed the concrete-only `HandleTextAsync` path and removed `RichMenuTextContext` / `RichMenuTextDecision`.
6. `LineRichMenuTextTriggerResolver` now has one public constructor that accepts `LineRichMenuTextTriggerOptions`.
7. `LineMessagingProcessor.AspNetCore.Tests` fake RichMenu processor was updated to match `ILineRichMenuProcessor`.
8. RichMenu success return strings in ChurchReport utility code were changed from mojibake to a clear success string.

## Review checklist

Classify findings as Critical / Warning / Info.

Critical:
- Build or test breakage.
- DI ambiguity or invalid service registration.
- Product-specific dependencies leaking into `LineMessagingProcessor.RichMenus`.
- RichMenu workflow leftovers in `LineMessagingProcessor.Workflows`.
- Reintroduced sync-over-async or duplicate PNG stream reads.
- Reintroduced old text-trigger special path (`HandleTextAsync`, `RichMenuTextContext`, `RichMenuTextDecision`).
- Reintroduced outdated test-only types such as `RichMenuResponse`, `RichMenuAliasResponse`, or `LineRichMenuOptions`.

Warning:
- Shared abstractions that are confusing or likely to cause future product integration problems.
- In-memory cache/state store documentation that could mislead future products into treating memory as durable storage.
- Gaps in provisioning, assignment, text trigger, DI registration, or boundary tests.

Info:
- Naming, readability, and maintainability suggestions.
- Small improvements that are not required before merge.

## Verification already run after fixes

- `dotnet test LineMessagingProcessor.RichMenus.Tests\LineMessagingProcessor.RichMenus.Tests.csproj -v minimal`
  - Passed: 13
- `dotnet test LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal`
  - Passed: 4
- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal`
  - Passed: 33
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineSharedWorkflow|FullyQualifiedName~PushUtilityWorkflow" -v minimal`
  - Passed: 28
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`
  - Passed: 0 warnings, 0 errors
- Boundary scan:
  - `LineMessagingProcessor.RichMenus` has no ChurchReport / CRM / Controller / DbContext / IActionResult references.
  - `LineMessagingProcessor.Workflows` has no RichMenu workflow leftovers.
- Legacy scan:
  - No `LineRichMenuOptions`, `RichMenuResponse`, `RichMenuAliasResponse`, `HandleTextAsync`, `RichMenuTextDecision`, `RichMenuTextContext`, `.GetAwaiter().GetResult()`, or `PngImageStreamFactory(CancellationToken.None)` remains in the reviewed RichMenu areas.
- Generated folders:
  - `bin/`, `obj/`, and `artifacts/` were cleaned after verification.

## Output

Return:
1. Critical findings, or explicitly state "No Critical findings".
2. Warning findings.
3. Info findings.
4. A merge recommendation.

</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.