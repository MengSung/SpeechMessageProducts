# F05B LINE ASP.NET Core Composition Adapter Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F05B
Workspace: F05B-line-aspnetcore-composition-adapter
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: fc3e15446cf9cab90c48d5771bab06512fe25b0c808e3e68a96e4aa8ffcb645c

Submitted pre-review issue SHA-256 excluding this field:
`E2E1C8E17658F6A7F48FFEE04CDBC306E1930535C79493C14FD28D395C1A2DAE`

## Executive Summary

Static read-only diagnosis confirmed three F05B issues. The registration API
accepts a blank channel token and an arbitrary API base URI without startup
validation; the underlying SDK places that token in the default Authorization
header and sends requests to the configured absolute URI. The transient
registration graph creates a separate HttpClient wrapper, processor, and
workflow chain for every independently resolved capability, and current host
consumers resolve notification and reply workflows separately in the same
request path. Finally, the extension is an order-sensitive composition bundle:
it directly registers F04, F05A, F06, and F07 concrete implementations,
duplicates core descriptors when invoked more than once, and replaces rather
than composes RichMenu trigger options.

No active cross-user state leak was confirmed. F05A processors are transient,
the F07 state store is keyed by LINE user ID and uses a concurrent dictionary,
and the RichMenu ID cache uses a lock. The DI path uses
`IHttpClientFactory`, so no socket-exhaustion claim is retained. No reflection,
assembly scanning, blocking startup I/O, or material startup hot spot exists in
the owned source. Optimization is not authorized.

## Ranked Confirmed Issues

### F05B-SEC-001 Unvalidated Options Can Send The Bearer Credential To An Arbitrary Or Non-TLS Endpoint

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 20
- Impact score: 22
- Likelihood/frequency score: 9
- Security urgency score: 14
- Performance gain score: 1
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: S
- Primary owner: F05B
- Cross-module: F04 request construction; X04A configuration; X01 host startup
- Gate blocked: false
- Files:
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorOptions.cs:19`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorOptions.cs:21`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorOptions.cs:23`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:53`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:55`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:57`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:60`
  - `Line.Messaging/LineMessagingClient.cs:107`
  - `Line.Messaging/LineMessagingClient.cs:111`
  - `Line.Messaging/LineMessagingClient.cs:113`
  - `Line.Messaging/LineMessagingClient.cs:134`
  - `Line.Messaging/LineMessagingClient.cs:141`
  - `Line.Messaging/LineMessagingClient.cs:144`
  - `Line.Messaging/LineMessagingClient.cs:432`
  - `Line.Messaging/LineMessagingClient.cs:434`
  - `Line.Messaging/LineMessagingClient.cs:436`
  - `SpeechMessageProducts.ChurchReport/Startup.cs:503`
  - `SpeechMessageProducts.ChurchReport/Startup.cs:506`
  - `SpeechMessageProducts.ChurchReport/Startup.cs:509`
- Evidence: F05B options default the token to empty and expose a freely mutable
  base URI. `AddLineMessagingProcessor` uses `Configure(Action)` but defines no
  validation or `ValidateOnStart`; its factory passes both values directly to
  F04. F04 assigns the token to `HttpClient.DefaultRequestHeaders.Authorization`,
  normalizes the supplied URI only by trimming and appending `/v2`, then sends
  absolute requests to that URI. ChurchReport explicitly falls back to an empty
  token when configuration is absent.
- Control/data/lifetime flow: host configuration -> cached `IOptions.Value` ->
  F05B transient client factory -> F04 sets `Authorization: Bearer <token>` ->
  absolute request to configured `ApiBaseUri`. A typo, compromised
  configuration source, or accidental HTTP/custom endpoint can therefore
  receive the credential; a blank token is discovered only when the factory or
  provider call is used, not at host startup.
- Impact: A valid channel access token can cross the intended LINE trust
  boundary or traverse plaintext HTTP. Missing credentials produce late,
  operation-shaped failures instead of deterministic startup configuration
  failure.
- Why this is necessary: F05B is the ASP.NET Core configuration and service
  registration owner. Credential presence and destination policy are
  composition preconditions.
- Recommended action: Register validated options and validate on start. Require
  a nonblank token and an absolute HTTPS URI; default to approved LINE hosts,
  with an explicit opt-in for test/emulator endpoints. Prefer configuring the
  named HttpClient through the options contract so the credential destination
  is visible at one boundary.
- Validation: Host startup tests for blank token, relative URI, HTTP URI,
  approved LINE URI, and explicitly allowed loopback/custom URI. A capturing
  handler must prove invalid options cause zero HTTP requests and that the
  Authorization header is sent only to the approved destination.
- Rollback boundary: Add validation without changing workflow contracts.
  Preserve the current default URI and provide an explicit compatibility
  switch for controlled non-LINE test endpoints.
- Extraction contract: validated credential plus validated provider endpoint
  -> named/typed LINE client; no operation is resolved from invalid options.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

### F05B-EXT-001 Registration Is An Order-Sensitive Bundle Instead Of A Composable Adapter Seam

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 74
- Confirmed: true
- Evidence confidence: 20
- Impact score: 19
- Likelihood/frequency score: 13
- Security urgency score: 5
- Performance gain score: 4
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F05B
- Cross-module: F04/F05A/F06/F07 providers; X01 consumer composition
- Gate blocked: false
- Files:
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj:14`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj:17`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:53`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:55`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:62`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:64`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:65`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:68`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:88`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:95`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:96`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:100`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:107`
  - `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs:38`
  - `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs:74`
  - `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs:77`
  - `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs:130`
