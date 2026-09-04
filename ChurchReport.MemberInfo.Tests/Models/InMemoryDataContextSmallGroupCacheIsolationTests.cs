// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs
// 所屬區塊：ChurchReport 產品層回歸測試，驗證 Session、記憶體快取與背景執行環境的隔離契約。
// 檔案責任：固定無 HTTP Session 時的資料上下文不得將暫時狀態寫入程序級 IMemoryCache，防止背景工作或除錯評估造成跨 scope 的無界保留。
// 主要型別：InMemoryDataContextSmallGroupCacheIsolationTests、ThrowingToolUtilityProvider。
// 主要成員：ListManager_without_HttpContext_does_not_add_process_cache_entries_after_repeated_access。
// 引用命名空間：ChurchReport.Models、FluentAssertions、Microsoft.AspNetCore.Http、Microsoft.Extensions.Caching.Memory、ToolUtilityNameSpace.DependencyInjection、Xunit。
// 閱讀路徑：先讀測試名稱與 Arrange/Act/Assert，了解本測試保護的無 Session 快取生命週期，再閱讀測試替身的失敗保護。
// 維護重點：不可將斷言改為只驗證後備物件可建立；關鍵契約是重複存取不增加程序級快取項目數。
// 行為保護：測試不建立真實 CRM 用戶端、連線、工作執行緒或背景工作；所有資源只由 using 範圍持有並在測試結束時確定釋放。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，並以 CRLF 結尾。
// ============================================================================
using ChurchReport.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;
using System.Reflection;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using ToolUtilityNameSpace.Factory;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Models;

/// <summary>
/// 驗證 <see cref="InMemoryDataContextSmallGroup"/> 在沒有 HTTP Session 的執行環境中，
/// 仍能提供只屬於目前 data context 的後備資料，而不污染程序級記憶體快取。
/// </summary>
/// <remarks>
/// 背景工作、非 HTTP 執行緒與除錯評估皆可能沒有 <see cref="HttpContext"/>。此測試以一千次存取重現
/// 原本以 <c>NOSESSION</c> 或唯一 key 寫入程序級快取的保留風險，並以 <see cref="MemoryCache.Count"/> 證明
/// 請求外的短命資料不會被提升為跨 request、跨使用者可見的程序級狀態。<see cref="MemoryCache"/> 的
/// 唯一 owner 是本測試的 <c>using</c> 範圍，結束時會同步 Dispose，避免測試本身留下資源或快取項目。
/// </remarks>
[Collection("LegacyToolUtilityFactory")]
public sealed class InMemoryDataContextSmallGroupCacheIsolationTests
{
    /// <summary>
    /// 保護每 request 建立的付款管理器及其 LINE client 具有明確的 Dispose 所有權。
    /// </summary>
    /// <remarks>
    /// 本測試先以型別契約捕捉「context 改為 scoped 後卻沒有釋放內部 HttpClient」的回歸；
    /// 若任一型別缺少 IDisposable，DI scope 結束時就沒有確定性清理路徑，會形成可重現的
    /// socket/managed-resource retention。此檢查不建立網路連線或真實付款流程。
    /// </remarks>
    [Fact]
    public void Request_owned_payment_components_must_expose_deterministic_disposal_contract()
    {
        typeof(InMemoryDataContextSmallGroup).GetInterfaces()
            .Should().Contain(typeof(IDisposable));
        typeof(DonationPaymentManager).GetInterfaces()
            .Should().Contain(typeof(IDisposable));
    }

