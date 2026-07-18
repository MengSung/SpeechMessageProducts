# 會友資訊固定頭像與姓名欄位 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓會友資訊的頭像與姓名在桌機水平捲動及手機左右滑動時固定於左側，並讓固定區本身仍可作為橫向手勢起點。

**Architecture:** 三種 MemberInfo DataGrid 繼續共用 `miMemberColumns()` 與單一 DevExtreme scrollable；前兩欄使用 DevExtreme 22.1.6 原生 fixed column。由共用 `onContentReady` 為固定資料列覆蓋層安裝一次性 touch bridge，只把確定的單指橫向位移轉送至同一個 `getScrollable()`，不建立額外表格或捲軸。

**Tech Stack:** ASP.NET Core Razor、JavaScript、DevExtreme DataGrid 22.1.6、CSS touch-action、xUnit、FluentAssertions、Node.js syntax check。

**Execution constraint:** 本任務依 AGENTS.md 的 M 任務規則由 lead agent Inline 執行。使用者要求不 Commit，因此所有 Commit checkpoint 改為 `git status`、`git diff --check` 與明確測試證據；不得提交或歸檔。

---

## File ownership map

- Test contract：`ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- Production view：`ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- Task evidence：`.ccg/tasks/freeze-member-avatar-name-columns/review.md`、`task.json`
- Approved design：`docs/superpowers/specs/2026-07-17-member-info-fixed-identity-columns-design.md`

不修改 Controller、DTO、CRM、權限、照片 API、搜尋 API 或會友細節 partial。

### Task 1: 建立固定欄與手機觸控轉接的失敗契約

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- Read: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [x] **Step 1: 在 `MemberInfoTreeViewContractTests` 加入固定欄契約**

```csharp
[Fact]
public void View_FixesOnlyAvatarAndNameColumnsOnTheLeft()
{
    var columns = Slice("function miMemberColumns(remotePaging)", "function miGridScrollingOptions()");

    columns.Should().MatchRegex(@"(?s)dataField:\s*'ContactId'[^}]*fixed:\s*true[^}]*fixedPosition:\s*'left'");
    columns.Should().MatchRegex(@"(?s)dataField:\s*'FullName'[^}]*fixed:\s*true[^}]*fixedPosition:\s*'left'");
    columns.Split("fixed: true", StringSplitOptions.None).Length.Should().Be(3);
    columns.Split("fixedPosition: 'left'", StringSplitOptions.None).Length.Should().Be(3);
}
```

此測試以出現次數限制只有頭像與姓名固定，避免性別以後的欄位被意外固定。

- [x] **Step 2: 加入固定區觸控行為契約**

```csharp
[Fact]
public void View_ForwardsFixedAreaHorizontalTouchToTheSingleGridScrollable()
{
    ViewText.Should().Contain("function miEnableFixedColumnTouchScroll(component)");
    ViewText.Should().Contain("function miMemberGridReady(e)");
    var bridge = Slice(
        "function miEnableFixedColumnTouchScroll(component)",
        "function miMemberGridReady(e)");

    bridge.Should().Contain(".dx-datagrid-rowsview .dx-datagrid-content-fixed");
    bridge.Should().Contain("component.getScrollable()");
    bridge.Should().Contain("Math.max(Math.abs(totalX), Math.abs(totalY)) < 6");
    bridge.Should().Contain("Math.abs(totalX) > Math.abs(totalY) ? 'x' : 'y'");
    bridge.Should().Contain("scrollable.scrollBy({ left: deltaX, top: 0 });");
    bridge.Should().Contain("event.preventDefault();");
    bridge.Should().Contain("event.stopPropagation();");

    ViewText.Should().Contain("touch-action: pan-y");
    ViewText.Should().Contain("onContentReady: miMemberGridReady");
}
```

