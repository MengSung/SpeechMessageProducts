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
