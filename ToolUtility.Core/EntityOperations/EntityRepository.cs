/*
 * 檔案整體說明
 *
 * 目的與定位：保留既有 Repository API 與操作失敗包裝，委派唯一的 Core CRUD／Owner／Query 驗證。 實例只限借入 SDK 的原授權 scope，禁止跨使用者／租戶 singleton；不保存 
 * Entity、Query 或回應。
 *
 * 主要功能與流程：
 * 本檔宣告 IEntityRepository、EntityRepository。
 * - 建立一次並要求 SDK 確認非空資料庫 ID；業務冪等鍵須由 caller 與資料庫事先保證。
 * - 只更新具有正式非空 ID 的實體；此舊契約不接受僅有替代鍵的候選。
 * - 精確刪除已授權的實體與資料庫 ID，不按名稱合併或刪除同名記錄。
 * - 每次直接讀取 SDK 並核對 ID／實體；null 欄位沿用完整欄位，指定欄位原樣傳遞。
 * 主要操作入口包含 Create、Update、Delete、Retrieve、RetrieveMultiple、AssignOwner。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、ToolUtility.Tests/DataOperations/ListCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/ListLifecycleTests.cs、ToolUtility.Tests/DataOperations/ListSafetyTests.cs、
 * ToolUtility.Tests/DataOperations/RepositoryCharacterizationTests.cs 追查目前工作樹中的合作／使用位置。
 */
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
