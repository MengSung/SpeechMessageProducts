// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DownloadIntegrateData.FollowUp.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DownloadIntegrateData
// 主要成員：GetNewComerFollowupInfo、GetNewComerFollowupInfoWithEntity、GetNewComerFollowupInfoCore、ProcessUnGroupedFollowUp、VerifyNewComerIdentity、GetFollowUpWeek、GetFollowUpWeekForUnGroup、BuildHistoryHeader、ProcessFollowUpRecord、UpdateWeekIndex
// 引用命名空間：System、System.Text、ChurchReport.Models、ChurchReport.Models.CrmTransmitModule、ChurchReport.WebServiceConnector.Converters、Microsoft.Extensions.Caching.Memory、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Text;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.WebServiceConnector.Converters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 整合資料下載服務 - 新人跟進處理
    /// </summary>
    public partial class DownloadIntegrateData
    {
        #region Operation-local CRM service helper

        /// <summary>
        /// 以呼叫端當次借用的 CRM service 取得新人跟進需要的 Contact。
        ///
        /// <para>
        /// service 僅以參數存在於此同步呼叫鏈，直接執行 SDK <see cref="IOrganizationService.Retrieve"/>
        /// 後即失去參考；不會寫入 DownloadIntegrateData、ToolUtility、Factory、static、cache 或
        /// <c>AsyncLocal</c>，也不會 Dispose。這使 Session 快取的上層物件無法把 A 的 mutable
        /// service 或 Contact 查詢狀態帶給 B。
        /// </para>
        /// </summary>
        /// <param name="organizationService">由呼叫端 lease owner 借出、仍由 owner 釋放的 service。</param>
        /// <param name="contactId">上層已驗證並授權的 Contact 識別。</param>
        /// <returns>只投影新人跟進與委身判斷所需欄位的 Contact。</returns>
        /// <exception cref="ArgumentNullException">當沒有 operation-local service 時擲回。</exception>
        /// <exception cref="ArgumentException">當 Contact 識別為空時擲回。</exception>
        private static Entity RetrieveFollowUpContact(IOrganizationService organizationService, Guid contactId)
        {
            ArgumentNullException.ThrowIfNull(organizationService);

            if (contactId == Guid.Empty)
            {
                throw new ArgumentException("新人跟進需要有效的聯絡人識別。", nameof(contactId));
            }

            return organizationService.Retrieve(
                "contact",
                contactId,
                new ColumnSet(
                    "customertypecode",
                    "new_start_tracking_date",
                    "fullname",
                    "gendercode",
                    "new_enter_church_date",
                    "description"));
        }

        /// <summary>
        /// 以 operation-local CRM service 讀取 Contact 最近十週的新人跟進出席紀錄。
        ///
        /// <para>
        /// 查詢條件與投影是固定 allowlist，避免未驗證的呼叫端輸入改變 CRM entity、欄位或
        /// 查詢範圍。此方法不使用 FetchXML 或 ToolUtility façade；直接 SDK query 既保持完整
        /// parameter boundary，也避免把 service 封裝在可能跨操作保存的相依物件內。
        /// </para>
        /// </summary>
        /// <param name="organizationService">當次借用且不可由此 helper 釋放的 CRM service。</param>
        /// <param name="contactId">上層已驗證並授權的 Contact 識別。</param>
        /// <returns>依主日由舊到新排序的固定欄位出席紀錄集合。</returns>
        /// <exception cref="ArgumentNullException">當沒有 operation-local service 時擲回。</exception>
        /// <exception cref="ArgumentException">當 Contact 識別為空時擲回。</exception>
        private static EntityCollection RetrieveFollowUpPresentRecords(
            IOrganizationService organizationService,
            Guid contactId)
        {
            ArgumentNullException.ThrowIfNull(organizationService);

            if (contactId == Guid.Empty)
            {
                throw new ArgumentException("新人跟進需要有效的聯絡人識別。", nameof(contactId));
            }

            var query = new QueryExpression("new_present_record")
            {
                ColumnSet = new ColumnSet(
                    "new_sunday_date",
                    "new_groupleader_present_record",
                    "new_followup_ways",
                    "new_follow_up",
                    "new_conclusion_choise",
                    "new_next_step",
                    "new_explanation",
                    "new_weeks"),
                Criteria = new FilterExpression(LogicalOperator.And)
            };

            query.Criteria.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, contactId);
            query.Criteria.AddCondition("new_sunday_date", ConditionOperator.LastXWeeks, 10);
            query.Orders.Add(new OrderExpression("new_sunday_date", OrderType.Ascending));

            return organizationService.RetrieveMultiple(query);
        }

        /// <summary>
        /// 以 operation-local CRM service 寫回單筆新人跟進週次。
        ///
        /// <para>
        /// 這是同步傳遞鏈的最後一跳，僅使用 direct SDK <see cref="IOrganizationService.Update"/>。
        /// 它不保留、包裝、Dispose 或重試 borrowed service；遇到故障、逾時、取消或不確定傳輸
        /// 狀態時保留原始例外讓呼叫端 owner fail closed 並處理 fault eviction。
        /// </para>
        /// </summary>
        /// <param name="organizationService">當次借用且不可由此 helper 釋放的 CRM service。</param>
        /// <param name="presentRecord">只包含本次週次變更的出席紀錄 entity。</param>
        /// <exception cref="ArgumentNullException">當沒有 operation-local service 或出席紀錄時擲回。</exception>
        /// <exception cref="ArgumentException">當 entity 類型或識別不符合固定更新契約時擲回。</exception>
        private static void UpdateFollowUpPresentRecord(
            IOrganizationService organizationService,
            Entity presentRecord)
        {
            ArgumentNullException.ThrowIfNull(organizationService);
            ArgumentNullException.ThrowIfNull(presentRecord);

            if (!string.Equals(presentRecord.LogicalName, "new_present_record", StringComparison.Ordinal) ||
                presentRecord.Id == Guid.Empty)
            {
                throw new ArgumentException("新人跟進週次只能寫回具有有效識別的出席紀錄。", nameof(presentRecord));
            }

            organizationService.Update(presentRecord);
        }

        #endregion

        #region 新人跟進資訊

        /// <summary>
        /// 取得新人跟進資訊（原版：自行查詢 Contact）
        /// </summary>
        private string GetNewComerFollowupInfo(Guid aNewComerId, ref string aFollowUpWeek)
        {
            try
            {
                Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aNewComerId);
                return GetNewComerFollowupInfoCore(aContact, ref aFollowUpWeek);
            }
            catch (Exception e)
            {
                string ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName} , Time = {DateTime.Now} , Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        /// <summary>
        /// ? 極速版：接收已快取的 Contact Entity，省去 CRM 網路往返（每位成員省 ~100-300ms）
        /// </summary>
        private string GetNewComerFollowupInfoWithEntity(Entity aContact, ref string aFollowUpWeek)
        {
            try
            {
                return GetNewComerFollowupInfoCore(aContact, ref aFollowUpWeek);
            }
            catch (Exception e)
            {
                string ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName} , Time = {DateTime.Now} , Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        /// <summary>
        /// 核心邏輯：不負責取得 Contact（由呼叫端提供）
        /// </summary>
        private string GetNewComerFollowupInfoCore(Entity aContact, ref string aFollowUpWeek)
        {
            string aFollowUpHistoryReport = "";

            if (VerifyNewComerIdentity(aContact))
            {
                int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

                if (aIdentityNumber == 100000004) // 未入組
                {
                    aFollowUpHistoryReport = ProcessUnGroupedFollowUp(aContact, ref aFollowUpWeek);
                }
                else // 新朋友
                {
                    aFollowUpHistoryReport = GetFollowUpWeek(aContact, ref aFollowUpWeek);
                }
            }

            return aFollowUpHistoryReport;
        }

        /// <summary>
        /// 處理未入組的跟進（可能是死灰復燃）
        /// </summary>
        private string ProcessUnGroupedFollowUp(Entity aContact, ref string aFollowUpWeek)
        {
            string aStartTracking = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_start_tracking_date");

            if (!string.IsNullOrEmpty(aStartTracking))
            {
                DateTime aStartTrackingDate = DateTime.Parse(aStartTracking);
                DateTime aSunday = CalculateSunday(aStartTrackingDate);
                return GetFollowUpWeekForUnGroup(aContact, ref aFollowUpWeek, aSunday);
            }
            else
            {
                return GetFollowUpWeek(aContact, ref aFollowUpWeek);
            }
        }

        /// <summary>
        /// 驗證是否為新人或未入組
        /// </summary>
        private bool VerifyNewComerIdentity(Entity aContact)
        {
            try
            {
                int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");
                return IdentityConverter.IsNewComerOrUnGrouped(aIdentityNumber);
            }
            catch (Exception e)
            {
                string ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName} , Time = {DateTime.Now} , Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion

        #region 跟進週次處理

        /// <summary>
        /// 取得跟進週次（一般新朋友）
        /// </summary>
        private string GetFollowUpWeek(Entity aContact, ref string MatchedWeekDay)
        {
            try
            {
                // ?? 不可快取：出席/跟進紀錄包含個人牧養資料（姓名、關懷描述、跟進結果）
                // 靜態快取會讓不同 Session 的使用者共享個人資料，構成 Session Leakage
                // 且 EntityCollection 為可變物件，TransferIdentity 可能修改內容導致資料污染
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySundayFetchXml(
                    10,
                    this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname"),
                    aContact.Id.ToString());

                // ? 極速：StringBuilder 取代 string +=，減少每次迭代的記憶體配置
                var sb = new StringBuilder(BuildHistoryHeader(aContact));
                sb.Append(PresentRecordCollection.Entities.Count > 0
                    ? "關懷歷程記錄:" + Environment.NewLine
                    : "沒有關懷歷程記錄!" + Environment.NewLine);

                int WeekCounter = 1;
                MatchedWeekDay = "";

                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    sb.Append(ProcessFollowUpRecord(PresentRecordEntity, ref MatchedWeekDay, ref WeekCounter));
                    TransferIdentity(aContact, WeekCounter, 10, 18);
                    WeekCounter++;
                }

                return sb.ToString();
            }
            catch (Exception e)
            {
                string ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName} , Time = {DateTime.Now} , Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        /// <summary>
        /// 取得跟進週次（未入組 - 死灰復燃）
        /// </summary>
        private string GetFollowUpWeekForUnGroup(Entity aContact, ref string MatchedWeekDay, DateTime aStartTrackingSunday)
        {
            try
            {
                // ?? 不可快取：出席/跟進紀錄包含個人牧養資料（同 GetFollowUpWeek）
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySundayFetchXml(
                    10,
                    this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname"),
                    aContact.Id.ToString());

                // ? 極速：StringBuilder 取代 string +=
                var sb = new StringBuilder(BuildHistoryHeader(aContact));
                sb.Append(PresentRecordCollection.Entities.Count > 0
                    ? "關懷歷程記錄:" + Environment.NewLine
                    : "沒有關懷歷程記錄!" + Environment.NewLine);

                int WeekCounter = 1;
                MatchedWeekDay = "";
                bool FoundFlag = false;

                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    DateTime aPresentRecordSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(PresentRecordEntity, "new_sunday_date");

                    if (!FoundFlag)
                    {
                        if (aPresentRecordSundayDate.ToShortDateString() == aStartTrackingSunday.ToShortDateString())
                        {
                            WeekCounter = 1;
                            FoundFlag = true;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    sb.Append(ProcessFollowUpRecord(PresentRecordEntity, ref MatchedWeekDay, ref WeekCounter));
                    TransferIdentity(aContact, WeekCounter, 10, 10);
                    WeekCounter++;
                }

                return sb.ToString();
            }
            catch (Exception e)
            {
                string ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName} , Time = {DateTime.Now} , Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion

        #region 歷程記錄建構

        /// <summary>
        /// 建構歷程記錄表頭
        /// </summary>
        private string BuildHistoryHeader(Entity aContact)
        {
            // ? 極速：StringBuilder 取代 string +=，4 次字串合併變成 1 次配置
            var sb = new StringBuilder(128);

            int Gender = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "gendercode");
            sb.Append(Gender == 200000 ? "性別:男性" : "性別:女性").Append(Environment.NewLine);

            try
            {
                DateTime FirstDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToLocalTime();
                if (FirstDate.Year > 0)
                    sb.Append("首次進入教會日期:").Append(FirstDate.ToShortDateString()).Append(Environment.NewLine);
            }
            catch { }

            string WelcomeRecord = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "description");
            if (!string.IsNullOrEmpty(WelcomeRecord))
                sb.Append("歡迎紀錄:").Append(Environment.NewLine).Append(WelcomeRecord).Append(Environment.NewLine).Append(Environment.NewLine);

            return sb.ToString();
        }

        /// <summary>
        /// 處理單筆跟進記錄
        /// </summary>
        private string ProcessFollowUpRecord(Entity PresentRecordEntity, ref string MatchedWeekDay, ref int WeekCounter)
        {
            string record = "";

            // 決定本週的週次
            DateTime aSundayDate = DateTime.Now;
            try
            {
                aSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(PresentRecordEntity, "new_sunday_date");
                if (aSundayDate.Date == this.m_Sunday.Date)
                {
                    MatchedWeekDay = ConvertNumberToFollowUpWeekPicker(WeekCounter);
                }
            }
            catch { }

            // 建構記錄文字
            record += $"第{ConvertNumberToFollowUpWeekPicker(WeekCounter)}週，{aSundayDate.Date.ToShortDateString()}，";
            record += $"小組長:{this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_groupleader_present_record")}，";

            // 跟進方式
            int FollowUpOptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_followup_ways");
            string aFollowUpOption = ConvertIndexToFollowUpOptionPicker(FollowUpOptionValue);
            string aFollowUpMethod = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_follow_up");
            if (!string.IsNullOrEmpty(aFollowUpMethod))
            {
                record += $"跟進方式:{aFollowUpOption}{aFollowUpMethod}，";
            }

            // 跟進結果
            if (PresentRecordEntity.Attributes.Contains("new_conclusion_choise"))
            {
                string aFollowUpResult = ConvertIndexToFollowUpResultPicker(
                    this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_conclusion_choise"));
                if (!string.IsNullOrEmpty(aFollowUpResult) && aFollowUpResult != "請選擇")
                {
                    record += $"跟進結果:{aFollowUpResult}，";
                }
            }

            // 下一步驟
            if (PresentRecordEntity.Attributes.Contains("new_next_step"))
            {
                string aFollowUpNextStep = ConvertIndexToFollowUpNextStepPicker(
                    this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_next_step"));
                if (!string.IsNullOrEmpty(aFollowUpNextStep) && aFollowUpNextStep != "請選擇")
                {
                    record += $"跟進下一步驟:{aFollowUpNextStep}，";
                }
            }

            // 跟進描述
            string aExplanation = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_explanation");
            if (!string.IsNullOrEmpty(aExplanation))
            {
                record += $"跟進描述:{aExplanation}{Environment.NewLine}{Environment.NewLine}";
            }
            else
            {
                record += Environment.NewLine + Environment.NewLine;
            }

            // 自動更新週次
            UpdateWeekIndex(PresentRecordEntity, WeekCounter);

            return record;
        }

        /// <summary>
        /// 更新出席紀錄的週次
        /// </summary>
        private void UpdateWeekIndex(Entity PresentRecordEntity, int WeekCounter)
        {
            try
            {
                int WeekIndex = ConvertNumberToWeekIndex(WeekCounter);
                this.m_ToolUtilityClass.SetOptionSetAttribute(PresentRecordEntity, "new_weeks", WeekIndex);

                if (CRM_TYPE == "DYNAMICS365")
                {
                    this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, PresentRecordEntity);
                }
                else
                {
                    this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, PresentRecordEntity);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新週次失敗: {ex.Message}");
            }
        }

        #endregion
    }
}
