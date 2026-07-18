# B05 Donation Product Payment Diagnostic Issues

Status: DRAFT
Module: B05
Workspace: B05-donation-product-payment
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: READY
Nested agent count: 0
Issue document SHA-256: PENDING_AFTER_CCG

## Executive Summary

B05 owns ChurchReport donation/product payment orchestration: donation input/audit, payment session handoff, host adapter, callback, CRM write, and post-payment notification decisions. It depends on F03A CRM, F08 provider protocol, F09 payment workflows/ASP.NET adapter, B01 identity/session, B06B fee master data, and B07 LINE transport. No product code was modified.

No CRITICAL security issue is confirmed from static evidence. The highest-value immediate handling item is callback diagnostic leakage in `PaymentReturnController`, where externally reachable callback failures write exception messages and stack traces to broad diagnostics sinks. The highest-value performance/design issue is synchronous LINE notification waiting from the payment callback path.

## Ranked Confirmed Issues

### B05-SEC-001 Callback exception diagnostics expose implementation detail in broad sinks

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 18
- Impact score: 21
- Likelihood/frequency score: 13
- Security urgency score: 13
- Performance gain score: 2
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: S
- Primary owner: B05
- Cross-module: false
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:97`
  - `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:104`
  - `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:155`
  - `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:165`
- Evidence: `ReturnCore` logs callback invocation data, then on exception writes message, stack trace, inner exception, masked token, and shop number to both Trace and Console.
- Control/data/lifetime flow: external payment return request -> host callback mapper -> payment gateway parse/query -> exception handler -> broad diagnostics sinks.
- Impact: externally triggerable callback failures can leak stack traces and provider error details into logs with broader visibility than payment diagnostics require.
- Why this is necessary: payment callbacks are a high-sensitivity boundary; diagnostics must be sanitized by default.
- Recommended action: introduce a B05 callback diagnostic sanitizer and structured logger policy. Keep full stack traces only in restricted application logs under secure diagnostics settings.
- Validation: malformed callback runtime validation should confirm no stack trace/raw provider exception in console/trace and only donor-safe page output.
- Rollback boundary: controller-level logging behavior only.
- Extraction contract: input exception/callback result/provider refs -> sanitized log fields and user-safe display message.
- CCG round history:
  - Round 1: Pending.

### B05-PERF-001 Payment notification path blocks synchronously on async LINE workflow

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 19
- Impact score: 20
- Likelihood/frequency score: 13
- Security urgency score: 2
- Performance gain score: 10
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B05
- Cross-module: B07/F06 transport dependency
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:113`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:128`
  - `SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs:84`
  - `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:139`
- Evidence: `SendLineMessage` calls `SendOrThrowAsync(...).GetAwaiter().GetResult()` and is reached from payment post-processing during provider callback handling.
- Control/data/lifetime flow: MyPay callback -> post-payment workflow -> CRM update and notification handler -> synchronous wait on async LINE workflow -> provider acknowledgement.
- Impact: callback request threads can be blocked by LINE transport latency or failure, increasing tail latency and reducing reliability under provider retry pressure.
- Why this is necessary: provider callback acknowledgement should have deterministic latency and should not depend on user notification delivery.
- Recommended action: make B05 notification handlers async end-to-end or enqueue B05 notification decisions into an idempotent outbox handled by B07/F06 transport.
- Validation: delayed fake LINE workflow should not materially delay provider acknowledgement beyond the intended policy.
- Rollback boundary: B05 notification service/handler contract.
- Extraction contract: notification decision input -> async delivery/outbox result.
- CCG round history:
  - Round 1: Pending.

### B05-PERF-002 Legacy donation payment processor bypasses host lifetime control

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 66
- Confirmed: true
- Evidence confidence: 17
- Impact score: 16
- Likelihood/frequency score: 10
- Security urgency score: 1
- Performance gain score: 8
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: L
- Primary owner: B05
- Cross-module: F03A/B07 dependencies
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:45`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:69`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:135`
- Evidence: processor constructors create LINE utilities and obtain ToolUtility through `ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")` instead of receiving host-managed abstractions.
- Control/data/lifetime flow: donation payment flow constructs processor -> direct CRM/LINE utilities -> fee creation/payment/notification.
- Impact: resource pooling, cancellation, telemetry, and testing are harder to control; hot payment paths remain coupled to legacy factories.
- Why this is necessary: B05 cannot optimize looped payment/notification/CRM behavior while resource lifetimes are hidden inside the processor.
- Recommended action: extract host-owned CRM fee operations and notification ports; keep provider protocol in F08/F09 and LINE transport in B07.
- Validation: DI composition smoke plus unit tests with fake CRM and notification ports.
- Rollback boundary: constructor/adapter layer around legacy processor.
- Extraction contract: donation payment processor input/output port for CRM, payment order, and notification dependencies.
- CCG round history:
  - Round 1: Pending.

