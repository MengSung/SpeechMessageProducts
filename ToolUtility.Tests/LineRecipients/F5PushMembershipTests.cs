/*
檔案整體說明
目的與定位：F5 Dataverse 收件人 adapter 的雙目標離線契約，不讀真資料或交接秘密。
主要功能與流程：以合成 SDK 實際觀察 WhoAmI、精確欄位、account→slot→Channel 及分頁查詢，
注入外產品、錯租戶、重複正式 ID 與第二頁故障，斷言完整名單核對及 fail-closed 行為。
使用邊界與維護：SDK 與所有 Entity 由案例獨佔，adapter Dispose 只解除借用參照；沒有連線、
背景工作或可變 static。此處的唯一性與組織身分只是合成 SDK 證據，不冒充 FS/F3/F5 實機。
*/
using System.Globalization;
using LineMessagingProcessor.Core.Gateway;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.LineRecipients;
using Xunit;

namespace ToolUtility.Tests.LineRecipients;

/// <summary>測真 adapter 的查詢與驗證，而不是只驗回傳預設 true 的替身。</summary>
public sealed class F5PushMembershipTests
{
    /// <summary>來源組織不符不能查 account 或 contact，路由文字不代表真正 SDK 身分。</summary>
    [Fact]
    public void Membership_WrongOrganization_StopsBeforeTenantRead()
    {
        using var sdk = new MembershipSdk();
        Assert.Throws<InvalidOperationException>(() => new DataverseRecipientMembership(sdk, Guid.NewGuid(), "GoodShepherd2"));
        Assert.Equal(0, sdk.Retrieves);
    }

    /// <summary>只以已配發名額讀回權威品牌；caller 顯示提示不參與資料真值。</summary>
    [Fact]
    public async Task Membership_ValidSlotChain_ReturnsAuthority()
    {
        using var sdk = new MembershipSdk();
        using var membership = new DataverseRecipientMembership(sdk, sdk.OrganizationId, "GoodShepherd2");
        var facts = await membership.ReadTenantAsync(sdk.Scope, default);
        Assert.NotNull(facts);
        Assert.Equal("權威租戶", facts!.DisplayName);
        Assert.True(facts.IsTestTenant);
        Assert.Equal(3, sdk.Retrieves);
        Assert.All(sdk.Columns, columns => Assert.False(columns.AllColumns));
    }

    /// <summary>產品或 slot 的租戶 lookup 不符均回拒絕，絕不改查舊 Channel 欄位。</summary>
    [Theory]
    [InlineData("foreign-product")]
    [InlineData("foreign-slot")]
    [InlineData("legacy-only")]
    [InlineData("disabled")]
    public async Task Membership_ForeignOrLegacyPath_IsRejected(string fault)
    {
        using var sdk = new MembershipSdk();
        if (fault == "foreign-product") sdk.Channel["new_product"] = "GoodButler";
        if (fault == "foreign-slot") sdk.Slot["new_tenant"] = new EntityReference("account", Guid.NewGuid());
        if (fault == "legacy-only")
        {
            sdk.Account.Attributes.Remove("new_channelslot");
            sdk.Account["new_linechannelid"] = new EntityReference("new_linechannel", sdk.Channel.Id);
        }
        if (fault == "disabled") sdk.Channel["new_channelstatus"] = new OptionSetValue(100000003);
        using var membership = new DataverseRecipientMembership(sdk, sdk.OrganizationId, "GoodShepherd2");
        Assert.Null(await membership.ReadTenantAsync(sdk.Scope, default));
        Assert.Empty(sdk.Queries);
    }

    /// <summary>測試標記缺失或「否」都不是測試租戶；不把複製資料視為授權。</summary>
    [Fact]
    public async Task Membership_MissingTestFlag_IsFalse()
    {
        using var sdk = new MembershipSdk();
        sdk.Account.Attributes.Remove("new_istesttenant");
        using var membership = new DataverseRecipientMembership(sdk, sdk.OrganizationId, "GoodShepherd2");
        Assert.False(await membership.IsTestTenantAsync(sdk.Account.Id, default));
    }

