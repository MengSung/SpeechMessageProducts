# F06 LINE Notification and Reply Workflows Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F06
Workspace: F06-line-notification-reply-workflows
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: ca6d098b26740ed3ccd11393bd147c16ddc5f8edd56f3ff8d6d9c244429c9393

Submitted issue SHA-256: CD13B2F93FDA1FEA6374EB0FF62B1DBE0996964F3214D902D47EF56ED1B63554

## Scoring Model

Each priority score is the sum of seven dimensions:

- evidence confidence: 0-20;
- impact: 0-25;
- likelihood/frequency: 0-15;
- security urgency: 0-15;
- performance gain: 0-10;
- loop leverage: 0-10;
- ease/reversibility: 0-5.

Maximum: 100.

## Executive Summary

Static read-only diagnosis confirmed six F06 issues. Public result and
exception graphs retain raw provider exceptions and sensitive workflow input,
including a complete reply request with one-time token and outbound messages.
Recipient kind is modeled but ignored during send, allowing a mislabeled valid
ID to route content to an unintended audience class. Retry keys are accepted as
arbitrary strings, and the result cannot represent accepted duplicates or
ambiguous transmission outcomes. Notification and reply message lists enforce
only nonempty input, not the provider maximum of five or non-null elements.
Neither workflow can propagate caller cancellation. Finally, notification and
reply duplicate concrete-processor validation/error normalization while
exposing inconsistent result shapes and no narrow unit-test seam.

No F06 automatic retry loop, repeated provider call, duplicate JSON
serialization, active recipient loop, or material repeated message-construction
cost was found. Optimization is not authorized.

## Ranked Confirmed Issues

### F06-SEC-001 Public Results And Exceptions Retain Sensitive Workflow Input And Raw Provider Failures

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 76
- Score arithmetic: `20 + 18 + 12 + 13 + 1 + 8 + 4 = 76`
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 12
- Security urgency score: 13
- Performance gain score: 1
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F06
- Cross-module: F04 provider detail; X02B logging; B05/B07 consumers
- Gate blocked: true
- Files:
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:47`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:53`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:54`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:80`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:81`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:21`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:38`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:45`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:47`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:51`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:53`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:55`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:58`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:74`
  - `LineMessagingProcessor.Workflows/LineReplyRequest.cs:25`
  - `LineMessagingProcessor.Workflows/LineReplyRequest.cs:27`
  - `LineMessagingProcessor.Workflows/LineReplyRequest.cs:29`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:50`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:56`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:57`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:83`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:84`
  - `LineMessagingProcessor.Workflows/LineReplyResult.cs:23`
  - `LineMessagingProcessor.Workflows/LineReplyResult.cs:30`
  - `LineMessagingProcessor.Workflows/LineReplyResult.cs:37`
  - `LineMessagingProcessor.Workflows/LineReplyResult.cs:45`
  - `LineMessagingProcessor.Workflows/LineReplyResult.cs:47`
  - `LineMessagingProcessor.Workflows/LineNotificationException.cs:21`
  - `LineMessagingProcessor.Workflows/LineNotificationException.cs:27`
  - `LineMessagingProcessor.Workflows/LineReplyException.cs:23`
  - `LineMessagingProcessor.Workflows/LineReplyException.cs:29`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:128`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:134`
- Evidence: Both workflows copy `ex.Message` and the original exception into
  public results. Notification results retain recipient, retry key, and caller
  metadata. Reply results retain the complete request by reference; that
  request contains the one-time reply token, outbound message list, and mutable
  metadata. The throwing adapters expose those results on public exceptions. A
  current consumer logs the thrown notification exception, proving exception
  logging reachability.
- Control/data/lifetime flow: provider/local failure -> F06 catches exception
  -> raw message and exception + request/recipient/metadata retained in public
  result -> result attached to public exception -> consumer logger/serializer/
  debugger can traverse the object graph after workflow completion.
- Impact: Sensitive identifiers, business metadata, message content, provider
  response text, and reply tokens have broader visibility and lifetime than the
  workflow operation requires. The repository proves retention and logging
  reachability, not observed production token exfiltration.
- Why this is necessary: Public workflow outcomes and internal diagnostics need
  separate bounded contracts.
- Recommended action: Return immutable sanitized outcomes containing stable
  status/error code and correlation only. Do not retain reply tokens, message
  graphs, raw exceptions, or caller-owned dictionaries. Send bounded provider
  detail to X02B internal diagnostics.
- Validation: Inject secret-like synthetic markers and prove they cannot be
  reached from public results/exceptions or generic structured logs; prove
  metadata is snapshotted and caller mutation cannot alter a completed result.
- Rollback boundary: Add sanitized result projections and compatibility
  adapters before removing legacy properties.
- Extraction contract: provider failure + workflow context -> sanitized public
  delivery outcome; internal exception -> X02B diagnostics.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED` by provider quota/billing HTTP 403;
    Claude `KEEP`; source reopened: true; retained unchanged.

