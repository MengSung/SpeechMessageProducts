/*
 * 檔案整體說明
 *
 * 目的與定位：N:1及N:N純SDK單頁契約，host先驗授權scope；整頁穩定ID驗證後發布，保留同名異ID與分頁metadata。 每頁最多5000列，超量/空/重複/錯表拒絕；結果只屬本caller，
 * 不當作跨使用者共用快照。 SDK故障由host共用告警處理，本契約不保存連線、credential或logger。
 *
 * 主要功能與流程：
 * 本檔宣告 IRelationshipQueryService。
 * - 依受信任metadata及非空父資料庫GUID查單頁，啟用條件保留於原link，不將字串ID變為另一型別。
 * - 父關聯單頁另加listname升序；顯示名稱不構成身份或去重依據。
 * - 固定contact/account內關聯欄位與alias，沒有父ID篩選，host須先授權SDK可見整體範圍。
 * - 週報日期沿原CurrentCulture字串與link Equal條件；不在抽取時暗中改時區、運算子或資料型別。
 * 資料／選項欄位包含 IRelationshipQueryService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/DataOperations/RelationshipQueryCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/RelationshipQuerySafetyTests.cs、
 * ToolUtility.Core/QueryOperations/RelationshipQueryService.cs、ToolUtility/Core/ToolUtilityFacade.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// N:1及N:N純SDK單頁契約，host先驗授權scope；整頁穩定ID驗證後發布，保留同名異ID與分頁metadata。
    /// 每頁最多5000列，超量/空/重複/錯表拒絕；結果只屬本caller，不當作跨使用者共用快照。
    /// SDK故障由host共用告警處理，本契約不保存連線、credential或logger。
    /// </summary>
    public interface IRelationshipQueryService
    {
        /// <summary>
        /// 依受信任metadata及非空父資料庫GUID查單頁，啟用條件保留於原link，不將字串ID變為另一型別。
        /// </summary>
        EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 父關聯單頁另加listname升序；顯示名稱不構成身份或去重依據。
        /// </summary>
        EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 固定contact/account內關聯欄位與alias，沒有父ID篩選，host須先授權SDK可見整體範圍。
        /// </summary>
        EntityCollection RetrieveManyToOneWithLinkEntity();

        /// <summary>
        /// 週報日期沿原CurrentCulture字串與link Equal條件；不在抽取時暗中改時區、運算子或資料型別。
        /// </summary>
        EntityCollection QueryWeeklyReportBySunday(DateTime sunday, string parentEntityName,
            string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 驗七個受信任結構名稱與非空GUID，保留根bool/啟用及link Guid條件；單頁結果不緩存。
        /// </summary>
        EntityCollection QueryManyToMany(string conditionAttributeName, string entityNameToSearch,
            string linkFromEntityName, string linkFromAttributeName, string linkToEntityName,
            string linkToAttributeName, string attributeName, Guid entityIdValue);

        /// <summary>
        /// 聯絡人固定N:N名單單頁，共用同一查詢邏輯；正式GUID不等於授權，caller自行處理續頁。
        /// </summary>
        EntityCollection QueryListOfContactManyToMany(Guid contactId);
    }
}
