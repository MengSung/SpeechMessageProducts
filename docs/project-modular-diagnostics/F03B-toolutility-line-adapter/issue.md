# F03B ToolUtility LINE Adapter Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F03B
Workspace: F03B-toolutility-line-adapter
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 30ab7e3d758908e3105549cbcb449ce1ec028617bf30672a963820d0bc8a8b32

## Executive Summary

Static read-only diagnosis confirmed five F03B issues. The adapter durably
persists full recipient/message data before LINE delivery succeeds; exposes
fragmented transport and duplicate CRM audit contracts; has incompatible
failure semantics that the sole explicit consumer invokes fire-and-forget;
performs serial per-recipient CRM I/O before one multicast request; and leaves
concrete SDK client ownership ambiguous, with the current consumer using the
obsolete internally-owned `HttpClient` path without disposal.

No channel token is exposed by F03B source, typed F04 serialization occurs once
per request, the CRM singleton prevents per-message CRM-client creation, and
the unreferenced legacy RichMenu methods were not overstated as active defects.
Optimization is not authorized. The provider gate is blocked by the
ToolUtility/ToolUtility.Tests target mismatch, solution exclusion, and a
F03B-owned subject-test constructor mismatch.

## Ranked Confirmed Issues

### F03B-SEC-001 Full Recipient And Message Data Is Persisted Before Delivery

- Category: Security
- Severity: High
- Priority: P0
- Priority score: 89
- Confirmed: true
- Evidence confidence: 20
- Impact score: 24
- Likelihood/frequency score: 15
- Security urgency score: 14
- Performance gain score: 4
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F03B
- Cross-module: F03A audit persistence; B07/B03 data policy; X04A privacy policy
- Gate blocked: true
- Files:
  - `ToolUtility/PushUtility.cs:58`
  - `ToolUtility/PushUtility.cs:64`
  - `ToolUtility/PushUtility.cs:82`
  - `ToolUtility/PushUtility.cs:89`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:60`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:42`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:43`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:58`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:191`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:473`
- Evidence: Text push and multicast call a compile-time-enabled CRM trace before
  the LINE request. The trace stores the full message and recipient LINE ID in a
  durable `letter` record. The consumer supplies member names and weekly-report
  content.
- Control/data/lifetime flow: B07/B03 business content and CRM-derived
  recipients -> F03B `PushUtility` -> F03B/F03A contact lookup and `letter`
  creation -> F04 LINE request. Failure after persistence leaves a sent-shaped
  record without delivery status.
- Impact: Sensitive notification content and recipient identifiers cross into
  durable CRM storage without minimization, retention, or delivery-state
  contract. Failed sends can be represented as successful activity.
- Why this is necessary: Delivery and audit are separate security/data-policy
  decisions; implicit pre-delivery full-content storage violates that boundary.
- Recommended action: Introduce explicit audit policy and typed delivery result;
  persist a minimized post-delivery record with status/correlation, and keep
  full content only when an approved policy requires it.
- Validation: Synthetic contract tests inspect CRM entities for minimization and
  prove failed sends cannot create successful audit records; human policy review
  confirms retention and ACLs.
- Rollback boundary: Add the new audit path beside legacy behavior; migrate the
  sole consumer before disabling legacy full-content tracing.
- Extraction contract: typed recipient/content + delivery result -> minimized
  audit record; F03A owns repository, F03B owns compatibility mapping.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

### F03B-EXT-001 The Adapter Has Fragmented Transport And Duplicate Audit Contracts

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 81
- Confirmed: true
- Evidence confidence: 20
- Impact score: 21
- Likelihood/frequency score: 15
- Security urgency score: 5
- Performance gain score: 6
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F03B
- Cross-module: F03A, F03Q, F04, F06
- Gate blocked: true
- Files:
  - `ToolUtility/PushUtility.cs:29`
  - `ToolUtility/PushUtility.cs:32`
  - `ToolUtility/PushUtility.cs:44`
  - `ToolUtility/LineMessaging/ILineMessageService.cs:20`
  - `ToolUtility/LineMessaging/LineMessageService.cs:34`
  - `ToolUtility/LineMessaging/LineMessageService.cs:41`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:40`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:58`
  - `ToolUtility/Core/ToolUtilityFacade.cs:146`
  - `ToolUtility/Core/ToolUtilityFacade.cs:527`
- Evidence: `PushUtility` sends through concrete F04 and audits through a global
  CRM singleton using `letter`. The separately named `ILineMessageService` does
  not send LINE messages; it writes a different `linemessage` schema and is
  wired through excluded F03Q.
- Control/data/lifetime flow: consumers cannot request one coherent adapter.
  Transport, audit, singleton CRM state, and facade compatibility are selected
  by which legacy method is called.
