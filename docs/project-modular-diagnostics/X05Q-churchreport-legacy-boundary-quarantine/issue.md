# X05Q ChurchReport Legacy Boundary Quarantine Diagnostic Issues

Status: RUNTIME_VALIDATION_PENDING
Module: X05Q
Workspace: X05Q-churchreport-legacy-boundary-quarantine
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: QUARANTINE
Issue document SHA-256: fa328e5e780fb88cfdc401ac4014fe53dfaf33483e2cb7bbd7a2302091838e80

Issue document SHA-256 before final CCG incorporation: BDB04989274E1D08D5BF70FFD2B463C4D006F699489DFF76DF8F9F11D4559797
Nested agent count: 0

## Executive Summary

X05Q is a quarantine owner, not a stable product module. The most valuable work is to shrink it safely by extracting explicit boundary contracts. The highest priority issue is a Critical session identity fallback boundary in `BaseChurchController`: auth ticket, ASP.NET Session, LINE identity, account/password mode, cached ListManager state, and CRM-backed rehydration are still coupled in shared legacy code. The next priority is the `/Home/*` compatibility facade, which preserves broad legacy entrypoints and manually service-locates dependencies. Performance findings are lower priority than the security boundary, but the same extraction path can reduce repeated ListManager setup, repeated CRM conversions, and process-wide upload serialization.

## Ranked Confirmed Issues

