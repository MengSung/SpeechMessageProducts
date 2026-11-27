/*
檔案整體說明
目的與定位：保護 F3 主機接線的實際 HTTP 授權契約，以真 Kestrel、正式 HMAC adapter 與目錄驗證路由不能跨 Channel。
主要功能與流程：每案建立獨立空白 builder、合成來源與六十四 bytes key；測試端獨立計算 HMAC，檢查有效空事件、錯頻道、未知頻道及原始 UTF-8 本文。可成功接收的去重與 queue 替身提供合法控制，避免永遠拒絕造成假綠；全程不連線 D365、LINE 或 SQL，也不載入 User Secrets。
使用邊界與維護：fixture 唯一擁有 Host、loopback client、合成來源及暫存根；所有 HTTP 訊息確定釋放，最後先 Stop 再 DisposeAsync 並刪除自身暫存目錄。來源 lease 逐次計數並在 DisposeAsync 歸零，不保存使用者或真憑證。相同程序的診斷 owner 由既有 F2Host collection 序列化，不把本機離線結果當成實機證據。
*/
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Signatures;
using LineMessagingProcessor.Core.ChannelPool;
using LineMessagingProcessor.Core.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SpeechMessage.Scheduling;
using SpeechMessage.Tenancy;
using SpeechMessageProducts.GoodShepherd2.Web.Channels;
using SpeechMessageProducts.GoodShepherd2.Web.Startup;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Dataverse;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Channels;

/// <summary>透過真正固定路徑測試 F3 接線；合成來源可授權兩個不同 Channel，queue 也具成功路徑。</summary>
[Collection("F2Host")]
public sealed class ChannelHostingTests
{
    /// <summary>有效空事件是 LINE Verify 的合法控制；改成空 verifier 或遺留拒絕插槽必使本案失敗。</summary>
    [Fact]
    public async Task Channels_ValidEmptyEvents_Returns200()
    {
        await using var host = new HostFixture();
        await host.StartAsync();
        Assert.Equal(HttpStatusCode.OK, await host.SendAsync("1234567890", "{\"events\":[]}", HostFixture.SecretOne));
        Assert.Equal(0, host.Pipeline.Claims);
        Assert.Equal(0, host.Pipeline.Enqueued);
        Assert.True(host.Source.Reads > 0);
    }

