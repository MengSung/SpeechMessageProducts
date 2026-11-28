/*
檔案整體說明
目的與定位：以真 TestInviteService 與單案記憶體 SDK 替身驗證 F12 邀請簽發，不執行 LIFF 兌換或遠端服務。
主要功能與流程：建立獨立組織、租戶、角色、名額與 Channel 候選，注入錯誤型別、重複身份、取消及寫入故障，斷言只存原始三十二位元組的 SHA-256 並返回正式 ID。
保護契約：只有三組精確組織／產品、Choice 是值、當前已配發名額與固定測試 Channel 可簽發；角色依租戶與代碼授權，不依名稱。憑證只能交付、不出現在預設格式化或 SDK payload。
資源擁有權：每案獨占假 SDK 與 OrganizationLease，假資料只活到測試結束；寫入是單列原子候選，故障前不提交。測試中的背景呼叫以有界訊號阻塞並在 finally 放行、等候排空及 Dispose，不遺棄工作。
失敗與驗證：查詢必須最多兩筆並拒絕 MoreRecords；每個失敗案例斷言零發布／零新增，取消後已提交的回應保留可清理 ID。所有結果均為離線合成證據，不代表 D365 資料庫唯一鍵或實機資源計數已驗證。
*/
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.TestTenants;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.TestTenants;

/// <summary>透過正式簽發入口驗證安全、隔離與有界生命週期；SDK 傳輸由單案記憶體替身取代。</summary>
public sealed class TestInviteServiceTests
{
    /// <summary>固定公開合成 Messaging Channel ID；不是 LINE token 或任何部署秘密。</summary>
    private const string TestChannel = "1234567890";

    /// <summary>獨立固定 UTC 時鐘，避免時區及執行速度影響到期斷言。</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);

    /// <summary>邀請唯一允許持久化的五欄，固定陣列只供斷言使用，不對外暴露。</summary>
    private static readonly string[] InviteColumns = ["new_expireson", "new_name", "new_role", "new_tenant", "new_tokenhash"];

    /// <summary>六組產品／角色都只持久化雜湊，正確連結正式租戶與角色，結果預設文字不含憑證。</summary>
    [Theory]
    [InlineData("goodshepherd20", "GoodShepherd2", "member")]
    [InlineData("goodshepherd20", "GoodShepherd2", "admin")]
    [InlineData("construction", "GoodButler", "member")]
    [InlineData("construction", "GoodButler", "admin")]
    [InlineData("association", "GoodGuild", "member")]
    [InlineData("association", "GoodGuild", "admin")]
    public void Invite_StoresOnlyHash(string organization, string product, string role)
    {
        using var service = new InviteOrganization(organization, product);
        service.Role["new_code"] = role;
        service.Role["new_isadmin"] = new OptionSetValue(role == "admin" ? 100000001 : 100000000);
        var result = Issue(service, role: role);
        var stored = Assert.Single(service.Invites);
        Assert.Equal(service.CreatedId, result.InviteId);
        Assert.NotEqual(Guid.Empty, result.InviteId);
        Assert.Equal(Guid.Empty, service.SubmittedId);
        Assert.Equal(43, result.Token.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", result.Token);
        var raw = Base64Url.DecodeFromChars(result.Token);
        try
        {
            Assert.Equal(32, raw.Length);
            Assert.Equal(result.Token, Base64Url.EncodeToString(raw));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(raw)), stored["new_tokenhash"]);
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
        Assert.Matches("^[0-9A-F]{64}$", Assert.IsType<string>(stored["new_tokenhash"]));
        Assert.Equal(new EntityReference("account", service.Tenant.Id), stored["new_tenant"]);
        Assert.Equal(new EntityReference("new_role", service.Role.Id), stored["new_role"]);
        Assert.Equal(Now.AddMinutes(30), result.ExpiresAt);
        Assert.Equal(TimeSpan.Zero, result.ExpiresAt.Offset);
        Assert.Equal(Now.AddMinutes(30).UtcDateTime, stored["new_expireson"]);
        Assert.Equal(DateTimeKind.Utc, Assert.IsType<DateTime>(stored["new_expireson"]).Kind);
        Assert.Equal(TestChannel, result.ChannelKey);
        Assert.Equal("", result.BasicId);
        Assert.False(stored.Contains("new_usedon"));
        Assert.Equal(InviteColumns,
            stored.Attributes.Keys.OrderBy(static value => value, StringComparer.Ordinal));
        Assert.DoesNotContain(result.Token, result.ToString(), StringComparison.Ordinal);
        Assert.All(stored.Attributes.Values.OfType<string>(), value => Assert.DoesNotContain(result.Token, value, StringComparison.Ordinal));
        Assert.Equal(1, service.Disposals);
        Assert.Equal(0, service.InFlight);
    }

