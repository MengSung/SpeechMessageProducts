// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Tools/WeeklyReportProcessor.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class WeeklyReportProcessor
// 主要成員：CreateWeeklyReportAndPresentRecord、ProcessListEntity、SetupWeeklyReportRecord、GetSmallGroupMemberNumber、FindListCollection、MergeCollectionSmallGroupAhead、CreateWeeklyReport、SetupWeeklyReortEntityAttributes、ResetWeeklyReortNumber、CreatePresentRecordList
// 引用命名空間：System、System.Collections.Generic、Microsoft.Xrm.Sdk、ToolUtilityNameSpace、System.Text.RegularExpressions
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;

#region CRM 2011 reference
using Microsoft.Xrm.Sdk;
#endregion

// 編譯後的執行指令
//"D:\音訊科技專案\CRM2011\PunchClockPlugIn\bin\Debug\IIS_Async_Reset and Copy - 直接.bat"
// $(SolutionDir)packages\ilmerge.2.14.1208\tools\ILMerge.exe  /keyfile:"D:\音訊科技專案\音訊科技金鑰\SpeechMessageCrmKey.snk"  /target:"library" /copyattrs /out:$(TargetDir)$(TargetName)$(TargetExt)  $(ProjectDir)$(IntermediateOutputPath)$(TargetName)$(TargetExt) $(ProjectDir)$(OutputPath)LineUtility.dll $(ProjectDir)$(OutputPath)ToolUtilityDynamics365.dll $(ProjectDir)$(OutputPath)YangMeillcStorLessonsPlugIn.dll $(SolutionDir)packages\RestSharp.105.2.3\lib\net45\RestSharp.dll
//測試

using ToolUtilityNameSpace;
using System.Text.RegularExpressions;

namespace ChurchReport.Tools
{
    public class WeeklyReportProcessor
    {
        #region 資料區
        #region 參數資料
        private ToolUtilityClass m_ToolUtilityClass;

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        #endregion
        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365-9.0";

        #region 除錯用參數
        private const int TOTAL_LEVEL = 1;//改變這個值，就會改追蹤的階層，值越小越不會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        //private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，值越大越會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
        // 如果 TRACE_LEVEL >= TRACE_LEVEL_GROUND 就會進行追蹤
        // 如果 TRACE_LEVEL < TRACE_LEVEL_GROUND 就不會進行追蹤
        //int TRACE_LEVEL = 5;
        //int TRACE_LEVEL_GROUND = 3;
        #endregion

        #endregion
        #endregion
        #region 上傳資料時所需要的參數

        DateTime m_Sunday;
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        //Entity m_ListEntity; // 小組名單實體紀錄
        //Entity m_WeeklyReportEntity; // 週報實體紀錄
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        //EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給區長/小家長的名單

        Guid m_DecipleGroupListId;
        //Guid m_GroupLeaderId; // 小組長
        //Guid m_RaceLeaderId; // 區長/小家長
        //Guid m_ShepherdLeaderId; // 區牧
        String m_SmallGroupPlace;
        String m_SmallGroupTime;

        Guid m_OwnerId; // 小組長的負責人 Id

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
                                                                       //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        //private const String SET_IDENTITY_METHOD = "透過過去8週出席次數"; // 設定委身類型的方式
        private const String SET_IDENTITY_METHOD = "透過回報網頁手動設定"; // 設定委身類型的方式

