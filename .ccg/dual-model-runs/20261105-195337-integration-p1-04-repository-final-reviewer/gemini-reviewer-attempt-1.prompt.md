ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: integration-p1-04-repository-final

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
P1-04 Repository增量末端審查。唯讀繁體中文，僅下列來源與證據；不探索Facade/設定/秘密/父工作樹。
前輪193656完整Gemini+Claude無Critical/High，Claude兩Warning：
W1記錄入口不支援沒有正式ID的聚合投影須明載；W2PageInfo=null時會原地補值須明載。
目前兩類別XML及報告均補，並加實際aggregate count無ID列拒絕/不補造ID的測試。
另本地I18nCheck正確抓13個從舊host搬入Core的中文診斷常值，已統一固定英文；
型別、ParamName、原驗證位置/順序、InnerException同一實例、單次I/O不變，掃描baseline不改，
新Core中文常值0、verify通過。前輪review不被當作新版已審，本輪請審完整最新來源。
新鮮完整方案16份TRX2167/2167、0失敗略過；ToolUtility net48實載net462 363/363、net10 457/457，
Core每target獨立恰一份coverage99.42%/100%；strict0warning/error；舊host17warning未隱藏或擴張重寫。
ChurchReport623/623、架構67/67；工具I18n108/108、Packaging27/27。其他00§8守門另外保存。
先host12特性綠、安全30紅/4綠、overload2紅、修後搬前含6caller98綠，再git mv。
明列安全差異含空/重複/跨表ID、名稱/Assign非空ID及一致性、Fetch根表/DTD、固定安全外層訊息及移除Trace。
保留SDK自身ArgumentException同一InnerException，不要建議一律catch ArgumentException rethrow破壞既有wrapper。
沒有更改IEntityQueryService既有實作者義務，ColumnSet原參照保留，Repository不新增Dispose契約或結果cache。
所有資源僅借入SDK scope；1000 A/B含59故障明確重試及active=0、Dispose=0、弱參照回收，
12案執行真QueryService/MarketingListService→Repo→SDK六個消費點，非實機授權/DI證據。
P1仍2/12，整包未完成；不連VM/D365/LINE、不push。請核W1/W2關閉，
並對最新來源列Critical/High/Warning/Info，勿將沒有新缺陷的已核定相容決策反覆要求改架構。


