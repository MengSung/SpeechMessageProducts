# Dynamics Local/Central Gateway Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the existing `ExecutionMode=Gateway` product boundary safely interchangeable between the Central Gateway endpoint and the Local Gateway localhost endpoint, bound response memory, and remove the known high-severity package blocker before any Data8 legacy-worker load is introduced.

**Architecture:** Central and Local remain deployment topologies of the same Gateway HTTP contract; no new execution-mode enum is added. Product configuration is validated fail-closed, the configured profile alias cannot be overridden by an individual product call, and Gateway responses are streamed into a bounded buffer with deterministic disposal. The checked-in Data8 project remains temporary but cannot advance into a Gateway worker until its explicitly vulnerable XML cryptography package is patched and the dependency scan is clean.

**Tech Stack:** .NET 10, ASP.NET Core options validation, `HttpClientFactory`, `SocketsHttpHandler`, xUnit, FluentAssertions, NuGet vulnerability auditing, PowerShell.

---

## Scope and file ownership

This milestone changes only the product-facing Gateway boundary and the explicit vulnerable package version:

- `SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs`
  - Adds a bounded Gateway response-size option.
- `SpeechMessage.Dynamics.ProductClient/Configuration/GatewayProductDynamicsOptionsValidator.cs`
  - Owns the strict Gateway-mode startup contract.
- `SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs`
  - Registers the validator and keeps one bounded `HttpClientFactory` handler pool.
- `SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs`
  - Pins requests to the configured profile and performs bounded response reads.
- `SpeechMessage.Dynamics.Tests/ProductModeOptionsTests.cs`
  - Covers Central/Local valid settings and invalid topology/configuration branches.
- `SpeechMessage.Dynamics.Tests/GatewayProductClientTests.cs`
  - Covers profile-override rejection, bounded response memory, cancellation, and response disposal.
- `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`
  - Moves `System.Security.Cryptography.Xml` from vulnerable `10.0.9` to patched `10.0.10` only.
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md`
  - Records red/green evidence, package scan, build, and test results.

This milestone deliberately does not create the CE 8.2 worker, enable `Package01FeeReadsEnabled`, remove Embedded, or change product traffic. Those are separate plans after this boundary is green.

### Task 1: Write failing startup-contract tests

**Files:**

- Modify: `SpeechMessage.Dynamics.Tests/ProductModeOptionsTests.cs`
- Test: `SpeechMessage.Dynamics.Tests/ProductModeOptionsTests.cs`

- [ ] **Step 1: Add a helper that resolves validated Gateway options**

Add these imports:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
```

Add this helper inside `ProductModeOptionsTests`:

```csharp
private static ProductDynamicsOptions ResolveGatewayOptions(
    string endpoint,
    string apiPrefix = "/v1",
    Action<ProductDynamicsOptions>? mutate = null)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSpeechMessageDynamicsGatewayProductClient(options =>
    {
        options.ExecutionMode = DynamicsExecutionMode.Gateway;
        options.ProfileAlias = "jesus-prod";
        options.Gateway = new GatewayModeOptions
        {
            Endpoint = endpoint,
            ApiPrefix = apiPrefix
        };
        mutate?.Invoke(options);
    });

    using var provider = services.BuildServiceProvider(validateScopes: true);
    return provider.GetRequiredService<IOptions<ProductDynamicsOptions>>().Value;
}
```

- [ ] **Step 2: Add valid Central and Local topology tests**

```csharp
[Theory]
[InlineData("https://dynamics-gateway.internal/")]
[InlineData("https://localhost:7244/")]
public void Gateway_mode_accepts_central_and_local_https_endpoints(string endpoint)
{
    var options = ResolveGatewayOptions(endpoint);
    options.Gateway!.Endpoint.Should().Be(endpoint);
}
```

- [ ] **Step 3: Add fail-closed configuration tests**

