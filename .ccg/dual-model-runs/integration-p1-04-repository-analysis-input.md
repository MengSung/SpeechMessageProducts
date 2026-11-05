Active task: .trellis/tasks/11-05-integration-p1。P1-04接續EntityRepository抽取的雙模型唯讀分析，只依以下來源，禁止修改/執行/擴讀或再呼模型。主會話inline實作，不派implement/check代理。不能讀Facade、appsettings、secrets，不連D365/LINE。
正式P1目標：只SDK/BCL資料操作抽Core雙產品net462/net10，namespace與公開既有API來源相容。host借入SDK先驗證授權/組織/使用者，Core不授權、不Dispose借入、不保留request/result/logger/租戶狀態。主P1仍2/12，本切片不等整個P1完成。原EntityRepository只有Trace BCL診斷而非真host依賴，不能因可移除Trace永久排除純資料候選。
現況：介面和class在同一host檔；6方法Create/Update/Delete/Retrieve(optional ColumnSet)/RetrieveMultiple/AssignOwner，Trace包含ID/原Exception.Message，不可帶入Core。既有QueryService與MarketingListService注入IEntityRepository，不改其業務。未找到EntityRepository專屬測試，須先在原host跑成功特性再安全紅綠最後git mv整檔。
候選設計供核對：保留各public方法既有引數null/空白/Guid檢查位置及型別；真SDK故障仍用原InvalidOperationException wrapper但固定安全外層訊息、InnerException同一instance，host通知不得記Message/ToString/Data。Create空ID、錯單筆ID/根實體、整頁duplicate/空ID新增fail-closed有意安全差異；不同ID同名保留，不去重不部分回傳。Update仍保留舊規則ID必非空，不擴成alternate-key-only，避免無聲放寬。
盡量重用現有Core EntityCrudService/OwnerManagementService/EntityQueryService，不重複SDK identity/Fetch parser。必要在EntityQueryService增加公有3參數RetrieveEntity(entityName,id,ColumnSet?) overload保留既有2參數；2參數委派3參數，全欄預設維持，caller傳ColumnSet實例/欄位原樣不修改。此overload必須在搬前host可用，不用InternalsVisibleTo跨assembly呼internal。
Repository建構時建立兩個Core CRUD/Owner adapter（用一個局部非null marker滿足舊logger ctor且都不保存）；只存與同一SDK scope同生命的adapter/借入SDK，不建cache/timer/tasks。Retrieve/Multiple用操作局部EntityQueryService確定性using（只清參照），避免Repo新增未被既有host消費者Dispose的生命週期。每次多一小型查詢物件沿用先前extension設計，不為省此分配建立static/跨租戶池。需評估其他更小且不複製邏輯的方案。
請提出具體相容性風險、必要特性golden/完整ColumnSet與paging/同名ID/故障retry/並行A/B與resource drain/借入ownership測試。依Critical/Warning/Info及建議實作步驟輸出；不要把舊unsafe Trace保留當相容義務，不要求net48產品或把Mock當實機。

