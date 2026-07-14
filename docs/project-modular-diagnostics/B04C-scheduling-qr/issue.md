# B04C Scheduling QR Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: B04C
Workspace: B04C-scheduling-qr
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: dab5c26e5e1a206e17032e61d341ad442719e8a495fde96239fe1f97f8e2d886

## Executive Summary

B04C owns Scheduler API/UI and personal, small-group, Sunday, course, and poll QR operation. The diagnostic found two high-value security issues, one performance issue, and one extraction issue. The highest-risk pattern is that QR scan POSTs accept client-posted LINE identity fields while the QR target is held in mutable in-memory context rather than a signed, expiring, replay-checked token. The scheduler mutation API also exposes raw create/update/delete actions without visible authorization, anti-forgery, ownership, or model validation checks in the controller.

CCG reviewer round 1 was executed. Gemini failed with provider quota/billing, Claude failed with session limit, and `completedBackends` is empty. Because no backend produced usable output, this diagnostic remains `DEGRADED_REVIEW_PENDING` and must not be marked `APPROVED_DEGRADED`.

## Ranked Confirmed Issues

### B04C-SEC-001 QR scan processing trusts client-posted LINE identity and mutable QR context

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 92
- Confirmed: true
- Evidence confidence: 19
- Impact score: 24
- Likelihood/frequency score: 14
- Security urgency score: 15
- Performance gain score: 6
- Loop leverage score: 9
- Ease/reversibility score: 5
- Effort: M
- Primary owner: B04C
- Cross-module: B01 identity contract; F06/B07 LINE workflow consumers
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:57
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:64
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:83
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:93
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:103
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:144
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:173
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:179
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:200
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:206
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:208
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:209
  - SpeechMessageProducts.ChurchReport/Views/QrCode/QrCodeView.cshtml:137
  - SpeechMessageProducts.ChurchReport/Views/QrCode/QrCodeView.cshtml:140
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SmallGroupQrCodeView.cshtml:134
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SmallGroupQrCodeView.cshtml:137
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SundayQrCodeView.cshtml:137
  - SpeechMessageProducts.ChurchReport/Views/QrCode/SundayQrCodeView.cshtml:140
- Evidence:
  - QR landing actions receive `QrCodeId` and store it in `InMemoryContext.ListManager.QrCodeId` before rendering LIFF views.
  - QR POST handlers accept `DisplayName`, `UserLineId`, `GroupId`, `RoomId`, and `ViewType` from AJAX payloads and call `SetupLineContext`.
  - QR utilities consume `InMemoryContext.ListManager.QrCodeId`, not a signed one-time token returned with the POST.
  - `SavePoll` uses `InMemoryContext.ListManager.QrCodeId` and `InMemoryContext.LineBindingViewModel.LineUserId`, coupling submission authority to mutable context.
- Control/data/lifetime flow:
  - QR URL -> controller stores QR code id -> LIFF view posts browser identity fields -> controller writes line context -> utility parses QR id string -> CRM attendance/poll/present-record mutation.
- Impact:
  - Forged, replayed, or cross-request scan data can be attributed to the wrong user or QR target if global LIFF verification and context isolation do not block it.
- Why this is necessary:
  - QR attendance is a state-changing identity flow and needs server-owned QR token verification before CRM mutation.
- Recommended action:
  - Introduce a B04C QR token verifier with signed target, action, expiry, nonce, and replay tracking; bind it to server-verified LINE identity and stop treating `ListManager.QrCodeId` as authoritative.
- Validation:
  - Test tampered QR id, expired token, replayed token, mismatched LINE user, and concurrent scans with different QR ids.
- Rollback boundary:
  - Keep legacy QR utilities behind an adapter and switch endpoints one scan type at a time.
- Extraction contract:
  - Input: signed QR token, verified LINE user id, action type. Output: validated scan context. Dependencies: B01 identity and F03A CRM. Consumers: QR controller POST actions and poll submission.
