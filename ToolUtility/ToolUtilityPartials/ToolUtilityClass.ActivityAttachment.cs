// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityPartials/ToolUtilityClass.ActivityAttachment.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：GetActivityPartyList、GetActivityPartyIdList、SetActivityStatusToCompleted、SetAppointmentStatusToScheduled、DownloadAnAttachment、UploadAnAttachment
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Collections
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
