# 會友資訊版面與搜尋互動 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成方案 A 的組織層級版面、可取消的全頁搜尋、直接替換樹狀畫面的搜尋結果表格，以及手機三種會友表格的手指水平滑動。

**Architecture:** 保留既有 `SearchDistrictTree` 授權與查詢入口，擴充回應模型帶回完整 `GroupMemberRowViewModel`。前端以明確搜尋狀態控制全頁遮罩、按鈕與結果區；原樹狀 DOM 不被搜尋重畫，取消或返回只切換可見區域。所有 DataGrid 共用關閉欄位隱藏及原生水平捲動設定。

**Tech Stack:** ASP.NET Core MVC、Razor、jQuery AJAX、DevExtreme DataGrid 21.2、xUnit、FluentAssertions

---

### Task 1: 建立會失敗的後端行為測試

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`

- [ ] **Step 1: 新增未填牧區留白測試**

在 `Build_UsesFallbacksAndKeepsChurchUngroupedNodeWhenEmpty` 中加入：

```csharp
tree.Districts.Single().AreaName.Should().BeEmpty();
```

- [ ] **Step 2: 新增搜尋完整列的去重與排序測試**

```csharp
[Fact]
public void Build_ReturnsDistinctAuthorizedRowsSortedByName()
{
    var memberships = new[] { Member("L1", "C1"), Member("L2", "C2") };
    var rows = new[]
    {
        Row("C2", "吳宜臻"),
        Row("c1", "吳啟光"),
        Row("C1", "重複列"),
        Row("C9", "不可見")
    };

    var result = MemberInfoTreeSearchBuilder.Build(
        memberships,
        new[] { "C1", "C2" },
        true,
        rows);

    result.Rows.Select(row => row.ContactId).Should().ContainInOrder("c1", "C2");
}
```

加入 `Row` helper，產生 `GroupMemberRowViewModel`。

- [ ] **Step 3: 新增 Controller 契約測試**

確認搜尋使用完整欄位、關係目標及列模型：

```csharp
Source.Should().Contain("BuildStrictCurrentContactQuery(");
Source.Should().Contain("GetTreeContactColumns()");
Source.Should().Contain("BatchRelationGoals(service, matchingContacts");
Source.Should().Contain("BuildMemberRows(service, matchingContacts, relations)");
```

- [ ] **Step 4: 執行測試並確認 RED**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore`

Expected: 失敗原因是 `Rows`／四參數 `Build` 尚不存在，以及未填牧區目前仍為 `(未填牧區)`。

### Task 2: 建立會失敗的前端契約測試

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`

- [ ] **Step 1: 新增方案 A 與人數位置測試**

確認 View 具有 `mi-leader-district`、`mi-leader-group`、`mi-count` 被加入 leader line，且不把 count 直接附加到 header。

- [ ] **Step 2: 新增搜尋狀態測試**

確認存在 `miSearchBtn`、`miSearchOverlay`、`miSearchResults`、`miStopSearch`、`miRestoreBrowseView`、紅色停止狀態，以及結果直接以 `miMountMemberGrid` 掛載。

- [ ] **Step 3: 新增手機水平手勢測試**

確認三種 DataGrid 共用：

```javascript
columnHidingEnabled: false
scrolling: miGridScrollingOptions()
```

並確認 CSS 包含 `overflow-x: auto`、`-webkit-overflow-scrolling: touch`、`touch-action: pan-x pan-y`，且 View 不再出現 `columnHidingEnabled: true`。

- [ ] **Step 4: 執行測試並確認 RED**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore`

Expected: 新增契約因正式 View 尚未有按鈕、遮罩、結果容器與水平滑動設定而失敗。

### Task 3: 擴充搜尋回應資料列

**Files:**
- Modify: `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`
- Modify: `ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs`
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`
- Modify: `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs`

- [ ] **Step 1: 加入 `Rows` 回應欄位**

```csharp
public List<GroupMemberRowViewModel> Rows { get; set; } = new List<GroupMemberRowViewModel>();
```

- [ ] **Step 2: Builder 接受、過濾、去重與排序資料列**

第四個參數為可空 `IEnumerable<GroupMemberRowViewModel>? rows`；只保留 ContactId 位於 matching set 的列，以 ContactId 不分大小寫去重，最後依 FullName 與 ContactId 排序。

- [ ] **Step 3: Controller 產生完整搜尋列**

`SearchDistrictTree` 改用 `GetTreeContactColumns()` 查詢；授權後篩出 `matchingContacts`，批次取得關係目標，呼叫 `BuildMemberRows`，並將 rows 傳入 Builder。

- [ ] **Step 4: 未填牧區改為空字串**

```csharp
public const string MissingAreaName = "";
```

- [ ] **Step 5: 執行後端測試確認 GREEN**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore`

