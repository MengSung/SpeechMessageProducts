# B06B Fee Management Diagnostic Issues

Status: RUNTIME_VALIDATION_PENDING
Module: B06B
Workspace: B06B-fee-management
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 96eccf548a00d6abba4b0bf3d1e023afcec18d9e537b570b2fd7f82be9a5c738

## Executive Summary

B06B has eight confirmed static findings. Bounded convergence confirmed missing
anti-forgery enforcement on active fee mutations. Login-switch clearing remains
runtime-validation pending and is not represented as confirmed.

## Ranked Confirmed Issues

### B06B-SEC-001 Fee mutation APIs lack anti-forgery enforcement

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 20
- Impact score: 22
- Likelihood/frequency score: 12
- Security urgency score: 15
- Performance gain score: 0
- Loop leverage score: 8
- Ease/reversibility score: 5
- Effort: S
- Primary owner: B06B
- Cross-module: B01, X01
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:349
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:394
  - SpeechMessageProducts.ChurchReport/Startup.cs:389
  - SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs:26
- Evidence: active PUT/POST fee mutations are protected by global authorization,
  but bounded filter/action search found no local or global automatic anti-forgery
  enforcement.
- Control/data/lifetime flow: authenticated browser request -> staged fee edit or
  batch commit -> B06B mutable state and CRM write.
- Impact: a cross-site request can trigger fee mutation under an authenticated
  browser session.
- Why this is necessary: authentication proves identity but does not provide CSRF
  protection.
- Recommended action: enforce anti-forgery automatically or on every mutation
  endpoint and transmit a token from the fee UI.
- Validation: missing and invalid token requests are rejected before any state or
  CRM mutation.
- Rollback boundary: MVC filter registration and B06B action/view token wiring.
- Extraction contract: N/A.
- CCG round history:
  - Round 1: Claude REWRITE; Gemini quota blocked; bounded search confirmed
    `STATIC_CONFIRMED_MISSING_ANTIFORGERY`.

### B06B-EXT-002 FeeList mixes session, loading, pending edits, and commit

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 20
- Impact score: 19
- Likelihood/frequency score: 13
- Security urgency score: 6
- Performance gain score: 7
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: L
- Primary owner: B06B
- Cross-module: B01, F03A
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Models/FeeList.cs:132
  - SpeechMessageProducts.ChurchReport/Models/FeeList.cs:145
  - SpeechMessageProducts.ChurchReport/Models/FeeList.cs:169
  - SpeechMessageProducts.ChurchReport/Models/FeeList.cs:240
  - SpeechMessageProducts.ChurchReport/Models/FeeList.cs:362
- Evidence: one object owns login scope, CRM loading, mutable UI lists,
  `ChangeHistory`, and commit behavior.
- Control/data/lifetime flow: session identity -> cached `FeeList` -> staged edit
  journal -> CRM commit.
- Impact: security, test, lifetime, and rollback boundaries cannot be changed
  independently.
- Why this is necessary: this object is the central blocker to an isolated B06B
  application boundary.
- Recommended action: split read service, session edit store, and commit service
  behind a compatibility facade.
- Validation: fake-backed scope, load, edit, commit, conflict, and rollback tests.
- Rollback boundary: retain the existing `FeeList` facade during migration.
- Extraction contract: fee queries/edit commands in; DTOs and commit results out;
  B01 identity and F03A CRM dependencies.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; source rechecked true.

### B06B-EXT-001 Fee master-data consumer contract is not explicit

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 75
- Confirmed: true
- Evidence confidence: 20
- Impact score: 19
- Likelihood/frequency score: 13
- Security urgency score: 2
- Performance gain score: 6
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: B06B
- Cross-module: B05
- Gate blocked: true
- Files:
  - docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:750
  - docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:810
  - SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs:42
  - SpeechMessageProducts.ChurchReport/Services/DonationDedicationFeeFormService.cs:58
- Evidence: the map assigns fee master data to B06B while B05 assembles donation
  choices through CRM-oriented services without a named B06B contract.
- Control/data/lifetime flow: B06B fee/reference data -> B05 form/query services ->
  donation choices.
- Impact: fee and payment ownership can drift and force cross-module changes.
- Why this is necessary: B05 must consume fee reference data without absorbing B06B
  implementation or CRM shape.
- Recommended action: document a narrow immutable fee-reference query/DTO contract.
- Validation: B05 consumer contract test using a fake B06B provider.
- Rollback boundary: adapter around the current donation fee query service.
- Extraction contract: fee-choice query in; immutable fee DTOs out; B05 consumer.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; source rechecked true.

### B06B-PERF-001 Full fee lists are materialized before grid shaping

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 74
- Confirmed: true
- Evidence confidence: 18
- Impact score: 18
- Likelihood/frequency score: 13
- Security urgency score: 0
- Performance gain score: 10
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: B06B
- Cross-module: F03A, X02C
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:256
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:262
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:304
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:322
- Evidence: concrete loaders populate full in-memory lists before
  `DataSourceLoader.Load` applies grid shaping.
- Control/data/lifetime flow: CRM retrieval -> full in-memory list -> paging/filter
  shaping -> response.
- Impact: latency and memory grow with the full dataset rather than the requested
  page; magnitude remains unmeasured.
- Why this is necessary: materialization is source-confirmed and is the primary
  measurable B06B query hot path.
