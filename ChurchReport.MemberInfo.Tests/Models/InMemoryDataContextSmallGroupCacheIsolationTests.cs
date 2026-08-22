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
/// 原本每次產生唯一 <c>NOSESSION</c> key 的保留風險，並以 <see cref="MemoryCache.Count"/> 證明
/// 請求外的短命資料不會被提升為跨 request、跨使用者可見的程序級狀態。<see cref="MemoryCache"/> 的
/// 唯一 owner 是本測試的 <c>using</c> 範圍，結束時會同步 Dispose，避免測試本身留下資源或快取項目。
/// </remarks>
[Collection("LegacyToolUtilityFactory")]
public sealed class InMemoryDataContextSmallGroupCacheIsolationTests
{
    /// <summary>
    /// 保護無 Session 路徑不會為每次 <see cref="InMemoryDataContextSmallGroup.ListManager"/> 存取建立
    /// 一筆程序級 cache entry 的契約。
    /// </summary>
    /// <remarks>
    /// 故障注入為明確使用 <see cref="HttpContextAccessor"/> 但不設定 <see cref="HttpContextAccessor.HttpContext"/>，
    /// 模擬 fire-and-forget 背景工作已離開 HTTP 管線的情況。決定性斷言為一千次存取之後
    /// <see cref="MemoryCache.Count"/> 仍為零；若重新出現以 Ticks 組成的唯一快取 key，本測試會保留
    /// 一千筆尚未過期的項目而立即失敗。
    /// </remarks>
    [Fact]
    public void ListManager_without_HttpContext_does_not_add_process_cache_entries_after_repeated_access()
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
            }

            memoryCache.Count.Should().Be(0,
                "無 Session 的短命狀態只能由目前 data context 持有，不能以每次唯一 key 寫入程序級快取");
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
        private bool _disposed;

        /// <summary>目前已提交且尚未移除的測試快取項目數。</summary>
        public int Count => _entries.Count;

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
        }

        /// <summary>由 entry 的 Dispose 提交值，對應真實 MemoryCache 的寫入完成時間。</summary>
        /// <param name="key">要提交的 key。</param>
        /// <param name="value">要提交的值。</param>
        internal void Commit(object key, object? value)
        {
            ThrowIfDisposed();
            _entries[key] = value;
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
                _owner.Commit(Key, Value);
            }
        }
    }
}
