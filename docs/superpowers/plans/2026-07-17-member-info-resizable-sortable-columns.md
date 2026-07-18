# 會友資訊姓名欄縮窄、欄寬調整與表頭排序 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將固定姓名欄預設縮為 96px，並讓頭像以外的會友欄位可用滑鼠／手指拖曳表頭分隔線調寬，點表頭則以單欄模式切換升冪／降冪。

**Architecture:** 三種 MemberInfo DataGrid 繼續共用 `miMemberColumns(remotePaging)`；頭像與姓名仍 fixed left。兩個 grid mount 入口都啟用 DevExtreme 22.1.6 原生 `widget` column resizing 與 single sorting，不新增自訂 header drag handler；無小組的計算欄 `RelationGoals` 維持禁止 remote sorting。

**Tech Stack:** ASP.NET Core Razor、JavaScript、DevExtreme DataGrid 22.1.6、xUnit、FluentAssertions、Node.js syntax check。

**Execution constraint:** 依 AGENTS.md，本任務為 M／medium-risk，由 lead agent Inline 執行；寫正式程式前已平行呼叫 Gemini／Claude 分析，但 Gemini 回 403、Claude wrapper 回 status 1，不能記為通過。使用者要求不 Commit，因此所有 commit checkpoint 改為測試、`git diff --check` 與書面證據；不得提交、合併或歸檔。

---

## File ownership map

- Production view：`ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- Test contract：`ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- Approved design：`docs/superpowers/specs/2026-07-17-member-info-resizable-sortable-columns-design.md`
- Task evidence：`.ccg/tasks/resize-sort-member-columns/review.md`、`.ccg/tasks/resize-sort-member-columns/task.json`

不修改 Controller、API、DTO、CRM、權限、照片、搜尋資料流或會友細節 partial。可攜式部署套件的 9／9 更新由既有 `build-member-info-portable-kit` task 接續，不與本次兩個 application files 混寫。

### Task 1: 建立姓名寬度、調寬與排序的失敗契約

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- Read: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [x] **Step 1: 加入姓名／頭像欄位契約**

接在 `View_FixesOnlyAvatarAndNameColumnsOnTheLeft` 後加入：

```csharp
[Fact]
public void View_UsesCompactResizableNameColumnAndLocksAvatarWidth()
{
    // 96px 讓 3～4 字姓名保持可讀，同時把 320px 手機的右側可視區增加 34px；80px 是手動縮欄下限。
    // 頭像是固定尺寸的圖片識別欄，不提供沒有意義的調寬或排序操作。
    var columns = Slice("function miMemberColumns(remotePaging)", "function miGridScrollingOptions()");

    columns.Should().MatchRegex(
        @"(?s)dataField:\s*'ContactId'[^}]*width:\s*72[^}]*allowResizing:\s*false[^}]*allowSorting:\s*false");
    columns.Should().MatchRegex(
        @"(?s)dataField:\s*'FullName'[^}]*width:\s*96[^}]*minWidth:\s*80[^}]*fixed:\s*true[^}]*fixedPosition:\s*'left'");
    columns.Split("allowResizing: false", StringSplitOptions.None).Length.Should().Be(2);
}
```

`Split(...).Length == 2` 表示整個欄位工廠只出現一次 `allowResizing: false`，防止其他資料欄被意外鎖寬。

- [x] **Step 2: 加入兩個 DataGrid 的互動設定契約**

接在上項測試後加入：

```csharp
[Fact]
public void View_EnablesNativeColumnResizingAndSingleColumnSortingForEveryMemberGrid()
{
    // 一般／搜尋共用第一個 mount，Ungrouped 使用第二個 mount；兩處必須採相同 widget resize 與 single sort。
    // widget 模式只改目前欄與 grid 總寬，不偷壓相鄰欄；禁用 reordering 則保護固定頭像／姓名順序。
    ViewText.Split("allowColumnResizing: true", StringSplitOptions.None).Length.Should().Be(3);
    ViewText.Split("columnResizingMode: 'widget'", StringSplitOptions.None).Length.Should().Be(3);
    ViewText.Split("sorting: { mode: 'single' }", StringSplitOptions.None).Length.Should().Be(3);
    ViewText.Should().NotContain("allowColumnReordering: true");
    ViewText.Should().Contain("allowSorting: !remotePaging");
}
```

每個 token 的 split 長度 3 代表實際出現 2 次，剛好對應 `miMountMemberGrid` 與 `miMountUngroupedGrid`。

