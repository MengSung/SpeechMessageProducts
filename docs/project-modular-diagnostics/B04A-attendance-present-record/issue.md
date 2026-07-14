# B04A Attendance Present Record Diagnostic Issue

Status: DEGRADED_REVIEW_PENDING
Module: B04A
Workspace: B04A-attendance-present-record
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 6feb93b8feb602ecac55c8a0bace7edf579d4e54e7b3e889ee35ab1d931c1675

## Executive Summary

B04A has six confirmed ranked findings: three security boundaries, two
performance defects, and one extraction blocker. Mutable connector identity and
name-based record selection remain runtime-validation concerns. The
`PresentFeeListView` owner question is a cross-module handoff, not a B04A issue.

## Ranked Confirmed Issues

### B04A-SEC-001 Present-record mutations lack complete local authorization proof

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 90
- Confirmed: true
- Evidence confidence: 19
- Impact score: 25
- Likelihood/frequency score: 14
- Security urgency score: 15
- Performance gain score: 4
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B04A
- Cross-module: B01 authentication/session and F03A CRM operations
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs:34
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs:53
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs:105
- Evidence: Insert and delete mutate shared in-memory and CRM-backed state without
  the `EnsureCorrectUserData()` call visible in the update action; none of the
  three action bodies shows complete list/record ownership and anti-forgery proof.
- Control/data/lifetime flow: HTTP key/values -> B04A controller action -> shared
  `InMemoryContext.ListManager` -> attendance projections and CRM mutation.
- Impact: a stale, forged, or incorrectly scoped request can reach another list's
  attendance data if protection outside this partial is absent or regresses.
- Why this is necessary: B04A owns the mutation boundary and must reject invalid
  identity/list/record context before any shared-state or CRM write.
- Recommended action: require authenticated identity, anti-forgery, session
  freshness, list permission, and record ownership in one mutation context.
- Validation: run B04A route, cross-list ownership, and stale-session scenarios in
  `evidence/runtime-validation-plan.md`.
- Rollback boundary: B04A controller mutation guard and command adapter only.
- Extraction contract: request identity/list/record/operation in; authorized
  mutation context or rejection out.
- CCG round history:
  - Round 1: run `20260712-125543-b04a-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B04A-SEC-002 Present-record query creates CRM data when a record is missing

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 86
- Confirmed: true
- Evidence confidence: 18
- Impact score: 23
- Likelihood/frequency score: 12
- Security urgency score: 14
- Performance gain score: 6
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B04A
- Cross-module: B01 identity/session and F03A CRM command boundary
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs:30
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs:59
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs:69
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs:127
- Evidence: `GetPresentRecordByLoginType` calls `CreatePresentRecordList` when a
  contact-scoped query finds no match; creation persists and assigns an owner.
- Control/data/lifetime flow: weekly-report query -> filter by mutable contact
  context -> no match -> create/retrieve/assign CRM present record -> return data.
- Impact: a read-shaped operation can persist a record using stale or mismatched
  contact/list state and callers cannot distinguish query from command behavior.
- Why this is necessary: query/command separation is required for authorization,
  idempotency, retries, and reliable B04A tests.
- Recommended action: return empty/not-found from queries and require an explicit,
  authorization-checked idempotent create command.
- Validation: run the create-on-read scenario in
  `evidence/runtime-validation-plan.md`.
- Rollback boundary: keep the legacy adapter while routing creation through the new
  command boundary.
- Extraction contract: contact/list/report query in; no-write result out; explicit
  create command returns record ID and per-command status.
- CCG round history:
  - Round 1: run `20260712-125543-b04a-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B04A-SEC-003 Update diagnostics log session ID and raw attendance payload

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 76
- Confirmed: true
- Evidence confidence: 20
- Impact score: 19
- Likelihood/frequency score: 11
- Security urgency score: 13
- Performance gain score: 2
- Loop leverage score: 7
- Ease/reversibility score: 4
- Effort: XS
- Primary owner: B04A
- Cross-module: X02B logging policy and B01 session identity
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs:63
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs:64
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs:66
- Evidence: the update action writes the session ID, key, raw `values`, method,
  path, and content type to the debug sink before mutation.
- Control/data/lifetime flow: untrusted update payload and session metadata ->
  controller interpolation -> process debug/trace listeners and downstream logs.
- Impact: attendance, follow-up, contact, or pastoral-care fields can be retained in
  logs outside the B04A data-access boundary.
- Why this is necessary: payload redaction is independent of endpoint authorization
  and removes confirmed sensitive diagnostic output.
- Recommended action: log only a correlation ID, operation, and sanitized outcome;
  remove session ID, key, and raw values.
- Validation: execute the logging scenario in
  `evidence/runtime-validation-plan.md` and assert forbidden fields are absent.
