# F02 Dataverse Connection Foundation Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: F02
Workspace: F02-dataverse-connection-foundation
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 4662622372597d7cb8156855776836579222509989e93167f921ad22ac561b97

## Executive Summary

Five confirmed F02 issues survived source reopening. The public client cannot
deterministically close its WCF/authentication resources, remote metadata and
authentication processing lacks finite resource policy, every client repeats
synchronous WSDL/STS discovery, metadata can redirect server-side discovery
outside the configured origin, and construction collapses discovery,
authentication, transport, SDK wrapping, and lifetime behind a lifecycle-free
`IOrganizationService` result.

No direct credential disclosure, current CallerId cross-user leak, unsafe
retry amplification, N+1 business query, or current NSspi native-handle leak
was confirmed. F02 remains gate-blocked; this document does not authorize
optimization.

## Ranked Confirmed Issues

### F02-PERF-001 The Public Client Cannot Release Its Channel And Authentication Resources

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 75
- Confirmed: true
- Evidence confidence: 20
- Impact score: 16
- Likelihood/frequency score: 14
- Security urgency score: 6
- Performance gain score: 8
- Loop leverage score: 9
- Ease/reversibility score: 2
- Effort: M
- Primary owner: F02
- Cross-module: F03A/F03Q consumers; X01 pool lifetime
- Gate blocked: true
- Files:
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:33`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:211`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:229`
  - `PowerPlatform.Dataverse.Client/ClaimsBasedAuthClient.cs:46`
  - `PowerPlatform.Dataverse.Client/ClaimsBasedAuthClient.cs:145`
  - `PowerPlatform.Dataverse.Client/ADAuthClient.cs:124`
  - `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:406`
  - `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:419`
- Evidence: `OnPremiseClient` exposes only `IOrganizationService`. It returns a
  WCF channel created from a credential-bearing `ChannelFactory`, while the
  factory owner has no success-path close/dispose. AD token refresh creates
  `NegotiateAuthentication` without disposal. Consumer cleanup casts the
  wrapper to `IDisposable`, which cannot succeed.
- Control/data/lifetime flow: pool/facade constructs F02 client -> F02 creates
  AD context or WCF factory/channel -> consumer stores `IOrganizationService`
  -> idle/host cleanup attempts `IDisposable` -> no F02 close/abort/dispose
  path.
- Impact: Long-lived or replaced connections can retain channel, socket,
  authentication, token, and buffer resources until indirect runtime cleanup.
  No credential read sink is claimed.
- Why this is necessary: F02 creates and owns these resources; consumer pools
  cannot repair a lifecycle contract erased by the provider.
- Recommended action: Return a disposable organization-service lease, close
  healthy WCF objects, abort faulted objects, and dispose AD/crypto resources
  on every success/failure path.
- Validation: Repeated fake AD/federation create-use-fault-replace-dispose
  tests with communication-state and handle/socket probes, plus F03A/F03Q/host
  compile gates.
- Rollback boundary: Add a compatibility adapter first; migrate pool ownership
  separately from F02 internals.
- Extraction contract: validated profile + auth session -> disposable
  `IOrganizationServiceLease` -> F03A/F03Q/X01 consumer.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP with score rewrite required;
    source rechecked true
  - Round 2: Gemini QUOTA_BLOCKED; Claude SESSION_BLOCKED; no usable verdict

### F02-SEC-001 Remote Metadata And Authentication Responses Lack Bounded Resource Policy

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 75
- Confirmed: true
- Evidence confidence: 20
- Impact score: 22
- Likelihood/frequency score: 10
- Security urgency score: 8
- Performance gain score: 5
- Loop leverage score: 7
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F02
- Cross-module: X01 availability; X04A endpoint policy
- Gate blocked: true
- Files:
  - `PowerPlatform.Dataverse.Client/ClaimsBasedAuthClient.cs:184`
  - `PowerPlatform.Dataverse.Client/ClaimsBasedAuthClient.cs:190`
  - `PowerPlatform.Dataverse.Client/Wsdl.cs:53`
  - `PowerPlatform.Dataverse.Client/Wsdl.cs:79`
  - `PowerPlatform.Dataverse.Client/ADAuthHelpers/BaseAuthRequest.cs:49`
  - `PowerPlatform.Dataverse.Client/ADAuthHelpers/BaseAuthRequest.cs:85`
  - `PowerPlatform.Dataverse.Client/ADAuthClient.cs:160`
  - `PowerPlatform.Dataverse.Client/ADAuthClient.cs:185`
- Evidence: Federated WCF message and reader quotas are set to
  `Int32.MaxValue`; WSDL import recursion has no byte/count/depth/deadline
  limits; AD authentication does not apply the client's timeout to WS-Trust
  requests and has no overall deadline or round cap.
- Control/data/lifetime flow: configured/compromised CRM or STS -> large,
  high-fan-out, slow, or indefinitely continuing response -> synchronous
  network/XML/WCF work in construction/authentication -> repeated pool
  initialization or request resource consumption.
- Impact: Memory exhaustion, prolonged blocking, XML allocation, and pool
  starvation. This is an availability claim, not credential disclosure or
  code execution.
- Why this is necessary: The limits are F02 transport/authentication policy and
  cannot be fixed by business-query consumers.
- Recommended action: Add finite configurable bytes/XML/import/depth/round and
  whole-operation deadline policy; retain protocol compatibility through
  tested upper bounds.
- Validation: Fake oversized, slow, high-fan-out, and endless negotiation
  fixtures must fail deterministically within limits while normal CRM
  transcripts remain compatible.
- Rollback boundary: Introduce policy with compatibility defaults before
  tightening production limits.
- Extraction contract: remote response -> bounded metadata/auth parser ->
  validated profile/session or classified failure.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP with score rewrite required;
    source rechecked true
  - Round 2: Gemini QUOTA_BLOCKED; Claude SESSION_BLOCKED; no usable verdict

### F02-EXT-001 Construction Collapses Discovery, Authentication, Transport, And Lifetime

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 73
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 13
- Security urgency score: 5
- Performance gain score: 5
- Loop leverage score: 10
- Ease/reversibility score: 2
- Effort: L
- Primary owner: F02
- Cross-module: F03A/F03Q/X01/X02C consumer migration
- Gate blocked: true
- Files:
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:97`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:168`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:171`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:258`
  - `PowerPlatform.Dataverse.Client/Wsdl.cs:51`
  - `PowerPlatform.Dataverse.Client/Wsdl.cs:82`
  - `ToolUtility/ConnectionOperations/CrmConnectionService.cs:430`
  - `ToolUtility/ConnectionOperations/CrmConnectionService.cs:435`
- Evidence: One public constructor performs synchronous remote discovery,
  protocol selection, credential application, AD/federated authentication,
  channel creation, and SDK wrapper construction. The consumer receives only
  `IOrganizationService`; WebRequest, serializers, clock, endpoint policy,
  session state, channel state, and cleanup are hidden and not injectable.
- Control/data/lifetime flow: URL/credentials -> monolithic constructor ->
  remote metadata and auth -> live transport -> lifecycle-free interface ->
  pool/facade consumer.
- Impact: The confirmed security/performance defects cannot be isolated,
  fixture-tested, cached, observed, or rolled back by responsibility. No
  F02-specific tests exist.
- Why this is necessary: A real provider seam unlocks bounded metadata,
  deterministic disposal, protocol fixtures, and required provider/consumer
  gates without moving F03A query semantics into F02.
- Recommended action: Extract metadata resolver, authentication-session
  factory, transport factory, and disposable lease, retaining a compatibility
  adapter.
- Validation: Unit fixtures per responsibility plus F03A/F03Q/host compile;
  dependency direction remains provider to consumer.
- Rollback boundary: Add interfaces/adapters first, then move discovery,
  authentication, and transport in separate F02 changes; consumer migrations
  remain owner-scoped.
- Extraction contract: URL/config -> immutable validated profile -> disposable
  auth session -> disposable transport lease -> SDK request/response; F03A
  retains query/CRUD construction.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true
  - Round 2: Gemini QUOTA_BLOCKED; Claude SESSION_BLOCKED; no usable verdict

### F02-PERF-002 Every Client Construction Repeats Synchronous Metadata Discovery

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 71
- Confirmed: true
- Evidence confidence: 20
- Impact score: 16
- Likelihood/frequency score: 15
- Security urgency score: 1
- Performance gain score: 8
- Loop leverage score: 8
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F02
- Cross-module: F03A pool; X01 startup
- Gate blocked: true
- Files:
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:123`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:139`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:171`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:209`
  - `PowerPlatform.Dataverse.Client/Wsdl.cs:53`
  - `PowerPlatform.Dataverse.Client/Wsdl.cs:79`
  - `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:274`
  - `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:315`
- Evidence: Each client synchronously downloads/materializes the organization
  WSDL graph; federation downloads/materializes the STS graph again.
  `WsdlLoader` deduplicates only inside one call and has no cross-client cache.
  The pool creates at least three clients and can grow to twenty.
- Control/data/lifetime flow: pool/facade client creation -> organization WSDL
  fetch/parse -> optional STS WSDL fetch/parse -> channel/client; repeat for
  every same-endpoint connection and replacement.
- Impact: Repeated startup/replacement latency, network I/O, XML allocations,
  and metadata-endpoint load. This is not claimed as per-request discovery.
- Why this is necessary: F02 owns discovery and is the only layer able to share
  safe immutable metadata without caching credentials or live channels.
- Recommended action: Add normalized-key, bounded-TTL, single-flight metadata
  profile caching with failure eviction.
- Validation: Compare cold/warm construction of 3 and 20 fake-endpoint clients;
  require one fetch graph per key/window and no credential/token/channel cache.
- Rollback boundary: Cache only immutable discovery output behind the resolver;
  disabling the cache restores current behavior.
- Extraction contract: normalized URL + SDK version -> validated immutable
  metadata profile -> per-connection authentication/transport.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true
  - Round 2: Gemini QUOTA_BLOCKED; Claude SESSION_BLOCKED; no usable verdict

### F02-SEC-002 Remote Metadata Can Redirect Discovery Outside The Configured Origin

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 63
- Confirmed: true
- Evidence confidence: 19
- Impact score: 17
- Likelihood/frequency score: 6
- Security urgency score: 10
- Performance gain score: 1
- Loop leverage score: 6
- Ease/reversibility score: 4
- Effort: S
- Primary owner: F02
- Cross-module: X04A approved endpoint policy
- Gate blocked: true
- Files:
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:123`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:126`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:171`
  - `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:182`
  - `PowerPlatform.Dataverse.Client/Wsdl.cs:64`
  - `PowerPlatform.Dataverse.Client/Wsdl.cs:79`
  - `PowerPlatform.Dataverse.Client/ClaimsBasedAuthClient.cs:165`
  - `PowerPlatform.Dataverse.Client/ClaimsBasedAuthClient.cs:176`
