# LINE Admin Error Notification Convergence

## Requirements

- Continue converging existing ChurchReport LINE call sites toward shared LINE workflow/processor modules.
- Keep ChurchReport product-specific administrator recipient IDs, prefixes, CRM/payment/poll/controller semantics inside ChurchReport.
- Do not put ChurchReport error-notification semantics into `LineMessagingProcessor` or `LineMessagingProcessor.Workflows`.
- Replace repeated `new LineMessagingProcessorClass().SendMessage(...)` error/admin notification calls with a single ChurchReport-side adapter that uses `ILineNotificationWorkflow`.
- Preserve best-effort behavior: admin error notification failures must not mask or replace the original exception path.
- Do not change Line Login binding notifications, ReplyUtility profile adapter, or payment notification service factory in this slice.

## Acceptance Criteria

- A ChurchReport product-layer admin LINE notifier exists and can be unit tested without real LINE HTTP.
- PollManager repeated admin error sends use the notifier instead of directly constructing `LineMessagingProcessorClass`.
- DonationPaymentManager admin error sends use the notifier instead of directly constructing `LineMessagingProcessorClass`.
- BaseChurchController / FeeManagementController admin error sends use the notifier where in scope.
- Direct `LineMessagingProcessorClass` creation count decreases and remaining instances are classified as intentionally retained.
- `ChurchReport.sln` builds.