- Rollback boundary: B04A controller diagnostic statements only.
- Extraction contract: structured B04A event metadata in; redacted event out.
- CCG round history:
  - Round 1: run `20260712-125543-b04a-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B04A-PERF-001 Present-record flows perform CRM reads and writes inside member loops

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 19
- Impact score: 22
- Likelihood/frequency score: 14
- Security urgency score: 5
- Performance gain score: 10
- Loop leverage score: 10
- Ease/reversibility score: 2
- Effort: L
- Primary owner: B04A
- Cross-module: F03A CRM batch operations and B02 member identity
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs:32
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs:53
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs:471
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs:518
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs:803
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs:838
- Evidence: create, update, search, and valid-member paths iterate members or
  present records while retrieving contacts/records, assigning owners, and updating
  CRM entities.
- Control/data/lifetime flow: member/present-record collection -> nested helper
  loops -> repeated synchronous F03A CRM calls -> partial per-row results.
- Impact: latency and throttling exposure scale with member count multiplied by
  helper calls, and failures can leave partially updated attendance state.
- Why this is necessary: batching is the largest confirmed B04A performance lever
  and provides a measurable request-scoped boundary.
- Recommended action: prefetch contacts, membership, and present records by GUID;
  compute a command plan and group CRM writes with per-record results.
- Validation: compare CRM call counts for 10, 50, and 200 members as defined in
  `evidence/runtime-validation-plan.md`.
- Rollback boundary: additive B04A batch gateway behind existing callers.
- Extraction contract: typed member/record IDs and command plan in; DTOs,
  per-record outcomes, and call-count metrics out.
- CCG round history:
  - Round 1: run `20260712-125543-b04a-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B04A-PERF-002 Thread-pool tasks mutate related attendance projections concurrently

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 71
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 11
- Security urgency score: 5
- Performance gain score: 6
- Loop leverage score: 7
- Ease/reversibility score: 4
- Effort: S
- Primary owner: B04A
- Cross-module: false
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs:79
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs:83
- Evidence: one update starts two `Task.Run` calls that mutate small-group and
  all-member projections, then awaits both.
- Control/data/lifetime flow: one HTTP update -> two thread-pool work items -> two
  related mutable projections -> join with non-deterministic exception ordering.
- Impact: scheduling overhead does not reduce CRM I/O and related projections can
  diverge if one mutation fails or concurrent requests interleave.
- Why this is necessary: the two updates represent one domain state transition and
  require atomic or explicitly versioned behavior.
- Recommended action: update both projections in one request-scoped domain command
  under one synchronization/versioning policy.
- Validation: run simultaneous same-key updates and assert deterministic state or an
  explicit conflict result.
- Rollback boundary: B04A in-memory update orchestration only.
- Extraction contract: key/values/current version in; one atomic projection update
  result out.
- CCG round history:
  - Round 1: run `20260712-125543-b04a-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B04A-EXT-001 Present-record service contract has no concrete B04A implementation

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 73
- Confirmed: true
- Evidence confidence: 19
- Impact score: 17
- Likelihood/frequency score: 12
- Security urgency score: 6
- Performance gain score: 7
- Loop leverage score: 10
- Ease/reversibility score: 2
- Effort: L
- Primary owner: B04A
- Cross-module: B02 contact consumer, F03A CRM provider, B04C consumer gate
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Services/PresentRecord/IPresentRecordService.cs:25
  - SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs:38
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs:30
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs:32
- Evidence: `IPresentRecordService` exists and `ContactService` consumes it, but no
  implementation was found under `Services/PresentRecord/**`; active behavior
  remains in legacy download/upload partials.
- Control/data/lifetime flow: B02 constructor requests interface -> unresolved B04A
  implementation seam, while legacy callers instantiate/use broad connector state.
- Impact: duplicated permission, query, and mutation logic cannot be tested or
  optimized behind the intended B04A contract.
- Why this is necessary: implementing the declared seam is the prerequisite for
  provider tests and the B04C consumer gate.
- Recommended action: implement narrow query, command, validation, mapping, and
  batch-gateway services behind compatibility adapters.
- Validation: B04A provider tests plus B04C scheduler/QR consumer smoke.
- Rollback boundary: additive service implementations and adapters; retain legacy
  partial methods until consumer gates pass.
- Extraction contract: authenticated context and typed query/command input in;
  present-record DTOs and per-command results out.
- CCG round history:
  - Round 1: run `20260712-125543-b04a-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

## Runtime Validation Pending

### B04A-SEC-004 Mutable identity state and display-name matching need runtime proof

- Confirmed: false
- Evidence: present-record creation/filtering depends on mutable connector fields;
  `SearchPresentRecordByName` and `UpdateContactInfomationFromList` match display
  names at `UploadIntegrateData.PresentRecord.cs:778` and
  `UploadIntegrateData.Contact.cs:53`.
- Required validation: prove connector lifetime and run duplicate/renamed-contact
  scenarios before deciding whether this is a separate security issue or part of
  the B04A query/command extraction.

## Deleted Or Rejected Candidates

- No hard-coded B04A secret or directly proven memory leak was found.

## Cross-Module Handoffs

### B04A-EXT-002 Clarify PresentFeeListView ownership

`Views/Home/PresentFeeListView.cshtml:92` loads `Home.LoadLessonList` while the
present-record mutation actions are commented out. The module map owner must
decide whether this is B04A UI, a B06 list/fee view, or legacy quarantine before
any extraction. It is not retained as a confirmed B04A defect.

## Final CCG Approval

`DEGRADED_REVIEW_PENDING`; round 1 produced no usable backend output.
