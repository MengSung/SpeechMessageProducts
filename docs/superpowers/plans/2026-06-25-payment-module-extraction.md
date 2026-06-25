# Payment Module Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a reusable `SpeechMessage.Payments` payment core and move Sinopac/QPay, MyPay, and Taishin/TSPG provider implementation out of ChurchReport while preserving ChurchReport routes and product workflows.

**Architecture:** Add a pure `net10.0` class library named `SpeechMessage.Payments` plus a focused test project. The new core owns provider protocol details, normalized contracts, options, diagnostics sanitization, provider routing, and provider implementations; ChurchReport keeps only ASP.NET route binding, CRM/LINE/product workflow, callback idempotency, and result pages. Migrate by provider in this order: MyPay, Taishin/TSPG, then Sinopac/QPay.

**Tech Stack:** .NET 10, C#, ASP.NET Core host adapters in ChurchReport, Microsoft.Extensions.DependencyInjection/Options/Logging/Http, Newtonsoft.Json for provider JSON compatibility, xUnit, FluentAssertions.

## Global Constraints

- The reusable payment project name is `SpeechMessage.Payments`.
- The reusable payment project targets `net10.0`.
- The reusable payment core must not include ASP.NET controllers, MVC views, `HttpRequest`, ChurchReport CRM, LINE messaging, ToolUtility, Dataverse SDK, database persistence, or product-specific workflows.
- ChurchReport may keep existing callback routes as thin adapters.
- The public payment contract must be provider-neutral and must not expose the current QPay-shaped `IPayment`, `CreOrder`, `QryOrderPay`, or provider SDK models as the main API.
- First release public operations are create payment, query payment status, parse and verify callbacks, select/configure providers, and normalize statuses/errors.
- Refund, capture, void, daily bill query, allotment query, and payment back-office UI are out of the first public contract.
- The core supports multiple named merchant profiles from JSON configuration.
- Callback URLs are supplied in `PaymentCreateRequest`, not hard-wired into reusable merchant profiles.
- The core is stateless: it does not store orders, update product records, deduplicate callbacks, or decide product idempotency.
- Provider diagnostics must be sanitized. Full tokens, signatures, keys, secrets, full card numbers, CVV/CVC, and sensitive personal identifiers must be masked or omitted.
- `LinePayCSharp` is out of scope and must remain untouched.
- Current Codex mode is inline. Do not dispatch implementation or check sub-agents in this session; use inline execution after user approval.

---

## File Structure

Create these projects:

- `SpeechMessage.Payments/SpeechMessage.Payments.csproj` - pure reusable payment core.
- `SpeechMessage.Payments.Tests/SpeechMessage.Payments.Tests.csproj` - xUnit tests for core contracts and provider behavior.

Create these core files:

- `SpeechMessage.Payments/Abstractions/IPaymentGateway.cs` - public provider-neutral host entry point.
- `SpeechMessage.Payments/Abstractions/IPaymentProvider.cs` - internal provider contract used by the gateway.
- `SpeechMessage.Payments/Abstractions/IPaymentProfileResolver.cs` - named profile resolver.
- `SpeechMessage.Payments/Models/*.cs` - request, result, status, error, callback, acknowledgement, customer, and line-item models.
- `SpeechMessage.Payments/Configuration/*.cs` - `PaymentOptions`, `PaymentMerchantProfile`, options validator, resolver, and configuration exception.
- `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs` - masks sensitive provider data.
- `SpeechMessage.Payments/Gateway/PaymentGateway.cs` - routes requests to the configured provider.
- `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs` - `AddSpeechMessagePayments`.
- `SpeechMessage.Payments/Providers/Common/*.cs` - shared dictionary, callback, HTTP, and status helpers.
- `SpeechMessage.Payments/Providers/MyPay/*.cs` - MyPay request mapping, status mapping, callback parsing, signature/hash verification, models, and provider.
- `SpeechMessage.Payments/Providers/Taishin/*.cs` - Taishin/TSPG request mapping, status mapping, callback parsing, hash verification, models, and provider.
- `SpeechMessage.Payments/Providers/Sinopac/*.cs` - Sinopac/QPay request mapping, status mapping, signing/crypto, callback parsing, models, and provider.

Create these ChurchReport adapter files:

- `ChurchReport/Payments/PaymentHttpRequestMapper.cs` - converts ASP.NET requests into `PaymentCallbackRequest`.
- `ChurchReport/Payments/PaymentAcknowledgementResultMapper.cs` - converts `PaymentCallbackAcknowledgement` into `IActionResult`.
- `ChurchReport/Payments/ChurchReportPaymentProfileResolver.cs` - maps legacy provider settings to named payment profiles during migration.
- `ChurchReport/Payments/PaymentCreateRequestFactory.cs` - builds neutral create-payment DTOs from ChurchReport product data.
- `ChurchReport/Payments/PaymentWorkflowResultMapper.cs` - maps neutral payment results into ChurchReport product workflow inputs.

Move or delete provider implementation from ChurchReport after each replacement is covered by tests:

- `ChurchReport/Tools/IPayment.cs`
- `ChurchReport/Tools/IQPayToolkit.cs`
- `ChurchReport/Tools/QPayToolkit.cs`
- `ChurchReport/Tools/QPayToolkitWrapper.cs`
- `ChurchReport/Tools/MyPayToolkit.cs`
- `ChurchReport/Tools/MyPayToolkitWrapper.cs`
- `ChurchReport/Tools/TspgToolkit.cs`
- `ChurchReport/Tools/TspgToolkitWrapper.cs`
- `ChurchReport/Tools/TSPGModels.cs`
- `ChurchReport/Tools/TSPGStandardModels.cs`
- `ChurchReport/Tools/TSPGStoreOrder.cs`
- `ChurchReport/Tools/TSPGWebhookHandler.cs`
- `ChurchReport/Tools/QPayWebhook.cs`
- provider-specific parts of `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.PaymentGateway.cs`

Keep outside the payment core:

