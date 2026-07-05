// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ActivityOperations/IActivityService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IActivityService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Collections
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;
using System.Collections;

namespace ToolUtilityNameSpace.ActivityOperations
{
    /// <summary>
    /// 活動實體操作服務介面 (Activity Operations)
    /// 處理 CRM 活動實體的常見操作，如取得收件人/寄件人、變更狀態等
    /// </summary>
    public interface IActivityService
    {
        /// <summary>
        /// 取得活動的參與者列表（寄件人或收件人）
        /// </summary>
        /// <param name="activityEntity">活動實體</param>
        /// <param name="fromOrTo">欄位名稱 (from 或 to)</param>
        /// <param name="partyList">輸出參數：參與者實體列表</param>
        /// <param name="partyTypeList">輸出參數：參與者類型列表</param>
        void GetActivityPartyList(Entity activityEntity, string fromOrTo, ArrayList partyList, ArrayList partyTypeList);

        /// <summary>
        /// 取得活動的參與者 ID 列表（寄件人或收件人）
        /// </summary>
        /// <param name="activityEntity">活動實體</param>
        /// <param name="fromOrTo">欄位名稱 (from 或 to)</param>
        /// <param name="partyIdList">輸出參數：參與者 ID 列表</param>
        /// <param name="partyTypeList">輸出參數：參與者類型列表</param>
        void GetActivityPartyIdList(Entity activityEntity, string fromOrTo, ArrayList partyIdList, ArrayList partyTypeList);

        /// <summary>
        /// 將活動狀態設為已完成
        /// </summary>
        /// <param name="activityName">活動邏輯名稱（如 phonecall, email, appointment 等）</param>
        /// <param name="activityId">活動 ID</param>
        void SetActivityStatusToCompleted(string activityName, Guid activityId);

        /// <summary>
        /// 將約會狀態設為已排程
        /// </summary>
        /// <param name="activityId">約會 ID</param>
        void SetAppointmentStatusToScheduled(Guid activityId);

        /// <summary>
        /// 將活動狀態設為已完成（使用外部服務）
        /// </summary>
        /// <param name="activityName">活動邏輯名稱</param>
        /// <param name="activityId">活動 ID</param>
        /// <param name="organizationService">外部組織服務</param>
        void SetActivityStatusToCompleted(string activityName, Guid activityId, IOrganizationService organizationService);

        /// <summary>
        /// 將約會狀態設為已排程（使用外部服務）
        /// </summary>
        /// <param name="activityId">約會 ID</param>
        /// <param name="organizationService">外部組織服務</param>
        void SetAppointmentStatusToScheduled(Guid activityId, IOrganizationService organizationService);
    }
}
