# F03A CRM Operations Library Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: F03A
Workspace: F03A-crm-operations-library
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 1b65e2b842544e7b4028f6e58829e105f38da3525509d6346c95e736819914dc

## Executive Summary

Static, read-only diagnosis confirmed five F03A issues: an active hard-coded CRM
credential fallback; a plaintext contact-password authentication contract that
returns a full CRM row; synchronous CRM I/O exposed as asynchronous APIs;
systemic all-column query defaults; and the absence of an independently
composable CRM operations boundary. The source contains guards and stronger
alternatives in several places, so unescaped FetchXML, attachment
authorization, per-member marketing-list calls, and connection-pool leakage
were not promoted without a proven reachable defect.

Optimization is not authorized. The provider gate is blocked because
`ToolUtility` targets `net10.0`, `ToolUtility.Tests` targets `net8.0`, the test
project is outside the solution, and the standalone ChurchReport performance
test has no executable test project.

## Ranked Confirmed Issues

### F03A-SEC-001 Active CRM Connection Falls Back To A Repository Credential

- Category: Security
- Severity: High
- Priority: P0
- Priority score: 88
- Confirmed: true
- Evidence confidence: 20
- Impact score: 25
- Likelihood/frequency score: 15
- Security urgency score: 15
- Performance gain score: 0
- Loop leverage score: 8
- Ease/reversibility score: 5
- Effort: S
- Primary owner: F03A
- Cross-module: X04A secret injection and rotation
- Gate blocked: true
- Files:
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:47`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:51`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:138`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:144`
  - `ToolUtility/Factory/ToolUtilityFactory.cs:50`
  - `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs:32`
- Evidence: Missing `CrmConnection:*` values fall back to a named server,
  organization, administrator identity, domain, and literal password. The
  constructor immediately calls `InitializeCrmConnection`, which passes that
  password to `CreateOnPremiseClient`.
- Control/data/lifetime flow: Host configuration -> static factory singleton ->
  `ToolUtilityClass` constructor -> fallback properties -> on-premise CRM
  client creation -> process-wide provider.
- Impact: Repository readers possess a production-shaped credential, and any
  environment with an omitted or misnamed secret attempts authentication with
  it. Exposure persists until the credential is rotated.
- Why this is necessary: Configuration fallback converts a missing-secret
  condition into secret use instead of startup failure.
- Recommended action: Rotate the credential, remove all secret-bearing
  fallbacks, inject a validated connection options contract, fail startup on
  missing values, and add repository secret scanning.
- Validation: In a separately approved task, verify startup rejects missing
  secrets and a secret scan reports no credential value.
- Rollback boundary: Separate credential rotation from the code/configuration
  contract change; never restore the exposed value.
- Extraction contract: Validated CRM connection options -> F02 client factory
  -> F03A typed operations; X04A owns runtime secret supply.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude SESSION_LIMIT_BLOCKED; no usable
    backend verdict; source rechecked false.

### F03A-SEC-002 Contact Authentication Uses Plaintext CRM Passwords And Returns The Full Contact

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 20
- Impact score: 23
- Likelihood/frequency score: 15
- Security urgency score: 14
- Performance gain score: 2
- Loop leverage score: 6
- Ease/reversibility score: 2
- Effort: L
- Primary owner: F03A
- Cross-module: B01 authorization/session; B02 member data; X02A cache policy
- Gate blocked: true
- Files:
  - `ToolUtility/ContactOperations/ContactService.cs:76`
  - `ToolUtility/ContactOperations/ContactService.cs:84`
  - `ToolUtility/ContactOperations/ContactService.cs:201`
  - `ToolUtility/ContactOperations/ContactService.cs:203`
  - `ToolUtility/ContactOperations/ContactService.cs:210`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Contact.cs:60`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs:501`
- Evidence: The synchronous authentication lookup selects every contact
  attribute, compares `new_app_pass` as a string, and returns that entire
  `Entity` to callers. Numerous ChurchReport workflows use the entity-returning
  facade method as their login-user lookup.
- Control/data/lifetime flow: Request account/password -> ChurchReport workflow
  -> `ToolUtilityClass` compatibility API -> F03Q facade dependency -> F03A
  `ContactService` -> all-column CRM contact -> plaintext equality -> full
  contact entity returned to business code.
- Impact: Password-equivalent data and unrelated contact PII cross the
  authentication boundary. A caller that only needs identity receives every
  CRM attribute available to the service account.
- Why this is necessary: Authentication should produce a narrow identity
  result, not expose the credential field or unrestricted contact record.
- Recommended action: Define a narrow authentication result, migrate password
  verification to an approved one-way credential mechanism, select only
  required columns, and have B01 establish authorization/session state after
  F03A returns identity evidence.
- Validation: Contract tests must prove the result excludes
  `new_app_pass`/unrequested attributes and rejects invalid credentials without
  identity disclosure.
- Rollback boundary: Introduce the narrow API beside the legacy method; migrate
  B01/B-module consumers before retiring the compatibility path.
