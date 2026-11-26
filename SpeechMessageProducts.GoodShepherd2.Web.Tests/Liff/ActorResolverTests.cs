/*
檔案整體說明
目的與定位：F6產品actor解析的離線契約測試，保護同一LINE身分在同Channel多租戶時的選擇、穩定ID與資源生命週期；不使用任何真實會友或秘密。
主要功能與流程：以合成SDK列、短期lease探針、記憶體selection及角色來源測試真實產品resolver。故障注入重複Contact ID、錯誤產品、跨Channel、失效選擇、第二頁故障與釋放失敗，成功只可在完整讀取及歸還租約後發布。
使用邊界與維護：每案獨立manager、資料列、計數與集合，不連D365或SQL、不共用token。不同Contact ID即使同名亦保留，多候選無有效選擇回null，切換必重新查角色；替身只屬當案，無背景工作。真正SDK生命週期另以受控阻塞及取消確認排空。
*/
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SpeechMessage.Liff.AspNetCore;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd2.Web.Liff;
using ToolUtilityNameSpace.Dataverse;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Liff;

/// <summary>使用真實resolver與可觀察SDK邊界；所有ID及名稱皆為單案合成資料。</summary>
public sealed class ActorResolverTests
{
    /// <summary>同Channel兩筆同名Contact有不同權威ID，必須使用完整scope儲存的目前選擇，不能按姓名合併。</summary>
    [Fact]
    public async Task Resolve_UserInTwoChurchesSameChannel_UsesCurrentChurch()
    {
        var f = new Fixture();
        var first = Fixture.Row();
        var second = Fixture.Row();
        f.Rows.AddRange([first, second]);
        f.Selection.Selected = second.Id;
        var actor = await f.Resolver.ResolveAsync(f.Identity, CancellationToken.None);
        Assert.NotNull(actor);
        Assert.Equal(second.Id, actor.ContactId);
        Assert.Equal(Fixture.Tenant(second), actor.TenantId);
        Assert.Equal(("GoodShepherd2", "2012804957", "synthetic-person"), Assert.Single(f.Selection.Reads));
        Assert.Equal(1, f.Released);
    }

    /// <summary>兩個合法候選沒有目前選擇時，不任取第一筆，要求前端重新選擇。</summary>
    [Fact]
    public async Task Resolve_TwoChurchesNoCurrent_ReturnsNull()
    {
        var f = new Fixture();
        f.Rows.AddRange([Fixture.Row(), Fixture.Row()]);
        Assert.Null(await f.Resolver.ResolveAsync(f.Identity, CancellationToken.None));
        Assert.Equal(1, f.Released);
    }

    /// <summary>選擇指向離開Channel的Contact時，只能回null；不得以舊選擇建立actor或回退另一候選。</summary>
    [Fact]
    public async Task Resolve_StaleSelection_ReturnsNull()
    {
        var f = new Fixture();
        f.Rows.AddRange([Fixture.Row(), Fixture.Row()]);
        f.Selection.Selected = Guid.NewGuid();
        Assert.Null(await f.Resolver.ResolveAsync(f.Identity, CancellationToken.None));
    }

