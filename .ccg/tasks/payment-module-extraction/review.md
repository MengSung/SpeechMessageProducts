# CCG Review - Payment Module Extraction

Date: 2026-06-26
Task: `payment-module-extraction`
Scope reviewed: Task 11 cleanup diff from the working tree at review time.

## Follow-up Review - Documentation And Traditional Chinese Code Comments

Date: 2026-06-27
Scope reviewed:

- `docs/payment-module-extraction-report-zh-TW.md`
- `SpeechMessage.Payments/Abstractions/*.cs`
- `SpeechMessage.Payments/Configuration/*.cs`
- `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs`
- `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs`
- `SpeechMessage.Payments/Gateway/PaymentGateway.cs`
- `SpeechMessage.Payments/Models/*.cs`
- `SpeechMessage.Payments/Providers/MyPay/*.cs`
- `SpeechMessage.Payments/Providers/Sinopac/*.cs`
- `SpeechMessage.Payments/Providers/Taishin/*.cs`
- `ChurchReport/Payments/*.cs`
- `ChurchReport/Controllers/MyPayController.cs`
- `ChurchReport/Controllers/TSPGController.cs`
- `ChurchReport/Controllers/QPayCardController.cs`
- `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.PaymentGateway.cs`
- `ChurchReport/Startup.cs`
- `SpeechMessage.Payments.Tests/**/*.cs`

External review status:

- Required CCG wrapper is not present in this environment (`Test-Path "$HOME\.claude\bin\codeagent-wrapper"` returned `False`), so Gemini/Claude dual-model review could not be run for this documentation/comment follow-up.

Manual review findings:

- Critical: none.
- Warning: none.
- Info:
  - Added a detailed Traditional Chinese extraction report covering architecture, modified/written projects and files, provider responsibilities, ChurchReport adapter boundaries, tests, known limitations, validation commands, deployment notes, and bugs fixed during the extraction.
  - Added Traditional Chinese maintenance comments across the reusable payment core, provider implementations, ChurchReport thin adapters/controllers, QPay compatibility bridge, and key tests.
  - Kept `SpeechMessage.Payments` boundary grep clean by using product-neutral wording inside core comments. ChurchReport-specific boundary explanations remain in ChurchReport adapter files and the report document.
  - No runtime logic, provider payload fields, route names, credential values, or callback acknowledgement behavior were changed in this follow-up; changes are comments/documentation only.
  - No `LinePayCSharp` changes were introduced.

Verification:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet build ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -m:1 -v minimal -p:BaseOutputPath=.\artifacts\payment-report-comments-member-build\ -p:UseSharedCompilation=false
dotnet vstest "ChurchReport.MemberInfo.Tests\artifacts\payment-report-comments-member-build\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll" --TestCaseFilter:"FullyQualifiedName~Payments"
dotnet build ChurchReport.sln --no-restore -v minimal -p:BaseOutputPath=.\artifacts\solution-build-payment-report-comments\ -p:UseSharedCompilation=false
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
rg -n "QPay\.Domain|QryOrderPay|TSResultContent|QryOrder\b|OrderInfo\b|TSResult\b|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|MyPayReturnModel|MyPayProcessingResult|MyPayStatusHelper" ChurchReport --glob "*.cs"
rg -n "\bIPayment\b|IQPayToolkit|QPayToolkit|QPayToolkitWrapper|MyPayToolkit|MyPayToolkitWrapper|TspgToolkit|TspgToolkitWrapper|TSPGWebhookHandler|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|TSPGPaymentRequest|TSPGPaymentNotification|StoreKey|StoreIV|auth_id_resp|BuildPaymentPostData|VerifyNotificationHash" ChurchReport --glob "*.cs"
git diff -- LinePayCSharp
git diff --check
```

Results:

- `SpeechMessage.Payments.Tests`: 53 passed.
- `ChurchReport.MemberInfo.Tests` build: 0 errors; one existing xUnit analyzer warning in `MemberInfoScopeGuardTests.cs` about a null argument.
- `ChurchReport.MemberInfo.Tests` payment filter: 39 passed.
- `ChurchReport.sln` build: 0 errors; two warnings:
  - one transient file-lock retry warning while copying `ToolUtility.dll`, which recovered during the build.
  - the existing `xUnit1012` analyzer warning in `MemberInfoScopeGuardTests.cs`.
- Boundary search in `SpeechMessage.Payments`: no matches.
- Legacy toolkit/provider model searches in compiled `ChurchReport` code: no matches.
- `LinePayCSharp`: no diff.
- `git diff --check`: no whitespace errors; Git reported CRLF normalization warnings only.
- A parallel `ChurchReport.MemberInfo.Tests` build attempt failed earlier because `ChurchReport\obj\Debug\net10.0\rpswa.dswa.cache.json` was locked by another process while the solution build was running. The sequential `-m:1` rebuild above passed, so this was treated as an environment/file-lock issue, not a code issue.

## Follow-up Review - MyPay Encrypted Create Payload Contract

Date: 2026-06-27
Scope reviewed:

- `SpeechMessage.Payments/Providers/MyPay/MyPayModels.cs`
- `SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs`
- `SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs`
- `ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs`
- `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.PaymentGateway.cs`
- `ChurchReport.MemberInfo.Tests/Payments/QPayCreatePaymentGatewayAdapterTests.cs`
- `ChurchReport.MemberInfo.Tests/Payments/QPayProcessorGatewayAdapterTests.cs`
- `.trellis/spec/backend/quality-guidelines.md`

External review status:

- Required CCG wrapper is not present in this environment (`Test-Path "$HOME\.claude\bin\codeagent-wrapper"` returned `False` earlier in this task), so Gemini/Claude review could not be run for this follow-up.

Manual review findings:

- Critical: none.
- Warning: none.
- Info:
  - Root cause candidate: after the direct merchant outer form fix, MyPay could still reject `encry_data` because the migrated encrypted payload no longer matched the previous working MyPay `api/orders` contract. The old flow sent `store_uid`, `items`, `cost`, `user_id`, `order_id`, `ip`, and `pfn`; the migrated mapper omitted `items`, `user_id`, and `ip`.
  - Secondary compatibility issue: the migrated mapper passed the neutral/QPay payment method value such as `C` into MyPay `pfn`. MyPay `pfn` is a payment-function value; card flow now defaults to legacy-compatible `0`, while explicit metadata/profile `PFN` can override it.
  - ChurchReport remains a product adapter: it supplies default line-item data and the contact name as `UserId`; provider-specific MyPay payload construction stays inside `SpeechMessage.Payments`.
  - No `ChurchReport`, ASP.NET, CRM, LINE, or persistence dependency was added to `SpeechMessage.Payments`.

Verification:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet build ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal -p:BaseOutputPath=.\artifacts\mypay-payload-fix-build\ -p:UseSharedCompilation=false
dotnet vstest "ChurchReport.MemberInfo.Tests\artifacts\mypay-payload-fix-build\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll" --TestCaseFilter:"FullyQualifiedName~Payments"
dotnet build ChurchReport.sln --no-restore -v minimal -p:BaseOutputPath=.\artifacts\solution-build-mypay-payload-fix\ -p:UseSharedCompilation=false
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
git diff --check
```

