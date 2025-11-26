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
