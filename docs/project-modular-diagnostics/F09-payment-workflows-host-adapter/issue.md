# F09 Payment Workflows Host Adapter Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F09
Workspace: F09-payment-workflows-host-adapter
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: e8a0247309d3d9076bf59fd7e2e582d663c516fa3a9eb7d8faaabbaf1e0fbe39

Pre-CCG issue document SHA-256: E215EE494E47B35A087818B6D840C60A20DE0E2901F8072413B30E84030B83D2
Nested agent count: 0

## Executive Summary

F09 is a small, cohesive payment workflow and ASP.NET Core adapter module. The
HTTP mapping, acknowledgement mapping, request factory, and result mapper are
mostly clean host-adapter seams over F08 provider core contracts.

One confirmed payment-integrity issue remains: the reusable post-payment
workflow has no idempotent side-effect contract, while current ChurchReport
payment callbacks expose front-channel and back-channel routes that can both
reach the same workflow for the same order. Provider retries or duplicated
callback delivery can therefore make CRM updates and payer notifications depend
on every consumer manually deduplicating side effects, which is not a safe
contract for a shared payment workflow.

## Ranked Confirmed Issues

### F09-SEC-001 Post-payment workflow lacks an idempotent side-effect contract

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 75
- Confirmed: true
- Evidence confidence: 18
- Impact score: 20
- Likelihood/frequency score: 13
- Security urgency score: 10
- Performance gain score: 5
- Loop leverage score: 6
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F09
- Cross-module: B05 consumer evidence; F08 replay guard is complementary but not sufficient
- Gate blocked: false
- Files:
  - SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:25
  - SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:43
  - SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:48
  - SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:53
  - SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs:91
  - SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs:123
  - SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs:251
  - SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs:277
  - SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:119
  - SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:139
  - SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs:145
  - SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs:54
  - SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs:84
  - SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs:61
  - SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:201

- Evidence:

- `PaymentPostPaymentWorkflow` captures all registered record updaters and payer
  notifiers in the constructor, but the constructor accepts no idempotency store,
  operation key provider, duplicate detector, or per-handler checkpoint contract.
- `ExecuteAsync` always loops through every record updater and then every payer
  notifier. It returns booleans based only on whether handlers are registered,
  not whether the current payment event was already processed.
- `TSPGController.PostBack` maps a successful callback to
  `HandleSuccessfulPaymentReturnAsync`, which calls
  `ExecutePostPaymentWorkflowAsync`.
- `TSPGController.ResultUrl` separately maps the callback result and calls
  `ExecutePostPaymentWorkflowAsync` before returning provider acknowledgement.
  The two actions represent Taishin front-channel and backend notification
  paths for the same order.
- `MyPayController.PaymentNotify` also maps the callback result, builds a
  post-payment context, and invokes the same F09 workflow.
- B05 handlers show real side effects behind the F09 interfaces:
  `ChurchReportPaymentRecordUpdater.UpdateAsync` updates a CRM entity, and
  `ChurchReportPaymentPayerNotifier.NotifyAsync` sends success or failure payer
  notifications.
- `PaymentCrmService.UpdateFeeEntityWithPaymentResult` appends provider result
  details to the CRM description every time it is called.
- `PaymentNotificationService` builds deterministic LINE retry keys for payer
  notices, which helps downstream LINE retry semantics, but it does not prevent
  F09 from re-running the CRM update and notifier handler.

- Control/data/lifetime flow:

Provider callback request -> F09 `PaymentHttpRequestMapper` ->
F08 `IPaymentGateway.ParseCallbackAsync` -> F09 `PaymentWorkflowResultMapper` ->
F09 `PaymentPostPaymentWorkflow.ExecuteAsync` -> B05 CRM updater and payer
notifier handlers. The F09 workflow has no idempotency boundary between a
normalized payment event and its side-effect handlers.

- Impact:

- A provider retry, duplicated callback delivery, or a normal provider
  front-channel plus back-channel flow can cause the same F09 workflow to run
  more than once for the same order when the consumer routes both events through
  post-payment processing.
- CRM descriptions can accumulate duplicate payment result blocks, increasing
  audit noise and storage churn.
- Payer notification code is invoked repeatedly. Current LINE retry keys may
  reduce duplicate delivery for LINE, but F09 does not guarantee that for all
  current or future notifier implementations.
