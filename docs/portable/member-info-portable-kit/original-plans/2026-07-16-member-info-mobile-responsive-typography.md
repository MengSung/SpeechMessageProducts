# Member Info Mobile Responsive Typography Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓 320–640px 手機畫面的會友資訊工具列、樹狀卡片與 DataGrid 依寬度流動調整字體及觸控尺寸，同時保留完整欄位與單一水平滑動。

**Architecture:** 所有變更限制在 `MemberInfoGrid.cshtml` 既有的 `max-width: 640px` media query，透過 CSS custom properties 與 `clamp()` 完成，不新增 resize JavaScript。DevExtreme 欄位、資料、API 與桌機樣式不變。

**Tech Stack:** ASP.NET Core Razor、CSS `clamp()`／custom properties、DevExtreme DataGrid 21.2、xUnit、FluentAssertions

---

### Task 1: 手機流動字級與觸控尺寸

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml:467-488`

- [x] **Step 1: 寫入會失敗的手機可讀性契約測試**

在 `MemberInfoTreeViewContractTests` 加入：

```csharp
[Fact]
public void View_UsesFluidMobileTypographyAndAccessibleTouchTargets()
{
    var mobile = Slice("@@media (max-width: 640px)", "iOS Safari");

    mobile.Should().Contain("--mi-mobile-district-font: clamp(");
    mobile.Should().Contain("--mi-mobile-tree-font: clamp(");
    mobile.Should().Contain("--mi-mobile-label-font: clamp(");
    mobile.Should().Contain("--mi-mobile-grid-font: clamp(");
    mobile.Should().Contain("--mi-mobile-grid-header-font: clamp(");
    mobile.Should().Contain("min-height: 48px");
    mobile.Should().Contain("width: 44px");
    mobile.Should().Contain("height: 44px");
    mobile.Should().Contain(".dx-datagrid-headers .dx-row > td");
    mobile.Should().Contain(".dx-datagrid-rowsview .dx-row > td");
    mobile.Should().Contain("font-size: var(--mi-mobile-grid-header-font)");
    mobile.Should().Contain("font-size: var(--mi-mobile-grid-font)");
}
```

並將 `View_KeepsSearchAndResyncActionsOnOneResponsiveRow` 的舊 `.78rem` 斷言改為：

```csharp
ViewText.Should().Contain("font-size: var(--mi-mobile-label-font)");
ViewText.Should().Contain("min-height: 48px");
```

- [x] **Step 2: 執行目標測試確認 RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~View_UsesFluidMobileTypographyAndAccessibleTouchTargets|FullyQualifiedName~View_KeepsSearchAndResyncActionsOnOneResponsiveRow"
```

Expected: FAIL；目前手機按鈕為 `.78rem`／40px，且沒有流動字級與 DataGrid 行動樣式。

- [x] **Step 3: 在 mobile media query 加入最小 CSS 實作**

以以下內容取代目前 media query 內的手機縮小規則：

```css
@@media (max-width: 640px) {
    .member-info-page {
        --mi-mobile-district-font: clamp(1.125rem, calc(1rem + .65vw), 1.25rem);
        --mi-mobile-tree-font: clamp(1rem, calc(.875rem + .65vw), 1.125rem);
        --mi-mobile-label-font: clamp(.875rem, calc(.8125rem + .3vw), .9375rem);
        --mi-mobile-grid-font: clamp(.9375rem, calc(.875rem + .3vw), 1rem);
        --mi-mobile-grid-header-font: clamp(1rem, calc(.9375rem + .32vw), 1.0625rem);
        padding: 8px 4px 14px;
    }

    .member-info-toolbar {
        align-items: flex-start;
        flex-direction: column;
    }

    .member-info-title { font-size: var(--mi-mobile-district-font); line-height: 1.4; }
    .member-info-access,
    .mi-resync-status,
    .mi-search-results-summary { font-size: var(--mi-mobile-label-font); line-height: 1.4; }

    .mi-tree-actions { gap: clamp(3px, 1vw, 6px); }
    .mi-search {
        height: 48px;
        padding: 0 clamp(5px, 1.5vw, 9px);
        font-size: 16px;
        line-height: 1.5;
    }
    .mi-btn {
        min-height: 48px;
        padding: 0 clamp(5px, 1.5vw, 9px);
        font-size: var(--mi-mobile-label-font);
        line-height: 1.4;
    }
    .mi-btn-search { gap: clamp(3px, 1vw, 5px); }
    .mi-search-btn-icon { font-size: clamp(1rem, calc(.875rem + .5vw), 1.125rem); }

    .mi-district-header {
        min-height: clamp(64px, 12vw, 72px);
        padding: clamp(10px, 2.5vw, 14px);
        align-items: center;
    }
    .mi-group-header {
        min-height: clamp(72px, 15vw, 84px);
        padding: clamp(10px, 2.5vw, 14px);
        align-items: center;
    }
    .mi-chevron {
        width: 44px;
        height: 44px;
        flex-basis: 44px;
        font-size: 24px;
    }
    .mi-title { gap: 6px; }
    .mi-node-title,
    .mi-leader-group { font-size: var(--mi-mobile-tree-font); line-height: 1.5; }
    .mi-leader-district { font-size: var(--mi-mobile-district-font); line-height: 1.4; }
    .mi-leader-line { gap: clamp(6px, 1.5vw, 10px); }
    .mi-count {
        padding: 4px clamp(8px, 2vw, 12px);
        font-size: var(--mi-mobile-label-font);
        line-height: 1.4;
    }

    .mi-pager { font-size: var(--mi-mobile-tree-font); line-height: 1.5; }
    .mi-pager button {
        min-height: 48px;
        font-size: var(--mi-mobile-label-font);
    }
    .mi-search-results-title { font-size: var(--mi-mobile-tree-font); line-height: 1.5; }
    .mi-search-results-header { align-items: flex-start; flex-direction: column; }

    #memberInfoPage .dx-datagrid-headers .dx-row > td {
        padding-top: clamp(10px, 2vw, 13px);
        padding-bottom: clamp(10px, 2vw, 13px);
        font-size: var(--mi-mobile-grid-header-font);
        line-height: 1.4;
    }
    #memberInfoPage .dx-datagrid-rowsview .dx-row > td {
        padding-top: clamp(8px, 2vw, 12px);
        padding-bottom: clamp(8px, 2vw, 12px);
        font-size: var(--mi-mobile-grid-font);
        line-height: 1.5;
    }
    #memberInfoPage .member-info-name-link {
        display: inline-flex;
        min-height: 44px;
        align-items: center;
        font-size: inherit;
        line-height: inherit;
    }

    .mi-grid-host { overflow-x: hidden; }
}
```

- [x] **Step 4: 執行目標測試確認 GREEN**

使用 Step 2 相同命令。

Expected: PASS。

- [x] **Step 5: 執行完整驗證**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --nologo
dotnet build ChurchReport.sln -c Debug --no-restore --no-incremental --nologo
git diff --check
```

另抽取 Razor `<script>`，將 `ViewBag.MemberInfoCanResync` 替換為 `true` 後執行 `node --check -`。

Expected: 所有測試通過；JavaScript 語法通過；Debug build 為 0 warnings、0 errors；`git diff --check` 無錯誤。

- [x] **Step 6: 重新啟動 VS 2026 並交由使用者驗收**

確認 `<本機連接埠>` 使用目前 Worktree，請使用者以 320、390／430、640px 驗證字級、按鈕、單列工具列與 DataGrid 手指橫滑。依使用者指示不執行 Git Commit。
