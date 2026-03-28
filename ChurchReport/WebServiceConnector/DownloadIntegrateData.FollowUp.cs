using System;
using System.Text;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.WebServiceConnector.Converters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 整合資料下載服務 - 新人跟進處理
    /// </summary>
    public partial class DownloadIntegrateData
    {
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
