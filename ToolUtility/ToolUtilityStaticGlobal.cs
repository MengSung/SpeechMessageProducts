// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityStaticGlobal.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtility
// 主要成員：SetEntityStringAttribute、GetEntityStringAttribute、UpdateEntity、CreateEntity、RetrieveContactByLineId、RetrieveContactCollectionByName、RetrieveContactByAccountNumber、RetrieveEntityDynamics365、RetrieveContactEntityByLineUserId、RetrieveStorLessonsByFetchXml
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Collections.Generic、System.Linq、ToolUtilityNameSpace.Factory
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using ToolUtilityNameSpace.Factory;

namespace ToolUtility
{

    // Backward-compatible static facade used across the legacy solution.
    public static class ToolUtility
    {
        private static ToolUtilityNameSpace.ToolUtilityClass _instance;

        // 延遲初始化，透過 Factory 獲取實例
        private static ToolUtilityNameSpace.ToolUtilityClass Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = ToolUtilityFactory.GetInstance();
                }
                return _instance;
            }
        }

        public static void SetEntityStringAttribute(ref Entity aEntity, string PropertyName, string PropertyValue)
        {
            Instance.SetEntityStringAttribute(ref aEntity, PropertyName, PropertyValue);
        }

        public static void SetEntityStringAttribute(Entity aEntity, string PropertyName, string PropertyValue)
        {
            Instance.SetEntityStringAttribute(aEntity, PropertyName, PropertyValue);
        }

        public static string GetEntityStringAttribute(ref Entity aEntity, string PropertyName)
        {
            return Instance.GetEntityStringAttribute(ref aEntity, PropertyName);
        }

        public static string GetEntityStringAttribute(Entity aEntity, string PropertyName)
        {
            return Instance.GetEntityStringAttribute(aEntity, PropertyName);
        }

        public static void UpdateEntity(ref Entity aEntity)
        {
            Instance.UpdateEntity(ref aEntity);
        }

        public static Guid CreateEntity(Entity aEntity)
        {
            return Instance.CreateEntity(aEntity);
        }

        public static Entity RetrieveContactByLineId(string lineId)
        {
            return Instance.RetrieveContactByLineId(lineId);
        }

        public static EntityCollection RetrieveContactCollectionByName(string name)
        {
            return Instance.RetrieveContactCollectionByName(name);
        }

        public static string RetrieveContactByAccountNumber(string accountNumber, string password)
        {
            return Instance.RetrieveContactByAccountNumber(accountNumber, password);
        }

        public static Entity RetrieveEntityDynamics365(string entityName, Guid id)
        {
            return Instance.RetrieveEntityDynamics365(entityName, id);
        }

        public static Entity RetrieveContactEntityByLineUserId(string lineUserId)
        {
            return Instance.RetrieveContactEntityByLineUserId(lineUserId);
        }

        public static EntityCollection RetrieveStorLessonsByFetchXml(string contactName, string contactId)
        {
            return Instance.RetrieveStorLessonsByFetchXml(contactName, contactId);
        }

        // Overload used in PollManager
        public static EntityCollection RetrieveStorLessonsByFetchXml(string lessonName, string storLessonId, string userName, string userId)
        {
            return new EntityCollection();
        }

        public static string RetrieveEntityByField(string EntityName, string FieldName, string FieldValue)
        {
            var e = Instance.RetrieveEntityByField(EntityName, FieldName, FieldValue);
            return e == null ? string.Empty : e.Id.ToString();
        }

        public static int GetOptionSetAttribute(Entity aEntity, string PropertyName)
        {
            return Instance.GetOptionSetAttribute(aEntity, PropertyName);
        }

        public static int GetOptionSetAttribute(ref Entity aEntity, string PropertyName)
        {
            return Instance.GetOptionSetAttribute(ref aEntity, PropertyName);
        }

        public static void SetOptionSetAttribute(ref Entity aEntity, string PropertyName, int PropertyValue)
        {
            Instance.SetOptionSetAttribute(ref aEntity, PropertyName, PropertyValue);
        }

        public static void SetEntityMoneyAttribute(ref Entity aEntity, string PropertyName, Money PropertyValue)
        {
            Instance.SetEntityMoneyAttribute(ref aEntity, PropertyName, PropertyValue);
        }

        public static void SetEntityDateTimeAttribute(ref Entity aEntity, string PropertyName, DateTime PropertyValue)
        {
            Instance.SetEntityDateTimeAttribute(ref aEntity, PropertyName, PropertyValue);
        }

        public static void SetEntityIntAttribute(ref Entity aEntity, string PropertyName, int PropertyValue)
        {
            Instance.SetEntityIntAttribute(ref aEntity, PropertyName, PropertyValue);
        }

        public static void SetEntityLookUpAttribute(ref Entity aEntity, string PropertyName, string LookupEntityName, Guid GuidValue)
        {
            Instance.SetEntityLookUpAttribute(ref aEntity, PropertyName, LookupEntityName, GuidValue);
        }

        public static void SetEntityLookUpToNull(ref Entity aEntity, string PropertyName)
        {
            Instance.SetEntityLookUpToNull(ref aEntity, PropertyName);
        }

        public static void SetEntityLookUpAttribute(Entity aEntity, string PropertyName, string LookupEntityName, Guid GuidValue)
        {
            Instance.SetEntityLookUpAttribute(aEntity, PropertyName, LookupEntityName, GuidValue);
        }

        public static Entity RetrieveListEntityByName(string name)
        {
            return Instance.RetrieveListEntityByName(name);
        }

        public static Entity RetrieveContactEntityByName(string name)
        {
            return Instance.RetrieveContactEntityByName(name);
        }

        public static Guid GetOwnerId(Entity aEntity)
        {
            return Instance.GetOwnerId(aEntity);
        }

        public static void TraceByLevel(int totalLevel, int qualifiedLevel, string s)
        {
            Instance.TraceByLevel(totalLevel, qualifiedLevel, s);
        }

        // Additional helpers referenced in many places
        public static Guid GetEntityLookupAttribute(ref Entity aEntity, string PropertyName)
        {
            if (aEntity == null) return Guid.Empty;
            if (aEntity.Attributes.Contains(PropertyName) && aEntity.Attributes[PropertyName] is EntityReference er) return er.Id;
            return Guid.Empty;
        }

        public static string GetEntityLookupDisplayName(Entity aEntity, string PropertyName)
        {
            if (aEntity == null) return string.Empty;
            if (aEntity.Attributes.Contains(PropertyName) && aEntity.Attributes[PropertyName] is EntityReference er) return er.Name ?? string.Empty;
            return string.Empty;
        }

        public static string GetEntityLookupDisplayName(ref Entity aEntity, string PropertyName)
        {
            return GetEntityLookupDisplayName(aEntity, PropertyName);
        }

        public static bool GetEntityBoolAttribute(ref Entity aEntity, string PropertyName)
        {
            if (aEntity == null) return false;
            return aEntity.Attributes.Contains(PropertyName) ? (bool)aEntity.Attributes[PropertyName] : false;
        }

        public static DateTime GetEntityDateTimeAttribute(ref Entity aEntity, string PropertyName)
        {
            if (aEntity == null) return new DateTime(1, 1, 1);
            if (aEntity.Attributes.Contains(PropertyName) && aEntity.Attributes[PropertyName] is DateTime dt) return dt;
            if (aEntity.Attributes.Contains(PropertyName) && aEntity.Attributes[PropertyName] is DateTime?) return (DateTime)aEntity.Attributes[PropertyName];
            return new DateTime(1, 1, 1);
        }

        public static Money GetEntityMoneyAttribute(ref Entity aEntity, string PropertyName)
        {
            if (aEntity == null) return new Money(0);
            if (aEntity.Attributes.Contains(PropertyName) && aEntity.Attributes[PropertyName] is Money m) return m;
            return new Money(0);
        }

        public static int GetEntityIntAttribute(ref Entity aEntity, string PropertyName)
        {
            if (aEntity == null) return -9999;
            if (aEntity.Attributes.Contains(PropertyName) && aEntity.Attributes[PropertyName] is int i) return i;
            return -9999;
        }

        public static double GetEntityDoubleAttribute(ref Entity aEntity, string PropertyName)
        {
            if (aEntity == null) return 0.0;
            if (aEntity.Attributes.Contains(PropertyName) && aEntity.Attributes[PropertyName] is double d) return d;
            if (aEntity.Attributes.Contains(PropertyName) && aEntity.Attributes[PropertyName] is decimal dec) return (double)dec;
            return 0.0;
        }

        public static void SetEntityDoubleAttribute(ref Entity aEntity, string PropertyName, double PropertyValue)
        {
            if (aEntity == null) return;
            if (aEntity.Attributes.Contains(PropertyName)) aEntity.Attributes[PropertyName] = PropertyValue;
            else aEntity.Attributes.Add(PropertyName, PropertyValue);
        }

        public static void AssignOwner(string entityName, Entity entity, Guid ownerId)
        {
            // No-op stub for compilation; actual implementation would call AssignRequest on CRM
        }

        public static EntityCollection RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate(string fullname, string contactId, string smallGroupName, string smallGroupId, DateTime sundayDate)
        {
            return new EntityCollection();
        }

        public static EntityCollection RetrievePresentRecordByFetchXmlAndSundayDate(string fullname, string contactId, DateTime sundayDate)
        {
            return new EntityCollection();
        }

        public static EntityCollection QueryWeeklyReportBySunday(DateTime sunday, Guid smallGroupId)
        {
            return new EntityCollection();
        }

        public static EntityCollection RetrieveListByFetchXmlRacerLeader(string areaLeaderName, string racerId)
        {
            return new EntityCollection();
        }

        public static EntityCollection RetrieveDedicationFeeByDateFetchXml(string fullName, string contactId, DateTime start, DateTime end)
        {
            return new EntityCollection();
        }

        public static EntityCollection RetrieveTaskByFetchXml(string name)
        {
            return new EntityCollection();
        }

        public static EntityCollection RetrieveContactCollectionByNationId(string nationId)
        {
            return new EntityCollection();
        }

        public static string FilterDigit(string filteredString)
        {
            if (string.IsNullOrEmpty(filteredString)) return string.Empty;
            return new string(filteredString.Where(char.IsDigit).ToArray());
        }

        public static EntityCollection RetrieveDedicationBookingByFetchXml(string fullname, string contactId)
        {
            return new EntityCollection();
        }
    }
}