# B04B Appointment Equipment Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: B04B
Workspace: B04B-appointment-equipment
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: c0f21f29833ea2c73f45a00bba27951054331b5b4ceacb6278a121b351dba3cf

Nested agent count: 0

## Executive Summary

B04B owns appointment, equipment borrowing, lesson/course, equipment status, and related Equipment UI. This diagnostic found one Critical security issue requiring immediate handling and two clear performance/extraction candidates. The Critical issue is that the appointment LINE binding path can mint session/auth-ticket identity from a request-supplied LINE user id without a visible trusted LINE/LIFF proof in the B04B flow.

## Scope Summary

- Primary owner files reviewed: AppointmentController, EquipmentController, AppointmentsDownUpLoader, DownloadEquipment, EquipmentStatusCalculator, B04B models, and Equipment views.
- Dependency context only: B01 authentication/session helpers, F03A CRM access, B02 member/contact data, X03 shared UI.
- Excluded: attendance, present-record, scheduler/QR, shared static assets, route composition, and unrelated business modules.

## Ranked Confirmed Issues

### B04B-SEC-001 Appointment LINE binding can mint identity from caller-supplied LINE user id

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 82
- Confirmed: true
- Evidence confidence: 18
- Impact score: 24
- Likelihood/frequency score: 13
- Security urgency score: 15
- Performance gain score: 0
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B04B
- Cross-module: X05Q, X04A, B01
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:134
  - SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:158
  - SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:185
  - SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:188
  - SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:193
  - SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:1011
  - SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs:25
  - SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs:26
  - SpeechMessageProducts.ChurchReport/Security/LoginClaimsFactory.cs:14
  - SpeechMessageProducts.ChurchReport/appsettings.json:70
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:135
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs:111
- Evidence:
  - `LoadAppointmentByLineId` accepts `UserLineId`, `GroupId`, `RoomId`, and `ViewType` from POST request parameters.
  - `SetupLineBindingContext` copies the request values into B04B in-memory/session-facing state.
  - `SetupAppointmentAccountPasswordAsync` assigns `"LineIdLogin"` plus the supplied LINE user id to appointment account/password state, writes `_LoginPassword` and `_SessionUserId`, then calls `IssueAuthTicketAsync`.
  - Appointment/equipment CRM connector login resolution treats `"LineIdLogin"` password as a LINE user id and resolves contact data through `RetrieveContactEntityByLineUserId`.
  - Base configuration sets `Security:EnforceGlobalAuthorization` to `false`,
    and neither Production nor Development overrides it. No local `[Authorize]`
    evidence was found for this endpoint, so the current default route is
    reachable without first proving a valid or fallback session.
  - `GlobalAuthorizationFilter` reads that setting and immediately returns when
    it is false, bypassing both anonymous-attribute and authenticated-user
    checks.
  - `IssueAuthTicketAsync` passes the caller-derived identity to
    `LoginClaimsFactory.Build` and then signs in the resulting principal.
- Control/data/lifetime flow:
  - Request body `UserLineId` -> B04B line binding model -> B04B appointment account/password -> session keys -> auth ticket -> CRM contact lookup by LINE id.
- Impact:
  - An anonymous caller can currently reach the endpoint and attempt to pivot
    appointment/equipment identity to another LINE user id because global
    authorization is disabled by default.
  - B04B appointment reads/writes and equipment data can then run under the wrong contact context.
- Why this is necessary:
  - Identity proofs must be verified by a trusted provider path before session/auth-ticket issuance. Caller-supplied identifiers are not authorization proof.
- Recommended action:
  - Require a verified LINE/LIFF token or server-side binding before setting B04B session identity or issuing an auth cookie.
  - Refuse mismatches between current authenticated subject and requested LINE subject.
  - Add tests for forged `UserLineId`, cross-user session pivot, and existing-session identity switching.
- Validation:
  - Runtime validation plan documents the exact forged-ID test scenario.
- Rollback boundary:
  - B04B AppointmentController and B04B appointment binding/session setup only; no attendance or QR behavior change required.
- Extraction contract:
  - `AppointmentLineBindingVerifier`: input trusted LINE proof plus B04B view context; output authorized appointment session identity or rejection.
- CCG round history:
  - Round 1: Claude KEEP with required evidence/score rewrite; Gemini quota
    blocked; source rechecked true.
  - Convergence R2: Claude REWRITE for ownership/evidence completeness; Gemini
    quota blocked; source rechecked true.

### B04B-PERF-001 Equipment lesson/status loading has nested CRM N+1 retrieval

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 74
- Confirmed: true
- Evidence confidence: 18
- Impact score: 20
- Likelihood/frequency score: 12
- Security urgency score: 0
- Performance gain score: 10
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B04B
- Cross-module: false; F03A is dependency context only
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs:303
  - SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs:375
  - SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs:391
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs:255
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs:290
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs:331
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/EquipmentStatusCalculator.cs:43
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/EquipmentStatusCalculator.cs:141
- Evidence:
  - Equipment lesson/status flows loop groups, contacts, relationship rows, stor lesson entities, and disciple lesson entities.
  - `CalculateEquipmentStatusForMembers` retrieves contact entities by full name and then invokes per-contact status calculation.
