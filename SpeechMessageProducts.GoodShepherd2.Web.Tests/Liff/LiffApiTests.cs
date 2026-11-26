/*
檔案整體說明
目的與定位：F6真實Kestrel及Program接線的離線整合驗證，保護HTTP身分、租戶ping隔離、公開設定與LIFF靜態頁；所有SDK及LINE傳輸均為合成依賴。
主要功能與流程：每案建立獨立Development主機、固定部署Channel及可觀察的SDK租約，以真實Bearer HTTP通過官方verifier形狀與產品resolver後讀whoami；joined投影的租戶旗標使用平台choice的SDK OptionSetValue而非bool，避免fixture掩蓋消費型別錯誤。對其他租戶ping必403，同租戶200；同時確認健康端點仍有效且設定不回傳秘密。
使用邊界與維護：主機collection序列執行以避免共用診斷owner衝突；本案只在loopback監聽，所有client、Host、lease與暫存目錄由單案owner按停止、排空、Dispose順序清理。源碼靜態資源只讀，不連LINE、D365或平台SQL，成功不代表外部頁面或手機驗收。
*/
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SpeechMessage.Liff.AspNetCore;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Dataverse;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Liff;

/// <summary>真實Program路由整合，故障會反映為實際HTTP狀態，不以直接呼叫handler代替接線證據。</summary>
[Collection("F2Host")]
public sealed class LiffApiTests
{
    /// <summary>本人屬甲租戶時，乙租戶ping必403且不回乙資料；同scope與whoami都能成功。</summary>
    [Fact]
    public async Task Api_TenantAUserRequestsTenantB_Returns403()
    {
        await using var f = await Fixture.StartAsync();
        using var denied = await f.GetAsync("/api/liff/tenant/" + Guid.NewGuid().ToString("D") + "/ping", true);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("", await denied.Content.ReadAsStringAsync());
        using var own = await f.GetAsync("/api/liff/tenant/" + f.TenantId.ToString("D") + "/ping", true);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var response = await f.GetAsync("/api/liff/whoami", true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(f.TenantId, body.RootElement.GetProperty("tenantId").GetGuid());
        Assert.Equal("合成甲租戶", body.RootElement.GetProperty("tenantName").GetString());
        Assert.Empty(body.RootElement.GetProperty("roles").EnumerateArray());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(3, f.Released);
    }

    /// <summary>無Bearer不能讀whoami；只回公開LIFF app ID及Development旗標，不公開Channel secret或LINE userId。</summary>
    [Fact]
    public async Task Api_MissingBearerAndPublicConfig_AreSeparated()
    {
        await using var f = await Fixture.StartAsync();
        using var denied = await f.GetAsync("/api/liff/whoami", false);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var config = await f.GetAsync("/api/liff/config", false);
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
        using var body = JsonDocument.Parse(await config.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
        Assert.Equal("synthetic-liff", body.RootElement.GetProperty("liffId").GetString());
        Assert.True(body.RootElement.GetProperty("isDevelopment").GetBoolean());
        Assert.Equal(0, f.Released);
    }

    /// <summary>F6靜態頁及首頁真實可讀，health/live沒有被新分支攔截；只算本機離線HTTP。</summary>
    [Theory]
    [InlineData("/liff/")]
    [InlineData("/liff/whoami.html")]
    [InlineData("/health/live")]
    public async Task Api_StaticAndExistingHealth_Return200(string path)
    {
        await using var f = await Fixture.StartAsync();
        using var response = await f.GetAsync(path, false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, f.Released);
    }

    /// <summary>三輪各64個A/B使用者交錯呼叫，whoami只回本人租戶，跨租戶ping全403；完成後所有SDK租約回基線。</summary>
    [Fact]
    public async Task Api_ConcurrentABRequests_StayIsolatedAndDrain()
    {
        await using var f = await Fixture.StartAsync();
        for (var round = 0; round < 3; round++)
        {
            await Task.WhenAll(Enumerable.Range(0, 64).Select(async index =>
            {
                var isB = index % 2 != 0;
                var own = isB ? f.SecondTenantId : f.TenantId;
                var other = isB ? f.TenantId : f.SecondTenantId;
                var token = isB ? "synthetic-token-b" : "synthetic-token";
                using var identity = await f.GetAsync("/api/liff/whoami", true, token);
                Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
                using var body = JsonDocument.Parse(await identity.Content.ReadAsStringAsync());
                Assert.Equal(own, body.RootElement.GetProperty("tenantId").GetGuid());
                Assert.Equal(isB ? "合成乙租戶" : "合成甲租戶", body.RootElement.GetProperty("tenantName").GetString());
                Assert.Empty(body.RootElement.GetProperty("roles").EnumerateArray());
                using var denied = await f.GetAsync("/api/liff/tenant/" + other.ToString("D") + "/ping", true, token);
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                Assert.Equal("", await denied.Content.ReadAsStringAsync());
            }));
            Assert.Equal(0, f.Active);
            Assert.Equal((round + 1) * 128, f.Released);
        }
    }

    /// <summary>缺少伺服器Channel映射時，有Bearer仍403且不開驗證transport或SDK，不能從前端補入權限。</summary>
    [Fact]
    public async Task Api_UnconfiguredMapping_FailsClosedWithoutExternalWork()
    {
        await using var f = await Fixture.StartAsync(configured: false);
        using var denied = await f.GetAsync("/api/liff/whoami", true);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, f.Verified);
        Assert.Equal(0, f.Active);
        Assert.Equal(0, f.Released);
    }

    /// <summary>單案主機與client的唯一owner，使用合成joined row而不是虛構已通過的遠端SDK。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        /// <summary>合成權威租戶，所有回應都應受它限制。</summary>
        internal Guid TenantId { get; } = Guid.NewGuid();
        /// <summary>合成乙租戶，不得在甲的whoami回應出現。</summary>
        internal Guid SecondTenantId { get; } = Guid.NewGuid();
        /// <summary>歸還次數可觀察完整SDK清理。</summary>
        internal int Released => Volatile.Read(ref released);
        /// <summary>當案完整歸還計數，並行callback以原子操作維護。</summary>
        private int released;
        /// <summary>當案尚未排空租約數，HTTP完成後必為零。</summary>
        internal int Active => Volatile.Read(ref active);
        /// <summary>只供當案租約生命週期的有界計數，不保留任何使用者。</summary>
        private int active;
        /// <summary>合成官方驗證操作數，未配置時必為零。</summary>
        internal int Verified => verificationHandler.Calls;
        /// <summary>唯一暫存診斷根，沒有真實秘密。</summary>
        private readonly string directory = Path.Combine(Path.GetTempPath(), "3p-f6-api-" + Guid.NewGuid().ToString("N"));
        /// <summary>本案唯一網路client，只連loopback。</summary>
        private HttpClient? client;
        /// <summary>本案唯一官方驗證合成client。</summary>
        private readonly HttpClient verificationClient;
        /// <summary>本案唯一transport探針，不跨主機保存身份。</summary>
        private readonly VerificationHandler verificationHandler = new();
        /// <summary>本案唯一真實Host，由DisposeAsync完整停止。</summary>
        private WebApplication? app;

        /// <summary>限制建立流程經StartAsync，確保失敗亦能清理部分建立的owner。</summary>
        private Fixture() => verificationClient = new HttpClient(verificationHandler);

        /// <summary>建立合成SDK及真實Program，Kestrel只綁loopback動態埠，部分失敗也會清理。</summary>
        internal static async Task<Fixture> StartAsync(bool configured = true)
        {
            var f = new Fixture();
            Directory.CreateDirectory(f.directory);
            try
            {
                var repository = new DirectoryInfo(AppContext.BaseDirectory);
                while (!File.Exists(Path.Combine(repository.FullName, "SpeechMessageProducts.sln")))
                    repository = repository.Parent ?? throw new InvalidOperationException("Repository was not found.");
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    EnvironmentName = Environments.Development, ContentRootPath = f.directory,
                    WebRootPath = Path.Combine(repository.FullName, "SpeechMessageProducts.GoodShepherd2.Web/wwwroot")
                });
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CrmConnection:ServerUrl"] = "https://goodshepherd20.example/XRMServices/2011/Organization.svc",
                    ["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20",
                    ["ExceptionNotifications:WriteExceptionLog"] = "true",
                    ["ExceptionNotifications:SendLine"] = "false",
                    ["ConnectionStrings:Platform"] = @"Server=(localdb)\MSSQLLocalDB;Database=GoodShepherd2Platform;Integrated Security=true;Encrypt=true",
                    ["Liff:LoginChannelId"] = configured ? "2012805181" : "", ["Liff:MessagingChannelKey"] = configured ? "2012804957" : "",
                    ["Liff:LiffId"] = "synthetic-liff"
                });
                var row = Row(f.TenantId, "synthetic-a", "合成甲租戶");
                var second = Row(f.SecondTenantId, "synthetic-b", "合成乙租戶");
                var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
                sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
                {
                    var expression = Assert.IsType<QueryExpression>(query);
                    var person = Assert.Single(expression.Criteria.Conditions).Values.Single();
                    return person switch
                    {
                        "synthetic-a" => new EntityCollection([row]),
                        "synthetic-b" => new EntityCollection([second]),
                        _ => throw new InvalidOperationException("Unexpected synthetic identity.")
                    };
                });
                var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
                manager.Setup(m => m.Acquire(It.IsAny<CancellationToken>())).Returns(() =>
                {
                    Interlocked.Increment(ref f.active);
                    var lease = new Mock<IClientLease>(MockBehavior.Strict);
                    lease.SetupGet(l => l.Service).Returns(sdk.Object);
                    lease.Setup(l => l.Dispose()).Callback(() =>
                    {
                        Interlocked.Increment(ref f.released);
                        Interlocked.Decrement(ref f.active);
                    });
                    lease.Setup(l => l.MarkFaulted());
                    return lease.Object;
                });
                builder.Services.AddSingleton(manager.Object);
                builder.Services.AddSingleton<ICurrentTenantSelectionStore, NoSqlSelection>();
                builder.Services.AddSingleton(new LiffIdTokenVerifier(f.verificationClient, new ChurchClock()));
                f.app = Program.BuildApplication(builder);
                await f.app.StartAsync();
                var address = Assert.Single(f.app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
                f.client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        /// <summary>每筆HTTP唯一request承載Bearer，不寫共用DefaultRequestHeaders。</summary>
        internal async Task<HttpResponseMessage> GetAsync(string path, bool bearer, string token = "synthetic-token")
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            if (bearer) request.Headers.Authorization = new("Bearer", token);
            return await client!.SendAsync(request);
        }

        /// <summary>先停止及處置Host，再client與暫存資料；沒有遺留程序或背景工作。</summary>
        public async ValueTask DisposeAsync()
        {
            try { if (app is not null) { try { await app.StopAsync(); } finally { await app.DisposeAsync(); } } }
            finally { client?.Dispose(); verificationClient.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        }

        /// <summary>對應01的完整joined SDK投影，不含Channel密文。</summary>
        private static Entity Row(Guid tenant, string person, string name)
        {
            var slot = Guid.NewGuid();
            var channel = Guid.NewGuid();
            var row = new Entity("contact", Guid.NewGuid()) { ["new_lineid"] = person, ["parentcustomerid"] = new EntityReference("account", tenant) };
            void Add(string alias, string entity, string field, object value) => row[alias + "." + field] = new AliasedValue(entity, field, value);
            Add("tenant", "account", "accountid", tenant); Add("tenant", "account", "name", name);
            Add("tenant", "account", "new_istenant", new OptionSetValue(100000001)); Add("tenant", "account", "new_channelslot", new EntityReference("new_channelslot", slot));
            Add("slot", "new_channelslot", "new_channelslotid", slot); Add("slot", "new_channelslot", "new_tenant", new EntityReference("account", tenant));
            Add("slot", "new_channelslot", "new_channel", new EntityReference("new_linechannel", channel));
            Add("channel", "new_linechannel", "new_linechannelid", channel); Add("channel", "new_linechannel", "new_product", "GoodShepherd2");
            Add("channel", "new_linechannel", "new_channelid", "2012804957");
            return row;
        }
    }

    /// <summary>唯一候選路徑不應觸碰selection；若接線誤用會直接失敗，不以替身宣稱SQL成功。</summary>
    private sealed class NoSqlSelection : ICurrentTenantSelectionStore
    {
        /// <summary>禁止SQL讀取，讓單一候選的零selection依賴可被觀察。</summary>
        public Task<Guid?> GetAsync(string product, string channelKey, string lineUserId, CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected selection read.");
        /// <summary>禁止SQL寫入，此組測試沒有執行租戶切換。</summary>
        public Task SetAsync(string product, string channelKey, string lineUserId, Guid contactId, CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected selection write.");
    }

    /// <summary>唯一合成官方驗證transport，沒有任何實際網路或User Secrets。</summary>
    private sealed class VerificationHandler : HttpMessageHandler
    {
        /// <summary>當案已完成的合成token驗證數，沒有保存token。</summary>
        internal int Calls => Volatile.Read(ref calls);
        /// <summary>並行HTTP使用的原子計數；無跨案靜態狀態。</summary>
        private int calls;
        /// <summary>回與部署aud相符的固定合成sub及2030到期值；所有response由verifier處置。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            var person = form.Split('&').Contains("id_token=synthetic-token-b", StringComparer.Ordinal) ? "synthetic-b" : "synthetic-a";
            Interlocked.Increment(ref calls);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { aud = "2012805181", sub = person, exp = 1893456000L }), Encoding.UTF8, "application/json")
            };
        }
    }
}
