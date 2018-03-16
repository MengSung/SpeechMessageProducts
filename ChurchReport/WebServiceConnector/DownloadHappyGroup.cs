using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.Models;

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
    public class DownloadHappyGroup
    {
        #region 資料區
        #region 參數資料
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

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

        HappyGroupWeeklyReportListClass m_HappyGroupWeeklyReportList = new HappyGroupWeeklyReportListClass();

        DateTime m_Sunday;
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要回報的幸福小組名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給族系族長/區長的名單

        Guid m_DecipleGroupListId;
        //Guid m_GroupLeaderId; // 小組長
        Guid m_RaceLeaderId; // 族系族長
        String m_SmallGroupPlace;
        String m_SmallGroupTime;

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
        //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        #endregion
        #region 下載及上傳資料區
        #region 主程式區
        public HappyGroupWeeklyReportListClass GetHappyGroupWeeklyReportList(String Account, String Password)
        {
            m_HappyGroupWeeklyReportList.HappyGroupWeeklyReportList = new List<HappyGroupWeeklyReport>();

            #region 找登入使用者及其ID
            FindLoginUser(Account, Password);
            if (m_ContactId == Guid.Empty) //是否有找到登入使用者及其ID
            {
                // 沒找到就回傳 null
                return null;
            }
            else
            {
                m_HappyGroupWeeklyReportList.LoginUserId = m_ContactId.ToString();
            }
            #endregion

            #region 先尋找帶領族系名單，若找到表示就是族系族長，若沒有則在繼續尋找帶領小組名單

            // 取得並過濾需要回報的幸福小組名單
            // 幸福小組要回報的名單，但是現階段網頁回報端並沒有看2個以上的幸福小組
            // 所以先過濾只有幸福小組長跟登入同一人才回傳
            FindListCollection();

            if (m_Lists.Entities.Count != 0)
            {
                // 有找到要點名的名單，所以是小組長以上回報
                #region 處理每個要點名的名單
                m_SetIdentityFlag = false; // 因為新朋友、未入組會變更委身類型，旗標防止設定太多次，false表示尚未設定

                // 取得週報
                ProcessWeeklyReportOfListEntity();
                #endregion

                return m_HappyGroupWeeklyReportList;
            }
            else
            {
                // 沒找到任何要點名的名單，所以是個人回報
                return null;
            }
            #endregion
        }
        #endregion
        #region 副程式呼叫
        #region 使用者登入
        private void FindLoginUser(String Account, String Password)
        {
            // 找登入使用者及其ID
            if (Account != "LineIdLogin")
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password);
            }
            else
            {
                // 用 LINE 登入
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);
            }

            this.m_ContactId = m_ContactEntity.Id;
        }
        #endregion
        #region 幸福小組名單
        /// <summary>
        /// 取得族系族長或是小長
        /// 所有的名單包括小組點名及幸福小組
        /// </summary>
        private void FindListCollection()
        {
            try
            {
                // 先尋找族系/區長名單
                EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_race_leager_list", "list");
                if (aListEntityCollection.Entities.Count > 0)
                {
                    #region 族系/區長 有名單
                    // 小組長小組名單集合 
                    EntityCollection aFamilyLeaderListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");

                    // 合併小組名單至族系名單，單扣除掉重複的
                    // 然後放在小組名單裡面
                    EntityCollection aMergeCollection = MergeCollection(ref aListEntityCollection, ref aFamilyLeaderListEntityCollection);

                    // 過濾掉需要點名的名單才進來
                    FilterHappyStartEndDateListEntity(aMergeCollection);

                    // 帶領族系裡有名單，所以是族系組長，就不用在往下找看是不是小組長了 
                    return;
                    #endregion
                }

                // 找到小組長小組名單集合 
                aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");
                if (aListEntityCollection.Entities.Count > 0)
                {
                    // 過濾掉需要點名的名單才進來
                    FilterHappyStartEndDateListEntity(aListEntityCollection);
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
                            // 需要點名的名單才進來
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
        private void FilterHappyStartEndDateListEntity(EntityCollection aListEntityCollection)
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
                    DateTime aHappyStartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ListEntity, "new_happy_start_date");
                    DateTime aHappyEndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ListEntity, "new_happy_end_date");
                    if (aHappyStartDate.Year != 1 && aHappyEndDate.Year != 1 && aHappyStartDate <= DateTime.Now && aHappyEndDate >= DateTime.Now)
                    {
                        // 幸福小組要回報的名單，但是現階段網頁回報端並沒有看2個以上的幸福小組
                        // 所以先過濾只有幸福小組長跟登入同一人才回傳
                        Guid SmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ListEntity, "new_contact_family_leader_list");

                        if (this.m_ContactId == SmallGroupLeaderId)
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
        #endregion
        #region 新增修改下載幸福小組週報及BEST
        #region 下載幸福小組週報
        private void ProcessWeeklyReportOfListEntity()
        {
            try
            {
                // 處理每個點名名單
                foreach (Entity ListEntity in this.m_Lists.Entities)
                {
                    // 取得每個需要點名的名單裡的每個週報

                    // 幸福小組名單的 ID ，按照程式來看是會指定到幸福小組名單的最後一個名單，
                    // 因為我們DevExtreme的MasterDetail元件新增也只能新增到最後一個名單
                    m_HappyGroupWeeklyReportList.ListEntityId = ListEntity.Id.ToString();

                    GetEachWeeklyReport(ListEntity);

                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        private void GetEachWeeklyReport(Entity ListEntity)
        {
            try
            {
                // 建立屬靈的認領者下拉選單
                SetSpiritLeaderList(ref ListEntity);

                // 取得幸福小組的週報
                EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");

                // 處理每個幸福小組週報
                //foreach (Entity WeeklyReportEntity in GroupWeeklyReportEntityCollection.Entities )
                for (int i = 0; i < GroupWeeklyReportEntityCollection.Entities.Count; i++)
                {
                    Entity WeeklyReportEntity = GroupWeeklyReportEntityCollection.Entities[i];
                    HappyGroupWeeklyReport aHappyGroupWeeklyReport = new HappyGroupWeeklyReport
                    {
                        HappyGroupName = m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname"),
                        HappyGroupListEntityId = ListEntity.Id.ToString(),
                        HappyGroupWeeklyReportId = WeeklyReportEntity.Id.ToString(),
                        Location = m_ToolUtilityClass.GetEntityStringAttribute(WeeklyReportEntity, "new_location"),
                        WeekCounter = m_ToolUtilityClass.GetEntityStringAttribute(WeeklyReportEntity, "new_weekly_index"),
                        MeetingDate = m_ToolUtilityClass.GetEntityDateTimeAttribute(WeeklyReportEntity, "new_group_date"),
                        StartTime = m_ToolUtilityClass.GetEntityStringAttribute(WeeklyReportEntity, "new_group_start_time"),
                        EndTime = m_ToolUtilityClass.GetEntityStringAttribute(WeeklyReportEntity, "new_group_end_time"),
                        Topic = this.ConvertIndexToTopic(m_ToolUtilityClass.GetOptionSetAttribute(WeeklyReportEntity, "new_topic")),
                        HappyWeeklyReport = this.m_ToolUtilityClass.GetEntityStringAttribute(WeeklyReportEntity, "new_memo"),

                    };

                    m_HappyGroupWeeklyReportList.HappyGroupWeeklyReportList.Add(aHappyGroupWeeklyReport);

                    GetEachHappyPresent(i, WeeklyReportEntity);

                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        private void SetSpiritLeaderList(ref Entity aListEntity)
        {
            try
            {
                bool ListType = false;
                EntityCollection MemberCollection = GetPersonalSmallGroupLeaderMemberData(aListEntity.Id, ref ListType);

                foreach (Entity MemberEntity in MemberCollection.Entities)
                {
                    // 每個組員
                    Entity aContactEntity;

                    if (ListType == false)
                    {
                        // 靜態名單
                        aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                    }
                    else
                    {
                        // 動態名單
                        aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                    }

                    if (IsAQulifiedSpiritLeaderMember(ref aContactEntity) == true)
                    {
                        String SpiritLeaderName = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "fullname");

                        if (m_HappyGroupWeeklyReportList.SpiritLeaderList == null)
                        {
                            m_HappyGroupWeeklyReportList.SpiritLeaderList += SpiritLeaderName + ",";
                        }
                        else
                        {
                            if (m_HappyGroupWeeklyReportList.SpiritLeaderList.Contains(SpiritLeaderName) != true)
                            {
                                m_HappyGroupWeeklyReportList.SpiritLeaderList += SpiritLeaderName + ",";
                            }
                        }
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
        private bool IsAQulifiedSpiritLeaderMember(ref Entity aContactEntity)
        {
            try
            {
                int Identity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");

                // 委身類型客製化
                //if (Identity != 100000004 && Identity != 100000000 && Identity != 100000005)
                //{
                //    // 新朋友、未入組、BEST不能成為屬靈認養者
                //    return true;
                //}
                if ( Identity != 100000005 )
                {
                    // BEST不能成為屬靈認養者
                    return true;
                }
                else
                {
                    return false;
                }
                //switch (Identity)
                //{
                //    case 100000002:
                //        return "1. 家族家長";
                //    case 1:
                //        return "2. 小組員";
                //    case 100000004:
                //        return "3. 未入小組";
                //    case 100000000:
                //        return "4. 新朋友";
                //    case 100000001:
                //        return "5. 學青牧養小組長";
                //    case 100000003:
                //        return "6. 區長";
                //    case 100000005:
                //        return "7. 區牧";
                //    default:
                //        return ".";
                //}

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private void GetEachHappyPresent(int WeeklyReportIndex, Entity WeeklyReportEntity)
        {
            try
            {
                // 取得幸福小組出席紀錄單
                EntityCollection HappyPresentEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", WeeklyReportEntity.Id.ToString(), "new_group_present_weekly_report_prese", "new_present_record");

                m_HappyGroupWeeklyReportList.HappyGroupWeeklyReportList[WeeklyReportIndex].BestRecordList = new List<BestRecord>();

                // 處理每個幸福小組出席紀錄單 
                for (int i = 0; i < HappyPresentEntityCollection.Entities.Count; i++)
                {
                    Entity HappyPresentEntity = HappyPresentEntityCollection.Entities[i];

                    // 出席紀錄單的連絡人
                    Guid aContactId = this.m_ToolUtilityClass.GetEntityLookupAttribute(HappyPresentEntity, "new_contact_new_present_record");
                    if (aContactId == Guid.Empty || aContactId == null)
                    {
                        // 出席紀錄單的連絡人如果是空白就不處理
                        continue;
                    }
                    Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);

                    // 待完成程式
                    String SpiritLeaderName = "";
                    SpiritLeaderName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aContactEntity, "new_contact_contact_spiritleader");

                    // 因為不是BEST的開組人員，仍然要能夠顯示它們屬靈認養者，所以才放進來
                    if (m_HappyGroupWeeklyReportList.SpiritLeaderList == null)
                    {
                        m_HappyGroupWeeklyReportList.SpiritLeaderList += SpiritLeaderName + ",";
                    }
                    else
                    {
                        if (m_HappyGroupWeeklyReportList.SpiritLeaderList.Contains(SpiritLeaderName) != true)
                        {
                            m_HappyGroupWeeklyReportList.SpiritLeaderList += SpiritLeaderName + ",";
                        }
                    }

                    BestRecord aBestRecord = new BestRecord
                    {
                        BestRecordParentId = HappyPresentEntity.Id.ToString(),
                        BestRecordId = HappyPresentEntity.Id.ToString(),
                        FullName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(HappyPresentEntity, "new_contact_new_present_record"),
                        MobilePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "mobilephone"),
                        Present = this.m_ToolUtilityClass.GetEntityIntAttribute(HappyPresentEntity, "new_happy_present") == 1 ? true : false,
                        Decision = this.m_ToolUtilityClass.GetEntityIntAttribute(HappyPresentEntity, "new_happy_decision") == 1 ? true : false,
                        Note = this.m_ToolUtilityClass.GetEntityStringAttribute(HappyPresentEntity, "new_name"),
                        BestLeader = SpiritLeaderName// 屬靈認領者
                    };

                    m_HappyGroupWeeklyReportList.HappyGroupWeeklyReportList[WeeklyReportIndex].BestRecordList.Add(aBestRecord);

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
        #region 新增幸福小組週報
        public void AddHappyGroupWeeklyReport(ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClassToBeAdded, ref HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded)
        {
            try
            {
                Entity HappyGroupListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", new Guid(aHappyGroupWeeklyReportListClassToBeAdded.ListEntityId));

                #region 新增幸福小組週報，同時以名單成員作為初始成員
                CreateWeeklyReport(ref HappyGroupListEntity, ref aHappyGroupWeeklyReportListClassToBeAdded, ref aHappyGroupWeeklyReportToBeAdded);
                #endregion


                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }


        private void CreateWeeklyReport(ref Entity aListEntity, ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClassToBeAdded, ref HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded)
        {
            try
            {
                #region 這是新建立的幸福小組週報
                aHappyGroupWeeklyReportToBeAdded.HappyGroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname");
                //aHappyGroupWeeklyReportToBeAdded.MeetingDate = aHappyGroupWeeklyReportToBeAdded.MeetingDate.AddDays(1);

                Entity aWeeklyReportEntity = new Entity("new_group_present_weekly_report");

                #region 指派週報的負責人
                //Guid ListOwnerId = aListEntity.GetAttributeValue<EntityReference>("ownerid").Id;
                //this.m_ToolUtilityClass.AssignOwner("new_group_present_weekly_report", aWeeklyReportEntity, ListOwnerId);
                #endregion

                #region 從幸福小組名單取得週報所要填入的欄位值
                // 小組聚會地點
                if (aHappyGroupWeeklyReportToBeAdded.Location != null)
                {
                    if (aHappyGroupWeeklyReportToBeAdded.Location != "")
                    {
                        m_SmallGroupPlace = aHappyGroupWeeklyReportToBeAdded.Location;
                    }
                    else
                    {
                        m_SmallGroupPlace = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_place");
                    }
                }
                else
                {
                    m_SmallGroupPlace = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_place");
                    aHappyGroupWeeklyReportToBeAdded.Location = m_SmallGroupPlace;
                }
                // 小組聚會時間
                String HappyGroupStartTime = "";
                if (aHappyGroupWeeklyReportToBeAdded.StartTime != null)
                {
                    if (aHappyGroupWeeklyReportToBeAdded.StartTime != "")
                    {
                        HappyGroupStartTime = this.m_SmallGroupTime = aHappyGroupWeeklyReportToBeAdded.StartTime;
                    }
                    else
                    {
                        HappyGroupStartTime = this.m_SmallGroupTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_start_time");
                    }
                }
                else
                {
                    HappyGroupStartTime = this.m_SmallGroupTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_start_time");
                    aHappyGroupWeeklyReportToBeAdded.StartTime = HappyGroupStartTime;
                }
                String HappyGroupEndTime = "";
                if (aHappyGroupWeeklyReportToBeAdded.EndTime != null)
                {
                    if (aHappyGroupWeeklyReportToBeAdded.EndTime != "")
                    {
                        HappyGroupEndTime = aHappyGroupWeeklyReportToBeAdded.EndTime;
                    }
                    else
                    {
                        HappyGroupEndTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_end_time");
                    }
                }
                else
                {
                    HappyGroupEndTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_end_time");

                    aHappyGroupWeeklyReportToBeAdded.EndTime = HappyGroupEndTime;
                }

                // 小家長 ID
                Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 區長 ID
                Guid RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");

                // 區牧長 ID
                Guid ShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");

                // 區名
                String AreaName = "";
                //String AreaName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_area_name");
                #endregion

                // 設定週報相關屬性
                this.SetupWeeklyReortEntityAttributes(ref aWeeklyReportEntity, AreaName, FamilyLeaderId, GroupLeaderId, RaceLeaderId, ShepherdLeaderId, aListEntity, aHappyGroupWeeklyReportToBeAdded.MeetingDate, HappyGroupStartTime, HappyGroupEndTime, m_SmallGroupPlace, m_SmallGroupTime, aHappyGroupWeeklyReportToBeAdded);

                // 新增週報
                aHappyGroupWeeklyReportToBeAdded.HappyGroupWeeklyReportId = this.m_ToolUtilityClass.CreateEntity(aWeeklyReportEntity).ToString();

                aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", new Guid(aHappyGroupWeeklyReportToBeAdded.HappyGroupWeeklyReportId));
                #endregion

                #region 建立幸福小組的 BEST
                aHappyGroupWeeklyReportToBeAdded.BestRecordList = new List<BestRecord>();
                CreateDefaultBestList(ref aWeeklyReportEntity, AreaName, FamilyLeaderId, GroupLeaderId, RaceLeaderId, ShepherdLeaderId, aListEntity, aHappyGroupWeeklyReportToBeAdded.MeetingDate, HappyGroupStartTime, HappyGroupEndTime, m_SmallGroupPlace, m_SmallGroupTime, ref aHappyGroupWeeklyReportListClassToBeAdded, ref aHappyGroupWeeklyReportToBeAdded);
                #endregion

                // 前台網頁要呈現的週報資料
                //aHappyGroupWeeklyReportListClassToBeAdded.HappyGroupWeeklyReportList.Add(aHappyGroupWeeklyReportToBeAdded);
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private void SetupWeeklyReortEntityAttributes(ref Entity aWeeklyReportEntity, String AreaName, Guid aFamilyLeaderId, Guid aGroupLeaderId, Guid aRaceLeaderId, Guid aShepherdLeaderId, Entity aListEntity, DateTime aMeetingDate, String HappyGroupStartTime, String HappyGroupEndTime, String SmallGroupPlace, String SmallGroupTime, HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded)
        {
            try
            {
                #region 設定週報名稱
                // 取得小組名單的名稱
                String WeeklyReportName = "";
                String GroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "listname");
                if (aHappyGroupWeeklyReportToBeAdded.Topic != null)
                {
                    WeeklyReportName = GroupName + "-" + aHappyGroupWeeklyReportToBeAdded.Topic;
                }
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
                #region 關聯區牧長屬性
                if (aShepherdLeaderId != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_contact_arealeader_weekly_report", "contact", aShepherdLeaderId); }
                #endregion
                #region 關聯小組名單 Lookup
                if (aListEntity.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aWeeklyReportEntity, "new_list_group_present_weekly_report", "list", aListEntity.Id); }
                #endregion
                #region 設定主日及小組聚會日期
                if (aMeetingDate.Year == 1)
                {
                    aMeetingDate = DateTime.Now;
                }

                int DayOfWeek = 1;
                if (aHappyGroupWeeklyReportToBeAdded.MeetingDate.Year == 1)
                {
                    aHappyGroupWeeklyReportToBeAdded.MeetingDate = DateTime.Now;
                    //設定主日日期
                    DayOfWeek = (int)aHappyGroupWeeklyReportToBeAdded.MeetingDate.DayOfWeek;
                }
                else
                {
                    //設定主日日期
                    DayOfWeek = (int)aHappyGroupWeeklyReportToBeAdded.MeetingDate.DayOfWeek ;
                }
                //設定主日日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_sunday_date", aMeetingDate.AddDays(-DayOfWeek));
                //設定小組日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", aMeetingDate);
                #endregion
                #region 設定小組聚會地點和時間
                //設定小組聚會地點
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_place", SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_location", SmallGroupPlace);
                //設定小組聚會時間
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_time", HappyGroupStartTime);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_start_time", HappyGroupStartTime);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_end_time", HappyGroupEndTime);
                #endregion
                #region 設定幸福小組週次
                if (aHappyGroupWeeklyReportToBeAdded.WeekCounter != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_weekly_index", aHappyGroupWeeklyReportToBeAdded.WeekCounter);
                }
                #endregion
                #region 設定幸福小組主題
                if (aHappyGroupWeeklyReportToBeAdded.Topic != null)
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_topic", ConvertTopicToIndex(aHappyGroupWeeklyReportToBeAdded.Topic));
                }
                #endregion
                #region 設定幸福小組日誌回報
                if (aHappyGroupWeeklyReportToBeAdded.HappyWeeklyReport != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_memo", aHappyGroupWeeklyReportToBeAdded.HappyWeeklyReport);
                }
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
        #region 新增幸福小組BEST連絡人
        public void CreateBest(ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClassToBeAdded, ref BestRecord aBestRecord)
        {
            try
            {
                if (aBestRecord.FullName != "")
                {
                    #region 取得要加入BEST的幸福小組週報
                    int WeeklyReportListCount = aHappyGroupWeeklyReportListClassToBeAdded.HappyGroupWeeklyReportList.Count;
                    Guid aWeeklyReportId = new Guid(aHappyGroupWeeklyReportListClassToBeAdded.HappyGroupWeeklyReportList[WeeklyReportListCount - 1].HappyGroupWeeklyReportId);
                    Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", aWeeklyReportId);

                    HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded = aHappyGroupWeeklyReportListClassToBeAdded.HappyGroupWeeklyReportList[WeeklyReportListCount - 1];
                    #endregion

                    #region 建立新的 BEST
                    Entity aBestContactEntity = CreateContactFromBest(ref aHappyGroupWeeklyReportListClassToBeAdded, ref aHappyGroupWeeklyReportToBeAdded, ref aBestRecord);
                    #endregion

                    #region 取得幸福小組名單
                    Entity HappyGroupListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", new Guid(aHappyGroupWeeklyReportListClassToBeAdded.ListEntityId));
                    #endregion

                    #region 新的 BEST 加入至幸福小組名單
                    AddNewBestInMemberList(aBestContactEntity, HappyGroupListEntity);
                    #endregion

                    #region 從幸福小組"名單"取得BEST出席單所要填入的欄位值
                    // 小組聚會地點
                    if (aHappyGroupWeeklyReportToBeAdded.Location != null)
                    {
                        if (aHappyGroupWeeklyReportToBeAdded.Location != "")
                        {
                            m_SmallGroupPlace = aHappyGroupWeeklyReportToBeAdded.Location;
                        }
                        else
                        {
                            m_SmallGroupPlace = this.m_ToolUtilityClass.GetEntityStringAttribute(ref HappyGroupListEntity, "new_group_place");
                        }
                    }
                    else
                    {
                        m_SmallGroupPlace = this.m_ToolUtilityClass.GetEntityStringAttribute(ref HappyGroupListEntity, "new_group_place");
                        aHappyGroupWeeklyReportToBeAdded.Location = m_SmallGroupPlace;
                    }
                    // 小組聚會時間
                    String HappyGroupStartTime = "";
                    if (aHappyGroupWeeklyReportToBeAdded.StartTime != null)
                    {
                        if (aHappyGroupWeeklyReportToBeAdded.StartTime != "")
                        {
                            HappyGroupStartTime = this.m_SmallGroupTime = aHappyGroupWeeklyReportToBeAdded.StartTime;
                        }
                        else
                        {
                            HappyGroupStartTime = this.m_SmallGroupTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref HappyGroupListEntity, "new_group_start_time");
                        }
                    }
                    else
                    {
                        HappyGroupStartTime = this.m_SmallGroupTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref HappyGroupListEntity, "new_group_start_time");
                        aHappyGroupWeeklyReportToBeAdded.StartTime = HappyGroupStartTime;
                    }
                    String HappyGroupEndTime = "";
                    if (aHappyGroupWeeklyReportToBeAdded.EndTime != null)
                    {
                        if (aHappyGroupWeeklyReportToBeAdded.EndTime != "")
                        {
                            HappyGroupEndTime = aHappyGroupWeeklyReportToBeAdded.EndTime;
                        }
                        else
                        {
                            HappyGroupEndTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref HappyGroupListEntity, "new_group_end_time");
                        }
                    }
                    else
                    {
                        HappyGroupEndTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref HappyGroupListEntity, "new_group_end_time");

                        aHappyGroupWeeklyReportToBeAdded.EndTime = HappyGroupEndTime;
                    }

                    // 小家長 ID
                    Guid FamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref HappyGroupListEntity, "new_familyhead_list");

                    // 小組長 ID
                    Guid GroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref HappyGroupListEntity, "new_contact_family_leader_list");

                    // 區長 ID
                    Guid RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref HappyGroupListEntity, "new_contact_race_leager_list");

                    // 區牧長 ID
                    Guid ShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref HappyGroupListEntity, "new_contact_list_arealeader");

                    // 區名
                    String AreaName = "";
                    //String AreaName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_area_name");
                    #endregion

                    #region 建立幸福小組 BEST 的出席紀錄單
                    CreateTheBestPresentRecord(aBestContactEntity, ref aWeeklyReportEntity, FamilyLeaderId, GroupLeaderId, RaceLeaderId, ShepherdLeaderId, HappyGroupListEntity, aHappyGroupWeeklyReportToBeAdded.MeetingDate, HappyGroupStartTime, HappyGroupEndTime, m_SmallGroupPlace, m_SmallGroupTime, ref aHappyGroupWeeklyReportListClassToBeAdded, ref aHappyGroupWeeklyReportToBeAdded, ref aBestRecord);
                    #endregion
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private void AddNewBestInMemberList(Entity NewBestContactEntity, Entity aListEntity)
        {
            try
            {
                if (aListEntity != null)
                {
                    #region 有找到被關聯的小組名單
                    bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(aListEntity, "type");

                    if (ListType == false)
                    {
                        // 靜態名單
                        List<Guid> memberGuidList = new List<Guid>();
                        memberGuidList.Add(NewBestContactEntity.Id);
                        m_ToolUtilityClass.AddMembersToMarketingList(aListEntity.Id, memberGuidList);
                    }
                    else
                    {
                        // 動態名單
                        EntityReference aListEntityReference = new EntityReference("list", aListEntity.Id);

                        // 內壢得勝靈糧堂
                        //this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_cell_list_contact", ref aListEntityReference);
                        // 楊梅靈糧堂
                        this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref NewBestContactEntity, "new_list_contact", ref aListEntityReference);

                        this.m_ToolUtilityClass.UpdateEntity(ref NewBestContactEntity);
                    }
                    #endregion
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }

        private Entity CreateContactFromBest(ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClassToBeAdded, ref HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded, ref BestRecord aBestRecord)
        {
            try
            {
                Entity aQueryBestContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByName(aBestRecord.FullName);

                if (aQueryBestContactEntity == null)
                {
                    #region 系統裡沒有這個姓名的BEST，就直接建立一個新的 BEST

                    // 建立一個新的 BEST
                    aQueryBestContactEntity = new Entity("contact");
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aQueryBestContactEntity, "lastname", aBestRecord.FullName);
                    #endregion
                }
                else
                {
                    #region 系統裡有同名同姓的BEST，要額外處理

                    String aQueryBestContactMobile = DigitsOnly.Replace(this.m_ToolUtilityClass.GetEntityStringAttribute(ref aQueryBestContactEntity, "mobilephone"), "");

                    if (aBestRecord.MobilePhone != "")
                    {
                        #region// 同名同姓，但是幸福小組長有輸入手機號碼
                        if (aQueryBestContactMobile != DigitsOnly.Replace(aBestRecord.MobilePhone, ""))
                        {
                            #region// 手機不同，建立一個新的 BEST
                            aQueryBestContactEntity = new Entity("contact");
                            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aQueryBestContactEntity, "lastname", aBestRecord.FullName);
                            #endregion
                        }
                        else
                        {
                            #region// 手機相同，就用系統的姓名
                            return aQueryBestContactEntity;
                            #endregion
                        }
                        #endregion
                    }
                    else
                    {
                        #region// 同名同姓，但是幸福小組長沒有輸入手機號碼，就在資料庫新增BEST資料紀錄，但是附加(BEST)字樣
                        aQueryBestContactEntity = new Entity("contact");
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref aQueryBestContactEntity, "lastname", aBestRecord.FullName + "(BEST)");
                        #endregion
                    }
                    #endregion
                }


                SetContactSpiritLeader(ref aQueryBestContactEntity, ref aHappyGroupWeeklyReportListClassToBeAdded, ref aBestRecord);

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aQueryBestContactEntity, "mobilephone", aBestRecord.MobilePhone);

                // 委身類型客製化，客製委身類型欄位，每間教會委身類型都不一樣，高雄錫安堂=>"幸福小組BEST" = 100000005
                // 設定成為 BEST 的委身類型
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aQueryBestContactEntity, "customertypecode", 100000005);

                if (this.m_ContactEntity == null)
                {
                    this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", new Guid(aHappyGroupWeeklyReportListClassToBeAdded.LoginUserId));
                }
                Guid LoginUserParentId = this.m_ToolUtilityClass.GetEntityLookupAttribute(this.m_ContactEntity, "parentcustomerid");
                if (LoginUserParentId != Guid.Empty) { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aQueryBestContactEntity, "parentcustomerid", "account", LoginUserParentId); }

                if (aBestRecord.Present == true)
                {
                    SetContactHappyTimesAndHistory(ref aQueryBestContactEntity, aHappyGroupWeeklyReportToBeAdded.Topic);
                }

                Guid aBestContactEntityId = this.m_ToolUtilityClass.CreateEntity(aQueryBestContactEntity);

                return this.m_ToolUtilityClass.RetrieveEntity("contact", aBestContactEntityId);

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetContactHappyTimesAndHistory(ref Entity BestContactEntity, String HappyCourse)
        {
            try
            {
                // 每個組員
                String OriginalHappyHistory = this.m_ToolUtilityClass.GetEntityStringAttribute(BestContactEntity, "new_happy_history");

                if (OriginalHappyHistory.Contains(HappyCourse) != true)
                {
                    OriginalHappyHistory += HappyCourse + ",";

                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref BestContactEntity, "new_happy_history", OriginalHappyHistory);

                    String[] CourseCounter = OriginalHappyHistory.Split(',');

                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref BestContactEntity, "new_happy_times", CourseCounter.Length - 1);
                }

            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void RemoveContactHappyTimesAndHistory(ref Entity BestContactEntity, String HappyCourse)
        {
            try
            {
                // 每個組員
                String OriginalHappyHistory = this.m_ToolUtilityClass.GetEntityStringAttribute(BestContactEntity, "new_happy_history");

                if (OriginalHappyHistory.Contains(HappyCourse) == true)
                {
                    OriginalHappyHistory = OriginalHappyHistory.Replace(HappyCourse + ",", "");

                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref BestContactEntity, "new_happy_history", OriginalHappyHistory);

                    String[] CourseCounter = OriginalHappyHistory.Split(',');

                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref BestContactEntity, "new_happy_times", CourseCounter.Length - 1);
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        private void SetContactSpiritLeader(ref Entity aQueryBestContactEntity, ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClassToBeAdded, ref BestRecord aBestRecord)
        {
            try
            {

                Guid aContactSpiritLeaderId = GetContactSpiritLeaderId(ref aHappyGroupWeeklyReportListClassToBeAdded, ref aBestRecord);

                if (aContactSpiritLeaderId != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(aQueryBestContactEntity, "new_contact_contact_spiritleader", "contact", aContactSpiritLeaderId);
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private Guid GetContactSpiritLeaderId(ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClassToBeAdded, ref BestRecord aBestRecord)
        {
            try
            {
                // 每個組員
                bool ListType = false;
                EntityCollection MemberCollection = GetPersonalSmallGroupLeaderMemberData(new Guid(aHappyGroupWeeklyReportListClassToBeAdded.ListEntityId), ref ListType);

                foreach (Entity MemberEntity in MemberCollection.Entities)
                {
                    #region//取得名單中的每個組員
                    Entity aContactEntity;

                    if (ListType == false)
                    {
                        // 靜態名單
                        aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                    }
                    else
                    {
                        // 動態名單
                        aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                    }
                    #endregion

                    if (this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "fullname") == aBestRecord.BestLeader)
                    {
                        return aContactEntity.Id;
                    }

                }

                return Guid.Empty;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private void CreateTheBestPresentRecord(Entity aBestContactEntity, ref Entity aWeeklyReportEntity, Guid aFamilyLeaderId, Guid aGroupLeaderId, Guid aRaceLeaderId, Guid aShepherdLeaderId, Entity aListEntity, DateTime aMeetingDate, String HappyGroupStartTime, String HappyGroupEndTime, String SmallGroupPlace, String SmallGroupTime, ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClassToBeAdded, ref HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded, ref BestRecord aBestRecord)
        {
            try
            {
                // 每個組員
                int aWeeklySmallGroupNumber = 0;
                Entity aPresentRecord = CreatePresentRecord(aBestContactEntity, ref aListEntity, aWeeklyReportEntity.Id, ref aWeeklySmallGroupNumber, aHappyGroupWeeklyReportToBeAdded, ref aBestRecord);

                // 前台網頁要呈現的Best資料
                //aHappyGroupWeeklyReportToBeAdded.BestRecordList.Add(aBestRecord);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private void CreateDefaultBestList(ref Entity aWeeklyReportEntity, String AreaName, Guid aFamilyLeaderId, Guid aGroupLeaderId, Guid aRaceLeaderId, Guid aShepherdLeaderId, Entity aListEntity, DateTime aMeetingDate, String HappyGroupStartTime, String HappyGroupEndTime, String SmallGroupPlace, String SmallGroupTime, ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClassToBeAdded, ref HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded)
        {
            try
            {
                bool ListType = false;
                EntityCollection MemberCollection = GetPersonalSmallGroupLeaderMemberData(aListEntity.Id, ref ListType);

                foreach (Entity MemberEntity in MemberCollection.Entities)
                {
                    // 每個組員
                    Entity aContactEntity;

                    if (ListType == false)
                    {
                        // 靜態名單
                        aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                    }
                    else
                    {
                        // 動態名單
                        aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                    }

                    int aWeeklySmallGroupNumber = 0;
                    Entity aPresentRecord = CreateDefaultPresentRecord(aContactEntity, ref aListEntity, aWeeklyReportEntity.Id, ref aWeeklySmallGroupNumber, aHappyGroupWeeklyReportToBeAdded);

                    BestRecord aBestRecord = new BestRecord
                    {
                        BestRecordParentId = aWeeklyReportEntity.Id.ToString(),
                        BestRecordId = aPresentRecord.Id.ToString(),
                        FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "fullname"),
                        MobilePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "mobilephone"),
                        Present = false,
                        Decision = false,
                        Note = "",
                        BestLeader = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aContactEntity, "new_contact_contact_spiritleader")// 屬靈認領者
                    };

                    // 前台網頁要呈現的新增BEST的資料 
                    aHappyGroupWeeklyReportToBeAdded.BestRecordList.Add(aBestRecord);

                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }

        private Entity CreateDefaultPresentRecord(Entity aContactEntity, ref Entity aListEntity, Guid aWeeklyReportId, ref int aWeeklySmallGroupNumber, HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded)
        {
            if (aContactEntity != null)
            {
                // 這是新建立的個人聚會與靈修記錄
                Entity aPresentRecord = new Entity("new_present_record");

                // 設定個人聚會與靈修記錄相關屬性
                this.SetupDefaultPresentRecordEntityAttributes(aPresentRecord, ref aContactEntity, ref aListEntity, ref aWeeklyReportId, false, false, "", this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "mobilephone"), ref aWeeklySmallGroupNumber, aHappyGroupWeeklyReportToBeAdded);

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
        private Entity CreatePresentRecord(Entity aContactEntity, ref Entity aListEntity, Guid aWeeklyReportId, ref int aWeeklySmallGroupNumber, HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded, ref BestRecord aBestRecord)
        {
            if (aContactEntity != null)
            {
                // 這是新建立的個人聚會與靈修記錄
                Entity aPresentRecord = new Entity("new_present_record");

                // 設定個人聚會與靈修記錄相關屬性
                this.SetupPresentRecordEntityAttributes(aPresentRecord, ref aContactEntity, ref aListEntity, ref aWeeklyReportId, false, false, "", this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "mobilephone"), ref aWeeklySmallGroupNumber, aHappyGroupWeeklyReportToBeAdded, ref aBestRecord);

                // 新增個人聚會與靈修記錄
                Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);

                aBestRecord.BestRecordEntityId = aBestRecord.BestRecordId = aPresentRecordId.ToString();

                //取得並回傳新建的聚會與靈修記錄
                return this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);
            }
            else
            {
                return null;
            }
        }

        private void SetupDefaultPresentRecordEntityAttributes(Entity aPresentRecord, ref Entity aContactEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, bool Present, bool Decision, String Note, String Phone, ref int aWeeklySmallGroupNumber, HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded)
        {
            try
            {
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
                String AreaName = "";
                //String AreaName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_area_name");

                #endregion
                #region 設定區名
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_area_name", AreaName);
                #endregion
                #region 設定一些LOOKUP 關聯
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
                #endregion
                #region 設定主日及小組聚會日期
                int DayOfWeek = (int)aHappyGroupWeeklyReportToBeAdded.MeetingDate.DayOfWeek + 1;
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", aHappyGroupWeeklyReportToBeAdded.MeetingDate.AddDays(-DayOfWeek));
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", aHappyGroupWeeklyReportToBeAdded.MeetingDate);
                #endregion
                #region 設定小組聚會地點和時間
                #region//設定小組聚會地點
                if (aHappyGroupWeeklyReportToBeAdded.Location != null)
                {
                    if (aHappyGroupWeeklyReportToBeAdded.Location != "")
                    {
                        m_SmallGroupPlace = aHappyGroupWeeklyReportToBeAdded.Location;
                    }
                    else
                    {
                        m_SmallGroupPlace = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_place");
                    }
                }
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_place", m_SmallGroupPlace);
                #endregion
                #region//設定小組聚會時間
                String HappyGroupStartTime = "";
                if (aHappyGroupWeeklyReportToBeAdded.StartTime != null)
                {
                    if (aHappyGroupWeeklyReportToBeAdded.StartTime != "")
                    {
                        HappyGroupStartTime = this.m_SmallGroupTime = aHappyGroupWeeklyReportToBeAdded.StartTime;
                    }
                    else
                    {
                        HappyGroupStartTime = this.m_SmallGroupTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_start_time");
                    }
                }
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_time", HappyGroupStartTime);
                #endregion
                #endregion
                #region 設定幸福小組出席
                if (Present == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 1);

                    aWeeklySmallGroupNumber += 1;
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 0);
                }
                #endregion
                #region 設定幸福小組決志
                if (Decision == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_decision", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_decision", 0);
                }
                #endregion
                #region 設定附註或是代禱事項

                // 轉換版本
                // 楊梅靈糧堂
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", Note);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.Note);

                // 內壢得勝靈糧堂
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", Phone);
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, ref Entity aContactEntity, ref Entity aListEntity, ref Guid aWeeklyReportId, bool Present, bool Decision, String Note, String Phone, ref int aWeeklySmallGroupNumber, HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded, ref BestRecord aBestRecord)
        {
            try
            {
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
                String AreaName = "";
                //String AreaName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_area_name");

                #endregion
                #region 設定區名
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_area_name", AreaName);
                #endregion
                #region 設定一些LOOKUP 關聯
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
                #endregion
                #region 設定主日及小組聚會日期
                int DayOfWeek = (int)aHappyGroupWeeklyReportToBeAdded.MeetingDate.DayOfWeek + 1;
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", aHappyGroupWeeklyReportToBeAdded.MeetingDate.AddDays(-DayOfWeek));
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", aHappyGroupWeeklyReportToBeAdded.MeetingDate);
                #endregion
                #region 設定小組聚會地點和時間
                #region//設定小組聚會地點
                if (aHappyGroupWeeklyReportToBeAdded.Location != null)
                {
                    if (aHappyGroupWeeklyReportToBeAdded.Location != "")
                    {
                        m_SmallGroupPlace = aHappyGroupWeeklyReportToBeAdded.Location;
                    }
                    else
                    {
                        m_SmallGroupPlace = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_place");
                    }
                }
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_place", m_SmallGroupPlace);
                #endregion
                #region//設定小組聚會時間
                String HappyGroupStartTime = "";
                if (aHappyGroupWeeklyReportToBeAdded.StartTime != null)
                {
                    if (aHappyGroupWeeklyReportToBeAdded.StartTime != "")
                    {
                        HappyGroupStartTime = this.m_SmallGroupTime = aHappyGroupWeeklyReportToBeAdded.StartTime;
                    }
                    else
                    {
                        HappyGroupStartTime = this.m_SmallGroupTime = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aListEntity, "new_group_start_time");
                    }
                }
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_time", HappyGroupStartTime);
                #endregion
                #endregion
                #region 設定幸福小組出席
                if (aBestRecord.Present == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 1);

                    aWeeklySmallGroupNumber += 1;
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 0);
                }
                #endregion
                #region 設定幸福小組決志
                if (aBestRecord.Decision == true)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_decision", 1);
                }
                else
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_decision", 0);
                }
                #endregion
                #region 設定附註或是代禱事項

                // 轉換版本
                // 楊梅靈糧堂
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", aBestRecord.Note);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.Note);

                // 內壢得勝靈糧堂
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region 設定行動電話
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", Phone);
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }

        #endregion
        #region 修改幸福小組週報
        public void UpdateHappyGroupWeeklyReport(String aHappyWeeklyReportId, ref HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeUpdated)
        {
            try
            {
                Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", new Guid(aHappyWeeklyReportId));

                #region 幸福小組週報，同時以名單成員作為初始成員
                this.SetHappyGroupWeeklyReport(ref aHappyGroupWeeklyReportToBeUpdated, ref aWeeklyReportEntity);
                #endregion

                this.m_ToolUtilityClass.UpdateEntity(ref aWeeklyReportEntity);
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SetHappyGroupWeeklyReport(ref HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeUpdated, ref Entity aWeeklyReportEntity)
        {
            try
            {
                #region 設定幸福小組週次
                if (aHappyGroupWeeklyReportToBeUpdated.WeekCounter != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_weekly_index", aHappyGroupWeeklyReportToBeUpdated.WeekCounter);
                }
                #endregion
                #region 設定小組聚會日期
                if (aHappyGroupWeeklyReportToBeUpdated.MeetingDate.Year != 1)
                {
                    //設定主日日期
                    int DayOfWeek = (int)aHappyGroupWeeklyReportToBeUpdated.MeetingDate.DayOfWeek + 1;
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_sunday_date", aHappyGroupWeeklyReportToBeUpdated.MeetingDate.AddDays(-DayOfWeek));
                    //設定小組日期
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", aHappyGroupWeeklyReportToBeUpdated.MeetingDate);
                }
                #endregion
                #region 設定幸福小組主題
                if (aHappyGroupWeeklyReportToBeUpdated.Topic != null)
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_topic", ConvertTopicToIndex(aHappyGroupWeeklyReportToBeUpdated.Topic));
                }
                #endregion
                #region 設定幸福小組日誌回報
                if (aHappyGroupWeeklyReportToBeUpdated.HappyWeeklyReport != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_memo", aHappyGroupWeeklyReportToBeUpdated.HappyWeeklyReport);
                }
                #endregion
                #region 設定小組聚會地點和時間
                //設定小組聚會地點
                if (aHappyGroupWeeklyReportToBeUpdated.Location != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_place", aHappyGroupWeeklyReportToBeUpdated.Location);
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_location", aHappyGroupWeeklyReportToBeUpdated.Location);
                }
                //設定小組聚會時間
                if (aHappyGroupWeeklyReportToBeUpdated.StartTime != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_time", aHappyGroupWeeklyReportToBeUpdated.StartTime);
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_start_time", aHappyGroupWeeklyReportToBeUpdated.StartTime);
                }
                if (aHappyGroupWeeklyReportToBeUpdated.EndTime != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_end_time", aHappyGroupWeeklyReportToBeUpdated.EndTime);
                }
                #endregion
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SetHappyGroupWeeklyReport( HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeUpdated, ref Entity aWeeklyReportEntity)
        {
            try
            {
                #region 設定幸福小組週次
                if (aHappyGroupWeeklyReportToBeUpdated.WeekCounter != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_weekly_index", aHappyGroupWeeklyReportToBeUpdated.WeekCounter);
                }
                #endregion
                #region 設定小組聚會日期
                if (aHappyGroupWeeklyReportToBeUpdated.MeetingDate.Year != 1)
                {
                    //設定主日日期
                    int DayOfWeek = (int)aHappyGroupWeeklyReportToBeUpdated.MeetingDate.DayOfWeek + 1;
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_sunday_date", aHappyGroupWeeklyReportToBeUpdated.MeetingDate.AddDays(-DayOfWeek));
                    //設定小組日期
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aWeeklyReportEntity, "new_group_date", aHappyGroupWeeklyReportToBeUpdated.MeetingDate);
                }
                #endregion
                #region 設定幸福小組主題
                if (aHappyGroupWeeklyReportToBeUpdated.Topic != null)
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aWeeklyReportEntity, "new_topic", ConvertTopicToIndex(aHappyGroupWeeklyReportToBeUpdated.Topic));
                }
                #endregion
                #region 設定幸福小組日誌回報
                if (aHappyGroupWeeklyReportToBeUpdated.HappyWeeklyReport != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_memo", aHappyGroupWeeklyReportToBeUpdated.HappyWeeklyReport);
                }
                #endregion
                #region 設定小組聚會地點和時間
                //設定小組聚會地點
                if (aHappyGroupWeeklyReportToBeUpdated.Location != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_place", aHappyGroupWeeklyReportToBeUpdated.Location);
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_location", aHappyGroupWeeklyReportToBeUpdated.Location);
                }
                //設定小組聚會時間
                if (aHappyGroupWeeklyReportToBeUpdated.StartTime != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_time", aHappyGroupWeeklyReportToBeUpdated.StartTime);
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_start_time", aHappyGroupWeeklyReportToBeUpdated.StartTime);
                }
                if (aHappyGroupWeeklyReportToBeUpdated.EndTime != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_group_end_time", aHappyGroupWeeklyReportToBeUpdated.EndTime);
                }
                #endregion
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion
        #region 修改幸福小組BEST
        public void UpdateBestRecord(String aPresentRecordId, ref BestRecord aBestRecord, bool PresentFlag, bool DecisionFlag)
        {
            try
            {
                #region 幸福小組週報，同時以名單成員作為初始成員
                // 取得 出席紀錄
                Entity aPresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", new Guid(aPresentRecordId));
                // 更新連絡人 幸福小組紀錄:次數及課程
                UpdateBestContactRecord(aPresentRecordEntity, ref aBestRecord, PresentFlag, DecisionFlag);
                #endregion
                #region 幸福小組週報，同時以名單成員作為初始成員
                this.SetBestPresentRecord(ref aBestRecord, ref aPresentRecordEntity, PresentFlag, DecisionFlag);
                this.m_ToolUtilityClass.UpdateEntity(ref aPresentRecordEntity);
                #endregion
                #region 計算幸福小組週報出席人數
                CalculateWeeklyReportTotalNumber(ref aPresentRecordEntity);
                #endregion
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void UpdateBestContactRecord(Entity aPresentRecordEntity, ref BestRecord aBestRecord, bool PresentFlag, bool DecisionFlag)
        {
            try
            {
                // 取得 BEST 連絡人
                Entity BestContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aPresentRecordEntity, "new_contact_new_present_record"));

                // 設定連絡人參數
                SetBestContactEntity(ref aPresentRecordEntity, ref aBestRecord, ref BestContactEntity, PresentFlag, DecisionFlag);

                // 更新連絡人
                this.m_ToolUtilityClass.UpdateEntity(ref BestContactEntity);

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SetBestContactEntity(ref Entity aPresentRecordEntity, ref BestRecord aBestRecord, ref Entity BestContactEntity, bool PresentFlag, bool DecisionFlag)
        {
            try
            {
                #region 設定BEST 姓名
                if ( aBestRecord.FullName != null && aBestRecord.FullName != "" )
                {
                    Entity aQueryBestContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByName(aBestRecord.FullName);

                    if (aQueryBestContactEntity == null)
                    {
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref BestContactEntity, "lastname", aBestRecord.FullName);
                    }
                    else
                    {
                        #region 系統已經有這個人了
                        #region 比對行動電話是否一致，
                        //this.m_ToolUtilityClass.SetEntityStringAttribute(ref BestContactEntity, "lastname", aBestRecord.FullName + "(BEST)");
                        #endregion
                        #endregion
                    }
                }
                #endregion
                #region 設定BEST 行動電話
                if (aBestRecord.MobilePhone != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref BestContactEntity, "mobilephone", aBestRecord.MobilePhone);
                }
                #endregion
                #region 設定屬靈帶領者

                if (aBestRecord.BestLeader != null)
                {
                    Entity HappyWeeklyReport = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", this.m_ToolUtilityClass.GetEntityLookupAttribute(aPresentRecordEntity, "new_group_present_weekly_report_prese"));

                    Entity HappyListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", this.m_ToolUtilityClass.GetEntityLookupAttribute(HappyWeeklyReport, "new_list_group_present_weekly_report"));
                    //GetContactSpiritLeaderId

                    Guid aContactSpiritLeaderId = GetContactSpiritLeaderId(HappyListEntity.Id, aBestRecord.BestLeader);

                    if (aContactSpiritLeaderId != Guid.Empty)
                    {
                        this.m_ToolUtilityClass.SetEntityLookUpAttribute(BestContactEntity, "new_contact_contact_spiritleader", "contact", aContactSpiritLeaderId);
                    }
                }
                #endregion
                #region 設定個人幸福小組出席紀錄次數與課程
                if (PresentFlag == true)
                {
                    // 從出席紀錄取得週報
                    Entity HappyWeeklyReport = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", this.m_ToolUtilityClass.GetEntityLookupAttribute(aPresentRecordEntity, "new_group_present_weekly_report_prese"));
                    String HappyCourse = ConvertIndexToTopic(this.m_ToolUtilityClass.GetOptionSetAttribute(HappyWeeklyReport, "new_topic"));
                    if (aBestRecord.Present == true)
                    {
                        SetContactHappyTimesAndHistory(ref BestContactEntity, HappyCourse);
                    }
                    else
                    {
                        RemoveContactHappyTimesAndHistory(ref BestContactEntity, HappyCourse);
                    }
                }
                #endregion
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private Guid GetContactSpiritLeaderId(Guid ListEntityId, String BestLeaderName)
        {
            try
            {
                // 每個組員
                bool ListType = false;
                EntityCollection MemberCollection = GetPersonalSmallGroupLeaderMemberData(ListEntityId, ref ListType);

                foreach (Entity MemberEntity in MemberCollection.Entities)
                {
                    #region//取得名單中的每個組員
                    Entity aContactEntity;

                    if (ListType == false)
                    {
                        // 靜態名單
                        aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                    }
                    else
                    {
                        // 動態名單
                        aContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                    }
                    #endregion

                    if (this.m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "fullname") == BestLeaderName)
                    {
                        return aContactEntity.Id;
                    }

                }

                return Guid.Empty;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw Exception;
            }
        }
        public void SetBestPresentRecord(ref BestRecord aBestRecord, ref Entity aPresentRecordEntity, bool PresentFlag, bool DecisionFlag)
        {
            try
            {
                #region 設定幸福小組出席
                if (PresentFlag == true)
                {
                    if (aBestRecord.Present == true)
                    {
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecordEntity, "new_happy_present", 1);
                    }
                    else
                    {
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecordEntity, "new_happy_present", 0);
                    }
                }
                #endregion
                #region 設定幸福小組決志
                if (DecisionFlag == true)
                {
                    if (aBestRecord.Decision == true)
                    {
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecordEntity, "new_happy_decision", 1);
                    }
                    else
                    {
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecordEntity, "new_happy_decision", 0);
                    }
                }
                #endregion
                #region 設定附註或是代禱事項

                // 轉換版本
                // 楊梅靈糧堂
                if (aBestRecord.Note != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecordEntity, "new_name", aBestRecord.Note);
                }
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.Note);

                // 內壢得勝靈糧堂
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region 設定行動電話
                if (aBestRecord.MobilePhone != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecordEntity, "new_cell_hpone", aBestRecord.MobilePhone);
                }
                #endregion
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion


        #region 上傳幸福小組週報
        public void UpdateHappyGroupWeeklyReportList(HappyGroupWeeklyReportListClass ActiveHappyGroupWeeklyReportList)
        {
            try
            {
                foreach (HappyGroupWeeklyReport aHappyGroupWeeklyReport in ActiveHappyGroupWeeklyReportList.HappyGroupWeeklyReportList)
                {
                    if(aHappyGroupWeeklyReport.WeeklyReportModifiedFlag == true )
                    {
                        #region 取得幸福小組週報實體
                        Entity aWeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", new Guid(aHappyGroupWeeklyReport.HappyGroupWeeklyReportId));
                        #endregion
                        #region 幸福小組週報，同時以名單成員作為初始成員
                        this.SetHappyGroupWeeklyReport( aHappyGroupWeeklyReport, ref aWeeklyReportEntity);
                        #endregion
                        #region 取得幸福小組週報實體
                        this.m_ToolUtilityClass.UpdateEntity(ref aWeeklyReportEntity);
                        #endregion
                    }
                    foreach ( BestRecord aBestRecord in aHappyGroupWeeklyReport.BestRecordList )
                    {
                        if (aBestRecord.ModifiedFlag == true)
                        {
                            #region 幸福小組週報，同時以名單成員作為初始成員
                            // 取得 出席紀錄
                            Entity aPresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", new Guid(aBestRecord.BestRecordId));
                            // 更新連絡人 幸福小組紀錄:次數及課程
                            UpdateBestContactRecord(aPresentRecordEntity, aBestRecord, aBestRecord.Present, aBestRecord.Decision);
                            #endregion
                            #region 幸福小組週報，同時以名單成員作為初始成員
                            this.SetBestPresentRecord(aBestRecord, ref aPresentRecordEntity, aBestRecord.Present, aBestRecord.Decision);
                            this.m_ToolUtilityClass.UpdateEntity(ref aPresentRecordEntity);
                            #endregion
                            #region 計算幸福小組週報出席人數
                            CalculateWeeklyReportTotalNumber(ref aPresentRecordEntity);
                            #endregion
                        }
                    }
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void UpdateBestContactRecord(Entity aPresentRecordEntity, BestRecord aBestRecord, bool PresentFlag, bool DecisionFlag)
        {
            try
            {
                // 取得 BEST 連絡人
                Entity BestContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aPresentRecordEntity, "new_contact_new_present_record"));

                // 設定連絡人參數
                SetBestContactEntity(ref aPresentRecordEntity, ref aBestRecord, ref BestContactEntity, PresentFlag, DecisionFlag);

                // 更新連絡人
                this.m_ToolUtilityClass.UpdateEntity(ref BestContactEntity);

                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        public void SetBestPresentRecord( BestRecord aBestRecord, ref Entity aPresentRecordEntity, bool PresentFlag, bool DecisionFlag)
        {
            try
            {
                #region 設定幸福小組出席
                if (PresentFlag == true)
                {
                    if (aBestRecord.Present == true)
                    {
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecordEntity, "new_happy_present", 1);
                    }
                    else
                    {
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecordEntity, "new_happy_present", 0);
                    }
                }
                #endregion
                #region 設定幸福小組決志
                if (DecisionFlag == true)
                {
                    if (aBestRecord.Decision == true)
                    {
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecordEntity, "new_happy_decision", 1);
                    }
                    else
                    {
                        this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecordEntity, "new_happy_decision", 0);
                    }
                }
                #endregion
                #region 設定附註或是代禱事項

                // 轉換版本
                // 楊梅靈糧堂
                if (aBestRecord.Note != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecordEntity, "new_name", aBestRecord.Note);
                }
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.Note);

                // 內壢得勝靈糧堂
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region 設定行動電話
                if (aBestRecord.MobilePhone != null)
                {
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecordEntity, "new_cell_hpone", aBestRecord.MobilePhone);
                }
                #endregion
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion

        #region 計算幸福小組週報出席人數
        public void CalculateWeeklyReportTotalNumber(ref Entity aPresentRecordEntity)
        {
            try
            {
                #region 從出席紀錄取得週報
                Entity HappyWeeklyReport = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", this.m_ToolUtilityClass.GetEntityLookupAttribute(aPresentRecordEntity, "new_group_present_weekly_report_prese"));
                #endregion
                #region 取得跟週報所有的相關的靈修出席紀錄單
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("new_group_present_weekly_report", "new_group_present_weekly_reportid", HappyWeeklyReport.Id.ToString(), "new_group_present_weekly_report_prese", "new_present_record");
                #endregion
                #region 計算幸福小組週報出席及決志人數
                int TotalHappyPresent = 0;
                int TotalHappyDecision = 0;
                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    TotalHappyPresent += this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_happy_present");
                    TotalHappyDecision += this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_happy_decision");
                }
                #endregion

                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_small_group_number", TotalHappyPresent);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_decision_number", TotalHappyDecision);

                this.m_ToolUtilityClass.UpdateEntity(ref HappyWeeklyReport);


                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        #endregion
        #region 轉換下拉選項
        private String ConvertIndexToTopic(int FollowUpWeekIndex)
        {
            switch (FollowUpWeekIndex)
            {
                case 100000000:
                    return "福音的能力";
                case 100000001:
                    return "幸福小組是符合聖經的佈道小組";
                case 100000002:
                    return "真幸福";
                case 100000003:
                    return "欺騙者";
                case 100000004:
                    return "救贖者耶穌";
                case 100000005:
                    return "垂聽禱告的上帝";
                case 100000006:
                    return "你要遇見神";
                case 100000007:
                    return "耶穌基督十字架的勝利";
                case 100000008:
                    return "釋放與自由";
                case 100000009:
                    return "帶來幸福的教會";
                default:
                    return "";
            }
        }
        private int ConvertTopicToIndex(String Topic)
        {
            switch (Topic)
            {
                case "福音的能力":
                    return 100000000;
                case "幸福小組是符合聖經的佈道小組":
                    return 100000001;
                case "真幸福":
                    return 100000002;
                case "欺騙者":
                    return 100000003;
                case "救贖者耶穌":
                    return 100000004;
                case "垂聽禱告的上帝":
                    return 100000005;
                case "你要遇見神":
                    return 100000006;
                case "耶穌基督十字架的勝利":
                    return 100000007;
                case "釋放與自由":
                    return 100000008;
                case "帶來幸福的教會":
                    return 100000009;
                default:
                    return 100000000;
            }
        }
        #endregion
        #endregion
        #region 工具區
        private EntityCollection GetPersonalSmallGroupLeaderMemberData(Guid ListEntityId, ref bool ListType)
        {
            #region // 處理每個小組名單
            //搜尋名單的組員
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);

            ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");
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

            return MemberCollection;
            #endregion

        }

        #endregion
        #endregion
        #endregion

    }
}
