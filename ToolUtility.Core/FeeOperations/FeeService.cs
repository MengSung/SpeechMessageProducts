/*
 * 檔案整體說明
 *
 * 目的與定位：收費與認獻的純 SDK 查詢；所有條件樹、候選集合及識別碼驗證均屬單次操作。 本服務不做授權：呼叫端必須先在伺服器確認教會／使用者及資料範圍，字串名稱不構成權限。
 *
 * 主要功能與流程：
 * 本檔宣告 FeeService。
 * - 建立與呼叫端相同授權／連線生命週期的查詢器，不接管借入 SDK 服務。
 * - 保留既有建構 API，但不保存任意 host logger，避免無用參照延長 request 或憑證生命週期。 本類不吞例外，故不在純核心重建日誌／LINE client；最終 host 負責共用通知。
 * - 讀取啟用且已核准的認獻，使用現有 FetchXmlQueryService 的有效條件，不再回傳假空集合。 保留原上限 1000、createdon 倒序及投影欄位；名稱只供 lookup 顯示，ID 才是查詢條件。
 * - 按認獻資料庫 ID 與已繳期數讀取啟用收費單，沿用有效舊查詢的 500 筆上限與建立時間排序。 所有外部文字由 XML writer 編碼，不能改變元素、條件或 OR／AND 結構。
 * 主要操作入口包含 RetrieveDedicationBooking、RetrieveFee、RetrieveDedicationFee、RetrieveDedicationFeeByDateRange、
 * GetPaidFee、GetNotYetPaidFee、RetrieveFeeByFetchXmlOrderNumber、CreateDedicationFetch；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/QueryOperations/FetchXmlQueryService.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.DataOperations;

namespace ToolUtilityNameSpace.FeeOperations
{
    /// <summary>
    /// 收費與認獻的純 SDK 查詢；所有條件樹、候選集合及識別碼驗證均屬單次操作。
    /// 本服務不做授權：呼叫端必須先在伺服器確認教會／使用者及資料範圍，字串名稱不構成權限。
    /// </summary>
    /// <remarks>
    /// 只借入已授權的 IOrganizationService，不取得 Dispose 責任；服務應由同一 host scope 擁有，
    /// 不可註冊跨租戶 singleton。相容 logger 不保存，沒有 cache、timer、queue 或背景 task。
    /// 回傳一個 SDK 查詢頁，保留 MoreRecords／PagingCookie，由原呼叫者繼續原有分頁流程；
    /// 發布前驗證整頁穩定 ID，不按名稱去重或改寫候選。SDK 故障原樣向最終 host 通知邊界傳遞。
    /// </remarks>
    public class FeeService : IFeeService
    {
        // 僅共享固定的結構欄位名稱；唯讀包裝不外洩底層陣列，且不包含教會、使用者或查詢資料。
        // 這三份有限常數活至組件卸載，不將每次呼叫的 XML／結果提升為共用狀態。
        private static readonly ReadOnlyCollection<string> DedicationBookingFields = Array.AsReadOnly(new[]
        {
            "new_dedication_bookingid", "new_name", "createdon", "new_dedication_booking_status"
        });
        private static readonly ReadOnlyCollection<string> FeeFields =
            Array.AsReadOnly(new[] { "new_feeid", "new_name", "createdon", "new_paid_period" });
        private static readonly ReadOnlyCollection<string> OrderFeeFields =
            Array.AsReadOnly(new[] { "new_feeid", "new_name", "createdon" });
        private readonly IOrganizationService _organizationService;

        /// <summary>
        /// 建立與呼叫端相同授權／連線生命週期的查詢器，不接管借入 SDK 服務。
        /// </summary>
        /// <param name="organizationService">伺服器已確認組織與身分的 SDK 服務；不可為 null。</param>
        public FeeService(IOrganizationService organizationService)
        {
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 保留既有建構 API，但不保存任意 host logger，避免無用參照延長 request 或憑證生命週期。
        /// 本類不吞例外，故不在純核心重建日誌／LINE client；最終 host 負責共用通知。
        /// </summary>
        /// <param name="logger">僅為來源相容保留，值不使用、不保存，也不 Dispose。</param>
        /// <param name="organizationService">借入的已授權 SDK 服務。</param>
        public FeeService(object? logger, IOrganizationService organizationService) : this(organizationService)
        {
        }

        /// <summary>
        /// 讀取啟用且已核准的認獻，使用現有 FetchXmlQueryService 的有效條件，不再回傳假空集合。
        /// 保留原上限 1000、createdon 倒序及投影欄位；名稱只供 lookup 顯示，ID 才是查詢條件。
        /// </summary>
        /// <param name="contactName">可含 XML 特殊字元的顯示名稱，不作授權。</param>
        /// <param name="contactId">非空資料庫聯絡人 GUID。</param>
        /// <returns>已完成整頁 ID 驗證、由呼叫端擁有的 SDK 查詢頁。</returns>
        public EntityCollection RetrieveDedicationBooking(string contactName, string contactId)
        {
            var fetch = CreateFetch("new_dedication_booking", DedicationBookingFields, "createdon", true, 1000);
            fetch.Element("entity")!.Add(new XElement("filter", new XAttribute("type", "and"),
                Lookup("new_contact_new_dedication_booking", contactName, "contact", contactId, nameof(contactId)),
                Condition("new_dedication_booking_status", "eq", "100000001"),
                Condition("statecode", "eq", "0")));
            return Query(fetch, false);
        }

        /// <summary>
        /// 按認獻資料庫 ID 與已繳期數讀取啟用收費單，沿用有效舊查詢的 500 筆上限與建立時間排序。
        /// 所有外部文字由 XML writer 編碼，不能改變元素、條件或 OR／AND 結構。
        /// </summary>
        /// <param name="dedicationBookingName">認獻顯示名稱，不作識別或授權。</param>
        /// <param name="dedicationBookingId">非空認獻資料庫 GUID。</param>
        /// <param name="paidPeriod">原有期數條件文字，空白拒絕以免變成廣泛查詢。</param>
        /// <returns>完整驗證的單頁 SDK 收費結果，不保存於服務。</returns>
        public EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod)
        {
            RequireText(paidPeriod, nameof(paidPeriod));
            var fetch = CreateFetch("new_fee", FeeFields,
                "createdon", true, 500);
            fetch.Element("entity")!.Add(new XElement("filter", new XAttribute("type", "and"),
                Lookup("new_dedication_booking_new_fee", dedicationBookingName, "new_dedication_booking",
                    dedicationBookingId, nameof(dedicationBookingId)),
                Condition("new_paid_period", "eq", paidPeriod), Condition("statecode", "eq", "0")));
            return Query(fetch, false);
        }

        /// <summary>
        /// 保留奉獻查詢既有欄位、名稱排序與分類非空條件，只修正 XML 安全及發布前 ID 驗證。
        /// 每次呼叫直接使用借入 SDK，不建立跨教會可變狀態或保留回傳集合。
        /// </summary>
        /// <param name="contactName">lookup 顯示名稱，可含特殊字元。</param>
        /// <param name="contactId">非空資料庫聯絡人 GUID。</param>
        /// <returns>原 SDK 單頁結果；同名不同 ID 完整保留，ID 衝突整頁拒絕。</returns>
        public EntityCollection RetrieveDedicationFee(string contactName, string contactId)
        {
            return Query(CreateDedicationFetch(contactName, contactId, false), true);
        }

        /// <summary>
        /// 在原奉獻查詢加上含端點日期與原付款狀態白名單；日期是呼叫端已決定的業務日期，
        /// 以 InvariantCulture 的西曆輸出，不作 UTC／時區偏移，也不修改 ambient culture。
        /// </summary>
        /// <param name="contactName">lookup 顯示名稱，不作授權。</param>
        /// <param name="contactId">非空聯絡人 GUID。</param>
        /// <param name="startDate">含端點起日，不大於迄日。</param>
        /// <param name="endDate">含端點迄日。</param>
        /// <returns>完整驗證的一頁結果；原 SDK 失敗不改成空集合。</returns>
        public EntityCollection RetrieveDedicationFeeByDateRange(string contactName, string contactId, DateTime startDate, DateTime endDate)
        {
            if (startDate.Date > endDate.Date) throw new ArgumentException("Invalid date range.", nameof(endDate));
            var fetch = CreateDedicationFetch(contactName, contactId, true);
            var status = new XElement("condition", new XAttribute("attribute", "new_pay_status"), new XAttribute("operator", "in"));
            foreach (var value in new[] { "100000001", "100000002", "100000003", "100000004", "100000006" })
                status.Add(new XElement("value", value));
            fetch.Element("entity")!.Element("filter")!.Add(status,
                Condition("new_pay_date", "on-or-after", startDate.ToString("yyyy-M-d", CultureInfo.InvariantCulture)),
                Condition("new_pay_date", "on-or-before", endDate.ToString("yyyy-M-d", CultureInfo.InvariantCulture)));
            return Query(fetch, true);
        }

        /// <summary>
        /// 依學習課紀錄 ID 查已繳收費單，還原舊來源的 pay_date 非空條件及投影／排序。
        /// 名稱參數原本未使用，保留相容但不把名稱插入查詢或當作授權界線。
        /// </summary>
        /// <param name="storLessonName">僅為舊 API 相容保留，不保存或使用。</param>
        /// <param name="storLessonId">非空學習課紀錄資料庫 GUID。</param>
        /// <returns>完整驗證、保留原分頁資訊的已繳收費頁。</returns>
        public EntityCollection GetPaidFee(string storLessonName, string storLessonId)
        {
            return Query(CreateLessonFeeFetch(storLessonId, true), false);
        }

        /// <summary>
        /// 依學習課紀錄 ID 查未繳收費單，還原舊來源 pay_date 為 null 的條件，不自行推導付款狀態。
        /// 同名不同資料庫 ID 保留，任何重複／欠缺 ID 在回傳前拒絕整頁。
        /// </summary>
        /// <param name="storLessonName">僅為舊 API 相容保留，不保存或使用。</param>
        /// <param name="storLessonId">非空學習課紀錄資料庫 GUID。</param>
        /// <returns>原投影及排序的完整 SDK 查詢頁。</returns>
        public EntityCollection GetNotYetPaidFee(string storLessonName, string storLessonId)
        {
            return Query(CreateLessonFeeFetch(storLessonId, false), false);
        }

        /// <summary>
        /// 使用同一訂單文字比對信用卡或 ATM 訂單欄位；保留 AND 內 OR 結構，名稱不是唯一鍵。
        /// 文字只作 XAttribute 值，不接受呼叫端 XML；空白拒絕，特殊字元可安全逐字還原。
        /// </summary>
        /// <param name="orderNumber">非空訂單號；仍須由 host 驗證該訂單屬於授權範圍。</param>
        /// <returns>已驗 ID 的單頁收費資料，不按訂單文字合併不同記錄。</returns>
        public EntityCollection RetrieveFeeByFetchXmlOrderNumber(string orderNumber)
        {
            RequireText(orderNumber, nameof(orderNumber));
            var fetch = CreateFetch("new_fee", OrderFeeFields, "new_name", false);
            fetch.Element("entity")!.Add(new XElement("filter", new XAttribute("type", "and"),
                new XElement("filter", new XAttribute("type", "or"),
                    Condition("new_q_pay_card_order_no", "eq", orderNumber),
                    Condition("new_q_pay_order_atm_no", "eq", orderNumber))));
            return Query(fetch, false);
        }

        /// <summary>建立單次奉獻查詢候選；字串陣列每次配置，不公開或保留跨操作的可變欄位集合。</summary>
        private static XElement CreateDedicationFetch(string name, string id, bool dateRange)
        {
            var fields = new List<string> { "new_feeid", "new_name", "createdon", "new_pay_date", "new_fee_shoud_pay",
                "new_fee_really_paid", "new_pay_way", "new_category", "new_others" };
            if (dateRange) fields.Add("new_paid_period");
            var fetch = CreateFetch("new_fee", fields, "new_name", false);
            fetch.Element("entity")!.Add(new XElement("filter", new XAttribute("type", "and"),
                Lookup("new_contact_new_fee", name, "contact", id, "contactId"),
                new XElement("condition", new XAttribute("attribute", "new_category"), new XAttribute("operator", "not-null"))));
            return fetch;
        }

        /// <summary>依舊來源建立已繳／未繳的共用條件，僅差投影及 null 運算子，不插入未使用顯示名稱。</summary>
        private static XElement CreateLessonFeeFetch(string id, bool paid)
        {
            var validId = RequireId(id, "storLessonId");
            var fields = paid ? new[] { "new_feeid", "new_name", "new_pay_date", "new_fee_really_paid" }
                : new[] { "new_feeid", "new_name", "new_fee_amount" };
            var fetch = CreateFetch("new_fee", fields, "new_name", false);
            fetch.Element("entity")!.Add(new XElement("filter", new XAttribute("type", "and"),
                Condition("new_stor_lessons_new_fee", "eq", validId),
                new XElement("condition", new XAttribute("attribute", "new_pay_date"), new XAttribute("operator", paid ? "not-null" : "null"))));
            return fetch;
        }

        /// <summary>只接受程式碼擁有的結構／欄位名稱；每次建立新的有限條件樹，不共享可變 XML。</summary>
        private static XElement CreateFetch(string entityName, IEnumerable<string> fields, string order, bool descending, int? top = null)
        {
            var entity = new XElement("entity", new XAttribute("name", entityName));
            foreach (var field in fields) entity.Add(new XElement("attribute", new XAttribute("name", field)));
            entity.Add(new XElement("order", new XAttribute("attribute", order), new XAttribute("descending", descending ? "true" : "false")));
            var fetch = new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("output-format", "xml-platform"),
                new XAttribute("mapping", "logical"), new XAttribute("distinct", "false"));
            if (top.HasValue) fetch.Add(new XAttribute("top", top.Value));
            fetch.Add(entity);
            return fetch;
        }

        /// <summary>固定結構名稱與運算子只來自程式；資料值由 XML 編碼，不以字串拼接插入結構。</summary>
        private static XElement Condition(string field, string operation, string value)
        {
            return new XElement("condition", new XAttribute("attribute", field), new XAttribute("operator", operation), new XAttribute("value", value));
        }

        /// <summary>先驗非空資料庫 GUID 再包為原有 lookup 形式；顯示名稱安全編碼且不構成權限。</summary>
        private static XElement Lookup(string field, string name, string entityType, string id, string parameterName)
        {
            return new XElement("condition", new XAttribute("attribute", field), new XAttribute("operator", "eq"),
                new XAttribute("uiname", name ?? string.Empty), new XAttribute("uitype", entityType),
                new XAttribute("value", "{" + RequireId(id, parameterName) + "}"));
        }

        /// <summary>拒絕空／非法識別碼且不回顯輸入；統一成 D 格式，名稱或暫存索引不能代替資料庫 ID。</summary>
        private static string RequireId(string value, string parameterName)
        {
            Guid id;
            if (!Guid.TryParse(value, out id) || id == Guid.Empty) throw new ArgumentException("A non-empty database identifier is required.", parameterName);
            return id.ToString("D");
        }

        /// <summary>阻擋空白查詢條件；不回顯文字，原字元及大小寫由資料庫原契約決定。</summary>
        private static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty query value is required.", parameterName);
        }

        /// <summary>
        /// 保留原操作所用SDK入口，委派共用單頁發布守門，避免Fetch與Fee兩套回應驗證漂移。
        /// 有top時先拒超量，完整驗ID才回傳；不保存結果、不處置借入SDK、不包裝真正SDK例外。
        /// </summary>
        private EntityCollection Query(XElement fetch, bool direct)
        {
            return SdkDataQuery.RunFetch(_organizationService, fetch, !direct);
        }
    }
}
