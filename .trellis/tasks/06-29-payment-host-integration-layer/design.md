# Payment Host Integration Layer Design

## Summary

Keep `SpeechMessage.Payments` as the pure reusable payment core. Add a second, optional reusable host integration project named `SpeechMessage.Payments.AspNetCore` for payment-related ASP.NET adapter utilities that are useful across multiple products.

This second project is not a new payment provider core. It is the thin bridge between ASP.NET host applications and the already extracted `SpeechMessage.Payments` neutral contract.

The intended long-term shape is:

```text
Product host applications
  ChurchReport
  Construction repair system
  Association membership system
  Invoice collection system
      |
      | product workflow, persistence, UI, notifications
      v
SpeechMessage.Payments.AspNetCore
  HttpRequest -> PaymentCallbackRequest mapping
  PaymentCallbackAcknowledgement -> IActionResult / IResult mapping
  optional neutral request-builder helpers
  host DI registration helpers
      |
      v
SpeechMessage.Payments
  provider-neutral contracts
  provider routing
  Sinopac/QPay provider protocol
  MyPay provider protocol
  Taishin/TSPG provider protocol
```

## Why This Is A Second Project

`SpeechMessage.Payments` was deliberately kept free of ASP.NET, controllers, CRM, LINE, MVC views, and product persistence. That boundary is correct and should not be weakened.

However, reusable products still need common host-side glue:

- read callback raw body safely after ASP.NET model binding
- flatten query/form/header values into the neutral callback DTO
- turn provider acknowledgement descriptors into HTTP responses
- register common payment host services through DI
- build neutral create-payment requests without leaking provider SDK DTOs

Those concerns are not provider protocol, so they should not go into the core. They are also not ChurchReport business workflow, so leaving them in `ChurchReport` makes future products copy code. A separate host integration project is the right boundary.

## Project Name And Target

Recommended project:

```text
SpeechMessage.Payments.AspNetCore
```

Recommended target:

```text
net10.0
```

Allowed references:

