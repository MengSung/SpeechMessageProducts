# P7.1 Package01 Data8 Read Final Review

Review only the post-review corrections in the current worktree. Do not suggest
or perform P6.2, Official Worker, feature-flag enablement, ChurchReport traffic
cutover, CE writes, P7.2, P8, deployment, commit, or push.

## Corrections requiring verification

1. `Invoke-Package01Data8ReadEvidence.ps1` snapshots all process variables it
   can override before repository or fixture validation, so every early exit
   restores rather than clears caller-owned variables.
2. Temporary directory deletion is non-throwing inside `finally`, so it cannot
   prevent credential clearing and environment restoration.
3. Fee and stor-lesson projection loops each enforce
   `OperationDefinition.MaximumPageBytes` before the cumulative response
   budget. The new offline regression injects an oversized but cumulative-safe
   page into each branch and requires a fail-closed exception plus one dispose.

## Required unchanged boundaries

- `Package01FeeReadsEnabled` stays `false`.
- No generic CRM CRUD, request-selected endpoint/profile/version/connector,
  FetchXML, secret, raw SDK response, CE mutation, or traffic cutover.
- The live evidence remains sanitized and is not rerun by this review.

## Verification already passed after correction

- PowerShell handoff test: 6 checks passed.
- `SpeechMessage.Dynamics.Tests` Release: 477 passed, 7 skipped.
- `ChurchReport.MemberInfo.Tests` Release: 395 passed, 2 skipped.
- `SpeechMessageProducts.sln` Release build: 0 warnings, 0 errors.
- Archived P7.0 validator: 7 tests passed; normal and `--build` validation have no errors.
- 14 P7.1-owned files passed UTF-8 no-BOM, CRLF-only, final-CRLF validation;
  `git diff --check` passed.

## Output

Return a concise Critical / Warning / Info review with concrete file and line.
Verify rather than assume. Security, state leakage, unbounded resources,
credential/data disclosure, CE mutation, or feature activation are Critical.