- [x] **Step 3: 執行針對性測試並確認 RED**

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore --configuration Debug --filter "FullyQualifiedName~View_UsesCompactResizableNameColumnAndLocksAvatarWidth|FullyQualifiedName~View_EnablesNativeColumnResizingAndSingleColumnSortingForEveryMemberGrid"
```

Expected: 2 failed、0 passed。第一項明確缺少 `width: 96`／`minWidth: 80`／`allowResizing: false`；第二項缺少兩個 grid 的 resizing／sorting 設定，而不是編譯或測試拼字錯誤。

### Task 2: 以 DevExtreme 原生設定完成最小實作

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- Test: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`

- [x] **Step 1: 縮窄姓名並鎖定頭像寬度**

在 `miMemberColumns(remotePaging)` 只修改前兩欄：

```javascript
{ dataField: 'ContactId', caption: '頭像', width: 72, fixed: true, fixedPosition: 'left',
  allowResizing: false, allowSorting: false, allowFiltering: false,
  cellTemplate: memberInfoAvatarCellTemplate },
{ dataField: 'FullName', caption: '姓名', width: 96, minWidth: 80,
  fixed: true, fixedPosition: 'left', cellTemplate: memberInfoNameCellTemplate },
```

不在性別至關係目標加入 `allowResizing: false`；`RelationGoals` 保留 `allowSorting: !remotePaging`。

- [x] **Step 2: 在一般／搜尋 grid 啟用原生調寬與單欄排序**

在 `miMountMemberGrid(host, rows)` 的 `dxDataGrid` options 中，接在 `showColumnLines: true` 後加入：

```javascript
allowColumnResizing: true,
columnResizingMode: 'widget',
sorting: { mode: 'single' },
```

保留 `columnAutoWidth: false`、`columnHidingEnabled: false`、`scrolling: miGridScrollingOptions()` 與 `onContentReady: miMemberGridReady`。

- [x] **Step 3: 在 Ungrouped remote grid 套用相同設定**

在 `miMountUngroupedGrid(host)` 的 `dxDataGrid` options 中，接在 `showColumnLines: true` 後加入相同三行：

```javascript
allowColumnResizing: true,
columnResizingMode: 'widget',
sorting: { mode: 'single' },
```

不得修改 `remoteOperations: { paging: true, sorting: true }` 或 `columns: miMemberColumns(true)`；因此姓名等實體欄位仍遠端排序，`RelationGoals` 仍不可排序。

- [x] **Step 4: 執行針對性測試並確認 GREEN**

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore --configuration Debug --filter "FullyQualifiedName~View_UsesCompactResizableNameColumnAndLocksAvatarWidth|FullyQualifiedName~View_EnablesNativeColumnResizingAndSingleColumnSortingForEveryMemberGrid"
```

Expected: 2 passed、0 failed、0 skipped。

- [x] **Step 5: 檢視最小差異**

```powershell
git diff -- ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs
git diff --check -- ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs
```

Expected: application diff 只含兩項新測試、兩欄設定與兩個 grid 各三項 options；`git diff --check` exit 0。

### Task 3: 完整驗證、審查與任務證據

**Files:**
- Verify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- Verify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- Create: `.ccg/tasks/resize-sort-member-columns/review.md`
- Modify: `.ccg/tasks/resize-sort-member-columns/task.json`

- [x] **Step 1: 執行完整 MemberInfo 測試**

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore --configuration Debug
```

Expected: 既有 105 項加 2 項新契約，共 107 passed、0 failed、0 skipped。

- [x] **Step 2: 執行 Razor JavaScript 語法檢查**

```powershell
$path = 'ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml'
$view = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $path), [Text.UTF8Encoding]::new($false, $true))
$start = $view.IndexOf('<script>', [StringComparison]::Ordinal)
$end = $view.LastIndexOf('</script>', [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'MemberInfoGrid script block not found.' }
$script = $view.Substring($start + 8, $end - ($start + 8))
$script = $script.Replace('@(ViewBag.MemberInfoCanResync == true ? "true" : "false")', 'true')
$script | node --check -
if ($LASTEXITCODE -ne 0) { throw 'node --check failed.' }
```

Expected: exit 0，沒有 JavaScript syntax error。

- [x] **Step 3: 執行 Debug build**

```powershell
dotnet build ChurchReport/ChurchReport.csproj --configuration Debug --no-restore
```

