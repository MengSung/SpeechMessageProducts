# F04 LINE Messaging SDK Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F04
Workspace: F04-line-messaging-sdk
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: f513e9cf5beb14b5e500e44797383e0fc19d23b7b3bc6ecbf07b5d0d6435967b

Submitted issue SHA-256:
`ABC1DD5EF6DE49D2814EC7A120D4D5B047C07EB312AED99858678EAB8738209F`

## Executive Summary

Static read-only diagnosis confirmed nine F04 issues: shared `HttpClient`
authorization state can cross channel-client boundaries; webhook verification
buffers unbounded input before authentication; nominal stream APIs buffer full
media responses and the transport never disposes response/request objects;
the public async API has no cancellation contract; retry handling accepts
invalid keys, omits narrowcast retry support, and treats LINE's accepted
duplicate response as an ordinary failure; error behavior is inconsistent;
modern webhook identity/redelivery fields are discarded; public interfaces
advertise placeholder operations; and duplicate project/test definitions split
the canonical SDK boundary.

The signature algorithm itself fails closed and uses HMAC-SHA256 with a
length-independent byte comparison. The current ASP.NET Core adapter creates a
fresh `HttpClient` instance per SDK client through `IHttpClientFactory`, which
reduces current token-state collision risk. Custom base URI support is
intentional for proxies and tests, so it is a configuration-policy handoff,
not a retained SSRF finding.

No optimization is authorized. Provider and consumer validation are blocked in
this diagnostic run because restore/build/test/package/generation commands are
prohibited. Any future F04 change must validate F03B, F05A-F07, F05B, and host
consumers after a canonical project/test boundary is approved.

## Ranked Confirmed Issues

### F04-SEC-001 Shared HttpClient Authorization State Can Bleed Across Channel Clients

- Category: Security
- Severity: High
- Priority: P0
- Priority score: 86
- Confirmed: true
- Evidence confidence: 20
- Impact score: 23
- Likelihood/frequency score: 13
- Security urgency score: 15
- Performance gain score: 2
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F04
- Cross-module: F05B/X01 composition; X04A token/configuration policy
- Gate blocked: true
- Files:
  - `Line.Messaging/LineMessagingClient.cs:107`
  - `Line.Messaging/LineMessagingClient.cs:109`
  - `Line.Messaging/LineMessagingClient.cs:111`
  - `Line.Messaging/Liff/LiffClient.cs:40`
  - `Line.Messaging/Liff/LiffClient.cs:42`
  - `Line.Messaging/Liff/LiffClient.cs:44`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:54`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:59`
- Evidence: Both injected-client constructors write the channel token into
  `HttpClient.DefaultRequestHeaders.Authorization`. The dependency is described
  as externally owned, but its mutable default header is changed globally for
  all subsequent requests made through that `HttpClient`.
- Control/data/lifetime flow: host supplies `HttpClient` -> F04 constructor
  mutates shared default authorization -> another F04 client or unrelated caller
  reusing the same instance can overwrite/read the active token -> requests use
  whichever token was most recently installed.
- Impact: Multi-channel or multi-tenant callers can send requests under the
  wrong LINE channel identity. With different configured base URIs, a token can
  also be sent to the wrong endpoint.
- Why this is necessary: A reusable SDK must keep credentials request-scoped or
  handler-scoped; accepting an externally owned client cannot imply exclusive
  ownership of its default headers.
- Recommended action: Keep the token immutable in the SDK client and attach
  `Authorization` to each `HttpRequestMessage`, or use a dedicated delegating
  handler/typed client per channel. Reject null/blank tokens before transport.
- Validation: Two clients sharing one capturing `HttpClient` must issue
  concurrent requests with their own tokens; an unrelated request on the same
  client must not inherit a LINE bearer token.
- Rollback boundary: Add request-scoped auth while preserving constructors;
  consumer signatures need not change.
