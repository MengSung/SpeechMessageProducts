# B04C Scheduling QR Diagnostic Issues

Status: DRAFT
Module: B04C
Workspace: docs/project-modular-diagnostics/B04C-scheduling-qr/
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: READY
Issue document SHA-256: pending
Nested agent count: 0

## Executive Summary

B04C owns scheduler API/UI and QR scan generation/operations for course, personal, small-group, and Sunday flows. This diagnostic found one Critical security issue requiring immediate handling and two obvious performance/design issues. The Critical issue is that QR scan POST endpoints trust a caller-supplied LINE user id from browser-side LIFF profile data and use it to mutate CRM attendance and QR scan state without visible server-side LINE token verification.

## Scope Summary

- Primary owner files reviewed: SchedulerController, SchedulerDataController, QrCodeController, QR utilities, scheduler views, QR views, and B04C-facing in-memory scheduler context.
- Dependency context only: F03A CRM access, F06/B07 LINE workflow/transport, B01 identity/session, B02 contact profile, B04A attendance/present-record, B04B appointment persistence, X03 shared UI.
- Excluded: group master data, LINE transport internals, attendance master data, and unrelated business modules.

## Ranked Confirmed Issues

### B04C-SEC-001 QR scan endpoints trust caller-supplied LINE user id for attendance mutations

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 93
- Confirmed: true
- Evidence confidence: 19
- Impact score: 25
- Likelihood/frequency score: 14
- Security urgency score: 15
- Performance gain score: 2
- Loop leverage score: 9
- Ease/reversibility score: 9
- Effort: M
- Primary owner: B04C
- Cross-module: false; B01/F06/B07/B04A are dependency or consumer context only
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Views/QrCode/QrCodeView.cshtml:59
  - SpeechMessageProducts.ChurchReport/Views/QrCode/QrCodeView.cshtml:113
  - SpeechMessageProducts.ChurchReport/Views/QrCode/QrCodeView.cshtml:140
  - SpeechMessageProducts.ChurchReport/Views/QrCode/PersonalQrCodeView.cshtml:60
  - SpeechMessageProducts.ChurchReport/Views/QrCode/PersonalQrCodeView.cshtml:113
  - SpeechMessageProducts.ChurchReport/Views/QrCode/PersonalQrCodeView.cshtml:145
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SmallGroupQrCodeView.cshtml:59
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SmallGroupQrCodeView.cshtml:110
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SmallGroupQrCodeView.cshtml:137
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SundayQrCodeView.cshtml:60
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SundayQrCodeView.cshtml:113
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SundayQrCodeView.cshtml:140
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:83
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:252
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:327
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:405
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:470
  - SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs:160
  - SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs:157
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:147
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:150
- Evidence:
  - QR views load LIFF, call `liff.getProfile()`, and POST profile-derived `UserLineId` to B04C endpoints without a server-verifiable LINE token.
  - Four B04C POST actions accept `UserLineId`, store line context, and pass it to QR utilities.
  - QR utilities resolve contacts by `RetrieveContactEntityByLineUserId(UserLineId)` and mutate CRM scan/attendance records.
- Control/data/lifetime flow:
  - Browser LIFF profile -> POST `UserLineId` -> QrCodeController -> InMemoryContext line binding -> QR utility -> CRM contact lookup -> present-record/course/weekly-report updates.
- Impact:
  - A forged or replayed QR POST can attempt to sign in/out as another LINE user and write attendance-like records for that contact.
  - Small-group QR can create/connect new contact/list records when a supplied LINE id is not found.
  - Sunday/personal QR can create present records and trigger attendance recalculation side effects.
- Why this is necessary:
  - Client-side profile data is not proof of identity. B04C must verify a LINE-issued token server-side before using a LINE subject for CRM writes.
- Recommended action:
  - Require and validate LINE id/access token proof on each QR POST.
  - Reject posted `UserLineId` when it differs from the validated token subject.
  - Add idempotency keyed by validated subject, QR id, and scan type.
  - Add forged user, missing-token, mismatched-token, and replay tests.
- Validation:
  - Runtime validation plan documents forged-subject tests for all four QR POST endpoints.
- Rollback boundary:
  - B04C QrCodeController, QR views, and QR utilities only; no attendance master-data or LINE transport internals change required.
- Extraction contract:
  - Verified QR scan command service: input QR kind/id/scan type plus validated LINE subject; output safe scan result or rejection.
- CCG round history:
  - Round 1: pending reviewer verdict; source rechecked true.

### B04C-PERF-001 Sunday/personal QR scan can fan out into nested CRM read/write loops

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 76
- Confirmed: true
- Evidence confidence: 18
- Impact score: 20
- Likelihood/frequency score: 12
- Security urgency score: 0
- Performance gain score: 10
- Loop leverage score: 10
- Ease/reversibility score: 6
- Effort: M
- Primary owner: B04C
- Cross-module: false; B04A and F03A are consumer/dependency context only
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:254
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:388
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:841
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:851
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:861
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:870
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:874
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:894
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:902
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SundayQrCodeView.cshtml:113
- Evidence:
  - Sunday QR scan may query present records, create missing records, loop through all contact lists, create weekly reports/present records, retrieve each generated weekly report, locate matching present records, sign them, and set saved flags.
  - The UI warns users to wait 5-10 seconds after scanning.
