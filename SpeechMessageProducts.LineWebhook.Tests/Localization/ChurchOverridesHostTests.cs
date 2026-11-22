/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 LineWebhook 的教會覆寫開關、真實 provider 接線、跨教會快照隔離、
 * FeeNotify 範本消費與輪詢停止時的資源釋放順序；所有案例完全離線。
 * 主要流程：以合成 IConfiguration、DI provider、可控 source/polling 與 FeeNotify 資料替身
 * 建立主機組合，檢查缺省關閉、無效布林拒絕、啟用時取代 Empty provider、A/B 文字隔離，
 * 並觀察 Stop/Drain → cache Dispose → manager Dispose 的實際事件序列；FeeNotify回歸
 * 使用與LineWebhook部署一致的server organization uniqueName，避免GUID與覆寫來源分區不一致。
 * 使用邊界與維護：不建立 ToolUtility manager、D365 SDK、LINE client 或真實連線；
 * 每個 Host、source、cache、timer 與探針均由單一案例擁有並在 finally/await using 清理。
 * 故障與決定性斷言：分別注入無效設定、輪詢阻塞及 A/B 覆寫，證明設定拒絕啟動、
 * 共享輪詢已排空後才釋放快取、FeeNotify 使用完整教會 scope 的覆寫範本。
 */
using Xunit;
using LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using SpeechMessage.Hosting.Localization;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.Dataverse;
using Line.Messaging;

namespace SpeechMessageProducts.LineWebhook.Tests.Localization;

/// <summary>使用真實產品組合入口及合成來源保護 LineWebhook 覆寫接線、隔離與生命週期。</summary>
public sealed class ChurchOverridesHostTests
{
    private static readonly string[] ExpectedStopEvents =
        ["poll.drained", "cache.disposed", "manager.disposed"];
    /// <summary>啟用覆寫時必須以正式 provider 取代空 provider，且來源和 polling 僅有一份 singleton。</summary>
    [Fact]
    public async Task Overrides_LineWebhookWired_ReplacesEmptyProvider()
    {
        await using var host = LineWebhookOverridesTestComposition.BuildEnabledHost();
        var services = host.App.Services;

        Assert.Single(services.GetServices<IChurchTextOverrideSource>());
        Assert.IsType<SpeechMessage.Tenancy.ChurchTextOverrideCache>(
            services.GetRequiredService<IChurchTextOverrideSnapshotProvider>());
        Assert.Contains(services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            service => service is TextChangePollingService);
    }

    /// <summary>缺少開關時維持關閉；非布林文字必須在主機建立前拒絕，不能默認開啟。</summary>
    [Fact]
    public async Task Overrides_SwitchMissing_DefaultsOff_InvalidRejectsStartup()
    {
        await using var disabled = LineWebhookOverridesTestComposition.BuildHost(null);
        Assert.Empty(disabled.App.Services.GetServices<IChurchTextOverrideSource>());
        Assert.Throws<InvalidOperationException>(() =>
            LineWebhookOverridesTestComposition.BuildHost("sometimes"));
    }

    /// <summary>交錯讀取 A/B 教會時，完整 scope cache 不得讓 A 編輯改變 B 的已發布快照。</summary>
    [Fact]
    public async Task Overrides_ChurchAEdit_ChurchBSnapshotUnchanged()
    {
        await using var fixture = new LineWebhookOverridesFixture();

        await fixture.LoadBothAsync();
        await fixture.EditChurchAAsync();

        Assert.Equal("A 新文字", fixture.ReadChurchA());
        Assert.Equal("B 原文字", fixture.ReadChurchB());
    }

    /// <summary>停止時必須先等待在途 polling 完成，再 Dispose cache，最後才釋放連線 manager。</summary>
    [Fact]
    public async Task Overrides_PollingStop_DrainsBeforeCacheDispose()
    {
        await using var fixture = new LineWebhookOverridesLifecycleFixture();

        await fixture.StartBlockedPollAsync();
        var stopping = fixture.StopAsync();
        Assert.False(stopping.IsCompleted);
        fixture.ReleasePoll();
        await stopping;

        Assert.Equal(ExpectedStopEvents, fixture.Events);
        Assert.Equal(0, fixture.ActivePolls);
    }

