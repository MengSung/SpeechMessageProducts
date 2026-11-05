# CCG reviewer Task: integration-p1-04-operations

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
P1-04 Operations抽取及既有審查證據補齊，唯讀審查；禁止擴讀任何其他檔案尤其Facade/credentials。只審本固定快照，不執行、不改檔。
需求：SDK-only Query到Core(net462;net10)，先舊行為golden綠再純化再git mv；不改原欄位排序top及status白名單，不cache或dispose借入SDK，完整穩定ID同名不同ID保留，真正異常交host通知，繁中完整XMLdoc/noBOM/CRLF。
本輪新Appointment/Lessons/MeetingStatistics三組：搬前13/13；故障安全測試81紅/18綠，修後搬前112/112；搬後全ToolUtility net48 207/207(真net462)及net10 282/282，Core覆蓋98.78/100。QueryByDate舊未過濾日期刻意保留；Meeting舊uitype提示值也不猜改。共同內部SdkDataQuery負責安全XML/日期與整頁ID，不建SDK/Queue/cache/Task，沒有隱藏重試。
前review20261105-133329雙模型完整，無Critical/High，Claude三Warning都是快照/執行證據缺：此輪附Core專案(無新NoWarn)、舊Entity character/DI/Target tests+golden及實際TRX Counters。前review已確認Fetch根表/DTD修補和CI精確集合，這輪不需重複CI。net462編譯String.IsNullOrEmpty缺nullable annotation，僅等價改顯式null/Length判斷，無警告抑制。
請具體查：golden是否真正符合附舊來源、參數跳脫/日期不變文化、ID發布/隔離、資源生命周期、公開契約相容、測試是否真的執行產品且斷言充分。Critical/High/Warning/Info，指精確檔案/位置/可重現路徑。全P1仍0/12，這不是實機或完整P104完成證據，其他候選仍待處理。

## ToolUtility.Core/AppointmentOperations/AppointmentService.cs
```
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

```

## ToolUtility.Core/AppointmentOperations/IAppointmentService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AppointmentOperations
{
    /// <summary>
    /// 已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。
    /// 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 host 共用例外通知邊界。
    /// 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
    /// </summary>
    public interface IAppointmentService
    {
        /// <summary>保留舊版未依日期過濾、全欄位 QueryByAttribute 的相容行為；如需日期過濾使用明確區間 API。整頁穩定 ID 驗證後才發布。</summary>
        /// <param name="selectedDate">舊版未使用的相容日期參數，不作授權或新增過濾。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByDate(DateTime selectedDate);

        /// <summary>依原 scheduledstart 含端點區間與投影查約會；拒絕反向區間，固定西曆日期，原分頁資訊完整保留。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByDateRange(DateTime startDate, DateTime endDate);

        /// <summary>依已授權聯絡人、今年與原三個簽核狀態查詢；顯示文字安全編碼，正式 GUID 驗證不能由名稱取代。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByContactWithinYear(string contactName, string contactId);

        /// <summary>在原日期區間追加既有排程值；文字只作 XML 屬性，不可注入或省略查詢條件。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <param name="scheduleType">原 SDK 排程值，安全編碼且不保存。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByDateRangeAndScheduleType(DateTime startDate, DateTime endDate, string scheduleType);
    }
}

```

## ToolUtility.Core/DataOperations/SdkDataQuery.cs
```
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.DataOperations
{
    /// <summary>
    /// 純資料操作的局部 XML 建構與單頁發布契約；僅保留有限結構常數，不保存任一操作資料。
    /// 不建立連線、快取、Task、timer 或通知器；授權與 SDK 生命週期由最終 host owner 負責。
    /// </summary>
    internal static class SdkDataQuery
    {
        /// <summary>拒絕未配置 SDK；回傳借用參照，不移轉 Dispose、憑證或連線所有權。</summary>
        internal static IOrganizationService RequireService(IOrganizationService? service)
        {
#if NETFRAMEWORK
            if (service == null) throw new ArgumentNullException(nameof(service));
#else
            ArgumentNullException.ThrowIfNull(service);
#endif
            return service;
        }

        /// <summary>建立新的根實體欄位與排序樹；輸入是各服務私有唯讀結構名稱，非 caller 授權依據。</summary>
        internal static XElement Entity(string name, IEnumerable<string> fields, string order, bool descending = false)
        {
            var entity = new XElement("entity", new XAttribute("name", name));
            foreach (var field in fields) entity.Add(new XElement("attribute", new XAttribute("name", field)));
            entity.Add(new XElement("order", new XAttribute("attribute", order),
                new XAttribute("descending", descending ? "true" : "false")));
            return entity;
        }

        /// <summary>建立操作局部 and 條件；所有 XElement 僅屬該次候選，不供跨執行緒共用。</summary>
        internal static XElement Filter(params XElement[] conditions) =>
            new XElement("filter", new XAttribute("type", "and"), conditions);

        /// <summary>條件文字只能進屬性值，自動編碼特殊字元；無值運算子不製造 value 屬性。</summary>
        internal static XElement Condition(string attribute, string operation, string? value = null)
        {
            var condition = new XElement("condition", new XAttribute("attribute", attribute),
                new XAttribute("operator", operation));
            if (value != null) condition.Add(new XAttribute("value", value));
            return condition;
        }

        /// <summary>
        /// lookup 必須是非空資料庫 GUID；顯示名稱只供舊 SDK uiname，不是權限或去重鍵。
        /// 拒絕訊息不回顯輸入；合法 GUID 以大括號 D 格式保留既有查詢語義。
        /// </summary>
        internal static XElement Lookup(string attribute, string? displayName, string entityName, string id)
        {
            if (!Guid.TryParse(id, out var parsed) || parsed == Guid.Empty)
                throw new ArgumentException("A database identifier is required.", nameof(id));
            return new XElement("condition", new XAttribute("attribute", attribute), new XAttribute("operator", "eq"),
                new XAttribute("uiname", displayName ?? string.Empty), new XAttribute("uitype", entityName),
                new XAttribute("value", "{" + parsed.ToString("D") + "}"));
        }

        /// <summary>
        /// 日期條件沿用既有西曆日期語義，不受執行緒文化或泰國曆影響；不自行做時區換算。
        /// 時間轉換由上層教會時鐘決定，此處只序列化 caller 已決定的日期。
        /// </summary>
        internal static string Date(DateTime value) => value.ToString("yyyy-M-d", CultureInfo.InvariantCulture);

        /// <summary>拒絕反向日期，不能交換端點或漏掉條件而擴張 caller 的查詢範圍。</summary>
        internal static void RequireRange(DateTime start, DateTime end)
        {
            if (start.Date > end.Date) throw new ArgumentException("Invalid date range.", nameof(end));
        }

        /// <summary>
        /// 包裝原 FetchXML 設定並只送一次 SDK，完整驗證根實體與全部穩定 ID 後才發布；
        /// 原分頁欄位不變，不自動無界讀取下一頁。SDK 故障不 catch 或包裝，交回 host 通知邊界。
        /// </summary>
        internal static EntityCollection Run(IOrganizationService service, XElement entity, bool distinct = false, int? top = null)
        {
            var fetch = new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("output-format", "xml-platform"),
                new XAttribute("mapping", "logical"), new XAttribute("distinct", distinct ? "true" : "false"));
            if (top.HasValue) fetch.Add(new XAttribute("top", top.Value));
            fetch.Add(entity);
            var candidate = service.RetrieveMultiple(new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting)));
            return ValidatePage(candidate, (string)entity.Attribute("name")!);
        }

        /// <summary>
        /// 所有候選只在單次操作內存活；先驗整頁 ID 非空、無重複且根實體相符，才回原候選。
        /// 不以名稱去重、不修改原頁、不持有 Entity 或 HashSet；空頁合法但 null 頁屬 SDK 完整性故障。
        /// </summary>
        internal static EntityCollection ValidatePage(EntityCollection? candidate, string entityName)
        {
            if (candidate == null) throw new InvalidOperationException("The SDK returned no query page.");
            var identities = new HashSet<Guid>();
            foreach (var row in candidate.Entities)
                if (row == null || row.Id == Guid.Empty || !string.Equals(row.LogicalName, entityName, StringComparison.Ordinal)
                    || !identities.Add(row.Id))
                    throw new InvalidOperationException("Query page contains invalid or duplicate database identities.");
            return candidate;
        }
    }
}

```

## ToolUtility.Core/EntityOperations/EntityQueryService.cs
```
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 不保存查詢結果的純 SDK 實體讀取器；授權、組織與服務身分必須由 host 先驗證。
    /// 每次建立操作局部候選，整頁穩定 ID 驗證後才回傳，不以名稱去重或發布部分結果。
    /// </summary>
    /// <remarks>
    /// SDK 只借入，不接管 Dispose／連線或憑證。服務應與原授權 scope 同生命週期，禁止跨租戶 singleton。
    /// 不建立 cache、timer、queue、Task.Run 或隱藏重試；SDK 的逾時由 host gateway／連線 owner 控制。
    /// Dispose 停止新操作並清除借用參照；已取得局部參照的同步操作可完成，host 須先 drain 才關閉 SDK。
    /// 真實 SDK 例外原樣上傳最終 host 共用通知邊界，不在純核心記錄原訊息或另建 LINE client。
    /// </remarks>
    public class EntityQueryService : IEntityQueryService, IDisposable
    {
        // 僅共享有限的唯讀結構名稱，不含使用者、組織、查詢條件或 SDK 結果。
        private static readonly ReadOnlyCollection<string> TaskFields = Array.AsReadOnly(new[]
        {
            "subject", "statecode", "prioritycode", "scheduledend", "createdby",
            "regardingobjectid", "activityid", "description"
        });
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>
        /// 保留舊 optional 建構式但不保存未使用的任意 logger，避免延長 request／憑證參照。
        /// 為相容允許未初始化 SDK；首次操作會明確拒絕，不將設定失敗當成查無資料。
        /// </summary>
        /// <param name="logger">只為來源相容保留，不使用、不保存，也不 Dispose。</param>
        /// <param name="organizationService">呼叫端已授權且擁有的 SDK；null 僅供舊建構相容。</param>
        public EntityQueryService(object? logger = null, IOrganizationService? organizationService = null)
        {
            _organizationService = organizationService;
        }

        /// <summary>
        /// 依舊單筆契約讀完整欄位，不走快取。SDK 回應 ID／實體必須與請求完全相符，否則拒絕發布。
        /// </summary>
        /// <param name="entityName">伺服器決定的合法資料庫 logical name，不構成授權。</param>
        /// <param name="entityId">已授權的非空資料庫 GUID。</param>
        /// <returns>本次操作擁有且已驗證的單筆實體。</returns>
        public Entity RetrieveEntity(string entityName, Guid entityId)
        {
            var service = GetService();
            RequireLogicalName(entityName, nameof(entityName));
            if (entityId == Guid.Empty) throw new ArgumentException("A database identifier is required.", nameof(entityId));
            var candidate = service.Retrieve(entityName, entityId, new ColumnSet(true));
            if (candidate == null || candidate.Id != entityId || !string.Equals(candidate.LogicalName, entityName, StringComparison.Ordinal))
                throw new InvalidOperationException("The SDK returned a mismatched database identity.");
            return candidate;
        }

        /// <summary>
        /// 還原舊來源直接 SDK 讀取的明確入口；每次重新查詢，不回傳前次 caller 修改的物件。
        /// 與 RetrieveEntity 共用相同識別驗證及故障傳遞，不複製第二套資料／快取邏輯。
        /// </summary>
        /// <param name="entityName">受信任的合法 logical name，含 XML 特殊字元的結構名稱會被拒絕。</param>
        /// <param name="entityId">伺服器已授權的非空資料庫 GUID。</param>
        /// <returns>本次 SDK 的完整欄位實體，生命週期只屬於呼叫端操作。</returns>
        public Entity RetrieveEntityNoCache(string entityName, Guid entityId) => RetrieveEntity(entityName, entityId);

        /// <summary>
        /// 保留 TopCount=1、AllColumns 與啟用條件的單筆欄位查詢；型別化值不拼接成 XML。
        /// 此 API 的一筆語義不代表按顯示名稱合併資料；空頁回 null，SDK 故障不回 null。
        /// </summary>
        /// <param name="entityName">已授權的資料庫實體名稱。</param>
        /// <param name="fieldName">伺服器決定的資料庫欄位名稱。</param>
        /// <param name="fieldValue">依原 SDK 型別化傳入的查詢值，不保存於服務。</param>
        /// <returns>完整驗頁後的首筆資料，或真正空頁的 null。</returns>
        public Entity? RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
        {
            var service = GetService();
            RequireLogicalName(entityName, nameof(entityName));
            RequireLogicalName(fieldName, nameof(fieldName));
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true), TopCount = 1 };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);
            var candidate = ValidatePage(service.RetrieveMultiple(query), entityName);
            return candidate.Entities.Count == 0 ? null : candidate.Entities[0];
        }

        /// <summary>
        /// 執行呼叫端已授權的查詢，保留既有「PageInfo 為 null 才補 5000／第一頁」相容行為。
        /// 不自動遍歷無界頁面；單頁 MoreRecords／PagingCookie 原樣交由原分頁 owner 繼續處理。
        /// </summary>
        /// <param name="query">操作擁有的查詢，不可在執行期間由其他執行緒改動。</param>
        /// <returns>整頁穩定 ID 驗證後的 SDK 候選，不保存或共享。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var service = GetService();
#if NETFRAMEWORK
            if (query == null) throw new ArgumentNullException(nameof(query));
#else
            ArgumentNullException.ThrowIfNull(query);
