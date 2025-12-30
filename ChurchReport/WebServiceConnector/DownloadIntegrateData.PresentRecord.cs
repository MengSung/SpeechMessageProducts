using System;
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 整合資料下載服務 - 出席紀錄處理
    /// </summary>
    public partial class DownloadIntegrateData
    {
        #region 出席紀錄查詢

        /// <summary>
        /// 根據登入類型取得出席紀錄
        /// </summary>
        private EntityCollection GetPresentRecordByLoginType(
            string GroupName, 
            Guid WeeklyReportId, 
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            EntityCollection PresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship(
                "new_group_present_weekly_report", 
                "new_group_present_weekly_reportid", 
                WeeklyReportId.ToString(), 
                "new_group_present_weekly_report_prese", 
                "new_present_record");

            if (this.m_LoginType == "小組長")
            {
                return PresentRecordCollection;
            }

            // 個人回報：只回傳對應的出席紀錄
            foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
            {
                if (this.m_ContactId == this.m_ToolUtilityClass.GetEntityLookupAttribute(PresentRecordEntity, "new_contact_new_present_record"))
                {
                    EntityCollection LocalPresentRecordCollection = new EntityCollection();
                    LocalPresentRecordCollection.Entities.Add(PresentRecordEntity);
                    return LocalPresentRecordCollection;
                }
            }

            // 個人回報，沒有找到對應的出席紀錄單，新增一個
            return CreatePresentRecordList(GroupName, ref this.m_ListEntity, ref WeeklyReportId, 0, 0, 0, 0, 0);
        }

        #endregion

        #region 出席紀錄建立

        /// <summary>
        /// 建立出席紀錄清單
        /// </summary>
        private EntityCollection CreatePresentRecordList(
            string GroupName, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            double ValidNumber, 
            double aWeeklySundayRate, 
            double aWeeklySmallGroupRate, 
            int aWeeklySundayNumber, 
            int aWeeklySmallGroupNumber)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            Member aMember = CreateMember(GroupName);
            Entity aPresentRecord = CreatePresentRecord(aMember, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber);

            aMember.PresentRecordId = aPresentRecord.Id.ToString();

            if (aPresentRecord != null)
            {
                PresentRecordEntityCollection.Entities.Add(aPresentRecord);
            }

            return PresentRecordEntityCollection;
        }

        /// <summary>
        /// 建立出席紀錄
        /// </summary>
        private Entity CreatePresentRecord(
            Member aMemberInfomation, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            double ValidNumber, 
            ref double aWeeklySundayRate, 
            ref double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber)
        {
            Entity aContactEntity = UpdateContactInfomationFromList(aMemberInfomation.FullName, aListEntity.Id);

            if (aContactEntity == null)
                return null;

            Entity aPresentRecord = new Entity("new_present_record");

            SetupPresentRecordEntityAttributes(
                aPresentRecord, 
                aMemberInfomation, 
                ref aContactEntity, 
                ref aListEntity, 
                ref aWeeklyReportId, 
                ValidNumber, 
                ref aWeeklySundayRate, 
                ref aWeeklySmallGroupRate, 
                ref aWeeklySundayNumber, 
                ref aWeeklySmallGroupNumber);

            Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);
            Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);

            this.m_ToolUtilityClass.AssignOwner("new_present_record", aRetrievedPresentRecord, this.m_ToolUtilityClass.GetOwnerId(aContactEntity));

            return aRetrievedPresentRecord;
        }

        /// <summary>
        /// 建立成員物件
        /// </summary>
        private Member CreateMember(string GroupName)
        {
            return new Member
            {
                PresentRecordId = ".......",
                ContactId = this.m_ContactEntity.Id.ToString(),
                Group = GroupName,
                FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "fullname"),
                
                Phone = DigitsOnly.Replace(this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "mobilephone"), ""),
                HomePhone = DigitsOnly.Replace(this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "telephone2"), ""),
                Address = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "address2_line1"),
                BirthDate = m_ToolUtilityClass.GetEntityDateTimeAttribute(this.m_ContactEntity, "birthdate"),
                Industry = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "new_industry"),
                EquipmentStatus = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "new_equipment_status"),
                SpiritualIdentity = ConvertIndexToSpiritualIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(this.m_ContactEntity, "new_spiriitual_identity")),
                BaptizedSituation = ConvertIndexToBaptizedSituation(this.m_ToolUtilityClass.GetOptionSetAttribute(this.m_ContactEntity, "new_baptized_situation")),
                
                Status = ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref m_ContactEntity, "customertypecode")),
                SmallGroupName = GroupName,
                SectionName = GroupName,
                PrayItem = "",
                Sunday = false,
                SmallGroup = false,
                Decision = false,
                
                FollowUpWeek = "未選擇",
                FollowUpResult = "",
                FollowUpOption = "",
                FollowUp = "",
                FollowUpNextStep = "",
                FollowUpNote = "",
                NewComerNote = "",
                
                SpiritualWork = 0,
                MorningPray = 0,
                GeneralCare = 0,
            };
        }

        #endregion

        #region 聯絡人查詢

        /// <summary>
        /// 從名單更新聯絡人資訊
        /// </summary>
        private Entity UpdateContactInfomationFromList(string ContactName, Guid ListEntityId)
        {
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);
            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");

            EntityCollection MemberCollection = GetMemberCollection(ListEntityId, ListType);

            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                Entity ContactEntity = GetContactFromMember(MemberEntity, ListType);

                if (IsActiveContact(ContactEntity))
                {
                    string FullName = m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "fullname");
                    if (FullName == ContactName)
                        return ContactEntity;
                }
            }

            return null;
        }

        #endregion

        #region 出席紀錄屬性設定

        /// <summary>
        /// 設定出席紀錄實體屬性
        /// </summary>
        private void SetupPresentRecordEntityAttributes(
            Entity aPresentRecord, 
            Member aMemberInfomation, 
            ref Entity aContactEntity, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            double ValidNumber, 
            ref double aWeeklySundayRate, 
            ref double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber)
        {
            try
            {
                // 設定名稱
                string PresentRecordName = $"{aMemberInfomation.FullName}-{this.m_Sunday:yy/MM/dd} 出席紀錄";
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", PresentRecordName);

                // 設定聯絡人關聯
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntity.Id);

                // 設定週報關聯
                if (aWeeklyReportId != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_group_present_weekly_report_prese", "new_group_present_weekly_report", aWeeklyReportId);
                }

                // 設定領袖關聯
                SetupLeaderReferences(ref aPresentRecord, ref aListEntity);

                // 設定日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", this.m_Sunday);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", this.m_Sunday);

                // 設定地點時間
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_place", this.m_ToolUtilityClass.GetEntityStringAttribute(aListEntity, "new_group_place"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_time", this.m_ToolUtilityClass.GetEntityStringAttribute(aListEntity, "new_group_time"));

                // 設定出席資料
                SetupAttendanceData(ref aPresentRecord, aMemberInfomation, ValidNumber, ref aWeeklySmallGroupNumber);

                // 設定新人跟進資料
                SetupFollowUpData(ref aPresentRecord, aMemberInfomation);

                // 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", aMemberInfomation.Phone);
            }
            catch (Exception e)
            {
                string ErrorString = $"ERROR : FullName = {this.GetType().FullName} , Time = {DateTime.Now} , Description = {e}";
                throw;
            }
        }

        /// <summary>
        /// 設定領袖關聯
        /// </summary>
        private void SetupLeaderReferences(ref Entity aPresentRecord, ref Entity aListEntity)
        {
            Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");
            Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
            Guid aRaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");
            Guid aShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");

            if (aFamilyLeaderId != Guid.Empty)
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_familyhead_present_record", "contact", aFamilyLeaderId);

            if (aGroupLeaderId != Guid.Empty)
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_groupleader_present_record", "contact", aGroupLeaderId);

            if (aRaceLeaderId != Guid.Empty)
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_race_leader_present_record", "contact", aRaceLeaderId);

            if (aShepherdLeaderId != Guid.Empty)
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_arealeader_present_record", "contact", aShepherdLeaderId);

            if (aListEntity.Id != Guid.Empty)
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_list_new_present_record", "list", aListEntity.Id);
        }

        /// <summary>
        /// 設定出席資料
        /// </summary>
        private void SetupAttendanceData(
            ref Entity aPresentRecord, 
            Member aMemberInfomation, 
            double ValidNumber, 
            ref int aWeeklySmallGroupNumber)
        {
            // 主日出席
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", aMemberInfomation.Sunday ? 1 : 0);
            if (aMemberInfomation.Sunday && ValidNumber > 0)
            {
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 1 / ValidNumber);
            }
            else
            {
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);
            }

            // 小組出席
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", aMemberInfomation.SmallGroup ? 1 : 0);
            if (aMemberInfomation.SmallGroup)
            {
                aWeeklySmallGroupNumber++;
                if (ValidNumber > 0)
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 1 / ValidNumber);
                }
            }
            else
            {
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);
            }

            // 其他出席
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_prayer_meeting_number", aMemberInfomation.PrayerMeeting ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_child_number", aMemberInfomation.Child ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_big_disciple_number", aMemberInfomation.BigDisciple ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leadership_small_lecture_number", aMemberInfomation.LeadershipSmallLecture ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leaders_gather_number", aMemberInfomation.Sunday ? 1 : 0);

            // 靈修資料
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_spiritual_work", aMemberInfomation.SpiritualWork);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_morning_pray", aMemberInfomation.MorningPray);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_general_care", aMemberInfomation.GeneralCare);
        }

        /// <summary>
        /// 設定跟進資料
        /// </summary>
        private void SetupFollowUpData(ref Entity aPresentRecord, Member aMemberInfomation)
        {
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMemberInfomation.FollowUpWeek));
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMemberInfomation.FollowUpResult));
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMemberInfomation.FollowUpNextStep));
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_follow_up", aMemberInfomation.FollowUpOption);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.PrayItem);
        }

        #endregion
    }
}
