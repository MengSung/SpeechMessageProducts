# F09 Security Analysis

Status: COMPLETE
Module: F09
Mode: DIAGNOSIS_ONLY

## Confirmed Security / Integrity Issue

### F09-SEC-001: Post-payment workflow lacks an idempotent side-effect contract

F09 owns the reusable post-payment pipeline. The current pipeline always runs
all registered record updaters and payer notifiers for each call:

- `SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:25` constructs
  the workflow from updater and notifier enumerables.
- `SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:43` to
  `SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:50` dispatches
  all handlers sequentially every time `ExecuteAsync` is called.
- `SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:53` to
  `SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:57` reports
  whether handlers exist, not whether a payment event was already processed.

Current consumers can call that same workflow more than once for one order:

- `SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs:91` to
  `SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs:94` sends a
  successful front-channel post-back through the post-payment workflow.
- `SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs:123` to
  `SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs:125` sends
  the backend result notification through the same workflow.
- `SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs:251` to
  `SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs:277` builds
  context and invokes `_postPaymentWorkflow.ExecuteAsync`.
- `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:119` to
  `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:145` maps
  MyPay callbacks and invokes the same workflow.

The downstream handlers have real side effects:

- `SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs:54`
  to `SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs:62`
  update CRM through `PaymentCrmService` and `ToolUtility.UpdateEntity`.
- `SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs:84`
  to `SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs:129`
  sends success or failure notifications.
- `SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs:61` to
  `SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs:75` appends
  payment result data to the CRM description every time it runs.
- `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:201`
  to `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:205`
  uses a deterministic LINE retry key for success notices, but that does not
  make the F09 workflow itself idempotent or cover CRM writes and future
  notifier implementations.

Security/payment-integrity impact:

- Replayed or repeated provider callbacks can duplicate downstream side effects
  when the consumer routes both events through F09 post-payment processing.
- Front-channel plus back-channel callback routes can both reach the workflow
  for the same successful order.
- CRM audit text can be appended repeatedly, and notifier handlers can run
  repeatedly.
- Correctness depends on every product handler implementing its own duplicate
  guard, while the shared F09 workflow does not expose a required guard seam.

Recommended security action:

- Introduce an F09-owned idempotency contract for post-payment workflow
  execution.
- Derive or require a stable operation key from provider profile, product order
  id, provider transaction id, and normalized status.
- Require consumers to provide an execution store/checkpoint implementation
  before side-effect handlers run.
- Return per-handler execution states so callers can distinguish completed,
  skipped duplicate, failed, and partially completed workflows.

## Rejected Security Candidates

### Redirect acknowledgement open redirect

Rejected. F08 defines `PaymentCallbackAcknowledgement.Redirect` and F09 maps it
to `RedirectResult` in
`SpeechMessage.Payments.AspNetCore/PaymentAcknowledgementResultMapper.cs:42`.
The test
`ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs:47`
uses an external URL. However, production provider inspection found Taishin
returning JSON acknowledgement, MyPay returning plain text acknowledgement, and
Sinopac returning none. No current production parser emits a redirect
acknowledgement from provider-controlled data, so there is no actionable
source-to-sink issue in F09 today.

### Header or raw callback payload leakage

Rejected. `PaymentHttpRequestMapper` carries headers and raw body into
`PaymentCallbackRequest`, but current providers consume form/query/body fields,
`PaymentDiagnosticsSanitizer` masks provider diagnostics, and
`PaymentCallbackLogger` logs only order id, transaction id, status, error kind,
and amount. No evidence showed headers or raw body being written to logs.

### Provider authenticity and callback binding

Rejected for F09 ownership. Callback authenticity, provider hash/signature
verification, provider status mapping, and provider replay/binding contracts
belong to F08. F09 still needs downstream side-effect idempotency because
retries and multiple host routes can reach F09 even when F08 adds stronger
verification.

## Security Items Not Found

- No F09-owned static/shared mutable state carrying user identity was found.
- No F09-owned cookie/session/claims/authorization decision was found.
- No F09-owned secret, token, credential, or card-number logging sink was found.
- No F09-owned crypto implementation was found.
