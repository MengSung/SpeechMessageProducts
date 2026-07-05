// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityPartials/ToolUtilityClass.Query2.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：RetrievePresentRecordByFetchXml、RetrievePresentRecordByFetchXmlAndSundayDate、RetrievePresentRecordByFetchXmlAndWeeklyReport、RetrievePresentRecordByFetchXmlAndContainEpiredDate、RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate、QueryPresentRecordByContactIdAndSunday、QueryPresentRecordSortBySunday、QueryPresentRecordSortBySundayFetchXml、QueryPresentRecordSortBySunday_BACKUP、QueryPresentRecordInWeeklyReportByContactId
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - 查詢操作 Part 2 (Partial Class 5/10)
    /// 包含：出席記錄、週報、關聯查詢方法
    /// </summary>
    public partial class ToolUtilityClass
    {
        #region 出席記錄查詢
        public EntityCollection RetrievePresentRecordByFetchXml(String WeeklyReportName, String WeeklyReportId, String ContactName, String ContactId)
            => _facade.RetrievePresentRecordByFetchXml(WeeklyReportName, WeeklyReportId, ContactName, ContactId);

        public EntityCollection RetrievePresentRecordByFetchXmlAndSundayDate(String ContactName, String ContactId, DateTime SundayDate)
            => _facade.RetrievePresentRecordByFetchXmlAndSundayDate(ContactName, ContactId, SundayDate);

        public EntityCollection RetrievePresentRecordByFetchXmlAndWeeklyReport(String ContactName, String ContactId, String WeeklyReportNmae, String WeeklyReportId)
            => _facade.RetrievePresentRecordByFetchXmlAndWeeklyReport(ContactName, ContactId, WeeklyReportNmae, WeeklyReportId);

        public EntityCollection RetrievePresentRecordByFetchXmlAndContainEpiredDate(String ContactName, String ContactId)
            => _facade.RetrievePresentRecordByFetchXmlAndContainEpiredDate(ContactName, ContactId);

        public EntityCollection RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate(String ContactName, String ContactId, String SmallGroupName, String SmallGroupId, DateTime SundayDate)
            => _facade.RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate(ContactName, ContactId, SmallGroupName, SmallGroupId, SundayDate);

        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid aListEntityId, Guid ContactId, int MonthPeriod)
            => _facade.QueryPresentRecordByContactIdAndSunday(aListEntityId, ContactId, MonthPeriod);

        public EntityCollection QueryPresentRecordSortBySunday(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.QueryPresentRecordSortBySunday(ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryPresentRecordSortBySundayFetchXml(int LastWeeks, String ContactName, String ContactId)
            => _facade.QueryPresentRecordSortBySundayFetchXml(LastWeeks, ContactName, ContactId);

        public EntityCollection QueryPresentRecordSortBySunday_BACKUP(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.RetrieveManyToOneRelationship(ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid aContactId, Guid aWeeklyReportEntityId)
            => _facade.QueryPresentRecordInWeeklyReportByContactId(aContactId, aWeeklyReportEntityId);
        #endregion

        #region 週報查詢
        public EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId)
        {
            try
            {
                return _facade.QueryWeeklyReportBeforeTowMonthOfSunday(aSunday, aListEntityId);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "QueryWeeklyReportBeforeTowMonthOfSunday 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection QueryWeeklyReportBySunday(DateTime aSunday, String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.QueryWeeklyReportBySunday(aSunday, ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryWeeklyReportBySunday(DateTime aSunday, Guid aListEntityId)
            => _facade.QueryWeeklyReportBySunday(aSunday, aListEntityId);

        public EntityCollection QueryWeeklyReportBeforeTwoMonthOfSunday(DateTime aSunday, Guid aListEntityId)
            => _facade.QueryWeeklyReportBeforeTwoMonthOfSunday(aSunday, aListEntityId);
        #endregion

        #region 關聯查詢 (N:1, N:N)
        public EntityCollection RetrieveManyToOneCollection()
            => _facade.RetrieveManyToOneWithLinkEntity();

        public EntityCollection RetrieveManyToOneRelationship()
            => _facade.RetrieveManyToOneWithLinkEntity();

        public EntityCollection RetrieveManyToOneRelationship(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.RetrieveManyToOneRelationship(ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryEntityListByDate(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.QueryEntityListByDate(ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryListsAndOrderedByListName(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.QueryListsAndOrderedByListName(ParentEntityName, ParentEntityIdName, ParentEntityId, AssociationName, ChildEntityName);

        public EntityCollection QueryListByContactId(Guid aContactId, String AssociationName)
            => _facade.QueryListByContactId(aContactId, AssociationName);

        public EntityCollection QueryManyToMany(String ConditionAttributeName, String EntityNameToSearch,
            String LinkFromEntityName, String LinkFromAttributeName, String LinkToEntityName,
            String LinkToAttributeName, String AttributeName, Guid EntityIdValue)
            => _facade.QueryManyToMany(ConditionAttributeName, EntityNameToSearch, LinkFromEntityName,
                LinkFromAttributeName, LinkToEntityName, LinkToAttributeName, AttributeName, EntityIdValue);

        public EntityCollection QueryListOfContactManyToMany(Guid ContactId)
            => _facade.QueryListOfContactManyToMany(ContactId);

        public EntityCollection QueryEntityList(String ParentEntityName, String ParentEntityIdName,
            String ParentEntityId, String AssociationName, String ChildEntityName)
            => _facade.QueryEntityListByDate(ParentEntityName, ParentEntityIdName, ParentEntityId,
                AssociationName, ChildEntityName);
        #endregion

        #region 特殊查詢
        public Entity QueryBloodReportByContactId(Guid ContactId)
        {
            try
            {
                var query = new QueryExpression
                {
                    EntityName = "new_blood_report",
                    ColumnSet = new ColumnSet(true)
                };
                var filter = new FilterExpression(LogicalOperator.And);
                filter.AddCondition("new_blood_contact_relation", ConditionOperator.Equal, ContactId);
                query.Criteria = filter;
                query.AddOrder("createdon", OrderType.Descending);

                var retrieved = m_Crm2011OrganizationService.RetrieveMultiple(query);
                return retrieved.Entities.Count > 0 ? retrieved.Entities[0] : null;
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "QueryBloodReportByContactId 錯誤: " + e.Message);
                throw;
            }
        }
        #endregion
    }
}