- The shared workflow contract makes correctness depend on B05/X01 consumers
  remembering to implement their own duplicate guards for every handler.

- Why this is necessary:

F08 should still own provider replay and callback binding. F09 nevertheless
owns the post-payment side-effect pipeline. A payment workflow should expose an
idempotent execution seam so downstream products can safely process provider
retries, browser returns, backend notifications, and future provider callback
variants without duplicating CRM updates or notifications.

- Recommended action:

- Add an F09-owned idempotency contract to the post-payment workflow, for
  example an operation key derived from provider profile, product order id,
  provider transaction id, and normalized status.
- Make `PaymentPostPaymentContext` carry the operation key or add a companion
  execution request model.
- Add a small interface such as `IPaymentPostPaymentExecutionStore` or a
  handler-level checkpoint contract so consumers can atomically decide
  `Started`, `AlreadyCompleted`, `Completed`, or `Failed`.
- Return per-handler execution results instead of booleans based only on handler
  registration.
- Keep B05 product-specific storage decisions in B05, but make the F09 workflow
  require the idempotency decision before dispatching side-effect handlers.

- Validation:

- Add F09 workflow tests proving two executions with the same operation key run
  CRM updater/notifier side effects once.
- Add consumer tests for TSPG `post-back` plus `result-url` with the same order.
- Add a failure-then-success scenario to define whether the status transition is
  allowed, skipped, or requires human reconciliation.

- Rollback boundary:

The change can be isolated to F09 workflow contracts plus B05 handler
registration/adapters. If the new guard blocks valid events, rollback by
disabling the store/checkpoint implementation while keeping the old sequential
handler dispatch.

- Extraction contract:

Input: normalized payment result plus product context and operation key.
Output: per-handler execution status and durable idempotency decision.
Dependency seam: F09 interface, B05 implementation.
Test seam: in-memory idempotency store for workflow unit tests.
Consumer: B05 payment callback and X01 host DI registration.

- CCG round history:

  - Round 1: Claude KEEP; Gemini quota/billing blocked with HTTP 403 and produced
    no usable output; degraded fallback accepted. Claude requested impact wording
    be tightened and the category be framed as payment integrity under Security.

## Runtime Validation Pending

No runtime validation was executed because this assignment explicitly forbids
restore, build, test, benchmark, codegen, formatting, and generated-output
commands. See `evidence/runtime-validation-plan.md`.

## Deleted Or Rejected Candidates

- Redirect acknowledgement open redirect: rejected. F08 exposes
  `PaymentCallbackAcknowledgement.Redirect`, F09 maps it to `RedirectResult`,
  and a unit test uses an external URL. However, read-only inspection found no
  production provider parser emitting redirect acknowledgements from callback
  data. Without a current untrusted source-to-redirect sink, this is not an
  immediately actionable security issue.
- Header/raw payload logging leak from `PaymentHttpRequestMapper`: rejected.
  The mapper carries headers and raw body in `PaymentCallbackRequest`, but
  current provider parsers consume form/query/body fields, provider diagnostics
  are sanitized, and `PaymentCallbackLogger` deliberately logs only order,
  transaction, status, error kind, and amount.
- Raw-body allocation in `PaymentHttpRequestMapper`: rejected as a standalone
  issue. The mapper reads the full body and can duplicate work for form posts,
  but no current evidence shows callback payload size or frequency large enough
  to justify a retained F09 performance issue. Keep it as a future optimization
  candidate if profiling or request-size evidence appears.
- B05 sync-over-async in notification paths: rejected for F09. It is real
  consumer evidence, but the blocking calls live in B05 services/tools rather
  than the F09 primary owner scope.

## Cross-Module Handoffs

- F08: callback replay and provider authenticity/binding remain provider-core
  concerns. The F09 issue complements that by guarding downstream side effects.
- B05: product-specific CRM storage and notification content should remain in
  B05, but B05 should implement the durable idempotency store/checkpoint behind
  the F09 contract.
- X01: host composition should wire the F09 idempotency contract to the B05
  implementation during DI registration.

## Final CCG Approval

APPROVED_DEGRADED. This is not full dual-model approval. Gemini was blocked by
provider quota/billing, while Claude completed with usable output and returned
KEEP with no Critical blockers. The completed-backend Warning edits were applied
to clarify payment-integrity categorization and impact wording.
