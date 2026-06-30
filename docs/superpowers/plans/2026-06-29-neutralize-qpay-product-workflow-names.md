# Neutralize QPay Product Workflow Names Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace ChurchReport product workflow names that incorrectly imply Sinopac/QPay-only behavior with provider-neutral donation/payment names, while preserving existing routes and behavior.

**Architecture:** Keep `SpeechMessage.Payments` as the pure provider core, `SpeechMessage.Payments.AspNetCore` as ASP.NET host glue, and `SpeechMessage.Payments.Workflows` as post-payment abstractions. Rename ChurchReport product workflow surfaces from `QPay*`/`Qpay*` to donation/payment-neutral names in stages, keeping compatibility adapters and old routes until tests prove the new names work.

**Tech Stack:** .NET 10, ASP.NET Core MVC, C#, xUnit, FluentAssertions, Microsoft.Xrm.Sdk, existing ChurchReport `ToolUtility`, existing `SpeechMessage.Payments*` projects.

---

## Linus-Style Code Management Rules

This migration must favor simple structure over clever compatibility tricks. The purpose is not cosmetic renaming; it is to make the payment code easy to reason about, easy to grep, and hard to misuse in future ASP.NET Core products.

- Keep data flow obvious. A payment request should move from ChurchReport UI/business input, through a neutral adapter, into `SpeechMessage.Payments`, then back through a neutral return workflow. Do not hide the flow behind reflection, service locators, dynamic dictionaries, or broad helper classes.
- Remove special cases by naming the correct abstraction. `QPay*` is a provider-specific name and must not be the main name for ChurchReport donation/payment workflow. `QPay` may remain only for Sinopac protocol code, legacy public routes, and obsolete compatibility wrappers.
- Prefer boring files with one job. Each new neutral file must own one responsibility: create-payment mapping, return workflow, workflow dispatch, fee update, recurring donation update, result helper, or debug logging. Do not create another large catch-all processor.
- Keep compatibility thin and temporary. Legacy `QPay*` classes should delegate to neutral classes and contain no duplicated business logic. If a wrapper needs more than constructor forwarding and one-line method delegation, extract a smaller neutral service first.
- Make illegal states harder to express. Method parameters and DTO names should describe product-neutral payment concepts (`DonationPaymentWorkflowResult`, `ProviderOrderRef`, `PaymentReturnController`) instead of leaking one provider name into unrelated product code.
- Use small commits that compile independently. Every task in this plan ends with focused tests/build and a commit so a broken rename can be reverted without losing unrelated extraction work.
- Do not move business workflow into the reusable core. CRM updates, LINE notifications, ChurchReport views, donation models, and `ToolUtility` remain in ChurchReport or in product-owned implementations of workflow abstractions. `SpeechMessage.Payments` remains provider core only.
- Avoid big-bang route migration. Public URLs and view names can stay as legacy aliases until a separate route migration is planned, because external links and provider callback settings may depend on them.

## Scope And Naming Policy

This plan is **not** another provider-core extraction. The provider protocol for Sinopac/QPay already belongs in `SpeechMessage.Payments/Providers/Sinopac`.

This plan targets misleading ChurchReport product workflow names such as:

- `QPayProcessor`
- `QpayManager`
- `QPayCreatePaymentGatewayAdapter`
- `QPayReturnWorkflow`
- `QPayProductWorkflowDispatcher`
- `QPayWorkflowPaymentResult`
- `QPayCardController`
- `QPayFeeProcessor`
- `QPayDedicationBookingProcessor`
- `QPayPaymentResultHelper`
- `QPayPaymentDebugLogger`

Names may continue to contain `QPay` only when they refer to:

- Sinopac/QPay provider protocol internals in `SpeechMessage.Payments/Providers/Sinopac`.
- Backward-compatible public URL routes such as `/Dedication/QPayView/{LineId}` or `/QPayLogin`, until a separate route migration is approved.
- Temporary obsolete aliases that forward to new neutral classes during migration.

## File Structure

Create or rename toward these product-neutral ChurchReport names:

- `ChurchReport/WebServiceConnector/DonationPaymentProcessor/*`
  - New home for the current `QPayProcessor` partial class files.
  - Class name: `DonationPaymentProcessor`.
  - Responsibility: ChurchReport donation/payment orchestration only.

- `ChurchReport/Models/DonationPaymentManager.cs`
  - Replacement for `QpayManager`.
  - Responsibility: UI-facing donation payment state and calls into `DonationPaymentProcessor`.

- `ChurchReport/Payments/DonationPaymentCreateGatewayAdapter.cs`
  - Replacement for `QPayCreatePaymentGatewayAdapter`.
  - Responsibility: legacy ChurchReport create-payment input to `PaymentCreateRequest`.

- `ChurchReport/Payments/DonationPaymentReturnWorkflow.cs`
  - Replacement for `QPayReturnWorkflow`.
  - Responsibility: provider-neutral return handling for ChurchReport donation/payment workflow.

- `ChurchReport/Payments/DonationPaymentProductWorkflowDispatcher.cs`
  - Replacement for `QPayProductWorkflowDispatcher`.
  - Responsibility: dispatch ChurchReport fee/dedication booking updates.

- `ChurchReport/Payments/DonationPaymentWorkflowResult.cs`
  - Replacement for `QPayWorkflowPaymentResult`.
  - Responsibility: product workflow DTO for payment return processing.