### F06-EXT-001 Retry And Idempotency Inputs And Outcomes Are Not A Stable Contract

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 76
- Score arithmetic: `20 + 19 + 14 + 5 + 4 + 10 + 4 = 76`
- Confirmed: true
- Evidence confidence: 20
- Impact score: 19
- Likelihood/frequency score: 14
- Security urgency score: 5
- Performance gain score: 4
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F06
- Cross-module: F04 retry/status headers; F05A capability; B05/B07 policy
- Gate blocked: true
- Files:
  - `LineMessagingProcessor.Workflows/LineNotificationRequest.cs:26`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:44`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:47`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:57`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:70`
  - `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs:467`
  - `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs:476`
  - `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs:480`
  - `Line.Messaging/LineMessagingClient.cs:167`
  - `Line.Messaging/LineMessagingClient.cs:174`
  - `Line.Messaging/LineMessagingClient.cs:179`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:78`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:95`
- Evidence: F06 accepts an arbitrary string and passes it unchanged. F04 adds
  every nonblank value without format validation. The F06 test approves a
  colon-delimited value, and B05 currently generates that shape. F06 results
  expose only generic success/failure plus the original key; they have no
  accepted-duplicate, provider-correlation, throttle, definitely-not-sent, or
  delivery-ambiguous outcome.
- Control/data/lifetime flow: product builds arbitrary key -> F06 passes it ->
  F04 emits header -> provider response/status headers are reduced to generic
  success or exception -> product cannot safely decide whether to retry an
  ambiguous push.
- Impact: Current idempotency intent can fail at the provider boundary, and
  ambiguous failures cannot be separated from definite rejection. Retrying
  without a stable outcome can duplicate delivery; not retrying can lose a
  notification.
- Why this is necessary: Workflow retry policy requires validated opaque input
  and typed delivery outcomes, while HTTP/header parsing remains F04-owned.
- Recommended action: Accept only a valid UUID retry key (or a dedicated value
  type), preserve provider request correlation, and model accepted duplicate,
  throttled, definitely-not-sent, and delivery-ambiguous outcomes. Do not add
  automatic retries.
- Validation: UUID/header matrix plus 200/202/409/429/5xx and pre/post-send
  timeout fixtures; one provider call per workflow invocation.
- Rollback boundary: Add a validated retry-key value and typed outcome beside
  the string/property before consumer migration.
- Extraction contract: optional retry UUID + provider correlation/status ->
  explicit idempotent delivery outcome.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED` by provider quota/billing HTTP 403;
    Claude `KEEP`; source reopened: true; retained unchanged.

### F06-EXT-002 Outbound Message Batches Do Not Enforce Provider Cardinality Or Non-Null Elements

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 74
- Score arithmetic: `20 + 18 + 14 + 3 + 5 + 9 + 5 = 74`
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 14
- Security urgency score: 3
- Performance gain score: 5
- Loop leverage score: 9
- Ease/reversibility score: 5
- Effort: S
- Primary owner: F06
- Cross-module: F04/F05A message transport contract
- Gate blocked: true
- Files:
  - `LineMessagingProcessor.Workflows/LineNotificationContent.cs:211`
  - `LineMessagingProcessor.Workflows/LineNotificationContent.cs:218`
  - `LineMessagingProcessor.Workflows/LineNotificationContent.cs:223`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:141`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:158`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:44`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:117`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:124`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:324`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:346`
  - `Line.Messaging/ILineMessagingClient.cs:34`
  - `Line.Messaging/ILineMessagingClient.cs:58`