- ChurchReport route actions, CRM updates, LINE notifications, fee/dedication classification, duplicate callback policy, audit records, and result views.
- `LinePayCSharp/**`.

---

## Public Interfaces

Create this public host contract:

```csharp
namespace SpeechMessage.Payments.Abstractions;

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

Create this internal provider contract:

```csharp
namespace SpeechMessage.Payments.Abstractions;

internal interface IPaymentProvider
{
    PaymentProviderKind ProviderKind { get; }

    Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request,
        CancellationToken cancellationToken);

    Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentMerchantProfile profile,
        PaymentQueryRequest request,
        CancellationToken cancellationToken);

    Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentMerchantProfile profile,
        PaymentCallbackRequest request,
        CancellationToken cancellationToken);
}
```

Result rules:

- Provider declines return normalized failed or pending results with `PaymentErrorKind.ProviderRejected`.
- Missing profile, invalid profile, unsupported provider, invalid callback signature, malformed payload, serialization failure, and network failure map to explicit `PaymentErrorKind` values.
- `ProviderData` and `Diagnostics` dictionaries are sanitized before leaving provider classes.

---

### Task 1: Create Core And Test Project Shells

**Files:**
- Create: `SpeechMessage.Payments/SpeechMessage.Payments.csproj`
- Create: `SpeechMessage.Payments.Tests/SpeechMessage.Payments.Tests.csproj`
- Modify: `ChurchReport.sln`

**Interfaces:**
- Consumes: no earlier task output.
- Produces: buildable project shells in the solution.

- [ ] **Step 1: Create projects and add references**

Run:

```powershell
dotnet new classlib -n SpeechMessage.Payments -f net10.0
dotnet new xunit -n SpeechMessage.Payments.Tests -f net10.0
dotnet sln ChurchReport.sln add SpeechMessage.Payments\SpeechMessage.Payments.csproj
dotnet sln ChurchReport.sln add SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
dotnet add SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj reference SpeechMessage.Payments\SpeechMessage.Payments.csproj
```

Expected: each command exits with code `0`.

- [ ] **Step 2: Replace `SpeechMessage.Payments.csproj`**

Use:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>true</IsPackable>
    <AssemblyName>SpeechMessage.Payments</AssemblyName>
    <RootNamespace>SpeechMessage.Payments</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Replace `SpeechMessage.Payments.Tests.csproj`**

Use:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>NU1605</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\SpeechMessage.Payments\SpeechMessage.Payments.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Remove template source files**

Run:

```powershell
Remove-Item -LiteralPath SpeechMessage.Payments\Class1.cs
Remove-Item -LiteralPath SpeechMessage.Payments.Tests\UnitTest1.cs
```

- [ ] **Step 5: Build project shells**

Run:

```powershell
dotnet build SpeechMessage.Payments\SpeechMessage.Payments.csproj
dotnet build SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
```

Expected: both builds succeed.

- [ ] **Step 6: Commit**

Run:

```powershell
git add ChurchReport.sln SpeechMessage.Payments SpeechMessage.Payments.Tests
git commit -m "feat: add payment core project shells"
```

---

### Task 2: Add Provider-Neutral Models And Gateway Interfaces

**Files:**
- Create: `SpeechMessage.Payments/Abstractions/IPaymentGateway.cs`
- Create: `SpeechMessage.Payments/Abstractions/IPaymentProvider.cs`
- Create: `SpeechMessage.Payments/Models/PaymentProviderKind.cs`
- Create: `SpeechMessage.Payments/Models/PaymentEnvironment.cs`
- Create: `SpeechMessage.Payments/Models/PaymentStatus.cs`
- Create: `SpeechMessage.Payments/Models/PaymentErrorKind.cs`
- Create: `SpeechMessage.Payments/Models/PaymentAckKind.cs`
- Create: `SpeechMessage.Payments/Models/PaymentError.cs`
- Create: `SpeechMessage.Payments/Models/PaymentCallbackAcknowledgement.cs`
- Create: `SpeechMessage.Payments/Models/PaymentCreateRequest.cs`
- Create: `SpeechMessage.Payments/Models/PaymentCreateResult.cs`
- Create: `SpeechMessage.Payments/Models/PaymentQueryRequest.cs`
- Create: `SpeechMessage.Payments/Models/PaymentStatusResult.cs`
- Create: `SpeechMessage.Payments/Models/PaymentCallbackRequest.cs`
- Create: `SpeechMessage.Payments/Models/PaymentCallbackResult.cs`
- Create: `SpeechMessage.Payments/Models/PaymentCustomer.cs`
- Create: `SpeechMessage.Payments/Models/PaymentLineItem.cs`
- Create: `SpeechMessage.Payments/Models/PaymentCallbacks.cs`
- Test: `SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs`

**Interfaces:**
- Consumes: Task 1 project shells.
- Produces: provider-neutral DTOs and gateway interfaces.

- [ ] **Step 1: Write failing model contract tests**

Create `SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs`:

```csharp
using FluentAssertions;
using SpeechMessage.Payments.Models;
using Xunit;

namespace SpeechMessage.Payments.Tests.Models;

public sealed class PaymentModelContractTests
{
    [Fact]
    public void Create_request_carries_profile_order_amount_and_callbacks()
    {
        var request = new PaymentCreateRequest
        {
            ProfileName = "JesusTest",
            ProductOrderId = "F202606250001",
            Amount = 1200m,
            Currency = "TWD",
            Description = "Fee payment",
            PaymentMethod = "CreditCard",
            Callbacks = new PaymentCallbacks
            {
                ReturnUrl = "https://example.test/return",
                BackendUrl = "https://example.test/backend",
                SuccessUrl = "https://example.test/success",
                FailureUrl = "https://example.test/failure"
            }
        };

        request.ProfileName.Should().Be("JesusTest");
        request.ProductOrderId.Should().Be("F202606250001");
        request.Amount.Should().Be(1200m);
        request.Callbacks.BackendUrl.Should().EndWith("/backend");
    }

