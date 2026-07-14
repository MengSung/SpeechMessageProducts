# B01 Identity Session Access Control Diagnostic Issues

Status: APPROVED_DEGRADED
Module: B01
Workspace: B01-identity-session-access-control
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 86d9b42618404e9081c184dee72dc8835f045366e791c1fa2b85cdfdb8077bb3

Pre-CCG issue SHA-256 round 1: `AAA28683B2F765F5EEF12731F3430D7C3BB3B34A8B42C649DE4E297164EBE9D0`

## Executive Summary

B01 has four security issues worth fixing before optimization. The strongest issue is account login comparing a CRM `new_app_pass` string directly with the submitted password, with no password-hash verification seam in B01. The global authorization gate is also registered but disabled in configuration while session-only identity fallback remains enabled. Login session reset code claims to regenerate the ASP.NET Core session id even though adjacent B01 code states that clear/commit does not rotate the session id. Finally, the LINE OAuth return-url flow appends raw LINE user ids into local redirect URLs. A lower-priority performance/extraction issue remains in line binding: the controller hides synchronous CRM operations behind Task-returning helpers.

## Ranked Confirmed Issues

### B01-SEC-003 Account login compares CRM password strings directly

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 84
- Confirmed: true
- Evidence confidence: 20
- Impact score: 25
- Likelihood/frequency score: 14
- Security urgency score: 15
- Performance gain score: 0
- Loop leverage score: 7
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B01
- Cross-module: F03A stores/retrieves the CRM contact field; B01 owns verification policy and login flow.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Login.cs:73
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:52
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:71
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:78
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:252
- Evidence:
  - `ProcessLogin` calls `ValidateUserCredentials`.
  - `ValidateUserCredentials` queries CRM contact columns `contactid` and `new_app_pass`.
  - The method reads `new_app_pass` as a string and rejects only when it is empty or not equal to `viewModel.Password`.
  - No B01 password hashing or verification helper was found under `Security/**`, `Models/Authentication/**`, `Services/Authentication/**`, or `ChurchReport.MemberInfo.Tests/Security/**`.
  - The same submitted password is written to `_LoginPassword` as a compatibility cache value.
- Control/data/lifetime flow:
  - Login POST -> `ProcessLogin` -> `ValidateUserCredentials` -> CRM `new_app_pass` string -> direct string equality against submitted password -> session compatibility cache and auth ticket issuance.
- Impact:
  - If `new_app_pass` stores plaintext or reversible password material, CRM/contact data exposure compromises user passwords immediately.
  - There is no visible salt, work factor, algorithm migration, or hash-version contract in B01.
  - Raw submitted passwords also remain in session compatibility state, expanding the blast radius of session leakage or diagnostics.
- Why this is necessary:
  - Password verification is a core B01 responsibility. Modular extraction should not preserve plaintext-equivalent verification as the module contract.
- Recommended action:
  - Introduce a B01 password verification service that verifies a salted adaptive hash such as PBKDF2, bcrypt, or Argon2.
  - Add a migration strategy for existing `new_app_pass` values: legacy verify once, then rehash and store only the new hash/version.
  - Stop storing raw account passwords in `_LoginPassword`; replace compatibility consumers with a server-side identity/account key.
  - Ensure login responses, claims, logs, and diagnostics never include raw password material.
- Validation:
  - Tests for valid hash, invalid password, empty hash, legacy migration, and no raw password in session/claims/login response.
  - Security search proving B01 no longer compares password strings directly or stores submitted passwords.
- Rollback boundary:
  - Keep the login route contract stable. Roll back only the password verifier and migration adapter if needed.
- Extraction contract:
  - Input: account id and submitted password.
  - Output: authenticated contact id or failure reason.
  - Dependency seam: CRM credential record reader plus password verifier.
- CCG round history:
  - Round 1: Claude Critical finding added as retained issue; Gemini quota blocked.

### B01-SEC-001 Global authorization is disabled and session fallback remains an identity authority

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 83
- Confirmed: true
- Evidence confidence: 19
- Impact score: 23
- Likelihood/frequency score: 13
- Security urgency score: 15
- Performance gain score: 1
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: S
- Primary owner: B01
- Cross-module: X01 host registration and X04A runtime configuration are consumers/dependencies; B02-B07 consume the auth/session result.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Startup.cs:389
  - SpeechMessageProducts.ChurchReport/appsettings.json:70
  - SpeechMessageProducts.ChurchReport/appsettings.json:71
  - SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs:25
  - SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs:31
  - SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs:65
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:217
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:242
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:252
  - SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:661
  - SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs:443