## ToolUtility/EntityOperations/EntityRepository.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/EntityOperations/EntityRepository.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IEntityRepository、class EntityRepository
// 主要成員：Create、Update、Delete、Retrieve、RetrieveMultiple、AssignOwner
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、System、System.Diagnostics
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Diagnostics;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// Entity Repository 介面
    /// 遵循 Repository Pattern，專責 Entity CRUD 操作
    /// </summary>
    public interface IEntityRepository
    {
        /// <summary>建立實體</summary>
        Guid Create(Entity entity);

        /// <summary>更新實體</summary>
        void Update(Entity entity);

        /// <summary>刪除實體</summary>
        void Delete(string entityName, Guid entityId);

        /// <summary>檢索單一實體</summary>
        Entity Retrieve(string entityName, Guid entityId, ColumnSet columnSet = null);

        /// <summary>檢索多個實體</summary>
        EntityCollection RetrieveMultiple(QueryBase query);

        /// <summary>指派實體擁有者</summary>
        void AssignOwner(string entityName, Entity entity, Guid newOwnerId);
    }

    /// <summary>
    /// Entity Repository 實現
    /// 使用 Dependency Injection 注入 IOrganizationService
    /// </summary>
    public class EntityRepository : IEntityRepository
    {
        private readonly IOrganizationService _organizationService;

        /// <summary>
        /// 建構函數 - 注入 IOrganizationService
        /// </summary>
        public EntityRepository(IOrganizationService organizationService)
        {
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 建立實體
        /// </summary>
        public Guid Create(Entity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            try
            {
                Trace.WriteLine($"[EntityRepository] Creating {entity.LogicalName}");
                var id = _organizationService.Create(entity);
                Trace.WriteLine($"[EntityRepository] Created successfully. ID: {id}");
                return id;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] Create failed: {ex.Message}");
                throw new InvalidOperationException($"建立 {entity.LogicalName} 失敗", ex);
            }
        }

        /// <summary>
        /// 更新實體
        /// </summary>
        public void Update(Entity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            if (entity.Id == Guid.Empty)
                throw new ArgumentException("Entity ID 不可為空", nameof(entity));

            try
            {
                Trace.WriteLine($"[EntityRepository] Updating {entity.LogicalName} (ID: {entity.Id})");
                _organizationService.Update(entity);
                Trace.WriteLine($"[EntityRepository] Updated successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] Update failed: {ex.Message}");
                throw new InvalidOperationException($"更新 {entity.LogicalName} 失敗", ex);
            }
        }

        /// <summary>
        /// 刪除實體
        /// </summary>
        public void Delete(string entityName, Guid entityId)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name 不可為空", nameof(entityName));

            if (entityId == Guid.Empty)
                throw new ArgumentException("Entity ID 不可為空", nameof(entityId));

            try
            {
                Trace.WriteLine($"[EntityRepository] Deleting {entityName} (ID: {entityId})");
                _organizationService.Delete(entityName, entityId);
                Trace.WriteLine($"[EntityRepository] Deleted successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] Delete failed: {ex.Message}");
                throw new InvalidOperationException($"刪除 {entityName} 失敗", ex);
            }
        }

        /// <summary>
        /// 檢索單一實體
        /// </summary>
        public Entity Retrieve(string entityName, Guid entityId, ColumnSet columnSet = null)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name 不可為空", nameof(entityName));

            if (entityId == Guid.Empty)
                throw new ArgumentException("Entity ID 不可為空", nameof(entityId));

            try
            {
                columnSet ??= new ColumnSet(true);
                Trace.WriteLine($"[EntityRepository] Retrieving {entityName} (ID: {entityId})");
                var entity = _organizationService.Retrieve(entityName, entityId, columnSet);
                Trace.WriteLine($"[EntityRepository] Retrieved successfully");
                return entity;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] Retrieve failed: {ex.Message}");
                throw new InvalidOperationException($"檢索 {entityName} 失敗", ex);
            }
        }

        /// <summary>
        /// 檢索多個實體
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            try
            {
                Trace.WriteLine($"[EntityRepository] RetrieveMultiple executing query");
                var results = _organizationService.RetrieveMultiple(query);
                Trace.WriteLine($"[EntityRepository] Retrieved {results.Entities.Count} entities");
                return results;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] RetrieveMultiple failed: {ex.Message}");
                throw new InvalidOperationException("檢索多個實體失敗", ex);
            }
        }

        /// <summary>
        /// 指派實體擁有者
        /// </summary>
        public void AssignOwner(string entityName, Entity entity, Guid newOwnerId)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name 不可為空", nameof(entityName));

            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            if (newOwnerId == Guid.Empty)
                throw new ArgumentException("New Owner ID 不可為空", nameof(newOwnerId));

            try
            {
                Trace.WriteLine($"[EntityRepository] Assigning {entityName} to owner {newOwnerId}");

                var assignRequest = new Microsoft.Crm.Sdk.Messages.AssignRequest
                {
                    Assignee = new EntityReference("systemuser", newOwnerId),
                    Target = new EntityReference(entityName, entity.Id)
                };

                _organizationService.Execute(assignRequest);
                Trace.WriteLine($"[EntityRepository] Owner assigned successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] AssignOwner failed: {ex.Message}");
                throw new InvalidOperationException($"指派 {entityName} 擁有者失敗", ex);
            }
        }
    }
}

