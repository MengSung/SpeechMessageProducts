/*
 * 檔案整體說明
 *
 * 目的與定位：純 BCL／SDK 的主機委派契約；組態、連線、cache 與最終告警實作仍由 net10 host 擁有。 只定義方法不持有資源，呼叫者必須先驗證使用者／組織／產品及記錄 scope，不能從傳入 ID
 * 推導權限。
 *
 * 主要功能與流程：
 * 本檔宣告 IToolUtilityCore。
 * - 借用 host 已授權的 CRM SDK，不接管連線／憑證或 Dispose；host 排空操作後才釋放，不跨授權 scope 保存。
 * - 以 caller 擁有的 Entity 建立記錄並回正式資料庫 ID；host 負責授權及跨程序冪等鍵，不以應用鎖代替資料庫唯一性。
 * - 更新 caller 獨占且具正式 ID 的 Entity，不公開共享 cache／Session 圖；SDK 故障交最終 host 共用告警。
 * - 依伺服器已授權的表名及正式 ID 刪除，不把顯示名稱或路由值當權限，不隱藏 SDK 失敗。
 * 資料／選項欄位包含 IToolUtilityCore，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
#nullable enable
using Microsoft.Xrm.Sdk;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.Core
{
    /// <summary>
    /// 純 BCL／SDK 的主機委派契約；組態、連線、cache 與最終告警實作仍由 net10 host 擁有。
    /// 只定義方法不持有資源，呼叫者必須先驗證使用者／組織／產品及記錄 scope，不能從傳入 ID 推導權限。
    /// </summary>
    public interface IToolUtilityCore
    {
        #region 組織服務屬性
        /// <summary>
        /// 借用 host 已授權的 CRM SDK，不接管連線／憑證或 Dispose；host 排空操作後才釋放，不跨授權 scope 保存。
        /// </summary>
        IOrganizationService OrganizationService { get; }
        #endregion

        #region Entity 基礎操作
        /// <summary>
        /// 以 caller 擁有的 Entity 建立記錄並回正式資料庫 ID；host 負責授權及跨程序冪等鍵，不以應用鎖代替資料庫唯一性。
        /// </summary>
        Guid CreateEntity(Entity entity);

        /// <summary>
        /// 更新 caller 獨占且具正式 ID 的 Entity，不公開共享 cache／Session 圖；SDK 故障交最終 host 共用告警。
        /// </summary>
        void UpdateEntity(ref Entity entity);

        /// <summary>
        /// 依伺服器已授權的表名及正式 ID 刪除，不把顯示名稱或路由值當權限，不隱藏 SDK 失敗。
        /// </summary>
        void DeleteEntity(string entityName, Guid entityId);

        /// <summary>
        /// 讀取並驗證單筆正式 ID／表名；結果生命週期只屬於這次操作，不保留 SDK／request 參照。
        /// </summary>
        Entity RetrieveEntity(string entityName, Guid entityId);
        #endregion

        #region 聯絡人操作
        /// <summary>
        /// 透過已授權 scope 的 LINE ID 查聯絡人；ID 為查詢值而非權限，host cache 與連線責任不得移入純契約。
        /// </summary>
        Entity RetrieveContactByLineId(string lineId);

        /// <summary>
        /// 以名稱過濾但按正式資料庫 ID 發布，合法同名資料完整保留，不以名稱去重或隱藏衝突。
        /// </summary>
        EntityCollection RetrieveContactCollectionByName(string contactFullName);
        #endregion

        #region 名單操作
        /// <summary>
        /// 在已授權名單中批次添加正式成員 ID；caller 取消停止後續批次，已成功寫入不宣稱可回滾，host 擁有排空責任。
        /// </summary>
        Task<int> AddMembersToMarketingListAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 傳回此次操作擁有的完整成員集合，驗穩定 ID 後才發布；caller 不得把可變 ArrayList 當共享快照。
        /// </summary>
        ArrayList GetAllMemberDataFromList(Guid listEntityId);
        #endregion
    }
}
