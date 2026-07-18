# 會友資訊欄位順序、姓名寬度與區／小組摘要 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將固定姓名欄縮為 62px 並移除人為最小寬度，依核准順序重排會友欄位，同時在區長與小組標頭顯示完整小組數、時間與地點。

**Architecture:** CRM list 的 `new_group_time`／`new_group_place` 透過既有單次 descriptor 查詢進入 `DistrictTreeBuilder`，再由擴充後的樹狀 ViewModel 傳給 Razor；`DistrictNodeViewModel.GroupCount` 在後端以完整 Groups 計算，避免前端分頁低估。三種 DataGrid 繼續共用 `miMemberColumns(remotePaging)`，只調整欄位設定與標頭呈現，不建立第二套資料流。

**Tech Stack:** ASP.NET Core MVC、C# 10／.NET 10、Dynamics CRM SDK、Razor、JavaScript、DevExtreme DataGrid 22.1.6、xUnit、FluentAssertions、Node.js syntax check。

---

## Execution constraints

- Active worktree：`.worktrees/Sunny_5.1.2.WorktreeTuneMemberView`。
- Active branch：`Sunny_5.1.2.WorktreeTuneMemberView`。
- 使用者要求不 Commit、不 merge、不 push；本計畫的 commit checkpoint 全部改為 `git diff`、測試輸出與 task evidence。
- 工作樹已有 portable-kit 與 `MemberInfoTreeViewContractTests.cs` 的未提交變更；所有 agent 必須先讀現況、保留它們，不可 reset、checkout 或覆寫。
- 本任務依 AGENTS.md 為 L+；實作採兩層 Parallel Spawn，所有 `spawn_agent` 必須使用 `fork_turns="none"`，同一層檔案所有權不得重疊。
- Gemini／Claude 分析已平行呼叫，但 Gemini 回 403「餘額不足」、Claude wrapper 回 status 1；不可記為通過，完成前仍需平行重試 review。
- 所有新增與修改文字使用 UTF-8；新註解以繁體中文說明「完整小組數不受分頁影響」與「只隱藏空的時間／地點摘要，不隱藏小組基本資訊」。

## File ownership map

### Layer 1 — tests only, parallel

- `test_backend`：`ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`
- `test_controller`：`ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
- `test_frontend`：`ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`

Layer 1 不得修改 production files，也不各自跑 `dotnet test`；三個 agent 完成後由 lead 統一執行針對性 RED，避免共用 `bin/obj` 競爭。

### Layer 2 — production only, parallel after RED

- `impl_backend`：
  - `ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs`
  - `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`
  - `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs`
- `impl_controller`：`ChurchReport/Controllers/MemberInfoController.cs`
- `impl_frontend`：`ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

Layer 2 不得修改測試或 task/docs。三個 implementation agent 都完成後，由 lead 統一執行針對性 GREEN、完整 suite、語法與 build。

### Lead-only evidence

- `.ccg/tasks/tune-member-info-columns-group-metadata/task.json`
- `.ccg/tasks/tune-member-info-columns-group-metadata/review.md`
- 本 Plan 的 checkbox 狀態
- portable-kit follow-up 僅記錄，不在本 application task 中改寫或重建 ZIP。