    /// <summary>正式、legacy、大小寫變體與錯誤產品必須在 SDK I/O 之前拒絕，不能信任 catalog 擴充。</summary>
    [Theory]
    [InlineData("production", "GoodShepherd2")]
    [InlineData("goodshepherd", "GoodShepherd2")]
    [InlineData("goodshepherdback", "GoodShepherd2")]
    [InlineData("GoodShepherd20", "GoodShepherd2")]
    [InlineData("goodshepherd20", "GoodButler")]
    [InlineData("construction", "GoodGuild")]
    [InlineData("association", "GoodShepherd2")]
    [InlineData("goodshepherd20", "goodshepherd2")]
    public void Invite_ProductionOrProductMismatch_RefusedBeforeIo(string organization, string product)
    {
        using var service = new InviteOrganization();
        Assert.Throws<InvalidDataException>(() => new TestInviteService(service, organization, product,
            new FixedClock(), channelKey: TestChannel));
        Assert.Equal(0, service.Calls);
        Assert.Empty(service.Invites);
    }

    /// <summary>缺少伺服器測試 Channel 或輸入非數字時禁止從顯示名稱推導，也不先接觸 SDK。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("T01")]
    [InlineData("123/456")]
    [InlineData("１２３４")]
    [InlineData("123456789012345678901")]
    public void Invite_InvalidServerChannel_RefusedBeforeIo(string? channel)
    {
        using var service = new InviteOrganization();
        Assert.Throws<InvalidDataException>(() => new TestInviteService(service, service.Organization, service.Product,
            channelKey: channel));
        Assert.Equal(0, service.Calls);
    }

    /// <summary>只接受精確 member／admin、非空租戶與一至三十分鐘，錯誤不帶輸入原文且不查詢。</summary>
    [Theory]
    [InlineData("member", 0, false)]
    [InlineData("member", -1, false)]
    [InlineData("member", 31, false)]
    [InlineData("member", int.MaxValue, false)]
    [InlineData("owner", 30, false)]
    [InlineData("Member", 30, false)]
    [InlineData(" admin", 30, false)]
    [InlineData("", 30, false)]
    [InlineData("member", 30, true)]
    public void Invite_InvalidArguments_RefusedBeforeIo(string role, int minutes, bool emptyTenant)
    {
        using var service = new InviteOrganization();
        var issuer = NewIssuer(service);
        var failure = Assert.Throws<InvalidDataException>(() => issuer.Create(emptyTenant ? Guid.Empty : service.Tenant.Id,
            role, minutes, CancellationToken.None));
        Assert.Equal("Test invite request rejected.", failure.Message);
        Assert.Equal(0, service.Calls);
    }

    /// <summary>未通過伺服器名稱、WhoAmI ID 或有效登入的回應，不能開始讀寫任何租戶資料。</summary>
    [Theory]
    [InlineData("name")]
    [InlineData("name-case")]
    [InlineData("empty-id")]
    [InlineData("who-id")]
    [InlineData("who-user")]
    public void Invite_ServerIdentityMismatch_Refused(string fault)
    {
        using var service = new InviteOrganization { IdentityFault = fault };
        Assert.Throws<InvalidDataException>(() => Issue(service));
        Assert.Empty(service.Invites);
        Assert.True(service.Calls <= 2);
        Assert.Equal(1, service.Disposals);
    }

    /// <summary>兩個 schema Choice 欄位都只接受明確「是」；bool、整數與缺欄不得被當成相同值。</summary>
    [Theory]
    [InlineData("new_istenant", "bool")]
    [InlineData("new_istesttenant", "bool")]
    [InlineData("new_istenant", "integer")]
    [InlineData("new_istesttenant", "integer")]
    [InlineData("new_istenant", "missing")]
    [InlineData("new_istesttenant", "missing")]
    [InlineData("new_istenant", "no")]
    [InlineData("new_istesttenant", "no")]
    public void Invite_TenantFlagsRequireChoiceYes(string field, string fault)
    {
        using var service = new InviteOrganization();
        service.Tenant[field] = fault switch
        {
            "bool" => true, "integer" => 100000001, "no" => new OptionSetValue(100000000), _ => null!
        };
        Assert.Throws<InvalidDataException>(() => Issue(service));
        Assert.Empty(service.Invites);
        Assert.Equal(1, service.Disposals);
    }