- `ChurchReport/Controllers/PaymentReturnController.cs`
  - Replacement or parallel alias for `QPayCardController`.
  - Responsibility: receive provider return/callback routes and call `IPaymentGateway`.

- `ChurchReport/Tools/DonationFeePaymentProcessor.cs`
  - Replacement for `QPayFeeProcessor`.
  - Responsibility: ChurchReport fee entity update and result page logic.

- `ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
  - Replacement for `QPayDedicationBookingProcessor`.
  - Responsibility: ChurchReport recurring donation booking update and result page logic.

- `ChurchReport/Tools/DonationPaymentResultHelper.cs`
  - Replacement for `QPayPaymentResultHelper`.

- `ChurchReport/Tools/DonationPaymentDebugLogger.cs`
  - Replacement for `QPayPaymentDebugLogger`.

Keep compatibility wrappers temporarily:

- `QPayProcessor` forwards to or inherits from `DonationPaymentProcessor`.
- `QpayManager` forwards to or inherits from `DonationPaymentManager`.
- `QPayCardController` keeps old route compatibility or delegates to `PaymentReturnController`.
- Old `/QPayLogin`, `/Home/QPayView`, and `/Dedication/QPayView` routes remain until a separate UI route migration.

Compatibility wrapper rule:

- A wrapper must not contain product workflow decisions, CRM update logic, LINE notification logic, provider request mapping, or return-status branching.
- A wrapper may contain only constructor forwarding, dependency adaptation required by existing callers, and direct delegation to the neutral implementation.
- During review, any duplicated `if/else` payment logic inside a legacy `QPay*` wrapper is a refactor failure and must be moved into the neutral class.

Naming audit rule:

- After every implementation task, run a scoped search for `QPay|Qpay|qpay`.
- Remaining matches must be classified as one of: Sinopac provider protocol, public legacy route/view, obsolete compatibility wrapper, or documentation.
- If a match is ChurchReport business workflow and is not a wrapper/route, the task is not complete.

---

### Task 1: Add Characterization Tests For Current QPay-Named Product Workflow

**Files:**
- Create: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs`
- Create: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowNamingTests.cs`
- Read: `ChurchReport/Controllers/QPayCardController.cs`
- Read: `ChurchReport/Payments/QPayReturnWorkflow.cs`
- Read: `ChurchReport/Payments/QPayWorkflowPaymentResult.cs`

- [ ] **Step 1: Write compatibility tests for the existing return workflow surface**

Create `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowNamingTests.cs`:

```csharp
using FluentAssertions;
using SpeechMessage.Payments.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentReturnWorkflowNamingTests
{
    [Fact]
    public void Legacy_qpay_workflow_result_is_still_available_during_rename()
    {
        var resultType = Type.GetType("ChurchReport.Payments.QPayWorkflowPaymentResult, ChurchReport");

        resultType.Should().NotBeNull("the first rename phase keeps old workflow DTO as compatibility surface");
    }

    [Fact]
    public void New_donation_payment_workflow_result_exists_after_rename()
    {
        var resultType = Type.GetType("ChurchReport.Payments.DonationPaymentWorkflowResult, ChurchReport");

        resultType.Should().NotBeNull("new product-neutral DTO should replace QPay-named workflow DTO");
    }
}
```

- [ ] **Step 2: Run tests and verify the new-name test fails**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~DonationPaymentReturnWorkflowNamingTests" -p:UseSharedCompilation=false
```

Expected:

- `Legacy_qpay_workflow_result_is_still_available_during_rename` passes.
- `New_donation_payment_workflow_result_exists_after_rename` fails because `DonationPaymentWorkflowResult` does not exist yet.

- [ ] **Step 3: Add controller naming compatibility test**

Create `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentNamingCompatibilityTests
{
    [Fact]
    public void Old_qpay_card_controller_remains_available_as_route_alias_during_migration()
    {
        var legacyType = Type.GetType("ChurchReport.Controllers.QPayCardController, ChurchReport");

        legacyType.Should().NotBeNull();
        legacyType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }

    [Fact]
    public void New_payment_return_controller_exists_after_rename()
    {
        var newType = Type.GetType("ChurchReport.Controllers.PaymentReturnController, ChurchReport");

        newType.Should().NotBeNull("provider-neutral return controller should replace QPayCardController as the primary name");
        newType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }
}
```

