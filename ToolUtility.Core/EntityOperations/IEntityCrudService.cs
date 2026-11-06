/*
 * 檔案整體說明
 *
 * 目的與定位：已授權 SDK scope 的單次寫入契約，不管理連線、保存輸入或提供隱藏重試。 caller 須以伺服器業務事件唯一鍵保護冪等，並在最終失敗邊界將原 Exception 交共用通知入口。
 *
 * 主要功能與流程：
 * 本檔宣告 IEntityCrudService。
 * - 新增操作獨占候選，成功才回傳非空資料庫 ID；不以姓名等內容去重。
 * - 更新具 ID 或伺服器替代鍵的單筆候選，原 SDK 故障直接向 caller 傳遞。
 * - 刪除範圍內的精確非空 ID，不自行搜尋同名資料或執行批次清理。
 * 資料／選項欄位包含 IEntityCrudService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs、ToolUtility.Tests/CoreTargetFrameworkTests.cs、
 * ToolUtility.Core/EntityOperations/EntityCrudService.cs、ToolUtility/Core/ToolUtilityFacade.cs 追查目前工作樹中的合作／使用位置。
 */
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