#endif
            var entityName = ExpectedEntity(query);
            if (query is QueryExpression expression && expression.PageInfo == null)
                expression.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            return ValidatePage(service.RetrieveMultiple(query), entityName);
        }

        /// <summary>
        /// 保留依名稱查第一個啟用帳戶 ID 的相容 API，只投影 accountid，不作所有記錄的名稱去重。
        /// host 必須先完成業務授權；未查到回 Guid.Empty，SDK 故障與識別衝突仍直接拋出。
        /// </summary>
        /// <param name="accountName">依原資料庫語義查詢的名稱，不是授權或唯一識別。</param>
        /// <returns>已驗證的首筆資料庫 ID，或真正空頁時的 Guid.Empty。</returns>
        public Guid RetrieveAccountByName(string accountName)
        {
            var service = GetService();
            var query = new QueryByAttribute("account") { ColumnSet = new ColumnSet("accountid"), TopCount = 1 };
            query.Attributes.AddRange("name", "statecode");
            query.Values.AddRange(accountName, 0);
            var candidate = ValidatePage(service.RetrieveMultiple(query), "account");
            return candidate.Entities.Count == 0 ? Guid.Empty : candidate.Entities[0].Id;
        }

        /// <summary>
        /// 保留工作欄位、100 筆上限、排序與啟用條件；主旨只能作 XML 屬性值，不能注入結構。
        /// 完整驗證同一消費頁的穩定 ID，合法同名但不同 ID 的工作不被刪減。
        /// </summary>
        /// <param name="subject">原查詢文字；null 依舊 API 視為空文字，不構成授權。</param>
        /// <returns>完整驗證的單頁工作結果；SDK 例外不被空集合遮蔽。</returns>
        public EntityCollection RetrieveTaskBySubject(string subject)
        {
            var service = GetService();
            var entity = new XElement("entity", new XAttribute("name", "task"));
            foreach (var field in TaskFields) entity.Add(new XElement("attribute", new XAttribute("name", field)));
            entity.Add(new XElement("order", new XAttribute("attribute", "createdon"), new XAttribute("descending", "true")),
                new XElement("filter", new XAttribute("type", "and"),
                    new XElement("condition", new XAttribute("attribute", "subject"), new XAttribute("operator", "eq"),
                        new XAttribute("value", subject ?? string.Empty)),
                    new XElement("condition", new XAttribute("attribute", "statecode"), new XAttribute("operator", "eq"), new XAttribute("value", "0"))));
            var fetch = new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("output-format", "xml-platform"),
                new XAttribute("mapping", "logical"), new XAttribute("distinct", "false"), new XAttribute("top", "100"), entity);
            var response = (RetrieveMultipleResponse)service.Execute(new RetrieveMultipleRequest
                { Query = new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting)) });
            return ValidatePage(response.EntityCollection, "task");
        }

        /// <summary>
        /// 保留完整 RetrieveMultipleRequest（含 SDK 選項）並驗證全部回應 ID，完成前不發布部分頁。
        /// 不攔截或重新包裝 SDK 真實故障，避免丟失 host 共用通知入口所需的原 Exception。
        /// </summary>
        /// <param name="request">呼叫者操作擁有的請求及已授權 Query，不保存於服務。</param>
        /// <returns>原分頁資訊與欄位均保留的完整頁。</returns>
        public EntityCollection ExecuteRetrieveMultiple(RetrieveMultipleRequest request)
        {
            var service = GetService();
#if NETFRAMEWORK
            if (request == null) throw new ArgumentNullException(nameof(request));
#else
            ArgumentNullException.ThrowIfNull(request);
#endif
            if (request.Query == null) throw new ArgumentException("A query is required.", nameof(request));
            var entityName = ExpectedEntity(request.Query);
            var response = (RetrieveMultipleResponse)service.Execute(request);
            return ValidatePage(response.EntityCollection, entityName);
        }

        /// <summary>
        /// 冪等停止新呼叫並清除保存的借入服務參照；不 Dispose 呼叫端擁有的 SDK，也不阻塞等待外部 I/O。
        /// 已借到局部參照的同步呼叫可完成，host 必須 drain 後才處置其原連線 owner。
        /// </summary>
        /// <param name="disposing">保留既有可覆寫清理契約；本類無自有 native／managed disposable。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Exchange(ref _organizationService, null);
        }

        /// <summary>由本服務 owner 確定性停止新操作；重複呼叫安全，不轉移借入 SDK 的所有權。</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>取得單次借用參照；Dispose 前已開始的同步 I/O 不被暗中取消，新操作則立即拒絕。</summary>
        private IOrganizationService GetService()
        {
            var service = Volatile.Read(ref _organizationService);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(EntityQueryService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return service ?? throw new InvalidOperationException("OrganizationService is not initialized.");
        }

        /// <summary>只接受 SDK logical name 字元，不把 XML 編碼當作任意結構名稱的合法性或授權。</summary>
        private static void RequireLogicalName(string value, string parameter)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("A logical name is required.", parameter);
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9') && character != '_')
                    throw new ArgumentException("An invalid logical name was supplied.", parameter);
        }

        /// <summary>
        /// 在任何 I/O 前辨識根實體，包含 FetchExpression，避免通用入口略過跨實體回應檢查。
        /// 此驗證不取代 host 授權，不改寫原 Query 或追加條件；未知 SDK 查詢型別明確拒絕而非降級。
        /// </summary>
        private static string ExpectedEntity(QueryBase query)
        {
            var entityName = query is QueryExpression expression ? expression.EntityName
                : query is QueryByAttribute attributes ? attributes.EntityName
                : query is FetchExpression fetch ? ExpectedFetchEntity(fetch)
                : throw new ArgumentException("Unsupported query type.", nameof(query));
            RequireLogicalName(entityName, nameof(query));
            return entityName;
        }

        /// <summary>
        /// 僅串流辨識一個無命名空間的 fetch／entity 根表，不建立或保存完整 XML 物件樹。
        /// DTD 禁止、resolver 為 null，不能讀檔或網路；字元上限等於既有輸入長度，禁止展開額外內容。
        /// Reader 與 StringReader 由單次操作擁有並在成功／拒絕／解析失敗時確定性 Dispose；
        /// 不保存查詢文字、SDK 或使用者資料。壞 XML 的固定外層訊息不包含原輸入。
        /// </summary>
        private static string ExpectedFetchEntity(FetchExpression query)
        {
            if (string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentException("A FetchXML query is required.", nameof(query));
            try
            {
                using var text = new StringReader(query.Query);
                using var reader = XmlReader.Create(text, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = query.Query.Length
                });
                if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "fetch"
                    || reader.NamespaceURI.Length != 0)
                    throw new ArgumentException("Invalid FetchXML root.", nameof(query));
                string? entityName = null;
                var entityCount = 0;
                while (reader.Read())
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1
                        && reader.LocalName == "entity" && reader.NamespaceURI.Length == 0)
                    {
                        entityCount++;
                        entityName = reader.GetAttribute("name");
                    }
                if (entityCount != 1 || entityName == null || entityName.Length == 0)
                    throw new ArgumentException("A single FetchXML entity is required.", nameof(query));
                return entityName;
            }
            catch (XmlException failure)
            {
                throw new ArgumentException("Invalid FetchXML query.", nameof(query), failure);
            }
        }

        /// <summary>
        /// 候選頁只活至本次操作：驗全部 ID 非空／不重複及查詢根實體，才原子式回傳。
        /// HashSet 與候選沒有 static／cache 引用；衝突不回顯 ID 或本文，也不改動原候選。
        /// </summary>
        private static EntityCollection ValidatePage(EntityCollection? candidate, string entityName)
        {
            if (candidate == null) throw new InvalidOperationException("The SDK returned no query page.");
            var identities = new HashSet<Guid>();
            foreach (var row in candidate.Entities)
                if (row == null || row.Id == Guid.Empty || string.IsNullOrEmpty(row.LogicalName)
                    || !string.Equals(row.LogicalName, entityName, StringComparison.Ordinal)
                    || !identities.Add(row.Id))
                    throw new InvalidOperationException("Query page contains invalid or duplicate database identities.");
            return candidate;
        }
    }
}

```

## ToolUtility.Core/EntityOperations/IEntityQueryService.cs
```
#nullable enable
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 同一已授權 host scope 的純 SDK 查詢契約；不擁有連線、不保存結果或個別使用者狀態。
    /// 單筆核對請求 ID，多筆驗證整個消費頁穩定 ID 後才回傳；SDK 故障交回 host 最終通知邊界。
    /// </summary>
    public interface IEntityQueryService
    {
        /// <summary>依資料庫 ID 直接讀完整欄位，回應 ID 與實體不符時拒絕，不走共用快取。</summary>
        /// <param name="entityName">伺服器已授權的合法實體 logical name。</param>
        /// <param name="entityId">非空的正式資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證實體。</returns>
        Entity RetrieveEntity(string entityName, Guid entityId);

        /// <summary>明確的不經快取入口；每次進 SDK，不能讀回上次 caller 修改或其他 scope 的 Entity。</summary>
        /// <param name="entityName">合法實體名稱，特殊 XML 字元不得作結構名稱。</param>
        /// <param name="entityId">伺服器已授權的非空資料庫 GUID。</param>
        /// <returns>新一次 SDK 完整欄位結果；故障不回空或假資料。</returns>
        Entity RetrieveEntityNoCache(string entityName, Guid entityId);

        /// <summary>保留啟用條件與 TopCount=1 單筆語義，以型別化值查詢，不把名稱當唯一資料列 ID。</summary>
        /// <param name="entityName">受信任實體 logical name。</param>
        /// <param name="fieldName">受信任欄位 logical name。</param>
        /// <param name="fieldValue">原資料庫比對值，不保存於服務。</param>
        /// <returns>驗證完整頁後的第一筆，或真正空頁的 null。</returns>
        Entity? RetrieveEntityByField(string entityName, string fieldName, string fieldValue);

        /// <summary>執行已授權操作局部查詢，保留 SDK 分頁資訊並拒絕同頁重複或欠缺正式 ID。</summary>
        /// <param name="query">由呼叫者擁有且執行期間不被並行修改的 Query。</param>
        /// <returns>完整驗證的單頁，呼叫端負責後續分頁，不共享可變集合。</returns>
        EntityCollection RetrieveMultiple(QueryBase query);

        /// <summary>保留依名稱取第一個啟用帳戶 ID 的相容 API，不是多筆集合的名稱去重。</summary>
        /// <param name="accountName">原資料庫名稱條件；不能構成身分或授權。</param>
        /// <returns>首筆正式 ID，或真正空頁的 Guid.Empty；故障直接拋出。</returns>
        Guid RetrieveAccountByName(string accountName);

        /// <summary>安全 XML 查啟用工作，保留100筆上限／原投影／排序，同名不同 ID 的列完整保留。</summary>
        /// <param name="subject">只作屬性值的主旨文字，特殊字元不能注入條件。</param>
        /// <returns>整頁 ID 驗證後的操作局部 SDK 結果。</returns>
        EntityCollection RetrieveTaskBySubject(string subject);

        /// <summary>原樣提交已授權的 RetrieveMultipleRequest，候選完成且全頁 ID 驗證後才發布。</summary>
        /// <param name="request">含 Query 的操作局部 SDK 請求，呼叫期間不可並行修改。</param>
        /// <returns>保留欄位與分頁資訊的完整頁；原 SDK 例外傳至 host。</returns>
        EntityCollection ExecuteRetrieveMultiple(RetrieveMultipleRequest request);
    }
}

```

## ToolUtility.Core/FeeOperations/FeeService.cs
```
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

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
            return Query(fetch, "new_dedication_booking", false);
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
            return Query(fetch, "new_fee", false);
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
            return Query(CreateDedicationFetch(contactName, contactId, false), "new_fee", true);
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
            return Query(fetch, "new_fee", true);
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
            return Query(CreateLessonFeeFetch(storLessonId, true), "new_fee", false);
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
            return Query(CreateLessonFeeFetch(storLessonId, false), "new_fee", false);
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
            return Query(fetch, "new_fee", false);
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
        /// 保留原操作所用SDK入口，完成I/O後驗證整頁才一次回傳；HashSet僅活至本次呼叫結束。
        /// 不存結果、不釋放借入服務、不攔截真實SDK例外；資料衝突不含ID／欄位本文且不靜默去重。
        /// </summary>
        private EntityCollection Query(XElement fetch, string entityName, bool direct)
        {
            var expression = new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting));
            var candidate = direct ? _organizationService.RetrieveMultiple(expression)
                : ((RetrieveMultipleResponse)_organizationService.Execute(new RetrieveMultipleRequest { Query = expression })).EntityCollection;
            if (candidate == null) throw new InvalidOperationException("The SDK returned no query page.");
            var identities = new HashSet<Guid>();
            foreach (var row in candidate.Entities)
            {
                if (row == null || row.Id == Guid.Empty || !string.Equals(row.LogicalName, entityName, StringComparison.Ordinal)
                    || !identities.Add(row.Id)) throw new InvalidOperationException("Query page contains invalid or duplicate database identities.");
            }
            return candidate;
        }
    }
}

```

## ToolUtility.Core/FeeOperations/IFeeService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.FeeOperations
{
    /// <summary>
    /// 單次授權範圍的收費查詢契約；呼叫者先驗證身分／教會，再提供資料庫 ID。
    /// 所有結果是操作擁有的 SDK 查詢頁，保留分頁資訊；不按名稱去重，ID 衝突整頁拒絕。
    /// 實作不得持有跨使用者狀態或接管借入連線，真正故障傳至 host 共用通知邊界。
    /// </summary>
    public interface IFeeService
    {
        /// <summary>
        /// 依認獻 ID 與期數讀啟用收費頁；文字安全組裝，不允許輸入改變條件樹。
        /// </summary>
        /// <param name="dedicationBookingName">僅供lookup顯示，不作識別或授權。</param>
        /// <param name="dedicationBookingId">非空認獻資料庫GUID。</param>
        /// <param name="paidPeriod">原查詢的非空期數文字。</param>
        /// <returns>保留原投影、排序與500筆上限的SDK頁。</returns>
        EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod);

        /// <summary>依聯絡人ID讀啟用且已核准認獻；不得以空集合代替SDK查詢或失敗。</summary>
        /// <param name="contactName">僅供lookup顯示，不作權限依據。</param>
        /// <param name="contactId">非空聯絡人資料庫GUID。</param>
        /// <returns>保留原建立時間倒序及1000筆上限的SDK頁。</returns>
        EntityCollection RetrieveDedicationBooking(string contactName, string contactId);

        /// <summary>讀分類非空的奉獻收費頁；整頁驗穩定ID後才回傳，不改合法同名記錄。</summary>
        /// <param name="contactName">可含特殊字元的lookup顯示名稱。</param>
        /// <param name="contactId">非空聯絡人資料庫GUID。</param>
        /// <returns>呼叫者擁有的原SDK頁，不在實作保存。</returns>
        EntityCollection RetrieveDedicationFee(string contactName, string contactId);

        /// <summary>
        /// 讀含端點日期內的奉獻頁，套用原付款狀態白名單；日期採西曆但不作時區偏移。
        /// </summary>
        /// <param name="contactName">lookup顯示名稱，不作權限。</param>
        /// <param name="contactId">非空聯絡人資料庫GUID。</param>
        /// <param name="startDate">呼叫者已決定的業務起日。</param>
        /// <param name="endDate">不早於起日的業務迄日。</param>
        /// <returns>完整驗證、保留原分頁資訊的一頁結果。</returns>
        EntityCollection RetrieveDedicationFeeByDateRange(string contactName, string contactId, DateTime startDate, DateTime endDate);

        /// <summary>依課程紀錄ID及pay_date非空查已繳頁，不推導或改寫付款狀態。</summary>
        /// <param name="storLessonName">只為來源相容保留，實作不使用或保存。</param>
        /// <param name="storLessonId">伺服器已授權的非空課程紀錄GUID。</param>
        /// <returns>含繳費日期及實繳額的SDK頁，合法同名不同ID均保留。</returns>
        EntityCollection GetPaidFee(string storLessonName, string storLessonId);

        /// <summary>依課程紀錄ID及pay_date為null查未繳頁，保留舊來源欄位及排序。</summary>
        /// <param name="storLessonName">只為相容保留，不當識別或授權。</param>
        /// <param name="storLessonId">伺服器已授權的非空課程紀錄GUID。</param>
        /// <returns>含應繳額的SDK頁，重複或欠缺穩定ID時整頁拒絕。</returns>
        EntityCollection GetNotYetPaidFee(string storLessonName, string storLessonId);

        /// <summary>以卡或ATM欄位OR比對訂單文字，安全編碼所有XML特殊字元且保留原欄位／排序。</summary>
        /// <param name="orderNumber">非空訂單文字；所屬權限仍由host先驗證。</param>
        /// <returns>完整ID驗證的SDK頁，不以文字合併不同資料庫記錄。</returns>
        EntityCollection RetrieveFeeByFetchXmlOrderNumber(string orderNumber);
    }
}

```

