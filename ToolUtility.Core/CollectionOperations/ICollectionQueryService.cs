/*
 * 檔案整體說明
 *
 * 目的與定位：純 SDK 集合查詢契約；授權及連線 scope 由 host 驗證與擁有，不保存 request／結果或跨租戶狀態。 Task 包裝仍執行同步 SDK；取消只在 I/O 前後檢查，不能中斷正在執行的同步呼叫。 
 * 單頁入口保留分頁 metadata；批次入口完整有界讀取，衝突與故障交最終 host 告警，不回部分成功。
 *
 * 主要功能與流程：
 * 本檔宣告 ICollectionQueryService、PagedResult。
 * - 沿用 AllColumns、欄位值及啟用條件，驗完整單頁正式身份；caller 擁有回傳結果及續頁責任。
 * - 透過 Execute 查指定主日前兩月週報，保留條件／排序；錯頁或故障不得偽裝空資料。
 * - 同步 SDK 的完成 Task 包裝；保留單頁及原例外，caller 取消前不執行 I/O，取消後不發布成功。
 * - 保留單條件與啟用條件；值為操作局部 SDK 型別，不作 XML 拼接或服務級快取。
 * 資料／選項欄位包含 ICollectionQueryService、Entities、TotalCount、MoreRecords、PagingCookie，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs、
 * ToolUtility.Tests/CoreTargetFrameworkTests.cs、ToolUtility.Core/CollectionOperations/CollectionQueryService.cs、
 * ToolUtility.Core/EntityOperations/EntityOptimizedQueryService.cs、ToolUtility/Core/ToolUtilityFacade.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.CollectionOperations
{
    /// <summary>
    /// 純 SDK 集合查詢契約；授權及連線 scope 由 host 驗證與擁有，不保存 request／結果或跨租戶狀態。
    /// Task 包裝仍執行同步 SDK；取消只在 I/O 前後檢查，不能中斷正在執行的同步呼叫。
    /// 單頁入口保留分頁 metadata；批次入口完整有界讀取，衝突與故障交最終 host 告警，不回部分成功。
    /// </summary>
    public interface ICollectionQueryService
    {
        #region 同步方法 (向下相容)

        /// <summary>
        /// 沿用 AllColumns、欄位值及啟用條件，驗完整單頁正式身份；caller 擁有回傳結果及續頁責任。
        /// </summary>
        EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue);

        /// <summary>
        /// 透過 Execute 查指定主日前兩月週報，保留條件／排序；錯頁或故障不得偽裝空資料。
        /// </summary>
        EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId);

        #endregion

        #region 非同步方法 (新增)

        /// <summary>
        /// 同步 SDK 的完成 Task 包裝；保留單頁及原例外，caller 取消前不執行 I/O，取消後不發布成功。
        /// </summary>
        Task<EntityCollection> RetrieveEntityCollectionByFieldAsync(
            string entityName,
            string fieldName,
            string fieldValue,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 保留單條件與啟用條件；值為操作局部 SDK 型別，不作 XML 拼接或服務級快取。
        /// </summary>
        Task<EntityCollection> RetrieveEntityCollectionByConditionAsync(
            string entityName,
            string fieldName,
            ConditionOperator conditionOperator,
            object value,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 保留多條件 AND 與啟用條件；caller 在同步執行期間須獨占字典，回傳完整驗證的單頁。
        /// </summary>
        Task<EntityCollection> RetrieveEntityCollectionByConditionsAsync(
            string entityName,
            Dictionary<string, object> conditions,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 與同步週報共用同一查詢；只由明確 caller token 判定正常取消，內部逾時仍屬故障。
        /// </summary>
        Task<EntityCollection> QueryWeeklyReportBeforeTowMonthOfSundayAsync(
            DateTime aSunday,
            Guid aListEntityId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 傳回一個有序完整頁與原 metadata；官方 XML cookie 的 page 指向上一頁，無 cookie 表第一頁。
        /// 沒有 SDK cookie 的後續頁改用實作類別明確 pageNumber 多載，不在服務保存游標或租戶狀態。
        /// </summary>
        Task<PagedResult<Entity>> RetrievePagedEntitiesAsync(
            string entityName,
            FilterExpression? filter = null,
            ColumnSet? columnSet = null,
            int pageSize = 100,
            string? pagingCookie = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 保留 caller 指定主鍵與啟用條件；最多 5000 個非空唯一 ID、5000 列與 1000 頁，超預算拒絕。
        /// 全分頁驗證後才回傳；缺少／不可見的記錄不補造，未請求 ID／重複 ID 必須失敗。
        /// </summary>
        Task<EntityCollection> RetrieveBatchByIdsAsync(
            string entityName,
            string idFieldName,
            IEnumerable<Guid> ids,
            ColumnSet? columnSet = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 沿用不含 statecode 的字典批次；null／空輸入回空，其他輸入及輸出有界且每個正式 ID 唯一。
        /// 同名不同 ID 均保留；不以 dictionary 索引覆寫掩蓋衝突，不宣稱假 SDK 證明實機加速。
        /// </summary>
        Task<Dictionary<Guid, Entity>> RetrieveBatchByIdsAsync(
            string entityName,
            List<Guid> entityIds,
            ColumnSet? columnSet = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 保留字串 IN 與啟用條件，null／空輸入不掃全表；最多 5000 輸入／輸出及 1000 頁。
        /// 結果只屬本次操作，不保存查詢值、Entity、cookie 或 SDK；後頁失敗不發布部分資料。
        /// </summary>
        Task<EntityCollection> RetrieveBatchByFieldValuesAsync(
            string entityName,
            string fieldName,
            List<string> fieldValues,
            ColumnSet? columnSet = null,
            CancellationToken cancellationToken = default);

        #endregion
    }

    /// <summary>
    /// 操作局部可變分頁 DTO，維持原公開 setter 以保留來源相容；不得用作 Session／共享快照。
    /// 所有 Entity 與 List 由此次 caller 擁有，服務不保存；若 host 快取必須另建足夠深的隔離複本。
    /// </summary>
    public class PagedResult<T>
    {
        /// <summary>
        /// 此次頁面獨占的清單；服務在發布前已驗證穩定 ID，caller 修改後不得宣稱仍為已驗共享快照。
        /// </summary>
        public List<T> Entities { get; set; } = new List<T>();

        /// <summary>
        /// SDK 原始 TotalRecordCount；未知值保留 -1，不把頁面筆數假裝全表總數。
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// SDK 表示有後續頁時為 true，caller 應繼續讀取，不把此頁當完整結果。
        /// </summary>
        public bool MoreRecords { get; set; }

        /// <summary>
        /// SDK 原始續頁 cookie，可為 null；只活至 caller 的此次分頁流程，不能跨授權 scope 重用。
        /// </summary>
        public string? PagingCookie { get; set; }
    }
}
