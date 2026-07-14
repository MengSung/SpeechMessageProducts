# F08 Runtime Validation Plan

Status: APPROVED_DEGRADED

## Commands Not Run

The assignment explicitly forbids `dotnet restore`, `dotnet build`, `dotnet test`, package restore, code generation, formatting, migrations, benchmarks, coverage, or commands that write generated/ignored/cache/lock/test-output files. None of those commands were run.

## Static Validation Performed

- Source inventory with `rg --files SpeechMessage.Payments LinePayCSharp SpeechMessage.Payments.Tests`.
- Line-level evidence collection with `rg -n`.
- Current write baseline with `git status --short`.
- Manual read-only inspection of read-only ASP.NET/host consumers to confirm boundary placement.

## Future Validation After Code Changes Are Allowed

Suggested verification commands for later implementation work:

```powershell
dotnet test SpeechMessage.Payments.Tests/SpeechMessage.Payments.Tests.csproj --filter "FullyQualifiedName~Providers|FullyQualifiedName~Gateway|FullyQualifiedName~Diagnostics|FullyQualifiedName~Models"
```

Additional targeted tests to add before or with fixes:

- MyPay callback with syntactically valid but unverified success fields must not be treated as cryptographically verified.
- Duplicate form keys and invalid JSON bodies must return `PaymentErrorKind.CallbackInvalid` instead of throwing.
- Taishin valid callback replay should be detected by the new replay/idempotency seam.
- Callback amount/currency mismatch against expected context should fail closed before `Succeeded` is exposed.
- Sinopac transport should send per-request `X-KeyID` without serializing unrelated calls.
- LinePay legacy client cancellation-token overloads should cancel pending HTTP operations.
- Provider HTTP non-success response containing token/hash-like fields should not leak through `PaymentError.Message`.

## Manual Review Checklist For Later Fixes

- Verify every retained issue has a unit test or documented integration test plan.
- Verify provider acknowledgements are still returned where provider contracts require them.
- Verify no host CRM/session/route decisions move into F08.
- Verify diagnostics still preserve safe provider troubleshooting fields while masking secrets.
- Verify public model changes are backward-compatible or have a migration plan for F09/B05 consumers.