## ToolUtility.Core/EntityOperations/EntityRepository.cs
```
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using ToolUtilityNameSpace.OwnerOperations;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 同一個 host 已授權 SDK scope 的同步資料操作；不建立連線、共用狀態或隱藏重試。
    /// 資料庫 ID／整頁識別驗證完成才回傳，名稱與 caller 輸入永遠不是授權依據。
    /// </summary>
    public interface IEntityRepository
    {
        /// <summary>建立一次並要求 SDK 確認非空資料庫 ID；業務冪等鍵須由 caller 與資料庫事先保證。</summary>
        /// <param name="entity">操作獨占的已授權候選，不得在呼叫期間並行修改。</param>
        /// <returns>SDK 確認的非空資料庫 GUID，不自行寫回候選。</returns>
        Guid Create(Entity entity);

        /// <summary>只更新具有正式非空 ID 的實體；此舊契約不接受僅有替代鍵的候選。</summary>
        /// <param name="entity">同一授權範圍中的持久化資料，不保存其屬性。</param>
        void Update(Entity entity);

        /// <summary>精確刪除已授權的實體與資料庫 ID，不按名稱合併或刪除同名記錄。</summary>
        /// <param name="entityName">伺服器決定的實體名稱。</param>
        /// <param name="entityId">非空的正式資料庫 ID。</param>
        void Delete(string entityName, Guid entityId);

        /// <summary>每次直接讀取 SDK 並核對 ID／實體；null 欄位沿用完整欄位，指定欄位原樣傳遞。</summary>
        /// <param name="entityName">已授權的合法 logical name。</param>
        /// <param name="entityId">同一授權範圍的正式資料庫 ID。</param>
        /// <param name="columnSet">caller 擁有的欄位集合；null 讀全部欄位。</param>
        /// <returns>本次操作擁有的完整單筆候選，不走共用快取。</returns>
        Entity Retrieve(string entityName, Guid entityId, ColumnSet? columnSet = null);

        /// <summary>只回傳整頁識別驗證成功的候選；保留分頁資訊，同名不同 ID 的列完整保留。</summary>
        /// <param name="query">已授權的操作局部查詢，不能在執行中並行改動。</param>
        /// <returns>本次 caller 獨占的 SDK 頁面；caller 自行擁有後續分頁。</returns>
        EntityCollection RetrieveMultiple(QueryBase query);

        /// <summary>以原 systemuser 指派語義執行一次；目標表名與 ID 必須一致且有效。</summary>
        /// <param name="entityName">必須等於 entity.LogicalName 的已授權表名。</param>
        /// <param name="entity">具有正式非空資料庫 ID 的操作獨占記錄。</param>
        /// <param name="newOwnerId">同一組織已授權的 systemuser ID。</param>
        void AssignOwner(string entityName, Entity entity, Guid newOwnerId);
    }

    /// <summary>
    /// 保留既有 Repository API 與操作失敗包裝，委派唯一的 Core CRUD／Owner／Query 驗證。
    /// 實例只限借入 SDK 的原授權 scope，禁止跨使用者／租戶 singleton；不保存 Entity、Query 或回應。
    /// </summary>
    /// <remarks>
    /// SDK、連線、逾時及 Dispose 由 host 擁有；本類沒有自有 disposable、工作、timer、cache 或佇列。
    /// Query 轉接器每次以 using 擁有並清除借用參照，不把 Dispose 義務新增給既有 Repository caller。
    /// 舊有顯式參數檢查仍在 try 前；新增 adapter 防禦及真 SDK 故障皆沿用操作失敗包裝。
    /// 包裝只用固定訊息並保留同一真實 InnerException，供 host 共用通知入口處理；不在 Core 記錄原故障或個資。
    /// 不按名稱去重、補造資料庫 ID、回傳部分頁或盲目重試狀態不確定的寫入。
    /// </remarks>
    public class EntityRepository : IEntityRepository
    {
        private readonly IOrganizationService _organizationService;
        private readonly EntityCrudService _crud;
        private readonly OwnerManagementService _owners;

        /// <summary>
        /// 只借用 host 已授權且仍存活的 SDK，不接管其生命週期或建立另一連線。
        /// 局部無狀態 marker 僅滿足舊 adapter 建構簽章，兩服務都不保存 marker。
        /// </summary>
        /// <param name="organizationService">不可為 null；host 須等同步操作排空後才處置原 SDK。</param>
        public EntityRepository(IOrganizationService organizationService)
        {
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
            var marker = new object();
            _crud = new EntityCrudService(marker, organizationService);
            _owners = new OwnerManagementService(marker, organizationService);
        }

        /// <summary>
        /// 保留 null 參數拒絕，單次建立成功且有正式 ID 才回傳，不自行修改 Entity 或補做重試。
        /// 新增名稱缺失與 SDK 空 ID 拒絕，錯誤固定包裝並保留真實 InnerException。
        /// </summary>
        /// <param name="entity">操作局部且已授權的候選，生命週期不延伸至方法返回後。</param>
        /// <returns>SDK 確認的非空 ID；不是應用程序生成的重試鍵。</returns>
        public Guid Create(Entity entity)
        {
#if NETFRAMEWORK
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif

            try
            {
                return _crud.CreateEntity(entity);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The entity could not be created.", ex);
            }
        }

        /// <summary>
        /// 維持 Repository 原有非空 ID 限制，即使有替代鍵也不放行空 ID。
        /// 只執行一次 SDK 更新；真實故障保留於固定包裝的 InnerException。
        /// </summary>
        /// <param name="entity">已授權且具有正式 ID 的操作局部實體，不保存或複製到共用狀態。</param>
        public void Update(Entity entity)
        {
#if NETFRAMEWORK
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif

            if (entity.Id == Guid.Empty)
                throw new ArgumentException("A database identity is required.", nameof(entity));

            try
            {
                _crud.UpdateEntity(entity);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The entity could not be updated.", ex);
            }
        }

        /// <summary>
        /// 保留表名、ID 的既有驗證順序及型別，刪除僅針對明確正式識別。
        /// SDK 故障不吞掉、不重試，不自行記錄原故障內容。
        /// </summary>
        /// <param name="entityName">伺服器已驗證的實體名稱，不能取代授權。</param>
        /// <param name="entityId">已授權的正式非空 GUID。</param>
        public void Delete(string entityName, Guid entityId)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("An entity name is required.", nameof(entityName));

            if (entityId == Guid.Empty)
                throw new ArgumentException("A database identity is required.", nameof(entityId));

            try
            {
                _crud.DeleteEntity(entityName, entityId);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The entity could not be deleted.", ex);
            }
        }

        /// <summary>
        /// 不走快取且保留指定 ColumnSet 實例；缺省仍讀 AllColumns。
        /// 回應 ID／表名不符即拒絕，不回傳錯 scope 的候選或偽裝成查無資料。
        /// </summary>
        /// <param name="entityName">已授權的資料庫 logical name。</param>
        /// <param name="entityId">本次 caller 已授權的正式資料庫 ID。</param>
        /// <param name="columnSet">操作局部欄位集合，null 沿用完整欄位。</param>
        /// <returns>只屬於本次操作的已驗證 SDK 實體。</returns>
        public Entity Retrieve(string entityName, Guid entityId, ColumnSet? columnSet = null)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("An entity name is required.", nameof(entityName));

            if (entityId == Guid.Empty)
                throw new ArgumentException("A database identity is required.", nameof(entityId));

            try
            {
                using var query = new EntityQueryService(organizationService: _organizationService);
                return query.RetrieveEntity(entityName, entityId, columnSet);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The entity could not be retrieved.", ex);
            }
        }

        /// <summary>
        /// 重用唯一查詢根表／整頁識別驗證，候選全部成功才一次回傳。
        /// 保留原分頁資訊，QueryExpression.PageInfo 缺失才採 Core 的5000筆第一頁預設；不跨頁自動載入。
        /// 此預設會直接寫入 caller 擁有的 QueryExpression.PageInfo；呼叫中不得共用可變查詢。
        /// 本入口只發布具正式資料庫 ID 的記錄，不支援欠缺記錄 ID 的聚合投影，也不為其補造識別。
        /// </summary>
        /// <param name="query">已授權且執行中不被並行修改的 caller-owned 查詢。</param>
        /// <returns>完整有效的單頁；同名不同 ID 不去重，衝突不部分發布。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
#if NETFRAMEWORK
            if (query == null)
                throw new ArgumentNullException(nameof(query));
#else
            ArgumentNullException.ThrowIfNull(query);
#endif

            try
            {
                using var reader = new EntityQueryService(organizationService: _organizationService);
                return reader.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The entity collection could not be retrieved.", ex);
            }
        }

        /// <summary>
        /// 保留原 systemuser 受派者語義及既有參數拒絕順序。
        /// 新增目標表名一致與非空正式 ID 防禦，成功時不改寫 caller 的 Entity 或持有其參照。
        /// </summary>
        /// <param name="entityName">已授權且等於 entity.LogicalName 的實體名稱。</param>
        /// <param name="entity">同一 SDK scope 的持久化實體，非空 ID 不是授權本身。</param>
        /// <param name="newOwnerId">同組織已授權的 systemuser 正式 ID。</param>
        public void AssignOwner(string entityName, Entity entity, Guid newOwnerId)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("An entity name is required.", nameof(entityName));

#if NETFRAMEWORK
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif

            if (newOwnerId == Guid.Empty)
                throw new ArgumentException("An owner database identity is required.", nameof(newOwnerId));

            try
            {
                _owners.AssignOwner(entityName, entity, newOwnerId);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The entity owner could not be assigned.", ex);
            }
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
        public Entity RetrieveEntity(string entityName, Guid entityId) => RetrieveEntity(entityName, entityId, null);

        /// <summary>
        /// 提供 Repository 所需的精確欄位讀取，與完整欄位入口共用相同正式識別驗證。
        /// 不重建 caller 的 ColumnSet；null 才建立本次操作的 AllColumns，回應不保存於服務。
        /// 此新增 overload 不擴充既有介面，以免強迫外部介面實作者新增成員。
        /// </summary>
        /// <param name="entityName">伺服器已授權的合法 logical name。</param>
        /// <param name="entityId">同一授權範圍的非空正式資料庫 ID。</param>
        /// <param name="columnSet">操作局部欄位集合，呼叫中不可並行改動；null 沿用完整欄位。</param>
        /// <returns>本次操作獨占且已驗證 ID／表名的 SDK 實體。</returns>
        public Entity RetrieveEntity(string entityName, Guid entityId, ColumnSet? columnSet)
        {
            var service = GetService();
            RequireLogicalName(entityName, nameof(entityName));
            if (entityId == Guid.Empty) throw new ArgumentException("A database identifier is required.", nameof(entityId));
            var candidate = service.Retrieve(entityName, entityId, columnSet ?? new ColumnSet(true));
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
        /// 補值會直接寫入 caller 的 QueryExpression；本記錄入口不支援沒有正式 ID 的聚合投影，不補造 ID。
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
using ToolUtilityNameSpace.OwnerOperations;
using ToolUtilityNameSpace.Interfaces;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.Extensions;
using ToolUtilityNameSpace.Constants;
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
            Assert.Same(assembly, typeof(EntityRepository).Assembly);
            Assert.Same(assembly, typeof(IEntityRepository).Assembly);
            Assert.Same(assembly, typeof(AppointmentService).Assembly);
            Assert.Same(assembly, typeof(LessonsService).Assembly);
            Assert.Same(assembly, typeof(MeetingStatisticsService).Assembly);
            Assert.Same(assembly, typeof(EntityCrudService).Assembly);
            Assert.Same(assembly, typeof(OwnerManagementService).Assembly);
            Assert.Same(assembly, typeof(EntityAttributeUtilityService).Assembly);
            Assert.Same(assembly, typeof(IEntityAttributeUtilityService).Assembly);
            Assert.Same(assembly, typeof(OrganizationServiceExtensions).Assembly);
            Assert.Same(assembly, typeof(CrmEntityColumns).Assembly);
            Assert.Equal("ToolUtility.Core", assembly.GetName().Name);
            var target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(target);
#if NETFRAMEWORK
            Assert.Equal(".NETFramework,Version=v4.6.2", target!.FrameworkName);
#else
            Assert.Equal(".NETCoreApp,Version=v10.0", target!.FrameworkName);
#endif
        }

#if !NETFRAMEWORK
        /// <summary>
        /// 實際反射抽取介面的公開 nullable 中繼資料，而非以檔頭是否有 pragma 判斷。
        /// Core strict 專案統一啟用 Nullable；若後續設定漂移成 oblivious，任一字串參數即決定性失敗。
        /// 屬性讀取的 attributeName 依舊允許 null，其餘字串必須維持非 null 契約。
        /// 不需修改產品公開 API 或新增重複 pragma 來配合靜態審查推測。
        /// </summary>
        [Theory]
        [InlineData(typeof(IAppointmentService))]
        [InlineData(typeof(ILessonsService))]
        [InlineData(typeof(IMeetingStatisticsService))]
        [InlineData(typeof(IEntityCrudService))]
        [InlineData(typeof(IEntityRepository))]
        [InlineData(typeof(IOwnerManagementService))]
        [InlineData(typeof(IEntityAttributeUtilityService))]
        public void CoreInterfaces_EmitNonObliviousStringContracts(System.Type contract)
        {
            var context = new NullabilityInfoContext();
            var checkedParameters = 0;
            foreach (var method in contract.GetMethods())
                foreach (var parameter in method.GetParameters())
                    if (parameter.ParameterType == typeof(string))
                    {
                        var expected = contract == typeof(IEntityAttributeUtilityService) && parameter.Name == "attributeName"
                            ? NullabilityState.Nullable : NullabilityState.NotNull;
                        Assert.Equal(expected, context.Create(parameter).ReadState);
                        checkedParameters++;
                    }
            Assert.True(checkedParameters > 0);
        }
#endif
    }
}

```