## ToolUtility.Core/LessonsOperations/ILessonsService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.LessonsOperations
{
    /// <summary>
    /// 已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。
    /// 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 host 共用例外通知邊界。
    /// 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
    /// </summary>
    public interface ILessonsService
    {
        /// <summary>依課程起訖日期與原報名 join 查課程；保留 distinct、原投影及完整正式 ID，不以顯示名稱去重。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveEnrolledLessons(DateTime startDate, DateTime endDate, string contactName, string contactId);

        /// <summary>依原課程起訖日期與分類排序查課程；拒絕反向區間，不依程序文化改變曆法，不自行轉換時區。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveLessonsByMonth(DateTime startDate, DateTime endDate);

        /// <summary>保留原狀態排除、5筆上限及聯絡人 outer join；兩 lookup 均驗非空 GUID，全部列驗證後才回完整頁。</summary>
        /// <param name="lessonName">僅作 uiname 的課程顯示文字。</param>
        /// <param name="lessonId">server 已授權的非空課程資料庫 GUID。</param>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveStorLessons(string lessonName, string lessonId, string contactName, string contactId);
    }
}

```

## ToolUtility.Core/LessonsOperations/LessonsService.cs
```
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

```

## ToolUtility.Core/MeetingStatisticsOperations/IMeetingStatisticsService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.MeetingStatisticsOperations
{
    /// <summary>
    /// 已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。
    /// 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 host 共用例外通知邊界。
    /// 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
    /// </summary>
    public interface IMeetingStatisticsService
    {
        /// <summary>依已決定語義的主日日期查原欄位與排序；固定西曆，不自行換算時區，正式列 ID 不能為空或重複。</summary>
        /// <param name="sundayDate">caller 已決定語義的主日日期，不在此轉時區。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveBySunday(DateTime sundayDate);

        /// <summary>保留原週報／聯絡人條件及相容 uitype 提示值；lookup GUID 先驗證，不用顯示文字決定權限。</summary>
        /// <param name="weeklyReportName">只供 uiname 的週報顯示文字。</param>
        /// <param name="weeklyReportId">已由 host 授權的非空週報 GUID。</param>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByWeeklyReportAndContact(string weeklyReportName, string weeklyReportId, string contactName, string contactId);

        /// <summary>依聯絡人與主日日期條件查原出席頁；安全編碼且完整驗證正式 ID，同名不同 ID 資料全保留。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <param name="sundayDate">caller 已決定語義的主日日期，不在此轉時區。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveBySundayDateAndContact(string contactName, string contactId, DateTime sundayDate);

        /// <summary>保留替代入口的參數順序與週報 uitype；安全組裝條件、整頁驗證，不另造快取或變更原查詢語義。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <param name="weeklyReportName">只供 uiname 的週報顯示文字。</param>
        /// <param name="weeklyReportId">已由 host 授權的非空週報 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByWeeklyReportAndContactAlt(string contactName, string contactId, string weeklyReportName, string weeklyReportId);

        /// <summary>保留聯絡人與關懷到期日 not-null 條件；不能省略 lookup 而擴張範圍，故障不能變成空集合。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveWithExpiredDateByContact(string contactName, string contactId);

        /// <summary>依聯絡人、小組與主日三項原條件查詢；兩個正式 lookup GUID 均先驗證，不以姓名隔離或去重。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <param name="smallGroupName">只供 uiname 的小組顯示文字。</param>
        /// <param name="smallGroupId">已由 host 授權的非空小組 GUID。</param>
        /// <param name="sundayDate">caller 已決定語義的主日日期，不在此轉時區。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByContactSmallGroupAndSundayDate(string contactName, string contactId, string smallGroupName, string smallGroupId, DateTime sundayDate);
    }
}

```

## ToolUtility.Core/MeetingStatisticsOperations/MeetingStatisticsService.cs
```
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

```

## ToolUtility.Core/ToolUtility.Core.csproj
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net462;net10.0</TargetFrameworks>
    <RootNamespace>ToolUtilityNameSpace</RootNamespace>
    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net462'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" PrivateAssets="all" />
    <!-- Framework SDK 傳遞引用 6.0.8，直接使用中央安全修補版；net10 則使用內建 JSON。 -->
    <PackageReference Include="System.Text.Json" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <!-- 僅取得 SDK 型別；純資料核心不建立 ServiceClient 或擁有連線。 -->
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" PrivateAssets="all" />
    <!-- 舊 SDK 帶入的傳遞版本有已知弱點；使用中央表的安全版本，維持完整 audit。 -->
    <PackageReference Include="System.Security.Cryptography.Pkcs" PrivateAssets="all" />
  </ItemGroup>
</Project>

```

## ToolUtility.Tests/ToolUtility.Tests.csproj
```
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net48;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
  </PropertyGroup>

  <ItemGroup>
    <!-- 測試框架 -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    
    <!-- Mock 框架 -->
    <PackageReference Include="Moq" />
    
    <!-- 斷言庫 -->
    <PackageReference Include="FluentAssertions" />
    
    <!-- 程式碼覆蓋率 -->
    <PackageReference Include="coverlet.collector">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.msbuild">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <!-- 專案參考 -->
    <ProjectReference Include="..\ToolUtility\ToolUtility.csproj" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ToolUtility.Core\ToolUtility.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- CRM SDK 套件（用於測試） -->
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" PrivateAssets="all" />
    <PackageReference Include="System.Text.Json" PrivateAssets="all" />
    <!-- 既有 host 測試完整保留在 net10；Framework 宿主只編譯純 Core 契約，不引用 net10 host。 -->
    <Compile Remove="**\*.cs" />
    <Compile Include="FeeOperations\*.cs" />
    <Compile Include="EntityOperations\EntityQueryCharacterizationTests.cs" />
    <Compile Include="EntityOperations\EntityQueryContractTests.cs" />
    <Compile Include="DataOperations\*.cs" />
    <Compile Include="TestHelpers\GoldenQueryAssertions*.cs" />
    <Compile Include="CoreTargetFrameworkTests.cs" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" PrivateAssets="all" />
    <!-- 測試直接消費 SDK，需同樣以中央安全版本覆蓋其舊傳遞相依。 -->
    <PackageReference Include="System.Security.Cryptography.Pkcs" PrivateAssets="all" />
    <PackageReference Include="System.Security.Permissions" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <!-- 黃金查詢是合成資料，內嵌後兩個測試宿主均不依賴執行目錄或網路。 -->
    <EmbeddedResource Include="Golden\ToolUtility\*.xml"
                      LogicalName="ToolUtility.Tests.Golden.ToolUtility.%(Filename)%(Extension)" />
  </ItemGroup>

</Project>

```

## ToolUtility.Tests/CoreTargetFrameworkTests.cs
```
using System.Reflection;
using System.Runtime.Versioning;
using ToolUtilityNameSpace.FeeOperations;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using Xunit;

namespace ToolUtility.Tests
{
    /// <summary>
    /// 反射實際載入產品組件，而非只讀csproj文字；Framework測試宿主可以net48，
    /// 但產品輸出必須仍是核定net462，不能誤載net48／host組件而假稱雙目標通過。
    /// </summary>
    public sealed class CoreTargetFrameworkTests
    {
        /// <summary>
        /// 比對FeeService所屬assembly的目標中繼資料；錯用任一產品目標即決定性失敗，
        /// 不接觸D365、不配置連線或其他需處置資源。
        /// </summary>
        [Fact]
        public void LoadedCore_UsesApprovedProductTarget()
        {
            var assembly = typeof(FeeService).Assembly;
            Assert.Same(assembly, typeof(EntityQueryService).Assembly);
            Assert.Same(assembly, typeof(AppointmentService).Assembly);
            Assert.Same(assembly, typeof(LessonsService).Assembly);
            Assert.Same(assembly, typeof(MeetingStatisticsService).Assembly);
            Assert.Equal("ToolUtility.Core", assembly.GetName().Name);
            var target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(target);
#if NETFRAMEWORK
            Assert.Equal(".NETFramework,Version=v4.6.2", target!.FrameworkName);
#else
            Assert.Equal(".NETCoreApp,Version=v10.0", target!.FrameworkName);
#endif
        }
    }
}

```

## ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ServiceCollectionExtensions
// 主要成員：AddToolUtility
// 引用命名空間：Microsoft.Extensions.DependencyInjection、System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace ToolUtilityNameSpace.DependencyInjection
{
    /// <summary>
    /// 產品級 ToolUtility 組合根；產品與組態只能由受信任的 host 提供，不能來自 request。
    /// 一個容器只屬於一個產品，manager／pool 是容器擁有的 singleton，資料操作維持 scoped。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 註冊 ToolUtility 服務到 DI 容器
        /// ToolUtilityClass 與其 Provider 都使用 Scoped 生命週期；每個 request
        /// 取得自己的 Dataverse 連線租約，request 結束時由 DI 確定性釋放。
        /// </summary>
        /// <param name="services">服務集合</param>
        /// <returns>服務集合</returns>
        [Obsolete("請改用 AddToolUtility(configuration, product) 明確指定產品；此相容多載預定於階段 6 移除。")]
        public static IServiceCollection AddToolUtility(this IServiceCollection services)
        {
            // 舊 API 沒有組態參數，延至解析時才從原容器取得組態；不可為取得設定另建 ServiceProvider。
            return RegisterServices(services, null, "ChurchReport");
        }

        /// <summary>
        /// 以明確的產品與組態註冊資料服務，讓實際 DataverseConnectionKey 包含正確產品。
        /// 組態只活至 host 容器結束，不保存 HttpContext、Session 或個別使用者值。
        /// 同一容器的衝突產品／組態會在任何新增註冊前拒絕，避免第一份 singleton 靜默勝出。
        /// </summary>
        /// <param name="services">由 host 擁有且尚在建構中的服務集合；非並行註冊 API。</param>
        /// <param name="configuration">此產品的 host 組態；讀取必要連線設定時仍須通過 manager 驗證。</param>
        /// <param name="product">伺服器組合根選定的非空產品識別，不接受呼叫端路由作為權限。</param>
        /// <returns>同一服務集合；相同組態實例與產品重複註冊為冪等。</returns>
        public static IServiceCollection AddToolUtility(
            this IServiceCollection services, IConfiguration configuration, string product)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            return RegisterServices(services, configuration, product);
        }

        /// <summary>
        /// 共用新舊 API 的唯一註冊流程，不建立隱藏容器或捕獲 scoped service。
        /// null 組態只供無參數相容入口，所有需組態的 factory 透過同一解析函式取得來源。
        /// </summary>
        private static IServiceCollection RegisterServices(
            IServiceCollection services, IConfiguration configuration, string product)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (string.IsNullOrWhiteSpace(product))
                throw new ArgumentException("A non-empty product identifier is required.", nameof(product));
            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType != typeof(ProductRegistration)) continue;
                var previous = (ProductRegistration)descriptor.ImplementationInstance;
                if (!string.Equals(previous.Product, product, StringComparison.Ordinal)
                    || !ReferenceEquals(previous.Configuration, configuration))
                    throw new InvalidOperationException("ToolUtility is already registered for a different product or configuration.");
                return services;
            }

            services.AddSingleton(new ProductRegistration(product, configuration));
            if (configuration != null) services.TryAddSingleton<IConfiguration>(configuration);

            // 此委派只捕獲 host 組態，不保存 provider；每個 factory 仍在原容器依原生命週期執行。
            IConfiguration ResolveConfiguration(IServiceProvider provider) =>
                configuration ?? provider.GetRequiredService<IConfiguration>();

            // Manager 與 pool 都是程序級資源擁有者；它們只能依賴 Singleton，
            // 絕不捕獲 request-scoped service。TryAdd 讓測試或其他產品可先提供
            // 自己的假連線服務，而不會意外建立第二個組合根實例。
            services.TryAddSingleton<ICrmConnectionService, CrmConnectionService>();
            // 未由產品組合根明確 opt-in 時，統一診斷一律 fail closed。這個 fallback
            // 不讀取 appsettings 的 Enabled，避免共用 ToolUtility 被其他 Host 誤開檔案追蹤。
            services.TryAddSingleton<DiagnosticTraceOptions>(_ =>
                DiagnosticTraceOptions.CreateDisabled(Environment.CurrentDirectory));
            services.TryAddSingleton<DataverseTraceOptions>(sp =>
            {
                var options = DataverseTraceOptions.FromDiagnosticOptions(
                    sp.GetRequiredService<DiagnosticTraceOptions>());
                options.Validate();
                return options;
            });
            // Trace 是唯一擁有背景佇列、檔案與程序內 HMAC salt 的 singleton；它不依賴
            // scoped service，也不保存 HttpContext，request 關聯只在 middleware 的 AsyncLocal 範圍內存在。
            services.TryAddSingleton<DataverseTrace>();
            services.TryAddSingleton<DataversePoolOptions>(sp =>
            {
                var options = new DataversePoolOptions();
                ResolveConfiguration(sp)
                    .GetSection("Dataverse:Pool")
                    .Bind(options);
                options.Validate();
                return options;
            });
            services.TryAddSingleton<DataverseConnectionManager>(sp =>
            {
                var effectiveConfiguration = ResolveConfiguration(sp);
                return new DataverseConnectionManager(
                    sp.GetRequiredService<ICrmConnectionService>(),
                    effectiveConfiguration,
                    product,
                    ResolveEnvironmentName(effectiveConfiguration),
                    sp.GetRequiredService<DataversePoolOptions>());
            });
            services.TryAddSingleton<IDataverseConnectionManager>(sp =>
                sp.GetRequiredService<DataverseConnectionManager>());
            services.TryAddSingleton<IBoundedClientPool>(sp =>
                sp.GetRequiredService<DataverseConnectionManager>().Pool);
            services.TryAddSingleton<IToolUtilityTracer, NullToolUtilityTracer>();
            services.TryAddSingleton<ICrmConnectionPool>(sp =>
                new ConnectionPoolStatsAdapter(sp.GetRequiredService<IDataverseConnectionManager>()));
            services.TryAddScoped<IDataverseGateway, DataverseGateway>();
            services.TryAddScoped<IOrganizationService, GatewayOrganizationService>();

            services.AddScoped<ToolUtilityClass>(sp => new ToolUtilityClass(
                sp.GetRequiredService<IOrganizationService>(),
                sp.GetRequiredService<IToolUtilityTracer>(),
                ResolveConfiguration(sp)));
            services.AddScoped<IToolUtilityProvider, ToolUtilityProvider>();
            return services;
        }

        /// <summary>
        /// 註冊期間的有限不可變標記，只含 host 擁有的產品與組態參照，不是使用者狀態快取。
        /// 與 IServiceCollection／容器同生命週期，不擁有組態或任何 IDisposable。
        /// </summary>
        private sealed class ProductRegistration
        {
            /// <summary>保存首次註冊的受信任邊界，供後續註冊比對，不讀取或複製機密內容。</summary>
            internal ProductRegistration(string product, IConfiguration configuration)
            {
                Product = product;
                Configuration = configuration;
            }

            /// <summary>唯一的 host 產品識別，不由 request 改寫。</summary>
            internal string Product { get; }

            /// <summary>原 host 組態；null 表示延用既有 DI 解析，不代表缺少連線驗證。</summary>
            internal IConfiguration Configuration { get; }
        }

        /// <summary>
        /// 解析主機環境名稱而不讓 ToolUtility 組件直接依賴 ASP.NET Core。
        /// Web Host 會把環境名稱放入組態；測試與非 Web 組合根則使用明確的
        /// DOTNET/ASPNETCORE 環境變數，最後才回到 Production。這個值只進入
        /// Pool Key，不承載使用者身分，也不會使不同環境共用 client。
        /// </summary>
        private static string ResolveEnvironmentName(IConfiguration configuration)
        {
            return configuration["ASPNETCORE_ENVIRONMENT"]
                ?? configuration["DOTNET_ENVIRONMENT"]
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? "Production";
        }
    }
}

```

