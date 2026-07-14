# F03Q ToolUtility Mixed Facade Quarantine Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: F03Q
Workspace: F03Q-toolutility-mixed-facade-quarantine
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: QUARANTINE
Issue document SHA-256: 9c19de5dd6fb56d3c237fd5be51e0f57cde23348ea9ab25221d458dc4f6a5fa0

CCG-submitted draft SHA-256: 0EED72F9FC96F9DF52931BE14129D2541CF92A1F29D9C6CAF105EDD814A3D72B

## Executive Summary

F03Q is not a stable shared layer. The authoritative map places
`ToolUtility/Core/ToolUtilityFacade.cs` here because one facade owns a mutable
CRM connection, eighteen CRM-oriented lazy services, and one LINE-oriented
lazy service. The map-owned integration test also mixes CRM and LINE behavior.

Read-only diagnosis confirmed four issues. A real credential remains in source
comments; the facade has no cohesive contract and must be split by owner; its
public connection-switch APIs replace initialized service state without
disposal or synchronization; and the only F03Q-owned test cannot bind to the
current constructor and validates a LINE persistence path different from the
production compatibility path.

This document does not propose an all-at-once optimization. It defines narrow
handoffs to F03A and F03B, plus gate work for F01D and secret handling for
X04A. Product code, tests, builds, and runtime measurements were not modified
or executed.

## Ranked Confirmed Issues

### F03Q-SEC-001 The Quarantined Facade Retains A Plaintext CRM Credential In Source

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 79
- Confirmed: true
- Evidence confidence: 20
- Impact score: 22
- Likelihood/frequency score: 11
- Security urgency score: 15
- Performance gain score: 0
- Loop leverage score: 6
- Ease/reversibility score: 5
- Effort: XS
- Primary owner: F03Q
- Cross-module: F03A active connection path; X04A secret injection and rotation
- Gate blocked: true
- Files:
  - `ToolUtility/Core/ToolUtilityFacade.cs:91`
  - `ToolUtility/Core/ToolUtilityFacade.cs:92`
  - `ToolUtility/Core/ToolUtilityFacade.cs:93`
  - `ToolUtility/Core/ToolUtilityFacade.cs:95`
- Evidence: The constructor contains a commented CRM endpoint, administrator
  identity, and literal password. Comments are still repository content and
  disclose the credential to every reader and clone. The same password is also
  active in the F03A-owned compatibility connection fallback, which increases
  the probability that this is not synthetic sample data.
- Control/data/lifetime flow: Source history or checkout -> plaintext comment
  -> repository reader, backup, indexer, or scanner. No runtime guard can
  revoke disclosure from existing history.
- Impact: The credential must be treated as compromised. Removing only the
  comment does not rotate the credential or remove the active F03A fallback.
- Why this is necessary: Quarantine does not permit secret-bearing historical
  scaffolding to remain in an owner file.
- Recommended action: F03Q removes the commented credential in its own small
  change; F03A removes the active fallback; X04A rotates the credential and
  supplies validated runtime secrets. These are separate owner tasks.
- Validation: Repository secret scan and targeted source search show no
  credential value; an independently approved X04A/F03A task proves startup
  fails when required secrets are absent.
- Rollback boundary: Never restore the disclosed value. Roll back code
  structure independently from credential rotation.
- Extraction contract: N/A
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude SESSION_LIMIT_BLOCKED; no
    per-issue verdict; reviewer source rechecked false.

### F03Q-EXT-001 The Facade Is A Mixed Compatibility Boundary, Not A Cohesive Contract

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 79
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 14
- Security urgency score: 5
- Performance gain score: 5
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: F03Q
- Cross-module: F03A CRM operations; F03B LINE adapter; F02/F04 dependencies
- Gate blocked: true
- Files:
  - `ToolUtility/Core/ToolUtilityFacade.cs:51`
  - `ToolUtility/Core/ToolUtilityFacade.cs:56`
  - `ToolUtility/Core/ToolUtilityFacade.cs:58`
  - `ToolUtility/Core/ToolUtilityFacade.cs:64`
  - `ToolUtility/Core/ToolUtilityFacade.cs:137`
  - `ToolUtility/Core/ToolUtilityFacade.cs:146`
  - `ToolUtility/Core/ToolUtilityFacade.cs:527`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:42`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:87`
