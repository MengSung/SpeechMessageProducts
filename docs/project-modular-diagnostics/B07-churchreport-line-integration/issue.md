# B07 ChurchReport LINE Integration Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: B07
Workspace: B07-churchreport-line-integration
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 5c101b6ac128c80ac5cf9bf6106ca08f351ee4e85b1f402641817f03a0599265

## Executive Summary

B07 has four confirmed security findings, three performance/lifetime findings,
and two extraction candidates. Generic F04-F07 workflow internals and B05 payment
decisions remain outside B07. Legacy RichMenu/media observations without a
reachable caller are retained as rejected candidates, not ranked issues.

## Ranked Confirmed Issues

### B07-SEC-001 Operational LINE recipient identifiers are hard-coded

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 76
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 12
- Security urgency score: 13
- Performance gain score: 1
- Loop leverage score: 8
- Ease/reversibility score: 5
- Effort: XS
- Primary owner: B07
- Cross-module: X04A runtime configuration
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:29
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:56
  - SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs:114
- Evidence: the same operational LINE user ID is compiled into the admin service
  and two legacy utilities.
- Control/data/lifetime flow: compiled recipient constant -> B07 notification
  facade/utility -> outbound LINE delivery destination.
- Impact: deployment-specific routing cannot be audited or changed independently,
  and a stale recipient can receive operational notifications.
- Why this is necessary: routing identifiers are not credentials, but they are
  production configuration and must be validated per environment.
- Recommended action: move recipients to validated B07 options with explicit
  environment ownership and startup checks.
- Validation: configuration tests plus a non-production recipient smoke defined in
  `evidence/runtime-validation-plan.md`.
- Rollback boundary: B07 options binding and recipient resolution only.
- Extraction contract: notification purpose/environment in; validated recipient
  set out.
- CCG round history:
  - Round 1: run `20260711-171819-b07-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B07-SEC-002 Binding URL exposes display name and LINE user ID in the path

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 81
- Confirmed: true
- Evidence confidence: 19
- Impact score: 22
- Likelihood/frequency score: 12
- Security urgency score: 14
- Performance gain score: 2
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B07
- Cross-module: B01 binding/session decision and X04A public host configuration
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:80
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:135
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:163
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:165
- Evidence: B07 uses a compiled public host and appends URL-encoded display name
  and LINE user ID directly to the binding route path.
- Control/data/lifetime flow: LINE profile -> B07 URL builder -> message -> browser,
  proxy, referrer, and server path logs -> binding endpoint.
- Impact: stable identity and display-name data can persist in transport and access
  logs beyond the intended binding workflow.
- Why this is necessary: URL encoding preserves syntax but does not make identity
  values opaque or short-lived.
- Recommended action: configure the public host and replace path identity with an
  opaque, expiring, single-use binding state token.
- Validation: assert generated URLs contain no display name/user ID and reject
  expired/replayed state in the B07/B01 binding smoke.
- Rollback boundary: B07 binding URL generation while preserving the endpoint
  compatibility adapter.
- Extraction contract: binding subject and expiry in; opaque one-time URL out.
- CCG round history:
  - Round 1: run `20260711-171819-b07-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B07-SEC-003 Best-effort admin notification swallows all send failures

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 66
- Confirmed: true
- Evidence confidence: 19
- Impact score: 14
- Likelihood/frequency score: 11
- Security urgency score: 9
- Performance gain score: 2
- Loop leverage score: 6
- Ease/reversibility score: 5
- Effort: XS
- Primary owner: B07
- Cross-module: X02B observability
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:100
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:112
- Evidence: the admin facade sends through the workflow and catches every exception
  without recording a sanitized failure signal.
- Control/data/lifetime flow: operational alert -> LINE workflow -> exception ->
  empty catch -> caller observes best-effort completion.
- Impact: failed security/operational alerts disappear without an auditable metric
  or correlation event.
- Why this is necessary: best-effort delivery may be intentional, but invisible
  failure prevents operators from distinguishing delivery from loss.
- Recommended action: preserve non-throwing behavior while emitting redacted
  structured telemetry and a failure counter.
- Validation: force a non-production send failure and assert one sanitized event
  with no token or recipient value.
- Rollback boundary: telemetry around B07 admin sends only.
- Extraction contract: send result/exception category in; redacted outcome event
  and metric out.