## ToolUtility.Tests/DataOperations/RepositoryCharacterizationTests.cs
```
using System;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 在原 host 先鎖定 Repository 六入口的有效 SDK 邊界，再由相同測試保護 Core 搬移。
    /// 全部識別與內容為合成值；只替代外部 SDK，不假稱 D365、授權或資料庫唯一鍵已驗收。
    /// </summary>
    public sealed class RepositoryCharacterizationTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OwnerId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>寫入參數與既有黃金檔一致，原 Entity 不改動，每次只呼叫一次 SDK。</summary>
        /// <param name="operation">固定的合成寫入種類，不從使用者輸入產生。</param>
        [Theory]
        [InlineData("create")]
        [InlineData("update")]
        [InlineData("delete")]
        public void Crud_ValidInput_PreservesGoldenRequest(string operation)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("contact", RecordId) { ["fullname"] = "合成同名" };
            XElement? actual = null;
            if (operation == "create")
                sdk.Setup(s => s.Create(entity)).Callback<Entity>(e => actual = Describe(operation, e)).Returns(RecordId);
            else if (operation == "update")
                sdk.Setup(s => s.Update(entity)).Callback<Entity>(e => actual = Describe(operation, e));
            else
                sdk.Setup(s => s.Delete("contact", RecordId))
                    .Callback<string, Guid>((name, id) => actual = Describe(operation, new Entity(name, id)));
            var repository = new EntityRepository(sdk.Object);
            if (operation == "create") Assert.Equal(RecordId, repository.Create(entity));
            else if (operation == "update") repository.Update(entity);
            else repository.Delete("contact", RecordId);
            Assert.NotNull(actual);
            GoldenQueryAssertions.Equal("mutation-" + operation + ".xml", actual!.ToString());
            Assert.Equal(RecordId, entity.Id);
            Assert.Equal("合成同名", entity.GetAttributeValue<string>("fullname"));
            sdk.VerifyAll();
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>省略或傳 null 欄位仍讀 AllColumns；明確欄位實例原樣交 SDK，回應不被重建或保留。</summary>
        /// <param name="mode">省略、null、指定欄位或明確全欄位。</param>
        [Theory]
        [InlineData("omitted")]
        [InlineData("null")]
        [InlineData("explicit")]
        [InlineData("all")]
        public void Retrieve_PreservesColumnSetAndResponse(string mode)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var columns = mode == "explicit" ? new ColumnSet("fullname") : mode == "all" ? new ColumnSet(true) : null;
            var expected = new Entity("contact", RecordId) { ["fullname"] = "合成同名" };
            ColumnSet? actual = null;
            sdk.Setup(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()))
                .Callback<string, Guid, ColumnSet>((_, _, sent) => actual = sent).Returns(expected);
            var repository = new EntityRepository(sdk.Object);
            var result = mode == "omitted" ? repository.Retrieve("contact", RecordId)
                : repository.Retrieve("contact", RecordId, columns!);
            Assert.Same(expected, result);
            Assert.NotNull(actual);
            if (columns != null) Assert.Same(columns, actual);
            else Assert.True(actual!.AllColumns);
            sdk.Verify(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>三種合法查詢與完整分頁資訊原樣通過；相同顯示名稱的不同 ID 都保留。</summary>
        /// <param name="kind">SDK 支援的固定查詢種類。</param>
        [Theory]
        [InlineData("expression")]
        [InlineData("attributes")]
        [InlineData("fetch")]
        public void RetrieveMultiple_PreservesQueryRowsAndPaging(string kind)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var paging = new PagingInfo { Count = 23, PageNumber = 4, PagingCookie = "synthetic-cookie" };
            QueryBase query = kind == "expression" ? new QueryExpression("contact")
                { ColumnSet = new ColumnSet("fullname"), PageInfo = paging }
                : kind == "attributes" ? new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) }
                : new FetchExpression("<fetch page='4' count='23'><entity name='contact'><attribute name='fullname'/></entity></fetch>");
            var expected = new EntityCollection
            {
                MoreRecords = true, PagingCookie = "synthetic-next", TotalRecordCount = 63
            };
            expected.Entities.Add(new Entity("contact", RecordId) { ["fullname"] = "合成同名" });
            expected.Entities.Add(new Entity("contact", OwnerId) { ["fullname"] = "合成同名" });
            sdk.Setup(s => s.RetrieveMultiple(query)).Returns(expected);
            var result = new EntityRepository(sdk.Object).RetrieveMultiple(query);
            Assert.Same(expected, result);
            Assert.Equal(2, result.Entities.Count);
            Assert.Equal(RecordId, result.Entities[0].Id);
            Assert.Equal(OwnerId, result.Entities[1].Id);
            Assert.True(result.MoreRecords);
            Assert.Equal("synthetic-next", result.PagingCookie);
            Assert.Equal(63, result.TotalRecordCount);
            if (query is QueryExpression expression) Assert.Same(paging, expression.PageInfo);
            sdk.Verify(s => s.RetrieveMultiple(query), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>指派仍使用 systemuser 與原目標資料庫 ID；請求與原 golden 相同且不寫回 Entity。</summary>
        [Fact]
        public void AssignOwner_PreservesGoldenRequest()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            AssignRequest? actual = null;
            sdk.Setup(s => s.Execute(It.IsAny<AssignRequest>()))
                .Callback<OrganizationRequest>(request => actual = (AssignRequest)request).Returns(new AssignResponse());
            var entity = new Entity("contact", RecordId);
            new EntityRepository(sdk.Object).AssignOwner("contact", entity, OwnerId);
            Assert.NotNull(actual);
            GoldenQueryAssertions.Equal("mutation-assign.xml", new XElement("assign",
                new XElement("target", new XAttribute("name", actual!.Target.LogicalName), new XAttribute("id", actual.Target.Id)),
                new XElement("assignee", new XAttribute("name", actual.Assignee.LogicalName), new XAttribute("id", actual.Assignee.Id))).ToString());
            Assert.False(entity.Contains("ownerid"));
            sdk.Verify(s => s.Execute(It.IsAny<AssignRequest>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>既有參數拒絕的位置、型別及名稱不變；特別保護 Update 不放寬為純替代鍵更新。</summary>
        [Fact]
        public void ExistingValidation_RejectsBeforeSdkWithOriginalParameterNames()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var repository = new EntityRepository(sdk.Object);
            Assert.Equal("organizationService", Assert.Throws<ArgumentNullException>(() => new EntityRepository(null!)).ParamName);
            Assert.Equal("entity", Assert.Throws<ArgumentNullException>(() => repository.Create(null!)).ParamName);
            Assert.Equal("entity", Assert.Throws<ArgumentNullException>(() => repository.Update(null!)).ParamName);
            var alternateKeyOnly = new Entity("contact");
            alternateKeyOnly.KeyAttributes["new_externalid"] = "synthetic-event";
            Assert.Equal("entity", Assert.Throws<ArgumentException>(() => repository.Update(alternateKeyOnly)).ParamName);
            Assert.Equal("entityName", Assert.Throws<ArgumentException>(() => repository.Delete(" ", RecordId)).ParamName);
            Assert.Equal("entityId", Assert.Throws<ArgumentException>(() => repository.Delete("contact", Guid.Empty)).ParamName);
            Assert.Equal("entityName", Assert.Throws<ArgumentException>(() => repository.Retrieve(" ", RecordId)).ParamName);
            Assert.Equal("entityId", Assert.Throws<ArgumentException>(() => repository.Retrieve("contact", Guid.Empty)).ParamName);
            Assert.Equal("query", Assert.Throws<ArgumentNullException>(() => repository.RetrieveMultiple(null!)).ParamName);
            Assert.Equal("entityName", Assert.Throws<ArgumentException>(() => repository.AssignOwner(" ", null!, Guid.Empty)).ParamName);
            Assert.Equal("entity", Assert.Throws<ArgumentNullException>(() => repository.AssignOwner("contact", null!, Guid.Empty)).ParamName);
            Assert.Equal("newOwnerId", Assert.Throws<ArgumentException>(() => repository.AssignOwner("contact", new Entity("contact", RecordId), Guid.Empty)).ParamName);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>只投影合成 fullname 與固定識別；不序列化 SDK、憑證或任意使用者資料。</summary>
        private static XElement Describe(string operation, Entity entity)
        {
            var xml = new XElement(operation, new XAttribute("name", entity.LogicalName), new XAttribute("id", entity.Id));
            if (entity.Contains("fullname")) xml.Add(new XElement("fullname", entity.GetAttributeValue<string>("fullname")));
            return xml;
        }
    }
}

```

