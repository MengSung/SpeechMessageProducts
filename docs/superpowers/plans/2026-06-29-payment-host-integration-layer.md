# Payment Host Integration Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reusable `SpeechMessage.Payments.AspNetCore` host integration project and move generic ASP.NET payment adapter utilities out of `ChurchReport` while keeping `SpeechMessage.Payments` provider core unchanged.

**Architecture:** `SpeechMessage.Payments` remains the pure provider core. `SpeechMessage.Payments.AspNetCore` owns ASP.NET request/acknowledgement glue. `ChurchReport` keeps controllers, CRM, LINE, donation workflow, legacy QPay compatibility, and product views.

**Tech Stack:** .NET 10, ASP.NET Core MVC abstractions, `SpeechMessage.Payments`, xUnit, FluentAssertions, PowerShell, `rg`, `dotnet build/test`.

---

## Spec Inputs

- `docs/superpowers/specs/2026-06-29-payment-host-integration-layer-design.md`
- `.trellis/tasks/06-29-payment-host-integration-layer/prd.md`
- `.trellis/tasks/06-29-payment-host-integration-layer/design.md`
- `.trellis/tasks/06-29-payment-host-integration-layer/implement.md`

## Non-Negotiable Boundaries

- Do not modify provider protocol behavior in `SpeechMessage.Payments`.
- Do not move ASP.NET controllers into reusable payment projects.
- Do not move CRM, LINE, donation, fee, result-page, session, or ChurchReport route workflow into reusable payment projects.
- Do not rename `QPayView`, `QPayLogin`, `QpayManager`, or `QPayProcessor` in this plan.
- Do not dispatch subagents in this Codex session because the current mode says inline execution only.
- Keep `LinePayCSharp` untouched.

## File Structure

Create:

- `SpeechMessage.Payments.AspNetCore/SpeechMessage.Payments.AspNetCore.csproj`
  - Class library project for reusable ASP.NET host adapter utilities.
- `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs`
  - Maps ASP.NET `HttpRequest` into provider-neutral `PaymentCallbackRequest`.
- `SpeechMessage.Payments.AspNetCore/PaymentAcknowledgementResultMapper.cs`
  - Maps `PaymentCallbackAcknowledgement` into MVC `IActionResult`.
- `SpeechMessage.Payments.AspNetCore/DependencyInjection/PaymentAspNetCoreServiceCollectionExtensions.cs`
  - Registers host adapter services.

Modify:

- `ChurchReport.sln`
  - Add the new project.
- `ChurchReport/ChurchReport.csproj`
  - Add `ProjectReference` to `SpeechMessage.Payments.AspNetCore`.
- `ChurchReport/Startup.cs`
  - Replace direct moved-class registrations with `services.AddSpeechMessagePaymentAspNetCore()`.
- `ChurchReport/Controllers/MyPayController.cs`
  - Add `using SpeechMessage.Payments.AspNetCore;` for moved mapper types.
- `ChurchReport/Controllers/TSPGController.cs`
  - Add `using SpeechMessage.Payments.AspNetCore;` for moved mapper types.
- `ChurchReport/Controllers/QPayCardController.cs`
  - Add `using SpeechMessage.Payments.AspNetCore;` for moved mapper types.
- `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`
  - Add `ProjectReference` to `SpeechMessage.Payments.AspNetCore`.
- `ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs`
  - Change mapper namespace import from `ChurchReport.Payments` to `SpeechMessage.Payments.AspNetCore`.
- `ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs`
  - Change mapper namespace import from `ChurchReport.Payments` to `SpeechMessage.Payments.AspNetCore`.
- Any other test file that directly constructs `PaymentHttpRequestMapper` or `PaymentAcknowledgementResultMapper`.

Delete after replacements compile:

- `ChurchReport/Payments/PaymentHttpRequestMapper.cs`
- `ChurchReport/Payments/PaymentAcknowledgementResultMapper.cs`

Do not move:

- `ChurchReport/Payments/ChurchReportPaymentProfileResolver.cs`
- `ChurchReport/Payments/PaymentCreateRequestFactory.cs`
- `ChurchReport/Payments/PaymentWorkflowResultMapper.cs`
- `ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs`
- `ChurchReport/Payments/QPayReturnWorkflow.cs`
- `ChurchReport/Payments/QPayProductWorkflowDispatcher.cs`
- `ChurchReport/Payments/QPayWorkflowPaymentResult.cs`
- `ChurchReport/Payments/LegacyQPayModels.cs`

