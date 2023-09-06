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
using ChurchReport.Models;
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class DownloadIntegrateData
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
        #region 下載資料時所需要的參數
        DateTime m_Sunday;
        Entity m_ListEntity; //小組實體紀錄
        Entity m_ContactEntity; //登入者在系統裡的實體
        Entity m_WeeklyReportEntity; // 週報實體

        Guid m_ContactId; //登入者在系統裡的ID
        String m_LoginType = ""; // "小組長" 或是 "個人回報"

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以

        #endregion
        #region 下載資料區
        #region 主程式區
        public void SetupIntegrateData(String Account, String Password, String LoginType, DateTime aDownloadDate, String ListEntityId, String WeeklyReportEntityId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            this.m_LoginType = LoginType;

            #region 先根據日期尋找當週主日日期
            // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。

            int DayOfWeek = (int)aDownloadDate.DayOfWeek;

            // 每周以星期六為第一日
            if (DayOfWeek < 6)
            {
                // 小於 6 表示星期日到星期五=>當週的星期日為認定的主日
                this.m_Sunday = aDownloadDate.AddDays(-DayOfWeek);
            }
            else
            {
                // 為 6 = 星期六 (表示 DayOfWeek.Saturday)表示要加1到下一個星期日為認定的主日
                this.m_Sunday = aDownloadDate.AddDays(1);
            }
            #endregion

            this.SetupHeaderData( Account, Password, aDownloadDate, ListEntityId, WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);

            this.SetupShepherdData(ListEntityId, WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);

            this.SetupWeeklyReportData(WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);

            this.SetupWeeklyReportChartData( ref aListSmallGroupWeeklyReport );

            return;
        }
        public void SetupHeaderData(String Account, String Password, DateTime aDownloadDate, String ListEntityId, String WeeklyReportEntityId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            #region 找登入使用者及其ID
            FindLoginUser(Account, Password); // 也就是設定 this.m_ContactEntity
            if (m_ContactId == Guid.Empty) //是否有找到登入使用者及其ID
            { return; } // 沒找到就回傳 null 
            else
            {
                // 取得登入者的姓名
                //LoginFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ContactEntity, "fullname");
            }
            #endregion

            aListSmallGroupWeeklyReport.LoadFlag = true;
            this.m_ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", new Guid(ListEntityId));

            aListSmallGroupWeeklyReport.ListEntityName = this.m_ToolUtilityClass.GetEntityStringAttribute(m_ListEntity, "listname");

            if (aListSmallGroupWeeklyReport.ListEntityName.Contains("幸福") != true)
            {
                aListSmallGroupWeeklyReport.GroupType = "一般小組";
            }
            else
            {
                aListSmallGroupWeeklyReport.GroupType = "幸福小組";
            }

            aListSmallGroupWeeklyReport.WeeklyReportEntityId = WeeklyReportEntityId;
            if ( WeeklyReportEntityId != "" && WeeklyReportEntityId != null )
            {
                this.m_WeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", new Guid(WeeklyReportEntityId));
            }

            aListSmallGroupWeeklyReport.LoginType = this.m_LoginType;
            aListSmallGroupWeeklyReport.SmallGroupLeaderFullName = m_ToolUtilityClass.GetEntityLookupDisplayName(ref m_ListEntity, "new_contact_family_leader_list");
            aListSmallGroupWeeklyReport.SundayPrayers = aDownloadDate;
            //aListSmallGroupWeeklyReport.SundayPeriod = "小組日期對應到主日期間是: " + m_Sunday.ToLocalTime().ToShortDateString() + " ~ " + m_Sunday.AddDays(6).ToLocalTime().ToShortDateString();
            aListSmallGroupWeeklyReport.SundayPeriod = "小組日期對應到主日期間是: " + m_Sunday.AddDays(-1).ToLocalTime().ToShortDateString() + " ~ " + m_Sunday.AddDays(5).ToLocalTime().ToShortDateString();
            aListSmallGroupWeeklyReport.SmallGroupLeaderContactId = m_ContactId.ToString();
            aListSmallGroupWeeklyReport.SmallGroupLeaderFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ContactEntity, "fullname");

            return;
        }
        public void SetupShepherdData(String ListEntityId, String WeeklyReportEntityId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 包含 3 個SmallGroupData ( 小組牧養、新人跟進關懷、基本資料維護)
            // 而每個又包含一個Members陣列
            aListSmallGroupWeeklyReport.m_SmallGroupDataList = new SmallGroupDataList();

            // 取得基本資料
            this.GetAllMemeberDataList(ListEntityId, WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);

            // 待完成....
            // 
            if (aListSmallGroupWeeklyReport.GroupType.Contains("幸福") != true)
            {
                this.SetSmallGroupData(ref aListSmallGroupWeeklyReport);

                this.SetNewPersonFollowUpData(ref aListSmallGroupWeeklyReport);
            }
            else //"幸福小組"
            {
                this.SetHappyGroupData(ref aListSmallGroupWeeklyReport);
            }
            //this.SetHappyGroupData(ref aListSmallGroupWeeklyReport);

            EntityCollection aListEntityCollection = m_ToolUtilityClass.RetrieveListByFetchXml();

            aListSmallGroupWeeklyReport.GroupArray.Clear();
            foreach (Entity aList in aListEntityCollection.Entities)
            {
                aListSmallGroupWeeklyReport.GroupArray.Add(m_ToolUtilityClass.GetEntityStringAttribute(aList, "listname")); 
            }
                // }
                // 待完成....
                // 
                // if( 小組名稱不包含 "幸福" )
                // {
                //this.SetSmallGroupData(ref aListSmallGroupWeeklyReport);

                //this.SetNewPersonFollowUpData(ref aListSmallGroupWeeklyReport);
                // }
                // else "幸福小組"
                // {

                //this.SetHappyGroupData(ref aListSmallGroupWeeklyReport);

                // }
                #region 排序委身類型、並且去除掉數字、空白、逗號
                // 排序委身類型
                aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members !=null ? aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.OrderBy(o => o.Status).ToList() : null;
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members != null ? aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members.OrderBy(o => o.Status).ToList():null;
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.Members = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.Members != null ? aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.Members.OrderBy(o => o.Status).ToList() : null;
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.Members = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.Members != null ? aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.Members.OrderBy(o => o.Status).ToList() : null;
            // 去除掉數字、空白、逗號
            RemoveNumericAndBlank(aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members);
            RemoveNumericAndBlank(aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members);
            RemoveNumericAndBlank(aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.Members);
            RemoveNumericAndBlank(aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.Members);
            #endregion

        }
        public void SetupWeeklyReportData(String WeeklyReportEntityId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            if (aListSmallGroupWeeklyReport.GroupType == "幸福小組")
            {
                #region 這是幸福小組，所以要設定週次及主題
                if (WeeklyReportEntityId != "" && WeeklyReportEntityId != null)
                {
                    aListSmallGroupWeeklyReport.HappyWeekIndex = m_ToolUtilityClass.GetEntityStringAttribute(this.m_WeeklyReportEntity, "new_weekly_index");
                    aListSmallGroupWeeklyReport.HappyWeekTopic = this.ConvertIndexToTopic(m_ToolUtilityClass.GetOptionSetAttribute(this.m_WeeklyReportEntity, "new_topic"));
                }
                else
                {
                    aListSmallGroupWeeklyReport.HappyWeekIndex = "";
                    aListSmallGroupWeeklyReport.HappyWeekTopic = "";
                }
                #endregion
            }

            #region 無論是一般小組或是幸福小組，都要設定小組日誌、分析及暫停
            if ( WeeklyReportEntityId != "" && WeeklyReportEntityId != null)
            {
                aListSmallGroupWeeklyReport.WeeklyReportData = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_WeeklyReportEntity, "new_memo");
                aListSmallGroupWeeklyReport.WeeklyReportAnalysis = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_WeeklyReportEntity, "new_sunday_present_report");
                //取得"週報狀態"的暫停值
                aListSmallGroupWeeklyReport.PauseCheckBox = this.m_ToolUtilityClass.GetOptionSetAttribute(ref this.m_WeeklyReportEntity, "new_weekly_report_status") == 100000002 ? true:false ;
            }
            else
            {
                aListSmallGroupWeeklyReport.WeeklyReportData = "";
                aListSmallGroupWeeklyReport.WeeklyReportAnalysis = "";
                // //取得"週報狀態"的暫停值，因為還沒有此週報，所以先傳回沒暫停
                aListSmallGroupWeeklyReport.PauseCheckBox = false;
            }
            #endregion

        }
        public void SetupWeeklyReportChartData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            if (aListSmallGroupWeeklyReport.m_WeeklyReportChart == null)
            {
                aListSmallGroupWeeklyReport.m_WeeklyReportChart = new ChartDataList();
                if (aListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList == null)
                {
                    aListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList = new List<ChartData>();
                }
            }
            else
            {
                if (aListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList != null)
                {
                    aListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList.Clear();
                }
                else
                {
                    aListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList = new List<ChartData>();
                }
            }

            EntityCollection GroupWeeklyReportEntityCollection = this.m_ToolUtilityClass.QueryWeeklyReportBeforeTowMonthOfSunday(this.m_Sunday, this.m_ListEntity.Id);

            foreach (Entity aWeeklyReporEntity in GroupWeeklyReportEntityCollection.Entities)
            {
                int aSundayNumber = this.m_ToolUtilityClass.GetEntityIntAttribute(aWeeklyReporEntity, "new_sunday_present_number");
                int aSmallNumber = this.m_ToolUtilityClass.GetEntityIntAttribute(aWeeklyReporEntity, "new_small_group_number");
                aListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList.Add
                (
                    new ChartData
                    {
                        WeeklyReportEntityId = aWeeklyReporEntity.Id.ToString(),
                        SundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aWeeklyReporEntity, "new_sunday_date").ToLocalTime().ToShortDateString(),
                        SundayNumber = aSundayNumber >= 0 ? aSundayNumber : 0,
                        SmallNumber = aSmallNumber >= 0 ? aSmallNumber : 0,
                    }
                );
            }

        }
        public void GetAllMemeberDataList(String ListEntityId, String WeeklyReportEntityId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData = new SmallGroupData();
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members = new List<Member>();

            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.LoginType = aListSmallGroupWeeklyReport.LoginType;

            GroupWeeklyReportGuid aGroupWeeklyReportGuid = new GroupWeeklyReportGuid();

            //if (WeeklyReportEntityId != Guid.Empty.ToString())
            if (WeeklyReportEntityId != "")
            {
                #region 這個點名名單有找到主日周報，去找個人聚會與靈修記錄集合
                // 在 APP 中會呈現的小組名稱
                GetAllMemberDataFromPresentRecord(aListSmallGroupWeeklyReport.ListEntityName, new Guid(WeeklyReportEntityId), ref aListSmallGroupWeeklyReport);
                #endregion

            }
            else
            {
                #region 這個點名名單沒有找到主日周報， 找點名名單的小組組員做為要點名的清單
                if (m_LoginType == "小組長")
                {
                    GetAllMemberDataFromList(aListSmallGroupWeeklyReport.ListEntityName, new Guid(ListEntityId), ref aListSmallGroupWeeklyReport);
                    //GetSmallGroupLeaderMemberData(DisplayedGroupName, ListEntity.Id);
                }
                else
                {
                    SetAllMemberDataByPersonalReport(aListSmallGroupWeeklyReport.ListEntityName, ref aListSmallGroupWeeklyReport);
                }
                #endregion
            }
            return;
        }
        #endregion
        #region 副程式呼叫
        private void FindLoginUser(String Account, String Password)
    {
        // 找登入使用者及其ID
        if (Account != "LineIdLogin")
        {
            this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password);
        }
        else
        {
            this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);
        }

        this.m_ContactId = m_ContactEntity.Id;
    }
        private void RemoveNumericAndBlank( List<Member> aMemberList)
        {
            if (aMemberList != null)
            {
                foreach (Member aMember in aMemberList)
                {
                    // 去除掉數字、空白、逗號
                    aMember.Status = Regex.Replace(aMember.Status, "[0-9]", "");//過濾掉數字
                    aMember.Status = aMember.Status.Replace(" ", ""); // //過濾掉空白
                    aMember.Status = aMember.Status.Replace(".", ""); // //過濾掉逗號
                }
            }
        }
        private void GetAllMemberDataFromPresentRecord(String GroupName, Guid WeeklyReportId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 搜尋這個週報裡的所有個人聚會與靈修記錄集合
            EntityCollection PresentRecordCollection = GetPresentRecordByLoginType( GroupName,  WeeklyReportId, ref aListSmallGroupWeeklyReport);

            #region// 處理每個出席紀錄(個人聚會與靈修記錄集合)
            foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
            {
                // 每個出席紀錄(個人聚會與靈修記錄集合)
                if (PresentRecordEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = PresentRecordEntity.Attributes["statecode"] as OptionSetValue;

                    if ( aOptionState.Value == 0 && this.m_ToolUtilityClass.GetEntityBoolAttribute(PresentRecordEntity, "new_not_display") == false )
                    {
                        #region 只回傳使用中的每個出席紀錄
                        #region 填寫 MemberInfomation 所需要的每個欄位
                        #region// 出席紀錄組員的全名
                        String FullName = "";
                        EntityReference aFullNameEntityReference = new EntityReference();
                        if (PresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
                        {
                            aFullNameEntityReference = (EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"];

                            FullName = (string)aFullNameEntityReference.Name;
                        }
                        else
                        {
                            continue;
                        }
                        #endregion
                        #region// 依據紀錄組員的全名，找到手機號碼、家裡電話、地址、生日、職業及專長
                        Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);
                        // 組員的手機
                        String aMobilePhone = "";
                        if (aContactEntity.Attributes.Contains("mobilephone"))
                        {
                            aMobilePhone = (string)aContactEntity.Attributes["mobilephone"];
                        }
                        // 組員的家裡電話
                        String aHomePhone = "";
                        if (aContactEntity.Attributes.Contains("telephone2"))
                        {
                            aHomePhone = (string)aContactEntity.Attributes["telephone2"];
                        }
                        // 組員的地址
                        String aAddress = "";
                        if (aContactEntity.Attributes.Contains("address2_line1"))
                        {
                            aAddress = (string)aContactEntity.Attributes["address2_line1"];
                        }

                        // 組員的生日
                        DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContactEntity, "birthdate").ToLocalTime();

                        // 組員的職業及專長
                        String aIndustry = "";
                        if (aContactEntity.Attributes.Contains("new_industry"))
                        {
                            aIndustry = (string)aContactEntity.Attributes["new_industry"];
                        }

                        // 組員的裝備狀態
                        String aEquipmentStatus = "";
                        if (aContactEntity.Attributes.Contains("new_equipment_status"))
                        {
                            aEquipmentStatus = (string)aContactEntity.Attributes["new_equipment_status"];
                        }

                        // 組員的受洗狀態
                        String aSpiritualIdentity = "";
                        if (aContactEntity.Attributes.Contains("new_spiriitual_identity"))
                        {
                            aSpiritualIdentity = ConvertIndexToSpiritualIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(aContactEntity, "new_spiriitual_identity"));
                        }

                        // 組員的個人附註
                        String aDescription = "";
                        if (aContactEntity.Attributes.Contains("description"))
                        {
                            aDescription = (string)aContactEntity.Attributes["description"];
                        }
                        #endregion
                        #region// 委身類型
                        String aIdentity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode"));

                        //aIdentity = Regex.Replace(aIdentity, "[0-9]", "");//過濾掉數字
                        //aIdentity = aIdentity.Replace(" ", ""); // //過濾掉空白
                        //aIdentity = aIdentity.Replace(".", ""); // //過濾掉逗號

                        #endregion
                        #region// 出席紀錄組員的手機
                        String Telephone = "";
                        if (PresentRecordEntity.Attributes.Contains("new_cell_hpone"))
                        {
                            Telephone = (string)PresentRecordEntity.Attributes["new_cell_hpone"];
                        }
                        #endregion
                        #region// 出席紀錄組員的附註

                        // 神住611靈糧堂
                        String aNote = "";
                        //if (PresentRecordEntity.Attributes.Contains("new_name"))
                        //{
                        //    aNote = (string)PresentRecordEntity.Attributes["new_name"];
                        //}
                        if (PresentRecordEntity.Attributes.Contains("new_explanation"))
                        {
                            aNote = (string)PresentRecordEntity.Attributes["new_explanation"];
                        }
                        // 內壢神住611靈糧堂
                        //String aNote = "";
                        //if (PresentRecordEntity.Attributes.Contains("new_memo"))
                        //{
                        //    aNote = (string)PresentRecordEntity.Attributes["new_memo"];
                        //}
                        #endregion
                        #region// 主日點名
                        bool aSundayPresent = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_sunday_present_this_week") > 0 ? true : false;
                        #endregion
                        #region// 小組點名
                        bool aSmallGroupPresent = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_group_present_this_week") > 0 ? true : false;
                        #endregion
                        #region// 禱告會次數
                        bool aPrayerMeeting = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_prayer_meeting_number") > 0 ? true : false;
                        #endregion
                        #region// 門徒訓練班次數
                        bool aChild = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_child_number") > 0 ? true : false;
                        #endregion
                        #region// 門徒大聚次數
                        bool aBigDisciple = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_big_disciple_number") > 0 ? true : false;
                        #endregion
                        #region// 小組長小講堂次數
                        bool aLeadershipSmallLecture = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_leadership_small_lecture_number") > 0 ? true : false;
                        #endregion
                        #region// 小組長大聚次數
                        bool aLeadersGather = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_leaders_gather_number") > 0 ? true : false;
                        #endregion
                        #region// 決志
                        bool aDecision = this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_happy_decision") > 0 ? true : false;
                        #endregion
                        #region// 禱告次數，讀經次數
                        // 禱告次數
                        int aPrayNumber = 0;
                        if (PresentRecordEntity.Attributes.Contains("new_general_care"))
                        {
                            aPrayNumber = (int)PresentRecordEntity.Attributes["new_general_care"];
                        }
                        // 讀經次數
                        int aSpiritNumber = 0;
                        if (PresentRecordEntity.Attributes.Contains("new_spiritual_work"))
                        {
                            aSpiritNumber = (int)PresentRecordEntity.Attributes["new_spiritual_work"];
                        }
                        // 早禱
                        int aFamilyNumber = 0;
                        if (PresentRecordEntity.Attributes.Contains("new_morning_pray"))
                        {
                            aFamilyNumber = (int)PresentRecordEntity.Attributes["new_morning_pray"];
                        }
                        // 晚禱
                        int aWorkAndCampusNumber = 0;
                        if (PresentRecordEntity.Attributes.Contains("new_evening_pray"))
                        {
                            aWorkAndCampusNumber = (int)PresentRecordEntity.Attributes["new_evening_pray"];
                        }
                        #endregion
                        #region// 本週牧養狀態
                        // 本週牧養狀態(內壢神住611靈糧堂專用)
                        String aShepherdStatus = "";
                        if (PresentRecordEntity.Attributes.Contains("new_shepherd_situation"))
                        {
                            aShepherdStatus = (String)PresentRecordEntity.Attributes["new_shepherd_situation"];
                        }
                        //一對一牧養材料(內壢神住611靈糧堂專用)
                        String aOneOnOne = "";
                        if (PresentRecordEntity.Attributes.Contains("new_onebyone_situation"))
                        {
                            aOneOnOne = (String)PresentRecordEntity.Attributes["new_onebyone_situation"];
                        }
                        // 培訓系統選項(內壢神住611靈糧堂專用)
                        String aTraining = "";
                        if (PresentRecordEntity.Attributes.Contains("new_training_system"))
                        {
                            aTraining = (String)PresentRecordEntity.Attributes["new_training_system"];
                        }
                        // 裝備課程的英文名字可能是有點取錯了可是因為表單已經取了，就先將錯就錯先了
                        // 裝備課程(內壢神住611靈糧堂專用)
                        String aIncubate = "";
                        if (PresentRecordEntity.Attributes.Contains("new_equipment_class"))
                        {
                            aIncubate = (String)PresentRecordEntity.Attributes["new_equipment_class"];
                        }
                        #endregion
                        #region// 新人跟進週次、結果、下一步驟、歷程記錄

                        //新人跟進週次
                        String aFollowUpWeek = "";
                        if (PresentRecordEntity.Attributes.Contains("new_weeks"))
                        {
                            int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_weeks");
                            aFollowUpWeek = ConvertIndexToFollowUpWeekPicker(OptionValue);
                        }

                        //新人跟進結果
                        String aFollowUpResult = "";
                        if (PresentRecordEntity.Attributes.Contains("new_conclusion_choise"))
                        {
                            int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_conclusion_choise");
                            aFollowUpResult = ConvertIndexToFollowUpResultPicker(OptionValue);
                        }

                        //新人跟進下一步驟
                        String aFollowUpNextStep = "";
                        if (PresentRecordEntity.Attributes.Contains("new_next_step"))
                        {
                            int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_next_step");
                            aFollowUpNextStep = ConvertIndexToFollowUpNextStepPicker(OptionValue);
                        }

                        // 跟進方式選項
                        String aFollowUpOption = "";
                        if (PresentRecordEntity.Attributes.Contains("new_followup_ways"))
                        {
                            int OptionValue = this.m_ToolUtilityClass.GetOptionSetAttribute(PresentRecordEntity, "new_followup_ways");
                            aFollowUpOption = ConvertIndexToFollowUpOptionPicker(OptionValue);
                        }

                        // 跟進方式
                        String aFollowUp = "";
                        if (PresentRecordEntity.Attributes.Contains("new_follow_up"))
                        {
                            aFollowUp = (String)PresentRecordEntity.Attributes["new_follow_up"];
                        }

                        // 備註
                        String aFollowUpNote = "";
                        if (PresentRecordEntity.Attributes.Contains("new_explanation"))
                        {
                            aFollowUpNote = (String)PresentRecordEntity.Attributes["new_explanation"];
                        }


                        // 取得新人跟進週次，及跟進歷程記錄
                        String aNewComerNote = GetNewComerFollowupInfo(aFullNameEntityReference.Id, ref aFollowUpWeek);

                        #endregion

                        #region 靈修、晨、晚禱
                        // 讀經次數
                        int aSpiritualWork = 0;
                        if (PresentRecordEntity.Attributes.Contains("new_spiritual_work"))
                        {
                            aSpiritualWork = (int)PresentRecordEntity.Attributes["new_spiritual_work"];
                        }
                        // 晨禱
                        int aMorningPray = 0;
                        if (PresentRecordEntity.Attributes.Contains("new_morning_pray"))
                        {
                            aMorningPray = (int)PresentRecordEntity.Attributes["new_morning_pray"];
                        }
                        // 晚禱次數
                        int aGeneralCare = 0;
                        if (PresentRecordEntity.Attributes.Contains("new_general_care"))
                        {
                            aGeneralCare = (int)PresentRecordEntity.Attributes["new_general_care"];
                        }

                        #endregion
                        #endregion

                        #region 傳回給手機的資料
                        //10.未入組結案" 不用進入 APP
                        if (aIdentity != "10. 未入組結案")
                        {
                            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add
                            (
                                new Member
                                {
                                    PresentRecordId = PresentRecordEntity.Id.ToString(),
                                    Group = GroupName,
                                    FullName = FullName,
                                    #region 個人基本資料
                                    Phone = DigitsOnly.Replace(aMobilePhone, ""),
                                    HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                                    Address = aAddress,
                                    BirthDate = aBirthDate,
                                    Industry = aIndustry,
                                    EquipmentStatus = aEquipmentStatus,
                                    SpiritualIdentity = aSpiritualIdentity,
                                    BestLeader = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aContactEntity, "new_contact_contact_spiritleader"),// 屬靈認領者
                                    BestIntroducer = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "new_best_introducer"),
                                    BestRelationship = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "new_best_relationship"),
                                    #endregion
                                    Status = aIdentity, // 委身類型
                                    SmallGroupName = GroupName,
                                    SectionName = GroupName,
                                    PrayItem = aNote,
                                    Sunday = aSundayPresent, //主日出席
                                    SmallGroup = aSmallGroupPresent,//小組出席
                                    PrayerMeeting = aPrayerMeeting,// 禱告會次數
                                    Child = aChild,// 門徒訓練班次數
                                    BigDisciple = aBigDisciple,// 門徒大聚次數
                                    LeadershipSmallLecture = aLeadershipSmallLecture,// 小組長小講堂次數
                                    LeadersGather = aLeadersGather,// 小組長大聚次數
                                    Decision = aDecision, //決志
                                    Description = aDescription,
                                    #region 新人跟進關懷
                                    FollowUpWeek = aFollowUpWeek,
                                    FollowUpResult = aFollowUpResult,
                                    FollowUpOption = aFollowUpOption,
                                    FollowUp = aFollowUp,
                                    FollowUpNextStep = aFollowUpNextStep,
                                    FollowUpNote = aFollowUpNote,
                                    NewComerNote = aNewComerNote,
                                    #endregion
                                    #region 靈修、晨、晚禱
                                    SpiritualWork = aSpiritualWork, // 讀經次數
                                    MorningPray = aMorningPray, // 晨禱(家庭祭壇)
                                    GeneralCare = aGeneralCare, // 晚禱(禱告會次數)
                                    #endregion
                                }
                            );
                        }
                        #endregion

                        #endregion
                    }
                    else
                    {
                        //String StateCode = "非使用中";
                    }
                }
            }
            #endregion

            return;
        }
        private EntityCollection GetPresentRecordByLoginType(String GroupName, Guid WeeklyReportId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 搜尋這個週報裡的所有個人聚會與靈修記錄集合
            EntityCollection PresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", WeeklyReportId.ToString(), "new_group_present_weekly_report_prese", "new_present_record");
            if (this.m_LoginType == "小組長")
            {
                return PresentRecordCollection;
            }
            else
            {
                //個人回報所以僅傳回對應到個人回報的出席紀錄單即可
                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    if( this.m_ContactId == this.m_ToolUtilityClass.GetEntityLookupAttribute(PresentRecordEntity, "new_contact_new_present_record"))
                    {
                        EntityCollection LocalPresentRecordCollection = new EntityCollection();

                        LocalPresentRecordCollection.Entities.Add(PresentRecordEntity);

                        return LocalPresentRecordCollection;
                    }
                }
            }

            // 個人回報，沒有找到對應的出席紀錄單，那就新增一個
            return CreatePresentRecordList(GroupName, ref this.m_ListEntity, ref WeeklyReportId, 0, 0, 0, 0, 0);
        }
        private EntityCollection CreatePresentRecordList(String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, Double aWeeklySundayRate, Double aWeeklySmallGroupRate, int aWeeklySundayNumber, int aWeeklySmallGroupNumber)
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
        private Entity CreatePresentRecord(Member aMemberInfomation, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            // 必須是名單裡的人，不能是只有依據姓名更新
            Entity aContactEntity = UpdateContactInfomationFromList(aMemberInfomation.FullName, aListEntity.Id);
            //Entity aContactEntity = m_ToolUtilityClass.RetrieveContactEntityByName(aMemberInfomation.Name);
            //Entity aSearchedContactEntity = m_ToolUtilityClass.RetrieveContactByNameAndMobile(ref m_ToolUtilityClass.m_OrganizationService, aMemberInfomation.Name, aMemberInfomation.Phone );

            Entity aToUpdateContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactEntity.Id);
            //Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aSearchedContactEntity.Id);

            if (aContactEntity != null)
            {
                // 這是新建立的個人聚會與靈修記錄
                Entity aPresentRecord = new Entity("new_present_record");

                // 設定個人聚會與靈修記錄相關屬性
                this.SetupPresentRecordEntityAttributes(aPresentRecord, aMemberInfomation, ref aContactEntity, ref aListEntity, ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber);

                // 新增個人聚會與靈修記錄
                Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);
                Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);

                //指派負責人
                this.m_ToolUtilityClass.AssignOwner("new_present_record", aRetrievedPresentRecord, this.m_ToolUtilityClass.GetOwnerId( aContactEntity ));

                //取得並回傳新建的聚會與靈修記錄
                return aRetrievedPresentRecord;

            }
            else
            {
                return null;
            }
        }


        private Member CreateMember(String GroupName)
        {
            return new Member
            {
                PresentRecordId = ".......",
                Group = GroupName,
                FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "fullname"),
                #region 個人基本資料

                Phone = DigitsOnly.Replace(this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "mobilephone"), ""),
                HomePhone = DigitsOnly.Replace(this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "telephone2"), ""),
                Address = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "address2_line1"),
                BirthDate = m_ToolUtilityClass.GetEntityDateTimeAttribute(this.m_ContactEntity, "birthdate"),
                Industry = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "new_industry"),
                EquipmentStatus = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "new_equipment_status"),
                SpiritualIdentity = ConvertIndexToSpiritualIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(this.m_ContactEntity, "new_spiriitual_identity")),

                #endregion
                Status = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref m_ContactEntity, "customertypecode")), // 委身類型
                SmallGroupName = GroupName,
                SectionName = GroupName,
                PrayItem = "",
                Sunday = false, //主日出席
                SmallGroup = false,//小組出席
                Decision = false, //決志
                #region 新人跟進關懷
                FollowUpWeek = "未選擇",
                FollowUpResult = "",
                FollowUpOption = "",
                FollowUp = "",
                FollowUpNextStep = "",
                FollowUpNote = "",
                NewComerNote = "",
                #endregion
                #region 靈修、晨、晚禱
                SpiritualWork = 0, // 讀經次數
                MorningPray = 0, // 晨禱(家庭祭壇)
                GeneralCare = 0, // 晚禱(禱告會次數)
                #endregion
            };

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

        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, Member aMemberInfomation, ref Entity aContactEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber)
        {
            try
            {
                #region 設定名稱
                String PresentRecordName = aMemberInfomation.FullName + String.Format("-{0:00}/{1:00}/{2:00} 出席紀錄", this.m_Sunday.Year, this.m_Sunday.Month, this.m_Sunday.Day);
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
                #region 從名單取得 區名、小家長 ID、小組長 ID、小家長、上代族系族長長 ID
                // 小家長 ID
                Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 小家長 ID
                Guid aRaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                // 上代族系族長長 ID
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
                #region 關聯上代族系族長長屬性
                if (aShepherdLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_arealeader_present_record", "contact", aShepherdLeaderId); }
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
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_place", this.m_ToolUtilityClass.GetEntityStringAttribute(aListEntity, "new_group_place"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_time", this.m_ToolUtilityClass.GetEntityStringAttribute(aListEntity, "new_group_time"));
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
                if (aMemberInfomation.Sunday == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);
                }
                #endregion
                #region 設定主日出席率
                if (aMemberInfomation.Sunday == true)
                {
                    if (ValidNumber > 0)
                    {
                        this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 1 / ValidNumber);
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);
                }
                #endregion
                #region 設定小組出席
                if (aMemberInfomation.SmallGroup == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 1);

                    aWeeklySmallGroupNumber += 1;
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);
                }
                #endregion
                #region 設定小組出席率
                if (aMemberInfomation.SmallGroup == true)
                {
                    if (ValidNumber > 0)
                    {
                        this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 1 / ValidNumber);
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);
                }
                #endregion





                #region 禱告會次數
                if (aMemberInfomation.PrayerMeeting == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_prayer_meeting_number", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_prayer_meeting_number", 0);
                }
                #endregion

                #region 門徒訓練班次數
                if (aMemberInfomation.Child == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_child_number", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_child_number", 0);
                }
                #endregion

                #region 門徒大聚次數
                if (aMemberInfomation.BigDisciple == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_big_disciple_number", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_big_disciple_number", 0);
                }
                #endregion

                #region 小組長小講堂次數
                if (aMemberInfomation.LeadershipSmallLecture == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leadership_small_lecture_number", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leadership_small_lecture_number", 0);
                }
                #endregion

                #region 小組長大聚次數
                if (aMemberInfomation.Sunday == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leaders_gather_number", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leaders_gather_number", 0);
                }
                #endregion

                #region 設定附註或是代禱事項

                // 神住611靈糧堂
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", aMemberInfomation.Note);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.PrayItem);

                // 內壢神住611靈糧堂
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region// 新人跟進

                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMemberInfomation.FollowUpWeek));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMemberInfomation.FollowUpResult));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMemberInfomation.FollowUpNextStep));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_follow_up", aMemberInfomation.FollowUpOption);

                // 因為之前APP無法直接把代禱事項和新人跟進關懷用在表單中
                // 但是網頁現在可以了
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMemberInfomation.FollowUpNote);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.PrayItem);

                #endregion
                #region// 讀經次數

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
                case "考慮中，繼續跟進":
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

        private void GetAllMemberDataFromList(String GroupName, Guid ListEntityId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
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

            int PresentRecordIdCounter = 0;
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

                        #region// 依據紀錄組員的全名，找到手機號碼、家裡電話、地址、生日、職業及專長
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
                        DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref ContactEntity, "birthdate").ToLocalTime();

                        // 組員的職業及專長
                        String aIndustry = "";
                        if (ContactEntity.Attributes.Contains("new_industry"))
                        {
                            aIndustry = (string)ContactEntity.Attributes["new_industry"];
                        }

                        // 組員的裝備狀態
                        String aEquipmentStatus = "";
                        if (ContactEntity.Attributes.Contains("new_equipment_status"))
                        {
                            aEquipmentStatus = (string)ContactEntity.Attributes["new_equipment_status"];
                        }

                        // 組員的受洗狀態
                        String aSpiritualIdentity = "";
                        if (ContactEntity.Attributes.Contains("new_spiriitual_identity"))
                        {
                            aSpiritualIdentity = ConvertIndexToSpiritualIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ContactEntity, "new_spiriitual_identity"));
                        }

                        // 組員的個人附註
                        String aDescription = "";
                        if ( ContactEntity.Attributes.Contains("description"))
                        {
                            aDescription = (string) ContactEntity.Attributes["description"];
                        }
                        #endregion

                        #region// 委身類型
                        String aIdentity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref ContactEntity, "customertypecode"));

                        // 去除掉數字、空白、逗號
                        //aIdentity = Regex.Replace(aIdentity, "[0-9]", "");//過濾掉數字
                        //aIdentity = aIdentity.Replace(" ", ""); // //過濾掉空白
                        //aIdentity = aIdentity.Replace(".", ""); // //過濾掉逗號

                        #endregion


                        // 取得新人跟進週次，及跟進歷程記錄
                        String aFollowUpWeek = "未選擇";
                        String aNewComerNote = GetNewComerFollowupInfo(ContactEntity.Id, ref aFollowUpWeek);


                        #region 傳回給網頁的資料
                        //10.未入組結案" 不用進入 APP
                        if (aIdentity != "10. 未入組結案")
                        {
                            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add
                            (
                                new Member
                                {
                                    PresentRecordId = PresentRecordIdCounter++.ToString(),
                                    Group = GroupName,
                                    FullName = FullName,
                                    #region 個人基本資料

                                    Phone = DigitsOnly.Replace(aMobilePhone, ""),
                                    HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                                    Address = aAddress,
                                    BirthDate = aBirthDate,
                                    Industry = aIndustry,
                                    EquipmentStatus = aEquipmentStatus,
                                    SpiritualIdentity = aSpiritualIdentity,
                                    BestLeader = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ContactEntity, "new_contact_contact_spiritleader"),// 屬靈認領者
                                    BestIntroducer = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "new_best_introducer"),
                                    BestRelationship = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "new_best_relationship"),
                                    #endregion
                                    Status = aIdentity, // 委身類型
                                    SmallGroupName = GroupName,
                                    SectionName = GroupName,
                                    PrayItem = "",
                                    Sunday = false, //主日出席
                                    SmallGroup = false,//小組出席
                                    Decision = false, //決志
                                    Description = aDescription,
                                    #region 新人跟進關懷
                                    FollowUpWeek = aFollowUpWeek,
                                    FollowUpResult = "",
                                    FollowUpOption = "",
                                    FollowUp = "",
                                    FollowUpNextStep = "",
                                    FollowUpNote = "",
                                    NewComerNote = aNewComerNote,
                                    #endregion
                                    #region 靈修、晨、晚禱
                                    SpiritualWork = 0, // 讀經次數
                                    MorningPray = 0, // 晨禱(家庭祭壇)
                                    GeneralCare = 0, // 晚禱(禱告會次數)
                                    #endregion
                                }
                            );
                        }
                        #endregion

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
        private void SetAllMemberDataByPersonalReport(String GroupName, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            #region 只回傳使用中的組員

            #region// 依據紀錄組員的全名，找到手機號碼、家裡電話、地址、生日、職業及專長
            // 組員的全名
            String FullName = "";
            if ( this.m_ContactEntity.Attributes.Contains("fullname"))
            {
                FullName = (string)m_ContactEntity.Attributes["fullname"];
            }
            // 組員的手機
            String aMobilePhone = "";
            if (m_ContactEntity.Attributes.Contains("mobilephone"))
            {
                aMobilePhone = (string)m_ContactEntity.Attributes["mobilephone"];
            }
            // 組員的家裡電話
            String aHomePhone = "";
            if (m_ContactEntity.Attributes.Contains("telephone2"))
            {
                aHomePhone = (string)m_ContactEntity.Attributes["telephone2"];
            }
            // 組員的地址
            String aAddress = "";
            if (m_ContactEntity.Attributes.Contains("address2_line1"))
            {
                aAddress = (string)m_ContactEntity.Attributes["address2_line1"];
            }

            // 組員的生日
            DateTime aBirthDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref m_ContactEntity, "birthdate").ToLocalTime();

            // 組員的職業及專長
            String aIndustry = "";
            if (m_ContactEntity.Attributes.Contains("new_industry"))
            {
                aIndustry = (string)m_ContactEntity.Attributes["new_industry"];
            }

            // 組員的裝備狀態
            String aEquipmentStatus = "";
            if (m_ContactEntity.Attributes.Contains("new_equipment_status"))
            {
                aEquipmentStatus = (string)m_ContactEntity.Attributes["new_equipment_status"];
            }

            // 組員的受洗狀態
            String aSpiritualIdentity = "";
            if (m_ContactEntity.Attributes.Contains("new_spiriitual_identity"))
            {
                aSpiritualIdentity = ConvertIndexToSpiritualIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(this.m_ContactEntity, "new_spiriitual_identity"));
            }

            // 組員的個人附註
            String aDescription = "";
            if ( m_ContactEntity.Attributes.Contains("description"))
            {
                aDescription = (string)m_ContactEntity.Attributes["description"];
            }
            #endregion

            #region// 委身類型
            String aIdentity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref m_ContactEntity, "customertypecode"));

            // 去除掉數字、空白、逗號
            //aIdentity = Regex.Replace(aIdentity, "[0-9]", "");//過濾掉數字
            //aIdentity = aIdentity.Replace(" ", ""); // //過濾掉空白
            //aIdentity = aIdentity.Replace(".", ""); // //過濾掉逗號

            #endregion


            // 取得新人跟進週次，及跟進歷程記錄
            String aFollowUpWeek = "未選擇";
            String aNewComerNote = GetNewComerFollowupInfo(m_ContactEntity.Id, ref aFollowUpWeek);


            #region 傳回給網頁的資料
            //10.未入組結案" 不用進入 APP
            if (aIdentity != "10. 未入組結案")
            {
                aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add
                (
                    new Member
                    {
                        PresentRecordId = DateTime.Now.ToLongTimeString().ToString(),
                        Group = GroupName,
                        FullName = FullName,
                        #region 個人基本資料

                        Phone = DigitsOnly.Replace(aMobilePhone, ""),
                        HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                        Address = aAddress,
                        BirthDate = aBirthDate,
                        Industry = aIndustry,
                        EquipmentStatus = aEquipmentStatus,
                        SpiritualIdentity = aSpiritualIdentity,
                        BestLeader = this.m_ToolUtilityClass.GetEntityLookupDisplayName(m_ContactEntity, "new_contact_contact_spiritleader"),// 屬靈認領者
                        BestIntroducer = this.m_ToolUtilityClass.GetEntityStringAttribute(m_ContactEntity, "new_best_introducer"),
                        BestRelationship = this.m_ToolUtilityClass.GetEntityStringAttribute(m_ContactEntity, "new_best_relationship"),

                        #endregion
                        Status = aIdentity, // 委身類型
                        SmallGroupName = GroupName,
                        SectionName = GroupName,
                        PrayItem = "",
                        Sunday = false, //主日出席
                        SmallGroup = false,//小組出席
                        Decision = false, //決志
                        Description = aDescription,
                        #region 新人跟進關懷
                        FollowUpWeek = aFollowUpWeek,
                        FollowUpResult = "",
                        FollowUpOption = "",
                        FollowUp = "",
                        FollowUpNextStep = "",
                        FollowUpNote = "",
                        NewComerNote = aNewComerNote,
                                    #endregion
                        #region 靈修、晨、晚禱
                                    SpiritualWork = 0, // 讀經次數
                                    MorningPray = 0, // 晨禱(家庭祭壇)
                                    GeneralCare = 0, // 晚禱(禱告會次數)
                                    #endregion
                    }
                );
            }
            #endregion

            #endregion
        }
        private void SetSmallGroupData( ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData = new SmallGroupData();
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members = new List<Member>();

            foreach (Member aMember in aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members)
            {
                if (aMember.Status.Contains("牧師師母") || aMember.Status.Contains("小家長") || aMember.Status.Contains("上代族系族長") || aMember.Status.Contains("小組長") || aMember.Status.Contains("門徒") || aMember.Status.Contains("小組組員"))
                {
                    aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members.Add(aMember);
                }
            }

            // 控制牧養點名回報是否顯示
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.DisplayFlag = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members.Count > 0 ? true : false;
            //aListSmallGroupWeeklyReport.SmallGroupDisplayFlag = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members.Count > 0 ? true : false;
        }
        private void SetNewPersonFollowUpData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData = new SmallGroupData();
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.Members = new List<Member>();

            foreach (Member aMember in aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members)
            {
                if (aMember.Status.Contains("新朋友") || aMember.Status.Contains("未入組"))
                {
                    aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.Members.Add(aMember);
                }
            }
            // 控制新人跟進關懷點名回報是否顯示
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.DisplayFlag = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.Members.Count > 0 ? true : false;
            //aListSmallGroupWeeklyReport.NewPersonFollowUpDisplayFlag = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members.Count > 0 ? true : false;
        }
        private void SetHappyGroupData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup = new SmallGroupData();
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.Members = new List<Member>();

            foreach (Member aMember in aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members)
            {
                //if (aMember.Status.Contains("新朋友") || aMember.Status.Contains("未入組"))
                //{
                    aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.Members.Add(aMember);
                //}
            }
            // 控制新人跟進關懷點名回報是否顯示
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.DisplayFlag = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.Members.Count > 0 ? true : false;
            //aListSmallGroupWeeklyReport.NewPersonFollowUpDisplayFlag = aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members.Count > 0 ? true : false;
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
                            DateTime aSunday = new DateTime();
                            // 每周以星期六為第一日
                            if (DayOfWeek < 6)
                            {
                                // 小於 6 表示星期日到星期五=>當週的星期日為認定的主日
                                aSunday = DateTime.Now.AddDays(-DayOfWeek);
                            }
                            else
                            {
                                // 為 6 = 星期六 (表示 DayOfWeek.Saturday)表示要加1到下一個星期日為認定的主日
                                aSunday = DateTime.Now.AddDays(1);
                            }
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
                // 委身類型客製化
                // 確認是否是新人或是未入組
                int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

                //case 100000006:
                //    return "01. 牧師師母";
                //case 100000009:
                //    return "02. 上代族系族長";
                //case 100000003:
                //    return "03. 小家長";
                //case 100000008:
                //    return "04. 小組長";
                //case 100000002:
                //    return "05. 實習小組長";
                //case 1:
                //    return "06. 小組組員";
                //case 100000005:
                //    return "07. 幸福BEST";
                //case 100000004:
                //    return "08. 未入組";
                //case 100000000:
                //    return "09. 新朋友";
                //case 100000007:
                //    return "10. 外教會";
                //case 100000001:
                //    return "11. 結案";

                // 委身類型客製化
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
                //EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySunday("contact", "contactid", aContact.Id.ToString(), "new_contact_new_present_record", "new_present_record");
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySundayFetchXml(10, this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname"), aContact.Id.ToString());

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

                    //Entity PresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordEntity.Id);

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
                //EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySunday("contact", "contactid", aContact.Id.ToString(), "new_contact_new_present_record", "new_present_record");
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.QueryPresentRecordSortBySundayFetchXml(10, this.m_ToolUtilityClass.GetEntityStringAttribute(aContact, "fullname"), aContact.Id.ToString());

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

                    if ( TRANSFER_IDENTITY_FLAG == true )
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
                    return "考慮中，繼續跟進";
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
        private String ConvertIndexToFollowUpOptionPicker(int FollowUpWays )
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

        private String ConvertIndexToTopic(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 100000000:
                    return "預備週";
                case 100000001:
                    return "真幸福";
                case 100000002:
                    return "真相大白";
                case 100000003:
                    return "萬世巨星";
                case 100000004:
                    return "幸福連線";
                case 100000005:
                    return "當上帝來敲門";
                case 100000006:
                    return "十字架的勝利";
                case 100000007:
                    return "釋放與自由";
                case 100000008:
                    return "幸福的教會";
                default:
                    return "";
            }
        }

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
                if (aIdentityType == "07. 未入組" || aIdentityType == "08. 新朋友")
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
                else if (aIdentityType == "05. 小組組員")
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


        // 委身類型客製化，委身類型客製化
        //神住611靈糧堂
        private String ConvertIndexToIdentity(int Identity)
        {
            switch (Identity)
            {
                case 100000006:
                    return "01. 牧師師母";
                case 100000002:
                    return "011. 上代族系族長";
                case 100000003:
                    return "02. 小家長";
                case 100000008:
                    return "03. 小組長";
                case 100000012:
                    return "04. 門徒";
                case 1:
                    return "05. 小組組員";
                case 100000005:
                    return "06. 幸福BEST";
                case 100000004:
                    return "07. 未入組";
                case 100000000:
                    return "08. 新朋友";
                case 100000007:
                    return "09. 外教會";
                case 100000001:
                    return "10. 結案";
                default:
                    return ".";
            }
        }
        private String ConvertIndexToSpiritualIdentity_BACKUP(int SpiritualIdentity)
        {
            switch (SpiritualIdentity)
            {
                case 100000004:
                    return "-未知-";
                case 100000001:
                    return "基督徒";
                case 100000002:
                    return "已決志";
                case 100000005:
                    return "慕道友";
                case 100000003:
                    return "未信主";
                default:
                    return ".";
            }
        }
        private String ConvertIndexToSpiritualIdentity(int SpiritualIdentity)
        {
            switch (SpiritualIdentity)
            {
                case 100000004:
                    return "-未知-";
                case 100000001:
                    return "基督徒";
                case 100000002:
                    return "已決志";
                case 100000005:
                    return "慕道友";
                case 100000003:
                    return "未信主";
                default:
                    return ".";
            }
        }
        #endregion
    }
}
