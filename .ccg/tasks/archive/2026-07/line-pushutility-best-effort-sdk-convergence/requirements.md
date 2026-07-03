# PushUtility Best-Effort SDK Message Convergence

## Requirements

- Continue converging existing ChurchReport LINE call sites toward the shared LINE workflow.
- Keep ChurchReport CRM, payment, donation, MVC, and other product-specific flow inside ChurchReport.
- Keep shared LINE projects product-agnostic.
- Preserve legacy best-effort behavior for existing `PushUtility` methods that currently swallow LINE send failures.
- Route safe best-effort SDK message methods through `ILineNotificationWorkflow.SendAsync(...)` when workflow is injected.
- Do not change rich-menu operations or synchronous demo/template methods in this slice.

## Acceptance Criteria

- `PushUtility.SendMessage(string, List<ISendMessage>)` uses `ILineNotificationWorkflow` when injected and keeps swallowing failures.
- `PushUtility.SendImage(...)` uses `ILineNotificationWorkflow` when injected and keeps swallowing failures.
- The implementation centralizes the best-effort SDK-message workflow routing instead of duplicating request construction in every method.
- Existing `PushUtilityWorkflowTests` pass.
- `LineMessagingProcessor.Workflows.Tests` pass.
- `ChurchReport.sln` builds.