- Impact: There is no stable contract for delivery status, retry, cancellation,
  audit schema, or dependency lifetime. Changes cannot be validated or migrated
  independently.
- Why this is necessary: F03B is the declared ToolUtility LINE adapter owner,
  but its public abstractions describe persistence and transport inconsistently.
- Recommended action: Define one narrow compatibility adapter over
  F04 `ILineMessagingClient` or F06 workflow and a separate F03A audit port;
  retain legacy APIs as migration wrappers.
- Validation: Contract tests prove one audit schema, one typed result model, and
  no direct global factory dependency in the clean adapter.
- Rollback boundary: Introduce beside legacy surfaces; migrate F03Q and B07
  separately before removing old schemas.
- Extraction contract: typed recipient/content/importance/retry/cancellation ->
  typed delivery result; optional minimized audit dependency.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

### F03B-EXT-002 Failure Semantics Are Inconsistent And The Consumer Drops Tasks

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 79
- Confirmed: true
- Evidence confidence: 20
- Impact score: 22
- Likelihood/frequency score: 14
- Security urgency score: 5
- Performance gain score: 5
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F03B
- Cross-module: B07 consumer; F06 result/failure classification
- Gate blocked: true
- Files:
  - `ToolUtility/PushUtility.cs:47`
  - `ToolUtility/PushUtility.cs:51`
  - `ToolUtility/PushUtility.cs:93`
  - `ToolUtility/PushUtility.cs:97`
  - `ToolUtility/PushUtility.cs:115`
  - `ToolUtility/PushUtility.cs:119`
  - `ToolUtility/PushUtility.cs:230`
  - `ToolUtility/PushUtility.cs:234`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:111`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:129`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:477`
- Evidence: text/list/multicast methods rethrow, while multiple media/template
  methods suppress exceptions and return normally. The explicit consumer calls
  multicast from synchronous methods without awaiting or observing the task.
- Control/data/lifetime flow: synchronous B07 method starts F03B task -> F03B
  performs synchronous audit then awaits LINE -> caller returns -> later
  exception is unobserved; surrounding synchronous `try/catch` cannot classify
  it.
- Impact: Required notifications can be silently lost, callers cannot choose
  reliable versus best-effort behavior, and CRM audit may already exist.
- Why this is necessary: A reusable adapter must make delivery outcome explicit
  and consistent across message kinds.
- Recommended action: Return a typed result for every send, require explicit
  best-effort/reliable policy, support retry key/cancellation, and migrate B07
  to await the operation.
- Validation: fake handler tests cover every message kind, failure class,
  cancellation, and consumer awaiting; no unobserved task remains.
- Rollback boundary: Add new result-returning methods and migrate call sites one
  workflow at a time; retain legacy swallowing wrappers where compatibility is
  explicitly required.
- Extraction contract: F06-style delivery request/result; B07 owns business
  decision to continue or fail.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

### F03B-PERF-001 Multicast Performs Serial Per-Recipient CRM I/O Before One LINE Request

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 79
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 14
- Security urgency score: 3
- Performance gain score: 10
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F03B
- Cross-module: F03A batching/query; X02C runtime measurement
- Gate blocked: true
- Files:
  - `ToolUtility/PushUtility.cs:82`
  - `ToolUtility/PushUtility.cs:89`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:72`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:74`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:81`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:99`
- Evidence: The multicast path completes a sequential recipient loop before the
  F04 multicast call. Each matched recipient costs one CRM lookup and one CRM
  create, producing up to `2N` synchronous CRM network operations.
- Control/data/lifetime flow: recipient list -> serial CRM lookup/create loop ->
  one F04 serialization and LINE request.
- Impact: Latency and CRM load grow linearly with recipient count; one CRM
  failure blocks all LINE delivery.
- Why this is necessary: LINE already accepts a bounded recipient batch, so
  adapter-side serial auditing defeats the transport's batching advantage.
- Recommended action: decouple delivery from audit; use one bounded post-send
  audit batch without per-recipient contact lookup on the request path.
- Validation: call-count tests and synthetic measurements at 1/10/100/500
  recipients prove one LINE request and at most one audit batch.
- Rollback boundary: preserve legacy audit behind a feature/policy switch until
  batch output is compared.
- Extraction contract: delivery result -> batch audit records; F03A owns batch
  persistence.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

### F03B-PERF-002 Concrete Client Ownership Leaves The Current Consumer With An Undisposed Internal HttpClient

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 72
- Confirmed: true
- Evidence confidence: 19
- Impact score: 18
- Likelihood/frequency score: 12
- Security urgency score: 3
- Performance gain score: 9
- Loop leverage score: 8
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F03B
- Cross-module: B07 composition; F04 client contract; X01 DI lifetime
- Gate blocked: true
- Files:
  - `ToolUtility/PushUtility.cs:29`
  - `ToolUtility/PushUtility.cs:34`
  - `Line.Messaging/LineMessagingClient.cs:118`
  - `Line.Messaging/LineMessagingClient.cs:123`
  - `Line.Messaging/LineMessagingClient.cs:126`
  - `Line.Messaging/LineMessagingClient.cs:2823`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:38`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:65`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:67`
