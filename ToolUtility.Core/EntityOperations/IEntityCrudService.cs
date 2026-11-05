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
