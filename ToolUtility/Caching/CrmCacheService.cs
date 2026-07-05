// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Caching/CrmCacheService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class CrmCacheService
// 主要成員：InvalidateAsync
// 引用命名空間：System、System.Threading、System.Threading.Tasks、Microsoft.Extensions.Caching.Distributed、Microsoft.Extensions.Caching.Memory、Microsoft.Extensions.Logging
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ToolUtility.Caching
{
    /// <summary>
    /// CRM 快取服務（骨架）
    /// - 採用 Cache-Aside 模式
    /// - 支援 MemoryCache 與 DistributedCache（例如 Redis）
    /// - 預設過期：滑動 5 分鐘 + 絕對 30 分鐘（可覆寫）
    /// </summary>
    public class CrmCacheService
    {
        private readonly IMemoryCache _memoryCache;
        private readonly IDistributedCache _distributedCache;
        private readonly ILogger<CrmCacheService> _logger;

        public CrmCacheService(IMemoryCache memoryCache,
                               IDistributedCache distributedCache,
                               ILogger<CrmCacheService> logger)
        {
            _memoryCache = memoryCache;
            _distributedCache = distributedCache;
            _logger = logger;
        }

        /// <summary>
        /// 取得或建立快取資料（優先使用 MemoryCache，再回退 DistributedCache）
        /// </summary>
        public async Task<T> GetOrCreateAsync<T>(string key,
                                                 Func<Task<T>> factory,
                                                 TimeSpan? memoryAbsoluteExpire = null,
                                                 TimeSpan? memorySlidingExpire = null,
                                                 TimeSpan? distributedAbsoluteExpire = null,
                                                 CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            // 先查 MemoryCache
            if (_memoryCache.TryGetValue(key, out T memoryValue))
            {
                return memoryValue;
            }

            // 再查 DistributedCache（僅適用可序列化資料，這裡示範用 JSON 字串，骨架不強制）
            try
            {
                var cachedBytes = await _distributedCache.GetAsync(key, cancellationToken);
                if (cachedBytes != null)
                {
                    // 注意：這裡僅作骨架，實作時請改為真正的序列化/反序列化
                    var json = System.Text.Encoding.UTF8.GetString(cachedBytes);
                    var fromJson = System.Text.Json.JsonSerializer.Deserialize<T>(json);
                    if (fromJson != null)
                    {
                        SetMemory(key, fromJson, memoryAbsoluteExpire, memorySlidingExpire);
                        return fromJson;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DistributedCache 取得快取失敗，將回退至資料來源 factory");
            }

            // 最終回資料來源
            var value = await factory();
            // 寫入 MemoryCache
            SetMemory(key, value, memoryAbsoluteExpire, memorySlidingExpire);
            // 寫入 DistributedCache
            await SetDistributedAsync(key, value, distributedAbsoluteExpire, cancellationToken);
            return value;
        }

        /// <summary>
        /// 嘗試取得 Memory 快取，不命中時回傳 false。
        /// </summary>
        public bool TryGetFromMemory<T>(string key, out T value)
        {
            return _memoryCache.TryGetValue(key, out value);
        }

        /// <summary>
        /// 寫入 MemoryCache
        /// </summary>
        public void SetMemory<T>(string key, T value,
                                 TimeSpan? absoluteExpire = null,
                                 TimeSpan? slidingExpire = null)
        {
            var options = new MemoryCacheEntryOptions();
            options.AbsoluteExpirationRelativeToNow = absoluteExpire ?? TimeSpan.FromMinutes(30);
            options.SlidingExpiration = slidingExpire ?? TimeSpan.FromMinutes(5);
            _memoryCache.Set(key, value, options);
        }

        /// <summary>
        /// 寫入 DistributedCache（JSON 序列化骨架）
        /// </summary>
        public async Task SetDistributedAsync<T>(string key, T value,
                                                 TimeSpan? absoluteExpire = null,
                                                 CancellationToken cancellationToken = default)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(value);
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = absoluteExpire ?? TimeSpan.FromMinutes(10)
                };
                await _distributedCache.SetAsync(key, bytes, options, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DistributedCache 寫入失敗");
            }
        }

        /// <summary>
        /// 失效指定 key 的快取（Memory + Distributed）
        /// </summary>
        public async Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                _memoryCache.Remove(key);
                await _distributedCache.RemoveAsync(key, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "快取失敗: {Key}", key);
            }
        }

        // ==================== 可優先快取的查詢建議（標記點位） ====================
        // - 名單/組織靜態資料（MemoryCache，30 分鐘）：
        //   ChurchListDataProcessor：教會列表、群組常數、部門清單
        // - 常用參照資料（MemoryCache，15-30 分鐘）：
        //   WeeklyReportManager：固定選項、分類清單
        // - 使用者資料（DistributedCache，10 分鐘）：
        //   PersonalInfomatioManager：個人檔案、身份綁定資訊
        // - 常用查詢結果（Query Result Cache，5 分鐘）：
        //   DownloadListManager、DownloadIntegrateData：列表查詢結果
        // - 頁面載入使用之高頻查詢：
        //   HomeController、DedicationController：首頁統計、奉獻類別、信用卡清單等
    }
}