    /// <summary>600 個不同正式 contact 同名仍全部驗證；每個 IN 名單最多 500 且條件含租戶。</summary>
    [Fact]
    public async Task Membership_600Recipients_BoundedQueriesAndSameNameRecordsPreserved()
    {
        using var sdk = new MembershipSdk();
        using var membership = new DataverseRecipientMembership(sdk, sdk.OrganizationId, "GoodShepherd2");
        Assert.True(await membership.AreAllMembersAsync(sdk.Scope, Users(600), default));
        Assert.Equal(2, sdk.Queries.Count);
        foreach (var query in sdk.Queries)
        {
            Assert.Contains(query.Criteria.Conditions, item => item.AttributeName == "parentcustomerid"
                && item.Values.Count == 1 && Equals(item.Values[0], sdk.Account.Id));
            var input = Assert.Single(query.Criteria.Conditions.Where(item => item.AttributeName == "new_lineid"));
            Assert.InRange(input.Values.Count, 1, 500);
            Assert.False(query.ColumnSet.AllColumns);
        }
        Assert.Equal(600, sdk.Returned);
    }

    /// <summary>後端忽略租戶 filter 的故障注入仍須被實際返回列驗證擋下。</summary>
    [Fact]
    public async Task Membership_ForeignReturnedRow_IsRejected()
    {
        using var sdk = new MembershipSdk { ForeignContact = true };
        using var membership = new DataverseRecipientMembership(sdk, sdk.OrganizationId, "GoodShepherd2");
        Assert.False(await membership.AreAllMembersAsync(sdk.Scope, Users(1), default));
    }

    /// <summary>501人跨兩個查詢批次，首批500人全合法但尾批查無此人時，完整名單仍須拒絕。</summary>
    [Fact]
    public async Task Membership_LastRecipientNotFound_AcrossBatchBoundary_IsRejected()
    {
        var users = Users(501);
        using var sdk = new MembershipSdk { OmittedUserId = users[500] };
        using var membership = new DataverseRecipientMembership(sdk, sdk.OrganizationId, "GoodShepherd2");
        Assert.False(await membership.AreAllMembersAsync(sdk.Scope, users, default));
        Assert.Equal(2, sdk.Queries.Count);
        Assert.Equal(500, sdk.Returned);
    }

    /// <summary>即使後端忽略Active條件而返回同租戶正式Contact，停用狀態仍須整案拒絕。</summary>
    [Fact]
    public async Task Membership_InactiveContactReturned_IsRejected()
    {
        using var sdk = new MembershipSdk { InactiveContact = true };
        using var membership = new DataverseRecipientMembership(sdk, sdk.OrganizationId, "GoodShepherd2");
        Assert.False(await membership.AreAllMembersAsync(sdk.Scope, Users(1), default));
        Assert.Equal(1, sdk.Returned);
    }

    /// <summary>正式 contact ID 重複不准靠 HashSet 靜默掉其中一列。</summary>
    [Fact]
    public async Task Membership_DuplicateStableRowIdentity_FailsClosed()
    {
        using var sdk = new MembershipSdk { DuplicateIdentity = true };
        using var membership = new DataverseRecipientMembership(sdk, sdk.OrganizationId, "GoodShepherd2");
        await Assert.ThrowsAsync<InvalidOperationException>(() => membership.AreAllMembersAsync(sdk.Scope, Users(2), default));
    }

