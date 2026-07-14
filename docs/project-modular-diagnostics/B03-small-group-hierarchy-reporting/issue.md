# B03 Small Group Hierarchy Reporting Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: B03
Workspace: B03-small-group-hierarchy-reporting
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 6712396a3cdbbce4c29f0b7a81e441bbf99681458095507a1c24e917ddfd34c4

Issue document SHA-256 before CCG: see review-log CCG Round 1 entry.

## Executive Summary

B03 has four evidence-backed findings: two security boundaries, one performance
hot path, and one extraction blocker. No product source/config/project/solution
or test file was modified. No restore/build/test/codegen/format/migration or
generated-output command was run.

## Ranked Confirmed Issues

### B03-SEC-001 SaveIntegrate starts CRM mutations through a weak request boundary

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 18
- Impact score: 22
- Likelihood/frequency score: 11
- Security urgency score: 14
- Performance gain score: 4
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B03
- Cross-module: B01 auth/session, F03A CRM operations, X02A status/cache
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml:26
  - SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml:132
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs:33
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs:65
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs:84
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs:123
  - SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs:168
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.AsyncWrapper.cs:51
- Evidence: The page posts weekly-report data to `SaveIntegrate`; the AJAX payload
  does not include an anti-forgery token. The `[HttpPost]` action captures
  account/password/login/list/member state from session-scoped `InMemoryContext`,
  starts untracked background upload with `CancellationToken.None`, mutates
  captured member lists, swallows background failures, and returns success before
  CRM persistence completes.
- Control/data/lifetime flow: browser AJAX POST -> B03 controller -> session
  `ListManager` state -> fire-and-forget task -> sync CRM upload wrapper.
- Impact: Authenticated/session-fallback requests can start high-impact CRM
  weekly-report mutations without visible B03-local anti-forgery/list authorization
  and without request-visible completion/failure semantics.
- Why this is necessary: B03 owns the weekly-report mutation boundary, so it must
  prove request integrity and list ownership before downstream CRM work starts.
- Recommended action: Add anti-forgery validation and token transmission, re-check
  current contact/list authorization before persistence, and replace untracked
  `Task.Run` with an idempotent queued upload/status model.
- Validation: evidence/runtime-validation-plan.md, B03-SEC-001.
- Rollback boundary: B03 controller/view/upload orchestration only.
- Extraction contract: authenticated contact/session/list ID and report payload
  in; durable upload command/status out.
- CCG round history:
  - Round 1: run `20260711-131640-b03-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B03-PERF-001 Weekly report flows perform unbatched CRM work inside nested loops

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 19
- Impact score: 21
- Likelihood/frequency score: 13
- Security urgency score: 3
- Performance gain score: 10
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: L
- Primary owner: B03
- Cross-module: F03A CRM operations, B02 member/contact identity, B07 LINE notifications
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs:145
  - SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs:259
  - SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs:313
  - SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs:548
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.AsyncWrapper.cs:51
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs:152
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs:225
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs:457
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/WeeklyReportManager.cs:135
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/WeeklyReportManager.cs:212
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/WeeklyReportManager.cs:330
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/WeeklyReportManager.cs:505
- Evidence: B03 repeatedly queries hierarchy lists, weekly-report relationships,
  members, contacts, present records, updates, owner assignment, and notifications
  inside list/member loops. The async wrapper moves synchronous CRM work to the
  thread pool rather than using native async I/O.
- Control/data/lifetime flow: one report request expands hierarchy lists and
  members, then repeatedly calls synchronous CRM query/update and notification
  operations from nested loops running on request or thread-pool work.
- Impact: CRM round trips and thread-pool work scale by role-query count, list
  count, member count, and present-record count.
- Why this is necessary: batching this B03-owned hot path removes repeated remote
  calls while preserving F03A as the owner of generic CRM operations.
- Recommended action: Add a batchable B03 weekly-report service with explicit CRM
  projections and replace nested duplicate scans with dictionaries/sets.
- Validation: evidence/runtime-validation-plan.md, B03-PERF-001.
- Rollback boundary: B03 weekly-report facade with compatibility adapter.
- Extraction contract: contact ID, list IDs, Sunday date, and projection in;
  weekly-report DTOs/upload result and call-count metrics out.
- CCG round history:
  - Round 1: run `20260711-131640-b03-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B03-EXT-001 InMemoryDataContextSmallGroup owns non-B03 managers

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 74
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 12
- Security urgency score: 5
- Performance gain score: 5
- Loop leverage score: 10
- Ease/reversibility score: 2
- Effort: L
- Primary owner: B03
- Cross-module: B02, B04, B05, B06, B07, F03A, X02A
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:53
  - SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:95
  - SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:906
  - SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:960
  - SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:1015
  - SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:1125
  - SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:1180
  - SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:1239
  - SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:1300
  - SpeechMessageProducts.ChurchReport/Models/ListManager.cs:25
  - SpeechMessageProducts.ChurchReport/Models/ListManager.cs:220
