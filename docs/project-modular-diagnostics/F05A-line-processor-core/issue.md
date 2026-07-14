# F05A LINE Processor Core Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F05A
Workspace: F05A-line-processor-core
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: b8faee43e9b89741a1b6374fcb9fec2634f80acfb6867a7c4bc79aab0bb27472

Submitted issue SHA-256: 646461BC0ED5205B95977B8DA74C6E7816AED2D883259CA31576E980A7C05A46

## Executive Summary

Static read-only diagnosis confirmed five F05A issues. Processor instances
created from a channel token own an obsolete internally-created SDK
`HttpClient`, but the processor's `Dispose` and finalizer release nothing.
The public processor surface cannot propagate cancellation to provider calls.
The sole concrete compatibility class combines credential discovery, mutable
legacy event state, push/reply/profile/RichMenu transport, product-specific
binding behavior, and a duplicate profile DTO, forcing F06/F07 consumers to
depend on or adapt the whole class. Credential validation is split across
constructors and methods, so missing credentials fail locally for only one
legacy send method while current workflow-backed paths can issue an
unauthenticated provider request. Finally, two legacy profile/binding helpers
send `Exception.ToString()` to a caller-selected LINE recipient.

No active caller of the dynamic event dispatcher or postback parser was found.
No core dispatch loop, duplicate JSON serialization, blocking wait, or
per-request SDK creation in the ASP.NET DI path was confirmed. F04 owns HTTP
serialization and the lower-level cancellation overloads; F06/F07 own workflow
result models. Optimization is not authorized.

## Ranked Confirmed Issues

### F05A-EXT-001 One Concrete Compatibility Class Collapses Unrelated Processor Contracts

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 80
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 15
- Security urgency score: 3
- Performance gain score: 7
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: L
- Primary owner: F05A
- Cross-module: F04 dependency; F05B composition; F06/F07/B07 consumers
- Gate blocked: false
- Files:
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:27`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:35`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:37`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:40`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:158`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:255`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:339`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:361`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:575`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:655`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:673`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:699`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:716`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:25`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:27`
  - `LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs:22`
  - `LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs:39`
- Evidence: The only F05A public type is one concrete 730-line class. It owns
  ambient credential resolution, public mutable legacy fields, an untyped event
  dispatcher, push/reply/profile/RichMenu pass-throughs, product-specific
  binding/error behavior, postback parsing, and a second profile DTO. F06
  injects the concrete class directly. F07 had to define a 15-method
  `ILineRichMenuProcessor` and a pass-through adapter around it.
- Control/data/lifetime flow: F05B/B07 composition -> concrete F05A class ->
  concrete F04 client -> provider. F06 catches provider exceptions and builds
  notification/reply results; F07 builds a separate capability interface and
  result models because F05A exposes neither a narrow seam nor stable
  capability contracts.
- Impact: Consumers cannot fake only the capability they use, independently
  evolve cancellation/lifetime policy, or migrate one transport family without
  retaining unrelated legacy behavior. Test seams require a real concrete SDK
  client plus capturing HTTP handler.
- Why this is necessary: F05A is the declared processor-interface owner, but it
  currently exports a compatibility class rather than explicit processor
  contracts.
- Recommended action: Introduce narrow capability interfaces over F04
  `ILineMessagingClient` for send/reply/profile and RichMenu transport. Keep
  F06/F07 workflow result classification in those owners. Move ambient
  configuration, event dispatch, binding URLs/messages, mutable fields, and the
  duplicate DTO behind separately named legacy adapters.
- Validation: Compile-time contract tests with fake capability interfaces;
  consumer tests for F05B/F06/F07/B07; API-compatibility tests prove legacy
  wrappers delegate without changing payloads.
- Rollback boundary: Add interfaces beside `LineMessagingProcessorClass`, then
  migrate one consumer family at a time. Do not remove legacy methods until
  consumer inventory is complete.
- Extraction contract: typed recipient/message/profile/RichMenu operations +
  cancellation + explicit dependency ownership -> provider response or
  exception; workflow results remain F06/F07-owned.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

### F05A-PERF-001 Token-Owned SDK Clients Are Never Disposed

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 77
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 14
- Security urgency score: 3
- Performance gain score: 9
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: S
- Primary owner: F05A
- Cross-module: F04 client ownership; F05B/B07/X01 composition
- Gate blocked: false
- Files:
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:45`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:50`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:54`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:132`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:134`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:146`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:152`
  - `Line.Messaging/LineMessagingClient.cs:107`
  - `Line.Messaging/LineMessagingClient.cs:110`
  - `Line.Messaging/LineMessagingClient.cs:123`
  - `Line.Messaging/LineMessagingClient.cs:126`
  - `Line.Messaging/LineMessagingClient.cs:127`
  - `Line.Messaging/LineMessagingClient.cs:2823`
  - `Line.Messaging/LineMessagingClient.cs:2825`
  - `Line.Messaging/LineMessagingClient.cs:2827`
  - `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:607`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:139`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:337`
