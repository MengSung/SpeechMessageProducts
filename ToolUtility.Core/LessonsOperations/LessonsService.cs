/*
 * 檔案整體說明
 *
 * 目的與定位：純 SDK 課程查詢，維持原欄位、join、排序與狀態條件；安全 XML 不容許文字改寫查詢結構。 SDK 只借入，host 先完成授權並擁有 timeout／drain／Dispose；本類不保存
 * logger、結果或憑證。 無 cache、Task、timer 或隱藏 retry；真實例外交回最終 host 共用通知，不自行發 LINE。
 *
 * 主要功能與流程：
 * 本檔宣告 LessonsService。
 * - 保留舊建構式但不保存任意 logger；SDK 只屬呼叫端 scope，不轉移所有權。
 * - 依原日期區間與報名 join 查課程；保留 distinct 及正式主鍵投影，不按課程名稱去重。
 * - 保留原課程日期區間、分類排序與欄位；固定西曆，不受 ambient culture 影響。
 * - 保留報名狀態排除、5筆上限、聯絡人 outer join 與原 alias；兩個 lookup 均驗正式非空 GUID。 linked 欄位與原查詢相同，不保存或記錄個資；結果只歸本次已授權操作。
 * 主要操作入口包含 RetrieveEnrolledLessons、RetrieveLessonsByMonth、RetrieveStorLessons、RangeEntity。
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

namespace ToolUtilityNameSpace.LessonsOperations
{
    /// <summary>
    /// 純 SDK 課程查詢，維持原欄位、join、排序與狀態條件；安全 XML 不容許文字改寫查詢結構。
    /// SDK 只借入，host 先完成授權並擁有 timeout／drain／Dispose；本類不保存 logger、結果或憑證。
    /// 無 cache、Task、timer 或隱藏 retry；真實例外交回最終 host 共用通知，不自行發 LINE。
    /// </summary>
    public class LessonsService : ILessonsService
    {
        private static readonly ReadOnlyCollection<string> LessonFields = Array.AsReadOnly(new[]
        {
            "new_name", "createdon", "new_class_start_date", "new_class_end_date", "new_classification", "new_disciple_lessonsid"
        });
        private static readonly ReadOnlyCollection<string> StorFields = Array.AsReadOnly(new[]
        {
            "createdon", "new_contact_new_stor_lessons", "new_fee", "new_pay_date", "new_new_disciple_lessons_new_stor_les", "new_stor_lessonsid"
        });
        private static readonly ReadOnlyCollection<string> LinkedContactFields = Array.AsReadOnly(new[]
        {
            "telephone2", "address2_line1", "parentcustomerid", "mobilephone", "emailaddress1"
        });
        private readonly IOrganizationService _organizationService;

        /// <summary>保留舊建構式但不保存任意 logger；SDK 只屬呼叫端 scope，不轉移所有權。</summary>
        /// <param name="logger">僅為來源相容，不使用或 Dispose。</param>
        /// <param name="organizationService">已授權的借入 SDK，null 立即拒絕。</param>
        public LessonsService(object? logger, IOrganizationService organizationService)
        {
            _organizationService = SdkDataQuery.RequireService(organizationService);
        }

        /// <summary>依原日期區間與報名 join 查課程；保留 distinct 及正式主鍵投影，不按課程名稱去重。</summary>
        /// <param name="startDate">課程起日條件，不自行換時區。</param>
        /// <param name="endDate">課程迄日條件，不早於起日。</param>
        /// <param name="contactName">安全編碼的顯示文字，不是授權。</param>
        /// <param name="contactId">server 已授權的非空聯絡人 GUID。</param>
        /// <returns>全部驗 ID 後的原分頁，SDK 真實故障不回空。</returns>
        public EntityCollection RetrieveEnrolledLessons(DateTime startDate, DateTime endDate, string contactName, string contactId)
        {
            var entity = RangeEntity(startDate, endDate);
            entity.Add(new XElement("link-entity", new XAttribute("name", "new_stor_lessons"),
                new XAttribute("from", "new_new_disciple_lessons_new_stor_les"), new XAttribute("to", "new_disciple_lessonsid"),
                new XAttribute("alias", "ab"), SdkDataQuery.Filter(
                    SdkDataQuery.Lookup("new_contact_new_stor_lessons", contactName, "contact", contactId))));
            return SdkDataQuery.Run(_organizationService, entity, distinct: true);
        }

        /// <summary>保留原課程日期區間、分類排序與欄位；固定西曆，不受 ambient culture 影響。</summary>
        /// <param name="startDate">含端點起日。</param>
        /// <param name="endDate">含端點迄日，不早於起日。</param>
        /// <returns>完整驗證的單頁，呼叫者負責後續分頁。</returns>
        public EntityCollection RetrieveLessonsByMonth(DateTime startDate, DateTime endDate) =>
            SdkDataQuery.Run(_organizationService, RangeEntity(startDate, endDate), distinct: true);

        /// <summary>
        /// 保留報名狀態排除、5筆上限、聯絡人 outer join 與原 alias；兩個 lookup 均驗正式非空 GUID。
        /// linked 欄位與原查詢相同，不保存或記錄個資；結果只歸本次已授權操作。
        /// </summary>
        /// <param name="lessonName">課程顯示文字，只作 uiname。</param>
        /// <param name="lessonId">已授權課程正式 GUID。</param>
        /// <param name="contactName">聯絡人顯示文字，只作 uiname。</param>
        /// <param name="contactId">已授權聯絡人正式 GUID。</param>
        /// <returns>完整驗證的單頁，不按同名合併。</returns>
        public EntityCollection RetrieveStorLessons(string lessonName, string lessonId, string contactName, string contactId)
        {
            var entity = SdkDataQuery.Entity("new_stor_lessons", StorFields, "createdon", descending: true);
            var statuses = SdkDataQuery.Condition("new_enroll_status", "not-in");
            statuses.Add(new XElement("value", "100000007"), new XElement("value", "100000009"), new XElement("value", "100000003"));
            entity.Add(SdkDataQuery.Filter(statuses,
                SdkDataQuery.Lookup("new_new_disciple_lessons_new_stor_les", lessonName, "new_disciple_lessons", lessonId),
                SdkDataQuery.Lookup("new_contact_new_stor_lessons", contactName, "contact", contactId),
                SdkDataQuery.Condition("statuscode", "ne", "2"), SdkDataQuery.Condition("statecode", "eq", "0")));
            var linked = new XElement("link-entity", new XAttribute("name", "contact"), new XAttribute("from", "contactid"),
                new XAttribute("to", "new_contact_new_stor_lessons"), new XAttribute("visible", "false"),
                new XAttribute("link-type", "outer"), new XAttribute("alias", "a_45d999afd4cc4001b091647bb91668ef"));
            foreach (var field in LinkedContactFields) linked.Add(new XElement("attribute", new XAttribute("name", field)));
            entity.Add(linked);
            return SdkDataQuery.Run(_organizationService, entity, top: 5);
        }

        /// <summary>單次擁有日期條件樹，不以共用 mutable XElement 降低配置而造成跨呼叫污染。</summary>
        private static XElement RangeEntity(DateTime start, DateTime end)
        {
            SdkDataQuery.RequireRange(start, end);
            var entity = SdkDataQuery.Entity("new_disciple_lessons", LessonFields, "new_classification");
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Condition("new_class_start_date", "on-or-after", SdkDataQuery.Date(start)),
                SdkDataQuery.Condition("new_class_end_date", "on-or-before", SdkDataQuery.Date(end))));
            return entity;
        }
    }
}
