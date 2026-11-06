using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SpeechMessage.Tenancy.Tests;

/// <summary>使用合成設定保護舊後備、完整 scope、憑證遮罩與純 resolver；不連 LINE／D365，不讀真實環境或時鐘。</summary>
public sealed class ConfigurationResolverTests
{
    /// <summary>不同大小寫的受信任組織必須選自身 token 及頻道 ID；不可先使用預設組織。</summary>
    [Fact(DisplayName = "租戶設定的頻道與憑證保持一致")]
    public void Resolve_ConfiguredOrg_ReturnsThatToken()
    {
        using var config = Config();
        var channel = new ConfigurationLineChannelResolver(config, "ProductA").Resolve(new TenantId("ALPHA", "ProductA"));
        Assert.Equal("synthetic-alpha", channel.GetAccessTokenForLegacyClient());
        Assert.Equal("channel-A", channel.ChannelId);
        Assert.False(channel.UsedFallback);
    }

    /// <summary>找不到 org 時保留 Jesus 後備，warning 只有固定事件；token、教會及任意 org 不可進 log。</summary>
    [Fact(DisplayName = "缺租戶頻道沿用Jesus並記錄安全告警")]
    public void Resolve_MissingOrg_FallsBackToJesusAndLogsWarning()
    {
        using var config = Config();
        var logger = new CaptureLogger();
        var channel = new ConfigurationLineChannelResolver(config, "ProductA", logger).Resolve(new TenantId("unknown", "ProductA"));
        Assert.Equal("synthetic-default", channel.GetAccessTokenForLegacyClient());
        Assert.True(channel.UsedFallback);
        Assert.Single(logger.Messages);
        Assert.DoesNotContain("synthetic-default", logger.Messages.Single(), StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", logger.Messages.Single(), StringComparison.Ordinal);
    }

    /// <summary>部署有指定預設別名時不能硬改回 Jesus；後備 token 與 ID 都來自同一別名。</summary>
    [Fact(DisplayName = "保留明確預設頻道別名")]
    public void Resolve_ExplicitDefault_PreservesAlias()
    {
        using var config = Config();
        config["LineMessaging:DefaultOrganization"] = "Beta";
        var channel = new ConfigurationLineChannelResolver(config, "ProductA").Resolve(new TenantId("unknown", "ProductA"));
        Assert.Equal("synthetic-beta", channel.GetAccessTokenForLegacyClient());
        Assert.Equal("channel-B", channel.ChannelId);
    }

    /// <summary>真實組織及預設別名可以叫 environment；另放不同環境頻道 ID，證明來源不可由別名字串推斷。</summary>
    /// <param name="useDefault">是否經缺組織後備選取同名別名。</param>
    [Theory(DisplayName = "environment別名不冒用環境頻道ID")]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_EnvironmentAlias_PreservesConfiguredChannelIdentity(bool useDefault)
    {
        using var config = Config();
        config["LineMessaging:environment:ChannelAccessToken"] = "synthetic-alias";
        config["LineMessaging:environment:ChannelId"] = "alias-channel";
        config["LineMessaging:DefaultOrganization"] = "environment";
        config["LINE_CHANNEL_ID"] = "environment-channel";
        var channel = new ConfigurationLineChannelResolver(config, "ProductA")
            .Resolve(new TenantId(useDefault ? "unknown" : "environment", "ProductA"));
        Assert.Equal("synthetic-alias", channel.GetAccessTokenForLegacyClient());
        Assert.Equal("alias-channel", channel.ChannelId);
    }

    /// <summary>四種舊來源優先序明確區分；同時存在全部 token，不能以統一順序偷改既有呼叫。</summary>
    /// <param name="selection">部署選定的相容政策。</param>
    /// <param name="expected">人工指定的既有結果。</param>
    [Theory(DisplayName = "集中解析保留四種既有優先序")]
    [InlineData(LineChannelSelection.OrganizationThenDefault, "synthetic-alpha")]
    [InlineData(LineChannelSelection.EnvironmentThenDefault, "synthetic-environment")]
    [InlineData(LineChannelSelection.DefaultThenEnvironment, "synthetic-default")]
    [InlineData(LineChannelSelection.EnvironmentThenOrganizationThenDefault, "synthetic-environment")]
    public void Resolve_LegacySelection_PreservesPrecedence(LineChannelSelection selection, string expected)
    {
        using var config = Config();
        config["LINE_CHANNEL_ACCESS_TOKEN"] = "synthetic-environment";
        var channel = new ConfigurationLineChannelResolver(config, "ProductA", selection: selection).Resolve(new TenantId("Alpha", "ProductA"));
        Assert.Equal(expected, channel.GetAccessTokenForLegacyClient());
    }

    /// <summary>Startup 原本以 null 合併：明確空 default 不能落到環境 token，必填拒絕且可選入口回 false。</summary>
    [Fact(DisplayName = "預設空字串保留舊null合併規則")]
    public void Resolve_DefaultEmpty_DoesNotSelectEnvironment()
    {
        using var config = Config();
        config["LineMessaging:Jesus:ChannelAccessToken"] = "";
        config["LINE_CHANNEL_ACCESS_TOKEN"] = "synthetic-environment";
        var resolver = new ConfigurationLineChannelResolver(config, "ProductA", selection: LineChannelSelection.DefaultThenEnvironment);
        var tenant = new TenantId("Alpha", "ProductA");
        Assert.False(resolver.TryResolve(tenant, out var channel));
        Assert.Null(channel);
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(tenant));
    }

