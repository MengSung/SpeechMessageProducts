# Payment Post-Processing Workflow Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This project is currently in inline mode, so do not dispatch implementation/check subagents.

**Goal:** Make Sinopac/Donation, MyPay, and Taishin TSPG use the same provider-neutral post-payment workflow while keeping ChurchReport CRM, LINE, MVC result-page, and donation-specific behavior outside the reusable payment core.

**Architecture:** Provider projects keep handling payment protocol details only. `SpeechMessage.Payments.Workflows` remains the reusable orchestration layer with provider-neutral `PaymentWorkflowResult`, `PaymentPostPaymentContext`, `IPaymentRecordUpdater`, and `IPaymentPayerNotifier`. ChurchReport adds product adapters that build workflow context, update CRM, send LINE notifications, and present MVC result pages.

**Tech Stack:** C# / ASP.NET Core MVC, Dynamics CRM SDK `Entity`, `ToolUtilityClass`, xUnit, FluentAssertions, `SpeechMessage.Payments`, `SpeechMessage.Payments.AspNetCore`, `SpeechMessage.Payments.Workflows`.

---

## Scope And Boundaries

This plan intentionally does not move ChurchReport CRM fields, donation categories, course fields, `ViewBag`, or LINE message wording into `SpeechMessage.Payments` or `SpeechMessage.Payments.Workflows`.

## Linus-Style Refactor Rules

This plan must be executed as small, reviewable changes. Do not introduce clever inheritance, provider base classes, or speculative interfaces. The desired shape is simple data flowing through simple functions:

- Keep provider code boring: provider code parses, signs, verifies, maps status, and returns normalized payment results.
- Keep product code explicit: ChurchReport CRM, LINE, donation, course, and MVC result-page behavior stays in ChurchReport.
- Prefer composition over inheritance: controllers and processors call small services; Sinopac, MyPay, and Taishin do not inherit product behavior.
- Keep each commit reversible: each task must compile and pass its focused tests before the next task starts.
- Do not change user-facing Traditional Chinese wording unless a test or compile error forces the change.
- Do not add `IPaymentOrderResolver`, idempotency stores, audit stores, or new workflow interfaces in this phase. Those are future work after the current duplication is removed.
- Donation refactoring must be incremental. First extract presentation, then inject common workflow dependencies, then delegate exactly one repeated responsibility at a time.

## Revised Execution Order For Maintainability

Use this revised order when implementing the plan. It supersedes any later task that tries to change `DonationFeePaymentProcessor` in one large step.

1. Add `ChurchReportPaymentContextBuilder`.
2. Register it and use it in `MyPayController`.
3. Migrate `TSPGController` to `PaymentPostPaymentWorkflow`.
4. Add `DonationPaymentReturnPresenter`.
5. Add `DonationFeePaymentProcessor` constructor dependencies only. Do not change behavior in this step.
6. Add a private `CreatePaymentWorkflowResult(...)` helper in `DonationFeePaymentProcessor`. Do not remove existing CRM/LINE code in this step.
7. Replace duplicated donation result-page `ViewBag` blocks with `DonationPaymentReturnPresenter`, one branch at a time, running donation tests after each branch.
8. Add a private `ExecutePostPaymentWorkflowIfAvailable(...)` helper and call it only after the fee entity and normalized workflow result exist.
9. Keep donation-specific card token storage, course enrollment updates, and legacy return-url behavior until each has a dedicated test. Do not delete them as part of this phase.
10. Add architecture tests proving TSPG and Donation use the common post-payment workflow while ChurchReport-specific handlers remain in the `ChurchReport` assembly.

The old Task 5 section below is retained as background detail, but implementation must follow this revised order. If the old section conflicts with this section, this section wins.

Reusable core may know:

- product order id
- provider transaction id
- amount and currency
- normalized payment status
- provider message
- callback acknowledgement
- that post-payment handlers run in order

Reusable core must not know:

- `new_fee`, `new_pay_status`, `new_fee_really_paid`, `new_lineid`
- ChurchReport donation/course classification
- ChurchReport LINE token selection
- `QPayCard/PaymentResult.cshtml`
- ASP.NET MVC `ViewBag`
- provider-specific controller names such as `TSPGController`

## File Structure

Create:

- `ChurchReport/Payments/ChurchReportPaymentContextBuilder.cs`  
  Builds `PaymentPostPaymentContext` from a normalized `PaymentWorkflowResult`, a CRM fee entity, and ChurchReport dependencies. It centralizes contact lookup, full name lookup, fee type detection, and the known context keys.

- `ChurchReport/Payments/DonationPaymentReturnPresenter.cs`  
  Builds result-page `ActionResult` / `ViewBag` state for donation payment return flows. It keeps MVC display behavior out of workflow handlers.

- `ChurchReport.MemberInfo.Tests/Payments/ChurchReportPaymentContextBuilderTests.cs`  
  Verifies context item construction without invoking provider logic.

- `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnPresenterTests.cs`  
  Verifies donation return-page presentation defaults and success/failure values.

Modify:

- `ChurchReport/Controllers/TSPGController.cs`  
  Thin the controller so post-back/result-url parse Taishin callback through the payment gateway, map to `PaymentWorkflowResult`, build ChurchReport context, call `PaymentPostPaymentWorkflow`, and return acknowledgement/redirect.