- Extraction contract: credential provider -> request builder/transport; never
  mutate caller-owned global header state.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED`; Claude `KEEP`; source reopened:
    true; rewrite required: false.

### F04-EXT-001 Retry Semantics Are Invalid And Incomplete

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 84
- Confirmed: true
- Evidence confidence: 20
- Impact score: 23
- Likelihood/frequency score: 14
- Security urgency score: 9
- Performance gain score: 4
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F04
- Cross-module: F03B/F05A/F06 delivery-result consumers
- Gate blocked: true
- Files:
  - `Line.Messaging/LineMessagingClient.cs:167`
  - `Line.Messaging/LineMessagingClient.cs:174`
  - `Line.Messaging/LineMessagingClient.cs:179`
  - `Line.Messaging/LineMessagingClient.cs:796`
  - `Line.Messaging/LineMessagingClient.cs:798`
  - `Line.Messaging/HttpResponseMessageExtensions.cs:29`
  - `Line.Messaging/HttpResponseMessageExtensions.cs:45`
  - `Line.Messaging/ILineMessagingClient.cs:67`
  - `Line.Messaging/ILineMessagingClient.cs:100`
  - `Line.Messaging/ILineMessagingClient.cs:133`
  - `Line.Messaging/ILineMessagingClient.cs:144`
  - `Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs:34`
  - `Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs:71`
- Evidence: Retry keys are accepted as any nonblank string through
  `TryAddWithoutValidation`; subject tests institutionalize non-UUID examples.
  Push, multicast, and broadcast expose retry-key overloads, while narrowcast
  does not. Every non-2xx response, including LINE's accepted duplicate HTTP
  409, is converted to `LineResponseException`, and response headers such as
  `X-Line-Accepted-Request-Id` are not represented in the error model.
- Control/data/lifetime flow: caller retries an idempotent send -> SDK sends an
  invalid key or cannot attach one to narrowcast -> LINE rejects it, or returns
  409 for a previously accepted request -> SDK reports ordinary failure and
  discards accepted-request correlation -> workflow can retry again or mark a
  delivered message failed.
- Impact: Duplicate-delivery protection is unreliable and delivery state cannot
  be reconciled correctly after ambiguous network failures.
- Why this is necessary: Retry keys and accepted-duplicate responses form one
  protocol contract; a header-only overload without result classification is
  incomplete.
- Recommended action: Add narrowcast retry support first, then validate/generate
  UUID retry keys, and return a typed send result containing request ID,
  accepted-request ID, status, and retry classification. Do not add automatic
  retries until idempotency and payload replay are explicit.
- Validation: Contract fixtures cover valid/invalid UUIDs, all four supported
  send endpoints, HTTP 409 with accepted request ID, and ambiguous timeout
  replay.
- Rollback boundary: Add result-returning overloads beside legacy `Task`
  methods, then migrate consumers.
- Extraction contract: `LineSendRequest` + retry UUID + cancellation ->
  `LineSendResult` with provider correlation.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED`; Claude `KEEP`; source reopened:
    true; rewrite required: false.

### F04-PERF-001 Stream APIs Buffer Full Media Bodies And Transport Objects Are Not Disposed

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 20
- Impact score: 23
- Likelihood/frequency score: 14
- Security urgency score: 3
- Performance gain score: 10
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F04
- Cross-module: F05A/F07/B07 media consumers; X02C measurement
- Gate blocked: true
- Files:
  - `Line.Messaging/LineMessagingClient.cs:967`
  - `Line.Messaging/LineMessagingClient.cs:969`
  - `Line.Messaging/LineMessagingClient.cs:971`
  - `Line.Messaging/LineMessagingClient.cs:1107`
  - `Line.Messaging/LineMessagingClient.cs:1109`
  - `Line.Messaging/LineMessagingClient.cs:1111`
  - `Line.Messaging/LineMessagingClient.cs:2120`
  - `Line.Messaging/LineMessagingClient.cs:2122`
  - `Line.Messaging/LineMessagingClient.cs:2124`
  - `Line.Messaging/ContentStream.cs:62`
  - `Line.Messaging/ContentStream.cs:155`
- Evidence: All three stream-returning methods use `HttpClient.GetAsync`
  without `HttpCompletionOption.ResponseHeadersRead`, so completion waits for
  response-content buffering before `ReadAsStreamAsync`. `ContentStream` owns
  only the returned stream and copied headers, not the `HttpResponseMessage`.
  Repository counts show 48 awaited response creations, 29 explicit request
  objects, zero response/request disposal scopes, and zero transport
  `CancellationToken` references.
- Control/data/lifetime flow: LINE media response -> `GetAsync` buffers content
  -> SDK obtains a stream over buffered content -> caller copies/disposes
  `ContentStream` -> response/request wrappers remain undisposed.
- Impact: Large media downloads consume avoidable memory and delay first-byte
  processing; repeated requests retain disposable wrappers and make connection
  release dependent on content behavior/GC.
