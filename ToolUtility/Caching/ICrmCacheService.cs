// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Caching/ICrmCacheService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface ICrmCacheService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.Caching
{
    /// <summary>
    /// CRM 快取服務介面
    /// ? Phase 3.2: 定義快取操作契約
    /// </summary>
    public interface ICrmCacheService
    {
        #region 泛型快取方法

        /// <summary>
        /// 獲取或創建快取項目（非同步）
        /// </summary>
        Task<T> GetOrCreateAsync<T>(
            string cacheKey,
            Func<Task<T>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 獲取或創建快取項目（同步）
        /// </summary>
        T GetOrCreate<T>(
            string cacheKey,
            Func<T> factory,
            TimeSpan? expiration = null);

        #endregion

        #region 實體快取

        /// <summary>
        /// 快取單一實體
        /// </summary>
        Task<Entity> GetOrCreateEntityAsync(
            string entityName,
            Guid entityId,
            Func<Task<Entity>> factory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 快取實體集合
        /// </summary>
        Task<EntityCollection> GetOrCreateEntityCollectionAsync(
            string cacheKey,
            Func<Task<EntityCollection>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default);

        #endregion

        #region 查詢結果快取

        /// <summary>
        /// 快取查詢結果
        /// </summary>
        Task<EntityCollection> GetOrCreateQueryResultAsync(
            string queryKey,
            Func<Task<EntityCollection>> factory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 快取聯絡人查詢結果
        /// </summary>
        Task<Entity> GetOrCreateContactAsync(
            string identifier,
            string identifierType,
            Func<Task<Entity>> factory,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 快取名單成員集合
        /// </summary>
        Task<EntityCollection> GetOrCreateListMembersAsync(
            Guid listId,
            Func<Task<EntityCollection>> factory,
            CancellationToken cancellationToken = default);

        #endregion

        #region 快取失效

        /// <summary>
        /// 移除單一快取項目
        /// </summary>
        void Remove(string cacheKey);

        /// <summary>
        /// 移除實體快取
        /// </summary>
        void RemoveEntity(string entityName, Guid entityId);

        /// <summary>
        /// 移除多個相關快取
        /// </summary>
        void RemoveMultiple(params string[] cacheKeys);

        /// <summary>
        /// 移除名單相關的所有快取
        /// </summary>
        void InvalidateListCache(Guid listId);

        /// <summary>
        /// 移除聯絡人相關的所有快取
        /// </summary>
        void InvalidateContactCache(string identifier, string identifierType);

        #endregion

        #region 輔助方法

        /// <summary>
        /// 建立查詢快取鍵
        /// </summary>
        string BuildQueryCacheKey(string entityName, params object[] parameters);

        #endregion
    }
}