## ToolUtility.Tests/DataOperations/RepositorySafetyTests.cs
```
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>Trace listener 為程序集合，這組測試獨占執行以免干擾其他診斷測試；不清空外部 listener。</summary>
    [CollectionDefinition("Repository trace isolation", DisableParallelization = true)]
    public sealed class RepositoryTraceCollection { }

    /// <summary>
    /// 保護 Repository 搬移新增的 fail-closed、固定安全包裝、借入 SDK 所有權與無快取隔離。
    /// 故障均由合成 SDK 邊界注入；驗證不代表真正 D365 或跨程序資料庫唯一索引驗收。
    /// </summary>
    [Collection("Repository trace isolation")]
    public sealed class RepositorySafetyTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OwnerId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>
        /// 以反射在新 overload 尚不存在時取得可執行紅測；公開三參數入口須保留原 ColumnSet 參照。
        /// 不把 overload 加到既有介面而破壞外部實作者，兩參數公開契約亦必須繼續存在。
        /// </summary>
        /// <param name="explicitColumns">是否提供精確欄位子集；null 保持 AllColumns 舊預設。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void QueryColumnOverload_PreservesColumnsWithoutChangingExistingInterface(bool explicitColumns)
        {
            var overload = typeof(EntityQueryService).GetMethod("RetrieveEntity",
                new[] { typeof(string), typeof(Guid), typeof(ColumnSet) });
            Assert.NotNull(overload);
            Assert.NotNull(typeof(EntityQueryService).GetMethod("RetrieveEntity", new[] { typeof(string), typeof(Guid) }));
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var columns = explicitColumns ? new ColumnSet("fullname") : null;
            var expected = new Entity("contact", RecordId);
            ColumnSet? sent = null;
            sdk.Setup(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()))
                .Callback<string, Guid, ColumnSet>((_, _, value) => sent = value).Returns(expected);
            using var service = new EntityQueryService(organizationService: sdk.Object);
            Assert.Same(expected, overload!.Invoke(service, new object?[] { "contact", RecordId, columns }));
            if (explicitColumns) Assert.Same(columns, sent);
            else Assert.True(sent!.AllColumns);
            sdk.Verify(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 六入口遇到真正 SDK 逾時或 ArgumentException 都維持 InvalidOperationException wrapper，
        /// 同一 InnerException、固定訊息及單次 I/O；不能以 catch ArgumentException 一律重拋而誤改 SDK 故障契約。
        /// </summary>
        /// <param name="operation">合成 SDK 操作。</param>
        /// <param name="argumentFault">是否注入 SDK 自身的參數例外。</param>
        [Theory]
        [InlineData("create", false)]
        [InlineData("update", false)]
        [InlineData("delete", false)]
        [InlineData("retrieve", false)]
        [InlineData("multiple", false)]
        [InlineData("assign", false)]
        [InlineData("create", true)]
        [InlineData("update", true)]
        [InlineData("delete", true)]
        [InlineData("retrieve", true)]
        [InlineData("multiple", true)]
        [InlineData("assign", true)]
        public void SdkFailure_PreservesRealExceptionAndFixedOuterMessage(string operation, bool argumentFault)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Exception failure = argumentFault ? new ArgumentException("synthetic_private_transport")
                : new TimeoutException("synthetic_private_transport");
            var entity = new Entity("p1_repository_marker", RecordId);
            var query = new QueryExpression(entity.LogicalName);
            if (operation == "create") sdk.Setup(s => s.Create(entity)).Throws(failure);
            else if (operation == "update") sdk.Setup(s => s.Update(entity)).Throws(failure);
            else if (operation == "delete") sdk.Setup(s => s.Delete(entity.LogicalName, RecordId)).Throws(failure);
            else if (operation == "retrieve") sdk.Setup(s => s.Retrieve(entity.LogicalName, RecordId, It.IsAny<ColumnSet>())).Throws(failure);
            else if (operation == "multiple") sdk.Setup(s => s.RetrieveMultiple(query)).Throws(failure);
            else sdk.Setup(s => s.Execute(It.IsAny<AssignRequest>())).Throws(failure);
            var repository = new EntityRepository(sdk.Object);
            var actual = Assert.Throws<InvalidOperationException>(() => Invoke(repository, operation, entity, query));
            Assert.Same(failure, actual.InnerException);
            Assert.Equal(Message(operation), actual.Message);
            sdk.VerifyAll();
            Assert.Single(sdk.Invocations);
        }

        /// <summary>SDK 未確認建立 ID 時拒絕回報成功，不盲目重試或改寫 caller 的未建立候選。</summary>
        [Fact]
        public void Create_EmptySdkIdentity_RejectsWithoutRetry()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("contact");
            sdk.Setup(s => s.Create(entity)).Returns(Guid.Empty);
            var failure = Assert.Throws<InvalidOperationException>(() => new EntityRepository(sdk.Object).Create(entity));
            Assert.Equal(Message("create"), failure.Message);
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Equal(Guid.Empty, entity.Id);
            sdk.Verify(s => s.Create(entity), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>單筆缺失、錯 ID 或跨表回應都拒絕，不能把 ColumnSet 子集當成缺少識別的理由。</summary>
        /// <param name="shape">合成回應的不完整形狀。</param>
        [Theory]
        [InlineData("missing")]
        [InlineData("wrong-id")]
        [InlineData("wrong-table")]
        public void Retrieve_InvalidIdentity_RejectsWholeResponse(string shape)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var candidate = shape == "missing" ? null : new Entity(shape == "wrong-table" ? "account" : "contact",
                shape == "wrong-id" ? OwnerId : RecordId);
            sdk.Setup(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>())).Returns(candidate!);
            var failure = Assert.Throws<InvalidOperationException>(() => new EntityRepository(sdk.Object)
                .Retrieve("contact", RecordId, new ColumnSet(false)));
            Assert.Equal(Message("retrieve"), failure.Message);
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            sdk.Verify(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>整頁缺失、空 ID、重複 ID 或跨表一律拒絕；不修剪原候選以偽造成功。</summary>
        /// <param name="shape">不合法頁面形狀。</param>
        /// <param name="fetch">是否經 FetchXML 根實體辨識。</param>
        [Theory]
        [InlineData("missing", false)]
        [InlineData("duplicate", false)]
        [InlineData("empty", false)]
        [InlineData("wrong-table", false)]
        [InlineData("missing", true)]
        [InlineData("duplicate", true)]
        [InlineData("empty", true)]
        [InlineData("wrong-table", true)]
        public void RetrieveMultiple_InvalidPage_RejectsBeforePublication(string shape, bool fetch)
        {
            var page = new EntityCollection();
            page.Entities.Add(new Entity("contact", RecordId));
            page.Entities.Add(new Entity(shape == "wrong-table" ? "account" : "contact",
                shape == "empty" ? Guid.Empty : shape == "duplicate" ? RecordId : OwnerId));
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            QueryBase query = fetch ? new FetchExpression("<fetch><entity name='contact'/></fetch>") : new QueryExpression("contact");
            sdk.Setup(s => s.RetrieveMultiple(query)).Returns(shape == "missing" ? null! : page);
            var failure = Assert.Throws<InvalidOperationException>(() => new EntityRepository(sdk.Object).RetrieveMultiple(query));
            Assert.Equal(Message("multiple"), failure.Message);
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Equal(2, page.Entities.Count);
            sdk.Verify(s => s.RetrieveMultiple(query), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 聚合投影不等於正式資料列；SDK count列缺資料庫ID必須拒絕，不補造ID或把0筆偽裝成功。
        /// 本測試具體化既有空ID紅測所保護的契約，不新增聚合功能或修改查詢。
        /// </summary>
        [Fact]
        public void AggregateProjectionWithoutRecordIdentity_RejectsWithoutInventingId()
        {
            var query = new FetchExpression("<fetch aggregate='true'><entity name='contact'><attribute name='contactid' alias='total' aggregate='count'/></entity></fetch>");
            var row = new Entity("contact") { ["total"] = new AliasedValue("contact", "contactid", 2) };
            var page = new EntityCollection(new[] { row });
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(query)).Returns(page);
            var failure = Assert.Throws<InvalidOperationException>(() => new EntityRepository(sdk.Object).RetrieveMultiple(query));
            Assert.Equal(Message("multiple"), failure.Message);
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Same(row, Assert.Single(page.Entities));
            Assert.Equal(Guid.Empty, row.Id);
            Assert.Equal(2, Assert.IsType<AliasedValue>(row["total"]).Value);
            sdk.Verify(s => s.RetrieveMultiple(query), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 新增 adapter 的輸入防禦在 I/O 前拒絕，維持舊入口對原本未檢查情況的操作失敗包裝。
        /// 這是明列的安全差異，不宣稱原 host 已驗證名稱、Assign 目標一致性或非空 ID。
        /// </summary>
        /// <param name="shape">原 host 未檢查的合成輸入。</param>
        [Theory]
        [InlineData("create-name")]
        [InlineData("update-name")]
        [InlineData("assign-name")]
        [InlineData("assign-id")]
        [InlineData("fetch-dtd")]
        [InlineData("fetch-two-roots")]
        public void AdapterInvalidInput_RejectsWithoutSdk(string shape)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var repository = new EntityRepository(sdk.Object);
            var failure = Assert.Throws<InvalidOperationException>(() =>
            {
                if (shape == "create-name") repository.Create(new Entity());
                else if (shape == "update-name") repository.Update(new Entity { Id = RecordId });
                else if (shape == "assign-name") repository.AssignOwner("contact", new Entity("account", RecordId), OwnerId);
                else if (shape == "assign-id") repository.AssignOwner("contact", new Entity("contact"), OwnerId);
                else repository.RetrieveMultiple(new FetchExpression(shape == "fetch-dtd"
                    ? "<!DOCTYPE fetch [<!ENTITY value 'synthetic'>]><fetch><entity name='contact'/></fetch>"
                    : "<fetch><entity name='contact'/><entity name='account'/></fetch>"));
            });
            Assert.IsAssignableFrom<ArgumentException>(failure.InnerException);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>合法空頁、caller 明確重試及原分頁補值皆可用；失敗或 caller 修改不留存在 Repository。</summary>
        [Fact]
        public void FailureRetryAndCallerMutation_UseFreshCandidatesAndBorrowSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var fault = new TimeoutException("synthetic_private_transport");
            var query = new QueryExpression("contact") { PageInfo = null };
            sdk.SetupSequence(s => s.RetrieveMultiple(query)).Throws(fault)
                .Returns(new EntityCollection()).Returns(new EntityCollection());
            var repository = new EntityRepository(sdk.Object);
            Assert.Same(fault, Assert.Throws<InvalidOperationException>(() => repository.RetrieveMultiple(query)).InnerException);
            var first = repository.RetrieveMultiple(query);
            Assert.Empty(first.Entities);
            first.Entities.Add(new Entity("contact", RecordId));
            var next = repository.RetrieveMultiple(query);
            Assert.Empty(next.Entities);
            Assert.NotSame(first, next);
            Assert.NotNull(query.PageInfo);
            Assert.Equal(5000, query.PageInfo.Count);
            Assert.Equal(1, query.PageInfo.PageNumber);
            disposable.Verify(d => d.Dispose(), Times.Never);
            sdk.Verify(s => s.RetrieveMultiple(query), Times.Exactly(3));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A/B 已授權 SDK scope 各持自己的 Repository，執行 1000 次含故障及重試的有界操作。
        /// Task 全數 await、活動 SDK 計數回零；相同名稱與不同版本／使用者值不跨 scope、不保存結果、不處置借入 SDK。
        /// </summary>
        /// <returns>測試擁有並完整排空的有限工作集合。</returns>
        [Fact]
        public async Task ConcurrentScopes_FaultsRetryAndMutation_DoNotCrossTalkAndDrain()
        {
            var active = 0;
            var finished = 0;
            var failures = 0;
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            for (var scope = 0; scope < clients.Length; scope++)
            {
                var expected = scope == 0 ? "scope-a" : "scope-b";
                clients[scope].As<IDisposable>();
                clients[scope].Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        var query = Assert.IsType<QueryByAttribute>(input);
                        Assert.Equal(expected, query.Values[0]);
                        if ((bool)query.Values[2]) throw new TimeoutException("synthetic_retry");
                        var row = new Entity("contact", Guid.NewGuid())
                            { ["fullname"] = "合成同名", ["scope"] = expected, ["version"] = query.Values[1] };
                        return new EntityCollection(new[] { row });
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            }
            var repositories = clients.Select(sdk => new EntityRepository(sdk.Object)).ToArray();
            var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var scope = index % 2;
                var expected = scope == 0 ? "scope-a" : "scope-b";
                var query = new QueryByAttribute("contact");
                query.AddAttributeValue("scope", expected);
                query.AddAttributeValue("version", index);
                query.AddAttributeValue("inject", index % 17 == 0);
                if (index % 17 == 0)
                {
                    Assert.IsType<TimeoutException>(Assert.Throws<InvalidOperationException>(() => repositories[scope].RetrieveMultiple(query)).InnerException);
                    Interlocked.Increment(ref failures);
                    query.Values[2] = false;
                }
                var first = Assert.Single(repositories[scope].RetrieveMultiple(query).Entities);
                Assert.Equal(expected, first["scope"]);
                Assert.Equal(index, first["version"]);
                first["scope"] = "caller-mutated";
                var second = Assert.Single(repositories[scope].RetrieveMultiple(query).Entities);
                Assert.Equal(expected, second["scope"]);
                Assert.Equal(index, second["version"]);
                Assert.NotSame(first, second);
                Interlocked.Increment(ref finished);
            })).ToArray();
            await Task.WhenAll(tasks);
            Assert.All(tasks, task => Assert.True(task.IsCompleted));
            Assert.Equal(1000, Volatile.Read(ref finished));
            Assert.Equal(59, Volatile.Read(ref failures));
            Assert.Equal(0, Volatile.Read(ref active));
            foreach (var sdk in clients) sdk.As<IDisposable>().Verify(d => d.Dispose(), Times.Never);
        }

        /// <summary>
        /// 保持 Repository 存活時，已返回的 Entity／Query／頁面必須可回收。
        /// SDK callback 只產生新物件而不記錄呼叫；NoInlining 隔離 JIT 區域，GC 後三個弱參照皆失效。
        /// </summary>
        [Fact]
        public void CompletedOperation_DoesNotRetainQueryPageOrEntity()
        {
            var repository = CreateAfterOperation(out var query, out var page, out var row);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(query.IsAlive);
            Assert.False(page.IsAlive);
            Assert.False(row.IsAlive);
            GC.KeepAlive(repository);
        }

        /// <summary>
        /// 成功與故障皆不得把實體名稱、資料庫 ID 或原例外訊息寫到程序 Trace。
        /// listener 只計本測試的合成 marker，不保存任何訊息，finally 僅移除自己並 Dispose。
        /// </summary>
        [Fact]
        public void Trace_DoesNotPublishOperationDataOrRawFailure()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("p1_repository_marker", RecordId);
            sdk.SetupSequence(s => s.Create(entity)).Returns(RecordId).Throws(new TimeoutException("p1_repository_marker_private"));
            var listener = new MarkerListener();
            Trace.Listeners.Add(listener);
            try
            {
                var repository = new EntityRepository(sdk.Object);
                Assert.Equal(RecordId, repository.Create(entity));
                Assert.Throws<InvalidOperationException>(() => repository.Create(entity));
                Trace.Flush();
                Assert.Equal(0, listener.MatchCount);
            }
            finally
            {
                Trace.Listeners.Remove(listener);
                listener.Dispose();
            }
            Assert.Equal(1, listener.Disposals);
        }

        /// <summary>實際呼叫指定入口，不在 helper 模擬產品包裝或驗證邏輯。</summary>
        private static void Invoke(EntityRepository repository, string operation, Entity entity, QueryBase query)
        {
            if (operation == "create") repository.Create(entity);
            else if (operation == "update") repository.Update(entity);
            else if (operation == "delete") repository.Delete(entity.LogicalName, entity.Id);
            else if (operation == "retrieve") repository.Retrieve(entity.LogicalName, entity.Id);
            else if (operation == "multiple") repository.RetrieveMultiple(query);
            else repository.AssignOwner(entity.LogicalName, entity, OwnerId);
        }

        /// <summary>測試明定的固定公開操作訊息，不包含 logical name、ID 或故障本文。</summary>
        private static string Message(string operation) => operation == "create" ? "The entity could not be created."
            : operation == "update" ? "The entity could not be updated." : operation == "delete" ? "The entity could not be deleted."
            : operation == "retrieve" ? "The entity could not be retrieved."
            : operation == "multiple" ? "The entity collection could not be retrieved." : "The entity owner could not be assigned.";

        /// <summary>不使用 Moq 呼叫紀錄，避免測試替身自己保留待測物件而導致假陽性。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static EntityRepository CreateAfterOperation(out WeakReference queryReference, out WeakReference pageReference, out WeakReference rowReference)
        {
            var repository = new EntityRepository(new NonRetainingSdk());
            var query = new QueryExpression("contact");
            var page = repository.RetrieveMultiple(query);
            queryReference = new WeakReference(query);
            pageReference = new WeakReference(page);
            rowReference = new WeakReference(page.Entities[0]);
            return repository;
        }

        /// <summary>只在同步讀取產生新候選；不保存任何輸入、回應或 disposable 資源，其餘入口若被誤用立即失敗。</summary>
        private sealed class NonRetainingSdk : IOrganizationService
        {
            /// <summary>每次產生新頁與資料庫 ID，沒有 callback 或記錄集合。</summary>
            public EntityCollection RetrieveMultiple(QueryBase query) => new EntityCollection(new[] { new Entity("contact", RecordId) });
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public Guid Create(Entity entity) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public void Update(Entity entity) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public void Delete(string entityName, Guid id) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        }

        /// <summary>只保留有限計數器；不保存其他測試／使用者的 Trace 內容，生命周期由單一測試 finally 擁有。</summary>
        private sealed class MarkerListener : TraceListener
        {
            private int _matches;
            private int _disposals;
            /// <summary>以原子讀取觀察合成 marker 命中數。</summary>
            internal int MatchCount => Volatile.Read(ref _matches);
            /// <summary>驗證本測試擁有的 listener 恰釋放一次。</summary>
            internal int Disposals => Volatile.Read(ref _disposals);
            /// <summary>只比對合成字串；訊息離開此呼叫後不被保留。</summary>
            public override void Write(string? message)
            {
                if (message != null && (message.IndexOf("p1_repository_marker", StringComparison.Ordinal) >= 0
                    || message.IndexOf(RecordId.ToString(), StringComparison.Ordinal) >= 0))
                    Interlocked.Increment(ref _matches);
            }
            /// <summary>與 Write 使用同一有限計數，不建立診斷佇列。</summary>
            public override void WriteLine(string? message) => Write(message);
            /// <summary>僅增加處置計數後交基底清理；不接管全域 Trace 集合。</summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing) Interlocked.Increment(ref _disposals);
                base.Dispose(disposing);
            }
        }
    }
}

```