    /// <summary>環境與告警 sender 容許 Bearer 前綴，核心憑證只能收到正規化且不含控制字元的 raw token。</summary>
    [Fact(DisplayName = "Bearer正規化不污染共用憑證")]
    public void Resolve_BearerPrefix_NormalizesOnce()
    {
        using var config = Config();
        config["LINE_CHANNEL_ACCESS_TOKEN"] = "Bearer synthetic-environment";
        var channel = new ConfigurationLineChannelResolver(config, "ProductA", selection: LineChannelSelection.EnvironmentThenDefault)
            .Resolve(new TenantId("Alpha", "ProductA"));
        Assert.Equal("synthetic-environment", channel.GetAccessTokenForLegacyClient());
        Assert.DoesNotContain("synthetic", channel.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic", JsonSerializer.Serialize(channel), StringComparison.Ordinal);
    }

    /// <summary>必填缺設定、錯產品及無效 enum 均拒絕，不能把其他產品的已選結果交回。</summary>
    [Fact(DisplayName = "缺設定與跨產品使用明確拒絕")]
    public void Resolve_MissingOrForeignScope_FailsClosed()
    {
        using var config = (ConfigurationRoot)new ConfigurationBuilder().Build();
        var resolver = new ConfigurationLineChannelResolver(config, "ProductA");
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new TenantId("Alpha", "ProductA")));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(new TenantId("Alpha", "ProductB")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfigurationLineChannelResolver(config, "ProductA", selection: (LineChannelSelection)42));
    }

    /// <summary>已選 token 若有標頭注入、非 ASCII、重複 Bearer 或只有前綴，必須拒絕而非再落到另一頻道。</summary>
    /// <param name="invalid">只供本案例的無效合成 token。</param>
    [Theory(DisplayName = "無效憑證拒絕而不冒用後備頻道")]
    [InlineData("synthetic\ninjection")]
    [InlineData("synthetic token")]
    [InlineData("Bearer Bearer synthetic")]
    [InlineData("Bearer ")]
    [InlineData("synthetic-é")]
    public void Resolve_InvalidHeaderToken_RejectsWithoutFallback(string invalid)
    {
        using var config = Config();
        config["LineMessaging:Alpha:ChannelAccessToken"] = invalid;
        var exception = Assert.Throws<ArgumentException>(() =>
            new ConfigurationLineChannelResolver(config, "ProductA").Resolve(new TenantId("Alpha", "ProductA")));
        Assert.DoesNotContain(invalid, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>設定有界；過長識別、token、頻道 ID 或控制字元 ID 均不可發布，錯誤也不包含來源值。</summary>
    [Fact(DisplayName = "憑證與頻道識別受固定容量限制")]
    public void Resolve_OverBudgetOrUnsafeIdentity_Rejects()
    {
        using var config = Config();
        Assert.Throws<ArgumentException>(() => new TenantId(new string('A', 129), "ProductA"));
        var resolver = new ConfigurationLineChannelResolver(config, "ProductA");
        var tenant = new TenantId("Alpha", "ProductA");
        config["LineMessaging:Alpha:ChannelAccessToken"] = new string('a', 8193);
        Assert.Throws<ArgumentException>(() => resolver.Resolve(tenant));
        config["LineMessaging:Alpha:ChannelAccessToken"] = "synthetic-alpha";
        foreach (var invalid in new[] { "", new string('a', 129), "id\ninjection" })
        {
            config["LineMessaging:Alpha:ChannelId"] = invalid;
            Assert.Throws<ArgumentException>(() => resolver.Resolve(tenant));
        }
    }

    /// <summary>Startup 只有 null 預設才可使用環境，token 與 ID 來自同一來源；告警空白 org token 仍落到預設。</summary>
    [Fact(DisplayName = "特殊政策的null與空白分支維持來源配對")]
    public void Resolve_SpecialFallbackBranches_PreserveMatchingIdentity()
    {
        using var config = Config();
        config["LineMessaging:Jesus:ChannelAccessToken"] = null;
        config["LINE_CHANNEL_ACCESS_TOKEN"] = "synthetic-environment";
        config["LINE_CHANNEL_ID"] = "environment-id";
        var result = new ConfigurationLineChannelResolver(config, "ProductA", selection: LineChannelSelection.DefaultThenEnvironment)
            .Resolve(new TenantId("Alpha", "ProductA"));
        Assert.Equal("synthetic-environment", result.GetAccessTokenForLegacyClient());
        Assert.Equal("environment-id", result.ChannelId);
        config["LINE_CHANNEL_ACCESS_TOKEN"] = " ";
        config["LineMessaging:Alpha:ChannelAccessToken"] = " ";
        config["LineMessaging:Jesus:ChannelAccessToken"] = "synthetic-default";
        Assert.Equal("synthetic-default", new ConfigurationLineChannelResolver(config, "ProductA",
            selection: LineChannelSelection.EnvironmentThenOrganizationThenDefault).Resolve(new TenantId("Alpha", "ProductA"))
            .GetAccessTokenForLegacyClient());
    }

    /// <summary>冒號、空白、控制字元及過長識別不能拼入 IConfiguration 路徑；提供的正式教會 ID 不可為空。</summary>
    /// <param name="invalid">不可信的識別格式。</param>
    [Theory(DisplayName = "租戶識別拒絕設定路徑注入")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Alpha:Beta")]
    [InlineData("Alpha\nBeta")]
    [InlineData("../Beta")]
    public void Tenant_InvalidIdentifier_Rejects(string invalid)
    {
        Assert.Throws<ArgumentException>(() => new TenantId(invalid, "ProductA"));
        Assert.Throws<ArgumentException>(() => new TenantId("Alpha", invalid));
        Assert.Throws<ArgumentException>(() => new TenantId("Alpha", "ProductA", Guid.Empty));
    }

    /// <summary>同一 resolver 供兩組織各千次並行，每次結果及 credential 都獨立且來源正確，所有 Task 必須排空。</summary>
    [Fact(DisplayName = "兩租戶千次並行解析不串憑證")]
    public async Task Resolve_TwoTenantsParallel_NoCrossTalk()
    {
        using var config = Config();
        var resolver = new ConfigurationLineChannelResolver(config, "ProductA");
        var tasks = Enumerable.Range(0, 1000).Select(_ => Task.Run(() =>
        {
            var first = resolver.Resolve(new TenantId("Alpha", "ProductA"));
            var second = resolver.Resolve(new TenantId("Beta", "ProductA"));
            Assert.Equal("synthetic-alpha", first.GetAccessTokenForLegacyClient());
            Assert.Equal("synthetic-beta", second.GetAccessTokenForLegacyClient());
            Assert.NotSame(first.Credential, second.Credential);
        })).ToArray();
        await Task.WhenAll(tasks);
        Assert.All(tasks, task => Assert.True(task.IsCompletedSuccessfully));
    }

    /// <summary>持續存活 resolver 完成百次解析後，不能保留 tenant、結果或 credential；設定根仍由本測試擁有。</summary>
    [Fact(DisplayName = "解析器不保留完成操作的範圍與憑證")]
    public void Resolve_CompletedCalls_DoNotRetainResults()
    {
        using var config = Config();
        var resolver = new ConfigurationLineChannelResolver(config, "ProductA");
        var refs = Enumerable.Range(0, 100).SelectMany(_ => Exercise(resolver)).ToArray();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(refs, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(resolver);
    }

    /// <summary>阻止 JIT 延長本地值生命週期，弱引用只測 resolver 是否保留而非測試堆疊。</summary>
    /// <param name="resolver">要持續存活的真實實例。</param>
    /// <returns>本次純弱引用。</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] Exercise(ConfigurationLineChannelResolver resolver)
    {
        var tenant = new TenantId("Alpha", "ProductA");
        var result = resolver.Resolve(tenant);
        return [new(tenant), new(result), new(result.Credential)];
    }

    /// <summary>只建立合成設定；呼叫者 using 釋放設定根，不讀檔、不加入環境或 secrets provider。</summary>
    /// <returns>由單一案例擁有的設定根。</returns>
    private static ConfigurationRoot Config() => (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["LineMessaging:Alpha:ChannelAccessToken"] = "synthetic-alpha",
        ["LineMessaging:Alpha:ChannelId"] = "channel-A",
        ["LineMessaging:Beta:ChannelAccessToken"] = "synthetic-beta",
        ["LineMessaging:Beta:ChannelId"] = "channel-B",
        ["LineMessaging:Jesus:ChannelAccessToken"] = "synthetic-default"
    }).Build();

    /// <summary>案例自有固定上限 logger，只保存本輪安全訊息；不接受任意無界日誌或外部資源。</summary>
    private sealed class CaptureLogger : ILogger<ConfigurationLineChannelResolver>
    {
        /// <summary>最多三十二筆的測試證據，案例結束即回收。</summary>
        internal ConcurrentQueue<string> Messages { get; } = new();
        /// <summary>不建立 scope 或保留泛型 state。</summary>
        /// <typeparam name="TState">logger scope 型別。</typeparam>
        /// <param name="state">不保留的狀態。</param>
        /// <returns>沒有 scope 資源。</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>只允許 warning 以捕捉真正後備事件。</summary>
        /// <param name="logLevel">待檢查等級。</param>
        /// <returns>warning 時為真。</returns>
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;
        /// <summary>保存安全輸出並確保此替身有界；不保存 state 或 Exception。</summary>
        /// <typeparam name="TState">單次日誌狀態。</typeparam>
        /// <param name="logLevel">等級。</param>
        /// <param name="eventId">固定事件。</param>
        /// <param name="state">本次狀態。</param>
        /// <param name="exception">本次例外，不保留。</param>
        /// <param name="formatter">只在本次使用的格式器。</param>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            if (Messages.Count >= 32) throw new InvalidOperationException("Test log budget exceeded.");
            Messages.Enqueue(formatter(state, exception));
        }
    }
}
