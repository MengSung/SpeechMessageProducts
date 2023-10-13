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
    public class ChurchListDataProcessor
    {
        #region 資料區
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        Random m_Random = new Random();//亂數種子
        #endregion
        #region 下載資料時所需要的參數
        private ChurchRoot m_LocalChurchRoot;

        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要回報的幸福小組名單

        #endregion
        #region 主程式區
        public ChurchRoot GetChurchListData(String Account, String Password, ref ChurchRoot aChurchRoot, ref List<String> aRaceLeaderArray, ref List<String> aAreaLeaderArray, ref List<String> aRaceLeaderSmallGroupArray, ref List<String> aChurchSmallGroupArray)
        {
            #region// 先把資料給儲存起來
            m_LocalChurchRoot           = aChurchRoot;

            //m_RaceLeaderArray           = aRaceLeaderArray;             //換族系要用到的族系族長清單
            //m_AreaLeaderArray           = aAreaLeaderArray;             //換上代族系要用到的上代族系族長清單
            //m_RaceLeaderSmallGroupArray = aRaceLeaderSmallGroupArray;   //換本區小組要用到的本區小組清單
            //m_ChurchSmallGroupArray     = aChurchSmallGroupArray;       //換全教會小組要用到的全教會小組清單
            #endregion

            #region 找登入使用者及其ID
            FindLoginUser(Account, Password);
            if (m_ContactId == Guid.Empty) //是否有找到登入使用者及其ID
            {
                // 沒找到就回傳 null
                return null;
            }
            else
            {
            }
            #endregion

            #region 先尋找帶領族系名單，若找到表示就是族系族長，若沒有則在繼續尋找帶領小組名單

            // 取得並過濾需要回報的小組名單
            FindListCollection();

            if (m_Lists.Entities.Count != 0)
            {
                // 有找到要點名的名單，所以是小組長以上回報
                #region 處理每個要點名的名單

                // 取得小組名稱、連絡人、上課紀錄單
                ProcessChurchRoot();
                //InitialSamplData();

                #endregion

                // 設定本區要換小組名單及族系族長
                SetChangeSmalllGroupAndRaceList(ref aRaceLeaderArray, ref aRaceLeaderSmallGroupArray);

                SetChangeSmalllGroupAndAreaList(ref aRaceLeaderArray, ref aAreaLeaderArray, ref aRaceLeaderSmallGroupArray, ref aChurchSmallGroupArray);

                return this.m_LocalChurchRoot;
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
        /// 取得族系族長或是小長
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

                // 共同族系族長 new_contact_co_race_leager_list
                //aListEntityCollection = m_ToolUtilityClass.QueryListsAndOrderedByListName("contact", "contactid", m_ContactId.ToString(), "new_contact_co_race_leager_list", "list");  // 共同族系族長
                aListEntityCollection = m_ToolUtilityClass.QueryListByContactId(m_ContactId, "new_contact_co_race_leager_list");  // 共同族系族長
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
                // 族系族長或是族系族長的名單若是與小組長名單重疊，則要過濾出僅有族長/族系族長的名單
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
        #region 設定教會組織架構
        private void ProcessChurchRoot()
        {
            try
            {
                // 處理每個點名名單
                int Counter = 0;
                this.m_LocalChurchRoot.AreaLeaderList = new List<AreaLeader>();

                foreach (Entity aListEntity in this.m_Lists.Entities)
                {
                    SetChurchRoot( aListEntity, this.m_LocalChurchRoot.AreaLeaderList );

                    // 取得小組名稱
                    //EquipmenSmallGroup aSmallGroupName = new EquipmenSmallGroup
                    //{
                    //    SmallGroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(ListEntity, "listname")
                    //};

                    //this.m_LocalChurchRoot.EquipmenSmallGroupList.Add(aSmallGroupName);

                    //// 設定 ID
                    //this.m_LocalChurchRoot.EquipmenSmallGroupList[Counter].LoginUserId = this.m_ContactId.ToString();
                    //this.m_LocalChurchRoot.EquipmenSmallGroupList[Counter].SmallGroupListEntityId = ListEntity.Id.ToString();

                    //GetEachContact(this.m_LocalChurchRoot.EquipmenSmallGroupList[Counter], ListEntity);

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
        private void SetChurchRoot(Entity aListEntity, List<AreaLeader> aAreaLeadersList)
        {
            try
            {
                Guid aArealeaderId = m_ToolUtilityClass.GetEntityLookupAttribute(aListEntity, "new_contact_list_arealeader");
                AreaLeader aAreaLeader = new AreaLeader();
                if (aArealeaderId != Guid.Empty)
                {
                    Entity aAreaLeaderContact = m_ToolUtilityClass.RetrieveEntity("contact", aArealeaderId);
                    aAreaLeader = SetAreaLeader(aAreaLeaderContact, aAreaLeadersList);
                }
                else 
                {
                    // 沒有填上代族系族長
                    aAreaLeader = SetAreaLeader( null , aAreaLeadersList);
                }

                RaceLeader aRaceLeader;
                if (aAreaLeader != null)
                {
                    if (aAreaLeader.RaceLeaderList == null)
                    {
                        aAreaLeader.RaceLeaderList = new List<RaceLeader>();
                    }

                    Entity aRaceLeaderContact = m_ToolUtilityClass.RetrieveEntity("contact", m_ToolUtilityClass.GetEntityLookupAttribute(aListEntity, "new_contact_race_leager_list"));
                    aRaceLeader = SetRaceLeader(aRaceLeaderContact, aAreaLeader.RaceLeaderList);

                    if (aRaceLeader != null)
                    {
                        if (aRaceLeader.SmallGroupList == null)
                        {
                            aRaceLeader.SmallGroupList = new List<SmallGroup>();
                        }

                        SmallGroup aSmallGroup = SetSmallGroupLeader(aListEntity, aRaceLeader.SmallGroupList);

                        if (aSmallGroup != null)
                        {
                            if (aSmallGroup.ContactMemberList == null)
                            {
                                aSmallGroup.ContactMemberList = new List<ContactMember>();
                            }

                            SetEachContact(aListEntity, aSmallGroup.ContactMemberList);
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
        private AreaLeader SetAreaLeader(Entity aAreaContactEntity, List<AreaLeader> aAreaLeadersList)
        {
            try
            {
                String FullName = "";
                String AreaLeaderId = "";
                if (aAreaContactEntity != null)
                {
                    FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(aAreaContactEntity, "fullname");
                    AreaLeaderId = aAreaContactEntity.Id.ToString() + "-" + m_Random.Next();
                }
                else
                {
                    FullName = "上代族系族長未填";
                    AreaLeaderId = "Unknown";
                }


                foreach (AreaLeader aAreaLeader in aAreaLeadersList)
                {
                    if (aAreaLeader.AreaLeaderName == FullName)
                    {
                        return aAreaLeader;
                    }
                }

                AreaLeader aNewAreaLeader = new AreaLeader
                {
                    AreaLeaderName = FullName,
                    AreaLeaderEntityId = AreaLeaderId,
                };

                aAreaLeadersList.Add(aNewAreaLeader);

                return aNewAreaLeader;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private RaceLeader SetRaceLeader(Entity aRaceContactEntity, List<RaceLeader> aRaceLeadersList)
        {
            try
            {
                String FullName = "";
                String RaceLeaderId = "";
                if (aRaceContactEntity != null)
                {
                    FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(aRaceContactEntity, "fullname");
                    RaceLeaderId = aRaceContactEntity.Id.ToString() + "-" + m_Random.Next();
                }
                else
                {
                    FullName = "族系族長未填";
                    RaceLeaderId = "Unknown";
                }


                foreach (RaceLeader aRaceLeader in aRaceLeadersList)
                {
                    if (aRaceLeader.RaceLeaderName == FullName)
                    {
                        return aRaceLeader;
                    }
                }

                RaceLeader aNewRaceLeader = new RaceLeader
                {
                    RaceLeaderName = FullName,
                    RaceLeaderEntityId = RaceLeaderId,
                };

                aRaceLeadersList.Add(aNewRaceLeader);

                return aNewRaceLeader;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private SmallGroup SetSmallGroupLeader(Entity aListEntity, List<SmallGroup> aSmallGroupList)
        {
            try
            {
                String aSmallGroupName = "";
                String aSmallGroupId = "";
                String aSmallGroupLeaderName = "";
                String aSmallGroupLeaderId = "";
                if ( aListEntity != null )
                {
                    aSmallGroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(aListEntity, "listname");
                    aSmallGroupId = aListEntity.Id.ToString() + "-" + m_Random.Next();
                    aSmallGroupLeaderName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aListEntity, "new_contact_family_leader_list");
                    aSmallGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(aListEntity, "new_contact_family_leader_list").ToString() + m_Random.Next();
                }
                else
                {
                    aSmallGroupName = "小組未填";
                    aSmallGroupId = "Unknown";
                    aSmallGroupLeaderName = "小組長未填";
                    aSmallGroupLeaderId = "Unknown";
                }


                foreach ( SmallGroup aSmallGroup in aSmallGroupList)
                {
                    if (aSmallGroup.SmallGroupName == aSmallGroupName)
                    {
                        return aSmallGroup;
                    }
                }

                SmallGroup aNewSmallGroup = new SmallGroup
                {
                    SmallGroupName = aSmallGroupName,
                    SmallGroupId = aSmallGroupId,
                    SmallGroupLeaderName = aSmallGroupLeaderName,
                    SmallGroupLeaderEntityId = aSmallGroupLeaderId,
                };

                aSmallGroupList.Add(aNewSmallGroup);

                return aNewSmallGroup;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }
        private void SetEachContact( Entity aListEntity, List<ContactMember> aContactMemberList)
        {
            try
            {
                EntityCollection MemberCollection = this.m_ToolUtilityClass.RetrieveMemberListCollectionByListIdDynamics365(aListEntity.Id);

                foreach (Entity aMemberEntity in MemberCollection.Entities)
                {
                    Entity ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)aMemberEntity.Attributes["entityid"]).Id);

                    if (DoesCaontactQualufied(ContactEntity) == true)
                    {
                        // 先找到系統的委身類型指標
                        int aIdentity = this.m_ToolUtilityClass.GetOptionSetAttribute(ref ContactEntity, "customertypecode");


                        // 連絡人委身類型符合需要顯示裝備狀態
                        ContactMember aContactMember = new ContactMember
                        {
                            SmallGroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(aListEntity, "listname"),
                            FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "fullname"),
                            ContactId = ContactEntity.Id.ToString() + "-" + m_Random.Next(),
                            Status = ConvertIndexToIdentity(aIdentity),
                            MobilePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(ContactEntity, "mobilephone"),
                        };

                        aContactMemberList.Add(aContactMember);
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
        private void SetChangeSmalllGroupAndRaceList(ref List<String> aRaceLeaderArray, ref List<String> aRaceLeaderSmallGroupArray)
        {
            try
            {
                foreach (Entity aListEntity in m_Lists.Entities)
                {
                    // 將本牧區的小組名稱放在清單中
                    String SmallGroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(aListEntity, "listname");

                    if (aRaceLeaderSmallGroupArray != null)
                    {
                        if (aRaceLeaderSmallGroupArray.Contains(SmallGroupName) != true)
                        {
                            aRaceLeaderSmallGroupArray.Add(SmallGroupName);
                        }
                    }
                    else
                    {
                        aRaceLeaderSmallGroupArray = new List<string>();

                        aRaceLeaderSmallGroupArray.Add(SmallGroupName);
                    }

                    // 將本牧區的族系族長名稱放在清單中
                    String RaceLeaderName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aListEntity, "new_contact_race_leager_list");
                    if (aRaceLeaderArray != null)
                    {
                        if (aRaceLeaderArray.Contains(RaceLeaderName) != true)
                        {
                            aRaceLeaderArray.Add(RaceLeaderName);
                        }
                    }
                    else
                    {
                        aRaceLeaderArray = new List<string>();

                        aRaceLeaderArray.Add(RaceLeaderName);
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
        private void SetChangeSmalllGroupAndAreaList(ref List<String> aRaceLeaderArray, ref List<String> aAreaLeaderArray, ref List<String> aRaceLeaderSmallGroupArray, ref List<String> aChurchSmallGroupArray)
        {
            try
            {
                EntityCollection aSmallGroupEntityCollection = this.m_ToolUtilityClass.RetrieveSmallGroupListCollectionByFetchXml();

                foreach (Entity aSmallGroupEntity in aSmallGroupEntityCollection.Entities)
                {
                    // 將本牧區的小組名稱放在清單中
                    String SmallGroupName = this.m_ToolUtilityClass.GetEntityStringAttribute(aSmallGroupEntity, "listname");

                    if (aChurchSmallGroupArray != null)
                    {
                        if ( aRaceLeaderSmallGroupArray.Contains(SmallGroupName) != true && aChurchSmallGroupArray.Contains(SmallGroupName) != true)
                        {
                            aChurchSmallGroupArray.Add(SmallGroupName);
                        }
                    }
                    else
                    {
                        aChurchSmallGroupArray = new List<string>();

                        aChurchSmallGroupArray.Add(SmallGroupName);
                    }

                    // 將本牧區的族系族長名稱放在清單中
                    String RaceLeaderName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(aSmallGroupEntity, "new_contact_race_leager_list");
                    if (aAreaLeaderArray != null)
                    {
                        if ( aRaceLeaderArray.Contains(RaceLeaderName) != true && aAreaLeaderArray.Contains(RaceLeaderName) != true )
                        {
                            aAreaLeaderArray.Add(RaceLeaderName);
                        }
                    }
                    else
                    {
                        aAreaLeaderArray = new List<string>();

                        aAreaLeaderArray.Add(RaceLeaderName);
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
        #region 新增小組
        public Entity CreateSmallGroup( String SmallGroupName, String AreLeaderId, String RaceLeaderId, Guid SmallGroupLeaderId, ref AddController aAddController, Guid aOwnerId )
        {
            try
            {
                // 處理每個點名名單
                Entity aNewListEntity = new Entity("list");
                SetupNewListParameter(ref aNewListEntity, SmallGroupName, AreLeaderId, RaceLeaderId, SmallGroupLeaderId, ref aAddController, aOwnerId);

                Entity aCreatedListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", this.m_ToolUtilityClass.CreateEntity(aNewListEntity));

                // 指派新增名單的負責人
                this.m_ToolUtilityClass.AssignOwner("list", aCreatedListEntity, aOwnerId);

                //m_ToolUtilityClass.UpdateEntity(aCreatedListEntity);

                return aCreatedListEntity;
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        private void SetupNewListParameter(ref Entity aNewListEntity, String SmallGroupName, String AreLeaderId, String RaceLeaderId, Guid SmallGroupLeaderId, ref AddController aAddController, Guid aOwnerId)
        {

            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewListEntity, "listname", SmallGroupName);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewListEntity, "purpose", "小組名單");

            // 設定牧區名稱
            Entity aAreaContact = this.m_ToolUtilityClass.RetrieveEntity("contact", new Guid(AreLeaderId));
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewListEntity, "new_area_name", m_ToolUtilityClass.GetEntityStringAttribute( ref aAreaContact, "fullname" ) + "牧區");

            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewListEntity, "new_contact_list_arealeader", "list", new Guid(AreLeaderId));
            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewListEntity, "new_contact_race_leager_list", "list", new Guid(RaceLeaderId));
            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewListEntity, "new_contact_family_leader_list", "list", SmallGroupLeaderId);
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewListEntity, "createdfromcode", 2);

            this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewListEntity, "type", false);
            this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewListEntity, "new_app_named", true);

        }

        #endregion
        #region 工具區
        private bool DoesCaontactQualufied(Entity aContact)
        {
            #region // 連絡人委身類型是否需要顯示裝備狀態

            int aIdentityNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(aContact, "customertypecode");

            //if (aIdentityNumber == 100000000 || aIdentityNumber == 100000004 || aIdentityNumber == 100000005 || aIdentityNumber == 100000007 || aIdentityNumber == 100000001)
            //{
            //    //    case 100000000:
            //    //        return "8. 新朋友";
            //    //    case 100000004:
            //    //        return "7. 未入組";
            //    //    case 100000005:
            //    //        return "06. 幸福BEST";
            //    //    case 100000007:
            //    //        return "09. 外教會.訪客";
            //    //    case 100000001:
            //    //        return "10. 結案";

            //    // 以上都不需要顯示
            //    return false;
            //}
            if ( aIdentityNumber == 100000005 || aIdentityNumber == 100000007 || aIdentityNumber == 100000001 )
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
        private void InitialSamplData()
        {
            this.m_LocalChurchRoot.AreaLeaderList = new List<AreaLeader>
            {
                new AreaLeader
                {
                     AreaLeaderName = "上代族系族長_001",
                     AreaLeaderEntityId = "001",
                     RaceLeaderList= new List<RaceLeader>
                     {
                        new RaceLeader
                        {
                            RaceLeaderName = "忠勤,族系族長 001-001",
                            RaceLeaderEntityId ="001-001",
                            SmallGroupList = new List<SmallGroup>
                            {
                                new SmallGroup
                                {
                                    SmallGroupName = "火熱小組-001-001-001",
                                    SmallGroupId = "001-001-001",
                                    SmallGroupLeaderName = "熱愛主-001",
                                    SmallGroupLeaderEntityId = "001-002-001",
                                    ContactMemberList = new  List<ContactMember>
                                    {
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-001-001-001",
                                            ContactId ="001-001-001-001",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                       },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-001-001-002",
                                            ContactId ="001-001-001-002",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                      },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-001-001-003",
                                            ContactId ="001-001-001-003",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                    }
                                },
                                new SmallGroup
                                {
                                    SmallGroupName = "火熱小組-001-001-002",
                                    SmallGroupId = "001-001-002",
                                    SmallGroupLeaderName = "熱愛主-002",
                                    SmallGroupLeaderEntityId = "001-002-001",
                                    ContactMemberList = new  List<ContactMember>
                                    {
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-001-002-001",
                                            ContactId ="001-001-002-001",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-001-002-002",
                                            ContactId ="001-001-002-002",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-001-002-003",
                                            ContactId ="001-001-002-003",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                    }
                                },
                                new SmallGroup
                                {
                                    SmallGroupName = "火熱小組-001-001-003",
                                    SmallGroupId = "001-001-003",
                                    SmallGroupLeaderName = "熱愛主-003",
                                    SmallGroupLeaderEntityId = "001-002-001",
                                    ContactMemberList = new  List<ContactMember>
                                    {
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-001-003-001",
                                            ContactId ="001-001-003-001",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-001-003-002",
                                            ContactId ="001-001-003-002",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-001-003-003",
                                            ContactId ="001-001-003-003",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                    }
                                }
                            }
                        },
                        new RaceLeader
                        {
                            RaceLeaderName = "忠勤,族系族長 001-002",
                            RaceLeaderEntityId ="001-002",
                            SmallGroupList = new List<SmallGroup>
                            {
                                new SmallGroup
                                {
                                    SmallGroupName = "火熱小組-001-002-001",
                                    SmallGroupId = "001-002-001",
                                    SmallGroupLeaderName = "熱愛主-004",
                                    SmallGroupLeaderEntityId = "001-002-001",
                                    ContactMemberList = new  List<ContactMember>
                                    {
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-002-001-001",
                                            ContactId ="001-002-001-001",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-002-001-002",
                                            ContactId ="001-002-001-002",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-002-001-003",
                                            ContactId ="001-002-001-003",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                    }
                                },
                                new SmallGroup
                                {
                                    SmallGroupName = "火熱小組-001-002-002",
                                    SmallGroupId = "001-002-002",
                                    SmallGroupLeaderName = "熱愛主-005",
                                    SmallGroupLeaderEntityId = "001-002-001",
                                    ContactMemberList = new  List<ContactMember>
                                    {
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-002-002-001",
                                            ContactId ="002-002-001",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-002-002-002",
                                            ContactId ="001-002-002-002",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-002-002-003",
                                            ContactId ="001-002-002-002",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                    }
                                },
                                new SmallGroup
                                {
                                    SmallGroupName = "火熱小組-001-002-003",
                                    SmallGroupId = "001-002-003",
                                    SmallGroupLeaderName = "熱愛主-006",
                                    SmallGroupLeaderEntityId = "001-002-001",
                                    ContactMemberList = new  List<ContactMember>
                                    {
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-002-003-001",
                                            ContactId ="001-002-003-001",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-002-003-002",
                                            ContactId ="001-002-003-002",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                        new ContactMember
                                        {
                                            FullName = "好喜樂-001-002-003-003",
                                            ContactId ="001-002-003-003",
                                            Status="小組組員",
                                            MobilePhone = "0952-961-652",
                                        },
                                    }
                                }
                            }
                        },
                     }
                },
                //new AreaLeader
                //{
                //     AreaLeaderName = "上代族系族長_002",
                //     AreaLeaderEntityId = "0002",
                //     RaceLeaderList= new List<RaceLeader>
                //     {
                //        new RaceLeader
                //        {
                //            RaceLeaderName = "忠勤,族系族長 003",
                //            RaceLeaderEntityId ="0002-003",
                //            SmallGroupList = new List<SmallGroup>
                //            {
                //                new SmallGroup
                //                {
                //                    SmallGroupName = "火熱小組-001",
                //                    SmallGroupId = "001-001",
                //                    ContactMemberList = new  List<ContactMember>
                //                    {
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-001",
                //                            ContactId ="001-001-001",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-002",
                //                            ContactId ="001-001-002",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-003",
                //                            ContactId ="001-001-003",
                //                            Status="小組組員"
                //                        },
                //                    }
                //                },
                //                new SmallGroup
                //                {
                //                    SmallGroupName = "火熱小組-002",
                //                    SmallGroupId = "001-002",
                //                    ContactMemberList = new  List<ContactMember>
                //                    {
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-001",
                //                            ContactId ="001-002-001",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-002",
                //                            ContactId ="001-002-002",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-003",
                //                            ContactId ="001-002-003",
                //                            Status="小組組員"
                //                        },
                //                    }
                //                },
                //                new SmallGroup
                //                {
                //                    SmallGroupName = "火熱小組-003",
                //                    SmallGroupId = "001-003",
                //                    ContactMemberList = new  List<ContactMember>
                //                    {
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-001",
                //                            ContactId ="001-003-001",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-002",
                //                            ContactId ="001-003-002",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-003",
                //                            ContactId ="001-003-003",
                //                            Status="小組組員"
                //                        },
                //                    }
                //                }
                //            }
                //        },
                //        new RaceLeader
                //        {
                //            RaceLeaderName = "忠勤,族系族長 004",
                //            RaceLeaderEntityId ="002",
                //            SmallGroupList = new List<SmallGroup>
                //            {
                //                new SmallGroup
                //                {
                //                    SmallGroupName = "火熱小組-002-001",
                //                    SmallGroupId = "002-001",
                //                    ContactMemberList = new  List<ContactMember>
                //                    {
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-002-001-001",
                //                            ContactId ="002-001-001",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-002-001-002",
                //                            ContactId ="002-001-002",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-003",
                //                            ContactId ="002-001-003",
                //                            Status="小組組員"
                //                        },
                //                    }
                //                },
                //                new SmallGroup
                //                {
                //                    SmallGroupName = "火熱小組-002-002",
                //                    SmallGroupId = "002-002",
                //                    ContactMemberList = new  List<ContactMember>
                //                    {
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-001",
                //                            ContactId ="002-002-001",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-002",
                //                            ContactId ="002-002-002",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-003",
                //                            ContactId ="002-002-003",
                //                            Status="小組組員"
                //                        },
                //                    }
                //                },
                //                new SmallGroup
                //                {
                //                    SmallGroupName = "火熱小組-002-003",
                //                    SmallGroupId = "002-003",
                //                    ContactMemberList = new  List<ContactMember>
                //                    {
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-001",
                //                            ContactId ="002-003-001",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-002",
                //                            ContactId ="002-003-002",
                //                            Status="小組組員"
                //                        },
                //                        new ContactMember
                //                        {
                //                            FullName = "好喜樂-003",
                //                            ContactId ="002-003-003",
                //                            Status="小組組員"
                //                        },
                //                    }
                //                }
                //            }
                //        },
                //     }
                //},
            };

        }
        // 神住611靈糧堂
        // 委身類型客製化
        private String ConvertIndexToIdentity(int Identity)
        {
            switch (Identity)
            {
                case 100000006:
                    return "牧師師母";
                case 100000002:
                    return "上代族系族長";
                case 100000003:
                    return "族系族長";
                case 100000008:
                    return "小組長";
                case 100000009:
                    return "關懷小組長";
                case 100000012:
                    return "小組同工";
                case 1:
                    return "小組組員";
                case 100000005:
                    return "幸福BEST";
                case 100000004:
                    return "未入組";
                case 100000000:
                    return "新朋友";
                case 100000007:
                    return "外教會.訪客";
                case 100000001:
                    return "結案";
                default:
                    return ".";
            }
        }

        #endregion

        #endregion
    }
}