---

### Task 1: Create Host Integration Project

**Files:**
- Create: `SpeechMessage.Payments.AspNetCore/SpeechMessage.Payments.AspNetCore.csproj`
- Modify: `ChurchReport.sln`

- [ ] **Step 1: Create the project directory**

Run:

```powershell
New-Item -ItemType Directory -Force -Path 'SpeechMessage.Payments.AspNetCore'
```

Expected: directory exists at `SpeechMessage.Payments.AspNetCore`.

- [ ] **Step 2: Create the project file**

Create `SpeechMessage.Payments.AspNetCore/SpeechMessage.Payments.AspNetCore.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>true</IsPackable>
    <AssemblyName>SpeechMessage.Payments.AspNetCore</AssemblyName>
    <RootNamespace>SpeechMessage.Payments.AspNetCore</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\SpeechMessage.Payments\SpeechMessage.Payments.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add the project to the solution**

Run:

```powershell
dotnet sln ChurchReport.sln add SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj
```

Expected: `ChurchReport.sln` contains `SpeechMessage.Payments.AspNetCore`.

- [ ] **Step 4: Build the new empty project**

Run:

```powershell
dotnet build SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj --no-restore -v minimal -p:UseSharedCompilation=false
```

Expected: build succeeds. If restore is required because the project is new, run:

```powershell
dotnet build SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj -v minimal -p:UseSharedCompilation=false
```

- [ ] **Step 5: Commit**

```powershell
git add SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj ChurchReport.sln
git commit -m "feat: add payment aspnetcore host project"
```

---

### Task 2: Move PaymentHttpRequestMapper

**Files:**
- Create: `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs`
- Delete: `ChurchReport/Payments/PaymentHttpRequestMapper.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs`

- [ ] **Step 1: Write the host-project mapper**

Create `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.AspNetCore;

/// <summary>
/// Maps ASP.NET Core <see cref="HttpRequest"/> data into the provider-neutral
/// <see cref="PaymentCallbackRequest"/> consumed by <c>SpeechMessage.Payments</c>.
/// This type is host glue: it belongs in the ASP.NET integration project, not in
/// the provider core and not in any single product application.
/// </summary>
public sealed class PaymentHttpRequestMapper
{
    public async Task<PaymentCallbackRequest> MapAsync(
        HttpRequest request,
        string profileName,
        PaymentProviderKind? providerHint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rawBody = await ReadRawBodyAsync(request, cancellationToken);
        var form = request.HasFormContentType
            ? Flatten(await request.ReadFormAsync(cancellationToken))
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new PaymentCallbackRequest
        {
            ProfileName = profileName,
            ProviderHint = providerHint,
            HttpMethod = request.Method,
            ContentType = request.ContentType ?? string.Empty,
            RawBody = rawBody,
            Query = Flatten(request.Query),
            Form = form,
            Headers = Flatten(request.Headers)
        };
    }

