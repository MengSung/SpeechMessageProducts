/*
檔案整體說明

目的與定位：驗證 SpeechMessage.Tenancy 對 LINE Webhook 頻道 secret 的部署解析契約，
保護頻道 A／B 的組織 mapping、缺 secret fail-closed 及不快取 request／secret 的邊界。

主要功能與流程：測試只建立含合成 secret 的 IConfiguration，建立 resolver 後逐一解析
數字 channelId，確認各頻道只能取得自己的組織設定；缺少或空白 secret 必須被啟動驗證
視為無效。測試不讀實際 User Secrets、不連 LINE、不建立背景工作或外部資源。

使用邊界與維護：設定根由每個案例建立並 Dispose，resolver 只借用設定根；測試中的
secret 是固定合成值，禁止複製部署機密。每個案例以決定性斷言驗證完整 mapping，不能
把通過 resolver 測試解讀成 LINE 實機驗簽或 Webhook 主機已部署。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Tenancy;
using Xunit;

namespace SpeechMessage.Tenancy.Tests;

/// <summary>
/// 驗證 Webhook 頻道 secret 解析的完整部署分區、缺省拒絕及重試邊界。
/// 測試只使用合成設定與記憶體設定根，不持有真實秘密或外部資源。
/// </summary>
public sealed class WebhookChannelSecretTests
{
    /// <summary>頻道 A 與 B 使用不同組織及不同合成 secret，避免測試只驗證單一 mapping。</summary>
    [Fact]
    public void Resolve_ChannelMappingsRemainIsolated()
    {
        using var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["LineWebhook:Channels:123:Organization"] = "Alpha",
            ["LineWebhook:Channels:456:Organization"] = "Beta",
            ["Deployment:Organizations:Alpha:TestChannelSecret"] = "0123456789abcdef0123456789abcdef",
            ["Deployment:Organizations:Beta:TestChannelSecret"] = "abcdef0123456789abcdef0123456789"
        });
        var resolver = new ConfigurationWebhookChannelSecretResolver(configuration, "GoodShepherd");

        Assert.Equal(["123", "456"], resolver.ConfiguredChannelIds);
        Assert.True(resolver.TryResolveSecret("123", out var alpha));
        Assert.True(resolver.TryResolveSecret("456", out var beta));
        Assert.Equal("0123456789abcdef0123456789abcdef", alpha);
        Assert.Equal("abcdef0123456789abcdef0123456789", beta);
        Assert.NotEqual(alpha, beta);
    }

    /// <summary>
    /// 缺少或空白 secret 必須在啟動驗證時拒絕；不能改讀另一頻道、
    /// access token 或預設組織來掩蓋配置缺項。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateConfiguration_MissingSecret_FailsClosed(string? secret)
    {
        using var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["LineWebhook:Channels:123:Organization"] = "Alpha",
            ["Deployment:Organizations:Alpha:TestChannelSecret"] = secret,
            ["Deployment:Organizations:Beta:TestChannelSecret"] = "0123456789abcdef0123456789abcdef"
        });
        var resolver = new ConfigurationWebhookChannelSecretResolver(configuration, "GoodShepherd");

        var error = Assert.Throws<InvalidOperationException>(() => resolver.ValidateConfiguration());
        Assert.Contains("Webhook channel secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789abcdef0123456789abcdef", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 未配置 channel、非法 route 或未配置 secret 均只回 false，不回傳
    /// 其他頻道的 secret；這是 HTTP 不受信任 route 的 fail-closed 邊界。
    /// </summary>
    [Fact]
    public void TryResolveSecret_UnknownOrInvalidChannel_ReturnsFalse()
    {
        using var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["LineWebhook:Channels:123:Organization"] = "Alpha",
            ["Deployment:Organizations:Alpha:TestChannelSecret"] = "0123456789abcdef0123456789abcdef"
        });
        var resolver = new ConfigurationWebhookChannelSecretResolver(configuration, "GoodShepherd");

        Assert.False(resolver.TryResolveSecret("456", out var unknown));
        Assert.Null(unknown);
        Assert.False(resolver.TryResolveSecret("123/../456", out var malformed));
        Assert.Null(malformed);
    }

    /// <summary>外部不能藉由陣列轉型改掉待檢查頻道；缺少第二個 secret 時仍必須拒絕啟動。</summary>
    [Fact]
    public void ConfiguredChannels_CallerMutationCannotBypassStartupValidation()
    {
        using var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["LineWebhook:Channels:123:Organization"] = "Alpha",
            ["LineWebhook:Channels:456:Organization"] = "Beta",
            ["Deployment:Organizations:Alpha:TestChannelSecret"] = "0123456789abcdef0123456789abcdef"
        });
        var resolver = new ConfigurationWebhookChannelSecretResolver(configuration, "GoodShepherd");
        var exposed = Assert.IsAssignableFrom<IList<string>>(resolver.ConfiguredChannelIds);

        Assert.Throws<NotSupportedException>(() => exposed[1] = "123");
        Assert.Equal(["123", "456"], resolver.ConfiguredChannelIds);
        Assert.Throws<InvalidOperationException>(() => resolver.ValidateConfiguration());
    }

    /// <summary>一千零二十四個部署頻道可建立；再多一筆必須先拒絕，避免 Host 擁有無上限的映射。</summary>
    [Theory]
    [InlineData(1024, false)]
    [InlineData(1025, true)]
    public void Configuration_ChannelCountHasExplicitRetentionLimit(int count, bool rejected)
    {
        var values = new Dictionary<string, string?>();
        for (var index = 0; index < count; index++)
            values.Add("LineWebhook:Channels:" + (index + 1).ToString(CultureInfo.InvariantCulture) + ":Organization", "Alpha");
        using var configuration = BuildConfiguration(values);
        if (rejected)
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                new ConfigurationWebhookChannelSecretResolver(configuration, "GoodShepherd"));
            Assert.Equal("Webhook channel mapping exceeds its configured limit.", error.Message);
        }
        else
        {
            var resolver = new ConfigurationWebhookChannelSecretResolver(configuration, "GoodShepherd");
            Assert.Equal(count, resolver.ConfiguredChannelIds.Count);
        }
    }

    /// <summary>公開序列化及字串表示不得暴露秘密，設定來源更新後不能沿用先前操作的解析結果。</summary>
    [Fact]
    public void Secret_IsNotSerializedOrCachedBetweenOperations()
    {
        const string first = "0123456789abcdef0123456789abcdef";
        const string second = "abcdef0123456789abcdef0123456789";
        using var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["LineWebhook:Channels:123:Organization"] = "Alpha",
            ["Deployment:Organizations:Alpha:TestChannelSecret"] = first
        });
        var resolver = new ConfigurationWebhookChannelSecretResolver(configuration, "GoodShepherd");
        Assert.True(resolver.TryResolveSecret("123", out var resolvedFirst));
        Assert.Equal(first, resolvedFirst);
        configuration["Deployment:Organizations:Alpha:TestChannelSecret"] = second;
        Assert.True(resolver.TryResolveSecret("123", out var resolvedSecond));
        Assert.Equal(second, resolvedSecond);
        Assert.DoesNotContain(first, JsonSerializer.Serialize(resolver), StringComparison.Ordinal);
        Assert.DoesNotContain(second, JsonSerializer.Serialize(resolver), StringComparison.Ordinal);
        Assert.DoesNotContain(second, resolver.ToString()!, StringComparison.Ordinal);
    }

    /// <summary>建立本案例唯一的記憶體設定根；設定值皆為合成資料並由呼叫端負責釋放。</summary>
    /// <param name="values">不含真實秘密的部署樣本。</param>
    /// <returns>本案例擁有的設定根。</returns>
    private static ConfigurationRoot BuildConfiguration(Dictionary<string, string?> values) =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
