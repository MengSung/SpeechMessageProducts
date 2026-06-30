# Payment Host Integration Layer Implementation Plan

## Scope

This plan is for a future implementation phase. Do not start coding until the user approves this design.

The implementation should extract reusable ASP.NET payment host utilities into a new project while leaving `SpeechMessage.Payments` provider core unchanged and leaving ChurchReport product workflow in ChurchReport.

## Proposed Project

```text
SpeechMessage.Payments.AspNetCore
```

## Ordered Tasks

1. Create `SpeechMessage.Payments.AspNetCore` as a `net10.0` class library.
2. Add project reference:
   - `SpeechMessage.Payments.AspNetCore` -> `SpeechMessage.Payments`
   - `ChurchReport` -> `SpeechMessage.Payments.AspNetCore`
3. Move `PaymentHttpRequestMapper` from `ChurchReport/Payments` to the new project.
4. Move `PaymentAcknowledgementResultMapper` from `ChurchReport/Payments` to the new project.
5. Add `AddSpeechMessagePaymentAspNetCore()` DI extension to the new project.
6. Update `ChurchReport/Startup.cs` to use the new DI extension and remove direct registration of moved classes.
7. Move or update tests for:
   - raw body buffering and rewind
   - form/query/header flattening
   - acknowledgement plain text, JSON, redirect, and default status response
8. Evaluate `PaymentCreateRequestFactory`:
   - move only if product-specific names can be removed cleanly
   - otherwise keep in ChurchReport for now
9. Keep all QPay compatibility classes in ChurchReport:
   - `QPayCreatePaymentGatewayAdapter`
   - `QPayReturnWorkflow`
   - `QPayProductWorkflowDispatcher`
   - `QPayWorkflowPaymentResult`
   - `LegacyQPayModels`
10. Run tests and boundary searches.
11. Update documentation and review notes.

## Files Expected To Move

Move:

```text
ChurchReport/Payments/PaymentHttpRequestMapper.cs
ChurchReport/Payments/PaymentAcknowledgementResultMapper.cs
```

Potentially move after review:

```text
ChurchReport/Payments/PaymentCreateRequestFactory.cs
```

Do not move:

```text
ChurchReport/Payments/ChurchReportPaymentProfileResolver.cs
ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs
ChurchReport/Payments/QPayReturnWorkflow.cs
ChurchReport/Payments/QPayProductWorkflowDispatcher.cs
ChurchReport/Payments/QPayWorkflowPaymentResult.cs
ChurchReport/Payments/LegacyQPayModels.cs
ChurchReport/Controllers/*
ChurchReport/Tools/*
ChurchReport/WebServiceConnector/QPayProcessor/*
ChurchReport/Views/*
```

## Validation Commands

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet build ChurchReport.sln --no-restore -v minimal -p:UseSharedCompilation=false
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|Dataverse|QPayFeeProcessor|QPayDedicationBookingProcessor|QpayManager|QpayModel" SpeechMessage.Payments.AspNetCore --glob "*.cs" --glob "*.csproj"
rg -n "class PaymentHttpRequestMapper|class PaymentAcknowledgementResultMapper" ChurchReport --glob "*.cs"
git diff -- LinePayCSharp
git diff --check
```

## Rollback

Because this migration should move only two small adapter classes first, rollback is straightforward:

1. Remove `SpeechMessage.Payments.AspNetCore` project reference from `ChurchReport`.
2. Move the adapter files back to `ChurchReport/Payments`.
3. Restore `Startup.cs` registrations.
4. Re-run payment adapter tests.

## Deferred Work

Do not include these in the first host-layer implementation:

- Renaming `QPayView`, `QPayLogin`, `QpayManager`, or `QPayProcessor`.
- Rewriting ChurchReport donation pages.
- Moving CRM/LINE workflow into a reusable project.
- Generalizing ChurchReport's `PAY_PROVIDER` display-name mapping into the reusable host project.
- Changing provider core behavior in `SpeechMessage.Payments`.