- Evidence: Only the initial URL is required to be HTTPS. Remote WSDL import
  locations and issuer metadata addresses are fetched directly with no
  same-origin, HTTPS, port, address-range, or allowlist policy; discovered
  metadata then selects the STS path.
- Control/data/lifetime flow: configured HTTPS CRM -> remotely supplied
  import/issuer URL -> server-side HTTP(S) request outside configured origin ->
  externally influenced STS discovery.
- Impact: SSRF/trust expansion under control or compromise of configured CRM
  metadata. WSDL GETs were not shown to forward passwords, and federated
  credentials use transport plus message credential security; direct
  credential exfiltration is not claimed.
- Why this is necessary: F02 consumes and trusts remote endpoint metadata
  before authentication/transport construction.
- Recommended action: Resolve relative URIs, require HTTPS, validate
  origin/host/port/address ranges, and apply credentials only after an
  immutable endpoint profile is approved.
- Validation: Same-origin/cross-origin/downgrade/loopback/link-local/private and
  explicitly approved on-prem endpoint fixtures.
- Rollback boundary: Add policy with explicit on-prem exceptions; do not alter
  X04A configuration values in the F02 change.
- Extraction contract: remote metadata URI -> F02 endpoint validator ->
  approved immutable endpoint profile or rejection.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true
  - Round 2: Gemini QUOTA_BLOCKED; Claude SESSION_BLOCKED; no usable verdict

