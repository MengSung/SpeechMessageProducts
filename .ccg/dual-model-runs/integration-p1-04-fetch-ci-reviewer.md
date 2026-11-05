# CCG reviewer Task: integration-p1-04-fetch-ci

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
P1-04後續唯讀審查，禁止擴讀其他檔案或任何含憑證的Facade。請只按本提示固定快照審查，不執行、不修改檔案。
前輪20261105-132016 dualModelComplete=true，無Critical/High；Claude W1 FetchExpression未驗根實體及W2反向日期未測已採納。新測試先11失敗/11通過，證據artifacts/p1-04-review-followup-red/；修補後Fee及Entity定向93/93，雙宿主與完整回歸待跑，不代表P1完成（0/12）。
請查核XML安全/相容/借入SDK所有權/固定外層錯誤，以及CI是否精確保留原三排除、新Core及tests與各目標coverage，新增AST測試有無放寬斷言。輸出Critical/High/Warning/Info，具體檔案行/失敗路徑；無執行證據不得猜通過。根實體驗證不是授權，既有aggregate空ID已被前版拒絕；保持SDK query原樣不重寫。Core net462/net10 strict，不新增NoWarn。

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
                if (entityCount != 1 || string.IsNullOrEmpty(entityName))
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

## .github/workflows/solution-ci.yml
```
name: Solution CI

on:
  push:
  pull_request:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  solution:
    runs-on: windows-latest
    timeout-minutes: 45
    defaults:
      run:
        shell: powershell
    env:
      DOTNET_CLI_UI_LANGUAGE: en-US
      VSLANG: '1033'
      BASE_COMMIT: 73b6e6cf409daa7559fd3c11d482e3efc542c0d7
      SixLaborsLicenseFile: ''
    steps:
      - name: Checkout full history
        uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - name: Select SDK from global.json
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Select Node for progress page tests
        uses: actions/setup-node@v4
        with:
          node-version: '22'
      - name: Prepare Six Labors license
        env:
          SIXLABORS_LICENSE: ${{ secrets.SIXLABORS_LICENSE }}
        run: |
          $ErrorActionPreference = 'Stop'
          # 使用者核定的一般金鑰只經步驟環境傳入，不插入腳本文字，也不輸出內容。
          if ([string]::IsNullOrWhiteSpace($env:SIXLABORS_LICENSE)) {
            $env:SIXLABORS_LICENSE = $null
            throw 'SIXLABORS_LICENSE is required for Solution CI.'
          }
          $created = $false
          $licensePath = $null
          try {
            if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or [string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
              throw 'Runner environment is unavailable.'
            }
            $licensePath = Join-Path $env:RUNNER_TEMP ('speechmessage-sixlabors-' + [Guid]::NewGuid().ToString('N') + '.lic')
            # CreateNew 不覆寫既有檔。單一步驟擁有 stream，寫入故障與成功均先關閉才進行清理。
            $stream = [IO.File]::Open($licensePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $created = $true
            try {
              $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false), 1024, $true)
              try { $writer.Write($env:SIXLABORS_LICENSE); $writer.Flush() }
              finally { $writer.Dispose() }
            } finally { $stream.Dispose() }
            # 後續建置只取得 runner 路徑；不把內容寫到 GITHUB_ENV、摘要或 artifact。
            [IO.File]::AppendAllText($env:GITHUB_ENV, "SixLaborsLicenseFile=$licensePath`n", [Text.UTF8Encoding]::new($false))
          } catch {
            if ($created -and [IO.File]::Exists($licensePath)) { Remove-Item -LiteralPath $licensePath -Force }
            throw 'Six Labors license preparation failed.'
          } finally { $env:SIXLABORS_LICENSE = $null }
      - name: Restore
        run: |
          dotnet restore SpeechMessageProducts.CI.slnf
          if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }
          foreach ($project in @('tools/I18nCheck.Tests', 'tools/PluginPackaging.Tests')) {
            dotnet restore $project
            if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
          }
      - name: Build without signing key
        run: |
          # 一般 Release 建置不讀金鑰、不打包；只有獨立正式打包步驟可以取得 secret。
          dotnet build SpeechMessageProducts.CI.slnf -c Release --no-restore -warnaserror:NU1605
          if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
          foreach ($project in @('tools/I18nCheck.Tests', 'tools/PluginPackaging.Tests')) {
            dotnet build $project -c Release --no-restore -warnaserror:NU1605
            if ($LASTEXITCODE -ne 0) { throw 'Tool build failed.' }
          }
      - name: Release solution tests and coverage
        run: |
          dotnet test SpeechMessageProducts.CI.slnf -c Release --no-build --collect:"XPlat Code Coverage" --results-directory artifacts/ci/solution-tests
          if ($LASTEXITCODE -ne 0) { throw 'Release solution tests failed.' }
      - name: Debug tracer contracts
        # 過渡期間依賴被排除的 ChurchReport；DevExtreme 來源核定並恢復完整 CI 後移除此條件。
        if: ${{ false }}
        run: |
          $tracerResults = 'artifacts/ci/debug-tracer'
          if (Test-Path -LiteralPath $tracerResults) { throw 'Tracer results must be fresh.' }
          dotnet test ToolUtility.Dataverse.Tests -c Debug --filter "FullyQualifiedName~FileToolUtilityTracerTests" --logger "trx;LogFileName=tracer.trx" --results-directory $tracerResults
          if ($LASTEXITCODE -ne 0) { throw 'Debug tracer tests failed.' }
          powershell -NoProfile -File tools/RepoQuality/Test-TrxExecution.ps1 -ReportPath (Join-Path $tracerResults 'tracer.trx')
          if ($LASTEXITCODE -ne 0) { throw 'Debug tracer tests were not actually executed successfully.' }
      - name: Coverage for each new module and target
        run: |
          # 各目標分開收集，禁止 net10 報告或其他測試的相依模組掩蓋 net48 的缺口。
          $cases = @(
            @{ Project='SpeechMessage.Localization.Tests'; Framework='net48'; Module='SpeechMessage.Localization' },
            @{ Project='SpeechMessage.Localization.Tests'; Framework='net10.0'; Module='SpeechMessage.Localization' },
            @{ Project='ToolUtility.Tests'; Framework='net48'; Module='ToolUtility.Core' },
            @{ Project='ToolUtility.Tests'; Framework='net10.0'; Module='ToolUtility.Core' },
            @{ Project='LineMessagingProcessor.Tests'; Framework='net48'; Module='LineMessagingProcessor.Core' },
            @{ Project='LineMessagingProcessor.Tests'; Framework='net10.0'; Module='LineMessagingProcessor.Core' },
            @{ Project='SpeechMessage.Dynamics.Plugins.Tests'; Framework='net48'; Module='SpeechMessage.Dynamics.Plugins' },
            @{ Project='SpeechMessage.Dynamics.Deployment.Tests'; Framework='net10.0'; Module='SpeechMessage.Dynamics.Deployment' },
            @{ Project='tools/I18nCheck.Tests'; Framework='net10.0'; Module='I18nCheck' },
            @{ Project='tools/PluginPackaging.Tests'; Framework='net10.0'; Module='PluginPackaging' }
          )
          foreach ($case in $cases) {
            $results = Join-Path 'artifacts/ci/coverage' ($case.Module + '/' + $case.Framework)
            if (Test-Path -LiteralPath $results) { throw 'Coverage output must be fresh for every run.' }
            $include = 'DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=[' + $case.Module + ']*'
            dotnet test $case.Project -c Release -f $case.Framework --no-build --collect:"XPlat Code Coverage" --results-directory $results -- $include
            if ($LASTEXITCODE -ne 0) { throw 'Module coverage tests failed.' }
            $reports = @(Get-ChildItem -LiteralPath $results -Filter coverage.cobertura.xml -File -Recurse)
            if ($reports.Count -ne 1) { throw 'Expected exactly one fresh report for this module and target.' }
            $coverage = & powershell -NoProfile -File tools/RepoQuality/Test-Coverage.ps1 -ReportPath $reports[0].FullName -Module $case.Module
            if ($LASTEXITCODE -ne 0) { throw 'Module coverage is below the required threshold or invalid.' }
            $coverage | Write-Output
            ($case.Framework + ' ' + ($coverage -join ' ')) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
          }
      - name: Repository quality gates
        run: |
          powershell -NoProfile -File tools/RepoQuality/Test-TextEncoding.ps1 -Scope Changed -Base $env:BASE_COMMIT
          if ($LASTEXITCODE -ne 0) { throw 'Text encoding failed.' }
          powershell -NoProfile -File tools/RepoQuality/Find-DuplicateCode.ps1 -Baseline tools/RepoQuality/duplicate-baseline.json
          if ($LASTEXITCODE -ne 0) { throw 'Duplicate source ratchet failed.' }
          powershell -NoProfile -File tools/RepoQuality/Test-PackageVersions.ps1
          if ($LASTEXITCODE -ne 0) { throw 'Package version guard failed.' }
          powershell -NoProfile -File tools/RepoQuality/Test-AnalyzerProbe.ps1
          if ($LASTEXITCODE -ne 0) { throw 'Analyzer probes failed.' }
          dotnet run --project tools/I18nCheck -- verify
          if ($LASTEXITCODE -ne 0) { throw 'I18n verification failed.' }
      - name: Repository quality tests
        run: |
          Import-Module Pester -RequiredVersion 3.4.0 -ErrorAction Stop
          $result = Invoke-Pester -Script @('tools/RepoQuality/Tests/Test-TextEncoding.Tests.ps1', 'tools/RepoQuality/Tests/Find-DuplicateCode.Tests.ps1') -PassThru
          if ($result.TotalCount -lt 35 -or $result.FailedCount -ne 0) { throw 'Repository quality tests failed or were not discovered.' }
          powershell -NoProfile -File tools/RepoQuality/Tests/Test-P002Settings.ps1
          if ($LASTEXITCODE -ne 0) { throw 'SDK and ignore settings failed.' }
          powershell -NoProfile -File tools/RepoQuality/Tests/Test-Coverage.Tests.ps1
          if ($LASTEXITCODE -ne 0) { throw 'Coverage gate tests failed.' }
          powershell -NoProfile -File tools/RepoQuality/Tests/Test-PackageVersions.Tests.ps1
          if ($LASTEXITCODE -ne 0) { throw 'Package path boundary contracts failed.' }
          powershell -NoProfile -File tools/RepoQuality/Tests/Test-CiTransition.Tests.ps1
          if ($LASTEXITCODE -ne 0) { throw 'CI transition scope contracts failed.' }
          powershell -NoProfile -File tools/RepoQuality/Tests/Test-TracerCi.Tests.ps1
          if ($LASTEXITCODE -ne 0) { throw 'Tracer CI contracts failed.' }
          powershell -NoProfile -File tools/RepoQuality/Tests/Test-SixLaborsCiLicense.Tests.ps1
          if ($LASTEXITCODE -ne 0) { throw 'Six Labors CI license contracts failed.' }
          node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs
          if ($LASTEXITCODE -ne 0) { throw 'Progress page tests failed.' }
          # 直接驗證實際 Razor adapter 與既有 jQuery Deferred，避免只跑伺服器測試卻漏掉表格初始化錯誤。
          node --test ChurchReport.MemberInfo.Tests/JavaScript/collection-load-coordinator.test.js ChurchReport.MemberInfo.Tests/JavaScript/weekly-report-load-adapter.test.js
          if ($LASTEXITCODE -ne 0) { throw 'Weekly report JavaScript contracts failed.' }
          powershell -NoProfile -File docs/scripts/Tests/Test-CcgReviewSummary.ps1
          if ($LASTEXITCODE -ne 0) { throw 'CCG summary contracts failed.' }
      - name: Offline plugin packaging tests
        run: |
          # 此處只建立與銷毀一次性測試金鑰，從不需要 GitHub 正式 secret。
          powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
          if ($LASTEXITCODE -ne 0) { throw 'Plugin metadata samples failed.' }
          powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1 -UseEphemeralTestKey
          if ($LASTEXITCODE -ne 0) { throw 'Plugin build samples failed.' }
      - name: Prepare optional signing key
        id: signing
        env:
          PLUGIN_SNK_BASE64: ${{ secrets.PLUGIN_SNK_BASE64 }}
        run: |
          # 無 secret 時只標示跳過，不能影響一般建置；本計畫不要求建立 GitHub secret。
          if ([string]::IsNullOrWhiteSpace($env:PLUGIN_SNK_BASE64)) {
            'Plugin packaging: skipped (PLUGIN_SNK_BASE64 is not configured).' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
            'enabled=false' | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
          } else {
            # 僅在 runner 暫存目錄還原；內容不輸出、不上傳。清理步驟以固定路徑獨立執行。
            $keyPath = Join-Path $env:RUNNER_TEMP 'speechmessage-plugin-signing.snk'
            if (Test-Path -LiteralPath $keyPath) { throw 'Signing key destination is unexpectedly occupied.' }
            $bytes = $null
            try {
              $bytes = [Convert]::FromBase64String($env:PLUGIN_SNK_BASE64)
              [IO.File]::WriteAllBytes($keyPath, $bytes)
            } finally {
              if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) }
              $env:PLUGIN_SNK_BASE64 = $null
            }
            'enabled=true' | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
          }
      - name: Explicit signed plugin packaging
        if: steps.signing.outputs.enabled == 'true'
        run: |
          $env:SPEECHMESSAGE_PLUGIN_KEYFILE = Join-Path $env:RUNNER_TEMP 'speechmessage-plugin-signing.snk'
          try {
            dotnet build SpeechMessage.Dynamics.Plugins -c Release -p:SpeechMessagePackagePlugin=true
            if ($LASTEXITCODE -ne 0) { throw 'Explicit plugin packaging failed.' }
            powershell -NoProfile -File tools/PluginPackaging/Test-PluginAssembly.ps1
            if ($LASTEXITCODE -ne 0) { throw 'Signed plugin assembly validation failed.' }
            'Plugin packaging: signed assembly validation passed.' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
          } finally { $env:SPEECHMESSAGE_PLUGIN_KEYFILE = $null }
      - name: Delete optional signing key
        if: always()
        run: |
          # 單一固定暫存檔，禁止遞迴刪除，也不接收外部指定路徑。
          $keyPath = Join-Path $env:RUNNER_TEMP 'speechmessage-plugin-signing.snk'
          if (Test-Path -LiteralPath $keyPath) { Remove-Item -LiteralPath $keyPath -Force }
      - name: Delete Six Labors license
        if: always()
        run: |
          $ErrorActionPreference = 'Stop'
          try {
            if (-not [string]::IsNullOrWhiteSpace($env:SixLaborsLicenseFile)) {
              # 只清除本步驟發布於 runner 根目錄的特定格式檔名；不得依任意路徑刪檔。
              $runnerRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\', '/')
              $licensePath = [IO.Path]::GetFullPath($env:SixLaborsLicenseFile)
              if (-not [string]::Equals([IO.Path]::GetDirectoryName($licensePath), $runnerRoot, [StringComparison]::OrdinalIgnoreCase) -or
                  [IO.Path]::GetFileName($licensePath) -cnotmatch '^speechmessage-sixlabors-[0-9a-f]{32}\.lic$') {
                throw 'Refusing to delete an unowned license path.'
              }
              if ([IO.File]::Exists($licensePath)) { Remove-Item -LiteralPath $licensePath -Force }
            }
          } finally { $env:SixLaborsLicenseFile = $null }
      - name: CI transition scope
        if: always()
        run: |
          $ErrorActionPreference = 'Stop'
          # 摘要直接依篩選檔列出省略專案，避免維護兩份排除清單造成假綠燈。
          $filter = Get-Content -LiteralPath SpeechMessageProducts.CI.slnf -Raw | ConvertFrom-Json
          $included = @($filter.solution.projects | ForEach-Object { $_.Replace('\', '/') })
          $solution = Get-Content -LiteralPath $filter.solution.path -Raw
          $all = @([regex]::Matches($solution, '(?m)^Project\([^\r\n]+?= "[^"]+", "(?<path>[^"]+\.csproj)"') | ForEach-Object { $_.Groups['path'].Value.Replace('\', '/') })
          $excluded = @($all | Where-Object { $included -notcontains $_ } | Sort-Object)
          $lines = @('### Transitional CI scope', '', 'This is not a full solution verification.', 'DevExtreme 23.1.5 source is pending user confirmation. Excluded projects:')
          $lines += @($excluded | ForEach-Object { '- ' + $_ })
          $lines += @('', 'Debug tracer is temporarily disabled because it depends on ChurchReport.', 'Restore full CI and tracer after the user approves a DevExtreme source. Local validation still covers the full solution.')
          $lines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
      - name: Upload coverage reports
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: coverage-reports
          path: artifacts/ci/**/coverage.cobertura.xml
          if-no-files-found: warn
          retention-days: 14

```

## SpeechMessageProducts.CI.slnf
```
{
  "solution": {
    "path": "SpeechMessageProducts.sln",
    "projects": [
      "Line.Messaging.Tests\\Line.Messaging.Tests.csproj",
      "Line.Messaging\\Line.Messaging.csproj",
      "LineMessagingProcessor.AspNetCore.Tests\\LineMessagingProcessor.AspNetCore.Tests.csproj",
      "LineMessagingProcessor.AspNetCore\\LineMessagingProcessor.AspNetCore.csproj",
      "LineMessagingProcessor.Core\\LineMessagingProcessor.Core.csproj",
      "LineMessagingProcessor.RichMenus.Tests\\LineMessagingProcessor.RichMenus.Tests.csproj",
      "LineMessagingProcessor.RichMenus\\LineMessagingProcessor.RichMenus.csproj",
      "LineMessagingProcessor.Tests\\LineMessagingProcessor.Tests.csproj",
      "LineMessagingProcessor.Workflows.Tests\\LineMessagingProcessor.Workflows.Tests.csproj",
      "LineMessagingProcessor.Workflows\\LineMessagingProcessor.Workflows.csproj",
      "LineMessagingProcessor\\LineMessagingProcessor.csproj",
      "PowerPlatform.Dataverse.Client\\PowerPlatform.Dataverse.Client.csproj",
      "SpeechMessage.Dynamics.Deployment.Tests\\SpeechMessage.Dynamics.Deployment.Tests.csproj",
      "SpeechMessage.Dynamics.Deployment\\SpeechMessage.Dynamics.Deployment.csproj",
      "SpeechMessage.Dynamics.Plugins.Tests\\SpeechMessage.Dynamics.Plugins.Tests.csproj",
      "SpeechMessage.Dynamics.Plugins\\SpeechMessage.Dynamics.Plugins.csproj",
      "SpeechMessage.Localization.Tests\\SpeechMessage.Localization.Tests.csproj",
      "SpeechMessage.Localization\\SpeechMessage.Localization.csproj",
      "SpeechMessage.Payments.AspNetCore\\SpeechMessage.Payments.AspNetCore.csproj",
      "SpeechMessage.Payments.Tests\\SpeechMessage.Payments.Tests.csproj",
      "SpeechMessage.Payments.Workflows\\SpeechMessage.Payments.Workflows.csproj",
      "SpeechMessage.Payments\\SpeechMessage.Payments.csproj",
      "ToolUtility.Core\\ToolUtility.Core.csproj",
      "ToolUtility.Tests\\ToolUtility.Tests.csproj",
      "ToolUtility\\ToolUtility.csproj"
    ]
  }
}

```

## tools/RepoQuality/Tests/Test-CiTransition.Tests.ps1
```
<#
.SYNOPSIS
以真實方案依賴與 workflow 執行區塊驗證 DevExtreme 來源未定時的過渡範圍。
.DESCRIPTION
省略篩選檔、漏掉間接依賴、把不相關專案排除、使用完整方案、隱藏摘要或誤啟動 tracer 都必須失敗。
只替換 dotnet 執行邊界以記錄實際命令參數，不連線或讀取設定中的機密。摘要使用合成暫存檔，確定清理。
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$workflow = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/solution-ci.yml'))
$filterName = 'SpeechMessageProducts.CI.slnf'
$script:calls = New-Object 'System.Collections.Generic.List[string[]]'
function dotnet {
    # 只記錄 workflow 真正傳入的命令參數；不執行建置或測試，也不接受任何帳密。
    $script:calls.Add([string[]]$args)
    $global:LASTEXITCODE = 0
}
function Get-RunBlock([string]$Name) {
    # 保留實際 run 內容；除 dotnet 外不替換任何指令，摘要會寫入測試自有檔案。
    $match = [regex]::Match($script:workflow, '(?ms)^      - name: ' + [regex]::Escape($Name) + '\r?\n(?<meta>.*?)        run: \|\r?\n(?<run>.*?)(?=^      - name:|\z)')
    if (-not $match.Success) { throw ('Workflow block missing: ' + $Name) }
    return $match
}
$cases = [ordered]@{
    Filter_ExcludesExactlyDependencyClosure = {
        $filter = [IO.File]::ReadAllText((Join-Path $root $filterName)) | ConvertFrom-Json
        if ($filter.solution.path -ne 'SpeechMessageProducts.sln') { throw 'Unexpected parent solution.' }
        $solution = [IO.File]::ReadAllText((Join-Path $root 'SpeechMessageProducts.sln'))
        $projects = @([regex]::Matches($solution, '(?m)^Project\([^\r\n]+?= "[^"]+", "(?<path>[^"]+\.csproj)"') | ForEach-Object { $_.Groups['path'].Value.Replace('\', '/') })
        $graph = @{}
        $excluded = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($project in $projects) {
            $path = Join-Path $root $project
            [xml]$xml = [IO.File]::ReadAllText($path)
            $graph[$project] = @($xml.SelectNodes('//ProjectReference') | ForEach-Object {
                $include = $_.GetAttribute('Include')
                if ($include.Contains('$')) { throw 'Unresolved dependency cannot be classified.' }
                [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($path)) $include)).Substring($root.Length + 1).Replace('\', '/')
            })
            if (@($xml.SelectNodes('//PackageReference') | Where-Object { $_.GetAttribute('Include') -eq 'DevExtreme.AspNet.Core' }).Count -gt 0) { [void]$excluded.Add($project) }
        }
        do {
            $changed = $false
            foreach ($project in $projects) {
                if (@($graph[$project] | Where-Object { $excluded.Contains($_) }).Count -gt 0) { if ($excluded.Add($project)) { $changed = $true } }
            }
        } while ($changed)
        $known = @('ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj', 'SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj', 'ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj')
        if (@(Compare-Object @($excluded | Sort-Object) ($known | Sort-Object)).Count -gt 0) { throw 'Dependency closure changed; review the transition scope.' }
        $expected = @($projects | Where-Object { -not $excluded.Contains($_) } | Sort-Object)
        $actual = @($filter.solution.projects | ForEach-Object { $_.Replace('\', '/') } | Sort-Object)
        # P1 新增專案仍須全部納入；精確差集與固定三項排除都保留，不再以 P0 的23掩蓋新專案。
        if ($actual.Count -ne $expected.Count -or @($actual | Select-Object -Unique).Count -ne $actual.Count -or @(Compare-Object $expected $actual).Count -gt 0) { throw 'Filter does not preserve exactly the eligible projects.' }
    }
    Coverage_IncludesBothCoreProductTargets = {
        # 評估 workflow 真正使用的矩陣宣告，不以字串存在代替目標／專案配對驗證。
        $run = (Get-RunBlock 'Coverage for each new module and target').Groups['run'].Value -replace '(?m)^          ', ''
        $tokens = $null
        $parseErrors = $null
        $ast = [Management.Automation.Language.Parser]::ParseInput($run, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count -gt 0) { throw 'Coverage run block is invalid.' }
        $assignment = $ast.Find({
            param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
                $node.Left.VariablePath.UserPath -eq 'cases'
        }, $true)
        if ($null -eq $assignment) { throw 'Coverage matrix is missing.' }
        $matrix = & ([scriptblock]::Create($assignment.Extent.Text + '; $cases'))
        $core = @($matrix | Where-Object { $_.Module -ceq 'ToolUtility.Core' })
        if ($core.Count -ne 2 -or @($core | Where-Object { $_.Project -cne 'ToolUtility.Tests' }).Count -gt 0) {
            throw 'Core coverage must use its complete test project for both targets.'
        }
        $frameworks = @($core | ForEach-Object { $_.Framework } | Sort-Object)
        if (@(Compare-Object @('net10.0', 'net48') $frameworks).Count -gt 0) {
            throw 'Core coverage must independently execute net48 and net10.0.'
        }
    }
    Commands_UseFilteredSolution = {
        foreach ($name in @('Restore', 'Build without signing key', 'Release solution tests and coverage')) {
            $script:calls.Clear()
            $run = (Get-RunBlock $name).Groups['run'].Value -replace '(?m)^          ', ''
            & ([scriptblock]::Create($run))
            if ($script:calls.Count -eq 0 -or $script:calls[0][1] -ne $filterName) { throw ('Wrong solution executed by ' + $name) }
        }
    }
    Tracer_DisabledButRetainsExecutableGuard = {
        $block = Get-RunBlock 'Debug tracer contracts'
        if ($block.Groups['meta'].Value -notmatch 'if:.*false' -or $block.Groups['run'].Value -notmatch 'Test-TrxExecution') { throw 'Dependent tracer must remain disabled with its guard preserved.' }
    }
    Summary_DisclosesAllExcludedProjects = {
        $block = Get-RunBlock 'CI transition scope'
        if ($block.Groups['meta'].Value -notmatch 'if: always\(\)') { throw 'Scope must be visible even on failure.' }
        $previous = $env:GITHUB_STEP_SUMMARY
        $temporary = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-ci-summary-' + [Guid]::NewGuid().ToString('N') + '.md')
        try {
            $env:GITHUB_STEP_SUMMARY = $temporary
            & ([scriptblock]::Create(($block.Groups['run'].Value -replace '(?m)^          ', '')))
            $text = [IO.File]::ReadAllText($temporary)
            foreach ($name in @('SpeechMessageProducts.ChurchReport', 'ChurchReport.MemberInfo.Tests', 'ToolUtility.Dataverse.Tests')) { if (-not $text.Contains($name)) { throw 'Excluded project missing from summary.' } }
            if ($text -notmatch 'Transitional' -or $text -notmatch 'not a full solution') { throw 'Partial coverage must be explicit.' }
        } finally {
            $env:GITHUB_STEP_SUMMARY = $previous
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        }
    }
    NugetConfig_DoesNotDeclareMachineSpecificSource = {
        [xml]$config = [IO.File]::ReadAllText((Join-Path $root 'SpeechMessageProducts.ChurchReport/NuGet.config'))
        if (@($config.SelectNodes('//packageSources/add')).Count -ne 0) { throw 'Repository must not declare a private or machine-specific source.' }
    }
}
$previousLocation = (Get-Location).Path
$failed = 0
try {
    Set-Location -LiteralPath $root
    foreach ($case in $cases.GetEnumerator()) {
        try { & $case.Value; Write-Output ('PASS ' + $case.Key) }
        catch { $failed++; Write-Output ('FAIL ' + $case.Key + ': ' + $_.Exception.GetType().Name) }
    }
} finally { Set-Location -LiteralPath $previousLocation }
Write-Output ('CI transition tests: ' + ($cases.Count - $failed) + '/' + $cases.Count)
if ($failed -gt 0) { exit 1 }
exit 0

```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