- Evidence: One public class owns the mutable `IOrganizationService`, eighteen
  CRM-oriented lazy service fields, an `ILineMessageService`, connection
  creation methods, CRUD/query/list/contact APIs, and a LINE message method.
  `ToolUtilityClass` constructs this facade and routes its broad compatibility
  surface through it. The module map explicitly states that F03Q has no stable
  contract and may only be split and handed off.
- Control/data/lifetime flow: Host DI -> static `ToolUtilityFactory` singleton
  -> `ToolUtilityClass` -> F03Q facade -> F03A CRM services and F03B LINE
  service, all sharing one mutable CRM client and one disposal boundary.
- Impact: CRM-only callers inherit LINE compile/lifetime coupling; LINE audit
  behavior inherits the entire CRM facade; tests cannot isolate either owner;
  and any connection or disposal change has a cross-responsibility blast
  radius.
- Why this is necessary: The current boundary contradicts the map's declared
  F03A and F03B contracts and prevents independent provider/consumer gates.
- Recommended action: Preserve F03Q only as a temporary compatibility adapter.
  Hand CRM methods to an F03A-owned typed facade and hand the LINE audit method
  to an F03B-owned adapter. Migrate consumers by owner; do not move the whole
  file or delete the compatibility adapter in one change.
- Validation: See the explicit split contracts below and
  `evidence/extraction-analysis.md`. Each owner requires its own contract tests,
  F03Q compatibility tests, and consumer compile gates after F01D repairs the
  test container.
- Rollback boundary: Add owner-specific interfaces beside the facade; migrate
  one method family/consumer group at a time; each migration can route back to
  F03Q without restoring removed secrets.
- Extraction contract: F03A seam: explicit CRM operation input -> typed result,
  F02 client dependency, CRM fake, B/X consumers. F03B seam: LINE audit input
  (`userId`, category/subject, message summary) -> explicit audit result,
  F03A narrow persistence dependency if retained, LINE adapter tests, legacy
  push consumers.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude SESSION_LIMIT_BLOCKED; no
    per-issue verdict; reviewer source rechecked false.

### F03Q-EXT-002 The F03Q Integration Test Is Invalid And Protects The Wrong LINE Path

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 63
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 10
- Security urgency score: 0
- Performance gain score: 0
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: S
- Primary owner: F03Q
- Cross-module: F01D test container; F03A CRM fake; F03B LINE behavior
- Gate blocked: true
- Files:
  - `ToolUtility/Core/ToolUtilityFacade.cs:83`
  - `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs:32`
  - `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs:38`
  - `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs:92`
  - `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs:99`
  - `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs:102`
  - `ToolUtility.Tests/TestHelpers/MockCrmClientFactory.cs:30`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:27`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:40`
  - `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:58`
- Evidence: The only constructor is
  `ToolUtilityFacade(IOrganizationService, object)`, but every map-owned test
  passes logger first and `ICrmClient` second. `ICrmClient` is not
  `IOrganizationService`, so the source has no matching constructor binding.
  The LINE test expects facade `CreatePushLineMessage` to create a
  `linemessage` entity. The production `ToolUtilityClass.CreatePushLineMessage`
  path instead looks up a contact and creates a `letter` entity through CRM
  facade methods.
- Control/data/lifetime flow: F01D test project -> F03Q integration test ->
  invalid constructor call; even after signature repair -> direct F03Q
  `ILineMessageService` path -> assertion on `linemessage`, bypassing the
  production F03B compatibility path.
- Impact: The quarantine has no executable proof for constructor/lifetime
  ownership, CRM/LINE separation, or the actual legacy LINE audit behavior.
  A green test after superficial repair could still validate the wrong path.
