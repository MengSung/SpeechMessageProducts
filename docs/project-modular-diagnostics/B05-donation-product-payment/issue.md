# B05 Donation Product Payment Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: B05
Workspace: B05-donation-product-payment
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 11c3079e50c2d3c7bdc9da3510c618ec4b0ec0d2a5bf383109227b2b534b5be8

## Executive Summary

B05 has five confirmed findings covering callback diagnostics, synchronous LINE
delivery, logging identifiers, legacy dependency lifetime, and extraction of the
B05 state/notification/CRM boundary. Callback state-transition idempotency remains
runtime-validation pending and is not presented as confirmed.

## Ranked Confirmed Issues

### B05-PERF-001 Callback acknowledgement waits on synchronous LINE delivery

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 79
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 13
- Security urgency score: 5
- Performance gain score: 9
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B05
- Cross-module: B07 LINE transport; F08/F09 callback contracts
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:113
  - SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:128
  - SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:120
  - SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:151
- Evidence: the B05 notification service calls the async LINE workflow with
  `.GetAwaiter().GetResult()` while callback handling still owns provider
  acknowledgement completion.
- Control/data/lifetime flow: provider callback -> B05 state/notification decision
  -> blocked LINE network task -> callback acknowledgement.
- Impact: LINE latency or failure consumes callback request threads and delays the
  provider acknowledgement path.
- Why this is necessary: external notification delivery must not determine payment
  callback availability or acknowledgement latency.
- Recommended action: make B05 notification async end-to-end or persist an
  idempotent notification decision to a bounded outbox.
- Validation: inject delayed/failing B07 transport and assert bounded callback
  acknowledgement latency plus eventual notification outcome.
- Rollback boundary: B05 notification dispatch adapter; preserve provider response
  shape and B07 transport contract.
- Extraction contract: payment outcome, recipient, content, retry key in;
  asynchronous delivery result or durable outbox receipt out.
- CCG round history:
  - Round 1: run `20260712-124759-b05-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B05-SEC-001 Callback exception diagnostics expose implementation details

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 72
- Confirmed: true
- Evidence confidence: 18
- Impact score: 18
- Likelihood/frequency score: 12
- Security urgency score: 13
- Performance gain score: 1
- Loop leverage score: 6
- Ease/reversibility score: 4
- Effort: XS
- Primary owner: B05
- Cross-module: X02B logging policy and F08/F09 provider adapters
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:97
  - SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:162
  - SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:172
- Evidence: externally reachable return handling writes exception message, stack
  trace, inner details, shop/order context, and provider failure information to
  trace/console output.
- Control/data/lifetime flow: provider-controlled callback -> exception -> formatted
  diagnostic string -> broad trace/console sinks.
- Impact: implementation, provider, and correlation details can be exposed in logs
  beyond the payment boundary.
- Why this is necessary: callback diagnostics cross an external trust boundary and
  require stricter redaction than internal debug output.
- Recommended action: introduce a B05 callback diagnostic sanitizer with stable
  categories and restricted raw-detail handling.
- Validation: send malformed callback fixtures and assert public/log output excludes
  stack, inner exception, token, credential, and raw provider payload fields.
- Rollback boundary: B05 callback logging/error response only.
- Extraction contract: exception and safe callback correlation in; redacted error
  category and event out.
- CCG round history:
  - Round 1: run `20260712-124759-b05-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B05-EXT-001 B05 payment state, CRM mutation, and notification ports are mixed

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 74
- Confirmed: true
- Evidence confidence: 19
- Impact score: 18
- Likelihood/frequency score: 12
- Security urgency score: 6
- Performance gain score: 7
- Loop leverage score: 10
- Ease/reversibility score: 2
- Effort: L
- Primary owner: B05
- Cross-module: F08 provider core, F09 host adapter, F03A CRM, B07 LINE
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs:39
  - SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:113
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:135
- Evidence: callback state interpretation, CRM fee mutation, LINE notification,
  retry keys, and legacy direct dependency construction are spread across B05
  services and processor partials.
- Control/data/lifetime flow: F08/F09 callback result -> B05 decisions -> direct
  F03A/CRM and B07/LINE calls with mixed synchronous and legacy lifetimes.
- Impact: idempotency, callback latency, and notification changes cannot be tested
  or rolled out independently behind one B05 contract.
