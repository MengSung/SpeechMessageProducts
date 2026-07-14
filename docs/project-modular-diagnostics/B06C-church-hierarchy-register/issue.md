# B06C Church Hierarchy Register Diagnostic Issues

Status: RUNTIME_VALIDATION_PENDING
Module: B06C
Workspace: B06C-church-hierarchy-register
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: fdc7242e5f80bf330f3fcac28b5aaa4020f8040cc91f44a5f8e120d93cc720ec

## Executive Summary

B06C has six confirmed static/governance findings. The active qualification path
has caller-controlled identity and anti-forgery concerns. Register code contains
confirmed dormant credential and logic defects, but bounded convergence proved
that `Home.ProcessRegister` is absent, so those paths are not runtime reachable.

## Ranked Confirmed Issues

### B06C-SEC-002 Qualification flow accepts caller-controlled LINE identity

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 18
- Impact score: 23
- Likelihood/frequency score: 10
- Security urgency score: 14
- Performance gain score: 0
- Loop leverage score: 8
- Ease/reversibility score: 5
- Effort: M
- Primary owner: B06C
- Cross-module: B01, B02
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:569
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:609
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:617
  - SpeechMessageProducts.ChurchReport/ViewModels/GalleryViewModel.cs:42
- Evidence: qualification input includes a caller-provided LINE user ID that is
  copied through shared view-model/context state into CRM-facing processing.
- Control/data/lifetime flow: browser qualification DTO -> Home controller ->
  shared `LineBindingViewModel` -> CRM contact/qualification update.
- Impact: caller-controlled identity reaches CRM PII operations; cross-account
  exploitation remains subject to B01 guard validation.
- Why this is necessary: B06C must bind qualification mutation to server-verified
  identity rather than a posted identifier.
- Recommended action: obtain identity from the authenticated B01 context and reject
  mismatched posted identifiers before B02/CRM access.
- Validation: authenticated user A posts user B identity; reject before CRM query or
  mutation and record no sensitive response.
- Rollback boundary: B06C qualification controller/view-model identity adapter.
- Extraction contract: verified B01 identity and qualification fields in; B06C
  qualification command/result out.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; source rechecked true.

### B06C-SEC-003 Qualification mutation lacks anti-forgery enforcement

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 73
- Confirmed: true
- Evidence confidence: 14
- Impact score: 22
- Likelihood/frequency score: 10
- Security urgency score: 14
- Performance gain score: 0
- Loop leverage score: 8
- Ease/reversibility score: 5
- Effort: S
- Primary owner: B06C
- Cross-module: B01, X01
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:603
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:605
  - SpeechMessageProducts.ChurchReport/Views/Home/QualificationView.cshtml:203
  - SpeechMessageProducts.ChurchReport/Startup.cs:377
  - SpeechMessageProducts.ChurchReport/Startup.cs:389
- Evidence: the active qualification mutation and submitting view have no local or
  globally automatic anti-forgery enforcement in the bounded source/filter search.
- Control/data/lifetime flow: authenticated browser POST -> B06C qualification
  action -> shared state and CRM mutation.
- Impact: a cross-site request can change qualification/contact data under an
  authenticated browser session.
- Why this is necessary: authorization does not establish request origin or intent.
- Recommended action: enforce anti-forgery on the action or automatically for unsafe
  methods and submit a token from the view.
- Validation: missing/invalid token requests fail before shared-state or CRM writes.
- Rollback boundary: B06C view/action and X01 MVC filter registration.
- Extraction contract: N/A.
- CCG round history:
  - Round 1: Claude requested runtime validation; bounded search statically
    confirmed the active qualification gap; Gemini quota blocked.

### B06C-SEC-001 Orphaned register connector writes raw passwords if reactivated

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 72
- Confirmed: true
- Evidence confidence: 20
- Impact score: 24
- Likelihood/frequency score: 3
- Security urgency score: 15
- Performance gain score: 0
- Loop leverage score: 8
- Ease/reversibility score: 2
- Effort: M
- Primary owner: B06C
- Cross-module: B01
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs:132
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs:146
  - SpeechMessageProducts.ChurchReport/Models/RegisterManager.cs:27
  - SpeechMessageProducts.ChurchReport/Models/RegisterManager.cs:31
  - SpeechMessageProducts.ChurchReport/Views/Home/Register.cshtml:20
- Evidence: dormant register code assigns submitted password material to CRM fields;
  bounded route search found no `Home.ProcessRegister` action.
- Control/data/lifetime flow: orphaned register form/model -> register connector ->
  raw password field write to CRM, only if the path is reactivated.
- Impact: reactivation would persist plaintext-equivalent credentials; there is no
  active request-path impact today.
- Why this is necessary: the dormant code must not be reconnected without a B01
  credential-storage contract.
- Recommended action: require adaptive hashing/migration through B01 and delete raw
  password writes before any route restoration.
- Validation: pre-reactivation static gate plus credential storage tests; no raw
  password appears in CRM/session/log fixtures.
- Rollback boundary: dormant B06C register connector and B01 credential adapter.
- Extraction contract: registration identity/password in; B01 credential command
  and safe result out.
- CCG round history:
  - Round 1: Claude requested runtime validation; bounded search recorded
    `STATIC_CONFIRMED_ORPHANED_PATH`; Gemini quota blocked.

