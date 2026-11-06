/*
 * 檔案整體說明
 *
 * 目的與定位：複雜查詢最小純SDK讀取邊界；不暴露IsReady、organization名稱、credential、ServiceClient或host生命週期。 host提供同一已授權scope的adapter，
 * 核心僅借用至Dispose／同步drain完成，不接管連線。
 *
 * 主要功能與流程：
 * 本檔宣告 ICrmQueryExecutor、OrganizationQueryExecutor。
 * - 單次讀取原表／ID／欄位；核心仍須驗證回傳身份，executor不負責建立授權。
 * - 單次原查詢單頁；保留metadata，候選只屬本次caller，不得跨scope cache。
 * - 執行原RetrieveMultipleRequest等查詢訊息；核心核對回應型別及候選，故障原樣傳遞。
 * - 只借入經host授權的SDK，不建立連線或持有其他request資訊。
 * 主要操作入口包含 Retrieve、RetrieveMultiple、Execute。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/QueryOperations/ComplexQueryDataService.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 複雜查詢最小純SDK讀取邊界；不暴露IsReady、organization名稱、credential、ServiceClient或host生命週期。
    /// host提供同一已授權scope的adapter，核心僅借用至Dispose／同步drain完成，不接管連線。
    /// </summary>
    public interface ICrmQueryExecutor
    {
        /// <summary>單次讀取原表／ID／欄位；核心仍須驗證回傳身份，executor不負責建立授權。</summary>
        Entity Retrieve(string entityName, Guid id, ColumnSet columns);

        /// <summary>單次原查詢單頁；保留metadata，候選只屬本次caller，不得跨scope cache。</summary>
        EntityCollection RetrieveMultiple(QueryBase query);

        /// <summary>執行原RetrieveMultipleRequest等查詢訊息；核心核對回應型別及候選，故障原樣傳遞。</summary>
        OrganizationResponse Execute(OrganizationRequest request);
    }

    /// <summary>純IOrganizationService讀取轉接；只有借入SDK參照，scope與真正Dispose仍屬host。</summary>
    public sealed class OrganizationQueryExecutor : ICrmQueryExecutor
    {
        private readonly IOrganizationService _service;

        /// <summary>只借入經host授權的SDK，不建立連線或持有其他request資訊。</summary>
        /// <param name="service">host擁有的SDK；本adapter不可跨不相容scope共用。</param>
        public OrganizationQueryExecutor(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>原樣委派單筆查詢，不做隱藏retry或保存Entity。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columns) => _service.Retrieve(entityName, id, columns);

        /// <summary>原樣委派單頁查詢，不合併不同呼叫的候選或變更filter。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => _service.RetrieveMultiple(query);

        /// <summary>保持原Execute route，避免用RetrieveMultiple替換而改變SDK adapter行為。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => _service.Execute(request);
    }
}