- CCG round history:
  - Round 1: run `20260711-171819-b07-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B07-SEC-004 Binding and token diagnostics disclose sensitive operational detail

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 65
- Confirmed: true
- Evidence confidence: 18
- Impact score: 14
- Likelihood/frequency score: 10
- Security urgency score: 10
- Performance gain score: 2
- Loop leverage score: 6
- Ease/reversibility score: 5
- Effort: XS
- Primary owner: B07
- Cross-module: X02B logging policy and X04A configuration
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineBindingUtility.cs:977
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineBindingUtility.cs:986
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:89
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:95
- Evidence: binding diagnostics log faith-status mapping and exception messages;
  token configuration diagnostics log raw exception text.
- Control/data/lifetime flow: member/config state and exceptions -> interpolated
  debug/trace messages -> process listeners and collected logs.
- Impact: contact attributes and provider/configuration detail can be retained in
  broad logs.
- Why this is necessary: B07 handles identity-adjacent and provider data that needs
  a consistent redaction boundary.
- Recommended action: log stable error categories and correlation IDs; redact
  member values, recipients, tokens, and raw provider/config exception text.
- Validation: static forbidden-field scan and induced-error log assertions.
- Rollback boundary: B07 logging statements and event mapping only.
- Extraction contract: B07 event category and safe metadata in; redacted structured
  event out.
- CCG round history:
  - Round 1: run `20260711-171819-b07-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B07-PERF-001 Legacy notification sends create unobserved async work

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 80
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 14
- Security urgency score: 4
- Performance gain score: 9
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B07
- Cross-module: B03 and other legacy notification consumers
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:111
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:129
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:146
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:163
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:195
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:226
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:477
- Evidence: seven active call sites invoke `MultiCastTextMessageAsync` without await,
  return tracking, cancellation, or bounded dispatch.
- Control/data/lifetime flow: request/business flow -> unobserved multicast task ->
  LINE network I/O continuing beyond caller lifetime -> lost failure/completion.
- Impact: concurrent request paths can accumulate uncontrolled work and delivery
  failures are not observable.
- Why this is necessary: explicit completion or bounded queue ownership is required
  before notification throughput can be measured or controlled.
- Recommended action: expose async methods end-to-end or enqueue a durable/bounded
  best-effort command with cancellation and outcome telemetry.
- Validation: load-test fan-out, bounded concurrency, cancellation, and failure
  observation as defined in the runtime plan.
- Rollback boundary: migrate one legacy consumer at a time through an adapter.
- Extraction contract: recipient/message/idempotency/cancellation in; awaited send
  result or bounded queue receipt out.
- CCG round history:
  - Round 1: run `20260711-171819-b07-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B07-PERF-002 Admin facade blocks request threads on LINE network latency

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 74
- Confirmed: true
- Evidence confidence: 20
- Impact score: 19
- Likelihood/frequency score: 12
- Security urgency score: 3
- Performance gain score: 8
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: S
- Primary owner: B07
- Cross-module: consumers of ChurchReportLineAdminNotificationService
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:100
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:110
- Evidence: synchronous admin notification calls async workflow delivery through
  `.GetAwaiter().GetResult()`.
- Control/data/lifetime flow: synchronous caller -> blocked request/thread-pool
  thread -> LINE network task -> synchronous continuation or swallowed exception.
- Impact: LINE latency consumes request threads and can amplify saturation under
  concurrent notifications.
- Why this is necessary: B07 already depends on async workflows, so the facade must
  preserve asynchronous lifetime rather than block it.
- Recommended action: make the facade async end-to-end or route synchronous legacy
  callers through the bounded dispatcher from B07-PERF-001.
- Validation: compare blocked-thread count and request latency before/after under
  non-production LINE delay.
- Rollback boundary: async facade overload plus compatibility adapter.
- Extraction contract: notification request/cancellation in; async result or queue
  receipt out.
- CCG round history:
  - Round 1: run `20260711-171819-b07-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B07-PERF-003 Legacy utilities repeatedly construct LINE clients and workflows

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 61
- Confirmed: true
- Evidence confidence: 17
- Impact score: 15
- Likelihood/frequency score: 10
- Security urgency score: 2
- Performance gain score: 7
- Loop leverage score: 7
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B07
- Cross-module: F04 client implementation and F06/F07 workflows
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:65
  - SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs:190
  - SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs:282
  - SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs:293
- Evidence: legacy utility construction creates `LineMessagingClient` and multiple
  workflow graphs instead of consuming one host-managed B07 facade.
