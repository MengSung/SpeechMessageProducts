/*
檔案整體說明
目的與定位：保護 F12 建立、名額錯配復原及清理的測試租戶隔離契約。
主要功能與流程：使用 operation-local 記憶體 SDK 模擬資料庫主鍵、正式 metadata、同步配發結果及交易回復；真服務負責選項驗證、manifest 發布及遠端父子範圍判斷，fake 不代做這些規則。
資源擁有權與隔離：所有 GUID、LINE 格式字串及路徑皆為合成；每個 fixture 唯一擁有暫存目錄與記憶體列，Dispose 僅刪自身目錄，不連線 D365、SQL 或 LINE。
故障與斷言：注入 Channel 錯配、正式組織、他租戶清單、LINE 故障及交易失敗；斷言不擴大清理、錯配精確釋放、無個資 manifest，且失敗保留可續跑證據。通過只表示離線契約成立。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.TestTenants;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.TestTenants;

/// <summary>以合成 SDK 驗證 F12 交易及範圍，不宣稱任何實機資料已建立。</summary>
public sealed class TestTenantServiceTests
{
    private const string User = "U11111111111111111111111111111111";

    /// <summary>正式及錯誤產品在任何 SDK 呼叫前拒絕，不用名稱相似或未知產品後備。</summary>
    [Theory]
    [InlineData("goodshepherdback", "GoodShepherd2")]
    [InlineData("goodshepherd20", "GoodButler")]
    public void Create_ProductionOrganization_Refused(string organization, string product)
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => new TestTenantService(fixture.Sdk, organization, product, fixture.Root));
        Assert.Empty(fixture.Sdk.Rows);
    }

    /// <summary>真服務建立兩個 choice 旗標、正式試用選項及角色；清單只記錄資料庫身份。</summary>
    [Fact]
    public void Create_OutputContainsNoLineUserId()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Service.Create("c201", "測試甲", "2012804957", [User], [], default);
        var account = fixture.Sdk.Rows[("account", manifest.TenantId)];
        Assert.Equal(100000001, account.GetAttributeValue<OptionSetValue>("new_istenant").Value);
        Assert.Equal(100000001, account.GetAttributeValue<OptionSetValue>("new_istesttenant").Value);
        Assert.Equal(912, account.GetAttributeValue<OptionSetValue>("new_lifecyclestatus").Value);
        Assert.DoesNotContain(User, File.ReadAllText(fixture.Path), StringComparison.Ordinal);
        Assert.Single(fixture.Sdk.Rows.Values.Where(row => row.LogicalName == "contact"));
        Assert.Equal(2, fixture.Sdk.Rows.Values.Count(row => row.LogicalName == "new_roleassignment"));
        Assert.All(manifest.Records, record => Assert.True(fixture.Sdk.Rows.ContainsKey((record.EntityLogicalName, record.Id))));
    }

    /// <summary>配到非預期 Channel 必須走正式釋放申請，刪除本次租戶，且回報失敗。</summary>
    [Fact]
    public void Create_ExpectChannelMismatch_ReleasedAndFails()
    {
        using var fixture = new Fixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Service.Create("c201", "測試甲", "2012804966", [], [], default));
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
        Assert.DoesNotContain(fixture.Sdk.Rows.Values, row => row.LogicalName == "account");
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>正式配發器回待配發且完全沒有 slot／Channel 時，建立失敗仍須清除本次列，不能留下一個永遠無法解析 Channel 的清單。</summary>
    [Fact]
    public void Create_NoCapacity_RemovesUnallocatedTenantWithoutCallingLineOrRelease()
    {
        using var fixture = new Fixture();
        fixture.Sdk.RefuseAllocation = true;
        Assert.Throws<InvalidOperationException>(() => fixture.Service.Create("c201", "測試甲", null, [], [User], default));
        Assert.Empty(fixture.Sdk.Rows);
        Assert.False(File.Exists(fixture.Path));
        Assert.Equal(0, fixture.Sdk.ReleaseCount);
    }

    /// <summary>缺 manifest 時不得呼叫 LINE 或任意 SDK 刪除，不能以租戶代碼直接廣域清理。</summary>
    [Fact]
    public async Task Cleanup_WithoutManifest_Refused()
    {
        using var fixture = new Fixture();
        var calls = 0;
        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Service.CleanupAsync("c201",
            (_, _, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
    }

    /// <summary>完整建立後精確清理，無關同名租戶保留，名額回到可用基線。</summary>
    [Fact]
    public async Task CreateThenCleanup_OnlyOwnTenantAndReturnsToBaseline()
    {
        using var fixture = new Fixture();
        var other = new Entity("account", Guid.NewGuid()) { ["name"] = "測試甲", ["new_tenantcode"] = "c202" };
        fixture.Sdk.Rows.Add((other.LogicalName, other.Id), other);
        fixture.Service.Create("c201", "測試甲", null, [], [User], default);
        var calls = 0;
        await fixture.Service.CleanupAsync("c201", (channel, users, _) =>
        {
            Assert.Equal("2012804957", channel);
            Assert.Equal(User, Assert.Single(users));
            calls++;
            return Task.CompletedTask;
        }, default);
        Assert.Equal(1, calls);
        Assert.Same(other, fixture.Sdk.Rows[("account", other.Id)]);
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
        Assert.False(File.Exists(fixture.Path));
        Assert.Empty(fixture.Sdk.Rows.Values.Where(row => row.LogicalName is "contact" or "new_role" or "new_roleassignment" or "new_slotrequest"));
    }

    /// <summary>LINE 真故障不繼續刪除 CRM 或釋放 Channel，清單保留供精確重跑。</summary>
    [Fact]
    public async Task Cleanup_LineFailure_PreservesManifestAndTenant()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Service.Create("c201", "測試甲", null, [], [], default);
        var failure = new IOException("synthetic line fault");
        var actual = await Assert.ThrowsAsync<IOException>(() => fixture.Service.CleanupAsync("c201", (_, _, _) => Task.FromException(failure), default));
        Assert.Same(failure, actual);
        Assert.True(fixture.Sdk.Rows.ContainsKey(("account", manifest.TenantId)));
        Assert.True(File.Exists(fixture.Path));
        Assert.Equal(0, fixture.Sdk.ReleaseCount);
    }

    /// <summary>清單列被改指其他租戶時整批拒絕，LINE 也不得先產生副作用。</summary>
    [Fact]
    public async Task Cleanup_ForeignParent_RejectsBeforeAnyMutation()
    {
        using var fixture = new Fixture();
        fixture.Service.Create("c201", "測試甲", null, [], [User], default);
        fixture.Sdk.Rows.Values.Single(row => row.LogicalName == "contact")["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
        var calls = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.CleanupAsync("c201", (_, _, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
    }

    /// <summary>角色指派即使指向本租戶 Contact，只要 role 屬他租戶就必須整批拒絕，不能漏查後依 cascade 刪除。</summary>
    [Fact]
    public async Task Cleanup_ContactAssignmentHasForeignRole_RefusedBeforeLine()
    {
        using var fixture = new Fixture();
        fixture.Service.Create("c201", "測試甲", null, [], [User], default);
        var contact = fixture.Sdk.Rows.Values.Single(row => row.LogicalName == "contact");
        var foreign = new Entity("new_roleassignment", Guid.NewGuid())
        { ["new_contactid"] = contact.ToEntityReference(), ["new_roleid"] = new EntityReference("new_role", Guid.NewGuid()) };
        fixture.Sdk.Rows.Add((foreign.LogicalName, foreign.Id), foreign);
        var calls = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.CleanupAsync("c201", (_, _, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
    }

    /// <summary>同步交易在中途失敗時由伺服器原子回滾，復原必讀回所有預先記錄 ID 均不存在才移除清單。</summary>
    [Fact]
    public void Create_TransactionFailure_RollsBackAndRemovesOnlyEmptyManifest()
    {
        using var fixture = new Fixture();
        fixture.Sdk.FailCreateAt = 2;
        Assert.Throws<IOException>(() => fixture.Service.Create("c201", "測試甲", null, [], [User], default));
        Assert.Empty(fixture.Sdk.Rows);
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>已提交但傳輸回應遺失時仍依寫前清單讀回、正式釋放並清理，不把未知結果當成未寫入。</summary>
    [Fact]
    public void Create_ResponseLostAfterCommit_RecoversUsingRecordedDatabaseKeys()
    {
        using var fixture = new Fixture();
        fixture.Sdk.LoseNextCommitResponse = true;
        Assert.Throws<IOException>(() => fixture.Service.Create("c201", "測試甲", null, [], [User], default));
        Assert.DoesNotContain(fixture.Sdk.Rows.Values, row => row.LogicalName is "account" or "contact" or "new_role" or "new_roleassignment" or "new_slotrequest");
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>無指定期望值時保留 F3 的實際配發結果，由命令白名單核對，不能替呼叫端改成第一個 Channel。</summary>
    [Fact]
    public void Create_ChannelValidationUsesAllocatedIdentityAndRecoversRejectedChannel()
    {
        using var fixture = new Fixture();
        string? observed = null;
        fixture.Service.Create("c201", "測試甲", null, [], [], default, channel => observed = channel);
        Assert.Equal("2012804957", observed);
        Assert.Equal(observed, fixture.Service.ReadChannelKey("c201", default));
    }

    /// <summary>配發結果不在命令核定清單時，原始驗證錯誤須保留並精確釋放本次租戶。</summary>
    [Fact]
    public void Create_UnapprovedAllocatedChannel_RecoversBeforeFailure()
    {
        using var fixture = new Fixture();
        var failure = new InvalidDataException("synthetic channel refused");
        Assert.Same(failure, Assert.Throws<InvalidDataException>(() => fixture.Service.Create(
            "c201", "測試甲", null, [], [], default, _ => throw failure)));
        Assert.False(File.Exists(fixture.Path));
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
    }

    /// <summary>撤銷未用邀請後須重新盤點並清除已完成加入的會友；LINE 僅收到第二份完整快照的使用者。</summary>
    [Fact]
    public async Task Cleanup_RevokesInviteAndRefreshesJoinedContactBeforeLine()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Service.Create("c201", "測試甲", null, [], [], default);
        var invite = new Entity("new_testinvite", Guid.NewGuid())
        { RowVersion = "1", ["new_tenant"] = new EntityReference("account", manifest.TenantId) };
        fixture.Sdk.Rows.Add((invite.LogicalName, invite.Id), invite);
        fixture.Sdk.AfterUpdate = _ =>
        {
            var joined = new Entity("contact", Guid.NewGuid())
            { ["parentcustomerid"] = new EntityReference("account", manifest.TenantId), ["new_lineid"] = User };
            fixture.Sdk.Rows.Add((joined.LogicalName, joined.Id), joined);
        };
        var calls = 0;
        await fixture.Service.CleanupAsync("c201", (_, users, _) =>
        {
            calls++;
            Assert.Equal(User, Assert.Single(users));
            Assert.NotNull(invite.GetAttributeValue<DateTime?>("new_usedon"));
            return Task.CompletedTask;
        }, default);
        Assert.Equal(1, calls);
        Assert.Equal(1, fixture.Sdk.UpdateCount);
        Assert.False(File.Exists(fixture.Path));
        Assert.DoesNotContain(fixture.Sdk.Rows.Values, row => row.LogicalName is "contact" or "new_testinvite");
    }

    /// <summary>第二輪仍有新未用邀請時有界停止；不能無限重試，也不能先呼叫 LINE 或刪資料。</summary>
    [Fact]
    public async Task Cleanup_ContinuouslyChangingInvites_StopsAfterOneRevocation()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Service.Create("c201", "測試甲", null, [], [], default);
        var invite = new Entity("new_testinvite", Guid.NewGuid())
        { RowVersion = "1", ["new_tenant"] = new EntityReference("account", manifest.TenantId) };
        fixture.Sdk.Rows.Add((invite.LogicalName, invite.Id), invite);
        fixture.Sdk.AfterUpdate = _ =>
        {
            var added = new Entity("new_testinvite", Guid.NewGuid())
            { RowVersion = "1", ["new_tenant"] = new EntityReference("account", manifest.TenantId) };
            fixture.Sdk.Rows.Add((added.LogicalName, added.Id), added);
        };
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CleanupAsync("c201",
            (_, _, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, calls);
        Assert.Equal(1, fixture.Sdk.UpdateCount);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
        Assert.True(File.Exists(fixture.Path));
    }

    /// <summary>邀請缺少版本時必須在任何更新或 LINE 副作用前拒絕，不能退回無條件標記已用。</summary>
    [Fact]
    public async Task Cleanup_InviteMissingRowVersion_RefusedBeforeMutation()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Service.Create("c201", "測試甲", null, [], [], default);
        var invite = new Entity("new_testinvite", Guid.NewGuid())
        { ["new_tenant"] = new EntityReference("account", manifest.TenantId) };
        fixture.Sdk.Rows.Add((invite.LogicalName, invite.Id), invite);
        var calls = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.CleanupAsync("c201",
            (_, _, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Sdk.UpdateCount);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
        Assert.True(File.Exists(fixture.Path));
    }

    /// <summary>caller 在撤銷交易返回時取消，保留可續跑清單；重跑使用已用邀請，不遺棄或重做原 CAS。</summary>
    [Fact]
    public async Task Cleanup_CancellationAfterRevocation_RetainsManifestAndCanResume()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var manifest = fixture.Service.Create("c201", "測試甲", null, [], [], default);
        var invite = new Entity("new_testinvite", Guid.NewGuid())
        { RowVersion = "1", ["new_tenant"] = new EntityReference("account", manifest.TenantId) };
        fixture.Sdk.Rows.Add((invite.LogicalName, invite.Id), invite);
        fixture.Sdk.AfterUpdate = _ => cancellation.Cancel();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CleanupAsync("c201",
            (_, _, _) => { calls++; return Task.CompletedTask; }, cancellation.Token));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
        Assert.True(File.Exists(fixture.Path));
        await fixture.Service.CleanupAsync("c201", (_, _, _) => Task.CompletedTask, default);
        Assert.Equal(1, fixture.Sdk.UpdateCount);
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>fixture 唯一擁有記憶體 SDK 與隨機暫存根；不存在外部 client 或背景工作。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>案例獨占的隨機本地根，僅儲存合成正式 ID manifest，不使用真實個資。</summary>
        internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "f12-" + Guid.NewGuid().ToString("N"));
        /// <summary>真 manifest writer 使用的固定 c201 路徑，供案例驗證失敗保留及成功刪除。</summary>
        internal string Path => System.IO.Path.Combine(Root, "test-tenants", "c201.json");
        /// <summary>案例唯一擁有的記憶體 SDK；沒有外部連線或背景活動。</summary>
        internal MemorySdk Sdk { get; } = new();
        /// <summary>每次借用同一案例 SDK 的真服務，所有可變資料仍由本 fixture 限定。</summary>
        internal TestTenantService Service => new(Sdk, "goodshepherd20", "GoodShepherd2", Root);
        /// <summary>所有同步及非同步作業已返回後，只刪案例自己建立的隨機目錄。</summary>
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }

    /// <summary>僅模擬正式 SDK 的 ID、範圍查詢與同步配發；真服務仍擁有安全驗證及清理順序。</summary>
    private sealed class MemorySdk : IOrganizationService
    {
        /// <summary>依實體及資料庫主鍵保留合成正式列；同名不同 ID 不覆寫。</summary>
        internal Dictionary<(string Entity, Guid Id), Entity> Rows { get; } = new();
        /// <summary>模擬正式 F3 釋放申請次數，不能由刪除 account 直接湊出基線。</summary>
        internal int ReleaseCount { get; private set; }
        /// <summary>精確刪除次數，範圍拒絕時必須維持零。</summary>
        internal int DeleteCount { get; private set; }
        /// <summary>指定第幾次 Create 拋錯，由外層合成交易回滾，零表示不注入。</summary>
        internal int FailCreateAt { get; set; }
        /// <summary>下一筆交易已提交後遺失回應，用以驗證真服務寫前主鍵清單。</summary>
        internal bool LoseNextCommitResponse { get; set; }
        /// <summary>同步配發器回待配發且不建立任何 Channel／slot lookup 的容量耗盡注入。</summary>
        internal bool RefuseAllocation { get; set; }
        /// <summary>僅在已套用單筆同步更新後注入有界競賽或取消，不建立背景工作。</summary>
        internal Action<Entity>? AfterUpdate { get; set; }
        /// <summary>實際更新次數，證明撤銷沒有無界重試。</summary>
        internal int UpdateCount { get; private set; }
        private int creates;
        private readonly Guid channel = Guid.NewGuid();
        private readonly Guid slot = Guid.NewGuid();

        /// <summary>保存 caller 指定正式主鍵並模擬配發／釋放結果；不代做產品範圍驗證。</summary>
        public Guid Create(Entity entity)
        {
            creates++;
            if (creates == FailCreateAt) throw new IOException("synthetic atomic transaction fault");
            entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
            Rows.Add((entity.LogicalName, entity.Id), entity);
            if (entity.LogicalName == "new_slotrequest")
            {
                var account = Rows[("account", entity.GetAttributeValue<EntityReference>("new_tenant").Id)];
                var releasing = entity.GetAttributeValue<OptionSetValue>("new_operation").Value == 100000001;
                if (!releasing && RefuseAllocation)
                {
                    entity["new_result"] = new OptionSetValue(100000001);
                    return entity.Id;
                }
                entity["new_result"] = new OptionSetValue(releasing ? 100000004 : 100000000);
                entity["new_channel"] = new EntityReference("new_linechannel", channel);
                entity["new_slot"] = new EntityReference("new_channelslot", slot);
                account["new_channelslot"] = releasing ? null : new EntityReference("new_channelslot", slot);
                Rows[("new_channelslot", slot)] = new Entity("new_channelslot", slot)
                { ["new_channel"] = new EntityReference("new_linechannel", channel), ["new_tenant"] = releasing ? null : account.ToEntityReference(),
                  ["new_slotstatus"] = new OptionSetValue(releasing ? 100000000 : 100000001), ["new_usedrichmenus"] = 0 };
                Rows[("new_linechannel", channel)] = new Entity("new_linechannel", channel)
                { ["new_product"] = "GoodShepherd2", ["new_channelid"] = "2012804957" };
                if (releasing) ReleaseCount++;
            }
            return entity.Id;
        }

        /// <summary>只依精確實體與主鍵讀取，不存在時回正式的 ObjectDoesNotExist fault 供真服務分類。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => Rows.TryGetValue((entityName, id), out var row)
            ? row : throw new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 });
        /// <summary>解讀真服務的完整 AND／OR 條件，不用固定回傳清單掩蓋漏查跨租戶指派。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = (QueryExpression)query;
            var result = new EntityCollection();
            foreach (var row in Rows.Values.Where(row => row.LogicalName == expression.EntityName && Matches(row, expression.Criteria)))
                result.Entities.Add(row);
            return result;
        }
        /// <summary>只模擬案例使用的 Equal／In 及巢狀 AND／OR，保留 lookup 的正式 ID 比較。</summary>
        private static bool Matches(Entity row, FilterExpression filter)
        {
            var matches = filter.Conditions.Select(condition =>
            {
                var actual = condition.AttributeName.EndsWith("id", StringComparison.Ordinal) && condition.AttributeName == row.LogicalName + "id"
                ? row.Id : row.Attributes.TryGetValue(condition.AttributeName, out var value) ? value is EntityReference reference ? reference.Id : value : null;
                return condition.Operator == ConditionOperator.In ? condition.Values.Contains(actual) : Equals(actual, condition.Values.SingleOrDefault());
            }).Concat(filter.Filters.Select(child => Matches(row, child)));
            return filter.FilterOperator == LogicalOperator.Or ? matches.Any(match => match) : matches.All(match => match);
        }
        /// <summary>回已發布試用 choice、真正逐項交易回應與寫入故障；任何子項失敗還原操作前快照。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveAttributeRequest)
            {
                var metadata = new PicklistAttributeMetadata
                { OptionSet = new OptionSetMetadata { Options = { new OptionMetadata(new Label("試用中", 1028), 912) } } };
                return new RetrieveAttributeResponse { Results = { ["AttributeMetadata"] = metadata } };
            }
            if (request is ExecuteTransactionRequest transaction)
            {
                var before = Rows.ToDictionary(pair => pair.Key, pair => Copy(pair.Value));
                var previousRelease = ReleaseCount;
                var responses = new OrganizationResponseCollection();
                try { foreach (var child in transaction.Requests) responses.Add(Execute(child)); }
                catch
                {
                    Rows.Clear();
                    foreach (var pair in before) Rows.Add(pair.Key, pair.Value);
                    ReleaseCount = previousRelease;
                    throw;
                }
                if (LoseNextCommitResponse)
                {
                    LoseNextCommitResponse = false;
                    throw new IOException("synthetic response loss after commit");
                }
                return new ExecuteTransactionResponse { Results = { ["Responses"] = responses } };
            }
            if (request is CreateRequest create) return new CreateResponse { Results = { ["id"] = Create(create.Target) } };
            if (request is DeleteRequest delete) { Delete(delete.Target.LogicalName, delete.Target.Id); return new DeleteResponse(); }
            if (request is UpdateRequest update)
            {
                // fake 只執行 CAS，產品若退成無條件 Update，即使最後清理結果相同仍須失敗。
                Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
                Assert.False(string.IsNullOrWhiteSpace(update.Target.RowVersion));
                Assert.Equal(Rows[(update.Target.LogicalName, update.Target.Id)].RowVersion, update.Target.RowVersion);
                Update(update.Target);
                return new UpdateResponse();
            }
            throw new NotSupportedException();
        }
        /// <summary>只複製 fake 交易私有候選，不保留下一交易會原地修改的 Attribute 字典。</summary>
        private static Entity Copy(Entity row)
        {
            var copy = new Entity(row.LogicalName, row.Id) { RowVersion = row.RowVersion };
            foreach (var pair in row.Attributes) copy[pair.Key] = pair.Value;
            return copy;
        }
        /// <summary>套用本次 CAS 欄位後再觸發同步故障注入，讓真服務的第二次盤點與取消判斷可被觀察。</summary>
        public void Update(Entity entity)
        {
            foreach (var pair in entity.Attributes) Rows[(entity.LogicalName, entity.Id)][pair.Key] = pair.Value;
            UpdateCount++;
            AfterUpdate?.Invoke(entity);
        }
        /// <summary>只刪指定主鍵，不以名稱或父範圍模擬 cascade；因此遺漏的子列會留在決定性斷言中。</summary>
        public void Delete(string entityName, Guid id) { Rows.Remove((entityName, id)); DeleteCount++; }
        /// <summary>案例不接受關聯捷徑，避免假 SDK 替產品默默建立未追蹤資料。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>案例不接受解除關聯捷徑，清理必須經真服務明列正式 ID。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