- Evidence: The token constructor creates F04's obsolete token-only client,
  which creates and owns an internal `HttpClient`. F05A implements
  `IDisposable`, but `Dispose(bool)` releases no dependency and the finalizer
  only sets `_disposed`. The injected-client constructor has different
  ownership and correctly must not dispose an externally managed client.
  A current controller wraps a token-created processor in `using`, but that
  disposal cannot reach the owned SDK client. Two current static workflow
  factories also create token-owned processors with no disposal surface.
- Control/data/lifetime flow: token/config -> F05A creates F04 client ->
  F04 creates owned `HttpClient` -> F05A consumer completes or disposes ->
  no SDK disposal -> handler/socket resources remain subject to indirect
  runtime cleanup. Every undisposed processor also enters the finalization
  path despite owning no unmanaged resource directly.
- Impact: Repeated legacy construction can retain handlers/sockets longer than
  intended and defeats deterministic cleanup. The DI path using
  `IHttpClientFactory` is not affected by ownership in the same way.
- Why this is necessary: F05A chooses whether it creates or receives the SDK
  client, so it must preserve that ownership distinction.
- Recommended action: Track an explicit ownership flag; dispose only
  token/config-created clients; remove the finalizer; prefer injected
  `ILineMessagingClient`/DI for production composition.
- Validation: Fake disposable-client tests cover token-owned versus injected
  ownership, idempotent processor disposal, and current `using` consumers.
  A loopback measurement may quantify handler/socket retention.
- Rollback boundary: Correct disposal without changing method signatures;
  injected-client behavior remains non-owning.
- Extraction contract: owned client lease versus externally managed client,
  explicit in constructor/factory contract.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

### F05A-PERF-002 Processor APIs Cannot Cancel In-Flight Provider Work

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 73
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 14
- Security urgency score: 2
- Performance gain score: 8
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F05A
- Cross-module: F04 cancellation overloads; F06/F07/B07 consumers
- Gate blocked: false
- Files:
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:317`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:329`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:339`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:351`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:361`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:387`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:575`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:582`
  - `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:27`
  - `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:97`
  - `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:74`
  - `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:82`
  - `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:106`
  - `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:97`
  - `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:100`
  - `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:137`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:56`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:63`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:64`
- Evidence: No F05A public operation accepts `CancellationToken`. F07 exposes
  cancellation on long-running synchronization and assignment workflows, but
  its processor interface and F05A adapter cannot pass the token into provider
  calls. The B07 profile provider checks cancellation immediately before
  calling F05A, then awaits a non-cancellable profile request.
- Control/data/lifetime flow: request/host cancellation -> consumer checks token
  before provider call -> F05A invokes F04 without token -> network operation
  continues until HTTP timeout/provider completion -> consumer can observe
  cancellation only before or between calls, not during the call.
- Impact: Shutdown, abandoned requests, and cancelled RichMenu batches can
  retain network work and delay resource release. A cancelled batch may stop
  only after the current provider call returns.
- Why this is necessary: Cancellation is part of an async processor contract,
  especially when F07 already advertises it to callers.
- Recommended action: Add cancellation to F05A capability interfaces and hand
  off the matching HTTP overload work to F04. F06/F07 then pass their existing
  tokens through rather than only checking between calls.
- Validation: Capturing handlers block until cancellation and prove cancellation
  reaches send/reply/profile/RichMenu requests; workflow tests distinguish
  caller cancellation from provider timeout.
- Rollback boundary: Add overloads first and retain current overloads as
  wrappers using `CancellationToken.None`.
- Extraction contract: every async provider operation accepts and propagates a
  caller token.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

### F05A-EXT-002 Credential Resolution And Validation Are Inconsistent

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 72
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 13
- Security urgency score: 6
- Performance gain score: 4
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F05A
- Cross-module: F04 client constructor; F05B/X04A configuration; B05/B07 consumers
- Gate blocked: false
- Files:
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:35`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:40`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:45`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:61`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:90`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:93`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:94`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:95`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:120`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:275`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:293`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:306`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:317`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:329`
  - `Line.Messaging/LineMessagingClient.cs:107`
  - `Line.Messaging/LineMessagingClient.cs:111`
  - `Line.Messaging/LineMessagingClient.cs:124`
  - `Line.Messaging/LineMessagingClient.cs:128`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:142`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:160`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:164`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:139`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:325`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:330`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:337`
