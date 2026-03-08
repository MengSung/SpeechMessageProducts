using System;
using System.Collections.Generic;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 上傳整合資料 - 小組指派 (Partial)
    /// 包含：指派新小組、結案處理
    /// </summary>
    public partial class UploadIntegrateData
    {
        #region 指派小組

        public String AssignNewSmallGroup(Entity aPresentRecordEntity, String AssignedSmallGroupName, Entity aActiveListEntity)
        {
            try
            {
                Entity aAssignedContact = GetAssignedContact(aPresentRecordEntity);
                Entity aAssingedSmallGroupEntity = this.m_ToolUtilityClass.RetrieveListEntityByName(AssignedSmallGroupName);

                AssignContactToList(AssignedSmallGroupName, aAssignedContact, aActiveListEntity, aAssingedSmallGroupEntity);

                if (!AssignedSmallGroupName.Contains("關懷"))
                    SetNotRemindFlag(aAssignedContact);

                if (aPresentRecordEntity != null)
                    m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecordEntity, "new_not_display", true);

                return "指派小組";
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        public Entity GetAssignedContact(Entity aPresentRecordEntity)
        {
            try
            {
                Guid AssignedContactId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aPresentRecordEntity, "new_contact_new_present_record");
                return this.m_ToolUtilityClass.RetrieveEntity("contact", AssignedContactId);
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        public String AssignContactToList(String AssignedSmallGroupName, Entity aAssignedContact, Entity aActiveListEntity, Entity aAssignedListEntity)
        {
            try
            {
                var aContactToBeSentToDynamics = new Entity("contact", aAssignedContact.Id);

                ConnectNewContactInMemberList(aContactToBeSentToDynamics.Id, AssignedSmallGroupName, aAssignedListEntity);

                try
                {
                    m_ToolUtilityClass.RemoveMembersToMarketingList(aActiveListEntity.Id, aContactToBeSentToDynamics.Id);
                }
                catch { }

                if (aAssignedListEntity != null && !AssignedSmallGroupName.Contains("關懷"))
                    CreateAssignedContactPresentRecord(aAssignedListEntity, aContactToBeSentToDynamics.Id, AssignedSmallGroupName);

                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aContactToBeSentToDynamics, "new_cell_list_contact", "list", aAssignedListEntity.Id);
                this.m_ToolUtilityClass.UpdateEntity(ref aContactToBeSentToDynamics);

                String LoginContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "fullname");
                String ExistContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(aAssignedContact, "fullname");

                m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);
                if (m_OwnerId != Guid.Empty)
                    this.m_ToolUtilityClass.AssignOwner("contact", aAssignedContact, this.m_OwnerId);

                String Result = $"{LoginContactFullName} 成功的加入 {ExistContactFullName} 到 {AssignedSmallGroupName}小組中";

                this.m_LineNotifyUtility.SendAddNewPersonResultLine(Result, aAssignedListEntity, "指派");
                this.m_LineNotifyUtility.SendListMemberLine(aAssignedListEntity, "指派");
                this.m_LineNotifyUtility.SendAddNewPersonResultLine(Result, aActiveListEntity, "指派");
                this.m_LineNotifyUtility.SendListMemberLine(aActiveListEntity, "指派");

                return Result;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private void ConnectNewContactInMemberList(Guid NewContactEntityId, String GroupName, Entity aListEntity)
        {
            try
            {
                if (aListEntity == null)
                    return;

                bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(aListEntity, "type");

                if (!ListType)
                {
                    List<Guid> memberGuidList = new List<Guid> { NewContactEntityId };
                    m_ToolUtilityClass.AddMembersToMarketingList(aListEntity.Id, memberGuidList);
                }
                else
                {
                    Entity aNewContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", NewContactEntityId);
                    EntityReference aListEntityReference = new EntityReference("list", aListEntity.Id);
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_list_contact", ref aListEntityReference);
                    this.m_ToolUtilityClass.UpdateEntity(ref aNewContactEntity);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private void CreateAssignedContactPresentRecord(Entity aListEntity, Guid NewContactEntityId, String GroupName)
        {
            try
            {
                if (aListEntity == null || NewContactEntityId == Guid.Empty)
                    return;

                // 依設定檔的每週第一日規則，取得今天所屬週次的主日日期。
                m_Sunday = ChurchReport.Services.SundayCalculator.CalculateSunday(
                    DateTime.Now,
                    ChurchReport.Services.WeeklyScheduleProvider.FirstDayOfWeek);

                EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.QueryWeeklyReportBySunday(this.m_Sunday, aListEntity.Id);

                if (GroupWeeklyReportEntityCollection.Entities.Count != 1)
                    return;

                EntityCollection PresentRecordEntityCollection = m_ToolUtilityClass.QueryPresentRecordInWeeklyReportByContactId(
                    NewContactEntityId, GroupWeeklyReportEntityCollection.Entities[0].Id);

                if (PresentRecordEntityCollection.Entities.Count > 0)
                    return;

                Entity aPresentRecord = new Entity("new_present_record");
                Entity aNewContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", NewContactEntityId);

                if (aNewContactEntity == null)
                    return;

                Guid aWeeklyReportId = GroupWeeklyReportEntityCollection.Entities[0].Id;
                String FullName = aNewContactEntity.Attributes.Contains("fullname") ? (string)aNewContactEntity.Attributes["fullname"] : "";
                String aMobilePhone = aNewContactEntity.Attributes.Contains("mobilephone") ? (string)aNewContactEntity.Attributes["mobilephone"] : "";
                String aHomePhone = aNewContactEntity.Attributes.Contains("telephone2") ? (string)aNewContactEntity.Attributes["telephone2"] : "";
                String aAddress = aNewContactEntity.Attributes.Contains("address2_line1") ? (string)aNewContactEntity.Attributes["address2_line1"] : "";

                String aFollowUpWeek = "";
                String aNewComerNote = GetNewComerFollowupInfo(NewContactEntityId, ref aFollowUpWeek);

                MemberInfomation aMemberInfomation = new MemberInfomation()
                {
                    Group = GroupName,
                    Name = FullName,
                    Phone = DigitsOnly.Replace(aMobilePhone, ""),
                    HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                    Address = aAddress,
                    Note = "",
                    Date = "2015/10/6",
                    Number = 5,
                    SundayPresent = false,
                    SmallGroupPresent = false,
                    FollowUpWeek = ".",
                    FollowUpResult = ".",
                    FollowUpNextStep = ".",
                    NewComerNote = aNewComerNote,
                };

                Double DUM_DOUBLE = 0;
                int DUM_INT = 0;

                SetupPresentRecordEntityAttributes(aPresentRecord, aMemberInfomation, ref aNewContactEntity, ref aListEntity, 
                    ref aWeeklyReportId, DUM_DOUBLE, ref DUM_DOUBLE, ref DUM_DOUBLE, ref DUM_INT, ref DUM_INT, ref DUM_INT, ref DUM_INT);

                Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);
                Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);
                this.m_ToolUtilityClass.AssignOwner("new_present_record", aRetrievedPresentRecord, this.m_ToolUtilityClass.GetOwnerId(aNewContactEntity));
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion

        #region 結案處理

        public String TerminateNewPersonFollowUp(Entity aPresentRecordEntity, String AssignedSmallGroupName, Entity aActiveListEntity)
        {
            try
            {
                Entity aAssignedContact = GetAssignedContact(aPresentRecordEntity);

                SetNotRemindFlag(aAssignedContact);

                this.m_ToolUtilityClass.SetEntityLookUpToNull(ref aAssignedContact, "new_cell_list_contact");
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aAssignedContact, "new_closed_date", DateTime.Now);
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aAssignedContact, "customertypecode", 100000001);
                this.m_ToolUtilityClass.UpdateEntity(ref aAssignedContact);

                try
                {
                    m_ToolUtilityClass.RemoveMembersToMarketingList(aActiveListEntity.Id, aAssignedContact.Id);
                }
                catch { }

                if (aPresentRecordEntity != null)
                    m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecordEntity, "new_not_display", true);

                String Result = $"{this.m_ToolUtilityClass.GetEntityStringAttribute(aAssignedContact, "fullname")}從{this.m_ToolUtilityClass.GetEntityStringAttribute(aActiveListEntity, "listname")} 被結案了";
                this.m_LineNotifyUtility.SendAddNewPersonResultLine(Result, aActiveListEntity);

                return "指派小組";
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        public void SetNotRemindFlag(Entity aAssignedContact)
        {
            try
            {
                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndContainEpiredDate(
                    this.m_ToolUtilityClass.GetEntityStringAttribute(aAssignedContact, "fullname"), aAssignedContact.Id.ToString());

                foreach (Entity aPresentRecord in aPresentRecordCollection.Entities)
                {
                    Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecord.Id);
                    m_ToolUtilityClass.SetEntityBoolAttribute(ref aRetrievedPresentRecord, "new_not_display", true);
                    this.m_ToolUtilityClass.UpdateEntity(ref aRetrievedPresentRecord);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        public void TerminateNewPersonCareWorkflow(Entity aPresentRecordEntity, String AssignedSmallGroupName)
        {
            try
            {
                m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecordEntity, "new_not_display", true);
                this.m_ToolUtilityClass.UpdateEntity(ref aPresentRecordEntity);
            }
            catch (System.Exception e)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion

        #region 輔助出席記錄方法 (for MemberInfomation)

        private void SetupPresentRecordEntityAttributes(
            Entity aPresentRecord, 
            MemberInfomation aMemberInfomation, 
            ref Entity aContactEntity, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref int ValidSundayMemberNumber, 
            ref int ValidSmallGroupMemberNumber)
        {
            try
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", 
                    this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "fullname") + "-" + m_Sunday.ToShortDateString() + " 出席紀錄");

                Guid aContactEntityId = aContactEntity.Id;
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntityId);

                if (aWeeklyReportId != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_group_present_weekly_report_prese", "new_group_present_weekly_report", aWeeklyReportId);

                // 設定領袖關聯
                Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");
                Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
                Guid aRaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                if (aFamilyLeaderId != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_familyhead_present_record", "contact", aFamilyLeaderId);
                if (aGroupLeaderId != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_groupleader_present_record", "contact", aGroupLeaderId);
                if (aRaceLeaderId != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_race_leader_present_record", "contact", aRaceLeaderId);
                if (aListEntity.Id != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_list_new_present_record", "list", aListEntity.Id);

                // 日期與地點
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", this.m_Sunday);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", this.m_Sunday);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_place", m_SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_time", m_SmallGroupTime);

                // 委身類型
                int OptionSetNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");
                String ClearIdentity = this.ConvertIndexToClearIdentity(OptionSetNumber);

                // 出席設定
                if (aMemberInfomation.SundayPresent)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 1);
                    AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, true);
                    aWeeklySundayNumber += 1;
                    if (ValidNumber != 0 && IsValidContact(aContactEntity))
                    {
                        ValidSundayMemberNumber++;
                        aWeeklySundayRate += 1 / ValidNumber;
                    }
                }
                else
                {
                    AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, false);
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);
                }

                if (aMemberInfomation.SmallGroupPresent)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 1);
                    AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, true);
                    aWeeklySmallGroupNumber += 1;
                    if (ValidNumber != 0 && IsValidContact(aContactEntity))
                    {
                        ValidSmallGroupMemberNumber++;
                        aWeeklySmallGroupRate += 1 / ValidNumber;
                    }
                }
                else
                {
                    AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, false);
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);
                }

                // 跟進設定
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.Note);
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMemberInfomation.FollowUpWeek));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMemberInfomation.FollowUpResult));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMemberInfomation.FollowUpNextStep));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_follow_up", aMemberInfomation.FollowUpOption);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", aMemberInfomation.Phone);
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        #endregion
    }
}
