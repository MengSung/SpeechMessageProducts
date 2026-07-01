# Review Notes

## Scope Reviewed

- `DonationPaymentManager` was reduced into a thinner coordinator while preserving its existing public controller/view entry points.
- ChurchReport-specific CRM, LINE, donation, booking, and form-assembly behavior stays in `ChurchReport.Services`; no ChurchReport product workflow was moved into `SpeechMessage.Payments` or `SpeechMessage.Payments.AspNetCore`.
- New or expanded ChurchReport services:
  - `DonationKeyInDedicationService`
  - `DonationBookingService`
  - `DonationContactCreationService`
  - `DonationPaymentModelAssembler`
  - `DonationLoginContactService`
  - `DonationDedicationFeeFormService`
- `DonationPaymentManager.cs` is now about 590 lines after this pass.
- Structure regression tests now assert that the manager delegates:
  - manual/key-in donation query and update workflow
  - booking list and cancellation workflow
  - contact creation and donation-numbering workflow
  - payment model assembly
  - donation login contact workflow
  - dedication fee form refresh workflow

## Local Review Result

### Critical

- None found in local review.

### Warning

- External Gemini + Claude review could not be executed because `$HOME\.claude\bin\codeagent-wrapper` is not available in this environment.
- `SpeechMessage.Payments.AspNetCore` boundary search still finds `PaymentHttpRequestMapper` registration. This is expected for the reusable ASP.NET Core host adapter layer and is not a ChurchReport/CRM/LINE dependency leak.

### Info

- Boundary searches found no ChurchReport product dependency in `SpeechMessage.Payments`.
- New ChurchReport services intentionally depend on CRM/ToolUtility/LINE-related product types because they are ChurchReport-specific workflow adapters, not reusable payment-core code.
- `git diff --check` passes.
- Modified/new text files were checked as UTF-8 without BOM and CRLF line endings.

## Verification Commands

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments" --no-restore -v minimal -p:UseSharedCompilation=false
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet build .\ChurchReport.sln --no-restore -v minimal -p:UseSharedCompilation=false
dotnet build .\ChurchReport.sln --no-restore -v minimal -p:UseSharedCompilation=false -p:OutDir="<temp>\churchreport-build-...\"
git diff --check
```

## Verification Result

- `ChurchReport.MemberInfo.Tests` payment subset: 110 passed, 0 failed.
- `SpeechMessage.Payments.Tests`: 55 passed, 0 failed.
- `ChurchReport.sln` normal build: blocked by a local file lock from Microsoft Visual Studio and IIS Express holding `ChurchReport\bin\Debug\net10.0\ChurchReport.dll`.
- `ChurchReport.sln` temp `OutDir` build: succeeded with 1 existing analyzer warning (`xUnit1012` in `MemberInfoScopeGuardTests.cs`) and 0 errors.
- `git diff --check`: passed.
- Encoding check: touched source/plan/review files report `Bom=False` and `LfOnly=False`.
