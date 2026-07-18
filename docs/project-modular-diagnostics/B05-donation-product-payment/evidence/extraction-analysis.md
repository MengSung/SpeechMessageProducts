# B05 Extraction Analysis

Status: DRAFT
Nested agent count: 0

## Extraction Candidates

### Candidate 1: Async payment notification port

Owning files:
- `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs`
- `SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs`

Contract:
- Input: payment workflow result, fee entity, contact entity, fee type, notification status.
- Output: delivery request accepted/sent result with retry key and sanitized telemetry.
- Dependencies: B07/F06 LINE workflow transport, B05 message payload builder.
- Consumers: MyPay callback post-payment workflow and legacy donation/ATM notification flows.

Why this accelerates optimization: it removes sync-over-async from callback handling and creates a stable point for outbox/retry improvements without moving LINE transport ownership into B05.

### Candidate 2: Donation payment CRM update port

Owning files:
- `SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs`

Contract:
- Input: fee id/entity, provider/product order ids, payment result, payment method, timestamps.
- Output: CRM update result and sanitized audit summary.
- Dependencies: F03A CRM CRUD/query.
- Consumers: callback post-payment workflow, legacy donation processor.

Why this accelerates optimization: it isolates CRM write semantics and enables batching/measurement without changing provider protocol code.

### Candidate 3: Payment callback diagnostic sanitizer

Owning files:
- `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs`
- `SpeechMessageProducts.ChurchReport/Services/PaymentCallbackLogger.cs`

Contract:
- Input: callback result/error/exception, provider hints, request correlation id.
- Output: sanitized structured log fields and donor-safe display message.
- Dependencies: X02B logging policy, F08/F09 callback result shape.
- Consumers: PaymentReturn and MyPay notify controllers.

Why this accelerates optimization: it removes duplicated ad hoc callback logging and creates a clear security boundary for provider return diagnostics.

### Candidate 4: Legacy donation payment processor split

Owning files:
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/**`
- `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`

Contract:
- Input: donation form model, contact, selected payment method, order/session ids.
- Output: fee creation/update result, payment URL/instructions, notification decision.
- Dependencies: F03A CRM, F09 payment host adapter, B07 notification transport.
- Consumers: donation payment UI and return workflow dispatcher.

Why this accelerates optimization: current partial class combines order creation, CRM fee management, LINE notification, display strings, and configuration/lifetime setup. Splitting by contract would reduce blast radius for looped optimization.

## Boundary Discipline

Extraction must not move F08 provider protocol, F09 provider-neutral workflow, B06B fee master data, or B07 generic LINE transport into B05. B05 should own only product-specific decisions, payloads, and CRM/payment session orchestration.
# B05 Extraction Analysis

Status: DEGRADED_REVIEW_PENDING
Nested agent count: 0

## Clean Extraction / Acceleration Candidates

### Candidate 1: B05 payment state transition service

Purpose:
- Own product-specific transition decisions for pending, paid, failed, cancelled, duplicate, and out-of-order callbacks.

Contract:
- Input: current CRM fee payment state, provider workflow result, provider/product order ids, callback timestamp.
- Output: transition decision, CRM mutation request, notification decision, idempotency/audit facts.

Why it helps:
- Directly addresses replay and transaction-state pollution risk.
- Lets F08/F09 keep provider parsing/query contracts while B05 owns product state.

### Candidate 2: Async payment notification port

Owning files:
- `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs`
- `SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs`
- legacy notification branches under `DonationPaymentProcessor`.

Contract:
- Input: donor/contact reference, payment result, fee type, message payload, retry key.
- Output: accepted/sent/skipped/failure result plus sanitized telemetry.

Why it helps:
- Removes sync-over-async from callback handling.
- Creates a stable place to add durable retry/outbox behavior without moving generic LINE transport into B05.

### Candidate 3: Donation payment CRM port

Owning files:
- `SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs`

Contract:
- Input: fee entity/id, transition decision, provider references, paid amount/date/method, audit text.
- Output: CRM update result and sanitized audit summary.

Why it helps:
- Makes CRM write shape measurable and testable.
- Enables batching/consolidation after baseline measurement.
- Keeps F03A as the CRM dependency provider.

### Candidate 4: Callback diagnostic sanitizer

Owning files:
- `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs`
- `SpeechMessageProducts.ChurchReport/Services/PaymentCallbackLogger.cs`

Contract:
- Input: callback result/error/exception, provider kind/profile, request correlation id, masked provider refs.
- Output: structured log fields and donor-safe message key.

Why it helps:
- Prevents future callback work from reintroducing raw stack trace, token, provider payload, or LINE id leakage.
- Can be reused by PaymentReturn and MyPay callback paths.

### Candidate 5: B05 boundary audit script/checklist

Purpose:
- Automatically verify B05 issues do not claim ownership of F08 provider protocol, B06B fee master data, or B07 generic LINE transport.

Contract:
- Input: changed files or diagnostic issue file.
- Output: boundary warnings for cross-module ownership drift.

Why it helps:
- Accelerates future looped optimization by catching scope drift early.

## Boundary Discipline

Do not extract provider crypto/signature/callback parser into B05. Do not move fee master data into B05. Do not move LINE transport retry mechanics into B05. B05 should own product-specific payment state, CRM mutation decisions, payment notification content, and host adapter orchestration.