- [ ] **Step 4: Run tests and verify the new controller test fails**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~DonationPaymentNamingCompatibilityTests" -p:UseSharedCompilation=false
```

Expected:

- Old controller test passes.
- New controller test fails.

- [ ] **Step 5: Commit characterization tests**

Run:

```powershell
git add ChurchReport.MemberInfo.Tests\Payments\DonationPaymentNamingCompatibilityTests.cs ChurchReport.MemberInfo.Tests\Payments\DonationPaymentReturnWorkflowNamingTests.cs
git commit -m "test: characterize legacy qpay product workflow names"
```

---

### Task 2: Rename Payment Workflow DTO And Return Workflow In `ChurchReport/Payments`

**Files:**
- Create: `ChurchReport/Payments/DonationPaymentWorkflowResult.cs`
- Create: `ChurchReport/Payments/DonationPaymentReturnWorkflow.cs`
- Modify: `ChurchReport/Payments/QPayWorkflowPaymentResult.cs`
- Modify: `ChurchReport/Payments/QPayReturnWorkflow.cs`
- Modify: `ChurchReport/Payments/QPayProductWorkflowDispatcher.cs`
- Modify: `ChurchReport/Startup.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowNamingTests.cs`

- [ ] **Step 1: Create neutral DTO**

Create `ChurchReport/Payments/DonationPaymentWorkflowResult.cs` by copying the public properties from `QPayWorkflowPaymentResult` and changing only the type name.

Expected shape:

```csharp
namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 產品流程使用的付款結果 DTO。
/// 這個型別屬於 ChurchReport 的奉獻/收費流程，不屬於任何單一金流供應商。
/// </summary>
public sealed record DonationPaymentWorkflowResult
{
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProviderOrderRef { get; init; } = string.Empty;
    public string ProviderTransactionId { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FailureCode { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
}
```

If the current `QPayWorkflowPaymentResult` has additional properties, copy them exactly.

- [ ] **Step 2: Convert old DTO into compatibility alias**

Modify `ChurchReport/Payments/QPayWorkflowPaymentResult.cs`:

```csharp
namespace ChurchReport.Payments;

/// <summary>
/// Backward-compatible alias for older ChurchReport code.
/// New code should use <see cref="DonationPaymentWorkflowResult"/>.
/// </summary>
[Obsolete("Use DonationPaymentWorkflowResult. QPay naming is retained only for route/code compatibility during migration.")]
public sealed record QPayWorkflowPaymentResult : DonationPaymentWorkflowResult;
```

If `DonationPaymentWorkflowResult` must remain sealed, do not inherit. Instead duplicate the properties temporarily and add conversion helpers:

```csharp
public DonationPaymentWorkflowResult ToDonationPaymentWorkflowResult() => new()
{
    ProductOrderId = ProductOrderId,
    ProviderOrderRef = ProviderOrderRef,
    ProviderTransactionId = ProviderTransactionId,
    Amount = Amount,
    Status = Status,
    Description = Description,
    FailureCode = FailureCode,
    FailureMessage = FailureMessage
};
```

- [ ] **Step 3: Create neutral return workflow interface and class**

Create `ChurchReport/Payments/DonationPaymentReturnWorkflow.cs` with the current logic from `QPayReturnWorkflow`, renamed:

```csharp
namespace ChurchReport.Payments;

public interface IDonationPaymentReturnWorkflow
{
    IActionResult HandleReturn(string shopNo, string providerOrderRef, PaymentStatusResult statusResult);
}

public sealed class DonationPaymentReturnWorkflow : IDonationPaymentReturnWorkflow
{
    private readonly IDonationPaymentProductWorkflowDispatcher? _productWorkflowDispatcher;

    public DonationPaymentReturnWorkflow(IDonationPaymentProductWorkflowDispatcher? productWorkflowDispatcher = null)
    {
        _productWorkflowDispatcher = productWorkflowDispatcher;
    }

    public IActionResult HandleReturn(string shopNo, string providerOrderRef, PaymentStatusResult statusResult)
    {
        // Copy current QPayReturnWorkflow behavior here without changing behavior.
    }
}
```

Copy every branch from `QPayReturnWorkflow.HandleReturn` exactly, but use `DonationPaymentWorkflowResult`.

- [ ] **Step 4: Keep old return workflow as compatibility wrapper**

Modify `ChurchReport/Payments/QPayReturnWorkflow.cs`:

```csharp
namespace ChurchReport.Payments;

[Obsolete("Use IDonationPaymentReturnWorkflow.")]
public interface IQPayReturnWorkflow : IDonationPaymentReturnWorkflow
{
}

[Obsolete("Use DonationPaymentReturnWorkflow.")]
public sealed class QPayReturnWorkflow : IQPayReturnWorkflow
{
    private readonly DonationPaymentReturnWorkflow _inner;

    public QPayReturnWorkflow(IQPayProductWorkflowDispatcher? productWorkflowDispatcher = null)
    {
        _inner = new DonationPaymentReturnWorkflow(productWorkflowDispatcher);
    }

    public IActionResult HandleReturn(string shopNo, string providerOrderRef, PaymentStatusResult statusResult)
    {
        return _inner.HandleReturn(shopNo, providerOrderRef, statusResult);
    }
}
```

- [ ] **Step 5: Rename workflow dispatcher interface and class**

Create neutral names in `ChurchReport/Payments/QPayProductWorkflowDispatcher.cs` or move to `DonationPaymentProductWorkflowDispatcher.cs`:

```csharp
public interface IDonationPaymentProductWorkflowDispatcher
{
    IActionResult HandleFeeReturn(string shopNo, string payToken, DonationPaymentWorkflowResult paymentResult);
    IActionResult HandleRecurringDonationReturn(string shopNo, string payToken, DonationPaymentWorkflowResult paymentResult);
}
```

Keep old interface as compatibility:

```csharp
[Obsolete("Use IDonationPaymentProductWorkflowDispatcher.")]
public interface IQPayProductWorkflowDispatcher : IDonationPaymentProductWorkflowDispatcher
{
}
```

- [ ] **Step 6: Update DI registration**

Modify `ChurchReport/Startup.cs` registrations:

```csharp
services.AddScoped<IDonationPaymentProductWorkflowDispatcher, DonationPaymentProductWorkflowDispatcher>();
services.AddScoped<IDonationPaymentReturnWorkflow, DonationPaymentReturnWorkflow>();

// Temporary compatibility registrations.
services.AddScoped<IQPayProductWorkflowDispatcher>(sp =>
    (IQPayProductWorkflowDispatcher)sp.GetRequiredService<IDonationPaymentProductWorkflowDispatcher>());
services.AddScoped<IQPayReturnWorkflow>(sp =>
    new QPayReturnWorkflow(sp.GetService<IQPayProductWorkflowDispatcher>()));
```

If the old interface cannot cast safely, register a small adapter class instead.

- [ ] **Step 7: Run focused tests**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~DonationPaymentReturnWorkflowNamingTests|FullyQualifiedName~QPayAdapterTests" -p:UseSharedCompilation=false
```

Expected: all selected tests pass.

- [ ] **Step 8: Commit**

Run:

```powershell
git add ChurchReport\Payments ChurchReport\Startup.cs ChurchReport.MemberInfo.Tests\Payments
git commit -m "refactor: add neutral donation payment return workflow names"
```

---

### Task 3: Rename Create-Payment Adapter Away From QPay

**Files:**
- Create: `ChurchReport/Payments/DonationPaymentCreateGatewayAdapter.cs`
- Modify: `ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs`
- Modify: `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.Core.cs`
- Modify: `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.PaymentGateway.cs`
- Modify: `ChurchReport/Controllers/BaseChurchController.cs`
- Modify: `ChurchReport/Models/ContextDictionary.cs`
- Modify: `ChurchReport/Startup.cs`

- [ ] **Step 1: Create neutral adapter class**

Create `ChurchReport/Payments/DonationPaymentCreateGatewayAdapter.cs` by copying the current `QPayCreatePaymentGatewayAdapter` implementation and renaming:

```csharp
namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 產品流程的建立付款 adapter。
/// 將既有奉獻/收費輸入轉成 SpeechMessage.Payments 的中立 PaymentCreateRequest。
/// </summary>
public sealed class DonationPaymentCreateGatewayAdapter
{
    // Copy constructor dependencies and methods from QPayCreatePaymentGatewayAdapter.
}
```

Do not change request mapping behavior in this task.

- [ ] **Step 2: Keep old adapter as compatibility wrapper**

Modify `ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs`:

```csharp
namespace ChurchReport.Payments;

[Obsolete("Use DonationPaymentCreateGatewayAdapter.")]
public sealed class QPayCreatePaymentGatewayAdapter
{
    private readonly DonationPaymentCreateGatewayAdapter _inner;

    public QPayCreatePaymentGatewayAdapter(/* same dependencies as before */)
    {
        _inner = new DonationPaymentCreateGatewayAdapter(/* pass dependencies */);
    }

    public Task<CreOrder> CreateLegacyOrderAsync(QPayCreatePaymentInput input)
    {
        return _inner.CreateLegacyOrderAsync(input);
    }
}
```

If `QPayCreatePaymentInput` is also provider-neutral in practice, create `DonationPaymentCreateInput` and keep conversion methods:

```csharp
public static DonationPaymentCreateInput FromLegacy(QPayCreatePaymentInput input) => new()
{
    Amount = input.Amount,
    ProductName = input.ProductName,
    ProductOrderId = input.ProductOrderId,
    ProductEntityId = input.ProductEntityId,
    PaymentOrganization = input.PaymentOrganization,
    PaymentCategory = input.PaymentCategory,
    PaymentMethod = input.PaymentMethod,
    PaymentMethodSubType = input.PaymentMethodSubType,
    ReturnUrl = input.ReturnUrl,
    BackendUrl = input.BackendUrl
};
```

- [ ] **Step 3: Update primary consumers to neutral adapter**

Modify:

- `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.Core.cs`
- `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.PaymentGateway.cs`
- `ChurchReport/Controllers/BaseChurchController.cs`
- `ChurchReport/Models/ContextDictionary.cs`

Replace primary field/property/constructor names:

```csharp
private readonly DonationPaymentCreateGatewayAdapter _donationPaymentCreateGatewayAdapter;
```

And method:

```csharp
private DonationPaymentCreateGatewayAdapter GetRequiredDonationPaymentCreateGatewayAdapter()
{
    if (_donationPaymentCreateGatewayAdapter == null)
    {
        throw new InvalidOperationException(
            "Donation payment create gateway adapter is required before creating payment orders.");
    }

    return _donationPaymentCreateGatewayAdapter;
}
```

- [ ] **Step 4: Update DI**

Modify `ChurchReport/Startup.cs`:

```csharp
services.AddScoped<DonationPaymentCreateGatewayAdapter>();

// Temporary compatibility registration.
services.AddScoped<QPayCreatePaymentGatewayAdapter>();
```

- [ ] **Step 5: Run focused tests and build**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~Payments" -p:UseSharedCompilation=false
dotnet build ChurchReport\ChurchReport.csproj --no-restore -v minimal -p:UseSharedCompilation=false
```

Expected: tests pass and build succeeds.

- [ ] **Step 6: Commit**

Run:

```powershell
git add ChurchReport\Payments ChurchReport\WebServiceConnector\QPayProcessor ChurchReport\Controllers\BaseChurchController.cs ChurchReport\Models\ContextDictionary.cs ChurchReport\Startup.cs
git commit -m "refactor: rename payment create adapter to donation payment"
```

---

### Task 4: Rename `QPayProcessor` Partial Class To `DonationPaymentProcessor`

**Files:**
- Create directory: `ChurchReport/WebServiceConnector/DonationPaymentProcessor/`
- Move/rename:
  - `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.Core.cs`
  - `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.FeeManagement.cs`
  - `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.DedicationBooking.cs`
  - `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.PaymentProcessing.cs`
  - `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.PaymentGateway.cs`
  - `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.EntityMapper.cs`
  - `ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.Utilities.cs`
- Create: `ChurchReport/WebServiceConnector/QPayProcessorCompatibility.cs`
- Modify: `ChurchReport/Models/QpayManager.cs`
- Modify: `ChurchReport/WebServiceConnector/QPayProcessor.cs`

- [ ] **Step 1: Create new directory**

Run:

```powershell
New-Item -ItemType Directory -Force -Path ChurchReport\WebServiceConnector\DonationPaymentProcessor
```

- [ ] **Step 2: Copy partial files into new directory**

Copy each source file to the new directory and rename file prefix:

```powershell
Copy-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.Core.cs ChurchReport\WebServiceConnector\DonationPaymentProcessor\DonationPaymentProcessor.Core.cs
Copy-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.FeeManagement.cs ChurchReport\WebServiceConnector\DonationPaymentProcessor\DonationPaymentProcessor.FeeManagement.cs
Copy-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.DedicationBooking.cs ChurchReport\WebServiceConnector\DonationPaymentProcessor\DonationPaymentProcessor.DedicationBooking.cs
Copy-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.PaymentProcessing.cs ChurchReport\WebServiceConnector\DonationPaymentProcessor\DonationPaymentProcessor.PaymentProcessing.cs
Copy-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.PaymentGateway.cs ChurchReport\WebServiceConnector\DonationPaymentProcessor\DonationPaymentProcessor.PaymentGateway.cs
Copy-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.EntityMapper.cs ChurchReport\WebServiceConnector\DonationPaymentProcessor\DonationPaymentProcessor.EntityMapper.cs
Copy-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.Utilities.cs ChurchReport\WebServiceConnector\DonationPaymentProcessor\DonationPaymentProcessor.Utilities.cs
```

- [ ] **Step 3: Replace class name inside copied files**

In every `DonationPaymentProcessor.*.cs`, replace:

```csharp
public partial class QPayProcessor
```

with:

```csharp
public partial class DonationPaymentProcessor
```

Replace constructor names:

```csharp
public QPayProcessor(
```

with:

```csharp
public DonationPaymentProcessor(
```

Replace trace prefixes from `[QPayProcessor]` to `[DonationPaymentProcessor]`.

- [ ] **Step 4: Create compatibility wrapper**

Create `ChurchReport/WebServiceConnector/QPayProcessorCompatibility.cs`:

```csharp
using ChurchReport.Payments;
using Line.Messaging;

namespace ChurchReport.WebServiceConnector;

[Obsolete("Use DonationPaymentProcessor. QPayProcessor is only a compatibility alias.")]
public class QPayProcessor : DonationPaymentProcessor
{
    public QPayProcessor(DonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
        : base(donationPaymentCreateGatewayAdapter)
    {
    }

    public QPayProcessor(
        LineMessagingClient lineMessagingClient,
        PushUtility pushUtility,
        ReplyUtility replyUtility,
        DonationPaymentCreateGatewayAdapter donationPaymentCreateGatewayAdapter)
        : base(lineMessagingClient, pushUtility, replyUtility, donationPaymentCreateGatewayAdapter)
    {
    }
}
```

If the constructors still require old adapter names, complete Task 3 first and update constructor signatures before this task.

- [ ] **Step 5: Update `QpayManager` to use new processor**

Modify `ChurchReport/Models/QpayManager.cs`:

```csharp
private DonationPaymentProcessor _donationPaymentProcessor;
```

Replace calls:

```csharp
m_QPayProcessor.SaveKeyInDedication(QpayModel)
m_QPayProcessor.CreateFeeAsync(m_Contact, QpayModel)
```

with:

```csharp
_donationPaymentProcessor.SaveKeyInDedication(QpayModel)
_donationPaymentProcessor.CreateFeeAsync(m_Contact, QpayModel)
```

Keep old field only if required by external code:

```csharp
[Obsolete("Use DonationPaymentProcessor-backed members instead.")]
private QPayProcessor? m_QPayProcessor;
```

- [ ] **Step 6: Exclude old copied implementation files**

After build passes with new copied files and compatibility wrapper, remove old partial implementation files:

```powershell
Remove-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.Core.cs
Remove-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.FeeManagement.cs
Remove-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.DedicationBooking.cs
Remove-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.PaymentProcessing.cs
Remove-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.PaymentGateway.cs
Remove-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.EntityMapper.cs
Remove-Item ChurchReport\WebServiceConnector\QPayProcessor\QPayProcessor.Utilities.cs
```

Leave `QPayProcessor.cs` only as a historical note or delete it if it contains only comments.

- [ ] **Step 7: Build**

Run:

```powershell
dotnet build ChurchReport\ChurchReport.csproj --no-restore -v minimal -p:UseSharedCompilation=false
```

Expected: build succeeds.

- [ ] **Step 8: Search for old processor implementation**

Run:

```powershell
Select-String -Path 'ChurchReport\WebServiceConnector\**\*.cs','ChurchReport\Models\*.cs' -Pattern 'public partial class QPayProcessor|new QPayProcessor|m_QPayProcessor'
```

Expected:

- `public partial class QPayProcessor` has no matches.
- `new QPayProcessor` appears only in compatibility tests or obsolete wrappers.
- `m_QPayProcessor` is removed or marked obsolete.

- [ ] **Step 9: Commit**

Run:

```powershell
git add ChurchReport\WebServiceConnector ChurchReport\Models\QpayManager.cs
git commit -m "refactor: rename qpay processor to donation payment processor"
```

---

### Task 5: Rename `QpayManager` To `DonationPaymentManager`

**Files:**
- Create: `ChurchReport/Models/DonationPaymentManager.cs`
- Modify: `ChurchReport/Models/QpayManager.cs`
- Modify: `ChurchReport/Models/ContextDictionary.cs`
- Modify: `ChurchReport/Controllers/DedicationController.cs`
- Modify: `ChurchReport/Controllers/DedicationAuditController.cs`
- Modify: `ChurchReport/Controllers/BaseChurchController.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs`

- [ ] **Step 1: Copy `QpayManager` to neutral name**

Create `ChurchReport/Models/DonationPaymentManager.cs` from `QpayManager.cs`.

Replace:

```csharp
public class QpayManager
```

with:

```csharp
public class DonationPaymentManager
```

Replace constructor names accordingly.

- [ ] **Step 2: Keep `QpayManager` as compatibility alias**

Modify `ChurchReport/Models/QpayManager.cs`:

```csharp
namespace ChurchReport.Models;

[Obsolete("Use DonationPaymentManager. QpayManager is retained only for compatibility.")]
public class QpayManager : DonationPaymentManager
{
    public QpayManager(/* same dependencies */)
        : base(/* pass dependencies */)
    {
    }
}
```

If constructor dependencies are complex, keep the original implementation during the first pass and only update consumers to `DonationPaymentManager`.

- [ ] **Step 3: Update `ContextDictionary` to expose neutral property**

Modify `ChurchReport/Models/ContextDictionary.cs`:

```csharp
public DonationPaymentManager DonationPaymentManager { get; }

[Obsolete("Use DonationPaymentManager.")]
public QpayManager QpayManager => (QpayManager)DonationPaymentManager;
```

If casting is not safe, keep both properties initialized from the same dependencies until old consumers are migrated.

- [ ] **Step 4: Update controllers to prefer neutral property**

Modify:

- `ChurchReport/Controllers/DedicationController.cs`
- `ChurchReport/Controllers/DedicationAuditController.cs`
- `ChurchReport/Controllers/BaseChurchController.cs`

Replace:

```csharp
InMemoryContext.QpayManager
```

with:

```csharp
InMemoryContext.DonationPaymentManager
```

Do not rename routes or action names in this task.

- [ ] **Step 5: Run focused controller tests**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~Dedication|FullyQualifiedName~Payments" -p:UseSharedCompilation=false
```

Expected: tests pass.

- [ ] **Step 6: Search for old manager references**

Run:

```powershell
Select-String -Path 'ChurchReport\Controllers\*.cs','ChurchReport\Models\*.cs' -Pattern 'QpayManager|m_QPayProcessor'
```

Expected:

- `QpayManager` remains only as compatibility alias and route/UI compatibility references.
- Controllers prefer `DonationPaymentManager`.

- [ ] **Step 7: Commit**

Run:

```powershell
git add ChurchReport\Models ChurchReport\Controllers
git commit -m "refactor: rename qpay manager to donation payment manager"
```

---

### Task 6: Rename Return Controller While Preserving Old Routes

**Files:**
- Create: `ChurchReport/Controllers/PaymentReturnController.cs`
- Modify: `ChurchReport/Controllers/QPayCardController.cs`
- Modify: `ChurchReport/Startup.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs`

- [ ] **Step 1: Create neutral return controller**

Create `ChurchReport/Controllers/PaymentReturnController.cs` by copying current `QPayCardController` behavior and renaming class/dependencies:

```csharp
namespace ChurchReport.Controllers;

public class PaymentReturnController : Controller
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly PaymentHttpRequestMapper _paymentHttpRequestMapper;
    private readonly ChurchReportPaymentProfileResolver _paymentProfileResolver;
    private readonly IDonationPaymentReturnWorkflow _donationPaymentReturnWorkflow;

    public PaymentReturnController(
        IPaymentGateway paymentGateway,
        PaymentHttpRequestMapper paymentHttpRequestMapper,
        ChurchReportPaymentProfileResolver paymentProfileResolver,
        IDonationPaymentReturnWorkflow donationPaymentReturnWorkflow)
    {
        _paymentGateway = paymentGateway ?? throw new ArgumentNullException(nameof(paymentGateway));
        _paymentHttpRequestMapper = paymentHttpRequestMapper ?? throw new ArgumentNullException(nameof(paymentHttpRequestMapper));
        _paymentProfileResolver = paymentProfileResolver ?? throw new ArgumentNullException(nameof(paymentProfileResolver));
        _donationPaymentReturnWorkflow = donationPaymentReturnWorkflow ?? throw new ArgumentNullException(nameof(donationPaymentReturnWorkflow));
    }
}
```

Move the current `QPayReturnUrl` action body into a neutral action name:

```csharp
[HttpGet]
[HttpPost]
[Route("/Payment/Return")]
public async Task<IActionResult> Return(string ShopNo, string PayToken)
{
    // Same behavior as QPayCardController.QPayReturnUrl.
}
```

- [ ] **Step 2: Keep old controller route as delegating compatibility surface**

Modify `ChurchReport/Controllers/QPayCardController.cs`:

```csharp
[Obsolete("Use PaymentReturnController. This controller exists only for existing QPay return URLs.")]
public class QPayCardController : Controller
{
    private readonly PaymentReturnController _inner;

    public QPayCardController(PaymentReturnController inner)
    {
        _inner = inner;
    }

    [HttpGet]
    [HttpPost]
    public Task<IActionResult> QPayReturnUrl(string ShopNo, string PayToken)
    {
        return _inner.Return(ShopNo, PayToken);
    }
}
```

If MVC cannot inject a controller into another controller cleanly, extract the action body into `DonationPaymentReturnEndpointHandler` and inject that handler into both controllers.

- [ ] **Step 3: Run tests**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~DonationPaymentNamingCompatibilityTests|FullyQualifiedName~QPayAdapterTests" -p:UseSharedCompilation=false
```

Expected: tests pass.

- [ ] **Step 4: Commit**

Run:

```powershell
git add ChurchReport\Controllers ChurchReport.MemberInfo.Tests\Payments
git commit -m "refactor: add provider-neutral payment return controller"
```

---

### Task 7: Rename Fee And Recurring Donation Return Processors

**Files:**
- Create: `ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- Create: `ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- Modify: `ChurchReport/Tools/QPayFeeProcessor.cs`
- Modify: `ChurchReport/Tools/QPayDedicationBookingProcessor.cs`
- Modify: `ChurchReport/Payments/DonationPaymentProductWorkflowDispatcher.cs`
- Modify: `ChurchReport/Tools/QPayPaymentResultHelper.cs`
- Modify: `ChurchReport/Tools/QPayPaymentDebugLogger.cs`

- [ ] **Step 1: Copy fee processor to neutral name**

Create `ChurchReport/Tools/DonationFeePaymentProcessor.cs` from `QPayFeeProcessor.cs`.

Replace:

```csharp
public class QPayFeeProcessor
```

with:

```csharp
public class DonationFeePaymentProcessor
```

Replace method:

```csharp
QPayFeeProcessorReturnUrl
```

with:

```csharp
HandlePaymentReturn
```

Change parameter type from `QPayWorkflowPaymentResult` to `DonationPaymentWorkflowResult`.

- [ ] **Step 2: Copy recurring donation processor to neutral name**

Create `ChurchReport/Tools/RecurringDonationPaymentProcessor.cs` from `QPayDedicationBookingProcessor.cs`.

Replace:

```csharp
public class QPayDedicationBookingProcessor
```

with:

```csharp
public class RecurringDonationPaymentProcessor
```

Replace method:

```csharp
QPayDedicationBookingProcessorReturnUrl
```

with:

```csharp
HandlePaymentReturn
```

Change parameter type from `QPayWorkflowPaymentResult` to `DonationPaymentWorkflowResult`.

- [ ] **Step 3: Keep old processors as compatibility wrappers**

Modify `QPayFeeProcessor.cs`:

```csharp
[Obsolete("Use DonationFeePaymentProcessor.")]
public class QPayFeeProcessor : DonationFeePaymentProcessor
{
}
```

Modify `QPayDedicationBookingProcessor.cs`:

```csharp
[Obsolete("Use RecurringDonationPaymentProcessor.")]
public class QPayDedicationBookingProcessor : RecurringDonationPaymentProcessor
{
}
```

If constructors or disposal logic prevent inheritance, keep wrappers that instantiate the neutral processors and delegate calls.

- [ ] **Step 4: Rename result helper and debug logger**

Create:

- `ChurchReport/Tools/DonationPaymentResultHelper.cs`
- `ChurchReport/Tools/DonationPaymentDebugLogger.cs`

Copy current helper/logger behavior and replace `QPayWorkflowPaymentResult` with `DonationPaymentWorkflowResult`.

Keep old files as compatibility wrappers:

```csharp
[Obsolete("Use DonationPaymentResultHelper.")]
public static class QPayPaymentResultHelper
{
    public static bool IsPaymentSuccess(QPayWorkflowPaymentResult result)
    {
        return DonationPaymentResultHelper.IsPaymentSuccess(result.ToDonationPaymentWorkflowResult());
    }
}
```

- [ ] **Step 5: Update dispatcher to use neutral processors**

Modify `ChurchReport/Payments/DonationPaymentProductWorkflowDispatcher.cs`:

```csharp
using var processor = new DonationFeePaymentProcessor();
return processor.HandlePaymentReturn(shopNo, payToken, paymentResult);
```

And:

```csharp
using var processor = new RecurringDonationPaymentProcessor();
return processor.HandlePaymentReturn(shopNo, payToken, paymentResult);
```

- [ ] **Step 6: Run focused tests and build**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~Payments" -p:UseSharedCompilation=false
dotnet build ChurchReport\ChurchReport.csproj --no-restore -v minimal -p:UseSharedCompilation=false
```

Expected: tests pass and build succeeds.

- [ ] **Step 7: Commit**

Run:

```powershell
git add ChurchReport\Tools ChurchReport\Payments
git commit -m "refactor: rename qpay fee processors to donation payment processors"
```

---

### Task 8: Boundary Search, Documentation, And Deferred Route Rename

**Files:**
- Create: `docs/payments/qpay-name-migration.md`
- Modify: `ChurchReport/文件/Mermaid 檔案/payment-flow-clear-v4.mmd` if needed
- Modify: `ChurchReport/文件/Mermaid 檔案/payment-flow-clear-v6.png` only if diagram is regenerated

- [ ] **Step 1: Create migration documentation**

Create `docs/payments/qpay-name-migration.md`:

```markdown
# QPay Name Migration

## Decision

ChurchReport product workflow classes should not use QPay names unless the code specifically belongs to the Sinopac/QPay provider protocol or a legacy route.

## New Names

| Old name | New name | Status |
| --- | --- | --- |
| QPayProcessor | DonationPaymentProcessor | Product workflow |
| QpayManager | DonationPaymentManager | UI/payment state |
| QPayCreatePaymentGatewayAdapter | DonationPaymentCreateGatewayAdapter | Create-payment adapter |
| QPayReturnWorkflow | DonationPaymentReturnWorkflow | Return workflow |
| QPayWorkflowPaymentResult | DonationPaymentWorkflowResult | Product workflow DTO |
| QPayCardController | PaymentReturnController | Provider return endpoint |
| QPayFeeProcessor | DonationFeePaymentProcessor | Fee return processor |
| QPayDedicationBookingProcessor | RecurringDonationPaymentProcessor | Recurring donation return processor |

## What Still May Use QPay

- Existing public URLs such as `/QPayLogin` and `/Dedication/QPayView/{LineId}`.
- Obsolete compatibility wrappers.
- Sinopac/QPay provider protocol internals.

## Deferred Work

Route and view names such as `QPayView.cshtml` and `QPayLogin.cshtml` require a separate UI migration because users or external links may depend on those URLs.
```

- [ ] **Step 2: Run QPay naming audit**

Run:

```powershell
Select-String -Path 'ChurchReport\**\*.cs','ChurchReport\**\*.cshtml','ChurchReport\**\*.json','ChurchReport\ChurchReport.csproj' -Pattern 'QPay|Qpay|qpay'
```

Expected remaining categories only:

- Sinopac/QPay provider protocol references.
- Obsolete compatibility wrappers.
- Legacy route/view names documented in `docs/payments/qpay-name-migration.md`.
- Comments explicitly describing compatibility.

- [ ] **Step 3: Verify reusable projects are not polluted**

Run:

```powershell
Select-String -Path 'SpeechMessage.Payments\**\*.cs','SpeechMessage.Payments.AspNetCore\**\*.cs','SpeechMessage.Payments.Workflows\**\*.cs' -Pattern 'ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|QPayProcessor|QpayManager|QPayFeeProcessor|QPayDedicationBookingProcessor'
```

Expected: no matches outside Sinopac provider protocol terms in `SpeechMessage.Payments`.

- [ ] **Step 4: Run tests and build**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~Payments" -p:UseSharedCompilation=false
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false
```

Expected:

- Payment tests pass.
- Payment core tests pass.
- Solution build succeeds.
- Existing unrelated warnings may remain but no new errors.

- [ ] **Step 5: Clean generated output**

Run:

```powershell
dotnet build-server shutdown
$root = (Resolve-Path -LiteralPath '.').Path
$targets = @(Get-ChildItem -Path . -Directory -Recurse -Force -Include artifacts,bin,obj)
foreach ($target in $targets) {
    $full = $target.FullName
    if (-not $full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside worktree: $full"
    }
}
foreach ($target in ($targets | Sort-Object FullName -Descending)) {
    Remove-Item -LiteralPath $target.FullName -Recurse -Force
}
```

Expected: no `artifacts`, `bin`, or `obj` directories remain under the worktree.

- [ ] **Step 6: Commit**

Run:

```powershell
git add docs\payments\qpay-name-migration.md ChurchReport SpeechMessage.Payments SpeechMessage.Payments.AspNetCore SpeechMessage.Payments.Workflows ChurchReport.MemberInfo.Tests
git commit -m "docs: document qpay naming migration"
```

---

## Final Verification Checklist

- [ ] `SpeechMessage.Payments` still owns provider protocol only.
- [ ] No ASP.NET controller, CRM, LINE, ToolUtility, or ChurchReport model moved into `SpeechMessage.Payments`.
- [ ] `QPayProcessor` no longer exists as the main product workflow class.
- [ ] `QpayManager` no longer exists as the main UI/payment state class.
- [ ] New code uses `DonationPayment*` or `PaymentReturn*` names.
- [ ] Old QPay names remain only as documented compatibility wrappers/routes/views.
- [ ] Existing public routes remain functional.
- [ ] `LinePayCSharp` is untouched.
- [ ] `bin/`, `obj/`, and `artifacts/` are cleaned before commit.

## Rollback Strategy

Each task keeps compatibility wrappers before deleting old names. If a task fails:

1. Revert only that task's commit.
2. Keep earlier neutral names already validated by tests.
3. Do not revert `SpeechMessage.Payments` core extraction.
4. Do not remove public route compatibility until a separate route migration plan exists.

## Self-Review Result

- Spec coverage: The plan covers the user's requirement to remove misleading QPay product workflow naming while preserving provider-core boundaries and routes.
- Placeholder scan: No `TBD`, `TODO`, or undefined "handle later" implementation steps remain.
- Type consistency: New names consistently use `DonationPayment*` for ChurchReport product workflow and `PaymentReturn*` for return endpoints. QPay names are retained only as obsolete compatibility wrappers or legacy routes.