Expected: 後端新增測試通過；前端契約仍失敗。

### Task 4: 實作方案 A 組織標頭

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [ ] **Step 1: 重構標頭 DOM**

`miAppendHeaderText` 產生可選的牧區／小組標題與 `mi-leader-line`；人數 `mi-count` append 到 leader line，區長與小組長分別使用 `mi-leader-district`／`mi-leader-group`。

- [ ] **Step 2: 套用方案 A CSS**

區長 leader 使用較大靛藍字與靛藍徽章；小組 leader 使用較小青綠字與青綠徽章。空白標題不建立 DOM。

- [ ] **Step 3: 執行 View 契約測試**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore`

Expected: 組織標頭相關測試通過。

### Task 5: 實作搜尋按鈕、全頁遮罩與結果替換

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [ ] **Step 1: 新增畫面結構**

加入 `miSearchBtn`（位於重新同步左側）、`miSearchOverlay`、`miBrowseView` 與 `miSearchResults`。結果區包含摘要、Grid host 與零筆／錯誤訊息位置。

- [ ] **Step 2: 新增全頁遮罩與按鈕狀態 CSS**

遮罩採 fixed inset、spinner 與 reduced-motion；搜尋中按鈕變紅並高於遮罩，只有該按鈕可操作。

- [ ] **Step 3: 實作明確搜尋狀態**

新增 `miSetSearchMode`、`miStartSearch`、`miStopSearch`、`miShowSearchResults`、`miShowSearchError`、`miRestoreBrowseView`。請求 token 防止舊回應覆蓋；取消 abort 不顯示錯誤。

- [ ] **Step 4: 將 Enter 與按鈕接到同一入口**

移除輸入 300ms 自動搜尋；Enter 阻止預設提交並呼叫 `miStartSearch`。結果狀態的按鈕呼叫 `miRestoreBrowseView`。

- [ ] **Step 5: 照片批次載入涵蓋結果表格**

將僅限 `#memberInfoTree` 的可見頭像 selector 擴大到 `#memberInfoPage` 內的資料表。

### Task 6: 實作三種表格手機水平滑動

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [ ] **Step 1: 統一 DevExtreme scrolling 設定**

```javascript
function miGridScrollingOptions() {
    return { useNative: true, showScrollbar: 'always', scrollByContent: true, scrollByThumb: true };
}
```

`miMountMemberGrid`、`miMountUngroupedGrid` 與搜尋結果掛載均使用此設定，且 `columnHidingEnabled: false`。

- [ ] **Step 2: 加入原生水平容器 CSS**

`.mi-grid-host` 與 DevExtreme scroll container 設 `overflow-x: auto`、`-webkit-overflow-scrolling: touch`、`touch-action: pan-x pan-y`、`overscroll-behavior-x: contain`。

- [ ] **Step 3: 確認沒有自訂 touchmove preventDefault**

表格不新增手勢攔截器，讓原生水平滑動與頁面垂直滑動依方向自然共存。

- [ ] **Step 4: 執行完整 MemberInfo 測試確認 GREEN**

Run: `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore`

Expected: 全部通過。

### Task 7: 建置、審查與實際驗證

**Files:**
- Review: all modified files above

- [ ] **Step 1: Debug 建置**

Run: `dotnet build ChurchReport/ChurchReport.csproj -c Debug --no-restore`

Expected: 0 errors。

- [ ] **Step 2: 檢查差異範圍**

Run: `git diff --check` and `git diff --stat`

Expected: 無 whitespace error，只有本任務與未提交的設計／計畫／CCG 檔案。

- [ ] **Step 3: 雙模型審查**

依 CCG 規範並行呼叫 Gemini 與 Claude reviewer；若服務不可用，記錄實際錯誤並以本地測試、差異審查及實際瀏覽器驗證補強，不能假稱外部審查成功。

- [ ] **Step 4: VS 2026 實際驗證**

在 Worktree 方案執行 43372，驗證桌機搜尋／停止／返回／零筆／多筆與窄螢幕水平手勢；確認沒有「…」欄。

- [ ] **Step 5: 保持未提交**

不執行 `git commit`。保留工作樹變更供使用者測試。