- Evidence:
  - MVC globally registers `GlobalAuthorizationFilter`, so this is the intended B01 authorization gate.
  - Runtime configuration sets `Security:EnforceGlobalAuthorization` to `false` and `Security:AllowSessionIdentityFallback` to `true`.
  - The filter returns immediately when enforcement is false.
  - When enforcement is later enabled, the filter still treats `_SessionUserId` or `_LoginPassword` as sufficient identity even if the `.ChurchReport.Auth` cookie principal is not authenticated.
  - Login setup writes `_SessionUserId`, `_LoginAccount`, and `_LoginPassword` into session as compatibility cache values.
  - Local comments identify `FeeManagementController.CurrentLogin()` and `BaseChurchController.EnsureCorrectUserData()` as compatibility consumers; those methods read `_LoginPassword` or `_LoginAccount/_LoginPassword` to scope cached user data.
- Control/data/lifetime flow:
  - Request -> MVC global filter -> config check -> either no authorization result or session fallback -> controller action.
  - Login flow separately issues the auth ticket, but the filter does not require that ticket when session fallback is enabled.
- Impact:
  - The deployed default gate state is "not enforced".
  - The fallback path preserves session values as a security authority, contradicting the project rule that the server-issued auth cookie ticket is the authority once login exists.
  - Session-only access can outlive or diverge from the auth ticket, especially around cookie expiry, partial logout, stale sessions, or future controller additions that assume global auth is active.
- Why this is necessary:
  - B01 owns the authentication/session contract for all authenticated business modules. Leaving the gate disabled or session-authoritative makes downstream module optimization unsafe because route-level assumptions are unclear.
- Recommended action:
  - Flip `Security:EnforceGlobalAuthorization` to `true` in a staged environment first, then production after B02-B07 route smoke coverage passes.
  - Flip `Security:AllowSessionIdentityFallback` to `false` only after compatibility consumers of `_LoginAccount` and `_LoginPassword` are replaced or explicitly adapted.
  - Replace `FeeManagementController.CurrentLogin()` and `BaseChurchController.EnsureCorrectUserData()` dependencies on raw `_LoginPassword` with an auth-ticket contact/account key or a server-side scoped identity service.
  - Change the code default for `AllowSessionIdentityFallback` to `false` so missing config fails closed.
  - Keep session values as compatibility cache only; do not let them authorize actions without an authenticated cookie principal.
- Validation:
  - Add/extend B01 security tests: unauthenticated secure action redirects/401s; authenticated cookie principal is allowed; `_SessionUserId` or `_LoginPassword` without authenticated cookie is rejected when fallback is false; anonymous endpoints still pass.
  - Run route smoke checks for B02-B07 after the gate flip.
- Rollback boundary:
  - Runtime rollback is limited to the two `Security:*` settings; code rollback is limited to `GlobalAuthorizationFilter`.
  - Consumer rollback must restore the prior compatibility adapter, not reopen session-only authorization.
- Extraction contract:
  - Input: `HttpContext.User`, anonymous endpoint metadata, rollout config.
  - Output: allow, 401, or login redirect.
  - Test seam: `GlobalAuthorizationFilterTests`.
  - Consumers: all MVC controllers in B02-B07 and X01 startup composition.
- CCG round history:
  - Round 1: Claude `KEEP`; Gemini quota blocked.

### B01-SEC-004 Login reset claims to rotate the session id, but clear/commit does not do that

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 18
- Impact score: 22
- Likelihood/frequency score: 12
- Security urgency score: 14
- Performance gain score: 0
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B01
- Cross-module: X01 session middleware configuration is a dependency.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:171
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:182
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:191
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:199
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:217
  - SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:973
  - SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:986
  - SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:998
- Evidence:
  - `InitializeUserSessionAsync` clears the current session and calls `CommitAsync`, with comments and debug output saying this regenerates the session id.
  - `BaseChurchController.RegenerateSessionId` uses the same clear/commit pattern but logs that ASP.NET Core does not rotate the session id here and that identity is bound to the auth ticket.
  - After the supposed rotation, B01 writes `_SessionUserId` and other identity cache values into session.
- Control/data/lifetime flow:
  - Existing session cookie -> login -> `Session.Clear()` -> `Session.CommitAsync()` -> same ASP.NET Core session cookie can continue -> B01 writes new identity cache values into that session -> auth ticket issued separately.
- Impact:
  - B01's session-fixation mitigation is documented as stronger than it is.
  - While auth-ticket authority mitigates the risk, B01-SEC-001 keeps session fallback enabled, making stale or fixed session ids security relevant.
  - Future maintainers may rely on the false "session id regenerated" invariant during optimization.
- Why this is necessary:
  - B01 cannot safely extract or optimize session handling while its core login reset contract contains a false security guarantee.
- Recommended action:
  - Treat the auth ticket as the only post-login authority and remove session-only authorization fallback.
  - Replace "session id regenerated" comments/assumptions with the actual ASP.NET Core behavior.
  - If session rotation is required, implement an explicit server-side login nonce or session-version store and reject old versions after login.
  - Delete or expire legacy session cookies as part of logout and login transition where compatible, but do not claim `CommitAsync` rotates the id.