    /// <summary>
    /// 保護所有 Session 資料 getter 在無 Session 路徑不會建立程序級 cache entry 的契約。
    /// </summary>
    /// <remarks>
    /// 故障注入為明確使用 <see cref="HttpContextAccessor"/> 但不設定 <see cref="HttpContextAccessor.HttpContext"/>，
    /// 模擬 fire-and-forget 背景工作已離開 HTTP 管線的情況。決定性斷言為一千次存取之後
    /// <see cref="MemoryCache.Count"/> 仍為零；若重新出現以 Ticks 組成的唯一快取 key，本測試會保留
    /// 一千筆尚未過期的項目而立即失敗。
    /// </remarks>
    [Fact]
    public void Session_state_getters_without_HttpContext_do_not_add_process_cache_entries_after_repeated_access()
    {
        var factoryState = ConfigureLegacyToolUtilityFactoryForModelConstruction();
        try
        {
            var accessor = new HttpContextAccessor
            {
                HttpContext = null
            };
            using var memoryCache = new CountingMemoryCache();
            var context = new InMemoryDataContextSmallGroup(
                accessor,
                memoryCache,
                new ThrowingToolUtilityProvider());

            accessor.HttpContext.Should().BeNull("本案例必須涵蓋無 HTTP Session 的背景執行環境");

            for (var index = 0; index < 1_000; index++)
            {
                context.ListManager.Should().NotBeNull("無 Session 路徑仍必須能提供目前資料上下文專屬的後備物件");
                context.SmallGroupDataList.Should().NotBeNull();
                context.WeeklyReportData.Should().NotBeNull();
                context.NewPersonModel.Should().NotBeNull();
                context.PersonalInfomationModel.Should().NotBeNull();
                context.LineBindingViewModel.Should().NotBeNull();
                context.AppointmentsListManager.Should().NotBeNull();
            }

            memoryCache.Count.Should().Be(0,
                "無 Session 的短命狀態只能由目前 data context 持有，不能以 NOSESSION 或每次唯一 key 寫入程序級快取");
        }
        finally
        {
            ResetLegacyToolUtilityFactory();
            ClearLegacyToolUtilityFactoryStatics();
            factoryState.Tracer.Dispose();
            factoryState.Provider.Dispose();
        }
    }

    /// <summary>
    /// 保護需要 request-scoped ToolUtility 的小組管理器不會被提升到程序級快取。
    /// </summary>
    /// <remarks>
    /// 故障注入使用沒有 HTTP Session 的背景執行環境，並以可追蹤的 provider 建立四種
    /// 需要 CRM 工具的管理器。決定性斷言是重複存取後快取仍為空，且同一 context 內
    /// 仍重用自己的管理器；若把管理器寫入 IMemoryCache，已完成的 DI scope 便可能在
    /// 下一個 request 被重用，本測試會立即捕捉該生命週期違約。
    /// </remarks>
    [Fact]
    public void Scoped_tool_utility_managers_are_context_local_and_never_enter_process_cache()
    {
        var factoryState = ConfigureLegacyToolUtilityFactoryForModelConstruction();
        try
        {
            var accessor = new HttpContextAccessor
            {
                HttpContext = null
            };
            using var memoryCache = new CountingMemoryCache();
            var context = new InMemoryDataContextSmallGroup(
                accessor,
                memoryCache,
                new NullToolUtilityProvider());

            var happyGroup = context.HappyGroupDataManager;
            var listManagement = context.ListManagementDataManager;
            var equipment = context.EquipmentDataManager;
            var feeList = context.FeeList;

            context.HappyGroupDataManager.Should().BeSameAs(happyGroup);
            context.ListManagementDataManager.Should().BeSameAs(listManagement);
            context.EquipmentDataManager.Should().BeSameAs(equipment);
            context.FeeList.Should().BeSameAs(feeList);
            memoryCache.Count.Should().Be(0,
                "持有 scoped ToolUtility 的管理器只能由目前 data context 擁有，不能留在程序級快取");
        }
        finally
        {
            ResetLegacyToolUtilityFactory();
            ClearLegacyToolUtilityFactoryStatics();
            factoryState.Tracer.Dispose();
            factoryState.Provider.Dispose();
        }
    }