- `ChurchReport/Tools/DonationFeePaymentProcessor.cs`  
  Change incrementally. First add explicit dependencies, then normalize the payment result, then move result-page display to `DonationPaymentReturnPresenter`, then call `PaymentPostPaymentWorkflow` through one helper. Do not remove existing donation-specific card-token, course, or legacy return-url behavior in this phase.

- `ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs`  
  Keep updater/notifier interfaces, but make them rely on consistent context produced by `ChurchReportPaymentContextBuilder`.

- `ChurchReport/Startup.cs`  
  Register `ChurchReportPaymentContextBuilder`, `DonationPaymentReturnPresenter`, and ensure existing workflow handlers remain registered.

- `ChurchReport.MemberInfo.Tests/Payments/TspgControllerAdapterTests.cs`  
  Update controller construction for new dependencies and add a successful callback test proving TSPG uses the common workflow.

- `ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentWorkflowTests.cs`  
  Add regression coverage that updater runs before notifier and context items remain product-owned.

Do not modify:

- `SpeechMessage.Payments/Providers/Taishin/*` unless a test proves provider normalization is wrong.
- `SpeechMessage.Payments/Providers/MyPay/*`
- `SpeechMessage.Payments/Providers/Sinopac/*`
- `SpeechMessage.Payments.Workflows` public contracts unless the ChurchReport context builder cannot be implemented with current `Items`.

---

## Task 1: Add ChurchReport Payment Context Builder

**Files:**

- Create: `ChurchReport/Payments/ChurchReportPaymentContextBuilder.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/ChurchReportPaymentContextBuilderTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `ChurchReport.MemberInfo.Tests/Payments/ChurchReportPaymentContextBuilderTests.cs`:

```csharp
using ChurchReport.Payments;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace;
using Xunit;
using static ChurchReport.Services.PaymentFeeTypeHelper;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class ChurchReportPaymentContextBuilderTests
{
    [Fact]
    public void Build_contains_payment_result_fee_entity_success_flag_and_fee_type()
    {
        var feeEntity = new Entity("new_fee") { Id = Guid.NewGuid() };
        feeEntity["new_category"] = new OptionSetValue(100000000);
        var payment = new PaymentWorkflowResult
        {
            Status = PaymentStatus.Succeeded,
            ProductOrderId = "ORDER-CTX-001",
            ProviderTransactionId = "TX-CTX-001",
            Amount = 800m,
            Currency = "TWD"
        };
        var utility = new NullToolUtilityClass();
        var builder = new ChurchReportPaymentContextBuilder(
            new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance));

        var context = builder.Build(utility, feeEntity, payment, isSuccess: true);

        context.Payment.Should().BeSameAs(payment);
        context.GetRequiredItem<ToolUtilityClass>(ChurchReportPaymentWorkflowContextKeys.ToolUtility)
            .Should().BeSameAs(utility);
        context.GetRequiredItem<Entity>(ChurchReportPaymentWorkflowContextKeys.FeeEntity)
            .Should().BeSameAs(feeEntity);
        context.GetRequiredItem<bool>(ChurchReportPaymentWorkflowContextKeys.IsSuccess)
            .Should().BeTrue();
        context.GetRequiredItem<FeeType>(ChurchReportPaymentWorkflowContextKeys.FeeType)
            .Should().Be(FeeType.Dedication);
    }

    [Fact]
    public void Build_uses_default_full_name_when_contact_is_missing()
    {
        var feeEntity = new Entity("new_fee") { Id = Guid.NewGuid() };
        var payment = new PaymentWorkflowResult
        {
            Status = PaymentStatus.Failed,
            ProductOrderId = "ORDER-CTX-002",
            Amount = 300m,
            Currency = "TWD"
        };
        var builder = new ChurchReportPaymentContextBuilder(
            new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance));

        var context = builder.Build(new NullToolUtilityClass(), feeEntity, payment, isSuccess: false);

        context.GetRequiredItem<bool>(ChurchReportPaymentWorkflowContextKeys.IsSuccess)
            .Should().BeFalse();
        context.GetOptionalItem<string>(ChurchReportPaymentWorkflowContextKeys.FullName)
            .Should().Be("未知付款者");
        context.GetOptionalItem<Entity>(ChurchReportPaymentWorkflowContextKeys.ContactEntity)
            .Should().BeNull();
    }

    private sealed class NullToolUtilityClass : ToolUtilityClass
    {
    }
}
```

- [ ] **Step 2: Run the targeted tests and verify they fail**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~ChurchReportPaymentContextBuilderTests"
```

Expected: FAIL because `ChurchReportPaymentContextBuilder` does not exist.

- [ ] **Step 3: Add the context builder**