- Validation:
  - Add tests or integration probes showing a pre-login session id cannot authorize after login unless the auth ticket is valid.
  - Add a regression test for any session-version or login-nonce mechanism introduced.
- Rollback boundary:
  - Roll back only the session-version/login-nonce mechanism and comments; keep auth-ticket authority intact.
- Extraction contract:
  - Input: current session id, login event, auth ticket issue result.
  - Output: post-login identity authority from cookie ticket plus optional non-authoritative session cache.
- CCG round history:
  - Round 1: Claude Critical finding added as retained issue; Gemini quota blocked.

### B01-SEC-002 OAuth return-url flow appends raw LINE user id into local redirect URLs

- Category: Security
- Severity: Medium
- Priority: P1
- Priority score: 70
- Confirmed: true
- Evidence confidence: 18
- Impact score: 19
- Likelihood/frequency score: 12
- Security urgency score: 12
- Performance gain score: 0
- Loop leverage score: 6
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B01
- Cross-module: Consumers that currently expect `{returnUrl}/{lineUserId}` must move to the B01 auth/session contract.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:64
  - SpeechMessageProducts.ChurchReport/Security/LocalReturnUrl.cs:12
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:517
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:528
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:531
- Evidence:
  - `LineLoginStart` stores any `_BINDING_` value or any URL that `LocalReturnUrl.IsLocal` classifies as local.
  - `LocalReturnUrl.IsLocal` prevents absolute/protocol-relative redirects, but it does not constrain which local route can receive a LINE identifier.
  - After OAuth callback and login processing, `ProcessLineUserLogin` redirects to `$"{returnUrl}/{lineUserId}"`.
- Control/data/lifetime flow:
  - Caller supplies local `returnUrl` -> B01 stores it in session -> LINE OAuth validates state -> B01 finds CRM contact -> B01 calls `ProcessLogin` -> B01 redirects browser to local path containing the raw LINE user id.
- Impact:
  - The LINE user id becomes part of the URL path. It can be retained in browser history, server logs, reverse-proxy logs, analytics, screenshots, referrers to same-origin assets, and downstream route parameters.
  - Any local route that is accepted by `LocalReturnUrl.IsLocal` can become a recipient for this identity parameter, so B01 does not own the identity handoff contract tightly enough.
- Why this is necessary:
  - The auth ticket already carries server-issued identity. Continuing to pass LINE IDs in URLs keeps downstream modules coupled to client-visible identity transport and blocks clean B01 extraction.
- Recommended action:
  - Replace `{returnUrl}/{lineUserId}` with a named, allowlisted post-login destination contract.
  - Let consumers read identity through the auth ticket/session service rather than a route segment.
  - If a legacy route still needs a transient key, use a short-lived opaque server-side nonce instead of the LINE user id.
- Validation:
  - Tests for `LineLoginStart` and `ProcessLineUserLogin` should assert that returned `Location` headers do not contain the LINE user id and that unsupported local return destinations are rejected.
  - Add consumer route smoke tests for the allowlisted destinations.
- Rollback boundary:
  - Roll back only the B01 return-url mapper and any explicitly listed consumer route adapters.
- Extraction contract:
  - Input: local post-login destination key.
  - Output: local redirect without raw identity material.
  - Dependency seam: `LocalReturnUrl` plus a new destination allowlist.
- CCG round history:
  - Round 1: Claude `KEEP`; Gemini quota blocked.

### B01-PERF-001 Line-binding controller hides synchronous CRM operations behind Task-returning helpers

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 57
- Confirmed: true
- Evidence confidence: 17
- Impact score: 13
- Likelihood/frequency score: 10
- Security urgency score: 0
- Performance gain score: 7
- Loop leverage score: 8
- Ease/reversibility score: 2
- Effort: M
- Primary owner: B01
- Cross-module: F03A/CRM operations provide the underlying CRM dependency.
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs:66
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs:81
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs:174
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs:246
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs:290
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs:330
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs:373
  - SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs:402
  - SpeechMessageProducts.ChurchReport/Middleware/SessionValidationMiddleware.cs:247
- Evidence:
  - `ProcessLineBinding` is async and awaits `CheckExistingLineBinding`, `FindMatchingContactByNameAndMobile`, update, create, and no-match handlers.
  - Those methods call `ExecuteCrmAsync`, but `ExecuteCrmAsync` invokes `operation()` synchronously and wraps the already completed result in `Task.FromResult` or `Task.CompletedTask`.
  - A single binding flow can perform multiple `RetrieveMultiple`, `Update`, and `Create` operations on the request thread.
  - A separate B01 mismatch path blocks on `Session.CommitAsync().GetAwaiter().GetResult()`, reinforcing that async/sync boundaries need cleanup.