    /// <summary>角色與名額必須恰一筆完整正式身份，重複、MoreRecords、錯租戶或錯型別都不能發布邀請。</summary>
    [Theory]
    [InlineData("role", "none")]
    [InlineData("role", "duplicate")]
    [InlineData("role", "more")]
    [InlineData("role", "id")]
    [InlineData("role", "entity")]
    [InlineData("role", "tenant")]
    [InlineData("role", "lookup")]
    [InlineData("role", "inactive")]
    [InlineData("role", "code")]
    [InlineData("role", "admin-choice")]
    [InlineData("slot", "none")]
    [InlineData("slot", "duplicate")]
    [InlineData("slot", "more")]
    [InlineData("slot", "id")]
    [InlineData("slot", "entity")]
    [InlineData("slot", "tenant")]
    [InlineData("slot", "lookup")]
    [InlineData("slot", "inactive")]
    [InlineData("slot", "status")]
    [InlineData("slot", "account-slot")]
    [InlineData("slot", "channel")]
    public void Invite_UniqueLookupConflict_Refused(string target, string fault)
    {
        using var service = new InviteOrganization { LookupTarget = target, LookupFault = fault };
        var row = target == "role" ? service.Role : service.Slot;
        switch (fault)
        {
            case "id": row.Id = Guid.Empty; break;
            case "entity": row.LogicalName = "contact"; break;
            case "tenant": row[target == "role" ? "new_tenantid" : "new_tenant"] = new EntityReference("account", Guid.NewGuid()); break;
            case "lookup": row[target == "role" ? "new_tenantid" : "new_tenant"] = new EntityReference("contact", service.Tenant.Id); break;
            case "inactive": row["statecode"] = new OptionSetValue(1); break;
            case "code": row["new_code"] = "admin"; break;
            case "admin-choice": row["new_isadmin"] = new OptionSetValue(100000001); break;
            case "status": row["new_slotstatus"] = new OptionSetValue(100000000); break;
            case "account-slot": service.Tenant["new_channelslot"] = new EntityReference("new_channelslot", Guid.NewGuid()); break;
            case "channel": row["new_channel"] = new EntityReference("contact", service.Channel.Id); break;
        }
        Assert.Throws<InvalidDataException>(() => Issue(service));
        Assert.Empty(service.Invites);
        Assert.Equal(1, service.Disposals);
    }

    /// <summary>租戶與 Channel 單筆讀回也須核對正式 ID、精確產品、當前關聯及可用狀態，禁止 legacy 後備。</summary>
    [Theory]
    [InlineData("tenant-id")]
    [InlineData("tenant-entity")]
    [InlineData("tenant-code")]
    [InlineData("tenant-inactive")]
    [InlineData("tenant-slot")]
    [InlineData("channel-id")]
    [InlineData("channel-entity")]
    [InlineData("channel-key")]
    [InlineData("channel-product")]
    [InlineData("channel-legacy")]
    [InlineData("channel-inactive")]
    [InlineData("channel-status")]
    [InlineData("channel-type")]
    public void Invite_AuthoritativeRowMismatch_Refused(string fault)
    {
        using var service = new InviteOrganization();
        switch (fault)
        {
            case "tenant-id": service.Tenant.Id = Guid.NewGuid(); break;
            case "tenant-entity": service.Tenant.LogicalName = "contact"; break;
            case "tenant-code": service.Tenant["new_tenantcode"] = ""; break;
            case "tenant-inactive": service.Tenant["statecode"] = new OptionSetValue(1); break;
            case "tenant-slot": service.Tenant["new_channelslot"] = new EntityReference("contact", service.Slot.Id); break;
            case "channel-id": service.Channel.Id = Guid.NewGuid(); break;
            case "channel-entity": service.Channel.LogicalName = "account"; break;
            case "channel-key": service.Channel["new_channelid"] = "9876543210"; break;
            case "channel-product": service.Channel["new_product"] = "GoodButler"; break;
            case "channel-legacy": service.Channel["new_product"] = ""; break;
            case "channel-inactive": service.Channel["statecode"] = new OptionSetValue(1); break;
            case "channel-status": service.Channel["new_channelstatus"] = new OptionSetValue(100000003); break;
            case "channel-type": service.Channel["new_channeltype"] = new OptionSetValue(100000000); break;
        }
        Assert.Throws<InvalidDataException>(() => Issue(service));
        Assert.Empty(service.Invites);
        Assert.Equal(1, service.Disposals);
    }