```csharp
[Theory]
[InlineData("http://localhost:7244/")]
[InlineData("https://user:password@localhost:7244/")]
[InlineData("https://localhost:7244/?target=https://crm.example/")]
[InlineData("https://crm.example/XRMServices/2011/Organization.svc")]
[InlineData("https://crm.example/api/data/v9.1/")]
public void Gateway_mode_rejects_unsafe_or_raw_crm_endpoints(string endpoint)
{
    var act = () => ResolveGatewayOptions(endpoint);
    act.Should().Throw<OptionsValidationException>();
}

[Theory]
[InlineData("")]
[InlineData("v1")]
[InlineData("/v1?x=1")]
[InlineData("/v1#fragment")]
[InlineData("/../v1")]
[InlineData("/v1//operations")]
public void Gateway_mode_rejects_invalid_api_prefix(string apiPrefix)
{
    var act = () => ResolveGatewayOptions("https://localhost:7244/", apiPrefix);
    act.Should().Throw<OptionsValidationException>();
}

[Fact]
public void Gateway_mode_rejects_inactive_embedded_branch()
{
    var act = () => ResolveGatewayOptions(
        "https://localhost:7244/",
        mutate: options => options.Embedded = new EmbeddedModeOptions
        {
            OrganizationWebApiBaseUri = "https://crm.example/api/data/v9.1/",
            CeVersion = "9.1",
            SecretReference = "forbidden-in-gateway-mode",
            ManifestOrRegistrySource = "forbidden-in-gateway-mode"
        });

    act.Should().Throw<OptionsValidationException>();
}
```

- [ ] **Step 4: Run tests and verify RED**

Run:

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --configuration Release --no-restore `
  --filter "FullyQualifiedName~ProductModeOptionsTests"
```

Expected: the new invalid-endpoint, prefix, and inactive-branch cases fail because the current registration checks only non-empty values.

### Task 2: Implement one strict Gateway configuration validator

**Files:**

- Create: `SpeechMessage.Dynamics.ProductClient/Configuration/GatewayProductDynamicsOptionsValidator.cs`
- Modify: `SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs`
- Test: `SpeechMessage.Dynamics.Tests/ProductModeOptionsTests.cs`

- [ ] **Step 1: Add the validator**

Create a public sealed `IValidateOptions<ProductDynamicsOptions>` implementation with these exact rules:

```csharp
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.ProductClient.Configuration;

public sealed class GatewayProductDynamicsOptionsValidator
    : IValidateOptions<ProductDynamicsOptions>
{
    public ValidateOptionsResult Validate(string? name, ProductDynamicsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.ExecutionMode != DynamicsExecutionMode.Gateway)
        {
            failures.Add("ExecutionMode must be Gateway.");
        }

        if (string.IsNullOrWhiteSpace(options.ProfileAlias) ||
            options.ProfileAlias.Length > 128 ||
            options.ProfileAlias.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')))
        {
            failures.Add("ProfileAlias must be 1-128 letters, digits, '.', '_' or '-'.");
        }

        if (options.Embedded is not null)
        {
            failures.Add("Embedded options are forbidden when ExecutionMode=Gateway.");
        }

        if (options.Gateway is null)
        {
            failures.Add("Gateway options are required.");
        }
        else
        {
            ValidateEndpoint(options.Gateway.Endpoint, failures);
            ValidateApiPrefix(options.Gateway.ApiPrefix, failures);
            if (options.Gateway.MaxResponseBytes is < 1024 or > 8_388_608)
            {
                failures.Add("Gateway MaxResponseBytes must be between 1024 and 8388608.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateEndpoint(string? value, ICollection<string> failures)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(endpoint.Host) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            failures.Add("Gateway Endpoint must be an absolute HTTPS URI without user-info, query, or fragment.");
            return;
        }

        var path = endpoint.AbsolutePath;
        if (path.Contains("/api/data/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/xrmservices/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("Organization.svc", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Gateway Endpoint cannot be a raw Dynamics endpoint.");
        }
    }

    private static void ValidateApiPrefix(string? value, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 64 ||
            !value.StartsWith('/', StringComparison.Ordinal) ||
            value.Contains('\\') ||
            value.Contains('?') ||
            value.Contains('#') ||
            value.Contains("..", StringComparison.Ordinal) ||
            value.Contains("//", StringComparison.Ordinal))
        {
            failures.Add("Gateway ApiPrefix must be one bounded absolute path without traversal, query, or fragment.");
        }
    }
}
```

- [ ] **Step 2: Register the validator once**

