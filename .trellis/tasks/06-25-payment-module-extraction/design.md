# Payment Module Extraction Design

## Summary

Create a new `net10.0` class library project inside the existing solution named
`SpeechMessage.Payments`. The project is a pure reusable payment core for the
existing Sinopac/QPay, MyPay, and Taishin/TSPG payment providers.

The core owns provider-specific payment protocol details: request/response
mapping, signing, encryption, decryption, callback parsing, callback
verification, status normalization, error normalization, and sanitized provider
diagnostics.

The core does not own ASP.NET controllers, MVC views, ChurchReport CRM updates,
LINE notifications, persistence, callback deduplication, donation classification,
or product-specific result handling.

ChurchReport keeps its existing payment routes as thin HTTP adapters during
migration. Those adapters translate ASP.NET requests into provider-neutral
payment DTOs, call `SpeechMessage.Payments`, then execute ChurchReport-specific
business workflow.

Line Pay remains out of scope for the first extraction release. The existing
`LinePayCSharp` project is not folded into the new core.

## Goals

- Move provider-specific Sinopac/QPay, MyPay, and Taishin/TSPG implementation
  out of ChurchReport into one independent project.
- Replace the current QPay-shaped `IPayment` boundary with provider-neutral
  contracts and DTOs.
- Preserve existing ChurchReport payment behavior and callback URLs while
  migrating through thin adapters.
- Support multiple named merchant profiles so each product or organization can
  choose a provider/profile through configuration.
- Keep the reusable payment core stateless and product-agnostic.
- Make future reuse possible for other products without dragging ChurchReport
  CRM, LINE, MVC, or donation workflow dependencies into the payment core.

## Non-Goals

- Do not move ASP.NET controllers into `SpeechMessage.Payments`.
- Do not move CRM fee updates, LINE messages, donation classification, or result
  page rendering into `SpeechMessage.Payments`.
- Do not add new providers beyond Sinopac/QPay, MyPay, and Taishin/TSPG.
- Do not fold `LinePayCSharp` into the first release.
- Do not include refund, capture/void, daily bill query, allotment query, or
  payment back-office UI in the first release.
- Do not add a database or any order/payment persistence to the core.

## Architecture

Use a pure core project plus product adapters:

```text
ChurchReport
  Controllers / workflows / CRM / LINE / views
  Thin payment adapters
      |
      v
SpeechMessage.Payments
  Abstractions
  Models
  Configuration
  Providers
    Sinopac
    MyPay
    Taishin
  Diagnostics
  DependencyInjection
```

`SpeechMessage.Payments` may depend on .NET and Microsoft.Extensions primitives
such as options, logging, dependency injection abstractions, and HTTP client
factory support. It must not depend on:

- `ChurchReport`
- `ToolUtility`
- `Line.Messaging`
- `Microsoft.Xrm.Sdk` or Dataverse models
- ASP.NET MVC controllers, views, or `HttpRequest`
- application database/persistence packages

ChurchReport may depend on `SpeechMessage.Payments`, but ChurchReport should not
own provider-specific payment implementation after migration.

## Public Contract

The new public contract is provider-neutral. It must not expose the current
QPay-shaped `IPayment`, `CreOrder`, `QryOrderPay`, or provider SDK models as the
main API.

Recommended public interface:

```csharp
public interface IPaymentGateway
{
    Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken = default);
}
```

The first release contract includes only:

- create payment
- query payment status
- parse and verify provider callbacks
- provider/profile selection
- status and error normalization

Provider-specific operations such as refund, capture, void, bill query, and
allotment query are explicitly outside the first public contract.

## Core Models

The public models should be provider-neutral and stable:

```text
PaymentCreateRequest
PaymentCreateResult
PaymentQueryRequest
PaymentStatusResult
PaymentCallbackRequest
PaymentCallbackResult
PaymentCallbackAcknowledgement
PaymentMerchantProfile
PaymentProviderKind
PaymentEnvironment
PaymentStatus
PaymentError
PaymentErrorKind
```

Each provider may keep provider-specific request/response models internally, but
those models should not leak into ChurchReport workflows.

## Callback Input Contract

The core must not take ASP.NET `HttpRequest`. ChurchReport owns HTTP binding and
adapts it into a neutral callback request.

Recommended callback request shape:

```csharp
public sealed class PaymentCallbackRequest
{
    public string ProfileName { get; init; }
    public PaymentProviderKind? ProviderHint { get; init; }
    public string HttpMethod { get; init; }
    public string ContentType { get; init; }
    public string RawBody { get; init; }
    public IReadOnlyDictionary<string, string> Query { get; init; }
    public IReadOnlyDictionary<string, string> Form { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
}
```

This keeps ASP.NET out of the core while still giving each provider enough
information to parse form, query, JSON, or mixed callback formats.

