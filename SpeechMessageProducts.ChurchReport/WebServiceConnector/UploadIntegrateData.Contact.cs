// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/UploadIntegrateData.Contact.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class UploadIntegrateData
// 主要成員：GetContactFromList、UpdateContactInfomationFromList、GetMemberCollection、GetContactFromMemberEntity、GetPersonalSmallGroupLeaderMemberData、UpdateContactInfomation、GetContactSpiritLeaderId、SetIdentityByUpload、SetIdentity、GetPresentNumber
// 引用命名空間：System、System.Collections.Generic、ChurchReport.Models、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 上傳整合資料 - 聯絡人處理 (Partial)
    /// 包含：更新聯絡人資訊、委身類型設定
    /// </summary>
    public partial class UploadIntegrateData
    {
        #region 聯絡人查詢

        private Entity GetContactFromList(Guid ListEntityId, String aContactFullName)
        {
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);
            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");

            EntityCollection MemberCollection = GetMemberCollection(ListEntityId, ListType);

            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                Entity ContactEntity = GetContactFromMemberEntity(MemberEntity, ListType);

                if (ContactEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;
                    if (aOptionState.Value == 0)
                    {
                        if (this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "fullname") == aContactFullName)
                            return ContactEntity;
                    }
                }
            }
            return null;
        }

        private Entity UpdateContactInfomationFromList(String ContactName, Guid ListEntityId)
        {
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);
            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");

            EntityCollection MemberCollection = GetMemberCollection(ListEntityId, ListType);

            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                Entity ContactEntity = GetContactFromMemberEntity(MemberEntity, ListType);

                if (ContactEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;
                    if (aOptionState.Value == 0 && ContactEntity.Attributes.Contains("fullname"))
                    {
                        String FullName = (string)ContactEntity.Attributes["fullname"];
                        if (FullName == ContactName)
                            return ContactEntity;
                    }
                }
            }
            return null;
        }

        private EntityCollection GetMemberCollection(Guid ListEntityId, bool ListType)
        {
            if (ListType == false)
            {
                return this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
            }
            else
            {
                return this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
            }
        }

        private Entity GetContactFromMemberEntity(Entity MemberEntity, bool ListType)
        {
            return ListType == false
                ? m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id)
                : m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
        }

        private EntityCollection GetPersonalSmallGroupLeaderMemberData(Guid ListEntityId, ref bool ListType)
        {
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);
            ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");
            return GetMemberCollection(ListEntityId, ListType);
        }

        #endregion

        #region 更新聯絡人資訊

        private void UpdateContactInfomation(Guid aListEntityId, Member aMember, ref Entity aContactEntity, String HappyWeekTopic)
        {
            bool ModifyFlag = false;

            // 手機
            if (aMember.Phone != null &&
                DigitsOnly.Replace(aMember.Phone, "") != DigitsOnly.Replace(this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "mobilephone"), ""))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "mobilephone", aMember.Phone);
                ModifyFlag = true;
            }

            // 家裡電話
            if (aMember.HomePhone != null &&
                DigitsOnly.Replace(aMember.HomePhone, "") != DigitsOnly.Replace(this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "telephone2"), ""))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "telephone2", aMember.HomePhone);
                ModifyFlag = true;
            }

            // 地址
            if (aMember.Address != null &&
                aMember.Address != this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "address2_line1"))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "address2_line1", aMember.Address);
                ModifyFlag = true;
            }

            // 生日
            if (aMember.BirthDate != null && aMember.BirthDate > DateTime.MinValue && aMember.BirthDate.Year > 1753)
            {
                if (aContactEntity.Attributes.Contains("birthdate"))
                {
                    DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContactEntity, "birthdate").ToLocalTime();
                    if (aMember.BirthDate != aBirthDate)
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "birthdate", aMember.BirthDate);
                        ModifyFlag = true;
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "birthdate", aMember.BirthDate);
                    ModifyFlag = true;
                }
            }

            // 職業及專長
            if (aMember.Industry != null &&
                aMember.Industry != this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_industry"))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_industry", aMember.Industry);
                ModifyFlag = true;
            }

            // 介紹人
            if (aMember.BestIntroducer != null &&
                aMember.BestIntroducer != this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_best_introducer"))
            {
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_best_introducer", aMember.BestIntroducer);
                ModifyFlag = true;
            }

            // 介紹人關係
            if (aMember.BestRelationship != null && aContactEntity.Attributes.Contains("new_best_relationship"))
            {
                String aBestRelationship = (string)aContactEntity.Attributes["new_best_relationship"];
                if (aMember.BestRelationship != aBestRelationship)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_best_relationship", aMember.BestRelationship);
                    ModifyFlag = true;
                }
            }

            // Best Leader (幸福小組專用)
            if (!string.IsNullOrEmpty(aMember.BestLeader) && this.m_GroupType == "幸福小組")
            {
                Guid aSearchedContactSpiritLeaderId = GetContactSpiritLeaderId(aListEntityId, aMember.BestLeader);
                if (aSearchedContactSpiritLeaderId != Guid.Empty)
                {
                    Guid aContactSpiritLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aContactEntity, "new_contact_contact_spiritleader");
                    if (aContactSpiritLeaderId != aSearchedContactSpiritLeaderId)
                    {
                        this.m_ToolUtilityClass.SetEntityLookUpAttribute(aContactEntity, "new_contact_contact_spiritleader", "contact", aSearchedContactSpiritLeaderId);
                        ModifyFlag = true;
                    }
                }
            }

            // 幸福小組出席
            if (!string.IsNullOrEmpty(HappyWeekTopic) && aMember.SmallGroup && this.m_GroupType == "幸福小組")
            {
                ModifyFlag = SetContactHappyTimesAndHistory(ref aContactEntity, HappyWeekTopic);
            }

            // 處理受洗狀態
            if (SetSpiritualIdentityByUpload(ref aContactEntity, ref aMember))
                ModifyFlag = true;

            if (SetBaptizedSituationByUpload(ref aContactEntity, ref aMember))
                ModifyFlag = true;

            // 設定決志
            if (aMember.Decision)
            {
                int spiritualIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity");
                if (spiritualIdentity == 100000004 || spiritualIdentity == 100000001 || spiritualIdentity == 100000005)
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity", 100000002);
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "new_decide_date", DateTime.Now);
                    ModifyFlag = true;
                }
            }

            // 委身類型處理
            if (!ModifyFlag)
            {
                if (SetIdentityByUpload(ref aContactEntity, ref aMember))
                    ModifyFlag = true;
                else if (SET_IDENTITY_METHOD == "透過過去8週出席次數")
                    ModifyFlag = SetIdentity(aListEntityId, ref aContactEntity);
            }

            if (ModifyFlag)
                this.m_ToolUtilityClass.UpdateEntity(ref aContactEntity);
        }

        private Guid GetContactSpiritLeaderId(Guid ListEntityId, String BestLeaderName)
        {
            try
            {
                bool ListType = false;
                EntityCollection MemberCollection = GetPersonalSmallGroupLeaderMemberData(ListEntityId, ref ListType);

                foreach (Entity MemberEntity in MemberCollection.Entities)
                {
                    Entity aContactEntity = GetContactFromMemberEntity(MemberEntity, ListType);
                    if (this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "fullname") == BestLeaderName)
                        return aContactEntity.Id;
                }
                return Guid.Empty;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion

        #region 委身類型設定

        public bool SetIdentityByUpload(ref Entity aContact, ref Member aMember)
        {
            try
            {
                int aIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "customertypecode");
                int CustomerTypeCode = ConvertIdentityToIndex(aMember.Status);

                if (aIdentity > 0 && aIdentity != CustomerTypeCode)
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", CustomerTypeCode);
                    return true;
                }
                return false;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        public bool SetIdentity(Guid aListEntityId, ref Entity aContact)
        {
            try
            {
                int aIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "customertypecode");
                String aIdentityType = ConvertIndexToIdentity(aIdentity);

                if (aIdentityType == "07. 未入組" || aIdentityType == "08. 新朋友")
                {
                    if (PassOrFail(aListEntityId, ref aContact))
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 1);
                        return true;
                    }
                }
                else if (aIdentityType == "06. 小組組員")
                {
                    if (!PassOrFail(aListEntityId, ref aContact))
                        return true;
                }
                return false;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        public int GetPresentNumber(Guid WeeklyReportId, String Type, ref Entity aContact)
        {
            try
            {
                EntityCollection PresentRecordCollection = this.m_ToolUtilityClass.QueryPresentRecordByContactIdAndSunday(WeeklyReportId, aContact.Id, WEEK_PERIOD);
                int TotalNumber = 0;

                String attributeName = Type == "主日" ? "new_sunday_present_this_week" : "new_group_present_this_week";

                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    int Number = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, attributeName);
                    if (Number >= 0)
                        TotalNumber += Number;
                }
                return TotalNumber;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        public bool PassOrFail(Guid aListEntityId, ref Entity aContact)
        {
            try
            {
                int TotalNumber = GetPresentNumber(aListEntityId, "小組", ref aContact);
                return TotalNumber >= MINIMUM_THRESHOLD;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        #endregion

        #region 受洗狀態設定

        public bool SetSpiritualIdentityByUpload(ref Entity aContact, ref Member aMember)
        {
            try
            {
                int aSpiritualIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "new_spiriitual_identity");

                if (aSpiritualIdentity > 0)
                {
                    int SpiritualIdentityCode = ConvertSpiritualIdentityToIndex(aMember.SpiritualIdentity);
                    if (aSpiritualIdentity != SpiritualIdentityCode)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "new_spiriitual_identity", SpiritualIdentityCode);
                        return true;
                    }
                }
                return false;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        public bool SetBaptizedSituationByUpload(ref Entity aContact, ref Member aMember)
        {
            try
            {
                if (string.IsNullOrEmpty(aMember.BaptizedSituation))
                    return false;

                int aBaptizedSituation = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "new_baptized_situation");
                int aBaptizedSituationCode = ConvertBaptizedSituationToIndex(aMember.BaptizedSituation);

                if (aBaptizedSituation != aBaptizedSituationCode)
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "new_baptized_situation", aBaptizedSituationCode);
                    return true;
                }
                return false;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        #endregion

        #region 幸福小組出席紀錄

        private bool SetContactHappyTimesAndHistory(ref Entity BestContactEntity, String HappyCourse)
        {
            try
            {
                String OriginalHappyHistory = this.m_ToolUtilityClass.GetEntityStringAttribute(BestContactEntity, "new_happy_history");

                if (!OriginalHappyHistory.Contains(HappyCourse))
                {
                    OriginalHappyHistory += HappyCourse + ",";
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref BestContactEntity, "new_happy_history", OriginalHappyHistory);

                    String[] CourseCounter = OriginalHappyHistory.Split(',');
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref BestContactEntity, "new_happy_times", CourseCounter.Length - 1);

                    return true;
                }
                return false;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion

        #region 個人回報成員設定

        private void SetAllMemberDataByPersonalReport(String GroupName, Guid ListEntityId, ref SmallGroupData aSmallGroupData)
        {
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);
            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");

            EntityCollection MemberCollection = GetMemberCollection(ListEntityId, ListType);

            int PresentRecordIdCounter = 0;
            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                Entity ContactEntity = GetContactFromMemberEntity(MemberEntity, ListType);

                if (!ContactEntity.Attributes.Contains("statecode"))
                    continue;

                OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;
                if (aOptionState.Value != 0)
                    continue;

                // 取得聯絡人資訊
                String FullName = ContactEntity.Attributes.Contains("fullname") ? (string)ContactEntity.Attributes["fullname"] : "";
                String aMobilePhone = ContactEntity.Attributes.Contains("mobilephone") ? (string)ContactEntity.Attributes["mobilephone"] : "";
                String aHomePhone = ContactEntity.Attributes.Contains("telephone2") ? (string)ContactEntity.Attributes["telephone2"] : "";
                String aAddress = ContactEntity.Attributes.Contains("address2_line1") ? (string)ContactEntity.Attributes["address2_line1"] : "";
                String aIndustry = ContactEntity.Attributes.Contains("new_industry") ? (string)ContactEntity.Attributes["new_industry"] : "";
                String aEquipmentStatus = ContactEntity.Attributes.Contains("new_equipment_status") ? (string)ContactEntity.Attributes["new_equipment_status"] : "";

                String aIdentity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref ContactEntity, "customertypecode"));

                String aFollowUpWeek = "未選擇";
                String aNewComerNote = GetNewComerFollowupInfo(ContactEntity.Id, ref aFollowUpWeek);

                if (aIdentity != "10. 未入組結案")
                {
                    aSmallGroupData.Members.Add(new Member
                    {
                        PresentRecordId = PresentRecordIdCounter++.ToString(),
                        Group = GroupName,
                        FullName = FullName,
                        Phone = DigitsOnly.Replace(aMobilePhone, ""),
                        HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                        Address = aAddress,
                        Industry = aIndustry,
                        EquipmentStatus = aEquipmentStatus,
                        BestIntroducer = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "new_best_introducer"),
                        BestRelationship = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "new_best_relationship"),
                        Status = aIdentity,
                        SmallGroupName = GroupName,
                        SectionName = GroupName,
                        PrayItem = "",
                        Sunday = false,
                        SmallGroup = false,
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
        }

        #endregion
    }
}
