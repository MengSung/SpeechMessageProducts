using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Extensions;

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
                // 有週報 -> 從出席紀錄取得成員 (使用批次查詢優化)
                GetAllMemberDataFromPresentRecordOptimized(
                    aListSmallGroupWeeklyReport.ListEntityName, 
                    new Guid(WeeklyReportEntityId), 
                    ref aListSmallGroupWeeklyReport);
            }
            else
            {
                // 無週報 -> 從名單取得成員
                if (m_LoginType == "小組長")
                {
                    GetAllMemberDataFromListOptimized(
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

        #region 從出席紀錄取得成員 (優化版 - 批次查詢)

        /// <summary>
        /// 從出席紀錄取得所有成員資料 (批次查詢優化版)
        /// 解決 N+1 查詢問題：原本每個出席紀錄都會查詢一次 Contact
        /// 優化後：批次查詢所有 Contact，減少 98% 的 CRM 查詢次數
        /// </summary>
        private void GetAllMemberDataFromPresentRecordOptimized(
            string GroupName, 
            Guid WeeklyReportId, 
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 1. 取得所有出席紀錄
            EntityCollection PresentRecordCollection = GetPresentRecordByLoginType(GroupName, WeeklyReportId, ref aListSmallGroupWeeklyReport);

            if (PresentRecordCollection.Entities.Count == 0)
                return;

            // 2. 提取所有需要查詢的 Contact ID
            var contactIds = ExtractContactIdsFromPresentRecords(PresentRecordCollection);

            if (!contactIds.Any())
                return;

            // 3. 批次查詢所有 Contact (解決 N+1 問題)
            var contactCache = BatchRetrieveContacts(contactIds);

            // 4. 處理每筆出席紀錄 (從快取取得 Contact)
            foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
            {
                ProcessPresentRecordEntityWithCache(
                    GroupName, 
                    PresentRecordEntity, 
                    contactCache,
                    ref aListSmallGroupWeeklyReport);
            }
        }

        /// <summary>
        /// 從出席紀錄集合中提取所有 Contact ID
        /// </summary>
        private List<Guid> ExtractContactIdsFromPresentRecords(EntityCollection presentRecordCollection)
        {
            var contactIds = new List<Guid>();

            foreach (Entity entity in presentRecordCollection.Entities)
            {
                // 檢查狀態碼
                if (!entity.Attributes.Contains("statecode"))
                    continue;

                OptionSetValue stateCode = entity.Attributes["statecode"] as OptionSetValue;
                if (stateCode.Value != 0)
                    continue;

                // 檢查是否隱藏
                if (this.m_ToolUtilityClass.GetEntityBoolAttribute(entity, "new_not_display"))
                    continue;

                // 提取 Contact ID
                if (entity.Attributes.Contains("new_contact_new_present_record"))
                {
                    EntityReference contactRef = (EntityReference)entity.Attributes["new_contact_new_present_record"];
                    contactIds.Add(contactRef.Id);
                }
            }

            return contactIds.Distinct().ToList();
        }

        /// <summary>
        /// 批次查詢 Contact 實體
        /// 使用 CRM 的 IN 條件一次查詢所有 Contact，避免 N+1 問題
        /// </summary>
        private Dictionary<Guid, Entity> BatchRetrieveContacts(List<Guid> contactIds)
        {
            if (!contactIds.Any())
                return new Dictionary<Guid, Entity>();

            const int BATCH_SIZE = 50; // CRM 建議每批最多 50 筆
            var result = new Dictionary<Guid, Entity>();

            // 分批處理
            var batches = SplitIntoBatches(contactIds, BATCH_SIZE);

            foreach (var batch in batches)
            {
                var query = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("contactid", ConditionOperator.In, batch.Cast<object>().ToArray())
                        }
                    }
                };

                try
                {
                    EntityCollection contacts;
                    // ? 效能修復：CRM_TYPE 為 "DYNAMICS365-9.0"，使用 StartsWith 比對
                    // 原本 CRM_TYPE == "DYNAMICS365" 永遠為 false，導致走錯分支引發 NullReferenceException
                    if (CRM_TYPE.StartsWith("DYNAMICS365", StringComparison.OrdinalIgnoreCase))
                    {
                        // 檢查 m_OrganizationService 是否為 null
                        if (this.m_ToolUtilityClass?.m_OrganizationService == null)
                        {
                            System.Diagnostics.Debug.WriteLine("[BatchRetrieveContacts] m_OrganizationService is null, falling back to individual queries");
                            // 降級處理：逐筆查詢
                            foreach (var id in batch)
                            {
                                try
                                {
                                    var contact = this.m_ToolUtilityClass.RetrieveEntity("contact", id);
                                    if (contact != null)
                                        result[contact.Id] = contact;
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[BatchRetrieveContacts] Failed to retrieve contact {id}: {ex.Message}");
                                }
                            }
                            continue;
                        }

                        contacts = this.m_ToolUtilityClass.m_OrganizationService.RetrieveMultiple(query);
                    }
                    else
                    {
                        // 檢查 m_Crm2011OrganizationService 是否為 null
                        if (this.m_ToolUtilityClass?.m_Crm2011OrganizationService == null)
                        {
                            System.Diagnostics.Debug.WriteLine("[BatchRetrieveContacts] m_Crm2011OrganizationService is null, falling back to individual queries");
                            // 降級處理：逐筆查詢
                            foreach (var id in batch)
                            {
                                try
                                {
                                    var contact = this.m_ToolUtilityClass.RetrieveEntity("contact", id);
                                    if (contact != null)
                                        result[contact.Id] = contact;
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[BatchRetrieveContacts] Failed to retrieve contact {id}: {ex.Message}");
                                }
                            }
                            continue;
                        }

                        contacts = this.m_ToolUtilityClass.m_Crm2011OrganizationService.RetrieveMultiple(query);
                    }

                    foreach (Entity contact in contacts.Entities)
                    {
                        result[contact.Id] = contact;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BatchRetrieveContacts] 批次查詢失敗: {ex.Message}");
                    // 降級處理：逐筆查詢
                    foreach (var id in batch)
                    {
                        try
                        {
                            var contact = this.m_ToolUtilityClass.RetrieveEntity("contact", id);
                            if (contact != null)
                                result[contact.Id] = contact;
                        }
                        catch (Exception innerEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[BatchRetrieveContacts] Failed to retrieve contact {id}: {innerEx.Message}");
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 將 ID 列表分割成批次
        /// </summary>
        private IEnumerable<List<Guid>> SplitIntoBatches(List<Guid> source, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
            {
                yield return source.Skip(i).Take(batchSize).ToList();
            }
        }

        /// <summary>
        /// 處理單筆出席紀錄 (使用快取的 Contact)
        /// </summary>
        private void ProcessPresentRecordEntityWithCache(
            string GroupName, 
            Entity PresentRecordEntity, 
            Dictionary<Guid, Entity> contactCache,
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

            // 從快取取得聯絡人詳細資料 (不再個別查詢 CRM)
            if (!contactCache.TryGetValue(aFullNameEntityReference.Id, out Entity aContactEntity))
            {
                // 快取中沒有，降級處理：單筆查詢
                aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);
                if (aContactEntity == null)
                    return;
            }

            // 建立成員物件
            Member member = CreateMemberFromPresentRecord(GroupName, PresentRecordEntity, aContactEntity, FullName, ContactId);

            // 排除結案成員
            if (member.Status != "10. 未入組結案")
            {
                aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add(member);
            }
        }

        /// <summary>
        /// 從出席紀錄取得所有成員資料 (原始版本，保留相容性)
        /// </summary>
        private void GetAllMemberDataFromPresentRecord(
            string GroupName, 
            Guid WeeklyReportId, 
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 呼叫優化版本
            GetAllMemberDataFromPresentRecordOptimized(GroupName, WeeklyReportId, ref aListSmallGroupWeeklyReport);
        }

        /// <summary>
        /// 處理單筆出席紀錄 (原始版本，保留相容性)
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
                Visit = attendanceInfo.Visit,
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

        #region 從名單取得成員 (優化版 - 批次查詢)

        /// <summary>
        /// 從名單取得所有成員資料 (批次查詢優化版)
        /// </summary>
        private void GetAllMemberDataFromListOptimized(
            string GroupName, 
            Guid ListEntityId, 
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);
            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");

            EntityCollection MemberCollection = GetMemberCollection(ListEntityId, ListType);

            if (MemberCollection.Entities.Count == 0)
                return;

            // 提取所有 Contact ID
            var contactIds = ExtractContactIdsFromMembers(MemberCollection, ListType);

            if (!contactIds.Any())
                return;

            // 批次查詢所有 Contact
            var contactCache = BatchRetrieveContacts(contactIds);

            int PresentRecordIdCounter = 0;
            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                Guid contactId = GetContactIdFromMember(MemberEntity, ListType);

                if (contactCache.TryGetValue(contactId, out Entity ContactEntity))
                {
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
        }

        /// <summary>
        /// 從成員集合中提取所有 Contact ID
        /// </summary>
        private List<Guid> ExtractContactIdsFromMembers(EntityCollection memberCollection, bool listType)
        {
            var contactIds = new List<Guid>();

            foreach (Entity member in memberCollection.Entities)
            {
                Guid contactId = GetContactIdFromMember(member, listType);
                if (contactId != Guid.Empty)
                {
                    contactIds.Add(contactId);
                }
            }

            return contactIds.Distinct().ToList();
        }

        /// <summary>
        /// 從成員實體取得 Contact ID
        /// </summary>
        private Guid GetContactIdFromMember(Entity memberEntity, bool listType)
        {
            try
            {
                if (listType == false)
                {
                    // 靜態名單
                    if (memberEntity.Attributes.Contains("entityid"))
                        return ((EntityReference)memberEntity.Attributes["entityid"]).Id;
                }
                else
                {
                    // 動態名單
                    if (memberEntity.Attributes.Contains("contactid"))
                        return (Guid)memberEntity.Attributes["contactid"];
                }
            }
            catch { }

            return Guid.Empty;
        }

        /// <summary>
        /// 從名單取得所有成員資料 (原始版本，保留相容性)
        /// </summary>
        private void GetAllMemberDataFromList(
            string GroupName, 
            Guid ListEntityId, 
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 呼叫優化版本
            GetAllMemberDataFromListOptimized(GroupName, ListEntityId, ref aListSmallGroupWeeklyReport);
        }

        /// <summary>
        /// 取得成員集合
        /// </summary>
        private EntityCollection GetMemberCollection(Guid ListEntityId, bool ListType)
        {
            // ? 效能修復：CRM_TYPE 為 "DYNAMICS365-9.0"，使用 StartsWith 比對
            bool isDynamics365 = CRM_TYPE.StartsWith("DYNAMICS365", StringComparison.OrdinalIgnoreCase);

            if (ListType == false)
            {
                // 靜態名單
                return isDynamics365
                    ? this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId)
                    : this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
            }
            else
            {
                // 動態名單
                return isDynamics365
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
            string Visit,
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
                Visit: presentRecordEntity.Attributes.Contains("new_visit")
                    ? ConvertIndexToVisit(m_ToolUtilityClass.GetOptionSetAttribute(presentRecordEntity, "new_visit"))
                    : string.Empty,
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
