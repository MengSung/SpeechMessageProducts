# F09 Runtime Validation Plan

Status: NOT_RUN_BY_ASSIGNMENT
Module: F09
Mode: DIAGNOSIS_ONLY

Runtime validation was not executed. The assignment forbids `dotnet restore`,
`dotnet build`, `dotnet test`, package restore, code generation, formatting,
migrations, benchmarks, generated files, test outputs, cache writes, lockfile
writes, and `bin/**` or `obj/**` writes.

## Future Validation For F09-SEC-001

When code changes are authorized, validate the idempotent post-payment workflow
with focused tests before any broader build/test gate:

1. F09 unit test: executing `PaymentPostPaymentWorkflow` twice with the same
   operation key runs `IPaymentRecordUpdater` and `IPaymentPayerNotifier` once.
2. F09 unit test: concurrent duplicate executions with the same operation key
   produce one completed result and one duplicate-skipped result.
3. F09 unit test: a failed handler records retryable failure and allows a later
   retry according to the chosen checkpoint contract.
4. F09 unit test: different statuses for the same order have explicit behavior
   documented by the contract, for example failure then success.
5. B05 consumer test: Taishin `post-back` and `result-url` for the same
   successful order do not append duplicate CRM description blocks.
6. B05 consumer test: duplicated MyPay notification for the same order does not
   send payer notification more than once.
7. X01 DI test or host smoke test: the idempotency contract resolves with the
   B05 implementation in the ChurchReport host.

Suggested future commands, only after authorization:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter Payment
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter Workflows
```

## Future Validation For Rejected Performance Candidate

If callback payload size or request-rate evidence later justifies optimizing
`PaymentHttpRequestMapper`, add measurement before changing behavior:

1. Capture representative callback payload sizes for Taishin, MyPay, and
   Sinopac.
2. Add a mapper unit test for query-only callbacks proving no raw body allocation
   is required if the design changes.
3. Add a malformed/oversized body test only if a product request-size policy is
   selected.

No such measurement was run in this diagnosis.
