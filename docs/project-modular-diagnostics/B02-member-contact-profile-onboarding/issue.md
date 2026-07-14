# B02 Member Contact Profile Onboarding Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: B02
Workspace: B02-member-contact-profile-onboarding
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: afd0749d61b89c40ec2e2525b4becc26bb5c464eea6f332c3900752f6281cf76

Pre-CCG issue document SHA-256: b08665e3a75a63241b6748f16e8126f48789380ad5a4cdb655e5e726f41683b8

## Executive Summary

B02 owns member/contact profile, onboarding, personal information, avatar, and follow-up flows in `SpeechMessageProducts.ChurchReport`. The most worthwhile fixes are security-first: object-level contact authorization is inconsistent between `MemberInfo` and `Personal`, B02 mutating endpoints have no anti-forgery validation, and the `Personal` avatar endpoints can read arbitrary contact images or LINE picture URLs by client-supplied contact id. Performance and extraction opportunities are secondary but concrete: the maintain-profile save path launches untracked background CRM work, and legacy contact/onboarding connectors repeatedly recreate metadata caches that the newer controller path already shares through `IMemoryCache`.

## Ranked Confirmed Issues

### B02-SEC-001 Personal maintain endpoints update arbitrary contacts from client-supplied ContactId values

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 90
- Confirmed: true
- Evidence confidence: 19
- Impact score: 25
- Likelihood/frequency score: 14
- Security urgency score: 15
- Performance gain score: 2
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: B02
- Cross-module: B01 auth/session supplies authenticated identity; F03A CRM performs the update; B03/B04/B05/B07 consume the contact identity after mutation.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:914
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:931
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:983
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1004
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1079
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1164
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1194
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1245
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:822
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:830
- Evidence:
  - `SaveMaintainPersonInfomation` accepts `string aResult`, deserializes it into `List<Member>`, iterates each client-provided `member.ContactId`, retrieves that CRM contact, and updates phone/address/birthdate without a per-contact scope check.
  - `UpdateMaintainPersonInfomation` is `[HttpPut]`, accepts `key` as ContactId, creates `new Entity("contact", contactGuid)`, and calls `toolUtility.UpdateEntity(entityToUpdate)` without calling `CanViewContact`, `MemberInfoScopeGuard`, or an equivalent allowed-list check.
  - The nearby B02 `MemberInfo.UpdateContactInfo` path proves the expected guard: it parses `contactId` and rejects when `!CanViewContact(contactGuid)`.
- Control/data/lifetime flow:
  - Browser grid payload -> `PersonalController.SaveMaintainPersonInfomation` / `UpdateMaintainPersonInfomation` -> `ToolUtility.RetrieveEntity` / `UpdateEntity` -> CRM contact table.
- Impact:
  - Any authenticated user who can reach the route can craft contact ids outside the visible maintain grid and change CRM phone, address, or birthdate for another contact.
- Why this is necessary:
  - B02 is the member/contact identity provider for multiple business modules. Unauthorized mutation contaminates downstream attendance, payment, LINE, and group workflows.
- Recommended action:
  - Gate every `Personal` contact mutation through a shared B02 contact scope service using the same policy as `MemberInfo.CanViewContact` / `CanViewContactsBatch`.
  - Reject contacts not in the user's maintain scope before any CRM retrieve/update.
  - Add tests that attempt to update a contact outside the visible list and expect forbidden/validation failure.
- Validation:
  - Unit/integration tests for `SaveMaintainPersonInfomation` and `UpdateMaintainPersonInfomation` with in-scope and out-of-scope contact ids.
  - Manual route probe after login using an arbitrary contact id not in the user's group.
- Rollback boundary:
  - Controller-level authorization guard only; no schema or CRM contract change.
- Extraction contract:
  - Input: authenticated user context plus requested contact ids.
  - Output: authorized contact id set or explicit rejection.
  - Dependency: B01 identity/session, F03A CRM, B02 scope policy.
