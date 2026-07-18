using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoTreeViewContractTests
{
    private static readonly string ViewText = File.ReadAllText(
        Path.Combine(FindRepositoryRoot(), "ChurchReport", "Views", "MemberInfo", "MemberInfoGrid.cshtml"));

    [Fact]
    public void View_UsesTreeEndpointsAndRemovesFlatGrid()
    {
        ViewText.Should().Contain("id=\"memberInfoTree\"");
        ViewText.Should().Contain("/MemberInfo/LoadDistrictTree");
        ViewText.Should().Contain("/MemberInfo/SearchDistrictTree");
        ViewText.Should().Contain("/MemberInfo/LoadGroupMembers");
        ViewText.Should().Contain("/MemberInfo/LoadUngroupedMembers");
        ViewText.Should().NotContain("LoadMemberInfoList");
        ViewText.Should().NotContain("memberInfoTogglePhotoFilter");
        ViewText.Should().NotContain("MemberInfoGridContainer");
    }

    [Theory]
    [InlineData("ContactId")]
    [InlineData("FullName")]
    [InlineData("Gender")]
    [InlineData("BirthDate")]
    [InlineData("Phone")]
    [InlineData("SpiritualIdentity")]
    [InlineData("Address")]
    [InlineData("MembershipStatus")]
    [InlineData("RelationGoals")]
    public void View_UsesPascalCaseMemberFields(string field)
    {
        ViewText.Should().Contain("dataField: '" + field + "'");
    }

    [Fact]
    public void View_UsesOneRelationGoalsColumnInsteadOfSplitColumns()
    {
        ViewText.Should().Contain("dataField: 'RelationGoals', caption: '關係目標'");
        ViewText.Should().NotContain("dataField: 'Relation'");
        ViewText.Should().NotContain("dataField: 'Goal'");
    }

    [Fact]
    public void View_DoesNotUseLowerCamelTreeDtoFields()
    {
        ViewText.Should().NotContain("tree.districts");
        ViewText.Should().NotContain("tree.hasUngrouped");
        ViewText.Should().NotContain("tree.ungroupedCount");
        ViewText.Should().NotContain("d.groups");
        ViewText.Should().NotContain("g.groupName");
        ViewText.Should().NotContain("data.contactId");
        ViewText.Should().NotContain("data.fullName");
    }

    [Fact]
    public void View_DisablesComputedColumnSortingOnlyForRemoteUngroupedGrid()
    {
        ViewText.Should().Contain("function miMemberColumns(remotePaging)");
        ViewText.Should().Contain("allowSorting: !remotePaging");
        ViewText.Should().Contain("columns: miMemberColumns(false)");
        ViewText.Should().Contain("columns: miMemberColumns(true)");
    }

    [Fact]
    public void View_TimesOutInitialTreeRequestAndOffersRetry()
    {
        ViewText.Should().Contain("$.ajax({ url: '/MemberInfo/LoadDistrictTree', type: 'GET', timeout: 30000 })");
        ViewText.Should().Contain("xhr && xhr.statusText === 'timeout' ? '載入逾時'");
        ViewText.Should().Contain("retry.addEventListener('click', function () { miLoadTree(); });");
    }

    [Fact]
    public void View_UsesAccessibleAnimatedLoadingCards()
    {
        ViewText.Should().Contain("function miLoadingHtml(compact)");
        ViewText.Should().Contain("role=\"status\" aria-live=\"polite\" aria-atomic=\"true\"");
        ViewText.Should().Contain("class=\"mi-loading-dots\" aria-hidden=\"true\"");
        ViewText.Should().Contain("class=\"mi-loading-dot\"");
        ViewText.Should().Contain("畫面沒有當掉");
        ViewText.Should().Contain("@@keyframes mi-loading-bounce");
        ViewText.Should().Contain("@@keyframes mi-loading-wash");
        ViewText.Should().Contain("@@media (prefers-reduced-motion: reduce)");
        ViewText.Should().Contain("miShowLoading(host, true);");
        ViewText.Should().Contain("miShowLoading(host, false);");
        ViewText.Should().Contain("var loadingHtml = miLoadingHtml(false);");
        ViewText.Should().NotContain("miElement('div', 'mi-message', '載入中...')");
        ViewText.Should().NotContain("member-info-detail-loading\">載入中...");
    }

    [Fact]
    public void View_UsesApprovedLeaderHierarchyAndKeepsCountsBesideNames()
    {
        // 區長與小組長各自使用核准的視覺層級；人數必須附著在姓名列，而非可展開／收合的外層 header。
        // 這可防止樣式重構後層級難以辨認，或人數被按鈕與箭頭推到另一側而失去與姓名的關聯。
        ViewText.Should().Contain("mi-leader-district");
        ViewText.Should().Contain("mi-leader-group");
        ViewText.Should().Contain("leaderLine.appendChild(miElement('span', 'mi-count', countText));");
        ViewText.Should().NotContain("header.appendChild(miElement('span', 'mi-count', countText));");
    }

    [Fact]
    public void View_UsesExplicitCancelableSearchAndInlineResults()
    {
        // 搜尋是明確按鈕啟動的狀態流程：browse、進行中 overlay、結果區與返回動作都要有獨立容器／入口。
        // 使用者停止搜尋時必須 abort 尚未完成的 XHR；成功後直接掛載完整 member rows，而不是只標示樹節點。
        // 同時禁止 input 即打 API 的舊模式，避免中文輸入法組字期間連續送出請求，造成結果閃爍與競態覆蓋。
        ViewText.Should().Contain("id=\"miSearchBtn\"");
        ViewText.Should().Contain("id=\"miSearchOverlay\"");
        ViewText.Should().Contain("id=\"miBrowseView\"");
        ViewText.Should().Contain("id=\"miSearchResults\"");
        ViewText.Should().Contain("function miStartSearch()");
        ViewText.Should().Contain("function miStopSearch()");
        ViewText.Should().Contain("function miRestoreBrowseView()");
        ViewText.Should().Contain("miState.searchXhr.abort();");
        ViewText.Should().Contain("停止搜尋");
        ViewText.Should().Contain("返回會友資訊");
        ViewText.Should().Contain("miMountMemberGrid(gridHost, rows);");
        ViewText.Should().NotContain("box.addEventListener('input'");
    }

    [Fact]
    public void View_AllMemberGridsUseNativeHorizontalTouchScrollingWithoutAdaptiveDots()
    {
        // 所有成員網格共用同一份 DevExtreme 原生捲動設定，欄位維持固定寬度且不折疊成 adaptive dots。
        // 外層 host 隱藏水平 overflow，把唯一的水平捲動責任交給 DataGrid 內部容器；pan-x/pan-y 則保留手機自然手勢。
        // 這組正反向契約防止舊的 1230px 外框或自訂 scrollbar 回流，否則手機會同時出現兩條水平捲軸且欄位難以操作。
        ViewText.Should().Contain("function miGridScrollingOptions()");
        ViewText.Should().Contain("useNative: true");
        ViewText.Should().Contain("showScrollbar: 'always'");
        ViewText.Should().Contain("scrollByContent: true");
        ViewText.Should().Contain("columnHidingEnabled: false");
        ViewText.Should().NotContain("columnHidingEnabled: true");
        ViewText.Should().NotContain("hidingPriority:");
        ViewText.Should().Contain("columnAutoWidth: false");
        ViewText.Should().Contain("elementAttr: { class: 'mi-wide-member-grid' }");
        ViewText.Should().MatchRegex(@"(?s)\.mi-wide-member-grid\s*\{[^}]*min-width:\s*0");
        ViewText.Should().MatchRegex(@"(?s)\.mi-grid-host\s*\{[^}]*overflow-x:\s*hidden");
        ViewText.Should().NotContain("min-width: 1230px");
        ViewText.Should().NotContain(".mi-grid-host::-webkit-scrollbar");
        ViewText.Should().Contain("-webkit-overflow-scrolling: touch");
        ViewText.Should().Contain("touch-action: pan-x pan-y");
    }

    [Fact]
    public void View_FixesOnlyAvatarAndNameColumnsOnTheLeft()
    {
        // 三種會友 DataGrid 共用同一份欄位工廠；只固定前兩欄，使用者滑到右側時仍能辨認目前會友。
        // 以出現次數限制 fixed 設定，避免性別以後的資料欄被誤固定，壓縮手機真正可滑動的區域。
        var columns = Slice("function miMemberColumns(remotePaging)", "function miGridScrollingOptions()");

        columns.Should().MatchRegex(@"(?s)dataField:\s*'ContactId'[^}]*fixed:\s*true[^}]*fixedPosition:\s*'left'");
        columns.Should().MatchRegex(@"(?s)dataField:\s*'FullName'[^}]*fixed:\s*true[^}]*fixedPosition:\s*'left'");
        columns.Split("fixed: true", StringSplitOptions.None).Length.Should().Be(3);
        columns.Split("fixedPosition: 'left'", StringSplitOptions.None).Length.Should().Be(3);
    }

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
            @"(?s)dataField:\s*'ContactId'[^}]*width:\s*72[^}]*fixed:\s*true[^}]*fixedPosition:\s*'left'[^}]*allowResizing:\s*false[^}]*allowSorting:\s*false");
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

    [Fact]
    public void View_SortsVisibleMembershipStatusByConfiguredOrderAndKeepsFallbacksLast()
    {
        // 儲存格繼續顯示 CRM 的中文會員身份；一般小組／搜尋結果依 metadata rank 排序，
        // 無小組遠端分頁把同一個 rank selector 交給後端，未知舊值與真正空白在正反向都置底。
        var columns = Slice("function miMemberColumns(remotePaging)", "function miGridScrollingOptions()");

        ViewText.Should().Contain("function miMembershipStatusSortValue(row)");
        ViewText.Should().Contain("row.MembershipStatusOrder");
        ViewText.Should().Contain("row.HasMembershipStatusValue");
        ViewText.Should().Contain("this.sortOrder === 'desc'");
        ViewText.Should().Contain("Number.MIN_SAFE_INTEGER + 1");
        ViewText.Should().Contain("Number.MAX_SAFE_INTEGER - 1");
        columns.Should().MatchRegex(
            @"(?s)dataField:\s*'MembershipStatus'[^}]*caption:\s*'會員身份'[^}]*" +
            @"calculateSortValue:\s*remotePaging\s*\?\s*'MembershipStatusOrder'\s*:\s*miMembershipStatusSortValue[^}]*" +
            @"sortOrder:\s*'asc'[^}]*sortIndex:\s*0");

        // metadata rank 只能作為隱藏排序鍵，不能多增加一個使用者看得到的欄位。
        columns.Should().NotContain("dataField: 'MembershipStatusOrder'");
        columns.Split("dataField:", StringSplitOptions.None).Length.Should().Be(10);
    }

    [Fact]
    public void View_RendersDistrictGroupCountsAndConditionalGroupMetadata()
    {
        // countTexts 先正規化為陣列，再逐筆建立 badge；這能同時支援區長的兩個統計與無小組的單一統計。
        var appendHeader = Slice("function miAppendHeaderText", "function miVisibleAreaName");
        appendHeader.Should().Contain(
            "var normalizedCounts = Array.isArray(countTexts) ? countTexts : [countTexts];");
        appendHeader.Should().Contain("normalizedCounts.forEach(function (countText)");
        var countLoopStart = appendHeader.IndexOf(
            "normalizedCounts.forEach(function (countText)", StringComparison.Ordinal);
        var countLoopEnd = appendHeader.IndexOf(
            "titleElement.appendChild(leaderLine);", StringComparison.Ordinal);
        countLoopStart.Should().BeGreaterThanOrEqualTo(0);
        countLoopEnd.Should().BeGreaterThan(countLoopStart);
        var countLoop = appendHeader[countLoopStart..countLoopEnd];
        countLoop.Should().Contain(
            "leaderLine.appendChild(miElement('span', 'mi-count', countText))");

        // metadata 必須逐項過濾空值並逐項渲染；全部為空時不建立摘要列，單項有值時仍可獨立顯示。
        appendHeader.Should().Contain(
            "var visibleMetadata = (metadataItems || []).filter(function (item) { return item.value; });");
        appendHeader.Should().Contain("if (visibleMetadata.length)");
        appendHeader.Should().Contain("visibleMetadata.forEach(function (item)");
        appendHeader.Should().Contain("item.label + item.value");
        appendHeader.Should().Contain("mi-group-meta");
        var metadataLoopStart = appendHeader.IndexOf(
            "visibleMetadata.forEach(function (item)", StringComparison.Ordinal);
        var metadataLoopEnd = appendHeader.IndexOf(
            "titleElement.appendChild(metadataLine);", StringComparison.Ordinal);
        metadataLoopStart.Should().BeGreaterThanOrEqualTo(0);
        metadataLoopEnd.Should().BeGreaterThan(metadataLoopStart);
        var metadataLoop = appendHeader[metadataLoopStart..metadataLoopEnd];
        metadataLoop.Should().Contain(
            "metadataLine.appendChild(miElement('span', 'mi-group-meta-item', item.label + item.value))");

        // 小組名稱、領袖與人數所在的 leader line 必須先加入標頭，不可被 metadata 是否有值所控制。
        var leaderLineAppend = appendHeader.IndexOf(
            "titleElement.appendChild(leaderLine);", StringComparison.Ordinal);
        var metadataCondition = appendHeader.IndexOf(
            "if (visibleMetadata.length)", StringComparison.Ordinal);
        leaderLineAppend.Should().BeGreaterThanOrEqualTo(0);
        metadataCondition.Should().BeGreaterThan(leaderLineAppend);

        var districtHeader = Slice("function miDistrictHeader", "function miGroupHeader");
        districtHeader.Should().MatchRegex(
            @"(?s)miAppendHeaderText\(\s*header,\s*miVisibleAreaName\(district\.AreaName\),\s*" +
            @"'區長：'\s*\+\s*\(district\.RaceLeaderName\s*\|\|\s*''\),\s*\[\s*" +
            @"\(district\.GroupCount\s*\|\|\s*0\)\s*\+\s*' 組',\s*" +
            @"'本區 '\s*\+\s*\(district\.MemberCount\s*\|\|\s*0\)\s*\+\s*' 人'\s*" +
            @"\],\s*'mi-leader-district'\s*\);");
        districtHeader.Should().Contain("(district.GroupCount || 0) + ' 組'");
        districtHeader.Should().Contain("'本區 ' + (district.MemberCount || 0) + ' 人'");
        districtHeader.IndexOf("(district.GroupCount || 0) + ' 組'", StringComparison.Ordinal)
            .Should().BeLessThan(districtHeader.IndexOf(
                "'本區 ' + (district.MemberCount || 0) + ' 人'", StringComparison.Ordinal));

        // 小組名稱、LeaderName 與 MemberCount 在 metadata filter 之前無條件傳入；時間／地點則各自 trim 後才顯示。
        var groupHeader = Slice("function miGroupHeader", "function miBirthText");
        groupHeader.Should().MatchRegex(
            @"(?s)miAppendHeaderText\(\s*header,\s*group\.GroupName\s*\|\|\s*'',\s*" +
            @"'小組長：'\s*\+\s*\(group\.LeaderName\s*\|\|\s*''\),\s*" +
            @"\(group\.MemberCount\s*\|\|\s*0\)\s*\+\s*' 人',\s*'mi-leader-group',\s*\[\s*" +
            @"\{\s*label:\s*'小組時間：',\s*value:\s*\(group\.GroupTime\s*\|\|\s*''\)\.trim\(\)\s*\},\s*" +
            @"\{\s*label:\s*'小組地點：',\s*value:\s*\(group\.GroupPlace\s*\|\|\s*''\)\.trim\(\)\s*\}\s*" +
            @"\]\s*\);");
        groupHeader.Should().Contain("label: '小組時間：', value: (group.GroupTime || '').trim()");
        groupHeader.Should().Contain("label: '小組地點：', value: (group.GroupPlace || '').trim()");

        ViewText.Should().MatchRegex(@"(?s)\.mi-group-meta\s*\{[^}]*display:\s*flex[^}]*flex-wrap:\s*wrap");
    }

    [Fact]
    public void View_EnablesNativeColumnResizingAndSingleColumnSortingForEveryMemberGrid()
    {
        // 一般／搜尋共用第一個 mount，Ungrouped 使用第二個 mount；兩處必須採相同 widget resize 與 single sort。
        // widget 模式只改目前欄與 grid 總寬，不偷壓相鄰欄；禁用 reordering 則保護固定頭像／姓名順序。
        var memberGridMount = Slice("function miMountMemberGrid(host, rows)", "function miShowLoadFailure");
        var ungroupedGridMount = Slice("function miMountUngroupedGrid(host)", "function miRenderUngroupedNode");

        memberGridMount.Should().Contain("allowColumnResizing: true");
        memberGridMount.Should().Contain("columnResizingMode: 'widget'");
        memberGridMount.Should().Contain("sorting: { mode: 'single' }");
        ungroupedGridMount.Should().Contain("allowColumnResizing: true");
        ungroupedGridMount.Should().Contain("columnResizingMode: 'widget'");
        ungroupedGridMount.Should().Contain("sorting: { mode: 'single' }");

        ViewText.Split("allowColumnResizing: true", StringSplitOptions.None).Length.Should().Be(3);
        ViewText.Split("columnResizingMode: 'widget'", StringSplitOptions.None).Length.Should().Be(3);
        ViewText.Split("sorting: { mode: 'single' }", StringSplitOptions.None).Length.Should().Be(3);
        ViewText.Should().NotContain("allowColumnReordering: true");
        ViewText.Should().Contain("allowSorting: !remotePaging");
    }

    [Fact]
    public void View_ForwardsFixedAreaHorizontalTouchToTheSingleGridScrollable()
    {
        // DevExtreme 22.1.6 會以覆蓋層呈現 fixed column；固定區必須把明確的水平手勢轉送到同一個 scrollable。
        // 方向門檻、垂直手勢保留及滑動後 click 抑制缺一不可，否則會造成頁面不能上下滑或誤開會友細節。
        ViewText.Should().Contain("function miEnableFixedColumnTouchScroll(component)");
        ViewText.Should().Contain("function miMemberGridReady(e)");
        var bridge = Slice(
            "function miEnableFixedColumnTouchScroll(component)",
            "function miMemberGridReady(e)");

        bridge.Should().Contain(".dx-datagrid-rowsview .dx-datagrid-content-fixed");
        bridge.Should().NotContain(".dx-datagrid-headers");
        bridge.Should().Contain("component.getScrollable()");
        bridge.Should().Contain("Math.max(Math.abs(totalX), Math.abs(totalY)) < 6");
        bridge.Should().Contain("Math.abs(totalX) > Math.abs(totalY) ? 'x' : 'y'");
        bridge.Should().Contain("scrollable.scrollBy({ left: deltaX, top: 0 });");
        bridge.Should().Contain("event.preventDefault();");
        bridge.Should().Contain("event.stopPropagation();");

        ViewText.Should().Contain("touch-action: pan-y");
        ViewText.Should().Contain("onContentReady: miMemberGridReady");
    }

    [Fact]
    public void View_UsesOnlyTheDevExtremeHorizontalScrollbar()
    {
        // DataGrid 必須直接初始化在 host；若再建立一層 gridElement，host 與 DevExtreme 容器都可能各自產生水平捲軸。
        // 這是對上個測試的結構性護欄，確保「單一水平捲軸」不是只靠目前 CSS 恰巧遮住第二條。
        ViewText.Should().Contain("$(host).dxDataGrid({");
        ViewText.Should().NotContain("function miCreateMemberGridElement(host)");
        ViewText.Should().NotContain("$(gridElement).dxDataGrid({");
    }

    [Fact]
    public void View_RestoresBrowseUiBeforeSafelyDisposingSearchGrid()
    {
        // 只切出清理函式本體，避免檔案其他位置出現 try/finally 就讓測試誤判通過。
        // dispose 即使因 DevExtreme 生命週期異常而失敗，也必須由 finally 清掉舊 instance，讓下一次搜尋可以重新掛載。
        var clearGrid = Slice("function miClearSearchResultGrid()", "function miShowSearchResultMessage");
        clearGrid.Should().Contain("try {");
        clearGrid.Should().Contain("catch (error)");
        clearGrid.Should().Contain("finally {");

        // 返回瀏覽狀態必須先恢復 browse、隱藏 results 並把狀態設回 idle，最後才處置搜尋網格。
        // 這個順序避免 disposal 丟例外時整頁卡在空白搜尋畫面，使用者既看不到樹也無法重新搜尋。
        var restore = Slice("function miRestoreBrowseView()", "function miStopSearch()");
        restore.IndexOf("resultsView.hidden = true", StringComparison.Ordinal)
            .Should().BeLessThan(restore.IndexOf("miClearSearchResultGrid();", StringComparison.Ordinal));
        restore.IndexOf("browseView.hidden = false", StringComparison.Ordinal)
            .Should().BeLessThan(restore.IndexOf("miClearSearchResultGrid();", StringComparison.Ordinal));
        restore.IndexOf("miSetSearchMode('idle');", StringComparison.Ordinal)
            .Should().BeLessThan(restore.IndexOf("miClearSearchResultGrid();", StringComparison.Ordinal));
    }

    [Fact]
    public void View_UsesFluidMobileTypographyAndAccessibleTouchTargets()
    {
        // Slice 將檢查限制在 640px 手機 media query，避免桌面樣式中同名 token 讓測試假通過。
        // 五組 clamp 變數分別服務區長、樹內容、操作標籤、表頭與資料格，讓窄螢幕縮放時仍維持清楚的資訊層級。
        var mobile = Slice("@@media (max-width: 640px)", "iOS Safari");

        // 48px 操作列與 44x44 圖示按鈕鎖定可觸控面積；表頭／資料格 selector 則確認字級真正套到 DevExtreme 產生的節點。
        // 這可防止只宣告 CSS 變數卻未使用，或手機版為塞入更多欄位而退回過小、難以閱讀與點擊的版面。
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

    [Fact]
    public void View_KeepsSearchAndResyncActionsOnOneResponsiveRow()
    {
        // 操作列不得換行；搜尋框以 min-width: 0 承擔縮小空間，重新同步按鈕則維持可讀字級與 48px 觸控高度。
        // 這可防止窄螢幕把同步動作擠到第二列，造成樹內容向下跳動或按鈕被誤認為與搜尋無關。
        ViewText.Should().Contain(".mi-tree-actions");
        ViewText.Should().Contain("flex-wrap: nowrap");
        ViewText.Should().Contain(".mi-search {");
        ViewText.Should().Contain("min-width: 0");
        ViewText.Should().Contain(".mi-btn-resync");
        ViewText.Should().Contain("font-size: var(--mi-mobile-label-font)");
        ViewText.Should().Contain("min-height: 48px");
    }

    [Fact]
    public void View_KeepsNativeSearchInputAtIosSafeFontSize()
    {
        // iOS Safari 會在聚焦小於 16px 的原生輸入框時自動放大；限制在手機區塊檢查，確保修正不是只存在於桌面樣式。
        var mobile = Slice("@@media (max-width: 640px)", "iOS Safari");

        // 高特異性 selector 必須涵蓋實際搜尋輸入框，同時絕不能用 viewport 禁止縮放來掩蓋自動 zoom。
        // 後兩個否定契約保留使用者 pinch-to-zoom 的無障礙能力，避免修好輸入體驗卻犧牲整頁可縮放性。
        mobile.Should().MatchRegex(@"(?s)\.mi-search\s*\{[^}]*font-size:\s*16px");
        ViewText.Should().Contain("#memberInfoPage #miTreeSearch,");
        ViewText.Should().NotContain("user-scalable=no");
        ViewText.Should().NotContain("maximum-scale=1");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ChurchReport.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate ChurchReport.sln from test output directory.");
    }

    private static string Slice(string startMarker, string endMarker)
    {
        // 以具名標記擷取單一 CSS／函式區段，讓區域性契約不會被檔案其他同名字串誤滿足。
        // 標記遺失或順序改變時先在這裡提供明確失敗，再回傳半開區間供各測試檢查真正的實作本體。
        var start = ViewText.IndexOf(startMarker, StringComparison.Ordinal);
        var end = ViewText.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return ViewText[start..end];
    }
}