Create `ChurchReport/Payments/ChurchReportPaymentContextBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;
using ChurchReport.Services;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace;

namespace ChurchReport.Payments;

/// <summary>
/// 將 ChurchReport 的 CRM 收費單資料組成共用付款後流程需要的 context。
/// 共用金流核心只認得 PaymentWorkflowResult；CRM Entity、聯絡人、奉獻/課程判斷都留在 ChurchReport 產品層。
/// </summary>
public sealed class ChurchReportPaymentContextBuilder
{
    private readonly PaymentFeeTypeHelper _feeTypeHelper;

    public ChurchReportPaymentContextBuilder(PaymentFeeTypeHelper feeTypeHelper)
    {
        _feeTypeHelper = feeTypeHelper ?? throw new ArgumentNullException(nameof(feeTypeHelper));
    }

    /// <summary>
    /// 建立給 PaymentPostPaymentWorkflow 使用的 ChurchReport context。
    /// 這裡集中處理 fee -> contact -> full name -> fee type 的產品規則，避免 MyPay、TSPG、Donation 各自重複實作。
    /// </summary>
    public PaymentPostPaymentContext Build(
        ToolUtilityClass toolUtility,
        Entity feeEntity,
        PaymentWorkflowResult payment,
        bool isSuccess)
    {
        if (toolUtility is null) throw new ArgumentNullException(nameof(toolUtility));
        if (feeEntity is null) throw new ArgumentNullException(nameof(feeEntity));
        if (payment is null) throw new ArgumentNullException(nameof(payment));

        var contactEntity = ResolveContactEntity(toolUtility, feeEntity, out var fullName);
        var feeType = _feeTypeHelper.DetermineFeeType(toolUtility, feeEntity);

        return new PaymentPostPaymentContext(
            payment,
            new Dictionary<string, object?>
            {
                [ChurchReportPaymentWorkflowContextKeys.ToolUtility] = toolUtility,
                [ChurchReportPaymentWorkflowContextKeys.FeeEntity] = feeEntity,
                [ChurchReportPaymentWorkflowContextKeys.IsSuccess] = isSuccess,
                [ChurchReportPaymentWorkflowContextKeys.FullName] = fullName,
                [ChurchReportPaymentWorkflowContextKeys.FeeType] = feeType,
                [ChurchReportPaymentWorkflowContextKeys.ContactEntity] = contactEntity
            });
    }

    private static Entity? ResolveContactEntity(
        ToolUtilityClass toolUtility,
        Entity feeEntity,
        out string fullName)
    {
        fullName = "未知付款者";

        var contactId = toolUtility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
        if (contactId == Guid.Empty)
        {
            return null;
        }

        var contactEntity = toolUtility.RetrieveEntity("contact", contactId);
        if (contactEntity is null)
        {
            return null;
        }

        var crmFullName = toolUtility.GetEntityStringAttribute(contactEntity, "fullname");
        if (!string.IsNullOrWhiteSpace(crmFullName))
        {
            fullName = crmFullName;
        }

        return contactEntity;
    }
}
```