## ToolUtility.Tests/DependencyInjection/ProductRegistrationTests.cs
```
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;
using Xunit;

namespace ToolUtility.Tests.DependencyInjection
{
    /// <summary>
    /// 驗證產品組合根真正傳入連線池的完整隔離鍵，而非只檢查註冊文字。
    /// 外部連線由合成 SDK 替身取代；容器是唯一 pool owner，每案結束必確定性 Dispose。
    /// </summary>
    public sealed class ProductRegistrationTests
    {
        /// <summary>
        /// 兩個產品使用相同合成組織／身分時，實際租約仍進入不同 Product 鍵。
        /// 若 factory 仍寫死 ChurchReport，本案必失敗；釋放後租約／等待者歸零，容器再關閉 client。
        /// </summary>
        [Fact]
        public void AddToolUtility_ProductParameter_PartitionsConnectionKey()
        {
            var aClient = Client();
            var bClient = Client();
            var aServices = Services(aClient);
            var bServices = Services(bClient);
            aServices.AddToolUtility(Configuration(), "ProductA");
            bServices.AddToolUtility(Configuration(), "ProductB");

            using (var a = aServices.BuildServiceProvider())
            using (var b = bServices.BuildServiceProvider())
            {
                var aManager = a.GetRequiredService<IDataverseConnectionManager>();
                var bManager = b.GetRequiredService<IDataverseConnectionManager>();
                using (var first = aManager.Acquire())
                using (var second = bManager.Acquire())
                {
                    Assert.NotSame(first.Service, second.Service);
                    var aKey = ActualKey(aManager);
                    var bKey = ActualKey(bManager);
                    Assert.Equal("ProductA", aKey.Product);
                    Assert.Equal("ProductB", bKey.Product);
                    Assert.NotEqual(aKey, bKey);
                    Assert.Equal(aKey.OrganizationUrl, bKey.OrganizationUrl);
                    Assert.Equal(aKey.EffectiveIdentity, bKey.EffectiveIdentity);
                }
                Assert.Equal(0, aManager.GetMetrics().Leased);
                Assert.Equal(0, bManager.GetMetrics().Waiting);
                Assert.Equal(1, aManager.GetMetrics().TotalReleases);
                Assert.Equal(1, bManager.GetMetrics().TotalReleases);
                aClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
                bClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
            }
            aClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Once);
            bClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Once);
        }

        /// <summary>
        /// 原本無參數的公開 overload 必須保留 ChurchReport 鍵與 DI 組態讀取，
        /// 不能在註冊期間另建容器或連線，亦不能讓未解析服務逃過容器關閉。
        /// </summary>
        [Fact]
        public void AddToolUtility_LegacyOverload_UsesChurchReport()
        {
            var client = Client();
            var services = Services(client);
            services.AddSingleton<IConfiguration>(Configuration());
            services.AddToolUtility();
            using (var provider = services.BuildServiceProvider())
            {
                var manager = provider.GetRequiredService<IDataverseConnectionManager>();
                using (manager.Acquire())
                    Assert.Equal("ChurchReport", ActualKey(manager).Product);
                Assert.Equal(0, manager.GetMetrics().Leased);
            }
            client.As<IDisposable>().Verify(service => service.Dispose(), Times.Once);
        }

        /// <summary>
        /// 同一容器先後註冊不同產品必須拒絕，不能讓 TryAddSingleton 靜默沿用第一個產品。
        /// 失敗前後服務描述數相等，避免部分註冊污染現有組合根。
        /// </summary>
        [Fact]
        public void AddToolUtility_DifferentProductsInOneContainer_RejectsWithoutMutation()
        {
            var services = Services(Client());
            var configuration = Configuration();
            services.AddToolUtility(configuration, "ProductA");
            var count = services.Count;

            Assert.Throws<InvalidOperationException>(() => services.AddToolUtility(configuration, "ProductB"));
            Assert.Equal(count, services.Count);
        }

        /// <summary>
        /// 完全相同組態與產品的重複註冊必須冪等，不能建立第二份 singleton owner 或 scoped provider。
        /// </summary>
        [Fact]
        public void AddToolUtility_SameRegistration_IsIdempotent()
        {
            var services = Services(Client());
            var configuration = Configuration();
            services.AddToolUtility(configuration, "ProductA");
            var count = services.Count;

            services.AddToolUtility(configuration, "ProductA");

            Assert.Equal(count, services.Count);
        }

        /// <summary>
        /// 產品名稱是受信任組合根的必要邊界；空白不得回退另一產品或改動容器。
        /// </summary>
        /// <param name="product">合成的無效產品值。</param>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void AddToolUtility_InvalidProduct_RejectsBeforeRegistration(string? product)
        {
            var services = new ServiceCollection();
            Assert.Throws<ArgumentException>(() => services.AddToolUtility(Configuration(), product!));
            Assert.Empty(services);
        }

        /// <summary>
        /// 同產品使用不同組態物件再註冊也須明確拒絕，避免 manager 與 facade 各自讀不同的身分／組織。
        /// </summary>
        [Fact]
        public void AddToolUtility_ConflictingConfiguration_RejectsWithoutMutation()
        {
            var services = Services(Client());
            services.AddToolUtility(Configuration(), "ProductA");
            var count = services.Count;
            Assert.Throws<InvalidOperationException>(() => services.AddToolUtility(Configuration(), "ProductA"));
            Assert.Equal(count, services.Count);
        }

        /// <summary>合成組態不含實際連線或機密；容量設一避免產生無用預熱 client。</summary>
        private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["CrmConnection:ServerUrl"] = "https://synthetic.invalid/organization",
                ["CrmConnection:Username"] = "synthetic-service",
                ["Dataverse:Pool:MinSize"] = "1",
                ["Dataverse:Pool:MaxN"] = "1",
                ["DOTNET_ENVIRONMENT"] = "P1OfflineTest"
            }).Build();

        /// <summary>建立只有確定性 Dispose 的合成 SDK；不啟動外部服務、不保存使用者資料。</summary>
        private static Mock<IOrganizationService> Client()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Loose);
            client.As<IDisposable>();
            return client;
        }

        /// <summary>只替換真正外部 I/O factory，保留 manager／pool 的完整產品鍵與租約流程。</summary>
        private static ServiceCollection Services(Mock<IOrganizationService> client)
        {
            var factory = new Mock<ICrmConnectionService>(MockBehavior.Strict);
            factory.Setup(service => service.CreateOnPremiseClient(
                "https://synthetic.invalid/organization", "synthetic-service", string.Empty)).Returns(client.Object);
            var services = new ServiceCollection();
            services.AddSingleton(factory.Object);
            return services;
        }

        /// <summary>
        /// 白箱只讀已建立子池的真正鍵，避免為測試擴大會揭露組織／服務身分的公開診斷 API。
        /// 讀取只發生於本案已取得租約、無並行新增的固定時點，不保留任何 pool 參照於案外。
        /// </summary>
        private static DataverseConnectionKey ActualKey(IDataverseConnectionManager manager)
        {
            var pool = Assert.IsType<DataverseConnectionManager>(manager).Pool;
            var field = typeof(BoundedClientPool).GetField("_subPools", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var dictionary = Assert.IsAssignableFrom<IDictionary>(field!.GetValue(pool));
            return Assert.IsType<DataverseConnectionKey>(Assert.Single(dictionary.Keys.Cast<object>()));
        }
    }
}

```

## ToolUtility.Tests/EntityOperations/EntityQueryCharacterizationTests.cs
```
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.EntityOperations
{
    /// <summary>
    /// 在 EntityOperations 搬移前鎖定三個代表方法的完整 SDK 請求與結果語義。
    /// 所有 ID、名稱與資料為合成值；只替換外部 SDK，不建立網路、快取或使用者狀態。
    /// </summary>
    public sealed class EntityQueryCharacterizationTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-4111-8111-111111111111");

        /// <summary>
        /// 單筆查詢必須保留實體名稱、資料庫 ID 及 AllColumns；不得意外走 host cache。
        /// 回傳同一操作擁有的 Entity，原有欄位不遺失。
        /// </summary>
        [Fact]
        public void RetrieveEntity_PreservesIdColumnsAndResult()
        {
            var expected = new Entity("contact", RecordId) { ["fullname"] = "Synthetic Contact" };
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId,
                It.Is<ColumnSet>(columns => columns.AllColumns))).Returns(expected);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(expected, service.RetrieveEntity("contact", RecordId));
            client.VerifyAll();
        }

        /// <summary>
        /// 欄位查詢保留 TopCount=1、完整欄位、業務欄位與啟用條件；特殊文字必為型別化值，
        /// 不能因搬移變成 XML 插入或把顯示名稱當唯一識別碼。
        /// </summary>
        [Fact]
        public void RetrieveEntityByField_PreservesTypedConditions()
        {
            const string value = "Synthetic <&\"' value";
            var expected = new Entity("contact", RecordId);
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryByAttribute>(query);
                    Assert.Equal("contact", actual.EntityName);
                    Assert.Equal(1, actual.TopCount);
                    Assert.True(actual.ColumnSet.AllColumns);
                    Assert.Equal(new[] { "fullname", "statecode" }, actual.Attributes);
                    Assert.Equal(new object[] { value, 0 }, actual.Values);
                }).Returns(new EntityCollection(new[] { expected }));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(expected, service.RetrieveEntityByField("contact", "fullname", value));
            client.VerifyAll();
        }

        /// <summary>
        /// 工作查詢保留 100 筆上限、原欄位投影、createdon倒序、主旨與statecode條件。
        /// 完整 XML 與獨立黃金檔比較；同名不同資料庫 ID 兩筆工作均保留。
        /// </summary>
        [Fact]
        public void RetrieveTaskBySubject_MatchesGoldenAndPreservesSameNameRows()
        {
            var expected = new EntityCollection(new[]
            {
                new Entity("task", RecordId) { ["subject"] = "Synthetic Task" },
                new Entity("task", new Guid("22222222-2222-4222-8222-222222222222")) { ["subject"] = "Synthetic Task" }
            });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request => GoldenQueryAssertions.Equal("entity-task.xml",
                    Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query))
                .Returns(() =>
                {
                    var response = new RetrieveMultipleResponse();
                    response.Results["EntityCollection"] = expected;
                    return response;
                });
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                var actual = service.RetrieveTaskBySubject("Synthetic Task");
                Assert.Same(expected, actual);
                Assert.Equal(2, actual.Entities.Count);
                Assert.NotEqual(actual.Entities[0].Id, actual.Entities[1].Id);
            }
        }
    }
}

```

