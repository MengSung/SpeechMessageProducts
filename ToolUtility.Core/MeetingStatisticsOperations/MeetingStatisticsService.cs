/*
 * 檔案整體說明
 *
 * 目的與定位：個人出席記錄純 SDK 查詢，維持全部原查詢條件與欄位；不同資料庫 ID 的同名資料完整保留。 只借入已授權 scope 的 SDK，不保存 logger、查詢結果或使用者狀態，不建立 cache／背景工作。
 * host 擁有 timeout／drain／Dispose 與最終共用通知；本類不關借入連線、不吞真實例外或送 LINE。
 *
 * 主要功能與流程：
 * 本檔宣告 MeetingStatisticsService。
 * - 保留相容建構式，logger 不保存；借入 SDK 不轉移身分、憑證或資源所有權。
 * - 依主日日期查原出席資料，固定西曆日期與 new_name 排序，不自行換算時區。
 * - 保留原週報／聯絡人查詢，包含原 uitype=new_disciple_lessons 相容文字； 這是舊版提示值而非授權，搬移時不猜測修正；明確週報型別的另一入口亦維持原值。
 * - 聯絡人與主日日期條件的安全 XML 查詢，不把顯示名稱當正式列 ID。
 * 主要操作入口包含 RetrieveBySunday、RetrieveByWeeklyReportAndContact、RetrieveBySundayDateAndContact、
 * RetrieveByWeeklyReportAndContactAlt、RetrieveWithExpiredDateByContact、RetrieveByContactSmallGroupAndSundayDate、
 * Contact、Query。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.DataOperations;

namespace ToolUtilityNameSpace.MeetingStatisticsOperations
{
    /// <summary>
    /// 個人出席記錄純 SDK 查詢，維持全部原查詢條件與欄位；不同資料庫 ID 的同名資料完整保留。
    /// 只借入已授權 scope 的 SDK，不保存 logger、查詢結果或使用者狀態，不建立 cache／背景工作。
    /// host 擁有 timeout／drain／Dispose 與最終共用通知；本類不關借入連線、不吞真實例外或送 LINE。
    /// </summary>
    public class MeetingStatisticsService : IMeetingStatisticsService
    {
        private static readonly ReadOnlyCollection<string> Fields = Array.AsReadOnly(new[] { "new_present_recordid", "new_name", "createdon" });
        private readonly IOrganizationService _organizationService;

        /// <summary>保留相容建構式，logger 不保存；借入 SDK 不轉移身分、憑證或資源所有權。</summary>
        /// <param name="logger">來源相容參數，不使用、不保存也不 Dispose。</param>
        /// <param name="organizationService">已由 host 授權的 SDK；null 即拒絕。</param>
        public MeetingStatisticsService(object? logger, IOrganizationService organizationService)
        {
            _organizationService = SdkDataQuery.RequireService(organizationService);
        }

        /// <summary>依主日日期查原出席資料，固定西曆日期與 new_name 排序，不自行換算時區。</summary>
        /// <param name="sundayDate">caller 已決定語義的主日日期。</param>
        /// <returns>通過全部穩定 ID 驗證的單頁。</returns>
        public EntityCollection RetrieveBySunday(DateTime sundayDate) => Query(
            SdkDataQuery.Condition("new_sunday_date", "on", SdkDataQuery.Date(sundayDate)));

        /// <summary>
        /// 保留原週報／聯絡人查詢，包含原 uitype=new_disciple_lessons 相容文字；
        /// 這是舊版提示值而非授權，搬移時不猜測修正；明確週報型別的另一入口亦維持原值。
        /// </summary>
        /// <param name="weeklyReportName">只作 uiname 的週報顯示文字。</param>
        /// <param name="weeklyReportId">server 已授權的週報 GUID。</param>
        /// <param name="contactName">只作 uiname 的聯絡人顯示文字。</param>
        /// <param name="contactId">server 已授權的聯絡人 GUID。</param>
        /// <returns>完整原查詢頁，不改變既有資料列數。</returns>
        public EntityCollection RetrieveByWeeklyReportAndContact(string weeklyReportName, string weeklyReportId, string contactName, string contactId) => Query(
            SdkDataQuery.Lookup("new_group_present_weekly_report_prese", weeklyReportName, "new_disciple_lessons", weeklyReportId),
            Contact(contactName, contactId));

        /// <summary>聯絡人與主日日期條件的安全 XML 查詢，不把顯示名稱當正式列 ID。</summary>
        /// <param name="contactName">安全編碼的顯示文字。</param>
        /// <param name="contactId">非空且已授權的聯絡人 GUID。</param>
        /// <param name="sundayDate">已由 caller 決定的主日日期。</param>
        /// <returns>驗證後原 SDK 單頁，含原分頁資訊。</returns>
        public EntityCollection RetrieveBySundayDateAndContact(string contactName, string contactId, DateTime sundayDate) => Query(
            Contact(contactName, contactId), SdkDataQuery.Condition("new_sunday_date", "on", SdkDataQuery.Date(sundayDate)));

        /// <summary>保留替代入口的週報 uitype 與參數順序，只做安全編碼及整頁 ID 驗證。</summary>
        /// <param name="contactName">聯絡人顯示文字。</param>
        /// <param name="contactId">非空聯絡人 GUID。</param>
        /// <param name="weeklyReportName">週報顯示文字。</param>
        /// <param name="weeklyReportId">非空週報 GUID，host 先驗授權。</param>
        /// <returns>完整操作局部候選，不與其他 scope 共用。</returns>
        public EntityCollection RetrieveByWeeklyReportAndContactAlt(string contactName, string contactId, string weeklyReportName, string weeklyReportId) => Query(
            SdkDataQuery.Lookup("new_group_present_weekly_report_prese", weeklyReportName, "new_group_present_weekly_report", weeklyReportId),
            Contact(contactName, contactId));

        /// <summary>保留聯絡人與關懷到期日 not-null 條件，不能省略 lookup 而擴大資料範圍。</summary>
        /// <param name="contactName">僅供 SDK uiname 的文字。</param>
        /// <param name="contactId">已授權的非空資料庫 GUID。</param>
        /// <returns>完整已驗證單頁；SDK 故障原樣傳至最終邊界。</returns>
        public EntityCollection RetrieveWithExpiredDateByContact(string contactName, string contactId) => Query(
            Contact(contactName, contactId), SdkDataQuery.Condition("new_care_expire_date", "not-null"));

        /// <summary>聯絡人、小組與主日三項條件均保留；兩 lookup 先驗正式 GUID，不以名稱作隔離。</summary>
        /// <param name="contactName">聯絡人顯示文字。</param>
        /// <param name="contactId">已授權的聯絡人 GUID。</param>
        /// <param name="smallGroupName">小組顯示文字。</param>
        /// <param name="smallGroupId">已授權的小組 GUID。</param>
        /// <param name="sundayDate">caller 已決定語義的主日日期。</param>
        /// <returns>完整驗 ID 的原 SDK 頁，同名不同 ID 全部保留。</returns>
        public EntityCollection RetrieveByContactSmallGroupAndSundayDate(string contactName, string contactId, string smallGroupName, string smallGroupId, DateTime sundayDate) => Query(
            SdkDataQuery.Lookup("new_list_new_present_record", smallGroupName, "list", smallGroupId), Contact(contactName, contactId),
            SdkDataQuery.Condition("new_sunday_date", "on", SdkDataQuery.Date(sundayDate)));

        /// <summary>建立新的聯絡人條件，不保存任一顯示文字或 ID，不取代 host 授權。</summary>
        private static XElement Contact(string name, string id) => SdkDataQuery.Lookup("new_contact_new_present_record", name, "contact", id);

        /// <summary>每次建立獨立候選樹，沒有共享 mutable XElement；SDK 後全頁驗證才發布。</summary>
        private EntityCollection Query(params XElement[] conditions)
        {
            var entity = SdkDataQuery.Entity("new_present_record", Fields, "new_name");
            entity.Add(SdkDataQuery.Filter(conditions));
            return SdkDataQuery.Run(_organizationService, entity);
        }
    }
}