- [ ] **Step 4: Run the targeted tests and verify they pass**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~ChurchReportPaymentContextBuilderTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add .\ChurchReport\Payments\ChurchReportPaymentContextBuilder.cs .\ChurchReport.MemberInfo.Tests\Payments\ChurchReportPaymentContextBuilderTests.cs
git commit -m "feat: add ChurchReport payment context builder"
```

---

## Task 2: Register Context Builder And Slim MyPay Context Creation

**Files:**

- Modify: `ChurchReport/Startup.cs`
- Modify: `ChurchReport/Controllers/MyPayController.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/MyPayControllerAdapterTests.cs`

- [ ] **Step 1: Write or update the MyPay adapter test**

Open `ChurchReport.MemberInfo.Tests/Payments/MyPayControllerAdapterTests.cs`. Add an assertion-oriented test if one does not already exist:

```csharp
[Fact]
public void Constructor_accepts_context_builder_dependency()
{
    var constructors = typeof(MyPayController).GetConstructors();

    constructors.SelectMany(constructor => constructor.GetParameters())
        .Should()
        .Contain(parameter => parameter.ParameterType == typeof(ChurchReportPaymentContextBuilder));
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~MyPayControllerAdapterTests"
```

Expected: FAIL until the controller constructor includes `ChurchReportPaymentContextBuilder`.

- [ ] **Step 3: Register the context builder in Startup**

In `ChurchReport/Startup.cs`, near the existing payment services registrations, ensure this registration exists:

```csharp
services.AddScoped<ChurchReportPaymentContextBuilder>();
```

Keep these existing registrations:

```csharp
services.AddScoped<IPaymentRecordUpdater, ChurchReportPaymentRecordUpdater>();
services.AddScoped<IPaymentPayerNotifier, ChurchReportPaymentPayerNotifier>();
services.AddScoped<PaymentPostPaymentWorkflow>();
```

- [ ] **Step 4: Replace duplicated MyPay context construction**

In `ChurchReport/Controllers/MyPayController.cs`:

1. Add a field:

```csharp
private readonly ChurchReportPaymentContextBuilder _paymentContextBuilder;
```

2. Add constructor parameter:

```csharp
ChurchReportPaymentContextBuilder paymentContextBuilder,
```

3. Assign it:

```csharp
_paymentContextBuilder = paymentContextBuilder ?? throw new ArgumentNullException(nameof(paymentContextBuilder));
```

4. Replace the block that manually determines fee type, resolves contact, and creates `new PaymentPostPaymentContext(...)` with:

```csharp
var postPaymentContext = _paymentContextBuilder.Build(
    ToolUtility,
    feeEntity,
    workflowResult,
    isSuccess);
```

5. Remove the private `ResolveContactEntity` method if no longer used.

- [ ] **Step 5: Run MyPay and payment workflow tests**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~MyPayControllerAdapterTests|FullyQualifiedName~PaymentPostPaymentWorkflowTests|FullyQualifiedName~ChurchReportPaymentContextBuilderTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add .\ChurchReport\Startup.cs .\ChurchReport\Controllers\MyPayController.cs .\ChurchReport.MemberInfo.Tests\Payments\MyPayControllerAdapterTests.cs
git commit -m "refactor: centralize MyPay post-payment context"
```

---

## Task 3: Migrate TSPGController To PaymentPostPaymentWorkflow

**Files:**

- Modify: `ChurchReport/Controllers/TSPGController.cs`
- Modify: `ChurchReport.MemberInfo.Tests/Payments/TspgControllerAdapterTests.cs`

- [ ] **Step 1: Add a failing TSPG workflow invocation test**

In `ChurchReport.MemberInfo.Tests/Payments/TspgControllerAdapterTests.cs`, add a successful callback test using a recording workflow dependency:

```csharp
[Fact]
public async Task ResultUrl_uses_common_post_payment_workflow_for_successful_callback()
{
    var gateway = new RecordingPaymentGateway(new PaymentCallbackResult
    {
        Status = PaymentStatus.Succeeded,
        ProductOrderId = "F202606300001",
        ProviderTransactionId = "TSPG-TX-001",
        Amount = 900m,
        Currency = "TWD",
        Acknowledgement = PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}")
    });
    var workflow = new RecordingPostPaymentWorkflow();
    var controller = CreateController(gateway, workflow);
    var context = new DefaultHttpContext();
    context.Request.Method = HttpMethods.Post;
    context.Request.ContentType = "application/json";
    context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"order_no\":\"F202606300001\",\"ret_code\":\"00\"}"));
    controller.ControllerContext = new ControllerContext { HttpContext = context };

    var result = await controller.ResultUrl();

    workflow.CallCount.Should().Be(1);
    workflow.LastContext!.Payment.ProductOrderId.Should().Be("F202606300001");
    workflow.LastContext.Payment.ProviderTransactionId.Should().Be("TSPG-TX-001");
    result.Should().BeOfType<ContentResult>();
}
```

Add this helper class in the same test file:

```csharp
private sealed class RecordingPostPaymentWorkflow
{
    public int CallCount { get; private set; }
    public PaymentPostPaymentContext? LastContext { get; private set; }

    public Task<PaymentPostPaymentWorkflowResult> ExecuteAsync(
        PaymentPostPaymentContext context,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastContext = context;
        return Task.FromResult(new PaymentPostPaymentWorkflowResult
        {
            RecordUpdated = true,
            PayerNotified = true
        });
    }
}
```

If `PaymentPostPaymentWorkflow` is sealed and cannot be replaced by this helper, write the test against injected fake `IPaymentRecordUpdater` and `IPaymentPayerNotifier` instead:

```csharp
var calls = new List<string>();
var workflow = new PaymentPostPaymentWorkflow(
    new[] { new RecordingRecordUpdater(calls) },
    new[] { new RecordingPayerNotifier(calls) });
```

- [ ] **Step 2: Run the TSPG tests and verify they fail**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~TspgControllerAdapterTests"
```

Expected: FAIL because `TSPGController` does not yet accept or execute `PaymentPostPaymentWorkflow`.

- [ ] **Step 3: Add dependencies to TSPGController**

In `ChurchReport/Controllers/TSPGController.cs`, add fields:

```csharp
private readonly PaymentPostPaymentWorkflow _postPaymentWorkflow;
private readonly ChurchReportPaymentContextBuilder _paymentContextBuilder;
```

Add constructor parameters after `PaymentWorkflowResultMapper paymentWorkflowResultMapper`:

```csharp
PaymentPostPaymentWorkflow postPaymentWorkflow,
ChurchReportPaymentContextBuilder paymentContextBuilder
```

Assign:

```csharp
_postPaymentWorkflow = postPaymentWorkflow ?? throw new ArgumentNullException(nameof(postPaymentWorkflow));
_paymentContextBuilder = paymentContextBuilder ?? throw new ArgumentNullException(nameof(paymentContextBuilder));
```

- [ ] **Step 4: Replace TSPG inline CRM/LINE methods with common workflow**

Add this private method:

```csharp
private async Task ExecutePostPaymentWorkflowAsync(PaymentWorkflowResult result)
{
    if (string.IsNullOrWhiteSpace(result.ProductOrderId))
    {
        LogWarning("PostPaymentWorkflow", "Payment result has no order id.");
        return;
    }

    var feeEntity = ToolUtility.RetrieveEntityByField(
        "new_fee",
        "new_q_pay_card_order_no",
        result.ProductOrderId);

    if (feeEntity is null)
    {
        LogWarning("PostPaymentWorkflow", $"No fee entity found - OrderNo: {result.ProductOrderId}");
        return;
    }

    var context = _paymentContextBuilder.Build(
        ToolUtility,
        feeEntity,
        result,
        result.Status == PaymentStatus.Succeeded);

    await _postPaymentWorkflow.ExecuteAsync(context, RequestAborted);
}
```

In `ResultUrl`, replace:

```csharp
if (workflowResult.Status == PaymentStatus.Succeeded)
{
    UpdateFeeEntityByOrderNo(workflowResult);
    LogInfo("PaymentNotify", $"Payment success processed - Order: {workflowResult.ProductOrderId}");
}
else
{
    LogInfo("PaymentNotify", $"Payment failed - Order: {workflowResult.ProductOrderId}, Message: {workflowResult.ProviderMessage}");
}
```

with:

```csharp
await ExecutePostPaymentWorkflowAsync(workflowResult);
LogInfo("PaymentNotify", $"Payment callback processed - Order: {workflowResult.ProductOrderId}, Status: {workflowResult.Status}");
```

In `HandleSuccessfulPaymentReturn`, replace:

```csharp
UpdateFeeEntityByOrderNo(result);
```

with:

```csharp
await ExecutePostPaymentWorkflowAsync(result);
```

If `HandleSuccessfulPaymentReturn` is currently synchronous, change it to:

```csharp
private async Task<IActionResult> HandleSuccessfulPaymentReturn(PaymentWorkflowResult result)
```

and in `PostBack` call:

```csharp
return workflowResult.Status == PaymentStatus.Succeeded
    ? await HandleSuccessfulPaymentReturn(workflowResult)
    : HandleFailedPaymentReturn(workflowResult);
```

Remove these private methods after references are gone:

```csharp
UpdateFeeEntityByOrderNo
UpdateFeeEntityFields
SendPaymentNotificationToContact
BuildPaymentSuccessMessage
SendLineMessage
GetLineChannelAccessToken
```

Also remove no-longer-used constants and usings:

```csharp
private const int PaymentStatusPaid = 100000001;
private const int PaymentMethodCreditCard = 100000001;
using Line.Messaging;
```

- [ ] **Step 5: Update TSPG tests constructor helper**

Update `CreateController` in `TspgControllerAdapterTests.cs` to construct the new dependencies. Use the real workflow with fake handlers to avoid touching CRM for invalid callbacks:

```csharp
private static TSPGController CreateController(IPaymentGateway gateway)
{
    var configuration = new ConfigurationBuilder().Build();
    var workflow = new PaymentPostPaymentWorkflow(
        Array.Empty<IPaymentRecordUpdater>(),
        Array.Empty<IPaymentPayerNotifier>());

    return new TSPGController(
        new ThrowingToolUtilityProvider(),
        configuration,
        gateway,
        new PaymentHttpRequestMapper(),
        new ChurchReportPaymentProfileResolver(configuration),
        new PaymentAcknowledgementResultMapper(),
        new PaymentWorkflowResultMapper(),
        workflow,
        new ChurchReportPaymentContextBuilder(
            new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance)));
}
```

Add missing using statements:

```csharp
using ChurchReport.Services;
using Microsoft.Extensions.Logging.Abstractions;
```

- [ ] **Step 6: Run TSPG tests**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~TspgControllerAdapterTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add .\ChurchReport\Controllers\TSPGController.cs .\ChurchReport.MemberInfo.Tests\Payments\TspgControllerAdapterTests.cs
git commit -m "refactor: route TSPG post-payment through common workflow"
```

