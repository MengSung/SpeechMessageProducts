// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/ContextDictionary.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class ContextDictionary、class ContextEntry
// 主要成員：GetInMemoryDataContextSmallGroup、CleanupExpiredEntries、RemoveOldestEntries、Remove、Context、LastAccessTime
// 引用命名空間：ChurchReport.Tools、ChurchReport.Payments、LineMessagingProcessor.Workflows、Microsoft.AspNetCore.Http、Microsoft.Extensions.Caching.Memory、System、System.Collections.Concurrent、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Tools;
using ChurchReport.Payments;
using LineMessagingProcessor.Workflows;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.DependencyInjection;
using LineMessagingProcessor.Workflows;

namespace ChurchReport.Models
{
    /// <summary>
    /// 管理 InMemoryDataContextSmallGroup 的靜態字典
    /// ✅ Phase 4: 增加自動清理機制，避免記憶體洩漏
    /// </summary>
    public static class ContextDictionary
    {
        // ✅ 使用 ConcurrentDictionary 確保執行緒安全
        private static readonly ConcurrentDictionary<string, ContextEntry> _contextDictionary
            = new ConcurrentDictionary<string, ContextEntry>();

        // ✅ 清理計時器
        //
        // ⚠️【資源不變量】此欄位由靜態建構函式建立，而靜態建構函式只在型別「首次被觸碰」時執行。
        // 這是刻意的：本型別在生產程式碼中已無使用者（BaseChurchController 直接建立 request-scoped 的
        // InMemoryDataContextSmallGroup），目前只有測試會用到它。
        // 只要沒有人碰這個型別，靜態建構函式就不會執行，也就不會有一個每 5 分鐘喚醒一次、
        // 且永遠不會被 Dispose 的常駐計時器掛在行程上。
        //
        // 因此請「不要」在啟動路徑（Startup／Program／任何 hosted service）加入對 ContextDictionary
        // 任何成員的參考，否則會在完全不需要的情況下把這個計時器帶進生產環境。
        private static readonly Timer _cleanupTimer;

        // ✅ 過期時間（30 分鐘未存取即過期）
        private static readonly TimeSpan _expirationTime = TimeSpan.FromMinutes(30);

        // ✅ 清理間隔（每 5 分鐘清理一次）
        private static readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

        // ✅ 最大項目數限制
        private const int MaxItems = 1000;

        /// <summary>
        /// 靜態建構函式 - 初始化清理計時器
        /// </summary>
        static ContextDictionary()
        {
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, _cleanupInterval, _cleanupInterval);
        }

        /// <summary>
        /// 向後相容的靜態屬性（已標記為過時）
        /// </summary>
        [Obsolete("請使用 GetInMemoryDataContextSmallGroup 方法，此屬性將在未來版本移除")]
        public static Dictionary<String, InMemoryDataContextSmallGroup> StaticContextDictionary
        {
            get
            {
                // 轉換為 Dictionary 以保持向後相容
                return _contextDictionary.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Context
                );
            }
        }

        /// <summary>
        /// 取得或建立 InMemoryDataContextSmallGroup
        /// </summary>
        public static InMemoryDataContextSmallGroup GetInMemoryDataContextSmallGroup(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider)
        {
            try
            {
                var session = httpContextAccessor.HttpContext?.Session;
                if (session == null)
                {
                    throw new InvalidOperationException("Session is not available");
                }

                var key = session.Id;
                // 從 ASP.NET Core DI 取得中性的奉獻付款建單 adapter。
                // ContextDictionary 只負責把每個 session 的 manager 串起來，不應直接依賴 QPay 命名的相容 adapter。
                var donationPaymentCreateGatewayAdapter =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(IDonationPaymentCreateGatewayAdapter))
                        as IDonationPaymentCreateGatewayAdapter;
                var lineNotificationWorkflow =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(ILineNotificationWorkflow))
                        as ILineNotificationWorkflow;
                var lineReplyWorkflow =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(ILineReplyWorkflow))
                        as ILineReplyWorkflow;

                // ✅ 使用 GetOrAdd 確保執行緒安全
                var entry = _contextDictionary.GetOrAdd(key, k =>
                {
                    // ✅ 檢查是否超過最大項目數
                    if (_contextDictionary.Count >= MaxItems)
                    {
                        // 強制清理過期項目
                        CleanupExpiredEntries(null);

                        // 如果仍然超過限制，清理最舊的項目
                        if (_contextDictionary.Count >= MaxItems)
                        {
                            RemoveOldestEntries(_contextDictionary.Count - MaxItems + 100);
                        }
                    }

                    return new ContextEntry
                    {
                        Context = new InMemoryDataContextSmallGroup(
                            httpContextAccessor,
                            memoryCache,
                            toolUtilityProvider,
                            donationPaymentCreateGatewayAdapter,
                            lineNotificationWorkflow,
                            lineReplyWorkflow),
                        LastAccessTime = DateTime.UtcNow
                    };
                });

                // ✅ 更新最後存取時間
                entry.LastAccessTime = DateTime.UtcNow;

                return entry.Context;
            }
            catch (System.Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[ContextDictionary] Error: {e.Message}");
                throw;
            }
        }

        /// <summary>
        /// 清理過期的項目
        /// </summary>
        private static void CleanupExpiredEntries(object state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var keysToRemove = _contextDictionary
                    .Where(kvp => (now - kvp.Value.LastAccessTime) > _expirationTime)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    if (_contextDictionary.TryRemove(key, out var removed))
                    {
                        // ✅ 釋放資源
                        (removed.Context as IDisposable)?.Dispose();
                        System.Diagnostics.Debug.WriteLine($"[ContextDictionary] Removed expired entry: {key}");
                    }
                }

                if (keysToRemove.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[ContextDictionary] Cleaned up {keysToRemove.Count} expired entries. Current count: {_contextDictionary.Count}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ContextDictionary] Cleanup error: {ex.Message}");
            }
        }

        /// <summary>
        /// 移除最舊的項目
        /// </summary>
        private static void RemoveOldestEntries(int count)
        {
            var oldestEntries = _contextDictionary
                .OrderBy(kvp => kvp.Value.LastAccessTime)
                .Take(count)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in oldestEntries)
            {
                if (_contextDictionary.TryRemove(key, out var removed))
                {
                    (removed.Context as IDisposable)?.Dispose();
                }
            }

            System.Diagnostics.Debug.WriteLine($"[ContextDictionary] Removed {oldestEntries.Count} oldest entries");
        }

        /// <summary>
        /// 手動移除指定的 Session
        /// </summary>
        public static void Remove(string sessionId)
        {
            if (_contextDictionary.TryRemove(sessionId, out var removed))
            {
                (removed.Context as IDisposable)?.Dispose();
            }
        }

        /// <summary>
        /// 取得目前的項目數
        /// </summary>
        public static int Count => _contextDictionary.Count;

        /// <summary>
        /// 內部類別：包含 Context 和最後存取時間
        /// </summary>
        private class ContextEntry
        {
            public InMemoryDataContextSmallGroup Context { get; set; }
            public DateTime LastAccessTime { get; set; }
        }
    }
}