## ToolUtility.Tests/EntityOperations/EntityQueryContractTests.cs
```
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.EntityOperations
{
    /// <summary>
    /// 保護不經快取的實體讀取、整頁穩定 ID 與安全 XML 邊界。
    /// 每案只借入合成 SDK，故障原樣傳遞給 host；沒有真正 D365 或 LINE 操作。
    /// </summary>
    public sealed class EntityQueryContractTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-4111-8111-111111111111");

        /// <summary>
        /// 相同 ID 連查兩次必須兩次進 SDK，第二次讀到更新值而非第一個 caller 修改後的物件。
        /// 所有欄位沿用舊來源 AllColumns，不把此方法變成 host cache 的別名。
        /// </summary>
        [Fact]
        public void RetrieveEntityNoCache_RepeatedId_ReadsFreshSdkResult()
        {
            var generation = 0;
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId,
                It.Is<ColumnSet>(columns => columns.AllColumns)))
                .Returns(() => new Entity("contact", RecordId) { ["fullname"] = ++generation });
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                var first = service.RetrieveEntityNoCache("contact", RecordId);
                first["fullname"] = "Synthetic caller mutation";
                var second = service.RetrieveEntityNoCache("contact", RecordId);
                Assert.NotSame(first, second);
                Assert.Equal(2, second["fullname"]);
            }
            client.Verify(service => service.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()), Times.Exactly(2));
        }

        /// <summary>
        /// 不經快取入口的 SDK 故障必須維持原 Exception 實例，不回空、不重試或輸出敏感本文。
        /// </summary>
        [Fact]
        public void RetrieveEntityNoCache_SdkFault_PropagatesOriginalException()
        {
            var failure = new InvalidOperationException("Synthetic private SDK fault");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId, It.IsAny<ColumnSet>())).Throws(failure);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(
                    () => service.RetrieveEntityNoCache("contact", RecordId)));
        }

        /// <summary>
        /// XML特殊字元作為實體 logical name 並非合法資料庫識別，必須在 I/O 前拒絕；
        /// 此方法用型別化 Retrieve 而不是把名稱拼成 FetchXML，不能靠 escape 放行任意結構。
        /// </summary>
        /// <param name="entityName">非法 logical name。</param>
        [Theory]
        [InlineData("<&\"'")]
        [InlineData("")]
        [InlineData(" ")]
        public void RetrieveEntityNoCache_InvalidEntityName_RejectsBeforeIo(string entityName)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<ArgumentException>(() => service.RetrieveEntityNoCache(entityName, RecordId));
            client.VerifyNoOtherCalls();
        }

        /// <summary>空 GUID 不能變成廣泛查詢或 draft key，拒絕時 SDK 不得被呼叫。</summary>
        [Fact]
        public void RetrieveEntityNoCache_EmptyId_RejectsBeforeIo()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<ArgumentException>(() => service.RetrieveEntityNoCache("contact", Guid.Empty));
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// SDK 回錯 ID 或實體是完整性故障，不可把其他資料回傳給原查詢者。
        /// </summary>
        /// <param name="wrongEntity">注入錯誤實體或不同非空 ID。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RetrieveEntityNoCache_IdentityMismatch_RejectsPublication(bool wrongEntity)
        {
            var candidate = new Entity(wrongEntity ? "account" : "contact",
                wrongEntity ? RecordId : new Guid("22222222-2222-4222-8222-222222222222"));
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId, It.IsAny<ColumnSet>())).Returns(candidate);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<InvalidOperationException>(() => service.RetrieveEntityNoCache("contact", RecordId));
        }

        /// <summary>
        /// 工作主旨文字含全部 XML 特殊字元仍只能成為一個屬性值；新增節點／條件或改變 statecode
        /// 都屬可重現注入。本案檢查解碼原值與精確條件數，不只驗證查詢字串包含輸入。
        /// </summary>
        [Fact]
        public void RetrieveTaskBySubject_XmlSpecialCharacters_CannotInjectConditions()
        {
            const string subject = "Synthetic <&\"' value";
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request =>
                {
                    var xml = Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query;
                    var conditions = XDocument.Parse(xml).Descendants("condition").ToArray();
                    Assert.Equal(2, conditions.Length);
                    Assert.Equal(subject, (string?)conditions[0].Attribute("value"));
                    Assert.Equal("0", (string?)conditions[1].Attribute("value"));
                }).Returns(Response(new EntityCollection()));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Empty(service.RetrieveTaskBySubject(subject).Entities);
        }

        /// <summary>
        /// 通用查詢與 Execute 兩條路徑都要驗整頁 ID；注入相同非空 ID 後必須整頁拒絕，
        /// 且原候選不被靜默刪減，讓上層保有可診斷的原始失敗。
        /// </summary>
        /// <param name="execute">使用 Execute 或 RetrieveMultiple。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MultiQuery_DuplicateId_RejectsEntireCandidate(bool execute)
        {
            var candidate = new EntityCollection(new[] { new Entity("contact", RecordId), new Entity("contact", RecordId) });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(candidate);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Returns(Response(candidate));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<InvalidOperationException>(() => Run(service, execute));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 空列 ID 是 SDK 完整性故障；合法不同 ID 的同名列在相同入口均需保留兩筆。
        /// </summary>
        /// <param name="missingId">是否注入空 GUID。</param>
        /// <param name="execute">SDK 執行路徑。</param>
        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void MultiQuery_StableIds_AreRequiredAndSameNamesArePreserved(bool missingId, bool execute)
        {
            var candidate = new EntityCollection(new[]
            {
                new Entity("contact", RecordId) { ["fullname"] = "Synthetic Same Name" },
                new Entity("contact", missingId ? Guid.Empty : new Guid("22222222-2222-4222-8222-222222222222"))
                    { ["fullname"] = "Synthetic Same Name" }
            });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(candidate);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Returns(Response(candidate));
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                if (missingId) Assert.Throws<InvalidOperationException>(() => Run(service, execute));
                else Assert.Equal(2, Run(service, execute).Entities.Count);
            }
        }

        /// <summary>
        /// FetchXML 兩個通用入口不能略過根實體驗證；注入不同實體、重複或空 ID 時，
        /// 即使 SDK 已回完整候選仍須整頁拒絕，而且不能靜默修改原資料來掩蓋衝突。
        /// </summary>
        /// <param name="execute">經 Execute 或 RetrieveMultiple 送出同一 FetchExpression。</param>
        /// <param name="fault">合成的跨實體、重複或空識別故障。</param>
        [Theory]
        [InlineData(false, "entity")]
        [InlineData(true, "entity")]
        [InlineData(false, "duplicate")]
        [InlineData(true, "duplicate")]
        [InlineData(false, "empty")]
        [InlineData(true, "empty")]
        public void FetchQuery_InvalidRowIdentity_RejectsEntireCandidate(bool execute, string fault)
        {
            var candidate = new EntityCollection(new[]
            {
                new Entity("contact", RecordId),
                new Entity(fault == "entity" ? "account" : "contact",
                    fault == "empty" ? Guid.Empty : fault == "duplicate" ? RecordId
                        : new Guid("22222222-2222-4222-8222-222222222222"))
            });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(candidate);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Returns(Response(candidate));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<InvalidOperationException>(() => RunFetch(service, execute,
                    new FetchExpression("<fetch><entity name='contact'/></fetch>")));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 只讀辨識 FetchXML 根實體不能改寫原查詢；包含 link-entity 仍以 contact 為主資料列。
        /// 合法同名不同 ID、原 SDK 分頁旗標及 cookie 必須完整保留，不引入快取或額外 I/O。
        /// </summary>
        /// <param name="execute">原 SDK 執行入口。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FetchQuery_ValidRows_PreservesQueryAndPaging(bool execute)
        {
            var query = new FetchExpression("<?xml version='1.0'?><fetch><entity name='contact'>" +
                "<attribute name='contactid'/><link-entity name='account' from='accountid' to='parentcustomerid'/>" +
                "</entity></fetch>");
            var candidate = new EntityCollection(new[]
            {
                new Entity("contact", RecordId) { ["fullname"] = "Synthetic Same Name" },
                new Entity("contact", new Guid("22222222-2222-4222-8222-222222222222"))
                    { ["fullname"] = "Synthetic Same Name" }
            }) { MoreRecords = true, PagingCookie = "Synthetic cookie" };
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(query)).Returns(candidate);
            client.Setup(service => service.Execute(It.Is<RetrieveMultipleRequest>(request =>
                ReferenceEquals(request.Query, query)))).Returns(Response(candidate));
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                var result = RunFetch(service, execute, query);
                Assert.Same(candidate, result);
                Assert.Equal(2, result.Entities.Count);
                Assert.True(result.MoreRecords);
                Assert.Equal("Synthetic cookie", result.PagingCookie);
            }
        }

        /// <summary>
        /// 無根實體、雙根實體、非法名稱、壞 XML 與 DTD／外部實體都在 SDK I/O 前拒絕。
        /// DTD 使用不存在的合成 URI，產品必須禁用解析而不是嘗試存取檔案或網路；
        /// 預期輸入拒絕只回固定訊息，不把 XML 本文放進例外訊息。
        /// </summary>
        /// <param name="xml">不合法或帶外部實體的合成文件。</param>
        [Theory]
        [InlineData("")]
        [InlineData("<fetch/>")]
        [InlineData("<other><entity name='contact'/></other>")]
        [InlineData("<fetch><entity/></fetch>")]
        [InlineData("<fetch><entity name='bad&amp;name'/></fetch>")]
        [InlineData("<fetch><entity name='contact'/><entity name='account'/></fetch>")]
        [InlineData("<fetch xmlns='urn:synthetic'><entity name='contact'/></fetch>")]
        [InlineData("<fetch><entity name='contact'></fetch>")]
        [InlineData("<!DOCTYPE fetch [<!ENTITY synthetic SYSTEM 'file:///p1-must-not-read'>]><fetch><entity name='contact'>&synthetic;</entity></fetch>")]
        public void FetchQuery_InvalidXml_RejectsBeforeIo(string xml)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
                foreach (var execute in new[] { false, true })
                    Assert.Throws<ArgumentException>(() => RunFetch(service, execute, new FetchExpression(xml)));
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Dispose 只停止新操作並釋放服務保存的借用參照，不關閉呼叫端 SDK。
        /// 以可計數的 IDisposable SDK 故障注入，關閉後不允許再做任何資料讀取。
        /// </summary>
        [Fact]
        public void Dispose_StopsNewOperationsWithoutDisposingBorrowedClient()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = client.As<IDisposable>();
            var service = new EntityQueryService(new object(), client.Object);
            service.Dispose();
            service.Dispose();
            Assert.Throws<ObjectDisposedException>(() => service.RetrieveEntity("contact", RecordId));
            disposable.Verify(value => value.Dispose(), Times.Never);
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 兩個已授權範圍交錯 1000 次必須只收到自己的 SDK 結果；八路固定上限避免測試無界 fan-out。
        /// 全部操作完成後 active 歸零，服務 Dispose 不釋放借入 client，也不留下背景工作。
        /// </summary>
        [Fact]
        public void NoCache_ParallelScopes_DoNotShareResultsAndDrain()
        {
            var otherId = new Guid("22222222-2222-4222-8222-222222222222");
            var first = new Mock<IOrganizationService>(MockBehavior.Strict);
            var second = new Mock<IOrganizationService>(MockBehavior.Strict);
            first.As<IDisposable>();
            second.As<IDisposable>();
            first.Setup(client => client.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()))
                .Returns(() => new Entity("contact", RecordId) { ["fullname"] = "Synthetic A" });
            second.Setup(client => client.Retrieve("contact", otherId, It.IsAny<ColumnSet>()))
                .Returns(() => new Entity("contact", otherId) { ["fullname"] = "Synthetic B" });
            var active = 0;
            var completed = 0;
            using (var a = new EntityQueryService(new object(), first.Object))
            using (var b = new EntityQueryService(new object(), second.Object))
                Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        var isFirst = index % 2 == 0;
                        var row = (isFirst ? a : b).RetrieveEntityNoCache("contact", isFirst ? RecordId : otherId);
                        Assert.Equal(isFirst ? "Synthetic A" : "Synthetic B", row["fullname"]);
                        row["fullname"] = "Synthetic caller mutation";
                        Interlocked.Increment(ref completed);
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            Assert.Equal(0, active);
            Assert.Equal(1000, completed);
            first.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
            second.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
        }

        /// <summary>
        /// 依名稱查帳戶保留原 projection／TopCount／啟用條件；真正空頁才回空 GUID。
        /// 同名資料由既有單筆契約取首筆，而不是改寫或刪除候選中的其他合法 ID。
        /// </summary>
        /// <param name="empty">注入真正空頁或合法資料。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AccountNameQuery_PreservesProjectionAndEmptySemantics(bool empty)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryByAttribute>(query);
                    Assert.Equal("account", actual.EntityName);
                    Assert.Equal(1, actual.TopCount);
                    Assert.Equal(new[] { "accountid" }, actual.ColumnSet.Columns);
                    Assert.False(actual.ColumnSet.AllColumns);
                    Assert.Equal(new[] { "name", "statecode" }, actual.Attributes);
                    Assert.Equal(new object[] { "Synthetic Account", 0 }, actual.Values);
                }).Returns(empty ? new EntityCollection() : new EntityCollection(new[] { new Entity("account", RecordId) }));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Equal(empty ? Guid.Empty : RecordId, service.RetrieveAccountByName("Synthetic Account"));
        }

        /// <summary>
        /// QueryExpression 的既有空 PageInfo 會補第一頁 5000，上層分頁旗標與 cookie 原樣保留。
        /// 以真 SDK 請求物件進替身，比對完整頁選項而非只檢查程式碼文字。
        /// </summary>
        [Fact]
        public void QueryExpression_NullPageInfo_PreservesDefaultAndResponsePaging()
        {
            var expected = new EntityCollection { MoreRecords = true, PagingCookie = "Synthetic cookie" };
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryExpression>(query);
                    Assert.Equal(5000, actual.PageInfo.Count);
                    Assert.Equal(1, actual.PageInfo.PageNumber);
                }).Returns(expected);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(expected, service.RetrieveMultiple(new QueryExpression("contact") { PageInfo = null }));
        }

        /// <summary>
        /// 未初始化、null Query、null Request 與缺 Query 的 Request 都必須明確失敗，不能回空資料。
        /// 這些預期拒絕不發 LINE，借入 SDK 替身不應收到任何 I/O。
        /// </summary>
        [Fact]
        public void InvalidOperationInputs_RejectWithoutSdkIo()
        {
            using (var uninitialized = new EntityQueryService())
                Assert.Throws<InvalidOperationException>(() => uninitialized.RetrieveEntity("contact", RecordId));
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                Assert.Throws<ArgumentNullException>(() => service.RetrieveMultiple(null!));
                Assert.Throws<ArgumentNullException>(() => service.ExecuteRetrieveMultiple(null!));
                Assert.Throws<ArgumentException>(() => service.ExecuteRetrieveMultiple(new RetrieveMultipleRequest()));
                client.Setup(value => value.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection());
                Assert.Null(service.RetrieveEntityByField("contact", "fullname", "Synthetic absent contact"));
            }
            client.Verify(value => value.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            client.VerifyNoOtherCalls();
        }

        /// <summary>建立操作局部 SDK 回應，不保留於 static 或共用快取。</summary>
        private static RetrieveMultipleResponse Response(EntityCollection candidate)
        {
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = candidate;
            return response;
        }

        /// <summary>由測試固定路徑呼叫真實服務，不在替身重做產品驗證邏輯。</summary>
        private static EntityCollection Run(EntityQueryService service, bool execute) => execute
            ? service.ExecuteRetrieveMultiple(new RetrieveMultipleRequest { Query = new QueryByAttribute("contact") })
            : service.RetrieveMultiple(new QueryByAttribute("contact"));

        /// <summary>向兩條真實產品路徑傳入同一查詢；替身不負責解析或驗證 XML。</summary>
        private static EntityCollection RunFetch(EntityQueryService service, bool execute, FetchExpression query) => execute
            ? service.ExecuteRetrieveMultiple(new RetrieveMultipleRequest { Query = query })
            : service.RetrieveMultiple(query);
    }
}

```

## ToolUtility.Tests/DataOperations/QueryOperationsCharacterizationTests.cs
```
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 在搬移與安全純化之前鎖定三組 SDK-only 操作的全部查詢，而非以重寫結果反推 golden。
    /// Golden 由固定舊來源模板填入合成資料；每案比較真正送到 SDK 的完整 XML，保留排序與欄位。
    /// </summary>
    public sealed class QueryOperationsCharacterizationTests
    {
        /// <summary>列出固定有限的公開操作；不讀機器設定、網路、個資或動態列舉外部輸入。</summary>
        public static IEnumerable<object[]> Operations()
        {
            foreach (var name in new[]
            {
                "appointment-range", "appointment-contact", "appointment-schedule",
                "lessons-enrolled", "lessons-month", "lessons-stor", "meeting-sunday",
                "meeting-weekly", "meeting-contact", "meeting-weekly-alt", "meeting-expired", "meeting-group"
            }) yield return new object[] { name };
        }

        /// <summary>
        /// SDK 回傳兩筆同名不同 ID 與分頁，服務不得去重、改寫或丟棄分頁欄位。
        /// 此搬前基準局部固定西曆並 finally 還原，不修改程序預設文化或寫回 golden。
        /// </summary>
        /// <param name="operation">固定測試操作與 golden 檔名。</param>
        [Theory]
        [MemberData(nameof(Operations))]
        public void ExistingQuery_MatchesGoldenAndPreservesCompleteResult(string operation)
        {
            var expected = DataOperationFixture.Rows(operation);
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => GoldenQueryAssertions.Equal(operation + ".xml",
                    Assert.IsType<FetchExpression>(query).Query)).Returns(expected);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var result = DataOperationFixture.Run(operation, client.Object);
                Assert.Same(expected, result);
                Assert.Equal(2, result.Entities.Count);
                Assert.NotEqual(result.Entities[0].Id, result.Entities[1].Id);
                Assert.Equal(result.Entities[0]["name"], result.Entities[1]["name"]);
                Assert.True(result.MoreRecords);
                Assert.Equal("Synthetic cookie", result.PagingCookie);
            }
            finally { CultureInfo.CurrentCulture = previous; }
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 舊 RetrieveByDate 雖有日期參數，實際不加日期條件；本階段搬移不擅自修改這項既有契約。
        /// 比對型別化 QueryByAttribute 與 AllColumns、空條件，日期篩選維持由明確區間 API 提供。
        /// </summary>
        [Fact]
        public void AppointmentByDate_PreservesLegacyUnfilteredQuery()
        {
            var expected = DataOperationFixture.Rows("appointment-range");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryByAttribute>(query);
                    Assert.Equal("appointment", actual.EntityName);
                    Assert.True(actual.ColumnSet.AllColumns);
                    Assert.Empty(actual.Attributes);
                    Assert.Empty(actual.Values);
                }).Returns(expected);
            var service = new AppointmentService(new object(), client.Object);
            Assert.Same(expected, service.RetrieveByDate(new DateTime(2027, 1, 2)));
        }
    }

    /// <summary>
    /// 合成查詢測試的固定資料及真實入口轉接；不重做產品查詢樹、驗證或資料發布邏輯。
    /// 每次建立獨立 EntityCollection，無共用 SDK、憑證或可變結果。
    /// </summary>
    internal static class DataOperationFixture
    {
        internal const string PrimaryId = "11111111-1111-4111-8111-111111111111";
        internal const string SecondaryId = "22222222-2222-4222-8222-222222222222";

        /// <summary>依操作選擇應回傳根實體，只包含有限的資料庫結構名稱。</summary>
        internal static string EntityName(string operation) => operation.StartsWith("appointment-", StringComparison.Ordinal)
            ? "appointment" : operation == "lessons-stor" ? "new_stor_lessons"
            : operation.StartsWith("lessons-", StringComparison.Ordinal) ? "new_disciple_lessons" : "new_present_record";

        /// <summary>建立同名不同 ID 的完整合成候選，確保測試能區分按名稱去重與正確 ID 驗證。</summary>
        internal static EntityCollection Rows(string operation) => new EntityCollection(new[]
        {
            new Entity(EntityName(operation), new Guid(PrimaryId)) { ["name"] = "Synthetic Same Name" },
            new Entity(EntityName(operation), new Guid(SecondaryId)) { ["name"] = "Synthetic Same Name" }
        }) { MoreRecords = true, PagingCookie = "Synthetic cookie" };

        /// <summary>
        /// 直接呼叫產品方法；所有參數只活於當案，替身只能在 SDK 邊界觀察真正查詢或注入故障。
        /// 日期與 ID 可替換以重現無效區間、文化、XML 字元與格式驗證，不把驗證寫進測試轉接。
        /// </summary>
        internal static EntityCollection Run(string operation, IOrganizationService sdk,
            string? text = null, string primaryId = PrimaryId, string secondaryId = SecondaryId,
            DateTime? start = null, DateTime? end = null)
        {
            var first = start ?? new DateTime(2027, 1, 2);
            var last = end ?? new DateTime(2027, 3, 4);
            switch (operation)
            {
                case "appointment-range": return new AppointmentService(new object(), sdk).RetrieveByDateRange(first, last);
                case "appointment-contact": return new AppointmentService(new object(), sdk)
                    .RetrieveByContactWithinYear(text ?? "Synthetic Contact", primaryId);
                case "appointment-schedule": return new AppointmentService(new object(), sdk)
                    .RetrieveByDateRangeAndScheduleType(first, last, text ?? "100000001");
                case "lessons-enrolled": return new LessonsService(new object(), sdk)
                    .RetrieveEnrolledLessons(first, last, text ?? "Synthetic Contact", primaryId);
                case "lessons-month": return new LessonsService(new object(), sdk).RetrieveLessonsByMonth(first, last);
                case "lessons-stor": return new LessonsService(new object(), sdk)
                    .RetrieveStorLessons(text ?? "Synthetic Lesson", secondaryId, text ?? "Synthetic Contact", primaryId);
                case "meeting-sunday": return new MeetingStatisticsService(new object(), sdk).RetrieveBySunday(first);
                case "meeting-weekly": return new MeetingStatisticsService(new object(), sdk)
                    .RetrieveByWeeklyReportAndContact(text ?? "Synthetic Weekly", secondaryId, text ?? "Synthetic Contact", primaryId);
                case "meeting-contact": return new MeetingStatisticsService(new object(), sdk)
                    .RetrieveBySundayDateAndContact(text ?? "Synthetic Contact", primaryId, first);
                case "meeting-weekly-alt": return new MeetingStatisticsService(new object(), sdk)
                    .RetrieveByWeeklyReportAndContactAlt(text ?? "Synthetic Contact", primaryId, text ?? "Synthetic Weekly", secondaryId);
                case "meeting-expired": return new MeetingStatisticsService(new object(), sdk)
                    .RetrieveWithExpiredDateByContact(text ?? "Synthetic Contact", primaryId);
                case "meeting-group": return new MeetingStatisticsService(new object(), sdk)
                    .RetrieveByContactSmallGroupAndSundayDate(text ?? "Synthetic Contact", primaryId, text ?? "Synthetic Group", secondaryId, first);
                default: throw new ArgumentException("Unknown synthetic operation.", nameof(operation));
            }
        }
    }
}

```

