# Neutral Payment DTO And QPay Name Containment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `QPay` naming from spreading across product-neutral payment flows, ensure only the Sinopac-specific provider path contains QPay-named code, and extract reusable payment form/order DTOs that future ASP.NET Core products can call before creating provider-specific payments.

**Architecture:** Keep `SpeechMessage.Payments` focused on provider protocol execution. Put future-product reusable form/order contracts in `SpeechMessage.Payments.Workflows`, then map those contracts into existing `SpeechMessage.Payments.Models.PaymentCreateRequest`. Keep ChurchReport donation, CRM, LINE, MVC, DevExtreme, and legacy route behavior in ChurchReport, but route all provider-neutral payment flows through `Payment` / `DonationPayment` names. Any `QPay` code path must be isolated to Sinopac provider protocol. Legacy external URLs may be preserved with route attributes on neutral controllers, but product-layer `QPay` type aliases should be removed rather than preserved.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, ASP.NET Core MVC, existing `SpeechMessage.Payments`, `SpeechMessage.Payments.Workflows`, and `ChurchReport.MemberInfo.Tests`.

---

## Design Rules

1. `QPay` is allowed only when the code is truly Sinopac/QPay provider protocol or a legacy externally visible route/action that must remain compatible.
2. Code used by Sinopac, MyPay, and Taishin must use neutral names: `Payment`, `DonationPayment`, `PaymentOrderDraft`, `PaymentPayerDraft`, `PaymentScheduleDraft`.
3. Reusable DTOs must not contain ChurchReport words such as `Dedication`, `CRM`, `LINE`, `Contact`, `ToolUtility`, `ViewBag`, or `DevExtreme`.
4. ChurchReport product models may contain ChurchReport donation words, but not provider names. The only product-layer exception is a literal legacy route template required by old external URLs/callbacks.
5. Do not move ASP.NET controllers, CRM updates, LINE notifications, MVC views, or ChurchReport donation classifications into reusable payment projects.
6. Prefer small mappers and immutable DTO records over inheritance. Keep behavior obvious and testable.
7. Each task below should be committed separately after tests pass.
8. Product-layer file names must also be neutral. Do not keep `QPay` / `Qpay` in ChurchReport file names merely for class compatibility; preserve old URLs with route attributes on neutral controllers.
9. Do not keep product-layer `QPay` / `Qpay` type aliases. This is a same-solution refactor, so update source callers to neutral names instead of carrying dirty compatibility classes.
10. MyPay, Taishin, Line Pay, and provider-neutral ChurchReport flows must never call a `QPay*` / `Qpay*` type directly. They must call neutral interfaces/classes, such as `IDonationPaymentCreateGatewayAdapter`, `DonationPaymentCreateGatewayAdapter`, `PaymentReturnController`, or `IPaymentGateway`.
11. Legacy `QPay` wording is allowed only inside explicit route-template strings needed by existing URLs/callbacks. C# class names, action method names, parameters, variables, DTOs, services, file names, and test names must use neutral payment names.

## File Map

### Create

- `SpeechMessage.Payments.Workflows/PaymentOrderDraft.cs`
  Product-neutral draft order submitted by any host product before provider create-payment.

- `SpeechMessage.Payments.Workflows/PaymentPayerDraft.cs`
  Product-neutral payer identity and contact data.

- `SpeechMessage.Payments.Workflows/PaymentLineItemDraft.cs`
  Product-neutral payable item data.

- `SpeechMessage.Payments.Workflows/PaymentMethodSelection.cs`
  Product-neutral payment method and provider metadata selection.

- `SpeechMessage.Payments.Workflows/PaymentScheduleDraft.cs`
  Product-neutral recurring-payment schedule.

- `SpeechMessage.Payments.Workflows/PaymentOrderDraftMapper.cs`
  Converts `PaymentOrderDraft` into `SpeechMessage.Payments.Models.PaymentCreateRequest`.

- `SpeechMessage.Payments.Tests/Workflows/PaymentOrderDraftMapperTests.cs`
  Verifies reusable DTO mapping into provider execution DTOs.

- `ChurchReport/Models/DonationPaymentFormModel.cs`
  ChurchReport donation form model that replaces `QpayModel` as the primary type name.

- `ChurchReport/Payments/DonationPaymentFormModelMapper.cs`
  Converts ChurchReport donation form state into `PaymentOrderDraft`.

- `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs`
  Locks primary ChurchReport naming and the rule that legacy `QPay` wording may appear only in explicit route templates.

- `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelMapperTests.cs`
  Verifies donation form data maps into reusable neutral DTOs.

### Modify

- `ChurchReport/Models/QpayModel.cs`
  Delete after moving the real implementation to `DonationPaymentFormModel.cs`; do not keep a `QpayModel` alias.

- `ChurchReport/Models/QpayManager.cs`
  Delete after all callers use `DonationPaymentManager`; do not keep a `QpayManager` alias.

- `ChurchReport/Models/DonationPaymentManager.cs`
  Replace internal primary state from `m_QpayModel` to `m_DonationPaymentFormModel`; update callers in the same task instead of keeping a bridge property.

- `ChurchReport/Controllers/DedicationController.cs`
  Rename internal variables and action parameters from `QpayModel` to `DonationPaymentFormModel`. If an old external URL still contains `QPay`, preserve that only in route metadata, not in C# type, parameter, or local variable names.

- `ChurchReport/Controllers/DedicationAuditController.cs`
  Rename action parameters and local variables from `QpayModel` to `DonationPaymentFormModel`.

- `ChurchReport/Controllers/BaseChurchController.cs`
  Read donation payment state through neutral `DonationPaymentManager` / form model naming.

- `ChurchReport/Controllers/QPayLoginController.cs`
  Rename the file/class to a neutral name such as `DonationPaymentLoginController`; preserve existing `/QPayLogin/...` URLs with route/action attributes if external callers still use them.

