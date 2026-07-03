# PushUtility Reliable Required Call-Site Cleanup Review

## Scope

- Added `PushUtility.SendReliableMessageAsync(...)` for required LINE text notifications that must preserve retry-key semantics.
- Routed ATM / transfer payment instructions through `SendReliableMessageAsync(...)` instead of the older required text path.
- Kept ChurchReport CRM, payment, donation, and MVC decisions inside ChurchReport.
- Kept shared LINE projects product-agnostic.
- Preserved legacy best-effort `PushUtility.SendMessage(...)` behavior.

## External Review

### Gemini

- Result: reviewed with actionable findings.
- Findings addressed:
  - Added constructor null guard for `PushUtility(LineMessagingClient, ILineNotificationWorkflow?)`.
  - Added ATM order / virtual-account defensive validation before building the LINE retry key.
  - Hardened `BuildAtmPaymentLineRetryKey(...)` for empty fee id and empty ATM account.
  - Improved the new test to assert `LineNotificationException.Result` status, error code, error message, and retry key.
  - Replaced the new XML documentation with stable readable English comments to avoid encoding ambiguity.
- Raw output: `.ccg/tasks/line-pushutility-reliable-required-callsite-cleanup/gemini-review.raw.md`

### Claude

- Result: wrapper/tooling failure before useful findings.
- Blocking: no. User previously authorized continuing when Claude review is unavailable and local validation plus Gemini review evidence are sufficient.
- Raw output: `.ccg/tasks/line-pushutility-reliable-required-callsite-cleanup/claude-review.raw.md`

## Local Validation

Fresh validation after review fixes:

- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter PushUtilityWorkflowTests`
  - Passed: 5 / 5
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter "DonationPaymentProcessor|DonationPaymentGateway|PushUtilityTests"`
  - Passed: 18 / 18
- `dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false`
  - Passed: 33 / 33
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`
  - Passed: 0 warnings / 0 errors

## Boundary Check

Command:

```powershell
rg -n "ChurchReport|Microsoft\.Xrm|Controller|IActionResult|DbContext" LineMessagingProcessor LineMessagingProcessor.Workflows LineMessagingProcessor.AspNetCore --glob "*.cs" --glob "*.csproj"
```

Result:

- Only existing comment-only references to `ChurchReport` were found in `LineMessagingProcessor\LineMessagingProcessorClass.cs`.
- No new runtime dependency from shared LINE projects back to ChurchReport was introduced.

## Decision

Approved for commit. Gemini findings were addressed and local validation passed.
