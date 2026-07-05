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

        public void SetActivityStatusToCompleted(String ActivityName, Guid aActivityId)
        {
            try
            {
                if (CRM_TYPE == "DYNAMICS365")
                {
                    _facade.SetActivityStatusToCompleted(ActivityName, aActivityId, m_OrganizationService);
                }
                else
                {
                    _facade.SetActivityStatusToCompleted(ActivityName, aActivityId, m_Crm2011OrganizationService);
                }
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void SetAppointmentStatusToScheduled(Guid aActivityId)
        {
            try
            {
                if (CRM_TYPE == "DYNAMICS365")
                {
                    _facade.SetAppointmentStatusToScheduled(aActivityId, m_OrganizationService);
                }
                else
                {
                    _facade.SetAppointmentStatusToScheduled(aActivityId, m_Crm2011OrganizationService);
                }
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
