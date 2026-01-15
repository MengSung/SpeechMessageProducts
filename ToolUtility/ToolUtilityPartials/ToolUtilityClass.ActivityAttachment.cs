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