## Runtime Validation Pending

None. Future acceptance measurements and negative fixtures are documented in
`evidence/runtime-validation-plan.md`.

## Deleted Or Rejected Candidates

- SHA-1/HMAC-SHA1 protocol use is automatically Critical: rejected; protocol
  compatibility and authenticator validation were considered.
- Current CallerId cross-user leakage: rejected; no current external assignment
  or parallel-retrieve caller was found.
- NSspi is the current net10 authentication path and leaks handles: rejected;
  current compilation selects `NegotiateAuthentication`, and NSspi generally
  owns SafeHandle/disposal paths.
- Every request rebuilds a client: rejected; the host pools clients.
- Business-query N+1 belongs to F02: rejected by map ownership.
- Missing retry proves waste or insecurity: rejected without a reachable
  amplification/replay failure.
- NSspi standalone project is a current runtime defect: rejected; retained as
  a dormant project-lifecycle decision because no current gate was run.
- Exceptions log passwords/tokens: rejected; reviewed messages include
  endpoint data, not credential/token values.

## Cross-Module Handoffs

1. F03A: consume the future lease while retaining CRUD/query/batching
   ownership.
2. F03Q: migrate mixed facade connection construction through a separate
   quarantine task.
3. X01: own DI, pool sizing, lease scheduling, and host shutdown.
4. X04A: define approved CRM/STS endpoint and credential configuration policy.
5. X02C: instrument the transport seam without owning connection behavior.
6. F01A: coordinate solution/project lifecycle only if NSspi enrollment or
   packaging changes.