- CCG round history:
  - Round 1: run `20260711-130607-b02-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B02-SEC-002 B02 mutating endpoints lack anti-forgery validation

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 84
- Confirmed: true
- Evidence confidence: 18
- Impact score: 23
- Likelihood/frequency score: 13
- Security urgency score: 15
- Performance gain score: 0
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: B02
- Cross-module: X01 could centralize `AutoValidateAntiforgeryToken`; B01 supplies cookie/session identity.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Startup.cs:377
  - SpeechMessageProducts.ChurchReport/Startup.cs:381
  - SpeechMessageProducts.ChurchReport/Startup.cs:388
  - SpeechMessageProducts.ChurchReport/Startup.cs:389
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:888
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:913
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1164
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:747
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:820
  - SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs:345
  - SpeechMessageProducts.ChurchReport/Views/Personal/MaintainPersonInfomationView.cshtml:139
  - SpeechMessageProducts.ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml:464
  - SpeechMessageProducts.ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml:545
- Evidence:
  - MVC setup adds `ThemeViewDataFilter`, `StrictNoCacheFilter`, and `GlobalAuthorizationFilter`; inspection found no `AutoValidateAntiforgeryToken`, `ValidateAntiForgeryToken`, or B02 request-verification-token pattern in the B02 controllers/views.
  - B02 has CRM-mutating POST/PUT actions for personal info, maintain info, member info update, image upload, and new-person save.
  - The relevant views call these endpoints with raw `$.ajax` POSTs and no visible anti-forgery header/token.
- Control/data/lifetime flow:
  - Cross-site POST/PUT with authenticated cookie/session -> MVC action -> CRM mutation.
- Impact:
  - If a user's auth/session cookie is valid, a malicious site can attempt state-changing B02 requests without a B02 token barrier.
- Why this is necessary:
  - The project spec says authenticated identity is cookie-ticket based and security decisions must not rely on client-controllable request metadata. CSRF protection is a required complement for cookie-authenticated state changes.
- Recommended action:
  - Prefer an X01 global `AutoValidateAntiforgeryTokenAttribute` rollout with explicit anonymous/API exceptions, or add `[ValidateAntiForgeryToken]` to B02 mutations and send tokens from Razor/AJAX.
  - Include AJAX token plumbing for DevExtreme and raw `$.ajax` flows.
- Validation:
  - Tests or route probes proving POST/PUT without token is rejected and same-origin tokened requests still succeed.
- Rollback boundary:
  - MVC filter/controller/view token change; no CRM schema change.
- Extraction contract: N/A.
- CCG round history:
  - Round 1: run `20260711-130607-b02-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B02-SEC-003 Personal avatar endpoints expose arbitrary contact images and LINE picture URLs by ContactId

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 18
- Impact score: 22
- Likelihood/frequency score: 12
- Security urgency score: 14
- Performance gain score: 2
- Loop leverage score: 9
- Ease/reversibility score: 5
- Effort: S
- Primary owner: B02
- Cross-module: B01 authenticated session; B07 LINE profile data may be exposed as URL fallback.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:498
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:511
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:520
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:540
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:566
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:571
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:661
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:685
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:713
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:727
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:271
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:277
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:375
- Evidence:
  - `Personal.GetContactImage` accepts an optional `contactId`; when supplied and parseable, it retrieves that CRM contact's `entityimage`, gender, and LINE picture URL with no B02 scope check.
  - `Personal.GetContactImagesBatch` accepts arbitrary request contact ids, retrieves all uncached ids with a CRM `ConditionOperator.In`, and returns image data or LINE URLs.
  - The equivalent `MemberInfo` avatar endpoints call `CanViewContact` or `CanViewContactsBatch`, proving a B02 policy exists but is not reused by `Personal`.
- Control/data/lifetime flow:
  - Query/body contact ids -> `PersonalController.ImageUpload` -> CRM contact image / `new_line_picture_url` -> JPEG, SVG, or redirect/URL response.
- Impact:
  - Profile photos and external LINE profile image URLs can be enumerated by contact id by any authenticated caller able to invoke `Personal` image endpoints.
- Why this is necessary:
  - B02 owns member profile PII. Photo and LINE profile URL leakage is lower impact than data mutation but still object-level disclosure.
- Recommended action:
  - Reuse the shared B02 contact scope guard before cache lookup and CRM lookup in single and batch Personal avatar endpoints.
  - Do not use or populate cross-user cache entries until authorization for the current caller is established.
- Validation:
  - Tests for current user image, in-scope group image, and out-of-scope contact image/batch id.
- Rollback boundary:
  - Controller guard and cache-order change only.
- Extraction contract:
  - Shared avatar service accepts authorized contact ids only.