In `ProductClientServiceCollectionExtensions.cs`, remove the inline `.Validate(...)` predicate and register:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<
        IValidateOptions<ProductDynamicsOptions>,
        GatewayProductDynamicsOptionsValidator>());

services.AddOptions<ProductDynamicsOptions>()
    .Configure(configure)
    .ValidateOnStart();
```

Keep the existing `HttpClientFactory` ownership and handler bounds unchanged.

- [ ] **Step 3: Run tests and verify GREEN**

Run the Task 1 command. Expected: all `ProductModeOptionsTests` pass.

### Task 3: Write failing profile-pin and bounded-response tests

**Files:**

- Modify: `SpeechMessage.Dynamics.Tests/GatewayProductClientTests.cs`
- Test: `SpeechMessage.Dynamics.Tests/GatewayProductClientTests.cs`

- [ ] **Step 1: Add a request profile-override test**

```csharp
[Fact]
public async Task Gateway_executor_rejects_request_profile_override_before_http_send()
{
    var sends = 0;
    using var httpClient = new HttpClient(new StubHandler(_ =>
    {
        Interlocked.Increment(ref sends);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }));
    var executor = CreateExecutor(httpClient, "https://localhost:7244/", "crm91");

    var result = await executor.ExecuteAsync(new OperationExecutionRequest
    {
        ProfileAlias = "crm82",
        CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
        WorkloadSubjectId = "church-report-service",
        Parameters = new Dictionary<string, object?>()
    });

    result.Succeeded.Should().BeFalse();
    result.ErrorCode.Should().Be(DynamicsErrorCodes.InvalidParameter);
    sends.Should().Be(0);
}
```

- [ ] **Step 2: Add a bounded chunked-response test**

Use a custom `TrackingContent` whose stream returns more than the configured limit and records disposal. Configure `MaxResponseBytes = 1024`. Assert:

```csharp
result.Succeeded.Should().BeFalse();
result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
content.StreamDisposed.Should().BeTrue();
```

The content must omit `Content-Length` so the test proves the streaming limit, not only header rejection.

- [ ] **Step 3: Add a declared Content-Length rejection test**

Return a response with `Content-Length = 2048` while the configured limit is 1024. Assert failure before the content stream is copied.

- [ ] **Step 4: Run tests and verify RED**

Run:

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --configuration Release --no-restore `
  --filter "FullyQualifiedName~GatewayProductClientTests"
