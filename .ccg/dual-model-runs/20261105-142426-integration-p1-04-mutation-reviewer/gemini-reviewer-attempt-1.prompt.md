ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: integration-p1-04-mutation

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
P1-04 CRUD/Owner抽取的雙模型唯讀審查。禁止執行、擴讀任何檔案/機密、修改或再呼叫模型。依既有抽取分析20261105-122655實作；僅產品原namespace保持不變、移到Core雙產品net462/net10，test宿主net48/net10並反射實net462。正式P1不移業務；此處只單次SDK轉接，host事先授權並擁有借入SDK。legacy logger參數保持null拒絕但不保存。
正常特性7/7先在舊host綠；新增安全22案14失敗/8通過，重現空/錯目標仍送SDK、owner包裝原message與改掉例外、logger retention等。修後搬前29/29，再git mv四檔。Core雙目標strict 0警告0錯誤；最新全ToolUtility net48(實net462)244/244、net10 324/324，無失敗略過；含新加四個null ctor與並行A/B1000次Create+1000次Assign，借入SDKDispose0、外層Task全部await。覆蓋第一目標98.91%，另一目標run執行中，不預判。
有意安全差異：新增/更新空名稱拒絕，更新允許原SDK的alternate key但無ID且無key拒絕，Create回空ID視狀態不確定不能盲目重試。Owner空輸入現是ArgumentException(預期拒絕)，真正SDKException原樣交host共用通知、不再製作含實體/ID/Message的包裝。原Exception本身仍可能有provider私人訊息，不能宣稱Exception.ToString安全；host notifier已契約不記Message/Data/ToString。此底層不推測業務冪等鍵，也不自行加Upsert重寫Create語義；caller需DB唯一鍵，不是本機鎖。所有測試是離線，不操作帳號或實機寫入。
請檢查正確性/相容性/生命週期/跨scope/安全/繁中維護註解，Critical/High/Warning/Info分類；舊語義差異有安全理由，不要求繼續保留資料外洩包裝。不能要求產品改net48或把Mock當實機。

## 搬前 ToolUtility/EntityOperations/EntityCrudService.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/EntityOperations/EntityCrudService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class EntityCrudService
// 主要成員：CreateEntity、UpdateEntity、DeleteEntity
// 引用命名空間：System、ToolUtilityNameSpace.Interfaces、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using ToolUtilityNameSpace.Interfaces;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.EntityOperations
{
    public class EntityCrudService : IEntityCrudService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public EntityCrudService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        public Guid CreateEntity(Entity entityToCreate)
        {
            if (entityToCreate == null) throw new ArgumentNullException(nameof(entityToCreate));
            return _organizationService.Create(entityToCreate);
        }

        public void UpdateEntity(Entity entityToUpdate)
        {
            if (entityToUpdate == null) throw new ArgumentNullException(nameof(entityToUpdate));
            _organizationService.Update(entityToUpdate);
        }

        public void DeleteEntity(string entityName, Guid entityId)
        {
            if (string.IsNullOrEmpty(entityName)) throw new ArgumentNullException(nameof(entityName));
            if (entityId == Guid.Empty) throw new ArgumentException("entityId cannot be empty", nameof(entityId));
            _organizationService.Delete(entityName, entityId);
        }
    }
}

```

## 現行 ToolUtility.Core/EntityOperations/EntityCrudService.cs
```csharp
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

## 搬前 ToolUtility/EntityOperations/IEntityCrudService.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/EntityOperations/IEntityCrudService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IEntityCrudService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.EntityOperations
{
    public interface IEntityCrudService
    {
        Guid CreateEntity(Entity entityToCreate);
        void UpdateEntity(Entity entityToUpdate);
        void DeleteEntity(string entityName, Guid entityId);
    }
}

