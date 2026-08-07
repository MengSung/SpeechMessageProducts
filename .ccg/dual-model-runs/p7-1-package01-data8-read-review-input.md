# P7.1 Package01 Data8 Read Review

Review only the P7.1-owned changes below. Do not suggest P6.2, Official Worker,
feature-flag enablement, ChurchReport traffic cutover, CE writes, P7.2, P8,
deployment, commits, or pushes.

## Required behavior

- ChurchReport continues to use `Package01FeeReadsEnabled=false`.
- The six Package01 read capabilities are fixed, typed, allowlisted Data8
  operations for `sunnyvalechback` CE 9.1 `Embedded + Data8` evidence only.
- No request may choose endpoint, CE version, profile, ConnectorKind, FetchXML,
  generic CRUD, credential, or raw SDK response.
- The PowerShell handoff reads a fixed local Windows Generic Credential only
  after local input validation; the secret is injected only into the bounded
  child test process, restored in `finally`, and never printed or persisted.
- The handoff must fail closed before `dotnet` or CE work when local input or
  credential lookup fails, and it must emit only sanitized evidence.
- The live evidence result has already confirmed six successful read operations;
  do not request fixture identifiers, endpoint details, account names, secrets,
  tokens, cookies, payloads, or raw exceptions.

## Files in scope

- `SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs`
- `ChurchReport.MemberInfo.Tests/LivePackage01Data8ReadEvidenceTests.cs`
- `docs/scripts/Invoke-Package01Data8ReadEvidence.ps1`
- `docs/scripts/Invoke-Package01Data8ReadEvidence.Tests.ps1`
- `.trellis/tasks/08-07-p7-1-package01-data8-read/*`
- P7.1 section only in `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`

## Evidence already passed

- PowerShell handoff test: 4 checks passed.
- `SpeechMessage.Dynamics.Tests` Release: 475 passed, 7 skipped.
- `ChurchReport.MemberInfo.Tests` Release: 395 passed, 2 opt-in live tests skipped.
- `SpeechMessageProducts.sln` Release build: 0 warnings, 0 errors.
- Archived P7.0 validator tests and normal/`--build` validation passed.
- Byte-level UTF-8 no-BOM, CRLF-only, final-CRLF scan and `git diff --check` passed.

## Output

Return only a concise `Critical` / `Warning` / `Info` review. Verify each
finding against the actual code and state the concrete file and line. Treat
leaked secrets, raw CRM data, unbounded resource lifetime, cross-profile state,
unvalidated input, unexpected CE mutation, or feature-flag activation as
Critical.