    /// <summary>別人的Contact不在當次已驗身分候選內，切換前拒絕且selection零寫入。</summary>
    [Fact]
    public async Task Selection_SetForOtherPersonsContact_Rejected()
    {
        var f = new Fixture();
        f.Rows.Add(Fixture.Row());
        Assert.False(await f.Resolver.SetSelectionAsync(f.Identity, Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(f.Selection.Writes);
    }

    /// <summary>同一身分切換後重新讀SDK與角色，前一個actor不能被覆寫，也不能保留前租戶角色。</summary>
    [Fact]
    public async Task Resolve_TenantSwitch_RebuildsScopeAndCache()
    {
        var f = new Fixture();
        var a = Fixture.Row();
        var b = Fixture.Row();
        f.Rows.AddRange([a, b]);
        f.Selection.Selected = a.Id;
        var first = await f.Resolver.ResolveAsync(f.Identity, CancellationToken.None);
        f.Selection.Selected = b.Id;
        var second = await f.Resolver.ResolveAsync(f.Identity, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(a.Id.ToString("N"), Assert.Single(first.RoleCodes));
        Assert.Equal(b.Id.ToString("N"), Assert.Single(second.RoleCodes));
        Assert.Equal(2, f.Queries);
        Assert.Equal(2, f.Released);
        Assert.NotEqual(first.TenantId, second.TenantId);
    }

    /// <summary>唯一候選不需要SQL選擇；只有重新驗本人成功後才可設定，寫入完整固定scope及權威Contact ID。</summary>
    [Fact]
    public async Task Resolve_OneCandidateAndSelection_RevalidatesBeforeWrite()
    {
        var f = new Fixture();
        var row = Fixture.Row();
        f.Rows.Add(row);
        var actor = await f.Resolver.ResolveAsync(f.Identity, CancellationToken.None);
        Assert.Equal(row.Id, actor!.ContactId);
        Assert.Empty(f.Selection.Reads);
        Assert.True(await f.Resolver.SetSelectionAsync(f.Identity, row.Id, CancellationToken.None));
        Assert.Equal(("GoodShepherd2", "2012804957", "synthetic-person", row.Id), Assert.Single(f.Selection.Writes));
        Assert.Equal(2, f.Released);
    }

    /// <summary>同集合重複資料庫ID是組裝衝突，不能Distinct或任取一筆；拒絕後下一次完整來源可成功重試。</summary>
    [Fact]
    public async Task Resolve_DuplicateIdentity_FailsBeforePublicationAndCanRetry()
    {
        var f = new Fixture();
        var row = Fixture.Row();
        f.Rows.AddRange([row, row]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Resolver.ResolveAsync(f.Identity, CancellationToken.None));
        Assert.Empty(f.Roles.Calls);
        Assert.Equal(1, f.Faulted);
        f.Rows.RemoveAt(1);
        Assert.Equal(row.Id, (await f.Resolver.ResolveAsync(f.Identity, CancellationToken.None))!.ContactId);
    }

    /// <summary>即使SDK忽略查詢filter回傳越界列，產品、Channel、本人及租戶關聯仍逐筆驗證，不能發布actor。</summary>
    /// <param name="field">要注入錯誤的別名欄位。</param>
    [Theory]
    [InlineData("channel.new_product")]
    [InlineData("channel.new_channelid")]
    [InlineData("new_lineid")]
    public async Task Resolve_UntrustedRowMismatch_FailsClosed(string field)
    {
        var f = new Fixture();
        var row = Fixture.Row();
        row[field] = field == "new_lineid" ? "another-person" : new AliasedValue("synthetic", field, "other");
        f.Rows.Add(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Resolver.ResolveAsync(f.Identity, CancellationToken.None));
        Assert.Empty(f.Roles.Calls);
    }

    /// <summary>歸還租約失敗不能將已建候選發布成成功，真正清理故障必須向HTTP診斷邊界傳遞。</summary>
    [Fact]
    public async Task Resolve_ReleaseFails_DoesNotPublishActor()
    {
        var f = new Fixture();
        f.Rows.Add(Fixture.Row());
        f.ReleaseFailure = true;
        await Assert.ThrowsAsync<IOException>(() => f.Resolver.ResolveAsync(f.Identity, CancellationToken.None));
    }

    /// <summary>第一頁合法而第二頁故障時，第一頁不得先發布；租約淘汰及歸還後，下次完整來源仍可重試。</summary>
    [Fact]
    public async Task Resolve_SecondPageFails_DrainsBeforeRetry()
    {
        var f = new Fixture();
        var row = Fixture.Row();
        f.Page = query => query.PageInfo.PageNumber == 1
            ? new EntityCollection([row]) { MoreRecords = true, PagingCookie = "synthetic-next" }
            : throw new IOException("synthetic-page-two");
        await Assert.ThrowsAsync<IOException>(() => f.Resolver.ResolveAsync(f.Identity, CancellationToken.None));
        Assert.Empty(f.Roles.Calls);
        Assert.Empty(f.Selection.Reads);
        Assert.Equal(1, f.Faulted);
        Assert.Equal(1, f.Released);
        f.Page = _ => new EntityCollection([row]);
        Assert.Equal(row.Id, (await f.Resolver.ResolveAsync(f.Identity, CancellationToken.None))!.ContactId);
        Assert.Equal(2, f.Released);
    }

    /// <summary>同步SDK進入後caller取消仍須等真正工作排空；等待中不得釋放租約或發布角色，排空後才傳遞取消。</summary>
    [Fact]
    public async Task Resolve_CancelInsideSdk_DrainsBeforeReturning()
    {
        var f = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var caller = new CancellationTokenSource();
        f.Page = _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Synthetic SDK drain was not released.");
            return new EntityCollection([Fixture.Row()]);
        };
        var operation = Task.Run(() => f.Resolver.ResolveAsync(f.Identity, caller.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await caller.CancelAsync();
            Assert.False(operation.IsCompleted);
            Assert.Equal(0, f.Released);
            Assert.Empty(f.Roles.Calls);
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, f.Released);
        Assert.Equal(1, f.Faulted);
        Assert.Empty(f.Roles.Calls);
    }

    /// <summary>前端與伺服器共同接受至多256字元名稱，超界來源拒絕發布且不讀角色，不能回傳無法消費的部分身分。</summary>
    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public async Task Resolve_TenantNameBound_MatchesPageContract(int length, bool valid)
    {
        var f = new Fixture();
        var row = Fixture.Row();
        row["tenant.name"] = new AliasedValue("account", "name", new string('名', length));
        f.Rows.Add(row);
        if (valid)
        {
            var actor = await f.Resolver.ResolveAsync(f.Identity, CancellationToken.None);
            Assert.Equal(length, f.Resolver.GetTenantName(actor!).Length);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Resolver.ResolveAsync(f.Identity, CancellationToken.None));
            Assert.Empty(f.Roles.Calls);
        }
        Assert.Equal(1, f.Released);
    }

    /// <summary>角色集合不保留呼叫端可變參照，caller修改後第二份actor及既有actor仍隔離。</summary>
    [Fact]
    public void Actor_RoleInputMutation_DoesNotMutatePublishedSnapshot()
    {
        var roles = new List<string> { "viewer" };
        var actor = new LiffActor(Guid.NewGuid(), Guid.NewGuid(), roles);
        roles[0] = "admin";
        Assert.Equal("viewer", Assert.Single(actor.RoleCodes));
    }

    /// <summary>單案SDK及scope owner；所有純資料依Fixture生命週期消失，不註冊背景或全域資源。</summary>
    private sealed class Fixture
    {
        /// <summary>合成完整候選，只有RetrieveMultiple呼叫時複製回傳。</summary>
        internal List<Entity> Rows { get; } = [];
        /// <summary>當案selection探針。</summary>
        internal Selection Selection { get; } = new();
        /// <summary>當案角色探針。</summary>
        internal Roles Roles { get; } = new();
        /// <summary>測試唯一真實resolver。</summary>
        internal GoodShepherd2LiffActorResolver Resolver { get; }
        /// <summary>合成已驗身分，效期來自相同受控clock。</summary>
        internal LiffIdentity Identity { get; } = new("synthetic-person", "同名", new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        /// <summary>唯一租約歸還次數。</summary>
        internal int Released { get; private set; }
        /// <summary>故障淘汰計數。</summary>
        internal int Faulted { get; private set; }
        /// <summary>真實查詢次數。</summary>
        internal int Queries { get; private set; }
        /// <summary>控制歸還故障，無外部資源。</summary>
        internal bool ReleaseFailure { get; set; }
        /// <summary>可觀察的同步分頁／故障入口，只在當案SDK呼叫堆疊內執行，不建立背景所有權。</summary>
        internal Func<QueryExpression, EntityCollection>? Page { get; set; }

        /// <summary>組合固定產品與Channel，SDK可觀察但不建立任何實際連線。</summary>
        internal Fixture()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                Queries++;
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.Equal("contact", expression.EntityName);
                Assert.False(expression.ColumnSet.AllColumns);
                return Page is null ? new EntityCollection(Rows.ToList()) : Page(expression);
            });
            var lease = new Mock<IClientLease>(MockBehavior.Strict);
            lease.SetupGet(l => l.Service).Returns(sdk.Object);
            lease.Setup(l => l.MarkFaulted()).Callback(() => Faulted++);
            lease.Setup(l => l.Dispose()).Callback(() =>
            {
                Released++;
                if (ReleaseFailure) throw new IOException("synthetic-release");
            });
            var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
            manager.Setup(m => m.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
            var clock = new Mock<IChurchClock>(MockBehavior.Strict);
            clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero));
            Resolver = new(manager.Object, Selection, Roles,
                new LiffOptions("2012805181", "2012804957", ""), clock.Object);
        }

