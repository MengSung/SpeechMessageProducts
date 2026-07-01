# Review Notes

## Scope Reviewed

- `DonationPaymentManager` was reduced from a mixed-responsibility class into a thinner coordinator for several payment-page responsibilities.
- ChurchReport-specific behavior was extracted into focused `ChurchReport.Services` classes:
  - `DonationCreditCardProfileService`
  - `DonationPaymentFormBuilder`
  - `DonationPaymentSubmissionService`
  - `DonationFeeQueryService`
  - `DonationBookingService`
  - `DonationContactService`
- `SpeechMessage.Payments.AspNetCore` comments were repaired to clearly describe the reusable ASP.NET Core host boundary without adding product dependencies.
- `DonationPaymentManager` now delegates ChurchReport CRM contact creation, matching, and missing-field update rules to `DonationContactService` while retaining legacy public entry points for controllers/views.
- A regression test asserts the contact section in `DonationPaymentManager` remains delegated to `DonationContactService` instead of reintroducing direct CRM contact mapping.

## Local Review Result

### Critical

- None found in local review.

### Warning

- External Gemini + Claude review could not be executed because `$HOME\.claude\bin\codeagent-wrapper` is not available in this environment.
- `DonationPaymentManager` still contains the high-level contact login decision flow and dedication booking CRM update flow. The current change deliberately avoided moving those workflow branches in the same pass to reduce payment-flow regression risk.

### Info

- Boundary searches found no `ChurchReport`, `ToolUtility`, `Line.Messaging`, `Microsoft.Xrm`, MVC, or persistence dependency in `SpeechMessage.Payments`.
- Boundary searches found no ChurchReport product model dependency in `SpeechMessage.Payments.AspNetCore`.
- `git diff --check` passes.
- Modified/new text files were checked as UTF-8 without BOM and CRLF line endings.

## Verification Commands

```powershell
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments" --no-restore -v minimal -p:UseSharedCompilation=false
dotnet build .\ChurchReport.sln --no-restore -v minimal -p:UseSharedCompilation=false
git diff --check
```

## Verification Result

- `SpeechMessage.Payments.Tests`: 55 passed.
- `ChurchReport.MemberInfo.Tests` payment subset: 104 passed.
- `ChurchReport.sln` build: succeeded with 0 warnings and 0 errors.
- `git diff --check`: passed.
- Encoding check: touched source/task files report `Bom=False` and `LfOnly=0`.