### B05-SEC-002 Payment notification logs expose stable user/payment correlation identifiers

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 61
- Confirmed: true
- Evidence confidence: 18
- Impact score: 14
- Likelihood/frequency score: 12
- Security urgency score: 8
- Performance gain score: 1
- Loop leverage score: 5
- Ease/reversibility score: 3
- Effort: S
- Primary owner: B05
- Cross-module: B07 transport consumer
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:78`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:130`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:134`
- Evidence: retry keys are built from order/status and success/failure logs include full LINE id and retry key.
- Control/data/lifetime flow: payment result -> retry key -> LINE request -> info/error log.
- Impact: logs can correlate donor LINE identity with payment order state.
- Why this is necessary: payment notification logs should minimize personal identifiers.
- Recommended action: hash/truncate LINE id and retry key in logs; preserve full values only in outbound request memory.
- Validation: log assertions should verify no raw LINE id/retry key in normal log messages.
- Rollback boundary: logging fields in B05 notification service.
- Extraction contract: sanitized notification telemetry fields.
- CCG round history:
  - Round 1: Pending.

### B05-EXT-001 Extract async notification and CRM update seams before looped optimization

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 58
- Confirmed: true
- Evidence confidence: 16
- Impact score: 13
- Likelihood/frequency score: 10
- Security urgency score: 2
- Performance gain score: 7
- Loop leverage score: 10
- Ease/reversibility score: 0
- Effort: L
- Primary owner: B05
- Cross-module: F03A/B07/F09 dependency seams
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs:39`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:144`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs:77`
- Evidence: CRM update, fee creation, notification content, and transport invocation are spread across service and legacy processor layers.
- Control/data/lifetime flow: payment result -> fee lookup/update -> notification decision -> LINE transport.
- Impact: future optimization loops will otherwise repeatedly touch high-risk callback and CRM code.
- Why this is necessary: clear ports let later work optimize CRM calls and notification delivery without crossing provider or LINE ownership boundaries.
- Recommended action: define B05-owned `PaymentNotificationPort` and `DonationPaymentCrmPort`; keep F08/F09/B07 dependencies behind existing contracts.
- Validation: unit tests with fake ports and callback workflow tests.
- Rollback boundary: adapter-only extraction around current behavior.
- Extraction contract: payment result/fee/contact input -> CRM update result and notification request.
- CCG round history:
  - Round 1: Pending.

## Runtime Validation Pending

- B05-SEC-001 requires malformed callback runtime validation to prove exact log sink visibility.
- B05-PERF-001 requires delayed fake LINE workflow validation to measure provider callback acknowledgement latency.
- B05-PERF-003 from evidence is tracked in `runtime-validation-plan.md` but not promoted to a ranked confirmed issue until delivery semantics are measured.

## Deleted Or Rejected Candidates

- Provider signature, crypto, callback parser correctness: rejected from B05 because owned by F08 provider protocol.
- Fee master data correctness: rejected from B05 because owned by B06B.
- Generic LINE transport retry implementation: rejected from B05 because owned by B07/F06; B05 owns only notification decision/content and retry-key use.
- `static` configuration alone in the legacy processor: not promoted to a standalone issue because no cross-tenant mutation or request data sharing was confirmed.

## Cross-Module Handoffs

- F03A: CRM call batching, ToolUtility behavior, and connection lifetime are dependencies for any CRM port extraction.
- F08/F09: provider parse/query/create behavior remains outside B05; B05 should consume stable result/request contracts only.
- B07/F06: LINE transport and reliable delivery mechanics remain outside B05; B05 should own payload and idempotency decision.
- X02B: logging policy can provide the common sanitizer/structured logging conventions.

## CCG Outcome Summary

Pending CCG review run `b05-issue-review-r1`.

## Review Changes Applied

Pending CCG review output.

## Final CCG Approval

Pending.
# B05 Donation Product Payment Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: B05
Workspace: B05-donation-product-payment
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: READY
Nested agent count: 0

## Executive Summary

B05 owns donation/product payment product orchestration: donation input and audit handoff, payment session state, host adapter use, callback handling, CRM fee writes, and post-payment notification decisions. F08 owns provider protocol and callback parser internals, F09 owns provider-neutral payment workflow/ASP.NET contracts, B06B owns fee master data, and B07/F06 own generic LINE transport.

No CRITICAL security issue is confirmed from static evidence. The highest-value immediate security item is callback diagnostics that write exception implementation details to broad sinks. The highest-value design/performance item is synchronous LINE notification waiting during payment callback processing. The highest-value acceleration path is extracting a B05 payment state transition service and async notification/CRM ports.