    /// <summary>一分鐘邊界與已滿但仍屬既有租戶的 Channel 均可使用，非零偏移時鐘也只儲存 UTC。</summary>
    [Fact]
    public void Invite_MinimumLifetimeAndFullChannel_UseUtc()
    {
        using var service = new InviteOrganization();
        service.Channel["new_channelstatus"] = new OptionSetValue(100000002);
        using var lease = new OrganizationLease(service, service.Dispose);
        var result = new TestInviteService(lease.Service, service.Organization, service.Product,
            new FixedClock(Now.ToOffset(TimeSpan.FromHours(8))), channelKey: TestChannel)
            .Create(service.RequestedTenant, "member", 1, CancellationToken.None);
        Assert.Equal(Now.AddMinutes(1), result.ExpiresAt);
        Assert.Equal(TimeSpan.Zero, result.ExpiresAt.Offset);
    }

    /// <summary>任一讀取完成時 caller 取消都必須停止新增，且同步入場呼叫已排空才能釋放租約。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Invite_ReadCancellation_NoWriteAndDisposed(int cancelAt)
    {
        using var cancellation = new CancellationTokenSource();
        using var service = new InviteOrganization();
        if (cancelAt == 0) cancellation.Cancel();
        service.AfterRead = () => { if (service.Calls == cancelAt) cancellation.Cancel(); };
        Assert.Throws<OperationCanceledException>(() => Issue(service, ct: cancellation.Token));
        Assert.Empty(service.Invites);
        Assert.Equal(cancelAt, service.Calls);
        Assert.Equal(0, service.InFlight);
        Assert.Equal(1, service.Disposals);
    }