## ToolUtility.Tests/DataOperations/QueryOperationsSafetyTests.cs
```
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 三組純 SDK 資料操作的故障、XML 編碼、跨 scope 與借入資源生命週期契約。
    /// 測試在純化前先重現缺陷；SDK 全為合成替身，不能冒充實機資料或部署驗收。
    /// </summary>
    public sealed class QueryOperationsSafetyTests
    {
        private const string SpecialText = "Synthetic <&\"' value";

        /// <summary>對每一實際 XML 消費頁注入四種完整性故障，覆蓋全部公開查詢而非只測 helper。</summary>
        public static IEnumerable<object[]> InvalidPages()
        {
            foreach (var item in QueryOperationsCharacterizationTests.Operations())
                foreach (var fault in new[] { "duplicate", "empty", "entity", "null" })
                    yield return new object[] { item[0], fault };
        }

        /// <summary>
        /// 重複 ID、空 ID、跨實體或缺候選都須整頁拒絕，不去重或回假空集合。
        /// 失敗後原候選筆數必須不變，避免以修改 SDK 結果掩蓋資料衝突。
        /// </summary>
        /// <param name="operation">真正服務入口。</param>
        /// <param name="fault">合成完整性故障。</param>
        [Theory]
        [MemberData(nameof(InvalidPages))]
        public void Query_InvalidPage_RejectsWithoutMutatingCandidate(string operation, string fault)
        {
            var candidate = DataOperationFixture.Rows(operation);
            if (fault == "duplicate") candidate.Entities[1].Id = candidate.Entities[0].Id;
            if (fault == "empty") candidate.Entities[1].Id = Guid.Empty;
            if (fault == "entity") candidate.Entities[1].LogicalName = "synthetic_wrong_entity";
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns(fault == "null" ? null! : candidate);
            Assert.Throws<InvalidOperationException>(() => DataOperationFixture.Run(operation, client.Object));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 會友、課程、週報、小組與排程文字包含全部特殊字元時，XML 解碼必須完整還原，
        /// 且只出現在預定屬性值，不能成為額外條件／節點；不以字串 Contains 當作安全證據。
        /// </summary>
        /// <param name="operation">有使用者顯示文字或排程值的查詢。</param>
        [Theory]
        [InlineData("appointment-contact")]
        [InlineData("appointment-schedule")]
        [InlineData("lessons-enrolled")]
        [InlineData("lessons-stor")]
        [InlineData("meeting-weekly")]
        [InlineData("meeting-contact")]
        [InlineData("meeting-weekly-alt")]
        [InlineData("meeting-expired")]
        [InlineData("meeting-group")]
        public void Query_XmlSpecialCharacters_RemainOnlyAttributeValues(string operation)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            string? ordinaryXml = null;
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var xml = Assert.IsType<FetchExpression>(query).Query;
                    var document = XDocument.Parse(xml);
                    if (ordinaryXml == null) ordinaryXml = xml;
                    else
                    {
                        var ordinary = XDocument.Parse(ordinaryXml);
                        Assert.Equal(ordinary.Descendants().Count(), document.Descendants().Count());
                        Assert.Contains(document.Descendants().Attributes(), value => value.Value == SpecialText);
                        Assert.Equal(ordinary.Descendants("condition").Count(), document.Descendants("condition").Count());
                    }
                }).Returns(DataOperationFixture.Rows(operation));
            DataOperationFixture.Run(operation, client.Object);
            DataOperationFixture.Run(operation, client.Object, text: SpecialText);
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(2));
        }

        /// <summary>非西曆環境下全部日期與選項仍須符合原西曆 golden，finally 還原當案文化。</summary>
        /// <param name="operation">全部固定合成操作。</param>
        [Theory]
        [MemberData(nameof(QueryOperationsCharacterizationTests.Operations), MemberType = typeof(QueryOperationsCharacterizationTests))]
        public void Query_ThaiCulture_UsesInvariantGregorianDates(string operation)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => GoldenQueryAssertions.Equal(operation + ".xml",
                    Assert.IsType<FetchExpression>(query).Query)).Returns(DataOperationFixture.Rows(operation));
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
                DataOperationFixture.Run(operation, client.Object);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        /// <summary>
        /// SDK 真正故障須原樣傳至最終 host 通知邊界；下一次可重新查詢，
        /// 不因失敗建立永久失敗 cache、隱藏 retry 或傳回部分候選。
        /// </summary>
        /// <param name="operation">全部查詢入口。</param>
        [Theory]
        [MemberData(nameof(QueryOperationsCharacterizationTests.Operations), MemberType = typeof(QueryOperationsCharacterizationTests))]
        public void Query_SdkFailure_PropagatesOriginalAndAllowsRetry(string operation)
        {
            var failure = new InvalidOperationException("Synthetic private SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var candidate = DataOperationFixture.Rows(operation);
            client.SetupSequence(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(failure).Returns(candidate);
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => DataOperationFixture.Run(operation, client.Object)));
            Assert.Same(candidate, DataOperationFixture.Run(operation, client.Object));
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(2));
        }

        /// <summary>反向日期區間不得進 SDK，也不能以交換端點改變 caller 的原始意圖。</summary>
        /// <param name="operation">含明確起訖日期的入口。</param>
        [Theory]
        [InlineData("appointment-range")]
        [InlineData("appointment-schedule")]
        [InlineData("lessons-enrolled")]
        [InlineData("lessons-month")]
        public void Query_ReverseDateRange_RejectsBeforeIo(string operation)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            Assert.Throws<ArgumentException>(() => DataOperationFixture.Run(operation, client.Object,
                start: new DateTime(2027, 3, 4), end: new DateTime(2027, 1, 2)));
            client.VerifyNoOtherCalls();
        }

        /// <summary>所有 lookup GUID 先驗證；含 XML 符號的偽 ID 不能成為條件或未授權廣泛查詢。</summary>
        /// <param name="operation">使用主或次要 lookup 的入口。</param>
        /// <param name="secondary">改壞第二個 lookup，避免只驗第一個 ID 的盲點。</param>
        [Theory]
        [InlineData("appointment-contact", false)]
        [InlineData("lessons-enrolled", false)]
        [InlineData("lessons-stor", false)]
        [InlineData("lessons-stor", true)]
        [InlineData("meeting-weekly", false)]
        [InlineData("meeting-weekly", true)]
        [InlineData("meeting-contact", false)]
        [InlineData("meeting-weekly-alt", false)]
        [InlineData("meeting-weekly-alt", true)]
        [InlineData("meeting-expired", false)]
        [InlineData("meeting-group", false)]
        [InlineData("meeting-group", true)]
        public void Query_InvalidLookup_RejectsBeforeIo(string operation, bool secondary)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            foreach (var invalid in new[] { "", "not-a-guid", "<&\"'", Guid.Empty.ToString() })
                Assert.Throws<ArgumentException>(() => DataOperationFixture.Run(operation, client.Object,
                    primaryId: secondary ? DataOperationFixture.PrimaryId : invalid,
                    secondaryId: secondary ? invalid : DataOperationFixture.SecondaryId));
            client.VerifyNoOtherCalls();
        }

        /// <summary>不存在的借入 SDK 是設定錯誤，三個建構式都需立即拒絕，不保存任意 logger。</summary>
        [Fact]
        public void Constructors_NullSdk_RejectImmediately()
        {
            Assert.Throws<ArgumentNullException>(() => new AppointmentService(new object(), null!));
            Assert.Throws<ArgumentNullException>(() => new LessonsService(new object(), null!));
            Assert.Throws<ArgumentNullException>(() => new MeetingStatisticsService(new object(), null!));
        }

        /// <summary>
        /// 八路交錯 1200 次查詢獨立 A/B SDK，資料與 caller 修改不能跨 scope 或下一次操作。
        /// 全部同步 I/O drain 後 active=0；借入的 IDisposable SDK 不由純資料服務關閉。
        /// </summary>
        [Fact]
        public void Queries_ParallelScopesAndCallerMutation_IsolateAndDrain()
        {
            var operations = QueryOperationsCharacterizationTests.Operations().Select(item => (string)item[0]).ToArray();
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            foreach (var client in clients) client.As<IDisposable>();
            for (var scope = 0; scope < clients.Length; scope++)
            {
                var marker = scope == 0 ? "Synthetic A" : "Synthetic B";
                clients[scope].Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                    .Returns<QueryBase>(query =>
                    {
                        var entityName = (string)XDocument.Parse(Assert.IsType<FetchExpression>(query).Query).Root!.Element("entity")!.Attribute("name")!;
                        return new EntityCollection(new[] { new Entity(entityName, Guid.NewGuid()) { ["name"] = marker } });
                    });
            }
            var active = 0;
            var completed = 0;
            Parallel.For(0, 1200, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
            {
                Interlocked.Increment(ref active);
                try
                {
                    var scope = index % 2;
                    var row = DataOperationFixture.Run(operations[index % operations.Length], clients[scope].Object).Entities[0];
                    Assert.Equal(scope == 0 ? "Synthetic A" : "Synthetic B", row["name"]);
                    row["name"] = "Synthetic caller mutation";
                    Interlocked.Increment(ref completed);
                }
                finally { Interlocked.Decrement(ref active); }
            });
            Assert.Equal(1200, completed);
            Assert.Equal(0, active);
            foreach (var client in clients) client.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
        }
    }
}

```