## Task 1: 建立 Backend Builder 的失敗契約

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`
- Read: `ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs`
- Read: `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`
- Read: `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs`

- [ ] **Step 1: 新增完整小組數與 metadata 行為測試**

在 `Build_FromTotalCurrentCount_DeductsDistinctCurrentGroupedContacts` 後加入：

```csharp
[Fact]
public void Build_PreservesGroupMetadataAndCountsAllDistrictGroups()
{
    // Reflection 讓 RED 階段在 property 尚不存在時產生清楚 assertion failure；
    // property 建立後，同一測試繼續驗證 descriptor → builder → view model 的完整映射。
    var first = Group("L1", "R1", "甲區長", "A牧區", "甲組");
    var second = Group("L2", "R1", "甲區長", "A牧區", "乙組");
    var descriptorTime = typeof(SmallGroupDescriptor).GetProperty("GroupTime");
    var descriptorPlace = typeof(SmallGroupDescriptor).GetProperty("GroupPlace");

    descriptorTime.Should().NotBeNull();
    descriptorPlace.Should().NotBeNull();
    descriptorTime!.SetValue(first, " 週五 19:30 ");
    descriptorPlace!.SetValue(first, " 教會二樓 ");
    descriptorTime!.SetValue(second, "   ");
    descriptorPlace!.SetValue(second, null);

    var tree = DistrictTreeBuilder.Build(
        new[] { first, second },
        new[] { Member("L1", "C1"), Member("L2", "C2") },
        new[] { "C1", "C2", "C3" },
        true,
        "church");

    var district = tree.Districts.Should().ContainSingle().Subject;
    var groupCount = district.GetType().GetProperty("GroupCount");
    groupCount.Should().NotBeNull();
    groupCount!.GetValue(district).Should().Be(2);

    var firstNode = district.Groups.Single(group => group.ListId == "L1");
    firstNode.GetType().GetProperty("GroupTime")!.GetValue(firstNode).Should().Be("週五 19:30");
    firstNode.GetType().GetProperty("GroupPlace")!.GetValue(firstNode).Should().Be("教會二樓");

    var secondNode = district.Groups.Single(group => group.ListId == "L2");
    secondNode.GetType().GetProperty("GroupTime")!.GetValue(secondNode).Should().Be(string.Empty);
    secondNode.GetType().GetProperty("GroupPlace")!.GetValue(secondNode).Should().Be(string.Empty);

    // 無小組入口與會員去重不影響實際小組節點數；既有小組基本資訊也不能因 metadata 空白消失。
    district.Groups.Should().OnlyContain(group => !string.IsNullOrWhiteSpace(group.GroupName));
    district.Groups.Should().OnlyContain(group => group.MemberCount == 1);
    tree.Ungrouped.Should().NotBeNull();
    tree.Ungrouped!.MemberCount.Should().Be(1);
}
```

- [ ] **Step 2: 僅檢查 test diff，不執行 production 修改**

Run:

```powershell
git diff -- ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs
```

Expected: 只新增一個 `[Fact]`；不得改動既有 helper、排序、未填區長或 ungrouped tests。

## Task 2: 建立 Controller CRM mapping 的失敗契約

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
- Read: `ChurchReport/Controllers/MemberInfoController.cs:1371-1442`

- [ ] **Step 1: 新增 list 欄位與 descriptor mapping 契約**

在 `Controller_MapsRelationGoalsIntoOneDtoField` 後加入：

```csharp
[Fact]
public void Controller_LoadsAndMapsSmallGroupTimeAndPlace()
{
    // 小組時間／地點必須加入既有 list descriptor query，禁止為每個小組再發一次 CRM request。
    // 原始碼契約同時鎖住 ColumnSet 與 descriptor mapping，避免只查到欄位卻漏傳給 Builder。
    Source.Should().Contain("\"new_group_time\"");
    Source.Should().Contain("\"new_group_place\"");
    Source.Should().Contain(
        "GroupTime = entity.GetAttributeValue<string>(\"new_group_time\") ?? string.Empty");
    Source.Should().Contain(
        "GroupPlace = entity.GetAttributeValue<string>(\"new_group_place\") ?? string.Empty");
}
```

- [ ] **Step 2: 檢查測試只約束既有 descriptor query**

Run:

```powershell
git diff -- ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs
```

Expected: 只新增上述 `[Fact]`；不得放寬既有授權、strict current contact 或 RelationGoals 契約。

## Task 3: 建立 Frontend 欄位與摘要的失敗契約

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- Read: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [ ] **Step 1: 更新姓名欄測試，保留目前未提交的 mount／touch bridge 斷言**

將既有 `View_UsesCompactResizableNameColumnAndLocksAvatarWidth` 改成：

```csharp
[Fact]
public void View_UsesApprovedMemberColumnOrderAndCompactName()
{
    // 62px 是 96px 的約 65%；只移除應用程式自行設定的 minWidth，原生 DevExtreme resizing 繼續負責拖曳。
    // 頭像仍固定 72px 且不可調寬／排序，姓名仍 fixed left，其他資料欄順序依產品核准排列。
    var columns = Slice("function miMemberColumns(remotePaging)", "function miGridScrollingOptions()");
    var fullNameStart = columns.IndexOf("dataField: 'FullName'", StringComparison.Ordinal);
    var fullNameEnd = columns.IndexOf("},", fullNameStart, StringComparison.Ordinal);
    fullNameStart.Should().BeGreaterThanOrEqualTo(0);
    fullNameEnd.Should().BeGreaterThan(fullNameStart);
    var fullNameColumn = columns[fullNameStart..(fullNameEnd + 2)];

    columns.Should().MatchRegex(
        @"(?s)dataField:\s*'ContactId'[^}]*width:\s*72[^}]*allowResizing:\s*false[^}]*allowSorting:\s*false");
    fullNameColumn.Should().Contain("width: 62");
    fullNameColumn.Should().Contain("fixed: true");
    fullNameColumn.Should().Contain("fixedPosition: 'left'");
    fullNameColumn.Should().NotContain("minWidth");
    columns.Split("allowResizing: false", StringSplitOptions.None).Length.Should().Be(2);

    var expectedFields = new[]
    {
        "ContactId", "FullName", "Phone", "BirthDate", "Address",
        "SpiritualIdentity", "MembershipStatus", "RelationGoals", "Gender"
    };
    var positions = expectedFields
        .Select(field => columns.IndexOf("dataField: '" + field + "'", StringComparison.Ordinal))
        .ToArray();

    positions.Should().OnlyHaveUniqueItems();
    positions.Should().BeInAscendingOrder();
    columns.Split("dataField:", StringSplitOptions.None).Length.Should().Be(10);
    columns.Should().MatchRegex(
        @"(?s)dataField:\s*'Phone'[^}]*caption:\s*'行動電話'[^}]*alignment:\s*'center'");
}
```

保留使用者目前未提交加入的以下內容，不得刪除：

```csharp
var memberGridMount = Slice("function miMountMemberGrid(host, rows)", "function miShowLoadFailure");
var ungroupedGridMount = Slice("function miMountUngroupedGrid(host)", "function miRenderUngroupedNode");
bridge.Should().NotContain(".dx-datagrid-headers");
```

- [ ] **Step 2: 新增區長 GroupCount 與條件式 metadata 契約**

在欄位測試後加入：

```csharp
[Fact]
public void View_RendersDistrictGroupCountsAndConditionalGroupMetadata()
{
    // countTexts 允許區長依序顯示「N 組」「本區 N 人」兩個 badge；無小組仍可傳入單一字串。
    var appendHeader = Slice("function miAppendHeaderText", "function miVisibleAreaName");
    appendHeader.Should().Contain("Array.isArray(countTexts) ? countTexts : [countTexts]");
    appendHeader.Should().Contain("metadataItems || []");
    appendHeader.Should().Contain("item.value");
    appendHeader.Should().Contain("mi-group-meta");

    var headers = Slice("function miDistrictHeader", "function miBirthText");
    headers.Should().Contain("(district.GroupCount || 0) + ' 組'");
    headers.Should().Contain("'本區 ' + (district.MemberCount || 0) + ' 人'");
    headers.IndexOf("(district.GroupCount || 0) + ' 組'", StringComparison.Ordinal)
        .Should().BeLessThan(headers.IndexOf("'本區 ' + (district.MemberCount || 0) + ' 人'", StringComparison.Ordinal));

    // 小組名稱、LeaderName 與 MemberCount 在 metadata filter 之前無條件傳入；時間／地點則各自 trim 後才顯示。
    headers.Should().Contain("group.GroupName || ''");
    headers.Should().Contain("'小組長：' + (group.LeaderName || '')");
    headers.Should().Contain("(group.MemberCount || 0) + ' 人'");
    headers.Should().Contain("label: '小組時間：', value: (group.GroupTime || '').trim()");
    headers.Should().Contain("label: '小組地點：', value: (group.GroupPlace || '').trim()");

    ViewText.Should().MatchRegex(@"(?s)\.mi-group-meta\s*\{[^}]*display:\s*flex[^}]*flex-wrap:\s*wrap");
}
```

- [ ] **Step 3: Lead 統一執行針對性 RED**

Layer 1 三個 agent 全部完成後，lead 執行：

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj `
  --no-restore --configuration Debug `
  --filter "FullyQualifiedName~Build_PreservesGroupMetadataAndCountsAllDistrictGroups|FullyQualifiedName~Controller_LoadsAndMapsSmallGroupTimeAndPlace|FullyQualifiedName~View_UsesApprovedMemberColumnOrderAndCompactName|FullyQualifiedName~View_RendersDistrictGroupCountsAndConditionalGroupMetadata"
```