- Why this is necessary: A method named and documented as the large-file path
  must stream from headers and must define response lifetime.
- Recommended action: Use `SendAsync(..., ResponseHeadersRead, cancellation)`;
  return a response-owned stream wrapper that disposes response/content/stream;
  dispose request/response objects for buffered JSON and byte-array paths.
- Validation: Loopback tests prove first bytes are readable before the body is
  complete, peak allocation remains bounded for large payloads, and response,
  content, stream, and request disposal each occur once.
- Rollback boundary: Preserve `ContentStream` public shape while extending it to
  own the response, then migrate internally.
- Extraction contract: response-owned streaming transport separate from JSON
  serialization.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED`; Claude `KEEP`; source reopened:
    true; rewrite required: false.

### F04-EXT-002 Webhook Models Drop Provider Identity And Redelivery Contracts

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 20
- Impact score: 22
- Likelihood/frequency score: 14
- Security urgency score: 10
- Performance gain score: 3
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F04
- Cross-module: F05A/F06/B07 webhook and mark-as-read consumers
- Gate blocked: true
- Files:
  - `Line.Messaging/Webhooks/WebhookEvent.cs:23`
  - `Line.Messaging/Webhooks/WebhookEvent.cs:28`
  - `Line.Messaging/Webhooks/WebhookEvent.cs:33`
  - `Line.Messaging/Webhooks/WebhookEvent.cs:38`
  - `Line.Messaging/Webhooks/WebhookEvent.cs:47`
  - `Line.Messaging/Webhooks/EventMessage.cs:21`
  - `Line.Messaging/Webhooks/EventMessage.cs:26`
  - `Line.Messaging/Webhooks/EventMessage.cs:31`
  - `Line.Messaging/Webhooks/WebhookEventType.cs:19`
  - `Line.Messaging/LineMessagingClient.cs:863`
- Evidence: Base webhook events retain only type/source/timestamp. They do not
  expose `webhookEventId`, `deliveryContext.isRedelivery`, event `mode`, or the
  webhook `markAsReadToken` required by the SDK's current mark-as-read method.
  Unknown event types are silently returned as null and skipped.
- Control/data/lifetime flow: verified LINE webhook JSON -> dynamic parser ->
  typed event drops provider event identity/redelivery/mode/read token ->
  consumer cannot deduplicate delivery, distinguish redelivery, or call the
  SDK's mark-as-read endpoint from the typed object.
- Impact: Replayed webhooks can be processed as new work, delivery provenance is
  lost, and consumers must reparse raw JSON or cannot use exposed SDK features.
- Why this is necessary: Provider event identity and redelivery state belong to
  the reusable protocol model, not product business logic.
- Recommended action: Add immutable metadata to all webhook event models,
  preserve unknown events/raw extension data, and expose message-level
  `quoteToken`, `markAsReadToken`, mentions/emojis where supplied.
- Validation: Official fixture tests cover first delivery/redelivery, active
  and standby modes, mark-as-read token, unknown future event preservation, and
  round-trip extension data.
- Rollback boundary: Add optional properties/unknown-event type without removing
  existing constructors.
- Extraction contract: verified raw body -> version-tolerant webhook envelope
  with provider identity and typed payload.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED`; Claude `KEEP`; source reopened:
    true; rewrite required: false.

### F04-SEC-002 Webhook Authentication Fully Buffers Unbounded Input Before Verification

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 18
- Impact score: 19
- Likelihood/frequency score: 12
- Security urgency score: 12
- Performance gain score: 6
- Loop leverage score: 8
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F04
- Cross-module: F05B/X01 host request limits
- Gate blocked: true
- Files:
  - `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:35`
  - `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:40`
  - `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:42`
  - `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:43`
  - `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:48`
  - `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:71`
  - `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:75`
  - `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:80`
- Evidence: The helper calls `ReadAsStringAsync` for the complete request body
  before reading/verifying the signature, then allocates UTF-8 body bytes again
  for HMAC and parses the full JSON dynamically. It accepts no maximum length or
  cancellation token.
- Control/data/lifetime flow: unauthenticated request body -> full string
  allocation -> full byte-array allocation and HMAC -> dynamic JSON tree ->
  typed events.
- Impact: If a host does not enforce a strict request-body limit, an attacker
  can force memory/CPU work before authentication and tie up request processing.
