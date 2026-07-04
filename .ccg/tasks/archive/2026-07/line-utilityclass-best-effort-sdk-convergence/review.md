# Review: LineUtilityClass Best-Effort SDK Message Convergence

## Scope

This slice adds an optional shared LINE notification workflow route to `ChurchReport.Tools.LineUtilityClass` without removing the legacy API or changing reply, multicast, rich menu, CRM parsing, or synchronous fire-and-forget methods.

## Changed Behavior

- Added optional `ILineNotificationWorkflow` support to `LineUtilityClass`.
- Centralized safe user-push SDK message sending in `SendBestEffortSdkMessagesAsync(...)`.
- Converted safe async push methods to use the shared workflow when injected, with fallback to `LineMessagingClient.PushMessageAsync(...)` when no workflow is present.
- Kept ChurchReport product-specific push statistics in ChurchReport through `ToolUtilityClass.CreatePushLineMessage` / testable delegate injection.
- Added an explicit comment documenting the current multi-organization token switching boundary: `SetupChannelAccessToken(...)` rebuilds only the local LINE client, so workflow injection for multi-org callers must wait until the workflow layer supports equivalent token routing.

## External Review

- Gemini review: completed, no Critical findings. Warning: document multi-organization token switching interaction with workflow and improve coverage for non-text methods.
- Claude review: completed, no Critical findings. Warning: same multi-organization workflow boundary and source-label coverage.
- Actions taken:
  - Added source-label coverage for representative safe methods, including image/video/audio/location/sticker/template/flex/confirm/imagemap.
  - Added product statistics delegate coverage so ChurchReport CRM/statistics semantics remain outside shared LINE projects.
  - Added code comment for the multi-organization workflow boundary.

## Validation Evidence

- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter LineUtilityClassWorkflowTests`
  - Passed: 3/3
  - Existing unrelated warning remains: `MemberInfoScopeGuardTests.cs(33,17): warning xUnit1012`
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter PushUtilityWorkflowTests`
  - Passed: 10/10
- `dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false`
  - Passed: 33/33
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`
  - Build succeeded: 0 warnings, 0 errors
- Shared LINE boundary check found only existing comment references to ChurchReport in `LineMessagingProcessorClass.cs`; no ChurchReport product dependency was added to shared LINE projects.

## Remaining LINE Call-Site Work

- `LineUtilityClass` still has intentionally retained direct LINE SDK paths for reply, multicast, rich menu, and synchronous legacy fire-and-forget methods.
- Some product code still constructs `PushUtility` without workflow; this is compatible fallback, not yet full DI convergence.
- Poll/error-reporting paths still instantiate `LineMessagingProcessorClass` directly and should be considered in a later product-level cleanup slice.