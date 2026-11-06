/*
 * 檔案整體說明
 *
 * 目的與定位：CRM/Dataverse 客戶端抽象介面 目的：隔離 OrganizationServiceProxy (WCF) 與 ServiceClient 的差異 設計模式：Adapter Pattern
 *
 * 主要功能與流程：
 * 本檔宣告 ICrmClient。
 * - 執行 CRM 請求（例如：AssignRequest、SetStateRequest）
 * 功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs、
 * ToolUtility.Tests/DataOperations/ComplexQueryLifecycleTests.cs、
 * ToolUtility/Adapters/DataverseServiceClientAdapter.cs、ToolUtility/Adapters/LegacyOrganizationServiceAdapter.cs、
 * ToolUtility/Factories/CrmClientFactory.cs 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.Interfaces
{
    /// <summary>
    /// CRM/Dataverse 客戶端抽象介面
    /// 目的：隔離 OrganizationServiceProxy (WCF) 與 ServiceClient 的差異
    /// 設計模式：Adapter Pattern
    /// </summary>
    /// <remarks>
    /// 此介面遵循 Linus 原則：
    /// 1. 簡潔優先 - 只暴露必要的 CRUD + Execute 操作
    /// 2. 向後相容 - 保留同步方法，逐步加入 async
    /// 3. 可測試性 - 方便 mock 與單元測試
    /// </remarks>
    public interface ICrmClient : IDisposable
    {
        #region 同步方法 (向後相容)

        /// <summary>
        /// 建立實體
        /// </summary>
        /// <param name="entity">要建立的實體</param>
        /// <returns>新建立實體的 GUID</returns>
        Guid Create(Entity entity);

        /// <summary>
        /// 更新實體
        /// </summary>
        /// <param name="entity">要更新的實體（必須包含 Id）</param>
        void Update(Entity entity);

        /// <summary>
        /// 刪除實體
        /// </summary>
        /// <param name="entityName">實體邏輯名稱（例如：contact）</param>
        /// <param name="id">實體 GUID</param>
        void Delete(string entityName, Guid id);

        /// <summary>
        /// 取得單一實體
        /// </summary>
        /// <param name="entityName">實體邏輯名稱</param>
        /// <param name="id">實體 GUID</param>
        /// <param name="columnSet">要取得的欄位集合</param>
        /// <returns>實體物件</returns>
        Entity Retrieve(string entityName, Guid id, ColumnSet columnSet);

        /// <summary>
        /// 取得多筆實體
        /// </summary>
        /// <param name="query">查詢條件（QueryExpression / QueryByAttribute / FetchExpression）</param>
        /// <returns>實體集合</returns>
        EntityCollection RetrieveMultiple(QueryBase query);

        /// <summary>
        /// 執行 CRM 請求（例如：AssignRequest、SetStateRequest）
        /// </summary>
        /// <param name="request">請求物件</param>
        /// <returns>回應物件</returns>
        OrganizationResponse Execute(OrganizationRequest request);

        #endregion

        #region 非同步方法 (現代化)

        /// <summary>
        /// 非同步建立實體
        /// </summary>
        Task<Guid> CreateAsync(Entity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 非同步更新實體
        /// </summary>
        Task UpdateAsync(Entity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 非同步刪除實體
        /// </summary>
        Task DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 非同步取得單一實體
        /// </summary>
        Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet, CancellationToken cancellationToken = default);

        /// <summary>
        /// 非同步取得多筆實體
        /// </summary>
        Task<EntityCollection> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken = default);

        /// <summary>
        /// 非同步執行 CRM 請求
        /// </summary>
        Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken = default);

        #endregion

        #region 連線狀態與資訊

        /// <summary>
        /// 是否已連線
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// 組織名稱
        /// </summary>
        string OrganizationName { get; }

        #endregion
    }
}