- Why this is necessary: The SDK helper is presented as the verification entry
  point; its safety must not depend on undocumented host limits.
- Recommended action: Accept a maximum body size and cancellation, reject
  missing/oversized content before allocation, compute HMAC over bounded raw
  bytes, use `CryptographicOperations.FixedTimeEquals`, then parse with explicit
  depth/shape limits.
- Validation: Oversized, missing-header, malformed-base64, malformed-JSON, and
  cancelled-body fixtures fail closed with bounded allocations.
- Rollback boundary: Add a bounded overload and deprecate the unbounded helper;
  F05B/X01 sets the host limit.
- Extraction contract: bounded raw request + signature + secret ->
  verified webhook envelope.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED`; Claude `KEEP`; source reopened:
    true; rewrite required: false.

### F04-EXT-003 Error Contracts Are Inconsistent And LIFF Delete Ignores Failure

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 20
- Impact score: 21
- Likelihood/frequency score: 13
- Security urgency score: 7
- Performance gain score: 4
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F04
- Cross-module: F05A/F06/F07 error normalization
- Gate blocked: true
- Files:
  - `Line.Messaging/HttpResponseMessageExtensions.cs:27`
  - `Line.Messaging/HttpResponseMessageExtensions.cs:36`
  - `Line.Messaging/HttpResponseMessageExtensions.cs:45`
  - `Line.Messaging/LineMessagingClient.cs:2798`
  - `Line.Messaging/LineMessagingClient.cs:2803`
  - `Line.Messaging/LineMessagingClient.cs:2805`
  - `Line.Messaging/LineMessagingClient.cs:2806`
  - `Line.Messaging/Liff/LiffClient.cs:122`
  - `Line.Messaging/Liff/LiffClient.cs:124`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:47`
- Evidence: Most non-success responses become `LineResponseException`, but the
  shared GET helper converts only HTTP 401 into `UnauthorizedAccessException`
  and embeds the raw response body/URL in its message. Consumers normalize
  `LineResponseException`. `LiffClient.DeleteLiffAppAsync` returns the
  `DeleteAsync` task as non-generic `Task`, discarding the response without
  success validation, body parsing, or disposal.
- Control/data/lifetime flow: LINE response -> endpoint-dependent exception
  type or no error at all -> consumer catches one type -> authentication or LIFF
  deletion failures escape classification or appear successful.
- Impact: Callers cannot implement consistent retry/auth/failure policy; LIFF
  deletion can falsely report success on 4xx/5xx.
- Why this is necessary: HTTP status, provider error body, request IDs, and
  retry headers must map through one stable SDK error/result contract.
- Recommended action: Centralize send/parse/error mapping, retain sanitized
  response metadata, never special-case endpoint exceptions outside the common
  type, and ensure every LIFF operation validates status.
- Validation: Matrix tests for 400/401/409/429/500 and malformed/empty bodies
  across JSON, binary, and LIFF endpoints assert one typed contract.
- Rollback boundary: Preserve `LineResponseException` inheritance while adding
  metadata/result types and correcting LIFF delete.
- Extraction contract: HTTP response -> sanitized `LineApiError` or typed
  success result.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED`; Claude `KEEP`; source reopened:
    true; rewrite required: false.

### F04-PERF-002 The Public Async Surface Cannot Propagate Cancellation

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 74
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 14
- Security urgency score: 2
- Performance gain score: 8
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: L
- Primary owner: F04
- Cross-module: all F04 consumers; F05B/X01 request lifetime
- Gate blocked: true
- Files:
  - `Line.Messaging/ILineMessagingClient.cs:35`
  - `Line.Messaging/ILineMessagingClient.cs:204`
  - `Line.Messaging/ILineMessagingClient.cs:515`
  - `Line.Messaging/LineMessagingClient.cs:436`
  - `Line.Messaging/LineMessagingClient.cs:969`
  - `Line.Messaging/LineMessagingClient.cs:2135`
  - `Line.Messaging/Liff/LiffClient.cs:82`
  - `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:40`
- Evidence: The SDK contains 99 distinct async method names across the concrete
  client and 94 on the interface, but zero `CancellationToken` references in
  F04 production source. HTTP calls, media transfer, webhook reads, and LIFF
  operations therefore rely only on `HttpClient.Timeout` or remote completion.
- Control/data/lifetime flow: host request/shutdown cancellation -> consumer
  calls F04 -> token cannot cross API boundary -> serialization/network/read
  continues after caller abandonment.
- Impact: Disconnected requests, shutdown, and superseded workflows continue
  consuming sockets, memory, CPU, and LINE quota until timeout.
- Why this is necessary: Cancellation is part of a reusable transport contract,
  especially for media and batch operations.
- Recommended action: Add optional cancellation to all I/O methods and pass it
  through `SendAsync`, content reads, and webhook parsing. Use staged default
  interface/overload migration for compatibility.
- Validation: A blocking handler and slow stream must observe cancellation
  promptly for representative JSON, upload, download, and webhook paths.
- Rollback boundary: Add overloads before changing existing signatures.
- Extraction contract: cancellation-aware transport used by all higher modules.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED`; Claude `KEEP`; source reopened:
    true; rewrite required: false.