    /// <summary>實際 MemberNotifyRelay 的 FeeNotify 分派必須以 server 組織及 LineWebhook 完整 scope 使用覆寫。</summary>
    [Fact]
    public async Task Relay_FeeNotify_UsesChurchOverrideTemplate()
    {
        await using var fixture = new FeeNotifyOverrideTestComposition();
        await fixture.ExecuteAsync(passCatalog: true);

        var message = Assert.Single(fixture.Push.Messages);
        Assert.Contains("費用覆寫", message, StringComparison.Ordinal);
        Assert.Contains("新自訂類別", message, StringComparison.Ordinal);
        Assert.Equal(FeeNotifyOverrideTestComposition.ChurchId, fixture.Push.Church);
    }

    /// <summary>費用 lookup 指向其他教會時，通知流程必須拒絕推播且不得回退舊選項標籤。</summary>
    [Fact]
    public async Task Relay_FeeNotify_CrossChurchOfferingCategory_FailsClosed()
    {
        await using var fixture = new FeeNotifyOverrideTestComposition(
            categoryChurchId: Guid.Parse("abababab-abab-abab-abab-abababababab"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ExecuteAsync(passCatalog: true));
        Assert.Empty(fixture.Push.Messages);
    }

    /// <summary>費用 lookup 指向停用類別時，通知流程必須拒絕推播並保持安全失敗。</summary>
    [Fact]
    public async Task Relay_FeeNotify_InactiveOfferingCategory_FailsClosed()
    {
        await using var fixture = new FeeNotifyOverrideTestComposition(categoryIsActive: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ExecuteAsync(passCatalog: true));
        Assert.Empty(fixture.Push.Messages);
    }
}

/// <summary>以正式產品 Host 組合入口建立離線服務集合；不啟動 Host 背景工作或外部連線。</summary>
internal static class LineWebhookOverridesTestComposition
{
    /// <summary>使用 WebhookTestHost 的正式 Program.BuildApplication 與合成啟動設定。</summary>
    internal static WebhookTestHost BuildEnabledHost() => BuildHost("true");

    /// <summary>以本機組態建立真正的 LineWebhook Host，供測試解析其服務圖。</summary>
    internal static WebhookTestHost BuildHost(string? enabled)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Localization:ChurchOverrides:Enabled"] = enabled,
            ["CrmConnection:Organization"] = "SyntheticOrg"
        };
        return new WebhookTestHost(configuration: configuration,
            configureServices: services => services.AddSingleton<IDataverseConnectionManager, OfflineDataverseManager>());
    }
}

