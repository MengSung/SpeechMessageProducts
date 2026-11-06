/*
 * 檔案整體說明
 *
 * 目的與定位：已驗證 SDK scope 的負責人讀取／指派契約，不處理登入或授權、不保存實體或憑證。 真正失敗交 caller 最終共用通知邊界，不隱藏重試或以顯示名稱取代資料庫身分。
 *
 * 主要功能與流程：
 * 本檔宣告 IOwnerManagementService。
 * - 讀取 ownerid 的資料庫 ID，缺值為 Guid.Empty，不呼叫 SDK。
 * - 一次 AssignRequest 指派 systemuser；目標名稱一致且兩 ID 非空才允許 I/O。
 * 資料／選項欄位包含 IOwnerManagementService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、ToolUtility.Core/OwnerOperations/OwnerManagementService.cs、
 * ToolUtility/Core/ToolUtilityFacade.cs 追查目前工作樹中的合作／使用位置。
 */
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
