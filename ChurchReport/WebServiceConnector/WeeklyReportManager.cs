using ChurchReport.Models.CrmTransmitModule;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    public class WeeklyReportManager
    {
        #region 資料區
        #region 參數資料
        ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private LineNotifyUtility m_LineNotifyUtility = new LineNotifyUtility();

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();
        #endregion

        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365";

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

        DateTime m_Sunday;
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給族系族長/小家長的名單

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
        //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        //List<Place2> m_GroupNamePlaces = new List<Place2>(); // 依據群組名稱過濾出來的會眾集合
        List<MemberInfomation> m_GroupNamedListMemberInfomation = new List<MemberInfomation>(); // 依據群組名稱過濾出來的會眾集合
        #endregion

        #region 週報管理
        #region WCF Service端

        #region 下載週報
        public WeeklyReport DownloadWeeklyReport(AccountPasswordData aAccountPasswordData, DateTime aDownloadDate)
        {
            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "下載週報");
            WeeklyReport aWeeklyReport = new WeeklyReport();

            #region 先根據日期尋找當週主日日期
            // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
            int DayOfWeek = (int)aDownloadDate.DayOfWeek;
            this.m_Sunday = aDownloadDate.AddDays(-DayOfWeek);
            #endregion

            #region 找小組長及其ID
            FindGroupLeader(aAccountPasswordData);
            if (m_ContactId == Guid.Empty) //是否有找到小組長的ID
            { return null; } // 沒找到就回傳 null 
            #endregion

            #region 先尋找帶領族系名單，若找到表示就是族系族長，若沒有則在繼續尋找帶領小組名單
            FindListCollectionForWeeklyReport();
            if (this.m_PresentLists.Entities.Count == 0)
            {
                return aWeeklyReport;// 沒找到任何要點名的名單 
            }
            #endregion

            #region 取得該組的小組日誌
            SetupWeeklyReport(ref aWeeklyReport);
            #endregion
            #region 取得整個族系或小組的出席紀錄報告
            SetupPresentReport(ref aWeeklyReport);
            #endregion

            return aWeeklyReport;
        }


        private void SetupWeeklyReport(ref WeeklyReport aWeeklyReport)
        {
            try
            {
                // 處理每個點名名單
                if (this.m_Lists.Entities.Count > 0)
                {
                    foreach (Entity ListEntity in this.m_Lists.Entities)
                    {
                        // 取得每個需要點名的名單裡的每個週報
                        EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");

                        // 根據日期看有沒有那個週報
                        //Entity GroupWeeklyReportEntity = FilterWeeklyReportByDate(ref GroupWeeklyReportEntityCollection);
                        Entity GroupWeeklyReportEntity = FilterWeeklyReportByDateAndGroupLeader(ref GroupWeeklyReportEntityCollection);

                        //依據找到的週報有還是沒有來決定下一步:  
                        //      有: 建立GroupName及WeeklyReportId
                        //    沒有: 建立GroupName及WeeklyReportId = Guid.Empty();
                        if (GroupWeeklyReportEntity != null)
                        {
                            #region 內壢神住611靈糧堂
                            //if (( aWeeklyReport.ReligiousInvestigator = this.m_ToolUtilityClass.GetEntityIntAttribute(ref GroupWeeklyReportEntity, "new_number_of_seekers")) < 0 )
                            //{
                            //    aWeeklyReport.ReligiousInvestigator = 0;
                            //}
                            //
                            //if(( aWeeklyReport.Baptized = this.m_ToolUtilityClass.GetEntityIntAttribute(ref GroupWeeklyReportEntity, "new_predict_to_be_baptized")) < 0 )
                            //{
                            //    aWeeklyReport.Baptized = 0;
                            //}
                            //
                            //if(( aWeeklyReport.FollowNumber = this.m_ToolUtilityClass.GetEntityIntAttribute(ref GroupWeeklyReportEntity, "new_times_of_followup")) < 0 )
                            //{
                            //    aWeeklyReport.FollowNumber = 0;
                            //}
                            //
                            //aWeeklyReport.PushMethod = this.m_ToolUtilityClass.GetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_push_mode");
                            //aWeeklyReport.ProgressMethod = this.m_ToolUtilityClass.GetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_methods_and_number");
                            //aWeeklyReport.OneOnOne = this.m_ToolUtilityClass.GetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_onebynoe_and_number");
                            #endregion
                            #region 楊梅神住611靈糧堂
                            // 小組日誌
                            aWeeklyReport.WeeklyReportContent = this.m_ToolUtilityClass.GetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_memo");
                            #endregion
                        }
                        else
                        {
                            #region 內壢神住611靈糧堂
                            //aWeeklyReport.ReligiousInvestigator = 0;
                            //aWeeklyReport.Baptized = 0;
                            //aWeeklyReport.FollowNumber = 0;
                            //aWeeklyReport.ProgressMethod = "還未建立小家回報單";
                            //aWeeklyReport.PushMethod = "還未建立小家回報單";
                            //aWeeklyReport.OneOnOne = "還未建立小家回報單";
                            #endregion
                            #region 楊梅神住611靈糧堂
                            // 小組日誌
                            //aWeeklyReport.WeeklyReportContent = "還沒有點過名，所以沒有小組日誌，請先點過名之後，才能上傳小組日誌";
                            //aWeeklyReport.WeeklyReportContent = "沒有週報資料，您可能是小家長，但不是小組長，所以沒有小組長日誌需要回報";
                            #endregion
                        }
                    }
                }
                else
                {
                    //aWeeklyReport.WeeklyReportContent = "沒有週報資料，您可能是小家長，但不是小組長，所以沒有小組長日誌需要回報";
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void SetupPresentReport(ref WeeklyReport aWeeklyReport)
        {
            try
            {
                // 處理每個點名名單
                String PresentReport = "";
                foreach (Entity ListEntity in this.m_PresentLists.Entities)
                {
                    // 取得每個需要點名的名單裡的每個週報
                    EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");

                    // 根據日期看有沒有那個週報
                    Entity GroupWeeklyReportEntity = FilterWeeklyReportByDate(ref GroupWeeklyReportEntityCollection);

                    //依據找到的週報有還是沒有來決定下一步:  
                    //      有: 建立GroupName及WeeklyReportId
                    //    沒有: 建立GroupName及WeeklyReportId = Guid.Empty();
                    if (GroupWeeklyReportEntity != null)
                    {
                        #region 神住611靈糧堂

                        // 出席紀錄
                        //aWeeklyReport.PresentContent = this.m_ToolUtilityClass.GetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_sunday_present_report");
                        String aLocalPresentReport = this.m_ToolUtilityClass.GetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_sunday_present_report");

                        if (aLocalPresentReport != "")
                        {
                            PresentReport += aLocalPresentReport;

                            // 小組日誌
                            String aLocalWeeklyReport = this.m_ToolUtilityClass.GetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_memo");
                            if (aLocalWeeklyReport != "")
                            {
                                String WeeklyReportName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref GroupWeeklyReportEntity, "new_list_group_present_weekly_report");
                                WeeklyReportName = Regex.Replace(WeeklyReportName, "[0-9]", "");//過濾掉數字
                                WeeklyReportName = WeeklyReportName.Replace(" ", ""); // //過濾掉空白

                                PresentReport += Environment.NewLine + WeeklyReportName + " 小組日誌" + Environment.NewLine;
                                PresentReport += this.m_ToolUtilityClass.GetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_memo");
                                PresentReport += Environment.NewLine + "******************************" + Environment.NewLine;
                            }

                        }
                        else
                        {
                            String WeeklyReportName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref GroupWeeklyReportEntity, "new_list_group_present_weekly_report");
                            WeeklyReportName = Regex.Replace(WeeklyReportName, "[0-9]", "");//過濾掉數字
                            WeeklyReportName = WeeklyReportName.Replace(" ", ""); // //過濾掉空白

                            PresentReport += WeeklyReportName + " ，還沒有點名" + Environment.NewLine;

                            // 小組日誌
                            String aLocalWeeklyReport = this.m_ToolUtilityClass.GetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_memo");
                            if (aLocalWeeklyReport != "")
                            {
                                PresentReport += Environment.NewLine + WeeklyReportName + " 小組日誌" + Environment.NewLine;
                                PresentReport += this.m_ToolUtilityClass.GetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_memo");
                                //PresentReport += Environment.NewLine + "******************************" + Environment.NewLine;
                            }

                            PresentReport += Environment.NewLine + "******************************" + Environment.NewLine;

                        }
                        #endregion
                    }
                    else
                    {
                    }
                }

                aWeeklyReport.PresentContent = PresentReport;

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion

        #region 上傳週報
        public WeeklyReport UploadWeeklyReport(AccountPasswordData aAccountPasswordData, DateTime aDownloadDate, WeeklyReport aWeeklyReport)
        {
            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "上傳週報");

            #region 先根據日期尋找當週主日日期
            // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
            int DayOfWeek = (int)aDownloadDate.DayOfWeek;
            this.m_Sunday = aDownloadDate.AddDays(-DayOfWeek);
            #endregion

            #region 找小組長及其ID
            FindGroupLeader(aAccountPasswordData);
            if (m_ContactId == Guid.Empty) //是否有找到小組長的ID
            { return null; } // 沒找到就回傳 null 
            #endregion

            #region 先尋找帶領族系名單，若找到表示就是族系族長，若沒有則在繼續尋找帶領小組名單
            FindListCollectionForWeeklyReport();
            if (this.m_PresentLists.Entities.Count == 0)
            {
                return null;// 沒找到任何要點名的名單 
            }
            #endregion

            #region 取得週報
            UpdateWeeklyReport(ref aWeeklyReport);
            #endregion

            this.m_PresentLists.Entities.Clear();
            this.m_Lists.Entities.Clear();

            return DownloadWeeklyReport(aAccountPasswordData, aDownloadDate);
        }
        private void UpdateWeeklyReport(ref WeeklyReport aWeeklyReport)
        {
            try
            {
                // 處理每個點名名單
                foreach (Entity ListEntity in this.m_Lists.Entities)
                {
                    // 取得每個需要點名的名單裡的每個週報
                    EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");

                    // 根據日期看有沒有那個週報，並且該週報的小組長與登入的小組長是同一個人
                    Entity GroupWeeklyReportEntity = FilterWeeklyReportByDateAndGroupLeader(ref GroupWeeklyReportEntityCollection);

                    //依據找到的週報有還是沒有來決定下一步:  
                    if (GroupWeeklyReportEntity != null)
                    {
                        //      有找到週報: 建立GroupName及WeeklyReportId
                        // 回報情況
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref GroupWeeklyReportEntity, "new_memo", aWeeklyReport.WeeklyReportContent);

                        // 小組日誌傳 LINE 給小家長、上代族系族長
                        m_LineNotifyUtility.SendWeeklyReportLine(aWeeklyReport.WeeklyReportContent, ListEntity);

                        this.m_ToolUtilityClass.UpdateEntity(ref GroupWeeklyReportEntity);
                    }
                    else
                    {
                        //    沒有找到週報: 建立GroupName及WeeklyReportId = Guid.Empty();
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

        #region 所需要的工具
        private void FindGroupLeader(AccountPasswordData aAccountPasswordData)
        {
            // 找小組長及其ID
            if (aAccountPasswordData.Account != "LineIdLogin")
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(aAccountPasswordData.Account, aAccountPasswordData.Password);
            }
            else
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(aAccountPasswordData.Password);
            }

            this.m_ContactId = m_ContactEntity.Id;
        }
        private void FindListCollectionForWeeklyReport()
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
                    FilterAppNamedListEntity("族長", aMergeCollection);

                    // 帶領族系裡有名單，所以是族系組長，就不用在往下找看是不是小組長了 
                    return;
                }

                // 找到小組長小組名單集合
                aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");
                if (aListEntityCollection.Entities.Count > 0)
                {
                    // 過濾掉需要點名的名單才進來，若是小組長則名單裡就應該沒有"小家長"
                    FilterAppNamedListEntity("小組長", aListEntityCollection);
                    return;
                }

                // 找到小家長小組名單集合 ，內壢神住611靈糧堂才有，因為是三層，神住611靈糧堂並沒有
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_familyhead_list", "list");
                //if (aListEntityCollection.Entities.Count > 0)
                //{
                //    FilterAppNamedListEntity("小家長", aListEntityCollection);
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
        private Entity FilterWeeklyReportByDate(ref EntityCollection GroupWeeklyReportEntityCollection)
        {
            try
            {
                // 處理每個點名名單
                DateTime GroupWeeklyReportSunday;
                foreach (Entity GroupWeeklyReportEntity in GroupWeeklyReportEntityCollection.Entities)
                {
                    // 尋找週報的星期天的日期
                    //DateTime GroupWeeklyReportSunday = aToolUtilityClass.GetEntityDateTimeAttribute(GroupWeeklyReportEntity, "new_sunday_date").ToUniversalTime();
                    GroupWeeklyReportSunday = m_ToolUtilityClass.GetEntityDateTimeAttribute(GroupWeeklyReportEntity, "new_sunday_date").ToLocalTime();

                    if (GroupWeeklyReportSunday.ToShortDateString() == this.m_Sunday.ToShortDateString())
                    {
                        // 有找到主日周報，去找個人聚會與靈修記錄集合
                        return GroupWeeklyReportEntity; // 回傳個人聚會與靈修記錄集合
                    }
                }
                return null;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private Entity FilterWeeklyReportByDateAndGroupLeader(ref EntityCollection GroupWeeklyReportEntityCollection)
        {
            try
            {
                // 處理每個點名名單
                DateTime GroupWeeklyReportSunday;
                foreach ( Entity GroupWeeklyReportEntity in GroupWeeklyReportEntityCollection.Entities )
                {
                    // 尋找週報的星期天的日期
                    //DateTime GroupWeeklyReportSunday = aToolUtilityClass.GetEntityDateTimeAttribute(GroupWeeklyReportEntity, "new_sunday_date").ToUniversalTime();
                    GroupWeeklyReportSunday = m_ToolUtilityClass.GetEntityDateTimeAttribute(GroupWeeklyReportEntity, "new_sunday_date").ToLocalTime();

                    // 取得該週報的小組長
                    Guid aSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(GroupWeeklyReportEntity, "new_groupleader_group_present_weekly_");

                    if ( GroupWeeklyReportSunday.ToShortDateString() == this.m_Sunday.ToShortDateString() && this.m_ContactId == aSmallGroupLeaderId )
                    {
                        // 有找到主日周報，去找個人聚會與靈修記錄集合
                        // 而且該週報小組長與登入的小組長是同一個人
                        return GroupWeeklyReportEntity; // 回傳個人聚會與靈修記錄集合
                    }
                }
                return null;
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
                            if (aIdentity == "族長") // 一般教會稱為小家長
                            {
                                //  族長   = new_contact_race_leager_list
                                //  小組長 = new_contact_family_leader_list
                                //  神住611靈糧堂，因為神住611靈糧堂沒有小家長
                                //Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ListEntity, "new_familyhead_list");
                                Guid GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ListEntity, "new_contact_family_leader_list");

                                String ListName = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname");

                                // 過濾掉需要點名的名單才進來，若是族長則名單裡就應該沒有"小家長"、"小組長"
                                //if (FamilyLeaderId == Guid.Empty && GroupLeaderId == Guid.Empty)
                                if (GroupLeaderId == Guid.Empty || GroupLeaderId == m_ContactId)
                                {
                                    if (!ListName.Contains("門徒")) // 包含"門徒"名單
                                    {
                                        this.m_Lists.Entities.Add(ListEntity);
                                    }
                                }

                                // 需要回報給族系族長/小家長的名單
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
                                // 需要回報給族系族長/小家長的名單
                                this.m_PresentLists.Entities.Add(ListEntity);
                            }
                            else if (aIdentity == "小家長")
                            {
                                this.m_Lists.Entities.Add(ListEntity);
                                // 需要回報給族系族長/小家長的名單
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
        #endregion

        #endregion
        #endregion

    }
}
