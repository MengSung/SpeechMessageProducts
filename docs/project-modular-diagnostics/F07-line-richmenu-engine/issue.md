# F07 LINE RichMenu Engine Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F07
Workspace: F07-line-richmenu-engine
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: fc16b4199a8c9bcb13121f03bbfe5a312e85d2605deb60e3c1b047b86f747dfe

## Executive Summary

F07 has seven confirmed issues across provider deletion, provisioning integrity,
state growth, cache-miss assignment, cancellation, reconciliation, and TTL state.
The degraded Claude review retained these findings; Gemini was quota blocked.

## Ranked Confirmed Issues

### F07-SEC-004 User unassign workflow deletes shared provider RichMenus

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 77
- Confirmed: true
- Evidence confidence: 20
- Impact score: 22
- Likelihood/frequency score: 10
- Security urgency score: 13
- Performance gain score: 1
- Loop leverage score: 7
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F07
- Cross-module: F05B composition
- Gate blocked: true
- Files:
  - LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs:40
  - LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:167
  - LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:174
  - LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs:83
- Evidence: `DeleteLinkedRichMenuAsync` resolves the menu linked to one user,
  unlinks that user, and then deletes the provider RichMenu; tests assert deletion.
- Control/data/lifetime flow: user lookup -> unlink -> shared provider delete.
- Impact: a menu shared by other users, aliases, or channel defaults can be removed
  from the provider by a user-scoped operation.
- Why this is necessary: unassignment and provider-resource administration have
  different ownership and blast radius.
- Recommended action: make user unassign non-destructive and isolate provider menu
  deletion behind an explicit administrative workflow not registered by default.
- Validation: unassign tests emit no provider DELETE; administrative deletion proves
  reference/alias/default safety.
- Rollback boundary: additive non-destructive API and composition registration.
- Extraction contract: explicit provider-resource administration command with
  reference-safety proof.
- CCG round history:
  - Round 1: Claude confirmed; Gemini quota blocked; source rechecked true.

### F07-SEC-003 Failed image upload can leave a reusable partial provider menu

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 76
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 10
- Security urgency score: 12
- Performance gain score: 2
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F07
- Cross-module: false
- Gate blocked: true
- Files:
  - LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:173
  - LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:194
  - LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:199
  - LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:126
- Evidence: provisioning creates a provider menu before upload, records failures
  without cleanup/quarantine, and later reuses existing menus by name.
- Control/data/lifetime flow: create -> failed upload -> no cleanup -> later
  name-only reuse -> alias/default/cache binding.
- Impact: later synchronization can bind aliases or defaults to a broken menu.
- Why this is necessary: multi-step provider mutation requires explicit staged state
  and compensation.
- Recommended action: record a provisioning journal/state and clean up or quarantine
  created IDs when upload fails; verify image/version before reuse.
- Validation: failed-upload fixture followed by a second sync never reuses the
  incomplete menu.
- Rollback boundary: F07 provisioning workflow and state only.
- Extraction contract: staged provider mutation state with create/upload/alias/
  default completion and compensation result.
- CCG round history:
  - Round 1: Claude confirmed; Gemini quota blocked; source rechecked true.

### F07-PERF-003 Default user state is unbounded and expiry sweep scans all users

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 72
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 12
- Security urgency score: 1
- Performance gain score: 9
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F07
- Cross-module: F05B, X01
- Gate blocked: true
- Files:
  - LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:29
  - LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:46
  - LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:65
  - LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs:67
- Evidence: singleton state inserts into a `ConcurrentDictionary` without capacity
  or eviction; each expiry query scans/materializes all values and sweeps serially.
- Control/data/lifetime flow: process-lifetime user assignments -> unbounded
  dictionary -> full scan/materialization -> serial expiry processing.
- Impact: memory grows with distinct users and sweep cost grows with all stored
  users rather than due records.
- Why this is necessary: the default registered store is not a bounded production
  state contract.
- Recommended action: provide a bounded or durable due-indexed state store with
  explicit retention/eviction policy.
- Validation: high-cardinality insertion and due-query benchmarks with memory and
  scan-count budgets.
- Rollback boundary: DI replacement of `IRichMenuStateStore`.
- Extraction contract: bounded `IRichMenuStateStore` with due-state query and
  retention policy.
- CCG round history:
  - Round 1: Claude confirmed; Gemini quota blocked; source rechecked true.

### F07-PERF-002 Cache-miss assignment repeats image materialization and menu listing

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 70
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 12
- Security urgency score: 1
- Performance gain score: 8
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F07
- Cross-module: false
- Gate blocked: true
- Files:
  - LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:240
  - LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:248
  - LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:254
  - LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:262
- Evidence: resolving one missing menu key opens/materializes PNG content and calls
  provider list before searching the returned menus.
- Control/data/lifetime flow: cache miss -> image materialization -> provider list ->
  in-memory search -> assignment.
- Impact: cold assignments repeat local I/O/allocation and provider list calls after
  restart or eviction.