- CCG round history:
  - Round 1: Gemini failed with provider quota/billing; Claude failed with session limit; completedBackends []; source rechecked true

### B04C-SEC-002 SchedulerDataController exposes unaudited appointment mutation surface

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 88
- Confirmed: true
- Evidence confidence: 18
- Impact score: 23
- Likelihood/frequency score: 13
- Security urgency score: 15
- Performance gain score: 4
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: S
- Primary owner: B04C
- Cross-module: B01 authorization policy; X01 route/DI gate
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:29
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:41
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:47
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:50
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:51
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:56
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:62
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:65
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:66
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:71
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:77
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:80
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:82
- Evidence:
  - The controller derives from `Controller` and the inspected file has no visible `[Authorize]`, anti-forgery, ownership, route-level policy, or active model validation.
  - `Post` and `Put` deserialize raw `values` and have validation blocks commented out.
  - `Put` and `Delete` mutate appointments by client-supplied `key` using `First(...)`.
- Control/data/lifetime flow:
  - DevExtreme scheduler endpoint -> raw JSON values/key -> in-memory appointment context -> add/update/delete and save.
- Impact:
  - If globally reachable, appointments can be read or mutated by unauthorized callers. Even if global auth blocks anonymous access, ownership and validation remain missing in the action surface.
- Why this is necessary:
  - Scheduler is a state-changing business surface and needs explicit B01 authorization plus owner checks.
- Recommended action:
  - Add scheduler mutation policy, validate DTOs, require anti-forgery or API CSRF protection, and use safe key lookup with ownership checks.
- Validation:
  - Add anonymous, wrong-owner, malformed payload, missing anti-forgery, and valid-owner tests.
- Rollback boundary:
  - Apply route/action filters behind a feature flag and split read-only from mutation endpoints.
- Extraction contract:
  - Input: appointment DTO/key plus authenticated principal. Output: authorized scheduler command result. Dependencies: B01 authorization and B04B appointment boundary.
- CCG round history:
  - Round 1: Gemini failed with provider quota/billing; Claude failed with session limit; completedBackends []; source rechecked true

### B04C-PERF-001 QR attendance utilities perform per-record CRM retrieve/update loops

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 73
- Confirmed: true
- Evidence confidence: 18
- Impact score: 18
- Likelihood/frequency score: 12
- Security urgency score: 5
- Performance gain score: 10
- Loop leverage score: 8
- Ease/reversibility score: 2
- Effort: M
- Primary owner: B04C
- Cross-module: F03A CRM operations library
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:202
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:261
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:268
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:270
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:273
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:285
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:292
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:296
  - SpeechMessageProducts.ChurchReport/Models/PollManager.cs:76
  - SpeechMessageProducts.ChurchReport/Models/PollManager.cs:85
  - SpeechMessageProducts.ChurchReport/Models/PollManager.cs:116
  - SpeechMessageProducts.ChurchReport/Models/PollManager.cs:121
  - SpeechMessageProducts.ChurchReport/Models/PollManager.cs:393
  - SpeechMessageProducts.ChurchReport/Models/PollManager.cs:414
  - SpeechMessageProducts.ChurchReport/Models/PollManager.cs:450
  - SpeechMessageProducts.ChurchReport/Models/PollManager.cs:455
- Evidence:
  - Personal QR signing retrieves meeting statistics and present records, loops through present records, retrieves each entity, and updates each individually.
  - Poll save performs serial contact, lesson, stored-lesson, contact update, stored-lesson update, create, retrieve, and owner-assignment calls.
- Control/data/lifetime flow:
  - Single QR scan -> multiple CRM retrieve/update calls -> per-record update and optional related weekly-report update.
- Impact:
  - Scan bursts after services or classes can see avoidable CRM round trips and partial-write windows.
- Why this is necessary:
  - Reducing latency and race windows in B04C QR scan/write flows is a clear optimization path.