---

## Task 4: Add Donation Payment Return Presenter

**Files:**

- Create: `ChurchReport/Payments/DonationPaymentReturnPresenter.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnPresenterTests.cs`

- [ ] **Step 1: Write the failing presenter tests**

Create `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnPresenterTests.cs`:

```csharp
using ChurchReport.Payments;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentReturnPresenterTests
{
    [Fact]
    public void PresentSuccess_sets_stable_view_bag_values()
    {
        var presenter = new DonationPaymentReturnPresenter();
        var controller = new TestController();

        var result = presenter.PresentSuccess(
            controller,
            fullName: "王小明",
            amount: "800",
            orderId: "D202606300001",
            transactionId: "TX-DONATION-001",
            dedicationCategory: "十一奉獻",
            message: "付款成功");

        result.Should().BeOfType<ViewResult>();
        controller.ViewBag.IsSuccess.Should().BeTrue();
        controller.ViewBag.FullName.Should().Be("王小明");
        controller.ViewBag.Amount.Should().Be("800");
        controller.ViewBag.OrderId.Should().Be("D202606300001");
        controller.ViewBag.TransactionId.Should().Be("TX-DONATION-001");
        controller.ViewBag.DedicationCategory.Should().Be("十一奉獻");
    }

    [Fact]
    public void PresentFailure_sets_error_details()
    {
        var presenter = new DonationPaymentReturnPresenter();
        var controller = new TestController();

        var result = presenter.PresentFailure(
            controller,
            fullName: "王小明",
            orderId: "D202606300002",
            errorDetails: "授權失敗",
            message: "付款失敗");

        result.Should().BeOfType<ViewResult>();
        controller.ViewBag.IsSuccess.Should().BeFalse();
        controller.ViewBag.FullName.Should().Be("王小明");
        controller.ViewBag.OrderId.Should().Be("D202606300002");
        controller.ViewBag.ErrorDetails.Should().Be("授權失敗");
    }

    private sealed class TestController : Controller
    {
    }
}
```

