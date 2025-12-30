using System;
using System.Collections.Generic;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 上傳整合資料 - 出席記錄管理 (Partial)
    /// 包含：建立/更新 Present Record
    /// </summary>
    public partial class UploadIntegrateData
    {
        #region 建立出席記錄

        private EntityCollection CreatePresentRecordList(
            SmallGroupData aSmallGroupData, 
            String GroupName, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref int ValidSundayMemberNumber, 
            ref int ValidSmallGroupMemberNumber, 
            ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            foreach (Member aMemberInfomation in aSmallGroupData.Members)
            {
                Entity aPresentRecord = CreatePresentRecord(
                    aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, 
                    ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                    ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                    ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                if (aPresentRecord != null)
                    PresentRecordEntityCollection.Entities.Add(aPresentRecord);
            }

            return PresentRecordEntityCollection;
        }

        private EntityCollection CreatePresentRecordListByList(
            SmallGroupData aSmallGroupData, 
            SmallGroupData aSmallGroupDataFromList, 
            String GroupName, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref int ValidSundayMemberNumber, 
            ref int ValidSmallGroupMemberNumber, 
            ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            if (aSmallGroupData.LoginType == "小組長")
            {
                foreach (Member aMemberInfomation in aSmallGroupData.Members)
                {
                    Entity aPresentRecord = CreatePresentRecord(
                        aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, 
                        ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                        ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                        ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                    this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                    if (aPresentRecord != null)
                        PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                }
            }
            else
            {
                foreach (Member aMemberInfomation in aSmallGroupDataFromList.Members)
                {
                    Entity aPresentRecord;
                    if (aSmallGroupData.Members[0].FullName != aMemberInfomation.FullName)
                    {
                        aPresentRecord = CreatePresentRecord(
                            aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, 
                            ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                            ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                            ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                    }
                    else
                    {
                        aPresentRecord = CreatePresentRecord(
                            aSmallGroupData.Members[0], ref aListEntity, ref aWeeklyReportId, ValidNumber, 
                            ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                            ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                            ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                    }

                    if (aPresentRecord != null)
                    {
                        this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);
                        PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                    }
                }
            }

            return PresentRecordEntityCollection;
        }

        private Entity CreatePresentRecord(
            Member aMemberInfomation, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            Entity aContactEntity = UpdateContactInfomationFromList(aMemberInfomation.FullName, aListEntity.Id);

            if (aContactEntity == null)
                return null;

            Entity aToUpdateContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactEntity.Id);
            UpdateContactInfomation(aListEntity.Id, aMemberInfomation, ref aToUpdateContactEntity, HappyWeekTopic);

            Entity aPresentRecord = new Entity("new_present_record");

            SetupPresentRecordEntityAttributes(
                aPresentRecord, aMemberInfomation, ref aContactEntity, ref aListEntity, 
                ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

            Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);

            return this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);
        }

        #endregion

        #region 更新出席記錄

        private void UpdatePresentRecord(
            List<MemberInfomation> aGroupNamedListMemberInfomation, 
            EntityCollection PresentRecordCollection, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, 
            SmallGroupData aSmallGroupData, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            try
            {
                foreach (Member aMember in aSmallGroupData.Members)
                {
                    Entity aMachedPresentRecordEntity = SearchPresentRecordByName(aMember.FullName, ref PresentRecordCollection);

                    if (aMachedPresentRecordEntity != null)
                    {
                        UpdateSinglePresentRecord(
                            aMember, aMachedPresentRecordEntity, ref aListEntity, 
                            ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                            ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                            HappyWeekTopic, PauseCheckBox);
                    }
                }

                // 移除已指派或轉介的成員
                for (int i = aSmallGroupData.Members.Count - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrEmpty(aSmallGroupData.Members[i].AssignedGroup) || 
                        aSmallGroupData.Members[i].FollowUpNextStep == "轉介")
                    {
                        aSmallGroupData.Members.RemoveAt(i);
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

        private void UpdateSinglePresentRecord(
            Member aMember,
            Entity aMachedPresentRecordEntity,
            ref Entity aListEntity,
            Double ValidNumber,
            ref Double aWeeklySundayRate,
            ref Double aWeeklySmallGroupRate,
            ref int aWeeklySundayNumber,
            ref int aWeeklySmallGroupNumber,
            String HappyWeekTopic,
            bool PauseCheckBox)
        {
            // 更新聯絡人資訊
            EntityReference aFullNameEntityReference = aMachedPresentRecordEntity.Attributes.Contains("new_contact_new_present_record")
                ? (EntityReference)aMachedPresentRecordEntity.Attributes["new_contact_new_present_record"]
                : new EntityReference();

            Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);
            Entity aToUpdateContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);
            UpdateContactInfomation(aListEntity.Id, aMember, ref aToUpdateContactEntity, HappyWeekTopic);

            // 取得委身類型
            String ClearIdentity = "";
            bool AccumulateFlag = this.IsValidMember(aMachedPresentRecordEntity, ref ClearIdentity);

            // 設定主日出席
            if (aMember.Sunday)
            {
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_sunday_present_this_week", 1);
                AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, true);
                aWeeklySundayNumber += 1;
                if (ValidNumber > 0 && AccumulateFlag)
                    aWeeklySundayRate += 1 / ValidNumber;
            }
            else
            {
                AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, false);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_sunday_present_this_week", 0);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_sunday_rate", 0.0);
            }

            // 設定小組出席
            if (aMember.SmallGroup)
            {
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_group_present_this_week", 1);
                AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, true);
                aWeeklySmallGroupNumber += 1;
                if (ValidNumber > 0 && AccumulateFlag)
                    aWeeklySmallGroupRate += 1 / ValidNumber;
            }
            else
            {
                AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, false);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_group_present_this_week", 0);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_small_group_rate", 0);
            }

            // 設定幸福小組與決志
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_happy_present", aMember.SmallGroup ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_happy_decision", aMember.Decision ? 1 : 0);

            // 設定代禱事項與跟進
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMember.PrayItem);
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMember.FollowUpWeek));
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMember.FollowUpResult));

            if (!string.IsNullOrEmpty(aMember.FollowUpNextStep))
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMember.FollowUpNextStep));

            if (!string.IsNullOrEmpty(aMember.FollowUpOption))
            {
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_followup_ways", ConvertFollowUpOptionToIndex(aMember.FollowUpOption));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_follow_up", aMember.FollowUpOption);
            }

            AddToDictionaryFollowByIdentity(ref ClearIdentity, ref aContactEntity, aMember);

            // 設定靈修次數
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_spiritual_work", aMember.SpiritualWork);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_morning_pray", aMember.MorningPray);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_general_care", aMember.GeneralCare);

            // 設定暫停與顯示
            this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aMachedPresentRecordEntity, "new_pause", PauseCheckBox);

            if (!string.IsNullOrEmpty(aMember.AssignedGroup) && !aMember.AssignedGroup.Contains("關懷"))
                m_ToolUtilityClass.SetEntityBoolAttribute(ref aMachedPresentRecordEntity, "new_not_display", true);

            this.m_ToolUtilityClass.AssignOwner("new_present_record", aMachedPresentRecordEntity, this.m_OwnerId);

            // 處理小組指派
            if (!string.IsNullOrEmpty(aMember.AssignedGroup))
                AssignNewSmallGroup(aMachedPresentRecordEntity, aMember.AssignedGroup, aListEntity);
            else if (aMember.FollowUpNextStep == "轉介")
                TerminateNewPersonFollowUp(aMachedPresentRecordEntity, aMember.AssignedGroup, aListEntity);

            this.m_ToolUtilityClass.UpdateEntity(ref aMachedPresentRecordEntity);
        }

        #endregion

        #region 設定出席記錄屬性

        private void SetupPresentRecordEntityAttributes(
            Entity aPresentRecord, 
            Member aMemberInfomation, 
            ref Entity aContactEntity, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            try
            {
                // 設定名稱
                String PresentRecordName = $"{aMemberInfomation.FullName}-{this.m_Sunday:00}/{this.m_Sunday.Month:00}/{this.m_Sunday.Day:00} 出席紀錄";
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", PresentRecordName);

                // 設定聯絡人
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntity.Id);

                // 關聯週報
                if (aWeeklyReportId != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_group_present_weekly_report_prese", "new_group_present_weekly_report", aWeeklyReportId);

                // 設定領袖關聯
                SetupLeaderReferences(ref aPresentRecord, ref aListEntity);

                // 設定日期與地點
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", this.m_Sunday);
                if (aGroupWeeklyReportGuid.SmallGroupDate.Year > 1)
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", aGroupWeeklyReportGuid.SmallGroupDate);

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_place", m_SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_time", m_SmallGroupTime);

                // 取得委身類型
                int OptionSetNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");
                String ClearIdentity = this.ConvertIndexToClearIdentity(OptionSetNumber);

                // 設定出席
                SetupAttendanceAttributes(ref aPresentRecord, aMemberInfomation, ref aListEntity, ValidNumber, 
                    ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                    ref ClearIdentity, ref aContactEntity);

                // 設定新人跟進
                SetupFollowUpAttributes(ref aPresentRecord, aMemberInfomation, ref ClearIdentity, ref aContactEntity);

                // 設定靈修與其他
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_spiritual_work", aMemberInfomation.SpiritualWork);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_morning_pray", aMemberInfomation.MorningPray);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_general_care", aMemberInfomation.GeneralCare);
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecord, "new_pause", PauseCheckBox);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", aMemberInfomation.Phone);
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

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

        private void SetupAttendanceAttributes(
            ref Entity aPresentRecord, 
            Member aMemberInfomation, 
            ref Entity aListEntity, 
            Double ValidNumber,
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber,
            ref String ClearIdentity, 
            ref Entity aContactEntity)
        {
            // 主日出席
            if (aMemberInfomation.Sunday)
            {
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 1);
                AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, true);
                aWeeklySundayNumber += 1;
                if (ValidNumber > 0 && IsValidContact(aContactEntity))
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 1 / ValidNumber);
                    aWeeklySundayRate += 1 / ValidNumber;
                }
            }
            else
            {
                AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, false);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);
            }

            // 小組出席
            if (aMemberInfomation.SmallGroup)
            {
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 1);
                AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, true);
                aWeeklySmallGroupNumber += 1;
                if (ValidNumber > 0 && IsValidContact(aContactEntity))
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 1 / ValidNumber);
                    aWeeklySmallGroupRate += 1 / ValidNumber;
                }
            }
            else
            {
                AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, false);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);
            }

            // 其他聚會
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_prayer_meeting_number", aMemberInfomation.PrayerMeeting ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_child_number", aMemberInfomation.Child ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_big_disciple_number", aMemberInfomation.BigDisciple ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leadership_small_lecture_number", aMemberInfomation.LeadershipSmallLecture ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leaders_gather_number", aMemberInfomation.Sunday ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", aMemberInfomation.SmallGroup ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_decision", aMemberInfomation.Decision ? 1 : 0);
        }

        private void SetupFollowUpAttributes(
            ref Entity aPresentRecord, 
            Member aMemberInfomation, 
            ref String ClearIdentity, 
            ref Entity aContactEntity)
        {
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMemberInfomation.FollowUpWeek));
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMemberInfomation.FollowUpResult));
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMemberInfomation.FollowUpNextStep));
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_follow_up", aMemberInfomation.FollowUpOption);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.PrayItem);

            AddToDictionaryFollowByIdentity(ref ClearIdentity, ref aContactEntity, aMemberInfomation);
        }

        #endregion

        #region 輔助方法

        private Entity SearchPresentRecordByName(String Name, ref EntityCollection PresentRecordCollection)
        {
            foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
            {
                String aPresentRecordName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_contact_new_present_record");
                if (Name == aPresentRecordName)
                {
                    Entity aRetrievedPresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", PresentRecordEntity.Id);

                    EntityReference aFullNameEntityReference = aRetrievedPresentRecordEntity.Attributes.Contains("new_contact_new_present_record")
                        ? (EntityReference)aRetrievedPresentRecordEntity.Attributes["new_contact_new_present_record"]
                        : new EntityReference();

                    Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);

                    if (m_ToolUtilityClass.GetOptionSetAttribute(aContactEntity, "customertypecode") != 100000001 &&
                        m_ToolUtilityClass.GetEntityBoolAttribute(aRetrievedPresentRecordEntity, "new_not_display") == false)
                    {
                        return PresentRecordEntity;
                    }
                }
            }
            return null;
        }

        public Double GetValidMemberNumber(EntityCollection aPresentRecordCollection)
        {
            try
            {
                Double ValidMemberNumber = 0;
                foreach (Entity PresentRecordEntity in aPresentRecordCollection.Entities)
                {
                    String ClearIdentity = "";
                    if (this.IsValidMember(PresentRecordEntity, ref ClearIdentity))
                        ValidMemberNumber++;
                }
                return ValidMemberNumber;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        public bool IsValidMember(Entity PresentRecordEntity, ref String ClearIdentity)
        {
            try
            {
                if (!PresentRecordEntity.Attributes.Contains("statecode"))
                    return false;

                OptionSetValue aOptionState = PresentRecordEntity.Attributes["statecode"] as OptionSetValue;
                if (aOptionState.Value != 0)
                    return false;

                if (!PresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
                    return false;

                EntityReference aEntityReference = (EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"];
                Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aEntityReference.Id);

                if (!aContactEntity.Attributes.Contains("customertypecode"))
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "customertypecode", 100000000);
                    this.m_ToolUtilityClass.UpdateEntity(ref aContactEntity);
                    return false;
                }

                OptionSetValue aCustomerTypeCode = aContactEntity.Attributes["customertypecode"] as OptionSetValue;
                ClearIdentity = this.ConvertIndexToClearIdentity(aCustomerTypeCode.Value);

                return aCustomerTypeCode.Value != 100000005 && 
                       aCustomerTypeCode.Value != 10000007 && 
                       aCustomerTypeCode.Value != 100000001;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        public bool IsValidContact(Entity aContactEntity)
        {
            try
            {
                int aCustomerTypeCodeValue = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");

                return aCustomerTypeCodeValue != 100000004 && 
                       aCustomerTypeCodeValue != 100000000 && 
                       aCustomerTypeCodeValue != 100000007 && 
                       aCustomerTypeCodeValue != EMPTY_VALUE;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        private double GetEffecttiveSmallGroupNumber(Guid ListEntityId)
        {
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);
            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");

            EntityCollection MemberCollection = ListType == false
                ? m_ToolUtilityClass.RetrieveMemberListCollectionByListId(ListEntityId)
                : m_ToolUtilityClass.RetrieveDynamicMemberList(ListEntityId);

            Double EffectiveNumber = 0.0;
            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                Entity ContactEntity = ListType == false
                    ? m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id)
                    : m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);

                if (!ContactEntity.Attributes.Contains("statecode"))
                    continue;

                OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;
                if (aOptionState.Value != 0)
                    continue;

                if (ContactEntity.Attributes.Contains("customertypecode"))
                {
                    OptionSetValue aCustomerTypeCode = ContactEntity.Attributes["customertypecode"] as OptionSetValue;
                    if (aCustomerTypeCode.Value != 100000004 && 
                        aCustomerTypeCode.Value != 100000000 && 
                        aCustomerTypeCode.Value != 100000007)
                    {
                        EffectiveNumber++;
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref ContactEntity, "customertypecode", 100000000);
                    this.m_ToolUtilityClass.UpdateEntity(ref ContactEntity);
                }
            }

            return EffectiveNumber;
        }

        #endregion
    }
}