        /// <summary>建立同名但權威ID不同的完整joined列，對應01的contact→account→slot→channel。</summary>
        internal static Entity Row()
        {
            var tenant = Guid.NewGuid();
            var slot = Guid.NewGuid();
            var channel = Guid.NewGuid();
            var row = new Entity("contact", Guid.NewGuid())
            {
                ["new_lineid"] = "synthetic-person",
                ["parentcustomerid"] = new EntityReference("account", tenant)
            };
            void Add(string alias, string entity, string field, object value) =>
                row[alias + "." + field] = new AliasedValue(entity, field, value);
            Add("tenant", "account", "accountid", tenant);
            Add("tenant", "account", "name", "同名租戶");
            Add("tenant", "account", "new_istenant", true);
            Add("tenant", "account", "new_channelslot", new EntityReference("new_channelslot", slot));
            Add("slot", "new_channelslot", "new_channelslotid", slot);
            Add("slot", "new_channelslot", "new_tenant", new EntityReference("account", tenant));
            Add("slot", "new_channelslot", "new_channel", new EntityReference("new_linechannel", channel));
            Add("channel", "new_linechannel", "new_linechannelid", channel);
            Add("channel", "new_linechannel", "new_channelid", "2012804957");
            Add("channel", "new_linechannel", "new_product", "GoodShepherd2");
            return row;
        }

