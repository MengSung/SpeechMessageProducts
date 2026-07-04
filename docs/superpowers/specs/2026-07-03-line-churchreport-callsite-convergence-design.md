# ChurchReport LINE Call Site Convergence Design

## Goal

Converge ChurchReport LINE notification call sites toward `ILineNotificationWorkflow` while keeping ChurchReport CRM, payment, donation, and view orchestration inside ChurchReport.

## Current State

The shared LINE foundation already exists:

- `Line.Messaging` owns LINE message models and JSON serialization.
- `LineMessagingProcessor` owns LINE API calls.
- `LineMessagingProcessor.Workflows` owns `ILineNotificationWorkflow`, `LineNotificationRequest`, `LineNotificationContent`, retry key support, and product-friendly message factories.
- `LineMessagingProcessor.AspNetCore` owns DI registration for ASP.NET Core products.

ChurchReport still contains several product-layer call sites that directly or indirectly call LINE:

- `ChurchReport/Tools/PushUtility.cs`
- `ChurchReport/Tools/ReplyUtility.cs`
- `ChurchReport/Tools/LineUtilityClass.cs`
- `ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- `ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- `ChurchReport/WebServiceConnector/DonationPaymentProcessor/*.cs`
- `ChurchReport/Services/DonationBookingService.cs`
- direct `new LineMessagingProcessorClass()` in controllers and managers for error/admin notices.

Some of these paths are necessary payment notifications where delivery failure should be visible or propagated. Other paths are optional onboarding, QR code, admin, or small-group notifications where legacy swallow-and-trace behavior may remain temporarily.

## Scope

This slice is intentionally limited to ChurchReport call-site convergence:

- Keep CRM entity lookup, fee updates, payment status mapping, donation page rendering, and product-specific message wording in ChurchReport.
- Do not move ChurchReport types into `LineMessagingProcessor.Workflows`.
- Do not expand LINE official API support in this slice.
- Do not rewrite all `PushUtility` media/template methods in one pass.
- Prioritize payment and required notification paths where LINE failure must not be silently hidden.

## Boundary Rules

### Shared LINE Projects

Allowed:

- LINE recipient abstractions.
- LINE content wrappers.
- LINE workflow send result and exception behavior.
- Provider-neutral retry key passing.
- Message validation that depends only on LINE Messaging API rules.

Forbidden:

- `ChurchReport` namespace references.
- `Microsoft.Xrm.Sdk` CRM types.
- Payment fee entities or donation form models.
- MVC `Controller`, `IActionResult`, `ViewBag`, or HTTP request/response types.
- ChurchReport-specific text templates.

### ChurchReport Product Layer

Allowed:

- CRM lookups and updates.
- Fee/payment/donation workflow decisions.
- Business-specific message text.
- Deciding whether a notification is required or best-effort.
- Mapping product IDs to retry keys.

Preferred LINE boundary:

- Required notifications call `ILineNotificationWorkflow.SendOrThrowAsync(...)`.
- Best-effort notifications may call `ILineNotificationWorkflow.SendAsync(...)` and log failures.
- Legacy `PushUtility.SendMessage(...)` can remain as a compatibility shim, but it should internally prefer `ILineNotificationWorkflow` when injected.

## Notification Classification

### Required / Failure Must Surface

These should move first:

- ATM payment instructions.
- Payment completion notification when the caller needs to know delivery failed.
- Payment failure notification when the UI or audit log must reflect that LINE delivery failed.
- Any flow that currently uses `SendMessageOrThrowAsync`.

### Best-Effort / Failure Can Be Logged

These can remain lower priority:

- QR code onboarding notices.
- Small group report broadcasts.
- Admin/debug notices to the maintainer LINE ID.
- Optional gratitude messages where page rendering or CRM update should not fail.

## Proposed First Implementation Slice

The safest first slice is to improve `PushUtility` and payment-oriented call sites without changing user-visible CRM/payment behavior:

1. Make `PushUtility.SendMessageOrThrowAsync(...)` use `ILineNotificationWorkflow.SendOrThrowAsync(...)` when workflow is available.
2. Add a list-message throwing overload so required non-text messages can still use the workflow escape hatch.
3. Preserve existing swallowing behavior of `PushUtility.SendMessage(...)`, but route through `ILineNotificationWorkflow.SendAsync(...)` when available.
4. Inject or pass a workflow-backed `PushUtility` into payment processors where constructors already support dependency injection.
5. Do not touch `ReplyUtility` reply-token behavior in this slice because reply messages are a different LINE API command and should have a separate workflow design.

## Acceptance Criteria

- Existing payment/donation behavior still compiles.
- Required payment instruction path can be verified to call workflow-backed `SendOrThrowAsync`.
- Best-effort `SendMessage(...)` remains non-throwing.
- `SpeechMessage.Payments` remains free of LINE dependencies.
- `LineMessagingProcessor.Workflows` remains free of ChurchReport dependencies.
- Tests prove workflow-backed `PushUtility` sends `LineNotificationRequest` and preserves throw/swallow semantics.

## Out of Scope

- Full migration of `LineUtilityClass`.
- Full migration of `ReplyUtility`.
- Broadcast/multicast workflow abstraction.
- Webhook event routing.
- Removing every direct `PushMessageAsync` call from ChurchReport in one change.
- Modifying CRM/payment business logic.