## ToolUtility.Tests/EntityOperations/RepositoryCallerRouteTests.cs
```
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.ListOperations;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.EntityOperations
{
    /// <summary>
    /// 執行既有 QueryService 五個入口與 MarketingListService 的真實已編譯委派鏈。
    /// 只替代外部 SDK，保護 Repository 跨 assembly 搬移的查詢形狀及整頁拒絕；不證明授權或實機 D365。
    /// </summary>
    public sealed class RepositoryCallerRouteTests
    {
        private static readonly Guid ContactId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ListId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>
        /// 六個消費點必須抵達同一 SDK 並保留各自投影／條件；正常回應完整返回，重複 ID 拒絕而不部分發布。
        /// 借入 SDK 的 Dispose 不被轉移，沒有外部設定、憑證或網路。
        /// </summary>
        /// <param name="route">實際產品消費點。</param>
        /// <param name="duplicate">是否注入同一消費頁的精確重複資料庫 ID。</param>
        [Theory]
        [InlineData("single", false)]
        [InlineData("collection", false)]
        [InlineData("fetch", false)]
        [InlineData("blood", false)]
        [InlineData("present", false)]
        [InlineData("list", false)]
        [InlineData("single", true)]
        [InlineData("collection", true)]
        [InlineData("fetch", true)]
        [InlineData("blood", true)]
        [InlineData("present", true)]
        [InlineData("list", true)]
        public void ActualCallers_PreserveQueryAndRejectDuplicatePage(string route, bool duplicate)
        {
            var table = route == "blood" ? "new_blood_report"
                : route == "present" ? "new_individual_meeting_and_spiritual_meditation_records"
                : route == "list" ? "listmember" : "contact";
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var page = new EntityCollection();
            page.Entities.Add(new Entity(table, ContactId) { ["fullname"] = "合成同名" });
            if (duplicate) page.Entities.Add(new Entity(table, ContactId));
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Callback<QueryBase>(query =>
            {
                if (route == "fetch")
                {
                    Assert.Equal("<fetch><entity name='contact'><attribute name='fullname'/></entity></fetch>",
                        Assert.IsType<FetchExpression>(query).Query);
                    return;
                }
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.Equal(table, expression.EntityName);
                Assert.True(expression.ColumnSet.AllColumns);
                if (route == "present")
                {
                    Assert.Equal(2, expression.Criteria.Conditions.Count);
                    Assert.Equal("new_contact_present_record", expression.Criteria.Conditions[0].AttributeName);
                    Assert.Equal(ContactId, Assert.Single(expression.Criteria.Conditions[0].Values));
                    Assert.Equal("new_sunday_date", expression.Criteria.Conditions[1].AttributeName);
                    Assert.Equal(ConditionOperator.GreaterEqual, expression.Criteria.Conditions[1].Operator);
                    Assert.IsType<DateTime>(Assert.Single(expression.Criteria.Conditions[1].Values));
                    Assert.Equal("new_sunday_date", Assert.Single(expression.Orders).AttributeName);
                }
                else
                {
                    var condition = Assert.Single(expression.Criteria.Conditions);
                    Assert.Equal(route == "blood" ? "new_blood_contact_relation" : route == "list" ? "listid" : "fullname",
                        condition.AttributeName);
                    Assert.Equal(ConditionOperator.Equal, condition.Operator);
                    Assert.Equal(route == "blood" ? (object)ContactId : route == "list" ? ListId : "合成同名",
                        Assert.Single(condition.Values));
                    if (route == "single") Assert.Equal(1, expression.TopCount);
                    if (route == "blood") Assert.Equal("createdon", Assert.Single(expression.Orders).AttributeName);
                }
            }).Returns(page);
            var repository = new EntityRepository(sdk.Object);
            var queryService = new QueryService(repository, sdk.Object);
            var listService = new MarketingListService(repository, sdk.Object);
            if (duplicate)
            {
                var failure = Assert.Throws<InvalidOperationException>(() => Invoke(route, queryService, listService));
                var repositoryFailure = Assert.IsType<InvalidOperationException>(failure.InnerException);
                Assert.Equal("The entity collection could not be retrieved.", repositoryFailure.Message);
                Assert.IsType<InvalidOperationException>(repositoryFailure.InnerException);
                Assert.Equal(2, page.Entities.Count);
            }
            else
            {
                var actual = Invoke(route, queryService, listService);
                if (route == "single" || route == "blood") Assert.Same(page.Entities[0], actual);
                else Assert.Same(page, actual);
            }
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            disposable.Verify(d => d.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>只選擇真實產品方法，不在 fixture 重建查詢或模擬 repository 呼叫。</summary>
        private static object Invoke(string route, QueryService service, MarketingListService lists)
        {
            if (route == "single") return service.RetrieveEntityByField("contact", "fullname", "合成同名");
            if (route == "collection") return service.RetrieveEntityCollectionByField("contact", "fullname", "合成同名");
            if (route == "fetch") return service.QueryByFetchXml("<fetch><entity name='contact'><attribute name='fullname'/></entity></fetch>");
            if (route == "blood") return service.QueryBloodReportByContactId(ContactId);
            if (route == "present") return service.QueryPresentRecordByContactIdAndSunday(ListId, ContactId, 3);
            return lists.RetrieveMembersByListId(ListId);
        }
    }
}

```

