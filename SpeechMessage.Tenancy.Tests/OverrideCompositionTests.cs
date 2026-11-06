using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Localization;
using Xunit;

namespace SpeechMessage.Tenancy.Tests;

/// <summary>驗真 DI owner 組合，不讓 singleton 快取保存 scoped request source，容器必須 await 確定清理同一 cache。</summary>
public sealed class OverrideCompositionTests
{
    /// <summary>source／validator／provider／catalog 使用同一套正式服務，兩個別名同 owner 且 async disposal 恰一次清理。</summary>
    [Fact]
    public async Task Composition_OneCacheOwner_IsDrainedByContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChurchTextOverrideSource, EmptySource>();
        Action<Exception, string> report = static (_, _) => { };
        services.AddChurchTextOverrides("這是一個長度剛好十二字的教會", report);
        services.AddChurchTextOverrides("這是一個長度剛好十二字的教會", report);
        var provider = services.BuildServiceProvider(validateScopes: true);
        var cache = provider.GetRequiredService<ChurchTextOverrideCache>();
        Assert.Same(cache, provider.GetRequiredService<IChurchTextOverrideSnapshotProvider>());
        var context = new TextContext(Guid.NewGuid(), "zh-TW", "zh-TW", "Alpha", "ChurchReport");
        await cache.LoadAsync(context, new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("連線測試成功", provider.GetRequiredService<ITextCatalog>().GetText(context, new TextKey("Common.Diagnostics.Ping")));
        await provider.DisposeAsync();
        Assert.Equal(0, cache.CachedScopeCount);
        Assert.Equal(0, cache.ActiveLoadCount);
        await cache.DisposeAsync();
    }

    /// <summary>缺來源、scoped/transient captive source 或重複來源都拒絕，不能在正式 Host 偷塞 no-op source。</summary>
    /// <param name="mode">錯誤來源配置。</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("scoped")]
    [InlineData("transient")]
    [InlineData("duplicate")]
    public void Composition_InvalidSourceLifetime_RejectsBeforeRegistration(string mode)
    {
        var services = new ServiceCollection();
        if (mode == "scoped") services.AddScoped<IChurchTextOverrideSource, EmptySource>();
        if (mode == "transient") services.AddTransient<IChurchTextOverrideSource, EmptySource>();
        if (mode == "duplicate")
        {
            services.AddSingleton<IChurchTextOverrideSource, EmptySource>();
            services.AddSingleton<IChurchTextOverrideSource, EmptySource>();
        }
        Assert.Throws<InvalidOperationException>(() => services.AddChurchTextOverrides("這是一個長度剛好十二字的教會", static (_, _) => { }));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ChurchTextOverrideCache));
    }

    /// <summary>不同 notifier 或最長樣本不能由第二次註冊靜默覆蓋；已有 catalog/provider owner 亦拒絕。</summary>
    [Fact]
    public void Composition_ConflictingOwner_Rejects()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChurchTextOverrideSource, EmptySource>();
        Action<Exception, string> report = static (_, _) => { };
        services.AddChurchTextOverrides("這是一個長度剛好十二字的教會", report);
        Assert.Throws<InvalidOperationException>(() => services.AddChurchTextOverrides("other", report));
        Assert.Throws<InvalidOperationException>(() => services.AddChurchTextOverrides("這是一個長度剛好十二字的教會", static (_, _) => throw new InvalidOperationException()));
    }

    /// <summary>只在 DI 測試使用的明確空候選來源，不是正式 Host fallback；本身沒有資源或背景工作。</summary>
    private sealed class EmptySource : IChurchTextOverrideSource
    {
        /// <summary>空資料仍是完整候選，透過真 validator／cache 後由 catalog 回退到系統原文。</summary>
        public Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Array.Empty<ChurchTextOverride>());
        }
    }
}
