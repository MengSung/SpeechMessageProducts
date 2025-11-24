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