- Recommended action: record baseline sizes and introduce server-side paging and
  projection behind an adapter.
- Validation: 10, 100, and 1,000-record timings, allocations, selected columns, and
  CRM call counts.
- Rollback boundary: retain the list-backed query adapter.
- Extraction contract: paged fee/lesson query in; page DTO and query metrics out.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; source rechecked true.

### B06B-EXT-003 FeeDownUpLoader is the CRM adapter boundary

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 61
- Confirmed: true
- Evidence confidence: 18
- Impact score: 14
- Likelihood/frequency score: 10
- Security urgency score: 0
- Performance gain score: 6
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B06B
- Cross-module: F03A
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs:105
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs:117
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs:210
- Evidence: the loader owns lesson, fee, and present-fee CRM retrieval and returns
  UI-facing models.
- Control/data/lifetime flow: B06B workflow -> concrete CRM loader -> mutable UI
  models.
- Impact: UI and CRM contracts cannot evolve or be tested independently.
- Why this is necessary: a DTO adapter is required for fake-backed B06B provider
  tests and F03A isolation.
- Recommended action: expose DTO-oriented query/write operations around the current
  loader.
- Validation: adapter contract tests with fake F03A operations.
- Rollback boundary: wrap rather than replace `FeeDownUpLoader` initially.
- Extraction contract: fee DTO query/update commands with F03A dependency.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; source rechecked true.

### B06B-PERF-002 Active and legacy fee UI surfaces coexist

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 59
- Confirmed: true
- Evidence confidence: 18
- Impact score: 14
- Likelihood/frequency score: 10
- Security urgency score: 0
- Performance gain score: 2
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: B06B
- Cross-module: X03
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Views/FeeManagement/Fee.cshtml:4
  - SpeechMessageProducts.ChurchReport/Views/Home/FeeView.cshtml:1
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:281
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:340
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:350
- Evidence: active FeeManagement views coexist with legacy Home views and redirect
  actions for the same capability.
- Control/data/lifetime flow: multiple route/view surfaces -> shared fee behavior and
  duplicated compatibility checks.
- Impact: each fee change carries duplicated browser, route, and validation cost.
- Why this is necessary: independent extraction requires one canonical route and UI
  contract.
- Recommended action: inventory traffic and retire or formalize legacy redirects.
- Validation: route snapshot and browser smoke for active and compatibility URLs.
- Rollback boundary: preserve redirects until traffic and consumer gates pass.
- Extraction contract: canonical B06B UI routes plus compatibility adapter.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; source rechecked true.

### B06B-SEC-003 Fee controller logs identifiers and exceptions to Debug

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 58
- Confirmed: true
- Evidence confidence: 18
- Impact score: 14
- Likelihood/frequency score: 10
- Security urgency score: 10
- Performance gain score: 0
- Loop leverage score: 3
- Ease/reversibility score: 3
- Effort: XS
- Primary owner: B06B
- Cross-module: X02B
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:293
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:355
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:383
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:384
- Evidence: fee IDs, keys, exception messages, and stack traces are passed to
  `Debug.WriteLine`.
- Control/data/lifetime flow: request/exception detail -> controller interpolation
  -> attached debug listener.
- Impact: identifiers and implementation details leak when debug output is
  collected; production exposure is configuration-dependent.
- Why this is necessary: the emitted data is source-confirmed even though listener
  collection needs runtime proof.
- Recommended action: use structured redacted logging with safe correlation values.
- Validation: Release/Debug listener configuration and forbidden-field log check.
- Rollback boundary: B06B logging statements only.
- Extraction contract: N/A.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; source rechecked true.

### B06B-PERF-003 Column metadata is a large mutable ViewBag contract

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 56
- Confirmed: true
- Evidence confidence: 18
- Impact score: 13
- Likelihood/frequency score: 10
- Security urgency score: 0
- Performance gain score: 2
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B06B
- Cross-module: X03
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:533
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:550
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:582
- Evidence: many CRM-backed class-name fields are copied into dynamic
  `ViewBag.Colume*` values consumed by Razor grids.
- Control/data/lifetime flow: CRM metadata -> mutable ViewBag names -> Razor grid
  columns.
- Impact: the UI contract is unstable, typo-prone, and difficult to validate.
- Why this is necessary: a typed boundary is required before controller/view
  extraction or metadata caching.
- Recommended action: introduce a typed column/view model and populate legacy
  ViewBag values during migration.
- Validation: rendered-header snapshot and mapping tests.
- Rollback boundary: dual-populate typed model and legacy ViewBag.
- Extraction contract: column metadata in; typed fee view model out.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; source rechecked true.

## Runtime Validation Pending

### B06B-SEC-002 Login-switch clearing remains unproven

- Confirmed: false
- Required validation: prove a login/account switch cannot retain staged fee state
  or commit another user's pending edits. Claude verdict was
  `NEEDS_RUNTIME_VALIDATION`.

## Deleted Or Rejected Candidates

- No other unconfirmed B06B candidate is retained as a ranked issue.

## Cross-Module Handoffs

- B05 consumes fee master data; F03A owns generic CRM operations; B01 owns identity;
  X03 owns shared UI assets; X02B owns logging policy.

## Final CCG Approval

`RUNTIME_VALIDATION_PENDING`; degraded Claude review and bounded static validation
do not satisfy login-switch and provider/consumer runtime gates.