- Why this is necessary: F03Q cannot hand off responsibilities without a test
  seam that distinguishes CRM and LINE contracts.
- Recommended action: F01D first repairs target framework/solution enrollment.
  F03Q then keeps only compatibility routing tests. F03A owns CRM contract
  tests with an F02-compatible fake. F03B owns actual LINE audit/adapter tests,
  including the current `letter` behavior or an explicitly approved replacement.
- Validation: Source-level constructor binding check, owner-specific fake
  contracts, and consumer compile gates. No test command is authorized in this
  diagnosis.
- Rollback boundary: Test ownership and fixtures can move independently from
  product code. Keep old tests disabled/documented until replacement contracts
  exist; do not delete coverage first.
- Extraction contract: Input/output/dependency/test/consumer seams are defined
  in `evidence/extraction-analysis.md`; this issue is the missing test seam for
  those contracts.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude SESSION_LIMIT_BLOCKED; no
    per-issue verdict; reviewer source rechecked false.

### F03Q-PERF-001 Connection Switching Orphans Initialized Services And Races Shared State

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 57
- Confirmed: true
- Evidence confidence: 18
- Impact score: 15
- Likelihood/frequency score: 3
- Security urgency score: 4
- Performance gain score: 7
- Loop leverage score: 7
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F03Q
- Cross-module: F03A connection consumers; F02 client ownership; X01 lifetime
- Gate blocked: true
- Files:
  - `ToolUtility/Core/ToolUtilityFacade.cs:56`
  - `ToolUtility/Core/ToolUtilityFacade.cs:137`
  - `ToolUtility/Core/ToolUtilityFacade.cs:164`
  - `ToolUtility/Core/ToolUtilityFacade.cs:167`
  - `ToolUtility/Core/ToolUtilityFacade.cs:177`
  - `ToolUtility/Core/ToolUtilityFacade.cs:297`
  - `ToolUtility/Core/ToolUtilityFacade.cs:299`
  - `ToolUtility/Core/ToolUtilityFacade.cs:307`
  - `ToolUtility/Core/ToolUtilityFacade.cs:309`
  - `ToolUtility/Core/ToolUtilityFacade.cs:327`
  - `ToolUtility/Core/ToolUtilityFacade.cs:330`
  - `ToolUtility/Core/ToolUtilityFacade.cs:126`
- Evidence: Public connection methods replace `_organizationService` and call
  `ReinitializeServicesIfNeeded`. If any service was created, that method
  overwrites all lazy fields with new wrappers but never disposes the old
  created services or the old CRM proxy. There is no lock around service use,
  connection replacement, lazy replacement, or disposal. Final facade disposal
  disposes only the current `_organizationService`.
- Control/data/lifetime flow: Caller initializes any lazy service -> caller
  invokes a public connection switch with credentials -> facade replaces the
  CRM client -> all lazy references are replaced -> old services retain the
  old client until collection/finalization; concurrent calls can select old or
  new wrappers.
- Impact: A public API invocation can leak WCF/CRM resources, continue work
  against stale organization credentials, and produce mixed results under
  concurrency. Repository search found no current production caller, so
  frequency is scored low rather than assumed.
- Why this is necessary: Mutable credential-bearing state and service
  construction cannot share an unsynchronized singleton facade.
- Recommended action: Move connection creation/ownership to F02/F03A, inject an
  immutable client into each owner-specific facade, and remove connection
  switching from the F03Q compatibility surface only after caller search and
  compatibility proof. If switching must remain temporarily, serialize it and
  explicitly dispose the prior graph.
- Validation: Static caller search remains part of every migration. A future
  authorized contract test would use disposable fake clients to prove one
  owner, no stale calls after replacement, and deterministic concurrent
  behavior.
- Rollback boundary: First deprecate and observe callers; then move one
  connection method family. Re-enable the compatibility route without merging
  CRM and LINE contracts if rollback is required.
- Extraction contract: Connection options input -> F02/F03A client factory ->
  immutable owner-specific service graph; no LINE dependency and no mutable
  process-wide credential state.