```

Expected: alias override currently sends HTTP, and oversized chunked content is currently read by unbounded `ReadAsStringAsync`.

### Task 4: Implement profile pinning and bounded Gateway response reads

**Files:**

- Modify: `SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs`
- Modify: `SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs`
- Test: `SpeechMessage.Dynamics.Tests/GatewayProductClientTests.cs`

- [ ] **Step 1: Add the response bound**

Add to `GatewayModeOptions`:

```csharp
public int MaxResponseBytes { get; set; } = 2_097_152;
```

The validator from Task 2 enforces 1 KiB through 8 MiB.

- [ ] **Step 2: Pin every request to the configured profile**

In `ExecuteAsync`, trim the configured alias and reject any non-empty request alias that differs by `StringComparison.OrdinalIgnoreCase`. Do not send HTTP on mismatch. When the request alias is empty, use the configured alias.

- [ ] **Step 3: Use response-header streaming**

Change the send call to:

```csharp
response = await _httpClient.SendAsync(
    message,
    HttpCompletionOption.ResponseHeadersRead,
    cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 4: Reject a declared oversized response before reading**

If `response.Content.Headers.ContentLength` exceeds `MaxResponseBytes`, return a sanitized `UpstreamFailure`. Do not log or return the body.

- [ ] **Step 5: Read chunked content into a bounded rented buffer**

Implement a private helper that:

1. opens `response.Content.ReadAsStreamAsync(cancellationToken)`;
2. rents an `ArrayPool<byte>` buffer no larger than 16 KiB;
3. copies into a bounded `MemoryStream` only while total bytes are `<= MaxResponseBytes`;
4. fails as soon as the next read would exceed the limit;
5. clears the rented byte buffer before returning it;
6. disposes the response, content stream, and memory stream on every path;
7. decodes UTF-8 only after the bounded copy succeeds.

No response body, token, URL query, or serialized exception is logged.

- [ ] **Step 6: Run tests and verify GREEN**

Run the Task 3 command. Expected: all `GatewayProductClientTests` pass.

- [ ] **Step 7: Run the complete Dynamics suite**

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --configuration Release --no-restore
```

Expected: all tests pass with no new warnings.

### Task 5: Patch and verify the Data8 XML cryptography dependency

**Files:**

- Modify: `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`
- Test: NuGet audit plus solution build

- [ ] **Step 1: Capture the vulnerable baseline**

Run:

```powershell
dotnet list .\PowerPlatform.Dataverse.Client\PowerPlatform.Dataverse.Client.csproj `
  package --vulnerable --include-transitive
```

Expected before the edit: `System.Security.Cryptography.Xml 10.0.9` reports five High advisories.

- [ ] **Step 2: Apply the minimal patched version**

Change only:

```xml
<PackageReference Include="System.Security.Cryptography.Xml" Version="10.0.10" />
```

Do not upgrade `Microsoft.PowerPlatform.Dataverse.Client` in the same change; that requires separate CE 8.2 compatibility evidence.

- [ ] **Step 3: Restore and rerun the vulnerability audit**

```powershell
dotnet restore .\PowerPlatform.Dataverse.Client\PowerPlatform.Dataverse.Client.csproj
dotnet list .\PowerPlatform.Dataverse.Client\PowerPlatform.Dataverse.Client.csproj `
  package --vulnerable --include-transitive
```

Expected: no vulnerable package is reported for this project.

- [ ] **Step 4: Run the solution Release build**

```powershell
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
```

Expected: 0 errors and the previous ten NU1903 warnings for this package are gone.

### Task 6: Record evidence and review the milestone

**Files:**

- Create: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md`
- Modify: `.ccg/tasks/dynamics-connection-compatibility/review.md`
- Test: all commands below

- [ ] **Step 1: Run final local gates**

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --configuration Release --no-restore
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
git diff --check
```

- [ ] **Step 2: Verify text format and secret safety**

Require UTF-8 without BOM and CRLF for every touched text file. Scan the diff for passwords, bearer tokens, client secrets, private keys, raw CRM response bodies, and user/session identifiers.

- [ ] **Step 3: Run CCG review through the self-healing entrypoint**

Review the milestone with Gemini and Claude using `docs/scripts/Start-CcgDualModelRun.ps1`. Any Critical finding is fixed and re-reviewed. If one provider is quota-blocked, record the run as degraded fallback rather than complete dual-model success.

- [ ] **Step 4: Write the verification report**

Record:

- exact RED failures and GREEN test counts;
- Central and Local valid endpoint examples;
- invalid endpoint/prefix/branch cases;
- maximum response limit and disposal evidence;
- NuGet advisories before and clean scan after;
- Release build result;
- remaining blockers: multi-profile runtime, CE 8.2 worker, authenticated VM administration, live CE 8.2/9.1 smoke, product migration, and final Data8 removal.

## Follow-on plans after this milestone

Once this plan is green, create and execute these separate implementation plans in order:

1. `2026-07-29-dynamics-multi-profile-runtime.md`
   - immutable `crm82`/`crm91` profile generations, router, replace-and-drain, shared organization admission, isolation and soak tests;
2. `2026-07-29-dynamics-ce82-legacy-worker.md`
   - bounded localhost IPC, Data8 worker lifecycle, crash/recycle/timeout/handle tests, later official net48 worker adapter;
3. `2026-07-29-dynamics-lab-winrm-and-live-validation.md`
   - authenticated WinRM baseline, DC/D365 role inventory, HTTPS/Kerberos-safe management path, control-plane provisioning, CE 8.2/9.1 smoke evidence;
4. `2026-07-29-dynamics-churchreport-local-gateway-migration.md`
   - Multiple Startup Projects, feature-flagged read migration, parity, browser validation, rollback;
5. `2026-07-29-dynamics-phase6-data8-removal.md`
   - dependency/source scans, removal of ProjectReference/Solution entry/source, credential rotation, final full verification.

Embedded remains compiled but deferred throughout these plans. `Package01FeeReadsEnabled` remains `false` until the product-migration plan's real-server and rollback gates pass.
