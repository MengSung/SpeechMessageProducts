#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.DataOperations;
using ToolUtilityNameSpace.FeeOperations;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 七個舊Fetch公開入口的純SDK實作；保留投影、排序、link、top及實際使用的篩選差異。
    /// 名稱／資料庫ID只構成查詢條件，不是授權；host必須先驗證使用者、教會及組織scope。
    /// </summary>
    /// <remarks>
    /// 只借用同scope的SDK，不保存logger、credential、查詢、結果或快取，不建立背景Task、timer或queue。
    /// 每次SDK同步在caller執行緒執行；單頁先核對型別、top與整頁正式ID，保留原分頁metadata。
    /// 不把top頁冒稱全表或按名稱去重。故障不catch、不回假空集合，由最終host交共用告警入口。
    /// Dispose停止新入口並清借用參照，已開始的局部SDK工作可drain；host必須先排空才關SDK。
    /// 兩個固定業務謂詞由中立resx編譯表複製成私有字串，不隨顯示語言翻譯資料庫條件。
    /// </remarks>
    public class FetchXmlQueryService : IFetchXmlQueryService, IDisposable
    {
        // 私有唯讀模板僅含固定結構名稱，逐次建立局部XML，不公開陣列或保留操作資料。
        private static readonly ReadOnlyCollection<string> ContactLessonFields = Array.AsReadOnly(new[]
        { "createdon", "new_contact_new_stor_lessons", "new_fee", "new_pay_date", "new_current_complete",
            "new_new_disciple_lessons_new_stor_les", "new_stor_lessonsid" });
        private static readonly ReadOnlyCollection<string> LessonFields = Array.AsReadOnly(new[]
        { "createdon", "new_contact_new_stor_lessons", "new_fee", "new_pay_date",
            "new_new_disciple_lessons_new_stor_les", "new_stor_lessonsid" });
        private static readonly ReadOnlyCollection<string> MeetingFields = Array.AsReadOnly(new[]
        { "new_meeting_statisticsid", "new_name", "createdon", "new_sunday_date" });
        private static readonly ReadOnlyCollection<string> ListFields = Array.AsReadOnly(new[]
        { "listname", "createdfromcode", "lastusedon", "purpose", "listid" });
        private static readonly ReadOnlyCollection<string> SmallGroupFields = Array.AsReadOnly(new[]
        { "listname", "createdfromcode", "lastusedon", "purpose", "new_contact_race_leager_list",
            "new_contact_family_leader_list", "listid" });
        private static readonly string ListPurpose = LegacyValue("Fetch.Query.ListPurpose");
        private static readonly string TestListPattern = LegacyValue("Fetch.Query.TestListPattern");
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>保持舊建構簽章與null拒絕；logger只驗證不保存／反射，SDK所有權仍屬原host。</summary>
        /// <param name="logger">來源相容的診斷參數，不得用它延長request或credential生命週期。</param>
        /// <param name="organizationService">已授權scope的SDK；服務不能註冊為跨使用者／租戶singleton。</param>
        public FetchXmlQueryService(object logger, IOrganizationService organizationService)
        {
#if NETFRAMEWORK
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (organizationService == null) throw new ArgumentNullException(nameof(organizationService));
#else
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(organizationService);
#endif
            _organizationService = organizationService;
        }

        /// <summary>
        /// 依聯絡人讀取學習課的一頁，沿用實際生效的RemoveConstrain查詢，不加回舊未使用模板的classification過濾。
        /// top1000、兩個link與欄位完全保留；顯示名稱XML編碼，ID必須非空GUID。
        /// </summary>
        public EntityCollection RetrieveStorLessonsByFetchXml(string contactName, string contactId)
        {
            var sdk = GetService();
            var entity = SdkDataQuery.Entity("new_stor_lessons", ContactLessonFields, "createdon", true);
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Lookup("new_contact_new_stor_lessons", contactName, "contact", contactId),
                SdkDataQuery.Condition("statecode", "eq", "0")),
                ContactLink("mobilephone", "emailaddress1"),
                new XElement("link-entity", new XAttribute("name", "new_disciple_lessons"),
                    new XAttribute("from", "new_disciple_lessonsid"), new XAttribute("to", "new_new_disciple_lessons_new_stor_les"),
                    new XAttribute("alias", "lesson"), new XElement("attribute", new XAttribute("name", "new_name"))));
            return SdkDataQuery.Run(sdk, entity, top: 1000, executeRequest: true);
        }

        /// <summary>依課程讀取學習課，保留三種enroll排除狀態、statuscode非2及啟用條件；不套用聯絡人查詢的不同投影。</summary>
        public EntityCollection RetrieveStorLessonsByDiscipleLessonsFetchXml(string lessonName, string lessonId)
        {
            var sdk = GetService();
            var entity = SdkDataQuery.Entity("new_stor_lessons", LessonFields, "createdon", true);
            entity.Add(SdkDataQuery.Filter(
                new XElement("condition", new XAttribute("attribute", "new_enroll_status"), new XAttribute("operator", "not-in"),
                    new XElement("value", "100000007"), new XElement("value", "100000009"), new XElement("value", "100000003")),
                SdkDataQuery.Lookup("new_new_disciple_lessons_new_stor_les", lessonName, "new_disciple_lessons", lessonId),
                SdkDataQuery.Condition("statuscode", "ne", "2"), SdkDataQuery.Condition("statecode", "eq", "0")),
                ContactLink("fullname", "mobilephone"));
            return SdkDataQuery.Run(sdk, entity, top: 1000, executeRequest: true);
        }

        /// <summary>
        /// 認獻查詢委派唯一Fee實作，仍使用Execute及top1000；不保留FeeService而讓Dispose後仍有可繞過的SDK參照。
        /// 已取得局部SDK的操作可排空，暫存委派不擁有連線或任何額外資源。
        /// </summary>
        public EntityCollection RetrieveDedicationBookingByFetchXml(string contactName, string contactId) =>
            new FeeService(GetService()).RetrieveDedicationBooking(contactName, contactId);

        /// <summary>主日日期以原西曆yyyy-MM-dd條件序列化，top100及原啟用狀態不變；不在此猜時區或改ambient culture。</summary>
        public EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime sundayDate)
        {
            var sdk = GetService();
            var entity = SdkDataQuery.Entity("new_meeting_statistics", MeetingFields, "createdon", true);
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Condition("statuscode", "eq", "1"),
                SdkDataQuery.Condition("new_sunday_date", "on", sundayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                SdkDataQuery.Condition("statecode", "eq", "0")));
            return SdkDataQuery.Run(sdk, entity, top: 100, executeRequest: true);
        }

        /// <summary>收費查詢逐次借用SDK委派唯一Fee邏輯，保留期數／啟用條件、top500與原查詢例外；名稱不是權限。</summary>
        public EntityCollection RetrieveFeeByFetchXml(string dedicationBookingName, string dedicationBookingId, string paidPeriod) =>
            new FeeService(GetService()).RetrieveFee(dedicationBookingName, dedicationBookingId, paidPeriod);

        /// <summary>保留原點名名單投影、purpose/app/status/state條件及top500；不暗中套用小組集合的測試名稱排除。</summary>
        public EntityCollection RetrieveListByFetchXml()
        {
            var sdk = GetService();
            var entity = SdkDataQuery.Entity("list", ListFields, "listname");
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Condition("statuscode", "eq", "0"),
                SdkDataQuery.Condition("purpose", "eq", ListPurpose), SdkDataQuery.Condition("new_app_named", "eq", "1"),
                SdkDataQuery.Condition("statecode", "eq", "0")));
            return SdkDataQuery.Run(sdk, entity, top: 500, executeRequest: true);
        }

        /// <summary>保留小組集合額外兩個lookup欄位、原條件順序及測試名稱not-like；top頁不是完整全表，分頁資訊不丟棄。</summary>
        public EntityCollection RetrieveSmallGroupListCollectionByFetchXml()
        {
            var sdk = GetService();
            var entity = SdkDataQuery.Entity("list", SmallGroupFields, "listname");
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Condition("new_app_named", "eq", "1"),
                SdkDataQuery.Condition("statuscode", "eq", "0"), SdkDataQuery.Condition("purpose", "eq", ListPurpose),
                SdkDataQuery.Condition("listname", "not-like", TestListPattern), SdkDataQuery.Condition("statecode", "eq", "0")));
            return SdkDataQuery.Run(sdk, entity, top: 500, executeRequest: true);
        }

        /// <summary>只建立本次link容器；兩個原入口具有不同欄位但同一outer/contact結構，不共享可變XElement。</summary>
        private static XElement ContactLink(string firstField, string secondField) =>
            new XElement("link-entity", new XAttribute("name", "contact"), new XAttribute("from", "contactid"),
                new XAttribute("to", "new_contact_new_stor_lessons"), new XAttribute("visible", "false"),
                new XAttribute("link-type", "outer"), new XAttribute("alias", "contact"),
                new XElement("attribute", new XAttribute("name", firstField)),
                new XElement("attribute", new XAttribute("name", secondField)));

        /// <summary>僅在型別初始化複製兩個中立固定謂詞；不公開generated陣列、不保留操作資料或隨語系改查詢。</summary>
        private static string LegacyValue(string key)
        {
            foreach (var row in FetchQueryLegacyData.Rows)
                if (string.Equals(row[0], key, StringComparison.Ordinal))
                    return row[1] ?? throw new InvalidOperationException("A neutral query predicate is required.");
            throw new InvalidOperationException("A neutral query predicate is missing.");
        }

        /// <summary>取得單次借用參照，不以全域鎖序列化不同scope；Dispose競爭時只有已取得參照的舊工作可以繼續。</summary>
        private IOrganizationService GetService()
        {
            var sdk = Volatile.Read(ref _organizationService);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(FetchXmlQueryService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return sdk ?? throw new ObjectDisposedException(nameof(FetchXmlQueryService));
        }

        /// <summary>標準可覆寫Dispose hook只清借用SDK參照，冪等且不阻塞外部同步I/O；host負責排空及真正連線釋放。</summary>
        /// <param name="disposing">本型別無自有unmanaged或背景資源；保留衍生類可延伸的標準清理形狀。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Exchange(ref _organizationService, null);
        }

        /// <summary>由服務owner停止新入口，不Dispose借入SDK、不建立終結器或留下logger/Fee委派強參照。</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