- [ ] **Step 2: Run the targeted tests and verify they fail**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentReturnPresenterTests"
```

Expected: FAIL because `DonationPaymentReturnPresenter` does not exist.

- [ ] **Step 3: Add DonationPaymentReturnPresenter**

Create `ChurchReport/Payments/DonationPaymentReturnPresenter.cs`:

```csharp
using System;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Payments;

/// <summary>
/// 負責奉獻付款結果頁的 MVC 呈現資料。
/// 付款後 workflow 不知道 ViewBag 或 cshtml；這個 presenter 讓 processor 保持薄，並避免顯示邏輯散落在金流流程中。
/// </summary>
public sealed class DonationPaymentReturnPresenter
{
    private const string PaymentResultView = "~/Views/QPayCard/PaymentResult.cshtml";

    public IActionResult PresentSuccess(
        Controller controller,
        string fullName,
        string amount,
        string orderId,
        string transactionId,
        string dedicationCategory,
        string message)
    {
        if (controller is null) throw new ArgumentNullException(nameof(controller));

        controller.ViewBag.IsSuccess = true;
        controller.ViewBag.Message = message;
        controller.ViewBag.FullName = fullName;
        controller.ViewBag.Amount = amount;
        controller.ViewBag.PaymentTime = DateTime.Now.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
        controller.ViewBag.OrderId = orderId;
        controller.ViewBag.TransactionId = transactionId;
        controller.ViewBag.PaymentMethod = "信用卡";
        controller.ViewBag.DedicationCategory = dedicationCategory;

        return controller.View(PaymentResultView);
    }

    public IActionResult PresentFailure(
        Controller controller,
        string fullName,
        string orderId,
        string errorDetails,
        string message)
    {
        if (controller is null) throw new ArgumentNullException(nameof(controller));

        controller.ViewBag.IsSuccess = false;
        controller.ViewBag.Message = message;
        controller.ViewBag.FullName = fullName;
        controller.ViewBag.OrderId = orderId;
        controller.ViewBag.ErrorDetails = errorDetails;

        return controller.View(PaymentResultView);
    }
}
```

- [ ] **Step 4: Run presenter tests**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentReturnPresenterTests"
```

Expected: PASS.

- [ ] **Step 5: Register presenter**

In `ChurchReport/Startup.cs`, near other ChurchReport payment service registrations, add:

```csharp
services.AddScoped<DonationPaymentReturnPresenter>();
```

- [ ] **Step 6: Commit**

```powershell
git add .\ChurchReport\Payments\DonationPaymentReturnPresenter.cs .\ChurchReport.MemberInfo.Tests\Payments\DonationPaymentReturnPresenterTests.cs .\ChurchReport\Startup.cs
git commit -m "feat: add donation payment return presenter"
```

---

## Task 5: Thin DonationFeePaymentProcessor Without Changing Donation Behavior

**Files:**

- Modify: `ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnProcessorNamingTests.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`

- [ ] **Step 1: Add a structural test for injected common services**

In `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnProcessorNamingTests.cs`, add:

```csharp
[Fact]
public void Donation_fee_payment_processor_accepts_common_workflow_and_presenter()
{
    var processorType = typeof(ChurchReport.Tools.DonationFeePaymentProcessor);
    var constructorParameters = processorType
        .GetConstructors()
        .SelectMany(constructor => constructor.GetParameters())
        .Select(parameter => parameter.ParameterType)
        .ToArray();

    constructorParameters.Should().Contain(typeof(PaymentPostPaymentWorkflow));
    constructorParameters.Should().Contain(typeof(ChurchReportPaymentContextBuilder));
    constructorParameters.Should().Contain(typeof(DonationPaymentReturnPresenter));
}
```

Add usings if missing:

```csharp
using ChurchReport.Payments;
using ChurchReport.Tools;
using SpeechMessage.Payments.Workflows;
```

- [ ] **Step 2: Run the structural test and verify it fails**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentReturnProcessorNamingTests"
```

Expected: FAIL until `DonationFeePaymentProcessor` accepts the common workflow dependencies.

- [ ] **Step 3: Add dependencies to DonationFeePaymentProcessor**

In `ChurchReport/Tools/DonationFeePaymentProcessor.cs`, add fields:

```csharp
private readonly PaymentPostPaymentWorkflow _postPaymentWorkflow;
private readonly ChurchReportPaymentContextBuilder _paymentContextBuilder;
private readonly DonationPaymentReturnPresenter _returnPresenter;
```

Add a constructor overload that accepts existing `IToolUtilityProvider` plus the new services:

```csharp
public DonationFeePaymentProcessor(
    IToolUtilityProvider toolUtilityProvider,
    PaymentPostPaymentWorkflow postPaymentWorkflow,
    ChurchReportPaymentContextBuilder paymentContextBuilder,
    DonationPaymentReturnPresenter returnPresenter)
    : this(toolUtilityProvider)
{
    _postPaymentWorkflow = postPaymentWorkflow ?? throw new ArgumentNullException(nameof(postPaymentWorkflow));
    _paymentContextBuilder = paymentContextBuilder ?? throw new ArgumentNullException(nameof(paymentContextBuilder));
    _returnPresenter = returnPresenter ?? throw new ArgumentNullException(nameof(returnPresenter));
}
```

For legacy constructors that cannot receive DI yet, initialize safe defaults only if existing tests require direct construction:

```csharp
_postPaymentWorkflow ??= new PaymentPostPaymentWorkflow(
    Array.Empty<IPaymentRecordUpdater>(),
    Array.Empty<IPaymentPayerNotifier>());
