/*
 * 檔案整體說明
 *
 * 目的與定位：聯絡人純資料操作與舊資訊格式；所有查詢只借用已授權scope的SDK，不以LINE／姓名／帳號條件取代授權。 不保存logger、cache、Entity、查詢值或credential，不建立連線、
 * Task.Run、timer、queue或取消registration。
 *
 * 主要功能與流程：
 * 本檔宣告 ContactDataService。
 * - 相容logger參數僅驗非null、不保存／反射；SDK限原授權scope借用，呼叫端擁有Dispose。
 * - 依原客戶編號查第一筆啟用資料；查無null，不以外部編號作正式資料列ID或身份授權。
 * - 保留外借SDK與ref筆數入口；先驗服務存活，null外借SDK保留舊null/count=0，不接管其生命週期。
 * - 依LINE綁定等值與啟用條件查第一筆，不快取／建立登入狀態，回應只屬此次caller。
 * 主要操作入口包含 RetrieveByContactId、RetrieveByLineId、RetrieveByLineIdForCollection、RetrieveCollectionByLineId、
 * RetrieveCollectionByName、RetrieveCollectionByFullName、RetrieveCollectionByNationId、RetrieveByFullName；
 * 其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.DataOperations;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.ContactOperations
{
    /// <summary>
    /// 聯絡人純資料操作與舊資訊格式；所有查詢只借用已授權scope的SDK，不以LINE／姓名／帳號條件取代授權。
    /// 不保存logger、cache、Entity、查詢值或credential，不建立連線、Task.Run、timer、queue或取消registration。
    /// </summary>
    /// <remarks>
    /// 單筆保留Top=1與AllColumns，全部候選驗正式ID／表／重複／上限後才發布。無top集合完整有界分頁，
    /// 最多5000列／1000頁／64Ki字元cookie；完整候選失敗直接拋原Exception，由最終host共用告警。
    /// 同步SDK只在caller執行緒執行，host擁有傳輸timeout與並行上限；Task包裝只區分完成、fault及明確caller取消。
    /// SDK不轉移所有權，Dispose停止新入口並清參照；已取得操作局部SDK的呼叫可drain，host先排空再關SDK。
    /// 固定legacy資源只含八個公開文案／提示，不可變編譯表不存個資、查詢或結果；不設定全域CultureInfo。
    /// </remarks>
    public class ContactDataService : IContactService, IDisposable
    {
        // 私有唯讀模板只有固定結構名稱；AddRange逐次複製，不讓SDK或caller取得可變模板。
        private static readonly ReadOnlyCollection<string> CustomerFields = Array.AsReadOnly(new[] { "build_customer_id" });
        private static readonly ReadOnlyCollection<string> LineFields = Array.AsReadOnly(new[] { "new_lineid" });
        private static readonly ReadOnlyCollection<string> NameFields = Array.AsReadOnly(new[] { "fullname" });
        private static readonly ReadOnlyCollection<string> NameMobileFields = Array.AsReadOnly(new[] { "fullname", "mobilephone" });
        private static readonly ReadOnlyCollection<string> AccountFields = Array.AsReadOnly(new[] { "new_app_acount" });
        private static readonly ReadOnlyCollection<string> DedicationFields = Array.AsReadOnly(new[] { "fullname", "telephone2", "address2_line1", "parentcustomerid", "new_church_jobtitle", "mobilephone", "emailaddress1", "pager", "new_cell_list_contact", "new_personal_id", "new_last_six_digit", "contactid" });
        private static readonly ReadOnlyCollection<string> PrefixFields = Array.AsReadOnly(new[] { "fullname", "pager", "telephone2", "address2_line1", "parentcustomerid", "new_church_jobtitle", "mobilephone", "emailaddress1", "contactid" });
        private const int MaximumRows = 5000;
        private const int MaximumPages = 1000;
        private const int MaximumCookieCharacters = 65536;
        private static readonly ReadOnlyDictionary<string, string> LegacyText = CreateText();
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>相容logger參數僅驗非null、不保存／反射；SDK限原授權scope借用，呼叫端擁有Dispose。</summary>
        /// <param name="logger">僅為相容的host診斷參數，不可藉此把個資帶入核心。</param>
        /// <param name="organizationService">同一已授權scope的SDK，不跨使用者／租戶共用此服務。</param>
        public ContactDataService(object logger, IOrganizationService organizationService)
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

        /// <summary>依原客戶編號查第一筆啟用資料；查無null，不以外部編號作正式資料列ID或身份授權。</summary>
        public Entity? RetrieveByContactId(string contactId) => Single(GetService(), CustomerFields, new object[] { contactId });

        /// <summary>保留外借SDK與ref筆數入口；先驗服務存活，null外借SDK保留舊null/count=0，不接管其生命週期。</summary>
        public Entity? RetrieveByContactId(IOrganizationService? externalService, string contactId, ref int count)
        {
            GetService();
            var row = externalService == null ? null : Single(externalService, CustomerFields, new object[] { contactId });
            count = row == null ? 0 : 1;
            return row;
        }

        /// <summary>依LINE綁定等值與啟用條件查第一筆，不快取／建立登入狀態，回應只屬此次caller。</summary>
        public Entity? RetrieveByLineId(string lineId) => Single(GetService(), LineFields, new object[] { lineId });

        /// <summary>保留歷史拼字與單筆實際語義；此別名不是完整集合入口。</summary>
        public Entity? RetrieveByLineIdForCollection(string lineId) => RetrieveByLineId(lineId);

        /// <summary>完整取得本SDK授權scope內的LINE等值集合；同名異ID保留，跨頁衝突／故障拒絕發布。</summary>
        public EntityCollection RetrieveCollectionByLineId(string lineId) => All(GetService(), "new_lineid", lineId);

        /// <summary>姓名只是條件而非身份；無top集合完整有界讀取，不按姓名合併合法資料列。</summary>
        public EntityCollection RetrieveCollectionByName(string contactFullName) => All(GetService(), "fullname", contactFullName);

        /// <summary>與姓名集合使用同一完整查詢，保留公開別名，不維持第二套資料邏輯或共享結果。</summary>
        public EntityCollection RetrieveCollectionByFullName(string fullName) => RetrieveCollectionByName(fullName);

        /// <summary>敏感識別值只活於操作局部查詢；完整驗頁後回傳，禁止記錄／cache或按內容去重。</summary>
        public EntityCollection RetrieveCollectionByNationId(string nationId) => All(GetService(), "new_personal_id", nationId);

        /// <summary>保留姓名第一筆語義；同名不代表同一身份，登入與完整顯示不可依賴此入口。</summary>
        public Entity? RetrieveByFullName(string fullName) => Single(GetService(), NameFields, new object[] { fullName });

        /// <summary>保留外部SDK多載的null語義；仍先驗本服務未Dispose，借入SDK不被保存／處置。</summary>
        public Entity? RetrieveByFullName(IOrganizationService? externalService, string fullName)
        {
            GetService();
            return externalService == null ? null : Single(externalService, NameFields, new object[] { fullName });
        }

        /// <summary>保留姓名與手機兩個AND條件與第一筆；值不是授權，不能以顯示內容作正式身份。</summary>
        public Entity? RetrieveByFullNameAndMobile(string fullName, string mobileNumber) =>
            Single(GetService(), NameMobileFields, new object[] { fullName, mobileNumber });

        /// <summary>只查帳號當前資料，不證明密碼已核對；SDK結果只屬此操作，不建立身份快取。</summary>
        public Entity? RetrieveAccountEntity(string accountNumber) =>
            Single(GetService(), AccountFields, new object[] { accountNumber });

        /// <summary>
        /// 每次核對當前SDK資料，不讀寫任何cache或保留成功身份；缺失／空密碼為業務拒絕，不進SDK。
        /// stored secret必須非空且Ordinal相等；不因兩個null／空值而放行未設定密碼的帳號。
        /// </summary>
        public Entity? RetrieveByAccountNumber(string accountNumber, string password)
        {
            var sdk = GetService();
            if (string.IsNullOrEmpty(password)) return null;
            var row = Single(sdk, AccountFields, new object[] { accountNumber });
            var stored = row?.GetAttributeValue<string>("new_app_pass");
            return !string.IsNullOrEmpty(stored) && string.Equals(stored, password, StringComparison.Ordinal) ? row : null;
        }

        /// <summary>維持舊成功GUID與三種失敗文案；結果只供原caller，不簽發票證、不寫Session或診斷。</summary>
        public string AccountLogin(string accountNumber, string password)
        {
            var row = RetrieveByAccountNumber(accountNumber, password);
            if (row != null) return row.Id.ToString();
            var exists = RetrieveAccountEntity(accountNumber);
            return Text(exists == null ? "AccountMissing" : !exists.Contains("new_app_pass") ? "PasswordUnset" : "PasswordIncorrect");
        }

        /// <summary>保留原客戶編號資訊格式；個資字串僅屬caller，空頁回空字串，故障不能轉成空。</summary>
        public string GetContactInfoByContactId(string contactId) => FormatContactInfo(RetrieveByContactId(contactId));

        /// <summary>姓名資訊沿用同一局部formatter；此顯示內容不作身份、快取鍵或通知內容。</summary>
        public string GetContactInfoByFullName(string fullName) => FormatContactInfo(RetrieveByFullName(fullName));

        /// <summary>外借SDK只用於此次查詢；null保留空資訊，Dispose後連外借入口也必須拒絕。</summary>
        public string GetContactInfoByFullName(IOrganizationService? externalService, string fullName) =>
            FormatContactInfo(RetrieveByFullName(externalService, fullName));

        /// <summary>直接包裝同一LINE同步查詢；取消前後檢查，不復用不完整隔離鍵的舊host cache。</summary>
        public Task<Entity?> RetrieveByLineIdAsync(string lineId, CancellationToken cancellationToken = default) =>
            ExecuteAsync(() => RetrieveByLineId(lineId), cancellationToken);

        /// <summary>每次以當前密碼核對；Task不排程背景工作或持有取消registration，真正故障原樣交host。</summary>
        public Task<Entity?> RetrieveByAccountNumberAsync(string accountNumber, string password, CancellationToken cancellationToken = default) =>
            ExecuteAsync(() => RetrieveByAccountNumber(accountNumber, password), cancellationToken);

        /// <summary>姓名查詢不使用跨scope／可變Entity快取；同一caller取消不能變成另一次呼叫的狀態。</summary>
        public Task<Entity?> RetrieveByFullNameAsync(string fullName, CancellationToken cancellationToken = default) =>
            ExecuteAsync(() => RetrieveByFullName(fullName), cancellationToken);

        /// <summary>
        /// 保留六項OR條件、兩個狀態AND及top100；值只能進XML屬性，提示／空條件略過，不發無條件查詢。
        /// Execute回應完整驗證後發布，不吞故障、不反射logger，沒有候選或原XML留在服務狀態。
        /// </summary>
        public EntityCollection QueryDediccationContatsByFetchXml(string dedicationNumber, string contactName,
            string homePhone, string mobile, string nationId, string lastSixDigit)
        {
            var sdk = GetService();
            var filter = new XElement("filter", new XAttribute("type", "or"));
            var values = new[] { dedicationNumber, contactName, homePhone, mobile, nationId, lastSixDigit };
            var fields = new[] { "pager", "fullname", "telephone2", "mobilephone", "new_personal_id", "new_last_six_digit" };
            for (var index = 0; index < values.Length; index++)
                if (!string.IsNullOrWhiteSpace(values[index]) && !values[index].StartsWith(Text("InputPrompt"), StringComparison.Ordinal))
                    filter.Add(SdkDataQuery.Condition(fields[index], index == 0 ? "eq" : "like",
                        index == 0 ? values[index] : "%" + values[index] + "%"));
            if (!filter.HasElements) return new EntityCollection();
            var entity = SdkDataQuery.Entity("contact", DedicationFields, "fullname");
            entity.Add(SdkDataQuery.Filter(filter, SdkDataQuery.Condition("statuscode", "eq", "1"), SdkDataQuery.Condition("statecode", "eq", "0")));
            return Fetch(sdk, entity, 100);
        }

        /// <summary>保留前綴like、top3及pager反向排序；前綴安全編碼，不拼接引號／節點，不保存個資。</summary>
        public EntityCollection QueryContatsByStartedDedicationNumber(string dedicationStartNumber)
        {
            var sdk = GetService();
            var entity = SdkDataQuery.Entity("contact", PrefixFields, "pager", true);
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Condition("pager", "like", dedicationStartNumber + "%"),
                SdkDataQuery.Condition("statecode", "eq", "0")));
            return Fetch(sdk, entity, 3);
        }

        /// <summary>所有typed單筆共用原QueryByAttribute形狀；在取第一筆前驗整頁／top，錯誤不能假裝查無。</summary>
        private static Entity? Single(IOrganizationService sdk, IEnumerable<string> fields, object[] values)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true), TopCount = 1 };
            query.Attributes.AddRange(fields); query.Attributes.Add("statecode");
            query.Values.AddRange(values); query.Values.Add(0);
            var page = SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), "contact");
            if (page.Entities.Count > 1) throw new InvalidOperationException("The SDK exceeded the requested row budget.");
            return page.Entities.Count == 0 ? null : page.Entities[0];
        }

        /// <summary>
        /// 保留typed查詢及啟用條件，添加正式ID穩定排序與有限續頁；局部candidate完成才發布。
        /// 任何後頁故障、重複ID或預算超量丟棄整個candidate，caller明確重試；不無界重試或靜默Distinct。
        /// </summary>
        private static EntityCollection All(IOrganizationService sdk, string field, string value)
        {
            var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true), PageInfo = new PagingInfo { Count = MaximumRows, PageNumber = 1 } };
            query.Attributes.AddRange(field, "statecode"); query.Values.AddRange(value, 0);
            query.Orders.Add(new OrderExpression("contactid", OrderType.Ascending));
            var candidate = new EntityCollection(); var ids = new HashSet<Guid>();
            for (var number = 1; number <= MaximumPages; number++)
            {
                query.PageInfo.PageNumber = number;
                var page = SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), "contact");
                if (page.Entities.Count > MaximumRows || page.MoreRecords && page.Entities.Count == 0)
                    throw new InvalidOperationException("The SDK returned an invalid query page.");
                foreach (var row in page.Entities)
                {
                    if (!ids.Add(row.Id) || ids.Count > MaximumRows)
                        throw new InvalidOperationException("Query output has conflicting identities or exceeds the local row budget.");
                    candidate.Entities.Add(row);
                }
                candidate.TotalRecordCount = page.TotalRecordCount;
                candidate.TotalRecordCountLimitExceeded = page.TotalRecordCountLimitExceeded;
                candidate.EntityName = page.EntityName;
                candidate.MinActiveRowVersion = page.MinActiveRowVersion;
                if (!page.MoreRecords) return candidate;
                if (ids.Count >= MaximumRows || page.PagingCookie?.Length > MaximumCookieCharacters)
                    throw new InvalidOperationException("Query paging exceeds the local resource budget.");
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
            throw new InvalidOperationException("Query paging exceeds the local page budget.");
        }

        /// <summary>復用唯一Execute根表／正式ID驗證；top預算也在SDK回應後強制，不將單頁冒稱完整全表。</summary>
        private static EntityCollection Fetch(IOrganizationService sdk, XElement entity, int top)
        {
            var xml = new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("output-format", "xml-platform"),
                new XAttribute("mapping", "logical"), new XAttribute("distinct", "false"), new XAttribute("top", top), entity);
            using var reader = new EntityQueryService(organizationService: sdk);
            var page = reader.ExecuteRetrieveMultiple(new RetrieveMultipleRequest { Query = new FetchExpression(xml.ToString(SaveOptions.DisableFormatting)) });
            if (page.Entities.Count > top) throw new InvalidOperationException("The SDK exceeded the requested row budget.");
            return page;
        }

        /// <summary>固定舊標籤從中立資源讀，不依ambient culture改格式；輸出只在方法局部建構、不記錄或保存。</summary>
        private static string FormatContactInfo(Entity? row)
        {
            if (row == null) return string.Empty;
            var text = new StringBuilder();
            var fields = new[] { "fullname", "build_customer_id", "telephone1", "emailaddress1" };
            var labels = new[] { "NameLabel", "CustomerLabel", "PhoneLabel", "EmailLabel" };
            for (var index = 0; index < fields.Length; index++)
                if (row.Contains(fields[index])) text.AppendLine(Text(labels[index]) + row[fields[index]]);
            return text.ToString();
        }

        /// <summary>只讀八個固定legacy文案；缺資源明確失敗，不按語系／caller輸入動態建立無界key。</summary>
        private static string Text(string key) => LegacyText.TryGetValue("Contact.Legacy." + key, out var value)
            ? value : throw new InvalidOperationException("A required legacy resource is missing.");

        /// <summary>從正式I18n產生表逐列複製中立文案成私有唯讀表；只讀固定八鍵，不公開可變Rows或衛星資源依賴。</summary>
        private static ReadOnlyDictionary<string, string> CreateText()
        {
            var text = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in ContactLegacyData.Rows) text.Add(row[0]!, row[1]!);
            return new ReadOnlyDictionary<string, string>(text);
        }

        /// <summary>同步SDK不Task.Run；只有明確caller token取消才回canceled，內部timeout保留原fault與型別。</summary>
        private static Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);
            try { var result = operation(); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(result); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Task.FromCanceled<T>(cancellationToken); }
            catch (Exception failure) { return Task.FromException<T>(failure); }
        }

        /// <summary>取得單次操作借用；Dispose競爭不關既有I/O，新入口停止，不以全域鎖序列化跨使用者查詢。</summary>
        private IOrganizationService GetService()
        {
            var sdk = Volatile.Read(ref _organizationService);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ContactDataService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return sdk ?? throw new ObjectDisposedException(nameof(ContactDataService));
        }

        /// <summary>標準可覆寫Dispose hook；冪等清除唯一借用參照，沒有自有連線／背景資源可關閉。</summary>
        /// <param name="disposing">保留標準模式；不接管host SDK所有權。</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Exchange(ref _organizationService, null);
        }

        /// <summary>停止新入口並清借用參照；host須先drain既有同步操作再處置SDK，不由本方法關連線。</summary>
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    }
}
