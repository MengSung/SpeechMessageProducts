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
    public class DownloadData
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
        #region 下載資料區
        #region 真實運作區塊，並非模擬區塊
        #region 主程式區
        public MemberInfomationPackage GetMemberDataPackage(DateTime aDownloadDate, AccountPasswordData aAccountPasswordData)
        {
            //實際要回傳，不是模擬
            return DownloadMemberPackageDataByDate(aDownloadDate, aAccountPasswordData);
        }

        private MemberInfomationPackage DownloadMemberPackageDataByDate(DateTime aDownloadDate, AccountPasswordData aAccountPasswordData)
        {
            #region 回傳網頁所需要的資料結構
            m_MemberInfomationPackage = new MemberInfomationPackage();
            m_MemberInfomationPackage.GroupWeeklyReportGuidList = new List<GroupWeeklyReportGuid>();
            m_MemberInfomationPackage.ListMemberInfomation = new List<MemberInfomation>();
            #endregion

            #region 先根據日期尋找當週主日日期
            // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
            int DayOfWeek = (int)aDownloadDate.DayOfWeek;
            this.m_Sunday = aDownloadDate.AddDays(-DayOfWeek);
            #endregion

            #region 找登入使用者及其ID
            FindLoginUser(aAccountPasswordData);
            if (m_ContactId == Guid.Empty) //是否有找到登入使用者及其ID
            { return null; } // 沒找到就回傳 null 
            #endregion

            #region 先尋找帶領族系名單，若找到表示就是族系族長，若沒有則在繼續尋找帶領小組名單
            FindListCollection();
            if (m_Lists.Entities.Count != 0)
            {
                #region// 有找到要點名的名單，所以是小組長以上回報
                m_MemberInfomationPackage.m_LoginType = "小組長";
                #region 處理每個要點名的名單
                m_SetIdentityFlag = false; // 因為新朋友、未入組會變更委身類型，旗標防止設定太多次，false表示尚未設定
                ProcessListEntity();
                #endregion

                #region 排序委身類型、並且去除掉數字、空白、逗號
                // 排序委身類型
                m_MemberInfomationPackage.ListMemberInfomation = m_MemberInfomationPackage.ListMemberInfomation.OrderBy(o => o.Identity).ToList();
                // 去除掉數字、空白、逗號
                RemoveNumericAndBlank();
                #endregion

                return m_MemberInfomationPackage;
                #endregion
            }
            else
            {
                #region// 沒找到任何要點名的名單，所以是個人回報
                m_MemberInfomationPackage.m_LoginType = "個人回報";

                #region 取得個人回報的名單
                this.m_Lists = this.m_ToolUtilityClass.QueryListOfContactManyToMany(this.m_ContactEntity.Id);
                #endregion

                #region 處理每個要點名的名單
                m_SetIdentityFlag = false; // 因為新朋友、未入組會變更委身類型，旗標防止設定太多次，false表示尚未設定
                ProcessPersonalListEntity();
                #endregion

                #region 排序委身類型、並且去除掉數字、空白、逗號
                // 排序委身類型
                m_MemberInfomationPackage.ListMemberInfomation = m_MemberInfomationPackage.ListMemberInfomation.OrderBy(o => o.Identity).ToList();
                // 去除掉數字、空白、逗號
                RemoveNumericAndBlank();
                #endregion

                return m_MemberInfomationPackage;
                #endregion
            }
            #endregion
        }
        #endregion
        #region 處理個人回報
        private void ProcessPersonalListEntity()
        {
            try
            {
                // 處理每個點名名單
                foreach (Entity ListEntity in this.m_Lists.Entities)
                {
                    // 取得每個需要點名的名單裡的每個週報
                    EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");


                    // 根據日期看有沒有那個週報
                    Entity GroupWeeklyReportEntity = FilterWeeklyReportByDate(ref GroupWeeklyReportEntityCollection);

                    //依據找到的週報有還是沒有來決定下一步:  
                    //      有: 建立GroupName及WeeklyReportId
                    //    沒有: 建立GroupName及WeeklyReportId = Guid.Empty();
                    String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname");
                    SetupPersonalMemberInfomationPackage(GroupName, ref GroupWeeklyReportEntity, ListEntity);
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        private void SetupPersonalMemberInfomationPackage(String GroupName, ref Entity GroupWeeklyReportEntity, Entity ListEntity)
        {
            try
            {
                GroupWeeklyReportGuid aGroupWeeklyReportGuid = new GroupWeeklyReportGuid();

                if (GroupWeeklyReportEntity != null)
                {
                    #region 這個點名名單有找到主日周報，去找個人聚會與靈修記錄集合
                    // 這個點名名單有找到主日周報，去找個人聚會與靈修記錄集合
                    aGroupWeeklyReportGuid.WeeklyReportGuid = GroupWeeklyReportEntity.Id;

                    // 回傳 APP 小組長姓名
                    aGroupWeeklyReportGuid.SmallGroupLeaderName = m_ToolUtilityClass.GetEntityLookupDisplayName(ListEntity, "new_contact_family_leader_list");
                    // 回傳 APP 小組聚會日期
                    aGroupWeeklyReportGuid.SmallGroupDate = m_ToolUtilityClass.GetEntityDateTimeAttribute(GroupWeeklyReportEntity, "new_group_date");

                    // 回傳 APP 主日出席率
                    aGroupWeeklyReportGuid.SundayPresentRate = m_ToolUtilityClass.GetEntityDoubleAttribute(GroupWeeklyReportEntity, "new_sunday_present_rate");
                    // 回傳 APP 小組出席率
                    aGroupWeeklyReportGuid.SmallGroupRate = m_ToolUtilityClass.GetEntityDoubleAttribute(GroupWeeklyReportEntity, "new_small_group_rate");
                    this.m_MemberInfomationPackage.GroupWeeklyReportGuidList.Add(aGroupWeeklyReportGuid);

                    // 在 APP 中會呈現的小組名稱
                    aGroupWeeklyReportGuid.GroupName = GroupName;
                    GetPersonalWeeklyReportMemberData(GroupName, GroupWeeklyReportEntity.Id);
                    #endregion
                }
                else
                {
                    #region 這個點名名單沒有找到主日周報， 找點名名單的小組組員做為要點名的清單
                    // 這個點名名單沒有找到主日周報， 找點名名單的小組組員做為要點名的清單
                    // 回傳 APP 這是空的 Guid，表示沒找到週報
                    aGroupWeeklyReportGuid.WeeklyReportGuid = Guid.Empty;
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
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }


        private void GetPersonalWeeklyReportMemberData(String GroupName, Guid WeeklyReportId)
        {
            // 搜尋這個週報裡的所有個人聚會與靈修記錄集合
            EntityCollection PresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", WeeklyReportId.ToString(), "new_group_present_weekly_report_prese", "new_present_record");

            #region// 處理每個出席紀錄(個人聚會與靈修記錄集合)

            foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
            {
                if (this.m_ToolUtilityClass.GetEntityLookupAttribute(PresentRecordEntity, "new_contact_new_present_record") == this.m_ContactEntity.Id)
                {
                    // 有找到登入者的個人聚會與靈修記錄
                    // 每個出席紀錄(個人聚會與靈修記錄集合)
                    if (PresentRecordEntity.Attributes.Contains("statecode"))
                    {
                        OptionSetValue aOptionState = PresentRecordEntity.Attributes["statecode"] as OptionSetValue;

                        if (aOptionState.Value == 0)
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
                            #region// 依據紀錄組員的全名，找到手機號碼、家裡電話、地址
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
                            // 組員的職業及專長
                            String aIndustry = "";
                            if (aContactEntity.Attributes.Contains("new_industry"))
                            {
                                aIndustry = (string)aContactEntity.Attributes["new_industry"];
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

                            // 楊梅靈糧堂
                            String aNote = "";
                            //if (PresentRecordEntity.Attributes.Contains("new_name"))
                            //{
                            //    aNote = (string)PresentRecordEntity.Attributes["new_name"];
                            //}
                            if (PresentRecordEntity.Attributes.Contains("new_explanation"))
                            {
                                aNote = (string)PresentRecordEntity.Attributes["new_explanation"];
                            }
                            // 內壢得勝靈糧堂
                            //String aNote = "";
                            //if (PresentRecordEntity.Attributes.Contains("new_memo"))
                            //{
                            //    aNote = (string)PresentRecordEntity.Attributes["new_memo"];
                            //}
                            #endregion
                            #region// 主日點名
                            bool aSundayPresent = false;
                            if (PresentRecordEntity.Attributes.Contains("new_sunday_present_this_week"))
                            {
                                if ((int)PresentRecordEntity.Attributes["new_sunday_present_this_week"] > 0)
                                { aSundayPresent = true; }
                            }
                            #endregion
                            #region// 小組點名
                            bool aSmallGroupPresent = false;
                            if (PresentRecordEntity.Attributes.Contains("new_group_present_this_week"))
                            {
                                if ((int)PresentRecordEntity.Attributes["new_group_present_this_week"] > 0)
                                { aSmallGroupPresent = true; }
                            }
                            #endregion
                            #region// 禱告次數，靈修次數
                            // 禱告次數
                            int aPrayNumber = 0;
                            if (PresentRecordEntity.Attributes.Contains("new_general_care"))
                            {
                                aPrayNumber = (int)PresentRecordEntity.Attributes["new_general_care"];
                            }
                            // 靈修次數
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
                            // 本週牧養狀態(內壢得勝靈糧堂專用)
                            String aShepherdStatus = "";
                            if (PresentRecordEntity.Attributes.Contains("new_shepherd_situation"))
                            {
                                aShepherdStatus = (String)PresentRecordEntity.Attributes["new_shepherd_situation"];
                            }
                            //一對一牧養材料(內壢得勝靈糧堂專用)
                            String aOneOnOne = "";
                            if (PresentRecordEntity.Attributes.Contains("new_onebyone_situation"))
                            {
                                aOneOnOne = (String)PresentRecordEntity.Attributes["new_onebyone_situation"];
                            }
                            // 培訓系統選項(內壢得勝靈糧堂專用)
                            String aTraining = "";
                            if (PresentRecordEntity.Attributes.Contains("new_training_system"))
                            {
                                aTraining = (String)PresentRecordEntity.Attributes["new_training_system"];
                            }
                            // 裝備課程的英文名字可能是有點取錯了可是因為表單已經取了，就先將錯就錯先了
                            // 裝備課程(內壢得勝靈糧堂專用)
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
                            #endregion

                            #region 傳回給手機的資料
                            MemberInfomation aMemberInfomation = new MemberInfomation()
                            {
                                Group = GroupName,
                                Name = FullName,
                                Identity = aIdentity,
                                Phone = DigitsOnly.Replace(aMobilePhone, ""),
                                HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                                Address = aAddress,
                                Industry = aIndustry,
                                Note = aNote,
                                Date = "2015/10/6", //隨意設定的日期，只是預備將來要用的
                                Number = 5,         //隨意設定的數字，只是預備將來要用的
                                SundayPresent = aSundayPresent, //主日出席
                                SmallGroupPresent = aSmallGroupPresent,//小組出席
                                PrayNumber = aPrayNumber, //禱告次數
                                SpiritNumber = aSpiritNumber,// 靈修次數
                                FamilyNumber = aFamilyNumber, // 早禱
                                WorkAndCampusNumber = aWorkAndCampusNumber,// 晚禱
                                ShepherdStatus = aShepherdStatus,// 本週牧養狀態(內壢得勝靈糧堂專用)
                                OneOnOne = aOneOnOne,//一對一牧養材料(內壢得勝靈糧堂專用)
                                Training = aTraining, //培訓系統選項(內壢得勝靈糧堂專用)
                                Incubate = aIncubate, // 裝備課程(內壢得勝靈糧堂專用)
                                FollowUpWeek = aFollowUpWeek, //新人跟進週次
                                FollowUpResult = aFollowUpResult,//新人跟進結果
                                FollowUpNextStep = aFollowUpNextStep,//新人跟進下一步驟
                                FollowUpOption = aFollowUpOption,// 跟進方式選項
                                FollowUp = aFollowUp,// 跟進方式
                                FollowUpNote = aFollowUpNote,// 備註
                                NewComerNote = aNewComerNote // 取得新人跟進週次，及跟進歷程記錄
                            };
                            #endregion

                            #region 10.未入組結案" 不用進入 APP
                            if (aIdentity != "10. 未入組結案")
                            {
                                // 10.未入組結案" 不用進入 APP
                                // 其他的則加入進來
                                this.m_MemberInfomationPackage.ListMemberInfomation.Add(aMemberInfomation);
                            }
                            #endregion

                            #endregion
                        }
                        else
                        {
                            //String StateCode = "非使用中";
                        }
                    }
                    break;// 有找到登入者的個人聚會與靈修記錄，所以就跳出迴圈
                }
            }
            #endregion

            return;
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
                                NewComerNote = aNewComerNote
                            };

                            // 委身類型客製，每間教會不同
                            if ( aIdentity != "11. 結案" )
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

                    break; // 在名單中找到登入者的ID就跳出
                }
            }
            #endregion

            return;
        }

        #endregion
        #region 副程式呼叫
        private void RemoveNumericAndBlank()
        {
            foreach (MemberInfomation aMemberInfomation in m_MemberInfomationPackage.ListMemberInfomation)
            {
                // 去除掉數字、空白、逗號
                aMemberInfomation.Identity = Regex.Replace(aMemberInfomation.Identity, "[0-9]", "");//過濾掉數字
                aMemberInfomation.Identity = aMemberInfomation.Identity.Replace(" ", ""); // //過濾掉空白
                aMemberInfomation.Identity = aMemberInfomation.Identity.Replace(".", ""); // //過濾掉逗號
            }
        }
        private void FindLoginUser(AccountPasswordData aAccountPasswordData)
        {
            // 找登入使用者及其ID
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
                    //EntityCollection aMergeCollection = MergeCollection(ref aListEntityCollection, ref aFamilyLeaderListEntityCollection);
                    EntityCollection aMergeCollection = MergeCollectionSmallGroupAhead(ref aListEntityCollection, ref aFamilyLeaderListEntityCollection);

                    
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

                // 找到小家長小組名單集合 ，內壢得勝靈糧堂才有，因為是三層，楊梅靈糧堂並沒有
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
        private void FindListCollection()
        {
            try
            {
                // 先尋找上代組長 new_contact_list_arealeader
                EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_race_leager_list", "list");  // 上代組長
                //EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_arealeader", "list"); // 族系族長
                if (aListEntityCollection.Entities.Count > 0)
                {
                    // 小組長小組名單集合
                    EntityCollection aFamilyLeaderListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");

                    // 合併小組名單至族系名單，單扣除掉重複的
                    // 然後放在小組名單裡面
                    EntityCollection aMergeCollection = MergeCollection(ref aListEntityCollection, ref aFamilyLeaderListEntityCollection);
                    EntityCollection aMergeCollection = MergeCollectionSmallGroupAhead(ref aListEntityCollection, ref aFamilyLeaderListEntityCollection);


                    // 過濾掉需要點名的名單才進來，而且不是幸福小組(因為有時幸福小組也會在APP點名的框框打勾)
                    // 但是過濾的結果會放在 => this.m_Lists
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
                // 族系族長或是區長的名單若是與小組長名單重疊，則要過濾出僅有族長/區長的名單
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
        private EntityCollection MergeCollectionSmallGroupAhead(ref EntityCollection aListEntityCollection, ref EntityCollection aFamilyLeaderListEntityCollection)
        {
            try
            {
                EntityCollection aMergedEntityCollection = new EntityCollection();

                // 族系族長或是區長的名單若是與小組長名單重疊，則要過濾出僅有族長/區長的名單
                // 合併小組名單至族系名單，單扣除掉重複的
                // 然後放在小組名單裡面
                foreach (Entity FamilyLeaderListEntity in aFamilyLeaderListEntityCollection.Entities)
                {
                    aMergedEntityCollection.Entities.Add(FamilyLeaderListEntity);
                }
                // 一個一個處理族系名單
                foreach (Entity RaceListEntity in aListEntityCollection.Entities)
                {
                    // 處理一個族系族長的名單
                    bool SearchedFlag = false;
                    foreach (Entity FamilyLeaderListEntity in aFamilyLeaderListEntityCollection.Entities)
                    {
                        // 比對每一個小組名單
                        if (RaceListEntity.Id == FamilyLeaderListEntity.Id)
                        {
                            // 族系族長的名單與小組長的名單有相同的了
                            SearchedFlag = true;
                            break;
                        }
                    }

                    if (SearchedFlag == false)
                    {
                        // 族系族長的名單沒有與小組長名單相同的
                        aMergedEntityCollection.Entities.Add(RaceListEntity);
                    }

                }

                return aMergedEntityCollection;
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
                // 過濾掉需要點名的名單才進來，而且不是幸福小組(因為有時幸福小組也會在APP點名的框框打勾)
                if (this.m_Lists != null && this.m_Lists.Entities != null)
                {
                    // this.m_Lists 就是要點名的名單
                    this.m_Lists.Entities.Clear();
                }

                foreach (Entity ListEntity in aListEntityCollection.Entities)
                {
                    if (ListEntity.Attributes.Contains("new_app_named"))
                    {
                        bool AppNamed = (bool)ListEntity.Attributes["new_app_named"];

                        DateTime aHappyStartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ListEntity, "new_happy_start_date");
                        DateTime aHappyEndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ListEntity, "new_happy_end_date");

                        if (AppNamed == true && aHappyStartDate.Year == 1 && aHappyEndDate.Year == 1)
                        {
                            // 需要點名的名單才進來，而且幸福小組的開始結束時間都沒填才是一般小組的名單
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
        private void ProcessListEntity()
        {
            try
            {
                // 處理每個點名名單
                foreach (Entity ListEntity in this.m_Lists.Entities)
                {
                    // 取得每個需要點名的名單裡的每個週報
                    EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");
                    

                    // 根據日期看有沒有那個週報
                    Entity GroupWeeklyReportEntity = FilterWeeklyReportByDate(ref GroupWeeklyReportEntityCollection);

                    //依據找到的週報有還是沒有來決定下一步:  
                    //      有: 建立GroupName及WeeklyReportId
                    //    沒有: 建立GroupName及WeeklyReportId = Guid.Empty();
                    String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname");
                    SetupMemberInfomationPackage(GroupName, ref GroupWeeklyReportEntity, ListEntity);
                }
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
        private void SetupMemberInfomationPackage(String GroupName, ref Entity GroupWeeklyReportEntity, Entity ListEntity)
        {
            try
            {
                GroupWeeklyReportGuid aGroupWeeklyReportGuid = new GroupWeeklyReportGuid();

                if (GroupWeeklyReportEntity != null)
                {
                    #region 這個點名名單有找到主日周報，去找個人聚會與靈修記錄集合
                    // 這個點名名單有找到主日周報，去找個人聚會與靈修記錄集合
                    aGroupWeeklyReportGuid.WeeklyReportGuid = GroupWeeklyReportEntity.Id;

                    // 回傳 APP 小組長姓名
                    aGroupWeeklyReportGuid.SmallGroupLeaderName = m_ToolUtilityClass.GetEntityLookupDisplayName(ListEntity, "new_contact_family_leader_list");
                    // 回傳 APP 小組聚會日期
                    // 非常怪異，取得日期與資料會少一天
                    //aGroupWeeklyReportGuid.SmallGroupDate = m_ToolUtilityClass.GetEntityDateTimeAttribute(GroupWeeklyReportEntity, "new_group_date");
                    aGroupWeeklyReportGuid.SmallGroupDate = m_ToolUtilityClass.GetEntityDateTimeAttribute(GroupWeeklyReportEntity, "new_group_date").AddDays(1);

                    // 回傳 APP 主日出席率
                    aGroupWeeklyReportGuid.SundayPresentRate = m_ToolUtilityClass.GetEntityDoubleAttribute(GroupWeeklyReportEntity, "new_sunday_present_rate");
                    // 回傳 APP 小組出席率
                    aGroupWeeklyReportGuid.SmallGroupRate = m_ToolUtilityClass.GetEntityDoubleAttribute(GroupWeeklyReportEntity, "new_small_group_rate");
                    this.m_MemberInfomationPackage.GroupWeeklyReportGuidList.Add(aGroupWeeklyReportGuid);

                    // 在 APP 中會呈現的小組名稱
                    String DisplayedGroupName = GroupName + "-主日出席率:" + aGroupWeeklyReportGuid.SundayPresentRate.ToString("#0.##%") + "小組出席率:" + aGroupWeeklyReportGuid.SmallGroupRate.ToString("#0.##%");
                    aGroupWeeklyReportGuid.GroupName = DisplayedGroupName;
                    GetWeeklyReportMemberData(DisplayedGroupName, GroupWeeklyReportEntity.Id);
                    #endregion
                }
                else
                {
                    #region 這個點名名單沒有找到主日周報， 找點名名單的小組組員做為要點名的清單
                    // 這個點名名單沒有找到主日周報， 找點名名單的小組組員做為要點名的清單
                    // 回傳 APP 這是空的 Guid，表示沒找到週報
                    aGroupWeeklyReportGuid.WeeklyReportGuid = Guid.Empty;

                    // 回傳 APP 小組長姓名
                    aGroupWeeklyReportGuid.SmallGroupLeaderName = m_ToolUtilityClass.GetEntityLookupDisplayName(ListEntity, "new_contact_family_leader_list");
                    // 回傳 APP 小組聚會日期
                    aGroupWeeklyReportGuid.SmallGroupDate = new DateTime(2000, 1, 1);

                    // 非常怪異，取得日期與資料會少一天
                    //aGroupWeeklyReportGuid.SmallGroupDate = m_ToolUtilityClass.GetEntityDateTimeAttribute(GroupWeeklyReportEntity, "new_group_date");
                    aGroupWeeklyReportGuid.SmallGroupDate = m_ToolUtilityClass.GetEntityDateTimeAttribute(GroupWeeklyReportEntity, "new_group_date").AddDays(1);

                    // 回傳 APP 主日出席率
                    aGroupWeeklyReportGuid.SundayPresentRate = 0;
                    // 回傳 APP 小組出席率
                    aGroupWeeklyReportGuid.SmallGroupRate = 0;

                    this.m_MemberInfomationPackage.GroupWeeklyReportGuidList.Add(aGroupWeeklyReportGuid);

                    // 在 APP 中會呈現的小組名稱
                    String DisplayedGroupName = GroupName + "-主日出席率:" + aGroupWeeklyReportGuid.SundayPresentRate.ToString("#0.##%") + "小組出席率:" + aGroupWeeklyReportGuid.SmallGroupRate.ToString("#0.##%");
                    aGroupWeeklyReportGuid.GroupName = DisplayedGroupName;
                    GetSmallGroupLeaderMemberData(DisplayedGroupName, ListEntity.Id);
                    #endregion
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void GetWeeklyReportMemberData(String GroupName, Guid WeeklyReportId)
        {
            // 搜尋這個週報裡的所有個人聚會與靈修記錄集合
            EntityCollection PresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", WeeklyReportId.ToString(), "new_group_present_weekly_report_prese", "new_present_record");

            #region// 處理每個出席紀錄(個人聚會與靈修記錄集合)

            foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
            {
                // 每個出席紀錄(個人聚會與靈修記錄集合)
                if (PresentRecordEntity.Attributes.Contains("statecode"))
                {
                    OptionSetValue aOptionState = PresentRecordEntity.Attributes["statecode"] as OptionSetValue;

                    if (aOptionState.Value == 0)
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
                        #region// 依據紀錄組員的全名，找到手機號碼、家裡電話、地址
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
                        // 組員的職業及專長
                        String aIndustry = "";
                        if (aContactEntity.Attributes.Contains("new_industry"))
                        {
                            aIndustry = (string)aContactEntity.Attributes["new_industry"];
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

                        // 楊梅靈糧堂
                        String aNote = "";
                        //if (PresentRecordEntity.Attributes.Contains("new_name"))
                        //{
                        //    aNote = (string)PresentRecordEntity.Attributes["new_name"];
                        //}
                        if (PresentRecordEntity.Attributes.Contains("new_explanation"))
                        {
                            aNote = (string)PresentRecordEntity.Attributes["new_explanation"];
                        }
                        // 內壢得勝靈糧堂
                        //String aNote = "";
                        //if (PresentRecordEntity.Attributes.Contains("new_memo"))
                        //{
                        //    aNote = (string)PresentRecordEntity.Attributes["new_memo"];
                        //}
                        #endregion
                        #region// 主日點名
                        bool aSundayPresent = false;
                        if (PresentRecordEntity.Attributes.Contains("new_sunday_present_this_week"))
                        {
                            if ((int)PresentRecordEntity.Attributes["new_sunday_present_this_week"] > 0)
                            { aSundayPresent = true; }
                        }
                        #endregion
                        #region// 小組點名
                        bool aSmallGroupPresent = false;
                        if (PresentRecordEntity.Attributes.Contains("new_group_present_this_week"))
                        {
                            if ((int)PresentRecordEntity.Attributes["new_group_present_this_week"] > 0)
                            { aSmallGroupPresent = true; }
                        }
                        #endregion
                        #region// 禱告次數，靈修次數
                        // 禱告次數
                        int aPrayNumber = 0;
                        if (PresentRecordEntity.Attributes.Contains("new_general_care"))
                        {
                            aPrayNumber = (int)PresentRecordEntity.Attributes["new_general_care"];
                        }
                        // 靈修次數
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
                        // 本週牧養狀態(內壢得勝靈糧堂專用)
                        String aShepherdStatus = "";
                        if (PresentRecordEntity.Attributes.Contains("new_shepherd_situation"))
                        {
                            aShepherdStatus = (String)PresentRecordEntity.Attributes["new_shepherd_situation"];
                        }
                        //一對一牧養材料(內壢得勝靈糧堂專用)
                        String aOneOnOne = "";
                        if (PresentRecordEntity.Attributes.Contains("new_onebyone_situation"))
                        {
                            aOneOnOne = (String)PresentRecordEntity.Attributes["new_onebyone_situation"];
                        }
                        // 培訓系統選項(內壢得勝靈糧堂專用)
                        String aTraining = "";
                        if (PresentRecordEntity.Attributes.Contains("new_training_system"))
                        {
                            aTraining = (String)PresentRecordEntity.Attributes["new_training_system"];
                        }
                        // 裝備課程的英文名字可能是有點取錯了可是因為表單已經取了，就先將錯就錯先了
                        // 裝備課程(內壢得勝靈糧堂專用)
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
                        #endregion

                        #region 傳回給手機的資料
                        MemberInfomation aMemberInfomation = new MemberInfomation()
                        {
                            Group = GroupName,
                            Name = FullName,
                            Identity = aIdentity,
                            Phone = DigitsOnly.Replace(aMobilePhone, ""),
                            HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                            Address = aAddress,
                            Industry = aIndustry,
                            Note = aNote,
                            Date = "2015/10/6", //隨意設定的日期，只是預備將來要用的
                            Number = 5,         //隨意設定的數字，只是預備將來要用的
                            SundayPresent = aSundayPresent, //主日出席
                            SmallGroupPresent = aSmallGroupPresent,//小組出席
                            PrayNumber = aPrayNumber, //禱告次數
                            SpiritNumber = aSpiritNumber,// 靈修次數
                            FamilyNumber = aFamilyNumber, // 早禱
                            WorkAndCampusNumber = aWorkAndCampusNumber,// 晚禱
                            ShepherdStatus = aShepherdStatus,// 本週牧養狀態(內壢得勝靈糧堂專用)
                            OneOnOne = aOneOnOne,//一對一牧養材料(內壢得勝靈糧堂專用)
                            Training = aTraining, //培訓系統選項(內壢得勝靈糧堂專用)
                            Incubate = aIncubate, // 裝備課程(內壢得勝靈糧堂專用)
                            FollowUpWeek = aFollowUpWeek, //新人跟進週次
                            FollowUpResult = aFollowUpResult,//新人跟進結果
                            FollowUpNextStep = aFollowUpNextStep,//新人跟進下一步驟
                            FollowUpOption = aFollowUpOption,// 跟進方式選項
                            FollowUp = aFollowUp,// 跟進方式
                            FollowUpNote = aFollowUpNote,// 備註
                            NewComerNote = aNewComerNote // 取得新人跟進週次，及跟進歷程記錄
                        };
                        #endregion

                        #region 10.未入組結案" 不用進入 APP
                        if (aIdentity != "10. 未入組結案")
                        {
                            // 10.未入組結案" 不用進入 APP
                            this.m_MemberInfomationPackage.ListMemberInfomation.Add(aMemberInfomation);
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
                // 委身類型客製化
                // 確認是否是新人或是未入組
                int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

                //case 100000006:
                //    return "01. 牧師師母";
                //case 100000009:
                //    return "02. 區牧";
                //case 100000003:
                //    return "03. 區長";
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
                //    return "10. 外教會.訪客";
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
                    DateTime FirstDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date");
                    if (FirstDate.Year > 0)
                    {
                        aFollowUpHistoryReport += "首次進入教會日期:" + this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToShortDateString() + Environment.NewLine;
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
                    DateTime FirstDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date");
                    if (FirstDate.Year > 0)
                    {
                        aFollowUpHistoryReport += "首次進入教會日期:" + this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "new_enter_church_date").ToShortDateString() + Environment.NewLine;
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
                else { }
            }
            else if (aIdentityNumber == 100000004)
            {
                //未入組
                if (Counter >= UnGroupMaxiNumber && m_SetIdentityFlag == false)
                {
                    // 只要設定一次就好
                    m_SetIdentityFlag = true;

                    // 未入組變為未入組結案(超過或是等於)
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000008);

                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        this.m_ToolUtilityClass.UpdateEntityDynamics365(ref this.m_ToolUtilityClass.m_OrganizationService, ref aContact );
                    }
                    else
                    {
                        this.m_ToolUtilityClass.UpdateEntityCrm2011(ref this.m_ToolUtilityClass.m_Crm2011OrganizationService, ref aContact );
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
                if ( CRM_TYPE == "DYNAMICS365" )
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
                if ( CRM_TYPE == "DYNAMICS365" )
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
                            Industry= aIndustry,
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
                            NewComerNote = aNewComerNote
                        };

                        // 委身類型客製化
                        if (aIdentity != "11. 結案")
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
        #endregion
        #endregion
        #region 模擬回覆下載資料
        public MemberInfomationPackage DownloadMemberPackageDataByDate_XamarinSimulation(DateTime aDownloadDate, AccountPasswordData aAccountPasswordData)
        {
            MemberInfomationPackage aMemberInfomationPackage = new MemberInfomationPackage();

            #region List<GroupWeeklyReportGuid>
            aMemberInfomationPackage.GroupWeeklyReportGuidList = new List<GroupWeeklyReportGuid>();


            GroupWeeklyReportGuid GroupWeeklyReportGuid_001 = new GroupWeeklyReportGuid();
            GroupWeeklyReportGuid_001.WeeklyReportGuid = Guid.NewGuid();
            GroupWeeklyReportGuid_001.GroupName = "永嫻小組";
            aMemberInfomationPackage.GroupWeeklyReportGuidList.Add(GroupWeeklyReportGuid_001);

            GroupWeeklyReportGuid GroupWeeklyReportGuid_002 = new GroupWeeklyReportGuid();
            GroupWeeklyReportGuid_002.WeeklyReportGuid = Guid.NewGuid();
            GroupWeeklyReportGuid_002.GroupName = "青年小組";
            aMemberInfomationPackage.GroupWeeklyReportGuidList.Add(GroupWeeklyReportGuid_002);

            GroupWeeklyReportGuid GroupWeeklyReportGuid_003 = new GroupWeeklyReportGuid();
            GroupWeeklyReportGuid_003.WeeklyReportGuid = Guid.NewGuid();
            GroupWeeklyReportGuid_003.GroupName = "兒童小組";
            aMemberInfomationPackage.GroupWeeklyReportGuidList.Add(GroupWeeklyReportGuid_003);
            #endregion

            #region ListMemberInfomation
            aMemberInfomationPackage.ListMemberInfomation = new List<MemberInfomation>();

            MemberInfomation MemberInfomation_001 = new MemberInfomation();
            MemberInfomation_001.Group = "永嫻小組";
            MemberInfomation_001.Name = "胡夢嵩";
            MemberInfomation_001.SundayPresent = true;
            aMemberInfomationPackage.ListMemberInfomation.Add(MemberInfomation_001);

            MemberInfomation MemberInfomation_002 = new MemberInfomation();
            MemberInfomation_002.Group = "永嫻小組";
            MemberInfomation_002.Name = "熊國平";
            aMemberInfomationPackage.ListMemberInfomation.Add(MemberInfomation_002);

            MemberInfomation MemberInfomation_003 = new MemberInfomation();
            MemberInfomation_003.Group = "永嫻小組";
            MemberInfomation_003.Name = "王晶球";
            MemberInfomation_003.SundayPresent = true;
            aMemberInfomationPackage.ListMemberInfomation.Add(MemberInfomation_003);

            MemberInfomation MemberInfomation_004 = new MemberInfomation();
            MemberInfomation_004.Group = "青年小組";
            MemberInfomation_004.Name = "吳連碧";
            aMemberInfomationPackage.ListMemberInfomation.Add(MemberInfomation_004);

            MemberInfomation MemberInfomation_005 = new MemberInfomation();
            MemberInfomation_005.Group = "青年小組";
            MemberInfomation_005.Name = "陳秀珍";
            aMemberInfomationPackage.ListMemberInfomation.Add(MemberInfomation_005);

            MemberInfomation MemberInfomation_006 = new MemberInfomation();
            MemberInfomation_006.Group = "青年小組";
            MemberInfomation_006.Name = "陳巧玲";
            aMemberInfomationPackage.ListMemberInfomation.Add(MemberInfomation_006);

            MemberInfomation MemberInfomation_007 = new MemberInfomation();
            MemberInfomation_007.Group = "兒童小組";
            MemberInfomation_007.Name = "胡逸凡";
            aMemberInfomationPackage.ListMemberInfomation.Add(MemberInfomation_007);

            MemberInfomation MemberInfomation_008 = new MemberInfomation();
            MemberInfomation_008.Group = "兒童小組";
            MemberInfomation_008.Name = "胡逸祥";
            aMemberInfomationPackage.ListMemberInfomation.Add(MemberInfomation_008);

            MemberInfomation MemberInfomation_009 = new MemberInfomation();
            MemberInfomation_009.Group = "兒童小組";
            MemberInfomation_009.Name = "李沒藥";
            aMemberInfomationPackage.ListMemberInfomation.Add(MemberInfomation_009);
            #endregion

            return aMemberInfomationPackage;
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
        //台中思恩堂豐富教會
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
        #endregion
    }
}