Expected: 4 failed、0 passed；失敗原因分別是 descriptor／view model properties 不存在、Controller 缺 CRM 欄位 mapping、姓名仍為 96／80 舊契約、區／小組摘要尚未支援多 badge 與 metadata。若出現 syntax error、測試名稱拼錯或既有 user diff 遺失，先修測試再重新確認 RED。

## Task 4: 實作 Backend DTO 與 Builder

**Files:**
- Modify: `ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs`
- Modify: `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`
- Modify: `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs`
- Test: `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`

- [ ] **Step 1: 擴充 descriptor**

在 `SmallGroupDescriptor` 的 `LeaderName` 後加入：

```csharp
public string GroupTime { get; set; } = string.Empty;
public string GroupPlace { get; set; } = string.Empty;
```

- [ ] **Step 2: 擴充樹狀 ViewModel**

`GroupNodeViewModel` 在 `LeaderName` 後加入：

```csharp
public string GroupTime { get; set; } = string.Empty;
public string GroupPlace { get; set; } = string.Empty;
```

`DistrictNodeViewModel` 在 `MemberCount` 後加入：

```csharp
public int GroupCount { get; set; }
```

- [ ] **Step 3: Builder 傳遞 metadata**

在建立 `GroupNodeViewModel` 時使用以下完整欄位片段：

