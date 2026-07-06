// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Caching/ISmallGroupCacheManager.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface ISmallGroupCacheManager、class SmallGroupCacheStatistics
// 主要成員：HitCount、MissCount、TotalCacheCount、ClearCount
// 引用命名空間：System、System.Collections.Generic、System.Linq
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.Services.Caching
{
    /// <summary>
    /// 小組快取管理介面
    /// 提供小組相關資料的快取管理功能
    /// 支援單元測試模擬 (Mocking)
    /// </summary>
    public interface ISmallGroupCacheManager
    {
        /// <summary>
        /// 清除多小組相關的所有快取
        /// 包括圖表快取和列表快取
        /// </summary>
        /// <param name="selectedDate">選擇的日期</param>
        /// <param name="account">使用者帳號</param>
        void ClearMultiGroupCache(DateTime selectedDate, string account = null);

        /// <summary>
        /// 清除整合視圖相關的快取
        /// </summary>
        /// <param name="listId">清單 ID</param>
        /// <param name="selectedDate">選擇的日期</param>
        void ClearIntegrateCache(string listId, DateTime selectedDate);

        /// <summary>
        /// 清除特定小組的所有快取
        /// </summary>
        /// <param name="listId">清單 ID</param>
        /// <param name="selectedDate">選擇的日期</param>
        /// <param name="account">使用者帳號</param>
        void ClearSmallGroupCache(string listId, DateTime selectedDate, string account = null);

        /// <summary>
        /// 清除所有小組相關的快取
        /// 用於大規模資料更新後的清理
        /// </summary>
        void ClearAllSmallGroupCache();

        /// <summary>
        /// 取得多小組圖表快取鍵
        /// </summary>
        /// <param name="selectedDate">選擇的日期</param>
        /// <param name="account">使用者帳號</param>
        /// <returns>快取鍵</returns>
        string GetMultiGroupChartCacheKey(DateTime selectedDate, string account = null);

        /// <summary>
        /// 取得多小組列表快取鍵
        /// </summary>
        /// <param name="selectedDate">選擇的日期</param>
        /// <param name="account">使用者帳號</param>
        /// <returns>快取鍵</returns>
        string GetMultiGroupGridCacheKey(DateTime selectedDate, string account = null);

        /// <summary>
        /// 取得整合視圖快取鍵
        /// </summary>
        /// <param name="listId">清單 ID</param>
        /// <param name="selectedDate">選擇的日期</param>
        /// <returns>快取鍵</returns>
        string GetIntegrateCacheKey(string listId, DateTime selectedDate);

        /// <summary>
        /// 檢查快取是否存在
        /// </summary>
        /// <param name="cacheKey">快取鍵</param>
        /// <returns>True 表示快取存在</returns>
        bool CacheExists(string cacheKey);

        /// <summary>
        /// 取得快取統計資訊
        /// 用於監控和除錯
        /// </summary>
        /// <returns>快取統計資訊</returns>
        SmallGroupCacheStatistics GetCacheStatistics();
    }

    /// <summary>
    /// 小組快取統計資訊
    /// </summary>
    public class SmallGroupCacheStatistics
    {
        /// <summary>
        /// 快取命中次數
        /// </summary>
        public long HitCount { get; set; }

        /// <summary>
        /// 快取未命中次數
        /// </summary>
        public long MissCount { get; set; }

        /// <summary>
        /// 快取總數
        /// </summary>
        public int TotalCacheCount { get; set; }

        /// <summary>
        /// 快取命中率 (0-100)
        /// </summary>
        public double HitRate => HitCount + MissCount > 0
            ? (double)HitCount / (HitCount + MissCount) * 100
            : 0;

        /// <summary>
        /// 清除次數
        /// </summary>
        public long ClearCount { get; set; }
    }
}
