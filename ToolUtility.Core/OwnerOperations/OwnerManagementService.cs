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