```csharp
district.Groups.Add(new GroupNodeViewModel
{
    ListId = group.ListId,
    GroupName = (group.GroupName ?? string.Empty).Trim(),
    LeaderName = string.IsNullOrWhiteSpace(group.LeaderName)
        ? MissingGroupLeaderName
        : group.LeaderName.Trim(),
    GroupTime = (group.GroupTime ?? string.Empty).Trim(),
    GroupPlace = (group.GroupPlace ?? string.Empty).Trim(),
    MemberCount = groupMembers?.Count ?? 0
});
```

- [ ] **Step 4: 以完整 Groups 設定 GroupCount**

將既有 district finalize loop 改成：

```csharp
foreach (var entry in districtNodes)
{
    entry.Value.MemberCount = districtMembers[entry.Key].Count;
    entry.Value.Groups = entry.Value.Groups
        .OrderBy(group => group.GroupName, StringComparer.Ordinal)
        .ThenBy(group => group.ListId, StringComparer.OrdinalIgnoreCase)
        .ToList();

    // GroupCount 必須來自未經前端分頁裁切的完整小組集合；「無小組」是獨立節點，不在此集合內。
    entry.Value.GroupCount = entry.Value.Groups.Count;
}
```

- [ ] **Step 5: 檢查 backend production diff**

Run:

```powershell
git diff -- ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs `
  ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs `
  ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs
```

Expected: 只新增兩個 metadata string properties、一個 GroupCount property、Builder mapping 與完整集合計數；不改 membership 去重、排序分桶或 ungrouped 計算。

## Task 5: 實作 Controller 的既有單次 CRM 查詢映射

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs:1392-1439`
- Test: `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`

- [ ] **Step 1: 擴充 list ColumnSet**

在 `new_contact_list_arealeader` 前加入時間與地點，使完整片段為：

```csharp
ColumnSet = new ColumnSet(
    "listid",
    "listname",
    "new_area_name",
    "new_contact_race_leager_list",
    "new_contact_family_leader_list",
    "new_group_time",
    "new_group_place",
    "new_contact_list_arealeader")