    private static async Task<string> ReadRawBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        return rawBody;
    }

    private static IReadOnlyDictionary<string, string> Flatten(IEnumerable<KeyValuePair<string, StringValues>> values)
    {
        return values.ToDictionary(
            pair => pair.Key,
            pair => string.Join(",", pair.Value.ToArray()),
            StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Update mapper tests to use the new namespace**

Modify `ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs`.

Replace:

```csharp
using ChurchReport.Payments;
```

With:

```csharp
using SpeechMessage.Payments.AspNetCore;
```

- [ ] **Step 3: Run the mapper tests before deleting the old class**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~PaymentHttpRequestMapperTests" -p:UseSharedCompilation=false
```

Expected: tests pass. If the compiler reports ambiguous `PaymentHttpRequestMapper`, fully qualify the test construction:

```csharp
var mapper = new SpeechMessage.Payments.AspNetCore.PaymentHttpRequestMapper();
```

- [ ] **Step 4: Delete the old ChurchReport mapper**

Delete:

```text
ChurchReport/Payments/PaymentHttpRequestMapper.cs
```

- [ ] **Step 5: Run the mapper tests after deletion**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~PaymentHttpRequestMapperTests" -p:UseSharedCompilation=false
```

Expected: tests pass.

- [ ] **Step 6: Commit**

```powershell
git add SpeechMessage.Payments.AspNetCore\PaymentHttpRequestMapper.cs ChurchReport.MemberInfo.Tests\Payments\PaymentHttpRequestMapperTests.cs ChurchReport\Payments\PaymentHttpRequestMapper.cs
git commit -m "refactor: move payment http request mapper to aspnetcore host layer"
```

---

### Task 3: Move PaymentAcknowledgementResultMapper

**Files:**
- Create: `SpeechMessage.Payments.AspNetCore/PaymentAcknowledgementResultMapper.cs`
- Delete: `ChurchReport/Payments/PaymentAcknowledgementResultMapper.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs`

- [ ] **Step 1: Write the host-project acknowledgement mapper**

Create `SpeechMessage.Payments.AspNetCore/PaymentAcknowledgementResultMapper.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.AspNetCore;

/// <summary>
/// Converts payment-core acknowledgement descriptors into ASP.NET MVC results.
/// Provider acknowledgement rules remain in <c>SpeechMessage.Payments</c>; this
/// class only performs the host framework response mapping.
/// </summary>
public sealed class PaymentAcknowledgementResultMapper
{
    public static IActionResult Map(PaymentCallbackAcknowledgement acknowledgement)
    {
        return acknowledgement.Kind switch
        {
            PaymentAckKind.PlainText => new ContentResult
            {
                Content = acknowledgement.Content,
                ContentType = "text/plain",
                StatusCode = acknowledgement.StatusCode
            },
            PaymentAckKind.Json => new ContentResult
            {
                Content = acknowledgement.Content,
                ContentType = "application/json",
                StatusCode = acknowledgement.StatusCode
            },
            PaymentAckKind.Redirect => new RedirectResult(acknowledgement.Content),
            _ => new StatusCodeResult(acknowledgement.StatusCode)
        };
    }

    public IActionResult ToActionResult(PaymentCallbackAcknowledgement acknowledgement)
    {
        return Map(acknowledgement);
    }
}
```

- [ ] **Step 2: Update acknowledgement tests to use the new namespace**

Modify `ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs`.

Replace:

```csharp
using ChurchReport.Payments;
```

With:

```csharp
using SpeechMessage.Payments.AspNetCore;
```

- [ ] **Step 3: Run acknowledgement tests before deleting the old class**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~PaymentAcknowledgementResultMapperTests" -p:UseSharedCompilation=false
```

Expected: tests pass.

- [ ] **Step 4: Delete the old ChurchReport acknowledgement mapper**

Delete:

```text
ChurchReport/Payments/PaymentAcknowledgementResultMapper.cs
```

- [ ] **Step 5: Run acknowledgement tests after deletion**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~PaymentAcknowledgementResultMapperTests" -p:UseSharedCompilation=false
```

Expected: tests pass.

- [ ] **Step 6: Commit**

```powershell
git add SpeechMessage.Payments.AspNetCore\PaymentAcknowledgementResultMapper.cs ChurchReport.MemberInfo.Tests\Payments\PaymentAcknowledgementResultMapperTests.cs ChurchReport\Payments\PaymentAcknowledgementResultMapper.cs
git commit -m "refactor: move payment acknowledgement mapper to aspnetcore host layer"
```

---

### Task 4: Add Host DI Extension

**Files:**
- Create: `SpeechMessage.Payments.AspNetCore/DependencyInjection/PaymentAspNetCoreServiceCollectionExtensions.cs`
- Modify: `ChurchReport/Startup.cs`

- [ ] **Step 1: Write the DI extension**

Create directory:

```powershell
New-Item -ItemType Directory -Force -Path 'SpeechMessage.Payments.AspNetCore\DependencyInjection'
```

Create `SpeechMessage.Payments.AspNetCore/DependencyInjection/PaymentAspNetCoreServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace SpeechMessage.Payments.AspNetCore.DependencyInjection;

/// <summary>
/// Registers reusable ASP.NET host integration services for SpeechMessage payments.
/// Product-specific services such as CRM, LINE, views, and workflows remain in
/// the host application.
/// </summary>
public static class PaymentAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddSpeechMessagePaymentAspNetCore(this IServiceCollection services)
    {
        services.AddScoped<PaymentHttpRequestMapper>();
        services.AddScoped<PaymentAcknowledgementResultMapper>();
        return services;
    }
}
```

- [ ] **Step 2: Update Startup using directives**

Modify `ChurchReport/Startup.cs`.

Add:

```csharp
using SpeechMessage.Payments.AspNetCore.DependencyInjection;
```

Keep existing:

```csharp
using ChurchReport.Payments;
using SpeechMessage.Payments.DependencyInjection;
```

- [ ] **Step 3: Replace direct service registrations**

In `ChurchReport/Startup.cs`, replace:

```csharp
services.AddSpeechMessagePayments(Configuration.GetSection("Payment"));
services.AddScoped<PaymentHttpRequestMapper>();
services.AddScoped<PaymentAcknowledgementResultMapper>();
services.AddScoped<ChurchReportPaymentProfileResolver>();
services.AddScoped<PaymentCreateRequestFactory>();
services.AddScoped<PaymentWorkflowResultMapper>();
```

With:

```csharp
services.AddSpeechMessagePayments(Configuration.GetSection("Payment"));
services.AddSpeechMessagePaymentAspNetCore();
services.AddScoped<ChurchReportPaymentProfileResolver>();
services.AddScoped<PaymentCreateRequestFactory>();
services.AddScoped<PaymentWorkflowResultMapper>();
```

- [ ] **Step 4: Build ChurchReport to catch DI namespace errors**

Run:

```powershell
dotnet build ChurchReport\ChurchReport.csproj --no-restore -v minimal -p:UseSharedCompilation=false
```

Expected: build succeeds or only existing unrelated warnings remain.

- [ ] **Step 5: Commit**

```powershell
git add SpeechMessage.Payments.AspNetCore\DependencyInjection\PaymentAspNetCoreServiceCollectionExtensions.cs ChurchReport\Startup.cs
git commit -m "feat: register payment aspnetcore host services"
```

---

### Task 5: Update ChurchReport Consumers To New Namespace

**Files:**
- Modify: `ChurchReport/Controllers/MyPayController.cs`
- Modify: `ChurchReport/Controllers/TSPGController.cs`
- Modify: `ChurchReport/Controllers/QPayCardController.cs`

- [ ] **Step 1: Update MyPayController usings**

Modify `ChurchReport/Controllers/MyPayController.cs`.

Keep:

```csharp
using ChurchReport.Payments;
```

Add:

```csharp
using SpeechMessage.Payments.AspNetCore;
```

Reason: `ChurchReport.Payments` still provides `ChurchReportPaymentProfileResolver` and `PaymentWorkflowResultMapper`; `SpeechMessage.Payments.AspNetCore` now provides `PaymentHttpRequestMapper` and `PaymentAcknowledgementResultMapper`.

- [ ] **Step 2: Update TSPGController usings**

Modify `ChurchReport/Controllers/TSPGController.cs`.

Keep:

```csharp
using ChurchReport.Payments;
```

Add:

```csharp
using SpeechMessage.Payments.AspNetCore;
```

- [ ] **Step 3: Update QPayCardController usings**

Modify `ChurchReport/Controllers/QPayCardController.cs`.

Keep:

```csharp
using ChurchReport.Payments;
```

Add:

```csharp
using SpeechMessage.Payments.AspNetCore;
```

- [ ] **Step 4: Search for remaining consumer compile sites**

Run:

```powershell
rg -n "PaymentHttpRequestMapper|PaymentAcknowledgementResultMapper" ChurchReport ChurchReport.MemberInfo.Tests --glob "*.cs"
```

Expected: references remain in controllers/tests, but type definitions no longer exist under `ChurchReport/Payments`.

- [ ] **Step 5: Build ChurchReport**

Run:

```powershell
dotnet build ChurchReport\ChurchReport.csproj --no-restore -v minimal -p:UseSharedCompilation=false
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```powershell
git add ChurchReport\Controllers\MyPayController.cs ChurchReport\Controllers\TSPGController.cs ChurchReport\Controllers\QPayCardController.cs
git commit -m "refactor: consume payment aspnetcore host mappers"
```

---

### Task 6: Wire Project References For Host And Tests

**Files:**
- Modify: `ChurchReport/ChurchReport.csproj`
- Modify: `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`

- [ ] **Step 1: Add ChurchReport project reference**

Modify `ChurchReport/ChurchReport.csproj`.

In the existing project reference item group, add:

```xml
<ProjectReference Include="..\SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj" />
```

The item group should contain both:

```xml
<ProjectReference Include="..\SpeechMessage.Payments\SpeechMessage.Payments.csproj" />
<ProjectReference Include="..\SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj" />
```

- [ ] **Step 2: Add test project reference**

Modify `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`.

In the item group with the `ChurchReport` reference, add:

```xml
<ProjectReference Include="..\SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj" />
```

The item group should contain:

```xml
<ProjectReference Include="..\ChurchReport\ChurchReport.csproj" />
<ProjectReference Include="..\SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj" />
```

- [ ] **Step 3: Build test project**

Run:

```powershell
dotnet build ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
```

Expected: build succeeds or reports only existing unrelated warnings.

- [ ] **Step 4: Commit**

```powershell
git add ChurchReport\ChurchReport.csproj ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj
git commit -m "build: reference payment aspnetcore host layer"
```

---

### Task 7: Move Or Update Direct Tests

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs`

- [ ] **Step 1: Verify PaymentHttpRequestMapperTests has the correct using**

Ensure `ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs` contains:

```csharp
using SpeechMessage.Payments.AspNetCore;
```

And does not contain:

```csharp
using ChurchReport.Payments;
```

- [ ] **Step 2: Verify PaymentAcknowledgementResultMapperTests has the correct using**

Ensure `ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs` contains:

```csharp
using SpeechMessage.Payments.AspNetCore;
```

And does not contain:

```csharp
using ChurchReport.Payments;
```

- [ ] **Step 3: Run the moved mapper tests**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~PaymentHttpRequestMapperTests|FullyQualifiedName~PaymentAcknowledgementResultMapperTests" -p:UseSharedCompilation=false
```

Expected: mapper tests pass. If the filter does not match both classes, run them separately:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~PaymentHttpRequestMapperTests" -p:UseSharedCompilation=false
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~PaymentAcknowledgementResultMapperTests" -p:UseSharedCompilation=false
```

- [ ] **Step 4: Commit if Task 2 or Task 3 did not already commit these test changes**

```powershell
git add ChurchReport.MemberInfo.Tests\Payments\PaymentHttpRequestMapperTests.cs ChurchReport.MemberInfo.Tests\Payments\PaymentAcknowledgementResultMapperTests.cs
git commit -m "test: point payment host mapper tests at aspnetcore layer"
```

If there is nothing to commit because earlier tasks already committed these changes, skip this commit.

---

### Task 8: Boundary Verification

**Files:**
- No implementation files changed in this task.
- Update review notes if findings need documentation.

- [ ] **Step 1: Verify provider core did not gain host dependencies**

Run:

```powershell
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
```

Expected: no matches. Acceptable false positives must be comments that do not introduce references; if any appear, inspect them manually.

- [ ] **Step 2: Verify host project has no ChurchReport/product dependencies**

Run:

```powershell
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|Dataverse|QPayFeeProcessor|QPayDedicationBookingProcessor|QpayManager|QpayModel" SpeechMessage.Payments.AspNetCore --glob "*.cs" --glob "*.csproj"
```

Expected: no matches.

- [ ] **Step 3: Verify moved adapter implementations are gone from ChurchReport**

Run:

```powershell
rg -n "class PaymentHttpRequestMapper|class PaymentAcknowledgementResultMapper" ChurchReport --glob "*.cs"
```

Expected: no matches.

- [ ] **Step 4: Audit remaining QPay names in product host**

Run:

```powershell
rg -n "QPay|Qpay|qpay" ChurchReport --glob "*.cs" --glob "*.cshtml" --glob "*.json" --glob "*.csproj" -g "!ChurchReport/文件/**"
```

Expected: matches remain only in ChurchReport product workflow, legacy routes, Sinopac configuration, or comments explaining compatibility. Do not attempt broad rename in this plan.

- [ ] **Step 5: Verify LinePay remains untouched**

Run:

```powershell
git diff -- LinePayCSharp
```

Expected: no diff.

- [ ] **Step 6: Check whitespace**

Run:

```powershell
git diff --check
```

Expected: no whitespace errors. CRLF warnings are acceptable if they are existing project style and not reported as errors.

- [ ] **Step 7: Commit boundary notes if needed**

If verification findings are documented, commit:

```powershell
git add .ccg\tasks\payment-host-integration-layer .trellis\tasks\06-29-payment-host-integration-layer docs\superpowers\plans\2026-06-29-payment-host-integration-layer.md
git commit -m "docs: record payment host integration verification"
```

If no docs changed, skip this commit.

---

### Task 9: Full Validation

**Files:**
- No planned implementation changes.

- [ ] **Step 1: Test payment core**

Run:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
```

Expected: all `SpeechMessage.Payments.Tests` tests pass.

- [ ] **Step 2: Test ChurchReport payment adapter tests**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~Payments" -p:UseSharedCompilation=false
```

Expected: payment-related tests pass. Existing unrelated analyzer warnings such as `xUnit1012` may appear if already present, but tests should pass.

- [ ] **Step 3: Build the solution**

Run:

```powershell
dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false
```

Expected: solution build succeeds. If a transient file-lock failure appears, wait and retry once.

- [ ] **Step 4: Review changed files**

Run:

```powershell
git status --short
git diff --name-only
```

Expected: changes are limited to:

```text
SpeechMessage.Payments.AspNetCore/**
ChurchReport.sln
ChurchReport/ChurchReport.csproj
ChurchReport/Startup.cs
ChurchReport/Controllers/MyPayController.cs
ChurchReport/Controllers/TSPGController.cs
ChurchReport/Controllers/QPayCardController.cs
ChurchReport/Payments/PaymentHttpRequestMapper.cs
ChurchReport/Payments/PaymentAcknowledgementResultMapper.cs
ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs
ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs
docs/superpowers/plans/2026-06-29-payment-host-integration-layer.md
.trellis/tasks/06-29-payment-host-integration-layer/**
.ccg/tasks/payment-host-integration-layer/**
```

- [ ] **Step 5: Commit final validation notes**

If there are uncommitted docs or review notes:

```powershell
git add docs\superpowers\plans\2026-06-29-payment-host-integration-layer.md .trellis\tasks\06-29-payment-host-integration-layer .ccg\tasks\payment-host-integration-layer
git commit -m "docs: add payment host integration implementation plan"
```

Skip if already committed.

---

## Self-Review

### Spec Coverage

- New reusable host project is covered by Tasks 1 and 4.
- `PaymentHttpRequestMapper` extraction is covered by Task 2.
- `PaymentAcknowledgementResultMapper` extraction is covered by Task 3.
- ChurchReport DI and consumer updates are covered by Tasks 4, 5, and 6.
- Tests are covered by Tasks 2, 3, 7, and 9.
- Boundary verification is covered by Task 8.
- No provider-core modification is enforced in Non-Negotiable Boundaries and Task 8.
- ChurchReport QPay workflow rename is explicitly deferred.

### Placeholder Scan

This plan contains no `TBD`, `TODO`, or vague "handle edge cases" steps. Every implementation step includes concrete paths, code, or commands.

### Type Consistency

- Namespace for moved host adapter types is consistently `SpeechMessage.Payments.AspNetCore`.
- DI extension namespace is consistently `SpeechMessage.Payments.AspNetCore.DependencyInjection`.
- Extension method name is consistently `AddSpeechMessagePaymentAspNetCore`.
- Project name is consistently `SpeechMessage.Payments.AspNetCore`.

## Execution Notes For This Repository

The active Codex mode for this session is inline. Even though the standard Superpowers header recommends subagent-driven development, do not dispatch subagents here. Implement this plan inline, task by task, with verification after each task.

CCG dual-model review is required for high-risk L+ changes, but `$HOME\.claude\bin\codeagent-wrapper` is currently missing in this environment. If that remains true during implementation, record the blocked external review in `.ccg/tasks/payment-host-integration-layer/analysis.md` or `review.md` rather than claiming it ran.