### B06C-EXT-002 Qualification flow mixes identity, CRM, and presentation state

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 57
- Confirmed: true
- Evidence confidence: 17
- Impact score: 15
- Likelihood/frequency score: 9
- Security urgency score: 8
- Performance gain score: 2
- Loop leverage score: 5
- Ease/reversibility score: 1
- Effort: M
- Primary owner: B06C
- Cross-module: B01, B02, B06A
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:569
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:582
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:609
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:622
  - SpeechMessageProducts.ChurchReport/ViewModels/GalleryViewModel.cs:42
  - SpeechMessageProducts.ChurchReport/ViewModels/GalleryViewModel.cs:47
- Evidence: controller methods read/post a mutable shared view model and invoke
  concrete CRM/reference behavior in the same qualification workflow.
- Control/data/lifetime flow: posted DTO -> shared mutable presentation state ->
  concrete identity/reference/CRM utilities.
- Impact: identity validation, CRM operations, and UI state cannot be tested or
  changed independently.
- Why this is necessary: trusted identity, CRM access, and presentation state need
  separate owner boundaries before B06C optimization.
- Recommended action: extract a B06C qualification command service consuming B01
  identity, B02 contact, and B06A reference contracts.
- Validation: fake-backed identity mismatch, qualification mapping, CRM failure, and
  result-view tests.
- Rollback boundary: additive service behind existing Home controller routes.
- Extraction contract: verified identity plus qualification DTO in; command result
  and presentation DTO out.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; source rechecked true.

### B06C-PERF-002 Orphaned register eligibility condition is tautological

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 51
- Confirmed: true
- Evidence confidence: 20
- Impact score: 12
- Likelihood/frequency score: 3
- Security urgency score: 6
- Performance gain score: 1
- Loop leverage score: 5
- Ease/reversibility score: 4
- Effort: S
- Primary owner: B06C
- Cross-module: false
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs:118
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs:153
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs:155
- Evidence: a dormant register eligibility condition compares alternatives in a
  way that is always satisfied; no active route reaches the connector.
- Control/data/lifetime flow: orphaned register input -> tautological eligibility
  branch -> list/contact processing if the path is reactivated.
- Impact: definite dormant logic defect can perform unnecessary or incorrect work,
  but has no current request-path performance cost.
- Why this is necessary: the condition must be corrected or removed before any
  route restoration so a false gate is not revived.
- Recommended action: replace the tautology with explicit eligibility states and
  tests or delete the orphaned branch.
- Validation: pre-reactivation no-list and allowed-list scenarios.
- Rollback boundary: dormant register eligibility function only.
- Extraction contract: N/A.
- CCG round history:
  - Round 1: Claude requested runtime validation; bounded search recorded
    `STATIC_CONFIRMED_ORPHANED_PATH`; Gemini quota blocked.

### B06C-EXT-003 Church hierarchy consumer contract with B06A is implicit

- Category: Extraction
- Severity: Medium
- Priority: P3
- Priority score: 49
- Confirmed: true
- Evidence confidence: 16
- Impact score: 12
- Likelihood/frequency score: 8
- Security urgency score: 0
- Performance gain score: 4
- Loop leverage score: 7
- Ease/reversibility score: 2
- Effort: S
- Primary owner: B06C
- Cross-module: B06A
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:157
  - SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:162
  - SpeechMessageProducts.ChurchReport/Controllers/ListManagementController.cs:93
  - SpeechMessageProducts.ChurchReport/Controllers/ListManagementController.cs:111
  - docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:749
  - docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:809
- Evidence: B06C compatibility routes consume list/hierarchy behavior that the map
  assigns to B06A, without a named provider/consumer payload and paging contract.
- Control/data/lifetime flow: B06C compatibility route -> B06A hierarchy provider ->
  `DataSourceLoader` response.
- Impact: hierarchy ownership can be duplicated and route changes can couple B06A
  and B06C implementation.
- Why this is necessary: an explicit contract prevents B06C from absorbing B06A
  reference-data responsibility.
- Recommended action: define the B06A provider/B06C consumer payload,
  authorization, paging, and compatibility route contract.
- Validation: provider/consumer fixture and route snapshot.
- Rollback boundary: compatibility adapter and contract tests only.
- Extraction contract: B06A hierarchy query/page DTO consumed by B06C with
  authorization and paging gate.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; fixture measurement remains
    unavailable.

## Runtime Validation Pending

- Active qualification identity and anti-forgery scenarios require an isolated
  fake CRM/B01 fixture. Register scenarios are `REGISTER_NOT_RUNTIME_REACHABLE`
  because `Home.ProcessRegister` is absent.

## Deleted Or Rejected Candidates

- B06C-PERF-001 has no active request-path performance impact.
- B06C-EXT-001 extraction is unnecessary until the missing register route is
  deliberately restored.
- The register half of the former anti-forgery candidate is not active and remains
  `REGISTER_NOT_RUNTIME_REACHABLE`.

## Cross-Module Handoffs

- B01 owns identity and credentials; B02 owns contacts; B06A owns hierarchy/reference
  data; F03A owns generic CRM operations.

## Final CCG Approval

`RUNTIME_VALIDATION_PENDING`; static findings are retained but active qualification
runtime gates and provider/consumer tests remain unavailable.
