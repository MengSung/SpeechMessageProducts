/*
 * 檔案整體說明
 *
 * 目的與定位：ToolUtilityClass - 查詢操作 Part 1 (Partial Class 4/10) 包含：一般查詢、約會、課程、工作、名單等查詢方法
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityClass。
 * 主要操作入口包含 RetrieveEntityByField、RetrieveEntityCollectionByField、RetrieveAccountCollectionByName、
 * RetrieveAppointmentsByDate、RetrieveAppointmentsByFetchXml、RetrieveAppointmentsByFetchXmlAndScheduleType、
 * RetrieveEnrolledLessonsByFetchXml、RetrieveLessonsByMonth；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