## Callback Acknowledgement Contract

Provider callback acknowledgement behavior is provider-specific:

- MyPay expects a plain text acknowledgement such as `8888`.
- TSPG backend callbacks expect JSON acknowledgement.
- QPay and TSPG frontend returns may require redirect or product-rendered pages.

ChurchReport adapters should not hard-code provider acknowledgement rules. The
core callback result should include a neutral acknowledgement descriptor:

```csharp
public sealed class PaymentCallbackAcknowledgement
{
    public PaymentAckKind Kind { get; init; }
    public string Content { get; init; }
    public int StatusCode { get; init; }
}
```

Supported acknowledgement kinds:

```text
None
PlainText
Json
Redirect
```

ChurchReport converts the acknowledgement descriptor into an ASP.NET response.
ChurchReport still decides product-specific success/failure pages and post-
payment workflow, but it does not decide provider protocol acknowledgements.

## Configuration

The core supports multiple named merchant profiles. Profile names are chosen by
the host product and supplied in payment requests.

Recommended configuration shape:

```json
{
  "Payment": {
    "DefaultProfile": "JesusTest",
    "Profiles": {
      "JesusTest": {
        "Provider": "Sinopac",
        "Environment": "Sandbox",
        "Credentials": {
          "ShopNo": "NA0149_001",
          "A1": "...",
          "A2": "...",
          "B1": "...",
          "B2": "...",
          "XKeyId": "..."
        },
        "Endpoints": {
          "ApiBaseUrl": "https://sandbox.sinopac.com/QPay.WebAPI/api/"
        }
      },
      "MyPayProduction": {
        "Provider": "MyPay",
        "Environment": "Production",
        "Credentials": {
          "StoreId": "...",
          "Key": "...",
          "IV": "..."
        },
        "Endpoints": {
          "ApiBaseUrl": "https://ka.mypay.tw/api/init"
        }
      },
      "TaishinSandbox": {
        "Provider": "Taishin",
        "Environment": "Sandbox",
        "Credentials": {
          "StoreId": "...",
          "StoreKey": "...",
          "StoreIV": "...",
          "TerminalId": "..."
        },
        "Endpoints": {
          "ApiBaseUrl": "https://tspg-t.taishinbank.com.tw/tspgapi/restapi"
        }
      }
    }
  }
}
```

Callback URLs should be provided by the product in `PaymentCreateRequest`, not
hard-wired into the reusable core profile. This avoids binding merchant profiles
to a specific ChurchReport deployment URL.

## HTTP Client Strategy

The core must not create unmanaged `HttpClient` instances per call. It should use
DI-managed HTTP clients supplied by the host.

Recommended approach:

- `SpeechMessage.Payments` registers provider typed or named clients.
- Host applications call a registration extension such as
  `services.AddSpeechMessagePayments(configuration.GetSection("Payment"))`.
- Tests can replace provider HTTP calls with mock handlers or fake clients.

This keeps connection lifetime management in the host while avoiding socket
exhaustion and improving testability.

## Status And Error Handling

The core distinguishes provider responses from product workflow failures.

Recommended normalized statuses:

```text
Pending
Succeeded
Failed
Cancelled
Unknown
```

Recommended error kinds:

```text
None
ConfigurationInvalid
RequestInvalid
ProviderRejected
ProviderUnavailable
SignatureInvalid
CallbackInvalid
NetworkFailure
SerializationFailure
UnsupportedOperation
Unexpected
```

Provider declines are not necessarily exceptions. They should usually return a
normalized failed or pending result with provider code/message preserved.

Exceptions are reserved for unrecoverable implementation problems or host
misconfiguration that cannot be represented as a payment result.

Each provider owns its own status mapper. ChurchReport should not parse provider
status codes after migration.

## Sanitization Policy

The core may expose sanitized provider diagnostics for audit/debugging, but it
must not expose raw secrets or sensitive payment data.

Allowed in normalized fields or sanitized diagnostics:

- provider response code
- provider response message
- product order id
- provider transaction id
- masked card or bank fields when provider documentation permits display
- payment amount and currency
- callback received timestamp when available

Must be masked or omitted:

- full PayToken or equivalent sensitive tokens
- hash/signature values
- StoreKey, StoreIV, A1, A2, B1, B2, XKey, API keys, and secrets
- full card number
- CVV/CVC
- full personal identifiers beyond what the product explicitly sent and already
  owns
- raw provider model instances that include sensitive data

The public result should expose sanitized data as a dictionary or normalized
fields, not as full provider raw models.

## ChurchReport Adapter Boundary

ChurchReport keeps current callback routes during migration:

- Read ASP.NET request data.
- Build provider-neutral payment DTOs.
- Call `IPaymentGateway`.
- Execute ChurchReport-specific workflow.
- Convert acknowledgement descriptors and product decisions to HTTP responses.