## wp-reports/P1-04-repository.md
```
# P1-04 Repository 抽取與驗證

日期：2026-11-05。此為 P1-04 的一個切片，主 P1 仍 **2/12**；不宣告整包完成。

## 需求對應與目前狀態

| 需求 | 實作 | 測試與證據 | 狀態 |
| --- | --- | --- | --- |
| 純 SDK 型別保持 namespace 並 git mv | `ToolUtility.Core/EntityOperations/EntityRepository.cs`，介面與類別同檔 | 原 host 六入口特性 `p1-04-repository-before/before.trx` 12/12；修後搬前連 Query 契約及實際 caller 98/98，之後才移動 | 已抽取，末端審查進行中 |
| 原驗證與 SDK 包裝相容 | 同上六方法 | 既有參數型別／ParamName／順序、Update 純替代鍵拒絕；12 案實際注入 SDK TimeoutException／ArgumentException，保留同一 InnerException、單次 I/O | 局部通過 |
| 欄位選擇不變 | `EntityQueryService.RetrieveEntity` 三參數 overload | 新 API 缺口 2 紅；null／省略 AllColumns、指定 ColumnSet 原參照、原二參數仍存在 | 局部通過 |
| 單筆與整頁穩定 ID | 共用既有 CRUD／Owner／Query | 空 Create ID、錯單筆 ID／表名、空／重複／跨表頁拒絕，原候選不裁剪，同名不同 ID 完整保留 | 局部通過 |
| 原消費點可運作 | `RepositoryCallerRouteTests.cs` | QueryService 五入口與 MarketingListService 一入口，每入口正常／重複 ID，共12案，實際產品方法→Repository→SDK；不讀設定／憑證 | 離線路徑通過 |
| 隔離及生命週期 | Repository 與操作局部 query adapter | A/B 1000次、59次故障後明確重試、2000個成功頁、active=0、借入Dispose=0；弱參照Query／頁／Entity可回收；listener finally釋放 | 局部通過 |
| 雙目標與strict | Core與`CoreTargetFrameworkTests` | net48實載net462 362/362；net10 456/456；獨立各一份coverage 99.42%／100%；strict雙目標0警告／0錯誤 | 通過 |
| 完整方案及00§8 | 全方案、架構、打包、encoding、i18n等 | 執行中，尚不借用上一切片2056當成本輪證據 | 待補 |
| Gemini＋Claude審查 | 專案自癒入口 | 分析191821雙模型完整、無quota降級；實作審查待結果 | 待補 |

## 行為決策與分析核實

- 原始基準 `b9f84d01`；原檔 SHA256、搬前TRX保存在
  `artifacts/p1-04-repository-before/baseline.json`，先特性綠再新增安全紅綠。
- 安全紅測34案中30失敗／4通過；另2個公開overload缺口紅測。未以編譯失敗、略過測試或放寬斷言冒充紅綠。
- 保留既有明確參數檢查在try前。Claude分析建議「catch ArgumentException一律重拋」不採：
  原host沒有檢查的新邊界原本即由SDK失敗包裝，SDK自身也可能拋ArgumentException。
  新測試對六操作各注入這種真SDK例外，確保仍包裝同一例外；不是依型別猜故障來源。
- 安全差異明列：移除含名稱／ID／原訊息的Trace；外層固定英文操作訊息；
  Create／Update缺名稱、Assign空ID／名稱不一致在SDK前拒絕；
  讀取缺失／錯識別、整頁空／重複／跨表ID拒絕，FetchXML必須有唯一合法根實體且禁DTD。
  新adapter拒絕仍沿用操作失敗包裝，InnerException保留可診斷型別。
- Update繼續要求非空資料庫ID，即使Core CRUD支援純替代鍵也不放寬舊Repository契約。
- 搜尋目前消費鏈只有QueryService五個及MarketingListService一個RetrieveMultiple呼叫，
  沒有產品直接new EntityRepository或經Repository指派的caller。十二案執行這六個實際消費點；
  不將此結果宣稱全部業務分支、DI、授權或實機D365已通過。
- 三參數overload加在具體QueryService上，不改既有IEntityQueryService要求，避免破壞外部實作者。
  QueryExpression.PageInfo缺失才補5000／第一頁；已指定的PageInfo／cookie與Query原參照保留。
  補值會直接寫入caller的可變QueryExpression，XML文件已明載，呼叫中不得跨操作共用。
- 記錄入口不支援缺乏正式資料庫ID的聚合投影；不得生成暫存GUID讓聚合列混入持久化記錄集合。
  已另加合成count列測試，驗證拒絕且不改ID／候選；目前六個消費點的形狀均為一般記錄查詢。
- Repository只限host已授權SDK scope，CRUD／Owner借用同一SDK、無狀態marker不保留。
  Query adapter每次using，沒有新增既有caller必須Dispose的Repository生命週期；
  不建立static SDK、池、cache、背景工作、通知器或隱藏重試。
- 真正故障以真InnerException交host共用通知邊界；Core不建立LINE或記錄故障本文。
  呼叫端仍須先做伺服器授權，表名與ID驗證不代表跨租戶授權。
- 首次strict搬後建置遇4個CA1510，按既有雙目標模式：
  net10使用ThrowIfNull、Framework保留顯式ArgumentNullException；沒有NoWarn或降低分析器。
  舊host測試原有17個warning保留，新增測試的1個nullable warning已補NotNull並消除。
- 首次I18nCheck抓到搬入Core的13個舊中文診斷常值，未更新或放寬掃描基準；
  改固定英文診斷後Core為0、I18nCheck verify通過。例外型別、驗證位置／ParamName及InnerException不變。
- 第一輪193656 reviewer為完整雙模型、無quota降級、無Critical／High。
  Claude兩個Warning分別為聚合投影與PageInfo原地改寫的文件清晰度，已補上述XML／報告及聚合案例。
  Gemini的marker／介面建議、Claude的pragma／配置成本為非阻斷Info，維持刻意相容與局部所有權設計。
  當時快照仍是中文診斷；最終英文化與補證將另外經雙模型複審，不冒稱舊review覆蓋新來源。

## 交付界線

本輪沒有操作VM／D365／LINE，沒有push或正式部署，沒有修改正式計畫，因此不需Word修正報告。
保留使用者原有付款測試及appsettings修改，僅比對雜湊、不輸出設定內容。
目前Goal工具仍usageLimited，未覆蓋，也不宣稱在背景持續執行。

```