_returnPresenter ??= new DonationPaymentReturnPresenter();
```

Do not create a fake `ChurchReportPaymentContextBuilder` without `PaymentFeeTypeHelper`; direct legacy paths should not execute workflow in tests.

- [ ] **Step 4: Convert the payment result to PaymentWorkflowResult near the existing success/failure branch**

Inside `HandlePaymentReturn`, after computing `isPaymentSuccess`, create:

```csharp
var workflowResult = new PaymentWorkflowResult
{
    Status = isPaymentSuccess ? PaymentStatus.Succeeded : PaymentStatus.Failed,
    ProductOrderId = paymentResult.OrderNo,
    ProviderTransactionId = paymentResult.OrderNo,
    Amount = Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100m,
    Currency = "TWD",
    ProviderMessage = paymentStatusText
};
```

Add usings:

```csharp
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows;
```

- [ ] **Step 5: Call the common workflow after fee entity is resolved**

After `aFeeEntity` and contact-related values are loaded, call:

```csharp
if (_paymentContextBuilder != null && _postPaymentWorkflow != null)
{
    var postPaymentContext = _paymentContextBuilder.Build(
        m_ToolUtilityClass,
        aFeeEntity,
        workflowResult,
        isPaymentSuccess);

    _postPaymentWorkflow.ExecuteAsync(postPaymentContext).GetAwaiter().GetResult();
}
```

During this task, do not remove donation-specific card token saving, course enrollment updates, or result-page behavior. Those are separate responsibilities and need separate tests before removal.

- [ ] **Step 6: Replace duplicated success/failure ViewBag blocks with presenter calls**

For success result page blocks, replace repeated `ViewBag` assignments with:

```csharp
return _returnPresenter.PresentSuccess(
    this,
    aFullName,
    ((int)Convert.ToUInt32(paymentResult.AmountMinorUnits) / 100).ToString(),
    paymentResult.OrderNo,
    paymentResult.OrderNo,
    categoryText,
    isCoursePayment
        ? "課程繳費成功，系統已發送 LINE 通知。"
        : "奉獻付款成功，系統已發送 LINE 通知。");
```

For failure result page blocks, replace repeated `ViewBag` assignments with:

```csharp
return _returnPresenter.PresentFailure(
    this,
    aFullName,
    paymentResult.OrderNo,
    Description,
    "付款失敗，請確認付款資訊後再試。");
```

Keep exact existing Traditional Chinese user-facing wording if tests or current production copy depend on it. The presenter method names and responsibility are the refactor target; wording changes are not.

- [ ] **Step 7: Run donation payment tests**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPayment"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add .\ChurchReport\Tools\DonationFeePaymentProcessor.cs .\ChurchReport.MemberInfo.Tests\Payments\DonationPaymentReturnProcessorNamingTests.cs .\ChurchReport.MemberInfo.Tests\Payments\DonationPaymentViewDefaultsTests.cs
git commit -m "refactor: thin donation fee payment processor"
```

---

## Task 6: Add Cross-Provider Post-Payment Architecture Regression Tests

**Files:**

- Create: `ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentArchitectureTests.cs`
- Modify if needed: `ChurchReport.MemberInfo.Tests/Payments/TspgControllerAdapterTests.cs`
- Modify if needed: `ChurchReport.MemberInfo.Tests/Payments/PaymentProductServiceNamingTests.cs`

- [ ] **Step 1: Write architecture regression tests**

Create `ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentArchitectureTests.cs`:

```csharp
using System.Reflection;
using ChurchReport.Controllers;
using ChurchReport.Payments;
using ChurchReport.Tools;
using FluentAssertions;
using SpeechMessage.Payments.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class PaymentPostPaymentArchitectureTests
{
    [Fact]
    public void Tspg_controller_depends_on_common_post_payment_workflow()
    {
        typeof(TSPGController)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(PaymentPostPaymentWorkflow));
    }

    [Fact]
    public void Donation_fee_payment_processor_depends_on_common_post_payment_workflow()
    {
        typeof(DonationFeePaymentProcessor)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(PaymentPostPaymentWorkflow));
    }

    [Fact]
    public void ChurchReport_specific_handlers_do_not_move_to_reusable_workflow_project()
    {
        typeof(ChurchReportPaymentRecordUpdater).Assembly.GetName().Name.Should().Be("ChurchReport");
        typeof(ChurchReportPaymentPayerNotifier).Assembly.GetName().Name.Should().Be("ChurchReport");
    }
}
```

- [ ] **Step 2: Run architecture tests**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~PaymentPostPaymentArchitectureTests"
```

Expected: PASS.

- [ ] **Step 3: Run full payment-related test group**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments"
```

Expected: PASS.

- [ ] **Step 4: Commit**