- Evidence: Notification and reply accept any nonempty message list. They do
  not reject six or more messages or null elements. F05A repeats only the
  nonempty check, while F04's public contract documents a maximum of five.
- Control/data/lifetime flow: caller supplies invalid list -> F06 validation
  succeeds -> list copied -> F05A accepts -> F04 serializes/provider call ->
  later serialization failure or provider rejection.
- Impact: Deterministically invalid requests consume allocations, network
  latency, provider quota, and failure-handling paths instead of failing before
  I/O. Notification and reply can diverge as validation evolves.
- Why this is necessary: The final outbound message batch is an F06 workflow
  input contract independent of individual factory guards.
- Recommended action: Introduce one immutable shared message-batch value that
  requires one to five non-null messages and snapshots the collection.
- Validation: zero, one-to-five, six, null-element, and caller-mutation tests for
  both workflows; invalid input must make zero provider calls.
- Rollback boundary: Add batch construction behind current request factories,
  then migrate request properties.
- Extraction contract: SDK message sequence -> immutable validated one-to-five
  message batch.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED` by provider quota/billing HTTP 403;
    Claude `KEEP`; source reopened: true; retained unchanged.

### F06-PERF-001 Workflow Contracts Cannot Cancel In-Flight Provider Calls

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 72
- Score arithmetic: `20 + 17 + 13 + 2 + 8 + 9 + 3 = 72`
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 13
- Security urgency score: 2
- Performance gain score: 8
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F06
- Cross-module: F04/F05A cancellation overloads; F05B/B04C/B05/B07 callers
- Gate blocked: true
- Files:
  - `LineMessagingProcessor.Workflows/ILineNotificationWorkflow.cs:21`
  - `LineMessagingProcessor.Workflows/ILineNotificationWorkflow.cs:23`
  - `LineMessagingProcessor.Workflows/ILineReplyWorkflow.cs:24`
  - `LineMessagingProcessor.Workflows/ILineReplyWorkflow.cs:26`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:44`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:65`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:70`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:44`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:68`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:73`
- Evidence: No F06 workflow method accepts cancellation, provider calls are
  tokenless, and every `TaskCanceledException` is classified as provider
  timeout/unavailability.
- Control/data/lifetime flow: caller abort/shutdown/cancel -> no F06 token ->
  active F05A/F04 network call continues -> completion or HTTP timeout ->
  cancellation-like exception labeled provider timeout.
- Impact: Abandoned notifications/replies retain network work and delay
  shutdown/resource release. A future caller token would still be
  misclassified unless the result distinguishes caller cancellation.
- Why this is necessary: Cancellation and outcome classification are part of
  the reusable async workflow contract.
- Recommended action: Add cancellation overloads, propagate through the narrow
  F05A/F04 capabilities, and model caller-cancelled separately from
  provider-timeout and delivery-ambiguous outcomes.
- Validation: Blocking handler observes notification/reply cancellation
  promptly; no later call starts; throwing adapters preserve cancellation.
- Rollback boundary: Add overloads first; current methods delegate with
  `CancellationToken.None`.
- Extraction contract: every F06 async operation accepts and propagates caller
  cancellation.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED` by provider quota/billing HTTP 403;
    Claude `KEEP`; source reopened: true; retained unchanged.

### F06-SEC-002 Recipient Kind And Identifier Are Not Validated As One Destination