- CCG round history:
  - Round 1: Gemini QUOTA_BLOCKED; Claude SESSION_LIMIT_BLOCKED; no
    per-issue verdict; reviewer source rechecked false.

## Runtime Validation Pending

None. F03Q-PERF-001 is a confirmed conditional defect in the public API; its
low repository usage is reflected in likelihood rather than converted into a
runtime hypothesis. No restore, build, test, package, generation, formatting,
migration, or output-producing command is authorized.

## Deleted Or Rejected Candidates

- Cross-user or cross-tenant leakage from the singleton alone: rejected as a
  confirmed issue. No per-user mutable field was found in the F03Q-owned file,
  and no current repository caller of the public connection-switch methods was
  found. F03Q-PERF-001 is limited to the behavior when that public API is used.
- LINE message content leakage: rejected as a confirmed security issue. The
  production F03B path stores user ID, subject, and message in CRM, but the
  repository does not provide a retention, authorization, or data-classification
  contract proving that persistence is unauthorized. This remains an F03B/B07
  policy handoff.
- Direct LINE transport inside `ToolUtilityFacade`: rejected. Its
  `ILineMessageService` writes a CRM entity; it does not call the LINE HTTP API.
- Immediate deletion of `ToolUtilityFacade`: rejected. Many
  `ToolUtilityClass` partials depend on it, and no repaired provider/consumer
  gate exists.
- Treating `ToolUtility/Core/ToolUtilityFacade.Metadata.cs` as F03Q-owned:
  rejected. It is CRM-only and falls under the F03A default because the map
  exception names only `ToolUtilityFacade.cs`.
- Claiming all eager CRM/LINE work occurs at construction: rejected. The
  service objects are lazy. The confirmed coupling is field/contract/lifetime
  coupling, not eager network I/O.

## Cross-Module Handoffs

1. F03A: own the CRM compatibility facade, immutable client dependency,
   connection lifecycle, and CRM contract tests.
2. F03B: own the LINE audit adapter decision and tests; determine whether the
   unused `linemessage` path is deleted or retained as an explicit contract.
3. F02: provide the CRM client construction/ownership seam used by F03A.
4. F04: remains the LINE HTTP/model contract provider; F03Q must not claim it.
5. F01D: repair `ToolUtility.Tests` framework/solution gate before executable
   split validation.
6. X04A: rotate the disclosed credential and own runtime secret supply.
7. X01: validate host lifetime after owner-specific registrations exist.
8. B07 and legacy push consumers: validate the selected F03B audit behavior;
   consumer ownership does not transfer product files to F03Q.

## Final CCG Approval

Final CCG disposition: `DEGRADED_REVIEW_PENDING`.

- Run ID: `20260710-211057-f03q-issue-review-r1-reviewer`.
- Submitted issue SHA-256:
  `0EED72F9FC96F9DF52931BE14129D2541CF92A1F29D9C6CAF105EDD814A3D72B`.
- Summary:
  `.ccg/dual-model-runs/20260710-211057-f03q-issue-review-r1-reviewer/summary.json`.
- Runner health check passed; this was not a local toolchain failure.
- Gemini returned provider quota/billing HTTP 403 and produced no output.
- Claude returned a provider session-limit blocker resetting at 21:20
  Asia/Taipei and produced no output.
- Parsed summary: `ok=false`, `quotaBlocked=true`,
  `degradedFallback=false`, `fallbackAccepted=true`,
  `completedBackends=[]`, `failedBackends=[gemini, claude]`.
- No backend completed, so the allowed single-model fallback could not apply.
- No per-issue `KEEP`, `REWRITE`, `DELETE`, or
  `NEEDS_RUNTIME_VALIDATION` verdict exists.
- Rewrite rounds used: 0 of 3.
- Locally retained confirmed issues pending external review: 4.
- Deleted/rejected candidates: 6.
- Runtime validation pending: 0.
- Nested agent count: 0.
