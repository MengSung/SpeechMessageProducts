// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/CollectionOperations/ICollectionQueryService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface ICollectionQueryService、class PagedResult
// 主要成員：Entities、TotalCount、MoreRecords、PagingCookie
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、System、System.Collections.Generic、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.CollectionOperations
{
    /// <summary>
    /// 集合查詢服務介面
    /// 提供同步與非同步查詢方法
    /// </summary>
    public interface ICollectionQueryService
    {
        #region 同步方法 (向下相容)

        /// <summary>
        /// 根據欄位查詢實體集合 (同步)
        /// </summary>
        EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue);

        /// <summary>
        /// 查詢週報資料 (同步)
        /// </summary>
        EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId);

        #endregion

        #region 非同步方法 (新增)

        /// <summary>
        /// 根據欄位查詢實體集合 (非同步)
        /// </summary>
        Task<EntityCollection> RetrieveEntityCollectionByFieldAsync(
            string entityName,
            string fieldName,
            string fieldValue,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 根據單一條件查詢實體集合 (非同步)
        /// </summary>
        Task<EntityCollection> RetrieveEntityCollectionByConditionAsync(
            string entityName,
            string fieldName,
            ConditionOperator conditionOperator,
            object value,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 根據多重條件查詢實體集合 (非同步)
        /// </summary>
        Task<EntityCollection> RetrieveEntityCollectionByConditionsAsync(
            string entityName,
            Dictionary<string, object> conditions,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 查詢週報資料 (非同步)
        /// </summary>
        Task<EntityCollection> QueryWeeklyReportBeforeTowMonthOfSundayAsync(
            DateTime aSunday,
            Guid aListEntityId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 分頁查詢實體集合 (非同步)
        /// </summary>
        Task<PagedResult<Entity>> RetrievePagedEntitiesAsync(
            string entityName,
            FilterExpression filter = null,
            ColumnSet columnSet = null,
            int pageSize = 100,
            string pagingCookie = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量查詢實體 (使用 IN 條件，非同步)
        /// </summary>
        Task<EntityCollection> RetrieveBatchByIdsAsync(
            string entityName,
            string idFieldName,
            IEnumerable<Guid> ids,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 批次根據 ID 列表查詢實體（避免 N+1 查詢）
        /// ? Phase 3.1: 批次查詢，效能提升 10-100倍
        /// </summary>
        Task<Dictionary<Guid, Entity>> RetrieveBatchByIdsAsync(
            string entityName,
            List<Guid> entityIds,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 批次根據欄位值查詢實體（使用 IN 條件）
        /// ? Phase 3.1: 批次查詢，避免循環查詢
        /// </summary>
        Task<EntityCollection> RetrieveBatchByFieldValuesAsync(
            string entityName,
            string fieldName,
            List<string> fieldValues,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default);

        #endregion
    }

    /// <summary>
    /// 分頁查詢結果模型
    /// </summary>
    public class PagedResult<T>
    {
        /// <summary>
        /// 實體列表
        /// </summary>
        public List<T> Entities { get; set; } = new List<T>();

        /// <summary>
        /// 總記錄數
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 是否有更多記錄
        /// </summary>
        public bool MoreRecords { get; set; }

        /// <summary>
        /// 分頁 Cookie
        /// </summary>
        public string PagingCookie { get; set; }
    }
}