    /// <summary>
    /// 同時保護 scoped manager 的生命週期與純 Session 資料的跨 request 快取契約。
    /// </summary>
    /// <remarks>
    /// 故障注入建立兩個共用同一個可寫 Session 的 context，模擬連續頁面 request；舊實作會把 manager
    /// 與其 scoped ToolUtility/adapter/workflow 一起存入程序級快取。決定性斷言為這些 manager 不共享參考，
    /// 純資料圖則可跨同一 Session 重用且具有 30 分鐘絕對與滑動到期。第三個不同 Session 證明資料圖
    /// 不會跨使用者重用。
    /// </remarks>
    [Fact]
    public void Scoped_tool_utility_managers_with_a_session_are_not_retained_or_reused_across_contexts()
    {
        var factoryState = ConfigureLegacyToolUtilityFactoryForModelConstruction();
        try
        {
            using var memoryCache = new CountingMemoryCache();
            var sharedSession = new TestSession("session-scoped-manager-lifecycle");
            var first = CreateSessionContext(memoryCache, sharedSession);
            var second = CreateSessionContext(memoryCache, sharedSession);

            first.Context.HappyGroupDataManager.Should().NotBeSameAs(second.Context.HappyGroupDataManager);
            first.Context.ListManagementDataManager.Should().NotBeSameAs(second.Context.ListManagementDataManager);
            first.Context.EquipmentDataManager.Should().NotBeSameAs(second.Context.EquipmentDataManager);
            first.Context.FeeList.Should().NotBeSameAs(second.Context.FeeList);
            first.Context.PollManager.Should().NotBeSameAs(second.Context.PollManager);

            first.Context.ListManager.Should().BeSameAs(second.Context.ListManager);
            first.Context.SmallGroupDataList.Should().BeSameAs(second.Context.SmallGroupDataList);
            first.Context.WeeklyReportData.Should().BeSameAs(second.Context.WeeklyReportData);
            first.Context.NewPersonModel.Should().BeSameAs(second.Context.NewPersonModel);
            first.Context.PersonalInfomationModel.Should().BeSameAs(second.Context.PersonalInfomationModel);
            first.Context.LineBindingViewModel.Should().BeSameAs(second.Context.LineBindingViewModel);
            first.Context.AppointmentsListManager.Should().BeSameAs(second.Context.AppointmentsListManager);

            memoryCache.Count.Should().Be(7,
                "只有不保存 scoped provider、adapter 或 workflow 的七個 Session 資料圖可跨同一 Session 快取");
            memoryCache.Lifetimes.Should().OnlyContain(lifetime =>
                lifetime.AbsoluteExpiration.HasValue
                && lifetime.SlidingExpiration == TimeSpan.FromMinutes(30)
                && lifetime.AbsoluteExpiration.Value > DateTimeOffset.Now.AddMinutes(29)
                && lifetime.AbsoluteExpiration.Value <= DateTimeOffset.Now.AddMinutes(31),
                "跨 request 資料圖必須同時具備硬性 30 分鐘絕對與滑動到期，避免無界保留");

            var otherSession = CreateSessionContext(memoryCache, new TestSession("another-session"));
            otherSession.Context.ListManager.Should().NotBeSameAs(first.Context.ListManager);
            otherSession.Context.SmallGroupDataList.Should().NotBeSameAs(first.Context.SmallGroupDataList);
            otherSession.Context.WeeklyReportData.Should().NotBeSameAs(first.Context.WeeklyReportData);
            otherSession.Context.NewPersonModel.Should().NotBeSameAs(first.Context.NewPersonModel);
            otherSession.Context.PersonalInfomationModel.Should().NotBeSameAs(first.Context.PersonalInfomationModel);
            otherSession.Context.LineBindingViewModel.Should().NotBeSameAs(first.Context.LineBindingViewModel);
            otherSession.Context.AppointmentsListManager.Should().NotBeSameAs(first.Context.AppointmentsListManager);
            memoryCache.Count.Should().Be(14,
                "不同 Session 必須得到另一組完整隔離的資料圖，而非讀取第一個 Session 的項目");
        }
        finally
        {
            ResetLegacyToolUtilityFactory();
            ClearLegacyToolUtilityFactoryStatics();
            factoryState.Tracer.Dispose();
            factoryState.Provider.Dispose();
        }
    }

