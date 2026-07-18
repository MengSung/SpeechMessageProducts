# B05 Security Analysis

Status: DRAFT
Nested agent count: 0

## Security Findings

### B05-SEC-001 Callback exception details are written to broad diagnostics sinks

`PaymentReturnController.ReturnCore` accepts GET and POST payment return routes, maps the live ASP.NET request into a provider callback request, parses it through the payment gateway, then queries payment status (`PaymentReturnController.cs:97-153`). On exceptions, the controller builds an error detail string containing timestamp, shop number, masked pay token, exception message, stack trace, and inner exception, then writes it to both `System.Diagnostics.Trace` and `Console` (`PaymentReturnController.cs:155-166`).

Security impact: this is not confirmed credential disclosure because `PayToken` is masked before logging. It is still a high-value hardening issue because callback failures are externally triggerable and stack traces / provider error messages can disclose implementation detail in shared operational logs. The issue is within B05 because it is callback and host adapter handling, not provider protocol internals.

Recommended handling: replace broad exception detail writes with structured logger events that carry a correlation id, masked provider references, and sanitized error class. Keep stack traces in restricted application error logs only when configured for secure diagnostics.

### B05-SEC-002 LINE identifiers and payment retry keys appear in payment notification logs

`PaymentNotificationService.SendLineMessage` logs the recipient LINE id and retry key on both success and failure (`PaymentNotificationService.cs:113-135`). The same service builds retry keys from product order id/status (`PaymentNotificationService.cs:78-96`) and sends success/failure notifications based on CRM fee data and provider workflow result (`PaymentNotificationService.cs:144-302`).

Security impact: this is a privacy/log minimization issue, not an immediate critical exploit. LINE ids and stable retry keys can act as user/payment correlation identifiers in logs. The log scope belongs to B05 because B05 decides when and what payment notification is sent; B07 owns generic LINE transport.

Recommended handling: log only hashed/truncated LINE ids and retry key hashes, keep full identifiers out of normal information/error messages, and preserve retry key only in the outbound transport request.

### B05-SEC-003 Legacy processor returns raw LINE notification failure reasons to payment page HTML

ATM notification handling returns display strings that include `FormatLineNotificationFailureReason(lastException)` and that helper returns the base exception message when present (`DonationPaymentProcessor.PaymentProcessing.cs:397-405`, `DonationPaymentProcessor.PaymentProcessing.cs:445-458` from source scan). Manual dedication notification handling follows the same timeout/background-continuation pattern and returns formatted exception-derived text to the user-facing result (`DonationPaymentProcessor.FeeManagement.cs:292-360`).

Security impact: this is a medium information disclosure risk. It may expose provider or transport error text to donors, depending on exception content. It is not ranked critical without runtime evidence that secrets or tokens are present in those exception messages.

Recommended handling: replace user-facing failure reasons with stable friendly messages and log sanitized technical detail server-side.

## Critical Security Issues

No CRITICAL security issue is confirmed from static B05 evidence. The strongest security issue is high-priority hardening around externally reachable callback exception logging.
# B05 Security Analysis

Status: DEGRADED_REVIEW_PENDING
Nested agent count: 0

## Security Focus

This pass checked donation/product/payment risks called out by the lead: payment authorization, transaction state pollution, replay/idempotency, session/token handling, sensitive payment data exposure, unauthorized CRM updates, and callback validation. Product code was read-only.

## Findings

### B05-SEC-001: Callback exception diagnostics expose implementation details

Evidence:
- `PaymentReturnController.ReturnCore` accepts externally reachable return callbacks, maps the live request, parses callback data, queries payment status, and hands the result to B05 return workflow (`PaymentReturnController.cs:97-153`).
- The exception handler builds a diagnostic string with timestamp, `ShopNo`, masked `PayToken`, exception message, stack trace, and inner exception, then writes it to `System.Diagnostics.Trace` and `Console` (`PaymentReturnController.cs:155-166`).

Risk:
- `PayToken` is masked, so this is not confirmed direct credential disclosure.
- The callback endpoint is externally triggerable, and broad trace/console sinks can expose implementation detail, stack traces, provider errors, internal class names, and operational context outside the narrow payment diagnostic audience.
- This is B05-owned host adapter/callback handling, not F08 provider parser logic.

Recommended handling:
- Route callback exceptions through a B05 diagnostic sanitizer that emits correlation id, provider kind/profile, masked provider references, and stable error classification.
- Keep raw stack traces in restricted structured application logs only when secure diagnostics are explicitly enabled.
- Use donor-safe result messages that do not include provider or exception internals.

### B05-SEC-002: Payment notification logs include stable user/payment correlation identifiers

Evidence:
- `PaymentNotificationService.BuildPaymentLineRetryKey` builds a stable retry key from order id / product order id and status (`PaymentNotificationService.cs:78-96`).
- `SendLineMessage` logs full `LineId` and retry key on success and failure (`PaymentNotificationService.cs:113-135`).
- Success/failure payment notifications are constructed from payment result, CRM fee fields, and contact LINE id (`PaymentNotificationService.cs:144-302`).

Risk:
- LINE ids and retry keys can correlate a donor/user with payment order state in operational logs.
- This is privacy/log-minimization risk, not a confirmed critical exploit.
- B05 owns notification timing/content and retry-key use; B07 owns the transport.

Recommended handling:
- Hash or truncate LINE ids and retry keys in logs.
- Preserve full retry key only in the outbound request object.
- Add log assertions for no raw LINE id or retry key in normal info/error messages.

### B05-SEC-003: User-facing notification failure details can include raw exception messages

Evidence:
- ATM notification failure returns a display string containing `FormatLineNotificationFailureReason(lastException)` after all LINE candidates fail (`DonationPaymentProcessor.PaymentProcessing.cs:397-405`).
- The failure reason helper returns the base exception message when present, based on source inspection of `DonationPaymentProcessor.PaymentProcessing.cs`.
- Manual dedication notification timeout/failure handling uses the same display-response pattern with background completion logging and exception-derived result text (`DonationPaymentProcessor.FeeManagement.cs:292-360`).

Risk:
- Depending on provider/transport exception content, donors may see technical LINE/payment transport details.
- No evidence confirms secret/token exposure, so this remains medium severity.

Recommended handling:
- Replace donor-visible technical failure details with stable friendly messages.
- Log sanitized technical details server-side using a correlation id.

### B05-SEC-004: Callback replay/state pollution needs explicit idempotency proof

Evidence:
- `MyPayController.PaymentNotify` parses provider callback, logs the callback result, retrieves a fee entity by `new_q_pay_order_number`, builds post-payment context, executes CRM update and notification, then returns provider acknowledgement (`MyPayController.cs:90-153`).
- `PaymentCrmService.UpdateFeeEntityWithPaymentResult` updates payment status, paid amount/date/method and appends provider details to CRM fields (`PaymentCrmService.cs:39-82`).

Risk:
- Static evidence shows a stable order lookup and update path, but does not prove idempotent state transition guards for duplicate callbacks, out-of-order callbacks, or success-after-failure pollution.
- This is not promoted to confirmed CRITICAL without runtime/source proof of missing guard, but it is a high-value validation item.

Recommended handling:
- Add a runtime validation case for duplicate callback and status downgrade attempts.
- Future optimization should define a B05 payment state machine with monotonic transitions and idempotency keys.

## Critical Security Assessment

No CRITICAL security issue is confirmed from static evidence. The highest-priority security issue is B05-SEC-001 because callback failures are externally reachable and write implementation details to broad diagnostics sinks. Callback replay/state pollution is a serious validation target but remains unconfirmed until duplicate/out-of-order behavior is tested.
