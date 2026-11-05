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
