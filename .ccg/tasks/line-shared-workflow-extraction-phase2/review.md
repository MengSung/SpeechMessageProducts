# LINE Shared Workflow Extraction Phase 2 Review

## Review Scope

Implemented reusable LINE shared workflow extraction Phase 2:

- Added `LineMessagingProcessor.Workflows` shared notification request/result/workflow project.
- Added `LineMessagingProcessor.AspNetCore` DI/options integration project.
- Routed `PaymentNotificationService.SendLineMessage(...)` through `ILineNotificationWorkflow`.
- Added optional workflow-backed text path to `PushUtility.SendMessage(string, string)` while keeping SDK fallback.
- Routed `MemberInfoController` profile refresh lookup through `LineMessagingProcessorClass` instead of direct `LineMessagingClient` construction for that path.

## External Review

### Gemini reviewer

Status: completed.

Findings:

- Critical: none reported.
- Warning: `LineNotificationRecipient.Users(...)` could silently send only the first user because `LineNotificationWorkflow` used `PrimaryId`.
- Info: `PaymentNotificationService.SendLineMessage(...)` remains sync-over-async because the public ChurchReport API is still synchronous.

Resolution:

- Fixed the `Users(...)` warning by explicitly rejecting multi-user recipients in `LineNotificationWorkflow.Validate(...)` with `ValidationFailed` and error code `line-recipient-users-not-supported`.
- Added regression test `SendAsync_rejects_multi_user_recipient_instead_of_sending_only_first_user`.
- Left the synchronous `SendLineMessage(...)` public API in place to avoid widening ChurchReport product workflow changes in this phase.

### Claude reviewer

Status: tool failure.

`codeagent-wrapper.exe --lite --backend claude` exited with status 1 before returning review findings. Per user instruction on 2026-07-03, Claude review failure is non-blocking for this task when Gemini review and local validation pass.

## Local Verification

Passed:

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet test LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false
```

Boundary checks:

- `LineMessagingProcessor.Workflows` and `LineMessagingProcessor.AspNetCore` have no code references to ChurchReport, CRM, controllers, DbContext, payment projects, or LinePayCSharp.
- `MemberInfoController` no longer directly constructs `LineMessagingClient(token)` for the profile refresh lookup path.
- Touched text files were normalized to UTF-8 without BOM and CRLF.

## Result

Approved to proceed to commit. No open Critical findings remain.