#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.DataOperations;

namespace ToolUtilityNameSpace.AppointmentOperations
{
    /// <summary>
    /// 不快取的約會純資料查詢器；僅借入已授權 IOrganizationService，不保存 logger 或查詢結果。
    /// SDK 的 timeout、drain、Dispose 由 host owner 負責，本服務無自有背景工作或可處置資源。
    /// 每次局部建 XML、整頁穩定 ID 驗證後回傳；真實例外交回 host 共用通知，不在此送 LINE。
    /// </summary>
    public class AppointmentService : IAppointmentService
    {
        private static readonly ReadOnlyCollection<string> RangeFields = Array.AsReadOnly(new[]
        {
            "subject", "statecode", "scheduledstart", "scheduledend", "regardingobjectid", "ownerid",
            "new_meeting_kind", "new_leave_kind", "new_location_kind", "activityid", "requiredattendees",
            "optionalattendees", "new_list_appointment", "description"
        });
        private static readonly ReadOnlyCollection<string> ContactFields = Array.AsReadOnly(new[]
        {
            "subject", "statecode", "scheduledstart", "scheduledend", "regardingobjectid", "ownerid",
            "new_meeting_kind", "new_leave_kind", "new_location_kind", "new_leave_signing_status", "activityid",
            "requiredattendees", "optionalattendees", "new_list_appointment", "description", "new_hours", "new_days"
        });
        private readonly IOrganizationService _organizationService;

        /// <summary>保留舊建構式來源相容；logger 不使用或保存，SDK 身分由 host 先驗證且只借入。</summary>
        /// <param name="logger">相容參數，不保存、不擁有也不 Dispose。</param>
        /// <param name="organizationService">同一已授權 scope 的 SDK；null 立即拒絕。</param>
        public AppointmentService(object? logger, IOrganizationService organizationService)
        {
            _organizationService = SdkDataQuery.RequireService(organizationService);
        }

        /// <summary>
        /// 保留舊 API 實際未依日期過濾的相容行為；需日期條件者用明確區間 API，不在搬移時改業務。
        /// SDK 全欄位單頁仍須完整驗 ID，結果與分頁屬當次操作，不共用可變資料。
        /// </summary>
        /// <param name="selectedDate">舊契約未使用的日期，不作授權或隱性過濾。</param>
        /// <returns>通過完整識別驗證的原 SDK 單頁。</returns>
        public EntityCollection RetrieveByDate(DateTime selectedDate) => SdkDataQuery.ValidatePage(
            _organizationService.RetrieveMultiple(new QueryByAttribute("appointment") { ColumnSet = new ColumnSet(true) }), "appointment");

        /// <summary>保留原投影、排序與 scheduledstart 含端點區間；固定西曆，不依程序文化變動。</summary>
        /// <param name="startDate">已由 caller 決定時區語義的起日。</param>
        /// <param name="endDate">不得早於起日的迄日。</param>
        /// <returns>原分頁資訊與全部合法 ID 均保留的單頁。</returns>
        public EntityCollection RetrieveByDateRange(DateTime startDate, DateTime endDate) =>
            SdkDataQuery.Run(_organizationService, RangeEntity(startDate, endDate));

        /// <summary>保留今年與三個簽核狀態條件；顯示名稱安全編碼，lookup ID 不能為空或注入結構。</summary>
        /// <param name="contactName">僅作 uiname 的顯示文字，不作授權或資料列識別。</param>
        /// <param name="contactId">由 server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>同名不同 ID 資料均保留的完整單頁。</returns>
        public EntityCollection RetrieveByContactWithinYear(string contactName, string contactId)
        {
            var entity = SdkDataQuery.Entity("appointment", ContactFields, "subject");
            var status = SdkDataQuery.Condition("new_leave_signing_status", "in");
            // 空白沿用原查詢常數文字，以完整 golden 鎖定；不修改既有狀態白名單。
            status.Add(new XElement("value", " 100000004 "), new XElement("value", " 100000001 "), new XElement("value", " 100000007 "));
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Lookup("new_applier_appointment", contactName, "contact", contactId),
                SdkDataQuery.Condition("scheduledstart", "this-year"), status));
            return SdkDataQuery.Run(_organizationService, entity);
        }

        /// <summary>日期區間加既有排程值，文字只進 XML 屬性；不自行改排程規則或默默刪掉條件。</summary>
        /// <param name="startDate">起日，與迄日一併驗證。</param>
        /// <param name="endDate">含端點迄日，不早於起日。</param>
        /// <param name="scheduleType">原 SDK 排程值，不保存或作為權限。</param>
        /// <returns>完整驗證的原 SDK 候選。</returns>
        public EntityCollection RetrieveByDateRangeAndScheduleType(DateTime startDate, DateTime endDate, string scheduleType)
        {
            var entity = RangeEntity(startDate, endDate);
            entity.Element("filter")!.Add(SdkDataQuery.Condition("new_meeting_kind", "eq", scheduleType ?? string.Empty));
            return SdkDataQuery.Run(_organizationService, entity);
        }

        /// <summary>共用同一區間條件樹，避免兩個公開入口產生不同欄位、排序或日期文化。</summary>
        private static XElement RangeEntity(DateTime start, DateTime end)
        {
            SdkDataQuery.RequireRange(start, end);
            var entity = SdkDataQuery.Entity("appointment", RangeFields, "subject");
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Condition("scheduledstart", "on-or-after", SdkDataQuery.Date(start)),
                SdkDataQuery.Condition("scheduledstart", "on-or-before", SdkDataQuery.Date(end))));
            return entity;
        }
    }
}
