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
    public class DownloadEquipment
    {
        #region 資料區
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365-9.0");
        #endregion
        #region 下載資料時所需要的參數
        private EquipmentRootClass m_LocalEquipmenRoot;

        // 一個人有多個幸福小組
        public HappyGroupListClass m_ActiveHappyGroupListClass = new HappyGroupListClass();

        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要回報的幸福小組名單

        String[] KeyDiscipleLessonList = new string[]{ "E1", "E2", "E3", "成長班","門徒班", "領袖班", "福上", "福中","福下" };

        #endregion
        #region 主程式區
        public EquipmentRootClass GetEquipmentList(String Account, String Password, ref EquipmentRootClass aEquipmenRoot)
        {
            // 先把 aEquipmenRoot 給儲存起來
            m_LocalEquipmenRoot = aEquipmenRoot;

            #region 找登入使用者及其ID
            FindLoginUser(Account, Password);
            if (m_ContactId == Guid.Empty) //是否有找到登入使用者及其ID
            {
                // 沒找到就回傳 null
                return null;
            }
            else
            {
                m_ActiveHappyGroupListClass.LoginUserId = m_ContactId.ToString();
            }
            #endregion

            #region 先尋找帶領族系名單，若找到表示就是區長，若沒有則在繼續尋找帶領小組名單

            // 取得並過濾需要回報的幸福小組名單
            FindListCollection();

            if (m_Lists.Entities.Count != 0)
            {
                // 有找到要點名的名單，所以是小組長以上回報
                #region 處理每個要點名的名單

                // 取得小組名稱、連絡人、上課紀錄單
                ProcessEquipmenSmallGroupList();
                #endregion

                return this.m_LocalEquipmenRoot;
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
        #region 小組名單
        /// <summary>
        /// 取得區長或是小長
        /// 所有的名單包括小組點名及幸福小組
        /// </summary>
        private void FindListCollection()
        {
            try
            {
                // 初始化 m_Lists
                // 小組同工 new_contact_list_vice_family_leader
                //this.m_Lists = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_list_vice_family_leader", "list");  // 小組同工
                //this.m_Lists = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 小組同工
                //MergeCollectionSmallGroupAhead(ref this.m_Lists);
                EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_list_vice_family_leader");  // 小組同工
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 小組長/小組同工 new_contact_family_leader_list
                //EntityCollection aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_family_leader_list", "list");  // 小組長/小組同工
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/小組同工
                //aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_family_leader_list");  // 小組長/小組同工
                MergeCollectionSmallGroupAhead(ref aListEntityCollection);

                // 共同區長 new_contact_co_race_leager_list
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_co_race_leager_list", "list");  // 共同區長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_co_race_leager_list");  // 共同區長
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
                // 區長或是區長的名單若是與小組長名單重疊，則要過濾出僅有族長/區長的名單
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

                            if (aHappyStartDate.Year != 1)
                            {
                                // 小組開始日期有填
                                if (aHappyEndDate.Year != 1)
                                {
                                    // 小組開始日期有填，小組結束日期有填
                                    if (DateTime.Now >= aHappyStartDate && DateTime.Now <= aHappyEndDate)
                                    {
                                        // 現在比小組開始日期還晚 ，比小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                                else
                                {
                                    // 小組開始日期有填，小組結束日期沒填
                                    if (DateTime.Now >= aHappyStartDate)
                                    {
                                        // 現在比小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                            }
                            else
                            {
                                // 小組開始日期沒填
                                if (aHappyEndDate.Year != 1)
                                {
                                    // 小組開始日期沒填，小組結束日期有填
                                    if (DateTime.Now <= aHappyEndDate)
                                    {
                                        // 現在比小組結束日期還早
                                        m_Lists.Entities.Add(aListEntity);
                                    }
                                }
                                else
                                {
                                    // 小組開始日期沒填，小組結束日期沒填
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
        #region 新增修改下載幸福小組週報及BEST
        #region 下載幸福小組週報
        private void ProcessEquipmenSmallGroupList()
        {
            try
            {
                // 處理每個點名名單
                int Counter = 0;
                this.m_LocalEquipmenRoot.EquipmenSmallGroupList = new List<EquipmenSmallGroup>();

                foreach (Entity ListEntity in this.m_Lists.Entities)
                {
                    // 取得小組名稱
                    EquipmenSmallGroup aSmallGroupName = new EquipmenSmallGroup
                    {
                        SmallGroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname")
                    };

                    this.m_LocalEquipmenRoot.EquipmenSmallGroupList.Add(aSmallGroupName);

                    // 設定 ID
                    this.m_LocalEquipmenRoot.EquipmenSmallGroupList[Counter].LoginUserId = this.m_ContactId.ToString();
                    this.m_LocalEquipmenRoot.EquipmenSmallGroupList[Counter].SmallGroupListEntityId = ListEntity.Id.ToString();

                    GetEachContact(this.m_LocalEquipmenRoot.EquipmenSmallGroupList[Counter], ListEntity);

                    Counter++;
                }
                return;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void GetEachContact(EquipmenSmallGroup aEquipmenSmallGroup, Entity aListEntity)
        {
            try
            {
                aEquipmenSmallGroup.EquipmentContactList = new List<EquipmentContact>();

                EntityCollection MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(aListEntity.Id);

                int Counter = 0;
                foreach (Entity aMemberEntity in MemberCollection.Entities)
                {
                    Entity ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)aMemberEntity.Attributes["entityid"]).Id);

                    if ( DoesCaontactQualufied(ContactEntity) == true)
                    {
                        // 連絡人委身類型符合需要顯示裝備狀態
                        EquipmentContact aEquipmentContact = new EquipmentContact
                        {
                            SmallGroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(aListEntity, "listname"),
                            ContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "fullname"),
                            EquipmentStatus = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "new_equipment_status"),
                            EquipmentContactId = ContactEntity.Id.ToString(),
                        };

                        aEquipmenSmallGroup.EquipmentContactList.Add(aEquipmentContact);

                        GetEachStorLesson(aEquipmenSmallGroup.EquipmentContactList[Counter], ContactEntity);

                        Counter++;
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
        private void GetEachStorLesson( EquipmentContact aEquipmentContact, Entity aContactEntity)
        {
            try
            {
                aEquipmentContact.StorLessonsList = new List<EquipmentStorLessons>();

                //EntityCollection MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(aListEntity.Id);

                // 取得幸福小組出席紀錄單
                EntityCollection StorLessonsCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship("contact", "contactid", aContactEntity.Id.ToString(), "new_contact_new_stor_lessons", "new_stor_lessons");

                foreach (Entity aStorLessons in StorLessonsCollection.Entities)
                {
                    Entity aStorLessonsEntity = m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aStorLessons.Id);

                    Guid aDicsipleLessonId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aStorLessonsEntity, "new_new_disciple_lessons_new_stor_les");

                    DateTime aDiscipleLessonsDateTime = new DateTime();
                    String aStageName = "";
                    if (aDicsipleLessonId != null && aDicsipleLessonId != Guid.Empty)
                    {
                        Entity aDiscipleLessonEntity = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aDicsipleLessonId);

                        if (aDiscipleLessonEntity != null )
                        {
                            aDiscipleLessonsDateTime = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessonEntity, "new_class_start_date");
                            aStageName = this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessonEntity, "new_now_stage_name");
                        }
                    }

                    if ( KeyDiscipleLessonList.Any(aStageName.Contains) )
                    {
                        EquipmentStorLessons aEquipmentStorLessons = new EquipmentStorLessons
                        {
                            // new_new_disciple_lessons_new_stor_les
                            //DiscipleLessonsName = this.m_ToolUtilityClass.GetEntityStringAttribute(aStorLessonsEntity, ""),
                            StorLessonsEntityId = aStorLessonsEntity.Id.ToString(),
                            EquipmentContactId = aContactEntity.Id.ToString(),
                            DiscipleLessonsName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aStorLessonsEntity, "new_new_disciple_lessons_new_stor_les"),
                            CurrentComplete = this.m_ToolUtilityClass.GetEntityBoolAttribute(aStorLessonsEntity, "new_current_complete"),
                            StageName = aStageName,
                            DiscipleLessonsDateTime = aDiscipleLessonsDateTime
                        };

                        aEquipmentContact.StorLessonsList.Add(aEquipmentStorLessons);
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
        #region 工具區
        private bool DoesCaontactQualufied( Entity aContact )
        {
            #region // 連絡人委身類型是否需要顯示裝備狀態

            int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

            if (aIdentityNumber == 100000000 || aIdentityNumber == 100000004 || aIdentityNumber == 100000005 || aIdentityNumber == 100000007 || aIdentityNumber == 100000001 )
            {
                //    case 100000000:
                //        return "8. 新朋友";
                //    case 100000004:
                //        return "7. 未入組";
                //    case 100000005:
                //        return "06. 幸福BEST";
                //    case 100000007:
                //        return "09. 外教會.訪客";
                //    case 100000001:
                //        return "10. 結案";

                // 以上都不需要顯示
                return false;
            }
            else
            {
                return true;
            }

            #endregion
        }
        #endregion
        #endregion
    }
}