Results:

- `SpeechMessage.Payments.Tests`: 53 passed.
- `ChurchReport.MemberInfo.Tests` payment filter: 39 passed.
- `ChurchReport.MemberInfo.Tests` build: 0 errors; one existing xUnit analyzer warning in `MemberInfoScopeGuardTests.cs` about a null argument.
- `ChurchReport.sln` build: 0 errors; same existing xUnit analyzer warning.
- Boundary search: no matches.
- `git diff --check`: no whitespace errors; Git reported CRLF normalization warnings only.

## Follow-up Review - MyPay Direct Merchant Form Contract

Date: 2026-06-27
Scope reviewed:

- `SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs`
- `SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs`
- `.trellis/spec/backend/quality-guidelines.md`

External review status:

- 2026-06-29 rerun attempted with `C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe` against commit `90898ecd` (`fix: restore sinopac recurring card setup`).
- Gemini command:
  `codeagent-wrapper.exe --progress --backend gemini - <repo>`
  Result: wrapper launched, but failed because `gemini command not found in PATH`.
- Claude command:
  `codeagent-wrapper.exe --progress --backend claude - <repo>`
  Result: wrapper launched, but failed because `claude command not found in PATH`.
- Both required external model reviews were invoked, but the underlying model CLIs are unavailable in this environment, so no external review reports were produced.

Manual review findings:

- Critical: none.
- Warning: none.
- Info:
  - Root cause candidate: the migrated MyPay create-payment mapper sent both top-level `store_uid` and top-level `agent_uid` to `/api/init`. MyPay direct merchant examples and the previous working ChurchReport `StoreOrder.GetPostData(...)` send only top-level `store_uid`, while `agent_uid` belongs to reseller `/api/agent` flows.
  - The mapper now defaults normal MyPay profiles to direct merchant mode and sends top-level `store_uid` only. Reseller mode is selected only when `Credentials:AgentId` is explicitly configured, and then the top-level form sends `agent_uid`.
  - The encrypted payload still includes the merchant `store_uid` for both modes, preserving the provider payload contract.
  - The fix stays inside `SpeechMessage.Payments`; ChurchReport remains responsible only for profile selection and product workflow.