```

## ToolUtility.Core/EntityOperations/EntityCrudService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 純 SDK 單次寫入轉接器；只借用 host 已驗證組織、使用者及授權範圍的服務，不建立連線或共用資料。
    /// 輸入與輸出只在同步呼叫存活，不保存 Entity、logger、Session、token 或 callback。
    /// </summary>
    /// <remarks>
    /// 實例最長與 SDK 原授權 scope 同生命週期，禁止跨租戶 singleton；SDK 的逾時與 Dispose 仍由 host 擁有。
    /// 不隱藏重試或包裝 SDK 例外，真實故障交回 host 的共用通知邊界。
    /// 此底層 API 不推測業務事件鍵；caller 必須先以資料庫唯一鍵／替代鍵確保跨程序冪等，不以顯示名稱去重。
    /// </remarks>
    public class EntityCrudService : IEntityCrudService
    {
        private readonly IOrganizationService _organizationService;

        /// <summary>保留舊建構簽章及 null 拒絕，不保存未使用 logger，避免延長 request／憑證的生命。</summary>
        /// <param name="logger">來源相容非 null 參數，不使用、保存或 Dispose。</param>
        /// <param name="organizationService">host 擁有且已授權的 SDK；不可在同步呼叫中途關閉。</param>
        public EntityCrudService(object logger, IOrganizationService organizationService)
        {
            _ = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>只執行一次 Create，驗證回傳非空資料庫 ID 後才回報成功；不修改 caller 的 Entity 或自行重試。</summary>
        /// <param name="entityToCreate">操作獨占的已授權候選，可未預先指定 ID；呼叫中不能由其他執行緒修改。</param>
        /// <returns>SDK 確認的非空資料庫 ID。</returns>
        /// <exception cref="InvalidOperationException">SDK 未提供有效 ID；寫入狀態不確定，須以原冪等鍵查證，不可盲目新增。</exception>
        public Guid CreateEntity(Entity entityToCreate)
        {
            var candidate = entityToCreate ?? throw new ArgumentNullException(nameof(entityToCreate));
            RequireName(candidate.LogicalName, nameof(entityToCreate));
            var identity = _organizationService.Create(candidate);
            if (identity == Guid.Empty) throw new InvalidOperationException("The SDK returned no created database identity.");
            return identity;
        }

        /// <summary>以既有資料庫 ID 或伺服器替代索引鍵更新一次；缺少兩者即在 I/O 前拒絕。</summary>
        /// <param name="entityToUpdate">已授權、操作獨占的候選；屬性、ID、KeyAttributes 完整交給 SDK。</param>
        public void UpdateEntity(Entity entityToUpdate)
        {
            var candidate = entityToUpdate ?? throw new ArgumentNullException(nameof(entityToUpdate));
            RequireName(candidate.LogicalName, nameof(entityToUpdate));
            if (candidate.Id == Guid.Empty && candidate.KeyAttributes.Count == 0)
                throw new ArgumentException("A database identity or server alternate key is required.", nameof(entityToUpdate));
            _organizationService.Update(candidate);
        }

        /// <summary>刪除 caller 已授權的精確非空 ID；不查詢、合併或刪除同名資料。</summary>
        /// <param name="entityName">伺服器決定的實體 logical name；不是授權依據。</param>
        /// <param name="entityId">已驗證屬於當前操作範圍的非空資料庫 ID。</param>
        public void DeleteEntity(string entityName, Guid entityId)
        {
            if (string.IsNullOrEmpty(entityName))
                throw new ArgumentNullException(nameof(entityName));
            RequireName(entityName, nameof(entityName));
            if (entityId == Guid.Empty) throw new ArgumentException("A database identity is required.", nameof(entityId));
            _organizationService.Delete(entityName, entityId);
        }

        /// <summary>型別化寫入不拼 XML；只拒絕缺少名稱，合法名稱與權限仍須由 SDK metadata／host 驗證。</summary>
        private static void RequireName(string name, string parameter)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An entity name is required.", parameter);
        }
    }
}

```