```

## 現行 ToolUtility.Core/EntityOperations/IEntityCrudService.cs
```csharp
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 已授權 SDK scope 的單次寫入契約，不管理連線、保存輸入或提供隱藏重試。
    /// caller 須以伺服器業務事件唯一鍵保護冪等，並在最終失敗邊界將原 Exception 交共用通知入口。
    /// </summary>
    public interface IEntityCrudService
    {
        /// <summary>新增操作獨占候選，成功才回傳非空資料庫 ID；不以姓名等內容去重。</summary>
        /// <param name="entityToCreate">已完成授權及冪等設計的候選，不移轉資源所有權。</param>
        /// <returns>SDK 確認的資料庫 ID。</returns>
        Guid CreateEntity(Entity entityToCreate);

        /// <summary>更新具 ID 或伺服器替代鍵的單筆候選，原 SDK 故障直接向 caller 傳遞。</summary>
        /// <param name="entityToUpdate">已授權、不可並行修改的候選，服務不保存其參照。</param>
        void UpdateEntity(Entity entityToUpdate);

        /// <summary>刪除範圍內的精確非空 ID，不自行搜尋同名資料或執行批次清理。</summary>
        /// <param name="entityName">伺服器已驗證的實體名稱。</param>
        /// <param name="entityId">已授權的資料庫 ID。</param>
        void DeleteEntity(string entityName, Guid entityId);
    }
}

```

## 搬前 ToolUtility/OwnerOperations/OwnerManagementService.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/OwnerOperations/OwnerManagementService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class OwnerManagementService
// 主要成員：GetOwnerId、GetOwnerName、AssignOwner
// 引用命名空間：Microsoft.Crm.Sdk.Messages、Microsoft.Xrm.Sdk、System、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.OwnerOperations
{
    /// <summary>
    /// 負責人管理服務實作
    /// 處理實體的 Owner 相關操作
    /// </summary>
    public class OwnerManagementService : IOwnerManagementService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public OwnerManagementService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 取得實體的負責人 ID
        /// </summary>
        public Guid GetOwnerId(Entity entity)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                var ownerRef = entity.GetAttributeValue<EntityReference>("ownerid");
                if (ownerRef == null)
                    return Guid.Empty;

                return ownerRef.Id;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error getting owner ID for entity '{entity?.LogicalName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 取得實體的負責人名稱
        /// </summary>
        public string GetOwnerName(Entity entity)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                var ownerRef = entity.GetAttributeValue<EntityReference>("ownerid");
                if (ownerRef == null)
                    return string.Empty;

                return ownerRef.Name ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error getting owner name for entity '{entity?.LogicalName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 指派負責人給實體
        /// </summary>
        public void AssignOwner(string entityName, Entity entity, Guid ownerId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(entityName))
                    throw new ArgumentNullException(nameof(entityName));

                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                if (ownerId == Guid.Empty)
                    throw new ArgumentException("Owner ID cannot be empty", nameof(ownerId));

                var assign = new AssignRequest
                {
                    Assignee = new EntityReference("systemuser", ownerId),
                    Target = new EntityReference(entityName, entity.Id)
                };

                _organizationService.Execute(assign);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error assigning owner for entity '{entityName}' (ID: {entity?.Id}): {ex.Message}", ex);
            }
        }
    }
}

```

## 現行 ToolUtility.Core/OwnerOperations/OwnerManagementService.cs
```csharp
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
            if (string.IsNullOrWhiteSpace(entityName) || !string.Equals(entityName, candidate.LogicalName, StringComparison.Ordinal))
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

## 搬前 ToolUtility/Interfaces/IOwnerManagementService.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Interfaces/IOwnerManagementService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IOwnerManagementService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：Microsoft.Xrm.Sdk、System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtilityNameSpace.Interfaces
{
    /// <summary>
    /// 負責人管理服務介面
    /// 處理實體的 Owner 相關操作
    /// </summary>
    public interface IOwnerManagementService
    {
        /// <summary>
        /// 取得實體的負責人 ID
        /// </summary>
        Guid GetOwnerId(Entity entity);

        /// <summary>
        /// 取得實體的負責人名稱
        /// </summary>
        string GetOwnerName(Entity entity);

        /// <summary>
        /// 指派負責人給實體
        /// </summary>
        void AssignOwner(string entityName, Entity entity, Guid ownerId);
    }
}

```