- Category: Security
- Severity: Medium
- Priority: P1
- Priority score: 70
- Score arithmetic: `20 + 17 + 9 + 11 + 2 + 7 + 4 = 70`
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 9
- Security urgency score: 11
- Performance gain score: 2
- Loop leverage score: 7
- Ease/reversibility score: 4
- Effort: S
- Primary owner: F06
- Cross-module: B04C/B05/B07 recipient construction
- Gate blocked: true
- Files:
  - `LineMessagingProcessor.Workflows/LineNotificationRecipient.cs:21`
  - `LineMessagingProcessor.Workflows/LineNotificationRecipient.cs:27`
  - `LineMessagingProcessor.Workflows/LineNotificationRecipient.cs:31`
  - `LineMessagingProcessor.Workflows/LineNotificationRecipient.cs:33`
  - `LineMessagingProcessor.Workflows/LineNotificationRecipient.cs:39`
  - `LineMessagingProcessor.Workflows/LineNotificationRecipient.cs:42`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:42`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:114`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:123`
- Evidence: Recipient factories store an enum kind and IDs, but send uses only
  the first ID. Validation checks nonblank primary ID and one special `Users`
  count; it does not enforce kind/ID consistency or normalize the ID.
- Control/data/lifetime flow: integration labels a valid group/room ID as user,
  or a user ID as group/room -> F06 ignores kind -> sends the first ID through
  the provider's common `to` field -> content reaches the ID's actual audience.
- Impact: A caller mistake can misdirect sensitive content across user/group/
  room audience classes while the workflow result still reports success for
  the declared recipient object. This is not a demonstrated authorization
  bypass.
- Why this is necessary: Recipient validation is explicitly F06-owned, and the
  current discriminator has no enforcement value.
- Recommended action: Replace kind-plus-list with an immutable normalized
  single destination and enforce kind/ID consistency before provider I/O.
  Define batching/multicast separately.
- Validation: user/group/room positive and mismatch tests, whitespace/malformed
  tests, and zero provider calls on invalid destinations.
- Rollback boundary: Add validated constructors/value type behind existing
  factories before removing `Ids`/`PrimaryId`.
- Extraction contract: `{kind, id}` -> validated immutable destination.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED` by provider quota/billing HTTP 403;
    Claude `KEEP`; source reopened: true; retained unchanged.

### F06-EXT-003 Notification And Reply Duplicate Concrete Workflow Normalization And Expose Inconsistent Results

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 70
- Score arithmetic: `20 + 17 + 12 + 3 + 5 + 9 + 4 = 70`
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 12
- Security urgency score: 3
- Performance gain score: 5
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F06
- Cross-module: F05A processor capability; F05B composition
- Gate blocked: true
- Files:
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:25`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:27`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:47`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:81`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:27`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:29`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:50`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:84`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:21`
  - `LineMessagingProcessor.Workflows/LineNotificationResult.cs:74`
  - `LineMessagingProcessor.Workflows/LineReplyResult.cs:23`
  - `LineMessagingProcessor.Workflows/LineReplyResult.cs:58`
  - `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs:537`
  - `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs:540`
- Evidence: Both workflows inject the whole concrete F05A compatibility class
  and duplicate the same four exception catches with operation-specific error
  strings. Notification projects selected request fields; reply retains the
  complete request. Tests require concrete F04/F05A plus an HTTP handler, and
  no reply workflow test exists.
- Control/data/lifetime flow: F06 workflow -> concrete F05A -> concrete/fake F04
  HTTP path -> duplicated catch matrix -> divergent public result graph. A
  provider classification change must be repeated in both workflows and tested
  through transport.
- Impact: Error semantics, redaction, cancellation, and retry outcomes can
  drift between notification and reply. F06 cannot unit-test only its workflow
  policy through a narrow capability.
- Why this is necessary: F05A owns the processor interface, while F06 owns
  consistent notification/reply validation and result normalization.
- Recommended action: Consume a narrow F05A send/reply capability; extract
  shared immutable recipient/message/result workflow modules and one provider
  outcome normalizer. Keep push/reply operation-specific policy explicit.
- Validation: Fake-capability table tests cover both workflows and every shared
  outcome; selected capturing-handler tests preserve payload shape.
- Rollback boundary: Add capability/normalizer beside existing constructors and
  migrate notification and reply separately.
- Extraction contract: validated operation input + narrow provider outcome ->
  one sanitized F06 delivery outcome.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED` by provider quota/billing HTTP 403;
    Claude `KEEP`; source reopened: true; retained unchanged.

