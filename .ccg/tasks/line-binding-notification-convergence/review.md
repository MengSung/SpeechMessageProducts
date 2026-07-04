# Review Notes

## Implemented

- Added `ChurchReportLineBindingNotificationService` behind `IChurchReportLineBindingNotificationService`.
- Kept ChurchReport-specific binding URL and prompt text in ChurchReport.
- Replaced `SmallGroupController.LineLogin` direct `new LineMessagingProcessorClass()` and `NotifyLineBinding(...)` call with the service.
- Registered the service in `Startup.ConfigureServices`.
- Added focused tests for binding URL shape, workflow request content, metadata, and failure propagation.
- Fixed malformed string literals in `BaseChurchController.cs` that blocked ChurchReport build/test execution.
- Routed `LineUtilityClass.MultiCastTextMessageAsync` through workflow when injected by splitting multicast recipients into one workflow request per user.
- Routed legacy sync `LineUtilityClass.SendMessage(string, string)` through the same best-effort workflow helper when injected.
- Added product-agnostic LINE reply-token workflow types in `LineMessagingProcessor.Workflows`: `ILineReplyWorkflow`, `LineReplyRequest`, `LineReplyResult`, `LineReplyException`, and `LineReplyWorkflow`.
- Added `LineMessagingProcessorClass.ReplyMessagesAsync(...)` as the SDK-backed processor adapter for reply-token calls.
- Registered `ILineReplyWorkflow` in `LineMessagingProcessor.AspNetCore`.
- Routed `ReplyUtility` text, SDK-message, confirm-template, imagemap, sticker, and echo reply paths through `ILineReplyWorkflow`.
- Passed `ILineReplyWorkflow` from `LineUtilityClass` into `ReplyUtility` and routed `LineUtilityClass.ReplyImage(...)` through `ReplyUtility`.
- Routed `PushUtility` rich-menu completion and legacy sync confirm/carousel push paths through the existing best-effort workflow helper when a workflow is injected.
- Routed `LineUtilityClass.AddRichMenuMessage(...)` completion push through the existing best-effort workflow helper when a workflow is injected.
- Added DI-friendly `ILineReplyWorkflow` constructor wiring for `DonationFeePaymentProcessor` and `RecurringDonationPaymentProcessor`; no-argument legacy constructors remain constructor-compatible and create workflow-backed helpers internally.

## Verification

Passed:

```powershell
dotnet build ChurchReport\ChurchReport.csproj -m:1 -v minimal -p:UseSharedCompilation=false
```

Result: build succeeded with 0 warnings and 0 errors.

Passed:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter "ChurchReportLineBindingNotificationServiceTests|LineUtilityClassWorkflowTests"
```

Result: 8 passed.

Passed:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter "LineUtilityClassWorkflowTests|PushUtilityWorkflowTests|ReplyUtilityGroupRoomProfileAdapterTests|ChurchReportLineBindingNotificationServiceTests"
```

Result: 26 passed. Existing warning remains in `MemberInfoScopeGuardTests.cs` for nullable xUnit data and is unrelated to this LINE convergence slice.

## Remaining LINE Surfaces

- `ReplyUtility`: active reply sending now flows through `ILineReplyWorkflow`; scan hits for `ReplyMessageAsync(...)` are comments or calls into `ReplyUtility`, not direct product SDK calls.
- `PushUtility`: active push sending now flows through `ILineNotificationWorkflow`; required/reliable notifications use `SendReliableMessageAsync(...)` and preserve retry metadata.
- `LineUtilityClass`: active push/reply helper paths now delegate to workflow-backed `PushUtility` / `ReplyUtility`; the remaining `ReplyMessageAsync(...)` hit is a wrapper call that resolves through `ILineReplyWorkflow`.
- `ChurchReportLineAdminNotificationService` and `PaymentNotificationService`: static default factories still construct `LineNotificationWorkflow(new LineMessagingProcessorClass(...))`; acceptable for legacy non-DI callers, but removable after all call sites become DI-backed.
- Comment-only QR utility hits remain false positives and do not execute LINE SDK calls.

## Push / Reply Call Classification

- Converted: `ReplyUtility` group and room profile lookup uses the processor adapter (`GetGroupMemberProfileAsync`, `GetRoomMemberProfileAsync`).
- Converted: `PushUtility` required text, required SDK-message, and reliable text paths use `ILineNotificationWorkflow` / `SendReliableMessageAsync`.
- Converted: `ReplyUtility` text, template, imagemap, sticker, echo, and SDK-message reply paths use `ILineReplyWorkflow`.
- Converted: `LineUtilityClass` push/reply wrappers use workflow-backed helper classes, so product callers no longer need to know LINE SDK details.
- Temporarily retained: LINE content download (`GetContentStreamAsync`) remains in `ReplyUtility` because it is a media-read concern, not a push/reply delivery path.
- Converted: rich-menu create/upload/link and unlink/delete helper paths now use `ILineRichMenuWorkflow`; default constructors create processor-backed workflow instances internally.
- Cleaned: old commented `PushMessageAsync(...)` and `ReplyMessageAsync(...)` remnants were removed so future direct-SDK scans show only active code paths.
- Needs redesign later: multi-organization token switching in `LineUtilityClass.ReInitializeLineMessagingClient(...)` needs a shared workflow-level token resolver before it can be fully product-neutral.

## Next Slice Recommendation

Prioritize converting remaining product constructors that still create LINE helpers manually into DI-backed construction. Keep ChurchReport CRM/payment/donation semantics in ChurchReport; only product-agnostic LINE transport, message delivery workflow, and reply workflow belong in shared LINE projects.
## Final Verification Update
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false` => PASS, 0 warnings, 0 errors in the final audit run.
- `dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false` => PASS, 37 tests.
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter "PushUtilityWorkflowTests|LineUtilityClassWorkflowTests|ReplyUtilityGroupRoomProfileAdapterTests|ChurchReportLineBindingNotificationServiceTests|ChurchReportLineAdminNotificationServiceTests|PaymentNotificationServiceWorkflowTests"` => PASS, 33 tests.
- Active direct SDK scan found no active ChurchReport product direct push/multicast/richmenu SDK calls.
- Remaining `.ReplyMessageAsync` matches are wrapper-level calls through `ReplyUtility`, not direct LINE SDK usage.
- RichMenu convergence task completed separately under `.ccg/tasks/line-richmenu-workflow-convergence`.