## 現行 ToolUtility.Core/Interfaces/IOwnerManagementService.cs
```csharp
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.Interfaces
{
    /// <summary>
    /// 已驗證 SDK scope 的負責人讀取／指派契約，不處理登入或授權、不保存實體或憑證。
    /// 真正失敗交 caller 最終共用通知邊界，不隱藏重試或以顯示名稱取代資料庫身分。
    /// </summary>
    public interface IOwnerManagementService
    {
        /// <summary>讀取 ownerid 的資料庫 ID，缺值為 Guid.Empty，不呼叫 SDK。</summary>
        /// <param name="entity">已授權的操作獨占實體。</param>
        /// <returns>原 ID 或缺省值，不作 caller 授權判定。</returns>
        Guid GetOwnerId(Entity entity);

        /// <summary>讀取既有顯示名稱，缺值回空字串，不記錄或共用此文字。</summary>
        /// <param name="entity">已授權非 null 實體。</param>
        /// <returns>操作生命週期內供 caller 使用的顯示名稱。</returns>
        string GetOwnerName(Entity entity);

        /// <summary>一次 AssignRequest 指派 systemuser；目標名稱一致且兩 ID 非空才允許 I/O。</summary>
        /// <param name="entityName">伺服器決定且與 entity 一致的 logical name。</param>
        /// <param name="entity">已授權且具資料庫 ID 的實體，不移轉所有權。</param>
        /// <param name="ownerId">已驗證同組織的 systemuser ID。</param>
        void AssignOwner(string entityName, Entity entity, Guid ownerId);
    }
}

```

## ToolUtility.Tests/DataOperations/MutationOperationsCharacterizationTests.cs
```
using System;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.OwnerOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 搬移前鎖定 CRUD 與負責人操作的正常 SDK 邊界；所有識別與內容皆為固定合成值。
    /// 替身只記錄實際產品送出的參數，不連 D365、不代替業務授權或宣稱資料庫冪等。
    /// </summary>
    public sealed class MutationOperationsCharacterizationTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OwnerId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>三個寫入入口必須只呼叫一次 SDK 且不改動 caller 實體；實際請求與搬前黃金檔完全相符。</summary>
        /// <param name="operation">合成的 create、update 或 delete 操作。</param>
        [Theory]
        [InlineData("create")]
        [InlineData("update")]
        [InlineData("delete")]
        public void Crud_ValidInput_PreservesSdkRequest(string operation)
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
            var service = new EntityCrudService(new object(), sdk.Object);
            if (operation == "create") Assert.Equal(RecordId, service.CreateEntity(entity));
            else if (operation == "update") service.UpdateEntity(entity);
            else service.DeleteEntity("contact", RecordId);
            Assert.NotNull(actual);
            GoldenQueryAssertions.Equal("mutation-" + operation + ".xml", actual!.ToString());
            Assert.Equal(RecordId, entity.Id);
            Assert.Equal("合成同名", entity.GetAttributeValue<string>("fullname"));
            sdk.VerifyAll();
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>只讀 ownerid 不呼叫 SDK；缺值沿用 Empty／空字串，合法顯示名稱原樣回傳而不是去重鍵。</summary>
        /// <param name="present">是否有 ownerid。</param>
        /// <param name="name">合成 owner 顯示名稱，可缺省。</param>
        [Theory]
        [InlineData(false, null)]
        [InlineData(true, null)]
        [InlineData(true, "合成負責人")]
        public void Owner_ReadOnly_PreservesMissingAndPresentValues(bool present, string? name)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("contact", RecordId);
            if (present) entity["ownerid"] = new EntityReference("systemuser", OwnerId) { Name = name };
            var service = new OwnerManagementService(new object(), sdk.Object);
            Assert.Equal(present ? OwnerId : Guid.Empty, service.GetOwnerId(entity));
            Assert.Equal(name ?? string.Empty, service.GetOwnerName(entity));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>指派仍以 systemuser 為受派者、原資料庫 ID 為 Target，不能誤換為顯示名稱或新識別。</summary>
        [Fact]
        public void Owner_Assign_PreservesTypedRequest()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            AssignRequest? actual = null;
            sdk.Setup(s => s.Execute(It.IsAny<AssignRequest>()))
                .Callback<OrganizationRequest>(request => actual = (AssignRequest)request)
                .Returns(new AssignResponse());
            var entity = new Entity("contact", RecordId);
            new OwnerManagementService(new object(), sdk.Object).AssignOwner("contact", entity, OwnerId);
            Assert.NotNull(actual);
            var xml = new XElement("assign",
                new XElement("target", new XAttribute("name", actual!.Target.LogicalName), new XAttribute("id", actual.Target.Id)),
                new XElement("assignee", new XAttribute("name", actual.Assignee.LogicalName), new XAttribute("id", actual.Assignee.Id)));
            GoldenQueryAssertions.Equal("mutation-assign.xml", xml.ToString());
            Assert.False(entity.Contains("ownerid"));
            sdk.Verify(s => s.Execute(It.IsAny<AssignRequest>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>把實際 SDK 收到的實體投影成固定可稽核 XML；只處理此 fixture 的 fullname，不放入連線或真實資料。</summary>
        private static XElement Describe(string operation, Entity entity)
        {
            var xml = new XElement(operation, new XAttribute("name", entity.LogicalName), new XAttribute("id", entity.Id));
            if (entity.Contains("fullname")) xml.Add(new XElement("fullname", entity.GetAttributeValue<string>("fullname")));
            return xml;
        }
    }
}

```