- Control/data/lifetime flow:
  - Equipment view/detail request -> member/list data -> per-contact CRM relationship query -> per-lesson CRM entity query -> per-lesson disciple entity query -> grid rows.
- Impact:
  - Page latency and CRM load scale with nested row counts instead of page/group-level query count.
  - The issue is amplified by detail expansion behavior in the Equipment views.
- Why this is necessary:
  - The current pattern is expensive by design and blocks later optimization unless isolated behind a B04B query service.
- Recommended action:
  - Introduce an equipment lesson/status read service that returns projected rows with batched CRM queries.
  - Prefer contact ids over full names.
  - Measure using existing `PerfPhase` labels before and after.
- Validation:
  - Runtime validation plan defines query count and latency measurements.
- Rollback boundary:
  - B04B equipment read model only; no B04A/B04C ownership overlap.
- Extraction contract:
  - Input: session/contact context, group id, contact id, filter/page options. Output: equipment group/contact/lesson read models.
- CCG round history:
  - Round 1: no usable backend output; source rechecked true.
  - Convergence R2: Claude REWRITE for ownership/evidence completeness; Gemini
    quota blocked; the required rewrite was reflected.

### B04B-PERF-002 Equipment UI auto-expansion multiplies backend detail loads

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 59
- Confirmed: true
- Evidence confidence: 17
- Impact score: 13
- Likelihood/frequency score: 11
- Security urgency score: 0
- Performance gain score: 8
- Loop leverage score: 7
- Ease/reversibility score: 3
- Effort: S
- Primary owner: B04B
- Cross-module: false; X03 shared assets are context only
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Views/Equipment/EquipmentContactView.cshtml:79
- Evidence:
  - Contact detail rows are configured with `AutoExpandAll(true)`, which can trigger all visible contacts to load lesson detail grids.
- Control/data/lifetime flow:
  - Equipment grid render -> contact master-detail auto expansion -> multiple
    `LoadEquipmentStorLessons` requests -> nested CRM retrieval.
- Impact:
  - UI behavior amplifies B04B-PERF-001 and increases client-side work on large grids.
- Why this is necessary:
  - This is a low-risk way to reduce request fan-out before deeper CRM batching.
- Recommended action:
  - Lazy-load details on user expansion.
- Validation:
  - Compare request counts and latency with contact auto-expand enabled vs disabled.
- Rollback boundary:
  - B04B Equipment views only.
- Extraction contract:
  - N/A.
- CCG round history:
  - Round 1: Claude requested splitting runtime-only event-handler impact from
    the statically confirmed auto-expansion fan-out; Gemini quota blocked;
    source rechecked true.
  - Convergence R2: Claude REWRITE because handler-only lines remained in the
    confirmed issue Files list; removed from confirmed evidence and retained in
    B04B-PERF-RV-001; source rechecked true.

## Runtime Validation Pending

- B04B-SEC-001: validate forged LINE id/session pivot behavior in a safe environment.
- B04B-PERF-001: measure CRM query count and latency using current `PerfPhase` labels.
- B04B-PERF-002: measure detail request fan-out with and without contact auto-expand.
- B04B-PERF-RV-001: determine whether row-prepared callbacks attach duplicate
  event handlers across grid rerenders; this is not a confirmed performance
  issue until browser instrumentation proves listener growth.

## Deleted Or Rejected Candidates

- CSRF on B04B mutation endpoints: plausible, but global cookie/antiforgery policy ownership is B01/X01 context; not promoted without broader route/security review.
- Shared DevExtreme/static asset optimization: X03 ownership, not B04B.
- Attendance/present-record and QR/scheduler findings: explicitly out of B04B scope.

## Cross-Module Handoffs

- B01: define the trusted LINE/LIFF proof contract used by B04B before session/auth-ticket issuance.
- F03A: provide or expose a batched CRM query capability for equipment lesson/status projections.
- X03: optional future review for shared CSS/JS hover and grid styling conventions, outside this B04B diagnostic.

## CCG Outcome Summary

- CCG review status: DEGRADED_REVIEW_PENDING.
- Prompt: .ccg/dual-model-runs/b04b-issue-review-r1-input.md
- Reviewer artifact: .ccg/dual-model-runs/b04b-issue-review-r1-reviewer.md
- Run folder: .ccg\dual-model-runs\20260712-123433-b04b-issue-review-r1-reviewer.

## Any Review Changes Applied

- Historical usable Claude findings from run
  `20260711-162341-b04b-issue-review-r1-reviewer` are reflected above. The later
  run `20260712-123433-b04b-issue-review-r1-reviewer` produced no usable backend
  output and contributes no verdict.

## Final CCG Approval

CCG status: DEGRADED_REVIEW_PENDING.
