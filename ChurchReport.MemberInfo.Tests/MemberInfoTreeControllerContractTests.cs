using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 MemberInfo 樹狀與網格 action 的公開 MVC 契約。測試只讀取目前 worktree 的 source，故障模型涵蓋
/// P7.4 將承諾 metadata 改為 request-local typed snapshot 後，意外保留同步 action 或遺漏原有授權與列投影。
/// 所有 source 字串與檔案 handle 僅存活於單一測試呼叫，不會建立 CRM、Gateway、Session、cache 或背景資源。
/// </summary>
public class MemberInfoTreeControllerContractTests
{
    private static readonly string Source = File.ReadAllText(
        Path.Combine(FindRepositoryRoot(), "SpeechMessageProducts.ChurchReport", "Controllers", "MemberInfoController.cs"));

    /// <summary>
    /// 保護 <c>LoadDistrictTree</c>、<c>SearchDistrictTree</c>、<c>LoadGroupMembers</c> 與
    /// <c>LoadUngroupedMembers</c> 的公開 MVC 簽章契約。此測試直接讀取編譯前 controller source；故障注入是將
    /// 任一已非同步 action 降回同步簽章，或讓既有 tree action 不再以正確公開簽章暴露。決定性斷言是
    /// <c>LoadDistrictTree</c> 保留既有同步讀取邊界，而其餘三個 action 必須回傳
    /// <see cref="System.Threading.Tasks.Task"/>，使 request-local Package03 metadata 的取消可由 ASP.NET Core 正確
    /// 傳遞。測試不建立 CRM 連線、不保存 controller、Session、profile 或 response，因此不會製造跨使用者狀態或
    /// 資源所有權。
    /// </summary>
    [Theory]
    [InlineData("LoadDistrictTree")]
    [InlineData("SearchDistrictTree")]
    [InlineData("LoadGroupMembers")]
    [InlineData("LoadUngroupedMembers")]
    public void Controller_ExposesRequiredTreeActions(string action)
    {
        if (string.Equals(action, "LoadDistrictTree", StringComparison.Ordinal))
        {
            Source.Should().Contain("public IActionResult LoadDistrictTree(");
            return;
        }

        Source.Should().Contain("public async Task<IActionResult> " + action + "(");
    }

    [Fact]
    public void Controller_UsesAuthoritativeListScopeAndSecondContactGuard()
    {
        Source.Should().Contain("MemberInfoScopeGuard.IsListAllowed");
        Source.Should().Contain("GetVisibleSmallGroupDescriptors");
        Source.Should().Contain("CanViewContactsBatch");
        Source.Should().Contain("FetchGroupMemberships");
    }

    [Fact]
    public void Controller_ImplementsStrictCurrentSearchAndServerPagedUngroupedQuery()
    {
        Source.Should().Contain("GetRequiredClosedCustomerTypeValue");
        Source.Should().Contain("MemberInfoTreeSearchBuilder.Build");
        Source.Should().Contain("BuildUngroupedContactQuery");
        Source.Should().Contain("ReturnTotalRecordCount = true");
        Source.Should().Contain("totalCount");
    }

    [Fact]
    public void Controller_SearchReturnsAuthorizedCompleteMemberRows()
    {
        // 搜尋端點不能只回傳用來展開樹節點的 contact ID；它必須先以完整欄位查出候選人，再套用第二層聯絡人授權。
        // 通過授權的 Entity 才能批次補關係目標並轉成與一般成員網格相同的完整資料列，最後交由 builder 去重與排序。
        // 這項原始碼契約防止兩種高風險回歸：搜尋結果只剩姓名／ID，以及未授權候選人在 DTO 建構前未被排除而外洩。
        Source.Should().Contain("BuildStrictCurrentContactQuery(");
        Source.Should().Contain("GetTreeContactColumns(),");
        Source.Should().Contain("matchingContacts = matchingContacts.Where(contact => allowedIds.Contains(contact.Id)).ToList();");
        Source.Should().Contain("BatchRelationGoals(service, matchingContacts.Select");
        Source.Should().Contain("BuildMemberRows(service, matchingContacts, relations, typedCommitmentOptions)");
        Source.Should().Contain("MemberInfoTreeSearchBuilder.Build(");
    }

    [Fact]
    public void Controller_ExposesScopeAndInvalidatesTreeCaches()
    {
        Source.Should().Contain("ViewBag.MemberInfoScope");
        Source.Should().Contain("member-info-tree:church");
        Source.Should().Contain("member-info-tree:grouped-current-ids:church");
    }

    [Fact]
    public void Controller_MapsRelationGoalsIntoOneDtoField()
    {
        Source.Should().Contain("RelationGoals = relationGoals");
        Source.Should().NotContain("Relation = relation.Relations");
        Source.Should().NotContain("Goal = relation.Goals");
    }

