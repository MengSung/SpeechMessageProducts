using System;
using System.Collections.Generic;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 整合資料下載服務 - 成員資料處理
    /// </summary>
    public partial class DownloadIntegrateData
    {
        #region 成員資料取得

        /// <summary>
        /// 取得所有成員資料清單
        /// </summary>
        public void GetAllMemeberDataList(
            string ListEntityId, 
            string WeeklyReportEntityId, 
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 初始化成員資料
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData = new SmallGroupData
            {
                Members = new List<Member>(),
                LoginType = aListSmallGroupWeeklyReport.LoginType
            };

            if (!string.IsNullOrEmpty(WeeklyReportEntityId))
            {
                // 有週報 -> 從出席紀錄取得成員
                GetAllMemberDataFromPresentRecord(
                    aListSmallGroupWeeklyReport.ListEntityName, 
                    new Guid(WeeklyReportEntityId), 
                    ref aListSmallGroupWeeklyReport);
            }
            else
            {
                // 無週報 -> 從名單取得成員
                if (m_LoginType == "小組長")
                {
                    GetAllMemberDataFromList(
                        aListSmallGroupWeeklyReport.ListEntityName, 
                        new Guid(ListEntityId), 
                        ref aListSmallGroupWeeklyReport);
                }
                else
                {
                    SetAllMemberDataByPersonalReport(
                        aListSmallGroupWeeklyReport.ListEntityName, 
                        ref aListSmallGroupWeeklyReport);
                }
            }
        }

        #endregion

        #region 從出席紀錄取得成員

        /// <summary>
        /// 從出席紀錄取得所有成員資料
        /// </summary>
        private void GetAllMemberDataFromPresentRecord(
            string GroupName, 
            Guid WeeklyReportId, 
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            EntityCollection PresentRecordCollection = GetPresentRecordByLoginType(GroupName, WeeklyReportId, ref aListSmallGroupWeeklyReport);

            foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
            {
                ProcessPresentRecordEntity(GroupName, PresentRecordEntity, ref aListSmallGroupWeeklyReport);
            }
        }

        /// <summary>
        /// 處理單筆出席紀錄
        /// </summary>
        private void ProcessPresentRecordEntity(
            string GroupName, 
            Entity PresentRecordEntity, 
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            if (!PresentRecordEntity.Attributes.Contains("statecode"))
                return;

            OptionSetValue aOptionState = PresentRecordEntity.Attributes["statecode"] as OptionSetValue;

            // 只處理使用中且未隱藏的紀錄
            if (aOptionState.Value != 0 || this.m_ToolUtilityClass.GetEntityBoolAttribute(PresentRecordEntity, "new_not_display"))
                return;

            // 取得聯絡人參照
            if (!PresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
                return;

            EntityReference aFullNameEntityReference = (EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"];
            string FullName = (string)aFullNameEntityReference.Name;
            string ContactId = aFullNameEntityReference.Id.ToString();

            // 取得聯絡人詳細資料
            Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);

            // 建立成員物件
            Member member = CreateMemberFromPresentRecord(GroupName, PresentRecordEntity, aContactEntity, FullName, ContactId);

            // 排除結案成員
            if (member.Status != "10. 未入組結案")
            {
                aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add(member);
            }
        }

        /// <summary>
        /// 從出席紀錄建立成員物件
        /// </summary>
        private Member CreateMemberFromPresentRecord(
            string GroupName, 
            Entity PresentRecordEntity, 
            Entity aContactEntity, 
            string FullName, 
            string ContactId)
        {
            // 取得聯絡人基本資料
            var contactInfo = ExtractContactInfo(aContactEntity);

            // 取得出席紀錄資料
            var attendanceInfo = ExtractAttendanceInfo(PresentRecordEntity);

            // 取得新人跟進資料
            var followUpInfo = ExtractFollowUpInfo(PresentRecordEntity, ((EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"]).Id);

            return new Member
            {
                PresentRecordId = PresentRecordEntity.Id.ToString(),
                ContactId = ContactId,
                Group = GroupName,
                FullName = FullName,
                
                // 個人基本資料
                Phone = DigitsOnly.Replace(contactInfo.MobilePhone, ""),
                HomePhone = DigitsOnly.Replace(contactInfo.HomePhone, ""),
                Address = contactInfo.Address,
                BirthDate = contactInfo.BirthDate,
                Industry = contactInfo.Industry,
                EquipmentStatus = contactInfo.EquipmentStatus,
                SpiritualIdentity = contactInfo.SpiritualIdentity,
                BaptizedSituation = contactInfo.BaptizedSituation,
                BestLeader = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aContactEntity, "new_contact_contact_spiritleader"),
                BestIntroducer = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "new_best_introducer"),
                BestRelationship = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "new_best_relationship"),
                Description = contactInfo.Description,
                
                // 委身類型
                Status = ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode")),
                SmallGroupName = GroupName,
                SectionName = GroupName,
                
                // 出席資料
                PrayItem = attendanceInfo.Note,
                Sunday = attendanceInfo.SundayPresent,
                SmallGroup = attendanceInfo.SmallGroupPresent,
                PrayerMeeting = attendanceInfo.PrayerMeeting,
                Child = attendanceInfo.Child,
                BigDisciple = attendanceInfo.BigDisciple,
                LeadershipSmallLecture = attendanceInfo.LeadershipSmallLecture,
                LeadersGather = attendanceInfo.LeadersGather,
                Decision = attendanceInfo.Decision,
                
                // 靈修資料
                SpiritualWork = attendanceInfo.SpiritualWork,
                MorningPray = attendanceInfo.MorningPray,
                GeneralCare = attendanceInfo.GeneralCare,
                
                // 新人跟進
                FollowUpWeek = followUpInfo.FollowUpWeek,
                FollowUpResult = followUpInfo.FollowUpResult,
                FollowUpOption = followUpInfo.FollowUpOption,
                FollowUp = followUpInfo.FollowUp,
                FollowUpNextStep = followUpInfo.FollowUpNextStep,
                FollowUpNote = followUpInfo.FollowUpNote,
                NewComerNote = followUpInfo.NewComerNote,
            };
        }

        #endregion

        #region 從名單取得成員

        /// <summary>
        /// 從名單取得所有成員資料
        /// </summary>
        private void GetAllMemberDataFromList(
            string GroupName, 
            Guid ListEntityId, 
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);
            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");

            EntityCollection MemberCollection = GetMemberCollection(ListEntityId, ListType);

            int PresentRecordIdCounter = 0;
            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                Entity ContactEntity = GetContactFromMember(MemberEntity, ListType);

                if (IsActiveContact(ContactEntity))
                {
                    Member member = CreateMemberFromContact(GroupName, ContactEntity, PresentRecordIdCounter++);
                    
                    if (member.Status != "10. 未入組結案")
                    {
                        aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add(member);
                    }
                }
            }
        }

        /// <summary>
        /// 取得成員集合
        /// </summary>
        private EntityCollection GetMemberCollection(Guid ListEntityId, bool ListType)
        {
            if (ListType == false)
            {
                // 靜態名單
                return CRM_TYPE == "DYNAMICS365"
                    ? this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId)
                    : this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
            }
            else
            {
                // 動態名單
                return CRM_TYPE == "DYNAMICS365"
                    ? this.m_ToolUtilityClass.RetrieveDynamicMemberListDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId)
                    : this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
            }
        }

        /// <summary>
        /// 從成員實體取得聯絡人
        /// </summary>
        private Entity GetContactFromMember(Entity MemberEntity, bool ListType)
        {
            if (ListType == false)
            {
                return m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
            }
            else
            {
                return m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
            }
        }

        /// <summary>
        /// 檢查聯絡人是否為使用中
        /// </summary>
        private bool IsActiveContact(Entity ContactEntity)
        {
            if (!ContactEntity.Attributes.Contains("statecode"))
                return false;

            OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;
            return aOptionState.Value == 0;
        }

        /// <summary>
        /// 從聯絡人建立成員物件
        /// </summary>
        private Member CreateMemberFromContact(string GroupName, Entity ContactEntity, int counter)
        {
            var contactInfo = ExtractContactInfo(ContactEntity);
            string aFollowUpWeek = "未選擇";
            string aNewComerNote = GetNewComerFollowupInfo(ContactEntity.Id, ref aFollowUpWeek);

            return new Member
            {
                PresentRecordId = counter.ToString(),
                ContactId = ContactEntity.Id.ToString(),
                Group = GroupName,
                FullName = contactInfo.FullName,
                
                // 個人基本資料
                Phone = DigitsOnly.Replace(contactInfo.MobilePhone, ""),
                HomePhone = DigitsOnly.Replace(contactInfo.HomePhone, ""),
                Address = contactInfo.Address,
                BirthDate = contactInfo.BirthDate,
                Industry = contactInfo.Industry,
                EquipmentStatus = contactInfo.EquipmentStatus,
                SpiritualIdentity = contactInfo.SpiritualIdentity,
                BaptizedSituation = contactInfo.BaptizedSituation,
                BestLeader = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ContactEntity, "new_contact_contact_spiritleader"),
                BestIntroducer = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "new_best_introducer"),
                BestRelationship = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "new_best_relationship"),
                Description = contactInfo.Description,
                
                // 委身類型
                Status = ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref ContactEntity, "customertypecode")),
                SmallGroupName = GroupName,
                SectionName = GroupName,
                
                // 預設值
                PrayItem = "",
                Sunday = false,
                SmallGroup = false,
                Decision = false,
                
                // 新人跟進
                FollowUpWeek = aFollowUpWeek,
                FollowUpResult = "",
                FollowUpOption = "",
                FollowUp = "",
                FollowUpNextStep = "",
                FollowUpNote = "",
                NewComerNote = aNewComerNote,
                
                // 靈修資料
                SpiritualWork = 0,
                MorningPray = 0,
                GeneralCare = 0,
            };
        }

        #endregion

        #region 個人回報

        /// <summary>
        /// 設定個人回報成員資料
        /// </summary>
        private void SetAllMemberDataByPersonalReport(string GroupName, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            var contactInfo = ExtractContactInfo(m_ContactEntity);
            string aFollowUpWeek = "未選擇";
            string aNewComerNote = GetNewComerFollowupInfo(m_ContactEntity.Id, ref aFollowUpWeek);

            string aIdentity = ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref m_ContactEntity, "customertypecode"));

            if (aIdentity != "10. 未入組結案")
            {
                aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add(new Member
                {
                    PresentRecordId = DateTime.Now.ToLongTimeString(),
                    ContactId = m_ContactEntity.Id.ToString(),
                    Group = GroupName,
                    FullName = contactInfo.FullName,
                    
                    Phone = DigitsOnly.Replace(contactInfo.MobilePhone, ""),
                    HomePhone = DigitsOnly.Replace(contactInfo.HomePhone, ""),
                    Address = contactInfo.Address,
                    BirthDate = contactInfo.BirthDate,
                    Industry = contactInfo.Industry,
                    EquipmentStatus = contactInfo.EquipmentStatus,
                    SpiritualIdentity = contactInfo.SpiritualIdentity,
                    BaptizedSituation = contactInfo.BaptizedSituation,
                    BestLeader = this.m_ToolUtilityClass.GetEntityLookupDisplayName(m_ContactEntity, "new_contact_contact_spiritleader"),
                    BestIntroducer = this.m_ToolUtilityClass.GetEntityStringAttribute(m_ContactEntity, "new_best_introducer"),
                    BestRelationship = this.m_ToolUtilityClass.GetEntityStringAttribute(m_ContactEntity, "new_best_relationship"),
                    Description = contactInfo.Description,
                    
                    Status = aIdentity,
                    SmallGroupName = GroupName,
                    SectionName = GroupName,
                    
                    PrayItem = "",
                    Sunday = false,
                    SmallGroup = false,
                    Decision = false,
                    
                    FollowUpWeek = aFollowUpWeek,
                    FollowUpResult = "",
                    FollowUpOption = "",
                    FollowUp = "",
                    FollowUpNextStep = "",
                    FollowUpNote = "",
                    NewComerNote = aNewComerNote,
                    
                    SpiritualWork = 0,
                    MorningPray = 0,
                    GeneralCare = 0,
                });
            }
        }

        #endregion

        #region 資料提取輔助類別

        /// <summary>
        /// 聯絡人資訊
        /// </summary>
        private record ContactInfo(
            string FullName,
            string MobilePhone,
            string HomePhone,
            string Address,
            DateTime BirthDate,
            string Industry,
            string EquipmentStatus,
            string SpiritualIdentity,
            string BaptizedSituation,
            string Description
        );

        /// <summary>
        /// 出席資訊
        /// </summary>
        private record AttendanceInfo(
            string Note,
            bool SundayPresent,
            bool SmallGroupPresent,
            bool PrayerMeeting,
            bool Child,
            bool BigDisciple,
            bool LeadershipSmallLecture,
            bool LeadersGather,
            bool Decision,
            int SpiritualWork,
            int MorningPray,
            int GeneralCare
        );

        /// <summary>
        /// 跟進資訊
        /// </summary>
        private record FollowUpInfoRecord(
            string FollowUpWeek,
            string FollowUpResult,
            string FollowUpOption,
            string FollowUp,
            string FollowUpNextStep,
            string FollowUpNote,
            string NewComerNote
        );

        /// <summary>
        /// 提取聯絡人資訊
        /// </summary>
        private ContactInfo ExtractContactInfo(Entity contactEntity)
        {
            return new ContactInfo(
                FullName: m_ToolUtilityClass.GetEntityStringAttribute(contactEntity, "fullname"),
                MobilePhone: m_ToolUtilityClass.GetEntityStringAttribute(contactEntity, "mobilephone"),
                HomePhone: m_ToolUtilityClass.GetEntityStringAttribute(contactEntity, "telephone2"),
                Address: m_ToolUtilityClass.GetEntityStringAttribute(contactEntity, "address2_line1"),
                BirthDate: m_ToolUtilityClass.GetEntityDateTimeAttribute(ref contactEntity, "birthdate").ToLocalTime(),
                Industry: m_ToolUtilityClass.GetEntityStringAttribute(contactEntity, "new_industry"),
                EquipmentStatus: m_ToolUtilityClass.GetEntityStringAttribute(contactEntity, "new_equipment_status"),
                SpiritualIdentity: contactEntity.Attributes.Contains("new_spiriitual_identity")
                    ? ConvertIndexToSpiritualIdentity(m_ToolUtilityClass.GetOptionSetAttribute(contactEntity, "new_spiriitual_identity"))
                    : "",
                BaptizedSituation: contactEntity.Attributes.Contains("new_baptized_situation")
                    ? ConvertIndexToBaptizedSituation(m_ToolUtilityClass.GetOptionSetAttribute(contactEntity, "new_baptized_situation"))
                    : "",
                Description: m_ToolUtilityClass.GetEntityStringAttribute(contactEntity, "description")
            );
        }

        /// <summary>
        /// 提取出席資訊
        /// </summary>
        private AttendanceInfo ExtractAttendanceInfo(Entity presentRecordEntity)
        {
            return new AttendanceInfo(
                Note: m_ToolUtilityClass.GetEntityStringAttribute(presentRecordEntity, "new_explanation"),
                SundayPresent: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_sunday_present_this_week") > 0,
                SmallGroupPresent: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_group_present_this_week") > 0,
                PrayerMeeting: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_prayer_meeting_number") > 0,
                Child: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_child_number") > 0,
                BigDisciple: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_big_disciple_number") > 0,
                LeadershipSmallLecture: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_leadership_small_lecture_number") > 0,
                LeadersGather: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_leaders_gather_number") > 0,
                Decision: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_happy_decision") > 0,
                SpiritualWork: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_spiritual_work"),
                MorningPray: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_morning_pray"),
                GeneralCare: m_ToolUtilityClass.GetEntityIntAttribute(presentRecordEntity, "new_general_care")
            );
        }

        /// <summary>
        /// 提取跟進資訊
        /// </summary>
        private FollowUpInfoRecord ExtractFollowUpInfo(Entity presentRecordEntity, Guid contactId)
        {
            string followUpWeek = presentRecordEntity.Attributes.Contains("new_weeks")
                ? ConvertIndexToFollowUpWeekPicker(m_ToolUtilityClass.GetOptionSetAttribute(presentRecordEntity, "new_weeks"))
                : "";

            string followUpResult = presentRecordEntity.Attributes.Contains("new_conclusion_choise")
                ? ConvertIndexToFollowUpResultPicker(m_ToolUtilityClass.GetOptionSetAttribute(presentRecordEntity, "new_conclusion_choise"))
                : "";

            string followUpNextStep = presentRecordEntity.Attributes.Contains("new_next_step")
                ? ConvertIndexToFollowUpNextStepPicker(m_ToolUtilityClass.GetOptionSetAttribute(presentRecordEntity, "new_next_step"))
                : "";

            string followUpOption = presentRecordEntity.Attributes.Contains("new_followup_ways")
                ? ConvertIndexToFollowUpOptionPicker(m_ToolUtilityClass.GetOptionSetAttribute(presentRecordEntity, "new_followup_ways"))
                : "";

            string newComerNote = GetNewComerFollowupInfo(contactId, ref followUpWeek);

            return new FollowUpInfoRecord(
                FollowUpWeek: followUpWeek,
                FollowUpResult: followUpResult,
                FollowUpOption: followUpOption,
                FollowUp: m_ToolUtilityClass.GetEntityStringAttribute(presentRecordEntity, "new_follow_up"),
                FollowUpNextStep: followUpNextStep,
                FollowUpNote: m_ToolUtilityClass.GetEntityStringAttribute(presentRecordEntity, "new_explanation"),
                NewComerNote: newComerNote
            );
        }

        #endregion
    }
}