## Ranked Issue List

### B05-SEC-001 Callback exception diagnostics expose implementation detail in broad sinks

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 84
- Confirmed: true
- Evidence confidence: 18
- Impact score: 22
- Likelihood/frequency score: 13
- Security urgency score: 14
- Performance gain score: 2
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: S
- Primary owner: B05
- Cross-module: false
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:97`
  - `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:104`
  - `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:155`
  - `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:165`
- Evidence: `ReturnCore` handles the externally reachable return callback, maps/parses/queries payment status, and on exception writes timestamp, shop number, masked token, exception message, stack trace, and inner exception to Trace and Console.
- Control/data/lifetime flow: external payment return request -> host request mapper -> payment gateway parse/query -> exception handler -> broad diagnostic sinks.
- Impact: externally triggerable callback failures can disclose implementation detail or provider error text in logs that are not narrowly scoped to secure payment diagnostics.
- Why this is necessary: payment callbacks are a sensitive boundary and should not use broad raw exception output by default.
- Recommended action: add a B05 callback diagnostic sanitizer and structured logging policy; keep raw stack traces only in restricted diagnostics.
- Validation: malformed callback request should show donor-safe UI and no raw stack trace/provider internals in broad sinks.
- Rollback boundary: controller logging behavior and sanitizer only.
- Extraction contract: callback exception/result/provider refs -> sanitized structured fields and donor-safe message.
- CCG round history:
  - Round 1: local document prepared for CCG review.

### B05-PERF-001 Callback acknowledgement is coupled to synchronous LINE notification delivery

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 80
- Confirmed: true
- Evidence confidence: 19
- Impact score: 21
- Likelihood/frequency score: 13
- Security urgency score: 3
- Performance gain score: 10
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B05
- Cross-module: B07/F06 transport dependency
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:113`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:128`
  - `SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs:84`
  - `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:139`
- Evidence: `SendLineMessage` synchronously waits on an async LINE notification workflow with `.GetAwaiter().GetResult()`, and MyPay callback awaits post-payment workflow before provider acknowledgement.
- Control/data/lifetime flow: MyPay callback -> fee lookup -> post-payment workflow -> notification handler -> sync wait on async LINE transport -> provider acknowledgement.
- Impact: LINE transport delay/failure can block callback request threads and increase duplicate provider retry pressure.
- Why this is necessary: payment provider acknowledgement should not be tied to user notification latency unless explicitly required by contract.
- Recommended action: make B05 notification handling async end-to-end, or persist a B05 notification decision/outbox item and acknowledge provider callback independently.
- Validation: delayed fake LINE workflow should quantify callback latency and prove the target design decouples acknowledgement.
- Rollback boundary: B05 notification service/handler contract.
- Extraction contract: payment notification decision -> async delivery/outbox result.
- CCG round history:
  - Round 1: local document prepared for CCG review.

### B05-SEC-003 Callback state transition idempotency and replay behavior lacks explicit proof

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 76
- Confirmed: false
- Evidence confidence: 13
- Impact score: 23
- Likelihood/frequency score: 11
- Security urgency score: 15
- Performance gain score: 2
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B05
- Cross-module: F08/F09 provide callback/result contracts
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:90`
  - `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:128`
  - `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:145`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs:39`
- Evidence: callback processing retrieves a fee by order number, builds post-payment context, updates CRM fee fields, and sends notification. Static inspection did not find an explicit monotonic transition guard in the observed B05 path.
- Control/data/lifetime flow: provider callback -> product order id -> fee entity -> CRM payment state mutation -> notification.
- Impact: duplicate or out-of-order callbacks could duplicate notifications or pollute transaction state if guards are absent.
- Why this is necessary: replay/idempotency is a core payment safety requirement.
- Recommended action: validate duplicate/out-of-order callback behavior and introduce a B05 payment state transition service if guards are missing.
- Validation: execute duplicate success, failed-after-success, success-after-failure, and unknown-order callback scenarios in controlled tests.
- Rollback boundary: B05 state transition service around current CRM update path.
- Extraction contract: current fee state + provider result -> idempotent transition decision.
- CCG round history:
  - Round 1: local document prepared for CCG review.

### B05-PERF-002 Legacy donation payment processor bypasses host-managed CRM/LINE lifetimes

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 67
- Confirmed: true
- Evidence confidence: 17
- Impact score: 16
- Likelihood/frequency score: 10
- Security urgency score: 2
- Performance gain score: 8
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: L
- Primary owner: B05
- Cross-module: F03A/B07 dependencies
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:45`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:69`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:135`
- Evidence: `DonationPaymentProcessor` owns static/lazy configuration, LINE utility fields, and direct `ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")` construction.
- Control/data/lifetime flow: donation payment flow -> legacy processor constructor -> direct CRM/LINE dependencies -> fee/payment/notification work.
- Impact: hot path resource lifetime, pooling, cancellation, and telemetry are difficult to optimize from host DI.
- Why this is necessary: B05 cannot accelerate payment loops cleanly while CRM/LINE dependencies are hidden behind direct construction.
- Recommended action: extract B05 CRM/payment/notification ports and inject F03A/B07/F09 implementations.
- Validation: DI smoke and fake-port unit tests around donation payment flows.
- Rollback boundary: adapter layer around legacy processor constructors.
- Extraction contract: donation form/contact/method -> fee/payment/notification result.
- CCG round history:
  - Round 1: local document prepared for CCG review.