- Control/data/lifetime flow: legacy utility instance -> new LINE client/processors
  and workflows -> outbound calls with lifetime tied to utility construction.
- Impact: handler/socket reuse and disposal are unclear, and repeated graph creation
  adds allocation and connection-pressure risk.
- Why this is necessary: one explicit facade lifetime is needed before measuring
  or changing generic F04-F07 dependencies.
- Recommended action: inject a host-managed B07 facade/options contract while
  leaving generic SDK/workflow internals with F04-F07.
- Validation: measure client/handler/socket counts under repeated facade resolution.
- Rollback boundary: B07 composition adapters; no F04-F07 ownership transfer.
- Extraction contract: host-managed generic workflows/options in; B07 profile,
  binding, push, reply, and catalog facade out.
- CCG round history:
  - Round 1: run `20260711-171819-b07-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B07-EXT-001 ChurchReport LINE routing lacks one validated facade/options contract

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 75
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 12
- Security urgency score: 6
- Performance gain score: 6
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B07
- Cross-module: X04A configuration and F04-F07 generic LINE providers
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:78
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:25
  - SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs:190
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:65
- Evidence: recipients, binding host, profile, push/reply, workflow construction,
  and legacy catalog concerns are split across services and broad utilities.
- Control/data/lifetime flow: ChurchReport caller -> one of several B07 services or
  utilities -> generic F04-F07 dependency with inconsistent options/lifetime.
- Impact: security, lifetime, and send-semantics fixes must be repeated and consumer
  migrations cannot be validated behind one boundary.
- Why this is necessary: B07 needs a cohesive application adapter without taking
  ownership of generic SDK/workflow internals.
- Recommended action: define validated B07 options and one facade for routing,
  profile, binding, push/reply semantics, and legacy catalog configuration.
- Validation: B07 provider tests and existing push/reply/profile adapter tests.
- Rollback boundary: additive facade with legacy adapters.
- Extraction contract: B07 use-case request in; generic workflow request/result out.
- CCG round history:
  - Round 1: run `20260711-171819-b07-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B07-EXT-002 Notification consumers need a staged async or queued migration seam

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 73
- Confirmed: true
- Evidence confidence: 19
- Impact score: 17
- Likelihood/frequency score: 12
- Security urgency score: 4
- Performance gain score: 8
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: L
- Primary owner: B07
- Cross-module: B02/B03/B05 legacy consumers; B05 retains payment decisions
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:111
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:477
  - SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:100
- Evidence: synchronous and fire-and-forget semantics coexist across legacy callers,
  so replacing the utility in one step would change cross-module behavior.
- Control/data/lifetime flow: multiple business owners -> legacy notification
  methods -> inconsistent sync/unobserved async delivery -> LINE workflow.
- Impact: performance and reliability fixes cannot be rolled out independently or
  measured per consumer.
- Why this is necessary: a staged seam preserves each owner's content/timing while
  B07 standardizes transport, queueing, cancellation, and outcomes.
- Recommended action: add an async/queued B07 port and migrate consumers one at a
  time with per-consumer delivery tests and retry-key propagation.
- Validation: consumer contract tests plus bounded queue/failure metrics.
- Rollback boundary: per-consumer adapter switch; preserve old utility until all
  consumer gates pass.
- Extraction contract: owner-selected content/recipient/idempotency in; B07
  transport outcome or durable queue receipt out.
- CCG round history:
  - Round 1: run `20260711-171819-b07-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

## Runtime Validation Pending

- Provider smoke, binding-token behavior, delivery failure telemetry, async fan-out,
  and client lifetime measurements remain pending per
  `evidence/runtime-validation-plan.md`.

## Deleted Or Rejected Candidates

- B07-PERF-004 legacy RichMenu local image path: rejected as a ranked issue because
  no reachable production caller was proven; retain it as catalog preflight debt.
- Dormant reply media disposal: rejected until the media path is reactivated or a
  reachable caller is demonstrated.
- No B07 credential literal was confirmed; operational recipient IDs are handled by
  B07-SEC-001 without calling them secrets.

## Cross-Module Handoffs

- F04-F07 retain generic SDK, workflow, and RichMenu engine ownership.
- B05 retains payment notification decision/content; B07 owns transport semantics.
- B01 retains login/session/binding decisions; B07 owns delivery and opaque URL
  construction after a B01 state contract is approved.

## Final CCG Approval

`DEGRADED_REVIEW_PENDING`; round 1 produced no usable backend output.