## Runtime Validation Pending

- Actual structured-logging projection of result/exception graphs.
- Exact provider error-body sensitivity.
- Rate of recipient kind/ID mismatch.
- Rate of invalid retry keys, accepted duplicates, throttling, and ambiguous
  outcomes.
- Cancellation latency and abandoned-call duration.
- Demand and safe limits for explicit multi-recipient batching.

These measurements refine impact. They do not negate the confirmed static
retention, validation, cancellation, and cohesion contracts.

## Deleted Or Rejected Candidates

- Missing automatic retry: rejected. Unconditional push replay can duplicate
  delivery, and reply tokens are one-time.
- Repeated provider calls: rejected. Notification and reply each invoke F05A
  once per workflow call.
- Duplicate JSON serialization: rejected. F06 copies message lists; F04
  serializes once.
- `ToList` copy as a standalone performance issue: rejected. It becomes a
  bounded maximum-five copy after F06-EXT-002.
- Active N+1 recipient loop: rejected. F06 has no recipient loop and rejects
  `Users` counts other than one.
- Missing batching as an immediate defect: not promoted. The current workflow
  is explicitly single-recipient; future multicast/fan-out needs a separate
  bounded contract with partial results and cancellation.
- Reply-token authorization bypass: rejected. F06 requires a nonblank token;
  no bypass around provider token validation was found.
- Channel access token leakage: rejected. F06 neither resolves nor exposes the
  channel credential.
- Factory validation is entirely absent: rejected. Shared helpers enforce
  required values, HTTPS URLs, coordinates, action counts, and ranges; the
  retained gap is the final message-batch contract.
- ChurchReport profile/CRM lookup, RichMenu, processor credential/lifetime,
  SDK transport, and ASP.NET composition findings: excluded by ownership.

## Cross-Module Handoffs

1. F04: provider request IDs, accepted-duplicate/throttle/ambiguous status,
   bounded errors, and cancellation-capable HTTP operations.
2. F05A: narrow send/reply capability with cancellation.
3. F05B: register the new capability and workflow adapters.
4. B04C/B05/B07: recipient construction and product retry/idempotency policy.
5. X02B: sanitized internal diagnostics and correlation.
6. X02C: cancellation, allocation, invalid-input, and future batch
   measurements.

## Final CCG Approval

Final CCG disposition: `APPROVED_DEGRADED`

- Run ID: `20260710-230734-f06-issue-review-r1-reviewer`
- Summary:
  `.ccg/dual-model-runs/20260710-230734-f06-issue-review-r1-reviewer/summary.json`
- Prompt:
  `.ccg/dual-model-runs/F06-issue-review-r1-input.md`
- Generated task:
  `.ccg/dual-model-runs/f06-issue-review-r1-reviewer.md`
- Submitted issue SHA-256:
  `CD13B2F93FDA1FEA6374EB0FF62B1DBE0996964F3214D902D47EF56ED1B63554`
- Runner summary: `ok=false`, `degradedFallback=true`,
  `fallbackAccepted=true`, `quotaBlocked=true`,
  `completedBackends=["claude"]`, `failedBackends=["gemini"]`.
- Gemini: provider quota/billing blocked with HTTP 403 and no usable output.
- Claude: usable output; `KEEP` for `F06-SEC-001`, `F06-EXT-001`,
  `F06-EXT-002`, `F06-PERF-001`, `F06-SEC-002`, and `F06-EXT-003`;
  unresolved Critical: 0; unresolved Warning: 0; final verdict `APPROVE`.
- CCG-required rewrites: 0 of maximum 3.
- Retained confirmed issues: 6.
- Deleted after CCG: 0.
- Issue-level runtime-validation verdicts: 0.
- Nested agent count: 0.

This is accepted single-model fallback, not completed dual-model approval.