## 本地已核TRX摘要
```json
{
  "reports": 16,
  "totals": {
    "total": 2167,
    "executed": 2167,
    "passed": 2167,
    "failed": 0,
    "notExecuted": 0
  },
  "runs": [
    {
      "file": "solution_net10.0_20261105194533.trx",
      "modules": [
        "LineMessagingProcessor.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "79",
        "executed": "79",
        "passed": "79",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net10.0_20261105194534.trx",
      "modules": [
        "LineMessagingProcessor.RichMenus.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "34",
        "executed": "34",
        "passed": "34",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net10.0_20261105194535.trx",
      "modules": [
        "SpeechMessage.Payments.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "55",
        "executed": "55",
        "passed": "55",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net10.0_20261105194536.trx",
      "modules": [
        "LineMessagingProcessor.AspNetCore.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "4",
        "executed": "4",
        "passed": "4",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net10.0_20261105194537.trx",
      "modules": [
        "LineMessagingProcessor.Workflows.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "33",
        "executed": "33",
        "passed": "33",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net10.0_20261105194538.trx",
      "modules": [
        "Line.Messaging.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "101",
        "executed": "101",
        "passed": "101",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net10.0_20261105194539.trx",
      "modules": [
        "ToolUtility.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "457",
        "executed": "457",
        "passed": "457",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net10.0_20261105194540.trx",
      "modules": [
        "SpeechMessage.Dynamics.Deployment.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "62",
        "executed": "62",
        "passed": "62",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net10.0_20261105194541.trx",
      "modules": [
        "ToolUtility.Dataverse.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "79",
        "executed": "79",
        "passed": "79",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net10.0_20261105194542.trx",
      "modules": [
        "ChurchReport.MemberInfo.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "623",
        "executed": "623",
        "passed": "623",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net10.0_20261105194609.trx",
      "modules": [
        "SpeechMessage.Localization.Tests.dll"
      ],
      "target": "net10.0",
      "counters": {
        "total": "67",
        "executed": "67",
        "passed": "67",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net48_20261105194534.trx",
      "modules": [
        "SpeechMessage.Localization.Tests.dll"
      ],
      "target": "net48",
      "counters": {
        "total": "43",
        "executed": "43",
        "passed": "43",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net48_20261105194535.trx",
      "modules": [
        "Line.Messaging.Tests.dll"
      ],
      "target": "net48",
      "counters": {
        "total": "96",
        "executed": "96",
        "passed": "96",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net48_20261105194536.trx",
      "modules": [
        "LineMessagingProcessor.Tests.dll"
      ],
      "target": "net48",
      "counters": {
        "total": "46",
        "executed": "46",
        "passed": "46",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net48_20261105194537.trx",
      "modules": [
        "SpeechMessage.Dynamics.Plugins.Tests.dll"
      ],
      "target": "net48",
      "counters": {
        "total": "25",
        "executed": "25",
        "passed": "25",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "solution_net48_20261105194538.trx",
      "modules": [
        "ToolUtility.Tests.dll"
      ],
      "target": "net48",
      "counters": {
        "total": "363",
        "executed": "363",
        "passed": "363",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    }
  ]
}
```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.