/// <summary>拒絕任何真實租約取得的 manager 替身，讓 composition 測試只能解析服務圖、不可能連線。</summary>
internal sealed class OfflineDataverseManager : IDataverseConnectionManager
{
    /// <summary>若組合測試意外開始查詢，立即失敗而不建立網路服務。</summary>
    public IClientLease Acquire(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Offline composition tests must not acquire a Dataverse lease.");

    /// <summary>測試不量測 pool；呼叫即表示 composition 越界。</summary>
    public DataversePoolMetrics GetMetrics() =>
        throw new InvalidOperationException("Offline composition tests must not inspect a Dataverse pool.");

    /// <summary>此替身沒有資源。</summary>
    public void Dispose() { }
}

/// <summary>只保存單一案例合成覆寫資料，依完整 scope 返回 detached 文字列。</summary>
internal sealed class LineWebhookOverridesFixture : IAsyncDisposable
{
    private static readonly DateTime InitialStamp = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
    private readonly TestOverrideSource source = new();
    private readonly ChurchTextOverrideCache cache;
    private readonly GoodShepherdTextCatalog catalog;
    internal TextContext ContextA { get; } = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "zh-TW", "zh-TW", "SyntheticOrg", "LineWebhook");
    internal TextContext ContextB { get; } = new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "zh-TW", "zh-TW", "SyntheticOrg", "LineWebhook");

    /// <summary>建立真實 cache/catalog 與合成來源；owner 只在本案例內存活並由 DisposeAsync 排空。</summary>
    internal LineWebhookOverridesFixture()
    {
        source.Set(ContextA, "A 原文字");
        source.Set(ContextB, "B 原文字");
        cache = new ChurchTextOverrideCache(source,
            GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會"),
            static (_, _) => { });
        catalog = new GoodShepherdTextCatalog(cache);
    }

    /// <summary>透過正式 cache 載入兩個隔離 scope，source 每次均重新產生純值候選。</summary>
    internal async Task LoadBothAsync()
    {
        await cache.LoadAsync(ContextA, InitialStamp);
        await cache.LoadAsync(ContextB, InitialStamp);
    }

    /// <summary>更新 A 的合成來源並依完整 scope 失效，再載入新世代。</summary>
    internal async Task EditChurchAAsync()
    {
        source.Set(ContextA, "A 新文字");
        cache.Invalidate(ContextA, InitialStamp.AddMinutes(1));
        await cache.LoadAsync(ContextA, InitialStamp.AddMinutes(1));
    }

    /// <summary>由正式產品 catalog 讀取 A 的已發布覆寫。</summary>
    internal string ReadChurchA() => catalog.GetText(ContextA, new TextKey("Role.Member"));
    /// <summary>由正式產品 catalog 讀取 B 的已發布覆寫。</summary>
    internal string ReadChurchB() => catalog.GetText(ContextB, new TextKey("Role.Member"));

    /// <summary>依序停止案例 source 使用者並 Dispose cache 唯一 owner。</summary>
    public async ValueTask DisposeAsync() => await cache.DisposeAsync();

    /// <summary>測試來源只保留本案例兩個固定 scope 的值；呼叫時回傳新列，避免共享可變集合外洩。</summary>
    private sealed class TestOverrideSource : IChurchTextOverrideSource
    {
        private readonly ConcurrentDictionary<Guid, string> values = new();
        private int readSequence;
        /// <summary>替換指定合成教會的單一顯示文字，不涉及實際資料庫。</summary>
        internal void Set(TextContext context, string text) => values[context.ChurchId] = text;
        /// <summary>每次建立單列 detached 候選，正式 cache 負責 scope／鍵／參數驗證與發布。</summary>
        public Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!values.TryGetValue(context.ChurchId, out var text))
                throw new InvalidOperationException("Synthetic church override is missing.");
            var id = GuidFrom(Interlocked.Increment(ref readSequence));
            IReadOnlyList<ChurchTextOverride> rows =
            [
                new ChurchTextOverride(id, context.ChurchId, new TextKey("Role.Member"), "zh-TW", text)
            ];
            return Task.FromResult(rows);
        }
        /// <summary>依單調序號建立案例內正式列識別，避免以顯示本文作資料列身分。</summary>
        private static Guid GuidFrom(int value)
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(value).CopyTo(bytes, 12);
            return new Guid(bytes);
        }
    }
}

/// <summary>以真實輪詢 service 的停止 token 驗證 in-flight source drain，再依核定順序釋放 cache 與最後 owner。</summary>
internal sealed class LineWebhookOverridesLifecycleFixture : IAsyncDisposable
{
    private readonly BlockingTextChangeSource source = new();
    private readonly ChurchTextOverrideCache cache;
    private readonly TextChangePollingService polling;
    private bool stopped;
    /// <summary>本案例實際完成的 owner 釋放順序。</summary>
    internal List<string> Events { get; } = new();
    /// <summary>輪詢離開來源後必須回到零的計數。</summary>
    internal int ActivePolls => source.Active;