- Evidence: F03B accepts only a concrete client and declares no ownership. The
  sole explicit consumer uses F04's obsolete token-only constructor, which
  creates an owned `HttpClient`, but the consumer is not disposable and never
  calls `Dispose`.
- Control/data/lifetime flow: each consumer construction -> new SDK client ->
  new internal `HttpClient` -> retained fields -> no deterministic disposal.
- Impact: Repeated consumer creation can retain handlers/sockets and prevents
  centralized timeout, policy, telemetry, and connection reuse.
- Why this is necessary: Client lifetime is part of an adapter contract, not an
  undocumented caller convention.
- Recommended action: inject F04 `ILineMessagingClient` or F06 workflow from
  host DI; state that F03B never owns/disposes injected dependencies.
- Validation: disposal/call-count tests and loopback socket measurements compare
  repeated construction with DI-managed reuse.
- Rollback boundary: change B07 composition without altering F04 protocol or
  legacy method signatures.
- Extraction contract: externally lifetime-managed interface dependency.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

## Runtime Validation Pending

- Socket/handler accumulation magnitude for F03B-PERF-002.
- Multicast latency slope and CRM cost magnitude for F03B-PERF-001.
- Production CRM ACL/retention effectiveness for F03B-SEC-001.
- Dormant external consumers not visible in this repository.

These measurements refine impact; they do not negate the confirmed static
contracts.

## Deleted Or Rejected Candidates

- Channel access token leakage in F03B: rejected. No F03B source stores or logs
  a token; configuration/token ownership is outside this leaf.
- Duplicate JSON serialization: rejected. F04 serializes typed push/multicast
  once per request.
- Per-message CRM client creation: rejected. `ToolUtilityFactory` returns one
  singleton after initialization.
- Active RichMenu cross-user deletion: not promoted because no current caller of
  the F03B method was found. Retire or delegate it to F07 before future use.
- RichMenu orphan creation: not promoted as active because the F03B method is
  unreferenced; the create-before-file-read sequence remains documented.
- `LineMessageService` null organization service: constructor lacks a guard at
  `ToolUtility/LineMessaging/LineMessageService.cs:28`, but the active F03Q
  wiring supplies its service.
  The larger contract/test mismatch is covered by F03B-EXT-001 and the gate.

## Cross-Module Handoffs

1. F03A: minimized/batched audit repository and contact mapping.
2. F03Q: remove mixed facade wiring after the F03B contract is explicit.
3. F04: retain HTTP, serialization, validation, and client disposal ownership.
4. F06: supply reusable delivery result, retry, and cancellation semantics.
5. F07: own RichMenu provisioning/deletion and retire F03B legacy operations.
6. B07/B03: classify content, await sends, and migrate `LineNotifyUtility`.
7. X01: host DI client/workflow lifetime.
8. X04A: retention/privacy policy and CRM access review.
9. F01A/F01D: repair the test container; F03B fixes and expands subject tests.

## Final CCG Approval

Final CCG disposition: `APPROVED_DEGRADED`.

- Run ID: `20260710-212135-f03b-issue-review-r1-reviewer`.
- Submitted issue SHA-256:
  `2850BBF90231DC60534E7B31E9AB7E10AF36D3901D5DF916775265B28F807374`.
- Gemini returned provider quota/billing HTTP 403 `余额不足` and produced no
  review output.
- Claude completed, reopened source for every issue, returned `KEEP` for all
  five issues, found no Critical or Warning defect in the diagnostic document,
  and issued final statement `APPROVE`.
- `summary.json`: `ok=false`, `degradedFallback=true`,
  `fallbackAccepted=true`, `quotaBlocked=true`,
  `completedBackends=["claude"]`.
- The Diagnostic Subagent independently reopened the challenged source and
  confirmed call ordering, duplicate schemas, exception behavior, serial CRM
  I/O, client ownership, guards, and subject-test mismatch.
- Retained confirmed issues: 5. Deleted after CCG: 0. Issue-level runtime
  pending: 0. Rejected/merged candidates: 6.
- This is accepted single-model fallback, not completed dual-model approval.
