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
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();

        bool m_SetIdentityFlag = false;
        #endregion

        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365";

        private const bool TRANSFER_IDENTITY_FLAG = false;

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

        //使用者 Client端傳回來的資料
        MemberInfomationPackage m_MemberInfomationPackage = new MemberInfomationPackage();
        //沒有週報，需要初始化的Member Data
        MemberInfomationPackage m_InitializedMemberInfomationPackage = new MemberInfomationPackage();

        DateTime m_Sunday;
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給族系族長/區長的名單

        Guid m_DecipleGroupListId;
        //Guid m_GroupLeaderId; // 小組長
        Guid m_RaceLeaderId; // 族系族長/區長
        Guid m_ShepherdLeaderId; // 區牧
        String m_SmallGroupPlace;
        String m_SmallGroupTime;

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
        //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        //List<Place2> m_GroupNamePlaces = new List<Place2>(); // 依據群組名稱過濾出來的會眾集合
        List<MemberInfomation> m_GroupNamedListMemberInfomation = new List<MemberInfomation>(); // 依據群組名稱過濾出來的會眾集合
        #endregion
        #region 上傳資料區
        #region WCF Service端
        #region 主程式
        public List<GroupWeeklyReportGuid> UploadMemberDataPackage(AccountPasswordData aAccountPasswordData, DateTime aSunday, String UploadCategory, MemberInfomationPackage aMemberInfomationPackage)
        {
            try
            {
                // 設定初始值
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "設定初始值");
                this.m_MemberInfomationPackage = aMemberInfomationPackage;

                // 設定參數，設定主日日期，找到操作使用者登入的ENTITY及ID
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "設定參數");
                SetupCommonParameter(aAccountPasswordData, aSunday);

                // 初始化報告字串
                //this.m_FeedBackReport.Clear();
                //this.ResetDictionary( aSunday );

                Entity aGraceLeaderWeeklyReportEntity = null;// 族系族長的週報

                // 一個一個上傳的週報
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

                    //台北基督之家小組名稱含有數字
                    //String FilteredOutDigitGroupName = Regex.Replace(FilteredGroupName, "[0-9]", "");//過濾掉數字
                    String FilteredOutDigitGroupName = FilteredGroupName.Replace(" ", ""); // //過濾掉空白
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

                        // this.m_ContactId 的意思是登入者在系統裡的ID，登入者是"小家長"、"小組長"、族系族長/區長"，或是個人回報
                        if (this.m_ContactId == aThisListSmallGroupLeaderId || this.m_ContactId == aThisListFamilyHeadId || this.m_ContactId == aThisListGraceLeaderId || aMemberInfomationPackage.m_LoginType == "個人回報")
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

                            if (aWeeklyReportId == Guid.Empty)
                            {
                                #region // 要建立週報
                                #region // 依據有效的週報的小組組員名單當作週報出席率的分母
                                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "依據有效的週報的小組組員名單當作週報出席率的分母");
                                Double ValidNumber = this.GetEffecttiveSmallGroupNumber(aListEntity.Id);
                                #endregion
                                #region// 要建立週報
                                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "要建立週報");
                                if (this.CreateWeeklyReportOrNot(ref aListEntity, aSunday)) // 判斷是否真要建立週報
                                {
                                    // 建立週報
                                    Double aWeeklySundayRate = 0.0;
                                    Double aWeeklySmallGroupRate = 0.0;
                                    int aWeeklySundayNumber = 0;
                                    int aWeeklySmallGroupNumber = 0;

                                    aGraceLeaderWeeklyReportEntity = CreateWeeklyReportAndPresentRecord(GroupName, aGroupWeeklyReportGuid, ref aListEntity, UploadCategory, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber);
                                }
                                #endregion
                                #endregion
                            }
                            else
                            {
                                #region// 更新週報
                                aGraceLeaderWeeklyReportEntity = UpdateWeeklyReportProcess(aGroupWeeklyReportGuid, ref aListEntity, ref aWeeklyReportId);
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
                #region 先根據日期尋找當週主日日期
                // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                int DayOfWeek = (int)aSunday.DayOfWeek;
                this.m_Sunday = aSunday.AddDays(-DayOfWeek);
                #endregion

                // 找到操作使用者登入的小組長ID
                if (aAccountPasswordData.Account != "LineIdLogin")
                {
                    this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(aAccountPasswordData.Account, aAccountPasswordData.Password);
                }
                else
                {
                    this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(aAccountPasswordData.Password);
                }

                m_ContactId = m_ContactEntity.Id;

                #region 蒐集建立週報所需要的屬性
                // 根據是否是族系族長還是小組長會設定不同的要上傳的名單集合
                // 並且該名單是有勾選APP點名的才被允許進來
                this.FindListCollection();

                if (m_Lists.Entities.Count == 0)
                {
                    // 沒找到任何要點名的名單，所以是個人回報
                    #region 取得個人回報的名單
                    this.m_Lists = this.m_ToolUtilityClass.QueryListOfContactManyToMany(this.m_ContactEntity.Id);
                    #endregion
                }

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
                // this.m_Lists = 需要點名的名單

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
                // this.m_Lists = 需要點名的名單
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
        private bool CreateWeeklyReportOrNot(ref Entity aListEntity, DateTime aSunday)
        {
            try
            {
                // 待完成事項，若是名單的相關週報已經有該週主日的週報則不建立

                //public EntityCollection QueryContactWeeklyReportBySunday(DateTime aSunday, String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)

                EntityCollection aWeeklyReportCollection = this.m_ToolUtilityClass.QueryWeeklyReportBySunday(aSunday, "list", "listid", aListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");

                foreach (Entity aWeeklyReport in aWeeklyReportCollection.Entities)
                {
                    DateTime WeeklyReportSunday = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aWeeklyReport, "new_sunday_date");

                    if (aSunday.Date == WeeklyReportSunday.Date)
                    {
                        // 已經有相同主日的週報了
                        return false;
                    }
                }
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
        #endregion
        #region 建立的週報
        //private void UpdateWeeklyReport(GroupWeeklyReportGuid aGroupWeeklyReportGuid, Guid aWeeklyReportId, ref Double ValidNumber, ref Entity aListEntity, String UploadCategory, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, bool CalculateFlag)
        //{
        //    try
        //    {
        //        #region // 依據之前點名的靈修紀錄的有效組員當作週報出席率的分母


        //        #region// 更新個人資料:手機、家裡電話、地址、設定委身類型
        //        // 更新個人聚會與靈修記錄
        //        UpdatePresentRecord(this.m_GroupNamedListMemberInfomation, aPresentRecordCollection, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber);
        //        #endregion

        //        #region// 更新週報

        //        return SetWeeklyReportRelatedInfo(ValidSundayMemberNumber, ValidSmallGroupMemberNumber, ValidNumber, aGroupWeeklyReportGuid, aWeeklyReportId, UploadCategory, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber);

        //        #endregion

        //        // 傳回建立的週報

        //    }
        //    catch (System.Exception Exception)
        //    {
        //        String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
        //        this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

        //        throw Exception;
        //    }
        //}
        private Entity ToUpdatePresentRecord(GroupWeeklyReportGuid aGroupWeeklyReportGuid, Guid aWeeklyReportId, ref Entity aListEntity, String UploadCategory, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, bool CalculateFlag)
        {
            try
            {
                #region // 依據之前點名的靈修紀錄的有效組員當作週報出席率的分母
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "依據之前點名的靈修紀錄的有效組員當作週報出席率的分母");


                // 已經有週報所以從靈修出席紀錄單計算出席率的分母
                #region 取得跟週報相關的靈修紀錄單
                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", aWeeklyReportId.ToString(), "new_group_present_weekly_report_prese", "new_present_record");
                #endregion

                Double ValidNumber = GetValidMemberNumber(aPresentRecordCollection);
                #endregion

                #region// 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 更新個人聚會與靈修記錄
                int ValidSundayMemberNumber = 0;
                int ValidSmallGroupMemberNumber = 0;
                UpdatePresentRecord(this.m_GroupNamedListMemberInfomation, aPresentRecordCollection, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid);
                #endregion

                #region// 更新週報

                return SetWeeklyReportRelatedInfo(ValidSundayMemberNumber, ValidSmallGroupMemberNumber, ValidNumber, aGroupWeeklyReportGuid, aWeeklyReportId, UploadCategory, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber);

                #endregion

                // 傳回建立的週報

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        //private Entity UpdateWeeklyReport(GroupWeeklyReportGuid aGroupWeeklyReportGuid, Guid aWeeklyReportId, ref Entity aListEntity, String UploadCategory, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, bool CalculateFlag)
        //{
        //    try
        //    {

        //        #region// 更新週報

        //        // 取得週報
        //        Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aWeeklyReportId);

        //        return SetWeeklyReportRelatedInfo(ValidSundayMemberNumber, ValidSmallGroupMemberNumber, ValidNumber, aGroupWeeklyReportGuid, aWeeklyReportId, UploadCategory, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber);

        //        #endregion

        //        // 傳回建立的週報

        //    }
        //    catch (System.Exception Exception)
        //    {
        //        String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
        //        this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

        //        throw Exception;
        //    }
        //}
        private void ToUpdatePersonalWeeklyReport(GroupWeeklyReportGuid aGroupWeeklyReportGuid, Guid aWeeklyReportId, ref Entity aListEntity, String UploadCategory, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, bool CalculateFlag)
        {
            try
            {
                #region// 更新週報

                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 更新個人聚會與靈修記錄
                int ValidSundayMemberNumber = 0;
                int ValidSmallGroupMemberNumber = 0;
                //UpdatePresentRecord(this.m_GroupNamedListMemberInfomation, ref aListEntity, ref aWeeklyReportId, 0, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber);
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private Entity SetWeeklyReportRelatedInfo(int ValidSundayMemberNumber, int ValidSmallGroupMemberNumber, Double ValidNumber, GroupWeeklyReportGuid aGroupWeeklyReportGuid, Guid aWeeklyReportId, String UploadCategory, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber)
        {
            try
            {
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
        private Guid CreateWeeklyReport(ref Entity aListEntity, GroupWeeklyReportGuid aGroupWeeklyReportGuid)
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

                // 區長 ID
                Guid RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                // 上代組長/(原來是:族系族長) ID
                Guid ShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");

                // 區名
                //String AreaName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_area_name");

                // 設定週報相關屬性
                this.SetupWeeklyReortEntityAttributes(ref aWeeklyReportEntity, FamilyLeaderId, GroupLeaderId, RaceLeaderId, ShepherdLeaderId, m_DecipleGroupListId, aListEntity, m_Sunday, m_SmallGroupPlace, m_SmallGroupTime, aGroupWeeklyReportGuid);

                // 新增週報
                return this.m_ToolUtilityClass.CreateEntity(aWeeklyReportEntity);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetupWeeklyReortEntityAttributes(ref Entity aWeeklyReportEntity, Guid aFamilyLeaderId, Guid aGroupLeaderId, Guid aRaceLeaderId, Guid aShepherdLeaderId, Guid aDecipleGroupList, Entity ListEntity, DateTime aSunday, String SmallGroupPlace, String SmallGroupTime, GroupWeeklyReportGuid aGroupWeeklyReportGuid)
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
                #region 關聯族系族長/區長屬性
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

                if (aGroupWeeklyReportGuid.SmallGroupDate.Year > 1)
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", aGroupWeeklyReportGuid.SmallGroupDate);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", DateTime.Now);

                }

                #endregion
                #region 設定小組聚會地點和時間
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_place", SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_time", SmallGroupTime);
                #endregion
                #region 設定小組人數

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aWeeklyReportEntity, "new_small_group_member_number", GetSmallGroupMemberNumber(ListEntity.Id));

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
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            #endregion

            return MemberCollection.Entities.Count;

            #endregion
        }



        private Entity CreateWeeklyReportAndPresentRecord(String GroupName, GroupWeeklyReportGuid aGroupWeeklyReportGuid, ref Entity aListEntity, String UploadCategory, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber)
        {
            try
            {
                if (this.m_MemberInfomationPackage.m_LoginType != "小組長")
                {
                    #region 個人回報
                    // COPY MEMBER DATA 的 Group Weekly Report 的部分
                    CopyMemberInfomationPackage(this.m_MemberInfomationPackage, this.m_InitializedMemberInfomationPackage);

                    // 初始化 Member Data 清單
                    InitialSmallGroupLeaderMemberData(GroupName, aListEntity.Id);

                    // 根據使用者上傳的點名資料，更新初始化 Member Data 清單
                    this.UpdateMemberDataFromUploadData();
                    #endregion
                }

                // 建立週報
                Guid aCreatedWeeklyReportId = CreateWeeklyReport(ref aListEntity, aGroupWeeklyReportGuid);

                // 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 建立的個人聚會與靈修記錄
                // 同時整理並取得:主日出席回報、小組出席回報、新人跟進字串，因為這樣就可以一魚兩吃，比較有效能一點
                int ValidSundayMemberNumber = 0;
                int ValidSmallGroupMemberNumber = 0;
                EntityCollection aPresentRecordCollection;
                if (this.m_MemberInfomationPackage.m_LoginType == "小組長")
                {
                    // 小組長回報
                    aPresentRecordCollection = CreatePresentRecordList(this.m_MemberInfomationPackage, GroupName, ref aListEntity, ref aCreatedWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber, ref aGroupWeeklyReportGuid);
                }
                else
                {
                    // 個人回報
                    aPresentRecordCollection = CreatePresentRecordList(this.m_InitializedMemberInfomationPackage, GroupName, ref aListEntity, ref aCreatedWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref ValidSundayMemberNumber, ref ValidSmallGroupMemberNumber, ref aGroupWeeklyReportGuid);
                }

                return UpdateWeeklyReport(aGroupWeeklyReportGuid, aPresentRecordCollection, ref aListEntity, ref aCreatedWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void InitialSmallGroupLeaderMemberData(String GroupName, Guid ListEntityId)
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
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            #endregion

            #region 針對每一個連絡人，建立 Member Data
            this.m_InitializedMemberInfomationPackage.ListMemberInfomation = new List<MemberInfomation>();
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

                if (ContactEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;

                    if (aOptionState.Value == 0)
                    {
                        #region 只回傳使用中的組員

                        #region // 組員基本資料
                        // 組員的全名
                        String FullName = "";
                        if (ContactEntity.Attributes.Contains("fullname"))
                        {
                            FullName = (string)ContactEntity.Attributes["fullname"];
                        }
                        // 組員的手機
                        String aMobilePhone = "";
                        if (ContactEntity.Attributes.Contains("mobilephone"))
                        {
                            aMobilePhone = (string)ContactEntity.Attributes["mobilephone"];
                        }
                        // 組員的家裡電話
                        String aHomePhone = "";
                        if (ContactEntity.Attributes.Contains("telephone2"))
                        {
                            aHomePhone = (string)ContactEntity.Attributes["telephone2"];
                        }
                        // 組員的地址
                        String aAddress = "";
                        if (ContactEntity.Attributes.Contains("address2_line1"))
                        {
                            aAddress = (string)ContactEntity.Attributes["address2_line1"];
                        }

                        // 組員的生日
                        //DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref ContactEntity, "birthdate").ToLocalTime();

                        // 組員的職業及專長
                        String aIndustry = "";
                        if (ContactEntity.Attributes.Contains("new_industry"))
                        {
                            aIndustry = (string)ContactEntity.Attributes["new_industry"];
                        }
                        #endregion
                        #region// 委身類型
                        String aIdentity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref ContactEntity, "customertypecode"));

                        //aIdentity = Regex.Replace(aIdentity, "[0-9]", "");//過濾掉數字
                        //aIdentity = aIdentity.Replace(" ", ""); // //過濾掉空白
                        //aIdentity = aIdentity.Replace(".", ""); // //過濾掉逗號

                        #endregion

                        // 取得新人跟進週次，及跟進歷程記錄
                        String aFollowUpWeek = "";
                        String aNewComerNote = GetNewComerFollowupInfo(ContactEntity.Id, ref aFollowUpWeek);

                        MemberInfomation aMemberInfomation = new MemberInfomation()
                        {
                            Group = GroupName,
                            Name = FullName,
                            Identity = aIdentity,
                            Phone = DigitsOnly.Replace(aMobilePhone, ""),
                            HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                            Address = aAddress,
                            Industry = aIndustry,
                            Note = "",
                            Date = "2015/10/6",
                            Number = 5,
                            SundayPresent = false,
                            SmallGroupPresent = false,

                            PrayNumber = 0,
                            SpiritNumber = 0,
                            FamilyNumber = 0,
                            WorkAndCampusNumber = 0,
                            ShepherdStatus = "",
                            OneOnOne = "",
                            Training = "",
                            Incubate = "",
                            FollowUpWeek = aFollowUpWeek,
                            FollowUpResult = ".",
                            FollowUpNextStep = ".",
                            FollowUpOption = "",
                            FollowUp = "",
                            FollowUpNote = "",
                            NewComerNote = aNewComerNote,
                            #region 靈修、晨、晚禱
                            SpiritualWork = 0,
                            MorningPray = 0,
                            GeneralCare = 0,
                            #endregion

                        };

                        if (aIdentity != "10.未入組結案")
                        {
                            // "10.未入組結案" 不用進入 APP
                            this.m_InitializedMemberInfomationPackage.ListMemberInfomation.Add(aMemberInfomation);
                        }

                        #endregion
                    }
                    else
                    { //String StateCode = "非使用中";
                    }
                }
            }
            #endregion

            #endregion

            return;
        }
        private void UpdateMemberDataFromUploadData()
        {
            try
            {
                foreach (MemberInfomation aUploadMemberData in this.m_MemberInfomationPackage.ListMemberInfomation)
                {
                    // 上傳的點名與系統的靈修紀錄對應尋找
                    MemberInfomation aMatchedMemberInfomation = SearchMemberDataByName(aUploadMemberData);

                    if (aMatchedMemberInfomation != null)
                    {
                        CopyMemberData(aUploadMemberData, aMatchedMemberInfomation);
                    }
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private MemberInfomation SearchMemberDataByName(MemberInfomation aUploadMemberData)
        {
            try
            {
                foreach (MemberInfomation aMemberInfomation in this.m_InitializedMemberInfomationPackage.ListMemberInfomation)
                {
                    if (aUploadMemberData.Name == aMemberInfomation.Name)
                    {
                        return aMemberInfomation;
                    }
                }
                return null;

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void CopyMemberData(MemberInfomation aFromMemberInfomation, MemberInfomation aToMemberInfomation)
        {
            try
            {
                aToMemberInfomation.Group = aFromMemberInfomation.Group;
                aToMemberInfomation.Name = aFromMemberInfomation.Name;
                aToMemberInfomation.Identity = aFromMemberInfomation.Identity;
                aToMemberInfomation.Phone = aFromMemberInfomation.Phone;
                aToMemberInfomation.HomePhone = aFromMemberInfomation.HomePhone;
                aToMemberInfomation.Address = aFromMemberInfomation.Address;
                //aToMemberInfomation.BirthDate = aFromMemberInfomation.BirthDate;
                aToMemberInfomation.Industry = aFromMemberInfomation.Industry;
                aToMemberInfomation.Note = aFromMemberInfomation.Note;
                aToMemberInfomation.Date = aFromMemberInfomation.Date;
                aToMemberInfomation.Number = aFromMemberInfomation.Number;
                aToMemberInfomation.SundayPresent = aFromMemberInfomation.SundayPresent;
                aToMemberInfomation.SmallGroupPresent = aFromMemberInfomation.SmallGroupPresent;
                aToMemberInfomation.PrayNumber = aFromMemberInfomation.PrayNumber;
                aToMemberInfomation.SpiritNumber = aFromMemberInfomation.SpiritNumber;
                aToMemberInfomation.FamilyNumber = aFromMemberInfomation.FamilyNumber;
                aToMemberInfomation.WorkAndCampusNumber = aFromMemberInfomation.WorkAndCampusNumber;
                aToMemberInfomation.ShepherdStatus = aFromMemberInfomation.ShepherdStatus;
                aToMemberInfomation.OneOnOne = aFromMemberInfomation.OneOnOne;
                aToMemberInfomation.Training = aFromMemberInfomation.Training;
                aToMemberInfomation.Incubate = aFromMemberInfomation.Incubate;
                aToMemberInfomation.FollowUpWeek = aFromMemberInfomation.FollowUpWeek;
                aToMemberInfomation.FollowUpResult = aFromMemberInfomation.FollowUpResult;
                aToMemberInfomation.FollowUpNextStep = aFromMemberInfomation.FollowUpNextStep;
                aToMemberInfomation.FollowUpOption = aFromMemberInfomation.FollowUpOption;
                aToMemberInfomation.FollowUp = aFromMemberInfomation.FollowUp;
                aToMemberInfomation.FollowUpNote = aFromMemberInfomation.FollowUpNote;
                aToMemberInfomation.NewComerNote = aFromMemberInfomation.NewComerNote;

                #region 靈修、晨、晚禱
                aToMemberInfomation.SpiritualWork = aFromMemberInfomation.SpiritualWork;
                aToMemberInfomation.MorningPray = aFromMemberInfomation.MorningPray;
                aToMemberInfomation.GeneralCare = aFromMemberInfomation.GeneralCare;
                #endregion

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private void CopyMemberInfomationPackage(MemberInfomationPackage aFromMemberInfomationPackage, MemberInfomationPackage aToMemberInfomationPackage)
        {
            try
            {
                if (aToMemberInfomationPackage.GroupWeeklyReportGuidList == null)
                {
                    aToMemberInfomationPackage.GroupWeeklyReportGuidList = new List<GroupWeeklyReportGuid>();
                }
                else
                {
                    aToMemberInfomationPackage.GroupWeeklyReportGuidList.Clear();
                }

                foreach (GroupWeeklyReportGuid aFromGroupWeeklyReportGuid in aFromMemberInfomationPackage.GroupWeeklyReportGuidList)
                {
                    GroupWeeklyReportGuid aToGroupWeeklyReportGuid = new GroupWeeklyReportGuid
                    {
                        GroupName = aFromGroupWeeklyReportGuid.GroupName,
                        SundayPresentRate = aFromGroupWeeklyReportGuid.SundayPresentRate,
                        SmallGroupDate = aFromGroupWeeklyReportGuid.SmallGroupDate,
                        SmallGroupRate = aFromGroupWeeklyReportGuid.SmallGroupRate,
                        WeeklyReportGuid = aFromGroupWeeklyReportGuid.WeeklyReportGuid
                    };

                    aToMemberInfomationPackage.GroupWeeklyReportGuidList.Add(aToGroupWeeklyReportGuid);

                }
                aToMemberInfomationPackage.WeeklyReportGuid = aFromMemberInfomationPackage.WeeklyReportGuid;

                aToMemberInfomationPackage.m_LoginType = aFromMemberInfomationPackage.m_LoginType;

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
        private EntityCollection CreatePresentRecordList(MemberInfomationPackage aMemberInfomationPackage, String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            if (aMemberInfomationPackage.m_LoginType == "小組長")
            {
                foreach (MemberInfomation aMemberInfomation in this.m_GroupNamedListMemberInfomation)
                {
                    // 更新個人資料:手機、家裡電話、地址、設定委身類型
                    // 新增個人聚會與靈修記錄
                    Entity aPresentRecord = CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid);
                    if (aPresentRecord != null)
                    {
                        PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                    }
                }
            }
            else
            {
                #region 個人回報
                //Entity aWeeklyReport = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aWeeklyReportId);

                //SetupPersonalMemberInfomationPackage(GroupName, ref aWeeklyReport, aListEntity);

                foreach (MemberInfomation aMemberInfomation in aMemberInfomationPackage.ListMemberInfomation)
                {
                    // 更新個人資料:手機、家裡電話、地址、設定委身類型
                    // 新增個人聚會與靈修記錄
                    Entity aPresentRecord = CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid);
                    if (aPresentRecord != null)
                    {
                        PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                    }
                }
                #endregion
            }

            return PresentRecordEntityCollection;
        }
        private void SetupPersonalMemberInfomationPackage(String GroupName, ref Entity GroupWeeklyReportEntity, Entity ListEntity)
        {
            try
            {
                GroupWeeklyReportGuid aGroupWeeklyReportGuid = new GroupWeeklyReportGuid();

                #region 這個點名名單沒有找到主日周報， 找點名名單的小組組員做為要點名的清單
                // 這個點名名單沒有找到主日周報， 找點名名單的小組組員做為要點名的清單
                // 回傳 APP 這是空的 Guid，表示沒找到週報
                aGroupWeeklyReportGuid.WeeklyReportGuid = Guid.Empty;

                // 回傳 APP 小組聚會日期
                aGroupWeeklyReportGuid.SmallGroupDate = new DateTime(2000, 1, 1);

                // 回傳 APP 主日出席率
                aGroupWeeklyReportGuid.SundayPresentRate = 0;
                // 回傳 APP 小組出席率
                aGroupWeeklyReportGuid.SmallGroupRate = 0;

                this.m_MemberInfomationPackage.GroupWeeklyReportGuidList.Add(aGroupWeeklyReportGuid);

                // 在 APP 中會呈現的小組名稱
                aGroupWeeklyReportGuid.GroupName = GroupName;
                GetPersonalSmallGroupLeaderMemberData(GroupName, ListEntity.Id);
                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private Entity CreatePresentRecord(MemberInfomation aMemberInfomation, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            // 必須是名單裡的人，不能是只有依據姓名更新
            Entity aContactEntity = UpdateContactInfomationFromList(aMemberInfomation.Name, aListEntity.Id);
            //Entity aContactEntity = m_ToolUtilityClass.RetrieveContactEntityByName(aMemberInfomation.Name);
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
                this.SetupPresentRecordEntityAttributes(aPresentRecord, aMemberInfomation, ref aContactEntity, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid);

                // 新增個人聚會與靈修記錄
                Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);

                //取得並回傳新建的聚會與靈修記錄
                return this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);
            }
            else
            {
                return null;
            }
        }
        private void GetPersonalSmallGroupLeaderMemberData(String GroupName, Guid ListEntityId)
        {
            #region // 處理每個小組名單
            //搜尋名單的組員
            //EntityCollection Contacts = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntityId.ToString(), "new_cell_list_contact", "contact");

            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);

            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");
            EntityCollection MemberCollection;
            if (ListType == false)
            {
                // 靜態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }

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

                if (ContactEntity.Id == this.m_ContactEntity.Id)
                {
                    // 在名單中找到登入者的ID 
                    if (ContactEntity.Attributes.Contains("statecode"))
                    {
                        OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;

                        if (aOptionState.Value == 0)
                        {
                            #region 只回傳使用中的組員

                            // 組員的全名
                            String FullName = "";
                            if (ContactEntity.Attributes.Contains("fullname"))
                            {
                                FullName = (string)ContactEntity.Attributes["fullname"];
                            }
                            // 組員的手機
                            String aMobilePhone = "";
                            if (ContactEntity.Attributes.Contains("mobilephone"))
                            {
                                aMobilePhone = (string)ContactEntity.Attributes["mobilephone"];
                            }
                            // 組員的家裡電話
                            String aHomePhone = "";
                            if (ContactEntity.Attributes.Contains("telephone2"))
                            {
                                aHomePhone = (string)ContactEntity.Attributes["telephone2"];
                            }
                            // 組員的地址
                            String aAddress = "";
                            if (ContactEntity.Attributes.Contains("address2_line1"))
                            {
                                aAddress = (string)ContactEntity.Attributes["address2_line1"];
                            }

                            // 組員的生日
                            //DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref ContactEntity, "birthdate").ToLocalTime();

                            // 組員的職業及專長
                            String aIndustry = "";
                            if (ContactEntity.Attributes.Contains("new_industry"))
                            {
                                aIndustry = (string)ContactEntity.Attributes["new_industry"];
                            }

                            #region// 委身類型
                            String aIdentity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref ContactEntity, "customertypecode"));

                            //aIdentity = Regex.Replace(aIdentity, "[0-9]", "");//過濾掉數字
                            //aIdentity = aIdentity.Replace(" ", ""); // //過濾掉空白
                            //aIdentity = aIdentity.Replace(".", ""); // //過濾掉逗號

                            #endregion


                            // 取得新人跟進週次，及跟進歷程記錄
                            String aFollowUpWeek = "";
                            String aNewComerNote = GetNewComerFollowupInfo(ContactEntity.Id, ref aFollowUpWeek);

                            bool SearchFlag = false;
                            foreach (MemberInfomation aPersonalMemberInfomation in this.m_GroupNamedListMemberInfomation)
                            {
                                if (aPersonalMemberInfomation.Name == FullName)
                                {
                                    SearchFlag = true;
                                    MemberInfomation aMemberInfomation = new MemberInfomation()
                                    {
                                        Group = GroupName,
                                        Name = aPersonalMemberInfomation.Name,
                                        Identity = aPersonalMemberInfomation.Identity,
                                        Phone = aPersonalMemberInfomation.Phone,
                                        HomePhone = aPersonalMemberInfomation.HomePhone,
                                        Address = aPersonalMemberInfomation.Address,
                                        //BirthDate = aBirthDate,
                                        Industry = aPersonalMemberInfomation.Industry,
                                        Note = aPersonalMemberInfomation.Note,
                                        Date = aPersonalMemberInfomation.Date,
                                        Number = aPersonalMemberInfomation.Number,
                                        SundayPresent = aPersonalMemberInfomation.SundayPresent,
                                        SmallGroupPresent = aPersonalMemberInfomation.SmallGroupPresent,

                                        PrayNumber = aPersonalMemberInfomation.PrayNumber,
                                        SpiritNumber = aPersonalMemberInfomation.SpiritNumber,
                                        FamilyNumber = aPersonalMemberInfomation.FamilyNumber,
                                        WorkAndCampusNumber = aPersonalMemberInfomation.WorkAndCampusNumber,
                                        ShepherdStatus = aPersonalMemberInfomation.ShepherdStatus,
                                        OneOnOne = aPersonalMemberInfomation.OneOnOne,
                                        Training = aPersonalMemberInfomation.Training,
                                        Incubate = aPersonalMemberInfomation.Incubate,
                                        FollowUpWeek = aPersonalMemberInfomation.FollowUpWeek,
                                        FollowUpResult = aPersonalMemberInfomation.FollowUpResult,
                                        FollowUpNextStep = aPersonalMemberInfomation.FollowUpNextStep,
                                        FollowUpOption = aPersonalMemberInfomation.FollowUpOption,
                                        FollowUp = aPersonalMemberInfomation.FollowUp,
                                        FollowUpNote = aPersonalMemberInfomation.FollowUpNote,
                                        NewComerNote = aPersonalMemberInfomation.NewComerNote,

                                        #region 靈修、晨、晚禱
                                        SpiritualWork = aPersonalMemberInfomation.SpiritualWork,
                                        MorningPray = aPersonalMemberInfomation.MorningPray,
                                        GeneralCare = aPersonalMemberInfomation.GeneralCare,
                                        #endregion
                                    };

                                    if (aIdentity != "10.未入組結案")
                                    {
                                        // "10.未入組結案" 不用進入 APP
                                        this.m_MemberInfomationPackage.ListMemberInfomation.Add(aMemberInfomation);
                                    }

                                    break;
                                }
                            }

                            if (SearchFlag == false)
                            {
                                MemberInfomation aMemberInfomation = new MemberInfomation()
                                {
                                    Group = GroupName,
                                    Name = FullName,
                                    Identity = aIdentity,
                                    Phone = DigitsOnly.Replace(aMobilePhone, ""),
                                    HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                                    Address = aAddress,
                                    //BirthDate = aBirthDate,
                                    Industry = aIndustry,
                                    Note = "",
                                    Date = "2015/10/6",
                                    Number = 5,
                                    SundayPresent = false,
                                    SmallGroupPresent = false,

                                    PrayNumber = 0,
                                    SpiritNumber = 0,
                                    FamilyNumber = 0,
                                    WorkAndCampusNumber = 0,
                                    ShepherdStatus = "",
                                    OneOnOne = "",
                                    Training = "",
                                    Incubate = "",
                                    FollowUpWeek = aFollowUpWeek,
                                    FollowUpResult = ".",
                                    FollowUpNextStep = ".",
                                    FollowUpOption = "",
                                    FollowUp = "",
                                    FollowUpNote = "",
                                    NewComerNote = aNewComerNote,
                                    #region 靈修、晨、晚禱
                                    SpiritualWork = 0,
                                    MorningPray = 0,
                                    GeneralCare = 0,
                                    #endregion
                                };

                                if (aIdentity != "10.未入組結案")
                                {
                                    // "10.未入組結案" 不用進入 APP
                                    this.m_MemberInfomationPackage.ListMemberInfomation.Add(aMemberInfomation);
                                }
                            }

                            #endregion
                        }
                        else
                        { //String StateCode = "非使用中";
                        }
                    }

                    break; // 在名單中找到登入者的ID就跳出
                }
            }
            #endregion

            return;
        }
        private String GetNewComerFollowupInfo(Guid aNewComerId, ref String aFollowUpWeek)
        {
            try
            {
                // 取得新人的實體
                Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aNewComerId);

                String aFollowUpHistoryReport = "";

                if (VerifyNewComerIdentity(aContact))
                {
                    // 確認是新人或是未入組才要處理

                    // 確認是否是新人或是未入組
                    int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

                    if (aIdentityNumber == 100000004)
                    {
                        #region// 未入組

                        String aStartTracking = this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "new_start_tracking_date");
                        if (aStartTracking != "")
                        {
                            // 如果是未入組就有可能是死灰復燃，所以要依據"開始關懷日期"是否要重燃關懷的過程
                            DateTime aStartTrackingDate = DateTime.Parse(aStartTracking);

                            #region 先根據日期尋找開始關懷日期的那週主日日期
                            // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                            int DayOfWeek = (int)aStartTrackingDate.DayOfWeek;
                            DateTime aSunday = aStartTrackingDate.AddDays(-DayOfWeek);
                            #endregion

                            aFollowUpHistoryReport = GetFollowUpWeekForUnGroup(aContact, ref aFollowUpWeek, aSunday);
                        }
                        else
                        {
                            // 不是死灰復燃的未入組，所以就按照正常程序關懷
                            aFollowUpHistoryReport = GetFollowUpWeek(aContact, ref aFollowUpWeek);
                        }
                        #endregion
                    }
                    else
                    {
                        #region// 新朋友

                        // 如果是新朋友就按正常程序來關懷，不會有死灰復燃的問題，因為根本就是新人
                        // 處理對應的週次及歡迎紀錄和每週跟進歷程
                        aFollowUpHistoryReport = GetFollowUpWeek(aContact, ref aFollowUpWeek);
                        #endregion
                    }
                }
                else
                {

                }
                return aFollowUpHistoryReport;
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private bool VerifyNewComerIdentity(Entity aContact)
        {
            try
            {
                // 確認是否是新人或是未入組
                int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

                if (aIdentityNumber == 100000000 || aIdentityNumber == 100000004)
                {
                    //    case 100000000:
                    //        return "8. 新朋友";
                    //    case 100000004:
                    //        return "7. 未入組";

                    return true;
                }
                else
                {
                    return false;
                }
                //switch (Identity)
                //{
                //    case 100000000:
                //        return "8. 新朋友";
                //    case 100000001:
                //        return "5. 神學生";
                //    case 100000002:
                //        return "4. 小組長";
                //    case 100000003:
                //        return "3. 全職同工";
                //    case 100000004:
                //        return "7. 未入組";
                //    case 100000005:
                //        return "1. 牧師";
                //    case 100000006:
                //        return "2, 師母";
                //    case 100000007:
                //        return "9. 外教會";
                //    case 100000008:
                //        return "10. 未入組結案";
                //    case 1:
                //        return "6. 小組組員";
                //    default:
                //        return ".";
                //}

            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private String GetFollowUpWeek(Entity aContact, ref String MatchedWeekDay)
        {
            try
            {
                String aFollowUpHistoryReport = "";

                #region 歷程記錄的表頭
                #region// 性別
                int Gender = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "gendercode");
                if (Gender == 200000)
                {
                    aFollowUpHistoryReport += "性別:男性" + Environment.NewLine;
                }
                else
                {
                    aFollowUpHistoryReport += "性別:女性" + Environment.NewLine;

                }
                #endregion
                #region// 首次進入教會日期
                try
                {
                    DateTime FirstDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToLocalTime();
                    if (FirstDate.Year > 0)
                    {
                        aFollowUpHistoryReport += "首次進入教會日期:" + this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToLocalTime().ToShortDateString() + Environment.NewLine;
                    }
                }
                catch (System.Exception Exception)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                }
                #endregion
                #region// 取得歡迎紀錄
                String WelcomeRecord = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "description");
                if (WelcomeRecord != "")
                {
                    aFollowUpHistoryReport += "歡迎紀錄:" + Environment.NewLine + WelcomeRecord + Environment.NewLine + Environment.NewLine;
                }
                #endregion
                #endregion

                // 取得與此新人相關的出席紀錄單
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySunday("contact", "contactid", aContact.Id.ToString(), "new_contact_new_present_record", "new_present_record");

                #region 關懷歷程記錄
                if (PresentRecordCollection.Entities.Count > 0)
                {
                    aFollowUpHistoryReport += "關懷歷程記錄:" + Environment.NewLine;
                }
                else
                {
                    aFollowUpHistoryReport += "沒有關懷歷程記錄!" + Environment.NewLine;
                }
                #endregion

                int WeekCounter = 1;
                MatchedWeekDay = "";
                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    #region 處理一個一個的出席紀錄

                    #region 決定本週的週次
                    DateTime aSundayDate = DateTime.Now;
                    try
                    {
                        aSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(PresentRecordEntity, "new_sunday_date");
                        if (aSundayDate.Date == this.m_Sunday.Date)
                        {
                            // 轉化成為中文的週次，這是要SHOW給APP看的
                            MatchedWeekDay = ConvertNumberToFollowUpWeekPicker(WeekCounter);
                        }
                    }
                    catch (System.Exception Exception)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                    }
                    #endregion

                    #region 新人跟進相關資訊
                    //aFollowUpHistoryReport += aSundayDate.Date.ToShortDateString() + "， 第" + ConvertNumberToFollowUpWeekPicker(WeekCounter) + "週，";
                    aFollowUpHistoryReport += "第" + ConvertNumberToFollowUpWeekPicker(WeekCounter) + "週，" + aSundayDate.Date.ToShortDateString() + "，";
                    aFollowUpHistoryReport += "小組長:" + this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_groupleader_present_record") + "，";

                    //if (aSundayDate != DateTime.Now)
                    //{
                    //    aFollowUpHistoryReport += "跟進日期:" + aSundayDate.ToShortDateString() + "，";
                    //}

                    #region //跟進方式
                    int FollowUpOptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_followup_ways");
                    String aFollowUpOption = ConvertIndexToFollowUpOptionPicker(FollowUpOptionValue);
                    String aFollowUpMethod = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_follow_up");
                    if (aFollowUpMethod != "")
                    {
                        aFollowUpHistoryReport += "跟進方式:" + aFollowUpOption + aFollowUpMethod + "，";
                    }
                    #endregion
                    #region//新人跟進結果
                    String aFollowUpResult = "";
                    if (PresentRecordEntity.Attributes.Contains("new_conclusion_choise"))
                    {
                        int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_conclusion_choise");
                        aFollowUpResult = ConvertIndexToFollowUpResultPicker(OptionValue);
                    }
                    if (aFollowUpResult != "" && aFollowUpResult != "請選擇")
                    {
                        aFollowUpHistoryReport += "跟進結果:" + aFollowUpResult + "，";
                    }
                    #endregion
                    #region//新人跟進下一步驟
                    String aFollowUpNextStep = "";
                    if (PresentRecordEntity.Attributes.Contains("new_next_step"))
                    {
                        int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_next_step");
                        aFollowUpNextStep = ConvertIndexToFollowUpNextStepPicker(OptionValue);
                    }
                    if (aFollowUpNextStep != "" && aFollowUpNextStep != "請選擇")
                    {
                        aFollowUpHistoryReport += "跟進下一步驟:" + aFollowUpNextStep + "，";
                    }
                    #endregion
                    #region//跟進描述
                    String aExplanation = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_explanation");
                    if (aExplanation != "")
                    {
                        aFollowUpHistoryReport += "跟進描述:" + aExplanation + Environment.NewLine + Environment.NewLine;
                    }
                    else
                    {
                        aFollowUpHistoryReport += Environment.NewLine + Environment.NewLine;
                    }
                    #endregion
                    #endregion

                    #region 自動幫忙重新設定關懷週次

                    try
                    {
                        int WeekIndex = ConvertNumberToWeekIndex(WeekCounter);
                        this.m_ToolUtilityClass.SetOptionSetAttribute(PresentRecordEntity, "new_weeks", WeekIndex);
                        //Entity aRetrievedPresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", PresentRecordEntity.Id);
                        //this.m_ToolUtilityClass.UpdateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, PresentRecordEntity);
                        this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, PresentRecordEntity);
                        //this.m_ToolUtilityClass.UpdateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, ref aRetrievedPresentRecordEntity);
                    }
                    catch (System.Exception Exception)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                    }
                    #endregion

                    // 自動把新朋友若是超過10週的關懷則設為未入組，把未入組若是超過或等於18週的關懷則設為未入組結案
                    TransferIdentity(aContact, WeekCounter, 10, 18);

                    WeekCounter++;
                    #endregion
                }

                return aFollowUpHistoryReport;
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private String GetFollowUpWeekForUnGroup(Entity aContact, ref String MatchedWeekDay, DateTime aStartTrackingSunday)
        {
            try
            {
                String aFollowUpHistoryReport = "";

                #region 歷程記錄的表頭
                #region// 性別
                int Gender = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "gendercode");
                if (Gender == 200000)
                {
                    aFollowUpHistoryReport += "性別:男性" + Environment.NewLine;
                }
                else
                {
                    aFollowUpHistoryReport += "性別:女性" + Environment.NewLine;

                }
                #endregion
                #region// 首次進入教會日期
                try
                {
                    DateTime FirstDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToLocalTime();
                    if (FirstDate.Year > 0)
                    {
                        aFollowUpHistoryReport += "首次進入教會日期:" + this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToLocalTime().ToShortDateString() + Environment.NewLine;
                    }
                }
                catch (System.Exception Exception)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                }
                #endregion
                #region// 取得歡迎紀錄
                String WelcomeRecord = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "description");
                if (WelcomeRecord != "")
                {
                    aFollowUpHistoryReport += "歡迎紀錄:" + Environment.NewLine + WelcomeRecord + Environment.NewLine + Environment.NewLine;
                }
                #endregion
                #endregion

                // 取得與此新人相關的出席紀錄單
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySunday("contact", "contactid", aContact.Id.ToString(), "new_contact_new_present_record", "new_present_record");

                #region 關懷歷程記錄
                if (PresentRecordCollection.Entities.Count > 0)
                {
                    aFollowUpHistoryReport += "關懷歷程記錄:" + Environment.NewLine;
                }
                else
                {
                    aFollowUpHistoryReport += "沒有關懷歷程記錄!" + Environment.NewLine;
                }
                #endregion

                int WeekCounter = 1;
                MatchedWeekDay = "";
                bool FoundFlag = false;
                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    #region 處理一個一個的出席紀錄

                    DateTime aPresentRecordSundayDate = DateTime.Now;

                    aPresentRecordSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(PresentRecordEntity, "new_sunday_date");

                    if (FoundFlag == false)
                    {
                        if (aPresentRecordSundayDate.ToShortDateString() == aStartTrackingSunday.ToShortDateString())
                        {
                            // 找到了死灰復燃的那個主日日期
                            WeekCounter = 1; // 設定為第一周
                            FoundFlag = true; // 開始循序累加周次
                        }
                        else
                        {
                            continue;
                        }
                    }

                    #region 決定本週的週次
                    DateTime aSundayDate = DateTime.Now;
                    try
                    {
                        aSundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(PresentRecordEntity, "new_sunday_date");
                        if (aSundayDate.Date == this.m_Sunday.Date)
                        {
                            // 轉化成為中文的週次，這是要SHOW給APP看的
                            MatchedWeekDay = ConvertNumberToFollowUpWeekPicker(WeekCounter);
                        }
                    }
                    catch (System.Exception Exception)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                    }
                    #endregion

                    #region 新人跟進相關資訊
                    //aFollowUpHistoryReport += aSundayDate.Date.ToShortDateString() + "， 第" + ConvertNumberToFollowUpWeekPicker(WeekCounter) + "週，";
                    aFollowUpHistoryReport += "第" + ConvertNumberToFollowUpWeekPicker(WeekCounter) + "週，" + aSundayDate.Date.ToShortDateString() + "，";
                    aFollowUpHistoryReport += "小組長:" + this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_groupleader_present_record") + "，";

                    //if (aSundayDate != DateTime.Now)
                    //{
                    //    aFollowUpHistoryReport += "跟進日期:" + aSundayDate.ToShortDateString() + "，";
                    //}

                    #region //跟進方式
                    String aFollowUpMethod = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_follow_up");
                    if (aFollowUpMethod != "")
                    {
                        aFollowUpHistoryReport += "跟進方式:" + aFollowUpMethod + "，";
                    }
                    #endregion
                    #region//新人跟進結果
                    String aFollowUpResult = "";
                    if (PresentRecordEntity.Attributes.Contains("new_conclusion_choise"))
                    {
                        int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_conclusion_choise");
                        aFollowUpResult = ConvertIndexToFollowUpResultPicker(OptionValue);
                    }
                    if (aFollowUpResult != "" && aFollowUpResult != "請選擇")
                    {
                        aFollowUpHistoryReport += "跟進結果:" + aFollowUpResult + "，";
                    }
                    #endregion
                    #region//新人跟進下一步驟
                    String aFollowUpNextStep = "";
                    if (PresentRecordEntity.Attributes.Contains("new_next_step"))
                    {
                        int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_next_step");
                        aFollowUpNextStep = ConvertIndexToFollowUpNextStepPicker(OptionValue);
                    }
                    if (aFollowUpNextStep != "" && aFollowUpNextStep != "請選擇")
                    {
                        aFollowUpHistoryReport += "跟進下一步驟:" + aFollowUpNextStep + "，";
                    }
                    #endregion
                    #region//跟進描述
                    String aExplanation = this.m_ToolUtilityClass.GetEntityStringAttribute(PresentRecordEntity, "new_explanation");
                    if (aExplanation != "")
                    {
                        aFollowUpHistoryReport += "跟進描述:" + aExplanation + Environment.NewLine + Environment.NewLine;
                    }
                    else
                    {
                        aFollowUpHistoryReport += Environment.NewLine + Environment.NewLine;
                    }
                    #endregion
                    #endregion

                    #region 自動幫忙重新設定關懷週次

                    try
                    {
                        int WeekIndex = ConvertNumberToWeekIndex(WeekCounter);
                        this.m_ToolUtilityClass.SetOptionSetAttribute(PresentRecordEntity, "new_weeks", WeekIndex);
                        //Entity aRetrievedPresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", PresentRecordEntity.Id);
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, PresentRecordEntity);
                        }
                        else
                        {
                            this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, PresentRecordEntity);
                        }
                        //this.m_ToolUtilityClass.UpdateEntity(ref this.m_ToolUtilityClass.m_OrganizationService, ref aRetrievedPresentRecordEntity);
                    }
                    catch (System.Exception Exception)
                    {
                        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                    }
                    #endregion

                    // 因為這是未入組死灰復燃，把未入組若是超過或等於10週的關懷則設為未入組結案
                    TransferIdentity(aContact, WeekCounter, 10, 10);

                    WeekCounter++;
                    #endregion
                }

                return aFollowUpHistoryReport;
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private void TransferIdentity(Entity aContact, int Counter, int NewComeMaxiNumber, int UnGroupMaxiNumber)
        {
            //switch (Identity)
            //{
            //    case 100000000:
            //        return "8. 新朋友";
            //    case 100000001:
            //        return "5. 神學生";
            //    case 100000002:
            //        return "4. 小組長";
            //    case 100000003:
            //        return "3. 全職同工";
            //    case 100000004:
            //        return "7. 未入組";
            //    case 100000005:
            //        return "1. 牧師";
            //    case 100000006:
            //        return "2, 師母";
            //    case 100000007:
            //        return "9. 外教會";
            //    case 100000008:
            //        return "10. 未入組結案";
            //    case 1:
            //        return "6. 小組組員";
            //    default:
            //        return ".";
            //}


            // 確認是否是新人或是未入組
            int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

            // 因為新朋友、未入組會變更委身類型，旗標防止設定太多次，false表示尚未設定
            if (aIdentityNumber == 100000000)
            {

                //m_SetIdentityFlag = false; // 因為新朋友、未入組會變更委身類型，旗標防止設定太多次，false表示尚未設定

                // 新朋友
                if (Counter >= NewComeMaxiNumber && m_SetIdentityFlag == false)
                {
                    // 只要設定一次就好
                    m_SetIdentityFlag = true;

                    if (TRANSFER_IDENTITY_FLAG == true)
                    {
                        // 新朋友變為未入組
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);
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
                else { }
            }
            else if (aIdentityNumber == 100000004)
            {
                //未入組
                if (Counter >= UnGroupMaxiNumber && m_SetIdentityFlag == false)
                {
                    // 只要設定一次就好
                    m_SetIdentityFlag = true;

                    if (TRANSFER_IDENTITY_FLAG == true)
                    {
                        // 未入組變為未入組結案(超過或是等於)
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000001);

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
                else { }
            }
            else
            {

            }

        }
        private String ConvertNumberToFollowUpWeekPicker(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 1:
                    return "一";
                case 2:
                    return "二";
                case 3:
                    return "三";
                case 4:
                    return "四";
                case 5:
                    return "五";
                case 6:
                    return "六";
                case 7:
                    return "七";
                case 8:
                    return "八";
                case 9:
                    return "九";
                case 10:
                    return "十";
                case 11:
                    return "十一";
                case 12:
                    return "十二";
                case 13:
                    return "十三";
                case 14:
                    return "十四";
                case 15:
                    return "十五";
                case 16:
                    return "十六";
                case 17:
                    return "十七";
                case 18:
                    return "十八";
                case 19:
                    return "十九";
                case 20:
                    return "二十";
                default:
                    return "二十";
            }
        }
        private int ConvertNumberToWeekIndex(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 1:
                    return 100000000;
                case 2:
                    return 100000001;
                case 3:
                    return 100000002;
                case 4:
                    return 100000003;
                case 5:
                    return 100000004;
                case 6:
                    return 100000005;
                case 7:
                    return 100000006;
                case 8:
                    return 100000007;
                case 9:
                    return 100000008;
                case 10:
                    return 100000009;
                case 11:
                    return 100000010;
                case 12:
                    return 100000011;
                case 13:
                    return 100000012;
                case 14:
                    return 100000013;
                case 15:
                    return 100000014;
                case 16:
                    return 100000015;
                case 17:
                    return 100000016;
                case 18:
                    return 100000017;
                case 19:
                    return 100000018;
                case 20:
                    return 100000019;
                default:
                    return 100000007;
            }
        }
        private String ConvertIndexToFollowUpWeekPicker(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 100000000:
                    return "一";
                case 100000001:
                    return "二";
                case 100000002:
                    return "三";
                case 100000003:
                    return "四";
                case 100000004:
                    return "五";
                case 100000005:
                    return "六";
                case 100000006:
                    return "七";
                case 100000007:
                    return "八";
                case 100000009:
                    return "九";
                case 100000010:
                    return "十";
                case 100000011:
                    return "十一";
                case 100000012:
                    return "十二";
                case 100000013:
                    return "十三";
                case 100000014:
                    return "十四";
                case 100000015:
                    return "十五";
                case 100000016:
                    return "十六";
                case 100000017:
                    return "十七";
                case 100000018:
                    return "十八";
                case 100000019:
                    return "十九";
                case 100000020:
                    return "二十";
                case 100000008:
                    return "未選擇";
                default:
                    return ".";
            }
        }
        private String ConvertIndexToFollowUpResultPicker(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 100000000:
                    return "請選擇";
                case 100000001:
                    return "熱情回應";
                case 100000002:
                    return "渴慕認識信仰";
                case 100000003:
                    return "沒聯絡上";
                case 100000004:
                    return "反應冷淡";
                case 100000005:
                    return "考慮中";
                case 100000006:
                    return "入小組";
                case 100000007:
                    return "來主日";
                case 100000008:
                    return "轉介";
                case 100000009:
                    return "其他";
                default:
                    return "";
            }
        }
        private String ConvertIndexToFollowUpNextStepPicker(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 100000000:
                    return "請選擇";
                case 100000001:
                    return "繼續跟進";
                case 100000002:
                    return "轉介";
                default:
                    return "";
            }
        }
        private String ConvertIndexToFollowUpOptionPicker(int FollowUpWays)
        {
            switch (FollowUpWays)
            {
                case 100000000:
                    return "電話";
                case 100000001:
                    return "探訪";
                case 100000002:
                    return "Line/FB";
                case 100000003:
                    return "出遊/吃飯";
                case 100000004:
                    return "懷鄉/其他課程";
                case 100000005:
                    return "約談";
                case 100000006:
                    return "沒跟進";
                case 100000007:
                    return "其他";
                default:
                    return "";
            }
        }
        private void GetSmallGroupLeaderMemberData(String GroupName, Guid ListEntityId)
        {
            #region // 處理每個小組名單
            //搜尋名單的組員
            //EntityCollection Contacts = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntityId.ToString(), "new_cell_list_contact", "contact");

            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);

            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");
            EntityCollection MemberCollection;
            if (ListType == false)
            {
                // 靜態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }

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

                if (ContactEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;

                    if (aOptionState.Value == 0)
                    {
                        #region 只回傳使用中的組員

                        // 組員的全名
                        String FullName = "";
                        if (ContactEntity.Attributes.Contains("fullname"))
                        {
                            FullName = (string)ContactEntity.Attributes["fullname"];
                        }
                        // 組員的手機
                        String aMobilePhone = "";
                        if (ContactEntity.Attributes.Contains("mobilephone"))
                        {
                            aMobilePhone = (string)ContactEntity.Attributes["mobilephone"];
                        }
                        // 組員的家裡電話
                        String aHomePhone = "";
                        if (ContactEntity.Attributes.Contains("telephone2"))
                        {
                            aHomePhone = (string)ContactEntity.Attributes["telephone2"];
                        }
                        // 組員的地址
                        String aAddress = "";
                        if (ContactEntity.Attributes.Contains("address2_line1"))
                        {
                            aAddress = (string)ContactEntity.Attributes["address2_line1"];
                        }

                        // 組員的生日
                        //DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref ContactEntity, "birthdate").ToLocalTime();

                        // 組員的職業及專長
                        String aIndustry = "";
                        if (ContactEntity.Attributes.Contains("new_industry"))
                        {
                            aIndustry = (string)ContactEntity.Attributes["new_industry"];
                        }

                        #region// 委身類型
                        String aIdentity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref ContactEntity, "customertypecode"));

                        //aIdentity = Regex.Replace(aIdentity, "[0-9]", "");//過濾掉數字
                        //aIdentity = aIdentity.Replace(" ", ""); // //過濾掉空白
                        //aIdentity = aIdentity.Replace(".", ""); // //過濾掉逗號

                        #endregion


                        // 取得新人跟進週次，及跟進歷程記錄
                        String aFollowUpWeek = "";
                        String aNewComerNote = GetNewComerFollowupInfo(ContactEntity.Id, ref aFollowUpWeek);

                        MemberInfomation aMemberInfomation = new MemberInfomation()
                        {
                            Group = GroupName,
                            Name = FullName,
                            Identity = aIdentity,
                            Phone = DigitsOnly.Replace(aMobilePhone, ""),
                            HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                            Address = aAddress,
                            //BirthDate =aBirthDate,
                            Industry = aIndustry,
                            Note = "",
                            Date = "2015/10/6",
                            Number = 5,
                            SundayPresent = false,
                            SmallGroupPresent = false,

                            PrayNumber = 0,
                            SpiritNumber = 0,
                            FamilyNumber = 0,
                            WorkAndCampusNumber = 0,
                            ShepherdStatus = "",
                            OneOnOne = "",
                            Training = "",
                            Incubate = "",
                            FollowUpWeek = aFollowUpWeek,
                            FollowUpResult = ".",
                            FollowUpNextStep = ".",
                            FollowUpOption = "",
                            FollowUp = "",
                            FollowUpNote = "",
                            NewComerNote = aNewComerNote,
                            #region 靈修、晨、晚禱
                            SpiritualWork = 0,
                            MorningPray = 0,
                            GeneralCare = 0,
                            #endregion

                        };

                        if (aIdentity != "10.未入組結案")
                        {
                            // "10.未入組結案" 不用進入 APP
                            this.m_MemberInfomationPackage.ListMemberInfomation.Add(aMemberInfomation);
                        }

                        #endregion
                    }
                    else
                    { //String StateCode = "非使用中";
                    }
                }
            }
            #endregion

            return;
        }
        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, MemberInfomation aMemberInfomation, ref Entity aContactEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid)
        {
            try
            {
                #region 設定名稱
                String PresentRecordName = aMemberInfomation.Name + String.Format("-{0:00}/{1:00}/{2:00} 出席紀錄", this.m_Sunday.Year, this.m_Sunday.Month, this.m_Sunday.Day);
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
                #region 從名單取得 區名、小家長 ID、小組長 ID、區長、區牧長 ID
                // 小家長 ID
                Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 區長 ID
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
                if (aGroupWeeklyReportGuid.SmallGroupDate.Year > 1)
                {
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", aGroupWeeklyReportGuid.SmallGroupDate);
                }
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
                    if (ValidNumber > 0)
                    {
                        this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 1 / ValidNumber);

                        if (IsValidContact(aContactEntity) == true)
                        {
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
                    if (ValidNumber > 0)
                    {
                        this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 1 / ValidNumber);

                        if (IsValidContact(aContactEntity) == true)
                        {
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
                #region// 靈修次數

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_spiritual_work", aMemberInfomation.SpiritualWork);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_morning_pray", aMemberInfomation.MorningPray);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_general_care", aMemberInfomation.GeneralCare);

                //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_general_care", aMemberInfomation.PrayNumber);
                //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_spiritual_work", aMemberInfomation.SpiritNumber);
                //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_morning_pray", aMemberInfomation.FamilyNumber);
                //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_evening_pray", aMemberInfomation.WorkAndCampusNumber);
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
        private Entity UpdateContactInfomationFromList(String ContactName, Guid ListEntityId)
        {
            #region // 處理每個小組名單
            //搜尋名單的組員
            //EntityCollection Contacts = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntityId.ToString(), "new_cell_list_contact", "contact");

            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);

            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");
            EntityCollection MemberCollection;
            if (ListType == false)
            {
                // 靜態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.m_ToolUtilityClass.RetrieveDynamicMemberListCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ListEntityId);
                }
            }

            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                // 名單中每個組員
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

                // 必須是使用中的連絡人
                if (ContactEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;

                    if (aOptionState.Value == 0)
                    {
                        #region 只回傳使用中的組員
                        // 組員的全名
                        String FullName = "";
                        if (ContactEntity.Attributes.Contains("fullname"))
                        {
                            FullName = (string)ContactEntity.Attributes["fullname"];

                            if (FullName == ContactName)
                                return ContactEntity;
                        }

                        #endregion
                    }
                    else
                    { //String StateCode = "非使用中";
                    }
                }
            }
            #endregion

            return null;
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

            // 組員的生日
            //if (aContactEntity.Attributes.Contains("birthdate"))
            //{
            //    DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContactEntity, "birthdate").ToLocalTime().ToLocalTime();
            //    if (aMemberInfomation.BirthDate != aBirthDate)
            //    {
            //        // 系統裡的聯絡人職業及專長跟APP上傳的不一致
            //        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "birthdate", aMemberInfomation.BirthDate);
            //        ModifyFlag = true;
            //    }
            //}
            //else
            //{
            //    if (aMemberInfomation.BirthDate > DateTime.MinValue)
            //    {
            //        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "birthdate", aMemberInfomation.BirthDate);
            //        ModifyFlag = true;
            //    }
            //}

            // 組員的職業及專長(台北基督之家)
            String aIndustry = "";
            if (aContactEntity.Attributes.Contains("new_industry"))
            {
                aIndustry = (string)aContactEntity.Attributes["new_industry"];
                if (aMemberInfomation.Industry != aIndustry)
                {
                    // 系統裡的聯絡人職業及專長跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_industry", aMemberInfomation.Industry);
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

            // 組員的生日
            //if (aContactEntity.Attributes.Contains("birthdate"))
            //{
            //    DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContactEntity, "birthdate").ToLocalTime();
            //    if (aMemberInfomation.BirthDate != aBirthDate)
            //    {
            //        // 系統裡的聯絡人職業及專長跟APP上傳的不一致
            //        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "birthdate", aMemberInfomation.BirthDate);
            //        ModifyFlag = true;
            //    }
            //}
            //else
            //{
            //    if (aMemberInfomation.BirthDate > DateTime.MinValue)
            //    {
            //        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "birthdate", aMemberInfomation.BirthDate);
            //        ModifyFlag = true;
            //    }
            //}


            // 組員的職業及專長(台北基督之家)
            String aIndustry = "";
            if (aContactEntity.Attributes.Contains("new_industry"))
            {
                aIndustry = (string)aContactEntity.Attributes["new_industry"];
                if (aMemberInfomation.Industry != aIndustry)
                {
                    // 系統裡的聯絡人職業及專長跟APP上傳的不一致
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_industry", aMemberInfomation.Industry);
                    ModifyFlag = true;
                }
            }
            else
            {
                if (aMemberInfomation.Industry != "")
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_industry", aMemberInfomation.Industry);
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
        private Entity UpdateWeeklyReport(GroupWeeklyReportGuid aGroupWeeklyReportGuid, EntityCollection PresentRecordCollection, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber)
        {
            try
            {
                #region 更新週報
                int ValidSundayMemberNumber = 0;
                int ValidSmallGroupMemberNumber = 0;
                aWeeklySundayNumber = 0;
                aWeeklySundayRate = 0.0;
                aWeeklySmallGroupNumber = 0;
                aWeeklySmallGroupRate = 0.0;

                foreach (Entity aMachedPresentRecordEntity in PresentRecordCollection.Entities)
                {
                    if (aMachedPresentRecordEntity != null)
                    {
                        #region 計算個人聚會與靈修記錄
                        // 是否符合累積出席率可以貢獻出席的的委身類型，並且順便取的委身類型
                        String ClearIdentity = "";
                        bool AccumulateFlag = this.IsValidMember(aMachedPresentRecordEntity, ref ClearIdentity);

                        #region 取得主日出席
                        if (this.m_ToolUtilityClass.GetEntityIntAttribute(aMachedPresentRecordEntity, "new_sunday_present_this_week") == 1)
                        {
                            aWeeklySundayNumber += 1;
                            if (ValidNumber > 0)
                            {
                                if (AccumulateFlag == true)
                                {
                                    ValidSundayMemberNumber += 1;
                                    aWeeklySundayRate += 1 / ValidNumber;
                                }
                            }
                        }
                        else
                        { }
                        #endregion
                        #region 設定小組出席
                        if (this.m_ToolUtilityClass.GetEntityIntAttribute(aMachedPresentRecordEntity, "new_group_present_this_week") == 1)
                        {
                            aWeeklySmallGroupNumber += 1;
                            if (ValidNumber > 0)
                            {
                                if (AccumulateFlag == true)
                                {
                                    ValidSmallGroupMemberNumber += 1;
                                    aWeeklySmallGroupRate += 1 / ValidNumber;
                                }
                            }
                        }
                        else
                        {
                        }
                        #endregion

                        #endregion
                    }
                    else
                    {
                    }
                }

                #endregion

                #region 設定週報狀態，設定為已點名、週報主日出席率、小組出席率

                Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aWeeklyReportId);

                #region 設定小組聚會日期

                // 取得周報的小組長，若是與登入的回報者同一個人
                // 才有權限更改小組聚會日期
                if (this.m_ContactEntity.Id == this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aWeeklyReportEntity, "new_groupleader_group_present_weekly_"))
                {
                    if (aGroupWeeklyReportGuid.SmallGroupDate.Year > 1)
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", aGroupWeeklyReportGuid.SmallGroupDate);
                    }
                    else
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", DateTime.Now);
                    }
                }
                #endregion

                // 設定新人跟進報告

                // 設定週報點名狀態
                this.SetupWeeklyReportStatus("主日點名", ref aWeeklyReportEntity);

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
                aGroupWeeklyReportGuid.SmallGroupRate = aWeeklySmallGroupRate;     // 回傳至 APP 的小組出席率
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
        private Entity UpdateWeeklyReportProcess(GroupWeeklyReportGuid aGroupWeeklyReportGuid, ref Entity aListEntity, ref Guid aWeeklyReportId)
        {
            try
            {
                #region 更新週報流程
                Double aWeeklySundayRate = 0.0;
                Double aWeeklySmallGroupRate = 0.0;
                int aWeeklySundayNumber = 0;
                int aWeeklySmallGroupNumber = 0;

                Double ValidNumber = 0.0F; // 依據之前點名的靈修紀錄的有效組員當作週報出席率的分母

                // 已經有週報所以從靈修出席紀錄單計算出席率的分母
                #region 取得跟週報相關的靈修紀錄單
                EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", aWeeklyReportId.ToString(), "new_group_present_weekly_report_prese", "new_present_record");
                #endregion

                // 依據靈修出席紀錄單計算出席率的分母
                ValidNumber = GetValidMemberNumber(aPresentRecordCollection);

                #region// 更新個人資料:手機、家裡電話、地址、設定委身類型
                // 更新個人聚會與靈修記錄
                UpdatePresentRecord(this.m_GroupNamedListMemberInfomation, aPresentRecordCollection, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid);
                #endregion

                #region 更新週報
                return UpdateWeeklyReport(aGroupWeeklyReportGuid, aPresentRecordCollection, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber);
                #endregion

                #endregion
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void UpdatePresentRecord(List<MemberInfomation> aGroupNamedListMemberInfomation, EntityCollection PresentRecordCollection, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid)
        {
            try
            {
                #region 更新每個靈修紀錄
                foreach (MemberInfomation aMemberInfomation in aGroupNamedListMemberInfomation)
                {
                    // 上傳的點名與系統的靈修紀錄對應尋找
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
                        String ClearIdentity = ""; //順便取的委身類型
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
                            if (ValidNumber > 0)
                            {
                                //this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_sunday_rate", 1 / ValidNumber);

                                if (AccumulateFlag == true)
                                {
                                    aWeeklySundayRate += 1 / ValidNumber;
                                }
                            }
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_sunday_rate", 0.0);
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
                            if (ValidNumber > 0)
                            {
                                //this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_small_group_rate", 1 / ValidNumber);

                                if (AccumulateFlag == true)
                                {
                                    aWeeklySmallGroupRate += 1 / ValidNumber;
                                }
                            }
                        }
                        else
                        {
                            this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_small_group_rate", 0);
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
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_followup_ways", ConvertFollowUpOptionToIndex(aMemberInfomation.FollowUpOption));
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_follow_up", aMemberInfomation.FollowUp);

                        // 因為之前APP無法直接把代禱事項和新人跟進關懷用在表單中
                        // 但是網頁現在可以了
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMemberInfomation.FollowUpNote);
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMemberInfomation.Note);

                        AddToDictionaryFollowByIdentity(ref ClearIdentity, ref aContactEntity, aMemberInfomation);

                        #endregion

                        #endregion
                        #region// 靈修次數

                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_spiritual_work", aMemberInfomation.SpiritualWork);
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_morning_pray", aMemberInfomation.MorningPray);
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_general_care", aMemberInfomation.GeneralCare);

                        //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_general_care", aMemberInfomation.PrayNumber);
                        //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_spiritual_work", aMemberInfomation.SpiritNumber);
                        //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_morning_pray", aMemberInfomation.FamilyNumber);
                        //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_evening_pray", aMemberInfomation.WorkAndCampusNumber);
                        #endregion

                        #region 更新個人聚會與靈修記錄
                        this.m_ToolUtilityClass.UpdateEntity(ref aMachedPresentRecordEntity);
                        #endregion

                        #endregion

                    }
                    else
                    {
                        // 沒找到靈修紀錄
                        CreatePresentRecord(aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, ref aGroupWeeklyReportGuid);
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
        private int ConvertFollowUpOptionToIndex(String FollowUpNextStep)
        {
            switch (FollowUpNextStep)
            {
                case "電話":
                    return 100000000;
                case "探訪":
                    return 100000001;
                case "Line/FB":
                    return 100000002;
                case "出遊/吃飯":
                    return 100000003;
                case "懷鄉/其他課程":
                    return 100000004;
                case "約談":
                    return 100000005;
                case "沒跟進":
                    return 100000006;
                case "其他":
                    return 100000007;
                default:
                    return 100000000;
            }
        }
        private Entity SearchPresentRecordByName(String Name, ref EntityCollection PresentRecordCollection)
        {
            //回傳與上傳姓名符合的靈修單
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
                        int Number = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_sunday_present_this_week");
                        if (Number >= 0)
                            TotalNumber += Number;
                    }

                    return TotalNumber;

                }
                else if (Type == "小組")
                {
                    foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                    {
                        // 小組次數
                        int Number = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_group_present_this_week");
                        if (Number >= 0)
                            TotalNumber += Number;
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

        // 委身類型客製化
        // 台中生命之道靈糧堂
        private String ConvertIndexToIdentity(int Identity)
        {
            switch (Identity)
            {
                case 100000006:
                    return "01. 牧師師母";
                case 100000003:
                    return "02. 區長";
                case 100000008:
                    return "03. 小組長";
                case 100000012:
                    return "04. 副組長";
                case 1:
                    return "05. 小組組員";
                case 100000005:
                    return "06. 幸福BEST";
                case 100000004:
                    return "07. 未入組";
                case 100000000:
                    return "08. 新朋友";
                case 100000007:
                    return "09. 外教會.訪客";
                case 100000001:
                    return "10. 結案";
                default:
                    return ".";
            }
        }





        //楊梅靈糧堂
        //private String ConvertIndexToIdentity(int Identity)
        //{
        //    switch (Identity)
        //    {
        //        case 100000000:
        //            return "8. 新朋友";
        //        case 100000001:
        //            return "5. 神學生";
        //        case 100000002:
        //            return "4. 小組長";
        //        case 100000003:
        //            return "3. 全職同工";
        //        case 100000004:
        //            return "7. 未入組";
        //        case 100000005:
        //            return "1. 牧師";
        //        case 100000006:
        //            return "2, 師母";
        //        case 100000007:
        //            return "9. 外教會";
        //        case 100000008:
        //            return "10. 未入組結案";
        //        case 1:
        //            return "6. 小組組員";
        //        default:
        //            return ".";
        //    }
        //}

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