    /// <summary>寫入已入場後即使 caller 取消，仍等 SDK 返回並交回已提交 ID，不能先 Dispose 或遺失清理依據。</summary>
    [Fact]
    public async Task Invite_CancelDuringCommit_DrainsThenReturnsCommittedIdentity()
    {
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var service = new InviteOrganization();
        service.BeforeCommit = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
        var work = Task.Run(() => Issue(service, ct: cancellation.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            cancellation.Cancel();
            Assert.False(work.IsCompleted);
            Assert.Equal(0, service.Disposals);
            Assert.Equal(1, service.InFlight);
        }
        finally
        {
            release.Set();
            _ = await work.WaitAsync(TimeSpan.FromSeconds(10));
        }
        var result = await work.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(service.CreatedId, result.InviteId);
        Assert.Single(service.Invites);
        Assert.Equal(0, service.InFlight);
        Assert.Equal(1, service.Disposals);
    }

    /// <summary>SDK 拒絕單列原子 Create 時保留真正例外並釋放租約；不以新 ID 或重試掩蓋故障。</summary>
    [Fact]
    public void Invite_WriteFailure_NoPartialPublicationAndDisposed()
    {
        using var service = new InviteOrganization();
        var fault = new InvalidOperationException("synthetic-sdk-fault");
        service.BeforeCommit = () => throw fault;
        Assert.Same(fault, Assert.Throws<InvalidOperationException>(() => Issue(service)));
        Assert.Empty(service.Invites);
        Assert.Equal(0, service.InFlight);
        Assert.Equal(1, service.Disposals);
    }

    /// <summary>不接受缺少正式主鍵的 Create 成功回應，不能把送出的空 ID 或本地 draft ID 當清理依據。</summary>
    [Fact]
    public void Invite_EmptyServerId_Refused()
    {
        using var service = new InviteOrganization { ReturnEmptyId = true };
        Assert.Throws<InvalidDataException>(() => Issue(service));
        Assert.Equal(1, service.Disposals);
    }

    /// <summary>交錯一百二十八個獨立租戶、產品與同名角色，不共享憑證、候選或服務；每次返回後資源回零。</summary>
    [Fact]
    public void Invite_InterleavedTenants_NoCrossScopeRetention()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 128; index++)
        {
            var organization = index % 2 == 0 ? "goodshepherd20" : "construction";
            var product = index % 2 == 0 ? "GoodShepherd2" : "GoodButler";
            using var service = new InviteOrganization(organization, product);
            var result = Issue(service);
            Assert.True(tokens.Add(result.Token));
            Assert.Equal(service.RequestedTenant, Assert.Single(service.Invites).GetAttributeValue<EntityReference>("new_tenant").Id);
            Assert.Equal(0, service.InFlight);
            Assert.Equal(1, service.Disposals);
        }
        tokens.Clear();
    }

    /// <summary>建立命令區域的服務，測試組合根以固定公開配置提供 Channel key，而非使用者欄位。</summary>
    private static TestInviteService NewIssuer(InviteOrganization service) =>
        new(service, service.Organization, service.Product, new FixedClock(), channelKey: TestChannel);

    /// <summary>模擬 CLI 的唯一租約 owner；成功與故障都在結果返回前釋放 SDK，簽發器不能接管借用資源。</summary>
    private static TestInviteResult Issue(InviteOrganization service, string role = "member", CancellationToken ct = default)
    {
        using var lease = new OrganizationLease(service, service.Dispose);
        return new TestInviteService(lease.Service, service.Organization, service.Product, new FixedClock(), channelKey: TestChannel)
            .Create(service.RequestedTenant, role, 30, ct);
    }

    /// <summary>純值測試時鐘；無 timer、subscription 或任何需要釋放的資源。</summary>
    private sealed class FixedClock(DateTimeOffset? now = null) : TimeProvider
    {
        /// <summary>只回傳此案固定時刻，不讀主機時區或時間。</summary>
        public override DateTimeOffset GetUtcNow() => now ?? Now;
    }

    /// <summary>單案 SDK 與原子單列資料庫替身；只接受精確訊息和有界查詢，Dispose 必須在同步呼叫排空之後。</summary>
    private sealed class InviteOrganization : IOrganizationService, IDisposable
    {
        private readonly Guid organizationId = Guid.NewGuid();
        private bool disposed;

        /// <summary>以合成正式 ID 建立完整且彼此對應的帳戶、角色、名額與產品 Channel，名稱刻意不作身分依據。</summary>
        internal InviteOrganization(string organization = "goodshepherd20", string product = "GoodShepherd2")
        {
            Organization = organization; Product = product;
            RequestedTenant = Guid.NewGuid();
            Channel = new Entity("new_linechannel", Guid.NewGuid())
            {
                ["new_channelid"] = TestChannel, ["new_product"] = product,
                ["new_channeltype"] = new OptionSetValue(100000001), ["new_channelstatus"] = new OptionSetValue(100000001),
                ["statecode"] = new OptionSetValue(0), ["new_name"] = "same-display-name"
            };
            Slot = new Entity("new_channelslot", Guid.NewGuid())
            {
                ["new_tenant"] = new EntityReference("account", RequestedTenant), ["new_channel"] = Channel.ToEntityReference(),
                ["new_slotstatus"] = new OptionSetValue(100000001), ["statecode"] = new OptionSetValue(0)
            };
            Tenant = new Entity("account", RequestedTenant)
            {
                ["new_istenant"] = new OptionSetValue(100000001), ["new_istesttenant"] = new OptionSetValue(100000001),
                ["new_channelslot"] = Slot.ToEntityReference(), ["new_tenantcode"] = "c201", ["statecode"] = new OptionSetValue(0)
            };
            Role = new Entity("new_role", Guid.NewGuid())
            {
                ["new_tenantid"] = new EntityReference("account", RequestedTenant), ["new_code"] = "member",
                ["new_isadmin"] = new OptionSetValue(100000000), ["statecode"] = new OptionSetValue(0), ["new_name"] = "same-display-name"
            };
        }

        /// <summary>此案固定測試組織名稱，身分訊息由此伺服器值回應，不複製 request 值。</summary>
        internal string Organization { get; }
        /// <summary>此案固定產品，作為獨立 SDK 候選的權威產品，不使用顯示名稱。</summary>
        internal string Product { get; }
        /// <summary>呼叫端查詢的固定合成 account 主鍵，與可注入錯誤的回應 ID 分開。</summary>
        internal Guid RequestedTenant { get; }
        /// <summary>本案合成租戶候選，只有測試在呼叫前注入故障，不跨案例共用。</summary>
        internal Entity Tenant { get; }
        /// <summary>本案合成角色，代碼與管理員 Choice 必須由產品重新驗證。</summary>
        internal Entity Role { get; }
        /// <summary>本案當前已配發名額，包含租戶與 Channel 的正式 lookup。</summary>
        internal Entity Slot { get; }
        /// <summary>本案產品 Channel 的安全欄位，沒有 token 或 secret。</summary>
        internal Entity Channel { get; }
        /// <summary>只存已原子提交的完整資料列快照，保留到測試案末供決定性斷言。</summary>
        internal List<Entity> Invites { get; } = [];
        /// <summary>假伺服器在提交時產生的正式主鍵，產品不得事先猜測或自行指定。</summary>
        internal Guid CreatedId { get; private set; }
        /// <summary>記錄 Create 送入的主鍵，應為空以證明未使用本地 draft ID。</summary>
        internal Guid SubmittedId { get; private set; }
        /// <summary>精確 SDK 呼叫次數，用來證明拒絕與取消之前沒有額外 I/O。</summary>
        internal int Calls { get; private set; }
        /// <summary>目前已入場的同步呼叫數，必須在 owner 釋放服務前回零。</summary>
        internal int InFlight { get; private set; }
        /// <summary>唯一 owner 實際執行的釋放次數，重複 Dispose 不增加。</summary>
        internal int Disposals { get; private set; }
        /// <summary>固定合成身分故障種類，不能引入外部組織或秘密。</summary>
        internal string? IdentityFault { get; init; }
        /// <summary>指定角色或名額這一個查詢注入故障，不改寫其他查詢契約。</summary>
        internal string? LookupTarget { get; init; }
        /// <summary>唯一性與續頁的固定故障種類，產品必須自行拒絕回應。</summary>
        internal string? LookupFault { get; init; }
        /// <summary>注入成功提交但缺失回應主鍵，保護不能發布 draft 身分的契約。</summary>
        internal bool ReturnEmptyId { get; init; }
        /// <summary>只在同步讀取仍入場時注入取消，Dispose 時解除委派捕捉。</summary>
        internal Action? AfterRead { get; set; }
        /// <summary>只在原子提交前注入故障或有界阻塞，finally 必須排空並解除委派。</summary>
        internal Action? BeforeCommit { get; set; }

        /// <summary>只允許兩種唯讀組織身分訊息，模擬伺服器回應，不根據 caller 參數製造授權。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => Read<OrganizationResponse>(() => request switch
        {
            RetrieveCurrentOrganizationRequest => new RetrieveCurrentOrganizationResponse
            {
                Results = { ["Detail"] = new OrganizationDetail
                {
                    OrganizationId = IdentityFault == "empty-id" ? Guid.Empty : organizationId,
                    UniqueName = IdentityFault switch { "name" => "production", "name-case" => "GoodShepherd20", _ => Organization }
                } }
            },
            WhoAmIRequest => new WhoAmIResponse
            {
                Results = { ["OrganizationId"] = IdentityFault == "who-id" ? Guid.NewGuid() : organizationId,
                    ["UserId"] = IdentityFault == "who-user" ? Guid.Empty : Guid.NewGuid() }
            },
            _ => throw new NotSupportedException()
        });

        /// <summary>只讀 account／Channel 正式 ID 且禁止 AllColumns 或秘密欄位；回傳選定欄位的獨立快照。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => Read(() =>
        {
            Assert.False(columnSet.AllColumns);
            Assert.DoesNotContain(columnSet.Columns, static name => name.Contains("token", StringComparison.OrdinalIgnoreCase) || name.Contains("secret", StringComparison.OrdinalIgnoreCase));
            var row = entityName switch { "account" => Tenant, "new_linechannel" => Channel, _ => throw new NotSupportedException() };
            Assert.Equal(entityName == "account" ? RequestedTenant : Slot.GetAttributeValue<EntityReference>("new_channel").Id, id);
            return Snapshot(row, columnSet);
        });

        /// <summary>拒絕寬掃描、名稱授權與未約束租戶查詢；唯一性故障是回應注入，用以測試簽發器獨立重驗。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => Read(() =>
        {
            var expression = Assert.IsType<QueryExpression>(query);
            Assert.Equal(2, expression.TopCount);
            Assert.False(expression.ColumnSet.AllColumns);
            Assert.Equal(LogicalOperator.And, expression.Criteria.FilterOperator);
            Assert.Empty(expression.Criteria.Filters);
            Assert.Empty(expression.LinkEntities);
            Assert.DoesNotContain(expression.Criteria.Conditions, static condition => condition.AttributeName == "new_name");
            var isRole = expression.EntityName == "new_role";
            Assert.True(isRole || expression.EntityName == "new_channelslot");
            RequireCondition(expression, isRole ? "new_tenantid" : "new_tenant", RequestedTenant);
            RequireCondition(expression, "statecode", 0);
            RequireCondition(expression, isRole ? "new_code" : "new_slotstatus", isRole ? Role.GetAttributeValue<string>("new_code") == "admin" && LookupFault != "code" ? "admin" : "member" : 100000001);
            var target = isRole ? "role" : "slot";
            var result = new EntityCollection { MoreRecords = LookupTarget == target && LookupFault == "more" };
            if (LookupTarget != target || LookupFault != "none") result.Entities.Add(Snapshot(isRole ? Role : Slot, expression.ColumnSet));
            if (LookupTarget == target && LookupFault == "duplicate") result.Entities.Add(Snapshot(isRole ? Role : Slot, expression.ColumnSet));
            return result;
        });

        /// <summary>單列 Create 先完成故障注入與唯一雜湊檢查，成功才原子新增並返回伺服器主鍵；不自動重試。</summary>
        public Guid Create(Entity entity)
        {
            Enter();
            try
            {
                Assert.Equal("new_testinvite", entity.LogicalName);
                SubmittedId = entity.Id;
                BeforeCommit?.Invoke();
                Assert.DoesNotContain(Invites, row => Equals(row["new_tokenhash"], entity["new_tokenhash"]));
                CreatedId = Guid.NewGuid();
                var stored = Snapshot(entity, new ColumnSet(entity.Attributes.Keys.ToArray()));
                stored.Id = CreatedId;
                Invites.Add(stored);
                return ReturnEmptyId ? Guid.Empty : CreatedId;
            }
            finally { InFlight--; }
        }

        /// <summary>發起同步呼叫的唯一計數入口，已釋放的服務不得重用。</summary>
        private void Enter()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Calls++; InFlight++;
        }

        /// <summary>讀取返回前執行故障／取消注入，finally 排空才允許外層 Dispose。</summary>
        private T Read<T>(Func<T> read)
        {
            Enter();
            try { var result = read(); AfterRead?.Invoke(); return result; }
            finally { InFlight--; }
        }

        /// <summary>建立足夠深的 SDK 欄位快照，不讓服務以保留或修改共享 Entity 通過隔離測試。</summary>
        private static Entity Snapshot(Entity row, ColumnSet columns)
        {
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var name in columns.Columns)
                if (row.Attributes.TryGetValue(name, out var value)) copy[name] = value switch
                {
                    EntityReference reference => new EntityReference(reference.LogicalName, reference.Id),
                    OptionSetValue option => new OptionSetValue(option.Value), _ => value
                };
            return copy;
        }

        /// <summary>斷言正式查詢的單一等號限制，防止替身忽略錯誤查詢而讓測試虛假通過。</summary>
        private static void RequireCondition(QueryExpression query, string attribute, object value)
        {
            var condition = Assert.Single(query.Criteria.Conditions, candidate => candidate.AttributeName == attribute);
            Assert.Equal(ConditionOperator.Equal, condition.Operator);
            Assert.Equal(value, Assert.Single(condition.Values));
        }

        /// <summary>測試 owner 冪等釋放服務及 callback；仍有入場呼叫時釋放直接使測試失敗，保留資料只供斷言到案末。</summary>
        public void Dispose()
        {
            if (disposed) return;
            Assert.Equal(0, InFlight);
            disposed = true; Disposals++;
            AfterRead = null; BeforeCommit = null;
        }

        /// <summary>簽發器不得額外更新既有租戶、角色或名額。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>邀請失敗不允許刪除不屬於本次簽發的資料。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>本簽發流程沒有關聯寫入，意外呼叫必須失敗。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本簽發流程沒有解除關聯，禁止替身悄悄接受額外副作用。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