    /// <summary>T01 secret 不能授權 T02 路徑；同一測試再以 T02 secret 成功，證明拒絕不是全面假拒絕。</summary>
    [Fact]
    public async Task Channels_Resolver_WrongSecret_SignatureFails()
    {
        await using var host = new HostFixture();
        await host.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await host.SendAsync("2234567890", "{\"events\":[]}", HostFixture.SecretOne));
        Assert.Equal(0, host.Pipeline.Claims);
        Assert.Equal(0, host.Pipeline.Enqueued);
        Assert.Equal(HttpStatusCode.OK, await host.SendAsync("2234567890", "{\"events\":[]}", HostFixture.SecretTwo));
    }

    /// <summary>空白、換行、中文字與 Unicode escape 都是原始簽章的一部分；只簽語意等價 JSON 應拒絕。</summary>
    [Fact]
    public async Task Channels_RawUtf8Bytes_AreVerifiedWithoutNormalization()
    {
        await using var host = new HostFixture();
        await host.StartAsync();
        const string body = "{\r\n  \"note\":\"合成資料\\u4e00\", \"events\" : []\r\n}";
        Assert.Equal(HttpStatusCode.OK, await host.SendAsync("1234567890", body, HostFixture.SecretOne));
        Assert.Equal(HttpStatusCode.Unauthorized, await host.SendAsync("1234567890", body,
            HostFixture.SecretOne, "{\"note\":\"合成資料一\",\"events\":[]}"));
    }

    /// <summary>合法事件實際通過去重與交接；若接線無條件回 200 或替身永不接受，本案必須失敗。</summary>
    [Fact]
    public async Task Channels_ValidEvent_ClaimsAndEnqueuesOnce()
    {
        await using var host = new HostFixture();
        await host.StartAsync();
        const string body = "{\"events\":[{\"webhookEventId\":\"synthetic-event-001\",\"type\":\"follow\",\"timestamp\":1,\"source\":{\"type\":\"user\",\"userId\":\"synthetic-user\"},\"deliveryContext\":{\"isRedelivery\":false}}]}";
        Assert.Equal(HttpStatusCode.OK, await host.SendAsync("1234567890", body, HostFixture.SecretOne));
        Assert.Equal(1, host.Pipeline.Claims);
        Assert.Equal(1, host.Pipeline.Enqueued);
        Assert.Equal(1, host.Pipeline.Committed);
        Assert.Equal(1, host.Pipeline.Released);
    }

    /// <summary>陌生 Channel 與錯 secret 都必須在去重前拒絕，不可回到原本的無條件 200 stub。</summary>
    /// <param name="channel">合成路由鍵。</param>
    /// <param name="secret">故意錯誤或不屬該路由的合成 secret。</param>
    [Theory]
    [InlineData("1234567890", "synthetic-wrong-channel-secret")]
    [InlineData("9999999999", HostFixture.SecretOne)]
    public async Task Channels_InvalidOrUnknown_Returns401WithoutQueue(string channel, string secret)
    {
        await using var host = new HostFixture();
        await host.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await host.SendAsync(channel, "{\"events\":[]}", secret));
        Assert.Equal(0, host.Pipeline.Claims);
        Assert.Equal(0, host.Pipeline.Enqueued);
    }

    /// <summary>只保留可拒絕路徑；缺少整對設定不能解析來源或 SDK，即使先前註冊了假允許 verifier。</summary>
    [Fact]
    public async Task Channels_Unconfigured_RejectsWithoutOpeningSource()
    {
        await using var host = new HostFixture(configured: false);
        await host.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await host.SendAsync("1234567890", "{\"events\":[]}", HostFixture.SecretOne));
        Assert.Equal(0, host.Source.Reads);
        Assert.Equal(0, host.Pipeline.Claims);
    }

    /// <summary>部分、巢狀、未知或非標準設定都在註冊前拒絕；不可悄悄關閉功能或用他產品設定後備。</summary>
    /// <param name="fault">故意破壞的部署輸入，所有值都是本測試建立的合成資料。</param>
    [Theory]
    [InlineData("missing-key")]
    [InlineData("missing-organization")]
    [InlineData("empty-key")]
    [InlineData("invalid-base64")]
    [InlineData("short-key")]
    [InlineData("long-key")]
    [InlineData("whitespace-key")]
    [InlineData("empty-organization")]
    [InlineData("invalid-organization")]
    [InlineData("scheduling-mismatch")]
    [InlineData("scheduling-nested")]
    [InlineData("unknown-channel-setting")]
    [InlineData("key-nested")]
    [InlineData("legacy")]
    [InlineData("config-channel")]
    public async Task Channels_InvalidConfiguration_FailsBeforeRegistrationOrConnection(string fault)
    {
        await using var host = new HostFixture();
        var configuration = host.Builder.Configuration;
        switch (fault)
        {
            case "missing-key": configuration["LineChannels:TokenKey"] = null; break;
            case "missing-organization": configuration["Channels:OrganizationId"] = null; break;
            case "empty-key": configuration["LineChannels:TokenKey"] = ""; break;
            case "invalid-base64": configuration["LineChannels:TokenKey"] = "synthetic-invalid"; break;
            case "short-key": configuration["LineChannels:TokenKey"] = Convert.ToBase64String(new byte[63]); break;
            case "long-key": configuration["LineChannels:TokenKey"] = Convert.ToBase64String(new byte[65]); break;
            case "whitespace-key": configuration["LineChannels:TokenKey"] += "\r\n"; break;
            case "empty-organization": configuration["Channels:OrganizationId"] = Guid.Empty.ToString("D"); break;
            case "invalid-organization": configuration["Channels:OrganizationId"] = "synthetic-invalid"; break;
            case "scheduling-mismatch": configuration["Scheduling:OrganizationId"] = Guid.NewGuid().ToString("D"); break;
            case "scheduling-nested": configuration["Scheduling:OrganizationId:Other"] = "synthetic"; break;
            case "unknown-channel-setting": configuration["Channels:Product"] = "GoodButler"; break;
            case "key-nested": configuration["LineChannels:TokenKey:Other"] = "synthetic"; break;
            case "legacy": configuration["LineWebhook:EnableLegacyCallback"] = "true"; break;
            case "config-channel": configuration["LineWebhook:Channels:0:ChannelId"] = "1234567890"; break;
        }
        var count = host.Builder.Services.Count;
        Assert.Throws<InvalidOperationException>(() => GoodShepherd2ChannelHosting.AddGoodShepherd2Channels(host.Builder));
        Assert.Equal(count, host.Builder.Services.Count);
        Assert.Equal(0, host.Source.Reads);
    }

    /// <summary>非單一數字 segment 必須在 body／resolver 前拒絕；保護路由不被另一個工作包的 endpoint 接走。</summary>
    /// <param name="path">故意不符固定路徑契約的 URI。</param>
    [Theory]
    [InlineData("/webhook")]
    [InlineData("/webhook/")]
    [InlineData("/webhook/1234567890/")]
    [InlineData("/webhook/1234567890/another")]
    [InlineData("/webhook/abc")]
    [InlineData("/webhook/123456789012345678901")]
    [InlineData("/webhook/123%2F456")]
    public async Task Channels_InvalidRoute_RefusesWithoutDirectoryRead(string path)
    {
        await using var host = new HostFixture();
        await host.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.Source.Reads);
    }

    /// <summary>有效路徑只接受 POST；GET 不能碰目錄，其他模組路徑仍交還原管線。</summary>
    [Fact]
    public async Task Channels_OnlyPost_AndUnrelatedRouteContinues()
    {
        await using var host = new HostFixture();
        await host.StartAsync();
        using var get = await host.Client.GetAsync(new Uri("/webhook/1234567890", UriKind.Relative));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Contains("POST", get.Content.Headers.Allow);
        using var other = await host.Client.GetAsync(new Uri("/unrelated", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);
        Assert.Equal(0, host.Source.Reads);
    }

    /// <summary>真實 resolver 與 verifier 必須唯一；即使先放入允許型替身亦被移除，不能殘留任何 fallback。</summary>
    [Fact]
    public async Task Channels_ReplacesInactiveAndForeignResolvers_UsesSingleDirectory()
    {
        await using var host = new HostFixture();
        var permissive = new Mock<IWebhookSignatureVerifier>();
        permissive.Setup(value => value.VerifyAsync(It.IsAny<string>(), It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult(true));
        host.Builder.Services.AddSingleton(permissive.Object);
        host.Builder.Services.AddSingleton(new Mock<IWebhookChannelSecretResolver>().Object);
        await host.StartAsync();
        var directory = host.Services.GetRequiredService<DataverseChannelDirectory>();
        Assert.Same(directory, Assert.Single(host.Services.GetServices<IWebhookChannelSecretResolver>()));
        Assert.Same(directory, Assert.Single(host.Services.GetServices<IChannelCredentialSource>()));
        Assert.IsType<LineWebhookSignatureVerifier>(Assert.Single(host.Services.GetServices<IWebhookSignatureVerifier>()));
        Assert.NotNull(host.Services.GetRequiredService<ChannelHealthChecker>());
        Assert.NotNull(host.Services.GetRequiredService<ChannelCredentialTransport>());
        Assert.Equal(HttpStatusCode.Unauthorized, await host.SendAsync("1234567890", "{\"events\":[]}", "synthetic-wrong-secret"));
    }

    /// <summary>設定只在 Add 時快照；後續改寫組態不能改變驗簽 key 或組織，也不能附帶啟用排程。</summary>
    [Fact]
    public async Task Channels_ConfigurationSnapshot_AndMatchingSchedulingDoesNotEnableJobs()
    {
        await using var host = new HostFixture();
        host.Builder.Configuration["Scheduling:OrganizationId"] = HostFixture.Organization.ToString("D");
        await host.StartAsync(() =>
        {
            host.Builder.Configuration["LineChannels:TokenKey"] = "synthetic-invalid-after-registration";
            host.Builder.Configuration["Channels:OrganizationId"] = Guid.NewGuid().ToString("D");
        });
        Assert.Null(host.Services.GetService<IJobScheduler>());
        Assert.Equal(HttpStatusCode.OK, await host.SendAsync("1234567890", "{\"events\":[]}", HostFixture.SecretOne));
    }

    /// <summary>三十二個真 HTTP 操作交錯使用兩個 secret；各自正簽成功、跨簽拒絕，所有 lease 最終回零。</summary>
    [Fact]
    public async Task Channels_ThirtyTwoConcurrentRequests_AreIsolatedAndDrained()
    {
        await using var host = new HostFixture();
        await host.StartAsync();
        var statuses = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => host.SendAsync(
            index % 2 == 0 ? "1234567890" : "2234567890", "{\"events\":[]}",
            index % 4 == 0 || index % 4 == 3 ? HostFixture.SecretOne : HostFixture.SecretTwo)));
        for (var index = 0; index < statuses.Length; index++)
            Assert.Equal(index % 4 < 2 ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, statuses[index]);
        Assert.Equal(0, host.Source.Active);
        Assert.Equal(0, host.Pipeline.Claims);
    }

    /// <summary>重複 Host 啟停後舊目錄必須已拒絕入場且擁有 key 全零；新 Host 可重新通過真驗簽。</summary>
    [Fact]
    public async Task Channels_StopClearsKey_AndRestartHasNoRetainedOwner()
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            await using var host = new HostFixture();
            await host.StartAsync();
            var directory = host.Services.GetRequiredService<DataverseChannelDirectory>();
            Assert.Equal(HttpStatusCode.OK, await host.SendAsync("1234567890", "{\"events\":[]}", HostFixture.SecretOne));
            var key = Assert.IsType<byte[]>(typeof(DataverseChannelDirectory).GetField("key", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(directory));
            Assert.Contains(key, value => value != 0);
            await host.StopAsync();
            Assert.All(key, value => Assert.Equal((byte)0, value));
            Assert.Throws<ObjectDisposedException>(() => directory.TryResolveSecret("1234567890", out _));
            Assert.Equal(0, host.Source.Active);
        }
    }

    /// <summary>真正 SDK adapter 的主機預設必須使用固定產品查詢及 manager lease，不能只有測試 helper 可運作。</summary>
    [Fact]
    public async Task Channels_DefaultSdkAdapter_UsesFixedProductAndOneOperationLease()
    {
        await using var host = new HostFixture();
        var sdk = new Mock<IOrganizationServiceAsync2>(MockBehavior.Strict);
        sdk.Setup(value => value.ExecuteAsync(It.IsAny<WhoAmIRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhoAmIResponse { Results = new ParameterCollection { ["OrganizationId"] = HostFixture.Organization } });
        var foreignProbes = 0;
        sdk.Setup(value => value.RetrieveMultipleAsync(It.IsAny<QueryBase>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryBase query, CancellationToken _) =>
            {
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.Equal("new_linechannel", expression.EntityName);
                if (expression.TopCount == 1)
                {
                    // 同一已驗證租約只觀察外產品標記；不能把偵測查詢當成讀取外產品憑證的授權。
                    Assert.False(expression.ColumnSet.AllColumns);
                    Assert.Equal("new_product", Assert.Single(expression.ColumnSet.Columns));
                    Assert.Equal(LogicalOperator.And, expression.Criteria.FilterOperator);
                    Assert.Equal(3, expression.Criteria.Conditions.Count);
                    Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "new_product" &&
                        condition.Operator == ConditionOperator.NotNull && condition.Values.Count == 0);
                    Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "new_product" &&
                        condition.Operator == ConditionOperator.NotEqual && Equals(Assert.Single(condition.Values), "GoodShepherd2"));
                    Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "new_product" &&
                        condition.Operator == ConditionOperator.NotEqual && Equals(Assert.Single(condition.Values), ""));
                    Interlocked.Increment(ref foreignProbes);
                    return new EntityCollection();
                }
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "new_product" &&
                    condition.Operator == ConditionOperator.Equal && condition.Values.Count == 1 && Equals(condition.Values[0], "GoodShepherd2"));
                if (expression.ColumnSet.Columns.Contains("new_accesstokenencrypted"))
                {
                    var id = Assert.Single(expression.Criteria.Conditions.Where(condition => condition.AttributeName == "new_linechannelid")).Values[0];
                    var row = Assert.Single(host.Source.Rows.Where(value => value.Id == (Guid)id));
                    return new EntityCollection([new Entity("new_linechannel", row.Id)
                    {
                        ["new_product"] = "GoodShepherd2", ["new_accesstokenencrypted"] = row.TokenEncrypted,
                        ["new_channelsecretencrypted"] = row.SecretEncrypted
                    }]);
                }
                return new EntityCollection(host.Source.Rows.Select(row => new Entity("new_linechannel", row.Id)
                {
                    ["new_linechannelid"] = row.Id, ["new_product"] = "GoodShepherd2", ["new_channelid"] = row.ChannelKey,
                    ["new_channelstatus"] = new OptionSetValue(100000001), ["modifiedon"] = row.ModifiedOn, ["versionnumber"] = 1L
                }).ToList());
            });
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        lease.SetupGet(value => value.Service).Returns(sdk.Object);
        lease.Setup(value => value.Dispose());
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        host.Builder.Services.RemoveAll<IChannelDirectorySource>();
        host.Builder.Services.RemoveAll<IDataverseConnectionManager>();
        host.Builder.Services.AddSingleton(manager.Object);
        await host.StartAsync();
        var source = Assert.IsType<DataverseChannelDirectorySource>(host.Services.GetRequiredService<IChannelDirectorySource>());
        Assert.Equal("GoodShepherd2", source.Product);
        Assert.Equal(HostFixture.Organization, source.OrganizationId);
        manager.Verify(value => value.Acquire(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(HttpStatusCode.OK, await host.SendAsync("1234567890", "{\"events\":[]}", HostFixture.SecretOne));
        manager.Verify(value => value.Acquire(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, foreignProbes);
        lease.Verify(value => value.Dispose(), Times.Once);
        lease.Verify(value => value.MarkFaulted(), Times.Never);
    }

    /// <summary>停止時卡住的來源先收到取消；故意延後 lease 歸還，證明 Host 不能提前關 diagnostics 或完成 Stop。</summary>
    [Fact]
    public async Task Channels_StopDuringResolve_DrainsBeforeOtherOwnersStop()
    {
        await using var host = new HostFixture();
        host.Source.BlockNextRead = true;
        host.Builder.Services.Configure<HostOptions>(options => options.ServicesStopConcurrently = true);
        var probe = new StopProbe();
        host.Builder.Services.AddSingleton<IHostedService>(probe);
        await host.StartAsync();
        var directory = host.Services.GetRequiredService<DataverseChannelDirectory>();
        var key = Assert.IsType<byte[]>(typeof(DataverseChannelDirectory).GetField("key", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(directory));
        probe.OnStop = () =>
        {
            Assert.Equal(0, host.Source.Active);
            Assert.All(key, value => Assert.Equal((byte)0, value));
            Assert.Throws<ObjectDisposedException>(() => directory.TryResolveSecret("1234567890", out _));
        };
        var request = host.SendAsync("1234567890", "{\"events\":[]}", HostFixture.SecretOne);
        Task? stopping = null;
        try
        {
            await host.Source.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stopping = host.StopAsync();
            await host.Source.ReadCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(stopping.IsCompleted);
            Assert.False(probe.Stopped);
            Assert.Equal(1, host.Source.Active);
            Assert.Contains(key, value => value != 0);
        }
        finally
        {
            host.Source.AllowDrain.TrySetResult();
            if (stopping is not null) await stopping.WaitAsync(TimeSpan.FromSeconds(15));
            var status = await request.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        }
        Assert.True(probe.Stopped);
        Assert.Equal(0, host.Source.Active);
    }

    /// <summary>未啟動的主機若已解析目錄，直接非同步 Dispose 仍必須關 timer、清 key，不依賴 Stop 事件。</summary>
    [Fact]
    public async Task Channels_DisposeWithoutStart_ClearsDirectoryKey()
    {
        await using var host = new HostFixture();
        host.Build();
        var directory = host.Services.GetRequiredService<DataverseChannelDirectory>();
        var key = Assert.IsType<byte[]>(typeof(DataverseChannelDirectory).GetField("key", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(directory));
        host.SkipStop = true;
        await host.DisposeAsync();
        Assert.All(key, value => Assert.Equal((byte)0, value));
        Assert.Throws<ObjectDisposedException>(() => directory.TryResolveSecret("1234567890", out _));
        Assert.Equal(0, host.Source.Reads);
    }

    /// <summary>來源真故障不得回落舊憑證；修復後能以全新候選驗簽成功，來源租約均確定釋放。</summary>
    [Fact]
    public async Task Channels_SourceFailure_RejectsThenCanRetry()
    {
        await using var host = new HostFixture();
        await host.StartAsync();
        Assert.Equal(HttpStatusCode.OK, await host.SendAsync("1234567890", "{\"events\":[]}", HostFixture.SecretOne));
        host.Source.NextFailure = new IOException("synthetic-channel-source-failure");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await host.SendAsync("1234567890", "{\"events\":[]}", HostFixture.SecretOne));
        Assert.Equal(0, host.Source.Active);
        Assert.Equal(HttpStatusCode.OK, await host.SendAsync("1234567890", "{\"events\":[]}", HostFixture.SecretOne));
    }

    /// <summary>在讀 body 前限制最多六十四個在途 request；超額直接回 503，不增加 body buffer 或同步 SDK 等待者。</summary>
    [Fact]
    public async Task Channels_RequestAdmission_IsBoundedBeforeReadingBody()
    {
        await using var host = new HostFixture();
        await host.StartAsync();
        var application = new ApplicationBuilder(host.Services);
        new GoodShepherd2ChannelStartupFilter().Configure(_ => { })(application);
        var pipeline = application.Build();
        var bodies = Enumerable.Range(0, 64).Select(_ => new BlockingBody()).ToArray();
        var requests = bodies.Select(body => new DefaultHttpContext
        {
            RequestServices = host.Services, Request = { Method = "POST", Path = "/webhook/1234567890", Body = body }
        }).ToArray();
        var operations = requests.Select(context => pipeline(context)).ToArray();
        try
        {
            await Task.WhenAll(bodies.Select(body => body.Entered.Task)).WaitAsync(TimeSpan.FromSeconds(10));
            var overflow = new DefaultHttpContext
            {
                RequestServices = host.Services, Request = { Method = "POST", Path = "/webhook/1234567890", Body = Stream.Null }
            };
            await pipeline(overflow);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, overflow.Response.StatusCode);
            Assert.Equal(0, host.Source.Reads);
        }
        finally
        {
            foreach (var body in bodies) body.Release.TrySetResult();
            await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var body in bodies) await body.DisposeAsync();
        }
        Assert.All(requests, context => Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode));
        var afterDrain = new DefaultHttpContext
        {
            RequestServices = host.Services, Request = { Method = "POST", Path = "/webhook/1234567890", Body = Stream.Null }
        };
        await pipeline(afterDrain);
        Assert.Equal(StatusCodes.Status401Unauthorized, afterDrain.Response.StatusCode);
    }

    /// <summary>獨立 Kestrel fixture；先註冊安全合成依賴，再套用與 Program 相同的 Foundation 及 F3 接點。</summary>
    private sealed class HostFixture : IAsyncDisposable
    {
        internal const string SecretOne = "synthetic-T01-channel-secret";
        internal const string SecretTwo = "synthetic-T02-channel-secret";
        internal static readonly Guid Organization = new("40111111-1111-4111-8111-111111111111");
        private readonly string root = Path.Combine(Path.GetTempPath(), "f3-channel-host-" + Guid.NewGuid().ToString("N"));
        private WebApplication? app;
        private HttpClient? client;
        private int disposed;
        /// <summary>僅生命週期故障注入案略過 Stop，以直接測試容器 Dispose 的清理路徑。</summary>
        internal bool SkipStop { get; set; }
        /// <summary>單案 builder 只含 in-memory 設定，不讀環境、檔案或秘密 provider。</summary>
        internal WebApplicationBuilder Builder { get; }
        /// <summary>合成來源活至 fixture／Host 清理，不持有 SDK。</summary>
        internal SyntheticSource Source { get; }
        /// <summary>成功控制亦會 commit 並釋放 claim，方便驗證錯誤簽章零副作用。</summary>
        internal SyntheticPipeline Pipeline { get; } = new();
        /// <summary>已啟動單案的 HTTP client，只連線自身 loopback 埠。</summary>
        internal HttpClient Client => client!;
        /// <summary>借用當次 Host 容器進行接線斷言，不建立第二個 provider。</summary>
        internal IServiceProvider Services => app!.Services;

        /// <summary>建立兩筆真正加密的合成 Channel；原始 key 只在本建構式持有且在 finally 清零。</summary>
        internal HostFixture(bool configured = true)
        {
            Directory.CreateDirectory(root);
            Builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development, ContentRootPath = root,
                ApplicationName = typeof(ChannelHostingTests).Assembly.FullName
            });
            Builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var key = Enumerable.Range(1, 64).Select(value => (byte)value).ToArray();
            try
            {
                Source = new SyntheticSource(key);
                Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExceptionNotifications:WriteExceptionLog"] = "false",
                    ["ExceptionNotifications:SendLine"] = "false"
                });
                if (configured)
                {
                    Builder.Configuration["Channels:OrganizationId"] = Organization.ToString("D");
                    Builder.Configuration["LineChannels:TokenKey"] = Convert.ToBase64String(key);
                }
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            Builder.Services.AddSingleton<IChannelDirectorySource>(_ => Source);
            Builder.Services.AddSingleton<IWebhookEventDeduplicator>(Pipeline);
            Builder.Services.AddSingleton<IWebhookEventQueue>(Pipeline);
            var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
            Builder.Services.AddSingleton(manager.Object);
            Builder.Services.AddGoodShepherd2Foundation(Builder.Configuration, Builder.Environment, Builder.Logging);
        }

        /// <summary>在已獨立建立的 Foundation 上套用 F3，再啟動真正 loopback HTTP；不是測試 server 模擬。</summary>
        internal async Task StartAsync(Action? afterRegistration = null)
        {
            Build(afterRegistration);
            await app!.StartAsync();
            var address = Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
            client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>建立尚未 Start 的真主機，讓 Dispose 前未啟動的路徑亦可明確驗證。</summary>
        internal void Build(Action? afterRegistration = null)
        {
            GoodShepherd2ChannelHosting.AddGoodShepherd2Channels(Builder);
            afterRegistration?.Invoke();
            app = Builder.Build();
            app.MapGet("/unrelated", () => Microsoft.AspNetCore.Http.Results.NoContent());
        }

        /// <summary>讓測試在容器仍存活時檢查停止後目錄，清理本身仍由 fixture 的 finally 保證完成。</summary>
        internal Task StopAsync() => app!.StopAsync();

        /// <summary>測試端獨立計算原始 UTF-8 HMAC；可刻意以另一份 bytes 簽章，不呼叫被測 verifier 產生預期值。</summary>
        internal async Task<HttpStatusCode> SendAsync(string channel, string body, string secret, string? signedBody = null)
        {
            var secretBytes = Encoding.UTF8.GetBytes(secret);
            byte[] signature;
            try { signature = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(signedBody ?? body)); }
            finally { CryptographicOperations.ZeroMemory(secretBytes); }
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhook/" + channel, UriKind.Relative))
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            };
            request.Headers.Add("X-Line-Signature", Convert.ToBase64String(signature));
            CryptographicOperations.ZeroMemory(signature);
            using var response = await client!.SendAsync(request);
            return response.StatusCode;
        }

        /// <summary>無論啟動是否成功都依序釋放 client、停止 Host、非同步 Dispose；最後歸還組態及移除自身暫存資料。</summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            client?.Dispose();
            try
            {
                if (app is not null)
                {
                    try { if (!SkipStop) await app.StopAsync(); }
                    finally { await app.DisposeAsync(); }
                }
                else ((IDisposable)Builder.Configuration).Dispose();
            }
            finally
            {
                try { Source.Dispose(); }
                finally { Directory.Delete(root, recursive: true); }
            }
        }
    }

    /// <summary>固定 GoodShepherd2／合成組織的來源；只回離線正式 ID 與真正密文，計數 lease 生命週期以檢測未排空。</summary>
    private sealed class SyntheticSource : IChannelDirectorySource, IDisposable
    {
        private readonly ChannelDirectoryRecord[] rows;
        private int reads;
        private int active;
        /// <summary>故障注入只影響下一次讀取，不改變其後合法來源的授權行為。</summary>
        internal Exception? NextFailure;
        /// <summary>下一次讀取暫停直到 directory shutdown 取消，用來驗證取消後仍等待來源釋放。</summary>
        internal bool BlockNextRead { get; set; }
        /// <summary>三個純訊號由單案 owner 擁有，沒有 timer、背景工作或等待未釋放的 callback。</summary>
        internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReadCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource AllowDrain { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>供獨立 SDK stub 回應資料，都是固定合成資料，不能從此屬性繞過正式目錄解密。</summary>
        internal IReadOnlyList<ChannelDirectoryRecord> Rows => rows;
        /// <summary>每次讀取計數，不是用來決定授權的快取。</summary>
        internal int Reads => Volatile.Read(ref reads);
        /// <summary>在途 lease 數，釋放來源前必須回到零。</summary>
        internal int Active => Volatile.Read(ref active);
        /// <summary>明確的固定部署身分，不能由測試 HTTP 改寫。</summary>
        public Guid OrganizationId => HostFixture.Organization;
        /// <summary>精確產品字串，不使用路由做授權。</summary>
        public string Product => "GoodShepherd2";
        /// <summary>使用現有 protector 加密，不以替身解密繞過真實 key 契約。</summary>
        internal SyntheticSource(byte[] key)
        {
            rows = [Row("50111111-1111-4111-8111-111111111111", "1234567890", HostFixture.SecretOne, key),
                Row("50222222-2222-4222-8222-222222222222", "2234567890", HostFixture.SecretTwo, key)];
        }
        /// <summary>單次 operation 開立 lease，取消在入場前即拒絕，不建立背景工作。</summary>
        public ValueTask<IChannelDirectorySourceLease> OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref active);
            return ValueTask.FromResult<IChannelDirectorySourceLease>(new Lease(this));
        }
        /// <summary>來源唯一 owner 驗證排空，不處置任何借入 SDK；fixture 允許重入此無資源檢查。</summary>
        public void Dispose() => Assert.Equal(0, Active);
        /// <summary>正式唯一 ID、choice、數字版本與 UTC metadata 均獨立固定，避免依被測映射反推預期值。</summary>
        private static ChannelDirectoryRecord Row(string id, string channel, string secret, byte[] key) => new(
            Guid.Parse(id), "GoodShepherd2", channel, DirectoryChannelStatus.Operating, "1",
            new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc),
            ChannelTokenProtector.Protect("synthetic-channel-token-" + channel, key), ChannelTokenProtector.Protect(secret, key));

        /// <summary>只活在一個目錄操作內；讀取返回 detached record，Dispose 恰一次歸還 active。</summary>
        private sealed class Lease(SyntheticSource owner) : IChannelDirectorySourceLease
        {
            private int released;
            /// <summary>metadata 刻意去掉密文，完整讀取仍由正式 directory 解密。</summary>
            public async Task<IReadOnlyList<ChannelDirectoryRecord>> ReadAsync(bool includeCredentials, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref owner.reads);
                var failure = Interlocked.Exchange(ref owner.NextFailure, null);
                if (failure is not null) throw failure;
                if (owner.BlockNextRead)
                {
                    owner.BlockNextRead = false;
                    owner.ReadEntered.TrySetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException)
                    {
                        owner.ReadCancelled.TrySetResult();
                        // 故意不沿用已取消的操作 token，否則無法證明 owner 等待真正的來源歸還。
                        await owner.AllowDrain.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                        throw;
                    }
                }
                IReadOnlyList<ChannelDirectoryRecord> result = includeCredentials ? owner.rows : owner.rows.Select(row =>
                    new ChannelDirectoryRecord(row.Id, row.Product, row.ChannelKey, row.Status, row.Revision, row.ModifiedOn)).ToArray();
                return result;
            }
            /// <summary>本 HTTP 測試不執行健康寫回，若意外呼叫則失敗而非默認成功。</summary>
            public Task WriteHealthAsync(ChannelDirectoryRecord channel, ChannelHealthResult result, bool disable, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("Unexpected synthetic health write.");
            /// <summary>沒有非同步 I/O；重入不會把 active 扣成負數。</summary>
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref released, 1) == 0) Interlocked.Decrement(ref owner.active);
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>另一個借用依賴的關閉探針；在 Stop 觀察已排空，而不接管目錄或 diagnostics。</summary>
    private sealed class StopProbe : IHostedService
    {
        /// <summary>單案預先設定的斷言 callback，只活至 fixture 結束，沒有 request 參照。</summary>
        internal Action? OnStop { get; set; }
        /// <summary>確認即使並行停止服務，目錄 drain 完成前也尚未進入其他 Stop。</summary>
        internal bool Stopped { get; private set; }
        /// <summary>不建立常駐工作或資源。</summary>
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>執行關閉順序斷言後記錄訊號，不以取消略過必要檢查。</summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            OnStop?.Invoke();
            Stopped = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>只阻擋單次 body read 的合成串流；純訊號讓 admission 測試建立六十四個真正尚未返回的 await。</summary>
    private sealed class BlockingBody : MemoryStream
    {
        /// <summary>server 已開始讀 body 的訊號，證明 request 正持有入場容量。</summary>
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>由單案 finally 統一放行，不建立 timer 或無 owner 的背景 task。</summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>等待測試明確放行後回 EOF；caller 取消仍可結束等待，不保存借入 memory。</summary>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return 0;
        }
    }

    /// <summary>最小合法事件交接替身，只計數、不保存 envelope；成功控制與拒絕路徑共用同一實作。</summary>
    private sealed class SyntheticPipeline : IWebhookEventDeduplicator, IWebhookEventQueue
    {
        private int claims;
        private int enqueued;
        private int committed;
        private int released;
        internal int Claims => Volatile.Read(ref claims);
        internal int Enqueued => Volatile.Read(ref enqueued);
        internal int Committed => Volatile.Read(ref committed);
        internal int Released => Volatile.Read(ref released);
        /// <summary>每筆合法事件取得真正可 commit／釋放的單次 claim；不接觸 SQL 或保存身分。</summary>
        public ValueTask<IWebhookEventRegistration?> TryRegisterAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref claims);
            return ValueTask.FromResult<IWebhookEventRegistration?>(new Registration(this));
        }
        /// <summary>模擬取得容量後提交並接管事件，不保留 claim 或本文。</summary>
        public async ValueTask<bool> TryEnqueueAsync(WebhookEnvelope envelope, IWebhookEventRegistration registration, CancellationToken cancellationToken)
        {
            await registration.CommitAsync(cancellationToken);
            Interlocked.Increment(ref enqueued);
            return true;
        }
        /// <summary>單筆 claim 的唯一 endpoint owner，明確記錄 commit 與 release，沒有連線或背景資源。</summary>
        private sealed class Registration(SyntheticPipeline owner) : IWebhookEventRegistration
        {
            /// <summary>成功控制不能略過 commit；caller 取消直接中止。</summary>
            public ValueTask CommitAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref owner.committed);
                return ValueTask.CompletedTask;
            }
            /// <summary>確認接收器在所有路徑釋放自己的 claim，不保存事件參照。</summary>
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.released);
                return ValueTask.CompletedTask;
            }
        }
    }
}
