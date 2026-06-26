# CCG Review - Payment Module Extraction

Date: 2026-06-26
Task: `payment-module-extraction`
Scope reviewed: Task 11 cleanup diff from the working tree at review time.

## Follow-up Review - Sinopac ATM Virtual Account

Date: 2026-06-26
Scope reviewed:

- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs`
- `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs`
- `SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs`
- `SpeechMessage.Payments.Tests/Diagnostics/PaymentDiagnosticsSanitizerTests.cs`
- `ChurchReport.MemberInfo.Tests/Payments/QPayCreatePaymentGatewayAdapterTests.cs`
- `.trellis/spec/backend/quality-guidelines.md`

External review status:

- Required CCG wrapper is not present in this environment (`Test-Path "$HOME\.claude\bin\codeagent-wrapper"` previously returned `False`), so Gemini/Claude review could not be rerun.

Manual review findings:

- Critical: none.
- Warning: none.
- Info:
  - Root cause: Sinopac ATM create response contained `ATMParam.AtmPayNo`, but `BuildCreateProviderData(...)` did not expose it as `ProviderData["atm_pay_no"]`. ChurchReport's adapter already read that key, so the rendered ATM account was blank.
- Secondary root cause: generic numeric-card masking would mask 13-19 digit ATM virtual account values unless `atm_pay_no` is explicitly treated as safe bank-payment instruction data.
- Additional guard: both the Sinopac core result mapper and ChurchReport legacy adapter now fail closed if ATM create-payment data lacks an ATM virtual account number, preventing blank account instructions from being rendered.
- Fix keeps provider protocol ownership in `SpeechMessage.Payments`; ChurchReport remains a thin adapter that maps provider-neutral data into the legacy `CreOrder` shape.

Verification:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore --filter "FullyQualifiedName~SinopacProviderTests|FullyQualifiedName~PaymentDiagnosticsSanitizerTests" -v minimal
dotnet build ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal
dotnet vstest ChurchReport.MemberInfo.Tests\bin\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll --TestCaseFilter:"FullyQualifiedName~QPayCreatePaymentGatewayAdapterTests"
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal
dotnet vstest ChurchReport.MemberInfo.Tests\bin\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll
dotnet build ChurchReport.sln --no-restore -v minimal
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
rg -n "QPay\.Domain|QryOrderPay|TSResultContent|QryOrder\b|OrderInfo\b|TSResult\b|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|MyPayReturnModel|MyPayProcessingResult|MyPayStatusHelper" ChurchReport --glob "*.cs"
rg -n "\bIPayment\b|IQPayToolkit|QPayToolkit|QPayToolkitWrapper|MyPayToolkit|MyPayToolkitWrapper|TspgToolkit|TspgToolkitWrapper|TSPGWebhookHandler|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|TSPGPaymentRequest|TSPGPaymentNotification|StoreKey|StoreIV|auth_id_resp|BuildPaymentPostData|VerifyNotificationHash" ChurchReport --glob "*.cs"
git diff -- LinePayCSharp
git diff --check
```

Results:

- Targeted core tests: 13 passed.
- Targeted core tests after fail-closed guard: 14 passed.
- Targeted ChurchReport adapter tests: 5 passed.
- `SpeechMessage.Payments.Tests`: 49 passed.
- `ChurchReport.MemberInfo.Tests`: 79 passed.
- `ChurchReport.sln` build: 0 warnings, 0 errors.
- Boundary searches: no matches.
- `LinePayCSharp`: no diff.
- `git diff --check`: no whitespace errors; Git reported CRLF normalization warnings only.

## Follow-up Review - Sinopac Card Redirect and HTTP 400

Date: 2026-06-26
Scope reviewed:

- `SpeechMessage.Payments/Providers/Sinopac/SinopacCrypto.cs`
- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs`
- `SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs`
- `ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs`
- `ChurchReport.MemberInfo.Tests/Payments/QPayCreatePaymentGatewayAdapterTests.cs`
- `ChurchReport/Views/Dedication/QPayView.cshtml`
- `.trellis/spec/backend/quality-guidelines.md`

External review status:

- Required CCG wrapper was checked with `Test-Path "$HOME\.claude\bin\codeagent-wrapper"`.
- Result: `False`.
- Gemini/Claude CCG review could not be rerun from this environment because the wrapper is not present.

Manual review findings:

- Critical: none.
- Warning: none.
- Info:
  - Sinopac AES key derivation now preserves legacy uppercase hex bytes; the sandbox `JesusTest` regression locks the value to `89C697BCC1C10908864428F5C58A068A`.
  - Sinopac create-payment mapping now fails closed if a hosted card/mobile/LinePay response lacks a payment page URL.
  - ChurchReport QPay legacy adapter now fails closed instead of creating a successful `CreOrder` with empty `CardParam.CardPayURL`.
  - Donation page JavaScript now redirects only to non-empty absolute `http`/`https` URLs. Legacy `DedicationResult` URL fallback is limited to credit-card flow; explicit `PaymentPageUrl`/`RedirectUrl` remains valid for future hosted payment responses.
  - HTTP 400 diagnostics now include the Sinopac route and a truncated response-body snippet, improving next-failure diagnosis without adding credentials to provider data.

Verification:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal
dotnet vstest ChurchReport.MemberInfo.Tests\bin\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll
dotnet build ChurchReport.sln --no-restore -v minimal
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
rg -n "QPay\.Domain|QryOrderPay|TSResultContent|QryOrder\b|OrderInfo\b|TSResult\b|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|MyPayReturnModel|MyPayProcessingResult|MyPayStatusHelper" ChurchReport --glob "*.cs"
rg -n "\bIPayment\b|IQPayToolkit|QPayToolkit|QPayToolkitWrapper|MyPayToolkit|MyPayToolkitWrapper|TspgToolkit|TspgToolkitWrapper|TSPGWebhookHandler|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|TSPGPaymentRequest|TSPGPaymentNotification|StoreKey|StoreIV|auth_id_resp|BuildPaymentPostData|VerifyNotificationHash" ChurchReport --glob "*.cs"
git diff -- LinePayCSharp
git diff --check
```

Results:

- `SpeechMessage.Payments.Tests`: 47 passed.
- `ChurchReport.MemberInfo.Tests`: 77 passed.
- `ChurchReport.sln` build: 0 warnings, 0 errors.
- Boundary searches: no matches.
- `LinePayCSharp`: no diff.
- `git diff --check`: no whitespace errors; Git reported CRLF normalization warnings only.
- User verification: card donation now redirects to the Sinopac credit-card entry page.

## External Review Status

### Gemini

Result: completed enough to produce a review report, then the wrapper timed out during later retry handling.

Findings:

- Critical: none
- Warning: none
- Info:
  - Deleting `ChurchReport/Models/MyPayReturnModel.cs` and `ChurchReport/Services/MyPayStatusHelper.cs` is consistent with the payment boundary cleanup.
  - Constructor and DI cleanup for `MyPayStatusHelper` references is consistent across `MyPayController`, `MyPayCrmService`, `MyPayNotificationService`, tests, and `Startup.cs`.
  - The `QPayProcessorGatewayAdapterTests` `TryGetValue` helper change from `null` to `Array.Empty<byte>()` is correct for the non-nullable `out byte[]` contract.
  - No sensitive-data or credential exposure was introduced by this diff.

Wrapper caveat:

- First Gemini attempt failed with `spawn EPERM`.
- Escalated retry produced the review content above, then repeatedly hit backend `500` errors and timed out. The review content was captured from the command output, but the wrapper did not exit cleanly.

### Claude

Result: failed to produce a report.

Attempts:

- First run exited with status `1`; wrapper log was deleted.
- Second run exited with status `1`; wrapper log was deleted.
- A later escalated retry was interrupted by the user before completion.

Because the Claude reviewer produced no usable findings, no Claude findings were applied.

## Local Verification

Commands run successfully:

```powershell
dotnet build ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal
dotnet vstest ChurchReport.MemberInfo.Tests\bin\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll --TestCaseFilter:"FullyQualifiedName~MyPayControllerAdapterTests|FullyQualifiedName~QPayReturnWorkflowTests|FullyQualifiedName~QPayProcessorGatewayAdapterTests|FullyQualifiedName~QPayCreatePaymentGatewayAdapterTests"
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal
dotnet vstest ChurchReport.MemberInfo.Tests\bin\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll
dotnet build ChurchReport.sln --no-restore -v minimal
```

Results:

- `SpeechMessage.Payments.Tests`: 44 passed.
- `ChurchReport.MemberInfo.Tests`: 76 passed.
- `ChurchReport.sln` build: 0 warnings, 0 errors.

Boundary searches:

```powershell
rg -n 'ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext' SpeechMessage.Payments --glob '*.cs' --glob '*.csproj'
rg -n 'QPay\.Domain|QryOrderPay|TSResultContent|QryOrder\b|OrderInfo\b|TSResult\b|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|MyPayReturnModel|MyPayProcessingResult|MyPayStatusHelper' ChurchReport --glob '*.cs'
rg -n '\bIPayment\b|IQPayToolkit|QPayToolkit|QPayToolkitWrapper|MyPayToolkit|MyPayToolkitWrapper|TspgToolkit|TspgToolkitWrapper|TSPGWebhookHandler|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|TSPGPaymentRequest|TSPGPaymentNotification|StoreKey|StoreIV|auth_id_resp|BuildPaymentPostData|VerifyNotificationHash' ChurchReport --glob '*.cs'
rg -n 'LinePay' SpeechMessage.Payments --glob '*.cs' --glob '*.csproj'
git diff -- LinePayCSharp
```