### B05-SEC-002 Payment notification logs expose stable user/payment correlation identifiers

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 62
- Confirmed: true
- Evidence confidence: 18
- Impact score: 14
- Likelihood/frequency score: 12
- Security urgency score: 8
- Performance gain score: 1
- Loop leverage score: 5
- Ease/reversibility score: 4
- Effort: S
- Primary owner: B05
- Cross-module: B07 transport consumer
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:78`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:130`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:134`
- Evidence: retry keys are built from order/status and logs include full LINE id and retry key.
- Control/data/lifetime flow: payment result -> retry key -> LINE request -> info/error logs.
- Impact: operational logs can correlate donor LINE identity with payment order state.
- Why this is necessary: payment notification logs should minimize personal identifiers.
- Recommended action: hash/truncate LINE ids and retry keys in logs while keeping full values only in outbound request memory.
- Validation: log tests assert no raw LINE id or retry key in normal messages.
- Rollback boundary: B05 notification logging fields.
- Extraction contract: notification request -> sanitized telemetry fields.
- CCG round history:
  - Round 1: local document prepared for CCG review.

### B05-EXT-001 Extract B05 payment state, notification, and CRM ports before looped optimization

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 59
- Confirmed: true
- Evidence confidence: 16
- Impact score: 13
- Likelihood/frequency score: 10
- Security urgency score: 2
- Performance gain score: 8
- Loop leverage score: 10
- Ease/reversibility score: 0
- Effort: L
- Primary owner: B05
- Cross-module: F03A/F09/B07 dependency seams
- Gate blocked: false
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs:39`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:144`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs:77`
- Evidence: CRM updates, fee creation, notification payloads, and transport invocation are spread across services and legacy processor partials.
- Control/data/lifetime flow: payment result -> fee lookup/update -> notification decision -> LINE transport.
- Impact: future optimization will otherwise repeatedly touch high-risk callback and CRM code.
- Why this is necessary: clean ports enable state-machine, batching, and async notification improvements while preserving module ownership.
- Recommended action: define B05-owned state transition, notification, and CRM ports; keep F08/F09/B07 dependencies behind contracts.
- Validation: callback workflow tests using fake state/CRM/notification ports.
- Rollback boundary: adapter-only extraction around current behavior.
- Extraction contract: payment result/fee/contact input -> state transition, CRM mutation, notification request.
- CCG round history:
  - Round 1: local document prepared for CCG review.

## Runtime Validation Items

- Duplicate callback and out-of-order status validation for B05-SEC-003.
- Malformed return request diagnostic leakage validation for B05-SEC-001.
- Delayed/failing LINE workflow latency validation for B05-PERF-001.
- CRM call-count baseline for B05-PERF-002 and B05-EXT-001.

## Deleted Or Rejected Candidates

- Provider signature/crypto/callback parser issue: rejected from B05 because F08 owns provider protocol.
- Fee master data correctness issue: rejected from B05 because B06B owns fee master data.
- Generic LINE transport retry issue: rejected from B05 because B07/F06 own transport mechanics.
- Static configuration alone in the legacy processor: not promoted as security issue because no cross-user mutable request state was confirmed.

## Cross-Module Handoffs

- F03A: CRM call batching, ToolUtility behavior, and CRM connection lifetime.
- F08/F09: provider parse/query/create behavior and payment adapter contracts.
- B07/F06: LINE delivery mechanics and retry transport.
- X02B: shared logging policy and sanitizer conventions.

## CCG Outcome Summary

External review has not produced usable backend output yet for this document revision, so the diagnostic remains DEGRADED_REVIEW_PENDING until the CCG runner completes.

## Review Changes Applied

Local evidence was ranked, scoped, and separated into confirmed issues versus runtime-validation items before external review. No external reviewer changes have been applied yet.

## Final CCG Approval

DEGRADED_REVIEW_PENDING