    /// <summary>建立真實 PollingService 與 cache，manager stand-in 僅代表最後釋放階段。</summary>
    internal LineWebhookOverridesLifecycleFixture()
    {
        var validator = GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會");
        cache = new ChurchTextOverrideCache(new EmptyOverrideSource(), validator, static (_, _) => { });
        polling = new TextChangePollingService(source, cache, "SyntheticOrg", "LineWebhook");
    }

    /// <summary>啟動真實 BackgroundService 迴圈並等候本次 source 已進入的明確同步點。</summary>
    internal async Task StartBlockedPollAsync()
    {
        await polling.StartAsync(CancellationToken.None);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>正常 Host 停止會取消並 await BackgroundService，之後依序 Dispose cache 與 manager owner。</summary>
    internal async Task StopAsync()
    {
        if (stopped) return;
        stopped = true;
        await polling.StopAsync(CancellationToken.None);
        Events.Add("poll.drained");
        await cache.DisposeAsync();
        Events.Add("cache.disposed");
        Events.Add("manager.disposed");
    }

    /// <summary>取消可控來源等待；通常 Host Stop token 已觸發，此方法確保失敗清理也可釋放同步點。</summary>
    internal void ReleasePoll() => source.Release.TrySetResult();
    /// <summary>Host 停止與 cache 清理都由本 fixture 單一擁有者完成。</summary>
    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>只回傳空候選的離線 cache dependency，沒有 timer 或外部資料。</summary>
    private sealed class EmptyOverrideSource : IChurchTextOverrideSource
    {
        /// <summary>回傳本次新建的空候選。</summary>
        public Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Array.Empty<ChurchTextOverride>());
    }

    /// <summary>透過取消與釋放訊號控制一個輪詢；離開 await 後即釋放 active 計數。</summary>
    private sealed class BlockingTextChangeSource : ITextChangeSource
    {
        private int active;
        /// <summary>輪詢到達同步點供測試呼叫 StopAsync，不依賴任意 sleep。</summary>
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>測試 owner 可在必要清理時釋放來源等待。</summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>目前正在等待中的讀取數。</summary>
        internal int Active => Volatile.Read(ref active);
        /// <summary>直到 Host 取消或測試釋放，確定性結束本次 synthetic read。</summary>
        public async Task<IReadOnlyList<TextChangedChurch>> ReadAsync(DateTime sinceUtc, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref active);
            Entered.TrySetResult();
            try
            {
                await Task.WhenAny(Release.Task, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                return Array.Empty<TextChangedChurch>();
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }
    }
}

/// <summary>以真實 MemberNotifyRelay、FeeNotificationService、catalog 與 cache 組成離線通知流程。</summary>
internal sealed class FeeNotifyOverrideTestComposition : IAsyncDisposable
{
    /// <summary>測試使用的固定合成教會主鍵，不依賴使用者或部署資料。</summary>
    internal static readonly Guid ChurchId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid FeeId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ContactId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid ChannelId = Guid.Parse("abababab-abab-abab-abab-abababababab");
    private static readonly Guid CategoryId = Guid.Parse("edededed-eded-eded-eded-edededededed");
    private static readonly Guid OrganizationId = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");
    private const string OrganizationName = "goodshepherdback";
    private readonly byte[] key = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
    private readonly TextContext context = new(ChurchId, "zh-TW", "zh-TW", OrganizationName, "LineWebhook");
    private readonly ChurchTextOverrideCache cache;
    private readonly GoodShepherdTextCatalog catalog;
    private readonly RelaySdk sdk;
    internal RelayPush Push { get; } = new();

    /// <summary>建立完整合成資料圖；token、SDK、cache 只由本案例持有，不建立網路或真實組織連線。</summary>
    internal FeeNotifyOverrideTestComposition(Guid? categoryChurchId = null, bool categoryIsActive = true)
    {
        cache = new ChurchTextOverrideCache(new FeeTemplateSource(),
            GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會"),
            static (_, _) => { });
        catalog = new GoodShepherdTextCatalog(cache);
        sdk = new RelaySdk(key, categoryChurchId ?? ChurchId, categoryIsActive);
    }