## ToolUtility.Tests/FeeOperations/FeeQueryContractTests.cs
```
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.FeeOperations;
using Xunit;

namespace ToolUtility.Tests.FeeOperations
{
    /// <summary>
    /// 保護 P1-04 新增收費查詢及既有查詢安全契約。所有資料、ID與故障均合成；
    /// 每案獨立借入 SDK 替身，沒有實機連線、共享快取或持續背景工作。
    /// </summary>
    public sealed class FeeQueryContractTests
    {
        private const string ScopeId = "11111111-1111-4111-8111-111111111111";
        private const string SpecialText = "Synthetic <&\"' value";

        /// <summary>
        /// 鎖定三個舊來源補回方法的完整條件及欄位；注入同名不同ID兩筆資料，
        /// 以完整golden及兩筆ID斷言證明沒有按名稱合併或丟失。
        /// </summary>
        /// <param name="operation">待新增的三個查詢之一。</param>
        /// <param name="golden">唯讀合成XML資源。</param>
        [Theory]
        [InlineData("Paid", "fee-paid.xml")]
        [InlineData("Unpaid", "fee-unpaid.xml")]
        [InlineData("Order", "fee-order.xml")]
        public void NewQuery_MatchesGoldenAndPreservesSameNameRows(string operation, string golden)
        {
            var expected = Rows();
            expected.MoreRecords = true;
            expected.PagingCookie = "Synthetic paging cookie";
            var client = Client(expected, xml => GoldenQueryAssertions.Equal(golden, xml));
            var sut = new FeeService(new object(), client.Object);

            var result = Invoke(sut, operation);

            Assert.Same(expected, result);
            Assert.True(result.MoreRecords);
            Assert.Equal("Synthetic paging cookie", result.PagingCookie);
            Assert.Equal(2, result.Entities.Count);
            Assert.NotEqual(result.Entities[0].Id, result.Entities[1].Id);
            Assert.Equal(result.Entities[0]["new_name"], result.Entities[1]["new_name"]);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>
        /// 課程ID是查詢界線；注入空白、空GUID及XML注入字串均須在I/O前拒絕，
        /// 不可將XML編碼誤當成授權或合法識別碼驗證。
        /// </summary>
        /// <param name="operation">已繳或未繳查詢。</param>
        /// <param name="invalidId">無效的合成識別值。</param>
        [Theory]
        [InlineData("Paid", "")]
        [InlineData("Unpaid", " ")]
        [InlineData("Paid", "00000000-0000-0000-0000-000000000000")]
        [InlineData("Unpaid", "<&\"'")]
        public void LessonQuery_InvalidId_RejectsBeforeIo(string operation, string invalidId)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(new object(), client.Object);

            Assert.Throws<ArgumentException>(() => Invoke(sut, operation, invalidId));

            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 舊API保留但未使用的課程名稱可含XML字元，不能被加入查詢或拿來當識別／權限；
        /// 完整golden仍相等，證明兩個方法只按伺服器提供的課程ID查詢。
        /// </summary>
        /// <param name="operation">已繳或未繳查詢。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        public void LessonQuery_DisplayNameCannotChangeScope(string operation)
        {
            var client = Client(Rows(), xml => GoldenQueryAssertions.Equal(
                operation == "Paid" ? "fee-paid.xml" : "fee-unpaid.xml", xml));
            var sut = new FeeService(new object(), client.Object);

            Assert.Equal(2, Invoke(sut, operation, ScopeId, SpecialText).Entities.Count);
        }

        /// <summary>
        /// 訂單號是文字而非GUID；含所有XML特殊字元仍須只產生原有兩個OR條件，
        /// 解碼後值逐字相等，不能新增節點或讓卡／ATM任一分支遺失。
        /// </summary>
        [Fact]
        public void OrderQuery_XmlSpecialCharacters_RoundTripWithoutInjection()
        {
            var client = Client(Rows(), xml =>
            {
                var entity = XDocument.Parse(xml).Root!.Element("entity")!;
                var conditions = entity.Element("filter")!.Element("filter")!.Elements("condition").ToArray();
                Assert.Equal("or", (string?)entity.Element("filter")!.Element("filter")!.Attribute("type"));
                Assert.Equal(2, conditions.Length);
                Assert.Equal(new[] { "new_q_pay_card_order_no", "new_q_pay_order_atm_no" },
                    conditions.Select(condition => (string?)condition.Attribute("attribute")));
                Assert.All(conditions, condition => Assert.Equal(SpecialText, (string?)condition.Attribute("value")));
            });
            var sut = new FeeService(new object(), client.Object);

            Assert.Equal(2, sut.RetrieveFeeByFetchXmlOrderNumber(SpecialText).Entities.Count);
        }

        /// <summary>
        /// 空訂單號不得擴張為廣泛查詢，並且拒絕前不碰SDK。
        /// </summary>
        /// <param name="orderNumber">合成無效輸入。</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void OrderQuery_EmptyInput_RejectsBeforeIo(string? orderNumber)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(new object(), client.Object);

            Assert.Throws<ArgumentException>(() => sut.RetrieveFeeByFetchXmlOrderNumber(orderNumber!));

            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 每個新增方法注入SDK故障，原始同一Exception必須傳回host，不能回空集合、
        /// 吞例外或包成含私密本文的新訊息。Core不是最終通知邊界。
        /// </summary>
        /// <param name="operation">新增查詢名稱。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        [InlineData("Order")]
        public void NewQuery_SdkFault_PropagatesSameException(string operation)
        {
            var fault = new InvalidOperationException("Synthetic private SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Throws(fault);
            var sut = new FeeService(new object(), client.Object);

            Assert.Same(fault, Assert.Throws<InvalidOperationException>(() => Invoke(sut, operation)));
        }

        /// <summary>
        /// 空殼認獻／收費方法必須沿用已鎖定的有效查詢，而不是自行回空集合。
        /// SDK request、上限及欄位與有效來源golden完全一致。
        /// </summary>
        /// <param name="operation">原有空方法。</param>
        [Theory]
        [InlineData("Booking")]
        [InlineData("Fee")]
        public void FormerPlaceholder_UsesEstablishedQuery(string operation)
        {
            var expected = operation == "Booking" ? new EntityCollection(new[]
            {
                new Entity("new_dedication_booking", new Guid("22222222-2222-4222-8222-222222222222"))
            }) : Rows();
            var client = Client(expected, xml => GoldenQueryAssertions.Equal(
                operation == "Booking" ? "query-dedication-booking.xml" : "query-fee-by-booking.xml", xml));
            var sut = new FeeService(new object(), client.Object);

            Assert.Same(expected, Invoke(sut, operation));
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>
        /// 注入同一非空資料庫ID兩次；所有受改查詢都必須拒絕整個候選，
        /// 不可保留一筆、名稱去重或將部分結果交給上層。
        /// </summary>
        /// <param name="operation">Fee全部公開查詢入口。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        [InlineData("Order")]
        [InlineData("Booking")]
        [InlineData("Fee")]
        [InlineData("Dedication")]
        [InlineData("DateRange")]
        public void Query_DuplicateStableId_RejectsPublication(string operation)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            candidate.Entities[1].Id = candidate.Entities[0].Id;
            var client = Client(candidate, _ => { });
            var sut = new FeeService(new object(), client.Object);

            var fault = Assert.Throws<InvalidOperationException>(() => Invoke(sut, operation));

            Assert.DoesNotContain(candidate.Entities[0].Id.ToString(), fault.Message);
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 欠缺真實資料庫 ID 的正式列或混入其他實體列均拒絕；不能產生draft key讓正式資料通過。
        /// </summary>
        /// <param name="operation">Fee全部查詢入口。</param>
        /// <param name="missingId">注入空ID或錯誤實體型別。</param>
        [Theory]
        [InlineData("Paid", true)]
        [InlineData("Unpaid", false)]
        [InlineData("Order", true)]
        [InlineData("Booking", false)]
        [InlineData("Fee", true)]
        [InlineData("Dedication", false)]
        [InlineData("DateRange", true)]
        public void Query_InvalidStableIdentity_RejectsPublication(string operation, bool missingId)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            if (missingId) candidate.Entities[1].Id = Guid.Empty;
            else candidate.Entities[1].LogicalName = "synthetic_wrong_entity";
            var client = Client(candidate, _ => { });

            Assert.Throws<InvalidOperationException>(() => Invoke(new FeeService(new object(), client.Object), operation));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 所有lookup顯示名稱及期數文字只可當XML值；含特殊字元解碼後須逐字相等，
        /// 條件數不增加，證明修復既有入口而非只保護新增訂單方法。
        /// </summary>
        /// <param name="operation">使用外部文字的既有查詢。</param>
        [Theory]
        [InlineData("Booking")]
        [InlineData("Fee")]
        [InlineData("Dedication")]
        [InlineData("DateRange")]
        public void ExistingQuery_SpecialDisplayName_IsOnlyAnAttributeValue(string operation)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            var client = Client(candidate, xml =>
            {
                var lookups = XDocument.Parse(xml).Descendants("condition")
                    .Where(condition => condition.Attribute("uiname") != null).ToArray();
                Assert.Single(lookups);
                Assert.Equal(SpecialText, (string?)lookups[0].Attribute("uiname"));
                if (operation == "Fee")
                    Assert.Equal(SpecialText, (string?)XDocument.Parse(xml).Descendants("condition")
                        .Single(condition => (string?)condition.Attribute("attribute") == "new_paid_period").Attribute("value"));
            });

            var sut = new FeeService(new object(), client.Object);
            if (operation == "Fee")
                sut.RetrieveFee(SpecialText, ScopeId, SpecialText);
            else
                Invoke(sut, operation, ScopeId, SpecialText);
        }

        /// <summary>
        /// SDK每次提供獨立操作資料；第一個呼叫者修改集合不可污染下一次回應，
        /// 並以兩次真實SDK呼叫證明服務沒有偷偷快取前一範圍的可變結果。
        /// </summary>
        [Fact]
        public void Query_CallerMutation_DoesNotBecomeNextOperationState()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Returns(() =>
                {
                    var response = new RetrieveMultipleResponse();
                    response.Results["EntityCollection"] = Rows();
                    return response;
                });
            var sut = new FeeService(new object(), client.Object);
            var first = Invoke(sut, "Paid");
            first.Entities[0]["new_name"] = "Synthetic caller mutation";
            first.Entities.RemoveAt(1);

            var second = Invoke(sut, "Paid");

            Assert.NotSame(first, second);
            Assert.Equal(2, second.Entities.Count);
            Assert.Equal("Synthetic Fee", second.Entities[0]["new_name"]);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Exactly(2));
        }

        /// <summary>建構時拒絕沒有授權SDK的服務，不能延至呼叫才以NullReferenceException掩蓋設定失敗。</summary>
        [Fact]
        public void Constructor_NullService_RejectsImmediately()
        {
            Assert.Throws<ArgumentNullException>(() => new FeeService(null!));
        }

        /// <summary>
        /// 期數是必要業務條件；明確鎖定空白拒絕而非悄悄擴張查詢。
        /// 現有 ChurchReport 消費者固定傳入001，此邊界不改變其有效流程；拒絕前不碰SDK。
        /// </summary>
        /// <param name="period">無效的合成期數。</param>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void RetrieveFee_EmptyPeriod_RejectsBeforeIo(string period)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(client.Object);
            Assert.Throws<ArgumentException>(() => sut.RetrieveFee("Synthetic Booking", ScopeId, period));
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 非西曆環境不能改變資料庫日期條件；在本案局部注入泰國曆並finally還原，
        /// 應仍符合已核對的西曆golden，不能靠改主機全域文化換取通過。
        /// </summary>
        [Fact]
        public void DateRange_ThaiCulture_UsesGregorianQuery()
        {
            var client = Client(Rows(), xml => GoldenQueryAssertions.Equal("fee-dedication-date-range.xml", xml));
            var sut = new FeeService(new object(), client.Object);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
                Invoke(sut, "DateRange");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        /// <summary>
        /// 反向日期區間是預期輸入拒絕，必須在 SDK I/O 前停止，不能擴張成不受限查詢。
        /// 此案例保護既有安全分支；不修改產品斷言或以空集合冒充合法結果。
        /// </summary>
        [Fact]
        public void DateRange_StartAfterEnd_RejectsBeforeIo()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(client.Object);
            Assert.Throws<ArgumentException>(() => sut.RetrieveDedicationFeeByDateRange("Synthetic Contact", ScopeId,
                new DateTime(2027, 3, 4), new DateTime(2027, 1, 2)));
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A/B各自借入SDK，交錯執行1000次不混查詢或資料，操作全部排空後不留活動工作；
        /// IDisposable借入服務不能被Fee關閉。固定八路排程避免測試本身無界fan-out。
        /// </summary>
        [Fact]
        public void Query_ParallelScopes_DoNotLeakOrDisposeBorrowedService()
        {
            var firstClient = Client(Rows(), xml => Assert.Contains(ScopeId, xml));
            var secondId = "44444444-4444-4444-8444-444444444444";
            var secondRows = Rows();
            secondRows.Entities[0].Id = new Guid("55555555-5555-4555-8555-555555555555");
            var secondClient = Client(secondRows, xml => Assert.Contains(secondId, xml));
            var firstDisposable = firstClient.As<IDisposable>();
            var secondDisposable = secondClient.As<IDisposable>();
            var first = new FeeService(new object(), firstClient.Object);
            var second = new FeeService(new object(), secondClient.Object);
            var active = 0;
            var completed = 0;

            Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
            {
                Interlocked.Increment(ref active);
                try
                {
                    var isFirst = index % 2 == 0;
                    var result = Invoke(isFirst ? first : second, "Paid", isFirst ? ScopeId : secondId);
                    Assert.Equal(isFirst ? Rows().Entities[0].Id : secondRows.Entities[0].Id, result.Entities[0].Id);
                    Interlocked.Increment(ref completed);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });

            Assert.Equal(1000, completed);
            Assert.Equal(0, active);
            firstDisposable.Verify(service => service.Dispose(), Times.Never);
            secondDisposable.Verify(service => service.Dispose(), Times.Never);
        }

        /// <summary>
        /// 建立每案獨立SDK替身；只保留該案候選，callback收到的XML立即驗證，不保存請求或credential。
        /// </summary>
        private static Mock<IOrganizationService> Client(EntityCollection candidate, Action<string> assertXml)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = candidate;
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request => assertXml(
                    Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query))
                .Returns(response);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => assertXml(Assert.IsType<FetchExpression>(query).Query))
                .Returns(candidate);
            return client;
        }

        /// <summary>合成的兩筆同名不同ID資料，沒有靜態可變資料或真實個資。</summary>
        private static EntityCollection Rows(string logicalName = "new_fee") => new EntityCollection(new[]
        {
            new Entity(logicalName, new Guid("22222222-2222-4222-8222-222222222222")) { ["new_name"] = "Synthetic Fee" },
            new Entity(logicalName, new Guid("33333333-3333-4333-8333-333333333333")) { ["new_name"] = "Synthetic Fee" }
        });

        /// <summary>依固定測試名稱呼叫真正服務，不在替身複製查詢或驗證邏輯。</summary>
        private static EntityCollection Invoke(FeeService sut, string operation, string id = ScopeId, string name = "Synthetic Contact")
        {
            switch (operation)
            {
                case "Paid": return sut.GetPaidFee(name, id);
                case "Unpaid": return sut.GetNotYetPaidFee(name, id);
                case "Order": return sut.RetrieveFeeByFetchXmlOrderNumber("Synthetic-Order");
                case "Booking": return sut.RetrieveDedicationBooking(name, id);
                case "Fee": return sut.RetrieveFee("Synthetic Booking", id, "3");
                case "Dedication": return sut.RetrieveDedicationFee(name, id);
                case "DateRange": return sut.RetrieveDedicationFeeByDateRange(name, id, new DateTime(2027, 1, 2), new DateTime(2027, 3, 4));
                default: throw new ArgumentException("Unknown synthetic operation.", nameof(operation));
            }
        }
    }
}

```

