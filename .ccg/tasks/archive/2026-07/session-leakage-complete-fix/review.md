# Session Leakage Complete Fix Review

## CCG Review

- Runner: `docs/scripts/Start-CcgDualModelRun.ps1`
- Run folder: `.ccg/dual-model-runs/20260706-093520-session-leakage-complete-fix-review-reviewer/`
- Result: degraded fallback, not full dual-model success.
- Completed backend: Gemini.
- Blocked backend: Claude quota/session limit (`resets 12pm Asia/Taipei`).
- Critical findings: none reported by Gemini.
- Warning findings addressed:
  - Replaced auth-ticket sync-over-async (`SignInAsync().GetAwaiter().GetResult()`) with awaited async flow.
  - Kept rollout flags intentionally conservative: `Security:EnforceGlobalAuthorization=false`, `Security:AllowSessionIdentityFallback=true` pending anonymous whitelist/staging matrix.

## Implemented Security Remediation

- Login JSON response no longer returns `account`, `password`, or `new_app_pass`; response contract is centralized in `LoginResponseFactory`.
- Auth tickets are issued through `.ChurchReport.Auth` using `LoginClaimsFactory` for account and LINE login flows.
- LINE identity recovery no longer reads `Referer`; recovery uses authenticated ticket claims only.
- OAuth `returnUrl` is accepted only for `_BINDING_` or local URLs via `LocalReturnUrl`.
- Logout now clears session, signs out the cookie auth scheme, and deletes `.ChurchReport.Session` plus `.ChurchReport.Auth`.
- Global MVC authorization filter is registered behind rollout flags.
- Response headers now include `Referrer-Policy: no-referrer` and `X-Frame-Options: SAMEORIGIN`.

## Verification

- `dotnet build ChurchReport/ChurchReport.csproj -c Debug` passed with 0 warnings and 0 errors.
- `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Security"` passed: 24/24.
- `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj` passed: 231/231.
- Safety search found no `TryGetLineUserIdFromRequest`, no auth-ticket sync-over-async helper, and no login JSON `account/password` response pattern. Remaining `viewModel.Account`/`viewModel.Password` assignments are internal state assignments.
- Edited text files were normalized to UTF-8 without BOM and CRLF.

## Residual Risk / Follow-up

- `Security:EnforceGlobalAuthorization` remains `false` for canary safety until the anonymous endpoint whitelist and staging matrix are fully verified.
- `Security:AllowSessionIdentityFallback` remains `true` until every legacy login path is confirmed to issue auth tickets.
- Out-of-scope audit tracks remain: secret rotation/history purge, password hashing and login throttling, Personal photo IDOR, CSRF, exception-message generalization, HSTS/CSP, and trusted proxy handling.