- Why this is necessary: provisioning already knows `menuKey -> richMenuId`; that
  index should be reusable and durable.
- Recommended action: persist a provisioning index/resolver and avoid image/provider
  list work on assignment hot paths.
- Validation: repeated cold-assignment tests assert image and provider-list call
  counts.
- Rollback boundary: fall back to the existing resolver on index miss.
- Extraction contract: durable `menuKey -> richMenuId` resolver with version proof.
- CCG round history:
  - Round 1: Claude required retaining this previously omitted finding; Gemini quota
    blocked; source rechecked true.

### F07-PERF-001 F07 cancellation tokens do not reach provider calls

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 69
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 11
- Security urgency score: 2
- Performance gain score: 8
- Loop leverage score: 8
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F07
- Cross-module: F04, F05A
- Gate blocked: true
- Files:
  - LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:27
  - LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:62
  - LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:136
  - LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:194
- Evidence: F07 workflows accept cancellation, but the processor create/upload/
  list/link/unlink/delete methods and provider calls are tokenless.
- Control/data/lifetime flow: caller cancellation -> F07 local loop stops while
  tokenless provider mutation continues.
- Impact: request abort, shutdown, or operator cancellation cannot stop in-flight
  provider work.
- Why this is necessary: cancellation must cross the F07 abstraction before
  downstream F04/F05A support can be used.
- Recommended action: add cancellation-aware processor overloads and provider client
  support while preserving compatibility overloads.
- Validation: cancellation before and during create/upload/list/link operations.
- Rollback boundary: old methods delegate through compatibility overloads.
- Extraction contract: cancellation-aware provider capability used by every F07
  workflow.
- CCG round history:
  - Round 1: Claude confirmed F07 owns the abstraction gap; Gemini quota blocked.

### F07-SEC-002 Same-menu assignment trusts local state over provider state

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 68
- Confirmed: true
- Evidence confidence: 20
- Impact score: 17
- Likelihood/frequency score: 9
- Security urgency score: 9
- Performance gain score: 1
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F07
- Cross-module: false
- Gate blocked: true
- Files:
  - LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:120
  - LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:123
  - LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:136
  - LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:101
- Evidence: matching local `CurrentMenuKey` returns success without provider link or
  reconciliation; default local state is a singleton in-memory store.
- Control/data/lifetime flow: matching local state -> early success -> provider link
  skipped.
- Impact: stale local state can claim success while the provider links another menu.
- Why this is necessary: local state is an optimization hint, not an authoritative
  provider assignment proof.
- Recommended action: define explicit reconciliation policy, freshness/version
  proof, and provider verification for stale/critical assignments.
- Validation: local/provider divergence fixtures never report false success.
- Rollback boundary: configurable fast path retained for proven-fresh state.
- Extraction contract: local/provider reconciliation decision and result.
- CCG round history:
  - Round 1: Claude confirmed; Gemini quota blocked; source rechecked true.

### F07-SEC-001 RichMenu TTL is dropped before assignment state is stored

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 64
- Confirmed: true
- Evidence confidence: 20
- Impact score: 16
- Likelihood/frequency score: 5
- Security urgency score: 9
- Performance gain score: 1
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F07
- Cross-module: false
- Gate blocked: true
- Files:
  - LineMessagingProcessor.RichMenus/RichMenuDecision.cs:52
  - LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs:102
  - LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs:28
  - LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:148
- Evidence: decisions expose TTL, but the orchestrator/assignment contract omits it
  and stored state always uses `expiresAt: null`.
- Control/data/lifetime flow: policy TTL -> orchestrator drops TTL -> assignment
  state persists no expiry -> sweep cannot find the assignment.
- Impact: a future/custom temporary policy can leave a user on a non-expiring menu;
  current built-in policy does not pass TTL.
- Why this is necessary: a modeled security/lifecycle decision must survive to the
  state used by expiry enforcement.
- Recommended action: carry TTL/expiry in an assignment command and store the
  derived `ExpiresAt` atomically with assignment.
- Validation: expiry and no-TTL assignment/sweep scenarios.
- Rollback boundary: compatibility overload without TTL remains available.
- Extraction contract: expiring assignment command with policy TTL and stored
  expiry result.
- CCG round history:
  - Round 1: Claude confirmed and required latent-impact wording; Gemini quota
    blocked; source rechecked true.

## Runtime Validation Pending

- Provider cancellation, high-cardinality store behavior, reconciliation, and
  cache-miss call-count measurements remain defined by the evidence plan.

## Deleted Or Rejected Candidates

- F07-PERF-004 copy-on-write ID cache behavior is not retained because menu-key
  cardinality is expected to be small and catalog-bounded.
- The renamed-solution boundary-test observation remains validation debt, not a
  security/performance/extraction issue.

## Cross-Module Handoffs

- F04/F05A own downstream provider cancellation support; F05B/X01 own composition.

## Final CCG Approval

`APPROVED_DEGRADED`; Claude findings were reflected and Gemini was quota blocked.