    /// <summary>
    /// 保護跨 Session 資料圖快取具有全域硬上限，而不是只依賴到期掃描。
    /// </summary>
    /// <remarks>
    /// 故障注入建立超過產品上限的獨立 Session，並只讀取一個可跨 request 的資料圖；
    /// 決定性斷言是測試快取的項目數不得超過 4,096。這證明在五分鐘到期掃描尚未發生
    /// 前，流量尖峰也不會讓 Session 專屬物件圖無界保留。每個 context 與測試快取都由
    /// 本測試方法擁有，結束後立即失去可達性，不保存 CRM、帳密或背景資源。
    /// </remarks>
    [Fact]
    public void Session_data_graph_cache_keeps_a_hard_global_entry_bound_under_many_sessions()
    {
        var factoryState = ConfigureLegacyToolUtilityFactoryForModelConstruction();
        try
        {
            using var memoryCache = new CountingMemoryCache();

            for (var index = 0; index < 700; index++)
            {
                var session = new TestSession($"many-session-{index:D4}");
                var context = CreateSessionContext(memoryCache, session).Context;
                context.ListManager.Should().NotBeNull();
                context.SmallGroupDataList.Should().NotBeNull();
                context.WeeklyReportData.Should().NotBeNull();
                context.NewPersonModel.Should().NotBeNull();
                context.PersonalInfomationModel.Should().NotBeNull();
                context.LineBindingViewModel.Should().NotBeNull();
                context.AppointmentsListManager.Should().NotBeNull();
            }

            memoryCache.Count.Should().BeLessOrEqualTo(4_096,
                "Session 資料圖快取必須在到期掃描前維持固定硬上限，避免大量新 Session 造成記憶體無界成長");
        }
        finally
        {
            ResetLegacyToolUtilityFactory();
            ClearLegacyToolUtilityFactoryStatics();
            factoryState.Tracer.Dispose();
            factoryState.Provider.Dispose();
        }
    }

