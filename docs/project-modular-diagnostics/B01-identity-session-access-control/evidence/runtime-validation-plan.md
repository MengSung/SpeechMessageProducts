# B01 Runtime Validation Plan

Mode: DIAGNOSIS_ONLY
Runtime validation status: not executed in this diagnostic pass.

## B01-SEC-001

- Goal: prove the global authorization gate enforces cookie-ticket identity and does not authorize through session cache values.
- Setup: enable `Security:EnforceGlobalAuthorization=true` and `Security:AllowSessionIdentityFallback=false` in a controlled environment.
- Checks:
  - Anonymous secure action returns login redirect or 401 for AJAX.
  - Authenticated `.ChurchReport.Auth` principal passes.
  - Session containing `_SessionUserId` or `_LoginPassword` without authenticated principal fails.
  - `[AllowAnonymous]` actions still pass.
  - B02-B07 representative routes pass after login.

## B01-SEC-003

- Goal: prove account login uses a versioned password verifier and no raw password session authority remains.
- Setup: unit tests around a B01 password verification service with fake CRM credential records.
- Checks:
  - Valid salted hash authenticates.
  - Invalid password fails.
  - Empty/missing hash fails closed.
  - Legacy `new_app_pass` value can be migrated under an explicit one-time compatibility path.
  - `_LoginPassword`, auth claims, login response JSON, and logs do not contain the submitted password.

## B01-SEC-004

- Goal: prove login no longer depends on a false ASP.NET Core session-id rotation invariant.
- Setup: integration or component test around login with an existing pre-login session id.
- Checks:
  - Pre-login session values are cleared or versioned.
  - Post-login authorization requires `.ChurchReport.Auth`.
  - Session-only state cannot authorize without the auth ticket.
  - Any session-version or login-nonce value rejects old versions after login.

## B01-SEC-002

- Goal: prove OAuth post-login redirects do not expose raw LINE user ids.
- Setup: route-level test or integration harness for `LineLoginStart` -> callback/session state -> `ProcessLineUserLogin`.
- Checks:
  - Non-local return URLs are rejected.
  - Unsupported local return destinations are rejected by allowlist.
  - Successful redirect `Location` does not contain the LINE user id.
  - Consumer routes resolve identity from auth/session service, not route segment.

## B01-PERF-001

- Goal: quantify line-binding request-thread blocking and CRM call count before optimization.
- Setup: fake or instrumented CRM gateway around existing binding flows.
- Checks:
  - Existing contact path call count and duration.
  - Duplicate-name path call count and duration.
  - Create-contact path call count and duration.
  - CRM timeout/fault behavior.
  - Cancellation behavior after service extraction.

## Not Allowed In This Diagnostic Pass

- No `dotnet restore`, `dotnet build`, `dotnet test`, package restore, code generation, formatting, migrations, or commands that create `bin/**`, `obj/**`, caches, lockfiles, generated files, or test outputs.
