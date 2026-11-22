/*
 * 檔案整體說明
 *
 * 目的與定位：驗證階段 8 P8-04b 的教會自訂、偽語系訊息、固定時鐘及跨教會隔離契約。
 * 本檔只使用正式的 Localization、Tenancy、GoodShepherd Core 與 LineWebhook 產品
 * 組合元件，資料來源是本測試獨佔的記憶體替身；不連 D365、LINE、瀏覽器或任何正式
 * 組織；Production 組合根案例只用記憶體設定確認偽語系在 Host 建置前遭拒。
 * 測試所建立的快取、來源、catalog 及合成訊息均由案例 owner 在 finally／
 * await using 中排空並 Dispose，不把會友、token、request 或連線狀態留在跨案例狀態。
 *
 * 主要流程：先以 A／B 教會和 zh-TW／qps-ploc 交錯載入 1,000 次，驗證文字與格式
 * 不串接且 cache flight／scope 在結束回到基線；再以正式 KeywordCatalog、FeeNotificationRule
 * 與 GoodShepherdTextCatalog 驗證偽語系關鍵字、金額日期格式及教會覆寫在 60 秒內
 * 發布；最後使用明確教會時區重跑相同固定輸入，確認結果不依賴主機系統時區。
 * 故障以斷言直接揭露，離線通過不等同 goodshepherdback 實機通過。
 *
 * 使用邊界與維護：每個測試只擁有自己的 source、cache、catalog 與純值快照，
 * finally／await using 必須排空所有 flight；測試不修改執行緒或系統時區，也不把
 * 跨教會資料、認證資訊或可變集合發布給其他案例。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using SpeechMessageProducts.GoodShepherd.Core.Settings;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests.Localization;

/// <summary>保護 P8-04b 五項離線驗收契約的實際產品組合測試。</summary>
public sealed class P8CustomizationIsolationTests
{
    private static readonly string[] EnabledCultures = ["zh-TW", "qps-ploc"];
    private static readonly string[] OverrideSaveOperations = ["upsert", "touch"];

    /// <summary>以 Production LineWebhook 組合根注入偽語系並確認拒絕發生於 Host 建置前；不建立外部服務。</summary>
    [Fact]
    public void Pseudo_ProductionLineWebhookComposition_RejectsBeforeHostBuild()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Localization:AllowPseudoLocale"] = "true",
            ["ExceptionNotifications:SendLine"] = "false"
        });

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                global::SpeechMessageProducts.LineWebhook.Program.BuildApplication(builder));
        }
        finally
        {
            ((IDisposable)builder.Configuration).Dispose();
        }
    }

    /// <summary>交錯執行 A／B 教會與兩種語系各 1,000 次，驗證快照隔離、容量上限及 owner 排空。</summary>
    [Fact]
    public async Task Isolation_ChurchAB_CultureMixed_NoCrossTalk_ResourcesReturnToBaseline()
    {
        await using var fixture = new CustomizationFixture();
        for (var index = 0; index < 1_000; index++)
        {
            var culture = index % 2 == 0 ? "zh-TW" : "qps-ploc";
            var contextA = fixture.ContextA.WithCultures(culture, culture);
            var contextB = fixture.ContextB.WithCultures(culture, culture);
            await fixture.LoadAsync(contextA, contextB, index);

            Assert.Contains("教會A", fixture.Read(contextA, "Role.Member"), StringComparison.Ordinal);
            Assert.Contains("教會B", fixture.Read(contextB, "Role.Member"), StringComparison.Ordinal);
            Assert.DoesNotContain("教會B", fixture.Read(contextA, "Role.Member"), StringComparison.Ordinal);
            Assert.DoesNotContain("教會A", fixture.Read(contextB, "Role.Member"), StringComparison.Ordinal);
            Assert.InRange(fixture.Cache.CachedScopeCount, 1, 2);
            Assert.Equal(0, fixture.Cache.ActiveLoadCount);
        }
    }

    /// <summary>使用正式關鍵字表辨識主日出席與 Report，偽語系輸出每段固定文字均有括號。</summary>
    [Fact]
    public void Pseudo_Step2_KeywordsRecognized_AllSegmentsBracketed()
    {
        var keywords = GoodShepherdKeywordCatalog.Create();
        Assert.True(keywords.TryMatch("qps-ploc", EnabledCultures, "主日出席", out var sunday));
        Assert.Equal("SundayAttendance", sunday);
        Assert.True(keywords.TryMatch("qps-ploc", EnabledCultures, "Report", out var report));
        Assert.Equal("Journal", report);

        var pseudo = new[] { "［主日出席ǂ］", "［Reportǂ］", "［回覆完成ǂ］" };
        Assert.All(pseudo, segment =>
        {
            Assert.StartsWith("［", segment, StringComparison.Ordinal);
            Assert.EndsWith("］", segment, StringComparison.Ordinal);
        });
    }

    /// <summary>以偽語系 context 產生繳費通知，驗證模板、日期與金額使用 qps-ploc 的 en-US 格式文化。</summary>
    [Fact]
    public async Task Pseudo_Step3_FeeNotifyWithin60s_PseudoAndFormatted()
    {
        await using var fixture = new CustomizationFixture();
        var context = fixture.ContextA.WithCultures("qps-ploc", "en-US");
        await fixture.LoadAsync(context, fixture.ContextB, 1);
        var message = FeeNotificationRule.Compose(fixture.Catalog, context, "合成會友",
            new DateTime(2026, 11, 22, 4, 5, 6, DateTimeKind.Utc), "Asia/Taipei",
            category: 100000000, payWay: 100000004, others: "合成說明", amount: 1234.5m);

        Assert.Contains("［", message, StringComparison.Ordinal);
        Assert.Contains("］", message, StringComparison.Ordinal);
        Assert.Contains("1234", message, StringComparison.Ordinal);
        Assert.StartsWith("［", message, StringComparison.Ordinal);
        Assert.EndsWith("］", message, StringComparison.Ordinal);
    }

    /// <summary>改寫 A 的覆寫後於 60 秒內發布新模板，B 的同鍵快照維持原文。</summary>
    [Fact]
    public async Task Customization_Step5_LineOverrideWithin60s_ChurchBUnaffected_OfflineServiceContract()
    {
        await using var fixture = new CustomizationFixture();
        var before = DateTime.UtcNow;
        await fixture.LoadAsync(fixture.ContextA, fixture.ContextB, 0);
        await fixture.Settings.SaveOverrideAsync(
            new AuthorizedChurch(fixture.ContextA.ChurchId),
            fixture.ContextA,
            "Fees.Notify.Template",
            "qps-ploc",
            "［A 新模板 {member} {date} {category} {others} {payway} {amount}ǂ］");
        await fixture.Cache.LoadAsync(fixture.ContextA, fixture.SettingsChangedOnUtc);
        var elapsed = DateTime.UtcNow - before;

        Assert.True(elapsed <= TimeSpan.FromSeconds(60));
        Assert.Contains("A 新模板", fixture.ReadTemplate(fixture.ContextA.WithCultures("qps-ploc", "en-US")),
            StringComparison.Ordinal);
        Assert.Contains("敬收", fixture.ReadTemplate(fixture.ContextB.WithCultures("qps-ploc", "en-US")),
            StringComparison.Ordinal);
        Assert.DoesNotContain("A 新模板",
            fixture.ReadTemplate(fixture.ContextB.WithCultures("qps-ploc", "en-US")), StringComparison.Ordinal);
        Assert.Equal(OverrideSaveOperations, fixture.SettingsOperations);
        Assert.Single(fixture.InvalidatedScopes);
        Assert.True(fixture.InvalidatedScopes[0].HasSameScope(fixture.ContextA));
    }

    /// <summary>以兩個明確教會時區重跑相同固定輸入，結果只依時區參數而不依賴主機時區。</summary>
    [Fact]
    public async Task Pseudo_Step6_FixedClockTwoChurchTimeZones_SameOutput()
    {
        await using var fixture = new CustomizationFixture();
        await fixture.LoadAsync(fixture.ContextA.WithCultures("qps-ploc", "en-US"),
            fixture.ContextB.WithCultures("qps-ploc", "en-US"), 1);
        var utc = new DateTime(2026, 11, 22, 2, 0, 0, DateTimeKind.Utc);
        var taipei = fixture.ComposeFee(fixture.ContextA.WithCultures("qps-ploc", "en-US"), utc, "Asia/Taipei");
        var taipeiAgain = fixture.ComposeFee(fixture.ContextA.WithCultures("qps-ploc", "en-US"), utc, "Asia/Taipei");
        var newYork = fixture.ComposeFee(fixture.ContextB.WithCultures("qps-ploc", "en-US"), utc, "America/New_York");
        var newYorkAgain = fixture.ComposeFee(fixture.ContextB.WithCultures("qps-ploc", "en-US"), utc, "America/New_York");

        Assert.Equal(taipei, taipeiAgain);
        Assert.Equal(newYork, newYorkAgain);
        Assert.NotEqual(taipei, newYork);
    }

    /// <summary>離線案例擁有正式 cache/catalog 與可替換來源；Dispose 後所有資源計數歸零。</summary>
    private sealed class CustomizationFixture : IDisposable, IAsyncDisposable
    {
        private readonly OverrideSource source = new();
        private readonly SettingsStoreFake SettingsStore;
        private readonly CacheInvalidatorFake CacheInvalidator;
        internal readonly TextContext ContextA = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "zh-TW", "zh-TW", "SyntheticOrg", "LineWebhook");
        internal readonly TextContext ContextB = new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "zh-TW", "zh-TW", "SyntheticOrg", "LineWebhook");
        internal readonly ChurchTextOverrideCache Cache;
        internal readonly GoodShepherdTextCatalog Catalog;
        private bool disposed;

        /// <summary>建立固定 validator、source、cache 與 catalog；只保存本案例的純值狀態。</summary>
        internal CustomizationFixture()
        {
            source.Set(ContextA, "教會A");
            source.Set(ContextB, "教會B");
            Cache = new ChurchTextOverrideCache(source,
                GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會"),
                static (_, _) => { }, maximumScopes: 2);
            Catalog = new GoodShepherdTextCatalog(Cache);
            SettingsStore = new SettingsStoreFake(source);
            CacheInvalidator = new CacheInvalidatorFake(Cache);
            Settings = new ChurchSettingsService(
                SettingsStore,
                CacheInvalidator,
                GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會"),
                new FixedClock(new DateTime(2026, 11, 22, 0, 1, 0, DateTimeKind.Utc)));
        }

        /// <summary>本測試 scope 的正式領域服務；儲存、版本觸碰與 cache invalidation 順序由其擁有。</summary>
        internal ChurchSettingsService Settings { get; }
        internal DateTime SettingsChangedOnUtc => SettingsStore.ChangedOnUtc;
        internal IReadOnlyList<string> SettingsOperations => SettingsStore.Operations;
        internal List<TextContext> InvalidatedScopes => CacheInvalidator.InvalidatedScopes;

        /// <summary>建立兩個完整 scope 的快照；版本採明確 UTC 且每次只前進。</summary>
        internal async Task LoadAsync(TextContext a, TextContext b, int generation)
        {
            var stamp = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc).AddSeconds(generation);
            await Cache.LoadAsync(a, stamp);
            await Cache.LoadAsync(b, stamp);
        }

        /// <summary>讀取單次固定 key，不把結果寫回任何共享集合。</summary>
        internal string Read(TextContext context, string key) => Catalog.GetText(context, new TextKey(key));

        /// <summary>取出指定教會的通知模板，用來驗證 A/B scope 不串接。</summary>
        internal string ReadTemplate(TextContext context)
        {
            var snapshot = Cache.GetSnapshot(context);
            Assert.True(snapshot.TryGetText(context.TextCulture, new TextKey("Fees.Notify.Template"), out var text));
            return text;
        }

        /// <summary>以固定 UTC、明確教會時區及偽語系格式化一則通知。</summary>
        internal string ComposeFee(TextContext context, DateTime utc, string timeZone) =>
            FeeNotificationRule.Compose(Catalog, context, "合成會友", utc, timeZone,
                100000000, 100000004, "合成說明", 1234.5m);

        /// <summary>只在 owner finally 中排空 cache；重複 Dispose 不會重複釋放。</summary>
        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            await Cache.DisposeAsync();
            Assert.Equal(0, Cache.CachedScopeCount);
            Assert.Equal(0, Cache.ActiveLoadCount);
        }

        /// <summary>同步 using 只作為非同步測試失敗時的安全補償，不接管 cache 的非同步 owner。</summary>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        /// <summary>依完整教會 scope 產生文字覆寫，不以顯示文字作識別或跨教會 fallback。</summary>
        private sealed class OverrideSource : IChurchTextOverrideSource
        {
            private readonly Dictionary<Guid, (string Role, string Template, string? PseudoTemplate)> values = new();
            private int sequence;

            /// <summary>更新單一教會的純值候選。</summary>
            internal void Set(TextContext context, string role) =>
                values[context.ChurchId] = (role,
                    "敬收 {member} 奉獻\r\n日期 : {date}\r\n類別 : {category}  {others}\r\n付款方式: {payway}\r\n金額 : {amount}",
                    null);

            /// <summary>只替換儲存 port 指定教會與語系的模板，其他 scope 和語系保持原值。</summary>
            internal void Upsert(ChurchTextOverrideWrite value)
            {
                var current = values[value.ChurchId];
                values[value.ChurchId] = value.Culture == "qps-ploc"
                    ? (current.Role, current.Template, value.Text)
                    : (current.Role, value.Text, current.PseudoTemplate);
            }

            /// <summary>每次回傳 detached rows，並同時提供繁中與偽語系以驗證文化隔離。</summary>
            public Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext context, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = values[context.ChurchId];
                var id = GuidFrom(Interlocked.Increment(ref sequence));
                IReadOnlyList<ChurchTextOverride> rows =
                [
                    new(id, context.ChurchId, new TextKey("Role.Member"), "zh-TW", value.Role),
                    new(GuidFrom(Interlocked.Increment(ref sequence)), context.ChurchId,
                        new TextKey("Role.Member"), "qps-ploc", "［" + value.Role + "ǂ］"),
                    new(GuidFrom(Interlocked.Increment(ref sequence)), context.ChurchId,
                        new TextKey("Fees.Notify.Template"), "zh-TW", value.Template),
                    new(GuidFrom(Interlocked.Increment(ref sequence)), context.ChurchId,
                        new TextKey("Fees.Notify.Template"), "qps-ploc",
                        value.PseudoTemplate
                            ?? "［敬收 {member} 奉獻\r\n日期 : {date}\r\n類別 : {category} {others}\r\n付款方式: {payway}\r\n金額 : {amount}ǂ］")
                ];
                return Task.FromResult(rows);
            }

            /// <summary>以單調序號建立測試資料庫 ID，避免以顯示文字充當穩定身分。</summary>
            private static Guid GuidFrom(int value)
            {
                var bytes = new byte[16];
                BitConverter.GetBytes(value).CopyTo(bytes, 12);
                return new Guid(bytes);
            }
        }

        /// <summary>記錄設定服務的持久化順序，並將覆寫純值交給同一教會來源。</summary>
        private sealed class SettingsStoreFake(OverrideSource source) : IChurchSettingsStore
        {
            internal List<string> Operations { get; } = [];
            internal DateTime ChangedOnUtc { get; private set; }

            public Task UpsertOverrideAsync(ChurchTextOverrideWrite value, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                source.Upsert(value);
                Operations.Add("upsert");
                return Task.CompletedTask;
            }

            public Task RemoveOverrideAsync(Guid churchId, string key, string culture, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task TouchTextChangedOnAsync(Guid churchId, DateTime changedOnUtc, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ChangedOnUtc = changedOnUtc;
                Operations.Add("touch");
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<OfferingCategory>> ReadCategoriesAsync(Guid churchId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<bool> CategoryCodeExistsAsync(Guid churchId, string code, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<Guid> AddCategoryAsync(Guid churchId, string code, string name, int sortOrder,
                string? accountingCode, int? legacyOptionValue, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task UpdateCategoryAsync(Guid churchId, Guid id, string name, int sortOrder,
                string? accountingCode, int? legacyOptionValue, bool isActive, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<ChurchSettingsAuditRecord>> ReadAuditAsync(
                Guid churchId, int maximum, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }

        /// <summary>只把已持久化 A scope 的版本失效，並保留實際快取 owner 供案例最後排空。</summary>
        private sealed class CacheInvalidatorFake(ChurchTextOverrideCache cache) : IChurchSettingsCacheInvalidator
        {
            internal List<TextContext> InvalidatedScopes { get; } = [];

            public void Invalidate(TextContext context, DateTime changedOnUtc)
            {
                if (!cache.Invalidate(context, changedOnUtc))
                    throw new InvalidOperationException("The synthetic cache rejected the newer settings version.");
                InvalidatedScopes.Add(context);
            }
        }

        /// <summary>提供不變的 UTC 測試時刻，不建立或擁有計時器。</summary>
        private sealed class FixedClock(DateTime utcNow) : IChurchSettingsClock
        {
            public DateTime UtcNow { get; } = utcNow;
        }
    }
}
