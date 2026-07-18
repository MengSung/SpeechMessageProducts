# Member Info Loading Animation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace MemberInfo's plain loading text with an accessible animated card that remains reassuring during long requests.

**Architecture:** Keep the existing request, timeout, failure, and retry paths unchanged. Add one shared HTML builder and one long-wait scheduler in `MemberInfoGrid.cshtml`, then reuse them for tree, group, and detail-popup loading states; CSS supplies the visual animation and reduced-motion fallback.

**Tech Stack:** ASP.NET Core Razor, browser JavaScript, CSS animations, xUnit, FluentAssertions

---

### Task 1: Lock the loading-state contract

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`

- [x] **Step 1: Write the failing test**

Add a test that requires `miLoadingHtml`, all three call sites, `role="status"`, `aria-live="polite"`, `aria-atomic="true"`, three loading dots, the long-wait reassurance, the loading keyframes, and `prefers-reduced-motion`.

- [x] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~View_UsesAccessibleAnimatedLoadingCards" --no-restore
```

Expected: FAIL because `miLoadingHtml` and the animated loading-card markup do not exist.

- [x] **Step 3: Commit only after implementation is green**

The test and implementation form one behavior change and will be committed together after Task 2.

### Task 2: Implement the shared animated loading card

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- Test: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`

- [x] **Step 1: Add the visual states**

Add `.mi-loading-card`, `.mi-loading-dots`, `.mi-loading-dot`, copy styles, compact sizing, gradient and bounce keyframes. Add a `prefers-reduced-motion: reduce` rule that turns animation off.

- [x] **Step 2: Add the shared markup and long-wait behavior**

Implement `miLoadingHtml(compact)` with constant, accessible markup. Implement `miScheduleLoadingReassurance(root)` so the subtitle changes after 6000 ms only while the same card is still connected. Implement `miShowLoading(host, compact)` to render and schedule the card.

- [x] **Step 3: Replace all plain loading call sites**

Use `miShowLoading(host, true)` for group loads, `miShowLoading(host, false)` for the initial tree, and `miLoadingHtml(false)` for detail-popup content. Schedule the detail message after it is inserted.

- [x] **Step 4: Run the focused test and verify GREEN**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~View_UsesAccessibleAnimatedLoadingCards" --no-restore
```

Expected: PASS.

- [x] **Step 5: Run the complete MemberInfo suite**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore
```

Expected: all tests pass.

### Task 3: Verify browser behavior and repository scope

**Files:**
- Verify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- Verify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`

- [x] **Step 1: Build the solution**

Run:

```powershell
dotnet build ChurchReport.sln --no-restore
```

Expected: build succeeds with zero errors.

- [x] **Step 2: Check the Razor JavaScript syntax**

Extract the script from the Razor view, replace Razor-only values with valid JavaScript literals, and run `node --check` against the extracted file.

Expected: no syntax errors.

- [x] **Step 3: Verify port <本機連接埠>**

Open `http://localhost:<本機連接埠>/MemberInfo/Index`, force a reload, expand a group, and open a contact. Confirm animated feedback appears in all three loading states and the final/error states still replace it.

- [x] **Step 4: Inspect scope**

Run:

```powershell
git diff --check
git status --short
git diff -- ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs
```

Expected: only the planned MemberInfo loading behavior, tests, and CCG documentation changed.

- [x] **Step 5: Commit**

```powershell
git add ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs docs/superpowers/plans/2026-07-15-member-info-loading-animation.md
git commit -m "feat(memberinfo): animate long loading states"
```
