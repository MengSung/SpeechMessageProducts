/*
檔案整體說明
目的與定位：F12 開發邀請註冊的離線 HTTP 契約測試，保護環境開關、固定伺服器 Channel、Choice 租戶旗標與一次性交易；不連線 LINE、D365 或 SQL。
主要功能與流程：每案建立獨立 loopback Kestrel、合成官方驗證 transport 與具鎖定提交的 IOrganizationService 替身。真實 LIFF 廣域分支與邀請分支共同啟動，透過實際 POST 驗證拒絕及成功；以雙請求讀取屏障、交易故障及未知傳輸結果證明 CAS 排在兩筆 Create 前且整批回復。
使用邊界與維護：只使用合成身分與隨機邀請，替身交易提交不等同實機 ACID 證據。fixture 唯一擁有 Host、HTTP client、驗證 client、屏障及暫存目錄，先停止排空再 Dispose；每筆 SDK lease 計數歸零，所有 HTTP 訊息逐筆關閉。測試另讀本工作樹 schema 的中文選項，避免猜數字或用 bool 假裝 Choice；來源須 UTF-8 無 BOM、CRLF 與末尾 CRLF。
*/
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SpeechMessage.Liff.AspNetCore;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd2.Web.Liff;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Liff;

/// <summary>實際 HTTP 與正式端點配合可觀察 SDK 替身；每案完整釋放，無跨案授權快取。</summary>
[Collection("F2Host")]
public sealed class DevEnrollmentTests
{
    /// <summary>非 Development、缺省、false 及非布林設定皆不對應端點，亦不借用 SDK。</summary>
    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Staging", "true")]
    [InlineData("Development", null)]
    [InlineData("Development", "false")]
    [InlineData("Development", "1")]
    [InlineData("Development", " true ")]
    public async Task Enroll_NotMappedOutsideDevelopment(string environment, string? enabled)
    {
        await using var f = await Fixture.StartAsync(environment, enabled);
        using var response = await f.PostAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, f.Active);
        Assert.Equal(0, f.VerificationCalls);
    }

    /// <summary>無論邀請註冊在 F6 前或後，真實廣域 LIFF filter 不可吞掉精確 POST。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enroll_StartupFilterPrecedesBroadLiffBranch(bool registerFirst)
    {
        await using var f = await Fixture.StartAsync(registerFirst: registerFirst);
        using var response = await f.PostAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("已加入測試教會。", body.RootElement.GetProperty("message").GetString());
        Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal(1, f.Database.Contacts);
        Assert.Equal(1, f.Database.Assignments);
        Assert.Equal(1, f.Released);
        Assert.Equal(0, f.Active);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    /// <summary>成功後重送同一憑證必為空本文 403，Contact 與角色均不可再增。</summary>
    [Fact]
    public async Task Enroll_TokenReused_Refused()
    {
        await using var f = await Fixture.StartAsync();
        using var first = await f.PostAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var second = await f.PostAsync();
        await AssertDeniedAsync(second);
        Assert.Equal(1, f.Database.Transactions);
        Assert.Equal(1, f.Database.Contacts);
        Assert.Equal(0, f.Active);
    }

    /// <summary>到期或超過三十分鐘的邀請不能寫入；UTC 嚴格邊界不提供寬限。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    public async Task Enroll_Expired_Refused(int minutes)
    {
        await using var f = await Fixture.StartAsync();
        f.Database.Invite["new_expireson"] = DateTime.UtcNow.AddMinutes(minutes);
        using var response = await f.PostAsync();
        await AssertDeniedAsync(response);
        Assert.Equal(0, f.Database.Transactions);
        Assert.Equal(0, f.Active);
    }

    /// <summary>兩個旗標都必須是平台「是」的 OptionSetValue；布林、裸數字及「否」均拒絕。</summary>
    [Theory]
    [InlineData("new_istenant", "false")]
    [InlineData("new_istesttenant", "false")]
    [InlineData("new_istenant", "bool")]
    [InlineData("new_istesttenant", "bool")]
    [InlineData("new_istesttenant", "int")]
    public async Task Enroll_NonTestTenant_Refused(string field, string value)
    {
        await using var f = await Fixture.StartAsync();
        f.Database.Alias("tenant", "account", field, value switch
        {
            "bool" => true, "int" => 100000001, _ => new OptionSetValue(100000000)
        });
        using var response = await f.PostAsync();
        await AssertDeniedAsync(response);
        Assert.Equal(0, f.Database.Transactions);
    }

    /// <summary>官方回應拒絕 ID Token 時統一 403，驗證服務只接受伺服器 Login Channel，SDK 零入場。</summary>
    [Fact]
    public async Task Enroll_InvalidIdToken_Refused()
    {
        await using var f = await Fixture.StartAsync();
        f.Verifier.Status = HttpStatusCode.Unauthorized;
        using var response = await f.PostAsync();
        await AssertDeniedAsync(response);
        Assert.Equal(0, f.Released);
        Assert.False(File.Exists(Path.Combine(f.DirectoryPath, "Logs", "Exception.log")));
    }

    /// <summary>錯產品、舊空產品、跨 Channel、角色跨租戶或非白名單角色均不可建立資料。</summary>
    [Theory]
    [InlineData("product")]
    [InlineData("legacy")]
    [InlineData("channel")]
    [InlineData("role-tenant")]
    [InlineData("role-code")]
    [InlineData("inactive")]
    [InlineData("lifecycle")]
    [InlineData("hash")]
    [InlineData("version")]
    public async Task Enroll_StoredScopeMismatch_Refused(string fault)
    {
        await using var f = await Fixture.StartAsync();
        var db = f.Database;
        switch (fault)
        {
            case "product": db.Alias("channel", "new_linechannel", "new_product", "GoodButler"); break;
            case "legacy": db.Alias("channel", "new_linechannel", "new_product", ""); break;
            case "channel": db.Alias("channel", "new_linechannel", "new_channelid", "9999999999"); break;
            case "role-tenant": db.Alias("role", "new_role", "new_tenantid", new EntityReference("account", Guid.NewGuid())); break;
            case "role-code": db.Alias("role", "new_role", "new_code", "owner"); break;
            case "inactive": db.Alias("tenant", "account", "statecode", new OptionSetValue(1)); break;
            case "lifecycle": db.Alias("tenant", "account", "new_lifecyclestatus", new OptionSetValue(100000006)); break;
            case "hash": db.Invite["new_tokenhash"] = new string('A', 64); break;
            case "version": db.Invite.RowVersion = null; break;
        }
        using var response = await f.PostAsync();
        await AssertDeniedAsync(response);
        Assert.Equal(0, db.Transactions);
    }

    /// <summary>請求只接受 token 欄位與有界 JSON；非正規 base64url、額外聲稱及重複欄位皆拒絕。</summary>
    [Theory]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("oversize")]
    [InlineData("chunked")]
    [InlineData("content-type")]
    [InlineData("padding")]
    [InlineData("noncanonical")]
    [InlineData("bearer")]
    public async Task Enroll_UntrustedBody_RefusedBeforeSdk(string fault)
    {
        await using var f = await Fixture.StartAsync();
        var body = JsonSerializer.Serialize(new { token = f.Token });
        if (fault == "extra") body = JsonSerializer.Serialize(new { token = f.Token, role = "admin" });
        if (fault == "duplicate") body = "{\"token\":\"" + f.Token + "\",\"token\":\"" + f.Token + "\"}";
        if (fault is "oversize" or "chunked") body = new string(' ', 2049);
        if (fault == "padding") body = JsonSerializer.Serialize(new { token = f.Token + "=" });
        if (fault == "noncanonical") body = JsonSerializer.Serialize(new { token = new string('A', 42) + "B" });
        using var response = await f.PostAsync(body, fault == "content-type" ? "text/plain" : "application/json", fault != "bearer", fault == "chunked");
        await AssertDeniedAsync(response);
        Assert.Equal(0, f.Released);
    }

    /// <summary>同一邀請的兩個讀取都在提交前完成；資料庫 CAS 只能讓一人及一份角色提交。</summary>
    [Fact]
    public async Task Enroll_ConcurrentReplay_OneAtomicWinnerAndLeasesDrain()
    {
        await using var f = await Fixture.StartAsync();
        f.Database.ReadBarrier = new Barrier(2);
        var replies = await Task.WhenAll(f.PostAsync(), f.PostAsync());
        try
        {
            Assert.Single(replies, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Forbidden);
            Assert.Equal(2, f.Database.Transactions);
            Assert.Equal(1, f.Database.Contacts);
            Assert.Equal(1, f.Database.Assignments);
            Assert.Equal(2, f.Released);
            Assert.Equal(0, f.Active);
        }
        finally { foreach (var response in replies) response.Dispose(); }
    }

    /// <summary>清理在讀取後先以 CAS 作廢邀請，既有讀者的舊 RowVersion 必須整筆拒絕，不能在重盤點後建立新 Contact。</summary>
    [Fact]
    public async Task Enroll_CleanupRevokesAfterRead_CasPreventsContactCreation()
    {
        await using var f = await Fixture.StartAsync();
        f.Database.BeforeReadReturn = () =>
        {
            f.Database.Invite["new_usedon"] = DateTime.UtcNow;
            f.Database.Invite.RowVersion = "8";
        };
        using var response = await f.PostAsync();
        await AssertDeniedAsync(response);
        Assert.Equal(1, f.Database.Transactions);
        Assert.Equal(0, f.Database.Contacts);
        Assert.Equal(0, f.Database.Assignments);
        Assert.Equal(0, f.Active);
    }

    /// <summary>交易內任一步真實故障完整回復，未知傳輸結果不得自動重送；故障及清理均保留診斷。</summary>
    [Theory]
    [InlineData("contact")]
    [InlineData("assignment")]
    [InlineData("unknown")]
    [InlineData("dispose")]
    public async Task Enroll_TransactionFault_FailsClosedWithoutRetryAndDisposes(string fault)
    {
        await using var f = await Fixture.StartAsync();
        f.Database.Fault = fault;
        using var response = await f.PostAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, f.Database.Transactions);
        Assert.Equal(fault is "unknown" or "dispose" ? 1 : 0, f.Database.Contacts);
        Assert.Equal(fault is "unknown" or "dispose" ? 1 : 0, f.Database.Assignments);
        Assert.Equal(1, f.Released);
        Assert.Equal(0, f.Active);
        var evidence = await File.ReadAllTextAsync(Path.Combine(f.DirectoryPath, "Logs", "Exception.log"));
        Assert.DoesNotContain(f.Token, evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-line-sub", evidence, StringComparison.Ordinal);
    }

    /// <summary>重複正式 ID 或未完整分頁屬資料故障，整批不發布且 SDK 淘汰、歸還。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enroll_AmbiguousInvite_DiagnosedAndNoWrites(bool duplicate)
    {
        await using var f = await Fixture.StartAsync();
        f.Database.Duplicate = duplicate;
        f.Database.MoreRecords = !duplicate;
        using var response = await f.PostAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, f.Database.Transactions);
        Assert.Equal(1, f.Faulted);
        Assert.Equal(0, f.Active);
    }

    /// <summary>同一伺服器角色資料中的 admin 可以加入，不需要也不能由前端傳角色。</summary>
    [Fact]
    public async Task Enroll_StoredAdminRole_Assigned()
    {
        await using var f = await Fixture.StartAsync();
        f.Database.Alias("role", "new_role", "new_code", "admin");
        using var response = await f.PostAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, f.Database.Assignments);
        Assert.Equal(0, f.Active);
    }

    /// <summary>SDK 讀取期間 ID Token 到期，即使邀請尚有效亦必須拒絕，不能沿用已過期身分提交。</summary>
    [Fact]
    public async Task Enroll_IdTokenExpiresDuringSdkRead_Refused()
    {
        await using var f = await Fixture.StartAsync();
        f.Database.BeforeReadReturn = () => f.UtcNow = f.UtcNow.AddMinutes(6);
        using var response = await f.PostAsync();
        await AssertDeniedAsync(response);
        Assert.Equal(0, f.Database.Transactions);
        Assert.Equal(0, f.Active);
    }

    /// <summary>明確 caller 取消不能提早結束已入場同步 SDK；受控閘門釋放後才傳取消並歸還租約，不告警。</summary>
    [Fact]
    public async Task Enroll_CallerCancellation_DrainsSdkBeforeCompletion()
    {
        await using var f = await Fixture.StartAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        f.Database.BeforeReadReturn = () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("Synthetic SDK drain deadline.");
        };
        var operation = Task.Run(() => f.InvokeHandlerAsync(cancellation.Token));
        Exception? failure = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(8)));
            cancellation.Cancel();
            Assert.False(operation.IsCompleted);
            Assert.Equal(1, f.Active);
        }
        finally
        {
            release.Set();
            try { await operation; }
            catch (Exception exception) { failure = exception; }
        }
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(0, f.Database.Transactions);
        Assert.Equal(0, f.Active);
        Assert.Equal(1, f.Released);
        Assert.False(File.Exists(Path.Combine(f.DirectoryPath, "Logs", "Exception.log")));
    }

    /// <summary>內部 SDK 取消不是 caller 取消，必須淘汰租約並以真實例外型別告警；官方服務故障亦不可偽裝成 token 拒絕。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enroll_InternalServiceFailure_Diagnosed(bool internalCancellation)
    {
        await using var f = await Fixture.StartAsync();
        if (internalCancellation) f.Database.BeforeReadReturn = () => throw new OperationCanceledException("Synthetic SDK timeout.");
        else f.Verifier.Status = HttpStatusCode.InternalServerError;
        using var response = await f.PostAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, f.Database.Transactions);
        Assert.Equal(0, f.Active);
        Assert.Equal(internalCancellation ? 1 : 0, f.Faulted);
        var evidence = await File.ReadAllTextAsync(Path.Combine(f.DirectoryPath, "Logs", "Exception.log"));
        Assert.Contains(internalCancellation ? "OperationCanceledException" : "HttpRequestException", evidence, StringComparison.Ordinal);
    }

    /// <summary>直接讀 UTF-8 schema 的中文標籤核對兩個 Choice 與試用狀態，避免 fixture 自證錯值。</summary>
    [Fact]
    public void Enroll_ChoiceValuesMatchAuthoritativeSchema()
    {
        var root = RepositoryRoot();
        using var core = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "d365/schema/platform/00-core.schema.json"), Encoding.UTF8));
        foreach (var name in new[] { "new_istenant", "new_istesttenant" })
            Assert.Equal(100000001, Choice(core, name, "是"));
        using var product = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "d365/schema/goodshepherd.schema.json"), Encoding.UTF8));
        Assert.Equal(100000002, Choice(product, "new_lifecyclestatus", "試用中"));
    }

    /// <summary>從唯一 account 欄位及精確標籤取得契約數值，重複或非 Choice 都使測試失敗。</summary>
    private static int Choice(JsonDocument document, string name, string label)
    {
        var account = Assert.Single(document.RootElement.GetProperty("entities").EnumerateArray(), e => e.GetProperty("logicalName").GetString() == "account");
        var field = Assert.Single(account.GetProperty("fields").EnumerateArray(), f => f.GetProperty("logicalName").GetString() == name);
        Assert.Equal("choice", field.GetProperty("type").GetString());
        return Assert.Single(field.GetProperty("options").EnumerateArray(), o => o.GetProperty("label").GetString() == label).GetProperty("value").GetInt32();
    }

    /// <summary>所有預期拒絕必須一致且沒有錯誤原因或敏感本文。</summary>
    private static async Task AssertDeniedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }

    /// <summary>只查本案測試輸出祖先的方案，不跨工作樹選取來源。</summary>
    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(current.FullName, "SpeechMessageProducts.sln")))
            current = current.Parent ?? throw new InvalidOperationException("Repository unavailable.");
        return current.FullName;
    }

    /// <summary>單案資源 owner；反射只讓尚未實作端點的 RED 成為明確斷言而非編譯失敗。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        /// <summary>本案唯一合成邀請，只供 POST，從不寫入證據或診斷。</summary>
        internal string Token { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        /// <summary>本案資料庫替身，不與其他主機共用。</summary>
        internal FakeDatabase Database { get; private set; } = null!;
        /// <summary>本案 verifier transport，由 verificationClient 唯一關閉。</summary>
        internal VerificationHandler Verifier { get; } = new();
        /// <summary>合成官方驗證次數，停用端點必為零。</summary>
        internal int VerificationCalls => Verifier.Calls;
        /// <summary>本案獨有的診斷暫存根，不含正式設定。</summary>
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "f12-enroll-" + Guid.NewGuid().ToString("N"));
        /// <summary>尚在 SDK 操作中的租約數，HTTP 完成後必為零。</summary>
        internal int Active;
        /// <summary>完整歸還數，Dispose 失敗也必先歸零。</summary>
        internal int Released;
        /// <summary>真正 SDK 故障的淘汰計數。</summary>
        internal int Faulted;
        /// <summary>同案明確 UTC 測試時鐘，只在受控 SDK 閘門後由測試推進。</summary>
        internal DateTimeOffset UtcNow;
        /// <summary>同一主機唯一 loopback client，不使用共用預設 Authorization。</summary>
        private HttpClient? client;
        /// <summary>合成驗證 client，無外部網路 handler。</summary>
        private HttpClient? verificationClient;
        /// <summary>唯一 HTTP Host，停止後才能釋放依賴。</summary>
        private WebApplication? app;

        /// <summary>組合真實 broad LIFF filter 與受測 filter；所有 DI 外部依賴皆合成，建置失敗亦清理。</summary>
        internal static async Task<Fixture> StartAsync(string environment = "Development", string? enabled = "true", bool registerFirst = true)
        {
            var f = new Fixture();
            Directory.CreateDirectory(f.DirectoryPath);
            try
            {
                f.Database = new FakeDatabase(f.Token);
                f.UtcNow = DateTimeOffset.UtcNow;
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    EnvironmentName = environment, ContentRootPath = f.DirectoryPath,
                    WebRootPath = Path.Combine(RepositoryRoot(), "SpeechMessageProducts.GoodShepherd2.Web/wwwroot")
                });
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Logging.ClearProviders();
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GoodShepherd2:DevEnrollment:Enabled"] = enabled,
                    ["CrmConnection:ServerUrl"] = "https://goodshepherd20.example/XRMServices/2011/Organization.svc",
                    ["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20",
                    ["Liff:LoginChannelId"] = "2012805181", ["Liff:MessagingChannelKey"] = "2012804957", ["Liff:LiffId"] = "synthetic-liff"
                });
                var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
                manager.Setup(m => m.Acquire(It.IsAny<CancellationToken>())).Returns(() =>
                {
                    Interlocked.Increment(ref f.Active);
                    var lease = new Mock<IClientLease>(MockBehavior.Strict);
                    lease.SetupGet(l => l.Service).Returns(f.Database.Service.Object);
                    lease.Setup(l => l.MarkFaulted()).Callback(() => Interlocked.Increment(ref f.Faulted));
                    lease.Setup(l => l.Dispose()).Callback(() =>
                    {
                        Interlocked.Increment(ref f.Released);
                        Interlocked.Decrement(ref f.Active);
                        if (f.Database.Fault == "dispose") throw new IOException("Synthetic release failure.");
                    });
                    return lease.Object;
                });
                builder.Services.AddSingleton(manager.Object);
                var clock = new Mock<IChurchClock>(MockBehavior.Strict);
                clock.SetupGet(c => c.UtcNow).Returns(() => f.UtcNow);
                builder.Services.AddSingleton(clock.Object);
                builder.Services.AddSingleton<ICurrentTenantSelectionStore>(Mock.Of<ICurrentTenantSelectionStore>());
                builder.Services.AddSingleton(_ => new ExceptionDiagnostics(Path.Combine(f.DirectoryPath, "Logs"),
                    outputOptions: new ExceptionOutputOptions(true, false)));
                f.verificationClient = new HttpClient(f.Verifier);
                builder.Services.AddSingleton(new LiffIdTokenVerifier(f.verificationClient, clock.Object));
                if (registerFirst) Register(builder);
                if (environment == "Development") GoodShepherd2LiffHosting.AddGoodShepherd2Liff(builder);
                if (!registerFirst) Register(builder);
                f.app = builder.Build();
                await f.app.StartAsync();
                var address = Assert.Single(f.app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
                f.client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        /// <summary>尋找產品唯一註冊入口；缺實作時 RED 直接指出缺少產品類型。</summary>
        private static void Register(WebApplicationBuilder builder)
        {
            var type = typeof(Program).Assembly.GetType("SpeechMessageProducts.GoodShepherd2.Web.Liff.DevEnrollmentEndpoint");
            Assert.NotNull(type);
            var method = type.GetMethod("AddGoodShepherd2DevEnrollment", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(null, [builder]);
        }

        /// <summary>每筆請求獨有 Bearer 與本文，不把身分放 HttpClient 共用狀態。</summary>
        internal async Task<HttpResponseMessage> PostAsync(string? body = null, string contentType = "application/json", bool bearer = true, bool chunked = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/liff/dev/enroll")
            {
                Content = new StringContent(body ?? JsonSerializer.Serialize(new { token = Token }), Encoding.UTF8, contentType)
            };
            if (bearer) request.Headers.Authorization = new("Bearer", "synthetic-id-token");
            if (chunked) request.Headers.TransferEncodingChunked = true;
            return await client!.SendAsync(request);
        }

        /// <summary>取消排空案例直接等待正式 handler，避免 HttpClient 提早結束連線掩蓋伺服器尚在 SDK 中；scope 與 stream 全由本次擁有。</summary>
        internal async Task InvokeHandlerAsync(CancellationToken cancellationToken)
        {
            await using var scope = app!.Services.CreateAsyncScope();
            using var body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { token = Token })));
            using var output = new MemoryStream();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = cancellationToken };
            context.Request.ContentType = "application/json";
            context.Request.Headers.Authorization = "Bearer synthetic-id-token";
            context.Request.Body = body;
            context.Response.Body = output;
            var type = typeof(Program).Assembly.GetType("SpeechMessageProducts.GoodShepherd2.Web.Liff.DevEnrollmentEndpoint")!;
            var method = type.GetMethod("HandleAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(null, [context])!;
        }

        /// <summary>先停止排空 HTTP，再關閉主機及 client，最後釋放屏障與本案暫存證據。</summary>
        public async ValueTask DisposeAsync()
        {
            try { if (app is not null) { try { await app.StopAsync(); } finally { await app.DisposeAsync(); } } }
            finally
            {
                client?.Dispose(); verificationClient?.Dispose(); Database?.ReadBarrier?.Dispose();
                if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }

    /// <summary>只在本案鎖內提交 detached 交易候選；故障時不發布，不能取代 D365 實機交易驗證。</summary>
    private sealed class FakeDatabase
    {
        /// <summary>合成 SDK 唯一入口；未宣告的非交易 Create／Update 會由 Strict mock 拒絕。</summary>
        internal Mock<IOrganizationService> Service { get; } = new(MockBehavior.Strict);
        /// <summary>本案唯一邀請列，其 joined 欄位逐一受產品驗證。</summary>
        internal Entity Invite { get; } = new("new_testinvite", Guid.NewGuid()) { RowVersion = "7" };
        /// <summary>只同步本案資料庫提交，不假裝應用程式鎖提供實機唯一性。</summary>
        private readonly object gate = new();
        /// <summary>兩個讀者於 CAS 前會合；fixture 最後 Dispose。</summary>
        internal Barrier? ReadBarrier;
        /// <summary>合成同步 SDK 已取得候選後的受控故障／取消閘門，不建立背景工作。</summary>
        internal Action? BeforeReadReturn;
        /// <summary>故障注入名稱，只是合成案例選擇。</summary>
        internal string? Fault;
        /// <summary>重複正式 ID 注入。</summary>
        internal bool Duplicate;
        /// <summary>不完整分頁注入。</summary>
        internal bool MoreRecords;
        /// <summary>提交成功的 Contact 計數。</summary>
        internal int Contacts;
        /// <summary>提交成功的角色計數。</summary>
        internal int Assignments;
        /// <summary>所有交易嘗試計數，用以確認不會自動重試。</summary>
        internal int Transactions;

        /// <summary>建立完整 joined 合成列；數值另由 schema 標籤案例獨立核對。</summary>
        internal FakeDatabase(string token)
        {
            var tenant = Guid.NewGuid(); var role = Guid.NewGuid(); var slot = Guid.NewGuid(); var channel = Guid.NewGuid();
            Invite["new_tokenhash"] = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=")));
            Invite["new_tenant"] = new EntityReference("account", tenant);
            Invite["new_role"] = new EntityReference("new_role", role);
            Invite["new_expireson"] = DateTime.UtcNow.AddMinutes(20);
            Invite["createdon"] = DateTime.UtcNow;
            Alias("tenant", "account", "accountid", tenant);
            Alias("tenant", "account", "statecode", new OptionSetValue(0));
            Alias("tenant", "account", "new_istenant", new OptionSetValue(100000001));
            Alias("tenant", "account", "new_istesttenant", new OptionSetValue(100000001));
            Alias("tenant", "account", "new_lifecyclestatus", new OptionSetValue(100000002));
            Alias("tenant", "account", "new_tenantcode", "synthetic-c201");
            Alias("tenant", "account", "new_channelslot", new EntityReference("new_channelslot", slot));
            Alias("slot", "new_channelslot", "new_channelslotid", slot);
            Alias("slot", "new_channelslot", "new_tenant", new EntityReference("account", tenant));
            Alias("slot", "new_channelslot", "new_channel", new EntityReference("new_linechannel", channel));
            Alias("channel", "new_linechannel", "new_linechannelid", channel);
            Alias("channel", "new_linechannel", "new_channelid", "2012804957");
            Alias("channel", "new_linechannel", "new_product", "GoodShepherd2");
            Alias("role", "new_role", "new_roleid", role);
            Alias("role", "new_role", "new_tenantid", new EntityReference("account", tenant));
            Alias("role", "new_role", "new_code", "member");
            Service.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.Equal("new_testinvite", expression.EntityName);
                Assert.Equal(2, expression.TopCount);
                Assert.Equal("new_tokenhash", Assert.Single(expression.Criteria.Conditions).AttributeName);
                Assert.DoesNotContain(token, expression.Criteria.Conditions[0].Values.Cast<string>());
                Entity row;
                lock (gate) { row = new Entity(Invite.LogicalName, Invite.Id) { RowVersion = Invite.RowVersion }; row.Attributes.AddRange(Invite.Attributes); }
                BeforeReadReturn?.Invoke();
                if (ReadBarrier is not null) Assert.True(ReadBarrier.SignalAndWait(TimeSpan.FromSeconds(8)));
                return new EntityCollection(Duplicate ? [row, row] : [row]) { MoreRecords = MoreRecords };
            });
            Service.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) => Execute(request));
        }

        /// <summary>只替換明確來源的合成別名，不以顯示名稱識別資料。</summary>
        internal void Alias(string alias, string entity, string field, object value) => Invite[alias + "." + field] = new AliasedValue(entity, field, value);

        /// <summary>檢查真實交易形狀與順序；先驗 CAS，再候選兩個 Create，全部成功才在鎖內發布。</summary>
        private ExecuteTransactionResponse Execute(OrganizationRequest request)
        {
            var transaction = Assert.IsType<ExecuteTransactionRequest>(request);
            Assert.Equal(3, transaction.Requests.Count);
            var update = Assert.IsType<UpdateRequest>(transaction.Requests[0]);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
            Assert.Equal(Invite.Id, update.Target.Id);
            Assert.Equal("new_testinvite", update.Target.LogicalName);
            var used = Assert.IsType<DateTime>(update.Target["new_usedon"]);
            Assert.Equal(DateTimeKind.Utc, used.Kind);
            var contact = Assert.IsType<CreateRequest>(transaction.Requests[1]).Target;
            var assignment = Assert.IsType<CreateRequest>(transaction.Requests[2]).Target;
            Assert.Equal("contact", contact.LogicalName);
            Assert.Equal("new_roleassignment", assignment.LogicalName);
            Assert.NotEqual(Guid.Empty, contact.Id);
            Assert.NotEqual(Guid.Empty, assignment.Id);
            Assert.Equal("synthetic-line-sub", contact["new_lineid"]);
            Assert.Equal(((EntityReference)Invite["new_tenant"]).Id, ((EntityReference)contact["parentcustomerid"]).Id);
            Assert.Equal(contact.Id, ((EntityReference)assignment["new_contactid"]).Id);
            Assert.Equal(((EntityReference)Invite["new_role"]).Id, ((EntityReference)assignment["new_roleid"]).Id);
            lock (gate)
            {
                Transactions++;
                if (Invite.RowVersion != update.Target.RowVersion)
                    throw new FaultException<OrganizationServiceFault>(new ExecuteTransactionFault
                    { FaultedRequestIndex = 0, ErrorCode = -2147088254 });
                if (Fault is "contact" or "assignment") throw new IOException("Synthetic transaction failure.");
                Contacts++; Assignments++; Invite["new_usedon"] = used; Invite.RowVersion = "8";
                if (Fault == "unknown") throw new IOException("Synthetic unknown commit outcome.");
                return new ExecuteTransactionResponse();
            }
        }
    }

    /// <summary>合成官方驗證 transport；核對預期 Login Channel 與固定官方 URL，無任何真實 HTTP。</summary>
    private sealed class VerificationHandler : HttpMessageHandler
    {
        /// <summary>本案可設定的預期拒絕狀態。</summary>
        internal HttpStatusCode Status = HttpStatusCode.OK;
        /// <summary>已驗證操作數，不保留 token 或使用者欄位。</summary>
        internal int Calls;
        /// <summary>每次只回固定合成 sub；真實 verifier 負責處置 request、response 與 timeout。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://api.line.me/oauth2/v2.1/verify", request.RequestUri!.AbsoluteUri);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("client_id=2012805181", body, StringComparison.Ordinal);
            Interlocked.Increment(ref Calls);
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    aud = "2012805181", sub = "synthetic-line-sub", exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
