// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DownloadIntegrateData.Identity.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DownloadIntegrateData
// 主要成員：SetIdentity、UpgradeToGroupMember、DowngradeToUnGrouped、UpdateContactEntity、GetPresentNumber、PassOrFail、TransferIdentity、TryTransferNewComerToUnGrouped、TryTransferUnGroupedToClosed
// 引用命名空間：System、ChurchReport.Models、ChurchReport.Models.CrmTransmitModule、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 整合資料下載服務 - 委身類型處理
    /// </summary>
    public partial class DownloadIntegrateData
    {
        #region Operation-local CRM service helper

        /// <summary>
        /// 以呼叫端當次借用的 CRM service 讀取委身判斷所需的出席紀錄。
        ///
        /// <para>
        /// 此 helper 的 service 生命週期嚴格限制在同步呼叫期間：它只以參數接收並直接傳給
        /// <see cref="IOrganizationService.RetrieveMultiple(QueryBase)"/>，不會寫入 instance、
        /// static、<c>AsyncLocal</c>、cache、Factory 或 ToolUtility，也不會 Dispose、Close 或
        /// 釋放它。因此下一個使用者、profile 或 connector generation 無法重用本次可變連線。
        /// </para>
        ///
        /// <para>
        /// 查詢使用固定 entity、欄位、條件與排序，不接受 caller 提供的 FetchXML、endpoint、
        /// profile 或任意欄位。未提供有效識別或 service 時在任何 CRM I/O 前 fail closed，避免
        /// 回落到共用 ToolUtility service 或建立不受隔離保護的查詢。
        /// </para>
        /// </summary>
        /// <param name="organizationService">呼叫端 lease owner 借用且仍由其釋放的 CRM service。</param>
        /// <param name="listEntityId">已由上層授權的名單識別。</param>
        /// <param name="contactId">已由上層授權的聯絡人識別。</param>
        /// <returns>僅含委身門檻統計需要欄位的當次出席紀錄集合。</returns>
        /// <exception cref="ArgumentNullException">當沒有 operation-local service 時擲回。</exception>
        /// <exception cref="ArgumentException">當名單或聯絡人識別為空時擲回。</exception>
        private static EntityCollection RetrieveIdentityPresentRecords(
            IOrganizationService organizationService,
            Guid listEntityId,
            Guid contactId)
        {
            ArgumentNullException.ThrowIfNull(organizationService);

            if (listEntityId == Guid.Empty)
            {
                throw new ArgumentException("委身判斷需要有效的名單識別。", nameof(listEntityId));
            }

            if (contactId == Guid.Empty)
            {
                throw new ArgumentException("委身判斷需要有效的聯絡人識別。", nameof(contactId));
            }

            var query = new QueryExpression("new_present_record")
            {
                ColumnSet = new ColumnSet(
                    "new_sunday_present_this_week",
                    "new_group_present_this_week"),
                Criteria = new FilterExpression(LogicalOperator.And)
            };

            query.Criteria.AddCondition("new_list_new_present_record", ConditionOperator.Equal, listEntityId);
            query.Criteria.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, contactId);
            query.Criteria.AddCondition("new_sunday_date", ConditionOperator.LastXWeeks, WEEK_PERIOD);
            query.Orders.Add(new OrderExpression("new_sunday_date", OrderType.Descending));

            return organizationService.RetrieveMultiple(query);
        }

        /// <summary>
        /// 以 operation-local CRM service 更新已完成委身轉換的 Contact。
        ///
        /// <para>
        /// 呼叫端仍是 service 的唯一 owner；本 helper 不包裝 ToolUtility、不重試、不 catch 後
        /// 改寫例外，也不 Dispose service。若 transport 已 fault、逾時或取消，原始 SDK 例外會
        /// 回到 owner，由外層依其 pool／lease 規則淘汰或歸還，避免不確定連線流給下一個操作。
        /// </para>
        /// </summary>
        /// <param name="organizationService">當次借用且不可由此 helper 釋放的 CRM service。</param>
        /// <param name="contact">只包含本次欲寫回欄位的 Contact entity。</param>
        /// <exception cref="ArgumentNullException">當沒有 operation-local service 或 Contact 時擲回。</exception>
        /// <exception cref="ArgumentException">當 Contact 類型或識別不符合固定更新契約時擲回。</exception>
        private static void UpdateIdentityContact(IOrganizationService organizationService, Entity contact)
        {
            ArgumentNullException.ThrowIfNull(organizationService);
            ArgumentNullException.ThrowIfNull(contact);

            if (!string.Equals(contact.LogicalName, "contact", StringComparison.Ordinal) || contact.Id == Guid.Empty)
            {
                throw new ArgumentException("委身更新只能寫回具有有效識別的 Contact。", nameof(contact));
            }

            organizationService.Update(contact);
        }

        #endregion

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