- Evidence: The B03 context creates/caches B03 list/report managers plus
  ListManagementDataManager, EquipmentDataManager, FeeList, AppointmentsListManager,
  DonationPaymentManager, PollManager, LINE binding state, and ToolUtilityClass.
- Control/data/lifetime flow: session setup creates one broad mutable context;
  unrelated module managers are retained behind it and reused by B03 controllers
  and cross-module consumers for the session lifetime.
- Impact: B03 extraction or acceleration requires touching unrelated owner concerns
  unless this context is split first.
- Why this is necessary: a narrow B03 state contract is required before B03 can be
  diagnosed, tested, or optimized independently from B02/B04/B05/B06/B07 state.
- Recommended action: Introduce a narrow B03 session/report-state contract, move
  non-B03 managers to owner modules, and keep compatibility properties until
  consumer gates pass.
- Validation: evidence/runtime-validation-plan.md, B03-EXT-001.
- Rollback boundary: additive adapter/interface layer first.
- Extraction contract: B03 owns list/report session DTOs and weekly-report facades;
  other modules own their managers.
- CCG round history:
  - Round 1: run `20260711-131640-b03-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B03-SEC-002 SpiritLeader lookup lacks a visible B03-local ownership check

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 62
- Confirmed: true
- Evidence confidence: 15
- Impact score: 15
- Likelihood/frequency score: 8
- Security urgency score: 12
- Performance gain score: 3
- Loop leverage score: 6
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B03
- Cross-module: B02 member/contact identity, F03A CRM operations, B01 auth/session
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Views/Home/DetailGrid.cshtml:79
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SpiritLeaderLookupController.cs:27
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SpiritLeaderLookupController.cs:39
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs:541
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs:2765
- Evidence: The grid passes row `HappyGroupListEntityId` into the lookup API. The
  controller accepts `id`, retrieves list/member/contact data, and returns leader
  names without visible B03-local list ownership validation.
- Control/data/lifetime flow: browser row ID -> lookup controller `id` -> B03 list,
  member, and contact retrieval -> leader-name response.
- Impact: If CRM/service-level security does not reject arbitrary IDs, an
  authenticated user may enumerate leader names for another list.
- Why this is necessary: the missing local ownership check is confirmed even
  though exploitability remains a runtime-validation question.
- Recommended action: Validate requested list ID against the current contact's
  permitted B03 list set, or remove caller-supplied list IDs from the API.
- Validation: evidence/runtime-validation-plan.md, B03-SEC-002.
- Rollback boundary: B03 lookup controller/service only.
- Extraction contract: current contact/session plus requested list ID in;
  authorized lookup values out.
- CCG round history:
  - Round 1: run `20260711-131640-b03-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

## Runtime Validation Pending

Runtime validation is pending because this pass is diagnosis-only and B03 has no
direct executable gate. The validation plan covers anti-forgery/list authorization,
arbitrary-list lookup denial, CRM call-count measurement, and context extraction
consumer gates.

## Deleted Or Rejected Candidates

- Missing `[Authorize]` standalone issue: rejected because global authorization is
  registered and enforced.
- Cache-key registration-only concern: rejected because deterministic removal
  exists and no stale-cache failure was proven.
- CSS/static asset optimization: rejected for B03 because shared UI/vendor asset
  governance belongs to X03.

## Cross-Module Handoffs

- B01: confirm anti-forgery/session identity pattern for authenticated AJAX POSTs.
- F03A: provide batchable CRM query/write contracts and fake/counting adapters.
- B02: define contact/list ownership contract consumed by B03 lookup/save flows.
- B04/B06/B07/X02A/X03: validate consumers and UI/token/cache conventions before
  optimization.

## Final CCG Approval

Round 1 CCG review is pending. The current valid status is
`DEGRADED_REVIEW_PENDING` until the self-healing runner produces usable backend
output or records a concrete blocker.