## Final CCG Approval

Substantive workflow status: `DEGRADED_REVIEW_PENDING`.

- Round 1 submitted SHA-256:
  `B6C4004DD0EB4501FC79C57F503D29A33DF095E5B4D15B17F125486DB8C7ACC7`.
- Run ID: `20260710-203921-f02-issue-review-r1-reviewer`.
- Gemini produced no usable output because provider billing/quota returned
  HTTP 403 insufficient balance.
- Claude reopened original sources, returned KEEP for all five diagnoses, and
  reported no write side effects, but required score rewrites for
  F02-PERF-001 and F02-SEC-001.
- The requested score changes were applied and all affected source ranges were
  reopened by the diagnostic agent.
- Round 2 submitted SHA-256:
  `413B34350F1AF1E3CE24A12F9F011F9BFC019C5014B28082696216B779C6BFB4`.
- Run ID: `20260710-205026-f02-issue-review-r2-reviewer`.
- Round 2 produced no usable backend: Gemini remained billing-blocked and
  Claude hit a provider session limit resetting at 9:20 PM Asia/Taipei.
- Round 2 summary has `degradedFallback=false`, `quotaBlocked=true`,
  `completedBackends=[]`, and `failedBackends=["gemini","claude"]`.
- The workflow therefore cannot claim `APPROVED` or `APPROVED_DEGRADED`.
  Immediate repeated provider calls are prohibited by the CCG guide.
- Retained: 5. Deleted: 0. Runtime pending: 0. Cross-module handoff groups: 6.

The five rescored issues remain the diagnostic agent's confirmed static
findings, but final independent approval is pending a usable reviewer round.