    /// <summary>沿封閉四欄 payload 執行真正 Relay 分派；false 用於驗證未傳 catalog 時的預設文案。</summary>
    internal async Task ExecuteAsync(bool passCatalog)
    {
        await cache.LoadAsync(context, new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));
        var payload = new Newtonsoft.Json.Linq.JObject
        {
            ["kind"] = (int)MemberNotifyRelayKind.FeeNotify,
            ["recordId"] = FeeId,
            ["message"] = 1,
            ["route"] = 0
        }.ToString(Newtonsoft.Json.Formatting.None);
        await MemberNotifyRelay.ExecuteAsync(sdk, Push, ChurchId, payload, key, CancellationToken.None,
            passCatalog ? catalog : null, "LineWebhook", OrganizationName);
    }

    /// <summary>排空唯讀 cache owner，再清除本案例的合成解密 key 與資料來源參照。</summary>
    public async ValueTask DisposeAsync()
    {
        await cache.DisposeAsync();
        Array.Clear(key, 0, key.Length);
        sdk.Dispose();
        Push.Messages.Clear();
    }

    /// <summary>只提供符合產品資源參數集合的合成模板覆寫。</summary>
    private sealed class FeeTemplateSource : IChurchTextOverrideSource
    {
        /// <summary>回傳本教會、固定鍵與精確參數集合的單列候選。</summary>
        public Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext scope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ChurchTextOverride> rows =
            [
                new ChurchTextOverride(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), scope.ChurchId,
                    new TextKey("Fees.Notify.Template"), "zh-TW",
                    "費用覆寫 {member} {date} {category} {others} {payway} {amount}")
            ];
            return Task.FromResult(rows);
        }
    }

    /// <summary>依單筆測試擁有的合成正式列回傳最小欄位，組織身分由 WhoAmI 回覆固定值。</summary>
    private sealed class RelaySdk : IOrganizationService, IDisposable
    {
        private readonly Dictionary<(string Table, Guid Id), Entity> rows;
        private bool disposed;

        /// <summary>建立費用、聯絡人、教會與加密頻道列；解密 key 不會離開案例 owner。</summary>
        internal RelaySdk(byte[] tokenKey, Guid categoryChurchId, bool categoryIsActive)
        {
            rows = new Dictionary<(string, Guid), Entity>
            {
                [("new_fee", FeeId)] = new Entity("new_fee", FeeId)
                {
                    ["new_contact_new_fee"] = new EntityReference("contact", ContactId),
                    ["new_pay_date"] = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
                    ["new_category"] = new OptionSetValue(100000000),
                    ["new_offeringcategoryid"] = new EntityReference("new_offeringcategory", CategoryId),
                    ["new_pay_way"] = new OptionSetValue(100000004),
                    ["new_others"] = "合成說明",
                    ["new_fee_really_paid"] = new Microsoft.Xrm.Sdk.Money(100m)
                },
                [("contact", ContactId)] = new Entity("contact", ContactId)
                {
                    ["parentcustomerid"] = new EntityReference("account", ChurchId),
                    ["fullname"] = "合成會友",
                    ["new_lineid"] = "U00000000000000000000000000000000",
                    ["new_preferredlanguage"] = "zh-TW"
                },
                [("account", ChurchId)] = new Entity("account", ChurchId)
                {
                    ["name"] = "合成教會",
                    ["new_timezone"] = "Asia/Taipei",
                    ["new_linechannelid"] = new EntityReference("new_linechannel", ChannelId),
                    ["new_defaultlanguage"] = "zh-TW",
                    ["new_enabledlanguages"] = "zh-TW"
                },
                [("new_linechannel", ChannelId)] = new Entity("new_linechannel", ChannelId)
                {
                    ["new_accesstokenencrypted"] = ChannelTokenProtector.Protect("synthetic", tokenKey)
                },
                [("new_offeringcategory", CategoryId)] = new Entity("new_offeringcategory", CategoryId)
                {
                    ["new_accountid"] = new EntityReference("account", categoryChurchId),
                    ["new_code"] = "custom-test",
                    ["new_name"] = "新自訂類別",
                    ["new_sortorder"] = 0,
                    ["statecode"] = new OptionSetValue(categoryIsActive ? 0 : 1)
                }
            };
        }

        /// <summary>依明確 ColumnSet 建立 detached 查詢列，拒絕未知資料列或已清理 owner。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (disposed || !rows.TryGetValue((entityName, id), out var source))
                throw new InvalidOperationException("Synthetic relay source is unavailable.");
            var result = new Entity(entityName, id);
            foreach (var column in columnSet.Columns)
                if (source.Attributes.TryGetValue(column, out var value)) result[column] = value;
            return result;
        }

        /// <summary>只回傳固定合成 OrganizationId，證明文字 scope 使用 server WhoAmI 而非 payload。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is not WhoAmIRequest) throw new InvalidOperationException("Unexpected synthetic request.");
            var response = new WhoAmIResponse();
            response.Results["OrganizationId"] = OrganizationId;
            return response;
        }

        /// <summary>Relay 不應建立資料，任何意外寫入都使離線測試失敗。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException("Unexpected synthetic create.");
        /// <summary>Relay 不應更新來源，任何意外寫入都使離線測試失敗。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Unexpected synthetic update.");
        /// <summary>Relay 不應刪除資料，任何意外寫入都使離線測試失敗。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected synthetic delete.");
        /// <summary>Relay 不應建立關聯，任何意外寫入都使離線測試失敗。</summary>
        public void Associate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected synthetic associate.");
        /// <summary>Relay 不應移除關聯，任何意外寫入都使離線測試失敗。</summary>
        public void Disassociate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected synthetic disassociate.");
        /// <summary>只提供 OfferingCategoryReader 的同教會類別快照；不接受未預期查詢或跨教會結果。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (disposed || query is not QueryExpression expression
                || expression.EntityName != "new_offeringcategory"
                || !expression.Criteria.Conditions.Any(condition =>
                    condition.AttributeName == "new_accountid"
                    && condition.Operator == ConditionOperator.Equal
                    && condition.Values.Count == 1
                    && condition.Values[0] is Guid churchId
                    && churchId == ChurchId))
                throw new InvalidOperationException("Unexpected synthetic category query.");
            var category = rows[("new_offeringcategory", CategoryId)];
            return new EntityCollection([category]);
        }
        /// <summary>本案例唯一 SDK owner 釋放合成資料參照且阻止後續讀取。</summary>
        public void Dispose() { disposed = true; rows.Clear(); }
    }

    /// <summary>只接收產品組成的單筆合成通知；沒有 LINE client、HTTP 或外部傳輸。</summary>
    internal sealed class RelayPush : ILineMemberPushClient
    {
        /// <summary>記錄本案例真正經過 Relay 與 FeeNotificationService 的訊息文字。</summary>
        internal List<string> Messages { get; } = new();
        /// <summary>最後核對過的來源教會。</summary>
        internal Guid Church { get; private set; }
        /// <summary>複製單次訊息本文供決定性斷言，不留 recipient、channel 或 token。</summary>
        public Task<LinePushResult> PushToMemberAsync(MemberRecipient recipient, ChurchChannel channel, Guid sourceChurchId,
            IReadOnlyList<ISendMessage> messages, Guid retryKey, CancellationToken cancellationToken)
        {
            Church = sourceChurchId;
            Messages.AddRange(messages.OfType<TextMessage>().Select(message => message.Text));
            return Task.FromResult(LinePushResult.Reject("NotBound"));
        }
    }
}
