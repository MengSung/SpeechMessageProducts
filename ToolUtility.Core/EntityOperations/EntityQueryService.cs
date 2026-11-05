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
            if (service.Execute(request) is not RetrieveMultipleResponse response)
                throw new InvalidOperationException("The SDK returned an invalid query response.");
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