    /// <summary>分頁中途故障原樣傳出，不回部分成功或把後續失敗當空頁。</summary>
    [Fact]
    public async Task Membership_SecondPageFailure_DoesNotPublishPartialSuccess()
    {
        using var sdk = new MembershipSdk { FailSecondPage = true };
        using var membership = new DataverseRecipientMembership(sdk, sdk.OrganizationId, "GoodShepherd2");
        Assert.Same(sdk.PageFailure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            membership.AreAllMembersAsync(sdk.Scope, Users(2), default)));
        Assert.Equal(2, sdk.Queries.Count);
    }

    /// <summary>Dispose 拒絕新入口但不得釋放外借 SDK，沒有背景查詢會在返回後繼續。</summary>
    [Fact]
    public async Task Membership_Dispose_ClearsBorrowedReferenceOnly()
    {
        using var sdk = new MembershipSdk();
        var membership = new DataverseRecipientMembership(sdk, sdk.OrganizationId, "GoodShepherd2");
        membership.Dispose();
        Assert.False(sdk.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => membership.IsTestTenantAsync(sdk.Account.Id, default));
    }

    /// <summary>合法合成識別不對應任何真實手機，保持有序且不使用顯示名稱作身分。</summary>
    private static string[] Users(int count) =>
        Enumerable.Range(1, count).Select(index => "U" + index.ToString("x32", CultureInfo.InvariantCulture)).ToArray();

    /// <summary>單案例同步 SDK owner，固定組織及名額鏈，所有方法只操作當次合成記憶體。</summary>
    private sealed class MembershipSdk : IOrganizationService, IDisposable
    {
        internal readonly Guid OrganizationId = Guid.NewGuid();
        internal readonly Entity Account = new("account", Guid.NewGuid());
        internal readonly Entity Slot = new("new_channelslot", Guid.NewGuid());
        internal readonly Entity Channel = new("new_linechannel", Guid.NewGuid());
        internal readonly List<ColumnSet> Columns = new();
        internal readonly List<QueryExpression> Queries = new();
        internal readonly InvalidOperationException PageFailure = new("synthetic second page failure");
        internal bool ForeignContact, DuplicateIdentity, FailSecondPage, InactiveContact, Disposed;
        internal string? OmittedUserId;
        internal int Retrieves, Returned;
        internal PushScope Scope => new("GoodShepherd2", "10001", Account.Id, "untrusted hint", null);

        /// <summary>正式主鍵與 lookup 均不同，合成有效控制可分辨全面拒絕的假實作。</summary>
        internal MembershipSdk()
        {
            Account["name"] = "權威租戶";
            Account["new_logourl"] = "https://example.test/icon";
            Account["new_istenant"] = new OptionSetValue(100000001);
            Account["new_istesttenant"] = new OptionSetValue(100000001);
            Account["statecode"] = new OptionSetValue(0);
            Account["new_channelslot"] = new EntityReference("new_channelslot", Slot.Id);
            Slot["statecode"] = new OptionSetValue(0);
            Slot["new_tenant"] = new EntityReference("account", Account.Id);
            Slot["new_slotstatus"] = new OptionSetValue(100000001);
            Slot["new_channel"] = new EntityReference("new_linechannel", Channel.Id);
            Channel["statecode"] = new OptionSetValue(0);
            Channel["new_product"] = "GoodShepherd2";
            Channel["new_channelid"] = "10001";
            Channel["new_channelstatus"] = new OptionSetValue(100000001);
        }
        /// <summary>返回同一正式身分並記錄實際 ColumnSet，不默認查無或替換 ID。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Retrieves++;
            Columns.Add(columnSet);
            var row = new[] { Account, Slot, Channel }.Single(item => item.LogicalName == entityName && item.Id == id);
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var pair in row.Attributes.Where(pair => columnSet.Columns.Contains(pair.Key)))
                copy.Attributes.Add(pair);
            return copy;
        }
        /// <summary>按真正IN輸入生成不同正式ID的同名contact；可注入缺列、停用、外租戶與重複ID。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            Queries.Add(expression);
            if (FailSecondPage && expression.PageInfo.PageNumber > 1) throw PageFailure;
            var values = expression.Criteria.Conditions.Single(item => item.AttributeName == "new_lineid").Values.Cast<string>().ToArray();
            var repeated = Guid.NewGuid();
            var rows = values.Where(value => value != OmittedUserId).Select(value => new Entity("contact", DuplicateIdentity ? repeated : Guid.NewGuid())
            {
                ["new_lineid"] = value, ["parentcustomerid"] = new EntityReference("account", ForeignContact ? Guid.NewGuid() : Account.Id),
                ["statecode"] = new OptionSetValue(InactiveContact ? 1 : 0), ["fullname"] = "相同顯示名稱"
            }).ToList();
            if (FailSecondPage) rows = rows.Take(1).ToList();
            Returned += rows.Count;
            return new EntityCollection(rows) { MoreRecords = FailSecondPage, PagingCookie = FailSecondPage ? "synthetic-page-1" : null };
        }
        /// <summary>只允許 WhoAmI，不接觸任何遠端或管理命令。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Assert.Equal("WhoAmI", request.RequestName);
            return new OrganizationResponse { Results = new ParameterCollection { ["OrganizationId"] = OrganizationId } };
        }
        /// <summary>測試 owner 關閉標記，不取得外部資源。</summary>
        public void Dispose() => Disposed = true;
        /// <summary>會員查詢不得建立記錄。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>會員查詢不得更新記錄。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>會員查詢不得刪除記錄。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>會員查詢不得建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>會員查詢不得移除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
