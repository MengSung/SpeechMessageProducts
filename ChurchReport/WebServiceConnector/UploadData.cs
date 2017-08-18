using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.Models.CrmTransmitModule;

#region Dynamics 365 Microsoft.Xrm.Sdk.dll
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace;
using System.Text.RegularExpressions;
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class UploadData
    {
        #region 資料區
        #region 參數資料
        ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();

        bool m_SetIdentityFlag = false;
        #endregion

        #region 常數參數

        private const String CRM_TYPE = "CRM2011";
        //private const String CRM_TYPE = "DYNAMICS365";

        //private const int MONTH_PERIOD = 2;      //幾個月內出席超過這次數就會改變委身類型=>小組組員
        private const int WEEK_PERIOD = 8;      //過去幾　WEEK_PERIOD　周內出席超過這次數就會改變委身類型=>小組組員
        private const int MINIMUM_THRESHOLD = 4;      //2個月內出席超過這次數就會改變委身類型=>小組組員

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

        MemberInfomationPackage m_MemberInfomationPackage = new MemberInfomationPackage();
        DateTime m_Sunday;
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給族系族長/區長的名單

        Guid m_DecipleGroupListId;
        //Guid m_GroupLeaderId; // 小組長
        Guid m_RaceLeaderId; // 族系族長
        String m_SmallGroupPlace;
        String m_SmallGroupTime;

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
        //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        //List<Place2> m_GroupNamePlaces = new List<Place2>(); // 依據群組名稱過濾出來的會眾集合
        List<MemberInfomation> m_GroupNamedListMemberInfomation = new List<MemberInfomation>(); // 依據群組名稱過濾出來的會眾集合
        #endregion

        #region 上傳資料區

        #region WCF Service端
        public List<GroupWeeklyReportGuid> UploadMemberDataPackage(AccountPasswordData aAccountPasswordData, DateTime aSunday, String UploadCategory, MemberInfomationPackage aMemberInfomationPackage)
        {
            try
            {
                // 設定初始值
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "設定初始值");
                this.m_MemberInfomationPackage = aMemberInfomationPackage;

                // 設定參數
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "設定參數");
                SetupCommonParameter(aAccountPasswordData, aSunday);

                // 初始化報告字串
                //this.m_FeedBackReport.Clear();
                //this.ResetDictionary( aSunday );

                Entity aGraceLeaderWeeklyReportEntity = null;// 族系族長的週報

                foreach (GroupWeeklyReportGuid aGroupWeeklyReportGuid in m_MemberInfomationPackage.GroupWeeklyReportGuidList)
                {
                    // 初始化報告字串
                    this.m_FeedBackReport.Clear();
                    this.ResetDictionary(aSunday);

                    this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "依序處理要點名的清單");
                    #region 依序處理要點名的清單
                    #region 處理小組名稱、初始化字典
                    // 從 APP 傳來的包含主日出席率及小組出席率之後的小組名稱
                    String GroupName = aGroupWeeklyReportGuid.GroupName;

                    // 去除掉主日出席率及小組出席率之後的小組名稱
                    String FilteredGroupName = ToolUtilityClass.DeletePresentRate(GroupName);

                    String FilteredOutDigitGroupName = Regex.Replace(FilteredGroupName, "[0-9]", "");//過濾掉數字
                    FilteredOutDigitGroupName = FilteredOutDigitGroupName.Replace(" ", ""); // //過濾掉空白
                    AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", FilteredOutDigitGroupName + Environment.NewLine + "主日出席紀錄:");
                    //AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭" , "");
                    //AddToDictionary(ref this.m_FeedBackReport, "跟進統計表頭"     , "");

                    Guid aWeeklyReportId = aGroupWeeklyReportGuid.WeeklyReportGuid;
                    #endregion
                    #region 處理一個名單
                    this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "處理每個已被過濾萃取的名單，找到與群組名稱相同的名單");
                    Entity aListEntity = FindListByName(FilteredGroupName);// 處理每個已被過濾萃取的名單，找到與群組名稱相同的名單
                    if (aListEntity != null)
                    {
                        // 有找到要點名的名單，但是必須登入的使用者與此名單的小組長ID要一致才能夠修改或新增點名內容，也就是族系族長不能修改小組長的點名單
                        #region 先找到"小家長"、"小組長"、族系族長/區長"
                        // 先找到這個名單的小家長 ID，內壢得勝靈糧堂專用
                        Guid aThisListFamilyHeadId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                        // 先找到這個名單的小組長 ID
                        Guid aThisListSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                        // 先找到這個名單的族系族長/區長 ID
                        Guid aThisListGraceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                        #endregion

                        // this.m_ContactId 的意思是登入者在系統裡的ID，登入者是"小家長"、"小組長"、族系族長/區長"
                        if (this.m_ContactId == aThisListSmallGroupLeaderId || this.m_ContactId == aThisListFamilyHeadId || this.m_ContactId == aThisListGraceLeaderId)
                        {
                            #region 有找到要點名的名單，而且登入的操作者與此名單或是與小組長ID、或是與小家長ID、或是與族系族長/區長一致

                            // 設定是否要計算過去N週的出席的旗標，族系族長/區長不要去計算或修改小組長的出席紀錄
                            bool CalculateFlag = DeterminCalculateFlag(m_ContactId, aThisListFamilyHeadId, aThisListSmallGroupLeaderId, aThisListGraceLeaderId);
                            CalculateFlag = true; // 強迫每個都計算
                            if (CalculateFlag == false)
                            {
                                // 如果不需要計算那就直接處理下一個小組
                                continue;
                            }
                            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "有找到要點名的名單");
                            // 重新整理跟群組名稱一致的點名清單
                            this.SetupGroupNamedListMemberInfomation(GroupName);

                            Double aWeeklySundayRate = 0.0;
                            Double aWeeklySmallGroupRate = 0.0;
                            int aWeeklySundayNumber = 0;
                            int aWeeklySmallGroupNumber = 0;
                            if (aWeeklyReportId == Guid.Empty)
                            {
                                #region // 要建立週報
                                #region // 依據有效的週報的小組組員名單當作週報出席率的分母
                                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "依據有效的週報的小組組員名單當作週報出席率的分母");
                                Double ValidNumber = this.GetEffecttiveSmallGroupNumber(aListEntity.Id);
                                #endregion
                                #region// 要建立週報
                                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "要建立週報");
                                if (this.CreateWeeklyReportOrNot(ref aListEntity)) // 判斷是否真要建立週報
                                {
                                    // 建立週報
                                    aGraceLeaderWeeklyReportEntity = ToCreateWeeklyReport(aGroupWeeklyReportGuid, ref aListEntity, UploadCategory, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber);
                                }
                                #endregion
                                #endregion
                            }
                            else
                            {
                                #region// 更新週報
                                aGraceLeaderWeeklyReportEntity = ToUpdateWeeklyReport(aGroupWeeklyReportGuid, aWeeklyReportId, ref aListEntity, UploadCategory, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, CalculateFlag);
                                #endregion
                            }

                            #region 如果登入者是族系族長，則他的週報要留下來，以便寫入他底下的小組長，所有的主日、小組出席紀錄
                            //if ( this.m_ContactId != aThisListGraceLeaderId )
                            //{
                            //    //　不是族系族長，週報設為ＮＵＬＬ
                            //    aGraceLeaderWeeklyReportEntity = null;
                            //}
                            #endregion

                            #endregion
                        }
                        else
                        {
                            #region 有找到要點名的名單，但是登入的操作者與此名單小組長ID或是與小家長ID "不一致"，所以就忽略不處理
                            #endregion
                        }
                    }
                    else
                    {
                        // 根本找不到這個要被點名的名單，所以就甚麼也不做
                    }
                    #endregion
                    #endregion
                }

                #region // 更新族系族長週報

                //if ( aGraceLeaderWeeklyReportEntity != null )
                //{
                //    //主日出席寫入至族系族長週報
                //    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aGraceLeaderWeeklyReportEntity, "new_sunday_present_report", this.GetDictionaryValue(ref this.m_FeedBackReport, "主日統計") );
                //    //小組出席寫入至族系族長週報
                //    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aGraceLeaderWeeklyReportEntity, "new_small_group_present_report", this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計"));
                //    //新人跟進寫入至週報
                //    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aGraceLeaderWeeklyReportEntity, "new_follow_up_report", this.GetDictionaryValue(ref this.m_FeedBackReport, "新朋友跟進"));
                //
                //    // 更新族系族長週報
                //    this.m_ToolUtilityClass.UpdateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, ref aGraceLeaderWeeklyReportEntity);
                //}
                #endregion

                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "回傳結果");
                return m_MemberInfomationPackage.GroupWeeklyReportGuidList;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private bool DeterminCalculateFlag(Guid m_ContactId, Guid aThisListFamilyHeadId, Guid aThisListSmallGroupLeaderId, Guid aThisListGraceLeaderId)
        {
            // Guid m_ContactId,                    登入者
            // Guid aThisListFamilyHeadId,          小家長
            // Guid aThisListSmallGroupLeaderId,    小組長
            // Guid aThisListGraceLeaderId，        族系族長/區長
            try
            {
                if (m_ContactId == aThisListSmallGroupLeaderId && m_ContactId != aThisListGraceLeaderId)
                {
                    #region 登入者是小組長，但不是族系族長/區長
                    return true;
                    #endregion
                }
                else if (m_ContactId == aThisListSmallGroupLeaderId && m_ContactId == aThisListGraceLeaderId)
                {
                    #region  登入者是族系族長/區長，而且也是族系族長/區長
                    return true;
                    #endregion
                }
                else if (m_ContactId != aThisListSmallGroupLeaderId && m_ContactId == aThisListGraceLeaderId)
                {
                    #region  登入者是族系族長/區長，而且也是族系族長/區長
                    return false;
                    #endregion
                }
                else
                {
                    #region 登入者是族系族長/區長，但不是小組長
                    return false;
                    #endregion
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupWeeklyReportStatus(String UploadCategory, ref Entity aWeeklyReportEntity)
        {
            try
            {
                // 設定週報點名狀態
                // 均未點名                 = 100,000,000
                // 均已點名                 = 100,000,001
                // 主日點名，小組未點名     = 100,000,003
                // 小組點名，主日未點名     = 100,000,004

                int Status = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status");
                if (Status == 100000000)
                {
                    #region 均未點名
                    if (UploadCategory == "主日點名")
                    {
                        // 設定主日點名，小組未點名
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000003);
                    }
                    else if (UploadCategory == "小組點名")
                    {
                        // 小組點名，主日未點名
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000004);
                    }
                    else { }
                    #endregion
                }
                else if (Status == 100000003)
                {
                    #region 主日點名，小組未點名
                    if (UploadCategory == "小組點名")
                    {
                        // 均已點名
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                    }
                    else { }
                    #endregion
                }
                else if (Status == 100000004)
                {
                    #region 小組點名，主日未點名
                    if (UploadCategory == "主日點名")
                    {
                        // 均已點名
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                    }
                    else { }
                    #endregion
                }
                else
                {
                    #region 均未點名
                    if (UploadCategory == "主日點名")
                    {
                        // 設定主日點名，小組未點名
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000003);
                    }
                    else if (UploadCategory == "小組點名")
                    {
                        // 小組點名，主日未點名
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000004);
                    }
                    else { }
                    #endregion
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupCommonParameter(AccountPasswordData aAccountPasswordData, DateTime aSunday)
        {
            try
            {
                // 設定主日日期
                m_Sunday = aSunday;

                // 找到操作使用者登入的小組長ID
                m_ContactEntity = m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(aAccountPasswordData.Account, aAccountPasswordData.Password);
                m_ContactId = m_ContactEntity.Id;

                #region 蒐集建立週報所需要的屬性
                // 根據是否是族系族長還是小組長會設定不同的要上傳的名單集合
                // 並且該名單是有勾選APP點名的才被允許進來
                this.FindListCollection();

                // 搜尋小組長的門徒小組名單Lookup Id
                m_DecipleGroupListId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_deciple_group_list_contact");

                // 搜尋小家長的小組長 Lookup Id
                // 小組長 ID
                //this.m_GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_race_leader_contact");

                // 搜尋小家長的族系族長 Lookup Id
                // 族系組長 ID
                m_RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_race_leader_contact");

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void FindListCollection()
        {
            try
            {
                // 先尋找族系名單
                EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_race_leager_list", "list");
                if (aListEntityCollection.Entities.Count > 0)
                {
                    // 小組長小組名單集合
                    EntityCollection aFamilyLeaderListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");

                    // 合併小組名單至族系名單，單扣除掉重複的
                    // 然後放在小組名單裡面
                    EntityCollection aMergeCollection = MergeCollection(ref aListEntityCollection, ref aFamilyLeaderListEntityCollection);


                    // 過濾掉需要點名的名單才進來
                    FilterAppNamedListEntity(aMergeCollection);

                    // 帶領族系裡有名單，所以是族系組長，就不用在往下找看是不是小組長了 
                    return;
                }

                // 找到小組長小組名單集合 
                aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");
                if (aListEntityCollection.Entities.Count > 0)
                {
                    // 過濾掉需要點名的名單才進來
                    FilterAppNamedListEntity(aListEntityCollection);
                    // 帶領族系裡有名單，所以是族系組長，就不用在往下找看是不是小組長了 
                    return;
                }

                // 找到小家長小組名單集合 
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_familyhead_list", "list");
                //if (aListEntityCollection.Entities.Count > 0)
                //{
                //    FilterAppNamedListEntity(aListEntityCollection);
                //    return;
                //}

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private EntityCollection MergeCollection(ref EntityCollection aListEntityCollection, ref EntityCollection aFamilyLeaderListEntityCollection)
        {
            try
            {
                // 合併小組名單至族系名單，單扣除掉重複的
                // 然後放在小組名單裡面
                foreach (Entity RaceListEntity in aListEntityCollection.Entities)
                {
                    // 一個一個處理族系名單
                    bool Flag = false;
                    foreach (Entity FamilyLeaderListEntity in aFamilyLeaderListEntityCollection.Entities)
                    {
                        if (RaceListEntity.Id == FamilyLeaderListEntity.Id)
                        {
                            // 在小組名單裡已經有了，就跳出迴圈，不再找了
                            Flag = true;
                            break;
                        }
                    }

                    if (Flag == false)
                    {
                        // 這個小組名單並沒有在族系名單之中
                        //aListEntityCollection.Entities.Add(FamilyLeaderListEntity);

                        // 這個族系名單並沒有在小組名單之中
                        aFamilyLeaderListEntityCollection.Entities.Add(RaceListEntity);
                    }
                    else { }
                }

                return aFamilyLeaderListEntityCollection;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void FilterAppNamedListEntity(EntityCollection aListEntityCollection)
        {
            try
            {
                // 過濾掉需要點名的名單才進來
                if (this.m_Lists != null && this.m_Lists.Entities != null)
                {
                    this.m_Lists.Entities.Clear();
                }

                foreach (Entity ListEntity in aListEntityCollection.Entities)
                {
                    if (ListEntity.Attributes.Contains("new_app_named"))
                    {
                        bool AppNamed = (bool)ListEntity.Attributes["new_app_named"];

                        if (AppNamed == true)
                        {
                            this.m_Lists.Entities.Add(ListEntity);
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
        private void FilterAppNamedListEntity(String aIdentity, EntityCollection aListEntityCollection)
        {
            try
            {
                // 過濾掉需要點名的名單才進來
                if (this.m_Lists != null && this.m_Lists.Entities != null)
                {
                    this.m_Lists.Entities.Clear();
                }

                foreach (Entity ListEntity in aListEntityCollection.Entities)
                {
                    if (ListEntity.Attributes.Contains("new_app_named"))
                    {
                        bool AppNamed = (bool)ListEntity.Attributes["new_app_named"];

                        if (AppNamed == true)
                        {
                            if (aIdentity == "族長")
                            {
                                //  族長   = new_contact_race_leager_list
                                //  小組長 = new_contact_family_leader_list
                                //  楊梅靈糧堂，因為楊梅靈糧堂沒有小家長
                                //Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ListEntity, "new_familyhead_list");
                                Guid GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ListEntity, "new_contact_family_leader_list");

                                String ListName = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname");

                                // 過濾掉需要點名的名單才進來，若是族長則名單裡就應該沒有"小家長"、"小組長"
                                //if (FamilyLeaderId == Guid.Empty && GroupLeaderId == Guid.Empty)
                                if (GroupLeaderId == Guid.Empty || GroupLeaderId == m_ContactId)
                                {
                                    if (!ListName.Contains("門徒")) // 不包含"門徒"名單
                                    {
                                        this.m_Lists.Entities.Add(ListEntity);
                                    }
                                }

                                // 需要回報給族系族長/區長的名單
                                if (!ListName.Contains("門徒")) // 不包含"門徒"名單
                                {
                                    this.m_PresentLists.Entities.Add(ListEntity);
                                }

                            }
                            else if (aIdentity == "小組長")
                            {
                                //Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ListEntity, "new_familyhead_list");
                                //
                                //// 過濾掉需要點名的名單才進來，若是小組長則名單裡就應該沒有"小家長"
                                //if (FamilyLeaderId == Guid.Empty )
                                //{
                                //    this.m_Lists.Entities.Add(ListEntity);
                                //}


                                this.m_Lists.Entities.Add(ListEntity);
                                // 需要回報給族系族長/區長的名單
                                this.m_PresentLists.Entities.Add(ListEntity);
                            }
                            else if (aIdentity == "小家長")
                            {
                                this.m_Lists.Entities.Add(ListEntity);
                                // 需要回報給族系族長/區長的名單
                                this.m_PresentLists.Entities.Add(ListEntity);
                            }
                            else { }
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

        private bool CreateWeeklyReportOrNot(ref Entity aListEntity)
        {
            try
            {
                return true; // 永遠都是通過，可以建立周報、靈修紀錄單
                #region
                //if (aListEntity != null)
                //{
                //    #region// 有找到吻合的名單
                //    // 比對名單中的小組長跟族系組長是否是同一人
                //    // 名單裡的小組長 ID
                //    Guid aSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
                //    // 名單裡的族系族長 ID
                //    Guid aRaceGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");
                //
                //    if (this.m_RaceLeaderId == Guid.Empty) // m_RaceLeaderId 是指小組長的族系組長ID
                //    {
                //        #region// 是族系組長
                //        if (aSmallGroupLeaderId == aRaceGroupLeaderId)
                //        {
                //            // 名單裡的小組長和族系組長是同一個
                //            // 族長自己的小組我已要新建週報
                //            return true;
                //        }
                //        else
                //        {
                //            if (RACE_LEADER_CAN_CREATE_WEEKLYREPORT == true)
                //            {
                //                // 族系組長能否幫小組長建立週報
                //                return true;
                //            }
                //            else
                //            {
                //                // 名單裡的小組長和族系組長不是同一個
                //                // 就不要建立週報，因為不希望族長幫小組長建立週報
                //                return false;
                //            }
                //        }
                //        #endregion
                //    }
                //    else
                //    {
                //        #region// 是小組長
                //        return true;
                //        #endregion
                //    }
                //    #endregion
                //}
                //else
                //{
                //    #region// 沒找到吻合的名單
                //    return false;
                //    #endregion
                //}
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private Entity FindListByName(String GroupName)
        {
            try
            {
                // 處理每個已被過濾萃取的名單，找到與群組名稱相同的名單
                foreach (Entity aListEntity in this.m_Lists.Entities)
                {
                    if (aListEntity.Attributes.Contains("listname"))
                    {
                        String aListName = (String)aListEntity.Attributes["listname"];//取得名單名稱
                        if (aListName == GroupName) // 比對群組名稱和名單名稱
                        { return aListEntity; } // 有找到吻合的名單
                        else { } // 不吻合，則繼續比對下一個名單
                    }
                }
                return null; // 全部都不吻合
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void SetupGroupNamedListMemberInfomation(String GroupName)
        {
            try
            {
                // 重新整理跟群組名稱一致的點名清單
                this.m_GroupNamedListMemberInfomation = null;
                this.m_GroupNamedListMemberInfomation = new List<MemberInfomation>(); // 依據群組名稱過濾出來的會眾集合
                this.m_GroupNamedListMemberInfomation.Clear();

                foreach (MemberInfomation aMemberInfomation in this.m_MemberInfomationPackage.ListMemberInfomation)
                {
                    if (aMemberInfomation.Group == GroupName)
                    {
                        this.m_GroupNamedListMemberInfomation.Add(aMemberInfomation);
                    }
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #region 建立的週報
        private Entity ToCreateWeeklyReport(GroupWeeklyReportGuid aGroupWeeklyReportGuid, ref Entity aListEntity, String UploadCategory, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber)
        {
            try
            {
                // 建立週報
                Guid aCreatedWeeklyReportId = CreateWeeklyReport(ref aListEntity);

                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 建立的個人聚會與靈修記錄
                // 同時整理並取得:主日出席回報、小組出席回報、新人跟進字串，因為這樣就可以一魚兩吃，比較有效能一點
                int ValidSundayMemberNumber = 0;
                int ValidSmallGroupMemberNumber = 0;
                CreatePresentRecordList(ref aListEntity, ref aCreatedWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber);

                #region 設定週報狀態，設定為已點名、週報主日出席率、小組出席率
                Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aCreatedWeeklyReportId);

                // 設定週報點名狀態
                this.SetupWeeklyReportStatus(UploadCategory, ref aWeeklyReportEntity);
                //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);

                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_sunday_present_rate", aWeeklySundayRate);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_small_group_rate", aWeeklySmallGroupRate);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_sunday_present_number", aWeeklySundayNumber);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_small_group_number", aWeeklySmallGroupNumber);

                //百分比 
                AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", ValidSundayMemberNumber.ToString() + "/" + ValidNumber.ToString() + "，" + String.Format("{0:0%}", aWeeklySundayRate) + Environment.NewLine);
                AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭", ValidSmallGroupMemberNumber.ToString() + "/" + ValidNumber.ToString() + "，" + String.Format("{0:0%}", aWeeklySmallGroupRate) + Environment.NewLine);

                //AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", ValidSundayMemberNumber.ToString() + "/" + ValidNumber.ToString() + "，" + aWeeklySundayRate.ToString("{0:0%}") + Environment.NewLine);
                //AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭", ValidSmallGroupMemberNumber.ToString() + "/" + ValidNumber.ToString() + "，" + aWeeklySmallGroupRate.ToString("{0:0%}") + Environment.NewLine);

                // 建立週報主日出席、小組出席、新人跟進字串內容
                this.SetupWeeklyReportResult(ref aWeeklyReportEntity);

                // 更新週報
                this.m_ToolUtilityClass.UpdateEntity(ref aWeeklyReportEntity);
                #endregion

                #region 回傳至 APP
                aGroupWeeklyReportGuid.WeeklyReportGuid = aCreatedWeeklyReportId;   // 回傳至 APP 的週報 Id
                aGroupWeeklyReportGuid.SundayPresentRate = aWeeklySundayRate;       // 回傳至 APP 的主日出席率
                aGroupWeeklyReportGuid.SmallGroupRate = aWeeklySmallGroupRate;      // 回傳至 APP 的小組出席率
                #endregion

                // 傳回建立的週報
                return aWeeklyReportEntity;

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private Entity ToUpdateWeeklyReport(GroupWeeklyReportGuid aGroupWeeklyReportGuid, Guid aWeeklyReportId, ref Entity aListEntity, String UploadCategory, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, bool CalculateFlag)
        {
            try
            {
                #region// 更新週報
                #region // 依據之前點名的靈修紀錄的有效組員當作週報出席率的分母
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "依據之前點名的靈修紀錄的有效組員當作週報出席率的分母");
                Double ValidNumber = GetValidMemberNumber(aWeeklyReportId);
                #endregion
                #region// 更新週報
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "更新週報");

                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 更新個人聚會與靈修記錄
                int ValidSundayMemberNumber = 0;
                int ValidSmallGroupMemberNumber = 0;
                UpdatePresentRecord(ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber);

                #region 設定週報狀態，設定為已點名、週報主日出席率、小組出席率

                Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aWeeklyReportId);

                // 設定新人跟進報告

                // 設定週報點名狀態
                this.SetupWeeklyReportStatus(UploadCategory, ref aWeeklyReportEntity);

                #region 設定出席率、人數
                //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000001);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_sunday_present_rate", aWeeklySundayRate);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aWeeklyReportEntity, "new_small_group_rate", aWeeklySmallGroupRate);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_sunday_present_number", aWeeklySundayNumber);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_small_group_number", aWeeklySmallGroupNumber);

                // 更新週報
                //this.m_ToolUtilityClass.UpdateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, ref aWeeklyReportEntity);

                // 加入百分比 
                AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", ValidSundayMemberNumber.ToString() + "/" + ValidNumber.ToString() + "，" + String.Format("{0:0%}", aWeeklySundayRate) + Environment.NewLine);
                AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭", ValidSmallGroupMemberNumber.ToString() + "/" + ValidNumber.ToString() + "，" + String.Format("{0:0%}", aWeeklySmallGroupRate) + Environment.NewLine);
                #endregion

                // 建立週報主日出席、小組出席、新人跟進字串內容
                this.SetupWeeklyReportResult(ref aWeeklyReportEntity);

                // 更新週報
                this.m_ToolUtilityClass.UpdateEntity(ref aWeeklyReportEntity);

                #endregion

                #region 回傳至 APP
                aGroupWeeklyReportGuid.WeeklyReportGuid = aWeeklyReportId;         // 回傳至 APP 的週報 Id
                aGroupWeeklyReportGuid.SundayPresentRate = aWeeklySundayRate;       // 回傳至 APP 的主日出席率
                aGroupWeeklyReportGuid.SmallGroupRate = aWeeklySmallGroupRate;   // 回傳至 APP 的小組出席率
                #endregion
                #endregion
                #endregion

                // 傳回建立的週報
                return aWeeklyReportEntity;

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupWeeklyReportResult(ref Entity aWeeklyReportEntity)
        {
            try
            {
                SetupSundayPresentResult(ref aWeeklyReportEntity);
                //SetupSmallGroupPresentResult(ref aWeeklyReportEntity );
                //SetupFollowupResult(ref aWeeklyReportEntity );
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupSundayPresentResult(ref Entity aWeeklyReportEntity)
        {
            try
            {

                String SundayResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "主日出席統計表頭") +
                    "\t" + "A.小組組員" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "出席　：", "主日統計小組組員出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "未出席：", "主日統計小組組員未出席字串") +
                    "\t" + "B.未入組家人" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "出席　：", "主日統計未入組出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "未出席：", "主日統計未入組出未席字串") +
                    "\t" + "C.新朋友" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "出席　：", "主日統計新人出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "未出席：", "主日統計新人未出席字串")
                    ;



                //String SundayResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "主日出席統計表頭") +
                //    "\t" + "A.小組組員" + Environment.NewLine +
                //    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "主日統計小組組員出席字串") + Environment.NewLine +
                //    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "主日統計小組組員未出席字串") + Environment.NewLine +
                //    "\t" + "B.未入組家人" + Environment.NewLine +
                //    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "主日統計未入組出席字串") + Environment.NewLine +
                //    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "主日統計未入組出未席字串") + Environment.NewLine +
                //    "\t" + "C.新朋友" + Environment.NewLine +
                //    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "主日統計新人出席字串") + Environment.NewLine +
                //    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計新人未出席字串") + Environment.NewLine
                //    ;
                //SundayResult += "---------------------------------" + Environment.NewLine;
                SundayResult += Environment.NewLine + Environment.NewLine;

                String SmallGroupResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "小組出席統計表頭") +
                    "\t" + "A.小組組員" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "出席　：", "小組統計小組組員出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "未出席：", "小組統計小組組員未出席字串") +
                    "\t" + "B.未入組家人" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "出席　：", "小組統計未入組出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "未出席：", "小組統計未入組出未席字串") +
                    "\t" + "C.新朋友" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "出席　：", "小組統計新人出席字串") +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "\t\t" + "未出席：", "小組統計新人未出席字串")
                    ;

                //String SmallGroupResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "小組出席統計表頭") +
                //    "\t" + "A.小組組員" + Environment.NewLine +
                //    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計小組組員出席字串") + Environment.NewLine +
                //    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計小組組員未出席字串") + Environment.NewLine +
                //    "\t" + "B.未入組家人" + Environment.NewLine +
                //    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計未入組出席字串") + Environment.NewLine +
                //    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計未入組出未席字串") + Environment.NewLine +
                //    "\t" + "C.新朋友" + Environment.NewLine +
                //    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計新人出席字串") + Environment.NewLine +
                //    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計新人未出席字串") + Environment.NewLine
                //    ;
                //SmallGroupResult += "---------------------------------" + Environment.NewLine;
                SmallGroupResult += Environment.NewLine + Environment.NewLine;

                String FollowUpResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "跟進統計表頭") +
                    "\t" + "A.未入組跟進統計內容" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "未入組跟進統計內容") + Environment.NewLine +
                    "\t" + "B.新朋友跟進統計內容" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "新朋友跟進統計內容") + Environment.NewLine
                    ;
                FollowUpResult += "---------------------------------" + Environment.NewLine;

                // 主日出席寫入至週報
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_sunday_present_report", SundayResult + SmallGroupResult + FollowUpResult);

                // 加總族系族長的主日出席
                //AddToDictionary(ref this.m_FeedBackReport, "主日統計", SundayResult);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupSmallGroupPresentResult(ref Entity aWeeklyReportEntity)
        {
            try
            {
                String SmallGroupResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "小組出席統計表頭") +
                    "\t" + "A.小組組員" + Environment.NewLine +
                    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計小組組員出席字串") + Environment.NewLine +
                    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計小組組員未出席字串") + Environment.NewLine +
                    "\t" + "B.未入組家人" + Environment.NewLine +
                    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計未入組出席字串") + Environment.NewLine +
                    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計未入組出未席字串") + Environment.NewLine +
                    "\t" + "C.新朋友" + Environment.NewLine +
                    "\t\t" + "出席　：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計新人出席字串") + Environment.NewLine +
                    "\t\t" + "未出席：" + this.GetDictionaryValue(ref this.m_FeedBackReport, "小組統計新人未出席字串") + Environment.NewLine
                    ;

                SmallGroupResult += "---------------------------------" + Environment.NewLine;

                //小組出席寫入至週報
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_small_group_present_report", SmallGroupResult);

                // 加總族系族長的小組出席
                //AddToDictionary(ref this.m_FeedBackReport, "小組統計", SmallGroupResult);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupFollowupResult(ref Entity aWeeklyReportEntity)
        {
            try
            {
                String FollowUpResult = this.GetDictionaryValue(ref this.m_FeedBackReport, "跟進統計表頭") +
                    "\t" + "A.未入組跟進統計內容" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "未入組跟進統計內容") + Environment.NewLine +
                    "\t" + "B.新朋友跟進統計內容" + Environment.NewLine +
                    this.GetDictionaryValue(ref this.m_FeedBackReport, "新朋友跟進統計內容") + Environment.NewLine
                    ;

                FollowUpResult += "---------------------------------" + Environment.NewLine;

                //新人跟進寫入至週報
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_follow_up_report", FollowUpResult);

                // 加總族系族長的新人跟進
                //AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進", FollowUpResult);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private Guid CreateWeeklyReport(ref Entity aListEntity)
        {
            try
            {
                // 這是新建立的週報
                Entity aWeeklyReportEntity = new Entity("new_group_present_weekly_report");

                // 小組聚會地點和時間
                m_SmallGroupPlace = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_place");
                m_SmallGroupTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_time");

                // 小家長 ID
                Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 區長 ID
                Guid RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                // 設定週報相關屬性
                this.SetupWeeklyReortEntityAttributes(ref aWeeklyReportEntity, FamilyLeaderId, GroupLeaderId, RaceLeaderId, m_DecipleGroupListId, aListEntity.Id, m_Sunday, m_SmallGroupPlace, m_SmallGroupTime);

                // 新增週報
                return this.m_ToolUtilityClass.CreateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, aWeeklyReportEntity);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupWeeklyReortEntityAttributes(ref Entity aWeeklyReportEntity, Guid aFamilyLeaderId, Guid aGroupLeaderId, Guid aRaceLeaderId, Guid aDecipleGroupList, Guid ListEntityId, DateTime aSunday, String SmallGroupPlace, String SmallGroupTime)
        {
            try
            {
                #region 關聯小家長屬性
                if (aFamilyLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_contact_weekly_report_parents", "contact", aFamilyLeaderId); }
                #endregion
                #region 關聯小組長屬性
                if (aGroupLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_groupleader_group_present_weekly_", "contact", aGroupLeaderId); }
                #endregion
                #region 關聯族系組長屬性
                if (aRaceLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_group_head_group_present_weekly_r", "contact", aRaceLeaderId); }
                #endregion
                #region 關聯小組名單 Lookup
                if (ListEntityId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_list_group_present_weekly_report", "list", ListEntityId); }
                #endregion
                #region 關聯門徒小組名單 Lookup
                if (aDecipleGroupList != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_deciple_list_group_present_weekly", "list", aDecipleGroupList); }
                #endregion
                #region 設定主日及小組聚會日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_sunday_date", aSunday);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", aSunday);
                #endregion
                #region 設定小組聚會地點和時間
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_place", SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_time", SmallGroupTime);
                #endregion
                #region 設定週報狀態，設定為均未點名，因為後面程式還會再設定一次
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_weekly_report_status", 100000000);
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
        private void CreatePresentRecordList(ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber)
        {
            foreach (MemberInfomation aMemberInfomation in this.m_GroupNamedListMemberInfomation)
            {
                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 新增個人聚會與靈修記錄
                CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber);
            }
        }
        private void CreatePresentRecord(MemberInfomation aMemberInfomation, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber)
        {
            Entity aContactEntity = m_ToolUtilityClass.RetrieveContactEntityByName( aMemberInfomation.Name);
            //Entity aSearchedContactEntity = m_ToolUtilityClass.RetrieveContactByNameAndMobile(ref m_ToolUtilityClass.m_OrganizationService, aMemberInfomation.Name, aMemberInfomation.Phone );

            Entity aToUpdateContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactEntity.Id);
            //Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aSearchedContactEntity.Id);

            if (aContactEntity != null)
            {
                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 但是不知何故，更新連絡人之後，委身類型卻會"自動變成" 新朋友，所以就先用一個可以受影響的Entity aToUpdateContactEntity，去更新連絡人
                UpdateContactInfomation(aListEntity.Id, ref aMemberInfomation, ref aToUpdateContactEntity);

                // 這是新建立的個人聚會與靈修記錄
                Entity aPresentRecord = new Entity("new_present_record");

                // 設定個人聚會與靈修記錄相關屬性
                this.SetupPresentRecordEntityAttributes(aPresentRecord, aMemberInfomation, ref aContactEntity, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber);

                // 新增個人聚會與靈修記錄
                Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, aPresentRecord);

                //取得新建的聚會與靈修記錄
                //Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);

                //更新聚會與靈修記錄，以便交由SDK自己計算出席率
                //this.m_ToolUtilityClass.UpdateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, ref aRetrievedPresentRecord);
            }
        }
        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, MemberInfomation aMemberInfomation, ref Entity aContactEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber)
        {
            try
            {
                #region 設定姓名
                // 找到組員ID
                Guid aContactEntityId = aContactEntity.Id;
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntityId);
                #endregion
                #region 關聯週報 Lookup
                if (aWeeklyReportId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_group_present_weekly_report_prese", "new_group_present_weekly_report", aWeeklyReportId); }
                #endregion
                #region 從名單取得 小家長 ID、小組長 ID、區長 ID
                // 小家長 ID
                Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 區長 ID
                Guid aRaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");
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
                #region 關聯小組名單 Lookup
                if (aListEntity.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_list_new_present_record", "list", aListEntity.Id); }
                #endregion
                #region 設定主日及小組聚會日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", this.m_Sunday);
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", this.m_Sunday);
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
                if (aMemberInfomation.SundayPresent == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 1);
                    AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, true);
                    aWeeklySundayNumber += 1;
                }
                else
                {
                    AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, false);
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);
                }
                #endregion
                #region 設定主日出席率
                if (aMemberInfomation.SundayPresent == true)
                {
                    if (ValidNumber != 0)
                    {
                        this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 1 / ValidNumber);

                        if (IsValidContact(aContactEntity) == true)
                        {
                            ValidSundayMemberNumber++;
                            aWeeklySundayRate += 1 / ValidNumber;
                        }
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);
                }
                #endregion
                #region 設定小組出席
                if (aMemberInfomation.SmallGroupPresent == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 1);

                    AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, true);

                    aWeeklySmallGroupNumber += 1;
                }
                else
                {
                    AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, false);

                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);
                }
                #endregion
                #region 設定小組出席率
                if (aMemberInfomation.SmallGroupPresent == true)
                {
                    if (ValidNumber != 0)
                    {
                        this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 1 / ValidNumber);

                        if (IsValidContact(aContactEntity) == true)
                        {
                            ValidSmallGroupMemberNumber++;
                            aWeeklySmallGroupRate += 1 / ValidNumber; ;
                        }
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);
                }
                #endregion
                #region 設定附註或是代禱事項

                // 楊梅靈糧堂
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", aMemberInfomation.Note);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.Note);

                // 內壢得勝靈糧堂
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region// 新人跟進

                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMemberInfomation.FollowUpWeek));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMemberInfomation.FollowUpResult));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMemberInfomation.FollowUpNextStep));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_follow_up", aMemberInfomation.FollowUp);

                // 因為之前APP無法直接把代禱事項和新人跟進關懷用在表單中
                // 但是網頁現在可以了
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMemberInfomation.FollowUpNote);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.Note);

                AddToDictionaryFollowByIdentity(ref ClearIdentity, ref aContactEntity, aMemberInfomation);

                #endregion
                #region 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", aMemberInfomation.Phone);
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }

        private double GetEffecttiveSmallGroupNumber(Guid ListEntityId)
        {
            #region // 處理每個小組名單
            //搜尋名單的組員
            //EntityCollection Contacts = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntityId.ToString(), "new_cell_list_contact", "contact");
            //EntityCollection MemberCollection = m_ToolUtilityClass.RetrieveMemberListCollectionByListId(ref m_ToolUtilityClass.m_OrganizationService, ListEntityId);

            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);

            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");
            EntityCollection MemberCollection;
            if (ListType == false)
            {
                // 靜態名單
                MemberCollection = m_ToolUtilityClass.RetrieveMemberListCollectionByListId(ListEntityId);
            }
            else
            {
                // 動態名單
                MemberCollection = m_ToolUtilityClass.RetrieveDynamicMemberList(ListEntityId);
            }

            Double EffectiveNumber = 0.0;
            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                // 每個組員
                Entity ContactEntity;

                if (ListType == false)
                {
                    // 靜態名單
                    ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                }
                else
                {
                    // 動態名單
                    ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                }


                // 每個組員
                if (ContactEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;

                    if (aOptionState.Value == 0)
                    {
                        #region 只回傳使用中的組員
                        if (ContactEntity.Attributes.Contains("customertypecode"))
                        {
                            OptionSetValue aCustomerTypeCode = ContactEntity.Attributes["customertypecode"] as OptionSetValue;

                            // 如果是新朋友或是未入組則不列入累積，楊梅靈糧堂
                            if (aCustomerTypeCode.Value != 100000004 && aCustomerTypeCode.Value != 100000000 && aCustomerTypeCode.Value != 100000007)
                            {
                                EffectiveNumber++;
                            }

                            // 如果是新朋友或是未入組則不列入累積，內壢得勝靈糧堂
                            // 10.不穩定組員   =   100,000,008
                            // 11.新朋友       =   100,000,009
                            // 12.未入組       =   100,000,010
                            // 13.暫不入組     =   100,000,012
                            // 14.結案         =   100,000,011
                            //if (aCustomerTypeCode.Value != 100000008 && aCustomerTypeCode.Value != 100000009 && aCustomerTypeCode.Value != 100000010 && aCustomerTypeCode.Value != 100000012 && aCustomerTypeCode.Value != 100000011 && aCustomerTypeCode.Value != EMPTY_VALUE )
                            //{
                            //    EffectiveNumber++;
                            //}
                            //else
                            //{ }

                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetOptionSetAttribute(ref ContactEntity, "customertypecode", 100000000);

                            this.m_ToolUtilityClass.UpdateEntity(ref ContactEntity);
                        }
                        #endregion
                    }
                    else
                    { //String StateCode = "非使用中";
                    }
                }
            }
            #endregion

            return EffectiveNumber;
        }

        #endregion
        #region 更新個人紀錄:手機、家裡電話、地址、設定委身類型
        private void UpdateContactInfomation(Guid aListEntityId, ref MemberInfomation aMemberInfomation, ref Entity aContactEntity)
        {
            bool ModifyFlag = false;
            #region // 更新個人資料:手機、家裡電話、地址、設定委身類型
            // 組員的手機
            String aMobilePhone = "";
            if (aContactEntity.Attributes.Contains("mobilephone"))
            {
                aMobilePhone = (string)aContactEntity.Attributes["mobilephone"];
                aMobilePhone = DigitsOnly.Replace(aMobilePhone, "");

                String aMemberInfomationPhone = DigitsOnly.Replace(aMemberInfomation.Phone, "");

                if (aMemberInfomationPhone != aMobilePhone)
                {
                    // 系統裡的聯絡人手機跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "mobilephone", aMemberInfomation.Phone);
                    ModifyFlag = true;
                }
            }
            // 組員的家裡電話
            String aHomePhone = "";
            if (aContactEntity.Attributes.Contains("telephone2"))
            {
                aHomePhone = (string)aContactEntity.Attributes["telephone2"];
                aHomePhone = DigitsOnly.Replace(aHomePhone, "");
                String aMemberInfomationHomePhone = DigitsOnly.Replace(aMemberInfomation.HomePhone, "");

                if (aMemberInfomationHomePhone != aHomePhone)
                {
                    // 系統裡的聯絡人家裡電話跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "telephone2", aMemberInfomation.HomePhone);
                    ModifyFlag = true;
                }
            }

            // 組員的地址
            String aAddress = "";
            if (aContactEntity.Attributes.Contains("address2_line1"))
            {
                aAddress = (string)aContactEntity.Attributes["address2_line1"];
                if (aMemberInfomation.Address != aAddress)
                {
                    // 系統裡的聯絡人家裡電話跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "address2_line1", aMemberInfomation.Address);
                    ModifyFlag = true;
                }
            }
            #endregion

            // 經由最近8週的出席次數計算、設定委身類型
            SetIdentity(aListEntityId, ref aContactEntity, ref aMemberInfomation);

            if (ModifyFlag == true)
            {
                // 更新聯絡人
                this.m_ToolUtilityClass.UpdateEntity(ref aContactEntity);
            }

        }
        private void UpdateContactInfomation(Guid aListEntityId, MemberInfomation aMemberInfomation, ref Entity aContactEntity)
        {
            bool ModifyFlag = false;
            #region // 更新個人資料:手機、家裡電話、地址、設定委身類型
            // 組員的手機
            String aMobilePhone = "";
            if (aContactEntity.Attributes.Contains("mobilephone"))
            {
                aMobilePhone = (string)aContactEntity.Attributes["mobilephone"];
                aMobilePhone = DigitsOnly.Replace(aMobilePhone, "");

                String aMemberInfomationPhone = DigitsOnly.Replace(aMemberInfomation.Phone, "");

                if (aMemberInfomationPhone != aMobilePhone)
                {
                    // 系統裡的聯絡人手機跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "mobilephone", aMemberInfomation.Phone);
                    ModifyFlag = true;
                }
            }
            else
            {
                if (aMemberInfomation.Phone != "")
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "mobilephone", aMemberInfomation.Phone);
                    ModifyFlag = true;
                }
            }



            // 組員的家裡電話
            String aHomePhone = "";
            if (aContactEntity.Attributes.Contains("telephone2"))
            {
                aHomePhone = (string)aContactEntity.Attributes["telephone2"];
                aHomePhone = DigitsOnly.Replace(aHomePhone, "");
                String aMemberInfomationHomePhone = DigitsOnly.Replace(aMemberInfomation.HomePhone, "");

                if (aMemberInfomationHomePhone != aHomePhone)
                {
                    // 系統裡的聯絡人家裡電話跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "telephone2", aMemberInfomation.HomePhone);
                    ModifyFlag = true;
                }
            }
            else
            {
                if (aMemberInfomation.HomePhone != "")
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "telephone2", aMemberInfomation.HomePhone);
                    ModifyFlag = true;
                }
            }

            // 組員的地址
            String aAddress = "";
            if (aContactEntity.Attributes.Contains("address2_line1"))
            {
                aAddress = (string)aContactEntity.Attributes["address2_line1"];
                if (aMemberInfomation.Address != aAddress)
                {
                    // 系統裡的聯絡人地址跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "address2_line1", aMemberInfomation.Address);
                    ModifyFlag = true;
                }
            }
            else
            {
                if (aMemberInfomation.Address != "")
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "address2_line1", aMemberInfomation.Address);
                    ModifyFlag = true;
                }
            }

            #endregion

            // 設定委身類型
            SetIdentity(aListEntityId, ref aContactEntity, ref aMemberInfomation);

            if (ModifyFlag == true)
            {
                // 更新聯絡人
                this.m_ToolUtilityClass.UpdateEntity(ref aContactEntity);
            }

        }

        #endregion
        #region 更新個人聚會與靈修記錄


        #region 更新出席紀錄
        private void UpdatePresentRecord(ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber)
        {
            try
            { 
                #region 取得跟週報相關的靈修紀錄
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", aWeeklyReportId.ToString(), "new_group_present_weekly_report_prese", "new_present_record");
                #endregion
                #region 更新每個靈修紀錄
                ValidSundayMemberNumber = 0;
                ValidSmallGroupMemberNumber = 0;
                foreach (MemberInfomation aMemberInfomation in this.m_GroupNamedListMemberInfomation)
                {
                    Entity aMachedPresentRecordEntity = SearchPresentRecordByName(aMemberInfomation.Name, ref PresentRecordCollection);
                    if (aMachedPresentRecordEntity != null)
                    {
                        // 有找到靈修紀錄
                        // 更新個人資料:手機、家裡電話、地址、設定委身類型
                        // 更新個人聚會與靈修記錄

                        #region 更新個人資料:手機、家裡電話、地址、委身類型
                        EntityReference aFullNameEntityReference = new EntityReference();
                        if (aMachedPresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
                        {
                            aFullNameEntityReference = (EntityReference)aMachedPresentRecordEntity.Attributes["new_contact_new_present_record"];
                        }
                        Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);

                        // 更新個人資料:手機、家裡電話、地址、設定委身類型
                        // 但是不知何故，更新連絡人之後，委身類型卻會"自動變成" 新朋友，所以就先用一個可以受影響的Entity aToUpdateContactEntity，去更新連絡人
                        Entity aToUpdateContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);
                        UpdateContactInfomation(aListEntity.Id, aMemberInfomation, ref aToUpdateContactEntity);

                        #endregion

                        #region 設定及更新個人聚會與靈修記錄
                        // 是否符合累積出席率可以貢獻出席的的委身類型，並且順便取的委身類型
                        String ClearIdentity = "";
                        bool AccumulateFlag = this.IsValidMember(aMachedPresentRecordEntity, ref ClearIdentity);

                        #region 設定主日出席
                        if (aMemberInfomation.SundayPresent == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_sunday_present_this_week", 1);

                            AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, true);

                            aWeeklySundayNumber += 1;
                        }
                        else
                        {
                            AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, false);
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_sunday_present_this_week", 0);
                        }
                        #endregion
                        #region 設定主日出席率
                        if (aMemberInfomation.SundayPresent == true)
                        {
                            if (ValidNumber != 0)
                            {
                                //this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_sunday_rate", 1 / ValidNumber);

                                if (AccumulateFlag == true)
                                {
                                    ValidSundayMemberNumber += 1;

                                    aWeeklySundayRate += 1 / ValidNumber;
                                }
                            }
                        }
                        else
                        {
                            //this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_sunday_rate", 0.0);
                        }
                        #endregion
                        #region 設定小組出席
                        if (aMemberInfomation.SmallGroupPresent == true)
                        {
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_group_present_this_week", 1);
                            AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, true);
                            aWeeklySmallGroupNumber += 1;
                        }
                        else
                        {
                            AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, false);
                            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_group_present_this_week", 0);
                        }
                        #endregion
                        #region 設定小組出席率
                        if (aMemberInfomation.SmallGroupPresent == true)
                        {
                            if (ValidNumber != 0)
                            {
                                //this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_small_group_rate", 1 / ValidNumber);

                                if (AccumulateFlag == true)
                                {
                                    ValidSmallGroupMemberNumber += 1;
                                    aWeeklySmallGroupRate += 1 / ValidNumber;
                                }
                            }
                        }
                        else
                        {
                            //this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_small_group_rate", 0);
                        }
                        #endregion
                        #region 設定附註或是代禱事項
                        // 楊梅靈糧堂
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_name", aMemberInfomation.Note);
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMemberInfomation.Note);
                    
                        // 內壢得勝靈糧堂
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_memo", aMemberInfomation.Note);
                        #endregion
                        #region 內壢得勝靈糧堂的欄位
                        #region// 靈修次數
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_general_care", aMemberInfomation.PrayNumber);
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_spiritual_work", aMemberInfomation.SpiritNumber);
                        //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_morning_pray", aMemberInfomation.FamilyNumber);
                        //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_evening_pray", aMemberInfomation.WorkAndCampusNumber);
                        #endregion

                        #region// 牧養狀態
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_shepherd_situation", aMemberInfomation.ShepherdStatus);
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_onebyone_situation", aMemberInfomation.OneOnOne);
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_training_system", aMemberInfomation.Training);
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_equipment_class", aMemberInfomation.Incubate);
                        #endregion

                        #region// 新人跟進

                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMemberInfomation.FollowUpWeek));
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMemberInfomation.FollowUpResult));
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMemberInfomation.FollowUpNextStep));
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_follow_up", aMemberInfomation.FollowUp);

                        // 因為之前APP無法直接把代禱事項和新人跟進關懷用在表單中
                        // 但是網頁現在可以了
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMemberInfomation.FollowUpNote);
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMemberInfomation.Note);

                        AddToDictionaryFollowByIdentity(ref ClearIdentity, ref aContactEntity, aMemberInfomation);

                        #endregion

                        #endregion
                        #region 更新個人聚會與靈修記錄
                        this.m_ToolUtilityClass.UpdateEntity(ref aMachedPresentRecordEntity);
                        #endregion

                        #endregion

                    }
                    else
                    {
                        // 沒找到靈修紀錄
                        CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber);
                    }
                }

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }

}

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

        private int ConvertFollowUpWeekPickerToIndex(String FollowUpWeek)
        {
            switch (FollowUpWeek)
            {
                case "一":
                    return 100000000;
                case "二":
                    return 100000001;
                case "三":
                    return 100000002;
                case "四":
                    return 100000003;
                case "五":
                    return 100000004;
                case "六":
                    return 100000005;
                case "七":
                    return 100000006;
                case "八":
                    return 100000007;
                case "九":
                    return 100000008;
                case "十":
                    return 100000009;
                case "十一":
                    return 100000010;
                case "十二":
                    return 100000011;
                case "十三":
                    return 100000012;
                case "十四":
                    return 100000013;
                case "十五":
                    return 100000014;
                case "十六":
                    return 100000015;
                case "十七":
                    return 100000016;
                case "十八":
                    return 100000017;
                case "十九":
                    return 100000018;
                case "二十":
                    return 100000019;
                default:
                    return 100000008;
            }
        }

        private int ConvertFollowUpResultPickerToIndex(String FollowUpResult)
        {
            switch (FollowUpResult)
            {
                case "請選擇":
                    return 100000000;
                case "熱情回應":
                    return 100000001;
                case "渴慕認識信仰":
                    return 100000002;
                case "沒聯絡上":
                    return 100000003;
                case "反應冷淡":
                    return 100000004;
                case "考慮中":
                    return 100000005;
                case "入小組":
                    return 100000006;
                case "來主日":
                    return 100000007;
                case "轉介":
                    return 100000008;
                case "其他":
                    return 100000009;
                default:
                    return 100000000;
            }
        }
        private int ConvertFollowUpNextStepPickerToIndex(String FollowUpNextStep)
        {
            switch (FollowUpNextStep)
            {
                case "請選擇":
                    return 100000000;
                case "繼續跟進":
                    return 100000001;
                case "轉介":
                    return 100000002;
                default:
                    return 100000000;
            }
        }


        private Entity SearchPresentRecordByName(String Name, ref EntityCollection PresentRecordCollection)
        {
            foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
            {
                String aPresentRecordName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_contact_new_present_record");
                if (Name == aPresentRecordName)
                {
                    return PresentRecordEntity;
                }
            }

            return null;
        }


        public Double GetValidMemberNumber(Guid aWeeklyReportId)
        {
            try
            {
                #region 取得跟週報相關的靈修紀錄
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", aWeeklyReportId.ToString(), "new_group_present_weekly_report_prese", "new_present_record");
                #endregion

                Double ValidMemberNumber = 0;
                #region// 處理每個個人聚會與靈修記錄
                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
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

                                //// 如果是新朋友、未入組、外教會則不列入累積，楊梅靈糧堂
                                if (aCustomerTypeCode.Value != 100000004 && aCustomerTypeCode.Value != 100000000 && aCustomerTypeCode.Value != 100000007)
                                {
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }

                                // 如果是新朋友或是未入組則不列入累積，內壢得勝靈糧堂
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

                // 如果是新朋友或是未入組則不列入累積，楊梅靈糧堂
                if (aCustomerTypeCodeValue != 100000004 && aCustomerTypeCodeValue != 100000000 && aCustomerTypeCodeValue != 100000007 && aCustomerTypeCodeValue != EMPTY_VALUE)
                {
                    return true;
                }
                else
                {
                    return false;
                }


                // 如果是新朋友或是未入組則不列入累積，內壢得勝靈糧堂
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
        #endregion

        #region 設定委身類型

        public void SetIdentity(Guid aListEntityId, ref Entity aContact, ref MemberInfomation aMemberInfomation)
        {
            try
            {
                // 先找到委身類型
                int aIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "customertypecode");

                String aIdentityType = ConvertIndexToIdentity(aIdentity);

                if (aIdentityType == "7. 未入組" || aIdentityType == "8. 新朋友")
                {
                    // 如果委身型態是"未入組"或是"新朋友"
                    // 先搜尋過去2個月的靈修出席紀錄
                    // 如果主日次數+小組次數 大於等於 8 次，則委身類型設定為"小組組員"
                    if (PassOrFail(aListEntityId, ref aContact) == true)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 1);
                        // 更新連絡人
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact);
                        }
                    }
                }
                else if (aIdentityType == "06. 小組組員")
                {
                    // 如果主日次數+小組次數 小於 8 次，則委身類型設定為"未入組"
                    if (PassOrFail(aListEntityId, ref aContact) == false)
                    {
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);
                        // 更新連絡人
                        //this.m_ToolUtilityClass.UpdateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact);
                    }
                }
                else { }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public int GetPresentNumber(Guid WeeklyReportId, String Type, ref Entity aContact)
        {
            try
            {
                // 過去幾週的靈修出席紀錄
                EntityCollection PresentRecordCollection = this.m_ToolUtilityClass.QueryPresentRecordByContactIdAndSunday(WeeklyReportId, aContact.Id, WEEK_PERIOD);

                int TotalNumber = 0;

                if (Type == "主日")
                {
                    foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                    {
                        // 主日次數
                        TotalNumber += this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_sunday_present_this_week");
                    }

                    return TotalNumber;

                }
                else if (Type == "小組")
                {
                    foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                    {
                        // 小組次數
                        TotalNumber += this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_group_present_this_week");
                    }

                    return TotalNumber;


                }

                return TotalNumber;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public bool PassOrFail(Guid aListEntityId, ref Entity aContact)
        {
            try
            {
                int TotalNumber = GetPresentNumber(aListEntityId, "小組", ref aContact);

                // 如果主日次數+小組次數 大於等於 MINIMUM_THRESHOLD 次，則委身類型設定為"小組組員"
                if (TotalNumber >= MINIMUM_THRESHOLD)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        private String ConvertIndexToIdentity(int Identity)
        {
            switch (Identity)
            {
                case 100000000:
                    return "8. 新朋友";
                case 100000001:
                    return "5. 神學生";
                case 100000002:
                    return "4. 小組長";
                case 100000003:
                    return "3. 全職同工";
                case 100000004:
                    return "7. 未入組";
                case 100000005:
                    return "1. 牧師";
                case 100000006:
                    return "2, 師母";
                case 100000007:
                    return "9. 外教會";
                case 100000008:
                    return "10. 未入組結案";
                case 1:
                    return "6. 小組組員";
                default:
                    return ".";
            }
        }

        #endregion

        #region 字典處理函式庫
        private void InitializeDictionary(DateTime aSunday)
        {
            try
            {
                AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", aSunday.ToLocalTime().ToShortDateString() + "主日出席紀錄(過去八週主日出席次數)" + Environment.NewLine);
                AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭", aSunday.ToLocalTime().ToShortDateString() + "小組出席紀錄(過去八週主日出席次數)" + Environment.NewLine);
                AddToDictionary(ref this.m_FeedBackReport, "跟進統計表頭", aSunday.ToLocalTime().ToShortDateString() + "跟進統計報告" + Environment.NewLine);

                AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員未出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出未席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "主日統計新人出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "主日統計新人未出席字串", "");

                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員未出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出未席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人未出席字串", "");

                AddToDictionary(ref this.m_FeedBackReport, "未入組跟進統計內容", "");
                AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進統計內容", "");
                //AddToDictionary(ref this.m_FeedBackReport, "未入組跟進統計內容", "未入組跟進" + Environment.NewLine);
                //AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進統計內容", "新朋友跟進" + Environment.NewLine);

                //AddToDictionary(ref this.m_FeedBackReport, "主日統計", "");
                //AddToDictionary(ref this.m_FeedBackReport, "小組統計", "");
                //AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進", "");

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
        private void ResetDictionary(DateTime aSunday)
        {
            try
            {
                AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭", aSunday.ToLocalTime().ToShortDateString() + "出席紀錄(過去八週主日出席次數)" + Environment.NewLine);
                AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭", "小組出席紀錄:");
                AddToDictionary(ref this.m_FeedBackReport, "跟進統計表頭", "跟進統計報告" + Environment.NewLine);

                AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員未出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出未席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "主日統計新人出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "主日統計新人未出席字串", "");

                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員未出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出未席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人未出席字串", "");

                AddToDictionary(ref this.m_FeedBackReport, "未入組跟進統計內容", "");
                AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進統計內容", "");
                //AddToDictionary(ref this.m_FeedBackReport, "未入組跟進統計內容", "未入組跟進" + Environment.NewLine);
                //AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進統計內容", "新朋友跟進" + Environment.NewLine);

                AddToDictionary(ref this.m_FeedBackReport, "主日統計", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計", "");
                AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進", "");

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
        private void AddToDictionaryByIdentity(Guid aListEntityId, String Type, ref String Identity, ref Entity aContact, bool Presentflag)
        {
            try
            {
                String ContactName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                switch (Identity)
                {
                    case "小組組員":
                        if (Type == "主日")
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員出席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + ") ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員未出席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + ") ");
                            }
                        }
                        else
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員出席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + ") ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員未出席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + ") ");
                            }
                        }

                        return;
                    case "未入組":

                        if (Type == "主日")
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + ") ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出未席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + ") ");
                            }
                        }
                        else
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + ") ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出未席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + ") ");
                            }
                        }

                        return;
                    case "新朋友":

                        if (Type == "主日")
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計新人出席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + ") ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計新人未出席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + ") ");
                            }
                        }
                        else
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人出席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + ") ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人未出席字串", ContactName + "(" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + ") ");
                            }
                        }

                        return;
                    default:
                        return;
                }
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
        private void AddToDictionaryFollowByIdentity(ref String Identity, ref Entity aContact, MemberInfomation aMemberInfomation)
        {
            try
            {
                String ContactName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                String PersonalFollowUp = SetFollowUpString(ref aMemberInfomation);

                String FollowUp = "";
                if (PersonalFollowUp != "")
                {
                    FollowUp = "\t\t" + ContactName + Environment.NewLine + PersonalFollowUp + Environment.NewLine;
                }
                else
                {
                    FollowUp = "\t\t" + ContactName + " : 沒有跟進活動" + Environment.NewLine;
                }

                switch (Identity)
                {
                    case "未入組":
                        AddToDictionary(ref this.m_FeedBackReport, "未入組跟進統計內容", FollowUp);

                        return;
                    case "新朋友":

                        AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進統計內容", FollowUp);

                        return;
                    default:
                        return;
                }
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
        private String SetFollowUpString(ref MemberInfomation aMemberInfomation)
        {
            try
            {
                return
                    AppendHeadString("\t\t\t跟進週次:", aMemberInfomation.FollowUpWeek) +
                    AppendHeadString("\t\t\t跟進方式:", aMemberInfomation.FollowUp) +
                    AppendHeadString("\t\t\t跟進結果:", aMemberInfomation.FollowUpResult) +
                    AppendHeadString("\t\t\t下一步驟:", aMemberInfomation.FollowUpNextStep) +
                    AppendHeadString("\t\t\t跟進摘要:", aMemberInfomation.FollowUpNote)
                    ;
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }
        private String AppendHeadString(String HeadString, String BodyString)
        {
            try
            {
                if (BodyString != "" && BodyString != "." && BodyString != "請選擇")
                {
                    return HeadString + BodyString + Environment.NewLine;
                }
                else
                {
                    return "";
                }
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
        private String GetDictionaryValue(ref Dictionary<String, String> aDictionary, String HeadString, String Method)
        {
            try
            {
                if (aDictionary[Method] != "")
                {
                    return HeadString + aDictionary[Method] + Environment.NewLine;
                }
                else
                {
                    return "";
                }
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
