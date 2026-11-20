/*
 * 檔案整體說明
 *
 * 目的與定位：保護 ChurchReport 組合根啟用教會覆寫時確實註冊既有真實來源、快取與輪詢，
 * 並驗證設定存檔立即失效本程序快取；只使用合成依賴，不連 D365 或 LINE。
 * 主要流程：測試實際組態註冊入口及 DI 描述子，確認唯一 singleton provider 來自 AddChurchTextOverrides，
 * source/polling 只在開關有效時存在；另以可控來源載入 ChurchTextOverrideCache 後執行存檔，
 * 斷言同一完整 organization/product/church scope 立即失效而其他教會快照保持不變。
 * 使用邊界與維護：測試替身只存本案例固定文字與 GUID，不建立真實 manager、SDK、timer 或秘密；
 * 所有 cache 與 service provider 均由案例建立並 await Dispose，不能把離線測試記為實機結果。
 * 故障與決定性斷言：缺少接線、空 provider 共存、不同教會被失效或 Host 提前釋放 owner 均使案例失敗。
 */
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Hosting.Localization;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Settings;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using ToolUtilityNameSpace.Dataverse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Localization;

/// <summary>從正式 Startup 接點與真實記憶體 cache 驗證 ChurchReport 覆寫註冊及即時失效。</summary>
public sealed class ChurchOverridesCompositionTests
{
    /// <summary>生產 ChurchReport 組合必須以真實 D365 source、共用快取與 polling 取代空 provider。</summary>
    [Fact]
    public void Overrides_ChurchReportWired_RealSourceAndPolling()
    {
        // 呼叫 ChurchReport 正式組合入口；離線替身會在任何租約取得時立即失敗，
        // 因此通過只證明 DI 註冊圖正確，不代表 D365 實機連線或查詢成功。
        var result = ChurchReportOverridesTestComposition.BuildEnabledServiceCollection();

        Assert.Single(result.GetServices<IChurchTextOverrideSource>());
        Assert.IsType<ChurchTextOverrideCache>(result.GetRequiredService<IChurchTextOverrideSnapshotProvider>());
        Assert.Contains(result.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            item => item is TextChangePollingService);
        Assert.DoesNotContain(result.GetServices<IChurchTextOverrideSnapshotProvider>(),
            item => item.GetType().Name.Contains("Empty", StringComparison.Ordinal));
    }

    /// <summary>存檔後只失效相同完整 scope，A 教會快照立即重讀而 B 教會既有快照保持原值。</summary>
    [Fact]
    public async Task SettingsService_Save_InvalidatesLocalCacheImmediately()
    {
        await using var fixture = new ChurchOverridesCacheFixture();

        await fixture.LoadBothAsync();
        await fixture.SaveOverrideForAAsync();

        Assert.Equal("新 A 文字", fixture.ReadA());
        Assert.Equal("原 B 文字", fixture.ReadB());
        Assert.Equal(2, fixture.Source.ReadCountFor(fixture.ChurchA));
        Assert.Equal(1, fixture.Source.ReadCountFor(fixture.ChurchB));
    }
}

/// <summary>呼叫正式 ChurchReport 覆寫註冊入口建立純記憶體 DI 圖，拒絕任何 Dataverse 租約。</summary>
internal static class ChurchReportOverridesTestComposition
{
    /// <summary>使用固定合成組態與禁止連線的 manager 建立 enabled Host service provider。</summary>
    internal static ServiceProvider BuildEnabledServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataverseConnectionManager, ChurchReportOfflineManager>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Localization:ChurchOverrides:Enabled"] = "true",
                ["CrmConnection:Organization"] = "SyntheticChurchReport"
            }).Build();
        global::ChurchReport.ChurchTextOverrideRegistration.AddChurchReportChurchTextOverrides(services, configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }
}