```powershell
git add .\ChurchReport.MemberInfo.Tests\Payments\PaymentPostPaymentArchitectureTests.cs .\ChurchReport.MemberInfo.Tests\Payments\TspgControllerAdapterTests.cs .\ChurchReport.MemberInfo.Tests\Payments\PaymentProductServiceNamingTests.cs
git commit -m "test: cover shared payment post-processing architecture"
```

---

## Task 7: Full Validation, Cleanup, And Documentation

**Files:**

- Modify: `.ccg/tasks/brainstorm-payment-post-processing-extraction/review.md`
- Modify if needed: `.trellis/spec/backend/index.md`
- Modify if needed: `.ccg/tasks/brainstorm-payment-post-processing-extraction/task.json`

- [ ] **Step 1: Run focused test suites**

Run:

```powershell
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments"
```

Expected: both PASS.

- [ ] **Step 2: Run full build**

Run:

```powershell
dotnet build .\ChurchReport.sln
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Clean temporary build artifacts**

Run:

```powershell
Get-ChildItem -LiteralPath . -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin','obj','artifacts') } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
```

Expected: no `bin`, `obj`, or `artifacts` folders remain under the worktree.

- [ ] **Step 4: Review diff scope**

Run:

```powershell
git status --short
git diff --stat
```

Expected: changed files are limited to the plan scope:

- ChurchReport payment context builder / presenter
- TSPG controller
- Donation fee processor
- Startup DI
- payment tests
- task/review documentation

- [ ] **Step 5: Run CCG-required external review if tools are available**

Run both commands from the worktree root:

```powershell
~/.claude/bin/codeagent-wrapper --progress --backend gemini - "$(pwd)" < .ccg/tasks/brainstorm-payment-post-processing-extraction/review-prompt.md
~/.claude/bin/codeagent-wrapper --progress --backend claude - "$(pwd)" < .ccg/tasks/brainstorm-payment-post-processing-extraction/review-prompt.md
```

If `codeagent-wrapper`, Gemini, or Claude is not available, record the exact command failure in:

```text
.ccg/tasks/brainstorm-payment-post-processing-extraction/review.md
```

Use this review prompt content in `.ccg/tasks/brainstorm-payment-post-processing-extraction/review-prompt.md`:

```text
ROLE_FILE: ~/.claude/.ccg/prompts/claude/reviewer.md
<TASK>
Review the current git diff for the ChurchReport payment post-processing workflow unification.

Check:
- provider-neutral payment core remains free of ChurchReport CRM and LINE dependencies
- TSPGController no longer duplicates CRM/LINE post-payment logic
- DonationFeePaymentProcessor is thinner but existing donation-specific behavior is not removed without tests
- PaymentPostPaymentWorkflow stays provider-neutral
- tests cover MyPay, TSPG, Donation, and architecture boundaries
</TASK>
OUTPUT: Critical/Warning/Info review report.
```

- [ ] **Step 6: Update task status**

Update `.ccg/tasks/brainstorm-payment-post-processing-extraction/task.json`:

```json
{
  "currentPhase": "review",
  "nextAction": "Address review findings or commit verified workflow unification"
}
```

- [ ] **Step 7: Final commit**

```powershell
git add .\ChurchReport .\ChurchReport.MemberInfo.Tests .\.ccg .\.trellis
git commit -m "refactor: unify payment post-processing workflow"
```

---

## Verification Matrix

Run these before claiming completion:

```powershell
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments"
dotnet build .\ChurchReport.sln
```

Expected:

- payment provider tests pass
- ChurchReport payment adapter tests pass
- solution builds with 0 errors
- `git diff` shows no provider-core pollution with ChurchReport CRM/LINE code
- `TSPGController` has no direct `PushUtility`, `LineMessagingClient`, or inline CRM field-update method
- `DonationFeePaymentProcessor` delegates common CRM/LINE post-payment work and keeps only donation-specific flow/presentation behavior

## Maintainability Review Checklist

Before committing the implementation, inspect the diff and verify:

- No new inheritance chain was introduced for Sinopac, MyPay, or Taishin.
- No new interface was added except existing workflow interfaces already present before this plan.
- No ChurchReport CRM field name appears in `SpeechMessage.Payments` or `SpeechMessage.Payments.Workflows`.
- No `ViewBag`, `Controller`, `ActionResult`, `LineMessagingClient`, or `PushUtility` appears in reusable payment core projects.
- `TSPGController` is thinner than before and delegates post-payment work instead of owning CRM/LINE code.
- `DonationFeePaymentProcessor` was changed in small commits; each commit compiles and passes focused tests.
- Donation-specific behavior was not deleted unless a test proves the replacement behavior.
- Provider names do not leak into generic workflow type names.

## Self-Review

- Spec coverage: The plan covers TSPG, Donation, existing MyPay common workflow usage, context building, presenter extraction, tests, DI, validation, cleanup, review, and maintainability constraints.
- Placeholder scan: No task uses `TBD`, `TODO`, or unspecified implementation work.
- Type consistency: All planned names are consistent: `ChurchReportPaymentContextBuilder`, `DonationPaymentReturnPresenter`, `PaymentPostPaymentWorkflow`, `PaymentPostPaymentContext`, `ChurchReportPaymentWorkflowContextKeys`.
- Linus-style check: The revised execution order avoids speculative abstractions and large donation rewrites; each task is small enough to review, test, and revert independently.