- Recommended action:
  - Add a batch-oriented QR attendance command service using F03A query/write contracts where available.
- Validation:
  - Instrument CRM call counts and latency per scan type before and after batching.
- Rollback boundary:
  - Keep old utility methods as adapter fallback while introducing batch validation/write service.
- Extraction contract:
  - Input: validated QR scan context. Output: attendance/poll write result. Dependency: F03A batch CRM API. Consumer: QrCodeController.
- CCG round history:
  - Round 1: Gemini failed with provider quota/billing; Claude failed with session limit; completedBackends []; source rechecked true

### B04C-EXT-001 QR and scheduler logic should be extracted behind explicit B04C services

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 70
- Confirmed: true
- Evidence confidence: 17
- Impact score: 16
- Likelihood/frequency score: 12
- Security urgency score: 8
- Performance gain score: 8
- Loop leverage score: 9
- Ease/reversibility score: 0
- Effort: L
- Primary owner: B04C
- Cross-module: B01, F03A, F06, B04A/B04B consumers
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:30
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:95
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:178
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:204
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:31
  - SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs:160
  - SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs:157
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:147
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:147
- Evidence:
  - Controllers instantiate QR utilities and `PollManager` directly.
  - Utility classes parse QR strings, resolve CRM entities, apply business rules, and write CRM records.
  - Scheduler API mutates raw appointment context rather than command/query services.
- Control/data/lifetime flow:
  - Controller -> direct utility/model instantiation -> CRM utility calls -> in-memory context mutation -> response.
- Impact:
  - Security verification, batching, runtime validation, and boundary tests cannot be inserted consistently.
- Why this is necessary:
  - The highest-leverage optimization is a B04C domain boundary with QR verification, scheduler validation, and batch write contracts.
- Recommended action:
  - Extract `IQrScanVerifier`, `IQrAttendanceCommandService`, and `ISchedulerCommandService`.
- Validation:
  - Contract tests for QR token verification, attendance command results, scheduler authorization, and consumer compatibility.
- Rollback boundary:
  - Introduce interfaces and adapters without deleting legacy utilities; migrate one scan type at a time.
- Extraction contract:
  - Inputs: signed token, verified identity, scheduler command DTO. Outputs: validation result and write result. Dependencies: B01 identity, F03A CRM, B04A/B04B data concepts.
- CCG round history:
  - Round 1: Gemini failed with provider quota/billing; Claude failed with session limit; completedBackends []; source rechecked true

## Runtime Validation Pending

- B04C-SEC-001: Verify whether LIFF id token validation exists outside the inspected controller/view path.
- B04C-SEC-002: Verify global auth/filter coverage for `SchedulerDataController`.
- B04C-PERF-001: Measure CRM call counts and latency under representative scan bursts.

## Deleted Or Rejected Candidates

- `WeeklyScheduleProvider.Initialize` static configuration was not promoted because it appears to initialize stable schedule configuration, not request-specific state.
- Encoded text corruption in comments/views was not promoted because it is outside QR/scheduler security, performance, and extraction value.
- Duplicated QR view JavaScript was not promoted independently; it is covered only where it affects token and identity flow.

## Cross-Module Handoffs

- B01: verified LINE identity/session contract for QR POSTs and scheduler mutation authorization.
- F03A: batch CRM retrieve/update contracts for QR attendance and poll flows.
- B04A: attendance/present-record ownership when QR writes create or update present records.
- B04B: appointment/equipment ownership boundaries for scheduler command validation.
- X01: route/auth snapshot gates for SchedulerDataController and QR POST endpoints.

## Final CCG Approval

CCG runner executed round 1, but no backend produced usable output. Gemini failed with provider quota/billing and Claude failed with session limit, so final status remains `DEGRADED_REVIEW_PENDING` and must not be promoted to `APPROVED_DEGRADED`.