/// <summary>將真實 ChurchSettingsService 存檔連到真 cache，以 source 讀取次數證明 A 失效而 B 不失效。</summary>
internal sealed class ChurchOverridesCacheFixture : IAsyncDisposable
{
    private static readonly DateTime Stamp = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
    internal Guid ChurchA { get; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    internal Guid ChurchB { get; } = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly OverrideSource source = new();
    private readonly ChurchTextOverrideCache cache;
    private readonly GoodShepherdTextCatalog catalog;
    private readonly ChurchSettingsService settings;
    private readonly TextContext contextA;
    private readonly TextContext contextB;
    internal OverrideSource Source => source;

    /// <summary>每案例單獨擁有 cache、catalog、source 與 settings service，無 Host 或外部 SDK。</summary>
    internal ChurchOverridesCacheFixture()
    {
        contextA = new TextContext(ChurchA, "zh-TW", "zh-TW", "SyntheticOrg", "ChurchReport");
        contextB = new TextContext(ChurchB, "zh-TW", "zh-TW", "SyntheticOrg", "ChurchReport");
        source.Set(ChurchA, "原 A 文字");
        source.Set(ChurchB, "原 B 文字");
        cache = new ChurchTextOverrideCache(source,
            GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會"),
            static (_, _) => { });
        catalog = new GoodShepherdTextCatalog(cache);
        settings = new ChurchSettingsService(new SettingsStore(source),
            new CacheInvalidator(cache), GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會"),
            new FixedClock());
    }

    /// <summary>先載入兩個 scope 的完整快照，使後續失效範圍可由來源讀取計數分辨。</summary>
    internal async Task LoadBothAsync()
    {
        await cache.LoadAsync(contextA, Stamp);
        await cache.LoadAsync(contextB, Stamp);
    }

    /// <summary>透過正式設定服務儲存 A，再讀取新的 A 世代；不清除或重新讀取 B。</summary>
    internal async Task SaveOverrideForAAsync()
    {
        await settings.SaveOverrideAsync(new AuthorizedChurch(ChurchA), contextA,
            "Role.Member", "zh-TW", "新 A 文字");
        await cache.LoadAsync(contextA, Stamp.AddMinutes(1));
    }

    /// <summary>以正式產品 catalog 讀 A 的已發布詞彙。</summary>
    internal string ReadA() => catalog.GetText(contextA, new TextKey("Role.Member"));
    /// <summary>以正式產品 catalog 讀 B 的原有快照。</summary>
    internal string ReadB() => catalog.GetText(contextB, new TextKey("Role.Member"));
    /// <summary>測試 owner 排空 cache 的共享來源工作並釋放唯一快取資源。</summary>
    public ValueTask DisposeAsync() => cache.DisposeAsync();

    /// <summary>只保存兩筆合成文字；每次回傳新列，正式 cache 負責 scope 與候選驗證。</summary>
    internal sealed class OverrideSource : IChurchTextOverrideSource
    {
        private readonly Dictionary<Guid, string> values = new();
        private readonly Dictionary<Guid, int> reads = new();
        /// <summary>替換單一合成教會的顯示文字。</summary>
        internal void Set(Guid church, string value) => values[church] = value;
        /// <summary>取得單一教會的 cache load 次數。</summary>
        internal int ReadCountFor(Guid church) => reads.TryGetValue(church, out var count) ? count : 0;
        /// <summary>為指定 scope 建立一列 detached 候選，不保存呼叫者 context。</summary>
        public Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reads[context.ChurchId] = ReadCountFor(context.ChurchId) + 1;
            IReadOnlyList<ChurchTextOverride> rows =
            [
                new ChurchTextOverride(GuidFrom(context.ChurchId), context.ChurchId,
                    new TextKey("Role.Member"), "zh-TW", values[context.ChurchId])
            ];
            return Task.FromResult(rows);
        }
        /// <summary>由合成 scope 產生非空穩定資料列 ID，不以顯示文字作 identity。</summary>
        private static Guid GuidFrom(Guid church)
        {
            var bytes = church.ToByteArray();
            bytes[15] ^= 0x5a;
            return new Guid(bytes);
        }
    }

    /// <summary>將設定服務的失效 port 轉接既有 cache，不保存跨案例狀態。</summary>
    private sealed class CacheInvalidator(ChurchTextOverrideCache owner) : IChurchSettingsCacheInvalidator
    {
        /// <summary>只使已成功持久化的相同完整 scope 進入新版本。</summary>
        public void Invalidate(TextContext context, DateTime changedOnUtc) => owner.Invalidate(context, changedOnUtc);
    }

    /// <summary>固定時鐘確保存檔版本明確 UTC，且不依賴執行機器時區。</summary>
    private sealed class FixedClock : IChurchSettingsClock
    {
        /// <summary>提供單一合成 UTC 時刻。</summary>
        public DateTime UtcNow => Stamp.AddMinutes(1);
    }

    /// <summary>只需在本測試更新來源文字；其他未使用儲存操作明確拒絕。</summary>
    private sealed class SettingsStore(OverrideSource source) : IChurchSettingsStore
    {
        /// <summary>更新合成來源值以模擬 Dataverse Upsert 已提交。</summary>
        public Task UpsertOverrideAsync(ChurchTextOverrideWrite value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.Set(value.ChurchId, value.Text);
            return Task.CompletedTask;
        }
        /// <summary>本案例不測試刪除操作。</summary>
        public Task RemoveOverrideAsync(Guid churchId, string key, string culture, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>時間戳在此替身中只用來證明呼叫完成。</summary>
        public Task TouchTextChangedOnAsync(Guid churchId, DateTime changedOnUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>本案例不管理奉獻類別。</summary>
        public Task<IReadOnlyList<OfferingCategory>> ReadCategoriesAsync(Guid churchId, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>本案例不管理奉獻類別代碼。</summary>
        public Task<bool> CategoryCodeExistsAsync(Guid churchId, string code, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>本案例不新增奉獻類別。</summary>
        public Task<Guid> AddCategoryAsync(Guid churchId, string code, string name, int sortOrder, string? accountingCode, int? legacyOptionValue, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>本案例不更新奉獻類別。</summary>
        public Task UpdateCategoryAsync(Guid churchId, Guid id, string name, int sortOrder, string? accountingCode, int? legacyOptionValue, bool isActive, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>本案例不查詢稽核資料。</summary>
        public Task<IReadOnlyList<ChurchSettingsAuditRecord>> ReadAuditAsync(Guid churchId, int maximum, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

/// <summary>ChurchReport composition test 專用的無連線 manager；任何真實資料操作都立即失敗。</summary>
internal sealed class ChurchReportOfflineManager : IDataverseConnectionManager
{
    /// <summary>禁止組合測試取得 SDK 租約。</summary>
    public IClientLease Acquire(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("ChurchReport composition tests must remain offline.");
    /// <summary>本測試不量測連線池。</summary>
    public DataversePoolMetrics GetMetrics() =>
        throw new InvalidOperationException("ChurchReport composition tests must remain offline.");
    /// <summary>替身沒有資源。</summary>
    public void Dispose() { }
}