- Why this is necessary: B05 owns payment orchestration but must not absorb provider,
  generic CRM, or LINE transport ownership.
- Recommended action: define B05-owned state-transition, CRM mutation, and
  notification-decision ports with injected F03A/B07/F09 adapters.
- Validation: provider fixtures, state-transition tests, fake CRM mutation tests,
  and B07 notification contract tests.
- Rollback boundary: additive ports/adapters with one workflow migrated at a time.
- Extraction contract: provider-neutral payment result/current state in; permitted
  state transition, CRM command, and notification decision out.
- CCG round history:
  - Round 1: run `20260712-124759-b05-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B05-SEC-002 Payment notification logs stable recipient and retry identifiers

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 69
- Confirmed: true
- Evidence confidence: 20
- Impact score: 15
- Likelihood/frequency score: 12
- Security urgency score: 10
- Performance gain score: 1
- Loop leverage score: 6
- Ease/reversibility score: 5
- Effort: XS
- Primary owner: B05
- Cross-module: X02B logging policy and B07 transport
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:113
  - SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:130
  - SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:134
- Evidence: success and failure logs include the full LINE user ID and deterministic
  retry key derived from payment order/status context.
- Control/data/lifetime flow: payment/recipient state -> B05 retry-key construction
  -> interpolated information/error logs.
- Impact: stable identity and payment correlation can be joined across retained log
  records.
- Why this is necessary: operational correlation can be preserved without logging
  raw recipient or retry identifiers.
- Recommended action: hash or truncate identifiers under a documented X02B/B05
  redaction policy while keeping a safe correlation token.
- Validation: log-capture test asserts no raw LINE ID/order-derived retry key and
  stable safe correlation for matching events.
- Rollback boundary: B05 notification event formatting only.
- Extraction contract: raw internal identifiers in; safe one-way correlation values
  out.
- CCG round history:
  - Round 1: run `20260712-124759-b05-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B05-PERF-002 Legacy donation processor bypasses host-managed dependency lifetimes

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 68
- Confirmed: true
- Evidence confidence: 18
- Impact score: 16
- Likelihood/frequency score: 11
- Security urgency score: 5
- Performance gain score: 7
- Loop leverage score: 9
- Ease/reversibility score: 2
- Effort: M
- Primary owner: B05
- Cross-module: F03A CRM, B07 LINE, F09 host adapter
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:119
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:135
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:192
- Evidence: legacy constructors own configuration/LINE utilities and call
  `ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")` instead of consuming the
  host-managed CRM/payment/notification adapters.
- Control/data/lifetime flow: legacy processor construction -> direct ToolUtility
  factory and utility graph -> CRM/LINE operations outside host lifetime control.
- Impact: connection/client reuse, disposal, test isolation, and call-count
  measurement remain unclear.
- Why this is necessary: the B05 orchestration boundary must consume provider
  lifetimes rather than constructing them.
- Recommended action: inject the B05 ports from B05-EXT-001 and retain legacy
  constructors only as compatibility adapters during migration.
- Validation: DI lifetime/resolution test and fake adapter call-count tests with no
  external CRM/LINE access.
- Rollback boundary: processor construction and adapter wiring only.
- Extraction contract: host-managed B05/F03A/B07/F09 ports in; no direct factory
  construction in business flow.
- CCG round history:
  - Round 1: run `20260712-124759-b05-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

## Runtime Validation Pending

### B05-SEC-003 Callback state-transition idempotency needs runtime proof

- Confirmed: false
- Evidence: `MyPayController.cs:90-153` and `PaymentCrmService.cs:39-82` update fee
  state and notification decisions, but static inspection did not prove behavior
  for duplicate or out-of-order callbacks.
- Required validation: duplicate success, failed-after-success,
  success-after-failure, replay, and unknown-order fixtures; retain as pending until
  the transition contract is proven.

## Deleted Or Rejected Candidates

- No additional confirmed B05 issue was retained from the static pass.

## Cross-Module Handoffs

- F08 owns provider protocol; F09 owns host/provider adapter composition; F03A owns
  generic CRM access; B07 owns LINE transport. B05 owns payment decisions and the
  orchestration ports that consume those modules.

## Final CCG Approval

`DEGRADED_REVIEW_PENDING`; round 1 produced no usable backend output.