### F04-EXT-004 Public Interfaces Advertise Placeholder Operations As Supported

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 70
- Confirmed: true
- Evidence confidence: 20
- Impact score: 19
- Likelihood/frequency score: 12
- Security urgency score: 2
- Performance gain score: 3
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F04
- Cross-module: F05A/F07 API consumers
- Gate blocked: true
- Files:
  - `Line.Messaging/ILineMessagingClient.cs:698`
  - `Line.Messaging/ILineMessagingClient.cs:791`
  - `Line.Messaging/LineMessagingClient.cs:2675`
  - `Line.Messaging/LineMessagingClient.cs:2776`
  - `Line.Messaging/LineMessagingClient.cs:2785`
  - `Line.Messaging/LineMessagingClient.cs:2789`
- Evidence: Twelve audience-group methods are public/interface members but
  always throw `NotImplementedException`. `GetFollowersAsync` is public,
  documented as a custom extension, performs no I/O, and returns an empty list
  as successful data.
- Control/data/lifetime flow: consumer discovers a compile-time SDK capability
  -> production invocation either throws an implementation placeholder or
  returns a plausible empty result -> caller cannot distinguish unsupported
  from valid empty provider data.
- Impact: The public contract overstates API parity and can produce runtime
  failures or silent data loss.
- Why this is necessary: Unsupported operations must not masquerade as stable
  reusable APIs.
- Recommended action: Implement against verified protocol fixtures or remove
  from the stable interface; represent unsupported capability explicitly.
- Validation: Interface parity inventory and fixture tests for every retained
  method; no public method may contain `NotImplementedException` or synthetic
  success.
- Rollback boundary: Introduce capability/version interfaces and obsolete
  placeholders before removal.
