# Review: PushUtility Best-Effort SDK Message Convergence

## Scope

This slice routes safe ChurchReport `PushUtility` best-effort SDK message methods through `ILineNotificationWorkflow.SendAsync(...)` when a workflow is injected, while preserving the legacy behavior of swallowing failures for non-critical notifications.

## Changed Behavior

- Centralized best-effort SDK-message routing in `PushUtility.SendBestEffortSdkMessagesAsync(...)`.
- Converted these safe best-effort methods to the shared workflow path when available:
  - `SendMessage(string, List<ISendMessage>)`
  - text-message fallback inside `SendMessage(string, string)`
  - `SendImage`
  - `SendVideo`
  - `SendAudeo`
  - `SendLocation`
  - `SendSticker`
  - `PostSerializedTemplate`
  - `PostSerializedConfirm`
  - `PostSerializedImageMap`
- Preserved legacy fallback to `LineMessagingClient.PushMessageAsync(...)` when no workflow is injected.

## External Review

- Gemini review rerun: completed. It raised a stale/over-conservative note about `SendAudeo` vs `SendAudio` metadata naming. Current code and tests intentionally use the cleaner source label `ChurchReport.PushUtility.SendAudio` while keeping the legacy method name for API compatibility.
- Claude review rerun: completed. It found no Critical issues. It raised one Warning that the `_lineNotificationWorkflow == null` fallback branch lacked test coverage.
- Action taken: added `SendImage_uses_legacy_line_client_when_workflow_is_not_provided`, which records the outgoing LINE push request and verifies URL, authorization, recipient, message type, original URL, and preview URL.

## Validation Evidence

- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter PushUtilityWorkflowTests`
  - Passed: 10/10
  - Existing unrelated warning remains: `MemberInfoScopeGuardTests.cs(33,17): warning xUnit1012`
- `dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false`
  - Passed: 33/33
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`
  - Build succeeded: 0 warnings, 0 errors
- Shared LINE boundary check:
  - `rg -n "ChurchReport|Microsoft\.Xrm|Controller|IActionResult|DbContext" LineMessagingProcessor LineMessagingProcessor.Workflows LineMessagingProcessor.AspNetCore --glob "*.cs" --glob "*.csproj"`
  - Only existing comment references to ChurchReport were found in `LineMessagingProcessorClass.cs`; no product dependency was added to shared LINE projects.

## Remaining LINE Call-Site Work

This slice does not complete the whole ChurchReport LINE convergence objective. Remaining direct push surfaces include `ChurchReport\Tools\LineUtilityClass.cs` and some intentionally retained `PushUtility` fallback/rich-menu/synchronous legacy paths. These should be handled in separate, smaller slices.