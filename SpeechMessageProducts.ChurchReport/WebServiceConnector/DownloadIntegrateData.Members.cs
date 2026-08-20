// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DownloadIntegrateData.Members.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DownloadIntegrateData、record ContactInfo、record AttendanceInfo、record FollowUpInfoRecord
// 主要成員：GetAllMemeberDataList、GetAllMemberDataFromPresentRecordOptimized、ExtractContactIdsFromPresentRecords、SplitIntoBatches、ToObjectArray、ProcessPresentRecordEntityWithCache、GetAllMemberDataFromPresentRecord、ProcessPresentRecordEntity、CreateMemberFromPresentRecord、GetAllMemberDataFromListOptimized
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks、ChurchReport.Models、ChurchReport.Models.CrmTransmitModule、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
        /// <summary>
        /// 成員頁面實際會用到的 Contact 欄位清單。
        /// 第二波優化將批次查詢從全欄位縮到必要欄位，
        /// 直接減少 CRM 傳輸量與序列化成本。
        /// </summary>
        private static readonly string[] MemberContactColumns =
        {
            "statecode",
            "fullname",
            "mobilephone",
            "telephone2",
            "address2_line1",
            "birthdate",
            "new_industry",
            "new_equipment_status",
            "new_spiriitual_identity",
            "new_baptized_situation",
            "description",
            "new_contact_contact_spiritleader",
            "new_best_introducer",
            "new_best_relationship",
            "customertypecode",
            "new_start_tracking_date",
            "gendercode",
            "new_enter_church_date"
        };

        #region 成員資料取得

        /// <summary>
        /// 取得所有成員資料清單
        /// </summary>
        public void GetAllMemeberDataList(
            string ListEntityId,
            string WeeklyReportEntityId,
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 初始化成員資料 — ? 極速：預配 64 筆容量，避免 List 多次擴容
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData = new SmallGroupData
            {
                Members = new List<Member>(64),
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

            if (contactIds.Count == 0)
                return;

            // 3. 批次查詢所有 Contact (解決 N+1 問題) — 預先配置容量
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
        /// ? 效能優化：使用 HashSet 即時去重，避免 .Distinct().ToList() 的額外迭代與記憶體配置
        /// </summary>
        private List<Guid> ExtractContactIdsFromPresentRecords(EntityCollection presentRecordCollection)
        {
            var contactIdSet = new HashSet<Guid>();

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
                    contactIdSet.Add(contactRef.Id);
                }
            }

            return new List<Guid>(contactIdSet);
        }

        /// <summary>
        /// 批次查詢 Contact 實體
        /// 使用 CRM 的 IN 條件一次查詢所有 Contact，避免 N+1 問題
        /// </summary>
        private Dictionary<Guid, Entity> BatchRetrieveContacts(List<Guid> contactIds)
        {
            // ? 效能優化：使用 Count 取代 Any()，避免 LINQ 列舉器配置
            if (contactIds.Count == 0)
                return new Dictionary<Guid, Entity>();

            const int BATCH_SIZE = 50; // CRM 建議每批最多 50 筆
            // ? 極速：預先配置 Dictionary 容量，避免多次 rehash
            var result = new Dictionary<Guid, Entity>(contactIds.Count);

            // 分批處理
            var batches = SplitIntoBatches(contactIds, BATCH_SIZE);

            foreach (var batch in batches)
            {
                var query = new QueryExpression("contact")
                {
                    // 只抓成員報表會使用到的欄位，避免每批都把整個 Contact 實體搬回來。
                    ColumnSet = CreateMemberContactColumnSet(),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            // ? 極速：直接建立 object[]，避免 Cast<object>() LINQ 中間集合
                            new ConditionExpression("contactid", ConditionOperator.In, ToObjectArray(batch))
                        }
                    }
                };

                try
                {
                    var organizationService = GetCurrentOrganizationService();
                    if (organizationService == null)
                    {
                        System.Diagnostics.Debug.WriteLine("[BatchRetrieveContacts] CRM service is null, falling back to individual queries");
                        RetrieveContactsIndividually(batch, result);
                        continue;
                    }

                    EntityCollection contacts = organizationService.RetrieveMultiple(query);

                    foreach (Entity contact in contacts.Entities)
                    {
                        result[contact.Id] = contact;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BatchRetrieveContacts] 批次查詢失敗: {ex.Message}");
                    // 降級處理：逐筆查詢
                    RetrieveContactsIndividually(batch, result);
                }
            }

            return result;
        }

        /// <summary>
        /// 將 ID 列表分割成批次
        /// ? 效能優化：使用 GetRange 取代 Skip().Take()，從 O(n?) 降到 O(n)
        /// </summary>
        private IEnumerable<List<Guid>> SplitIntoBatches(List<Guid> source, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
            {
                int count = Math.Min(batchSize, source.Count - i);
                yield return source.GetRange(i, count);
            }
        }

        /// <summary>
        /// ? 極速：零 LINQ 的 Guid → object[] 轉換，避免 Cast&lt;object&gt;().ToArray() 的中間集合配置
        /// </summary>
        private static object[] ToObjectArray(List<Guid> guids)
        {
            var arr = new object[guids.Count];
            for (int i = 0; i < guids.Count; i++)
                arr[i] = guids[i];
            return arr;
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

            // ? 極速：傳入已快取的 Contact Entity，省去 GetNewComerFollowupInfo 內的重複 CRM 查詢
            var followUpInfo = ExtractFollowUpInfo(PresentRecordEntity, ((EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"]).Id, aContactEntity);

            return new Member
            {
                PresentRecordId = PresentRecordEntity.Id.ToString(),
                ContactId = ContactId,
                Group = GroupName,
                FullName = FullName,

                // 個人基本資料
                Phone = KeepDigitsOnly(contactInfo.MobilePhone),
                HomePhone = KeepDigitsOnly(contactInfo.HomePhone),
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
            Entity ListEntity = RetrieveListTypeEntity(ListEntityId);
            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");

            EntityCollection MemberCollection = GetMemberCollection(ListEntityId, ListType);

            if (MemberCollection.Entities.Count == 0)
                return;

            // 提取所有 Contact ID
            var contactIds = ExtractContactIdsFromMembers(MemberCollection, ListType);

            if (contactIds.Count == 0)
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
        /// ? 效能優化：使用 HashSet 即時去重，避免 .Distinct().ToList() 的額外迭代與記憶體配置
        /// </summary>
        private List<Guid> ExtractContactIdsFromMembers(EntityCollection memberCollection, bool listType)
        {
            var contactIdSet = new HashSet<Guid>();

            foreach (Entity member in memberCollection.Entities)
            {
                Guid contactId = GetContactIdFromMember(member, listType);
                if (contactId != Guid.Empty)
                {
                    contactIdSet.Add(contactId);
                }
            }

            return new List<Guid>(contactIdSet);
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
            if (ListType == false)
            {
                // 靜態名單
                return this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
            }
            else
            {
                // 動態名單
                return this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
            }
        }

        /// <summary>
        /// 從成員實體取得聯絡人
        /// </summary>
        private Entity GetContactFromMember(Entity MemberEntity, bool ListType)
        {
            if (ListType == false)
            {
                return RetrieveMemberContact(((EntityReference)MemberEntity.Attributes["entityid"]).Id);
            }
            else
            {
                return RetrieveMemberContact((Guid)MemberEntity.Attributes["contactid"]);
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
            // ? 極速：傳入已有的 ContactEntity，省去重複 CRM 查詢
            string aNewComerNote = GetNewComerFollowupInfoWithEntity(ContactEntity, ref aFollowUpWeek);

            return new Member
            {
                PresentRecordId = counter.ToString(),
                ContactId = ContactEntity.Id.ToString(),
                Group = GroupName,
                FullName = contactInfo.FullName,

                // 個人基本資料
                Phone = KeepDigitsOnly(contactInfo.MobilePhone),
                HomePhone = KeepDigitsOnly(contactInfo.HomePhone),
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
            // ? 極速：已有 m_ContactEntity，直接傳入
            string aNewComerNote = GetNewComerFollowupInfoWithEntity(m_ContactEntity, ref aFollowUpWeek);

            string aIdentity = ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref m_ContactEntity, "customertypecode"));

            if (aIdentity != "10. 未入組結案")
            {
                aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add(new Member
                {
                    PresentRecordId = DateTime.Now.ToLongTimeString(),
                    ContactId = m_ContactEntity.Id.ToString(),
                    Group = GroupName,
                    FullName = contactInfo.FullName,

                    Phone = KeepDigitsOnly(contactInfo.MobilePhone),
                    HomePhone = KeepDigitsOnly(contactInfo.HomePhone),
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

        #region CRM 查詢輔助

        /// <summary>
        /// 每個 Contact 欄位在目前組織是否真實存在的快取。Key 為 logical name，值為存在與否。
        /// 只在程序內存活，且 metadata 查詢每個 entity 只做一次，不會形成額外的 CRM 往返成本。
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Generic.HashSet<string>>
            s_existingAttributeCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 建立成員 Contact 查詢欄位集合，並先以組織 metadata 濾掉本組織不存在的欄位。
        /// 每次回傳新實例，避免可變的 ColumnSet 在不同查詢間被共享。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 為什麼需要過濾：ColumnSet 只要含有一個不存在的欄位，Dataverse 就會讓<b>整個查詢</b>失敗
        /// （<c>'Contact' entity doesn't contain attribute with Name = '...'</c>）。此路徑原本使用全欄位
        /// ColumnSet，不存在的欄位只會被忽略；改為「只撈必要欄位」的效能最佳化之後，同一個欄位就從
        /// 無害變成硬錯誤，並且逐筆降級查詢用的是同一組欄位，因此批次失敗後 50 筆會全部再失敗一次，
        /// 最終成員資料整批取不到。
        /// </para>
        /// <para>
        /// 為什麼不直接把欄位從清單移除：各組織的自訂欄位不一致，某個欄位在開發組織不存在不代表正式
        /// 組織也沒有。以 metadata 為準可讓同一份程式碼在所有組織都取到該組織實際擁有的欄位。
        /// </para>
        /// <para>
        /// 失敗時採 fail-open：metadata 查不到就原樣使用既有欄位清單，維持修改前的行為，確保這段防護
        /// 不會自己變成新的故障點。
        /// </para>
        /// </remarks>
        private ColumnSet CreateMemberContactColumnSet()
        {
            var existing = GetExistingAttributeNames("contact");
            if (existing == null || existing.Count == 0)
                return new ColumnSet(MemberContactColumns);

            var usable = MemberContactColumns.Where(existing.Contains).ToArray();

            // 全部都被濾掉代表 metadata 結果不可信，寧可維持原行為也不要送出空欄位查詢。
            if (usable.Length == 0)
                return new ColumnSet(MemberContactColumns);

            if (usable.Length != MemberContactColumns.Length)
            {
                var dropped = string.Join(", ", MemberContactColumns.Where(name => !existing.Contains(name)));
                System.Diagnostics.Debug.WriteLine(
                    $"[MemberContactColumns] 本組織不存在下列 Contact 欄位，已自查詢移除：{dropped}");
            }

            return new ColumnSet(usable);
        }

        /// <summary>
        /// 取得指定 entity 在目前組織實際存在的欄位 logical name 集合，結果以程序級快取保存。
        /// </summary>
        /// <param name="entityLogicalName">要查詢 metadata 的 entity logical name。</param>
        /// <returns>存在的欄位名稱集合；無法取得 metadata 時回傳 <see langword="null"/> 讓呼叫端 fail-open。</returns>
        private System.Collections.Generic.HashSet<string> GetExistingAttributeNames(string entityLogicalName)
        {
            if (s_existingAttributeCache.TryGetValue(entityLogicalName, out var cached))
                return cached;

            try
            {
                var organizationService = GetCurrentOrganizationService();
                if (organizationService == null)
                    return null;

                var response = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)organizationService.Execute(
                    new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
                    {
                        LogicalName = entityLogicalName,
                        EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes,
                        RetrieveAsIfPublished = false
                    });

                var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var attribute in response.EntityMetadata?.Attributes ?? System.Array.Empty<Microsoft.Xrm.Sdk.Metadata.AttributeMetadata>())
                {
                    if (!string.IsNullOrEmpty(attribute.LogicalName))
                        names.Add(attribute.LogicalName);
                }

                if (names.Count == 0)
                    return null;

                s_existingAttributeCache[entityLogicalName] = names;
                return names;
            }
            catch (Exception ex)
            {
                // metadata 不可得時不阻斷查詢，維持既有欄位清單即可；只記錄一次供診斷。
                System.Diagnostics.Debug.WriteLine(
                    $"[MemberContactColumns] 無法取得 {entityLogicalName} metadata，沿用原欄位清單：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 判斷目前是否走 Dynamics 365 連線。
        /// CRM_TYPE 在此專案可能附帶版本號，因此改用 StartsWith。
        /// </summary>
        private bool IsDynamics365Crm()
        {
            return (CRM_TYPE ?? string.Empty).StartsWith("DYNAMICS365", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 取得目前應使用的 CRM service。
        /// </summary>
        private IOrganizationService GetCurrentOrganizationService()
        {
            return this.m_ToolUtilityClass?.m_Crm2011OrganizationService;
        }

        /// <summary>
        /// 逐筆補抓 Contact。
        /// 批次查詢失敗時仍保留原本降級行為，但單筆查詢也盡量只抓必要欄位。
        /// </summary>
        private void RetrieveContactsIndividually(IEnumerable<Guid> contactIds, Dictionary<Guid, Entity> result)
        {
            foreach (var id in contactIds)
            {
                try
                {
                    var contact = RetrieveMemberContact(id);
                    if (contact != null)
                    {
                        result[contact.Id] = contact;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BatchRetrieveContacts] Failed to retrieve contact {id}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 依成員報表需求抓取單筆 Contact。
        /// </summary>
        private Entity RetrieveMemberContact(Guid contactId)
        {
            var organizationService = GetCurrentOrganizationService();
            if (organizationService == null)
            {
                return this.m_ToolUtilityClass.RetrieveEntity("contact", contactId);
            }

            return organizationService.Retrieve("contact", contactId, CreateMemberContactColumnSet());
        }

        /// <summary>
        /// 只抓 list.type，避免為了判斷靜態/動態名單而載入整個 List 實體。
        /// </summary>
        private Entity RetrieveListTypeEntity(Guid listEntityId)
        {
            var organizationService = GetCurrentOrganizationService();
            if (organizationService == null)
            {
                return this.m_ToolUtilityClass.RetrieveEntity("list", listEntityId);
            }

            return organizationService.Retrieve("list", listEntityId, new ColumnSet("type"));
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
        /// ? 極速版：接收已快取的 Contact Entity，省去 GetNewComerFollowupInfo 內的重複 CRM 查詢
        /// </summary>
        private FollowUpInfoRecord ExtractFollowUpInfo(Entity presentRecordEntity, Guid contactId, Entity cachedContactEntity = null)
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

            // ? 極速：若有已快取的 Contact，直接傳入，省去 CRM 網路往返
            string newComerNote = cachedContactEntity != null
                ? GetNewComerFollowupInfoWithEntity(cachedContactEntity, ref followUpWeek)
                : GetNewComerFollowupInfo(contactId, ref followUpWeek);

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