    /// <summary>
    /// 建立具可寫 Session 的 request-scoped data context，模擬跨頁但相同 Session 的兩次 request。
    /// </summary>
    /// <param name="memoryCache">程序級快取替身，用於確認沒有 manager 被提升到 process root。</param>
    /// <param name="session">由呼叫端持有的 Session；重用同一實例可模擬跨 request 的既有 Session 狀態。</param>
    /// <returns>供測試持有的 context 與不會共享 AsyncLocal 的 HTTP accessor。</returns>
    private static (InMemoryDataContextSmallGroup Context, FixedHttpContextAccessor Accessor) CreateSessionContext(
        IMemoryCache memoryCache,
        TestSession session)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(session));
        var accessor = new FixedHttpContextAccessor(httpContext);

        return (
            new InMemoryDataContextSmallGroup(accessor, memoryCache, new NullToolUtilityProvider()),
            accessor);
    }

    /// <summary>
    /// 為模型建構器提供不連線的 legacy Factory 設定。
    /// </summary>
    /// <remarks>
    /// <see cref="ListManager"/> 目前會建立 <c>ListSmallGroupWeeklyReport</c>，其相容建構路徑
    /// 會讀取 <see cref="ToolUtilityFactory"/>。測試只注入組態、無操作 tracer 與不持有 request
    /// 狀態的 ambient gateway；不建立真實 CRM client，也不把 Session 或 credential 寫入 static state。
    /// </remarks>
    private static (ServiceProvider Provider, NullToolUtilityTracer Tracer) ConfigureLegacyToolUtilityFactoryForModelConstruction()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmConnection:ServerUrl"] = "https://org.test/XRMServices/2011/Organization.svc",
                ["CrmConnection:Username"] = "test-user",
                ["CrmConnection:Password"] = "test-secret"
            })
            .Build();
        var scopeProvider = new ServiceCollection().BuildServiceProvider();

        ToolUtilityFactory.SetConfiguration(configuration);
        var tracer = new NullToolUtilityTracer();
        ToolUtilityFactory.SetTracer(tracer);
        ToolUtilityFactory.SetAmbientService(new AmbientGatewayOrganizationService(
            static () => null,
            scopeProvider.GetRequiredService<IServiceScopeFactory>()));

        return (scopeProvider, tracer);
    }

    /// <summary>
    /// 以反射呼叫測試專用的 internal reset，清除 static Factory 的單例與 ambient 參考。
    /// </summary>
    /// <remarks>
    /// 反射只用於測試隔離；產品公開 API 與其資源所有權不被改變。若清理遺漏，後續測試可能
    /// 觀察到前一個測試的組態或服務提供者，形成跨測試狀態洩漏。
    /// </remarks>
    private static void ResetLegacyToolUtilityFactory()
    {
        var reset = typeof(ToolUtilityFactory).GetMethod(
            "ResetInstance",
            BindingFlags.Static | BindingFlags.NonPublic);
        reset.Should().NotBeNull();
        reset!.Invoke(null, null);
    }

    /// <summary>
    /// 清除測試寫入的 Factory 組態與 ambient 解析委派，避免已 Dispose 的測試 provider 被 static
    /// 欄位保留，造成後續測試或 process shutdown 的資源生命週期延長。
    /// </summary>
    private static void ClearLegacyToolUtilityFactoryStatics()
    {
        foreach (var fieldName in new[] { "_configuration", "_ambientService", "_tracer" })
        {
            var field = typeof(ToolUtilityFactory).GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.SetValue(null, null);
        }
    }

    /// <summary>
    /// 阻止測試在不應觸及 CRM 的快取隔離路徑意外取得 ToolUtility。
    /// </summary>
    /// <remarks>
    /// 此替身沒有連線、計時器、訂閱或可釋放資源；任何呼叫都會立即失敗，確保本測試只驗證
    /// <see cref="InMemoryDataContextSmallGroup.ListManager"/> 的記憶體快取所有權與隔離行為。
    /// </remarks>
    private sealed class ThrowingToolUtilityProvider : IToolUtilityProvider
    {
        /// <summary>
        /// 拒絕建立或取得 CRM 工具，因為本測試的無 Session 快取契約不需要外部連線。
        /// </summary>
        /// <returns>此方法永遠不會正常回傳。</returns>
        /// <exception cref="InvalidOperationException">指出測試範圍外的 CRM 依賴被意外觸及。</exception>
        public ToolUtilityClass GetToolUtility()
        {
            throw new InvalidOperationException("無 Session 的 ListManager 快取測試不應建立或使用 ToolUtility。");
        }
    }

    /// <summary>
    /// 提供不連線的 ToolUtility 測試替身，讓建構式測試只驗證管理器的生命週期所有權。
    /// </summary>
    /// <remarks>
    /// 本替身刻意回傳 null；測試不呼叫任何 CRM 方法，因此不建立連線、租約、背景工作或
    ///其他可釋放資源。若 getter 將 manager 放入程序級快取，快取項目數斷言仍會失敗。
    /// </remarks>
    private sealed class NullToolUtilityProvider : IToolUtilityProvider
    {
        /// <summary>
        /// 回傳僅供建構式使用的空工具參考。
        /// </summary>
        /// <returns>未使用的空 ToolUtility 參考。</returns>
        public ToolUtilityClass GetToolUtility() => null!;
    }

    /// <summary>
    /// 將測試 Session 提供給 DefaultHttpContext 的 feature，避免建立真實 Session store 或背景 IO。
    /// </summary>
    private sealed class TestSessionFeature : ISessionFeature
    {
        /// <summary>以測試擁有的短命 Session 建立 feature。</summary>
        /// <param name="session">只在目前測試 request 使用的記憶體 Session。</param>
        public TestSessionFeature(ISession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>目前 request 可讀寫的 Session。</summary>
        public ISession Session { get; set; }
    }

    /// <summary>
    /// 為每個測試 context 固定 HTTP context，避免真實 <see cref="HttpContextAccessor"/> 的靜態
    /// AsyncLocal 儲存槽讓兩個模擬 request 意外共用最後寫入的 context。
    /// </summary>
    /// <remarks>
    /// 這個替身不建立 scope、背景工作、計時器或 callback；它只保存呼叫端明確擁有的短命
    /// <see cref="HttpContext"/>。因此兩個測試 context 能並列驗證 Session partition，而不會測到
    /// 測試工具自身的 ambient 狀態洩漏。
    /// </remarks>
    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        /// <summary>以指定的 request context 建立不共用的 accessor。</summary>
        /// <param name="httpContext">只屬於這個測試 request 的 HTTP context。</param>
        public FixedHttpContextAccessor(HttpContext httpContext)
        {
            HttpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
        }

        /// <summary>目前測試 context 唯一可見的 HTTP context。</summary>
        public HttpContext? HttpContext { get; set; }
    }

    /// <summary>
    /// 不連線的記憶體 Session，保留 data context 產生隔離 key 與 dirty flag 所需的最小語意。
    /// </summary>
    /// <remarks>
    /// 字典的唯一 owner 是測試方法；沒有靜態保存、計時器、訂閱或背景提交。測試完成後所有內容均
    /// 失去可達性，因此不會把 Session 資料殘留給其他測試或使用者。
    /// </remarks>
    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        /// <summary>以穩定 ID 建立可用的測試 Session。</summary>
        /// <param name="id">測試指定的 Session 識別值。</param>
        public TestSession(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        /// <summary>此替身不模擬遠端失效，故在整個測試期間可用。</summary>
        public bool IsAvailable => true;

        /// <summary>目前測試 Session 的識別值。</summary>
        public string Id { get; }

        /// <summary>目前已寫入的 key；僅供 ASP.NET Core Session 擴充方法使用。</summary>
        public IEnumerable<string> Keys => _values.Keys;

        /// <summary>立即清空這個測試 Session 的所有短命資料。</summary>
        public void Clear() => _values.Clear();

        /// <summary>測試替身沒有遠端 store，提交立即完成。</summary>
        /// <param name="cancellationToken">取消權杖；此無 IO 實作不保留或註冊它。</param>
        /// <returns>已完成的工作。</returns>
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>測試替身沒有遠端 store，載入立即完成。</summary>
        /// <param name="cancellationToken">取消權杖；此無 IO 實作不保留或註冊它。</param>
        /// <returns>已完成的工作。</returns>
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>移除指定的短命 Session 值。</summary>
        /// <param name="key">要移除的 Session key。</param>
        public void Remove(string key) => _values.Remove(key);

        /// <summary>以複本保存值，避免呼叫端可變陣列跨邊界共享。</summary>
        /// <param name="key">Session key。</param>
        /// <param name="value">要保存的位元組資料。</param>
        public void Set(string key, byte[] value) => _values[key] = value?.ToArray() ?? Array.Empty<byte>();

        /// <summary>讀取已保存值的複本，避免測試 Session 暴露內部可變陣列。</summary>
        /// <param name="key">Session key。</param>
        /// <param name="value">命中時的資料複本；未命中時為空陣列。</param>
        /// <returns>是否找到值。</returns>
        public bool TryGetValue(string key, out byte[] value)
        {
            if (_values.TryGetValue(key, out var stored))
            {
                value = stored.ToArray();
                return true;
            }

            value = Array.Empty<byte>();
            return false;
        }
    }

    /// <summary>
    /// 以真實 <see cref="IMemoryCache"/> 寫入協定模擬程序級快取，並公開可決定性驗證的項目數。
    /// </summary>
    /// <remarks>
    /// <see cref="InMemoryDataContextSmallGroup"/> 透過 Microsoft 的 <c>Get</c>/<c>Set</c> 擴充方法
    /// 存取快取：<c>Set</c> 會建立 entry、設定值，最後在 entry Dispose 時提交。本替身忠實保留這個
    /// 提交時機，但刻意不實作到期計時器、回呼執行緒或背景清理，避免測試本身產生非決定性資源；
    /// <see cref="Dispose"/> 是唯一清理路徑，會立即清空所有測試項目。
    /// </remarks>
    private sealed class CountingMemoryCache : IMemoryCache
    {
        private readonly Dictionary<object, object?> _entries = new();
        private readonly Dictionary<object, (DateTimeOffset? AbsoluteExpiration, TimeSpan? SlidingExpiration)> _lifetimes = new();
        private bool _disposed;

        /// <summary>目前已提交且尚未移除的測試快取項目數。</summary>
        public int Count => _entries.Count;

        /// <summary>
        /// 所有已提交項目的到期設定，用於驗證跨 request 資料圖具有可預測的保留上限。
        /// </summary>
        /// <remarks>
        /// 此集合只保存到期中繼資料，不保存 Session、HttpContext、provider 或其他 request 資源；其 owner
        /// 仍是本測試替身，<see cref="Dispose"/> 會與 entries 一起清空，避免測試產生額外保留。
        /// </remarks>
        public IEnumerable<(DateTimeOffset? AbsoluteExpiration, TimeSpan? SlidingExpiration)> Lifetimes => _lifetimes.Values;

        /// <summary>依 key 取得已提交的值。</summary>
        /// <param name="key">由產品程式提供的快取 key。</param>
        /// <param name="value">命中時的值；未命中時為 <see langword="null"/>。</param>
        /// <returns>是否命中目前測試替身持有的項目。</returns>
        public bool TryGetValue(object key, out object? value)
        {
            ThrowIfDisposed();
            return _entries.TryGetValue(key, out value);
        }

        /// <summary>建立一個在 Dispose 時才提交的測試 cache entry。</summary>
        /// <param name="key">要寫入的快取 key。</param>
        /// <returns>由呼叫端填入值與選項的 entry。</returns>
        public ICacheEntry CreateEntry(object key)
        {
            ThrowIfDisposed();
            return new CountingCacheEntry(this, key);
        }

        /// <summary>移除指定 key，模擬 IMemoryCache 的明確失效語意。</summary>
        /// <param name="key">要移除的快取 key。</param>
        public void Remove(object key)
        {
            ThrowIfDisposed();
            _entries.Remove(key);
            _lifetimes.Remove(key);
        }

        /// <summary>
        /// 釋放測試快取並立即清空資料，避免測試間保留任何 request、使用者或模型參考。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _entries.Clear();
            _lifetimes.Clear();
        }

        /// <summary>由 entry 的 Dispose 提交值，對應真實 MemoryCache 的寫入完成時間。</summary>
        /// <param name="key">要提交的 key。</param>
        /// <param name="value">要提交的值。</param>
        /// <param name="absoluteExpiration">產品指定的絕對到期時間。</param>
        /// <param name="slidingExpiration">產品指定的滑動到期時間。</param>
        internal void Commit(
            object key,
            object? value,
            DateTimeOffset? absoluteExpiration,
            TimeSpan? slidingExpiration)
        {
            ThrowIfDisposed();
            _entries[key] = value;
            _lifetimes[key] = (absoluteExpiration, slidingExpiration);
        }

        /// <summary>避免已釋放的替身被誤用而把資料重新保留到測試外。</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CountingMemoryCache));
            }
        }

        /// <summary>
        /// 承載單次 Set 的選項與值，並在 Dispose 時由 owner 唯一提交。
        /// </summary>
        private sealed class CountingCacheEntry : ICacheEntry
        {
            private readonly CountingMemoryCache _owner;
            private bool _disposed;

            /// <summary>建立尚未提交的 entry，owner 是唯一允許寫入字典的資源擁有者。</summary>
            public CountingCacheEntry(CountingMemoryCache owner, object key)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                Key = key ?? throw new ArgumentNullException(nameof(key));
            }

            /// <summary>此 entry 的 key。</summary>
            public object Key { get; }

            /// <summary>呼叫端要在 Dispose 時提交的值。</summary>
            public object? Value { get; set; }

            /// <summary>保留產品設定的絕對到期時間；測試不啟動到期計時器。</summary>
            public DateTimeOffset? AbsoluteExpiration { get; set; }

            /// <summary>保留產品設定的相對絕對到期時間；測試不啟動背景排程。</summary>
            public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }

            /// <summary>保留產品設定的滑動到期時間；測試不啟動背景排程。</summary>
            public TimeSpan? SlidingExpiration { get; set; }

            /// <summary>保留產品提供的變更 token；本測試不註冊回呼，避免外部資源生命週期。</summary>
            public IList<IChangeToken> ExpirationTokens { get; } = new List<IChangeToken>();

            /// <summary>保留產品提供的 eviction 回呼設定；本測試不執行回呼。</summary>
            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } = new List<PostEvictionCallbackRegistration>();

            /// <summary>保留快取優先序設定。</summary>
            public CacheItemPriority Priority { get; set; }

            /// <summary>保留可選的大小設定。</summary>
            public long? Size { get; set; }

            /// <summary>
            /// 冪等地提交 entry；若產品未提供值，與 IMemoryCache 相同地保留 null 值的寫入意圖。
            /// </summary>
            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _owner.Commit(Key, Value, AbsoluteExpiration, SlidingExpiration);
            }
        }
    }
}