```

- [ ] **Step 2: 映射 descriptor**

在 `LeaderName` 前加入：

```csharp
GroupTime = entity.GetAttributeValue<string>("new_group_time") ?? string.Empty,
GroupPlace = entity.GetAttributeValue<string>("new_group_place") ?? string.Empty,
```

完整尾段保持：

```csharp
RaceLeaderName = raceLeader?.Name ?? string.Empty,
RaceLeaderKey = raceLeader?.Id.ToString() ?? string.Empty,
GroupTime = entity.GetAttributeValue<string>("new_group_time") ?? string.Empty,
GroupPlace = entity.GetAttributeValue<string>("new_group_place") ?? string.Empty,
LeaderName = groupLeader?.Name ?? string.Empty
```

- [ ] **Step 3: 確認沒有 N+1 查詢**

Run:

```powershell
git diff -- ChurchReport/Controllers/MemberInfoController.cs
```

Expected: 只修改 `FetchSmallGroupDescriptors` 的既有 `ColumnSet` 與 projection；不得新增 `service.Retrieve`、第二個 query loop、權限例外或 cache bypass。

## Task 6: 實作 Frontend 摘要與欄位順序

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- Test: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`

- [ ] **Step 1: 新增小組 metadata 樣式**

在 `.mi-leader-line`／`.mi-count` 樣式附近加入：

```css
.mi-group-meta {
    display: flex;
    align-items: center;
    gap: 6px 14px;
    flex-wrap: wrap;
    color: #64748b;
    font-size: .86rem;
    font-weight: 650;
    line-height: 1.45;
}
.mi-group-meta-item { display: inline-flex; align-items: baseline; }
```

在 `@@media (max-width: 640px)` 內加入：

```css
.mi-group-meta {
    gap: 4px 10px;
    font-size: var(--mi-mobile-label-font);
}
```

- [ ] **Step 2: 擴充共用標頭 helper**

將 `miAppendHeaderText` 完整替換為：

```javascript
// countTexts 可傳單一字串或陣列：區長使用兩個 badge，無小組與一般小組沿用單一 badge。
// metadataItems 只建立有值的項目；全部空白時不產生摘要列，因此小組名稱、Leader 與人數不受影響。
function miAppendHeaderText(header, title, leaderText, countTexts, leaderClass, metadataItems) {
    header.appendChild(miElement('span', 'mi-chevron', '▸'));
    var titleElement = miElement('span', 'mi-title');
    if (title) { titleElement.appendChild(miElement('span', 'mi-node-title', title)); }
    var leaderLine = miElement('span', 'mi-leader-line ' + leaderClass);
    if (leaderText) { leaderLine.appendChild(miElement('span', 'mi-leader-name', leaderText)); }

    var normalizedCounts = Array.isArray(countTexts) ? countTexts : [countTexts];
    normalizedCounts.forEach(function (countText) {
        if (countText) { leaderLine.appendChild(miElement('span', 'mi-count', countText)); }
    });
    titleElement.appendChild(leaderLine);

    var visibleMetadata = (metadataItems || []).filter(function (item) { return item.value; });
    if (visibleMetadata.length) {
        var metadataLine = miElement('span', 'mi-group-meta');
        visibleMetadata.forEach(function (item) {
            metadataLine.appendChild(miElement('span', 'mi-group-meta-item', item.label + item.value));
        });
        titleElement.appendChild(metadataLine);
    }

    header.appendChild(titleElement);
}
```

- [ ] **Step 3: 區長使用兩個 badge**

將 `miDistrictHeader` 的 helper call 改成：

```javascript
miAppendHeaderText(header, miVisibleAreaName(district.AreaName),
    '區長：' + (district.RaceLeaderName || ''),
    [
        (district.GroupCount || 0) + ' 組',
        '本區 ' + (district.MemberCount || 0) + ' 人'
    ],
    'mi-leader-district');
```