- `SpeechMessage.Payments`
- ASP.NET Core abstractions used for request and response mapping
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Configuration.Abstractions` only for generic options binding, if needed

Forbidden references:

- `ChurchReport`
- `ToolUtility`
- `Line.Messaging`
- `Microsoft.Xrm.Sdk`
- `Microsoft.PowerPlatform.Dataverse.Client`
- MVC views or Razor pages from a product
- product database/persistence packages
- product-specific controller classes

## Responsibility Split

### `SpeechMessage.Payments`

Owns:

- payment contracts and neutral DTOs
- provider selection by profile
- provider options and validation
- provider HTTP calls
- provider signing, encryption, callback parsing, and status normalization
- sanitized diagnostics

Does not own:

- ASP.NET `HttpRequest`
- `IActionResult`
- controller routing
- UI/view results
- product workflow
- CRM/LINE/persistence

### `SpeechMessage.Payments.AspNetCore`

Owns:

- `HttpRequest` to `PaymentCallbackRequest` mapping
- safe request-body buffering for callbacks
- query/form/header flattening
- `PaymentCallbackAcknowledgement` to ASP.NET response mapping
- optional generic profile-name resolution from a host-supplied mapping
- optional generic `PaymentCreateRequest` construction helpers that depend only on neutral models
- DI extension methods for host applications

Does not own:

- provider protocol rules
- provider SDK DTOs
- CRM, LINE, donation, fee, invoice, or repair workflow
- ChurchReport route names
- legacy QPay-compatible `CreOrder` return shapes

### `ChurchReport`

Owns:

- controllers and routes
- `QPayLogin`, `QPayView`, and existing route compatibility
- `DedicationController`, `QpayManager`, `QpayModel`
- CRM fee and dedication booking updates
- LINE notifications
- product result pages and redirect decisions
- callback deduplication and persistence policy
- temporary adapters required to keep older ChurchReport code working

Must stop owning, after phase 2 implementation:

- reusable ASP.NET callback request mapping
- reusable acknowledgement-to-response mapping
- generic host DI helper code
- product-neutral create request boilerplate

## Current Class Classification

### Move To `SpeechMessage.Payments.AspNetCore`

These are reusable host adapter utilities:

```text
ChurchReport/Payments/PaymentHttpRequestMapper.cs
ChurchReport/Payments/PaymentAcknowledgementResultMapper.cs
```

Possible move after review:

```text
ChurchReport/Payments/PaymentCreateRequestFactory.cs
```

`PaymentCreateRequestFactory` is mostly neutral today, but the input type must be checked carefully. It can move only if it remains free of ChurchReport-specific field names and workflow assumptions.

### Keep In ChurchReport

These are product-specific or legacy compatibility classes:

```text
ChurchReport/Payments/ChurchReportPaymentProfileResolver.cs
ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs
ChurchReport/Payments/QPayReturnWorkflow.cs
ChurchReport/Payments/QPayProductWorkflowDispatcher.cs
ChurchReport/Payments/QPayWorkflowPaymentResult.cs
ChurchReport/Payments/LegacyQPayModels.cs
```

Reasons:

- `ChurchReportPaymentProfileResolver` maps ChurchReport's legacy `PAY_PROVIDER` display names to profile names.
- `QPayCreatePaymentGatewayAdapter` exists to satisfy old ChurchReport QPay-shaped callers.
- `QPayReturnWorkflow` returns ChurchReport views and dispatches ChurchReport-specific product workflow.
- `QPayProductWorkflowDispatcher` calls `QPayFeeProcessor` and `QPayDedicationBookingProcessor`, which own CRM/LINE logic.
- `QPayWorkflowPaymentResult` is a compatibility DTO for old ChurchReport processors.
- `LegacyQPayModels` exists only to keep old ChurchReport processors alive during staged migration.

### Rename Or Remove In A Later ChurchReport Product Refactor

These names are product/UI compatibility, not reusable payment infrastructure:

```text
ChurchReport/Controllers/QPayCardController.cs
ChurchReport/Controllers/HomeController.cs QPayLogin compatibility routes
ChurchReport/Controllers/DedicationController.cs QPayView routes/actions
ChurchReport/Models/QpayManager.cs
ChurchReport/Views/Dedication/QPayView.cshtml
ChurchReport/Views/Home/QPayLogin.cshtml
ChurchReport/WebServiceConnector/QPayProcessor/*
ChurchReport/Tools/QPayFeeProcessor.cs
ChurchReport/Tools/QPayDedicationBookingProcessor.cs
ChurchReport/Tools/QPayPaymentDebugLogger.cs
ChurchReport/Tools/QPayPaymentResultHelper.cs
```

These should not be moved into `SpeechMessage.Payments.AspNetCore`. They can be renamed to product-neutral ChurchReport names later, for example:

- `QpayManager` -> `OnlineDonationManager`
- `QPayView` -> `OnlineDonationView`
- `QPayProcessor` -> `DonationPaymentWorkflowProcessor`
- `QPayCardController` -> `PaymentReturnController` or `SinopacReturnController`

That rename is a separate product-domain refactor because it touches routes, views, tests, and user-facing behavior.

## QPay Naming Policy

`QPay` is acceptable only where it means the Sinopac provider protocol or a legacy ChurchReport compatibility surface.

Acceptable:

- `SpeechMessage.Payments/Providers/Sinopac/*` comments and internal DTOs
- Sinopac endpoint URLs containing `QPay.WebAPI`
- ChurchReport legacy routes preserved for backward compatibility
- old ChurchReport workflow names until a separate rename task replaces them

Not acceptable after phase 2:

- generic host adapter class names in reusable projects
- provider-neutral public contracts
- shared host DI extension names
- common request/ack mapper names in ChurchReport when they can live in `SpeechMessage.Payments.AspNetCore`

## Proposed Public API In Host Project

Recommended types:

```csharp
public sealed class PaymentHttpRequestMapper
{
    Task<PaymentCallbackRequest> MapAsync(
        HttpRequest request,
        string profileName,
        PaymentProviderKind? providerHint = null,
        CancellationToken cancellationToken = default);
}
```

```csharp
public sealed class PaymentAcknowledgementResultMapper
{
    IActionResult ToActionResult(PaymentCallbackAcknowledgement acknowledgement);
}
```

Optional generic profile resolver:

```csharp
public interface IPaymentProfileNameResolver
{
    string ResolveProfileName(string? requestedProfileName = null);
}
```

The generic resolver must be configured by the host. It must not know ChurchReport's Chinese `PAY_PROVIDER` values.

Recommended DI extension:

```csharp
public static class PaymentAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddSpeechMessagePaymentAspNetCore(
        this IServiceCollection services);
}
```

## Migration Flow

1. Create `SpeechMessage.Payments.AspNetCore` and add it to the solution.
2. Move `PaymentHttpRequestMapper` into the new project without behavior changes.
3. Move `PaymentAcknowledgementResultMapper` into the new project without behavior changes.
4. Add tests in a new or existing test project proving raw-body buffering, form/query/header mapping, and acknowledgement mapping are unchanged.
5. Update ChurchReport references and DI registration to consume the host project.
6. Evaluate `PaymentCreateRequestFactory`. Move it only if its input can be made product-neutral without breaking ChurchReport.
7. Keep QPay compatibility classes in ChurchReport for now.
8. Run boundary searches and solution build.

## Verification Commands

Core boundary:

```powershell
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
```

Host project boundary:

```powershell
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|Dataverse|QPayFeeProcessor|QPayDedicationBookingProcessor|QpayManager|QpayModel" SpeechMessage.Payments.AspNetCore --glob "*.cs" --glob "*.csproj"
```

ChurchReport reusable adapter cleanup:

```powershell
rg -n "class PaymentHttpRequestMapper|class PaymentAcknowledgementResultMapper" ChurchReport --glob "*.cs"
```

QPay naming audit:

```powershell
rg -n "QPay|Qpay|qpay" ChurchReport --glob "*.cs" --glob "*.cshtml" --glob "*.json" --glob "*.csproj" -g "!ChurchReport/文件/**"
```

Expected after phase 2:

- `PaymentHttpRequestMapper` and `PaymentAcknowledgementResultMapper` are no longer implemented in ChurchReport.
- `SpeechMessage.Payments.AspNetCore` has no ChurchReport, CRM, LINE, or product workflow dependency.
- QPay strings remain in ChurchReport only where they are route/UI/legacy compatibility or explicit Sinopac configuration.
- `SpeechMessage.Payments` remains unchanged except for project reference/build metadata if absolutely required.

## Risks

- Moving too much into `SpeechMessage.Payments.AspNetCore` would recreate the coupling problem in a new project.
- Renaming QPay UI/workflow in the same task would increase blast radius and risk breaking ChurchReport routes.
- A generic profile resolver could accidentally encode ChurchReport's `PAY_PROVIDER` behavior. Keep that mapping host-owned.
- Existing tests reference ChurchReport adapter classes. They must be moved or updated with minimal behavior change.

## Decision

Proceed with a two-layer reusable payment architecture:

1. `SpeechMessage.Payments` for provider protocol core.
2. `SpeechMessage.Payments.AspNetCore` for reusable ASP.NET host adapters.

Keep ChurchReport product workflow and legacy QPay compatibility in ChurchReport until a separate product-domain rename/refactor is approved.