- [x] **Step 3: 執行針對性測試並確認 RED**

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore --configuration Debug --filter "FullyQualifiedName~View_FixesOnlyAvatarAndNameColumnsOnTheLeft|FullyQualifiedName~View_ForwardsFixedAreaHorizontalTouchToTheSingleGridScrollable"
```

Expected: 兩項新測試失敗，原因分別是頭像／姓名沒有 `fixed` 設定，以及 touch bridge 尚不存在；不是編譯或測試拼字錯誤。

### Task 2: 以最小實作固定身分欄並保留手機手勢

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- Test: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`

- [x] **Step 1: 將頭像與姓名固定於左側**

在 `miMemberColumns()` 只修改前兩欄：

```javascript
{ dataField: 'ContactId', caption: '頭像', width: 72, fixed: true, fixedPosition: 'left',
  allowSorting: false, allowFiltering: false, cellTemplate: memberInfoAvatarCellTemplate },
{ dataField: 'FullName', caption: '姓名', width: 130, fixed: true, fixedPosition: 'left',
  cellTemplate: memberInfoNameCellTemplate },
```

性別至關係目標的欄位保持原狀且不得加入 `fixed`。

- [x] **Step 2: 限定固定資料列的 touch-action**

接在既有 `.mi-grid-host .dx-scrollable-container` 觸控樣式附近：

```css
.mi-grid-host .dx-datagrid-rowsview .dx-datagrid-content-fixed .dx-datagrid-table td {
    touch-action: pan-y;
}
```

`pan-y` 保留頁面垂直手勢，將固定儲存格的水平位移留給下一步的 touch bridge；不得把整個 DataGrid 設成 `touch-action: none`。

- [x] **Step 3: 建立固定區單次 touch bridge**

在 `miMemberColumns()` 後、`miGridScrollingOptions()` 前加入：

```javascript
function miEnableFixedColumnTouchScroll(component) {
    var element = component && typeof component.element === 'function' ? component.element() : null;
    var root = element && element.jquery ? element.get(0) : element;
    var fixedContent = root && root.querySelector(
        '.dx-datagrid-rowsview .dx-datagrid-content-fixed');
    var scrollable = component && typeof component.getScrollable === 'function'
        ? component.getScrollable()
        : null;

    if (!fixedContent || !scrollable || fixedContent.dataset.miTouchScrollBound === '1') { return; }
    fixedContent.dataset.miTouchScrollBound = '1';

    var gesture = null;
    var suppressClickUntil = 0;

    fixedContent.addEventListener('touchstart', function (event) {
        if (!event.touches || event.touches.length !== 1) { gesture = null; return; }
        var touch = event.touches[0];
        gesture = { startX: touch.clientX, startY: touch.clientY, lastX: touch.clientX, axis: '' };
    }, { passive: true });

    fixedContent.addEventListener('touchmove', function (event) {
        if (!gesture || !event.touches || event.touches.length !== 1) { return; }
        var touch = event.touches[0];
        var totalX = touch.clientX - gesture.startX;
        var totalY = touch.clientY - gesture.startY;

        if (!gesture.axis) {
            if (Math.max(Math.abs(totalX), Math.abs(totalY)) < 6) { return; }
            gesture.axis = Math.abs(totalX) > Math.abs(totalY) ? 'x' : 'y';
        }
        if (gesture.axis !== 'x') { return; }

        var deltaX = gesture.lastX - touch.clientX;
        gesture.lastX = touch.clientX;
        suppressClickUntil = Date.now() + 350;
        event.preventDefault();
        scrollable.scrollBy({ left: deltaX, top: 0 });
    }, { passive: false });

    function endGesture() { gesture = null; }
    fixedContent.addEventListener('touchend', endGesture, { passive: true });
    fixedContent.addEventListener('touchcancel', endGesture, { passive: true });
    fixedContent.addEventListener('click', function (event) {
        if (Date.now() > suppressClickUntil) { return; }
        event.preventDefault();
        event.stopPropagation();
    }, true);
}

function miMemberGridReady(e) {
    miEnableFixedColumnTouchScroll(e && e.component);
    window.memberInfoPreloadImages({ component: e.component });
}
```

