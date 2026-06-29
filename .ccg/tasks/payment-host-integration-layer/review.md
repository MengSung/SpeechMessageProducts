# Payment Host Integration Layer Review

## Local Verification

- `dotnet restore ChurchReport.sln -v minimal`: passed.
- `dotnet build SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj --no-restore -m:1 -v minimal -p:BaseOutputPath=.\artifacts\payment-host-aspnetcore-build\ -p:UseSharedCompilation=false`: passed with 0 warnings and 0 errors.
- `dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false`: passed, 53 tests.
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~Payments" -p:BaseOutputPath=.\artifacts\payment-host-member-test-fresh\ -p:UseSharedCompilation=false`: passed, 39 tests.
- `dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:BaseOutputPath=.\artifacts\payment-host-solution-build-fresh\ -p:UseSharedCompilation=false`: passed with 0 errors and one existing `xUnit1012` warning in `ChurchReport.MemberInfo.Tests\MemberInfoScopeGuardTests.cs`.
- `git diff -- LinePayCSharp`: no diff.
- `git diff --check`: no whitespace errors; only existing CRLF normalization warnings.

## Boundary Review

- `SpeechMessage.Payments.AspNetCore` contains ASP.NET Core host glue only:
  - `PaymentHttpRequestMapper`
  - `PaymentAcknowledgementResultMapper`
  - `AddSpeechMessagePaymentAspNetCore()`
- Boundary search found no dependencies from `SpeechMessage.Payments.AspNetCore` to ChurchReport, CRM, LINE, Dataverse, ToolUtility, or QPay product workflow classes.
- ChurchReport search found no remaining `class PaymentHttpRequestMapper` or `class PaymentAcknowledgementResultMapper` definitions after moving those classes into the reusable ASP.NET project.
- `SpeechMessage.Payments` provider core was not expanded with ASP.NET concerns.
- `LinePayCSharp` was not modified.

## External Review Status

CCG requires dual external model review for this L+ high-risk backend/payment task. The required wrapper is unavailable in this environment:

```powershell
Test-Path "$HOME\.claude\bin\codeagent-wrapper"
# False
```

Therefore Gemini/Claude external review has not been executed. This task should remain open until the wrapper is restored or the user approves an alternate review path.