ChurchReport remains responsible for:

- Dynamics CRM fee updates
- LINE notifications
- donation/fee classification
- callback deduplication and idempotency
- application audit records
- success/failure views and redirects

ChurchReport must not keep provider-specific:

- signing/encryption/decryption code
- provider request model construction
- provider callback payload parsing
- provider status code interpretation
- provider SDK/toolkit implementation

## Special Scope Decisions

`QPayLoginController` is not part of the payment core if it is product login or
binding flow. If it contains provider credential/token exchange logic required by
Sinopac payment calls, that exchange logic belongs in the payment core while the
HTTP login/controller flow remains in ChurchReport.

QPay ATM/card callback parsing is included only to the extent required by the
first-release payment minimum closed loop. QPay bill, allotment, capture, void,
and maintenance operations are not included.

Line Pay remains untouched in the first release. The implementation plan must
include a non-interference check for existing Line Pay paths.

## Migration Strategy

Use staged migration through thin adapters.

Recommended sequence:

1. Add `SpeechMessage.Payments` and test project shells.
2. Add provider-neutral abstractions, models, options, sanitizer, status mapper
   scaffolding, and DI registration.
3. Migrate MyPay first because it is the most isolated provider flow.
4. Migrate TSPG second because its controller is large and has multiple callback
   shapes.
5. Migrate Sinopac/QPay last because the current `IPayment` and `QPayProcessor`
   are deeply QPay-shaped.
6. After each provider migration, run boundary searches to verify provider-
   specific implementation did not remain in ChurchReport.
7. Keep ChurchReport routes stable throughout migration.

Temporary compatibility adapters are allowed only to preserve existing
ChurchReport behavior. They must be removed or reduced once ChurchReport calls
the provider-neutral core directly.

## Testing Strategy

Create a test project such as `SpeechMessage.Payments.Tests`.

Core tests:

- options binding and validation for multiple named profiles
- provider selection by profile name
- provider-specific status code mapping to normalized statuses
- sensitive data sanitization
- callback parsing for query, form, JSON, and mixed payloads
- invalid signature and invalid payload behavior
- provider API timeout and malformed response behavior

Provider contract tests:

- use provider sample fixtures for Sinopac/QPay, MyPay, and Taishin/TSPG
- avoid real production secrets and real card data
- assert provider-specific payloads map to neutral DTOs
- assert provider acknowledgement descriptors are correct

ChurchReport adapter tests:

- existing routes still accept expected callback request shapes
- controllers call `IPaymentGateway.ParseCallbackAsync`
- controllers do not parse provider signature/status directly
- normalized success triggers existing CRM/LINE workflow
- normalized failure does not incorrectly mark CRM payment as successful
- duplicate callback handling remains product-owned

Boundary verification:

- `SpeechMessage.Payments` must not reference ChurchReport, ToolUtility,
  Line.Messaging, ASP.NET MVC, or CRM SDK types.
- ChurchReport must not contain provider SDK, signing, encryption, callback
  parsing, or provider status mapping implementation after each migrated
  provider.
- QPay-specific public model dependencies must be removed from ChurchReport
  consumers when those consumers migrate to the neutral contract.

## Rollout And Rollback

Roll out by provider, not by rewriting all payment paths at once.

Each provider migration should preserve existing callback URLs and user-facing
behavior. If a migrated provider fails validation, rollback is to point the thin
adapter back to the pre-migration implementation for that provider while leaving
other provider migrations untouched.

No provider-specific code should be deleted before tests and boundary searches
confirm the new provider implementation covers the current behavior.

## Risks And Mitigations

- Current `IPayment` is QPay-shaped.
  Mitigation: introduce neutral contract and migrate consumers away from QPay
  DTOs.

- Callback HTTP shapes differ across providers.
  Mitigation: neutral callback request DTO plus provider-owned parsers and
  acknowledgement descriptor.

- Sanitized raw data may accidentally expose sensitive fields.
  Mitigation: explicit redaction policy plus sanitizer tests.

- Multi-profile configuration is a new capability.
  Mitigation: define schema before implementation and cover binding/validation
  with tests.

- TSPG controller is large and mixes payment, CRM, LINE, and response handling.
  Mitigation: migrate after MyPay and split provider parsing from product
  workflow deliberately.

- Line Pay is out of scope but shares payment terminology.
  Mitigation: non-interference checks before and after migration.

## Approval State

The user approved:

- solution-local independent project
- pure reusable payment core
- no ASP.NET controllers in the core
- provider-neutral DTOs and interfaces
- first release limited to payment minimum closed loop
- multiple named merchant profiles
- stateless core
- sanitized provider diagnostics
- thin ChurchReport adapters
- `net10.0`
- Line Pay out of scope
- staged migration with contract refinements above
