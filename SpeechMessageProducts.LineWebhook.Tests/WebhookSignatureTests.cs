/*
檔案整體說明

目的與定位：驗證 LINE Webhook 主機使用 Tenancy 的頻道 secret 解析正式驗簽，
重用 LineSignature，並以原始 request bytes 完成 HMAC，不把 route 直接當授權。

主要功能與流程：以合成 IConfiguration 建立真實產品 composition，使用固定 secret
產生 HMAC-SHA256 header，送入含尾端空白及 UTF-8 bytes 的原始內容，確認正確簽章
成功、錯誤簽章拒絕及跨頻道 mapping 不可互用。測試不讀實際秘密、不送 LINE，
不建立 background task、timer 或外部連線。

使用邊界與維護：設定根、ServiceProvider 及其所有服務由案例擁有並釋放；合成 secret
僅在測試方法範圍內使用，不能輸出到 diagnostics。這些測試只證明離線契約，不宣稱
LINE 平台、IIS 或公開 Webhook URL 的實機結果。
*/
using System.Security.Cryptography;
using System.Text;
using Line.Messaging;
using LineMessagingProcessor.AspNetCore.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>
/// 驗證產品 composition 下正式 Webhook verifier 的原始 bytes、頻道隔離與錯誤簽章契約。
/// 測試使用真 DI 與既有 LineSignature，不使用假的 HMAC 實作。
/// </summary>
public sealed class WebhookSignatureTests
{
    private const string AlphaSecret = "0123456789abcdef0123456789abcdef";
    private const string BetaSecret = "abcdef0123456789abcdef0123456789";

    /// <summary>正確 HMAC 必須以未重新序列化的 body bytes 成功驗簽。</summary>
    [Fact]
    public async Task Verify_UsesOriginalBodyBytes()
    {
        using var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);
        var verifier = provider.GetRequiredService<IWebhookSignatureVerifier>();
        var body = Encoding.UTF8.GetBytes("{\"events\":[]} \r\n");
        var signature = CreateSignature(body, AlphaSecret);

        var result = await verifier.VerifyAsync("123", body, signature, CancellationToken.None);

        Assert.True(result);
    }

    /// <summary>錯誤簽章必須拒絕，且不因 signature 格式錯誤而拋出秘密相關例外。</summary>
    [Fact]
    public async Task Verify_InvalidSignature_ReturnsFalse()
    {
        using var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);
        var verifier = provider.GetRequiredService<IWebhookSignatureVerifier>();
        var body = Encoding.UTF8.GetBytes("{}");

        var result = await verifier.VerifyAsync("123", body, CreateSignature(body, BetaSecret), CancellationToken.None);

        Assert.False(result);
    }

    /// <summary>頻道 A 的 signature 不得在頻道 B 驗證成功，保護 mapping 與 secret 的租戶隔離。</summary>
    [Fact]
    public async Task Verify_ChannelSecretCannotCrossChannels()
    {
        using var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);
        var verifier = provider.GetRequiredService<IWebhookSignatureVerifier>();
        var body = Encoding.UTF8.GetBytes("{\"events\":[{\"type\":\"follow\",\"webhookEventId\":\"one\"}]}");

        var result = await verifier.VerifyAsync("456", body, CreateSignature(body, AlphaSecret), CancellationToken.None);

        Assert.False(result);
    }

    /// <summary>原始內容只改一個位元組就必須失敗，不能以 JSON 等價或重新序列化代替真正驗簽。</summary>
    [Fact]
    public async Task Verify_OneByteChangeInvalidatesSignature()
    {
        using var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);
        var verifier = provider.GetRequiredService<IWebhookSignatureVerifier>();
        var body = Encoding.UTF8.GetBytes("{\"events\":[]} ");
        var signature = CreateSignature(body, AlphaSecret);
        body[^1] = (byte)'\t';
        Assert.False(await verifier.VerifyAsync("123", body, signature, CancellationToken.None));
    }

    /// <summary>格式錯誤及錯誤長度 header 屬預期拒絕，不能以 FormatException 或略過驗簽取代 false。</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid")]
    [InlineData("AA==")]
    public async Task Verify_MalformedSignatureFailsClosed(string signature)
    {
        using var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);
        var verifier = provider.GetRequiredService<IWebhookSignatureVerifier>();
        Assert.False(await verifier.VerifyAsync("123", "{}"u8.ToArray(), signature, CancellationToken.None));
    }

    /// <summary>呼叫端已取消時必須保留取消 token，不開始 crypto 或把取消當成驗簽成功。</summary>
    [Fact]
    public async Task Verify_CallerCancellationPreservesToken()
    {
        using var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);
        var verifier = provider.GetRequiredService<IWebhookSignatureVerifier>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await verifier.VerifyAsync("123", "{}"u8.ToArray(), "invalid", cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    /// <summary>一百個 A／B 並行操作共用無狀態 verifier，各自正確秘密成功、交叉秘密失敗且不保留結果。</summary>
    [Fact]
    public async Task Verify_ConcurrentChannelsCannotReuseAnotherSecret()
    {
        using var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);
        var verifier = provider.GetRequiredService<IWebhookSignatureVerifier>();
        var body = "{}"u8.ToArray();
        var alpha = CreateSignature(body, AlphaSecret);
        var beta = CreateSignature(body, BetaSecret);
        await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(async () =>
        {
            var isAlpha = index % 2 == 0;
            var channel = isAlpha ? "123" : "456";
            Assert.True(await verifier.VerifyAsync(channel, body, isAlpha ? alpha : beta, CancellationToken.None));
            Assert.False(await verifier.VerifyAsync(channel, body, isAlpha ? beta : alpha, CancellationToken.None));
        })));
    }

    /// <summary>建立含兩個非秘密 mapping 與合成 secret 的設定根。</summary>
    private static ConfigurationRoot BuildConfiguration() =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LineWebhook:Channels:123:Organization"] = "Alpha",
            ["LineWebhook:Channels:456:Organization"] = "Beta",
            ["Deployment:Organizations:Alpha:TestChannelSecret"] = AlphaSecret,
            ["Deployment:Organizations:Beta:TestChannelSecret"] = BetaSecret
        }).Build();

    /// <summary>
    /// 以正式 AddLineWebhook 入口建立 provider；本組只解析驗簽器，dispatcher 的 CRM graph
    /// 由真 Host／端到端測試另行驗證，因此不在此純驗簽案例強制建構未使用的外部服務。
    /// </summary>
    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddLineWebhook(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = true
        });
    }

    /// <summary>只在測試記憶體中產生 LINE HMAC header，不寫檔、不送網路。</summary>
    private static string CreateSignature(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }
}