- CCG round history:
  - Round 1: run `20260711-130607-b02-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B02-PERF-001 Maintain-profile save starts untracked background CRM writes

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 75
- Confirmed: true
- Evidence confidence: 17
- Impact score: 17
- Likelihood/frequency score: 12
- Security urgency score: 5
- Performance gain score: 10
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B02
- Cross-module: F03A CRM operations; X02B observability if background job status is surfaced.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:909
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:971
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:983
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1004
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1079
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1128
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1139
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1140
  - SpeechMessageProducts.ChurchReport/Views/Personal/MaintainPersonInfomationView.cshtml:143
- Evidence:
  - The controller comments explicitly describe fire-and-forget upload.
  - The action launches `_ = Task.Run(...)`, loops over every submitted member, performs CRM retrieve/update calls, and immediately returns success while background work is still running.
  - The view sets a 3000 ms AJAX timeout, which encourages returning before CRM completion rather than reporting actual commit status.
- Control/data/lifetime flow:
  - Browser payload -> request thread validates/deserializes -> untracked `Task.Run` -> shared `ToolUtility` CRM calls after response.
- Impact:
  - Concurrent saves can enqueue unbounded thread-pool work, hide partial failures from the user, and run CRM operations outside the request cancellation/lifetime.
- Why this is necessary:
  - Maintain-profile saves are a high-volume B02 workflow. Hidden background failures make operational diagnosis and rollback difficult.
- Recommended action:
  - Replace fire-and-forget with awaited bounded batch processing, a durable queue with status, or a server-side job component with cancellation, throttling, and observable result.
  - Return accepted/job id only if a real queue owns the work.
- Validation:
  - Load test with large maintain grids and concurrent users; assert request result matches actual CRM update status.
- Rollback boundary:
  - Controller save path only; no CRM schema change.
- Extraction contract:
  - Contact update batch command: validated contact updates in, per-contact result out.
- CCG round history:
  - Round 1: run `20260711-130607-b02-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B02-PERF-002 OptionSet metadata lookups recreate MemoryCache instances and defeat the existing 24-hour metadata cache

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 69
- Confirmed: true
- Evidence confidence: 18
- Impact score: 15
- Likelihood/frequency score: 11
- Security urgency score: 0
- Performance gain score: 10
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: S
- Primary owner: B02
- Cross-module: F03A CRM metadata; X02A shared cache if centralized.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs:36
  - SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs:48
  - SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs:67
  - SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs:76
  - SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs:106
  - SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs:109
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:580
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:587
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:590
  - SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs:299
  - SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs:302
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs:583
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs:586
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/PersonalInfomatioManager.cs:311
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/PersonalInfomatioManager.cs:314
- Evidence:
  - `OptionSetMetadataService` is designed to cache metadata for 24 hours and calls CRM metadata retrieval on cache miss.
  - `PersonalController.GetOptionSetMetadataService` already uses injected `IMemoryCache` or a static fallback cache.
  - `ContactService`, `NewPerson`, and `PersonalInfomatioManager` create `new MemoryCache(new MemoryCacheOptions())` at each conversion/attribute mapping site, so repeated option conversions do not share the intended metadata cache.
- Control/data/lifetime flow:
  - Contact/onboarding field mapping -> fresh metadata service/cache -> `RetrieveAttributeRequest` on miss -> discard local cache after method.
- Impact:
  - Repeated onboarding/profile saves can re-query CRM metadata for the same option sets and allocate unnecessary cache instances.
- Why this is necessary:
  - This is a low-risk acceleration fix with a proven local pattern and high reuse across B02 onboarding/profile code.
- Recommended action:
  - Inject or pass a shared `OptionSetMetadataService`/`IMemoryCache` into B02 contact and connector paths.
  - Centralize contact option mapping helper for `familystatuscode`, `new_spiriitual_identity`, `customertypecode`, and display text conversions.
- Validation:
  - Unit tests for mapping behavior plus instrumentation proving a repeated mapping sequence causes one metadata retrieval per option set within cache duration.
- Rollback boundary:
  - Service construction/helper extraction only.
- Extraction contract:
  - Contact option mapping service: logical entity/attribute/text or value in, option value/text out.
