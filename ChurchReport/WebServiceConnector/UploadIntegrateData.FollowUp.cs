using System;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 上傳整合資料 - 新人跟進 (Partial)
    /// 包含：新人跟進相關邏輯、委身類型轉換
    /// </summary>
    public partial class UploadIntegrateData
    {
        #region 新人跟進資訊

        private String GetNewComerFollowupInfo(Guid aNewComerId, ref String aFollowUpWeek)
        {
            try
            {
                Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aNewComerId);
                String aFollowUpHistoryReport = "";

                if (!VerifyNewComerIdentity(aContact))
                    return aFollowUpHistoryReport;

                int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

                if (aIdentityNumber == 100000004) // 未入組
                {
                    String aStartTracking = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_start_tracking_date");
                    if (!string.IsNullOrEmpty(aStartTracking))
                    {
                        DateTime aStartTrackingDate = DateTime.Parse(aStartTracking);
                        // 保留既有以「目前日期」判斷所屬主日的流程，
                        // 但改由集中式服務依設定的每週第一日計算主日。
                        DateTime aSunday = ChurchReport.Services.SundayCalculator.CalculateSunday(
                            DateTime.Now,
                            ChurchReport.Services.WeeklyScheduleProvider.FirstDayOfWeek);

                        aFollowUpHistoryReport = GetFollowUpWeekForUnGroup(aContact, ref aFollowUpWeek, aSunday);
                    }
                    else
                    {
                        aFollowUpHistoryReport = GetFollowUpWeek(aContact, ref aFollowUpWeek);
                    }
                }
                else // 新朋友
                {
                    aFollowUpHistoryReport = GetFollowUpWeek(aContact, ref aFollowUpWeek);
                }

                return aFollowUpHistoryReport;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private bool VerifyNewComerIdentity(Entity aContact)
        {
            try
            {
                int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");
                return aIdentityNumber == 100000000 || aIdentityNumber == 100000004;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private String GetFollowUpWeek(Entity aContact, ref String MatchedWeekDay)
        {
            try
            {
                String aFollowUpHistoryReport = BuildFollowUpHeader(ref aContact);

                EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySundayFetchXml(
                    10, this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname"), aContact.Id.ToString());

                aFollowUpHistoryReport += PresentRecordCollection.Entities.Count > 0 
                    ? "關懷歷程記錄:" + Environment.NewLine 
                    : "沒有關懷歷程記錄!" + Environment.NewLine;

                int WeekCounter = 1;
                MatchedWeekDay = "";

                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    DateTime aSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(PresentRecordEntity, "new_sunday_date");
                    
                    if (aSundayDate.Date == this.m_Sunday.Date)
                        MatchedWeekDay = ConvertNumberToFollowUpWeekPicker(WeekCounter);

                    aFollowUpHistoryReport += BuildFollowUpRecord(PresentRecordEntity, WeekCounter, aSundayDate);
                    UpdateWeekIndex(PresentRecordEntity, WeekCounter);
                    TransferIdentity(aContact, WeekCounter, 10, 18);

                    WeekCounter++;
                }

                return aFollowUpHistoryReport;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private String GetFollowUpWeekForUnGroup(Entity aContact, ref String MatchedWeekDay, DateTime aStartTrackingSunday)
        {
            try
            {
                String aFollowUpHistoryReport = BuildFollowUpHeader(ref aContact);

                EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySundayFetchXml(
                    10, this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname"), aContact.Id.ToString());

                aFollowUpHistoryReport += PresentRecordCollection.Entities.Count > 0 
                    ? "關懷歷程記錄:" + Environment.NewLine 
                    : "沒有關懷歷程記錄!" + Environment.NewLine;

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

                    DateTime aSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(PresentRecordEntity, "new_sunday_date");
                    
                    if (aSundayDate.Date == this.m_Sunday.Date)
                        MatchedWeekDay = ConvertNumberToFollowUpWeekPicker(WeekCounter);

                    aFollowUpHistoryReport += BuildFollowUpRecord(PresentRecordEntity, WeekCounter, aSundayDate);
                    UpdateWeekIndex(PresentRecordEntity, WeekCounter);
                    TransferIdentity(aContact, WeekCounter, 10, 10);

                    WeekCounter++;
                }

                return aFollowUpHistoryReport;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private String BuildFollowUpHeader(ref Entity aContact)
        {
            String header = "";

            // 性別
            int Gender = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "gendercode");
            header += Gender == 200000 ? "性別:男性" + Environment.NewLine : "性別:女性" + Environment.NewLine;

            // 首次進入教會日期
            try
            {
                DateTime FirstDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToLocalTime();
                if (FirstDate.Year > 0)
                    header += "首次進入教會日期:" + FirstDate.ToShortDateString() + Environment.NewLine;
            }
            catch { }

            // 歡迎紀錄
            String WelcomeRecord = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "description");
            if (!string.IsNullOrEmpty(WelcomeRecord))
                header += "歡迎紀錄:" + Environment.NewLine + WelcomeRecord + Environment.NewLine + Environment.NewLine;

            return header;
        }

        private String BuildFollowUpRecord(Entity PresentRecordEntity, int WeekCounter, DateTime aSundayDate)
        {
            String record = $"第{ConvertNumberToFollowUpWeekPicker(WeekCounter)}週，{aSundayDate.Date.ToShortDateString()}，";
            record += "小組長:" + this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_groupleader_present_record") + "，";

            // 跟進方式
            int FollowUpOptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_followup_ways");
            String aFollowUpOption = ConvertIndexToFollowUpOptionPicker(FollowUpOptionValue);
            String aFollowUpMethod = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_follow_up");
            if (!string.IsNullOrEmpty(aFollowUpMethod))
                record += "跟進方式:" + aFollowUpOption + aFollowUpMethod + "，";

            // 跟進結果
            if (PresentRecordEntity.Attributes.Contains("new_conclusion_choise"))
            {
                int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_conclusion_choise");
                String aFollowUpResult = ConvertIndexToFollowUpResultPicker(OptionValue);
                if (!string.IsNullOrEmpty(aFollowUpResult) && aFollowUpResult != "請選擇")
                    record += "跟進結果:" + aFollowUpResult + "，";
            }

            // 跟進下一步驟
            if (PresentRecordEntity.Attributes.Contains("new_next_step"))
            {
                int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_next_step");
                String aFollowUpNextStep = ConvertIndexToFollowUpNextStepPicker(OptionValue);
                if (!string.IsNullOrEmpty(aFollowUpNextStep) && aFollowUpNextStep != "請選擇")
                    record += "跟進下一步驟:" + aFollowUpNextStep + "，";
            }

            // 跟進描述
            String aExplanation = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_explanation");
            record += !string.IsNullOrEmpty(aExplanation) 
                ? "跟進描述:" + aExplanation + Environment.NewLine + Environment.NewLine 
                : Environment.NewLine + Environment.NewLine;

            return record;
        }

        private void UpdateWeekIndex(Entity PresentRecordEntity, int WeekCounter)
        {
            try
            {
                int WeekIndex = ConvertNumberToWeekIndex(WeekCounter);
                this.m_ToolUtilityClass.SetOptionSetAttribute(PresentRecordEntity, "new_weeks", WeekIndex);

                if (CRM_TYPE == "DYNAMICS365")
                    this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, PresentRecordEntity);
                else
                    this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, PresentRecordEntity);
            }
            catch { }
        }

        private void TransferIdentity(Entity aContact, int Counter, int NewComeMaxiNumber, int UnGroupMaxiNumber)
        {
            int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

            if (aIdentityNumber == 100000000) // 新朋友
            {
                if (Counter >= NewComeMaxiNumber && !m_SetIdentityFlag)
                {
                    m_SetIdentityFlag = true;
                    if (TRANSFER_IDENTITY_FLAG)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);
                        UpdateContactEntity(ref aContact);
                    }
                }
            }
            else if (aIdentityNumber == 100000004) // 未入組
            {
                if (Counter >= UnGroupMaxiNumber && !m_SetIdentityFlag)
                {
                    m_SetIdentityFlag = true;
                    if (TRANSFER_IDENTITY_FLAG)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000001);
                        UpdateContactEntity(ref aContact);
                    }
                }
            }
        }

        private void UpdateContactEntity(ref Entity aContact)
        {
            if (CRM_TYPE == "DYNAMICS365")
                this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
            else
                this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
        }

        #endregion
    }
}