- Evidence: The default constructor performs hidden current-directory JSON and
  environment discovery, cached once in a process-wide `Lazy<string>`.
  Token/config constructors accept blank values. Only legacy `SendMessage`
  calls `GetRequiredChannelAccessToken`; reliable/general send, reply, profile,
  and RichMenu methods call F04 directly. F04 accepts the empty value into an
  Authorization header. Current workflow factories explicitly return empty
  string on missing configuration and then call the unchecked
  `SendMessagesAsync` path.
- Control/data/lifetime flow: absent/misread configuration -> empty token ->
  token-created F05A/F04 client -> `SendMessagesAsync` -> external request with
  an empty Bearer credential -> workflow reports provider failure rather than a
  deterministic local configuration failure. The ambient default constructor
  also binds the first resolved token for process lifetime.
- Impact: Equivalent operations have different credential failure semantics;
  misconfiguration causes unnecessary external calls and obscures the owning
  configuration defect. Hidden process-global resolution prevents explicit
  rotation and tenant/organization selection.
- Why this is necessary: Credential presence and client construction are
  processor/composition preconditions, not per-method legacy behavior.
- Recommended action: Remove ambient discovery from the clean F05A contract;
  validate token/options before client creation in F05B/composition; expose
  explicit client injection; keep default/config constructors only as obsolete
  compatibility adapters with uniform fail-fast validation.
- Validation: Constructor/options tests cover blank credentials for every
  operation family, environment/config precedence, rotation/reconstruction,
  and zero HTTP calls on invalid configuration.
- Rollback boundary: Add validated factories and migrate current workflow
  factories before deprecating legacy constructors.
- Extraction contract: validated credential/client supplied by composition;
  F05A does not discover product configuration.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

### F05A-SEC-001 Legacy Helpers Send Full Exception Details To A LINE Recipient

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 67
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 5
- Security urgency score: 12
- Performance gain score: 1
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: S
- Primary owner: F05A
- Cross-module: B07 product binding workflow; X02B logging
- Gate blocked: false
- Files:
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:655`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:657`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:663`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:665`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:667`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:673`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:688`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:690`
  - `LineMessagingProcessor/LineMessagingProcessorClass.cs:692`
- Evidence: `GetUserDisplayName` and `NotifyLineBinding` catch any exception,
  build a message containing the full type name, current time, and
  `Exception.ToString()`, then push that message to the caller-provided LINE
  user ID before rethrowing. `NotifyLineBinding` calls `GetUserDisplayName`, so
  one profile failure can traverse both disclosure handlers.
- Control/data/lifetime flow: caller-selected user ID -> provider/profile or
  local failure -> full exception/stack string -> F05A push to that user ->
  exception rethrown. If the inner helper rethrows after its error push, the
  outer helper can send a second diagnostic message.
- Impact: Provider response text, exception types, stack frames, internal
  namespaces, and source/runtime details can be disclosed to an end user.
  Exact production exception content requires runtime capture; channel tokens
  were not shown in this path.
- Why this is necessary: User-facing delivery and internal diagnostics must be
  separate sinks with redaction and ownership.
- Recommended action: Remove exception-to-recipient behavior from F05A. Return
  or throw a classified failure; log sanitized diagnostics through X02B; let
  B07 choose a stable user-facing message.
- Validation: Fake provider exceptions prove no stack/provider body reaches
  outbound messages, one failure produces at most one user notification, and
  internal correlation is logged separately.
- Rollback boundary: Preserve the public helper signatures temporarily, but
  replace diagnostic payloads with a stable generic message and delegate the
  product binding flow to B07.