- Extraction contract: Account credential input -> credential verifier ->
  contact ID/status result; no CRM `Entity` or password attribute crosses the
  boundary.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude SESSION_LIMIT_BLOCKED; no usable
    backend verdict; source rechecked false.

### F03A-EXT-001 CRM Operations Have Typed Services But No Independent Composition Boundary

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 14
- Security urgency score: 5
- Performance gain score: 4
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: F03A
- Cross-module: F03Q compatibility facade; F03B project dependency; X01 host DI
- Gate blocked: true
- Files:
  - `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs:32`
  - `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs:35`
  - `ToolUtility/DependencyInjection/ToolUtilityProvider.cs:30`
  - `ToolUtility/Factory/ToolUtilityFactory.cs:27`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs:28`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Query1.cs:27`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.ActivityAttachment.cs:90`
  - `ToolUtility/ToolUtility.csproj:52`
- Evidence: F03A already contains cohesive interfaces and implementations for
  CRUD, query, attributes, attachments, lists, contacts, and connections, but
  DI registers only a provider for a static `ToolUtilityClass` singleton.
  F03A partial APIs route through the excluded F03Q mixed facade, and the
  physical project still references the F03B LINE dependency.
- Control/data/lifetime flow: Host DI -> singleton provider -> static factory ->
  monolithic `ToolUtilityClass` -> F03Q facade -> lazily created F03A service.
  Consumers cannot request the F03A capability they actually use.
- Impact: CRM-only consumers inherit mixed facade lifetime, LINE build
  coupling, broad APIs, and a larger testing/optimization surface. F03A cannot
  establish its provider/consumer gate independently.
- Why this is necessary: The map declares F03A as the reusable CRM contract,
  while current composition exposes F03Q as the practical contract.
- Recommended action: Register typed F03A interfaces against an explicit CRM
  client/connection dependency, retain `ToolUtilityClass`/F03Q as a
  compatibility adapter, and migrate consumers in owner-specific tasks.
- Validation: DI resolution tests for each typed service, CRM fake contract
  tests, F03Q compatibility tests, and consumer compile gates after the
  net8/net10 test-container repair.
- Rollback boundary: Each typed registration and consumer migration is
  independently reversible; do not delete the facade in the extraction commit.
- Extraction contract: Typed query/CRUD/attribute/attachment/list interfaces;
  inputs are CRM identifiers/entities/query specifications, outputs are narrow
  results, dependency points to F02 client abstraction, tests use CRM fakes.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude SESSION_LIMIT_BLOCKED; no usable
    backend verdict; source rechecked false.

### F03A-PERF-001 Async-Labeled APIs Execute Synchronous CRM I/O

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 75
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 13
- Security urgency score: 1
- Performance gain score: 10
- Loop leverage score: 8
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F03A
- Cross-module: F02 async client capability; X02C future load measurement
- Gate blocked: true
- Files:
  - `ToolUtility/CollectionOperations/CollectionQueryService.cs:386`
  - `ToolUtility/CollectionOperations/CollectionQueryService.cs:395`
  - `ToolUtility/Extensions/CrmAsyncExtensions.cs:42`
  - `ToolUtility/Extensions/CrmAsyncExtensions.cs:218`
  - `ToolUtility/EntityOperations/EntityOptimizedQueryService.cs:357`
  - `ToolUtility/ContactOperations/ContactService.cs:413`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs:68`
  - `ToolUtility/ListOperations/ListService.cs:390`
- Evidence: Several helpers invoke synchronous `IOrganizationService`
  operations before returning `Task.FromResult`; two partial methods are
  `async` without an await and call synchronous Create/Update. List batching
  uses `Task.Run` around synchronous `Execute`, consuming ThreadPool workers.
- Control/data/lifetime flow: Async-labeled public call -> synchronous CRM
  request on caller thread, or ThreadPool dispatch -> network wait -> completed
  Task. Cancellation is checked before dispatch but cannot cancel in-flight
  synchronous CRM I/O.
- Impact: Request threads or ThreadPool workers remain blocked for CRM latency;
  concurrent callers cannot obtain true nonblocking I/O and cancellation
  semantics are misleading.
- Why this is necessary: The API contract invites concurrency that its
  implementation does not provide.
- Recommended action: Route async contracts through an F02 client that exposes
  native async SDK operations; keep explicitly named synchronous methods where
  only `IOrganizationService` is available; remove `async`/`Task.Run` wrappers
  that merely relocate blocking.
- Validation: After gate repair, fake-client tests must prove the native async
  method is invoked and cancellation reaches the client; X02C may later measure
  thread usage under load.
- Rollback boundary: Preserve synchronous compatibility methods while moving
  each async API family independently.
- Extraction contract: N/A
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude SESSION_LIMIT_BLOCKED; no usable
    backend verdict; source rechecked false.