門檻未達或判定為垂直手勢時不得呼叫 `preventDefault()`；普通點擊姓名仍進入既有細節流程。

- [x] **Step 4: 三種 DataGrid 共用相同 ready handler**

將 `miMountMemberGrid()` 與 `miMountUngroupedGrid()` 的既有 inline handler 都改成：

```javascript
onContentReady: miMemberGridReady
```

搜尋結果呼叫 `miMountMemberGrid()`，因此不另建第三份 handler。

- [x] **Step 5: 執行針對性測試並確認 GREEN**

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore --configuration Debug --filter "FullyQualifiedName~View_FixesOnlyAvatarAndNameColumnsOnTheLeft|FullyQualifiedName~View_ForwardsFixedAreaHorizontalTouchToTheSingleGridScrollable"
```

Expected: 2 passed, 0 failed。

### Task 3: 完整驗證與審查

**Files:**
- Verify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- Verify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- Create: `.ccg/tasks/freeze-member-avatar-name-columns/review.md`
- Modify: `.ccg/tasks/freeze-member-avatar-name-columns/task.json`

- [x] **Step 1: 執行完整 MemberInfo 測試**

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore --configuration Debug
```

Expected: 原有 103 項加 2 項新契約全部通過，0 failed、0 skipped。

- [x] **Step 2: 執行 Razor JavaScript 語法檢查**

從 `MemberInfoGrid.cshtml` 擷取唯一 `<script>` 內容，將 `@ViewBag.MemberInfoCanResync` 替換為 `true`，將 Razor `@(...)` 常值替換成合法 JavaScript literal，再透過 stdin 執行：

```powershell
node --check -
```

Expected: exit code 0，沒有 JavaScript syntax error。

- [x] **Step 3: 執行 Debug build**

```powershell
dotnet build ChurchReport/ChurchReport.csproj --configuration Debug --no-restore
```

Expected: 0 errors；若 VS 鎖定預設輸出，只改用 worktree 內已驗證的替代 `BaseOutputPath`，不得關閉使用者的 VS 或程序。

- [x] **Step 4: 驗證編碼與差異範圍**

```powershell
git diff --check
git status --short
```

以 strict UTF-8 decoder 驗證本任務新增／修改文字，並掃描 U+FFFD。Expected: 0 decode failures、0 U+FFFD；應用程式差異只包含上述 Razor 與測試檔，另保留使用者及其他既有 task／portable-kit 變更。

- [x] **Step 5: 平行重試 Gemini 與 Claude 審查**

依 AGENTS.md wrapper 模板，同時要求兩個 reviewer 檢查固定欄、手機手勢方向判定、click suppression、事件重複綁定、既有單一捲軸／adaptive dots 契約與變更範圍。外部失敗須原樣記錄，不得當作通過。

- [x] **Step 6: 寫入 review 與完成 task，但不 Commit／不歸檔**

`review.md` 記錄 Critical／Warning／Info、RED→GREEN、完整測試、語法、建置、UTF-8、diff 與外部模型狀態。所有 Critical 修正後，將 `task.json` 的 `status`／`currentPhase` 設為 `completed`，`nextAction` 設為等待使用者在 VS 的桌機與 320／390／430／640px 實際驗收。使用者要求自行 Commit，因此不得執行 CCG archive commit。

## Manual acceptance

1. 一般小組、無小組與多筆搜尋結果都滑到最右側，頭像與姓名仍固定。
2. 在 320、390／430、640px 從頭像、姓名及右側欄位起手左右滑，右側內容都能移動。
3. 在固定區上下滑，頁面垂直操作正常。
4. 輕點姓名會開啟正確會友細節；滑動姓名不會誤開。
5. 表格只有一條水平捲軸，沒有 adaptive 三點欄位。
