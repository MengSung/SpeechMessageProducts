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
using ChurchReport.ViewModels;
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class PersonalInfomatioManager
    {
        #region 資料區
        #region 參數資料
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();

        bool m_SetIdentityFlag = false;

        private const int EMPTY_VALUE = -999999999;

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
        private LineNotifyUtility m_LineNotifyUtility = new LineNotifyUtility();

        MemberInfomationPackage m_MemberInfomationPackage = new MemberInfomationPackage();
        DateTime m_Sunday;
        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給族系族長/小家長的名單

        Guid m_DecipleGroupListId;
        //Guid m_GroupLeaderId; // 小組長
        Guid m_RaceLeaderId; // 族系族長
        String m_SmallGroupPlace;
        String m_SmallGroupTime;

        Guid m_OwnerId; // 小組長的負責人 Id

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
        //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        //List<Place2> m_GroupNamePlaces = new List<Place2>(); // 依據群組名稱過濾出來的會眾集合
        List<MemberInfomation> m_GroupNamedListMemberInfomation = new List<MemberInfomation>(); // 依據群組名稱過濾出來的會眾集合
        #endregion
        #region 建立新人
        public String CreateNewContact(AccountPasswordData aAccountPasswordData, ref NewContact aNewContact)
        {
            try
            {
                // 這是新建立的連絡人
                Entity aNewContactEntity = new Entity("contact");

                // 設定連絡人相關屬性
                Entity aListEntity = GetRelatedList(aAccountPasswordData, aNewContact.GroupName);

                if (aListEntity != null)
                {
                    SetupNewContactParameter(ref aNewContactEntity, aAccountPasswordData, ref aNewContact, aListEntity.Id);
                }
                else
                {
                    SetupNewContactParameter(ref aNewContactEntity, aAccountPasswordData, ref aNewContact, Guid.Empty);
                }

                // 新增連絡人
                Guid NewContactEntityId = this.m_ToolUtilityClass.CreateEntity(aNewContactEntity);
                aNewContact.PresentRecordId = NewContactEntityId.ToString();

                // 指派新增連絡人的負責人
                this.m_ToolUtilityClass.AssignOwner("contact", this.m_ToolUtilityClass.RetrieveEntity("contact", NewContactEntityId), this.m_OwnerId);

                // 將剛剛新增的聯絡人加入至成員名單
                ConnectNewContactInMemberList(NewContactEntityId, aNewContact.GroupName, aListEntity);

                #region 建立個人聚會與靈修記錄
                if (aListEntity != null)
                {
                    // 有找到被關聯的小組名單
                    CreateNewContactPresentRecord(aListEntity, NewContactEntityId, aNewContact.GroupName, ref aNewContact);
                    #region 關聯主要小組
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_cell_list_contact", "list", aListEntity.Id);
                    #endregion
                }
                #endregion
                #region 更新新建立的連絡人
                aNewContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", NewContactEntityId);
                this.m_ToolUtilityClass.UpdateEntity(ref aNewContactEntity);
                #endregion

                String LoginContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "fullname");

                String Result = LoginContactFullName + " 成功建立新人並且加入 " + aNewContact.Name + " 到 " + aNewContact.GroupName + "小組中";

                if (aListEntity != null)
                {
                    this.m_LineNotifyUtility.SendAddNewPersonResultLine(Result, aListEntity);
                    this.m_LineNotifyUtility.SendListMemberLine(aListEntity);
                }

                return Result;
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private void SetupNewContactParameter(ref Entity aNewContactEntity, AccountPasswordData aAccountPasswordData, ref NewContact aNewContact, Guid aListEntityId)
        {
            #region 關聯小組長屬性 找到小組長ID
            if (aAccountPasswordData.Account != "LineIdLogin")
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(aAccountPasswordData.Account, aAccountPasswordData.Password);
            }
            else
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(aAccountPasswordData.Password);
            }
            m_ContactId = m_ContactEntity.Id;

            // 小組長的負責人 Id
            m_OwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ContactEntity);

            #endregion
            #region 蒐集建立新人所需要的屬性

            // 搜尋小組長的門徒小組名單Lookup Id
            m_DecipleGroupListId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_deciple_group_list_contact");
            // 搜尋小組長的族系組長 Lookup Id
            m_RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_race_leader_contact");

            #endregion
            #region 建立關聯
            #region 關聯所屬教會
            //取得小組長的所屬教會
            Guid AccountId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "parentcustomerid");
            if (AccountId != Guid.Empty)
            { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "parentcustomerid", "account", AccountId); }
            #endregion
            #region 關聯族系組長屬性
            if (m_RaceLeaderId != Guid.Empty)
            { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_race_leader_contact", "contact", m_RaceLeaderId); }
            #endregion
            #region 關聯主要小組
            String GroupType = "";
            if (aListEntityId != null && aListEntityId != Guid.Empty)
            {
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_cell_list_contact", "list", aListEntityId);
            }
            #endregion
            #region 關聯邀請或轉介人
            if (m_ContactId != null && m_ContactId != Guid.Empty)
            {
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_invitnewperson_contact", "contact", m_ContactId);
            }
            #endregion
            #endregion
            #region 基本資料

            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "lastname", aNewContact.Name);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "mobilephone", aNewContact.MobilePhone);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "telephone2", aNewContact.HomePhone);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "address2_line1", aNewContact.Address);

            // 委身類型設定為新朋友


            // 內壢南崁基督長老教會牧養新朋友稱呼代碼，跟台中思恩堂不一樣
            //台中思恩堂
            if (aListEntityId != Guid.Empty)
            {
                if (this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ToolUtilityClass.RetrieveEntity("list", aListEntityId), "listname").Contains("幸福"))
                {
                    // 幸福小組新增的新人，委身類型設為"幸福 Best"
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "customertypecode", 100000005);
                }
                else
                {
                    if (aNewContact.CustomerTypeCode == "小組組員")
                    {
                        // 一般小組新增的新人，委身類型設為"小組組員"
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "customertypecode", 1);
                    }
                    else if (aNewContact.CustomerTypeCode == "新朋友")
                    {
                        // 一般小組新增的新人，委身類型設為"新朋友"
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "customertypecode", 100000000);
                    }
                    else
                    {
                        // 一般小組新增的新人，委身類型設為"新朋友"
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "customertypecode", 100000000);
                    }
                }
            }
            else
            {
                if (aNewContact.CustomerTypeCode == "小組組員")
                {
                    // 一般小組新增的新人，委身類型設為"小組組員"
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "customertypecode", 1);
                }
                else if (aNewContact.CustomerTypeCode == "新朋友")
                {
                    // 一般小組新增的新人，委身類型設為"新朋友"
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "customertypecode", 100000000);
                }
                else
                {
                    // 一般小組新增的新人，委身類型設為"新朋友"
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "customertypecode", 100000000);
                }
            }
            // 內壢南崁基督長老教會
            //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "customertypecode", 100000009);

            // 生日
            if (aNewContact.BirthDate.Year != 1919)
            {
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewContactEntity, "birthdate", aNewContact.BirthDate);
            }
            // 進教會日期
            if (aNewContact.FirstChurchDate.Year != 1919)
            {
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewContactEntity, "new_enter_church_date", aNewContact.FirstChurchDate);
            }

            // 性別，台中思恩堂
            if (aNewContact.Gender == "男性")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "gendercode", 200000); }
            else if (aNewContact.Gender == "女性")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "gendercode", 200001); }
            else { }

            // 內壢南崁基督長老教會性別值，跟南崁基督長老教會不一樣
            //if (aNewContact.Gender)
            //{ this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "gendercode", 100000001); }
            //else
            //{ this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "gendercode", 100000000); }

                // "未知", "已婚", "未婚", "離異", "喪偶","單身"
            if (aNewContact.MerrageState == "已婚")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 1); }
            else if (aNewContact.MerrageState == "未婚")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 2); }
            else if (aNewContact.MerrageState == "離異")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 3); }
            else if (aNewContact.MerrageState == "喪偶")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 4); }
            else if (aNewContact.MerrageState == "單身")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 100000000); }
            else if (aNewContact.MerrageState == "未知")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 100000001); }
            else if (aNewContact.MerrageState == "失婚")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 100000002); }
            else if (aNewContact.MerrageState == "單親")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 100000003); }
            else if (aNewContact.MerrageState == "婚姻")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 100000004); }
            else if (aNewContact.MerrageState == "離婚")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 100000005); }
            else
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "familystatuscode", 100000001); }


            // "基督徒", "慕道友"
            if (aNewContact.FaithStatus == "-未知-")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "new_spiriitual_identity", 100000004); }
            else if (aNewContact.FaithStatus == "基督徒")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "new_spiriitual_identity", 100000001); }
            else if (aNewContact.FaithStatus == "已決志")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "new_spiriitual_identity", 100000002); }
            else if (aNewContact.FaithStatus == "慕道友")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "new_spiriitual_identity", 100000005); }
            else if (aNewContact.FaithStatus == "未信主")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "new_spiriitual_identity", 100000003); }
            else
            {
                // -未知-
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewContactEntity, "new_spiriitual_identity", 100000004);
            }


            // 來源
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "new_coming_reason", aNewContact.Source);

            // 邀請人相關欄位設定
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "new_invitor", aNewContact.Introducer);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "assistantphone", aNewContact.IntroducerPhone);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "new_carers", aNewContact.IntroducerRelation);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "new_invitor_group", aNewContact.IntroducerGroup);

            // 首次參加活動日期
            if (aNewContact.FirstActionDate.Year > 1000)
            {
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewContactEntity, "new_recently_visitchurch_date", aNewContact.FirstActionDate);
            }

            // 首次參加教會主日日期
            if (aNewContact.FirstChurchDate.Year > 1000)
            {
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aNewContactEntity, "new_enter_church_date", aNewContact.FirstChurchDate);
            }

            // 職業及專長
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "new_industry", aNewContact.Industry);

            // 設定描述是由APP建立的
            String aFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_ContactEntity, "fullname");
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewContactEntity, "description", aFullName + " 透過網頁回報建立的新人" + Environment.NewLine + aNewContact.Note);

            #endregion
        }
        private Entity GetRelatedList(AccountPasswordData aAccountPasswordData, String GroupName)
        {
            try
            {
                #region 關聯小組長屬性 找到小組長ID
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
                #endregion

                #region 蒐集建立新人所需要的屬性

                // 搜尋小組長的門徒小組名單Lookup Id
                m_DecipleGroupListId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_deciple_group_list_contact");
                // 搜尋小組長的族系組長 Lookup Id
                m_RaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref m_ContactEntity, "new_race_leader_contact");

                #endregion

                // 根據是否是族系族長還是小組長會設定不同的要上傳的名單集合
                // 並且該名單是有勾選APP點名的才被允許進來
                this.FindListCollection();

                // 找到要被關聯的小組名單集合
                Entity FoundListEntity = FindListByName(GroupName);

                if (FoundListEntity != null)
                {
                    return FoundListEntity;
                }
                else
                {
                    return this.m_ToolUtilityClass.RetrieveListEntityByName(GroupName);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        private void ConnectNewContactInMemberList(Guid NewContactEntityId, String GroupName, Entity aListEntity)
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
                        memberGuidList.Add(NewContactEntityId);
                        m_ToolUtilityClass.AddMembersToMarketingList(aListEntity.Id, memberGuidList);
                    }
                    else
                    {
                        // 動態名單
                        Entity aNewContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", NewContactEntityId);
                        EntityReference aListEntityReference = new EntityReference("list", aListEntity.Id);

                        // 內壢南崁基督長老教會
                        //this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_cell_list_contact", ref aListEntityReference);
                        // 南崁基督長老教會
                        this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewContactEntity, "new_list_contact", ref aListEntityReference);

                        this.m_ToolUtilityClass.UpdateEntity(ref aNewContactEntity);
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
        private void CreateNewContactPresentRecord(Entity aListEntity, Guid NewContactEntityId, String GroupName, ref NewContact aNewContact)
        {
            try
            {
                #region 建立個人聚會與靈修記錄

                if (aListEntity == null || NewContactEntityId == null || NewContactEntityId == Guid.Empty)
                { return; }

                //// 取得每個需要點名的名單裡的每個週報
                //EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", aListEntity.Id.ToString(), "new_list_group_present_weekly_report", "new_group_present_weekly_report");
                //if (GroupWeeklyReportEntityCollection == null || GroupWeeklyReportEntityCollection.Entities.Count <= 0)
                //{ return; }

                // 根據日期看有沒有那個週報
                #region 先根據日期尋找當週主日日期
                // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                int DayOfWeek = (int)DateTime.Now.DayOfWeek;

                // 當週的星期日為認定的主日
                //this.m_Sunday = DateTime.Now.AddDays(-DayOfWeek);
                DateTime aSunday;
                // 每周以星期六為第一日
                if (DayOfWeek != 6)
                {
                    // 如果不是星期六則是上個星期天
                    aSunday = DateTime.Now.AddDays(-DayOfWeek);
                }
                else
                {
                    // 如果是星期六則是下個星期天
                    aSunday = DateTime.Now.AddDays(1);
                }
                #endregion
                //Entity GroupWeeklyReportEntity = FilterWeeklyReportByDate(ref GroupWeeklyReportEntityCollection);
                //if (GroupWeeklyReportEntity == null)
                //{ return; }

                // 尋找此小組的某一個主日的週報集合
                EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.QueryWeeklyReportBySunday(this.m_Sunday, aListEntity.Id);

                // 此小組的某一個主日的週報集合，應該僅有一個，也就是第0個的週報
                //Entity GroupWeeklyReportEntity = GroupWeeklyReportEntityCollection.Entities.Count == 1 ? GroupWeeklyReportEntityCollection.Entities[0] : null;
                if (GroupWeeklyReportEntityCollection.Entities.Count == 1)
                {
                    EntityCollection PresentRecordEntityCollection = m_ToolUtilityClass.QueryPresentRecordInWeeklyReportByContactId(NewContactEntityId, GroupWeeklyReportEntityCollection.Entities[0].Id);

                    if (PresentRecordEntityCollection.Entities.Count == 0)
                    {
                        // 還沒有出席紀錄單才要建立
                        // 這是新建立的個人聚會與靈修記錄
                        Entity aPresentRecord = new Entity("new_present_record");

                        // 設定個人聚會與靈修記錄相關屬性
                        #region 準備所需要的參數

                        #region 個人資料
                        if (NewContactEntityId == null || NewContactEntityId == Guid.Empty)
                        { return; }
                        Entity aNewContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", NewContactEntityId);
                        if (aNewContactEntity == null)
                        { return; }
                        Guid aWeeklyReportId = GroupWeeklyReportEntityCollection.Entities[0].Id;

                        // 組員的全名
                        String FullName = "";
                        if (aNewContactEntity.Attributes.Contains("fullname"))
                        {
                            FullName = (string)aNewContactEntity.Attributes["fullname"];
                        }
                        // 組員的手機
                        String aMobilePhone = "";
                        if (aNewContactEntity.Attributes.Contains("mobilephone"))
                        {
                            aMobilePhone = (string)aNewContactEntity.Attributes["mobilephone"];
                        }
                        // 組員的家裡電話
                        String aHomePhone = "";
                        if (aNewContactEntity.Attributes.Contains("telephone2"))
                        {
                            aHomePhone = (string)aNewContactEntity.Attributes["telephone2"];
                        }
                        // 組員的地址
                        String aAddress = "";
                        if (aNewContactEntity.Attributes.Contains("address2_line1"))
                        {
                            aAddress = (string)aNewContactEntity.Attributes["address2_line1"];
                        }
                        #endregion

                        // 取得新人跟進週次，及跟進歷程記錄
                        String aFollowUpWeek = "";
                        String aNewComerNote = GetNewComerFollowupInfo(NewContactEntityId, ref aFollowUpWeek);

                        MemberInfomation aMemberInfomation = new MemberInfomation()
                        {
                            Group = GroupName,
                            Name = FullName,
                            Phone = DigitsOnly.Replace(aMobilePhone, ""),
                            HomePhone = DigitsOnly.Replace(aHomePhone, ""),
                            Address = aAddress,
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
                            FollowUpWeek = ".",
                            FollowUpResult = ".",
                            FollowUpNextStep = ".",
                            FollowUp = "",
                            FollowUpNote = "",
                            NewComerNote = aNewComerNote,
                            #region 靈修、晨、晚禱
                            SpiritualWork = 0,
                            MorningPray = 0,
                            GeneralCare = 0,
                            #endregion

                        };

                        Double DUM_DOUBLE = 0;
                        int DUM_INT = 0;
                        #endregion

                        this.SetupPresentRecordEntityAttributes(aPresentRecord, aMemberInfomation, ref aNewContactEntity, ref aListEntity, ref aWeeklyReportId, DUM_DOUBLE, ref DUM_DOUBLE, ref DUM_DOUBLE, ref DUM_INT, ref DUM_INT, ref DUM_INT, ref DUM_INT);

                        // 新增個人聚會與靈修記錄
                        Guid aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);
                        Entity aRetrievedPresentRecord = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);

                        //指派負責人
                        this.m_ToolUtilityClass.AssignOwner("new_present_record", aRetrievedPresentRecord, this.m_ToolUtilityClass.GetOwnerId(aNewContactEntity));

                        aNewContact.PresentRecordId = aPresentRecordId.ToString();

                    }
                }
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
            }
        }
        #endregion
        #region 設定個人相關資料
        public void SetPersonalInfomationViewModel(Entity aContactEntity, ref PersonalInfomationViewModel aPersonalInfomationViewModel)
        {
            #region 基本資料

            aPersonalInfomationViewModel.FullName = aPersonalInfomationViewModel.FirstName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "fullname");//登入者全名
            aPersonalInfomationViewModel.Phone = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "mobilephone");//行動電話
            aPersonalInfomationViewModel.HomePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "telephone2");// 住家電話
            aPersonalInfomationViewModel.OfficePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "company");//公司電話
            aPersonalInfomationViewModel.Facebook = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_fb_account");//Facebook帳號
            aPersonalInfomationViewModel.Instagram = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_ig_account");//Instagram帳號
            aPersonalInfomationViewModel.Email = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "emailaddress1"); //電子郵件
            aPersonalInfomationViewModel.LastSixDigit = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_last_six_digit");// 銀行帳戶後六碼
            //aPersonalInfomationViewModel.NtbtOrNot = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aContactEntity, "new_ntbt_ornot");// 是否上傳國稅局
            aPersonalInfomationViewModel.Address = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "address2_line1");// 地址
            aPersonalInfomationViewModel.PersonalId = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_personal_id");// 身份證字號

            aPersonalInfomationViewModel.NtbtOrNot = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aContactEntity, "new_ntbt_ornot") == true ? "是" : "否";// 是否上傳國稅局

            // 委身類型
            aPersonalInfomationViewModel.CustomerTypeCode = ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode"));

            // 生日
            DateTime Birthday = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContactEntity, "birthdate");
            if (Birthday.Year != 1)
            {
                aPersonalInfomationViewModel.BirthDate = Birthday.ToLocalTime();
            }

            // 進教會日期
            DateTime FirstChurchDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContactEntity, "new_enter_church_date");
            if ( FirstChurchDate.Year != 1 )
            {
                aPersonalInfomationViewModel.HireDate = FirstChurchDate.ToLocalTime();
            }

            // 性別
            if (this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "gendercode") == 200000)
            { aPersonalInfomationViewModel.Gender = "男性"; }
            else { aPersonalInfomationViewModel.Gender = "女性"; }

            // 婚姻狀態 : "未知", "已婚", "未婚", "離異", "喪偶","單身"
            int MerrageState = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "familystatuscode");
            if (MerrageState == 1)
            { aPersonalInfomationViewModel.MerrageState = "已婚"; }
            else if (MerrageState == 2)
            { aPersonalInfomationViewModel.MerrageState = "未婚"; }
            else if (MerrageState == 3)
            { aPersonalInfomationViewModel.MerrageState = "離異"; }
            else if (MerrageState == 4)
            { aPersonalInfomationViewModel.MerrageState = "喪偶"; }
            else if (MerrageState == 100000000)
            { aPersonalInfomationViewModel.MerrageState = "單身"; }
            else if (MerrageState == 100000001)
            { aPersonalInfomationViewModel.MerrageState = "未知"; }
            else if (MerrageState == 100000002)
            { aPersonalInfomationViewModel.MerrageState = "失婚"; }
            else if (MerrageState == 100000003)
            { aPersonalInfomationViewModel.MerrageState = "單親"; }
            else if (MerrageState == 100000004)
            { aPersonalInfomationViewModel.MerrageState = "婚姻"; }
            else if (MerrageState == 100000005)
            { aPersonalInfomationViewModel.MerrageState = "離婚"; }
            else
            {
                //未知
                aPersonalInfomationViewModel.MerrageState = "未知";
            }

            // 信仰狀態 : "基督徒", "慕道友"
            int FaithStatus = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity");

            if (FaithStatus == 100000004)
            { aPersonalInfomationViewModel.Status = "-未知-"; }
            else if (FaithStatus == 100000001)
            { aPersonalInfomationViewModel.Status = "基督徒"; }
            else if (FaithStatus == 100000002)
            { aPersonalInfomationViewModel.Status = "已決志"; }
            else if (FaithStatus == 100000005)
            { aPersonalInfomationViewModel.Status = "慕道友"; }
            else if (FaithStatus == 100000003)
            { aPersonalInfomationViewModel.Status = "未信主"; }
            else 
            {
                // -未知-
                aPersonalInfomationViewModel.Status = "-未知-";
            }

            // 邀請人相關欄位設定
            aPersonalInfomationViewModel.Introducer = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_invitor");
            aPersonalInfomationViewModel.IntroducerRelation = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_carers");

            //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_invitor", aNewContact.Introducer);// 邀請人
            //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "assistantphone", aNewContact.IntroducerPhone);
            //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_carers", aNewContact.IntroducerRelation);
            //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_invitor_group", aNewContact.IntroducerGroup);

            // 職業及專長 Industry
            aPersonalInfomationViewModel.Industry = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_industry");

            // 裝備狀態
            aPersonalInfomationViewModel.EquipmentStatus = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "new_equipment_status");

            // 設定描述
            aPersonalInfomationViewModel.Notes = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "description");

            #endregion
        }

        // 委身類型客製化，委身類型客製化
        //南崁基督長老教會
        private String ConvertIndexToIdentity(int CustomerTypeCode)
        {
            switch (CustomerTypeCode)
            {
                case 100000006:
                    return "牧師師母";
                case 100000002:
                    return "區牧";
                case 100000003:
                    return "小區長";
                case 100000008:
                    return "小組長";
                case 100000009:
                    return "副小組長";
                case 100000012:
                    return "核心同工";
                case 1:
                    return "小組組員";
                case 100000005:
                    return "幸福BEST";
                case 100000004:
                    return "未入組";
                case 100000000:
                    return "新朋友";
                case 100000007:
                    return "外教會";
                case 100000001:
                    return "結案";
                default:
                    return "未知";
            }
        }

        #endregion
        #region 上傳個人相關資料
        public void UpdatePersonalInfomationViewModel(PersonalInfomationViewModel aPersonalInfomationViewModel, Entity aContactEntity )
        {
            #region 基本資料

            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "mobilephone", aPersonalInfomationViewModel.Phone);               //行動電話
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "telephone2", aPersonalInfomationViewModel.HomePhone);            //住家電話
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "company", aPersonalInfomationViewModel.OfficePhone);             //公司電話
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_fb_account", aPersonalInfomationViewModel.Facebook);         //Facebook帳號
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_ig_account", aPersonalInfomationViewModel.Instagram);         //instagram帳號
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "emailaddress1", aPersonalInfomationViewModel.Email);             //電子郵件
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_last_six_digit", aPersonalInfomationViewModel.LastSixDigit); // 銀行帳戶後六碼
            //this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aContactEntity, "new_ntbt_ornot", aPersonalInfomationViewModel.NtbtOrNot);          // 是否上傳國稅局
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "address2_line1", aPersonalInfomationViewModel.Address);          // 地址
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_personal_id", aPersonalInfomationViewModel.PersonalId);      // 身份證字號

            // 是否上傳國稅局
            if (aPersonalInfomationViewModel.NtbtOrNot == "是")
            {
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aContactEntity, "new_ntbt_ornot", true);
            }
            else
            {
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aContactEntity, "new_ntbt_ornot", false);
            }

            // 委身類型
            if (aPersonalInfomationViewModel.CustomerTypeCode == "小組組員")
            {
                // 一般小組新增的新人，委身類型設為"小組組員"
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "customertypecode", 1);
            }
            else if (aPersonalInfomationViewModel.CustomerTypeCode == "新朋友")
            {
                // 一般小組新增的新人，委身類型設為"新朋友"
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "customertypecode", 100000000);
            }
            else
            {
                // 一般小組新增的新人，委身類型設為"新朋友"
                //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "customertypecode", 100000000);
            }

            // 生日
            if (aPersonalInfomationViewModel.BirthDate.Year != 1919)
            {
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aContactEntity, "birthdate", aPersonalInfomationViewModel.BirthDate);
            }

            // 性別
            if (aPersonalInfomationViewModel.Gender == "男性")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "gendercode", 200000); }
            else if (aPersonalInfomationViewModel.Gender == "女性")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "gendercode", 200001); }
            else { }

            // 婚姻狀態 : "未知", "已婚", "未婚", "離異", "喪偶","單身"
            if (aPersonalInfomationViewModel.MerrageState == "已婚")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 1); }
            else if (aPersonalInfomationViewModel.MerrageState == "未婚")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 2); }
            else if (aPersonalInfomationViewModel.MerrageState == "離異")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 3); }
            else if (aPersonalInfomationViewModel.MerrageState == "喪偶")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 4); }
            else if (aPersonalInfomationViewModel.MerrageState == "單身")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 100000000); }
            else if (aPersonalInfomationViewModel.MerrageState == "未知")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 100000001); }
            else if (aPersonalInfomationViewModel.MerrageState == "失婚")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 100000002); }
            else if (aPersonalInfomationViewModel.MerrageState == "單親")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 100000003); }
            else if (aPersonalInfomationViewModel.MerrageState == "婚姻")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 100000004); }
            else if (aPersonalInfomationViewModel.MerrageState == "離婚")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 100000005); }
            else//"未知"
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "familystatuscode", 100000001); }



            // 信仰狀態 : "基督徒", "慕道友"
            if (aPersonalInfomationViewModel.Status == "-未知-")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity", 100000004); }
            else if (aPersonalInfomationViewModel.Status == "基督徒")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity", 100000001); }
            else if (aPersonalInfomationViewModel.Status == "已決志")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity", 100000002); }
            else if (aPersonalInfomationViewModel.Status == "慕道友")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity", 100000005); }
            else if (aPersonalInfomationViewModel.Status == "未信主")
            { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity", 100000003); }
            else
            {
                // -未知-
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "new_spiriitual_identity", 100000004);
            }

            // 邀請人相關欄位設定
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_invitor", aPersonalInfomationViewModel.Introducer);
            //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "assistantphone", aPersonalInfomationViewModel.IntroducerPhone);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_carers", aPersonalInfomationViewModel.IntroducerRelation);
            //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_invitor_group", aPersonalInfomationViewModel.IntroducerGroup);

            // 職業及專長 Industry
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aContactEntity, "new_industry", aPersonalInfomationViewModel.Industry);

            #endregion

            // 更新登入者個人相關資料
            this.m_ToolUtilityClass.UpdateEntity(ref aContactEntity);
        }
        #endregion
        #region 所需要的工具
        private void FindListCollection()
        {
            try
            {
                // 初始化 m_Lists
                // 小組同工 new_contact_list_vice_family_leader
                //this.m_Lists = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_vice_family_leader", "list");  // 小組同工
                this.m_Lists = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 小組同工
                MergeCollectionSmallGroupAhead(ref this.m_Lists);

                // 小組長/小組同工 new_contact_family_leader_list
                //EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");  // 小組長/小組同工
                EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/小組同工
                //aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/小組同工
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 共同小家長 new_contact_co_race_leager_list
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_co_race_leager_list", "list");  // 共同小家長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_co_race_leager_list");  // 共同小家長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 上代組長 new_contact_race_leager_list
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_race_leager_list", "list");  // 上代組長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_race_leager_list");  // 上代組長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 族系族長 new_contact_list_arealeader
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_arealeader", "list");  // 族系族長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_arealeader");  // 族系族長
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 共同上代族系族長 new_contact_list_co_arealeader
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_co_arealeader");  // 共同上代族系族長
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
                // 族系族長或是小家長的名單若是與小組長名單重疊，則要過濾出僅有族長/小家長的名單
                // 合併小組名單至族系名單，單扣除掉重複的
                // 然後放在小組名單裡面
                // 一個一個處理族系名單
                foreach (Entity aListEntity in aListEntityCollection.Entities)
                {
                    // 處理一個族系族長的名單
                    bool SearchedFlag = false;
                    foreach (Entity m_ListEntity in this.m_Lists.Entities)
                    {
                        // 比對每一個小組名單
                        if (aListEntity.Id == m_ListEntity.Id)
                        {
                            // 族系族長的名單與小組長的名單有相同的了
                            SearchedFlag = true;
                            break;
                        }
                    }

                    if (SearchedFlag == false)
                    {
                        // 族系族長的名單沒有與小組長名單相同的
                        if (this.m_ToolUtilityClass.GetEntityBoolAttribute(aListEntity, "new_app_named") == true)
                        {
                            // 點名有打勾
                            m_Lists.Entities.Add(aListEntity);
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

                            #region 先根據日期尋找當週主日日期
                            // 其值的範圍從 0 (表示 DayOfWeek.Sunday) 為 6 (表示 DayOfWeek.Saturday)。
                            int DayOfWeek = (int)DateTime.Now.DayOfWeek;

                            // 當週的星期日為認定的主日
                            //this.m_Sunday = DateTime.Now.AddDays(-DayOfWeek);
                            DateTime aSunday;
                            // 每周以星期六為第一日
                            if (DayOfWeek != 6)
                            {
                                // 如果不是星期六則是上個星期天
                                aSunday = DateTime.Now.AddDays(-DayOfWeek);
                            }
                            else
                            {
                                // 如果是星期六則是下個星期天
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
                else if (Gender == 200001)
                {
                    aFollowUpHistoryReport += "性別:女性" + Environment.NewLine;
                }
                else
                {
                    aFollowUpHistoryReport += "性別:未知" + Environment.NewLine;
                }
                #endregion
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
                        this.m_ToolUtilityClass.UpdateEntity(PresentRecordEntity);
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
                else if (Gender == 200001)
                {
                    aFollowUpHistoryReport += "性別:女性" + Environment.NewLine;
                }
                else
                {
                    aFollowUpHistoryReport += "性別:未知" + Environment.NewLine;
                }

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
                        this.m_ToolUtilityClass.UpdateEntity(PresentRecordEntity);
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
                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                throw e;
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
                #region 從名單取得 小家長 ID、小組長 ID、小家長 ID
                // 小家長 ID
                Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");

                // 小組長 ID
                Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");

                // 小家長 ID
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

                // 南崁基督長老教會
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", aMemberInfomation.Note);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.Note);

                // 內壢南崁基督長老教會
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_memo", aMemberInfomation.Note);
                #endregion
                #region// 新人跟進

                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMemberInfomation.FollowUpWeek));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMemberInfomation.FollowUpResult));
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMemberInfomation.FollowUpNextStep));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_follow_up", aMemberInfomation.FollowUpOption);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.FollowUpNote);

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
                if (TRANSFER_IDENTITY_FLAG == true)
                {
                    // 新朋友
                    if (Counter >= NewComeMaxiNumber && m_SetIdentityFlag == false)
                    {
                        // 只要設定一次就好
                        m_SetIdentityFlag = true;

                        // 新朋友變為未入組
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000004);
                        this.m_ToolUtilityClass.UpdateEntity(ref aContact);
                    }
                    else { }
                }
            }
            else if (aIdentityNumber == 100000004)
            {
                if (TRANSFER_IDENTITY_FLAG == true)
                {
                    //未入組
                    if (Counter >= UnGroupMaxiNumber && m_SetIdentityFlag == false)
                    {
                        // 只要設定一次就好
                        m_SetIdentityFlag = true;

                        // 未入組變為未入組結案(超過或是等於)
                        this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContact, "customertypecode", 100000008);

                        this.m_ToolUtilityClass.UpdateEntity(ref aContact);
                    }
                    else { }
                }
            }
            else
            {

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
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計小組組員未出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) ");
                            }
                        }
                        else
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員未出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) ");
                            }
                        }

                        return;
                    case "未入組":

                        if (Type == "主日")
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計未入組出未席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) ");
                            }
                        }
                        else
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出未席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) ");
                            }
                        }

                        return;
                    case "新朋友":

                        if (Type == "主日")
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計新人出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "主日統計新人未出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "主日", ref aContact).ToString() + "次) ");
                            }
                        }
                        else
                        {
                            if (Presentflag == true)
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) ");
                            }
                            else
                            {
                                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人未出席字串", ContactName + "(共出席" + GetPresentNumber(aListEntityId, "小組", ref aContact).ToString() + "次) ");
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
        public bool IsValidContact(Entity aContactEntity)
        {
            try
            {
                #region// 處理組員是否列入計算

                // 找到該組員的屬性
                int aCustomerTypeCodeValue = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");
                //OptionSetValue aCustomerTypeCode = aContactEntity.Attributes["customertypecode"] as OptionSetValue;

                // 如果是新朋友或是未入組則不列入累積，南崁基督長老教會
                if (aCustomerTypeCodeValue != 100000004 && aCustomerTypeCodeValue != 100000000 && aCustomerTypeCodeValue != 100000007 && aCustomerTypeCodeValue != EMPTY_VALUE)
                {
                    return true;
                }
                else
                {
                    return false;
                }


                // 如果是新朋友或是未入組則不列入累積，內壢南崁基督長老教會
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
        private String SetFollowUpString(ref MemberInfomation aMemberInfomation)
        {
            try
            {
                return
                    AppendHeadString("\t\t\t跟進週次:", aMemberInfomation.FollowUpWeek) +
                    AppendHeadString("\t\t\t跟進方式:", aMemberInfomation.FollowUpOption) +
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
        #endregion
    }
}
