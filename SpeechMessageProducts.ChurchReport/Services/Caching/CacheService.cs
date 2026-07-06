// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Caching/CacheService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class CacheService
// 主要成員：Remove、RemoveByPrefix、RemoveMultiple、Exists、GetTrackedKeys、GetStatistics、ConfigureCacheEntry、ConfigureCacheEntryOptions、TrackKey、UntrackKey
// 引用命名空間：System、System.Collections.Concurrent、System.Collections.Generic、System.Linq、System.Threading、System.Threading.Tasks、Microsoft.Extensions.Caching.Memory、Microsoft.Extensions.Logging
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ChurchReport.Services.Caching
{
    /// <summary>
    /// 快取服務實作
    /// Phase 2.2: 提供 Memory Cache 的封裝與管理功能
    ///
    /// 功能特性:
    /// - 支援同步與非同步操作
    /// - 追蹤所有快取鍵（支援按前綴清除）
    /// - 提供快取統計資訊
    /// - 執行緒安全
    /// - 自動處理快取過期
    /// </summary>
    public class CacheService : ICacheService
    {
        #region 私有欄位

        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<CacheService> _logger;

        /// <summary>追蹤所有快取鍵（執行緒安全）</summary>
        private readonly ConcurrentDictionary<string, DateTime> _trackedKeys = new();

        /// <summary>統計計數器</summary>
        private long _hitCount;
        private long _missCount;

        /// <summary>預設過期時間</summary>
        private static readonly TimeSpan DefaultAbsoluteExpiration = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DefaultSlidingExpiration = TimeSpan.FromMinutes(5);

        #endregion

        #region 建構函式

        /// <summary>
        /// 建立快取服務實例
        /// </summary>
        /// <param name="memoryCache">記憶體快取</param>
        /// <param name="logger">日誌記錄器</param>
        public CacheService(IMemoryCache memoryCache, ILogger<CacheService> logger)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region 同步方法

        /// <inheritdoc/>
        public T GetOrCreate<T>(
            string key,
            Func<T> factory,
            TimeSpan? absoluteExpiration = null,
            TimeSpan? slidingExpiration = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            return _memoryCache.GetOrCreate(key, entry =>
            {
                // 設定快取選項
                ConfigureCacheEntry(entry, absoluteExpiration, slidingExpiration);

                // 追蹤快取鍵
                TrackKey(key);

                // 記錄未命中
                Interlocked.Increment(ref _missCount);

                _logger.LogDebug("[CacheService] 快取建立: {Key}", key);

                // 執行工廠方法取得資料
                return factory();
            });
        }

        /// <inheritdoc/>
        public bool TryGet<T>(string key, out T value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                value = default;
                return false;
            }

            var found = _memoryCache.TryGetValue(key, out value);

            if (found)
            {
                Interlocked.Increment(ref _hitCount);
                _logger.LogTrace("[CacheService] 快取命中: {Key}", key);
            }
            else
            {
                Interlocked.Increment(ref _missCount);
                _logger.LogTrace("[CacheService] 快取未命中: {Key}", key);
            }

            return found;
        }

        /// <inheritdoc/>
        public void Set<T>(
            string key,
            T value,
            TimeSpan? absoluteExpiration = null,
            TimeSpan? slidingExpiration = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            var options = new MemoryCacheEntryOptions();
            ConfigureCacheEntryOptions(options, absoluteExpiration, slidingExpiration);

            // 註冊移除回呼
            options.RegisterPostEvictionCallback((k, v, reason, state) =>
            {
                UntrackKey(k.ToString());
                _logger.LogDebug("[CacheService] 快取移除: {Key}, 原因: {Reason}", k, reason);
            });

            _memoryCache.Set(key, value, options);
            TrackKey(key);

            _logger.LogDebug("[CacheService] 快取設定: {Key}", key);
        }

        #endregion

        #region 非同步方法

        /// <inheritdoc/>
        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? absoluteExpiration = null,
            TimeSpan? slidingExpiration = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            // 先嘗試從快取取得
            if (TryGet<T>(key, out var cachedValue))
            {
                return cachedValue;
            }

            // 快取未命中，執行工廠方法
            cancellationToken.ThrowIfCancellationRequested();

            var value = await factory().ConfigureAwait(false);

            // 設定快取
            Set(key, value, absoluteExpiration, slidingExpiration);

            return value;
        }

        /// <inheritdoc/>
        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? absoluteExpiration = null,
            TimeSpan? slidingExpiration = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Set(key, value, absoluteExpiration, slidingExpiration);
            return Task.CompletedTask;
        }

        #endregion

        #region 快取管理

        /// <inheritdoc/>
        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            _memoryCache.Remove(key);
            UntrackKey(key);

            _logger.LogDebug("[CacheService] 快取已移除: {Key}", key);
        }

        /// <inheritdoc/>
        public void RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                return;

            var keysToRemove = _trackedKeys.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keysToRemove)
            {
                Remove(key);
            }

            _logger.LogDebug("[CacheService] 已移除 {Count} 個前綴為 '{Prefix}' 的快取",
                keysToRemove.Count, prefix);
        }

        /// <inheritdoc/>
        public void RemoveMultiple(params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return;

            foreach (var key in keys)
            {
                Remove(key);
            }
        }

        /// <inheritdoc/>
        public bool Exists(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            return _memoryCache.TryGetValue(key, out _);
        }

        /// <inheritdoc/>
        public IEnumerable<string> GetTrackedKeys()
        {
            return _trackedKeys.Keys.ToList();
        }

        /// <inheritdoc/>
        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics
            {
                TrackedKeyCount = _trackedKeys.Count,
                ItemCount = _trackedKeys.Count,
                HitCount = Interlocked.Read(ref _hitCount),
                MissCount = Interlocked.Read(ref _missCount)
            };
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 設定快取項目選項
        /// </summary>
        private void ConfigureCacheEntry(
            ICacheEntry entry,
            TimeSpan? absoluteExpiration,
            TimeSpan? slidingExpiration)
        {
            entry.AbsoluteExpirationRelativeToNow = absoluteExpiration ?? DefaultAbsoluteExpiration;
            entry.SlidingExpiration = slidingExpiration ?? DefaultSlidingExpiration;
            entry.Priority = CacheItemPriority.Normal;

            // 註冊移除回呼
            entry.RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                UntrackKey(key.ToString());
                _logger.LogDebug("[CacheService] 快取過期移除: {Key}, 原因: {Reason}", key, reason);
            });
        }

        /// <summary>
        /// 設定快取選項
        /// </summary>
        private void ConfigureCacheEntryOptions(
            MemoryCacheEntryOptions options,
            TimeSpan? absoluteExpiration,
            TimeSpan? slidingExpiration)
        {
            options.AbsoluteExpirationRelativeToNow = absoluteExpiration ?? DefaultAbsoluteExpiration;
            options.SlidingExpiration = slidingExpiration ?? DefaultSlidingExpiration;
            options.Priority = CacheItemPriority.Normal;
        }

        /// <summary>
        /// 追蹤快取鍵
        /// </summary>
        private void TrackKey(string key)
        {
            _trackedKeys.TryAdd(key, DateTime.UtcNow);
        }

        /// <summary>
        /// 取消追蹤快取鍵
        /// </summary>
        private void UntrackKey(string key)
        {
            _trackedKeys.TryRemove(key, out _);
        }

        #endregion
    }
}
