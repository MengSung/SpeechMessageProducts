// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityPartials/ToolUtilityClass.Query1.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：RetrieveEntityByField、RetrieveEntityCollectionByField、RetrieveAccountCollectionByName、RetrieveAppointmentsByDate、RetrieveAppointmentsByFetchXml、RetrieveAppointmentsByFetchXmlAndScheduleType、RetrieveEnrolledLessonsByFetchXml、RetrieveLessonsByMonth、RetrieveStorLessonsByFetchXml、RetrieveStorLessonsByDiscipleLessonsFetchXml
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
    /// ToolUtilityClass - 查詢操作 Part 1 (Partial Class 4/10)
    /// 包含：一般查詢、約會、課程、工作、名單等查詢方法
    /// </summary>
    public partial class ToolUtilityClass
    {
        #region 一般實體查詢
        public Entity RetrieveEntityByField(String EntityName, String FieldName, String FieldValue)
        {
            try
            {
                return _facade.RetrieveEntityByField(EntityName, FieldName, FieldValue);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveEntityByField 錯誤: " + e.Message);
                throw;
            }
        }

        public EntityCollection RetrieveEntityCollectionByField(String EntityName, String FieldName, String FieldValue)
        {
            try
            {
                return _facade.RetrieveEntityCollectionByField(EntityName, FieldName, FieldValue);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, "RetrieveEntityCollectionByField 錯誤: " + e.Message);
                throw;
            }
        }
        #endregion

        #region 客戶查詢
        public Guid RetrieveAccountCollectionByName(String AccountName)
            => _facade.RetrieveAccountCollectionByName(AccountName);
        #endregion

        #region 約會查詢
        public EntityCollection RetrieveAppointmentsByDate(DateTime aSelectedDate)
            => _facade.RetrieveAppointmentsByDate(aSelectedDate);

        public EntityCollection RetrieveAppointmentsByFetchXml(DateTime StartDate, DateTime EndDate)
            => _facade.RetrieveAppointmentsByFetchXml(StartDate, EndDate);

        public EntityCollection RetrieveAppointmentsByFetchXml(String ContactName, String ContactId)
            => _facade.RetrieveAppointmentsByFetchXml(ContactName, ContactId);

        public EntityCollection RetrieveAppointmentsByFetchXmlAndScheduleType(DateTime StartDate, DateTime EndDate, String ScheduleType)
            => _facade.RetrieveAppointmentsByFetchXmlAndScheduleType(StartDate, EndDate, ScheduleType);
        #endregion

        #region 課程查詢
        public EntityCollection RetrieveEnrolledLessonsByFetchXml(DateTime StartDate, DateTime EndDate, String ContactName, String ContactId)
            => _facade.RetrieveEnrolledLessonsByFetchXml(StartDate, EndDate, ContactName, ContactId);

        public EntityCollection RetrieveLessonsByMonth(DateTime StartDate, DateTime EndDate)
            => _facade.RetrieveLessonsByMonth(StartDate, EndDate);

        public EntityCollection RetrieveStorLessonsByFetchXml(String LessonName, String LessonId, String ContactName, String ContactId)
            => _facade.RetrieveStorLessonsByFetchXml(LessonName, LessonId, ContactName, ContactId);

        public EntityCollection RetrieveStorLessonsByFetchXml(String ContactName, String ContactId)
            => _facade.RetrieveStorLessonsByContact(ContactName, ContactId);

        public EntityCollection RetrieveStorLessonsByDiscipleLessonsFetchXml(String LessonName, String LessonId)
            => _facade.RetrieveStorLessonsByDiscipleLessons(LessonName, LessonId);
        #endregion

        #region 工作查詢
        public EntityCollection RetrieveTaskByFetchXml(String Subject)
            => _facade.RetrieveTaskByFetchXml(Subject);
        #endregion

        #region 名單查詢
        public Entity RetrieveListEntityByName(String ListName)
            => _facade.RetrieveListEntityByName(ListName);

        public EntityCollection RetrieveListByFetchXml()
            => _facade.RetrieveAllLists();

        public EntityCollection RetrieveListByFetchXmlContact(String ContactName)
            => _facade.RetrieveListByFetchXmlContact(ContactName);

        public EntityCollection RetrieveListByFetchXmlRacerLeader(String ContactName, String ContactId)
            => _facade.RetrieveListByFetchXmlRacerLeader(ContactName, ContactId);

        public EntityCollection RetrieveSmallGroupListCollectionByFetchXml()
            => _facade.RetrieveSmallGroupListCollection();
        #endregion

        #region 奉獻收費單查詢
        public EntityCollection RetrieveDedicationFeeByFetchXml(String ContactName, String ContactId)
            => _facade.RetrieveDedicationFeeByFetchXml(ContactName, ContactId);

        public EntityCollection RetrieveDedicationFeeByDateFetchXml(String ContactName, String ContactId, DateTime StartDate, DateTime EndDate)
            => _facade.RetrieveDedicationFeeByDateFetchXml(ContactName, ContactId, StartDate, EndDate);

        public EntityCollection RetrieveDedicationBookingByFetchXml(String ContactName, String ContactId)
            => _facade.RetrieveDedicationBooking(ContactName, ContactId);

        public EntityCollection RetrieveFeeByFetchXml(String DedicationBookingName, String DedicationBookingId, String PaidPeriod)
            => _facade.RetrieveFee(DedicationBookingName, DedicationBookingId, PaidPeriod);
        #endregion

        #region 聚會統計查詢
        public EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime SundayDate)
            => _facade.RetrieveMeetingStatistics(SundayDate);
        #endregion
    }
}