- Extraction contract: F05A transport failure -> classified exception/result;
  B07 user message; X02B internal log.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

## Runtime Validation Pending

- Handler/socket retention magnitude for F05A-PERF-001.
- Cancellation latency and abandoned-call duration for F05A-PERF-002.
- Exact provider/stack details exposed by F05A-SEC-001 in deployed builds.
- External consumers of public legacy methods not visible in this repository.

These measurements refine impact. The static ownership, missing propagation,
and explicit exception-message construction are confirmed.

## Deleted Or Rejected Candidates

- Active webhook signature bypass in `ProcessMessage`: not promoted. The public
  method trusts `dynamic` event fields, but repository search found no caller.
  F05B owns authenticated ASP.NET webhook entry.
- Current cross-request leakage through `m_UserId`/`m_Message`: not promoted.
  They are public mutable fields and unsafe for concurrent reuse, but the only
  DI registration is transient and the mutating dispatcher has no current
  caller.
- Postback parser denial of service: not promoted. `ParsePostBackString`
  blindly indexes split arrays, but only the unreferenced dynamic dispatcher
  calls it.
- Duplicate JSON serialization: rejected. F05A passes typed SDK messages; F04
  serializes once per provider request.
- Core dispatch-loop cost: rejected. F05A has no active recipient/menu loop.
  F07 workflow loops and ChurchReport profile loops belong to their consumers.
- Blocking async-over-sync: rejected. No `.Result`, `.Wait()`, or
  `GetAwaiter().GetResult()` was found in F05A.
- Channel token literal leakage: rejected. The F05A source contains no literal
  production bearer token; the credential subject test covers known old
  patterns.
- Duplicate legacy profile DTO allocation as a standalone performance issue:
  merged into F05A-EXT-001. Its only repository callers are legacy helpers and
  one compatibility test.
- Obsolete duplicate `LineMessagingProcessor_Net10.csproj`: recorded in the
  scope manifest as an F01A lifecycle handoff, not a processor-runtime issue.

## Cross-Module Handoffs

1. F04: add cancellation-capable SDK/interface overloads and preserve injected
   `HttpClient` ownership.
2. F05B: validate options at composition, register narrow F05A interfaces, and
   own DI lifetime only.
3. F06: retain notification/reply request, result, and provider-failure
   classification; pass cancellation when F05A supports it.
4. F07: retain RichMenu catalog/state/orchestration/results; replace the local
   pass-through adapter with the narrow F05A capability contract.
5. B07: own binding URLs, user-facing messages, profile use, and migration of
   legacy helper calls.
6. B05: migrate token-created payment notification factories to DI-managed
   workflows and explicit configuration failure.
7. X01/X04A: own client lifetime registration and credential/options policy.
8. X02B: own internal sanitized logging/correlation.
9. F01A: decide lifecycle/removal of
   `LineMessagingProcessor_Net10.csproj`.

## Final CCG Approval

Final CCG disposition: `APPROVED_DEGRADED`.

- Run ID: `20260710-221242-f05a-issue-review-r1-reviewer`.
- Submitted issue SHA-256:
  `646461BC0ED5205B95977B8DA74C6E7816AED2D883259CA31576E980A7C05A46`.
- Gemini returned provider quota/billing HTTP 403 and produced no review
  output.
- Claude completed, reopened source for every retained issue and rejected
  candidate, returned `KEEP` for all five issues, confirmed all score
  arithmetic and ownership boundaries, found zero Critical and zero Warning
  defects, and returned final module verdict `APPROVE`.
- `summary.json`: `ok=false`, `degradedFallback=true`,
  `fallbackAccepted=true`, `quotaBlocked=true`,
  `completedBackends=["claude"]`, `failedBackends=["gemini"]`.
- The Diagnostic Subagent independently reopened the load-bearing processor,
  SDK ownership, consumer cancellation, configuration, and exception sources
  after review. The five issue flows and counter-evidence remained valid.
- CCG-required rewrites: 0 of maximum 3.
- Retained confirmed issues: 5.
- Deleted after CCG: 0.
- Issue-level runtime-validation verdicts: 0.
- Rejected/merged candidates confirmed: 9.
- Runtime measurement groups remain documented for future optimization
  validation; they do not block the confirmed static diagnoses.
- This is accepted single-model fallback, not completed dual-model approval.
