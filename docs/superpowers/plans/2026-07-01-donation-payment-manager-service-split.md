# Donation Payment Manager Service Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce `ChurchReport/Models/DonationPaymentManager.cs` into a thinner coordinator by moving ChurchReport-specific donation workflows into focused services while keeping reusable payment abstractions product-neutral.

**Architecture:** `DonationPaymentManager` keeps its existing public entry points for controllers/views, but delegates large workflows to ChurchReport services. CRM, LINE, `DonationPaymentFormModel`, `new_fee`, `new_dedication_booking`, and奉獻編號 rules stay in `ChurchReport.Services`; only provider-neutral payment workflow interfaces remain in `SpeechMessage.Payments.Workflows`.

**Tech Stack:** ASP.NET Core MVC, .NET 10, Dynamics CRM `Microsoft.Xrm.Sdk`, existing `ToolUtilityClass`, xUnit/FluentAssertions tests.

---

## File Structure

- Create `ChurchReport/Services/DonationKeyInDedicationService.cs`
  - Owns manual donation query/audit/update logic currently in `SaveKeyInDedication`, `QueryKeyInDedication`, `AuditQueryDedication`, and `UpdateKeyInDedication`.
  - Returns `IActionResult` with the same JSON payloads to preserve UI behavior.

- Modify `ChurchReport/Services/DonationBookingService.cs`
  - Expand from status converter into booking list mapping and booking cancellation workflow.
  - Still ChurchReport-only because it uses `new_dedication_booking`, CRM updates, LINE notification, and legacy order-maintain behavior.

- Create `ChurchReport/Services/DonationContactCreationService.cs`
  - Owns “查詢不到時新增新人” and奉獻編號 assignment rules.
  - Keeps these rules out of payment core because they depend on ChurchReport contact numbering.

- Create `ChurchReport/Services/DonationPaymentModelAssembler.cs`
  - Owns `SetDonationPaymentModel` model initialization and form list population.
  - Uses existing focused services for credit cards, booking list, special categories, and fee categories.

- Modify `ChurchReport/Models/DonationPaymentManager.cs`
  - Add service fields.
  - Keep public methods but delegate to the services.
  - Remove direct large CRM mapping blocks from the manager.

- Modify `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentServiceExtractionTests.cs`
  - Add structure regression tests that assert the manager delegates the newly extracted sections.
  - Keep existing behavior tests for pure mapping services.

- Modify `.ccg/tasks/modularize-donation-payment-manager/review.md`
  - Record the additional split and verification results.

## Shared-Core Decision

- Do not move ChurchReport CRM/LINE/donation workflows into `SpeechMessage.Payments`, `SpeechMessage.Payments.AspNetCore`, or `SpeechMessage.Payments.Workflows`.
- Future ASP.NET Core products reuse the existing neutral primitives:
  - `PaymentOrderDraft`
  - `PaymentOrderDraftMapper`
  - `PaymentPostPaymentWorkflow`
  - `IPaymentRecordUpdater`
  - `IPaymentPayerNotifier`
- Product-specific implementations remain in each product. For ChurchReport, those implementations stay under `ChurchReport.Services`.

---

### Task 1: Add Delegation Regression Tests

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentServiceExtractionTests.cs`

- [ ] **Step 1: Add manager delegation assertions**

Add tests that read `DonationPaymentManager.cs` and assert these sections delegate:

```csharp
[Fact]
public void DonationPaymentManager_should_delegate_key_in_dedication_workflow()
{
    string managerSource = ReadRepositoryFile("ChurchReport", "Models", "DonationPaymentManager.cs");
    string section = ExtractSourceSection(managerSource, "public async Task<IActionResult> SaveKeyInDedication", "public DonationPaymentFormModel SetDedicationFeeList");

    section.Should().Contain("m_DonationKeyInDedicationService");
    section.Should().NotContain("QueryDediccationContatsByFetchXml");
    section.Should().NotContain("SaveKeyInDedication(DonationPaymentFormModel)");
}