- `ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
  Keep `DonationPaymentManager` as primary. Remove `QpayManager` bridge and update all callers.

- `ChurchReport/Models/IInMemoryDataContext.cs`
  Keep only primary `DonationPaymentManager`; remove obsolete `QpayManager` bridge from the interface.

- `ChurchReport/WebServiceConnector/DonationPaymentProcessor/*.cs`
  Rename parameters and local variables from `QpayModel` to `DonationPaymentFormModel`. Do not change provider call behavior in the same task.

- `ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs`
  Rename to `DonationPaymentCreateGatewayAdapter.cs`; update all callers to the neutral type.

- `ChurchReport/Payments/QPayProductWorkflowDispatcher.cs`
  Rename to `DonationPaymentProductWorkflowDispatcher.cs`; update all callers to the neutral type.

- `ChurchReport/Payments/QPayReturnWorkflow.cs`
  Rename to `DonationPaymentReturnWorkflow.cs`; update all callers to the neutral type.

- `ChurchReport/Payments/QPayWorkflowPaymentResult.cs`
  Rename to `DonationPaymentWorkflowResult.cs`; update all callers to the neutral type.

- `ChurchReport/Payments/LegacyQPayModels.cs`
  Delete if no longer referenced; otherwise rename the real remaining DTOs to neutral product names such as `LegacyDonationPaymentModels.cs`. Do not keep a QPay alias layer.

- `ChurchReport/WebServiceConnector/QPayProcessorCompatibility.cs`
  Delete if it is only an alias for `DonationPaymentProcessor`. If it still contains real behavior, move that behavior into `DonationPaymentProcessor` or another neutral helper, then delete the QPay-named wrapper.

- `ChurchReport/Controllers/QPayCardController.cs`
  Rename to `PaymentReturnController.cs` or `DonationPaymentReturnController.cs`; preserve legacy callback route attributes so bank callbacks do not break.

- `ChurchReport/Views/Dedication/QPayView.cshtml`
  Rename to `DonationPaymentView.cshtml` or `DonationPaymentForm.cshtml`; update controller `View(...)` calls explicitly so route URLs remain stable.

- `ChurchReport/Views/Home/QPayLogin.cshtml`
  Rename to `DonationPaymentLogin.cshtml`; update controller view names explicitly.

- `ChurchReport/Views/QPayCard/`
  Rename folder to match the neutral controller/view name after old callback URLs are covered by route attributes.

- `ChurchReport/wwwroot/css/QPayView.css`
  Rename to `DonationPaymentView.css`; update view references.

- `ChurchReport.MemberInfo.Tests/Payments/QPay*.cs`
  Rename test files to `DonationPayment*` or `PaymentReturn*` names unless the test specifically targets true Sinopac/QPay provider protocol.

- `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`
  Update tests to primary `DonationPaymentFormModel`; do not test a `QpayModel` alias because the alias should not remain.

- `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs`
  Tighten policy: `QpayManager` should not exist outside true Sinopac provider code.

### Do Not Modify For This Plan

- Provider protocol classes under `SpeechMessage.Payments/Providers/Sinopac`. They may keep QPay wording where it describes Sinopac/QPay.
- External callback route templates that would break bank callbacks without a deployment migration. Keep only the literal URL template; use neutral C# class and action method names.
- `LinePayCSharp`; Line Pay remains separate.

---

### Task 1: Add Reusable Neutral Payment Draft DTOs

**Files:**
- Create: `SpeechMessage.Payments.Workflows/PaymentPayerDraft.cs`
- Create: `SpeechMessage.Payments.Workflows/PaymentLineItemDraft.cs`
- Create: `SpeechMessage.Payments.Workflows/PaymentMethodSelection.cs`
- Create: `SpeechMessage.Payments.Workflows/PaymentScheduleDraft.cs`
- Create: `SpeechMessage.Payments.Workflows/PaymentOrderDraft.cs`
- Test: `SpeechMessage.Payments.Tests/Workflows/PaymentOrderDraftMapperTests.cs`

- [ ] **Step 1: Create the test file with DTO construction tests**

Create `SpeechMessage.Payments.Tests/Workflows/PaymentOrderDraftMapperTests.cs` with this initial content:

```csharp
using FluentAssertions;
using SpeechMessage.Payments.Workflows;
using Xunit;

namespace SpeechMessage.Payments.Tests.Workflows;

public sealed class PaymentOrderDraftMapperTests
{
    [Fact]
    public void Payment_order_draft_has_product_neutral_payer_items_method_and_schedule()
    {
        var draft = new PaymentOrderDraft
        {
            ProfileName = "MyPayProduction",
            ProductOrderId = "INV-20260701-001",
            Amount = 1200m,
            Currency = "TWD",
            Description = "Invoice payment",
            Payer = new PaymentPayerDraft
            {
                Name = "王小明",
                Phone = "0912345678",
                Email = "payer@example.com",
                ExternalPayerId = "customer-001"
            },
            Method = new PaymentMethodSelection
            {
                Method = "Card",
                SubType = "OneTime",
                Metadata = new Dictionary<string, string>
                {
                    ["SourceSystem"] = "InvoiceSystem"
                }
            },
            Items =
            [
                new PaymentLineItemDraft
                {
                    Name = "Repair fee",
                    Quantity = 1,
                    UnitPrice = 1200m,
                    Currency = "TWD"
                }
            ],
            Schedule = new PaymentScheduleDraft
            {
                IsRecurring = false
            }
        };

        draft.ProfileName.Should().Be("MyPayProduction");
        draft.Payer.ExternalPayerId.Should().Be("customer-001");
        draft.Items.Should().ContainSingle();
        draft.Method.Metadata.Should().ContainKey("SourceSystem");
        draft.Schedule.IsRecurring.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails before DTOs exist**

Run:

```powershell
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter "FullyQualifiedName~PaymentOrderDraftMapperTests"
```

Expected: compile failure because `PaymentOrderDraft`, `PaymentPayerDraft`, `PaymentLineItemDraft`, `PaymentMethodSelection`, and `PaymentScheduleDraft` do not exist.

- [ ] **Step 3: Add `PaymentPayerDraft`**

Create `SpeechMessage.Payments.Workflows/PaymentPayerDraft.cs`:

```csharp
namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Product-neutral payer data supplied by the host product before payment creation.
/// This DTO intentionally avoids CRM, LINE, membership, donation, and UI concepts so
/// ChurchReport, repair systems, membership systems, and invoice systems can all reuse it.
/// </summary>
public sealed record PaymentPayerDraft
{
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string ExternalPayerId { get; init; } = string.Empty;
}
```

- [ ] **Step 4: Add `PaymentLineItemDraft`**

Create `SpeechMessage.Payments.Workflows/PaymentLineItemDraft.cs`:

```csharp
namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Product-neutral payable item. Host products decide what the item means:
/// donation category, repair fee, membership dues, invoice line, or another product item.
/// </summary>
public sealed record PaymentLineItemDraft
{
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; } = 1;
    public decimal UnitPrice { get; init; }
    public string Currency { get; init; } = "TWD";
}
```

- [ ] **Step 5: Add `PaymentMethodSelection`**

Create `SpeechMessage.Payments.Workflows/PaymentMethodSelection.cs`:

```csharp
namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Product-neutral payment method selection. Provider-specific field names belong in metadata
/// only when the provider mapper requires them; ordinary host code should use neutral method names.
/// </summary>
public sealed record PaymentMethodSelection
{
    public string Method { get; init; } = string.Empty;
    public string SubType { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
```

- [ ] **Step 6: Add `PaymentScheduleDraft`**

Create `SpeechMessage.Payments.Workflows/PaymentScheduleDraft.cs`:

```csharp
namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Product-neutral recurring-payment schedule. Host products may use it for recurring donations,
/// membership dues, maintenance subscriptions, or invoice installments.
/// </summary>
public sealed record PaymentScheduleDraft
{
    public bool IsRecurring { get; init; }
    public int TotalPeriods { get; init; }
    public string PeriodType { get; init; } = string.Empty;
    public int Frequency { get; init; }
    public DateOnly? StartDate { get; init; }
}
```

- [ ] **Step 7: Add `PaymentOrderDraft`**

Create `SpeechMessage.Payments.Workflows/PaymentOrderDraft.cs`:

```csharp
namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Product-neutral order draft used before provider create-payment.
/// This is the reusable host-product boundary; it is not a provider protocol DTO.
/// </summary>
public sealed record PaymentOrderDraft
{
    public string ProfileName { get; init; } = string.Empty;
    public string ProductOrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string Description { get; init; } = string.Empty;
    public PaymentPayerDraft Payer { get; init; } = new();
    public PaymentMethodSelection Method { get; init; } = new();
    public PaymentScheduleDraft Schedule { get; init; } = new();
    public IReadOnlyList<PaymentLineItemDraft> Items { get; init; } = Array.Empty<PaymentLineItemDraft>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
```

- [ ] **Step 8: Run test to verify DTOs compile**

Run:

```powershell
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter "FullyQualifiedName~PaymentOrderDraftMapperTests"
```

Expected: 1 passed.

- [ ] **Step 9: Commit Task 1**

```powershell
git add SpeechMessage.Payments.Workflows\PaymentPayerDraft.cs SpeechMessage.Payments.Workflows\PaymentLineItemDraft.cs SpeechMessage.Payments.Workflows\PaymentMethodSelection.cs SpeechMessage.Payments.Workflows\PaymentScheduleDraft.cs SpeechMessage.Payments.Workflows\PaymentOrderDraft.cs SpeechMessage.Payments.Tests\Workflows\PaymentOrderDraftMapperTests.cs
git commit -m "feat: add neutral payment order draft contracts"
```

---

### Task 2: Map Neutral Drafts Into Provider Execution Requests

**Files:**
- Create: `SpeechMessage.Payments.Workflows/PaymentOrderDraftMapper.cs`
- Modify: `SpeechMessage.Payments.Tests/Workflows/PaymentOrderDraftMapperTests.cs`

- [ ] **Step 1: Add failing mapper test**

Append this test to `PaymentOrderDraftMapperTests`:

```csharp
[Fact]
public void Mapper_converts_neutral_order_draft_to_payment_create_request()
{
    var draft = new PaymentOrderDraft
    {
        ProfileName = "TaishinSandbox",
        ProductOrderId = "MEM-001",
        Amount = 500m,
        Currency = "TWD",
        Description = "Membership fee",
        Payer = new PaymentPayerDraft
        {
            Name = "陳會員",
            Phone = "0987654321",
            Email = "member@example.com",
            ExternalPayerId = "member-001"
        },
        Method = new PaymentMethodSelection
        {
            Method = "Card",
            SubType = "Recurring",
            Metadata = new Dictionary<string, string>
            {
                ["PFN"] = "0"
            }
        },
        Schedule = new PaymentScheduleDraft
        {
            IsRecurring = true,
            TotalPeriods = 12,
            PeriodType = "M",
            Frequency = 1,
            StartDate = new DateOnly(2026, 7, 1)
        },
        Items =
        [
            new PaymentLineItemDraft
            {
                Name = "Membership fee",
                Quantity = 1,
                UnitPrice = 500m,
                Currency = "TWD"
            }
        ],
        Metadata = new Dictionary<string, string>
        {
            ["HostProduct"] = "MemberSystem"
        }
    };

    var mapper = new PaymentOrderDraftMapper();

    var request = mapper.Map(draft);

    request.ProfileName.Should().Be("TaishinSandbox");
    request.ProductOrderId.Should().Be("MEM-001");
    request.Amount.Should().Be(500m);
    request.Customer.Name.Should().Be("陳會員");
    request.Customer.Phone.Should().Be("0987654321");
    request.Customer.Email.Should().Be("member@example.com");
    request.PaymentMethod.Should().Be("Card");
    request.PaymentMethodSubType.Should().Be("Recurring");
    request.Items.Should().ContainSingle(item =>
        item.Name == "Membership fee" &&
        item.Quantity == 1 &&
        item.UnitPrice == 500m &&
        item.Currency == "TWD");
    request.Metadata.Should().Contain("ExternalPayerId", "member-001");
    request.Metadata.Should().Contain("PFN", "0");
    request.Metadata.Should().Contain("HostProduct", "MemberSystem");
    request.Metadata.Should().Contain("Schedule.IsRecurring", "true");
    request.Metadata.Should().Contain("Schedule.TotalPeriods", "12");
    request.Metadata.Should().Contain("Schedule.PeriodType", "M");
    request.Metadata.Should().Contain("Schedule.Frequency", "1");
    request.Metadata.Should().Contain("Schedule.StartDate", "2026-07-01");
}
```

- [ ] **Step 2: Run test to verify mapper is missing**

Run:

```powershell
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter "FullyQualifiedName~PaymentOrderDraftMapperTests"
```

Expected: compile failure because `PaymentOrderDraftMapper` does not exist.

- [ ] **Step 3: Add mapper implementation**

Create `SpeechMessage.Payments.Workflows/PaymentOrderDraftMapper.cs`:

```csharp
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Converts reusable host-product order drafts into the lower-level provider execution request.
/// This mapper is deliberately small: validation and product-specific enrichment stay in the host.
/// </summary>
public sealed class PaymentOrderDraftMapper
{
    public PaymentCreateRequest Map(PaymentOrderDraft draft)
    {
        if (draft is null)
        {
            throw new ArgumentNullException(nameof(draft));
        }

        return new PaymentCreateRequest
        {
            ProfileName = draft.ProfileName,
            ProductOrderId = draft.ProductOrderId,
            Amount = draft.Amount,
            Currency = draft.Currency,
            Description = draft.Description,
            PaymentMethod = draft.Method.Method,
            PaymentMethodSubType = draft.Method.SubType,
            Customer = new PaymentCustomer
            {
                Name = draft.Payer.Name,
                Phone = draft.Payer.Phone,
                Email = draft.Payer.Email
            },
            Items = draft.Items.Select(item => new PaymentLineItem
            {
                Name = item.Name,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Currency = item.Currency
            }).ToArray(),
            Metadata = BuildMetadata(draft)
        };
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(PaymentOrderDraft draft)
    {
        var metadata = new Dictionary<string, string>(draft.Metadata);

        foreach (var pair in draft.Method.Metadata)
        {
            metadata[pair.Key] = pair.Value;
        }

        if (!string.IsNullOrWhiteSpace(draft.Payer.ExternalPayerId))
        {
            metadata["ExternalPayerId"] = draft.Payer.ExternalPayerId;
        }

        metadata["Schedule.IsRecurring"] = draft.Schedule.IsRecurring ? "true" : "false";

        if (draft.Schedule.TotalPeriods > 0)
        {
            metadata["Schedule.TotalPeriods"] = draft.Schedule.TotalPeriods.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(draft.Schedule.PeriodType))
        {
            metadata["Schedule.PeriodType"] = draft.Schedule.PeriodType;
        }

        if (draft.Schedule.Frequency > 0)
        {
            metadata["Schedule.Frequency"] = draft.Schedule.Frequency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (draft.Schedule.StartDate is DateOnly startDate)
        {
            metadata["Schedule.StartDate"] = startDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        return metadata;
    }
}
```

- [ ] **Step 4: Run mapper tests**

Run:

```powershell
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --filter "FullyQualifiedName~PaymentOrderDraftMapperTests"
```

Expected: 2 passed.

- [ ] **Step 5: Commit Task 2**

```powershell
git add SpeechMessage.Payments.Workflows\PaymentOrderDraftMapper.cs SpeechMessage.Payments.Tests\Workflows\PaymentOrderDraftMapperTests.cs
git commit -m "feat: map neutral payment drafts to create requests"
```

---

### Task 3: Introduce ChurchReport Primary Donation Form Model

**Files:**
- Create: `ChurchReport/Models/DonationPaymentFormModel.cs`
- Delete: `ChurchReport/Models/QpayModel.cs`
- Modify: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`
- Create: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs`

- [ ] **Step 1: Add naming policy test**

Create `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs`:

```csharp
using ChurchReport.Models;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentFormModelNamingTests
{
    [Fact]
    public void Donation_payment_form_model_is_primary_churchreport_form_model()
    {
        typeof(DonationPaymentFormModel).Should().NotBeNull();
        typeof(DonationPaymentFormModel).Name.Should().NotContain("Qpay");
        typeof(DonationPaymentFormModel).Name.Should().NotContain("QPay");
    }

    [Fact]
    public void Legacy_qpay_model_type_is_removed_from_churchreport_product_layer()
    {
        Type.GetType("ChurchReport.Models.QpayModel, ChurchReport").Should().BeNull(
            "QpayModel was a provider-shaped product model name; source callers should be migrated to DonationPaymentFormModel instead of using an alias");
    }
}
```

- [ ] **Step 2: Run naming test to verify it fails**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentFormModelNamingTests"
```

Expected: compile failure because `DonationPaymentFormModel` does not exist.

- [ ] **Step 3: Move primary implementation into `DonationPaymentFormModel`**

Create `ChurchReport/Models/DonationPaymentFormModel.cs` by moving the current full implementation from `QpayModel.cs` into this file and changing:

```csharp
public class QpayModel
```

to:

```csharp
public class DonationPaymentFormModel
```

Keep the existing properties and methods, including:

```csharp
public void EnsureFormDefaults()
public bool NeedsDonorIdentityRestore()
```

Do not change behavior in this task. Only move the primary type name.

- [ ] **Step 4: Delete the old provider-named model file**

Delete `ChurchReport/Models/QpayModel.cs`.

Expected: the `QpayModel` class no longer exists. Any compile errors must be fixed by changing callers to `DonationPaymentFormModel`.

- [ ] **Step 5: Update view-default tests to primary name**

In `DonationPaymentViewDefaultsTests.cs`, replace these exact test names and constructions:

```csharp
public void New_qpay_model_has_donation_category_and_payment_method_defaults()
var model = new QpayModel();
```

with:

```csharp
public void New_donation_payment_form_model_has_donation_category_and_payment_method_defaults()
var model = new DonationPaymentFormModel();
```

Replace:

```csharp
public void Qpay_model_can_restore_required_form_defaults_after_reused_state_is_cleared()
var model = new QpayModel
```

with:

```csharp
public void Donation_payment_form_model_can_restore_required_form_defaults_after_reused_state_is_cleared()
var model = new DonationPaymentFormModel
```

Replace:

```csharp
public void Qpay_model_reports_when_web_login_donor_identity_must_be_restored(
var model = new QpayModel
```

with:

```csharp
public void Donation_payment_form_model_reports_when_web_login_donor_identity_must_be_restored(
var model = new DonationPaymentFormModel
```

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentFormModelNamingTests|FullyQualifiedName~DonationPaymentViewDefaultsTests"
```

Expected: tests pass. `QpayModel` / `QpayManager` obsolete-alias warnings should not remain; any remaining warnings must be unrelated existing project warnings.

- [ ] **Step 7: Commit Task 3**

```powershell
git add ChurchReport\Models\DonationPaymentFormModel.cs ChurchReport.MemberInfo.Tests\Payments\DonationPaymentViewDefaultsTests.cs ChurchReport.MemberInfo.Tests\Payments\DonationPaymentFormModelNamingTests.cs
git rm ChurchReport\Models\QpayModel.cs
git commit -m "refactor: introduce donation payment form model"
```

---

### Task 4: Rename ChurchReport Internal Form State Away From QPay

**Files:**
- Modify: `ChurchReport/Models/DonationPaymentManager.cs`
- Modify: `ChurchReport/Controllers/DedicationController.cs`
- Modify: `ChurchReport/Controllers/DedicationAuditController.cs`
- Modify: `ChurchReport/Controllers/BaseChurchController.cs`
- Modify: `ChurchReport/WebServiceConnector/DonationPaymentProcessor/*.cs`
- Modify: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`

- [ ] **Step 1: Add text-based guard test**

Add this test to `DonationPaymentFormModelNamingTests.cs`:

```csharp
[Fact]
public void Churchreport_donation_flow_should_not_use_qpay_names_for_product_form_state()
{
    var repositoryRoot = FindRepositoryRoot();
    var files = Directory.GetFiles(Path.Combine(repositoryRoot, "ChurchReport"), "*.cs", SearchOption.AllDirectories)
        .Where(path =>
            !path.Contains($"{Path.DirectorySeparatorChar}Providers{Path.DirectorySeparatorChar}Sinopac{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    var forbiddenMatches = files
        .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, lineNumber = index + 1 }))
        .Where(item =>
            item.line.Contains("m_QpayModel", StringComparison.Ordinal) ||
            item.line.Contains("SetQpayModel", StringComparison.Ordinal) ||
            item.line.Contains("QpayModel ", StringComparison.Ordinal) ||
            item.line.Contains("QpayModel)", StringComparison.Ordinal))
        .Select(item => $"{Path.GetRelativePath(repositoryRoot, item.path)}:{item.lineNumber}:{item.line.Trim()}")
        .ToArray();

    forbiddenMatches.Should().BeEmpty(
        "ChurchReport product form state should use DonationPayment naming; QPay names are reserved for Sinopac provider protocol or legacy URL route templates");
}

private static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "ChurchReport.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate ChurchReport.sln from test output directory.");
}
```

If `DonationPaymentViewDefaultsTests.cs` already has a private `FindRepositoryRoot`, keep both private helpers; they are in different classes.

- [ ] **Step 2: Run guard test to see current offenders**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Churchreport_donation_flow_should_not_use_qpay_names_for_product_form_state"
```

Expected: fail with current `m_QpayModel`, `SetQpayModel`, and `QpayModel` references.

- [ ] **Step 3: Rename manager field without a bridge property**

In `DonationPaymentManager.cs`, replace primary field:

```csharp
public QpayModel m_QpayModel = new QpayModel();
```

with:

```csharp
public DonationPaymentFormModel m_DonationPaymentFormModel = new();
```

If the existing field has a different initializer, preserve its behavior in the new `m_DonationPaymentFormModel` initializer.

Do not keep `m_QpayModel` as a bridge property. Compile errors must be resolved by updating callers to `m_DonationPaymentFormModel`.

- [ ] **Step 4: Rename model-returning method**

In `DonationPaymentManager.cs`, make `SetDonationPaymentModel` return `DonationPaymentFormModel`:

```csharp
public DonationPaymentFormModel SetDonationPaymentModel(Entity aContact)
```

Inside the method, replace assignments to `m_QpayModel` with `m_DonationPaymentFormModel`.

Delete `SetQpayModel`. Compile errors must be resolved by updating callers to `SetDonationPaymentModel`.

- [ ] **Step 5: Rename controller local variables and parameters**

In `DedicationController.cs` and `DedicationAuditController.cs`:

Replace action parameters:

```csharp
QpayModel QpayModel
QpayModel aQpayModel
```

with:

```csharp
DonationPaymentFormModel donationPaymentFormModel
DonationPaymentFormModel queryModel
```

Replace local variables:

```csharp
QpayModel qpayModel
```

with:

```csharp
DonationPaymentFormModel donationPaymentFormModel
```

Replace access:

```csharp
InMemoryContext.DonationPaymentManager.m_QpayModel
```

with:

```csharp
InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel
```

Do not rename public route names or URLs in this task.

- [ ] **Step 6: Rename DonationPaymentProcessor parameters**

In all files under `ChurchReport/WebServiceConnector/DonationPaymentProcessor/`, replace method parameters named:

```csharp
QpayModel QpayModel
```

with:

```csharp
DonationPaymentFormModel donationPaymentFormModel
```

Replace property accesses accordingly:

```csharp
QpayModel.Amount
```

to:

```csharp
donationPaymentFormModel.Amount
```

Do not change provider call values in this task.

- [ ] **Step 7: Run focused naming guard**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentFormModelNamingTests"
```

Expected: pass. If it fails, inspect the listed offenders and rename them unless they are true Sinopac provider protocol references.

- [ ] **Step 8: Run payment tests**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments"
```

Expected: pass. `QPay` / `Qpay` alias warnings should not remain; any remaining warnings must be unrelated existing project warnings.

- [ ] **Step 9: Commit Task 4**

```powershell
git add ChurchReport\Models\DonationPaymentManager.cs ChurchReport\Controllers\DedicationController.cs ChurchReport\Controllers\DedicationAuditController.cs ChurchReport\Controllers\BaseChurchController.cs ChurchReport\WebServiceConnector\DonationPaymentProcessor ChurchReport.MemberInfo.Tests\Payments\DonationPaymentFormModelNamingTests.cs ChurchReport.MemberInfo.Tests\Payments\DonationPaymentViewDefaultsTests.cs
git commit -m "refactor: neutralize donation payment form state naming"
```

---

### Task 5: Map ChurchReport Donation Forms To Reusable Payment Drafts

**Files:**
- Create: `ChurchReport/Payments/DonationPaymentFormModelMapper.cs`
- Create: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelMapperTests.cs`

- [ ] **Step 1: Add mapper tests**

Create `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelMapperTests.cs`:

```csharp
using ChurchReport.Models;
using ChurchReport.Payments;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentFormModelMapperTests
{
    [Fact]
    public void Mapper_converts_donation_form_model_to_neutral_payment_order_draft()
    {
        var model = new DonationPaymentFormModel
        {
            FullName = "胡夢嵩",
            Mobile = "0911222333",
            SelectedContactId = "contact-001",
            Amount = 800,
            Category = "十一奉獻",
            PayWay = "信用卡",
            DeductTotalNumber = "12",
            SelectedCreditCard = "card-001"
        };

        var mapper = new DonationPaymentFormModelMapper();

        var draft = mapper.Map(
            model,
            profileName: "JesusTest",
            productOrderId: "fee-001",
            description: "十一奉獻-胡夢嵩");

        draft.ProfileName.Should().Be("JesusTest");
        draft.ProductOrderId.Should().Be("fee-001");
        draft.Amount.Should().Be(800);
        draft.Currency.Should().Be("TWD");
        draft.Description.Should().Be("十一奉獻-胡夢嵩");
        draft.Payer.Name.Should().Be("胡夢嵩");
        draft.Payer.Phone.Should().Be("0911222333");
        draft.Payer.ExternalPayerId.Should().Be("contact-001");
        draft.Method.Method.Should().Be("信用卡");
        draft.Method.SubType.Should().Be("card-001");
        draft.Items.Should().ContainSingle(item =>
            item.Name == "十一奉獻" &&
            item.Quantity == 1 &&
            item.UnitPrice == 800m);
        draft.Metadata.Should().Contain("ChurchReport.DonationCategory", "十一奉獻");
    }
}
```

- [ ] **Step 2: Run test to verify mapper is missing**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentFormModelMapperTests"
```

Expected: compile failure because `DonationPaymentFormModelMapper` does not exist.

- [ ] **Step 3: Add mapper implementation**

Create `ChurchReport/Payments/DonationPaymentFormModelMapper.cs`:

```csharp
using ChurchReport.Models;
using SpeechMessage.Payments.Workflows;

namespace ChurchReport.Payments;

/// <summary>
/// Converts the ChurchReport donation form model into reusable payment draft data.
/// ChurchReport-specific donation meaning stays in metadata; provider-specific creation remains elsewhere.
/// </summary>
public sealed class DonationPaymentFormModelMapper
{
    public PaymentOrderDraft Map(
        DonationPaymentFormModel model,
        string profileName,
        string productOrderId,
        string description)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        return new PaymentOrderDraft
        {
            ProfileName = profileName,
            ProductOrderId = productOrderId,
            Amount = model.Amount,
            Currency = "TWD",
            Description = description,
            Payer = new PaymentPayerDraft
            {
                Name = model.FullName ?? string.Empty,
                Phone = model.Mobile ?? string.Empty,
                ExternalPayerId = model.SelectedContactId ?? string.Empty
            },
            Method = new PaymentMethodSelection
            {
                Method = model.PayWay ?? string.Empty,
                SubType = model.SelectedCreditCard ?? string.Empty
            },
            Schedule = new PaymentScheduleDraft
            {
                IsRecurring = !string.IsNullOrWhiteSpace(model.DeductTotalNumber),
                TotalPeriods = ParsePositiveInt(model.DeductTotalNumber),
                PeriodType = string.IsNullOrWhiteSpace(model.DeductTotalNumber) ? string.Empty : "M",
                Frequency = string.IsNullOrWhiteSpace(model.DeductTotalNumber) ? 0 : 1
            },
            Items =
            [
                new PaymentLineItemDraft
                {
                    Name = model.Category ?? string.Empty,
                    Quantity = 1,
                    UnitPrice = model.Amount,
                    Currency = "TWD"
                }
            ],
            Metadata = new Dictionary<string, string>
            {
                ["ChurchReport.DonationCategory"] = model.Category ?? string.Empty,
                ["ChurchReport.Others"] = model.Others ?? string.Empty,
                ["ChurchReport.DedicationNumber"] = model.DedicationNumber ?? string.Empty
            }
        };
    }

    private static int ParsePositiveInt(string? value)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 0;
    }
}
```

- [ ] **Step 4: Run mapper tests**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentFormModelMapperTests"
```

Expected: pass.

- [ ] **Step 5: Commit Task 5**

```powershell
git add ChurchReport\Payments\DonationPaymentFormModelMapper.cs ChurchReport.MemberInfo.Tests\Payments\DonationPaymentFormModelMapperTests.cs
git commit -m "feat: map donation forms to neutral payment drafts"
```

---

### Task 6: Rename Product-Layer QPay File Names To Neutral Names

**Files:**
- Rename: `ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs` -> `ChurchReport/Payments/DonationPaymentCreateGatewayAdapter.cs`
- Rename: `ChurchReport/Payments/QPayProductWorkflowDispatcher.cs` -> `ChurchReport/Payments/DonationPaymentProductWorkflowDispatcher.cs`
- Rename: `ChurchReport/Payments/QPayReturnWorkflow.cs` -> `ChurchReport/Payments/DonationPaymentReturnWorkflow.cs`
- Rename: `ChurchReport/Payments/QPayWorkflowPaymentResult.cs` -> `ChurchReport/Payments/DonationPaymentWorkflowResult.cs`
- Rename or delete: `ChurchReport/Payments/LegacyQPayModels.cs` -> `ChurchReport/Payments/LegacyDonationPaymentModels.cs` only if still required by neutral source callers; otherwise delete it.
- Delete: `ChurchReport/WebServiceConnector/QPayProcessorCompatibility.cs` if it is only an alias; otherwise move the remaining behavior into `ChurchReport/WebServiceConnector/DonationPaymentProcessor.cs` or a neutral helper, then delete the QPay-named file.
- Rename: `ChurchReport/Controllers/QPayCardController.cs` -> `ChurchReport/Controllers/PaymentReturnController.cs`
- Rename: `ChurchReport/Controllers/QPayLoginController.cs` -> `ChurchReport/Controllers/DonationPaymentLoginController.cs`
- Rename: `ChurchReport/Views/Dedication/QPayView.cshtml` -> `ChurchReport/Views/Dedication/DonationPaymentView.cshtml`
- Rename: `ChurchReport/Views/Home/QPayLogin.cshtml` -> `ChurchReport/Views/Home/DonationPaymentLogin.cshtml`
- Rename: `ChurchReport/Views/QPayCard/` -> `ChurchReport/Views/PaymentReturn/`
- Rename: `ChurchReport/wwwroot/css/QPayView.css` -> `ChurchReport/wwwroot/css/DonationPaymentView.css`
- Rename: `ChurchReport.MemberInfo.Tests/Payments/QPayAdapterTests.cs` -> `ChurchReport.MemberInfo.Tests/Payments/PaymentReturnAdapterTests.cs`
- Rename: `ChurchReport.MemberInfo.Tests/Payments/QPayCreatePaymentGatewayAdapterTests.cs` -> `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentCreateGatewayAdapterTests.cs`
- Rename: `ChurchReport.MemberInfo.Tests/Payments/QPayProcessorGatewayAdapterTests.cs` -> `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs`
- Rename: `ChurchReport.MemberInfo.Tests/Payments/QPayReturnWorkflowTests.cs` -> `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs`
- Modify: production files listed by the boundary search
- Modify: `.trellis/spec/backend/quality-guidelines.md`

- [ ] **Step 1: Run solution-wide QPay inventory**

Run:

```powershell
Get-ChildItem -Path . -Recurse -Include *.cs,*.cshtml,*.json,*.md |
  Where-Object {
    $_.FullName -notmatch '\\bin\\|\\obj\\|\\.git\\|\\.worktrees\\|\\artifacts\\'
  } |
  Select-String -Pattern 'QPay','Qpay','qpay' |
  ForEach-Object { "{0}:{1}:{2}" -f $_.Path,$_.LineNumber,$_.Line.Trim() } |
  Set-Content -Encoding UTF8 .ccg\tasks\qpay-model-boundary-brainstorm\qpay-inventory.txt
```

Expected: inventory file lists all remaining QPay references.

- [ ] **Step 2: Classify remaining references**

Open `.ccg/tasks/qpay-model-boundary-brainstorm/qpay-inventory.txt` and classify each hit into one of these labels in a new file `.ccg/tasks/qpay-model-boundary-brainstorm/qpay-classification.md`:

```markdown
# QPay Reference Classification

## Allowed Provider Protocol

- `SpeechMessage.Payments/Providers/Sinopac/...`: true Sinopac/QPay provider implementation.

## Allowed Legacy Route Templates

- `ChurchReport/Controllers/DonationPaymentLoginController.cs`: may preserve old `/QPayLogin/...` URLs through route attributes only; the file/class name must be neutral.
- `ChurchReport/Controllers/PaymentReturnController.cs`: may preserve old `/QPayCard/...` URLs through route attributes only; the file/class name must be neutral.
- Legacy `QPay` text in those controllers must be limited to attribute route-template strings on `[Route(...)]`, `[HttpGet(...)]`, or `[HttpPost(...)]` lines. It must not appear in class names, action method names, parameters, locals, fields, injected services, DTOs, comments used as compatibility aliases, or helper names.

## Must Rename

- Any ChurchReport donation form state, manager field, method, variable, test name, file name, or generic adapter used by MyPay/Taishin/Sinopac together.
```

- [ ] **Step 3: Add guard test for allowed QPay locations**

In `DonationPaymentFormModelNamingTests.cs`, add:

```csharp
[Fact]
public void Qpay_references_are_confined_to_sinopac_provider_or_legacy_route_templates()
{
    var repositoryRoot = FindRepositoryRoot();
    var allowedProviderFragments = new[]
    {
        Path.Combine("SpeechMessage.Payments", "Providers", "Sinopac"),
        Path.Combine("SpeechMessage.Payments.Tests", "Providers", "Sinopac")
    };

    var scannedFiles = Directory.GetFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path =>
            !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    var offenders = scannedFiles
        .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, lineNumber = index + 1 }))
        .Where(item =>
            item.line.Contains("QPay", StringComparison.Ordinal) ||
            item.line.Contains("Qpay", StringComparison.Ordinal) ||
            item.line.Contains("qpay", StringComparison.Ordinal))
        .Where(item =>
        {
            var relative = Path.GetRelativePath(repositoryRoot, item.path);
            var isProviderCode = allowedProviderFragments.Any(fragment =>
                relative.StartsWith(fragment, StringComparison.OrdinalIgnoreCase));

            return !isProviderCode && !IsAllowedLegacyQPayRouteTemplate(relative, item.line);
        })
        .Select(item => $"{Path.GetRelativePath(repositoryRoot, item.path)}:{item.lineNumber}:{item.line.Trim()}")
        .ToArray();

    offenders.Should().BeEmpty(
        "QPay names should not appear in product-neutral code paths used by MyPay or Taishin");
}

private static bool IsAllowedLegacyQPayRouteTemplate(string relativePath, string line)
{
    var isLegacyRouteController =
        relativePath.Equals(Path.Combine("ChurchReport", "Controllers", "DonationPaymentLoginController.cs"), StringComparison.OrdinalIgnoreCase) ||
        relativePath.Equals(Path.Combine("ChurchReport", "Controllers", "PaymentReturnController.cs"), StringComparison.OrdinalIgnoreCase);

    if (!isLegacyRouteController)
    {
        return false;
    }

    var trimmed = line.TrimStart();
    var isRouteAttribute =
        trimmed.StartsWith("[Route(", StringComparison.Ordinal) ||
        trimmed.StartsWith("[HttpGet(", StringComparison.Ordinal) ||
        trimmed.StartsWith("[HttpPost(", StringComparison.Ordinal);

    return isRouteAttribute &&
        (line.Contains("QPayLogin", StringComparison.Ordinal) ||
         line.Contains("QPayCard", StringComparison.Ordinal));
}
```

- [ ] **Step 4: Add file-name guard test**

Append this test to `DonationPaymentFormModelNamingTests.cs`:

```csharp
[Fact]
public void Product_layer_file_names_should_not_contain_qpay()
{
    var repositoryRoot = FindRepositoryRoot();
    var allowedPathFragments = new[]
    {
        Path.Combine("SpeechMessage.Payments", "Providers", "Sinopac"),
        Path.Combine("SpeechMessage.Payments.Tests", "Providers", "Sinopac"),
        Path.Combine("ChurchReport", "文件")
    };

    var offenders = Directory.GetFileSystemEntries(repositoryRoot, "*", SearchOption.AllDirectories)
        .Where(path =>
            !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .Where(path =>
            Path.GetFileName(path).Contains("QPay", StringComparison.Ordinal) ||
            Path.GetFileName(path).Contains("Qpay", StringComparison.Ordinal) ||
            Path.GetFileName(path).Contains("qpay", StringComparison.Ordinal))
        .Where(path =>
        {
            var relative = Path.GetRelativePath(repositoryRoot, path);
            return !allowedPathFragments.Any(fragment =>
                relative.StartsWith(fragment, StringComparison.OrdinalIgnoreCase));
        })
        .Select(path => Path.GetRelativePath(repositoryRoot, path))
        .ToArray();

    offenders.Should().BeEmpty(
        "product-layer files should use neutral names; old URLs must be preserved by route attributes, not provider-shaped file names");
}
```

- [ ] **Step 5: Add neutral-call-path guard test**

Append this test to `DonationPaymentFormModelNamingTests.cs`:

```csharp
[Fact]
public void Provider_neutral_churchreport_code_must_not_call_qpay_types()
{
    var repositoryRoot = FindRepositoryRoot();
    var allowedFragments = new[]
    {
        Path.Combine("SpeechMessage.Payments", "Providers", "Sinopac"),
        Path.Combine("SpeechMessage.Payments.Tests", "Providers", "Sinopac")
    };

    var scannedFiles = Directory.GetFiles(Path.Combine(repositoryRoot, "ChurchReport"), "*.cs", SearchOption.AllDirectories)
        .Where(path =>
            !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .Where(path =>
        {
            var relative = Path.GetRelativePath(repositoryRoot, path);
            return !allowedFragments.Any(fragment =>
                relative.StartsWith(fragment, StringComparison.OrdinalIgnoreCase));
        })
        .ToArray();

    var forbiddenPatterns = new[]
    {
        "new QPay",
        "new Qpay",
        ": QPay",
        ": Qpay",
        "<QPay",
        "<Qpay",
        " QPay",
        " Qpay",
        "IQPay"
    };

    var offenders = scannedFiles
        .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, lineNumber = index + 1 }))
        .Where(item => forbiddenPatterns.Any(pattern => item.line.Contains(pattern, StringComparison.Ordinal)))
        .Select(item => $"{Path.GetRelativePath(repositoryRoot, item.path)}:{item.lineNumber}:{item.line.Trim()}")
        .ToArray();

    offenders.Should().BeEmpty(
        "provider-neutral ChurchReport code must call neutral Payment/DonationPayment types; product-layer QPay aliases should not exist");
}
```

Expected: this test fails until MyPay/Taishin/shared code no longer references `QPay*` or `Qpay*` types.

- [ ] **Step 6: Rename or delete offenders**

For each failing offender:

1. If it is generic create-payment or post-payment code, rename `QPay` to `Payment` or `DonationPayment`.
2. If it is ChurchReport donation UI state, rename `Qpay` to `DonationPayment`.
3. If it is true Sinopac provider protocol, move it under `SpeechMessage.Payments/Providers/Sinopac` if it is not already there.
4. If it is an externally visible legacy route, rename the file/class/action method to neutral and preserve the old URL only through route-template attributes.
5. Do not keep legacy product-layer `QPay*` class names for source compatibility. Update source callers to neutral names.
6. If MyPay, Taishin, or provider-neutral ChurchReport code calls a `QPay*` type, rename the type and update the caller to the neutral type.

Do not rename all references blindly. Each rename must preserve runtime routes and callback URLs.

- [ ] **Step 7: Update backend spec with naming rule**

Append this section to `.trellis/spec/backend/quality-guidelines.md`:

```markdown
## Payment Naming Neutrality

### Scope / Trigger

- Trigger: code is used by more than one payment provider, or code is a host-product payment workflow.
- `QPay` naming is reserved for true Sinopac/QPay provider protocol code and legacy URL route templates only.

### Contracts

- Reusable payment contracts must use neutral names such as `PaymentOrderDraft`, `PaymentPayerDraft`, `PaymentLineItemDraft`, `PaymentScheduleDraft`, and `PaymentCreateRequest`.
- ChurchReport donation UI/workflow code must use `DonationPayment` names, not `QPay`.
- MyPay and Taishin flows must never call classes whose primary name implies QPay/Sinopac ownership.
- Product-layer file names must not contain `QPay`, `Qpay`, or `qpay`. Preserve legacy URLs with route attributes, not provider-shaped file names.
- Do not keep product-layer `QPay` compatibility aliases. Existing source callers must be migrated to neutral classes.

### Validation

- Before completing payment refactors, run a solution-wide `QPay|Qpay|qpay` search.
- Remaining matches must be classified as Sinopac provider protocol, Sinopac tests, or legacy URL route templates.
- New code may not add `QPay` names outside Sinopac provider implementation or explicitly documented legacy URL route templates.
- Remaining product-layer file-name matches must be renamed unless they are historical documentation files under `ChurchReport/文件`.
- A guard test should fail if provider-neutral ChurchReport code references `new QPay*`, `new Qpay*`, `IQPay*`, or derives from `QPay*` / `Qpay*`.
```

- [ ] **Step 8: Run guard and payment tests**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentFormModelNamingTests|FullyQualifiedName~Payments"
```

Expected: pass.

- [ ] **Step 9: Commit Task 6**

```powershell
git add .ccg\tasks\qpay-model-boundary-brainstorm\qpay-inventory.txt .ccg\tasks\qpay-model-boundary-brainstorm\qpay-classification.md .trellis\spec\backend\quality-guidelines.md ChurchReport ChurchReport.MemberInfo.Tests
git commit -m "refactor: contain qpay names to provider code"
```

---

### Task 7: Full Validation And Review

**Files:**
- Modify: `.ccg/tasks/qpay-model-boundary-brainstorm/review.md`

- [ ] **Step 1: Run reusable payment tests**

Run:

```powershell
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 2: Run ChurchReport payment tests**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments"
```

Expected: all payment tests pass.

- [ ] **Step 3: Run solution build**

Run:

```powershell
dotnet build .\ChurchReport.sln
```

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Run boundary search for forbidden core dependencies**

Run:

```powershell
Get-ChildItem -Path SpeechMessage.Payments,SpeechMessage.Payments.Workflows -Recurse -Include *.cs,*.csproj |
  Select-String -Pattern 'ChurchReport','ToolUtility','Line\.Messaging','Microsoft\.Xrm','HttpRequest','Controller','IActionResult','DbContext'
```

Expected: no matches in `SpeechMessage.Payments`; no ChurchReport/CRM/LINE/MVC dependency in reusable workflow DTOs.

- [ ] **Step 5: Run QPay containment search**

Run:

```powershell
Get-ChildItem -Path . -Recurse -Include *.cs |
  Where-Object {
    $_.FullName -notmatch '\\bin\\|\\obj\\|\\.git\\|\\.worktrees\\|\\artifacts\\'
  } |
  Select-String -Pattern 'QPay','Qpay','qpay'
```

Expected: matches are limited to Sinopac provider/test code and documented legacy URL route templates.

- [ ] **Step 6: Run payment-flow regression checklist**

Use existing focused tests when they exist; otherwise add focused mocked tests before treating the task as complete. Do not call real bank, CRM, or LINE services.

Required regression coverage:

- Sinopac credit-card create flow: mocked gateway returns an absolute hosted payment URL; ChurchReport returns/redirects to that URL and does not fall back to the original donation page.
- Sinopac ATM/virtual-account create flow: mocked provider data includes `atm_pay_no`; ChurchReport renders the virtual account and keeps payment instructions visible even when LINE delivery is mocked as failed.
- MyPay/high-grand create flow: selected MyPay profile builds the encrypted create payload with MyPay-shaped fields and does not call QPay-named adapters/classes.
- Taishin TSPG create/return flow: TSPG profile and callback/return path still map to neutral payment results without QPay aliases.
- LINE Pay selection/redirect flow: choosing LINE Pay still routes to the LINE Pay path and does not depend on QPay-named product classes.
- CRM payment-record update workflow: a successful neutral payment workflow updates the ChurchReport CRM fee/payment record through ChurchReport-owned services only.
- LINE payer notification workflow: required payer notifications are sent through ChurchReport-owned LINE services, failures are surfaced, and no LINE dependency is introduced into `SpeechMessage.Payments`.

Expected: every affected payment path has either an existing passing test or a newly added mocked regression test recorded in review.md.

- [ ] **Step 7: Record fixed base SHA for final review**

Before starting Task 1 implementation, record the current commit:

```powershell
$baseSha = git rev-parse HEAD
Set-Content -LiteralPath .ccg\tasks\qpay-model-boundary-brainstorm\base-sha.txt -Value $baseSha -Encoding UTF8
```

Expected: final review uses `.ccg/tasks/qpay-model-boundary-brainstorm/base-sha.txt` instead of a moving HEAD-relative range.

- [ ] **Step 8: Attempt required CCG dual-model review**

Run Gemini review:

```powershell
$baseSha = Get-Content -LiteralPath .ccg\tasks\qpay-model-boundary-brainstorm\base-sha.txt
$diff = git diff --no-textconv "$baseSha..HEAD"
$task = @"
ROLE_FILE: ~/.claude/.ccg/prompts/gemini/reviewer.md
<TASK>
Review the neutral payment DTO and QPay naming containment changes.
Focus on provider boundary, naming neutrality, ChurchReport behavior preservation, and future product reuse.

$diff
</TASK>
OUTPUT: Critical/Warning/Info review report
"@
$task | & "$env:USERPROFILE\.claude\bin\codeagent-wrapper" --progress --backend gemini - (Get-Location).Path
```

Run Claude review:

```powershell
$baseSha = Get-Content -LiteralPath .ccg\tasks\qpay-model-boundary-brainstorm\base-sha.txt
$diff = git diff --no-textconv "$baseSha..HEAD"
$task = @"
ROLE_FILE: ~/.claude/.ccg/prompts/claude/reviewer.md
<TASK>
Review the neutral payment DTO and QPay naming containment changes.
Focus on provider boundary, naming neutrality, ChurchReport behavior preservation, and future product reuse.

$diff
</TASK>
OUTPUT: Critical/Warning/Info review report
"@
$task | & "$env:USERPROFILE\.claude\bin\codeagent-wrapper" --progress --backend claude - (Get-Location).Path
```

Expected: review reports are produced against the fixed base SHA. If the local machine still lacks `gemini` or `claude` in `PATH`, record that exact failure in review.md instead of claiming external review passed.

- [ ] **Step 9: Write review record**

Create or update `.ccg/tasks/qpay-model-boundary-brainstorm/review.md` with:

```markdown
# Review - Neutral Payment DTO And QPay Name Containment

## Local Verification

- `dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj`: result
- `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments"`: result
- `dotnet build .\ChurchReport.sln`: result
- Core dependency search: result
- QPay containment search: result
- Payment-flow regression checklist: result for Sinopac credit card, Sinopac ATM/virtual account, MyPay/high-grand, Taishin TSPG, LINE Pay, CRM payment-record update, and LINE payer notification
- Review base SHA: value from `.ccg/tasks/qpay-model-boundary-brainstorm/base-sha.txt`

## External Review

- Gemini: result or environment blocker
- Claude: result or environment blocker

## Findings

- Critical:
- Warning:
- Info:
```

- [ ] **Step 10: Commit validation record**

```powershell
git add .ccg\tasks\qpay-model-boundary-brainstorm\base-sha.txt .ccg\tasks\qpay-model-boundary-brainstorm\review.md
git commit -m "docs: record neutral payment naming review"
```

---

## Self-Review

- Spec coverage: The plan covers QPay naming containment, reusable DTO extraction, ChurchReport model rename, mapper path to payment core, validation, CCG review, and backend spec update.
- Placeholder scan: The plan avoids unfinished placeholder steps; implementation steps specify exact files, type names, test names, commands, and expected results.
- Type consistency: Reusable DTOs consistently use `PaymentOrderDraft`, `PaymentPayerDraft`, `PaymentLineItemDraft`, `PaymentMethodSelection`, `PaymentScheduleDraft`, and `PaymentOrderDraftMapper`. ChurchReport primary form type is consistently `DonationPaymentFormModel`.
- Boundary check: `SpeechMessage.Payments` remains provider protocol. `SpeechMessage.Payments.Workflows` receives reusable host-product DTOs and mapper only. ChurchReport keeps donation, CRM, LINE, MVC, and old external route templates.

## Execution Recommendation

Use inline execution for this repository because the active mode says do not dispatch implement/check sub-agents. Execute one task at a time, run the focused test after each task, and commit after each green task.