### F03A-PERF-002 Query Defaults Systemically Retrieve Every CRM Column

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 72
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 13
- Security urgency score: 5
- Performance gain score: 8
- Loop leverage score: 4
- Ease/reversibility score: 2
- Effort: L
- Primary owner: F03A
- Cross-module: B01-B06C consumers must declare required projections
- Gate blocked: true
- Files:
  - `ToolUtility/ContactOperations/ContactService.cs:119`
  - `ToolUtility/ContactOperations/ContactService.cs:203`
  - `ToolUtility/QueryOperations/QueryService.cs:79`
  - `ToolUtility/CollectionOperations/CollectionQueryService.cs:176`
  - `ToolUtility/AttachmentOperations/AttachmentService.cs:61`
  - `ToolUtility/EntityOperations/EntityRepository.cs:145`
  - `ToolUtility/Extensions/CrmAsyncExtensions.cs:210`
- Evidence: Static inspection found 50 F03A-owned `ColumnSet(true)`
  occurrences. They appear in generic defaults, contact login/retrieval,
  relationship/list queries, current-user lookup, and attachment retrieval
  where annotation document bodies may be included.
- Control/data/lifetime flow: Consumer omits a projection or calls a legacy
  helper -> F03A substitutes all columns -> CRM serializes the complete row ->
  network transfer -> SDK materializes all attributes -> broad `Entity` escapes
  to the consumer.
- Impact: Every affected call transfers and materializes unnecessary fields,
  including PII and potentially large binary attributes. Cost scales with row
  count and broadens accidental data exposure.
- Why this is necessary: A reusable query library must make projection
  explicit; an all-column default prevents predictable latency and data
  minimization.
- Recommended action: Define operation-specific column constants/result DTOs,
  require explicit projections for generic APIs, and add paging/binary-field
  safeguards for collections and attachments.
- Validation: Query-shape tests assert exact columns and paging; representative
  consumer contract tests assert no required field is lost.
- Rollback boundary: Migrate one API family and its consumers at a time; retain
  an explicitly named legacy all-column path only where proven necessary.
- Extraction contract: N/A
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude SESSION_LIMIT_BLOCKED; no usable
    backend verdict; source rechecked false.

## Runtime Validation Pending

None. The retained issues are confirmed by static control/data/lifetime flow.
Runtime checks are implementation acceptance work, not prerequisites for these
diagnoses.

## Deleted Or Rejected Candidates

- Unescaped FetchXML in appointment, lesson, fee, meeting-statistics, and
  present-record helpers: rejected as a confirmed injection issue because
  direct attacker-controlled reachability was not established. The reachable
  donation contact search escapes every user value at
  `ContactService.cs:287-303` and caps results at 100.
- Attachment size/MIME/authorization: not promoted because no production
  consumer was found. B-module authorization remains a handoff if the API is
  exposed.
- MarketingListService per-member `Task.Run`: rejected as a separate confirmed
  issue because no current consumer was found and the facade uses
  `ListService`, which batches SDK requests. Its blocking behavior is covered
  by F03A-PERF-001 where reachable.
- Connection-pool leak: rejected. `CrmConnectionPool` bounds acquisition,
  validates/removes connections, disposes its timer/semaphore, and disposes
  pooled clients.
- Cross-user singleton leakage: rejected. A process-wide singleton is present,
  but no mutable per-user F03A field was proved; F03Q mixed state is outside
  this owner.
- Contact authentication cache poisoning: not confirmed in the active facade
  path because F03Q constructs `ContactService` without a cache. The optional
  overload keys by account only and must be corrected before any future cache
  wiring.

## Cross-Module Handoffs

1. X04A: rotate and supply validated CRM secrets.
2. B01: own authorization/session creation and migrate to the narrow identity
   result.
3. B02/B03/B04A-B04C/B05/B06A-B06C: declare projections and migrate typed CRM
   contracts.
4. F02: expose the native async client contract required by F03A.
5. F03Q: retain and then shrink the mixed facade compatibility adapter.
6. F03B: separate the LINE build dependency when the physical project boundary
   is split.
7. F01D/F01A: repair and enroll the ToolUtility test gate; X01 owns host DI and
   consumer compile validation.

## Final CCG Approval

Final CCG disposition: `DEGRADED_REVIEW_PENDING`.

- Run ID: `20260710-204420-f03a-issue-review-r1-reviewer`.
- Submitted issue SHA-256:
  `6507D1BDD2505E4EDDBB93220E6285CC1B1CCA0CC2EB6ABAC35AE049D676E9F3`.
- Gemini returned provider quota/billing HTTP 403 `餘額不足`.
- Claude returned a session-limit blocker resetting at 21:20 Asia/Taipei.
- `summary.json`: `ok=false`, `quotaBlocked=true`,
  `degradedFallback=false`, no completed backend.
- No per-issue verdict exists, so none of the five issues is represented as
  externally approved, deleted, rewritten, or runtime-pending.
- Retained pending review: 5. Deleted: 0. Runtime pending: 0.
