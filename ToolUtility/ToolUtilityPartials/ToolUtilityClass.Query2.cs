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
