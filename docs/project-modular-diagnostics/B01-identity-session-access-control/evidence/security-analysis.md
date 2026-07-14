# B01 Security Analysis

Mode: DIAGNOSIS_ONLY

## Confirmed Security Findings

### B01-SEC-003 Account login compares CRM password strings directly

Evidence:

- `AuthenticationController.Login.cs:73` calls `ValidateUserCredentials` during login.
- `AuthenticationController.Private.cs:52` queries CRM field `new_app_pass`.
- `AuthenticationController.Private.cs:71-73` reads `new_app_pass` as a string.
- `AuthenticationController.Private.cs:78-79` compares that string directly with `viewModel.Password`.
- `AuthenticationController.Private.cs:252-253` writes the submitted account/password compatibility values to session.
- Search found no B01 password hash verification helper under `Security/**`, `Models/Authentication/**`, `Services/Authentication/**`, or `ChurchReport.MemberInfo.Tests/Security/**`.

Security impact:

- The visible B01 contract is plaintext-equivalent password verification.
- CRM/contact data exposure would expose reusable password material if the stored field is plaintext or reversible.
- Raw submitted passwords remain in session compatibility state, which should not be a post-login security authority.

### B01-SEC-001 Authorization gate disabled and session fallback authoritative

Evidence:

- `Startup.cs:389` registers `GlobalAuthorizationFilter` globally.
- `appsettings.json:70-71` sets `Security:EnforceGlobalAuthorization=false` and `Security:AllowSessionIdentityFallback=true`.
- `GlobalAuthorizationFilter.cs:25-35` returns before enforcing authorization when the flag is false and allows session fallback when enabled.
- `GlobalAuthorizationFilter.cs:65-70` treats `_SessionUserId` or `_LoginPassword` as sufficient server session identity.
- `AuthenticationController.Private.cs:217-226` writes `_SessionUserId` and session metadata.
- `AuthenticationController.Private.cs:252-253` writes `_LoginAccount` and `_LoginPassword` compatibility values.
- `AuthenticationController.Private.cs:242-249` names `FeeManagementController.CurrentLogin()` and `BaseChurchController.EnsureCorrectUserData()` as compatibility consumers.
- `BaseChurchController.cs:661-672` uses `_LoginPassword` to scope/cache user data.
- `FeeManagementController.cs:443-445` reads `_LoginAccount` and `_LoginPassword` through `CurrentLogin()`.

Security impact:

- The project has a server-issued `.ChurchReport.Auth` ticket path, but the configured gate is off.
- The fallback path makes session values an authorization authority when the project guideline says they should be compatibility cache only.
- B02-B07 cannot safely assume global authentication is enforced until both config flags and filter defaults are fixed.

### B01-SEC-004 Login reset claims to rotate the session id, but clear/commit does not do that

Evidence:

- `AuthenticationController.Private.cs:171-199` clears session and calls `CommitAsync` while comments/debug output claim the session id is regenerated.
- `AuthenticationController.Private.cs:217-226` writes identity cache values after the supposed rotation.
- `BaseChurchController.cs:973-998` contains the same clear/commit pattern but states that ASP.NET Core does not rotate the session id here and identity is bound to the auth ticket.

Security impact:

- The login flow records a stronger session-fixation guarantee than ASP.NET Core actually provides.
- Because B01-SEC-001 leaves session fallback enabled, the stale/fixed session id remains security relevant.
- Future B01 optimization could preserve a false invariant unless the contract is corrected.

### B01-SEC-002 LINE user id leaks through post-OAuth redirect URLs

Evidence:

- `AuthenticationController.LineLoginOAuth.cs:64-69` stores `_OAuthReturnUrl` when it is `_BINDING_` or classified local.
- `LocalReturnUrl.cs:12-17` only checks local URL shape; it does not allowlist destination routes.
- `AuthenticationController.LineLoginOAuth.cs:517-520` preserves that return URL across session clearing.
- `AuthenticationController.LineLoginOAuth.cs:528-531` processes login and redirects to `$"{returnUrl}/{lineUserId}"`.

Security impact:

- LINE user ids are identity material and become URL path data.
- Local routes, logs, browser history, analytics, screenshots, and same-origin referrers can receive the identifier.
- The handoff bypasses the server-issued auth ticket/claims contract B01 should own.

## Rejected Or Lower-Confidence Security Candidates

- Open redirect: rejected because `LocalReturnUrl.IsLocal` blocks protocol-relative and absolute URL forms, with tests in `LocalReturnUrlTests.cs:9-24`.
- Referer identity recovery: rejected because `RefererIdentityRemovedTests.cs:11-18` asserts the previous private method is absent.
- CSRF: not retained as a confirmed issue in this round. B01 POST actions do not show explicit antiforgery validation, but session/auth cookies are configured `SameSite=Lax` in `Startup.cs:571-578` and `Startup.cs:619-621`, reducing cross-site POST cookie attachment. Add antiforgery hardening later, but it is not stronger than B01-SEC-001 or B01-SEC-002 with current evidence.
- Token/PII logging: not retained as a separate issue. `AuthenticationController.LineLoginOAuth.cs:170` logs an access-token prefix and lines 179-180 log LINE profile data through `Debug.WriteLine`, but these calls are DEBUG conditional. Mask if DEBUG builds are deployed.

## Security Validation Plan

- Add password verifier tests for valid hash, invalid password, missing hash, legacy migration, and no raw password in session/claims/login response.
- Add filter tests proving session fallback cannot authorize without an authenticated cookie principal when fallback is false.
- Add config/startup smoke coverage proving global auth enforcement is enabled in target environments.
- Add login/session tests proving a pre-login session id cannot authorize without the auth ticket.
- Add OAuth redirect tests proving no `Location` header contains the raw LINE user id.
- Add destination allowlist tests for post-login return targets.