- Evidence: The adapter project references all four LINE projects.
  `AddLineMessagingProcessor` unconditionally appends concrete F04 client,
  concrete F05A processor, F06 notification/reply workflows, and all F07
  RichMenu services. Core registrations use `AddTransient`, so repeated calls
  append duplicate descriptors and a pre-registered custom implementation is
  not preserved unless registered after the extension. In contrast, RichMenu
  services use `TryAdd*`. A configured `AddLineRichMenus` call constructs a new
  trigger-options instance, removes every previous instance, and adds the new
  singleton, discarding mappings from earlier configuration calls. The subject
  test itself removes registrations to install a fake rather than exercising a
  supported override seam.
- Control/data/lifetime flow: extension call order -> descriptor order and
  option replacement -> direct resolution chooses the last descriptor while
  `IEnumerable<T>` exposes duplicates -> downstream host/test must understand
  F04/F05A/F06/F07 implementation details to replace one capability. A later
  trigger configuration silently replaces earlier mappings.
- Impact: Hosts cannot independently select transport, processor, workflow, and
  RichMenu capabilities. Repeated module registration can produce duplicate
  workflow instances and order-dependent behavior. Product tests and future
  adapters must use `RemoveAll` or rely on last-registration-wins semantics.
- Why this is necessary: An ASP.NET Core adapter should expose stable,
  capability-oriented registration seams and predictable override/idempotency
  behavior.
- Recommended action: Split transport/processor, notification/reply workflows,
  RichMenu basics, and RichMenu provisioning into explicit extensions. Use
  consistent `TryAdd*` or documented replacement semantics. Compose trigger
  configuration through the options pipeline instead of replacing a singleton
  instance. Add a clean capability interface from F05A before removing the
  compatibility class registration.
- Validation: Descriptor tests for one and two invocations; custom
  pre-registration and post-registration; `IEnumerable<T>` cardinality;
  independent F06/F07 opt-in; additive trigger mappings; product catalog
  replacement; `ValidateOnBuild` and `ValidateScopes`.
- Rollback boundary: Introduce granular extensions beside the current bundle,
  implement the bundle as a compatibility composition of those extensions,
  and migrate X01 before deprecating current ordering behavior.
- Extraction contract: explicit registration per capability, deterministic
  override/idempotency policy, additive options configuration, and no required
  knowledge of concrete downstream types.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

