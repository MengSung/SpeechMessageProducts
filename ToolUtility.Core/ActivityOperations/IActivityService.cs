/*
 * 檔案整體說明
 *
 * 目的與定位：同步活動資料操作的舊相容契約；host 先授權活動及 SDK scope，純核心不以 ID／表名代替授權。 所有輸出只屬於 caller 操作，不保存於服務、cache 或靜態集合；故障保留真實例外交最終 
 * host 告警。
 *
 * 主要功能與流程：
 * 本檔宣告 IActivityService。
 * - 完整讀取所有正式參照，驗過整個消費集合再按來源順序追加；缺列／錯頁／SDK 故障不部分發布。
 * - 不查 SDK，完整驗證參照及前綴後追加對齊 ID／表名；後段錯型不能留前半段結果。
 * - 以 ctor 借入 SDK 執行一次原狀態 1／4 轉移；未知回應或故障不能回報成功或重試。
 * - 以 ctor 借入 SDK 將原 appointment 轉為 3／5；不改變組織狀態模型或連線所有權。
 * 資料／選項欄位包含 IActivityService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/ActivityOperations/ActivityService.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using Microsoft.Xrm.Sdk;
using System;
using System.Collections;

namespace ToolUtilityNameSpace.ActivityOperations
{
    /// <summary>
    /// 同步活動資料操作的舊相容契約；host 先授權活動及 SDK scope，純核心不以 ID／表名代替授權。
    /// 所有輸出只屬於 caller 操作，不保存於服務、cache 或靜態集合；故障保留真實例外交最終 host 告警。
    /// </summary>
    /// <remarks>
    /// 實體及純 ID 入口維持 append、不清除既有前綴。兩個清單必須是不同的普通 ArrayList，
    /// 全程由 caller 獨占，不可同時列舉／修改；來源最多 5000 容器，完整輸出最多 5000 列。
    /// 正式身份為 logical name＋資料庫 GUID，同名不同 ID 及跨表同 GUID 保留；同表重複或缺正式 ID 拒絕發布。
    /// 全部驗證／I/O 完成才追加；舊兩物件參數無法對違約的並行讀者提供原子交換，不支援自訂／唯讀／固定清單。
    /// 未解析外部地址沒有 partyid 時沿用略過，不生成假 ID。SDK 只借入，不 Dispose、不盲目重試寫入。
    /// 具體服務 owner 在 host drain 後 Dispose 以清除借用參照；此介面不新增成員強迫舊外部實作者變更。
    /// </remarks>
    public interface IActivityService
    {
        /// <summary>完整讀取所有正式參照，驗過整個消費集合再按來源順序追加；缺列／錯頁／SDK 故障不部分發布。</summary>
        /// <param name="activityEntity">已授權且操作獨占的活動資料，服務不保存。</param>
        /// <param name="fromOrTo">參與者欄位的 logical name，包含原 requiredattendees 等欄位。</param>
        /// <param name="partyList">輸出完整實體及既有正式前綴的普通 ArrayList。</param>
        /// <param name="partyTypeList">不同於實體清單、與每一列對齊的 logical name 清單。</param>
        void GetActivityPartyList(Entity activityEntity, string fromOrTo, ArrayList partyList, ArrayList partyTypeList);

        /// <summary>不查 SDK，完整驗證參照及前綴後追加對齊 ID／表名；後段錯型不能留前半段結果。</summary>
        /// <param name="activityEntity">caller 擁有且已授權的活動。</param>
        /// <param name="fromOrTo">合法參與者欄位，不是授權憑據。</param>
        /// <param name="partyIdList">含非空正式 GUID 的普通 ArrayList，不可與類型參數別名。</param>
        /// <param name="partyTypeList">與每個 GUID 對齊的 logical name；caller 全程獨占。</param>
        void GetActivityPartyIdList(Entity activityEntity, string fromOrTo, ArrayList partyIdList, ArrayList partyTypeList);

        /// <summary>以 ctor 借入 SDK 執行一次原狀態 1／4 轉移；未知回應或故障不能回報成功或重試。</summary>
        /// <param name="activityName">host 已授權的活動 logical name，原組織須支援該狀態碼。</param>
        /// <param name="activityId">非空正式資料庫 ID。</param>
        void SetActivityStatusToCompleted(string activityName, Guid activityId);

        /// <summary>以 ctor 借入 SDK 將原 appointment 轉為 3／5；不改變組織狀態模型或連線所有權。</summary>
        /// <param name="activityId">同授權 scope 的非空正式約會 ID。</param>
        void SetAppointmentStatusToScheduled(Guid activityId);

        /// <summary>僅用本次明確借入 SDK 完成活動，不回退到 ctor SDK，原例外保留於固定安全包裝內。</summary>
        /// <param name="activityName">已授權的活動 logical name。</param>
        /// <param name="activityId">非空正式 ID。</param>
        /// <param name="organizationService">由 caller 擁有且維持到同步呼叫排空的 SDK，不 Dispose。</param>
        void SetActivityStatusToCompleted(string activityName, Guid activityId, IOrganizationService organizationService);

        /// <summary>僅用明確外部 SDK 執行一次 appointment 3／5 轉移，確認正確回應才成功。</summary>
        /// <param name="activityId">非空且已授權的正式約會 ID。</param>
        /// <param name="organizationService">caller 同 scope 的借入 SDK，生命週期不移交給核心。</param>
        void SetAppointmentStatusToScheduled(Guid activityId, IOrganizationService organizationService);
    }
}