### X05Q-SEC-001 Session identity fallback remains inside the quarantine boundary

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 95
- Confirmed: true
- Evidence confidence: 19
- Impact score: 25
- Likelihood/frequency score: 14
- Security urgency score: 15
- Performance gain score: 7
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: X05Q
- Cross-module: B01, B02, B03, B05, B07, X01, X04A consumers affected
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:558`
  - `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:641`
  - `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:743`
  - `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:978`
- Evidence: Session `_MemberInfoAccess`, `_LoginAccount`, `_LoginPassword`, `_SessionUserId`, auth claims, `InMemoryContext.ListManager`, and CRM-backed setup are reconciled inside the shared base controller. `RegenerateSessionId` clears and restores values but logs that ASP.NET Core does not rotate the Session ID.
- Control/data/lifetime flow: request enters a business controller inheriting `BaseChurchController`; base controller reads Session and/or claims; it may rebuild ListManager and write session login fields; downstream business logic uses the reconstructed context.
- Impact: identity/session confusion can affect multiple ChurchReport business modules because the boundary is shared and pre-module.
- Why this is necessary: no module can safely own or optimize files behind this base controller until identity state has one explicit adapter contract.
- Recommended action: extract `LegacySessionIdentityAdapter` with typed inputs/outputs, one validation decision per request, strict claim/session/account-mode matching, and auditable cache metadata.
- Validation: run the scenarios in `evidence/runtime-validation-plan.md` for X05Q-SEC-001 after an approved implementation branch exists.
- Rollback boundary: keep current controller routes; call adapter from the existing base controller first.
- Extraction contract: input auth principal/session/request/current context; output typed `LegacyUserContext`.
- CCG round history:
  - Round 1: Gemini quota/billing blocked; Claude KEEP; source rechecked true

### X05Q-SEC-002 `/Home/*` compatibility routes preserve a dangerous legacy entrypoint surface

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 18
- Impact score: 21
- Likelihood/frequency score: 13
- Security urgency score: 13
- Performance gain score: 4
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: X05Q
- Cross-module: many B modules via redirects and delegated calls
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:65`
  - `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:80`
  - `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:149`
  - `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:401`
- Evidence: `HomeController` retains broad backward-compatible routes for login, LINE binding, payment, fee, dedication, scheduler, equipment, and diagnostics/cache testing. Several methods manually resolve dependencies through `HttpContext.RequestServices`.
- Control/data/lifetime flow: old `/Home/*` URLs accept route/query/form values, then redirect or delegate to downstream controllers while preserving legacy parameter shapes.
- Impact: authorization, anti-forgery, route owner, and service lifetime assumptions are not captured in one manifest. Legacy callers may bypass the intended new controller boundaries.
- Why this is necessary: compatibility should be a deliberate facade, not an undocumented set of controller methods.
- Recommended action: create a `LegacyHomeRouteFacade` manifest listing route, owner, method, auth/session preconditions, accepted parameters, and target.
- Validation: route table inspection and manifest tests; no runtime write commands in this diagnosis.
- Rollback boundary: preserve route templates and change only internals after approval.
- Extraction contract: input legacy route and route values; output owned redirect/delegation target or quarantine failure.
- CCG round history:
  - Round 1: Gemini quota/billing blocked; Claude KEEP; source rechecked true

### X05Q-PERF-002 Mixed WebServiceConnector flows need batch query and converter seams

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 68
- Confirmed: true
- Evidence confidence: 16
- Impact score: 16
- Likelihood/frequency score: 11
- Security urgency score: 5
- Performance gain score: 9
- Loop leverage score: 9
- Ease/reversibility score: 2
- Effort: L
- Primary owner: X05Q
- Cross-module: B02, B03, B04A, B05, B06C
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs:260`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:219`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs:298`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Contact.cs:36`
- Evidence: multiple connector classes iterate CRM `EntityCollection` and in-memory lists, often nested, without a shared query/converter ownership contract.
- Control/data/lifetime flow: account/password context enters connector, CRM collections are materialized, then business DTOs are assembled through repeated loops and converters.
- Impact: likely N+1 and over-materialization risk; ownership remains unclear because query and conversion are mixed.
- Why this is necessary: batch query contracts both improve performance and create handoff-ready module boundaries.
- Recommended action: extract typed CRM query and converter facades for list hierarchy, member identity, present records, and weekly reports.
- Validation: count CRM calls, selected columns, materialized entities, and loop iterations.
- Rollback boundary: add facades beside existing connector methods, migrate one consumer at a time.
- Extraction contract: input typed query request; output typed DTO collection plus diagnostics.
- CCG round history:
  - Round 1: Gemini quota/billing blocked; Claude NEEDS_RUNTIME_VALIDATION; source rechecked true

### X05Q-PERF-001 ListManager setup and cache/session rehydration are duplicated across the boundary

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 66
- Confirmed: true
- Evidence confidence: 17
- Impact score: 15
- Likelihood/frequency score: 12
- Security urgency score: 8
- Performance gain score: 8
- Loop leverage score: 4
- Ease/reversibility score: 2
- Effort: M
- Primary owner: X05Q
- Cross-module: B03, B04A, B04C, B06, B07 likely consumers
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:641`
  - `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:705`
  - `SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs:40`
  - `SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs:118`
- Evidence: base controller can lazily rebuild ListManager from session data while `ListManagerCacheExtensions` separately caches and invalidates ListManager setup.
- Control/data/lifetime flow: controller request validates session, checks cache/session key, may call `SetupListManager`, then views/actions use mutable ListManager state.
- Impact: repeated CRM setup and conversion work on cache misses; hard-to-predict invalidation when session, password, and account mode diverge.
- Why this is necessary: the same adapter needed for security can batch validation and hydration.
- Recommended action: make `LegacySessionIdentityAdapter` return a typed cached context with a single ListManager hydration decision.
- Validation: measure setup count, cache hit ratio, CRM calls, and request time.
- Rollback boundary: adapter can be introduced behind existing calls.
- Extraction contract: input session/account/date/cache state; output hydrated context and cache metadata.
- CCG round history:
  - Round 1: Gemini quota/billing blocked; Claude KEEP; source rechecked true

### X05Q-PERF-003 Weekly report upload has process-wide serialization and read-after-write reload

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 53
- Confirmed: true
- Evidence confidence: 15
- Impact score: 13
- Likelihood/frequency score: 8
- Security urgency score: 3
- Performance gain score: 8
- Loop leverage score: 5
- Ease/reversibility score: 1
- Effort: L
- Primary owner: X05Q
- Cross-module: B03/B04A after ownership proof
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Core.cs:80`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Core.cs:92`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/WeeklyReportManager.cs:296`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/WeeklyReportManager.cs:328`
- Evidence: upload uses a static lock and `WeeklyReportManager.UploadWeeklyReport` returns `DownloadWeeklyReport` after upload.
- Control/data/lifetime flow: upload request enters connector, shared static lock serializes work, then a read path reloads data.
- Impact: avoidable lock wait and extra CRM IO are likely under concurrent uploads, but consistency needs runtime measurement before change.
- Why this is necessary: lower-value than security fixes, but should be included in the future batch-query extraction.
- Recommended action: validate whether keyed concurrency by list/week/report and partial post-write verification can replace global locking and full reload.
- Validation: concurrent upload measurement in an approved branch.
- Rollback boundary: keep old path behind feature flag until consistency proof exists.
- Extraction contract: keyed write policy plus post-write validation DTO.
- CCG round history:
  - Round 1: Gemini quota/billing blocked; Claude NEEDS_RUNTIME_VALIDATION; source rechecked true

## Runtime Validation Pending

- X05Q-PERF-001 needs runtime measurement of cache hit ratio, CRM call count, setup count, and request wall time.
- X05Q-PERF-002 needs CRM call-count measurement to rank individual connector extractions.
- X05Q-PERF-003 needs runtime measurement before any lock or reload change.

## Deleted Or Rejected Candidates

- `wwwroot/js/TreeView.js` remains X05Q scope but has no confirmed security or performance issue from inspected evidence.
- `Services/Navigation/INavigationService.cs` is interface-only in the inspected scope and does not prove a standalone issue.
- Hardcoded secret values in `appsettings.json` are owned by X04A; X05Q records only the credential-like boundary dependency.

## Cross-Module Handoffs

- B01 should consume the final session identity contract.
- X01 should own host route/lifetime registration once the Home compatibility facade is manifest-driven.
- X04A should own secret/config validation, with X05Q supplying the session/config usage matrix.
- B modules should receive WebServiceConnector methods only after batch query/converter responsibility proof.

## Final CCG Approval

Final review status: RUNTIME_VALIDATION_PENDING

CCG degraded review summary:

- Summary file: `.ccg/dual-model-runs/20260712-132714-x05q-issue-review-r1-reviewer/summary.json`
- completedBackends: `claude`
- failedBackends: `gemini`
- Degraded reason: Gemini provider quota/billing blocked with usable Claude output.
- Claude substantive review was usable, but its per-issue verdicts included
  `NEEDS_RUNTIME_VALIDATION` for X05Q-PERF-002 and X05Q-PERF-003. Under the
  workflow, those verdicts override module-level approval until the required
  runtime evidence exists.
- X05Q-PERF-001 also remains in the runtime measurement queue because no
  instrumentation currently proves setup/cache frequency.
- Current blocker:
  `RUNTIME_VALIDATION_PENDING_BLOCKED_BY_INSTRUMENTATION_OR_ISOLATED_FIXTURE`.
