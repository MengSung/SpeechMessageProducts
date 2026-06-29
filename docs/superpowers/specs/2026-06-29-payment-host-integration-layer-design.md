# Payment Host Integration Layer Extraction Design

## Executive Summary

The current `SpeechMessage.Payments` extraction is correct as a pure payment provider core. Do not modify that core for this request.

The uncomfortable `QPay*` residue in `ChurchReport.csproj` and `ChurchReport/Payments` is not all the same kind of residue. Some of it is:

- legitimate Sinopac provider vocabulary
- reusable ASP.NET host adapter code that should be moved out of ChurchReport
- ChurchReport-specific legacy workflow and UI naming that should stay until a separate product-domain rename

The recommended second-stage design is to add a separate reusable host integration project:

```text
SpeechMessage.Payments.AspNetCore
```

This project should own generic ASP.NET payment adapter utilities, while `SpeechMessage.Payments` remains provider core and ChurchReport remains product workflow.

## Target Architecture

```text
ChurchReport / future product hosts
  controllers
  product workflow
  CRM / database / LINE / notification code
  views and routes
      |
      v
SpeechMessage.Payments.AspNetCore
  ASP.NET request mapping
  callback acknowledgement response mapping
  optional generic host DI helpers
      |
      v
SpeechMessage.Payments
  provider-neutral payment contracts
  provider profile routing
  Sinopac/QPay provider protocol
  MyPay provider protocol
  Taishin/TSPG provider protocol
```

## Project Boundaries

### `SpeechMessage.Payments`

Keep this project as-is conceptually. It owns:

- provider-neutral payment contracts
- provider routing
- provider request/response mapping
- Sinopac/QPay signing, encryption, callback parsing, and status normalization
- MyPay and Taishin/TSPG protocol behavior
- sanitized provider diagnostics

It must not own:

- ASP.NET controllers
- `HttpRequest`
- `IActionResult`
- MVC views
- CRM or Dataverse updates
- LINE notifications
- product-specific idempotency or persistence

### `SpeechMessage.Payments.AspNetCore`

This new project should own reusable host integration only:

- `HttpRequest` -> `PaymentCallbackRequest`
- request body buffering and rewind behavior
- query/form/header flattening
- `PaymentCallbackAcknowledgement` -> ASP.NET response
- payment host DI extension methods
- optional host-supplied profile resolver abstraction

It must not reference:

- `ChurchReport`
- `ToolUtility`
- `Line.Messaging`
- CRM/Dataverse SDK
- ChurchReport controllers, views, models, or processors
- product persistence packages

### `ChurchReport`

ChurchReport keeps:

- routes and controllers
- donation/fee workflow
- `QPayLogin` and `QPayView` route compatibility
- `QpayManager` and `QpayModel` until a product rename is approved
- CRM updates
- LINE notifications
- result views
- legacy compatibility classes needed by existing processors

ChurchReport should no longer keep generic reusable ASP.NET payment adapter utilities after phase 2.

## Current File Classification

### Move To `SpeechMessage.Payments.AspNetCore`

```text
ChurchReport/Payments/PaymentHttpRequestMapper.cs
ChurchReport/Payments/PaymentAcknowledgementResultMapper.cs
```

These files are generic host adapter utilities. Future products will need the same behavior.

### Candidate For Move After Review

```text
ChurchReport/Payments/PaymentCreateRequestFactory.cs
```

This is mostly neutral, but the input model must be reviewed. If it contains ChurchReport product language, keep it in ChurchReport or split a generic builder into the host project.

### Keep In ChurchReport

```text
ChurchReport/Payments/ChurchReportPaymentProfileResolver.cs
ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs
ChurchReport/Payments/QPayReturnWorkflow.cs
ChurchReport/Payments/QPayProductWorkflowDispatcher.cs
ChurchReport/Payments/QPayWorkflowPaymentResult.cs
ChurchReport/Payments/LegacyQPayModels.cs
```

These are not generic payment infrastructure. They preserve ChurchReport behavior or map into ChurchReport-specific CRM/LINE/donation workflow.

### Separate Future Product Rename

```text
ChurchReport/Controllers/QPayCardController.cs
ChurchReport/Controllers/DedicationController.cs QPayView actions
ChurchReport/Controllers/HomeController.cs QPayLogin compatibility routes
ChurchReport/Models/QpayManager.cs
ChurchReport/Views/Dedication/QPayView.cshtml
ChurchReport/Views/Home/QPayLogin.cshtml
ChurchReport/WebServiceConnector/QPayProcessor/*
ChurchReport/Tools/QPayFeeProcessor.cs
ChurchReport/Tools/QPayDedicationBookingProcessor.cs
```

These names are ugly but product-owned. Moving them into a reusable payment project would contaminate the reusable boundary with ChurchReport-specific workflow. Rename them later as a separate ChurchReport product-domain refactor.

## Naming Rule

`QPay` is allowed where it means Sinopac's provider protocol or a preserved ChurchReport legacy route/workflow.

`QPay` is not allowed in new reusable host adapter names, provider-neutral public models, or generic DI extension names.

## Implementation Plan

1. Create `SpeechMessage.Payments.AspNetCore` as a `net10.0` class library.
2. Reference `SpeechMessage.Payments`.
3. Move `PaymentHttpRequestMapper`.
4. Move `PaymentAcknowledgementResultMapper`.
5. Add `AddSpeechMessagePaymentAspNetCore()` DI extension.
6. Update ChurchReport to reference the new project.
7. Update tests for the moved classes.
8. Evaluate `PaymentCreateRequestFactory` separately.
9. Keep all ChurchReport workflow and legacy QPay compatibility classes in ChurchReport.
10. Run boundary searches and full build.

## Verification

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet build ChurchReport.sln --no-restore -v minimal -p:UseSharedCompilation=false
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|Dataverse|QPayFeeProcessor|QPayDedicationBookingProcessor|QpayManager|QpayModel" SpeechMessage.Payments.AspNetCore --glob "*.cs" --glob "*.csproj"
rg -n "class PaymentHttpRequestMapper|class PaymentAcknowledgementResultMapper" ChurchReport --glob "*.cs"
rg -n "QPay|Qpay|qpay" ChurchReport --glob "*.cs" --glob "*.cshtml" --glob "*.json" --glob "*.csproj" -g "!ChurchReport/文件/**"
git diff -- LinePayCSharp
git diff --check
```

## Key Decision

Do not try to make `ChurchReport.csproj` completely free of every `QPay` string in this phase. That would require route/view/product workflow renaming and could break existing ChurchReport behavior.

Instead, remove reusable host adapter implementation from ChurchReport first. Then run a separate product-domain rename for ChurchReport's old `QPay` UI/workflow names if you want the entire ChurchReport web project to read cleanly.
