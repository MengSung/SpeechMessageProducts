# PushUtility Reliable Required Call-Site Cleanup

## Requirements

- Keep ChurchReport CRM, payment, donation, and MVC flow inside ChurchReport.
- Keep shared LINE projects product-agnostic.
- Add a ChurchReport `PushUtility` entry point for reliable required notifications.
- Reliable required notifications must not swallow LINE workflow failures.
- Reliable required notifications must carry a retry key into `ILineNotificationWorkflow`.
- Preserve existing best-effort `SendMessage(...)` behavior for legacy callers.

## Acceptance Criteria

- `PushUtility.SendReliableMessageAsync(...)` exists for text messages that need retry semantics.
- When `ILineNotificationWorkflow` is injected, `SendReliableMessageAsync(...)` sends a `LineNotificationRequest` with `RetryKey`.
- When workflow send fails, `SendReliableMessageAsync(...)` throws `LineNotificationException`.
- Existing `PushUtilityWorkflowTests` pass.
- `LineMessagingProcessor.Workflows.Tests` pass.
- `ChurchReport.sln` builds.