        /// <summary>只讀當案權威租戶ID，不依名稱或列位置推測身分。</summary>
        internal static Guid Tenant(Entity row) => (Guid)((AliasedValue)row["tenant.accountid"]).Value;
    }

    /// <summary>當案選擇儲存探針，完整記錄scope以確認產品及Channel不來自前端。</summary>
    private sealed class Selection : ICurrentTenantSelectionStore
    {
        /// <summary>合成目前選擇，可在請求間改變以測試重建。</summary>
        internal Guid? Selected { get; set; }
        /// <summary>當案讀取索引，無跨測試共用。</summary>
        internal List<(string, string, string)> Reads { get; } = [];
        /// <summary>當案寫入值，錯誤切換應維持空集合。</summary>
        internal List<(string, string, string, Guid)> Writes { get; } = [];
        /// <summary>回傳合成選擇，不模擬SQL原子性或實機成功。</summary>
        public Task<Guid?> GetAsync(string product, string channelKey, string lineUserId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Reads.Add((product, channelKey, lineUserId));
            return Task.FromResult(Selected);
        }
        /// <summary>只記錄已授權寫入，無外部資源。</summary>
        public Task SetAsync(string product, string channelKey, string lineUserId, Guid contactId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Writes.Add((product, channelKey, lineUserId, contactId));
            Selected = contactId;
            return Task.CompletedTask;
        }
    }

    /// <summary>每次依Contact回不同角色的合成來源，用來抓前一租戶快取被重用。</summary>
    private sealed class Roles : ILiffRoleResolver
    {
        /// <summary>當案角色讀取紀錄。</summary>
        internal List<(Guid, Guid)> Calls { get; } = [];
        /// <summary>角色回應與Contact權威ID綁定，沒有真正角色資料或SDK。</summary>
        public Task<IReadOnlyCollection<string>> ResolveRolesAsync(Guid contactId, Guid tenantId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((contactId, tenantId));
            return Task.FromResult<IReadOnlyCollection<string>>(new[] { contactId.ToString("N") });
        }
    }
}
