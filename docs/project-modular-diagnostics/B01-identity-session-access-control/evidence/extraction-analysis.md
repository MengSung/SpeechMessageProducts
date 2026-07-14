# B01 Extraction Analysis

Mode: DIAGNOSIS_ONLY

## Cohesive B01 Seams

### Password Verification

- Current files: `AuthenticationController.Private.cs`, CRM contact field `new_app_pass`, B01 security tests.
- Contract: account plus submitted password should be verified through a versioned password verifier, not direct string comparison.
- Extraction opportunity: introduce a password verification service with legacy migration and no raw password session storage.
- Issue link: B01-SEC-003.

### Authorization Gate

- Current files: `GlobalAuthorizationFilter.cs`, `Startup.cs`, `appsettings.json`, B01 security tests.
- Contract: authenticated cookie principal plus anonymous endpoint metadata decide allow/deny.
- Extraction opportunity: make session fallback an explicit rollout switch with default false and tests around every allowed anonymous endpoint.
- Issue link: B01-SEC-001.

### OAuth Return Destination

- Current files: `AuthenticationController.LineLoginOAuth.cs`, `LocalReturnUrl.cs`, `LocalReturnUrlTests.cs`.
- Contract: a post-login destination should be local, allowlisted, and identity-free.
- Extraction opportunity: replace arbitrary local path plus LINE ID route segment with a destination key and server-side identity lookup.
- Issue link: B01-SEC-002.

### Line Binding Application Service

- Current files: `AuthenticationController.LineBinding.cs`, `AuthenticationController.SaveUserId.cs`, `PhoneBindingController.cs`, `LineBindingViewModel` consumers.
- Contract: input profile/binding form plus CRM dependency returns a binding result.
- Extraction opportunity: move CRM query/update/create decisions out of the MVC controller and expose a DTO result. This enables focused tests and measured CRM operations.
- Issue link: B01-PERF-001.

### Login Claims And Response Shaping

- Current files: `LoginClaimsFactory.cs`, `LoginResponsePayload.cs`, `AuthenticationController.Private.cs`.
- Existing positive seam: `LoginClaimsFactory` and `LoginResponseFactory` already centralize cookie claims and credential-free login JSON.
- Keep these as B01-owned helpers during extraction.

## Acceleration Plan For Later Optimization Loops

1. Lock B01 auth gate behavior with tests before moving files.
2. Introduce a password verifier seam and migrate legacy `new_app_pass` values.
3. Correct the login session-reset contract so it no longer claims session-id rotation from `CommitAsync`.
4. Introduce a small B01 destination allowlist for OAuth return flows.
5. Extract line-binding service behind interfaces for CRM operations and result mapping.
6. Use B02-B07 route smoke tests as consumer gates after enabling global auth.

## Rejected Extraction Candidates

- Moving LINE HTTP transport into B01: rejected. LINE SDK/workflow ownership belongs to F04-F06/B07.
- Moving Startup middleware order into B01: rejected. X01 owns host composition; B01 should provide registration contracts and tests, not own the host.
