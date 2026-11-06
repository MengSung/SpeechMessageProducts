using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SpeechMessage.Tenancy.Tests;

/// <summary>驗證真實 DI 容器、設定來源故障及舊橋接，全部只使用合成值；不連外、不變更程序環境。</summary>
public sealed class TenancyCompositionTests
{
    /// <summary>同產品重複註冊必須維持唯一 resolver；容器關閉不能 Dispose 外部設定 provider，最後由本案例唯一釋放。</summary>
    [Fact(DisplayName = "DI可重入且不接管外部設定owner")]
    public void AddTenancy_RepeatedRegistration_BorrowsConfiguration()
    {
        var source = new FaultSource { FailReads = false };
        using (var config = (ConfigurationRoot)new ConfigurationBuilder().Add(source).Build())
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddSpeechMessageTenancy("ProductA").AddSpeechMessageTenancy("ProductA");
            using (var provider = services.BuildServiceProvider())
            {
                var first = provider.GetRequiredService<ILineChannelResolver>();
                Assert.Same(first, provider.GetRequiredService<ILineChannelResolver>());
                Assert.Same(first, provider.GetRequiredService<ConfigurationLineChannelResolver>());
                Assert.Equal("synthetic-alpha", first.Resolve(new TenantId("Alpha", "ProductA")).GetAccessTokenForLegacyClient());
                Assert.Equal("TW", provider.GetRequiredService<ITenantSettingsProvider>()
                    .GetSettings(new TenantId("Alpha", "ProductA", Guid.NewGuid())).Country);
            }
            Assert.Equal(0, source.Provider.DisposeCount);
            Assert.Equal("synthetic-alpha", config["LineMessaging:Alpha:ChannelAccessToken"]);
        }
        Assert.Equal(1, source.Provider.DisposeCount);
    }

    /// <summary>相同容器不允許第二產品覆蓋 singleton owner；不同容器則可各自使用同名組織而不串設定。</summary>
    [Fact(DisplayName = "DI產品邊界拒絕覆蓋且容器間互相隔離")]
    public void AddTenancy_ForeignProduct_RejectsWithoutCrossTalk()
    {
        using var configA = Config("synthetic-A");
        using var configB = Config("synthetic-B");
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configA);
        services.AddSpeechMessageTenancy("ProductA");
        Assert.Throws<InvalidOperationException>(() => services.AddSpeechMessageTenancy("ProductB"));
        using var first = services.BuildServiceProvider();
        var other = new ServiceCollection();
        other.AddSingleton<IConfiguration>(configB);
        other.AddSpeechMessageTenancy("ProductB");
        using var second = other.BuildServiceProvider();
        Assert.Equal("synthetic-A", first.GetRequiredService<ILineChannelResolver>()
            .Resolve(new TenantId("Alpha", "ProductA")).GetAccessTokenForLegacyClient());
        Assert.Equal("synthetic-B", second.GetRequiredService<ILineChannelResolver>()
            .Resolve(new TenantId("Alpha", "ProductB")).GetAccessTokenForLegacyClient());
    }

    /// <summary>設定來源故障必須保留原 Exception 身份；修正來源後可立即重試，不被失敗或舊結果快取阻擋。</summary>
    [Fact(DisplayName = "設定來源故障保真且可重試")]
    public void Resolve_ProviderFailure_PreservesExceptionAndRetries()
    {
        var source = new FaultSource();
        using var config = (ConfigurationRoot)new ConfigurationBuilder().Add(source).Build();
        var resolver = new ConfigurationLineChannelResolver(config, "ProductA");
        Assert.Same(source.Failure, Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new TenantId("Alpha", "ProductA"))));
        Assert.Same(source.Failure, Assert.Throws<InvalidOperationException>(() => LegacyLineChannelAccess.GetOptionalAccessToken(config, "Alpha")));
        source.FailReads = false;
        Assert.Equal("synthetic-alpha", resolver.Resolve(new TenantId("Alpha", "ProductA")).GetAccessTokenForLegacyClient());
    }

    /// <summary>空組織不能誤選名為 default 的真實別名；舊缺設定回空，明確組織則保留自身來源。</summary>
    [Fact(DisplayName = "舊入口集中解析並保留缺設定相容")]
    public void LegacyBridge_OptionalOrganization_PreservesDefaultPolicy()
    {
        using var config = Config("synthetic-alpha");
        config["LineMessaging:default:ChannelAccessToken"] = "synthetic-wrong";
        config["LineMessaging:Jesus:ChannelAccessToken"] = "synthetic-fallback";
        Assert.Equal("synthetic-fallback", LegacyLineChannelAccess.GetOptionalAccessToken(config));
        Assert.Equal("synthetic-alpha", LegacyLineChannelAccess.GetOptionalAccessToken(config, "Alpha"));
        config["LineMessaging:Jesus:ChannelAccessToken"] = null;
        Assert.Equal(string.Empty, LegacyLineChannelAccess.GetOptionalAccessToken(config));
    }

    /// <summary>只建立案例自有的記憶體設定根，不新增監聽或讀取真實部署機密。</summary>
    /// <param name="token">本案例合成 token。</param>
    /// <returns>必須由呼叫者釋放的設定根。</returns>
    private static ConfigurationRoot Config(string token) => (ConfigurationRoot)new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["LineMessaging:Alpha:ChannelAccessToken"] = token }).Build();

    /// <summary>故障來源只有單一 provider，設定根是其 owner；計數只活於本案例，不使用全域狀態。</summary>
    private sealed class FaultSource : IConfigurationSource
    {
        /// <summary>固定合成例外供同一實例斷言，來源值不寫入診斷。</summary>
        internal InvalidOperationException Failure { get; } = new("Synthetic provider failure.");
        /// <summary>由案例明確控制故障及重試，不建立任何 Task。</summary>
        internal bool FailReads { get; set; } = true;
        /// <summary>唯一 provider 只交給一個設定根管理。</summary>
        internal FaultProvider Provider { get; }
        /// <summary>建立本案例唯一 provider。</summary>
        internal FaultSource() => Provider = new FaultProvider(this);
        /// <summary>不產生副本或背景資源，由 ConfigurationRoot 接管。</summary>
        /// <param name="builder">本次設定 builder，不保留。</param>
        /// <returns>唯一 provider。</returns>
        public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
    }

    /// <summary>真實 IConfigurationProvider 的故障注入及 Dispose 計數；無連線、timer 或機密來源。</summary>
    private sealed class FaultProvider : ConfigurationProvider, IDisposable
    {
        /// <summary>案例來源只活至設定根釋放。</summary>
        private readonly FaultSource owner;
        /// <summary>斷言由設定根恰釋放一次，不能被 DI 提前接管。</summary>
        internal int DisposeCount { get; private set; }
        /// <summary>填入固定合成 token，只供真實 resolver 讀取。</summary>
        /// <param name="owner">案例自有故障開關。</param>
        internal FaultProvider(FaultSource owner)
        {
            this.owner = owner;
            Data["LineMessaging:Alpha:ChannelAccessToken"] = "synthetic-alpha";
        }
        /// <summary>只在讀 token 時注入原例外，其他欄位仍使用真實 provider 行為。</summary>
        /// <param name="key">本次設定鍵，不保留。</param>
        /// <param name="value">原始查詢結果，不寫入日誌。</param>
        /// <returns>真實 provider 是否有該鍵。</returns>
        public override bool TryGet(string key, out string? value)
        {
            if (owner.FailReads) throw owner.Failure;
            return base.TryGet(key, out value);
        }
        /// <summary>確定性計數，沒有其他需釋放資源。</summary>
        public void Dispose() => DisposeCount++;
    }
}