- Control/data/lifetime flow:
  - HTTP POST -> async MVC action -> `await ExecuteCrmAsync(...)` -> synchronous CRM SDK call completes on the request thread -> completed task returned after the blocking work is already done.
- Impact:
  - The controller advertises an async boundary without making the CRM boundary measurable or cancellable.
  - Under slow CRM or concurrent LINE binding bursts, request threads can remain occupied.
  - The current code comment correctly avoids `Task.Run`; the issue is not that `Task.Run` is missing, but that the synchronous CRM execution model is hidden inside controller helpers.
- Why this is necessary:
  - B01 login/binding is on the entry path for authenticated workflows. A clean extraction should isolate CRM-bound identity operations behind an explicit, measurable service contract.
- Recommended action:
  - Extract a B01 line-binding application service that exposes the real execution model.
  - Do not paper over synchronous CRM calls with `Task.Run`.
  - If only synchronous CRM APIs are available, make the gateway synchronous, add timeout/cancellation policy where the CRM client supports it, and instrument call count/duration.
  - If an async CRM client is available through F03A, use it directly and propagate cancellation tokens.
- Validation:
  - Add unit tests around the extracted service decision paths.
  - Add request-level timing around line binding and record CRM call count and duration before/after.
- Rollback boundary:
  - Keep the controller route contract stable; roll back only the extracted line-binding service wiring.
- Extraction contract:
  - Input: `LineBindingViewModel`.
  - Output: binding result DTO independent of MVC `JsonResult`.
  - Dependencies: CRM query/update/create gateway, duplicate-name policy, placeholder-contact cleanup.
- CCG round history:
  - Round 1: Claude `REWRITE`; rewritten to make the sync CRM boundary explicit and to avoid recommending `Task.Run`; Gemini quota blocked.

## Runtime Validation Pending

No issue is classified as `RUNTIME_VALIDATION_PENDING`. Runtime checks are still recommended before optimization:

- B01-SEC-003: hash verification and legacy credential migration tests.
- B01-SEC-001: route/auth smoke tests with enforcement on and session fallback off.
- B01-SEC-004: login/session probe proving pre-login session ids cannot authorize without the auth ticket.
- B01-SEC-002: callback/redirect tests proving LINE user ids are absent from `Location` headers.
- B01-PERF-001: timing and CRM call-count measurements for line binding.

## Deleted Or Rejected Candidates

- Open redirect through `returnUrl`: rejected. `LocalReturnUrl.IsLocal` rejects `//evil`, `/\evil`, absolute `http(s)` URLs, and other non-local strings, and `LocalReturnUrlTests` covers these cases.
- Referer-derived identity: rejected. `RefererIdentityRemovedTests` asserts the previous `TryGetLineUserIdFromRequest` hook is absent.
- CSRF as a retained high-priority issue: rejected for this round. B01 state-changing POST actions lack explicit antiforgery validation, but the auth and session cookies are `SameSite=Lax`, and no evidence was found that cross-site POSTs receive those cookies in the intended browser path. Keep this as a hardening backlog item, not a confirmed high-priority issue.
- IdentityAuditMiddleware memory leak: rejected. It uses a static dictionary, but registration is DEBUG-only and `IdentityAuditCleanupService` removes old entries.
- Server-side debug logging of token/LINE profile snippets: rejected as a retained issue because `System.Diagnostics.Debug.WriteLine` is compiled out of Release builds. Keep masking as a hygiene improvement if DEBUG builds are ever deployed.
- `LoginResponse` credential exposure: rejected as current issue because the active login JSON path uses `LoginResponseFactory.Build`, and `LoginResponseFactoryTests` assert the serialized payload does not contain `password`, `account`, or `new_app_pass`.

## Cross-Module Handoffs

- X01: host startup composes the MVC global filter, cookie auth, session, and middleware order.
- X04A: runtime configuration owns the `Security:*` deployment values.
- F03A: CRM operations are the underlying dependency for credential storage/retrieval and line binding.
- B02-B07: authenticated business modules consume the B01 identity/session contract and need smoke coverage when the global gate is enabled.

## Final CCG Approval

APPROVED_DEGRADED.

- Round 1: Gemini quota/billing blocked with no usable output; Claude completed. Claude kept B01-SEC-001 and B01-SEC-002, requested B01-PERF-001 rewrite, and raised B01-SEC-003 and B01-SEC-004.
- Round 2: Gemini quota/billing blocked with no usable output; Claude completed. Claude kept all retained issues and accepted `APPROVED_DEGRADED` after review-log metadata, appsettings line reference, and B01-SEC-001 migration sequencing were updated.
- Degraded fallback is recorded because only Claude produced usable output. This is not full dual-model approval.