    [Fact]
    public void Callback_request_is_http_neutral()
    {
        var request = new PaymentCallbackRequest
        {
            ProfileName = "MyPayProduction",
            ProviderHint = PaymentProviderKind.MyPay,
            HttpMethod = "POST",
            ContentType = "application/x-www-form-urlencoded",
            RawBody = "order_id=F1&prc=250",
            Query = new Dictionary<string, string>(),
            Form = new Dictionary<string, string> { ["order_id"] = "F1", ["prc"] = "250" },
            Headers = new Dictionary<string, string> { ["User-Agent"] = "provider" }
        };

        request.Form["prc"].Should().Be("250");
        typeof(PaymentCallbackRequest)
            .GetProperties()
            .Select(property => property.PropertyType.FullName ?? property.PropertyType.Name)
            .Should()
            .NotContain(name => name.Contains("HttpRequest", StringComparison.Ordinal));
    }

    [Fact]
    public void Acknowledgement_can_describe_provider_response_shape()
    {
        PaymentCallbackAcknowledgement.PlainText("8888").Kind.Should().Be(PaymentAckKind.PlainText);
        PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}").Kind.Should().Be(PaymentAckKind.Json);
        PaymentCallbackAcknowledgement.Redirect("https://example.test/success").Kind.Should().Be(PaymentAckKind.Redirect);
        PaymentCallbackAcknowledgement.None.Kind.Should().Be(PaymentAckKind.None);
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter PaymentModelContractTests
```

Expected: compile fails because the model types do not exist.

- [ ] **Step 3: Add enums**

Create the enum files with these exact values:

```csharp
public enum PaymentProviderKind { Unknown = 0, Sinopac = 1, MyPay = 2, Taishin = 3 }
public enum PaymentEnvironment { Sandbox = 0, Production = 1 }
public enum PaymentStatus { Unknown = 0, Pending = 1, Succeeded = 2, Failed = 3, Cancelled = 4 }
public enum PaymentErrorKind { None = 0, ConfigurationInvalid = 1, RequestInvalid = 2, ProviderRejected = 3, ProviderUnavailable = 4, SignatureInvalid = 5, CallbackInvalid = 6, NetworkFailure = 7, SerializationFailure = 8, UnsupportedOperation = 9, Unexpected = 10 }
public enum PaymentAckKind { None = 0, PlainText = 1, Json = 2, Redirect = 3 }
```

Each file uses namespace `SpeechMessage.Payments.Models`.

- [ ] **Step 4: Add request/result records**

Create the model records using the property names from the tests and the public design spec:

```csharp
public sealed record PaymentCreateRequest
{
    public string ProfileName { get; init; } = string.Empty;
    public PaymentProviderKind? ProviderHint { get; init; }
    public string ProductOrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string Description { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string PaymentMethodSubType { get; init; } = string.Empty;
    public PaymentCallbacks Callbacks { get; init; } = new();
    public PaymentCustomer Customer { get; init; } = new();
    public IReadOnlyList<PaymentLineItem> Items { get; init; } = Array.Empty<PaymentLineItem>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
```

Use equivalent records for `PaymentCreateResult`, `PaymentQueryRequest`, `PaymentStatusResult`, `PaymentCallbackRequest`, and `PaymentCallbackResult`. All result records include `PaymentError Error`, sanitized `ProviderData`, and sanitized `Diagnostics`.

- [ ] **Step 5: Add acknowledgement and error helpers**

`PaymentCallbackAcknowledgement` exposes:

```csharp
public static PaymentCallbackAcknowledgement None { get; }
public static PaymentCallbackAcknowledgement PlainText(string content, int statusCode = 200)
public static PaymentCallbackAcknowledgement Json(string content, int statusCode = 200)
public static PaymentCallbackAcknowledgement Redirect(string url, int statusCode = 302)
```

`PaymentError` exposes `PaymentError.None` and `bool HasError`.

- [ ] **Step 6: Add gateway interfaces**

Create `IPaymentGateway` and internal `IPaymentProvider` with the signatures in the Public Interfaces section.

- [ ] **Step 7: Run tests**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter PaymentModelContractTests
```

Expected: PASS.

- [ ] **Step 8: Commit**

Run:

```powershell
git add SpeechMessage.Payments SpeechMessage.Payments.Tests
git commit -m "feat: add payment core contract"
```

---

### Task 3: Add Options, Sanitizer, Gateway Router, And DI

**Files:**
- Create: `SpeechMessage.Payments/Properties/AssemblyInfo.cs`
- Create: `SpeechMessage.Payments/Configuration/PaymentOptions.cs`
- Create: `SpeechMessage.Payments/Configuration/PaymentMerchantProfile.cs`
- Create: `SpeechMessage.Payments/Configuration/PaymentConfigurationException.cs`
- Create: `SpeechMessage.Payments/Configuration/PaymentOptionsValidator.cs`
- Create: `SpeechMessage.Payments/Abstractions/IPaymentProfileResolver.cs`
- Create: `SpeechMessage.Payments/Configuration/OptionsPaymentProfileResolver.cs`
- Create: `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs`
- Create: `SpeechMessage.Payments/Gateway/PaymentGateway.cs`
- Create: `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs`
- Create: `SpeechMessage.Payments/Providers/Common/DictionaryExtensions.cs`
- Test: `SpeechMessage.Payments.Tests/Configuration/PaymentOptionsTests.cs`
- Test: `SpeechMessage.Payments.Tests/Diagnostics/PaymentDiagnosticsSanitizerTests.cs`
- Test: `SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs`

**Interfaces:**
- Consumes: Task 2 models and gateway interfaces.
- Produces: `AddSpeechMessagePayments(IConfiguration paymentSection)`, named profile resolution, redaction, and provider routing.

- [ ] **Step 1: Write options binding tests**

The test binds JSON with two profiles:

```csharp
var json = """
{
  "Payment": {
    "DefaultProfile": "JesusTest",
    "Profiles": {
      "JesusTest": {
        "Provider": "Sinopac",
        "Environment": "Sandbox",
        "Credentials": { "ShopNo": "NA0149_001", "A1": "a", "A2": "b", "B1": "c", "B2": "d", "XKeyId": "x" },
        "Endpoints": { "ApiBaseUrl": "https://sandbox.sinopac.test/api/" }
      },
      "MyPayProduction": {
        "Provider": "MyPay",
        "Environment": "Production",
        "Credentials": { "StoreId": "130544850001", "Key": "key", "IV": "iv" },
        "Endpoints": { "ApiBaseUrl": "https://ka.mypay.test/api/init" }
      }
    }
  }
}
""";
var configuration = new ConfigurationBuilder()
    .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
    .Build();
var options = new PaymentOptions();
configuration.GetSection("Payment").Bind(options);

options.DefaultProfile.Should().Be("JesusTest");
options.Profiles["MyPayProduction"].Provider.Should().Be(PaymentProviderKind.MyPay);
```

Also assert `OptionsPaymentProfileResolver.Resolve("")` uses the default profile and throws `PaymentConfigurationException` when no profile can be resolved.

- [ ] **Step 2: Write sanitizer tests**

Assert:

```csharp
var input = new Dictionary<string, string>
{
    ["PayToken"] = "1234567890abcdef",
    ["signature"] = "ABCDEF123456",
    ["StoreKey"] = "secret-key",
    ["cardno"] = "4111111111111111",
    ["ret_code"] = "00",
    ["order_no"] = "F202606250001"
};

var sanitized = PaymentDiagnosticsSanitizer.Sanitize(input);

sanitized["PayToken"].Should().Be("1234...cdef");
sanitized["signature"].Should().Be("***");
sanitized["StoreKey"].Should().Be("***");
sanitized["cardno"].Should().Be("411111******1111");
sanitized["ret_code"].Should().Be("00");
sanitized["order_no"].Should().Be("F202606250001");
```

- [ ] **Step 3: Write gateway routing tests**

Inside `PaymentGatewayTests`, define a fake provider class implementing `IPaymentProvider`. Assert:

- empty request profile uses `PaymentOptions.DefaultProfile`
- provider hint mismatch returns `PaymentErrorKind.ConfigurationInvalid`
- unsupported provider returns `PaymentErrorKind.UnsupportedOperation`
- routed provider receives the resolved profile name

- [ ] **Step 4: Run tests and verify they fail**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter "PaymentOptionsTests|PaymentDiagnosticsSanitizerTests|PaymentGatewayTests"
```

Expected: compile fails because infrastructure types do not exist.

- [ ] **Step 5: Implement options and resolver**

`PaymentOptions`:

```csharp
public sealed class PaymentOptions
{
    public string DefaultProfile { get; set; } = string.Empty;
    public Dictionary<string, PaymentMerchantProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
```

`PaymentMerchantProfile`:

```csharp
public sealed class PaymentMerchantProfile
{
    public string Name { get; set; } = string.Empty;
    public PaymentProviderKind Provider { get; set; } = PaymentProviderKind.Unknown;
    public PaymentEnvironment Environment { get; set; } = PaymentEnvironment.Sandbox;
    public Dictionary<string, string> Credentials { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Endpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
```

When a profile is resolved, set `PaymentMerchantProfile.Name` to the dictionary key.

- [ ] **Step 6: Implement sanitizer and gateway**

Sanitizer rules:

- token-like values longer than eight characters become first four, ellipsis, last four
- signature, hash, key, secret, IV, A1, A2, B1, B2, XKey, and CVV values become `***`
- 13 to 19 digit card values become first six, six asterisks, last four
- provider response code/message, order id, provider transaction id, amount, and currency are preserved

Gateway rules:

- resolve request profile or default profile
- verify provider hint when present
- select the matching `IPaymentProvider`
- return normalized errors instead of throwing for request/profile/provider selection problems

- [ ] **Step 7: Implement DI**

Create:

```csharp
public static IServiceCollection AddSpeechMessagePayments(
    this IServiceCollection services,
    IConfiguration paymentSection)
```

Register options, validator, profile resolver, gateway, named HTTP clients, and provider registrations as each provider task adds them.

- [ ] **Step 8: Run tests**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
```

Expected: PASS.

- [ ] **Step 9: Commit**

Run:

```powershell
git add SpeechMessage.Payments SpeechMessage.Payments.Tests
git commit -m "feat: add payment gateway infrastructure"
```

---

### Task 4: Add ChurchReport Thin Adapter Infrastructure

**Files:**
- Modify: `ChurchReport/ChurchReport.csproj`
- Modify: `ChurchReport/Startup.cs`
- Modify: `ChurchReport/appsettings.json`
- Create: `ChurchReport/Payments/PaymentHttpRequestMapper.cs`
- Create: `ChurchReport/Payments/PaymentAcknowledgementResultMapper.cs`
- Create: `ChurchReport/Payments/ChurchReportPaymentProfileResolver.cs`
- Create: `ChurchReport/Payments/PaymentCreateRequestFactory.cs`
- Create: `ChurchReport/Payments/PaymentWorkflowResultMapper.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/PaymentWorkflowResultMapperTests.cs`

**Interfaces:**
- Consumes: `SpeechMessage.Payments` DTOs and DI extension.
- Produces: host-side ASP.NET adapters that keep `HttpRequest` out of the core.

- [ ] **Step 1: Add ChurchReport project reference**

Run:

```powershell
dotnet add ChurchReport\ChurchReport.csproj reference SpeechMessage.Payments\SpeechMessage.Payments.csproj
```

Expected: exit code `0`.

- [ ] **Step 2: Write adapter tests**

Test:

- GET query maps to `PaymentCallbackRequest.Query`.
- POST form maps to `PaymentCallbackRequest.Form`.
- POST JSON maps raw body and content type.
- `PlainText("8888")` maps to `ContentResult` with `text/plain`.
- JSON acknowledgement maps to `application/json`.
- Redirect acknowledgement maps to `RedirectResult`.
- `None` maps to status code `200`.

- [ ] **Step 3: Run tests and verify they fail**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "PaymentHttpRequestMapperTests|PaymentAcknowledgementResultMapperTests|PaymentWorkflowResultMapperTests"
```

Expected: compile fails because adapter types do not exist.

- [ ] **Step 4: Implement adapters**

Implementation rules:

- `PaymentHttpRequestMapper` reads body once and resets stream position when seekable.
- dictionaries flatten multi-value query/form/header values with comma joins.
- adapters do not verify signatures, parse provider status, or reference provider SDK models.
- `PaymentAcknowledgementResultMapper` switches only on `PaymentAckKind`.
- `PaymentWorkflowResultMapper` reads neutral result fields only.

- [ ] **Step 5: Add `Payment` configuration**

Add `Payment:DefaultProfile` and `Payment:Profiles` to `ChurchReport/appsettings.json`, mirroring existing Sinopac, MyPay, and TSPG values. Keep legacy provider sections until the staged migrations remove their code paths.

- [ ] **Step 6: Register services**

In `Startup.cs`:

```csharp
services.AddSpeechMessagePayments(Configuration.GetSection("Payment"));
services.AddScoped<PaymentHttpRequestMapper>();
services.AddScoped<PaymentAcknowledgementResultMapper>();
services.AddScoped<ChurchReportPaymentProfileResolver>();
services.AddScoped<PaymentCreateRequestFactory>();
services.AddScoped<PaymentWorkflowResultMapper>();
```

Leave legacy `IPayment` registration until each provider migration removes its dependence.

- [ ] **Step 7: Run tests and build**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "PaymentHttpRequestMapperTests|PaymentAcknowledgementResultMapperTests|PaymentWorkflowResultMapperTests"
dotnet build ChurchReport\ChurchReport.csproj
```

Expected: PASS and build succeeds.

- [ ] **Step 8: Commit**

Run:

```powershell
git add ChurchReport\ChurchReport.csproj ChurchReport\Startup.cs ChurchReport\appsettings.json ChurchReport\Payments ChurchReport.MemberInfo.Tests\Payments
git commit -m "feat: add church payment adapters"
```

---

### Task 5: Migrate MyPay Core Provider

**Files:**
- Create: `SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs`
- Create: `SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs`
- Create: `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs`
- Create: `SpeechMessage.Payments/Providers/MyPay/MyPayStatusMapper.cs`
- Create: `SpeechMessage.Payments/Providers/MyPay/MyPayModels.cs`
- Create: `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs`
- Modify: `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs`
- Move provider model logic from: `ChurchReport/Models/MyPayReturnModel.cs`
- Move provider toolkit logic from: `ChurchReport/Tools/MyPayToolkit.cs`
- Move provider wrapper logic from: `ChurchReport/Tools/MyPayToolkitWrapper.cs`
- Test: `SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs`
- Test fixtures: `SpeechMessage.Payments.Tests/Fixtures/MyPay/*.json`

**Interfaces:**
- Consumes: `IPaymentProvider`, `PaymentMerchantProfile`, neutral DTOs.
- Produces: MyPay implementation for create, query, and callback parse.

- [ ] **Step 1: Write MyPay tests**

Assert:

- PRC `250`, `290`, and `600` map to `PaymentStatus.Succeeded`.
- PRC `300`, `400`, `260`, `270`, and `280` map to failed or pending following current `MyPayStatusHelper` behavior.
- valid callback returns `PaymentCallbackAcknowledgement.PlainText("8888")`.
- invalid callback returns `PaymentErrorKind.CallbackInvalid` and still returns plain text `8888` when the existing route would acknowledge.
- create request maps product order id, amount, callback URLs, and customer fields.
- full key, IV, token, signature, and card data do not appear in public result dictionaries.

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter MyPay
```

Expected: compile fails because MyPay provider types do not exist.

- [ ] **Step 3: Implement MyPay provider**

Move provider-only models and parsing from ChurchReport into the core. Keep provider models `internal`. Use DI-managed `HttpClient`; map HTTP failures to `PaymentErrorKind.NetworkFailure` and malformed provider responses to `PaymentErrorKind.SerializationFailure`.

- [ ] **Step 4: Register MyPay provider**

Register `MyPayPaymentProvider` as an `IPaymentProvider` without replacing other providers.

- [ ] **Step 5: Run tests**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter MyPay
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add SpeechMessage.Payments SpeechMessage.Payments.Tests
git commit -m "feat: migrate mypay provider core"
```

---

### Task 6: Convert MyPay ChurchReport Route To Thin Adapter

**Files:**
- Modify: `ChurchReport/Controllers/MyPayController.cs`
- Modify: `ChurchReport/Services/MyPayCrmService.cs`
- Modify: `ChurchReport/Services/MyPayMessageBuilder.cs`
- Modify: `ChurchReport/Services/MyPayNotificationService.cs`
- Modify: `ChurchReport/Services/MyPayLogger.cs`
- Modify: `ChurchReport/Services/MyPayStatusHelper.cs`
- Delete after replacement: `ChurchReport/Models/MyPayReturnModel.cs`
- Delete after replacement: `ChurchReport/Tools/MyPayToolkit.cs`
- Delete after replacement: `ChurchReport/Tools/MyPayToolkitWrapper.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/MyPayControllerAdapterTests.cs`

**Interfaces:**
- Consumes: `IPaymentGateway.ParseCallbackAsync`, `PaymentHttpRequestMapper`, `PaymentCallbackResult`.
- Produces: MyPay route that no longer parses provider fields or status codes in ChurchReport.

- [ ] **Step 1: Write controller adapter tests**

Test:

- `PaymentNotify` calls `IPaymentGateway.ParseCallbackAsync` once.
- `PaymentNotify` returns the acknowledgement from `PaymentCallbackResult.Acknowledgement`.
- success result calls existing CRM/LINE workflow.
- failed result does not mark CRM payment successful.
- controller code does not call `ValidateAllFields`, `IsSuccessfulPaymentStatus`, or inspect `prc`.

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter MyPayControllerAdapterTests
```

Expected: FAIL because the controller still binds `MyPayReturnModel` and parses MyPay fields directly.

- [ ] **Step 3: Refactor controller and workflow services**

Change `PaymentNotify` to accept no bound provider model. Build `PaymentCallbackRequest` from `PaymentHttpRequestMapper`, call `IPaymentGateway.ParseCallbackAsync`, then pass `PaymentCallbackResult` into ChurchReport workflow services. Services read order id, transaction id, status, amount, currency, and sanitized provider message from neutral result fields.

- [ ] **Step 4: Remove MyPay provider code from ChurchReport**

Delete the MyPay model/toolkit files after tests and build pass.

- [ ] **Step 5: Run boundary search**

Run:

```powershell
rg -n "MyPayReturnModel|ValidateAllFields|IsSuccessfulPaymentStatus|MyPayToolkit|MyPayToolkitWrapper|prc" ChurchReport --glob "*.cs"
```

Expected: no compiled ChurchReport code parses MyPay provider status or references MyPay toolkit/model classes.

- [ ] **Step 6: Run tests and build**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter MyPayControllerAdapterTests
dotnet build ChurchReport\ChurchReport.csproj
```

Expected: PASS and build succeeds.

- [ ] **Step 7: Commit**

Run:

```powershell
git add ChurchReport\Controllers\MyPayController.cs ChurchReport\Services ChurchReport.MemberInfo.Tests\Payments
git add -u ChurchReport\Models\MyPayReturnModel.cs ChurchReport\Tools\MyPayToolkit.cs ChurchReport\Tools\MyPayToolkitWrapper.cs
git commit -m "refactor: route mypay through payment core"
```

---

### Task 7: Migrate Taishin/TSPG Core Provider

**Files:**
- Create: `SpeechMessage.Payments/Providers/Taishin/TaishinPaymentProvider.cs`
- Create: `SpeechMessage.Payments/Providers/Taishin/TaishinRequestMapper.cs`
- Create: `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs`
- Create: `SpeechMessage.Payments/Providers/Taishin/TaishinStatusMapper.cs`
- Create: `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs`
- Create: `SpeechMessage.Payments/Providers/Taishin/TaishinModels.cs`
- Modify: `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs`
- Move provider logic from: `ChurchReport/Tools/TspgToolkit.cs`
- Move provider logic from: `ChurchReport/Tools/TspgToolkitWrapper.cs`
- Move provider logic from: `ChurchReport/Tools/TSPGModels.cs`
- Move provider logic from: `ChurchReport/Tools/TSPGStandardModels.cs`
- Move provider logic from: `ChurchReport/Tools/TSPGStoreOrder.cs`
- Move provider logic from: `ChurchReport/Tools/TSPGWebhookHandler.cs`
- Test: `SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs`
- Test fixtures: `SpeechMessage.Payments.Tests/Fixtures/Taishin/*.json`

**Interfaces:**
- Consumes: `IPaymentProvider`, `PaymentCallbackRequest`, neutral result DTOs.
- Produces: TSPG provider implementation with form, query, and JSON callback parsing.

- [ ] **Step 1: Write TSPG tests**

Test:

- frontend `post-back` query/form callback maps `ret_code=00` and `state=1` to `PaymentStatus.Succeeded`.
- backend `result-url` JSON callback maps nested `params.ret_code=00` to `PaymentStatus.Succeeded`.
- failed callback maps non-`00` `ret_code` to `PaymentStatus.Failed`.
- invalid hash maps to `PaymentErrorKind.SignatureInvalid`.
- backend acknowledgement is JSON.
- create/query mapping uses current cents/minor-unit behavior and request callback URLs.

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter Taishin
```

Expected: compile fails because Taishin provider types do not exist.

- [ ] **Step 3: Implement Taishin provider**

Move TSPG provider-only models and protocol code into `SpeechMessage.Payments/Providers/Taishin`. Parse callbacks only from `PaymentCallbackRequest.Query`, `Form`, `RawBody`, and `ContentType`. Use DI-managed `HttpClient`.

- [ ] **Step 4: Register provider and run tests**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter Taishin
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add SpeechMessage.Payments SpeechMessage.Payments.Tests
git commit -m "feat: migrate taishin payment core"
```

---

### Task 8: Convert TSPG ChurchReport Routes To Thin Adapters

**Files:**
- Modify: `ChurchReport/Controllers/TSPGController.cs`
- Delete after replacement: `ChurchReport/Tools/TSPGWebhookHandler.cs`
- Delete after replacement: `ChurchReport/Tools/TspgToolkit.cs`
- Delete after replacement: `ChurchReport/Tools/TspgToolkitWrapper.cs`
- Delete after replacement: `ChurchReport/Tools/TSPGModels.cs`
- Delete after replacement: `ChurchReport/Tools/TSPGStandardModels.cs`
- Delete after replacement: `ChurchReport/Tools/TSPGStoreOrder.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/TspgControllerAdapterTests.cs`

**Interfaces:**
- Consumes: Taishin provider core and ChurchReport adapter helpers.
- Produces: TSPG route actions that perform HTTP binding and product workflow only.

- [ ] **Step 1: Write route adapter tests**

Test:

- `post-back` calls `IPaymentGateway.ParseCallbackAsync`.
- `result-url` calls `IPaymentGateway.ParseCallbackAsync`.
- `result-url` returns the JSON acknowledgement from the core.
- controller no longer calls `TspgToolkit.OrderQuery`, `TspgToolkit.OrderCreate`, or local parse methods.
- success continues to call ChurchReport fee update workflow.
- failure does not mark the fee as paid.

- [ ] **Step 2: Refactor routes**

Replace local TSPG parse/status/hash logic with `PaymentHttpRequestMapper`, `IPaymentGateway.ParseCallbackAsync`, and `PaymentAcknowledgementResultMapper`. Keep product-specific redirects, CRM updates, and LINE notifications in ChurchReport.

- [ ] **Step 3: Delete moved provider files and run boundary search**

Run:

```powershell
rg -n "TspgToolkit|TSPGWebhookHandler|TSPGPaymentRequest|TSPGPaymentNotification|ret_code|auth_id_resp|StoreKey|StoreIV" ChurchReport --glob "*.cs"
```

Expected: no TSPG toolkit/model usage remains in compiled ChurchReport code. Provider status parsing belongs in `SpeechMessage.Payments`.

- [ ] **Step 4: Run tests, build, commit**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter TspgControllerAdapterTests
dotnet build ChurchReport\ChurchReport.csproj
git add ChurchReport\Controllers\TSPGController.cs ChurchReport.MemberInfo.Tests\Payments
git add -u ChurchReport\Tools\TSPGWebhookHandler.cs ChurchReport\Tools\TspgToolkit.cs ChurchReport\Tools\TspgToolkitWrapper.cs ChurchReport\Tools\TSPGModels.cs ChurchReport\Tools\TSPGStandardModels.cs ChurchReport\Tools\TSPGStoreOrder.cs
git commit -m "refactor: route taishin callbacks through payment core"
```

Expected: tests pass, build succeeds, commit succeeds.

---

### Task 9: Migrate Sinopac/QPay Core Provider

**Files:**
- Create: `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs`
- Create: `SpeechMessage.Payments/Providers/Sinopac/SinopacRequestMapper.cs`
- Create: `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs`
- Create: `SpeechMessage.Payments/Providers/Sinopac/SinopacStatusMapper.cs`
- Create: `SpeechMessage.Payments/Providers/Sinopac/SinopacSigner.cs`
- Create: `SpeechMessage.Payments/Providers/Sinopac/SinopacCrypto.cs`
- Create: `SpeechMessage.Payments/Providers/Sinopac/SinopacModels.cs`
- Modify: `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs`
- Move provider logic from: `ChurchReport/Tools/QPayToolkit.cs`
- Move provider logic from: `ChurchReport/Tools/QPayToolkitWrapper.cs`
- Move provider logic from: `ChurchReport/Tools/IQPayToolkit.cs`
- Move provider logic from: `ChurchReport/Tools/IPayment.cs`
- Test: `SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs`
- Test fixtures: `SpeechMessage.Payments.Tests/Fixtures/Sinopac/*.json`

**Interfaces:**
- Consumes: neutral contract and payment gateway infrastructure.
- Produces: Sinopac/QPay create, query, callback parse, signing, encryption, and status mapping.

- [ ] **Step 1: Write Sinopac tests**

Test:

- create payment maps product order id, amount, return URL, backend URL, and metadata into internal QPay request models.
- query maps `PaymentQueryRequest.PaymentToken` to QPay pay-token query.
- successful `QryOrderPay` fixture maps to `PaymentStatus.Succeeded`.
- declined or incomplete QPay fixture maps to failed or pending following current `QPayPaymentResultHelper`.
- invalid or missing `ShopNo`/`PayToken` maps to `PaymentErrorKind.CallbackInvalid`.
- full pay token, hash, A1/A2/B1/B2/XKey values do not leave the provider unsanitized.

- [ ] **Step 2: Implement Sinopac provider**

Move QPay protocol code into `SpeechMessage.Payments/Providers/Sinopac`. Keep QPay-shaped request and response classes internal. Do not expose `CreOrder`, `QryOrderPay`, or `QryOrderPayReq` in the public gateway contract.

- [ ] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter Sinopac
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
git add SpeechMessage.Payments SpeechMessage.Payments.Tests
git commit -m "feat: migrate sinopac payment core"
```

Expected: tests pass and commit succeeds.

---

### Task 10: Convert QPay ChurchReport Flow To Neutral Gateway

**Files:**
- Modify: `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.PaymentGateway.cs`
- Modify: `ChurchReport/Tools/QPayWebhook.cs`
- Modify: `ChurchReport/Controllers/QPayCardController.cs`
- Modify: `ChurchReport/Models/QpayManager.cs`
- Modify: `ChurchReport/Tools/QPayFeeProcessor.cs`
- Modify: `ChurchReport/Tools/QPayDedicationBookingProcessor.cs`
- Modify: `ChurchReport/Tools/QPayPaymentResultHelper.cs`
- Modify: `ChurchReport/Tools/QPayPaymentDebugLogger.cs`
- Delete after replacement: `ChurchReport/Tools/QPayToolkit.cs`
- Delete after replacement: `ChurchReport/Tools/QPayToolkitWrapper.cs`
- Delete after replacement: `ChurchReport/Tools/IQPayToolkit.cs`
- Delete after replacement: `ChurchReport/Tools/IPayment.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/QPayAdapterTests.cs`

**Interfaces:**
- Consumes: `IPaymentGateway.CreatePaymentAsync`, `QueryPaymentAsync`, and `ParseCallbackAsync`.
- Produces: ChurchReport QPay route/workflow without provider protocol implementation.

- [ ] **Step 1: Write QPay adapter tests**

Test:

- QPay return URL maps `ShopNo` and `PayToken` into neutral payment request DTOs.
- controller calls `IPaymentGateway` instead of `QPayProcessor.OrderPayQuery`.
- fee/dedication branching uses neutral metadata from the core, not raw `TSResultContent.Param3`.
- debug logger masks pay token and does not log raw provider signatures.
- `QpayManager` create-payment flow uses `PaymentCreateRequestFactory` and `IPaymentGateway.CreatePaymentAsync`.

- [ ] **Step 2: Replace old `IPayment` consumers**

Search:

```powershell
rg -n "IPayment" ChurchReport --glob "*.cs"
```

Replace payment create/query/parse consumers with `IPaymentGateway`. Product workflow classes remain in ChurchReport, but QPay request construction and status parsing move to `SpeechMessage.Payments`.

- [ ] **Step 3: Refactor return route**

`QPayCardController.QPayReturnUrl` keeps the same URL and parameters, builds a neutral request, calls the gateway, and renders the same ChurchReport success/failure view through product workflow code.

- [ ] **Step 4: Delete old QPay protocol files and run boundary search**

Run:

```powershell
rg -n "IPayment|IQPayToolkit|QPayToolkit|QPayToolkitWrapper|CreOrderReq|QryOrderPayReq|QryOrderPay|CreOrder|TSResultContent|PayToken|HashCode" ChurchReport --glob "*.cs"
```

Expected:

- no old toolkit or `IPayment` references
- no QPay request/response model references in ChurchReport consumers
- `PayToken` may remain only as an HTTP route/query field or masked display/debug value
- `HashCode` must not remain as payment signing logic in ChurchReport

- [ ] **Step 5: Run tests, build, commit**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter QPayAdapterTests
dotnet build ChurchReport\ChurchReport.csproj
git add ChurchReport ChurchReport.MemberInfo.Tests
git add -u ChurchReport\Tools\IPayment.cs ChurchReport\Tools\IQPayToolkit.cs ChurchReport\Tools\QPayToolkit.cs ChurchReport\Tools\QPayToolkitWrapper.cs
git commit -m "refactor: route qpay through payment core"
```

Expected: tests pass, build succeeds, commit succeeds.

---

### Task 11: Final Boundary Cleanup, Verification, And Documentation

**Files:**
- Modify: `ChurchReport/Startup.cs`
- Modify: `ChurchReport/appsettings.json`
- Create: `docs/payments/configuration.md`
- Modify if needed: `.trellis/spec/backend/index.md`
- Modify if needed: `.trellis/tasks/06-25-payment-module-extraction/prd.md`
- Modify if needed: `docs/superpowers/specs/2026-06-25-payment-module-extraction-design.md`

**Interfaces:**
- Consumes: all provider migrations and adapter tests.
- Produces: clean boundary, full verification, and configuration documentation.

- [ ] **Step 1: Remove legacy registrations**

Remove old wrapper registration from `Startup.cs`:

```csharp
services.AddScoped<IPayment, QPayToolkitWrapper>();
services.AddScoped<IPayment, MyPayToolkitWrapper>();
services.AddScoped<IPayment, TspgToolkitWrapper>();
services.AddScoped<TSPGWebhookHandler>();
```

Keep:

```csharp
services.AddSpeechMessagePayments(Configuration.GetSection("Payment"));
```

- [ ] **Step 2: Verify core forbidden dependencies**

Run:

```powershell
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
```

Expected: no matches.

- [ ] **Step 3: Verify ChurchReport no longer owns provider implementation**

Run:

```powershell
rg -n "QPayToolkit|MyPayToolkit|TspgToolkit|TSPGWebhookHandler|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|StoreKey|StoreIV|XKey|A1|A2|B1|B2|signature|hash" ChurchReport --glob "*.cs"
```

Expected: no provider toolkit/model/signing/encryption/status-mapping implementation in compiled ChurchReport code.

- [ ] **Step 4: Verify Line Pay non-interference**

Run:

```powershell
git diff -- LinePayCSharp
rg -n "LinePay" SpeechMessage.Payments ChurchReport --glob "*.cs" --glob "*.csproj"
```

Expected:

- `git diff -- LinePayCSharp` prints no diff
- `SpeechMessage.Payments` has no Line Pay provider implementation

- [ ] **Step 5: Write configuration documentation**

Create `docs/payments/configuration.md` documenting:

- `Payment:DefaultProfile`
- `Payment:Profiles:{name}:Provider`
- `Payment:Profiles:{name}:Environment`
- credential keys per provider
- endpoint keys per provider
- the rule that callback URLs are passed in `PaymentCreateRequest.Callbacks`
- the security note that current secrets in `appsettings.json` should be moved to environment-specific secret storage in a separate security task

- [ ] **Step 6: Run full verification**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj
dotnet build ChurchReport.sln
git diff --stat
```

Expected:

- both test projects pass
- solution builds
- diff contains payment extraction, tests, configuration docs, and task artifacts only

- [ ] **Step 7: CCG dual-model review gate**

Because this is L+ complexity and high risk, run CCG review with both Gemini and Claude against `git diff`. Save findings to `.ccg/tasks/payment-module-extraction/review.md`. Verify every Critical or Warning against actual code before changing anything.

- [ ] **Step 8: Trellis check and spec update**

Load and follow:

```powershell
Get-Content -LiteralPath '.agents\skills\trellis-check\SKILL.md'
Get-Content -LiteralPath '.agents\skills\trellis-update-spec\SKILL.md'
```

Update `.trellis/spec/` only for non-obvious project knowledge discovered during implementation.

- [ ] **Step 9: Commit**

Run:

```powershell
git add SpeechMessage.Payments SpeechMessage.Payments.Tests ChurchReport docs .trellis .ccg
git commit -m "feat: extract reusable payment core"
```

---

## Rollback Points

- After Task 4, rollback is to remove the ChurchReport project reference and adapter registration.
- After Task 6, rollback MyPay only by restoring MyPay controller/toolkit files from the previous commit.
- After Task 8, rollback Taishin only by restoring TSPG controller/toolkit files from the previous commit.
- After Task 10, rollback Sinopac/QPay only by restoring QPay toolkit, `IPayment`, and QPay route files from the previous commit.
- Do not delete legacy provider code until that provider's core tests, ChurchReport adapter tests, build, and boundary search pass.

## Self-Review Result

- Spec coverage: every approved requirement maps to a task: pure core in Tasks 1-3, ASP.NET-free callback contract in Tasks 2 and 4, multi-profile configuration in Tasks 3-4, sanitized diagnostics in Task 3 and provider tasks, MyPay/TSPG/QPay migration in Tasks 5-10, Line Pay non-interference in Task 11, and boundary cleanup in Task 11.
- Completeness scan: each task names exact files, interfaces consumed/produced, commands, expected results, and commit points.
- Type consistency: all tasks consistently use `IPaymentGateway`, `IPaymentProvider`, `PaymentCreateRequest`, `PaymentStatusResult`, `PaymentCallbackRequest`, `PaymentCallbackResult`, `PaymentCallbackAcknowledgement`, `PaymentMerchantProfile`, `PaymentProviderKind`, `PaymentStatus`, and `PaymentErrorKind`.