Expected: 0 errors。若 VS 鎖住預設輸出，記錄原始 lock 證據後改用以下隔離輸出，不得關閉 VS 或終止使用者程序：

```powershell
$isolatedOutput = Join-Path $env:TEMP 'ChurchReport-resize-sort-build\'
dotnet build ChurchReport/ChurchReport.csproj --configuration Debug --no-restore -p:BaseOutputPath=$isolatedOutput
```

- [x] **Step 4: 驗證嚴格 UTF-8、U+FFFD 與差異範圍**

```powershell
$utf8 = New-Object Text.UTF8Encoding($false, $true)
$files = @(
  'ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml',
  'ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs',
  'docs/superpowers/specs/2026-07-17-member-info-resizable-sortable-columns-design.md',
  'docs/superpowers/plans/2026-07-17-member-info-resizable-sortable-columns.md',
  '.ccg/tasks/resize-sort-member-columns/task.json'
)
foreach ($file in $files) {
  $text = $utf8.GetString([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $file)))
  if ($text.Contains([char]0xfffd)) { throw "U+FFFD found: $file" }
}
git diff --check
git status --short
```

Expected: 0 decode failures、0 U+FFFD、`git diff --check` exit 0；保留既有 portable-kit 變更，不覆蓋或誤算成此任務 application diff。

- [x] **Step 5: 平行重試 Gemini 與 Claude code review**

依 AGENTS.md reviewer 模板，以 `git diff -- ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs` 為範圍，同時檢查：96／80px、avatar opt-out、widget resize、single sorting、remote `RelationGoals`、固定欄 touch bridge、單一水平捲軸與三種 grid 一致性。外部模型失敗必須原樣記錄，不得當成通過；Critical／Important 若可由本機證據確認，修正後重新審查。

- [x] **Step 6: 寫入 review 與完成 application task**

`.ccg/tasks/resize-sort-member-columns/review.md` 記錄 RED→GREEN、107 tests、語法、build、UTF-8、diff、外部模型狀態與未完成的真機驗收。將 task 的 `status`／`currentPhase` 設為 `completed`，`nextAction` 設為等待使用者在 VS 的桌機與 320／390／430／640px 實測；不 Commit、不歸檔。

### Task 4: 人工操作驗收與可攜式 goal 銜接

**Files:**
- Read: `docs/superpowers/specs/2026-07-17-member-info-resizable-sortable-columns-design.md`
- Follow-up task: `.ccg/tasks/build-member-info-portable-kit/**`

- [ ] **Step 1: 在三種 DataGrid 驗收**

一般小組、無小組、搜尋結果都要驗證：姓名預設 96px；除頭像外可由表頭分隔線以滑鼠／手指調寬；頭像不能調寬；輕點表頭排序且再點切換方向；拖曳不排序；資料列姓名仍開正確明細。

- [ ] **Step 2: 在四種寬度驗收固定區與捲動**

於 320、390／430、640px 與桌機確認：頭像／姓名仍 fixed left、只有一條水平捲軸、右側資料可見範圍比 130px 姓名欄增加、固定區與右側資料列都能水平滑動、頁面仍能垂直滑動、沒有 adaptive dots。

- [ ] **Step 3: 接續既有 portable-kit task**

人工功能確認後，既有 `build-member-info-portable-kit` task 必須把本 Spec、Plan、使用者提示詞、整合契約、參考測試／patch 與驗收加入套件，權威數量更新為 9 Specs／9 Plans，再重建 deterministic Manifest 與 ZIP。此 follow-up 仍不 Commit，由使用者統一驗收。

## Self-review coverage map

| Approved requirement | Plan coverage |
|---|---|
| 姓名 96px／最小 80px | Task 1 Step 1、Task 2 Step 1 |
| 頭像 72px 且不可調寬／排序 | Task 1 Step 1、Task 2 Step 1 |
| 其餘欄位原生分隔線調寬 | Task 1 Step 2、Task 2 Steps 2–3、Task 4 Step 1 |
| widget 模式不壓縮下一欄 | Task 1 Step 2、Task 2 Steps 2–3 |
| 表頭單欄正反排序 | Task 1 Step 2、Task 2 Steps 2–3、Task 4 Step 1 |
| remote RelationGoals 不排序 | Task 1 Step 2、Task 2 Step 3 |
| 固定欄／單一捲軸／手機手勢不回歸 | Task 3 Steps 1–5、Task 4 Step 2 |
| 不 Commit、後續納入 portable goal | Execution constraint、Task 3 Step 6、Task 4 Step 3 |