- [ ] **Step 4: 小組保留基本資訊並條件式顯示 metadata**

將 `miGroupHeader` 的 helper call 改成：

```javascript
miAppendHeaderText(header, group.GroupName || '',
    '小組長：' + (group.LeaderName || ''),
    (group.MemberCount || 0) + ' 人',
    'mi-leader-group',
    [
        { label: '小組時間：', value: (group.GroupTime || '').trim() },
        { label: '小組地點：', value: (group.GroupPlace || '').trim() }
    ]);
```

`miRenderUngroupedNode` 繼續傳入單一 count string，不需 metadata 參數；helper 會把字串正規化為一個 badge。

- [ ] **Step 5: 依核准順序重排共用欄位**

將 `miMemberColumns(remotePaging)` 完整 return array 改為：

```javascript
return [
    { dataField: 'ContactId', caption: '頭像', width: 72, fixed: true, fixedPosition: 'left',
      allowResizing: false, allowSorting: false, allowFiltering: false,
      cellTemplate: memberInfoAvatarCellTemplate },
    { dataField: 'FullName', caption: '姓名', width: 62,
      fixed: true, fixedPosition: 'left', cellTemplate: memberInfoNameCellTemplate },
    { dataField: 'Phone', caption: '行動電話', width: 124, alignment: 'center' },
    { dataField: 'BirthDate', caption: '生日', dataType: 'date', width: 108,
      alignment: 'center', customizeText: miBirthText },
    { dataField: 'Address', caption: '地址', width: 250 },
    { dataField: 'SpiritualIdentity', caption: '信仰狀態', width: 108, alignment: 'center' },
    { dataField: 'MembershipStatus', caption: '會員身份', width: 110, alignment: 'center' },
    { dataField: 'RelationGoals', caption: '關係目標', width: 264,
      allowSorting: !remotePaging },
    { dataField: 'Gender', caption: '性別', width: 64, alignment: 'center' }
];
```

不得加入 `FullName.minWidth`；不得更改 `miGridScrollingOptions`、fixed rows touch bridge、兩個 grid mount 的 widget resizing／single sorting 或搜尋資料流。

- [ ] **Step 6: 檢查 frontend production diff**

Run:

```powershell
git diff -- ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
```

Expected: 只包含 metadata CSS、共用 header helper、兩個 header calls 與九欄設定；不出現 adaptive dots、第二條 scrollbar、自訂 header drag handler 或搜尋／Loading 修改。

## Task 7: Lead 整合並確認針對性 GREEN

**Files:**
- Verify: Layer 1 的三個 test files
- Verify: Layer 2 的五個 production files

- [ ] **Step 1: 等待並檢查所有 Layer 2 agents**

Lead 必須逐一確認：檔案存在、沒有超出 ownership、沒有 Commit、沒有 reset user changes。然後執行：

```powershell
git status --short --untracked-files=all
git diff -- ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs `
  ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs `
  ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs `
  ChurchReport/Controllers/MemberInfoController.cs `
  ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml `
  ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs `
  ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs `
  ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs
```

- [ ] **Step 2: 執行針對性 GREEN**

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj `
  --no-restore --configuration Debug `
  --filter "FullyQualifiedName~Build_PreservesGroupMetadataAndCountsAllDistrictGroups|FullyQualifiedName~Controller_LoadsAndMapsSmallGroupTimeAndPlace|FullyQualifiedName~View_UsesApprovedMemberColumnOrderAndCompactName|FullyQualifiedName~View_RendersDistrictGroupCountsAndConditionalGroupMetadata"
```

Expected: 4 passed、0 failed、0 skipped。若失敗，修 production code，不放寬已批准的 test contract。

- [ ] **Step 3: 檢查差異格式**

```powershell
git diff --check -- ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs `
  ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs `
  ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs `
  ChurchReport/Controllers/MemberInfoController.cs `
  ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml `
  ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs `
  ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs `
  ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs
```

Expected: exit 0。

## Task 8: 完整自動驗證

**Files:**
- Verify: `ChurchReport.MemberInfo.Tests/**`
- Verify: `ChurchReport/ChurchReport.csproj`
- Verify: all task files and approved Spec／Plan

- [ ] **Step 1: 執行完整 MemberInfo tests**

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj `
  --no-restore --configuration Debug
```

Expected: 所有 tests passed、0 failed、0 skipped；最終總數以實際 runner 輸出記錄，不沿用舊 107 項推估。

- [ ] **Step 2: 執行 Razor JavaScript syntax check**

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

Expected: exit 0，沒有 syntax error。

- [ ] **Step 3: 執行 Debug build**

先執行預設輸出：

```powershell
dotnet build ChurchReport/ChurchReport.csproj --configuration Debug --no-restore
```

Expected: 0 errors。若 VS 2026／ChurchReport process 鎖住預設 DLL，不終止使用者程序，改用：

```powershell
$isolatedOutput = Join-Path $env:TEMP 'ChurchReport-member-info-columns-group-metadata\'
dotnet build ChurchReport/ChurchReport.csproj --configuration Debug --no-restore `
  -p:BaseOutputPath=$isolatedOutput
```

Expected: isolated build 0 errors；review.md 同時記錄原始 file-lock 與隔離 build 結果。

- [ ] **Step 4: strict UTF-8 與 U+FFFD**

```powershell
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$files = @(
  'ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs',
  'ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs',
  'ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs',
  'ChurchReport/Controllers/MemberInfoController.cs',
  'ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml',
  'ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs',
  'ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs',
  'ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs',
  'docs/superpowers/specs/2026-07-17-member-info-column-order-group-metadata-design.md',
  'docs/superpowers/plans/2026-07-17-member-info-column-order-group-metadata.md',
  '.ccg/tasks/tune-member-info-columns-group-metadata/task.json',
  '.ccg/tasks/tune-member-info-columns-group-metadata/requirements.md'
)
foreach ($file in $files) {
  $text = $utf8.GetString([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $file)))
  if ($text.Contains([char]0xfffd)) { throw "U+FFFD found: $file" }
}
```

Expected: 無 decode exception、無 U+FFFD。

- [ ] **Step 5: JavaScript／資料契約靜態核對**

```powershell
rg -n "width: 62|minWidth: 80|caption: '手機'|caption: '行動電話'|GroupCount|GroupTime|GroupPlace|new_group_time|new_group_place|mi-group-meta" `
  ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml `
  ChurchReport/Services/MemberInfo `
  ChurchReport/ViewModels/MemberInfoTree `
  ChurchReport/Controllers/MemberInfoController.cs `
  ChurchReport.MemberInfo.Tests
```

Expected: application `FullName` 使用 62 且不再使用 80px minWidth；Phone caption 只有「行動電話」；新 DTO／CRM／metadata tokens 均存在。

## Task 9: 審查與交付證據

**Files:**
- Create: `.ccg/tasks/tune-member-info-columns-group-metadata/review.md`
- Modify: `.ccg/tasks/tune-member-info-columns-group-metadata/task.json`

- [ ] **Step 1: Spawn 本機 review agent**

使用 `fork_turns="none"`，只讀審查以下八個 application／test files。輸出 Critical／Warning／Info，檢查：

- GroupCount 是否為完整區小組數且不包含無小組。
- metadata 兩項皆空時只隱藏摘要，基本小組資訊仍在。
- CRM query 是否維持單次、沒有 N+1。
- 欄位順序、62px、無 FullName minWidth、Phone 置中、Gender 最後。
- fixed columns、remote RelationGoals、單一 scrollbar、touch bridge、search／Loading 無回歸。

Critical 必須修正後重新 review；Warning 逐項判斷並記錄。

- [ ] **Step 2: 平行重試 Gemini 與 Claude reviewer**

以 AGENTS.md reviewer prompt 同時提交 scoped git diff。Expected outcome 有兩種：

1. 模型成功：合併去重 Critical／Warning／Info；Critical 修正後再次雙模型 review。
2. 模型服務失敗：原樣記錄 HTTP status／wrapper status，標為 unavailable，不得寫 APPROVED。

- [ ] **Step 3: 寫 review.md**

`review.md` 必須包含：

- HEAD／branch／worktree。
- 雙模型分析與 review 的實際狀態。
- RED 4 failed 的命令與原因。
- GREEN 4 passed。
- 完整 test count、0 failed。
- node syntax、build、strict UTF-8、U+FFFD、diff check。
- scoped files 與保留的既有 portable-kit user changes。
- 尚待 VS 2026 的 320／390／430／640px 與桌機人工驗收。
- 明確聲明沒有 Commit、merge、push、reset、checkout 或終止 VS process。

- [ ] **Step 4: 更新 task 為 review，而非提前歸檔**

在使用者尚未真機確認前，設定：

```json
{
  "status": "in_progress",
  "currentPhase": "review",
  "nextAction": "等待使用者於 VS 2026 驗收姓名欄、欄位順序、區小組摘要與手機操作"
}
```

使用者明確確認後才能將 task 標為 completed。因使用者要求不 Commit，本次不執行 AGENTS.md 的 archive commit；不得自行擴張權限。

## Task 10: 使用者人工驗收與 portable goal follow-up

**Files:**
- Read: `docs/superpowers/specs/2026-07-17-member-info-column-order-group-metadata-design.md`
- Follow-up only: `.ccg/tasks/build-member-info-portable-kit/**`

- [ ] **Step 1: VS 2026 驗收**

驗收一般小組、搜尋結果、無小組：

1. 姓名預設 62px，沒有應用程式設定的 80px 下限，可繼續向左拖小。
2. 頭像／姓名固定；仍只有一條水平捲軸；手機可從固定區與資料列左右滑動。
3. 區長顯示「N 組」與「本區 N 人」，順序正確且跨分頁仍為完整小組數。
4. 小組時間／地點都空時，只看不到該摘要列；小組名稱、小組長與人數仍清楚可見。
5. 單項有值只顯示該項；兩項有值依時間、地點順序顯示。
6. 欄位順序精確；行動電話置中；性別最後。
7. 表頭正反排序、欄寬拖曳、姓名開細節、搜尋與 Loading 均正常。

- [ ] **Step 2: 使用者確認後完成 application task**

將 `task.json` 的 `status`／`currentPhase` 改為 `completed`，`nextAction` 記錄由使用者自行 Commit；仍不替使用者提交或合併。

- [ ] **Step 3: 接續 paused portable-kit goal**

將本 Spec、Plan、使用者提示詞、測試 snapshot 與新增量 patch 納入 portable kit；保留舊 patch 歷史，新增下一號 patch，不覆寫既有 patch04。更新 reference、manifest 與 ZIP 前，不宣告 portable goal 完成。

## Self-review coverage map

| Approved requirement | Plan coverage |
|---|---|
| 姓名 62px、移除 FullName minWidth | Tasks 3、6、7、8、10 |
| 頭像 72px、不可調寬／排序、姓名 fixed | Tasks 3、6、8、10 |
| 區長小組數在本區人數左側 | Tasks 1、4、6、7、10 |
| GroupCount 不受前端分頁或無小組影響 | Tasks 1、4、9、10 |
| 小組時間／地點來自既有 CRM list query | Tasks 2、5、8、9 |
| 兩者皆空只隱藏 metadata，不隱藏小組基本資訊 | Tasks 1、3、4、6、9、10 |
| 欄位精確順序、行動電話置中、性別最後 | Tasks 3、6、7、8、10 |
| widget resize、single sort、remote RelationGoals | Tasks 3、6、8、9、10 |
| 單一 scrollbar、fixed touch、無 adaptive dots | Tasks 3、6、8、9、10 |
| UTF-8、完整註解、不 Commit | Execution constraints、Tasks 8–10 |

Self-review 結論：所有 Spec requirement 均有 production、test、automatic verification 與人工驗收對應；property 名稱在所有 Task 一致為 `GroupCount`、`GroupTime`、`GroupPlace`，CRM 欄位一致為 `new_group_time`、`new_group_place`。計畫沒有 placeholder，也沒有要求修改 scope 外的權限、照片、搜尋或會友細節。