### F05B-PERF-001 Transient Capability Resolution Duplicates Client And Processor Graphs Within One Scope

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 69
- Confirmed: true
- Evidence confidence: 20
- Impact score: 13
- Likelihood/frequency score: 15
- Security urgency score: 1
- Performance gain score: 8
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: S
- Primary owner: F05B
- Cross-module: F05A mutable compatibility type; F06/F07 consumers; X01 host
- Gate blocked: false
- Files:
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:54`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:55`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:59`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:60`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:62`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:64`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:65`
  - `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:102`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:25`
  - `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:27`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:27`
  - `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:29`
  - `LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs:27`
  - `LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs:33`
  - `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:276`
  - `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:280`
  - `SpeechMessageProducts.ChurchReport/Models/ContextDictionary.cs:98`
  - `SpeechMessageProducts.ChurchReport/Models/ContextDictionary.cs:102`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:83`
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:88`
- Evidence: `LineMessagingClient`, `LineMessagingProcessorClass`,
  notification/reply workflows, and the RichMenu processor adapter are all
  transient. Each workflow constructor requests its own concrete processor,
  which requests its own client; every client factory invocation calls
  `IHttpClientFactory.CreateClient`. Current request paths resolve notification
  and reply workflows separately, and the binding service composes a profile
  provider plus notification workflow, producing independent processor/client
  graphs for the same token and endpoint.
- Control/data/lifetime flow: one request scope -> resolve capability A ->
  HttpClient wrapper A -> processor A -> workflow A; resolve capability B ->
  HttpClient wrapper B -> processor B -> workflow B. Handler pooling prevents
  socket exhaustion, but object graphs, default headers, serializer settings,
  DI tracking, and finalizable F05A processor instances are duplicated.
- Impact: Common multi-capability request paths allocate and track multiple
  equivalent transport graphs. The cost is bounded per resolution but repeats
  on every affected request and grows when RichMenu/profile capabilities are
  added.
- Why this is necessary: F05B owns the ASP.NET lifetime policy and can share
  one safe graph per explicit scope without making the mutable F05A
  compatibility class process-global.
- Recommended action: Define one scoped transport/processor lease for ASP.NET
  request scopes and make capability workflows scoped or factories over that
  lease. Keep singleton registration prohibited until F05A removes public
  mutable state. Document scope creation for hosted/background operations.
- Validation: Resolve notification, reply, profile, and RichMenu capabilities
  in one scope and assert one client/processor identity; assert a new scope
  receives a new graph; verify disposal at scope end; compare allocations and
  finalizer counts for single versus multi-capability resolution.
- Rollback boundary: Change only F05B lifetimes and preserve public workflow
  interfaces. Revert to transient registrations if a consumer cannot provide a
  scope, without changing F04/F05A/F06/F07 behavior.
- Extraction contract: one client/processor lease per DI scope, multiple narrow
  capability adapters, deterministic scope disposal.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude KEEP; source rechecked true;
    retained unchanged.

## Runtime Validation Pending

No issue is classified as runtime-only. Measurements are still required to
quantify duplicate graph allocation and to prove startup validation and scope
reuse after an approved implementation.

## Deleted Or Rejected Candidates

- Active cross-request/cross-user processor state leak: rejected. F05A is
  transient in F05B, and no owned singleton captures it.
- RichMenu singleton thread-safety failure: rejected. The ID cache locks access
  and the state store uses `ConcurrentDictionary`; retention/eviction policy is
  handed to F07.
- DI-path socket exhaustion: rejected. `IHttpClientFactory` owns handler
  pooling; the confirmed issue is duplicate wrappers/graphs, not per-request
  sockets.
- Meaningful startup/reflection overhead: rejected. Owned registration performs
  only descriptor additions, one `Any`, and optional `RemoveAll`; there is no
  assembly scan, reflection loop, file I/O, or network I/O.
- Options hot reload failure: not promoted. `IOptions<T>` intentionally caches
  one value; credential rotation requirements need X04A product policy.
- RichMenu state leakage across organizations: not promoted. F05B exposes one
  token/endpoint options object and no multi-organization runtime contract.

## Cross-Module Handoffs

- F04: preserve external `HttpClient` ownership and enforce/duplicate endpoint
  normalization only by an agreed contract.
- F05A: introduce narrow processor capabilities and remove mutable legacy state
  before considering singleton reuse.
- F06: accept narrow notification/reply capabilities rather than concrete
  `LineMessagingProcessorClass`.
- F07: own RichMenu state retention/eviction and capability interfaces; F05B
  owns only registration lifetime.
- X01: migrate host registration order and explicit scopes.
- X04A: define approved endpoint and credential rotation policy.

## Final CCG Approval

Round 1 completed through the project self-healing reviewer.

- Run ID: `20260710-225625-f05b-issue-review-r1-reviewer`
- `ok=false`
- `degradedFallback=true`
- `fallbackAccepted=true`
- `quotaBlocked=true`
- Completed backend: Claude
- Failed backend: Gemini
- Gemini: provider quota/billing HTTP 403; no usable output
- Claude: KEEP for all three issues; source reopened true for each; zero
  Critical; zero Warning; final verdict APPROVE
- Diagnostic Subagent: independently reopened all retained sources and
  confirmed the three verdicts
- Rewrites used: 0 of maximum 3
- Deleted issues: 0
- Issue-level runtime-validation verdicts: 0

Final state: `APPROVED_DEGRADED`. This is approved single-model fallback under
project policy, not full Gemini plus Claude consensus.