Results:

- No forbidden core dependency matches in `SpeechMessage.Payments`.
- No old QPay/MyPay/TSPG toolkit/model/status-helper matches in compiled `ChurchReport` code.
- No Line Pay implementation in `SpeechMessage.Payments`.
- No `LinePayCSharp` diff.

Strict keyword search:

```powershell
rg -n 'QPayToolkit|MyPayToolkit|TspgToolkit|TSPGWebhookHandler|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|StoreKey|StoreIV|XKey|A1|A2|B1|B2|signature|hash' ChurchReport --glob '*.cs'
```

Remaining matches were reviewed as false positives:

- Password/session fingerprint SHA256 hashing in `BaseChurchController` and `InMemoryDataContextSmallGroup`.
- Classroom/course strings such as `A2` and `B1`.
- URL-encoded image filenames.
- A `rehash` comment about dictionary capacity.

## Consolidated Finding

Critical: none.

Warning: none.

Info:

- Task 11 cleanup removed previously unused MyPay provider callback/status DTO code from `ChurchReport`.
- ChurchReport now keeps MyPay product workflow services and no longer carries the unused provider status helper dependency.
- The reusable payment core boundary remains clean after verification.

## 2026-06-26 Sinopac ATM LINE Notification Fix

Scope:

- Restored ChurchReport-owned ATM payment-instruction LINE notification observability after Sinopac ATM virtual account creation.
- Kept LINE notification behavior in ChurchReport product workflow; no LINE or ChurchReport dependency was added to `SpeechMessage.Payments`.
- `QpayManager` now constructs `QPayProcessor` through the compatibility constructor so the processor uses the manager-created LINE client and push utility.
- Added `PushUtility.SendMessageOrThrowAsync(...)` for required payment-instruction notifications. Existing legacy `SendMessage(...)` behavior remains unchanged for optional notification paths that intentionally do not block the workflow.
- `QPayProcessor.ProcessAtm(...)` now resolves LINE ID from explicit input, `new_lineid`, then `new_lineid_backup`; missing/rejected LINE delivery keeps the ATM instructions visible and appends a visible warning to the returned HTML.

Regression tests added:

- `ChurchReport.MemberInfo.Tests/Payments/PushUtilityTests.cs`
  - verifies strict LINE push posts to `/bot/message/push`.
  - verifies LINE API rejection propagates as `LineResponseException`.
  - verifies empty LINE user ID is rejected before any LINE HTTP call.

Verification:

```powershell
dotnet build ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal -p:BaseOutputPath=.\artifacts\test-build\ -p:UseSharedCompilation=false
dotnet vstest ChurchReport.MemberInfo.Tests\artifacts\test-build\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll --Tests:ChurchReport.MemberInfo.Tests.Payments.PushUtilityTests
dotnet vstest ChurchReport.MemberInfo.Tests\artifacts\test-build\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal
dotnet build ChurchReport.sln --no-restore -v minimal -p:BaseOutputPath=.\artifacts\solution-build\ -p:UseSharedCompilation=false
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
rg -n "QPay\.Domain|QryOrderPay|TSResultContent|QryOrder\b|OrderInfo\b|TSResult\b|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|MyPayReturnModel|MyPayProcessingResult|MyPayStatusHelper" ChurchReport --glob "*.cs"
rg -n "\bIPayment\b|IQPayToolkit|QPayToolkit|QPayToolkitWrapper|MyPayToolkit|MyPayToolkitWrapper|TspgToolkit|TspgToolkitWrapper|TSPGWebhookHandler|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|TSPGPaymentRequest|TSPGPaymentNotification|StoreKey|StoreIV|auth_id_resp|BuildPaymentPostData|VerifyNotificationHash" ChurchReport --glob "*.cs"
git diff -- LinePayCSharp
git diff --check
```

Results:

- `PushUtilityTests`: 3 passed.
- `ChurchReport.MemberInfo.Tests`: 82 passed.
- `SpeechMessage.Payments.Tests`: 49 passed.
- `ChurchReport.sln` build: 0 errors; one existing xUnit analyzer warning in `MemberInfoScopeGuardTests.cs` about a null argument.
- Initial default-output test/build attempts were blocked by locked DLLs under `ChurchReport/bin/Debug/net10.0`; lock owners were Visual Studio PID 15440 and IIS Express Worker Process PID 4956. The verified runs used isolated `artifacts/*` output paths to avoid touching the running app binaries.
- Boundary searches: no matches.
- `LinePayCSharp`: no diff.
- `git diff --check`: no whitespace errors; Git reported CRLF normalization warnings only.

External review:

- CCG wrapper remains unavailable in this environment (`$HOME/.claude/bin/codeagent-wrapper` not present), so Gemini/Claude review could not be run for this slice.