## ToolUtility.Tests/DataOperations/MutationOperationsSafetyTests.cs
```
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.OwnerOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 保護純 SDK 寫入的識別、故障與引用生命週期；故障在記憶體注入，不實際修改 D365 或帳號權限。
    /// 各案例使用獨立服務；正常業務授權與跨程序唯一鍵仍是 host／資料庫責任，不能以測試假裝已整合。
    /// </summary>
    public sealed class MutationOperationsSafetyTests
    {
        /// <summary>保留兩服務的舊 null 建構拒絕；不因移除 logger 欄位就放寬錯誤組態。</summary>
        /// <param name="owner">是否測負責人服務。</param>
        /// <param name="missingService">是否缺 SDK，否則缺 logger。</param>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Constructor_MissingDependency_Rejects(bool owner, bool missingService)
        {
            var sdk = missingService ? null : new Mock<IOrganizationService>().Object;
            var logger = missingService ? new object() : null;
            Assert.Throws<ArgumentNullException>(() =>
                owner ? (object)new OwnerManagementService(logger!, sdk!) : new EntityCrudService(logger!, sdk!));
        }

        /// <summary>無合法目標的操作必須在 I/O 前拒絕；Strict SDK 證明沒有送出任何部分寫入。</summary>
        /// <param name="operation">要拒絕的合成錯誤。</param>
        [Theory]
        [InlineData("create-null")]
        [InlineData("create-name")]
        [InlineData("update-null")]
        [InlineData("update-id")]
        [InlineData("update-name")]
        [InlineData("delete-name")]
        [InlineData("delete-id")]
        [InlineData("assign-null")]
        [InlineData("assign-id")]
        [InlineData("assign-owner")]
        [InlineData("assign-name")]
        [InlineData("assign-mismatch")]
        public void InvalidIdentity_RejectsBeforeSdk(string operation)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var crud = new EntityCrudService(new object(), sdk.Object);
            var owner = new OwnerManagementService(new object(), sdk.Object);
            var entity = new Entity("contact", Guid.NewGuid());
            Action action = operation switch
            {
                "create-null" => () => crud.CreateEntity(null!),
                "create-name" => () => crud.CreateEntity(new Entity(" ")),
                "update-null" => () => crud.UpdateEntity(null!),
                "update-id" => () => crud.UpdateEntity(new Entity("contact")),
                "update-name" => () => crud.UpdateEntity(new Entity(" ", entity.Id)),
                "delete-name" => () => crud.DeleteEntity(" ", entity.Id),
                "delete-id" => () => crud.DeleteEntity("contact", Guid.Empty),
                "assign-null" => () => owner.AssignOwner("contact", null!, Guid.NewGuid()),
                "assign-id" => () => owner.AssignOwner("contact", new Entity("contact"), Guid.NewGuid()),
                "assign-owner" => () => owner.AssignOwner("contact", entity, Guid.Empty),
                "assign-name" => () => owner.AssignOwner(" ", entity, Guid.NewGuid()),
                "assign-mismatch" => () => owner.AssignOwner("account", entity, Guid.NewGuid()),
                _ => throw new ArgumentException("Unknown test scenario.", nameof(operation))
            };
            Assert.ThrowsAny<ArgumentException>(action);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>SDK 真的故障必須把同一 Exception 傳回共用 host 邊界，不能吞掉、重試重複寫入或包成含個資訊息。</summary>
        /// <param name="operation">四個會寫入 SDK 的入口。</param>
        [Theory]
        [InlineData("create")]
        [InlineData("update")]
        [InlineData("delete")]
        [InlineData("assign")]
        public void SdkFault_PreservesOriginalExceptionAndDoesNotRetry(string operation)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var borrowed = sdk.As<IDisposable>();
            var fault = new TimeoutException("synthetic-private-fault");
            var entity = new Entity("contact", Guid.NewGuid());
            sdk.Setup(s => s.Create(entity)).Throws(fault);
            sdk.Setup(s => s.Update(entity)).Throws(fault);
            sdk.Setup(s => s.Delete("contact", entity.Id)).Throws(fault);
            sdk.Setup(s => s.Execute(It.IsAny<AssignRequest>())).Throws(fault);
            var crud = new EntityCrudService(new object(), sdk.Object);
            var owner = new OwnerManagementService(new object(), sdk.Object);
            Action action = operation switch
            {
                "create" => () => crud.CreateEntity(entity),
                "update" => () => crud.UpdateEntity(entity),
                "delete" => () => crud.DeleteEntity("contact", entity.Id),
                "assign" => () => owner.AssignOwner("contact", entity, Guid.NewGuid()),
                _ => throw new ArgumentException("Unknown test scenario.", nameof(operation))
            };
            Assert.Same(fault, Record.Exception(action));
            Assert.Single(sdk.Invocations);
            borrowed.Verify(d => d.Dispose(), Times.Never);
        }

        /// <summary>空 Create 結果不是成功資料庫身分；不得把它交給 caller 當已完成發布或自行重試。</summary>
        [Fact]
        public void Create_EmptySdkIdentity_RejectsOnce()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns(Guid.Empty);
            Assert.Throws<InvalidOperationException>(() =>
                new EntityCrudService(new object(), sdk.Object).CreateEntity(new Entity("contact")));
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>SDK 支援的伺服器替代索引鍵更新仍合法，不能為 GUID 守門破壞既有 alternate-key API。</summary>
        [Fact]
        public void Update_ServerAlternateKey_PreservesSdkContract()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("contact", "new_serverkey", "synthetic-event");
            sdk.Setup(s => s.Update(entity));
            new EntityCrudService(new object(), sdk.Object).UpdateEntity(entity);
            sdk.Verify(s => s.Update(entity), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>唯讀 getter 的 null 是輸入拒絕，不應包成含實體文字的功能故障或呼叫 SDK。</summary>
        [Fact]
        public void Owner_NullEntity_RejectsWithoutSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var owner = new OwnerManagementService(new object(), sdk.Object);
            Assert.Throws<ArgumentNullException>(() => owner.GetOwnerId(null!));
            Assert.Throws<ArgumentNullException>(() => owner.GetOwnerName(null!));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>未用的 legacy logger 不可被任一服務延長保存；無內嵌／no-inline 工廠排除測試區域變數根參照。</summary>
        /// <param name="owner">是否測試負責人服務。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LegacyLogger_IsNotRetained(bool owner)
        {
            var sdk = new Mock<IOrganizationService>().Object;
            var holder = CreateService(owner, sdk, out var logger);
            for (var count = 0; count < 3 && logger.IsAlive; count++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(logger.IsAlive);
            GC.KeepAlive(holder);
        }

        /// <summary>
        /// 兩個 scope 一千次並行寫入同名但不同 ID；每次都驗正確 SDK、完整參數與結果。
        /// Task 全部 await 後計數固定1000，借入 SDK Dispose 為0；服務不保存 entity 或產生內部工作。
        /// </summary>
        /// <returns>全部外層有界測試工作完成後的 Task。</returns>
        [Fact]
        public async Task ConcurrentScopes_SameDisplayNames_NoCrossTalkAndDrain()
        {
            var count = 0;
            var assignments = 0;
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            var services = new EntityCrudService[2];
            var owners = new OwnerManagementService[2];
            for (var index = 0; index < clients.Length; index++)
            {
                var id = ids[index];
                clients[index].As<IDisposable>();
                clients[index].Setup(s => s.Create(It.IsAny<Entity>())).Returns((Entity entity) =>
                {
                    Assert.Equal(id, entity.Id);
                    Assert.Equal("合成同名", entity.GetAttributeValue<string>("fullname"));
                    Interlocked.Increment(ref count);
                    return id;
                });
                clients[index].Setup(s => s.Execute(It.IsAny<AssignRequest>())).Returns((OrganizationRequest request) =>
                {
                    var assign = Assert.IsType<AssignRequest>(request);
                    Assert.Equal(id, assign.Target.Id);
                    Assert.Equal(id, assign.Assignee.Id);
                    Assert.Equal("contact", assign.Target.LogicalName);
                    Assert.Equal("systemuser", assign.Assignee.LogicalName);
                    Interlocked.Increment(ref assignments);
                    return new AssignResponse();
                });
                services[index] = new EntityCrudService(new object(), clients[index].Object);
                owners[index] = new OwnerManagementService(new object(), clients[index].Object);
            }
            var tasks = new Task[1000];
            for (var index = 0; index < tasks.Length; index++)
            {
                var scope = index % 2;
                tasks[index] = Task.Run(() =>
                {
                    var entity = new Entity("contact", ids[scope]) { ["fullname"] = "合成同名" };
                    Assert.Equal(ids[scope], services[scope].CreateEntity(entity));
                    owners[scope].AssignOwner("contact", entity, ids[scope]);
                });
            }
            await Task.WhenAll(tasks);
            Assert.All(tasks, task => Assert.True(task.IsCompleted));
            Assert.Equal(1000, Volatile.Read(ref count));
            Assert.Equal(1000, Volatile.Read(ref assignments));
            foreach (var client in clients) client.As<IDisposable>().Verify(d => d.Dispose(), Times.Never);
        }

        /// <summary>只把弱引用交回測試，服務可存活但無理由保留的 logger 必須可回收；不建立外部資源。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object CreateService(bool owner, IOrganizationService sdk, out WeakReference logger)
        {
            var candidate = new object();
            logger = new WeakReference(candidate);
            return owner ? (object)new OwnerManagementService(candidate, sdk) : new EntityCrudService(candidate, sdk);
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
            Assert.Same(assembly, typeof(EntityCrudService).Assembly);
            Assert.Same(assembly, typeof(OwnerManagementService).Assembly);
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
        /// 實際反射三個介面的公開 nullable 中繼資料，而非以檔頭是否有 pragma 判斷。
        /// Core strict 專案統一啟用 Nullable；若後續設定漂移成 oblivious，任一字串參數即決定性失敗。
        /// 不需修改產品公開 API 或新增重複 pragma 來配合靜態審查推測。
        /// </summary>
        [Theory]
        [InlineData(typeof(IAppointmentService))]
        [InlineData(typeof(ILessonsService))]
        [InlineData(typeof(IMeetingStatisticsService))]
        [InlineData(typeof(IEntityCrudService))]
        [InlineData(typeof(IOwnerManagementService))]
        public void CoreInterfaces_EmitNonObliviousStringContracts(System.Type contract)
        {
            var context = new NullabilityInfoContext();
            var checkedParameters = 0;
            foreach (var method in contract.GetMethods())
                foreach (var parameter in method.GetParameters())
                    if (parameter.ParameterType == typeof(string))
                    {
                        Assert.Equal(NullabilityState.NotNull, context.Create(parameter).ReadState);
                        checkedParameters++;
                    }
            Assert.True(checkedParameters > 0);
        }
#endif
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

## ToolUtility.Tests/Golden/ToolUtility/mutation-assign.xml
```
<assign><target name="contact" id="11111111-1111-1111-1111-111111111111" /><assignee name="systemuser" id="22222222-2222-2222-2222-222222222222" /></assign>

```

## ToolUtility.Tests/Golden/ToolUtility/mutation-create.xml
```
<create name="contact" id="11111111-1111-1111-1111-111111111111"><fullname>合成同名</fullname></create>

```

## ToolUtility.Tests/Golden/ToolUtility/mutation-delete.xml
```
<delete name="contact" id="11111111-1111-1111-1111-111111111111" />

```

## ToolUtility.Tests/Golden/ToolUtility/mutation-update.xml
```
<update name="contact" id="11111111-1111-1111-1111-111111111111"><fullname>合成同名</fullname></update>

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