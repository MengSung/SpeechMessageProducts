using ChurchReport.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.DependencyInjection;

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
            IPayment aPaymentService, 
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
                            aPaymentService, 
                            toolUtilityProvider),
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
