# Final review: diagnostics operator, lifecycle, runtime, and WinRM evidence

Review the current uncommitted implementation and documentation increment in this repository. Inspect `git diff` and untracked source/task/spec files, but treat `.ccg/dual-model-runs/**` as generated evidence rather than product code.

## Required scope

- `SpeechMessageProducts.ChurchReport/Security/DiagnosticsOperatorAuthorization.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs`
- `SpeechMessageProducts.ChurchReport/appsettings.json`
- the three changed security/lifecycle test files
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md`
- `.ccg/tasks/dynamics-connection-compatibility/task.json`
- `.ccg/tasks/dynamics-connection-compatibility/review.md`

## Review questions

1. Can an unauthenticated, unlisted, duplicate-claim, malformed-claim, Session/query/header/product-JSON, or non-cookie identity bypass the diagnostics operator boundary?
2. Does the named diagnostics HTTP client have bounded ownership, timeout, connection, pooling, cookie, redirect, proxy, decompression, and cleanup behavior without per-request handler/socket retention?
3. Do the owned-handler disposal and real LINE callback replay tests exercise production lifecycle/read-and-remove behavior rather than tautological test-only logic?
4. Is there any credible Session Leakage, Profile Leakage, Credential Leakage, cross-tenant mutable-state leakage, Memory Leakage, socket/handler/timer/task/subscription leak, or sensitive diagnostic output?
5. Is the direct-DLL content-root guidance technically correct and fail closed? Does it avoid weakening configuration validation to hide an operator launch mistake?
6. Is the WinRM evidence truthful and safe? The targets answered DNS/TCP 5985/WSMan Identify, but no approved authenticated administrative identity/session existed. No remote mutation, password attempt, Basic, unencrypted transport, TrustedHosts broadening, certificate trust mutation, or browser interstitial bypass may be claimed or recommended.
7. Is the runtime evidence internally consistent: Gateway 200/200/401/200/403/403/controlled-400, ChurchReport browser complete with zero JavaScript errors, Diagnostics anonymous 302 redirect to login, Gateway browser proof limited by the self-signed development certificate, and cleanup returning listeners/PSSessions to zero?
8. Do the task/spec documents avoid claiming overall Phase 4, real CE 8.2/9.1, soak/performance, Phase 5, Phase 6, or authenticated WinRM completion?
9. Confirm `Package01FeeReadsEnabled=false` remains authoritative and Embedded, Data8, and `Microsoft.PowerPlatform.Dataverse.Client` remain retained.

## Local evidence

- Dynamics tests: 252 passed, 0 failed, 1 opt-in SQL skip.
- ChurchReport tests: 374 passed, 0 failed.
- Release solution build: 0 warnings, 0 errors.
- Debug Gateway and ChurchReport builds: 0 warnings, 0 errors.
- Scoped `dotnet format --verify-no-changes`: passed.
- Strict UTF-8 without BOM, CRLF-only, final CRLF: passed for all changed/untracked text files.
- `git diff --check`: passed.
- Added sensitive assignments, unredacted provider Session markers, local profile paths, absolute worktree paths, Windows SIDs, forbidden diagnostics patterns, listeners, owned PSSessions, and related reviewer processes: all zero.

Output a Critical / Warning / Info report. Any Critical finding must identify the exact file and executable failure path. Distinguish an implementation defect from an explicitly documented open external gate.
