using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 上傳整合資料 - 核心類別 (Partial)
    /// 包含：欄位定義、常數、主要入口方法
    /// </summary>
    public partial class UploadIntegrateData
    {
        #region 欄位與常數
        
        // 透過 Factory 取得 ToolUtilityClass 單一實例
        private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
        private LineNotifyUtility m_LineNotifyUtility = new LineNotifyUtility();
        private static Regex DigitsOnly = new Regex(@"[^\d]");
        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();
        bool m_SetIdentityFlag = false;

        private const String CRM_TYPE = "DYNAMICS365-9.0";
        private const bool TRANSFER_IDENTITY_FLAG = false;
        private const int WEEK_PERIOD = 8;
        private const int MINIMUM_THRESHOLD = 4;
        private const int EMPTY_VALUE = -999999999;

        #region 除錯用參數
        private const int TOTAL_LEVEL = 1;
        private const int LEVEL_1 = 1;
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5;
        #endregion

        #endregion

        #region 上傳資料時所需要的參數

        MemberInfomationPackage m_MemberInfomationPackage = new MemberInfomationPackage();
        MemberInfomationPackage m_InitializedMemberInfomationPackage = new MemberInfomationPackage();

        DateTime m_Sunday;
        String m_LoginType = "";
        String m_GroupType = "";
        Entity m_ContactEntity;
        Guid m_ContactId;
        Entity m_ListEntity;
        Entity m_WeeklyReportEntity;
        EntityCollection m_Lists = new EntityCollection();
        EntityCollection m_PresentLists = new EntityCollection();

        Guid m_DecipleGroupListId;
        Guid m_RaceLeaderId;
        Guid m_ShepherdLeaderId;
        String m_SmallGroupPlace;
        String m_SmallGroupTime;
        Guid m_OwnerId;

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true;
        static readonly object m_UploadDataLocker = new object();
        private const String SET_IDENTITY_METHOD = "透過回報網頁手動設定";

        List<MemberInfomation> m_GroupNamedListMemberInfomation = new List<MemberInfomation>();

        #endregion

        #region 主要入口方法

        /// <summary>
        /// 上傳小組資料主程式
        /// </summary>
        public void UploadData(
            DateTime aSelectedDate, 
            String Account, 
            String Password, 
            String LoginType, 
            String GroupType, 
            String ListEntityId, 
            ref String WeeklyReportEntityId, 
            DateTime aSmallGroupDate, 
            SmallGroupData aSmallGroupData, 
            ref String WeeklyReportData, 
            ref String WeeklyReportAnalysis, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            try
            {
                m_LoginType = LoginType;
                m_GroupType = GroupType;

                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "設定參數");
                SetupCommonParameter(aSelectedDate, Account, Password, aSmallGroupDate, ListEntityId, WeeklyReportEntityId);

                Entity aGraceLeaderWeeklyReportEntity = null;

                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "回傳結果");
                this.m_FeedBackReport.Clear();
                this.ResetDictionary(m_Sunday);

                String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ListEntity, "listname");
                String FilteredGroupName = ToolUtilityClass.DeletePresentRate(GroupName);
                String FilteredOutDigitGroupName = FilteredGroupName.Replace(" ", "");
                
                AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", "主日出席統計:");

                Guid aWeeklyReportId = m_WeeklyReportEntity != null ? m_WeeklyReportEntity.Id : Guid.Empty;

                // 紀錄目前狀態，協助診斷為何會走到建立流程
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"UploadData: ListEntityId={ (m_ListEntity?.Id.ToString() ?? "null") }, PassedWeeklyReportEntityId={WeeklyReportEntityId}, BoundWeeklyReportId={(aWeeklyReportId==Guid.Empty?"Empty":aWeeklyReportId.ToString())}");

                // 確保如果 m_WeeklyReportEntity 已被找到，使用其 Id
                if (m_WeeklyReportEntity != null && aWeeklyReportId == Guid.Empty)
                {
                    aWeeklyReportId = m_WeeklyReportEntity.Id;
                    WeeklyReportEntityId = aWeeklyReportId.ToString();
                    this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"UploadData: bind m_WeeklyReportEntity.Id = {aWeeklyReportId}");
                }

                if (this.m_ListEntity != null)
                {
                    lock (m_UploadDataLocker)
                    {
                        if (aWeeklyReportId == Guid.Empty)
                        {
                            // 建立週報
                            aGraceLeaderWeeklyReportEntity = ProcessCreateWeeklyReport(
                                GroupName,
                                ref WeeklyReportEntityId,
                                aSmallGroupData,
                                WeeklyReportData,
                                HappyWeekIndex,
                                HappyWeekTopic,
                                PauseCheckBox);
                        }

        

        

                        else
                        {
                            // 更新週報
                            aGraceLeaderWeeklyReportEntity = ProcessUpdateWeeklyReport(
                                ref aWeeklyReportId, 
                                aSmallGroupData, 
                                WeeklyReportData, 
                                HappyWeekIndex, 
                                HappyWeekTopic, 
                                PauseCheckBox);
                        }
                    }
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        /// <summary>
        /// 處理建立週報流程
        /// </summary>
        private Entity ProcessCreateWeeklyReport(
            String GroupName,
            ref String WeeklyReportEntityId,
            SmallGroupData aSmallGroupData,
            String WeeklyReportData,
            String HappyWeekIndex,
            String HappyWeekTopic,
            bool PauseCheckBox)
        {
            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "依據有效的週報的小組組員名單當作週報出席率的分母");
            Double ValidNumber = this.GetEffecttiveSmallGroupNumber(m_ListEntity.Id);

            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "要建立週報");
            
            Double aWeeklySundayRate = 0.0;
            Double aWeeklySmallGroupRate = 0.0;
            int aWeeklySundayNumber = 0;
            int aWeeklySmallGroupNumber = 0;

            GroupWeeklyReportGuid aGroupWeeklyReportGuid = new GroupWeeklyReportGuid
            {
                WeeklyReportGuid = WeeklyReportEntityId != null && WeeklyReportEntityId != "" 
                    ? new Guid(WeeklyReportEntityId) 
                    : new Guid(),
                GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ListEntity, "listname"),
                SmallGroupLeaderName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref this.m_ListEntity, "new_contact_family_leader_list"),
                SmallGroupDate = m_Sunday,
                SmallGroupRate = 0,
                SundayPresentRate = 0,
            };

            return CreateWeeklyReportAndPresentRecord(
                GroupName, 
                aGroupWeeklyReportGuid, 
                ref WeeklyReportEntityId, 
                ref m_ListEntity, 
                "", 
                ValidNumber, 
                ref aWeeklySundayRate, 
                ref aWeeklySmallGroupRate, 
                ref aWeeklySundayNumber, 
                ref aWeeklySmallGroupNumber, 
                aSmallGroupData, 
                WeeklyReportData, 
                HappyWeekIndex, 
                HappyWeekTopic, 
                PauseCheckBox);
        }

        /// <summary>
        /// 處理更新週報流程
        /// </summary>
        private Entity ProcessUpdateWeeklyReport(
            ref Guid aWeeklyReportId,
            SmallGroupData aSmallGroupData,
            String WeeklyReportData,
            String HappyWeekIndex,
            String HappyWeekTopic,
            bool PauseCheckBox)
        {
            GroupWeeklyReportGuid aGroupWeeklyReportGuid = new GroupWeeklyReportGuid
            {
                WeeklyReportGuid = aWeeklyReportId,
                GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ListEntity, "listname"),
                SmallGroupLeaderName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref this.m_ListEntity, "new_contact_family_leader_list"),
                SmallGroupDate = m_Sunday,
                SmallGroupRate = 0,
                SundayPresentRate = 0,
            };

            return UpdateWeeklyReportProcess(
                aGroupWeeklyReportGuid, 
                ref m_ListEntity, 
                ref aWeeklyReportId, 
                aSmallGroupData, 
                WeeklyReportData, 
                HappyWeekIndex, 
                HappyWeekTopic, 
                PauseCheckBox);
        }

        /// <summary>
        /// 刪除成員
        /// </summary>
        public void DeleteMember(String Account, String Password, String ListEntityId, Member aMemberToBeDeleted)
        {
            try
            {
                Entity PresentRecordEntity = null;

                try
                {
                    PresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", new Guid(aMemberToBeDeleted.PresentRecordId));
                }
                catch (System.Exception) { }

                if (PresentRecordEntity != null)
                {
                    m_ToolUtilityClass.RemoveMembersToMarketingList(new Guid(ListEntityId), 
                        this.m_ToolUtilityClass.GetEntityLookupAttribute(ref PresentRecordEntity, "new_contact_new_present_record"));
                    m_ToolUtilityClass.DeleteEntity("new_present_record", new Guid(aMemberToBeDeleted.PresentRecordId));
                }
                else
                {
                    Entity aContact = GetContactFromList(new Guid(ListEntityId), aMemberToBeDeleted.FullName);
                    if (aContact != null)
                    {
                        m_ToolUtilityClass.RemoveMembersToMarketingList(new Guid(ListEntityId), aContact.Id);
                    }
                }

                Entity aListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", new Guid(ListEntityId));

                Entity LoginContact = Account != "LineIdLogin"
                    ? this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password)
                    : this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);

                String LoginContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref LoginContact, "fullname");
                String Result = $"{LoginContactFullName} 將 {aMemberToBeDeleted.FullName} 從{aMemberToBeDeleted.Group}移除掉了!";

                this.m_LineNotifyUtility.SendResultLine(Result, aListEntity);
                this.m_LineNotifyUtility.SendListMemberLine(aListEntity);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion

        #region 設定參數方法

        private void SetupCommonParameter(
            DateTime aSelectDate, 
            String Account, 
            String Password, 
            DateTime aSmallGroupDate, 
            String ListEntityId, 
            String WeeklyReportEntityId)
        {
            try
            {
                // 依據設定檔的每週第一日規則，集中計算所屬週次的主日日期。
                m_Sunday = ChurchReport.Services.SundayCalculator.CalculateSunday(
                    aSmallGroupDate,
                    ChurchReport.Services.WeeklyScheduleProvider.FirstDayOfWeek);

                // 找到操作使用者
                this.m_ContactEntity = Account != "LineIdLogin"
                    ? this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password)
                    : this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);

                m_ContactId = m_ContactEntity.Id;
                m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);

                // 蒐集建立週報所需要的屬性
                m_DecipleGroupListId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_deciple_group_list_contact");
                m_RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_race_leader_contact");

                this.m_ListEntity = !string.IsNullOrEmpty(ListEntityId) 
                    ? m_ToolUtilityClass.RetrieveEntity("list", new Guid(ListEntityId)) 
                    : null;
                    
                // 如果呼叫端沒有直接傳入週報 Id，嘗試依據主日與清單找出對應的週報
                this.m_WeeklyReportEntity = !string.IsNullOrEmpty(WeeklyReportEntityId)
                    ? m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", new Guid(WeeklyReportEntityId))
                    : null;

                if (this.m_WeeklyReportEntity == null && this.m_ListEntity != null)
                {
                    try
                    {
                        var weeklyCollection = m_ToolUtilityClass.QueryWeeklyReportBySunday(m_Sunday, m_ListEntity.Id);
                        if (weeklyCollection != null && weeklyCollection.Entities.Count > 0)
                        {
                            // 若有多筆則取第一筆（避免因為未提供 WeeklyReportEntityId 而重複建立）
                            this.m_WeeklyReportEntity = weeklyCollection.Entities[0];
                            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"找到既有週報 (count={weeklyCollection.Entities.Count})，採用 Id = {this.m_WeeklyReportEntity.Id}");
                        }
                        else
                        {
                            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "未找到既有週報，將以建立新週報處理");
                        }
                    }
                    catch (Exception ex)
                    {
                        this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"查詢既有週報時發生錯誤: {ex.Message}");
                    }
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion
    }
}
