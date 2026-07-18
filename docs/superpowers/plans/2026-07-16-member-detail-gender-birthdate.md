# Member Detail Gender and Birthdate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display gender and birthdate in the MemberInfo detail popup without adding another request.

**Architecture:** Extend the existing detail ViewModel and the existing CRM contact column set, then render two read-only fields in the current responsive field grid. A contract test locks the full data path from CRM attributes through the ViewModel to Razor labels and formatting.

**Tech Stack:** ASP.NET Core MVC, Razor, Dataverse Entity/OptionSet, xUnit, FluentAssertions

---

### Task 1: Add a failing end-to-end detail contract

**Files:**
- Create: `ChurchReport.MemberInfo.Tests/MemberInfoDetailContractTests.cs`

- [x] **Step 1: Write the failing test**

Create a test that reflects `MemberInfoDetailViewModel.Gender` and `.BirthDate`, then reads `MemberInfoController.cs` and `_MemberDetailPopup.cshtml` to require `gendercode`, `birthdate`, controller mappings, the two Chinese labels, `yyyy/MM/dd`, and the empty-state text.

- [x] **Step 2: Verify RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Release --filter "FullyQualifiedName~MemberInfoDetailContractTests" --no-restore
```

Expected: FAIL because `MemberInfoDetailViewModel` does not expose `Gender` or `BirthDate`.

### Task 2: Complete the existing Detail data path

**Files:**
- Modify: `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`
- Modify: `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`
- Test: `ChurchReport.MemberInfo.Tests/MemberInfoDetailContractTests.cs`

- [x] **Step 1: Extend the ViewModel**

Add `string Gender` and `DateTime? BirthDate` to `MemberInfoDetailViewModel`.

- [x] **Step 2: Retrieve and map CRM values**

Add `gendercode` and `birthdate` to `GetContactDetailColumns()`. In `Detail`, map gender through `GetOptionSetText(contact, "gendercode")`; map `birthdate` to nullable `DateTime` and discard sentinel values whose year is 1 or lower.

- [x] **Step 3: Render the two read-only fields**

Before the full-width relationship field, add one `.member-info-field` for 性別 and one for 生日. Show `（未設定）` for empty values and format valid dates with `Model.BirthDate.Value.ToString("yyyy/MM/dd")`.

- [x] **Step 4: Verify GREEN and regression suite**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Release --no-restore
dotnet build ChurchReport.sln -c Release --no-restore
```

Expected: all MemberInfo tests pass; build completes with zero errors.

- [x] **Step 5: Commit**

```powershell
git add ChurchReport.MemberInfo.Tests/MemberInfoDetailContractTests.cs ChurchReport/ViewModels/MemberInfoDetailViewModel.cs ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml docs/superpowers/plans/2026-07-16-member-detail-gender-birthdate.md
git commit -m "feat(memberinfo): show gender and birthday in details"
```
