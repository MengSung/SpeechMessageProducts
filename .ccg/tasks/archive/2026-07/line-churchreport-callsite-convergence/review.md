# LINE ChurchReport Call-Site Convergence Review

## Scope

- Route ChurchReport payment-priority LINE push paths through `ILineNotificationWorkflow` where a workflow is injected.
- Preserve legacy best-effort behavior for non-critical `PushUtility.SendMessage` calls.
- Keep CRM, payment, donation, MVC, and ChurchReport-specific business flow inside ChurchReport.
- Avoid introducing any ChurchReport product dependency into shared LINE workflow projects.

## Gemini Review

- Result: passed.
- Critical: none reported.
- Warning: none reported.
- Summary: Gemini confirmed the diff keeps ChurchReport product logic outside shared LINE projects, preserves required-vs-best-effort semantics, and adds focused tests for workflow routing and failure propagation.

Raw output:

- `.ccg/tasks/line-churchreport-callsite-convergence/gemini-review.raw.md`

## Claude Review

- Result: tooling failure.
- Blocking: no, per user instruction that Claude quota/tooling failures may be ignored when Gemini review and local validation pass.
- Observed failure: `claude exited with status 1` from `codeagent-wrapper`.

Raw output:

- `.ccg/tasks/line-churchreport-callsite-convergence/claude-review.raw.md`

## Local Validation Required Before Commit

Run fresh validation after this review file is written:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter PushUtilityWorkflowTests
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false
```

## Boundary Check

The shared LINE workflow projects must remain product-agnostic. Any `ChurchReport` text found in shared LINE code should be comment-only and must not create a product reference or runtime dependency.
