// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DownloadIntegrateData
// 主要成員：GetPresentRecordByLoginType、CreatePresentRecordList、CreatePresentRecord、CreateMember、UpdateContactInfomationFromList、SetupPresentRecordEntityAttributes、SetupLeaderReferences、SetupAttendanceData、SetupFollowUpData
// 引用命名空間：System、ChurchReport.Models、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 整合資料下載服務 - 出席紀錄處理
    /// </summary>
    public partial class DownloadIntegrateData
    {
        #region 出席紀錄查詢

        /// <summary>
        /// 使用僅屬於目前操作的 CRM service，取得指定週報的出席紀錄。
        ///
        /// <para>
        /// <paramref name="organizationService"/> 由最外層 request/lease owner 借出，本方法只
        /// 在同步 method parameter 範圍使用它，絕不寫入 instance、static、AsyncLocal、cache、
        /// Factory 或 ToolUtility，也絕不 Dispose。如此即使 DownloadIntegrateData 被 legacy
        /// session 快取重用，另一位使用者、profile 或 connector generation 也無從讀取本次
        /// service 的可變連線、認證或查詢結果。
        /// </para>
        ///
        /// <para>
        /// 查詢只接受已由上層流程決定的單一週報 ID，且條件直接送往傳入 service；不會呼叫
        /// ToolUtility 的 relationship helper 或以共用 service fallback。空 ID 視為無法證明
        /// 授權範圍，必須 fail closed 而非擴大為全表掃描。
        /// </para>
        /// </summary>
        /// <param name="organizationService">呼叫端借用且仍由呼叫端負責釋放的 CRM service。</param>
        /// <param name="weeklyReportId">已授權週報的唯一識別，不能是空值。</param>
        /// <returns>只符合該週報 lookup 的出席紀錄集合。</returns>
        /// <exception cref="ArgumentNullException">當未提供 operation-local service 時擲回。</exception>
        /// <exception cref="ArgumentOutOfRangeException">當週報 ID 為空且無法建立最小查詢範圍時擲回。</exception>
        private EntityCollection GetPresentRecordByLoginType(
            IOrganizationService organizationService,
            Guid weeklyReportId)
        {
            ArgumentNullException.ThrowIfNull(organizationService);

            if (weeklyReportId == Guid.Empty)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(weeklyReportId),
                    "出席紀錄查詢必須綁定已授權的週報識別，不能以空識別掃描 CRM。");
            }

            // 此 QueryExpression 沒有 fetch XML、沒有 caller 控制的 entity/欄位名稱，並且只保留
            // legacy flow 顯示與後續轉換所需的欄位。服務借用生命週期不跨出這次呼叫。
            var query = new QueryExpression("new_present_record")
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression(LogicalOperator.And)
            };
            query.Criteria.AddCondition(
                "new_group_present_weekly_report_prese",
                ConditionOperator.Equal,
                weeklyReportId);

            return organizationService.RetrieveMultiple(query);
        }



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
            Entity ListEntity = RetrieveListTypeEntity(ListEntityId);
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