    [Fact]
    public void Controller_MapsConfiguredCommitmentOrderWithoutExposingRawSortValue()
    {
        Source.Should().Contain("MemberInfoCommitmentTypeMetadataProvider");
        Source.Should().Contain("MemberInfoCommitmentTypeSort.OrderRows(");

        // 只檢查會友表格 DTO 的 mapper；詳細彈窗仍合法保留 MembershipStatusValue 供下拉選單預選。
        // 以行首 regex 辨識真正的屬性指派，避免誤把 HasMembershipStatusValue 視為 raw 欄位。
        var memberRows = Slice(
            "private List<GroupMemberRowViewModel> BuildMemberRows(",
            "private static string ResolveOptionSetText(");
        memberRows.Should().Contain("var membershipStatusValue =");
        memberRows.Should().Contain(
            "contact.GetAttributeValue<OptionSetValue>(\"customertypecode\")?.Value");
        memberRows.Should().Contain("MembershipStatusOrder = commitmentOption?.Order");
        memberRows.Should().Contain("HasMembershipStatusValue = membershipStatusValue.HasValue");
        memberRows.Should().NotMatchRegex(@"(?m)^\s*MembershipStatusValue\s*=");
    }

    [Fact]
    public void Controller_UsesConfiguredSegmentsBeforeUngroupedPaging()
    {
        Source.Should().Contain("QueryExpressionToFetchXmlRequest");
        Source.Should().Contain("MemberInfoCommitmentTypeCountQuery.CreateValueCountsFetch");
        Source.Should().Contain("MemberInfoCommitmentTypeCountQuery.ReadValueCounts");
        Source.Should().Contain("TryGetCommitmentTypeSort(");
        Source.Should().Contain("LoadUngroupedCommitmentTypePageAsync(");
        Source.Should().Contain("MemberInfoCommitmentTypeSort.BuildSegments(");
        Source.Should().Contain("MemberInfoCommitmentTypeSort.PlanSlices(");
        Source.Should().NotContain("EnableRawChoiceOrdering");
        Source.Should().NotContain("useraworderby");

        var segmentQuery = Slice(
            "private QueryExpression BuildUngroupedCommitmentSegmentQuery(",
            "private int CountUngroupedEmptyCommitmentSegment(");
        segmentQuery.Should().Contain("MemberInfoCommitmentTypeSegmentKind.Configured");
        segmentQuery.Should().Contain("ConditionOperator.Equal");
        segmentQuery.Should().Contain("ConditionOperator.NotNull");
        segmentQuery.Should().Contain("ConditionOperator.NotIn");
        segmentQuery.Should().Contain("ConditionOperator.Null");
        segmentQuery.Should().NotMatchRegex("query\\.AddOrder\\(\\s*\"customertypecode\"");
        segmentQuery.Should().Contain("query.AddOrder(\"fullname\", OrderType.Ascending)");
        segmentQuery.Should().Contain("query.AddOrder(\"contactid\", OrderType.Ascending)");
        Source.Should().NotContain("rawChoiceOrder");

        var mapper = Slice(
            "private static string MapUngroupedSortAttribute(",
            "private Dictionary<Guid, string> BatchRelationGoals(");
        mapper.Should().NotContain("MembershipStatus\", StringComparison.OrdinalIgnoreCase)) return \"customertypecode\"");
        mapper.Should().NotContain("return \"MembershipStatusValue\"");
        mapper.Should().NotContain("return \"MembershipStatusOrder\"");
    }

    [Fact]
    public void Controller_ChunksBatchAvatarCrmQueries()
    {
        var action = Slice(
            "public IActionResult GetContactImagesBatch(",
            "public IActionResult ResyncLineCandidateIds(");

        action.Should().Contain(
            "foreach (var chunk in uncachedGuids.Chunk(CrmInClauseChunkSize))");
        action.Should().Contain(
            "chunk.Select(guid => (object)guid).ToArray()");
        action.Should().NotContain(
            "ConditionOperator.In, uncachedGuids.Select(g => (object)g).ToArray()");
    }

    [Fact]
    public void Controller_LoadsAndMapsSmallGroupTimeAndPlace()
    {
        const string methodStartMarker = "private List<SmallGroupDescriptor> FetchSmallGroupDescriptors";
        const string methodEndMarker = "private List<GroupMembershipRow> FetchGroupMemberships";
        var methodStart = Source.IndexOf(methodStartMarker, StringComparison.Ordinal);
        var methodEnd = Source.IndexOf(methodEndMarker, StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = Source[methodStart..methodEnd];

        // 小組時間與地點必須沿用既有 list descriptor query 一次載入，禁止逐筆查詢造成 N+1；
        // 這裡分別鎖住同一方法內的 ColumnSet 與 DTO projection，避免由檔案其他同名字串誤滿足。
        var columnSetStart = method.IndexOf("ColumnSet = new ColumnSet(", StringComparison.Ordinal);
        var projectionStart = method.IndexOf("return new SmallGroupDescriptor", StringComparison.Ordinal);
        columnSetStart.Should().BeGreaterThanOrEqualTo(0);
        projectionStart.Should().BeGreaterThan(columnSetStart);
        var columnSet = method[columnSetStart..projectionStart];

        columnSet.Should().Contain("\"new_group_time\"");
        columnSet.Should().Contain("\"new_group_place\"");
        method.Should().Contain("GroupTime = entity.GetAttributeValue<string>(\"new_group_time\") ?? string.Empty");
        method.Should().Contain("GroupPlace = entity.GetAttributeValue<string>(\"new_group_place\") ?? string.Empty");
        method.Should().NotContain("service.Retrieve(");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate SpeechMessageProducts.sln from test output directory.");
    }

    private static string Slice(string startMarker, string endMarker)
    {
        var start = Source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = Source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return Source[start..end];
    }
}