- Extraction contract: capability-specific interfaces rather than one
  all-endpoint interface.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED`; Claude `KEEP`; source reopened:
    true; rewrite required: false.

### F04-EXT-005 Duplicate Project And Cross-Owned Test Definitions Split The Canonical Boundary

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 71
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 13
- Security urgency score: 2
- Performance gain score: 3
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: S
- Primary owner: F04
- Cross-module: F01A/F01D project/test governance; F05A test ownership
- Gate blocked: true
- Files:
  - `Line.Messaging/Line.Messaging.csproj:4`
  - `Line.Messaging/Line.Messaging.csproj:42`
  - `Line.Messaging/Line.Messaging_Net10.csproj:4`
  - `Line.Messaging/Line.Messaging_Net10.csproj:42`
  - `Line.Messaging.Tests/Line.Messaging.Tests.csproj:23`
  - `Line.Messaging.Tests/Line.Messaging.Tests.csproj:24`
  - `Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs:22`
  - `SpeechMessageProducts.sln:26`
- Evidence: Canonical and `_Net10` projects target the same framework and are
  byte-equivalent except for a BOM in the canonical file; only the canonical
  project is referenced. The F04 test project also references F05A solely for
  `LineMessagingProcessorCredentialTests`, which tests processor configuration,
  not SDK behavior.
- Control/data/lifetime flow: developers/tools can select either project file
  for the same source -> metadata/build identity can drift; F04 provider tests
  require F05A -> SDK gate is no longer an independent provider gate.
- Impact: There is no single authoritative project definition, and F04 test
  failures can originate from a downstream processor contract.
- Why this is necessary: Canonical project and subject-test ownership are
  prerequisites for reliable provider/consumer gates.
- Recommended action: Retire or archive `_Net10` after confirming no external
  build uses it; remove the F05A project reference and move the credential test
  to F05A's test project. Keep F04 tests limited to transport/models/webhooks.
- Validation: Project-reference inventory names one canonical F04 project;
  F04 tests have no F05A dependency; consumer gates run separately.
- Rollback boundary: Project/test ownership move only; no runtime code change.
- Extraction contract: F04 provider project + F04 subject tests; F05A consumer
  project/tests remain downstream.
- CCG round history:
  - Round 1: Gemini `QUOTA_BLOCKED`; Claude `KEEP`; source reopened:
    true; rewrite required: false.

## Runtime Validation Pending

- Memory/first-byte impact of F04-PERF-001 for representative LINE media sizes.
- Frequency of shared-client reuse across multiple channel credentials.
- Effective request-size limits in each host using the webhook helper.
- Production rate of retry HTTP 409, 429, and ambiguous network failures.
- External consumers or scripts that still select `Line.Messaging_Net10.csproj`.

These measurements refine impact. They do not negate the confirmed static
contracts.

## Deleted Or Rejected Candidates

- Weak webhook signature algorithm: rejected. The helper computes HMAC-SHA256,
  base64-decodes the supplied signature, compares all shared-length bytes, and
  fails closed on malformed input
  (`WebhookRequestMessageHelper.cs:71-88`, `:96-101`).
- Direct custom-base-URI SSRF/token exfiltration: rejected as a standalone F04
  issue. The URI is explicit configuration and subject tests deliberately
  support an internal gateway. X04A should restrict production configuration to
  approved HTTPS endpoints.
- Raw JSON overload injection: not promoted. The methods deliberately accept
  caller-authored JSON and provider-issued IDs/tokens. They should be obsolete
  in favor of typed serialization, but no untrusted caller was found.
- Automatic retries are missing: rejected as phrased. Automatic replay can be
  unsafe; the retained issue is the incomplete retry protocol/result contract.
- `StreamContent` wrapper disposal alone: merged into F04-PERF-001. Disposing it
  can also dispose the caller-owned stream, so ownership must be explicit.
- `Headers.GetValues` on a missing signature throws a framework exception
  rather than `InvalidSignatureException`: retained only as a validation case
  under F04-SEC-002, not a separate security bypass because the request fails.
- `VerifyContentPreparationAsync` returns true after an HTTP 200 body parse
  failure (`LineMessagingClient.cs:1052-1066`): documented as a serialization
  candidate for runtime/fixture validation; it does not yet justify a separate
  issue because LINE's current successful response shape is covered by tests.
- Text/media model validation gaps: not promoted as one broad issue. Existing
  constructors include some guards, but coverage is sparse; add fixture parity
  under the extraction contract.

## Cross-Module Handoffs

1. F01A/F01D: retire the duplicate project and enforce subject-test ownership.
2. F03B: consume the typed retry/error result without reimplementing HTTP.
3. F05A: own processor credential tests and map webhook metadata through its
   compatibility API.
4. F05B/X01: provide dedicated HTTP client/handler configuration, host request
   size limits, timeout, and cancellation.
5. F06: own business delivery policy while consuming F04 provider correlation.
6. F07: validate RichMenu media streaming and batch/retry result usage.
7. B07: consume webhook metadata and choose product idempotency behavior.
8. X02C: measure allocation, first-byte latency, and cancellation behavior.
9. X04A: approve production LINE API/proxy endpoints and token configuration.

## Final CCG Approval

Final CCG disposition: `APPROVED_DEGRADED`

- Run ID: `20260710-221228-f04-issue-review-r1-reviewer`
- Run directory:
  `.ccg/dual-model-runs/20260710-221228-f04-issue-review-r1-reviewer/`
- Runner result: `ok=false`, `degradedFallback=true`,
  `fallbackAccepted=true`, `quotaBlocked=true`
- Completed backend: Claude
- Blocked backend: Gemini, provider quota/billing HTTP 403 (`余额不足`), no
  usable output
- Claude verdicts: 9 `KEEP`, 0 `REWRITE`, 0 `DELETE`,
  0 `NEEDS_RUNTIME_VALIDATION`
- Claude final statement: `APPROVE`
- Independent post-review source reopening: complete for all nine issues
- Rewrite rounds: 0

This is not dual-model consensus. The result is accepted only as the
project-approved single-model fallback because Claude produced usable output
and Gemini was provider-quota blocked. The nine issues remain confirmed by
static source evidence; runtime measurements and future implementation gates
remain deferred.