Verification:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet build ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal -p:BaseOutputPath=.\artifacts\mypay-direct-form-fix-build\ -p:UseSharedCompilation=false
dotnet vstest "ChurchReport.MemberInfo.Tests\artifacts\mypay-direct-form-fix-build\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll" --TestCaseFilter:"FullyQualifiedName~Payments"
dotnet build ChurchReport.sln --no-restore -v minimal -p:BaseOutputPath=.\artifacts\solution-build-mypay-direct-form-fix\ -p:UseSharedCompilation=false
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
git diff --check
```

Results:

- `SpeechMessage.Payments.Tests`: 52 passed.
- `ChurchReport.MemberInfo.Tests` payment filter: 39 passed.
- `ChurchReport.MemberInfo.Tests` build: 0 errors; one existing xUnit analyzer warning in `MemberInfoScopeGuardTests.cs` about a null argument.
- `ChurchReport.sln` build: 0 errors; same existing xUnit analyzer warning.
- Boundary search: no matches.
- `git diff --check`: no whitespace errors; Git reported CRLF normalization warnings only.

## Follow-up Review - Sinopac Recurring Card Payment Page

Date: 2026-06-27
Scope reviewed:

- `ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs`
- `ChurchReport.MemberInfo.Tests/Payments/QPayCreatePaymentGatewayAdapterTests.cs`
- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs`
- `SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs`
- `.trellis/spec/backend/quality-guidelines.md`

External review status:

- Required CCG wrapper is not present in this environment (`Test-Path "$HOME\.claude\bin\codeagent-wrapper"` returned `False`), so Gemini/Claude review could not be run.

Manual review findings:

- Critical: none.
- Warning: none.
- Info:
  - Local re-review on 2026-06-29 covered commit `90898ecd` and confirmed the recurring-card default is scoped to `QPayCreatePaymentGatewayAdapter.BuildMetadata(...)`; `SpeechMessage.Payments` remains provider/protocol normalization only.
  - Root cause candidate: the recurring card flow can submit the visible UI default total-period value without posting it into `QpayModel.DeductTotalNumber`, causing the legacy adapter to send `DeductTotalNum=0` to Sinopac. The adapter now defaults `REGULAR` card setup to the legacy monthly schedule: 12 total deductions, period type `M`, frequency `1`.
  - Diagnostic gap: Sinopac rejected create responses may also lack a card payment URL. The provider now preserves Sinopac `Status` / `Description` when the bank rejected the request, instead of masking the actionable bank message with the generic missing payment page URL error.
  - One-time hosted card payment remains fail-closed if Sinopac reports success but omits `CardPayURL`.
  - The boundary remains clean: ChurchReport owns the legacy UI/workflow default; `SpeechMessage.Payments` only normalizes the provider response.

Verification:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal
dotnet build ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal -p:BaseOutputPath=.\artifacts\qpay-adapter-build\ -p:UseSharedCompilation=false
dotnet vstest ChurchReport.MemberInfo.Tests\artifacts\qpay-adapter-build\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll --Tests:ChurchReport.MemberInfo.Tests.Payments.QPayCreatePaymentGatewayAdapterTests.CreateCardPaymentAsync_defaults_recurring_schedule_when_ui_default_is_not_posted
dotnet vstest ChurchReport.MemberInfo.Tests\artifacts\qpay-adapter-build\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll --TestCaseFilter:"FullyQualifiedName~QPayCreatePaymentGatewayAdapterTests"
dotnet vstest ChurchReport.MemberInfo.Tests\artifacts\qpay-adapter-build\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll
dotnet build ChurchReport.sln --no-restore -v minimal -p:BaseOutputPath=.\artifacts\solution-build\ -p:UseSharedCompilation=false
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
rg -n "QPay\.Domain|QryOrderPay|TSResultContent|QryOrder\b|OrderInfo\b|TSResult\b|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|MyPayReturnModel|MyPayProcessingResult|MyPayStatusHelper" ChurchReport --glob "*.cs"
rg -n "\bIPayment\b|IQPayToolkit|QPayToolkit|QPayToolkitWrapper|MyPayToolkit|MyPayToolkitWrapper|TspgToolkit|TspgToolkitWrapper|TSPGWebhookHandler|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|TSPGPaymentRequest|TSPGPaymentNotification|StoreKey|StoreIV|auth_id_resp|BuildPaymentPostData|VerifyNotificationHash" ChurchReport --glob "*.cs"
git diff -- LinePayCSharp
git diff --check
```

Results:

- `SpeechMessage.Payments.Tests`: 50 passed.
- New recurring adapter regression: 1 passed.
- `QPayCreatePaymentGatewayAdapterTests`: 6 passed.
- `ChurchReport.MemberInfo.Tests`: 83 passed.
- `ChurchReport.sln` build: 0 errors; one existing xUnit analyzer warning in `MemberInfoScopeGuardTests.cs` about a null argument.
- Boundary searches: no matches.
- `LinePayCSharp`: no diff.
- `git diff --check`: no whitespace errors; Git reported CRLF normalization warnings only.
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj ...` hung without output in this environment; verified runs used isolated build output plus `dotnet vstest`.

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