- Control/data/lifetime flow:
  - QR scan -> contact lookup -> present-record lookup -> missing-record creation -> list loop -> weekly-report generation -> present-record update -> saved-flag recalculation trigger.
- Impact:
  - A single scan can become a large synchronous CRM workload and is prone to user retry/replay during long waits.
- Why this is necessary:
  - QR scan is a hot user path; excessive synchronous CRM fan-out blocks both user experience and later looped optimization.
- Recommended action:
  - Introduce a bounded QR scan command service with idempotency and measured CRM call counts.
  - Move expensive weekly-report creation behind explicit guard/queue where feasible.
- Validation:
  - Runtime validation plan defines per-scan CRM call count and elapsed-time measurements.
- Rollback boundary:
  - B04C Sunday/personal QR utilities and scan orchestration.
- Extraction contract:
  - Input: validated subject, QR id/type, scan action; output: one scan result plus deferred recalculation signal if needed.
- CCG round history:
  - Round 1: pending reviewer verdict; source rechecked true.

### B04C-PERF-002 Scheduler API uses session-cached collection materialization instead of bounded query contract

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 61
- Confirmed: true
- Evidence confidence: 17
- Impact score: 14
- Likelihood/frequency score: 10
- Security urgency score: 0
- Performance gain score: 8
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B04C
- Cross-module: false; B04B appointment persistence is dependency context only
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:41
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:47
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:62
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:77
  - SpeechMessageProducts.ChurchReport/Models/InMemoryAppointmentsDataContext.cs:34
  - SpeechMessageProducts.ChurchReport/Models/InMemoryAppointmentsDataContext.cs:44
  - SpeechMessageProducts.ChurchReport/Views/Home/Scheduler.cshtml:15
  - SpeechMessageProducts.ChurchReport/Views/Home/Scheduler.cshtml:31
  - SpeechMessageProducts.ChurchReport/Views/Home/Scheduler.cshtml:327
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:144
- Evidence:
  - SchedulerDataController loads DevExtreme data from `_data.Appointments`, a session-cached in-memory collection.
  - Scheduler views expose month/agenda/timeline UI modes against load/insert/update/delete endpoints.
  - Appointment dependency code retrieves monthly appointments and lesson-derived rows, but B04C lacks a visible bounded query contract by scheduler visible range and current subject.
- Control/data/lifetime flow:
  - Scheduler UI -> B04C WebApi load -> session cached appointment collection -> in-memory DataSourceLoader filtering -> dependency appointment retrieval/persistence.
- Impact:
  - Scheduler memory and latency can grow with per-session appointment volume.
  - Read and write behavior spans memory cache and CRM persistence, making ownership and stale-data behavior hard to validate.
- Why this is necessary:
  - A bounded scheduler contract is needed before safe optimization of scheduler load, paging, and write validation.
- Recommended action:
  - Extract B04C scheduler read/query and command boundary with visible date range, schedule type, resource filters, and subject ownership.
- Validation:
  - Runtime validation plan defines month/agenda/timeline request count and latency measurement.
- Rollback boundary:
  - B04C scheduler controller/API/views and B04B appointment dependency adapter only if later needed.
- Extraction contract:
  - Input date window and subject; output projected scheduler rows. Separate command input for create/update/delete with ownership checks.
- CCG round history:
  - Round 1: pending reviewer verdict; source rechecked true.

## Runtime Validation Pending

- B04C-SEC-001: validate forged LINE id and missing/mismatched token behavior for all four QR POST endpoints.
- B04C-PERF-001: measure Sunday/personal QR scan CRM call count, elapsed time, and duplicate/retry behavior.
- B04C-PERF-002: measure scheduler month/agenda/timeline request count, latency, rows materialized, and cache growth.

## Deleted Or Rejected Candidates

- LINE SDK script provenance/SRI: useful web-hardening topic, but X03/shared asset policy and external CDN strategy are outside B04C ownership.
- Generic CSRF on all B04C POST endpoints: plausible, but global antiforgery/cookie policy belongs to B01/X01; kept as context under B04C-SEC-001.
- Appointment/equipment identity pivot: already captured by B04B; B04C only cites appointment persistence as scheduler dependency context.
- Group master-data CRUD issues: B03 ownership.
- Attendance schema and present-record master-data issues: B04A ownership.

## Cross-Module Handoffs

- B01: define trusted LINE/LIFF proof validation contract for B04C QR scans.
- F06/B07: provide reusable LINE notification/profile workflow only after B04C validates the subject; transport internals remain outside B04C.
- F03A: provide batched CRM query/update primitives for QR scan and scheduler read models.
- B04A: consume QR scan outcomes into attendance/present-record contracts without owning QR request validation.
- B04B: provide appointment persistence adapter for scheduler command/read boundary.

## CCG Outcome Summary

- CCG review status: pending.
- Prompt: .ccg/dual-model-runs/b04c-issue-review-r1-input.md
- Reviewer artifact: .ccg/dual-model-runs/b04c-issue-review-r1-reviewer.md
- Run folder: pending.

## Any Review Changes Applied

- Pending CCG review.

## Final CCG Approval

Pending.