- CCG round history:
  - Round 1: run `20260711-130607-b02-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### B02-EXT-001 Avatar and contact update policies are duplicated across controllers, causing guard drift

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 71
- Confirmed: true
- Evidence confidence: 17
- Impact score: 14
- Likelihood/frequency score: 11
- Security urgency score: 8
- Performance gain score: 6
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: B02
- Cross-module: X03 shared UI consumes avatar/update endpoints; B01 supplies identity.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:271
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:344
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:747
  - SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:820
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:498
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:661
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:914
  - SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1164
- Evidence:
  - `MemberInfo` and `Personal` each implement contact image retrieval/batch retrieval with overlapping cache keys, thumbnail creation, base64 conversion, line-picture fallback, and CRM image reads.
  - `MemberInfo` implements scoped update/image guards; `Personal` implements similar contact update/image workflows without the same guard.
- Control/data/lifetime flow:
  - Shared profile UI actions are split across controller-specific implementations rather than a single B02 application service.
- Impact:
  - Security fixes must be applied in multiple places and can regress independently, as shown by the current guard mismatch.
- Why this is necessary:
  - Extraction directly prevents repeat security drift while also simplifying future B02 acceleration work.
- Recommended action:
  - Extract a B02 contact profile service with explicit operations: authorize contact ids, fetch avatars, update contact fields, upload image, and invalidate cache.
  - Controllers should orchestrate HTTP only; service owns policy and CRM/cache behavior.
- Validation:
  - Shared service tests cover `MemberInfo` and `Personal` callers with the same allowed/denied cases.
- Rollback boundary:
  - Internal B02 service extraction; preserve routes and view contracts.
- Extraction contract:
  - Inputs: user context, contact ids, update/image command.
  - Outputs: allowed results, denied results, cache invalidation result.
- CCG round history:
  - Round 1: run `20260711-130607-b02-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

## Runtime Validation Pending

No confirmed security issue requires runtime validation to prove the code path exists. Performance gains should still be measured before optimization work:

- B02-PERF-001: measure request duration, CRM update completion time, thread-pool pressure, and failure visibility before/after replacing fire-and-forget.
- B02-PERF-002: count CRM metadata `RetrieveAttributeRequest` calls for repeated onboarding/profile saves before/after shared metadata cache injection.

## Deleted Or Rejected Candidates

- `MemberInfo.GetContactImage` arbitrary id read: rejected because it checks `CanViewContact` before CRM retrieval.
- `MemberInfo.GetContactImagesBatch` arbitrary id batch read: rejected because it parses ids then applies `CanViewContactsBatch` before cache/CRM reads.
- Upload image decompression/large file issue: rejected as a confirmed issue because `MemberInfo`, `Personal`, and `NewPerson` upload paths enforce a 5 MB input limit and image MIME/extension checks. Image bomb hardening may still be tested later.
- `MemberInfoController` use of `HttpClient` inside `using`: rejected as a performance issue for this pass because it is bounded to the LINE profile resync flow and not enough evidence shows high frequency.
- `MemberInfo.CanViewContact` appearing to call `IsCurrentContact`: rejected as an over-restriction bug because `IsCurrentContact` means active/non-closed CRM contact, not "currently logged-in user".
- Debug logging in B02 controllers: rejected as a confirmed PII leak because the observed paths use `Debug.WriteLine` and existing comments say timing output does not write production trace logs.

## Cross-Module Handoffs

- B01: auth/session identity is prerequisite, but B02 must add object-level authorization and anti-forgery around contact/profile operations.
- F03A: CRM retrieve/update and metadata calls are the downstream side effects; fixes should preserve existing CRM contracts.
- B03/B04A-B04C/B05/B07: these modules consume member/contact identity and profile data; they should be included in consumer validation after B02 mutation guards are fixed.
- X01: global anti-forgery rollout may be the preferred implementation path, with B02 endpoints as required validation cases.
- X02A/X02B: shared cache and observability are useful for OptionSet caching and background job replacement if the optimization phase chooses a platform service.
- X03: B02 views and shared popup host consume these endpoints; route contracts should stay stable during extraction.

## Final CCG Approval

Final CCG disposition: DEGRADED_REVIEW_PENDING

CCG review was attempted through the required self-healing runner:

- Run path: `.ccg/dual-model-runs/20260711-130607-b02-issue-review-r1-reviewer/summary.json`
- Gemini: provider quota/billing blocked, no usable output.
- Claude: session limit blocked, no usable output.
- Completed backends: none.
- Degraded fallback: false.

Because no backend completed with usable output, this B02 report is not approved or approved-degraded. The retained issues remain locally evidence-backed and require a later external review retry when at least one backend can complete.