        #endregion
        #region 初始值設定
        public WeeklyReportProcessor( ToolUtilityClass aToolUtilityClass)
        {
            m_ToolUtilityClass = aToolUtilityClass;
        }
        #endregion
        #region 下載資料區
        #region 主程式區
        public void CreateWeeklyReportAndPresentRecord( Entity aLoginContact, DateTime aDownloadDate,  ref Dictionary<String, String> WeeklyReportDictionary)
        {
            try
            {
                #region 先根據日期尋找當週主日日期
				// 依設定檔的每週第一日規則，取得下載日期所屬週次的主日。
				DateTime aSunday = ChurchReport.Services.SundayCalculator.CalculateSunday(
					aDownloadDate,
					ChurchReport.Services.WeeklyScheduleProvider.FirstDayOfWeek);
				#endregion

				#region 找登入使用者及其ID
				m_ContactId = aLoginContact.Id;
                m_ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", m_ContactId);
                #endregion

                #region 先尋找帶領族系名單，若找到表示就是區長，若沒有則在繼續尋找帶領小組名單
                FindListCollection();
                #endregion

                #region 處理小組名單
                if (m_Lists.Entities.Count != 0)
                {
                    #region// 有找到要點名的名單，所以是小組長以上回報
                    #region 處理每個要點名的名單
                    ProcessListEntity(ref WeeklyReportDictionary);
                    #endregion

                    return;
                    #endregion
                }
                else
                {
                    #region// 沒找到任何要點名的名單，所以是個人回報，
                    return;
                    #endregion
                }
                #endregion
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }


        #endregion
        #region 處理小組名單
        private void ProcessListEntity(ref Dictionary<String, String> WeeklyReportDictionary)
        {
            try
            {
                // 處理每個點名名單
                foreach (Entity ListEntity in this.m_Lists.Entities)
                {
                    // 取得每個需要點名的名單裡的每個週報
                    //EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");

                    //EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.QueryWeeklyReportBySunday( this.m_Sunday, "list", "listid", ListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");

                    // 尋找此小組的某一個主日的週報集合
                    EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.QueryWeeklyReportBySunday(this.m_Sunday, ListEntity.Id);

                    // 此小組的某一個主日的週報集合，應該僅有一個，也就是第0個的週報
                    Entity GroupWeeklyReportEntity = GroupWeeklyReportEntityCollection.Entities.Count == 1 ? GroupWeeklyReportEntityCollection.Entities[0] : null;

                    //依據找到的週報有還是沒有來決定下一步:
                    //      有: 建立GroupName及WeeklyReportId
                    //    沒有: 建立GroupName及WeeklyReportId = Guid.Empty();
                    //String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname");

                    // 取得週報資料
                    // 小組長的負責人 Id
                    m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);

                    SetupWeeklyReportRecord(ref WeeklyReportDictionary, ListEntity, GroupWeeklyReportEntity);
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        public void SetupWeeklyReportRecord(ref Dictionary<String, String> WeeklyReportDictionary, Entity ListEntity, Entity GroupWeeklyReportEntity)
        {
            try
            {
                if (GroupWeeklyReportEntity != null)
                {
                    // 已經有週報
                    Entity RetrievedWeeklyReport = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", GroupWeeklyReportEntity.Id);
                    String GroupName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref RetrievedWeeklyReport, "new_list_group_present_weekly_report");
                    AddToDictionary(ref WeeklyReportDictionary, GroupName, GroupWeeklyReportEntity.Id.ToString());
                    //WeeklyReportList.Add(GroupWeeklyReportEntity.Id.ToString());
                }
                else
                {
                    // 還沒有週報，要建立週報
                    CreateWeeklyReport(ref ListEntity, ref WeeklyReportDictionary);
                }

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private int GetSmallGroupMemberNumber(Guid ListEntityId)
        {
            #region // 初始化每個小組名單，建立原始的 Member Data

            #region 取得小組名單，一個一個的連絡人實體
            //搜尋名單的組員
            //EntityCollection Contacts = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntityId.ToString(), "new_cell_list_contact", "contact");

            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);

            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");
            EntityCollection MemberCollection;
            if (ListType == false)
            {
                // 靜態名單
                if (CRM_TYPE == "DYNAMICS365" || CRM_TYPE == "DYNAMICS365-9.0")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ListEntityId);
                }
                else
                {
                    MemberCollection = new EntityCollection();
                    //MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365" || CRM_TYPE == "DYNAMICS365-9.0")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListDynamics365(ListEntityId);
                }
                else
                {
                    MemberCollection = new EntityCollection();
                    //MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            #endregion

            return MemberCollection.Entities.Count;

            #endregion
        }
        #endregion
        #region 處理個人回報
        #endregion
        #region 副程式呼叫
        private void FindListCollection()
        {
            try
            {
                // 初始化 m_Lists
                // 門徒 new_contact_list_vice_family_leader
                //this.m_Lists = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_vice_family_leader", "list");  // 門徒
                //this.m_Lists = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 門徒
                //MergeCollectionSmallGroupAhead(ref this.m_Lists);
                EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 門徒
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 小組長/門徒 new_contact_family_leader_list
                //EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");  // 小組長/門徒
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/門徒
                //aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/門徒
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 共同小家長 new_contact_co_race_leager_list
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_co_race_leager_list", "list");  // 共同小家長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_co_race_leager_list");  // 共同小家長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 上代組長 new_contact_race_leager_list
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_race_leager_list", "list");  // 上代組長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_race_leager_list");  // 上代組長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 區長 new_contact_list_arealeader
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_arealeader", "list");  // 區長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_arealeader");  // 區長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 共同區牧 new_contact_list_co_arealeader
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_co_arealeader");  // 共同區牧
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void MergeCollectionSmallGroupAhead(ref EntityCollection aListEntityCollection)
        {
            try
            {
                // 區長或是小家長的名單若是與小組長名單重疊，則要過濾出僅有族長/小家長的名單
                // 合併小組名單至族系名單，單扣除掉重複的
                // 然後放在小組名單裡面
                // 一個一個處理族系名單
                foreach (Entity aListEntity in aListEntityCollection.Entities)
                {
                    // 處理每一個要被確認是否已在m_Lists之中的名單
                    bool SearchedFlag = false;
                    foreach (Entity m_ListEntity in this.m_Lists.Entities)
                    {
                        // 比對每一個小組名單
                        if (aListEntity.Id == m_ListEntity.Id)
                        {
                            // 區長的名單與小組長的名單有相同的了
                            SearchedFlag = true;
                            break;
                        }
                    }

                    if (SearchedFlag == false)
                    {
                        // 區長的名單沒有與小組長名單相同的
                        if (this.m_ToolUtilityClass.GetEntityBoolAttribute(aListEntity, "new_app_named") == true)
                        {
                            // 點名有打勾
                            DateTime aHappyStartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aListEntity, "new_happy_start_date").ToLocalTime();
                            DateTime aHappyEndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aListEntity, "new_happy_end_date").ToLocalTime();

                            if ( aHappyStartDate.Year != 1 )
                            {
                                // 幸福小組開始日期有填
                                if (aHappyEndDate.Year != 1)
                                {
                                    // 幸福小組開始日期有填，小組結束日期有填
                                    if (DateTime.Now >= aHappyStartDate && DateTime.Now <= aHappyEndDate)
                                    {
                                        // 現在比幸福小組開始日期還晚 ，比幸福小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                                else
                                {
                                    // 幸福小組開始日期有填，幸福小組結束日期沒填
                                    if (DateTime.Now >= aHappyStartDate)
                                    {
                                        // 現在比幸福小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                            }
                            else
                            {
                                // 幸福小組開始日期沒填
                                if ( aHappyEndDate.Year != 1 )
                                {
                                    // 幸福小組開始日期沒填，幸福小組結束日期有填
                                    if (DateTime.Now <= aHappyEndDate)
                                    {
                                        // 現在比幸福小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                                else
                                {
                                    // 幸福小組開始日期沒填，幸福小組結束日期沒填
                                    m_Lists.Entities.Add(aListEntity);
                                }
                            }
                        }
                    }
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion
        #endregion
        #region 上傳資料區
        #region 建立的週報
        private Guid CreateWeeklyReport(ref Entity aListEntity, ref Dictionary<String, String> WeeklyReportDictionary)
        {
            try
            {
                // 這是新建立的週報
                Entity aWeeklyReportEntity = new Entity("new_group_present_weekly_report");

                #region 指派週報的負責人
                //Guid ListOwnerId = aListEntity.GetAttributeValue<EntityReference>("ownerid").Id;
                //this.m_ToolUtilityClass.AssignOwner("new_group_present_weekly_report", aWeeklyReportEntity, ListOwnerId);
                #endregion

                // 小組聚會地點和時間
                m_SmallGroupPlace = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_place");
                m_SmallGroupTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_time");

                // 小家長 ID
                Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 小家長 ID
                Guid RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                // 區牧長 ID/(原來是:區長) ID
                Guid ShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");

                // 區名
                //String AreaName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_area_name");

                // 設定週報相關屬性
                this.SetupWeeklyReortEntityAttributes(ref aWeeklyReportEntity, FamilyLeaderId, GroupLeaderId, RaceLeaderId, ShepherdLeaderId, m_DecipleGroupListId, aListEntity, m_Sunday, m_SmallGroupPlace, m_SmallGroupTime);

                // 重設主日、小組:出席率、人數 歸零
                ResetWeeklyReortNumber(ref aWeeklyReportEntity);

                // 新增週報
                Guid CreatedWeeklyReportEntityId = this.m_ToolUtilityClass.CreateEntity(aWeeklyReportEntity);

                // 指派週報的負責人
                try
                {
                    this.m_ToolUtilityClass.AssignOwner("new_group_present_weekly_report", this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", CreatedWeeklyReportEntityId), this.m_OwnerId);
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                }
                // 建立此小組的出席紀錄單
                String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname");
                EntityCollection aPresentRecordCollection = CreatePresentRecordList(GroupName, ref aListEntity, ref CreatedWeeklyReportEntityId, 0, 0.0, 0.0, 0, 0, "", "", false);

                // 回傳要建立 QR CODE 所需要的週報 ID 字串
                AddToDictionary(ref WeeklyReportDictionary, this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname"), CreatedWeeklyReportEntityId.ToString());

                return CreatedWeeklyReportEntityId;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupWeeklyReortEntityAttributes(ref Entity aWeeklyReportEntity, Guid aFamilyLeaderId, Guid aGroupLeaderId, Guid aRaceLeaderId, Guid aShepherdLeaderId, Guid aDecipleGroupList, Entity ListEntity, DateTime aSunday, String SmallGroupPlace, String SmallGroupTime)
        {
            try
            {
                #region 設定週報名稱
                // 取得小組名單的名稱
                String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref ListEntity, "listname");
                String WeeklyReportName = GroupName + String.Format("-{0:00}/{1:00}/{2:00}", aSunday.Year, aSunday.Month, aSunday.Day);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_name", WeeklyReportName);
                #endregion
                #region 設定區名
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_area_name", AreaName);
                #endregion
                #region 關聯小家長屬性
                if (aFamilyLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_contact_weekly_report_parents", "contact", aFamilyLeaderId); }
                #endregion
                #region 關聯小組長屬性
                if (aGroupLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_groupleader_group_present_weekly_", "contact", aGroupLeaderId); }
                #endregion
                #region 關聯區長/小家長屬性
                if (aRaceLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_group_head_group_present_weekly_r", "contact", aRaceLeaderId); }
                #endregion
                #region 關聯上代組長屬性
                if (aShepherdLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_contact_arealeader_weekly_report", "contact", aShepherdLeaderId); }
                #endregion
                #region 關聯小組名單 Lookup
                if (ListEntity.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_list_group_present_weekly_report", "list", ListEntity.Id); }
                #endregion
                #region 關聯門徒小組名單 Lookup
                if (aDecipleGroupList != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_deciple_list_group_present_weekly", "list", aDecipleGroupList); }
                #endregion
                #region 設定主日及小組聚會日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_sunday_date", aSunday);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", aSunday);

                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", DateTime.Now);

                #endregion
                #region 設定小組聚會地點和時間
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_place", SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_time", SmallGroupTime);
                #endregion
                #region 設定小組人數

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_small_group_member_number", GetSmallGroupMemberNumber(ListEntity.Id));

                #endregion
                #region 設定週報狀態，設定為均未點名，因為後面程式還會再設定一次
                // 均未點名 = 100000000
                // 均已點名 = 100000001
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void ResetWeeklyReortNumber(ref Entity aWeeklyReportEntity)
        {
            try
            {
                #region 設定出席率、人數
                //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_sunday_present_rate", 0.0);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_small_group_rate", 0.0);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_sunday_present_number", 0);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_small_group_number", 0);
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        #endregion
        #region 建立的個人聚會與靈修記錄
        private EntityCollection CreatePresentRecordList(String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, Double aWeeklySundayRate, Double aWeeklySmallGroupRate, int aWeeklySundayNumber, int aWeeklySmallGroupNumber, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            EntityCollection MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(aListEntity.Id);

            EntityCollection PresentRecordEntityCollection = new EntityCollection();
            foreach (Entity aMemberInfomation in MemberCollection.Entities)
            {
                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 新增個人聚會與靈修記錄
                Entity aPresentRecord = CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                //指派負責人
                try
                {
                    this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                }
                if (aPresentRecord != null)
                {
                    PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                }
            }

            return PresentRecordEntityCollection;
        }
        private EntityCollection CreatePresentRecordListByList(String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            EntityCollection MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(aListEntity.Id);

            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            foreach (Entity aMemberInfomation in MemberCollection.Entities)
            {
                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 新增個人聚會與靈修記錄
                Entity aPresentRecord = CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                //指派負責人
                try
                {
                    this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                }

                if (aPresentRecord != null)
                {
                    PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                }
            }

            return PresentRecordEntityCollection;
        }
        private Entity CreatePresentRecord(Entity MemberEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            //Entity aContactEntity = UpdateContactInfomationFromList(aMemberInfomation.FullName, aListEntity.Id);

            Entity ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
            //Entity aToUpdateContactEntity_001 = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)(aMemberInfomation.Attributes["entityid"])    .Id);

            //Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aSearchedContactEntity.Id);

            if (ContactEntity != null)
            {
                // 這是新建立的個人聚會與靈修記錄
                Entity aPresentRecord = new Entity("new_present_record");

                // 設定個人聚會與靈修記錄相關屬性
                this.SetupPresentRecordEntityAttributes(aPresentRecord, MemberEntity, ref ContactEntity, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                // 新增個人聚會與靈修記錄
                Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);
                Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);

                //指派負責人
                //this.m_ToolUtilityClass.AssignOwner("new_present_record", aRetrievedPresentRecord, this.m_ToolUtilityClass.GetOwnerId( ContactEntity ));

                //取得並回傳新建的聚會與靈修記錄
                return aRetrievedPresentRecord;
            }
            else
            {
                return null;
            }
        }
        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, Entity aMemberInfomation, ref Entity aContactEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            try
            {
                #region 設定名稱
                String PresentRecordName = m_ToolUtilityClass.GetEntityStringAttribute(ref aMemberInfomation, "fullname") + String.Format("-{0:00}/{1:00}/{2:00} 出席紀錄", this.m_Sunday.Year, this.m_Sunday.Month, this.m_Sunday.Day);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", PresentRecordName);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", PresentRecordName);
                #endregion
                #region 指派主日小組靈修出席單的負責人
                //Guid ListOwnerId = aListEntity.GetAttributeValue<EntityReference>("ownerid").Id;
                //this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, ListOwnerId);
                #endregion
                #region 設定姓名
                // 找到組員ID
                Guid aContactEntityId = aContactEntity.Id;
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntityId);
                #endregion
                #region 關聯週報 Lookup
                if (aWeeklyReportId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_group_present_weekly_report_prese", "new_group_present_weekly_report", aWeeklyReportId); }
                #endregion
                #region 從名單取得 區名、小家長 ID、小組長 ID、小家長、區牧長 ID
                // 小家長 ID
                Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 小家長 ID
                Guid aRaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                // 區牧長 ID
                Guid aShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");

                // 區名
                //String AreaName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_area_name");

                #endregion
                #region 設定區名
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_area_name", AreaName);
                #endregion
                #region 關聯小家長屬性
                if (aFamilyLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_familyhead_present_record", "contact", aFamilyLeaderId); }
                #endregion
                #region 關聯小組長屬性
                if (aGroupLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_groupleader_present_record", "contact", aGroupLeaderId); }
                #endregion
                #region 關聯族系組長屬性
                if (aRaceLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_race_leader_present_record", "contact", aRaceLeaderId); }
                #endregion
                #region 關聯區牧長屬性
                if (aShepherdLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_arealeader_present_record", "contact", aShepherdLeaderId); }
                #endregion
                #region 關聯小組名單 Lookup
                if (aListEntity.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_list_new_present_record", "list", aListEntity.Id); }
                #endregion
                #region 設定主日及小組聚會日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", this.m_Sunday);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", DateTime.Now);
                #endregion
                #region 設定小組聚會地點和時間
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_place", m_SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_time", m_SmallGroupTime);
                #endregion
                #region 取得比較易懂的委身類型
                // 找到該組員的屬性
                //OptionSetValue aCustomerTypeCode = aContactEntity.Attributes["customertypecode"] as OptionSetValue;

                int OptionSetNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");
                // 取得比較易懂的委身類型
                //String ClearIdentity = this.ConvertIndexToClearIdentity(aCustomerTypeCode.Value);
                String ClearIdentity = this.ConvertIndexToClearIdentity(OptionSetNumber);
                #endregion
                #region 設定主日出席
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);
                #endregion
                #region 設定主日出席率
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);
                #endregion
                #region 設定小組出席
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);
                #endregion
                #region 設定小組出席率
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);
                #endregion
                #region 設定幸福小組出席
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 0);
                #endregion
                #region 設定決志
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_decision", 0);
                #endregion
                #region 設定附註或是代禱事項

                // 好牧人
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", aMemberInfomation.Note);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.PrayItem);

                // 好牧人
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region 設定小組是否暫停
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecord, "new_pause", PauseCheckBox);
                #endregion

                #region 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "mobilephone"));
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        #endregion
        #region 更新個人聚會與靈修記錄

        #region 更新出席紀錄
        private String ConvertIndexToClearIdentity(int Identity)
        {
            // 取得比較易懂的委身類型
            switch (Identity)
            {
                case 100000000:
                    return "新朋友";
                case 100000004:
                    return "未入組";
                case 100000007://其實是外教會，不過我把他歸類為未入組
                    return "未入組";
                case 1:
                    return "小組組員";
                default:
                    return "小組組員";
            }
        }
        public Double GetValidMemberNumber(EntityCollection aPresentRecordCollection)
        {
            try
            {
                Double ValidMemberNumber = 0;
                #region// 處理每個個人聚會與靈修記錄
                foreach (Entity PresentRecordEntity in aPresentRecordCollection.Entities)
                {
                    // 是否符合累積出席率可以貢獻出席的的委身類型，並且順便取的委身類型
                    String ClearIdentity = "";
                    if (this.IsValidMember(PresentRecordEntity, ref ClearIdentity) == true)
                    {
                        ValidMemberNumber++;
                    }
                }
                #endregion

                return ValidMemberNumber;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public bool IsValidMember(Entity PresentRecordEntity, ref String ClearIdentity)
        {
            try
            {
                #region// 處理每個個人聚會與靈修記錄

                // 每個出席紀錄
                if (PresentRecordEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = PresentRecordEntity.Attributes["statecode"] as OptionSetValue;

                    if (aOptionState.Value == 0)
                    {
                        #region 只回傳使用中的每個出席紀錄
                        if (PresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
                        {
                            // 個人聚會與靈修記錄的組員
                            EntityReference aEntityReference = (EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"];

                            // 取得連絡人姓名
                            //ContactName = aEntityReference.Name;

                            // 個人聚會與靈修記錄的組員 id
                            Guid aContactId = aEntityReference.Id;

                            // 取得組員實體
                            Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);

                            if (aContactEntity.Attributes.Contains("customertypecode"))
                            {
                                // 找到該組員的屬性
                                OptionSetValue aCustomerTypeCode = aContactEntity.Attributes["customertypecode"] as OptionSetValue;

                                // 取得比較易懂的委身類型
                                ClearIdentity = this.ConvertIndexToClearIdentity(aCustomerTypeCode.Value);

                                // 版本轉換
                                //// 如果是新朋友、未入組、外教會則不列入累積，好牧人
                                if (aCustomerTypeCode.Value != 100000004 && aCustomerTypeCode.Value != 100000000 && aCustomerTypeCode.Value != 100000007)
                                {
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }

                                // 如果是新朋友或是未入組則不列入累積，好牧人
                                // 10.不穩定組員   =   100,000,008
                                // 11.新朋友       =   100,000,009
                                // 12.未入組       =   100,000,010
                                // 13.暫不入組     =   100,000,012
                                // 14.結案         =   100,000,011
                                //if (aCustomerTypeCode.Value != 100000008 && aCustomerTypeCode.Value != 100000009 && aCustomerTypeCode.Value != 100000010 && aCustomerTypeCode.Value != 100000012 && aCustomerTypeCode.Value != 100000011 )
                                //{
                                //    return true;
                                //}
                                //else
                                //{
                                //    return false;
                                //}
                            }
                            else
                            {
                                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "customertypecode", 100000000);

                                this.m_ToolUtilityClass.UpdateEntity(ref aContactEntity);

                                return false;
                            }
                        }
                        else
                        { return false; }
                        #endregion
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private const int EMPTY_VALUE = -999999999;
        public bool IsValidContact(Entity aContactEntity)
        {
            try
            {
                #region// 處理組員是否列入計算

                // 找到該組員的屬性
                int aCustomerTypeCodeValue = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");
                //OptionSetValue aCustomerTypeCode = aContactEntity.Attributes["customertypecode"] as OptionSetValue;

                // 如果是新朋友或是未入組則不列入累積，好牧人
                if (aCustomerTypeCodeValue != 100000004 && aCustomerTypeCodeValue != 100000000 && aCustomerTypeCodeValue != 100000007 && aCustomerTypeCodeValue != EMPTY_VALUE)
                {
                    return true;
                }
                else
                {
                    return false;
                }


                // 如果是新朋友或是未入組則不列入累積，好牧人
                // 10.不穩定組員   =   100,000,008
                // 11.新朋友       =   100,000,009
                // 12.未入組       =   100,000,010
                // 13.暫不入組     =   100,000,012
                // 14.結案         =   100,000,011
                //if (aCustomerTypeCodeValue != 100000008 && aCustomerTypeCodeValue != 100000009 && aCustomerTypeCodeValue != 100000010 && aCustomerTypeCodeValue != 100000012 && aCustomerTypeCodeValue != 100000011 && aCustomerTypeCodeValue != EMPTY_VALUE )
                //{
                //    return true;
                //}
                //else
                //{
                //    return false;
                //}
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion

        #endregion
        #endregion
        #region 字典處理函式庫
        private void ResetDictionary(DateTime aSunday)
        {
            try
            {
                //AddToDictionary(ref this.m_SigningReport, "主日出席統計表頭", aSunday.ToLocalTime().ToShortDateString() + "出席紀錄(過去八週主日出席次數)" + Environment.NewLine);
                //AddToDictionary(ref this.m_SigningReport, "主日出席統計表頭", aSunday.ToLocalTime().ToShortDateString() + "出席紀錄(過去八週主日出席次數)" + Environment.NewLine);
            }
            catch (FormatException)
            {
                return;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }
        private bool AddToDictionary(ref Dictionary<String, String> aDictionary, String Method, String Content)
        {
            try
            {
                if (aDictionary.ContainsKey(Method))
                {
                    // 關鍵( Key ) 已經在字典裡了
                    aDictionary[Method] += Content;
                    return true;
                }
                else
                {
                    // 關鍵( Key )還沒有在字典裡
                    aDictionary.Add(Method, Content);
                    return false;
                }
            }
            catch (FormatException)
            {
                return false;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }
        private String GetDictionaryValue(ref Dictionary<String, String> aDictionary, String Method)
        {
            try
            {
                return aDictionary[Method];
            }
            catch (FormatException)
            {
                return "";
            }
            catch (System.Exception e)
            {
                return "";
            }
        }
        #endregion

    }
}


