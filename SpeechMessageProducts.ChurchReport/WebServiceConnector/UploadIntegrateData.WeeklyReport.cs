// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class UploadIntegrateData
// 主要成員：CreateWeeklyReport、SetupWeeklyReortEntityAttributes、CreateWeeklyReportAndPresentRecord、UpdateWeeklyReport、UpdateWeeklyReportProcess、SetupWeeklyReportStatus、SetupWeeklyReportResult、SetupSundayPresentResult、GetSmallGroupMemberNumber、CreateWeeklyReportOrNot
// 引用命名空間：System、System.Collections.Generic、ChurchReport.Models、ChurchReport.Models.CrmTransmitModule、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 上傳整合資料 - 週報管理 (Partial)
    /// 包含：建立週報、更新週報、設定週報屬性
    /// </summary>
    public partial class UploadIntegrateData
    {
        #region 建立週報

        private Guid CreateWeeklyReport(ref Entity aListEntity, GroupWeeklyReportGuid aGroupWeeklyReportGuid)
        {
            try
            {
                Entity aWeeklyReportEntity = new Entity("new_group_present_weekly_report");

                m_SmallGroupPlace = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_place");
                m_SmallGroupTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_time");

                Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");
                Guid GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
                Guid RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");
                Guid ShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");

                SetupWeeklyReortEntityAttributes(
                    ref aWeeklyReportEntity,
                    FamilyLeaderId,
                    GroupLeaderId,
                    RaceLeaderId,
                    ShepherdLeaderId,
                    m_DecipleGroupListId,
                    aListEntity,
                    m_Sunday,
                    m_SmallGroupPlace,
                    m_SmallGroupTime,
                    aGroupWeeklyReportGuid);

                Guid CreatedWeeklyReportEntity = this.m_ToolUtilityClass.CreateEntity(aWeeklyReportEntity);

                this.m_ToolUtilityClass.AssignOwner(
                    "new_group_present_weekly_report",
                    this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", CreatedWeeklyReportEntity),
                    this.m_OwnerId);

                return CreatedWeeklyReportEntity;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private void SetupWeeklyReortEntityAttributes(
            ref Entity aWeeklyReportEntity,
            Guid aFamilyLeaderId,
            Guid aGroupLeaderId,
            Guid aRaceLeaderId,
            Guid aShepherdLeaderId,
            Guid aDecipleGroupList,
            Entity ListEntity,
            DateTime aSunday,
            String SmallGroupPlace,
            String SmallGroupTime,
            GroupWeeklyReportGuid aGroupWeeklyReportGuid)
        {
            try
            {
                // 設定週報名稱
                String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref ListEntity, "listname");
                String WeeklyReportName = GroupName + String.Format("-{0:00}/{1:00}/{2:00}", aSunday.Year, aSunday.Month, aSunday.Day);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_name", WeeklyReportName);

                // 關聯領袖屬性
                if (aFamilyLeaderId != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_contact_weekly_report_parents", "contact", aFamilyLeaderId);

                if (aGroupLeaderId != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_groupleader_group_present_weekly_", "contact", aGroupLeaderId);

                if (aRaceLeaderId != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_group_head_group_present_weekly_r", "contact", aRaceLeaderId);

                if (aShepherdLeaderId != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_contact_arealeader_weekly_report", "contact", aShepherdLeaderId);

                // 關聯名單
                if (ListEntity.Id != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_list_group_present_weekly_report", "list", ListEntity.Id);

                if (aDecipleGroupList != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_deciple_list_group_present_weekly", "list", aDecipleGroupList);

                // 設定日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_sunday_date", aSunday);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date",
                    aGroupWeeklyReportGuid.SmallGroupDate.Year > 1 ? aGroupWeeklyReportGuid.SmallGroupDate : DateTime.Now);

                // 設定聚會資訊
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_place", SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_time", SmallGroupTime);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_small_group_member_number", GetSmallGroupMemberNumber(ListEntity.Id));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private Entity CreateWeeklyReportAndPresentRecord(
            String GroupName,
            GroupWeeklyReportGuid aGroupWeeklyReportGuid,
            ref String WeeklyReportEntityId,
            ref Entity aListEntity,
            String UploadCategory,
            Double ValidNumber,
            ref Double aWeeklySundayRate,
            ref Double aWeeklySmallGroupRate,
            ref int aWeeklySundayNumber,
            ref int aWeeklySmallGroupNumber,
            SmallGroupData aSmallGroupData,
            String WeeklyReportData,
            String HappyWeekIndex,
            String HappyWeekTopic,
            bool PauseCheckBox)
        {
            try
            {
                Guid aCreatedWeeklyReportId = CreateWeeklyReport(ref aListEntity, aGroupWeeklyReportGuid);
                WeeklyReportEntityId = aCreatedWeeklyReportId.ToString();

                int ValidSundayMemberNumber = 0;
                int ValidSmallGroupMemberNumber = 0;
                EntityCollection aPresentRecordCollection;

                if (aSmallGroupData.LoginType == "小組長")
                {
                    aPresentRecordCollection = CreatePresentRecordList(
                        aSmallGroupData, GroupName, ref aListEntity, ref aCreatedWeeklyReportId,
                        ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate,
                        ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber,
                        ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber,
                        ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                }
                else
                {
                    SmallGroupData aSmallGroupDataFromList = new SmallGroupData { Members = new List<Member>() };
                    SetAllMemberDataByPersonalReport(GroupName, aListEntity.Id, ref aSmallGroupDataFromList);

                    aPresentRecordCollection = CreatePresentRecordListByList(
                        aSmallGroupData, aSmallGroupDataFromList, GroupName, ref aListEntity,
                        ref aCreatedWeeklyReportId, ValidNumber, ref aWeeklySundayRate,
                        ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber,
                        ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber,
                        ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                }

                return UpdateWeeklyReport(
                    aGroupWeeklyReportGuid, aPresentRecordCollection, ref aListEntity,
                    ref aCreatedWeeklyReportId, ValidNumber, ref aWeeklySundayRate,
                    ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber,
                    aSmallGroupData, WeeklyReportData, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion

        #region 更新週報

        private Entity UpdateWeeklyReport(
            GroupWeeklyReportGuid aGroupWeeklyReportGuid,
            EntityCollection PresentRecordCollection,
            ref Entity aListEntity,
            ref Guid aWeeklyReportId,
            Double ValidNumber,
            ref Double aWeeklySundayRate,
            ref Double aWeeklySmallGroupRate,
            ref int aWeeklySundayNumber,
            ref int aWeeklySmallGroupNumber,
            SmallGroupData aSmallGroupData,
            String WeeklyReportData,
            String HappyWeekIndex,
            String HappyWeekTopic,
            bool PauseCheckBox)
        {
            try
            {
                int ValidSundayMemberNumber = 0;
                int ValidSmallGroupMemberNumber = 0;
                aWeeklySundayNumber = 0;
                aWeeklySundayRate = 0.0;
                aWeeklySmallGroupNumber = 0;
                aWeeklySmallGroupRate = 0.0;

                // 計算出席統計
                foreach (Entity aMachedPresentRecordEntity in PresentRecordCollection.Entities)
                {
                    if (aMachedPresentRecordEntity != null)
                    {
                        String ClearIdentity = "";
                        bool AccumulateFlag = this.IsValidMember(aMachedPresentRecordEntity, ref ClearIdentity);

                        if (this.m_ToolUtilityClass.GetEntityIntAttribute(aMachedPresentRecordEntity, "new_sunday_present_this_week") == 1)
                        {
                            aWeeklySundayNumber += 1;
                            if (ValidNumber > 0 && AccumulateFlag)
                            {
                                ValidSundayMemberNumber += 1;
                                aWeeklySundayRate += 1 / ValidNumber;
                            }
                        }

                        if (this.m_ToolUtilityClass.GetEntityIntAttribute(aMachedPresentRecordEntity, "new_group_present_this_week") == 1)
                        {
                            aWeeklySmallGroupNumber += 1;
                            if (ValidNumber > 0 && AccumulateFlag)
                            {
                                ValidSmallGroupMemberNumber += 1;
                                aWeeklySmallGroupRate += 1 / ValidNumber;
                            }
                        }
                    }
                }

                // 更新週報實體
                Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aWeeklyReportId);

                // 設定小組聚會日期
                if (this.m_ContactEntity.Id == this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aWeeklyReportEntity, "new_groupleader_group_present_weekly_"))
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date",
                        aGroupWeeklyReportGuid.SmallGroupDate.Year > 1 ? aGroupWeeklyReportGuid.SmallGroupDate : DateTime.Now);
                }

                this.SetupWeeklyReportStatus("主日點名", ref aWeeklyReportEntity);

                // 設定出席率、人數
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_sunday_present_rate", aWeeklySundayRate);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_small_group_rate", aWeeklySmallGroupRate);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_sunday_present_number", aWeeklySundayNumber);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_small_group_number", aWeeklySmallGroupNumber);

                // 設定統計報告
                AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭",
                    $"{Environment.NewLine}實到{ValidSundayMemberNumber}人/應到{ValidNumber}人，出席率{aWeeklySundayRate:0%}");
                AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭",
                    $"{Environment.NewLine}實到{ValidSmallGroupMemberNumber}人/應到{ValidNumber}人，出席率{aWeeklySmallGroupRate:0%}");

                String SmallGroupResult = this.SetupWeeklyReportResult(ref aWeeklyReportEntity);

                // 設定日誌與幸福小組資訊
                if (WeeklyReportData != "不需更新小組日誌")
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_memo", WeeklyReportData);
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status",
                        PauseCheckBox ? 100000002 : 100000001);
                }

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_weekly_index", HappyWeekIndex);

                if (!string.IsNullOrEmpty(HappyWeekTopic))
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_topic", this.ConvertTopicToIndex(HappyWeekTopic));
                else
                    this.m_ToolUtilityClass.SetOptionSetAttributeNull(ref aWeeklyReportEntity, "new_topic");

                // 處理幸福小組
                if (this.m_GroupType == "幸福小組")
                {
                    ProcessHappyGroupMembers(ref aListEntity, aWeeklyReportEntity, HappyWeekIndex, HappyWeekTopic);
                    CalculateWeeklyReportTotalNumber(ref aWeeklyReportEntity);
                }

                // LINE 通知
                if (aSmallGroupData.LoginType == "小組長" && WeeklyReportData != "不需更新小組日誌")
                {
                    this.m_LineNotifyUtility.SendSmallGroupResultLine(
                        this.m_ContactEntity, SmallGroupResult, aGroupWeeklyReportGuid,
                        aWeeklyReportId, ref aListEntity, ref aSmallGroupData, WeeklyReportData, PauseCheckBox);
                }

                this.m_ToolUtilityClass.UpdateEntity(ref aWeeklyReportEntity);
                this.m_ToolUtilityClass.AssignOwner("new_group_present_weekly_report", aWeeklyReportEntity, this.m_OwnerId);

                // 回傳
                aGroupWeeklyReportGuid.WeeklyReportGuid = aWeeklyReportId;
                aGroupWeeklyReportGuid.SundayPresentRate = aWeeklySundayRate;
                aGroupWeeklyReportGuid.SmallGroupRate = aWeeklySmallGroupRate;

                return aWeeklyReportEntity;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private Entity UpdateWeeklyReportProcess(
            GroupWeeklyReportGuid aGroupWeeklyReportGuid,
            ref Entity aListEntity,
            ref Guid aWeeklyReportId,
            SmallGroupData aSmallGroupData,
            String WeeklyReportData,
            String HappyWeekIndex,
            String HappyWeekTopic,
            bool PauseCheckBox)
        {
            try
            {
                Double aWeeklySundayRate = 0.0;
                Double aWeeklySmallGroupRate = 0.0;
                int aWeeklySundayNumber = 0;
                int aWeeklySmallGroupNumber = 0;
                Double ValidNumber = 0.0F;

                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship(
                    "new_group_present_weekly_report", "new_group_present_weekly_reportid",
                    aWeeklyReportId.ToString(), "new_group_present_weekly_report_prese", "new_present_record");

                ValidNumber = GetValidMemberNumber(aPresentRecordCollection);

                UpdatePresentRecord(
                    this.m_GroupNamedListMemberInfomation, aPresentRecordCollection,
                    ref aListEntity, ref aWeeklyReportId, ValidNumber,
                    ref aWeeklySundayRate, ref aWeeklySmallGroupRate,
                    ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber,
                    ref aGroupWeeklyReportGuid, aSmallGroupData, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                return UpdateWeeklyReport(
                    aGroupWeeklyReportGuid, aPresentRecordCollection, ref aListEntity,
                    ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate,
                    ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber,
                    aSmallGroupData, WeeklyReportData, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion

        #region 週報輔助方法

        private void SetupWeeklyReportStatus(String UploadCategory, ref Entity aWeeklyReportEntity)
        {
            try
            {
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private String SetupWeeklyReportResult(ref Entity aWeeklyReportEntity)
        {
            try
            {
                return SetupSundayPresentResult(ref aWeeklyReportEntity);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private String SetupSundayPresentResult(ref Entity aWeeklyReportEntity)
        {
            try
            {
                string Apple = "\uD83C\uDF4F";
                string Heart = "\uD83D\uDC96";

                String SundayResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "主日出席統計表頭") +
                    Environment.NewLine + Apple + "已出席:(八週累計)" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計小組組員出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計未入組出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計新人出席字串") +
                    Environment.NewLine + Heart + "未出席:(八週累計)" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計小組組員未出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計未入組出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "主日統計新人未出席字串");

                String SmallGroupResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "小組出席統計表頭") +
                    Environment.NewLine + Apple + "已出席:(八週累計)" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "小組統計小組組員出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "小組統計未入組出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "小組統計新人出席字串") +
                    Environment.NewLine + Heart + "未出席:(八週累計)" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "小組統計小組組員未出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "小組統計未入組出未席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "", "小組統計新人未出席字串");

                String FollowUpResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "跟進統計表頭") +
                    "\t" + "A.未入組跟進統計內容" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "未入組跟進統計內容") + Environment.NewLine +
                    "\t" + "B.新朋友跟進統計內容" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "新朋友跟進統計內容") + Environment.NewLine +
                    "---------------------------------" + Environment.NewLine;

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_sunday_present_report",
                    SundayResult + SmallGroupResult + FollowUpResult);
                AddToDictionary(ref this.m_FeedBackReport, "主日統計", SundayResult);

                return SundayResult + Environment.NewLine +
                    "---------------------------------" + Environment.NewLine +
                    SmallGroupResult + Environment.NewLine +
                    "---------------------------------" + Environment.NewLine +
                    FollowUpResult;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private int GetSmallGroupMemberNumber(Guid ListEntityId)
        {
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);
            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");

            EntityCollection MemberCollection = ListType == false
                ? this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId)
                : this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);

            return MemberCollection.Entities.Count;
        }

        private bool CreateWeeklyReportOrNot(ref Entity aListEntity, DateTime aSunday)
        {
            try
            {
                EntityCollection aWeeklyReportCollection = this.m_ToolUtilityClass.QueryWeeklyReportBySunday(aSunday, aListEntity.Id);

                foreach (Entity aWeeklyReport in aWeeklyReportCollection.Entities)
                {
                    DateTime WeeklyReportSunday = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aWeeklyReport, "new_sunday_date");
                    if (aSunday.Date == WeeklyReportSunday.Date)
                        return false;
                }

                if (aListEntity != null)
                {
                    Guid aSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
                    if (this.m_ContactId == aSmallGroupLeaderId)
                        return true;
                    return m_LoginType != "小組長";
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

        private bool UpdateWeeklyReportOrNot(ref Entity aListEntity)
        {
            try
            {
                if (aListEntity != null)
                {
                    Guid aThisListCoSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_contact_list_vice_family_leader");
                    Guid aThisListSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ListEntity, "new_contact_family_leader_list");

                    if (this.m_ContactId == aThisListSmallGroupLeaderId || this.m_ContactId == aThisListCoSmallGroupLeaderId)
                        return true;
                    return m_LoginType != "小組長";
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

        private bool DeterminCalculateFlag(Guid m_ContactId, Guid aThisListFamilyHeadId, Guid aThisListSmallGroupLeaderId, Guid aThisListGraceLeaderId)
        {
            try
            {
                if (m_ContactId == aThisListSmallGroupLeaderId && m_ContactId != aThisListGraceLeaderId)
                    return true;
                if (m_ContactId == aThisListSmallGroupLeaderId && m_ContactId == aThisListGraceLeaderId)
                    return true;
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
    }
}
