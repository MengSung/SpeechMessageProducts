using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// N:1 和 N:N 關聯查詢服務介面
    /// </summary>
    public interface IRelationshipQueryService
    {
        /// <summary>
        /// 查詢 N:1 關聯的集合
        /// </summary>
        EntityCollection RetrieveManyToOneRelationship(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 查詢 N:1 關聯的集合(根據名稱排序)
        /// </summary>
        EntityCollection QueryListsAndOrderedByListName(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 查詢 N:1 關聯(使用 LinkEntity 取得關聯資料)
        /// </summary>
        EntityCollection RetrieveManyToOneWithLinkEntity();

        /// <summary>
        /// 查詢週報(根據主日日期和N:1關聯)
        /// </summary>
        EntityCollection QueryWeeklyReportBySunday(DateTime sunday, string parentEntityName,
            string parentEntityIdName, string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 查詢 N:N (ManyToMany) 的集合
        /// </summary>
        EntityCollection QueryManyToMany(string conditionAttributeName, string entityNameToSearch,
            string linkFromEntityName, string linkFromAttributeName, string linkToEntityName,
            string linkToAttributeName, string attributeName, Guid entityIdValue);

        /// <summary>
        /// 連絡人相關的各類名單 (N:N查詢)
        /// </summary>
        EntityCollection QueryListOfContactManyToMany(Guid contactId);
    }
}