## ToolUtility.Tests/Golden/ToolUtility/entity-task.xml
```
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" top="100">
  <entity name="task">
    <attribute name="subject" />
    <attribute name="statecode" />
    <attribute name="prioritycode" />
    <attribute name="scheduledend" />
    <attribute name="createdby" />
    <attribute name="regardingobjectid" />
    <attribute name="activityid" />
    <attribute name="description" />
    <order attribute="createdon" descending="true" />
    <filter type="and">
      <condition attribute="subject" operator="eq" value="Synthetic Task" />
      <condition attribute="statecode" operator="eq" value="0" />
    </filter>
  </entity>
</fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/appointment-contact.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='new_leave_signing_status' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <attribute name='new_hours' />
                        <attribute name='new_days' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_applier_appointment' operator='eq' uiname='Synthetic Contact' uitype='contact' value='{11111111-1111-4111-8111-111111111111}' />
                          <condition attribute='scheduledstart' operator='this-year' />
                          <condition attribute='new_leave_signing_status' operator='in'>
                                <value> 100000004 </value >
                                <value> 100000001 </value >
                                <value> 100000007 </value >
                          </condition >
                        </filter>
                      </entity>
                    </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/appointment-range.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after'  value='2027-1-2' />
                          <condition attribute='scheduledstart' operator='on-or-before' value='2027-3-4' />
                        </filter>
                      </entity>
                    </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/appointment-schedule.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after'  value='2027-1-2' />
                          <condition attribute='scheduledstart' operator='on-or-before' value='2027-3-4' />
                          <condition attribute='new_meeting_kind' operator='eq' value='100000001' />
                        </filter>
                      </entity>
                    </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/lessons-enrolled.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                      <entity name='new_disciple_lessons'>
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_class_start_date' />
                        <attribute name='new_class_end_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_disciple_lessonsid' />
                        <order attribute='new_classification' descending='false' />
                        <filter type='and'>
                            <condition attribute='new_class_start_date' operator='on-or-after'  value='2027-1-2' />
                            <condition attribute='new_class_end_date' operator='on-or-before' value='2027-3-4' />
                        </filter>
                        <link-entity name='new_stor_lessons' from='new_new_disciple_lessons_new_stor_les' to='new_disciple_lessonsid' alias='ab'>
                          <filter type='and'>
                            <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname='Synthetic Contact' uitype ='contact' value='{11111111-1111-4111-8111-111111111111}' />
                          </filter>
                        </link-entity>
                      </entity>
                    </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/lessons-month.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                      <entity name='new_disciple_lessons'>
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_class_start_date' />
                        <attribute name='new_class_end_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_disciple_lessonsid' />
                        <order attribute='new_classification' descending='false' />
                        <filter type='and'>
                            <condition attribute='new_class_start_date' operator='on-or-after'  value='2027-1-2' />
                            <condition attribute='new_class_end_date' operator='on-or-before' value='2027-3-4' />
                        </filter>
                      </entity>
                    </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/lessons-stor.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='5'>
                      <entity name='new_stor_lessons'>
                        <attribute name='createdon' />
                        <attribute name='new_contact_new_stor_lessons' />
                        <attribute name='new_fee' />
                        <attribute name='new_pay_date' />
                        <attribute name='new_new_disciple_lessons_new_stor_les' />
                        <attribute name='new_stor_lessonsid' />
                        <order attribute='createdon' descending='true' />
                        <filter type='and'>
                          <condition attribute='new_enroll_status' operator='not-in'>
                            <value>100000007</value>
                            <value>100000009</value>
                            <value>100000003</value>
                          </condition>
                          <condition attribute='new_new_disciple_lessons_new_stor_les' operator='eq' uiname='Synthetic Lesson' uitype='new_disciple_lessons' value='{22222222-2222-4222-8222-222222222222}' />
                          <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname='Synthetic Contact' uitype='contact' value='{11111111-1111-4111-8111-111111111111}' />
                          <condition attribute='statuscode' operator='ne' value='2' />
                          <condition attribute='statecode' operator='eq' value='0' />
                        </filter>
                        <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='a_45d999afd4cc4001b091647bb91668ef'>
                          <attribute name='telephone2' />
                          <attribute name='address2_line1' />
                          <attribute name='parentcustomerid' />
                          <attribute name='mobilephone' />
                          <attribute name='emailaddress1' />
                        </link-entity>
                      </entity>
                    </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/meeting-contact.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                        <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                             <condition attribute='new_contact_new_present_record' operator='eq' uiname='Synthetic Contact' uitype='contact' value='{11111111-1111-4111-8111-111111111111}' />
                             <condition attribute='new_sunday_date' operator='on' value='2027-1-2' />
                        </filter>
                      </entity>
                    </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/meeting-expired.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname='Synthetic Contact' uitype='contact' value='{11111111-1111-4111-8111-111111111111}' />
                          <condition attribute='new_care_expire_date' operator='not-null' />
                        </filter>
                      </entity>
                    </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/meeting-group.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_present_record'>
                            <attribute name='new_present_recordid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_list_new_present_record' operator='eq' uiname='Synthetic Group' uitype='list' value='{22222222-2222-4222-8222-222222222222}' />
                              <condition attribute='new_contact_new_present_record' operator='eq' uiname='Synthetic Contact' uitype='contact' value='{11111111-1111-4111-8111-111111111111}' />
                              <condition attribute='new_sunday_date' operator='on' value='2027-1-2' />
                            </filter>
                          </entity>
                        </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/meeting-sunday.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_sunday_date' operator='on' value='2027-1-2' />
                        </filter>
                      </entity>
                    </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/meeting-weekly-alt.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_group_present_weekly_report_prese' operator='eq' uiname='Synthetic Weekly' uitype='new_group_present_weekly_report' value='{22222222-2222-4222-8222-222222222222}' />
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname='Synthetic Contact' uitype='contact' value='{11111111-1111-4111-8111-111111111111}' />
                        </filter>
                      </entity>
                    </fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/meeting-weekly.xml
```
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_group_present_weekly_report_prese' operator='eq' uiname='Synthetic Weekly' uitype ='new_disciple_lessons' value='{22222222-2222-4222-8222-222222222222}' />
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname='Synthetic Contact' uitype='contact' value='{11111111-1111-4111-8111-111111111111}' />
                        </filter>
                      </entity>
                    </fetch>

```

## 固定基準47ab85f8原始來源 ToolUtility/AppointmentOperations/AppointmentService.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/AppointmentOperations/AppointmentService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class AppointmentService
// 主要成員：RetrieveByDate、RetrieveByDateRange、RetrieveByContactWithinYear、RetrieveByDateRangeAndScheduleType
// 引用命名空間：System、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、ToolUtilityNameSpace.EntityOperations
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.AppointmentOperations
{
    public class AppointmentService : IAppointmentService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public AppointmentService(object logger,IOrganizationService organizationService)
        {
            _logger = logger;
            _organizationService = organizationService;
        }

        public EntityCollection RetrieveByDate(DateTime selectedDate)
        {
            // Legacy method did not filter by date; keep behavior
            var query = new QueryByAttribute("appointment") { ColumnSet = new ColumnSet(true) };
            return _organizationService.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveByDateRange(DateTime startDate, DateTime endDate)
        {
            string start = $"'{startDate:yyyy-M-d}'";
            string end = $"'{endDate:yyyy-M-d}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after'  value={start} />
                          <condition attribute='scheduledstart' operator='on-or-before' value={end} />
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveByContactWithinYear(string contactName, string contactId)
        {
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='new_leave_signing_status' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <attribute name='new_hours' />
                        <attribute name='new_days' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_applier_appointment' operator='eq' uiname='{contactName}' uitype='contact' value='{{{contactId}}}' />
                          <condition attribute='scheduledstart' operator='this-year' />
                          <condition attribute='new_leave_signing_status' operator='in'>
                                <value> 100000004 </value >
                                <value> 100000001 </value >
                                <value> 100000007 </value >
                          </condition >
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveByDateRangeAndScheduleType(DateTime startDate, DateTime endDate, string scheduleType)
        {
            string start = $"'{startDate:yyyy-M-d}'";
            string end = $"'{endDate:yyyy-M-d}'";
            string sType = $"'{scheduleType}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after'  value={start} />
                          <condition attribute='scheduledstart' operator='on-or-before' value={end} />
                          <condition attribute='new_meeting_kind' operator='eq' value={sType} />
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}

```

## 固定基準47ab85f8原始來源 ToolUtility/LessonsOperations/LessonsService.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/LessonsOperations/LessonsService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class LessonsService
// 主要成員：RetrieveEnrolledLessons、RetrieveLessonsByMonth、RetrieveStorLessons
// 引用命名空間：System、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、ToolUtilityNameSpace.EntityOperations
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.LessonsOperations
{
    public class LessonsService : ILessonsService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public LessonsService(object logger, IOrganizationService organizationService)
        {
            _logger = logger;
            _organizationService = organizationService;
        }

        public EntityCollection RetrieveEnrolledLessons(DateTime startDate, DateTime endDate, string contactName, string contactId)
        {
            string s = $"'{startDate:yyyy-M-d}'";
            string e = $"'{endDate:yyyy-M-d}'";
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                      <entity name='new_disciple_lessons'>
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_class_start_date' />
                        <attribute name='new_class_end_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_disciple_lessonsid' />
                        <order attribute='new_classification' descending='false' />
                        <filter type='and'>
                            <condition attribute='new_class_start_date' operator='on-or-after'  value={s} />
                            <condition attribute='new_class_end_date' operator='on-or-before' value={e} />
                        </filter>
                        <link-entity name='new_stor_lessons' from='new_new_disciple_lessons_new_stor_les' to='new_disciple_lessonsid' alias='ab'>
                          <filter type='and'>
                            <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname={contactName} uitype ='contact' value={contactId} />
                          </filter>
                        </link-entity>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveLessonsByMonth(DateTime startDate, DateTime endDate)
        {
            string s = $"'{startDate:yyyy-M-d}'";
            string e = $"'{endDate:yyyy-M-d}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                      <entity name='new_disciple_lessons'>
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_class_start_date' />
                        <attribute name='new_class_end_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_disciple_lessonsid' />
                        <order attribute='new_classification' descending='false' />
                        <filter type='and'>
                            <condition attribute='new_class_start_date' operator='on-or-after'  value={s} />
                            <condition attribute='new_class_end_date' operator='on-or-before' value={e} />
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveStorLessons(string lessonName, string lessonId, string contactName, string contactId)
        {
            lessonName = $"'{lessonName}'";
            lessonId = $"'{{{lessonId}}}'";
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='5'>
                      <entity name='new_stor_lessons'>
                        <attribute name='createdon' />
                        <attribute name='new_contact_new_stor_lessons' />
                        <attribute name='new_fee' />
                        <attribute name='new_pay_date' />
                        <attribute name='new_new_disciple_lessons_new_stor_les' />
                        <attribute name='new_stor_lessonsid' />
                        <order attribute='createdon' descending='true' />
                        <filter type='and'>
                          <condition attribute='new_enroll_status' operator='not-in'>
                            <value>100000007</value>
                            <value>100000009</value>
                            <value>100000003</value>
                          </condition>
                          <condition attribute='new_new_disciple_lessons_new_stor_les' operator='eq' uiname={lessonName} uitype='new_disciple_lessons' value={lessonId} />
                          <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                          <condition attribute='statuscode' operator='ne' value='2' />
                          <condition attribute='statecode' operator='eq' value='0' />
                        </filter>
                        <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='a_45d999afd4cc4001b091647bb91668ef'>
                          <attribute name='telephone2' />
                          <attribute name='address2_line1' />
                          <attribute name='parentcustomerid' />
                          <attribute name='mobilephone' />
                          <attribute name='emailaddress1' />
                        </link-entity>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}

```

## 固定基準47ab85f8原始來源 ToolUtility/MeetingStatisticsOperations/MeetingStatisticsService.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/MeetingStatisticsOperations/MeetingStatisticsService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class MeetingStatisticsService
// 主要成員：RetrieveBySunday、RetrieveByWeeklyReportAndContact、RetrieveBySundayDateAndContact、RetrieveByWeeklyReportAndContactAlt、RetrieveWithExpiredDateByContact、RetrieveByContactSmallGroupAndSundayDate
// 引用命名空間：System、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、ToolUtilityNameSpace.EntityOperations
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.MeetingStatisticsOperations
{
    /// <summary>
    /// 個人聚會與靈修記錄服務實作
    /// </summary>
    public class MeetingStatisticsService : IMeetingStatisticsService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public MeetingStatisticsService(object logger, IOrganizationService organizationService)
        {
            _logger = logger;
            _organizationService = organizationService;
        }

        public EntityCollection RetrieveBySunday(DateTime sundayDate)
        {
            string dateStr = $"'{sundayDate:yyyy-M-d}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_sunday_date' operator='on' value={dateStr} />
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據週報和連絡人查詢出席記錄
        /// </summary>
        public EntityCollection RetrieveByWeeklyReportAndContact(string weeklyReportName, string weeklyReportId, string contactName, string contactId)
        {
            weeklyReportName = $"'{weeklyReportName}'";
            weeklyReportId = $"'{{{weeklyReportId}}}'";
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_group_present_weekly_report_prese' operator='eq' uiname={weeklyReportName} uitype ='new_disciple_lessons' value={weeklyReportId} />
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                        </filter>
                      </entity>
                    </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據連絡人和主日日期查詢出席記錄
        /// </summary>
        public EntityCollection RetrieveBySundayDateAndContact(string contactName, string contactId, DateTime sundayDate)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            string sundayDateString = $"'{sundayDate:yyyy-M-d}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                        <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                             <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                             <condition attribute='new_sunday_date' operator='on' value={sundayDateString} />
                        </filter>
                      </entity>
                    </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據週報和連絡人查詢出席記錄 (替代方法)
        /// </summary>
        public EntityCollection RetrieveByWeeklyReportAndContactAlt(string contactName, string contactId, string weeklyReportName, string weeklyReportId)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            weeklyReportName = $"'{weeklyReportName}'";
            weeklyReportId = $"'{{{weeklyReportId}}}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_group_present_weekly_report_prese' operator='eq' uiname={weeklyReportName} uitype='new_group_present_weekly_report' value={weeklyReportId} />
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                        </filter>
                      </entity>
                    </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 查詢包含關懷到期日的出席記錄
        /// </summary>
        public EntityCollection RetrieveWithExpiredDateByContact(string contactName, string contactId)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                          <condition attribute='new_care_expire_date' operator='not-null' />
                        </filter>
                      </entity>
                    </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據連絡人、小組和主日日期查詢出席記錄
        /// </summary>
        public EntityCollection RetrieveByContactSmallGroupAndSundayDate(string contactName, string contactId, string smallGroupName, string smallGroupId, DateTime sundayDate)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";
            smallGroupName = $"'{smallGroupName}'";
            smallGroupId = $"'{{{smallGroupId}}}'";
            string sundayDateString = $"'{sundayDate:yyyy-M-d}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_present_record'>
                            <attribute name='new_present_recordid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_list_new_present_record' operator='eq' uiname={smallGroupName} uitype='list' value={smallGroupId} />
                              <condition attribute='new_contact_new_present_record' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                              <condition attribute='new_sunday_date' operator='on' value={sundayDateString} />
                            </filter>
                          </entity>
                        </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }
    }
}

```

## 實際TRX機器證據（不含測試本文與個資）
artifacts/p1-04-review-followup-red/review-red.trx SHA256=76bb2a525e714fb07f879b5960a8e670761f64c88917204f4d51685a70614799
<ns0:Counters xmlns:ns0="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" total="22" executed="22" passed="11" failed="11" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
    
artifacts/p1-04-review-followup-green/review-green.trx SHA256=76560039e5dd50dc4d3002387d06252cf7d7b7b453161b013543de8fe2c1d35b
<ns0:Counters xmlns:ns0="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" total="93" executed="93" passed="93" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
    
artifacts/p1-04-operations-before/before.trx SHA256=c8191717f360d564cd275491bf8d3c1f4e47b187165ce9cdd26591e9bc1443e7
<ns0:Counters xmlns:ns0="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" total="13" executed="13" passed="13" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
    
artifacts/p1-04-operations-red/safety-red.trx SHA256=1286546a048ed06f970fdbdeb689898d37b1765f7674c0dfb8a66668cbbb9a68
<ns0:Counters xmlns:ns0="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" total="99" executed="99" passed="18" failed="81" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
    
artifacts/p1-04-operations-safe-before-move/before-move-green.trx SHA256=2fdc71a9b28c7b889cbe0f2f1d7ac56e7753b974ad57cb1cfed449952068d80a
<ns0:Counters xmlns:ns0="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" total="112" executed="112" passed="112" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
    
artifacts/p1-04-operations-dual/net10.0/operations.trx SHA256=f19fbf7aa6786fed773e594418d9a0c5249431d42cf91d87cacb145932bcb8c2
<ns0:Counters xmlns:ns0="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" total="282" executed="282" passed="282" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
    
artifacts/p1-04-operations-dual/net48/operations.trx SHA256=fab53ac1e219c02ea25e03a9cefbee12f8ca3c1dfa0e4d156dbf8bf191c9e601
<ns0:Counters xmlns:ns0="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" total="207" executed="207" passed="207" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
    

## p1-04-operations-core-strict.log
  ToolUtility.Core -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree\ToolUtility.Core\bin\Release\net462\ToolUtility.Core.dll
  ToolUtility.Core -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree\ToolUtility.Core\bin\Release\net10.0\ToolUtility.Core.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.01


## p1-04-operations-coverage-net48.log
Coverage ToolUtility.Core: 98.78%


## p1-04-operations-coverage-net10.0.log
Coverage ToolUtility.Core: 100.00%


## p1-04-package-guard.log
Package guard: projects=34 references=158 excluded=2 failures=0


## p1-04-analyzer-probes.log
Analyzer probe: checks=26 failures=0 buildExit=1 errors=RS0030


## p1-04-Test-CiTransition.Tests.ps1.log
PASS Filter_ExcludesExactlyDependencyClosure
PASS Coverage_IncludesBothCoreProductTargets
PASS Commands_UseFilteredSolution
PASS Tracer_DisabledButRetainsExecutableGuard
PASS Summary_DisclosesAllExcludedProjects
PASS NugetConfig_DoesNotDeclareMachineSpecificSource
CI transition tests: 6/6



## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
