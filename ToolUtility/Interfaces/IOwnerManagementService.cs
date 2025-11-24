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