## ToolUtility.Core/OwnerOperations/OwnerManagementService.cs
```
using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.OwnerOperations
{
    /// <summary>
    /// 以原 SDK ownerid／AssignRequest 處理已授權的單筆資料；不保存實體、logger 或使用者可變狀態。
    /// 實例限於 SDK 原授權 scope；借入連線、憑證與 Dispose 由 host 擁有，不建立背景工作。
    /// </summary>
    /// <remarks>
    /// 缺省值不是故障；型別錯誤與真正 SDK 失敗直接上傳 host 共用通知入口。
    /// 不把實體名稱、ID 或原始 Exception.Message 拼成新訊息，也不 catch 吞掉失敗。
    /// 指派不是授權機制，caller 須先驗證當前組織、目標記錄與受派者權限。
    /// </remarks>
    public class OwnerManagementService : IOwnerManagementService
    {
        private readonly IOrganizationService _organizationService;

        /// <summary>保留舊建構及 null 拒絕；未用 logger 不保存，SDK 僅借用且不在服務內 Dispose。</summary>
        /// <param name="logger">來源相容非 null 參數，不使用或持有。</param>
        /// <param name="organizationService">已授權的 caller-owned SDK，須存活至所有同步操作返回。</param>
        public OwnerManagementService(object logger, IOrganizationService organizationService)
        {
            _ = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>只讀操作獨占實體的 ownerid；缺值沿用 Guid.Empty，不呼叫 SDK 或保存 EntityReference。</summary>
        /// <param name="entity">已授權非 null 實體，呼叫中不可並行修改。</param>
        /// <returns>原 owner 資料庫 ID，或缺省的 Guid.Empty。</returns>
        public Guid GetOwnerId(Entity entity)
        {
            var candidate = entity ?? throw new ArgumentNullException(nameof(entity));
            return candidate.GetAttributeValue<EntityReference>("ownerid")?.Id ?? Guid.Empty;
        }

        /// <summary>回傳既有 owner 顯示名稱或空字串；名稱不是 ID、授權或跨記錄去重鍵。</summary>
        /// <param name="entity">已授權的操作獨占實體，不保存其屬性。</param>
        /// <returns>僅供本次 caller 顯示的名稱，不寫入診斷或共用快取。</returns>
        public string GetOwnerName(Entity entity)
        {
            var candidate = entity ?? throw new ArgumentNullException(nameof(entity));
            return candidate.GetAttributeValue<EntityReference>("ownerid")?.Name ?? string.Empty;
        }

        /// <summary>
        /// 保留 systemuser 指派語義；目標實體一致、兩個 ID 非空才 Execute 一次。
        /// 原例外直接交 caller，不包裝、回顯個資或重試寫入，也不修改 caller 的 Entity。
        /// </summary>
        /// <param name="entityName">須與 entity.LogicalName 完全一致的伺服器實體名稱。</param>
        /// <param name="entity">已授權的持久化記錄，不使用暫存 draft 身分。</param>
        /// <param name="ownerId">同組織內已授權的 systemuser 資料庫 ID。</param>
        public void AssignOwner(string entityName, Entity entity, Guid ownerId)
        {
            var candidate = entity ?? throw new ArgumentNullException(nameof(entity));
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentNullException(nameof(entityName));
            if (!string.Equals(entityName, candidate.LogicalName, StringComparison.Ordinal))
                throw new ArgumentException("The target entity name is missing or inconsistent.", nameof(entityName));
            if (candidate.Id == Guid.Empty) throw new ArgumentException("A database identity is required.", nameof(entity));
            if (ownerId == Guid.Empty) throw new ArgumentException("An owner database identity is required.", nameof(ownerId));
            var assign = new AssignRequest
            {
                Assignee = new EntityReference("systemuser", ownerId),
                Target = new EntityReference(entityName, candidate.Id)
            };
            _organizationService.Execute(assign);
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
