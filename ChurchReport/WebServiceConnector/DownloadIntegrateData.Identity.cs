using System;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 整合資料下載服務 - 委身類型處理
    /// </summary>
    public partial class DownloadIntegrateData
    {
        #region 委身類型設定

        /// <summary>
        /// 設定委身類型（根據出席次數自動調整）
        /// </summary>
        public void SetIdentity(Guid aListEntityId, ref Entity aContact, ref MemberInfomation aMemberInfomation)
        {
            try
            {
                int aIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "customertypecode");
                string aIdentityType = ConvertIndexToIdentity(aIdentity);

                if (aIdentityType == "07. 未入組" || aIdentityType == "08. 新朋友")
                {
                    // 如果主日次數+小組次數 >= MINIMUM_THRESHOLD，則升級為小組組員
                    if (PassOrFail(aListEntityId, ref aContact))
                    {
                        UpgradeToGroupMember(ref aContact);
                    }
                }
                else if (aIdentityType == "05. 小組組員")
                {
                    // 如果主日次數+小組次數 < MINIMUM_THRESHOLD，則降級為未入組
                    if (!PassOrFail(aListEntityId, ref aContact))
                    {
                        DowngradeToUnGrouped(ref aContact);
                    }
                }
            }
            catch (Exception e)
            {
                string ErrorString = $"ERROR : FullName = {this.GetType().FullName} , Time = {DateTime.Now} , Description = {e}";
                throw;
            }
        }

        /// <summary>
        /// 升級為小組組員
        /// </summary>
        private void UpgradeToGroupMember(ref Entity aContact)
        {
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 1);
            UpdateContactEntity(ref aContact);
        }

        /// <summary>
        /// 降級為未入組
        /// </summary>
        private void DowngradeToUnGrouped(ref Entity aContact)
        {
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);
            // 注意：原始程式碼這裡沒有實際執行更新
        }

        /// <summary>
        /// 更新聯絡人實體
        /// </summary>
        private void UpdateContactEntity(ref Entity aContact)
        {
            if (CRM_TYPE == "DYNAMICS365")
            {
                this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
            }
            else
            {
                this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
            }
        }

        #endregion

        #region 出席統計

        /// <summary>
        /// 取得出席次數
        /// </summary>
        public int GetPresentNumber(Guid WeeklyReportId, string Type, ref Entity aContact)
        {
            try
            {
                EntityCollection PresentRecordCollection = this.m_ToolUtilityClass.QueryPresentRecordByContactIdAndSunday(
                    WeeklyReportId, 
                    aContact.Id, 
                    WEEK_PERIOD);

                int TotalNumber = 0;
                string attributeName = Type == "主日" ? "new_sunday_present_this_week" : "new_group_present_this_week";

                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    TotalNumber += this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, attributeName);
                }

                return TotalNumber;
            }
            catch (Exception e)
            {
                string ErrorString = $"ERROR : FullName = {this.GetType().FullName} , Time = {DateTime.Now} , Description = {e}";
                throw;
            }
        }

        /// <summary>
        /// 判斷是否通過出席門檻
        /// </summary>
        public bool PassOrFail(Guid aListEntityId, ref Entity aContact)
        {
            try
            {
                int TotalNumber = GetPresentNumber(aListEntityId, "小組", ref aContact);
                return TotalNumber >= MINIMUM_THRESHOLD;
            }
            catch (Exception e)
            {
                string ErrorString = $"ERROR : FullName = {this.GetType().FullName} , Time = {DateTime.Now} , Description = {e}";
                throw;
            }
        }

        #endregion

        #region 委身類型自動轉換

        /// <summary>
        /// 自動轉換委身類型（新朋友→未入組→未入組結案）
        /// </summary>
        private void TransferIdentity(Entity aContact, int Counter, int NewComeMaxiNumber, int UnGroupMaxiNumber)
        {
            if (!TRANSFER_IDENTITY_FLAG)
                return;

            int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

            if (aIdentityNumber == 100000000) // 新朋友
            {
                TryTransferNewComerToUnGrouped(aContact, Counter, NewComeMaxiNumber);
            }
            else if (aIdentityNumber == 100000004) // 未入組
            {
                TryTransferUnGroupedToClosed(aContact, Counter, UnGroupMaxiNumber);
            }
        }

        /// <summary>
        /// 嘗試將新朋友轉為未入組
        /// </summary>
        private void TryTransferNewComerToUnGrouped(Entity aContact, int Counter, int MaxNumber)
        {
            if (Counter >= MaxNumber && !m_SetIdentityFlag)
            {
                m_SetIdentityFlag = true;
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);
                UpdateContactEntity(ref aContact);
            }
        }

        /// <summary>
        /// 嘗試將未入組轉為未入組結案
        /// </summary>
        private void TryTransferUnGroupedToClosed(Entity aContact, int Counter, int MaxNumber)
        {
            if (Counter >= MaxNumber && !m_SetIdentityFlag)
            {
                m_SetIdentityFlag = true;
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000001);
                UpdateContactEntity(ref aContact);
            }
        }

        #endregion
    }
}