[Fact]
public void DonationPaymentManager_should_delegate_booking_workflow()
{
    string managerSource = ReadRepositoryFile("ChurchReport", "Models", "DonationPaymentManager.cs");
    string section = ExtractSourceSection(managerSource, "public void ProcessDedicationBooking()", "public async Task<IActionResult> CreateContact");

    section.Should().Contain("m_DonationBookingService");
    section.Should().NotContain("RetrieveDedicationBookingByFetchXml");
    section.Should().NotContain("new_dedication_booking");
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments" --no-restore -v minimal -p:UseSharedCompilation=false
```

Expected: the new delegation tests fail because the manager still owns those workflows.

### Task 2: Extract Manual Donation Query/Update Workflow

**Files:**
- Create: `ChurchReport/Services/DonationKeyInDedicationService.cs`
- Modify: `ChurchReport/Models/DonationPaymentManager.cs`

- [ ] **Step 1: Create service**

Create `DonationKeyInDedicationService` with:

```csharp
public sealed class DonationKeyInDedicationService
{
    public DonationKeyInDedicationService(
        ToolUtilityClass utility,
        DonationPaymentFormModel formModel,
        DonationPaymentProcessor paymentProcessor,
        Func<object, JsonResult> json,
        Func<string, object?, RedirectToActionResult> redirectToAction,
        Func<string, string, void> errorNotifier)
}
```

The service contains the existing query, audit, and update logic copied from `DonationPaymentManager`, with Traditional Chinese comments explaining that the service is ChurchReport-specific and must not move to payment core.

- [ ] **Step 2: Delegate manager methods**

Replace `SaveKeyInDedication`, `QueryKeyInDedication`, `AuditQueryDedication`, and `UpdateKeyInDedication` bodies with calls to `m_DonationKeyInDedicationService`.

- [ ] **Step 3: Run tests and build**

Run the payment test subset and solution build. Expected: tests compile and the key-in delegation test passes.

### Task 3: Extract Booking List and Cancellation Workflow

**Files:**
- Modify: `ChurchReport/Services/DonationBookingService.cs`
- Modify: `ChurchReport/Models/DonationPaymentManager.cs`

- [ ] **Step 1: Expand booking service**

Add methods:

```csharp
public void FillBookingList(DonationPaymentFormModel model, Entity contact)
public void SelectDefaultBooking(DonationPaymentFormModel model)
public Task<string> DeleteBookingAsync(
    DonationPaymentFormModel model,
    Entity contact,
    DedicationBooking booking,
    Func<string, string, Task<OrderMaintain>> orderMaintainer,
    PushUtility pushUtility)
```

The service owns `new_dedication_booking` mapping, CRM status update, result-message composition, and LINE notification.

- [ ] **Step 2: Delegate manager methods**

Replace `ProcessDedicationBooking`, `GetDedicationBookingList`, and `DeleteDedicationBooking` bodies with calls to `m_DonationBookingService`.

- [ ] **Step 3: Run tests and build**

Run payment tests and build. Expected: booking delegation test passes.

### Task 4: Extract Contact Creation and Numbering Workflow

**Files:**
- Create: `ChurchReport/Services/DonationContactCreationService.cs`
- Modify: `ChurchReport/Models/DonationPaymentManager.cs`

- [ ] **Step 1: Create service**

Create methods:

```csharp
public IActionResult CreateContact(string fullName)
private void AutoDedicationNumbering(string fullName, Entity createdContact)
private void SetDedicationNumber(string startNumber, Entity createdContact)
```

Keep the behavior identical and document that numbering belongs to ChurchReport, not payment core.

- [ ] **Step 2: Delegate manager**

Replace `CreateContact(string FullName)`, `AutoDedicationNumbering`, and `SetDedicationNumber` with calls/removal where possible.

- [ ] **Step 3: Run tests and build**

Run payment tests and build.

### Task 5: Extract Donation Payment Model Assembly

**Files:**
- Create: `ChurchReport/Services/DonationPaymentModelAssembler.cs`
- Modify: `ChurchReport/Models/DonationPaymentManager.cs`

- [ ] **Step 1: Create assembler**

Move the body of `SetDonationPaymentModel(Entity aContact)` into the assembler. The assembler receives:

```csharp
ToolUtilityClass utility
DonationPaymentFormModel model
DonationBookingService bookingService
Action processCreditCards
```

It returns the initialized `DonationPaymentFormModel`.

- [ ] **Step 2: Delegate manager**

Replace `SetDonationPaymentModel` with:

```csharp
m_Contact = aContact;
return m_DonationPaymentModelAssembler.Build(aContact, m_DonationPaymentFormModel, ProcessCreditCard);
```

- [ ] **Step 3: Run tests and build**

Run payment tests and build.

### Task 6: Boundary and Encoding Verification

**Files:**
- Modify: `.ccg/tasks/modularize-donation-payment-manager/review.md`

- [ ] **Step 1: Run boundary searches**

Run searches verifying no ChurchReport product dependencies enter `SpeechMessage.Payments` or `SpeechMessage.Payments.AspNetCore`.

- [ ] **Step 2: Run final verification**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments" --no-restore -v minimal -p:UseSharedCompilation=false
dotnet test .\SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false
dotnet build .\ChurchReport.sln --no-restore -v minimal -p:UseSharedCompilation=false
git diff --check
```

- [ ] **Step 3: Check UTF-8 without BOM and CRLF**

Run a byte-level check on touched `.cs`, `.md`, and `.json` files. Expected: `Bom=False` and `LfOnly=0`.

- [ ] **Step 4: Update review notes**

Record:

- New services created.
- `DonationPaymentManager.cs` line-count reduction.
- Verification results.
- External Gemini/Claude review status.

## Self-Review

- Spec coverage: all approved design sections map to tasks 1-6.
- Boundary coverage: shared-core decision is explicit; ChurchReport CRM/LINE workflows stay out of common projects.
- Placeholder scan: no TBD/TODO placeholders remain.
- Type consistency: service names and method signatures are stable across tasks.
