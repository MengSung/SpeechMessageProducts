# CCG Review - Payment Module Extraction

Date: 2026-06-26
Task: `payment-module-extraction`
Scope reviewed: Task 11 cleanup diff from the working tree at review time.

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
