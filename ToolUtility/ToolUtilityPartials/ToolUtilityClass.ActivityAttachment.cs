/*
 * 檔案整體說明
 *
 * 目的與定位：ToolUtilityClass - 活動與附件操作 (Partial Class 8/10) 包含：活動狀態管理、附件上傳下載
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityClass。
 * - 將指定活動設為已完成，透過 gateway 代理在本次 CRM 操作期間租用 client。
 * - 將指定約會設為已排程，透過 gateway 代理維持每次操作的租約邊界。
 * 主要操作入口包含 GetActivityPartyList、GetActivityPartyIdList、SetActivityStatusToCompleted、
 * SetAppointmentStatusToScheduled、DownloadAnAttachment、UploadAnAttachment。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Images/ImageUploadCharacterizationTests.cs、
 * ChurchReport.MemberInfo.Tests/Integration/ContactOwnerRouteTests.cs、
 * ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/ChurchReportPaymentContextBuilderTests.cs 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.Xrm.Sdk;
using System;
using System.Collections;

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - 活動與附件操作 (Partial Class 8/10)
    /// 包含：活動狀態管理、附件上傳下載
    /// </summary>
    public partial class ToolUtilityClass
    {
        #region 活動操作
        public void GetActivityPartyList(Entity ActivityEntity, String FromOrTo, ArrayList aFromOrToList, ArrayList aFromOrToTypeList)
        {
            try
            {
                _facade.GetActivityPartyList(ActivityEntity, FromOrTo, aFromOrToList, aFromOrToTypeList);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void GetActivityPartyIdList(Entity ActivityEntity, String FromOrTo, ArrayList aFromOrToIdList, ArrayList aFromOrToTypeList)
        {
            try
            {
                _facade.GetActivityPartyIdList(ActivityEntity, FromOrTo, aFromOrToIdList, aFromOrToTypeList);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        /// <summary>
        /// 將指定活動設為已完成，透過 gateway 代理在本次 CRM 操作期間租用 client。
        /// </summary>
        /// <param name="ActivityName">活動的實體名稱。</param>
        /// <param name="aActivityId">要更新的活動識別碼。</param>
        /// <remarks>
        /// 舊 <c>OrganizationServiceProxy</c> 分支永遠無法執行：其判斷常數為
        /// <c>DYNAMICS365-9.0</c>，卻比較 <c>DYNAMICS365</c>。保留原 else 路徑的
        /// <c>m_Crm2011OrganizationService</c> 相容欄位；該欄位實際保存 gateway 代理，
        /// 因此不會跨 request 保存 raw client 或 lease。
        /// </remarks>
        public void SetActivityStatusToCompleted(String ActivityName, Guid aActivityId)
        {
            try
            {
                _facade.SetActivityStatusToCompleted(ActivityName, aActivityId, m_Crm2011OrganizationService);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        /// <summary>
        /// 將指定約會設為已排程，透過 gateway 代理維持每次操作的租約邊界。
        /// </summary>
        /// <param name="aActivityId">要更新的約會識別碼。</param>
        /// <remarks>
        /// 此方法與活動完成流程同樣移除了恆假條件下的未指派 proxy 分支，只保留既有
        /// 相容路徑。代理的 lease 由 Gateway 在呼叫完成或例外時確定性歸還／淘汰，
        /// ToolUtilityClass 不擁有也不釋放底層 client。
        /// </remarks>
        public void SetAppointmentStatusToScheduled(Guid aActivityId)
        {
            try
            {
                _facade.SetAppointmentStatusToScheduled(aActivityId, m_Crm2011OrganizationService);
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        #endregion

        #region 附件操作
        public EntityCollection DownloadAnAttachment(ref IOrganizationService aCrmService, Guid AnEntityId)
        {
            try
            {
                return _facade.DownloadAnAttachment(ref aCrmService, AnEntityId);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void UploadAnAttachment(ref IOrganizationService aCrmService, String EntityName, String Subject,
            String NoteText, String FileName, String MimeType, byte[] DocumentBody, Guid ToBeAttachedEntityId)
        {
            try
            {
                _facade.UploadAnAttachment(ref aCrmService, EntityName, Subject, NoteText, FileName, MimeType, DocumentBody, ToBeAttachedEntityId);
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        #endregion
    }
}
