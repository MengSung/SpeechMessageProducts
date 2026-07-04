# Review: LINE Admin Error Notification Convergence

## Scope

This slice consolidates ChurchReport product-layer administrator/error LINE notifications that previously created `LineMessagingProcessorClass` directly and sent to the hard-coded administrator LINE ID.

## Changed Behavior

- Added `ChurchReportLineAdminNotificationService` in the ChurchReport product layer.
- The service uses `ILineNotificationWorkflow.SendAsync(...)` for best-effort admin notifications.
- The service keeps ChurchReport-specific administrator recipient ID, product source labels, and error-message formatting inside ChurchReport.
- Replaced repeated admin error sends in:
  - `BaseChurchController.SendLineErrorNotification(...)`
  - `FeeManagementController`
  - `DonationPaymentManager`
  - `PollManager`
- Preserved original exception flow: LINE notification failures are swallowed and must not hide the original business exception.

## External Review

- Gemini review: completed. No Critical findings. It warned that registration-error formatting could drift if the category was folded into the source string.
- Claude review: completed. No Critical findings. It warned that token resolution now aligns with organization-aware ChurchReport token lookup and should be documented as an intentional behavior change. It also noted the old BaseChurchController admin ID constant became dead code.
- Actions taken:
  - Added/kept a three-argument `NotifyDefaultError(source, category, errorMessage)` path.
  - Updated `DonationPaymentManager.NotifyDonationRegistrationError(...)` to use source `好牧人` and category `註冊錯誤`, preserving legacy message shape.
  - Added tests for default error shape, registration error shape, and best-effort swallow behavior.
  - Removed the obsolete `LINE_ERROR_RECEIVER_ID` constant from `BaseChurchController`.

## Validation Evidence

- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter ChurchReportLineAdminNotificationServiceTests`
  - Passed: 3/3
  - Existing unrelated warning remains: `MemberInfoScopeGuardTests.cs(33,17): warning xUnit1012`
- `dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false`
  - Passed: 33/33
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`
  - Build succeeded: 0 warnings, 0 errors

## Remaining Direct Processor Usages

Remaining direct `LineMessagingProcessorClass` creation points are intentionally outside this admin-error slice:

- `ReplyUtility`: group/room profile adapter construction.
- `PaymentNotificationService`: creates the workflow-backed reliable payment notification sender.
- `ChurchReportLineAdminNotificationService`: creates the workflow-backed admin notification sender.
- `SmallGroupController.LineLogin`: LINE binding notification flow, not admin error notification.

These should be handled in later slices only if their domain semantics are explicitly addressed.