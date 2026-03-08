using System;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using ToolUtilityNameSpace.DependencyInjection;

using Newtonsoft.Json;
using System.Collections.Generic;
using ChurchReport.WebServiceConnector;
using Newtonsoft.Json.Linq;
using Microsoft.Xrm.Sdk;
using ChurchReport.Models.CrmTransmitModule;

namespace ChurchReport.Models
{
    public class ListManagementDataManager
    {
        #region 成員資料
        public String m_FullName = "";
        public String m_Account = "";
        public String m_Password = "";

        public String ListManagementType = "沒管理名單";

        private readonly ToolUtilityClass m_ToolUtilityClass;

        ChurchListDataProcessor m_ChurchListDataProcessor = new ChurchListDataProcessor();

        // 教會根目錄 
        public ChurchRoot m_ChurchRoot = new ChurchRoot();

        public List<String> m_RaceLeaderArray;            //換區長要用到的區長清單
        public List<String> m_AreaLeaderArray;          //換區牧要用到的區牧清單

        public List<String> m_RaceLeaderSmallGroupArray; //換本區小組要用到的本區小組清單
        public List<String> m_ChurchSmallGroupArray;    //換全教會小組要用到的全教會小組清單


        public AddController m_AddController = new AddController();

        static readonly object m_ModifyFlagLocker = new object();//避免多人同時輸入"小組出席"，會產生2個週報或是改變"委身類型"、"裝備狀態"                                                                 //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        Random m_Random = new Random();//亂數種子

        #endregion
        
        #region 建構函數
        /// <summary>
        /// 預設建構函數，使用 Factory 模式獲取 ToolUtilityClass 實例
        /// </summary>
        public ListManagementDataManager()
        {
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance();
        }

        /// <summary>
        /// 建構函數，使用 Dependency Injection 模式
        /// </summary>
        /// <param name="toolUtilityProvider">ToolUtility 提供者</param>
        public ListManagementDataManager(IToolUtilityProvider toolUtilityProvider)
        {
            if (toolUtilityProvider == null)
                throw new ArgumentNullException(nameof(toolUtilityProvider));
            
            m_ToolUtilityClass = toolUtilityProvider.GetToolUtility();
        }

        /// <summary>
        /// 建構函數，使用指定的 DiscoveryServiceType
        /// </summary>
        /// <param name="discoveryServiceType">服務類型</param>
        public ListManagementDataManager(string discoveryServiceType)
        {
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance(discoveryServiceType);
        }
        #endregion
        
        #region 初始化成員資料
        public void SetupListManagementData(String Account, String Password)
        {
            if (m_ChurchRoot.AreaLeaderList == null)
            {
                //InitialSamplData();
                m_ChurchRoot = m_ChurchListDataProcessor.GetChurchListData(Account, Password, ref m_ChurchRoot, ref m_RaceLeaderArray, ref m_AreaLeaderArray, ref m_RaceLeaderSmallGroupArray, ref m_ChurchSmallGroupArray);
            }
            else
            {
                // 已經有資料就不需要再處理什麼了
            }

            if (m_ChurchRoot != null && m_ChurchRoot.AreaLeaderList != null && m_ChurchRoot.AreaLeaderList.Count > 0)
            {
                ListManagementType = "有管理名單";
            }
            else
            {
                ListManagementType = "沒管理名單";
            }
        }
        #endregion
        #region 新增
        #region 新增區長
        public void AddRacerListManagementElement(string values, Entity aLoginContact)
        {
            #region 新增區長
            lock (m_ModifyFlagLocker)
            {
                if (m_AddController.ModifyFlag == false)
                {
                    m_AddController.ModifyFlag = true;

                    // 轉換(反序列)從網頁有改變的欄位成為C# Weekly Report的結構
                    //HappyGroupWeeklyReport aToAddHappyGroupWeeklyReport = JsonConvert.DeserializeObject<HappyGroupWeeklyReport>(values);
                    RaceLeader aToAddRaceLeader = new RaceLeader();
                    JsonConvert.PopulateObject(values, aToAddRaceLeader);

                    if (aToAddRaceLeader.RaceLeaderName == null)
                    {
                        aToAddRaceLeader.RaceLeaderEntityId = m_Random.Next().ToString();
                        aToAddRaceLeader.SmallGroupList = new List<SmallGroup>();

                        m_AddController.EntityId = aToAddRaceLeader.RaceLeaderEntityId;
                        m_AddController.Name = "沒輸入區長名稱";
                        m_AddController.ParentEntityId = "";
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "沒輸入區長名稱";
                    }
                    else if (aLoginContact != null && this.m_ToolUtilityClass.GetOptionSetAttribute(ref aLoginContact, "customertypecode") != 100000002)
                    {
                        aToAddRaceLeader.RaceLeaderEntityId = m_Random.Next().ToString();
                        aToAddRaceLeader.SmallGroupList = new List<SmallGroup>();

                        m_AddController.EntityId = aToAddRaceLeader.RaceLeaderEntityId;
                        m_AddController.Name = "不是區牧";
                        m_AddController.ParentEntityId = "";
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "不是區牧，不能新增區長";
                    }
                    else
                    {
                        EntityCollection aContactEntityCollection = m_ToolUtilityClass.RetrieveContactCollectionByName(aToAddRaceLeader.RaceLeaderName);

                        if (aContactEntityCollection.Entities.Count == 1)
                        {
                            if (m_RaceLeaderArray.Contains(aToAddRaceLeader.RaceLeaderName) != true)
                            {
                                aToAddRaceLeader.RaceLeaderEntityId = aContactEntityCollection.Entities[0].Id.ToString() + "-" + m_Random.Next();
                                aToAddRaceLeader.SmallGroupList = new List<SmallGroup>();

                                m_AddController.EntityId = aToAddRaceLeader.RaceLeaderEntityId;
                                m_AddController.Name = aToAddRaceLeader.RaceLeaderName;
                                m_AddController.ParentEntityId = SearchAreaLeaderIdFromRaceLeader(aToAddRaceLeader.RaceLeaderName);
                                m_AddController.Status = 3;
                                m_AddController.ProcessFlag = true;
                                m_AddController.Result = "正確加入區長" + aToAddRaceLeader.RaceLeaderName;

                                AddRaceLeader(SearchAreaLeaderIdFromRaceLeader(aToAddRaceLeader.RaceLeaderName), aToAddRaceLeader);
                                m_RaceLeaderArray.Add(aToAddRaceLeader.RaceLeaderName);
                            }
                            else
                            {
                                aToAddRaceLeader.RaceLeaderEntityId = m_Random.Next().ToString();
                                aToAddRaceLeader.SmallGroupList = new List<SmallGroup>();

                                m_AddController.EntityId = aToAddRaceLeader.RaceLeaderEntityId;
                                m_AddController.Name = "已經有區長";
                                m_AddController.ParentEntityId = SearchAreaLeaderIdFromRaceLeader("沒輸入區長名稱");
                                m_AddController.Status = 5;
                                m_AddController.ProcessFlag = false;
                                m_AddController.Result = "已經有區長: " + aToAddRaceLeader.RaceLeaderName;
                            }
                        }
                        else if (aContactEntityCollection.Entities.Count == 0)
                        {
                            aToAddRaceLeader.RaceLeaderEntityId = m_Random.Next().ToString();
                            aToAddRaceLeader.SmallGroupList = new List<SmallGroup>();

                            m_AddController.EntityId = aToAddRaceLeader.RaceLeaderEntityId;
                            m_AddController.Name = aToAddRaceLeader.RaceLeaderName;
                            m_AddController.ParentEntityId = SearchAreaLeaderIdFromRaceLeader(aToAddRaceLeader.RaceLeaderName);
                            m_AddController.Status = 5;
                            m_AddController.ProcessFlag = false;
                            m_AddController.Result = "沒有找到" + aToAddRaceLeader.RaceLeaderName;
                        }
                        else
                        {
                            aToAddRaceLeader.RaceLeaderEntityId = m_Random.Next().ToString();
                            aToAddRaceLeader.SmallGroupList = new List<SmallGroup>();

                            m_AddController.EntityId = aToAddRaceLeader.RaceLeaderEntityId;
                            m_AddController.Name = aToAddRaceLeader.RaceLeaderName;
                            m_AddController.ParentEntityId = SearchAreaLeaderIdFromRaceLeader(aToAddRaceLeader.RaceLeaderName);
                            m_AddController.Status = 5;
                            m_AddController.ProcessFlag = false;
                            m_AddController.Result = aToAddRaceLeader.RaceLeaderName + "有同名同姓";
                        }

                    }
                }
                else
                {
                    m_AddController.ModifyFlag = false;
                }
            }
            #endregion
        }
        public void AddRacerOnRowInserting(String EntityId, String aRaceLeaderName)
        {
            #region 新增區長
            lock (m_ModifyFlagLocker)
            {
                if (m_AddController.ModifyFlag == false)
                {
                    m_AddController.ModifyFlag = true;

                    if (aRaceLeaderName == "")
                    {
                        m_AddController.EntityId = EntityId;
                        m_AddController.Name = aRaceLeaderName;
                        m_AddController.ParentEntityId = SearchAreaLeaderIdFromRaceLeader(aRaceLeaderName);
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "沒輸入區長名稱";
                    }
                    else
                    {
                        EntityCollection aContactEntityCollection = m_ToolUtilityClass.RetrieveContactCollectionByName(aRaceLeaderName);

                        if (aContactEntityCollection.Entities.Count == 1)
                        {
                            if (m_RaceLeaderArray.Contains(aRaceLeaderName) != true)
                            {
                                RaceLeader aToAddRaceLeader = new RaceLeader();

                                aToAddRaceLeader.RaceLeaderEntityId = aContactEntityCollection.Entities[0].Id.ToString() + "-" + m_Random.Next();

                                aToAddRaceLeader.RaceLeaderName = aRaceLeaderName;
                                aToAddRaceLeader.SmallGroupList = new List<SmallGroup>();

                                AddRaceLeader(SearchAreaLeaderIdFromRaceLeader(aRaceLeaderName), aToAddRaceLeader);

                                m_AddController.EntityId = EntityId;
                                m_AddController.Name = aRaceLeaderName;
                                m_AddController.ParentEntityId = SearchAreaLeaderIdFromRaceLeader(aRaceLeaderName);
                                m_AddController.Status = 3;
                                m_AddController.ProcessFlag = true;
                                m_AddController.Result = "正確加入區長" + aRaceLeaderName;

                                m_RaceLeaderArray.Add(aToAddRaceLeader.RaceLeaderName);
                            }
                            else
                            {
                                m_AddController.EntityId = EntityId;
                                m_AddController.Name = aRaceLeaderName;
                                m_AddController.ParentEntityId = SearchAreaLeaderIdFromRaceLeader(aRaceLeaderName);
                                m_AddController.Status = 4;
                                m_AddController.ProcessFlag = false;
                                m_AddController.Result = "沒有找到" + aRaceLeaderName;
                            }
                        }
                        else if (aContactEntityCollection.Entities.Count == 0)
                        {
                            m_AddController.EntityId = EntityId;
                            m_AddController.Name = aRaceLeaderName;
                            m_AddController.ParentEntityId = SearchAreaLeaderIdFromRaceLeader(aRaceLeaderName);
                            m_AddController.Status = 4;
                            m_AddController.ProcessFlag = false;
                            m_AddController.Result = "已經有區長: " + aRaceLeaderName;
                        }
                        else
                        {
                            m_AddController.EntityId = EntityId;
                            m_AddController.Name = aRaceLeaderName;
                            m_AddController.ParentEntityId = SearchAreaLeaderIdFromRaceLeader(aRaceLeaderName);
                            m_AddController.Status = 4;
                            m_AddController.ProcessFlag = false;
                            m_AddController.Result = aRaceLeaderName + "有同名同姓";
                        }
                    }

                }
                else
                {
                    m_AddController.ModifyFlag = false;

                    m_AddController.ParentEntityId = SearchAreaLeaderIdFromRaceLeader(aRaceLeaderName);
                }
            }
            #endregion
        }
        public void AddRaceLeader(String ParentListId, RaceLeader aToAddRaceLeader)
        {
            //HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass = GetHappyGroupWeeklyReportListClass(ParentListId);

            // 新增幸福小組週報，同時以名單成員作為初始成員
            //m_DownloadHappyGroup.AddHappyGroupWeeklyReport(ref aHappyGroupWeeklyReportListClass, ref aToAddHappyGroupWeeklyReport);

            #region 前台網頁要呈現的週報資料
            // 前台網頁要呈現的週報資料，因為已經到後台把幸福小組周報相關資料(聚會時間、地點、組員名單等等)抓回來了，
            this.m_ChurchRoot.AreaLeaderList[0].RaceLeaderList.Add(aToAddRaceLeader);
            #endregion

        }
        #endregion
        #region 新增小組
        public void AddSmallGroupManagementElement(string values)
        {
            #region 新增小組
            lock (m_ModifyFlagLocker)
            {
                if (m_AddController.ModifyFlag == false)
                {
                    m_AddController.ModifyFlag = true;

                    // 轉換(反序列)從網頁有改變的欄位成為C# Weekly Report的結構
                    SmallGroup aToAddSmallGroup = new SmallGroup();
                    JsonConvert.PopulateObject(values, aToAddSmallGroup);

                    String MasterParentID = (String)JObject.Parse(values).GetValue("MasterParentID");

                    if (aToAddSmallGroup.SmallGroupName == null)
                    {
                        aToAddSmallGroup.SmallGroupId = m_Random.Next().ToString();
                        aToAddSmallGroup.ContactMemberList = new List<ContactMember>();
                        aToAddSmallGroup.SmallGroupName = "沒輸入小組名稱";

                        m_AddController.EntityId = aToAddSmallGroup.SmallGroupId;
                        m_AddController.Name = "沒輸入小組名稱";
                        m_AddController.ParentEntityId = MasterParentID;
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "沒輸入小組名稱";

                    }
                    else if (aToAddSmallGroup.SmallGroupLeaderName == null)
                    {
                        aToAddSmallGroup.SmallGroupId = m_Random.Next().ToString();
                        aToAddSmallGroup.ContactMemberList = new List<ContactMember>();

                        m_AddController.EntityId = aToAddSmallGroup.SmallGroupId;
                        m_AddController.Name = aToAddSmallGroup.SmallGroupLeaderName;
                        m_AddController.ParentEntityId = MasterParentID;
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "沒輸入小組長";

                    }
                    else
                    {
                        Entity aSmallGroupEntity = m_ToolUtilityClass.RetrieveListEntityByName(aToAddSmallGroup.SmallGroupName);

                        if (aSmallGroupEntity == null)
                        {
                            #region// 沒有相同的小組名稱
                            // 取得小組長
                            EntityCollection aContactEntityCollection = m_ToolUtilityClass.RetrieveContactCollectionByName(aToAddSmallGroup.SmallGroupLeaderName);

                            if (aContactEntityCollection.Entities.Count == 1)
                            {
                                // 有正確取得小組長
                                //Entity aCreatedSmallGroup = CreateSmallGroup();
                                // 小組長的負責人 Id
                                Guid aOwnerId = this.m_ToolUtilityClass.GetOwnerId(aContactEntityCollection[0]);

                                Entity aCreatedSmallGroup = m_ChurchListDataProcessor.CreateSmallGroup(aToAddSmallGroup.SmallGroupName, ParseParentId(SearchAreaLeaderIdFromRaceLeaderId(MasterParentID)), ParseParentId(MasterParentID), aContactEntityCollection[0].Id, ref m_AddController, aOwnerId);
                                aToAddSmallGroup.SmallGroupId = aCreatedSmallGroup.Id.ToString() + "-" + m_Random.Next();
                                //aToAddSmallGroup.SmallGroupId = aSmallGroupEntity.Id.ToString();
                                //aToAddSmallGroup.SmallGroupId = DateTime.Now.ToString("yyyyMMdd_HHmmss"); // 先暫時用於除錯、開發
                                aToAddSmallGroup.RaceLeaderEntityId = MasterParentID;
                                aToAddSmallGroup.ContactMemberList = new List<ContactMember>();

                                m_AddController.EntityId = aToAddSmallGroup.SmallGroupId;
                                m_AddController.Name = aToAddSmallGroup.SmallGroupName;
                                m_AddController.ParentEntityId = MasterParentID;
                                m_AddController.Status = 1;
                                m_AddController.ProcessFlag = true;
                                m_AddController.Result = "正確加入小組" + aToAddSmallGroup.SmallGroupName;
                                // 前台網頁可以呈現
                                AddSmallGroup(MasterParentID, aToAddSmallGroup);
                                if (m_RaceLeaderSmallGroupArray.Contains(aToAddSmallGroup.SmallGroupName) != true)
                                {
                                    m_RaceLeaderSmallGroupArray.Add(aToAddSmallGroup.SmallGroupName);
                                }
                            }
                            else if (aContactEntityCollection.Entities.Count == 0)
                            {
                                m_AddController.EntityId = aToAddSmallGroup.SmallGroupId;
                                m_AddController.Name = aToAddSmallGroup.SmallGroupName;
                                m_AddController.ParentEntityId = MasterParentID;
                                m_AddController.Status = 5;
                                m_AddController.ProcessFlag = true;
                                m_AddController.Result = "沒有找到小組長: " + aToAddSmallGroup.SmallGroupLeaderName;
                            }
                            else
                            {
                                m_AddController.EntityId = aToAddSmallGroup.SmallGroupId;
                                m_AddController.Name = aToAddSmallGroup.SmallGroupName;
                                m_AddController.ParentEntityId = MasterParentID;
                                m_AddController.Status = 5;
                                m_AddController.ProcessFlag = true;
                                m_AddController.Result = "小組長有找到同名同姓的問題: " + aToAddSmallGroup.SmallGroupLeaderName;
                            }
                            #endregion
                        }
                        else
                        {
                            aToAddSmallGroup.SmallGroupId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            aToAddSmallGroup.ContactMemberList = new List<ContactMember>();

                            m_AddController.EntityId = aToAddSmallGroup.SmallGroupId;
                            m_AddController.Name = aToAddSmallGroup.SmallGroupName;
                            m_AddController.ParentEntityId = MasterParentID;
                            m_AddController.Status = 5;
                            m_AddController.ProcessFlag = false;
                            m_AddController.Result = "已經有相同的小組名稱" + aToAddSmallGroup.SmallGroupName;
                        }
                    }

                }
                else
                {
                    m_AddController.ModifyFlag = false;
                }
            }
            #endregion
        }
        public void AddSmalllGroupOnRowInserted(string MasterParentId, string SmallGroupName, string SmallGroupLeaderName)
        {
            #region 新增小組
            lock (m_ModifyFlagLocker)
            {
                if (m_AddController.ModifyFlag == false)
                {
                    m_AddController.ModifyFlag = true;


                    if (SmallGroupName == null || SmallGroupName == "")
                    {
                        m_AddController.EntityId = m_AddController.EntityId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        m_AddController.Name = "沒輸入小組名稱";
                        m_AddController.ParentEntityId = MasterParentId;
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "沒輸入小組名稱";

                    }
                    else if (SmallGroupLeaderName == null || SmallGroupLeaderName == "")
                    {
                        m_AddController.EntityId = m_AddController.EntityId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        m_AddController.Name = SmallGroupLeaderName;
                        m_AddController.ParentEntityId = MasterParentId;
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "沒輸入小組長";

                    }
                    else
                    {
                        Entity aSmallGroupEntity = m_ToolUtilityClass.RetrieveListEntityByName(SmallGroupName);

                        if (aSmallGroupEntity == null)
                        {

                            #region// 沒有相同的小組名稱
                            EntityCollection aContactEntityCollection = m_ToolUtilityClass.RetrieveContactCollectionByName(SmallGroupLeaderName);

                            if (aContactEntityCollection.Entities.Count == 1)
                            {
                                // 改好後要用到真正資料庫建立的小組
                                //Entity aCreatedSmallGroup = CreateSmallGroup();
                                Guid aOwnerId = this.m_ToolUtilityClass.GetOwnerId(aContactEntityCollection[0]);

                                SmallGroup aToAddSmallGroup = new SmallGroup();
                                Entity aCreatedSmallGroup = m_ChurchListDataProcessor.CreateSmallGroup(aToAddSmallGroup.SmallGroupName, ParseParentId(SearchAreaLeaderIdFromRaceLeaderId(MasterParentId)), ParseParentId(MasterParentId), aContactEntityCollection[0].Id, ref m_AddController, aOwnerId);

                                aToAddSmallGroup.SmallGroupId = aCreatedSmallGroup.Id.ToString() + "-" + m_Random.Next();
                                //aToAddSmallGroup.SmallGroupId = DateTime.Now.ToString("yyyyMMdd_HHmmss"); // 先暫時用於除錯、開發
                                aToAddSmallGroup.SmallGroupName = SmallGroupName;
                                aToAddSmallGroup.SmallGroupLeaderName = SmallGroupLeaderName;
                                aToAddSmallGroup.RaceLeaderEntityId = MasterParentId;
                                aToAddSmallGroup.ContactMemberList = new List<ContactMember>();

                                m_AddController.EntityId = aSmallGroupEntity.Id.ToString() + "-" + m_Random.Next(); ;
                                m_AddController.Name = SmallGroupName;
                                m_AddController.ParentEntityId = MasterParentId;
                                m_AddController.Status = 1;
                                m_AddController.ProcessFlag = true;
                                m_AddController.Result = "正確加入小組" + SmallGroupName;

                                AddSmallGroup(MasterParentId, aToAddSmallGroup);

                                if (m_RaceLeaderSmallGroupArray.Contains(SmallGroupName) != true)
                                {
                                    m_RaceLeaderSmallGroupArray.Add(SmallGroupName);
                                }
                            }
                            else if (aContactEntityCollection.Entities.Count == 0)
                            {
                                m_AddController.EntityId = m_Random.Next().ToString(); // 先暫時用於除錯、開發
                                m_AddController.Name = SmallGroupName;
                                m_AddController.ParentEntityId = MasterParentId;
                                m_AddController.Status = 5;
                                m_AddController.ProcessFlag = true;
                                m_AddController.Result = "沒有找到小組長: " + SmallGroupLeaderName;
                            }
                            else
                            {
                                m_AddController.EntityId = m_Random.Next().ToString(); // 先暫時用於除錯、開發
                                m_AddController.Name = SmallGroupName;
                                m_AddController.ParentEntityId = MasterParentId;
                                m_AddController.Status = 5;
                                m_AddController.ProcessFlag = true;
                                m_AddController.Result = "小組長有找到同名同姓的問題: " + SmallGroupLeaderName;
                            }
                            #endregion

                            // 待完成
                            // 沒有相同的小組名稱
                            //m_AddController.EntityId = DateTime.Now.ToString("yyyyMMdd_HHmmss"); // 先暫時用於除錯、開發
                            //m_AddController.Name = SmallGroupName;
                            //m_AddController.ParentEntityId = MasterParentId;
                            //m_AddController.Status = 3;
                            //m_AddController.ProcessFlag = true;
                            //m_AddController.Result = "正確加入小組" + SmallGroupName;
                        }
                        else
                        {
                            // 已經有相同的小組名稱
                            m_AddController.EntityId = m_Random.Next().ToString(); // 先暫時用於除錯、開發
                            m_AddController.Name = SmallGroupName;
                            m_AddController.ParentEntityId = MasterParentId;
                            m_AddController.Status = 4;
                            m_AddController.ProcessFlag = false;
                            m_AddController.Result = "已經有相同的小組名稱" + SmallGroupName;
                        }
                    }
                }
                else
                {
                    m_AddController.ModifyFlag = false;

                    m_AddController.ParentEntityId = MasterParentId;
                }
            }
            #endregion
        }
        public void AddSmallGroup(String ParentListId, SmallGroup aToAddSmallGroup)
        {
            #region 加入小組
            // 前台網頁要呈現的週報資料，因為已經到後台把幸福小組周報相關資料(聚會時間、地點、組員名單等等)抓回來了，
            //GetRaceLeaderById(ParentListId).SmallGroupList.Add(aToAddSmallGroup);

            for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
            {
                for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                {
                    if (this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].RaceLeaderEntityId == ParentListId)
                    {
                        this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Add(aToAddSmallGroup);

                        return;
                    }
                }
            }

            #endregion
        }
        #endregion
        #region 新增連絡人
        public void AddContactManagementElement(string values, String Account, String Password)
        {
            #region 新增連絡人
            lock (m_ModifyFlagLocker)
            {
                if (m_AddController.ModifyFlag == false)
                {
                    m_AddController.ModifyFlag = true;

                    // 轉換(反序列)從網頁有改變的欄位成為C# Weekly Report的結構
                    ContactMember aToAddContactMember = new ContactMember();
                    JsonConvert.PopulateObject(values, aToAddContactMember);

                    String MasterParentID = (String)JObject.Parse(values).GetValue("MasterParentID");

                    if (aToAddContactMember.FullName == null)
                    {
                        aToAddContactMember.ContactId = m_Random.Next().ToString();
                        aToAddContactMember.FullName = "沒輸入姓名";

                        m_AddController.EntityId = aToAddContactMember.ContactId;
                        m_AddController.Name = "沒輸入姓名";
                        m_AddController.ParentEntityId = MasterParentID;
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "沒輸入姓名";

                    }
                    else if (aToAddContactMember.MobilePhone == null)
                    {
                        aToAddContactMember.ContactId = m_Random.Next().ToString();
                        aToAddContactMember.FullName = "沒輸手機";

                        m_AddController.EntityId = aToAddContactMember.ContactId;
                        m_AddController.Name = "沒輸入姓名";
                        m_AddController.ParentEntityId = MasterParentID;
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "沒輸手機";

                    }
                    else
                    {
                        NewContact aNewContact = new NewContact();

                        aNewContact.Name = aToAddContactMember.FullName;
                        aNewContact.MobilePhone = aToAddContactMember.MobilePhone;

                        // 委身類型
                        if (aToAddContactMember.Status != null && aToAddContactMember.Status != "")
                        {
                            aNewContact.CustomerTypeCode = aToAddContactMember.Status;
                        }
                        else
                        {
                            aToAddContactMember.Status = "新朋友";
                            aNewContact.CustomerTypeCode = "新朋友";
                        }

                        aNewContact.GroupName = GetSmallGroupById(MasterParentID).SmallGroupName;

                        NewPerson aNewPersonManager = new NewPerson();
                        AccountPasswordData aAccountPasswordData = new AccountPasswordData
                        {
                            Account = Account,
                            Password = Password
                        };

                        String CreateResult = aNewPersonManager.CreateNewContactFromView(aAccountPasswordData, ref aNewContact);

                        if (CreateResult.Contains("成功"))
                        {
                            #region// 成功加入後台資料庫
                            aToAddContactMember.ContactId = aNewContact.PresentRecordId + m_Random.Next().ToString();
                            aToAddContactMember.SmallGroupId = MasterParentID;

                            m_AddController.EntityId = aNewContact.PresentRecordId;
                            m_AddController.Name = aToAddContactMember.FullName;
                            m_AddController.ParentEntityId = MasterParentID;
                            m_AddController.Status = 1;
                            m_AddController.ProcessFlag = true;
                            m_AddController.Result = CreateResult;
                            // 前台網頁可以呈現
                            AddContactMember(MasterParentID, aToAddContactMember);
                            #endregion
                        }
                        else
                        {
                            m_AddController.EntityId = m_Random.Next().ToString();
                            m_AddController.Name = "無法加入";
                            m_AddController.ParentEntityId = MasterParentID;
                            m_AddController.Status = 5;
                            m_AddController.ProcessFlag = false;
                            m_AddController.Result = CreateResult;

                        }
                    }

                }
                else
                {
                    m_AddController.ModifyFlag = false;
                }
            }
            #endregion
        }
        public void AddContactOnRowInserted(string MasterParentID, string FullName, string Status, string MobilePhone, String Account, String Password)
        {
            #region 新增連絡人
            lock (m_ModifyFlagLocker)
            {
                if (m_AddController.ModifyFlag == false)
                {
                    m_AddController.ModifyFlag = true;

                    if (FullName == null || MobilePhone == "")
                    {
                        m_AddController.EntityId = m_AddController.EntityId = m_Random.Next().ToString();
                        m_AddController.Name = "沒輸入姓名";
                        m_AddController.ParentEntityId = MasterParentID;
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "沒輸入姓名";

                    }
                    else if (FullName == null || MobilePhone == "")
                    {
                        m_AddController.EntityId = m_AddController.EntityId = m_Random.Next().ToString();
                        m_AddController.Name = FullName;
                        m_AddController.ParentEntityId = MasterParentID;
                        m_AddController.Status = 5;
                        m_AddController.ProcessFlag = false;
                        m_AddController.Result = "沒輸入手機";

                    }
                    else
                    {

                        NewContact aNewContact = new NewContact();

                        aNewContact.Name = FullName;
                        aNewContact.MobilePhone = MobilePhone;

                        // 委身類型
                        aNewContact.CustomerTypeCode = Status;

                        aNewContact.GroupName = SearchSmallGroupNameByContactMemberFullName(FullName);

                        NewPerson aNewPersonManager = new NewPerson();
                        AccountPasswordData aAccountPasswordData = new AccountPasswordData
                        {
                            Account = Account,
                            Password = Password
                        };

                        String CreateResult = aNewPersonManager.CreateNewContactFromView(aAccountPasswordData, ref aNewContact);

                        if (CreateResult.Contains("成功"))
                        {
                            #region// 沒有相同的小組名稱
                            ContactMember aToAddContactMember = new ContactMember();
                            aToAddContactMember.ContactId = aNewContact.PresentRecordId + m_Random.Next().ToString();
                            aToAddContactMember.FullName = FullName;
                            aToAddContactMember.MobilePhone = MobilePhone;
                            aToAddContactMember.SmallGroupId = MasterParentID;

                            m_AddController.EntityId = aNewContact.PresentRecordId + m_Random.Next().ToString();
                            m_AddController.Name = FullName;
                            m_AddController.ParentEntityId = MasterParentID;
                            m_AddController.Status = 1;
                            m_AddController.ProcessFlag = true;
                            m_AddController.Result = CreateResult;

                            // 前台網頁可以呈現
                            AddContactMember(MasterParentID, aToAddContactMember);

                            #endregion
                        }
                        else
                        {
                            m_AddController.EntityId = m_Random.Next().ToString();
                            m_AddController.Name = "無法加入";
                            m_AddController.ParentEntityId = MasterParentID;
                            m_AddController.Status = 5;
                            m_AddController.ProcessFlag = false;
                            m_AddController.Result = CreateResult;
                        }
                    }
                }
                else
                {
                    m_AddController.ModifyFlag = false;

                    m_AddController.ParentEntityId = MasterParentID;
                }
            }
            #endregion
        }
        public void AddContactMember(String ParentListId, ContactMember aToAddContactMember)
        {
            //HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass = GetHappyGroupWeeklyReportListClass(ParentListId);

            // 新增幸福小組週報，同時以名單成員作為初始成員
            //m_DownloadHappyGroup.AddHappyGroupWeeklyReport(ref aHappyGroupWeeklyReportListClass, ref aToAddHappyGroupWeeklyReport);

            #region 前台網頁要呈現的週報資料
            // 前台網頁要呈現的週報資料，因為已經到後台把幸福小組周報相關資料(聚會時間、地點、組員名單等等)抓回來了，
            GetSmallGroupById(ParentListId).ContactMemberList.Add(aToAddContactMember);
            #endregion

        }
        #endregion
        #region 載入頁面時要用到
        public AreaLeader GetAreaLeaderByRaceLeaderId(String aRaceLeaderId)
        {
            // 尚未完成!
            foreach (AreaLeader aAreaLeader in this.m_ChurchRoot.AreaLeaderList)
            {
                foreach (RaceLeader aRaceLeader in aAreaLeader.RaceLeaderList)
                {
                    if (aRaceLeader.RaceLeaderEntityId == aRaceLeaderId)
                    {
                        return aAreaLeader;
                    }
                }
            }
            return null;
        }
        public RaceLeader GetRaceLeaderBySmallGroupId(String aSmallGroupId)
        {
            // 尚未完成!
            foreach (AreaLeader aAreaLeader in this.m_ChurchRoot.AreaLeaderList)
            {
                foreach (RaceLeader aRaceLeader in aAreaLeader.RaceLeaderList)
                {
                    foreach (SmallGroup aSmallGroup in aRaceLeader.SmallGroupList)
                    {
                        if (aSmallGroup.SmallGroupId == aSmallGroupId)
                        {
                            return aRaceLeader;
                        }

                    }
                }
            }
            return null;
        }
        #endregion
        #region 新增時要用到
        public RaceLeader GetRaceLeaderById(String aRaceLeaderId)
        {
            // 尚未完成!
            foreach (AreaLeader aAreaLeader in this.m_ChurchRoot.AreaLeaderList)
            {
                foreach (RaceLeader aRaceLeader in aAreaLeader.RaceLeaderList)
                {
                    if (aRaceLeader.RaceLeaderEntityId == aRaceLeaderId)
                    {
                        return aRaceLeader;
                    }
                }
            }
            return null;
        }
        public SmallGroup GetSmallGroupById(String aSmallGroupId)
        {
            foreach (AreaLeader aAreaLeader in this.m_ChurchRoot.AreaLeaderList)
            {
                foreach (RaceLeader aRaceLeader in aAreaLeader.RaceLeaderList)
                {
                    foreach (SmallGroup aSmallGroup in aRaceLeader.SmallGroupList)
                    {
                        if (aSmallGroup.SmallGroupId == aSmallGroupId)
                        {
                            return aSmallGroup;
                        }

                    }
                }
            }
            return null;
        }
        #endregion
        #endregion
        #region 修改
        public ContactMember SearchListManamementContactMember(string aContactMemberKey)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            for (int MemberCounter = 0; MemberCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList.Count; MemberCounter++)
                            {
                                if (aContactMemberKey == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList[MemberCounter].ContactId)
                                {
                                    return this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList[MemberCounter];
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void AddContactMemberToSmallGroup(ContactMember aContactMember, String aSmallGroupName)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            if (this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].SmallGroupName == aSmallGroupName)
                            {
                                this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList.Add(aContactMember);

                                return;
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void UpdateSmallGroupManagementElement(string key, string values)
        {
            try
            {
                lock (m_ModifyFlagLocker)
                {
                    if (m_AddController.ModifyFlag == false)
                    {
                        m_AddController.ModifyFlag = true;

                        // 找到該小組的紀錄
                        SmallGroup aSmallGroup = SearchSmallGroup(key);

                        // 被修改的小組
                        SmallGroup aUpdateSmallGroup = new SmallGroup();
                        JsonConvert.PopulateObject(values, aUpdateSmallGroup);


                        if (aUpdateSmallGroup.ChangeRaceLeader != null)
                        {
                            // 小組被換至本牧區的其他區長
                            Entity aRacerEntity = this.m_ToolUtilityClass.RetrieveContactEntityByName(aUpdateSmallGroup.ChangeRaceLeader);

                            Entity aUpdatedSmallGroupEntity = m_ToolUtilityClass.RetrieveEntity("list", new Guid(ParseParentId(aSmallGroup.SmallGroupId)));

                            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aUpdatedSmallGroupEntity, "new_contact_race_leager_list", "contact", aRacerEntity.Id);

                            this.m_ToolUtilityClass.UpdateEntity(aUpdatedSmallGroupEntity);

                            DeleteListManamement(key);

                            AddSmallGroupToRaceLeader(aSmallGroup, aUpdateSmallGroup.ChangeRaceLeader);

                            m_AddController.EntityId = aSmallGroup.SmallGroupId;
                            m_AddController.Name = aUpdateSmallGroup.ChangeRaceLeader;
                            m_AddController.ParentEntityId = SearchRaceLeaderByName(aUpdateSmallGroup.ChangeRaceLeader);
                            m_AddController.Status = 1;
                            m_AddController.ProcessFlag = true;
                            m_AddController.Result = "正確換到: " + aUpdateSmallGroup.ChangeRaceLeader + " 區長";

                        }
                        else if (aUpdateSmallGroup.ChangeAreaLeader != null)
                        {
                            // 小組被換至本教會其他牧區
                            // 取得區長
                            Entity aRacerEntity = this.m_ToolUtilityClass.RetrieveContactEntityByName(aUpdateSmallGroup.ChangeAreaLeader);

                            Entity aUpdatedSmallGroupEntity = m_ToolUtilityClass.RetrieveEntity("list", new Guid(ParseParentId(aSmallGroup.SmallGroupId)));

                            EntityCollection aSmallGroupCollection = m_ToolUtilityClass.RetrieveListByFetchXmlRacerLeader(aUpdateSmallGroup.ChangeAreaLeader, aRacerEntity.Id.ToString());

                            foreach (Entity SmallGroupEntity in aSmallGroupCollection.Entities)
                            {
                                // 取得區長的區牧
                                Entity aRetrievedSmallGroup = m_ToolUtilityClass.RetrieveEntity("list", SmallGroupEntity.Id);
                                Guid aAreaId = m_ToolUtilityClass.GetEntityLookupAttribute(aRetrievedSmallGroup, "new_contact_list_arealeader");
                                if (aAreaId != null && aAreaId != Guid.Empty)
                                {
                                    // 設定區牧
                                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aUpdatedSmallGroupEntity, "new_contact_list_arealeader", "contact", aAreaId);
                                    // 設定牧區名稱
                                    Entity AreaContact = m_ToolUtilityClass.RetrieveEntity("contact", aAreaId);
                                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aUpdatedSmallGroupEntity, "new_area_name", m_ToolUtilityClass.GetEntityStringAttribute(AreaContact, "fullname") + "牧區");
                                    break;
                                }
                            }
                            //設定區長
                            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aUpdatedSmallGroupEntity, "new_contact_race_leager_list", "contact", aRacerEntity.Id);
                            // 設定共同區牧、區長、小組長為空白
                            this.m_ToolUtilityClass.SetEntityLookUpToNull(ref aUpdatedSmallGroupEntity, "new_contact_list_co_arealeader");
                            this.m_ToolUtilityClass.SetEntityLookUpToNull(ref aUpdatedSmallGroupEntity, "new_contact_co_race_leager_list");
                            this.m_ToolUtilityClass.SetEntityLookUpToNull(ref aUpdatedSmallGroupEntity, "new_contact_list_vice_family_leader");

                            this.m_ToolUtilityClass.UpdateEntity(aUpdatedSmallGroupEntity);

                            DeleteListManamement(key);

                            m_AddController.EntityId = aSmallGroup.SmallGroupId;
                            m_AddController.Name = aUpdateSmallGroup.ChangeRaceLeader;
                            m_AddController.ParentEntityId = SearchRaceLeaderByName(aUpdateSmallGroup.ChangeRaceLeader);
                            m_AddController.Status = 1;
                            m_AddController.ProcessFlag = true;
                            m_AddController.Result = "正確換到: " + aUpdateSmallGroup.ChangeAreaLeader + " 區長";
                        }
                        else
                        {
                            // 不彈跳通知反應
                            m_AddController.EntityId = aSmallGroup.SmallGroupId;
                            m_AddController.Name = aUpdateSmallGroup.ChangeRaceLeader;
                            m_AddController.ParentEntityId = SearchRaceLeaderByName(aUpdateSmallGroup.ChangeRaceLeader);
                            m_AddController.Status = 6;// 不彈跳通知反應
                            m_AddController.ProcessFlag = false;
                            m_AddController.Result = "不彈跳通知反應";
                        }
                    }
                    else
                    {
                        m_AddController.ModifyFlag = true;
                    }
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void UpdateSmalllGroupOnRowUpdated(String Key, String Value)
        {
            lock (m_ModifyFlagLocker)
            {
                #region 新增小組
                lock (m_ModifyFlagLocker)
                {
                    if (m_AddController.ModifyFlag == false)
                    {
                        m_AddController.ModifyFlag = true;
                    }
                    else
                    {
                        m_AddController.ModifyFlag = false;
                    }
                }
                #endregion
            }
        }
        public void UpdateContactMemberManagementElement(string key, string values)
        {
            lock (m_ModifyFlagLocker)
            {
                try
                {
                    if (m_AddController.ModifyFlag == false)
                    {
                        m_AddController.ModifyFlag = true;

                        // 會友資料被修改
                        ContactMember aContactMember = SearchListManamementContactMember(key);

                        // 被修改的會友資料
                        ContactMember aUpdateContactMember = new ContactMember();
                        JsonConvert.PopulateObject(values, aUpdateContactMember);

                        if (aUpdateContactMember.RaceLeaderSmallGroup != null)
                        {
                            // 會友被換至本區小組
                            ProcessChangeSmallGroup(aContactMember, aUpdateContactMember.RaceLeaderSmallGroup, key);
                            AddContactMemberToSmallGroup(aContactMember, aUpdateContactMember.RaceLeaderSmallGroup);
                        }
                        else if (aUpdateContactMember.ChurchSmallGroup != null)
                        {
                            // 會友被換至其他牧區
                            ProcessChangeSmallGroup(aContactMember, aUpdateContactMember.ChurchSmallGroup, key);
                        }
                        else
                        {
                            // 不彈跳通知反應
                            m_AddController.EntityId = key;
                            m_AddController.Name = "";
                            m_AddController.ParentEntityId = "";
                            m_AddController.Status = 6;// 不彈跳通知反應
                            m_AddController.ProcessFlag = false;
                            m_AddController.Result = "不彈跳通知反應";

                        }
                    }
                    else
                    {
                        m_AddController.ModifyFlag = true;
                    }
                }
                catch (System.Exception e)
                {
                    string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                    throw e;
                }
            }
        }
        public void ProcessChangeSmallGroup(ContactMember aContactMember, String SmallGroupName, string key)
        {
            try
            {
                // 連絡人被換至本牧區的其他小組
                // 取得目前的小組
                Entity aCurrentSmallGroupEntity = this.m_ToolUtilityClass.RetrieveListEntityByName(SearchSmallGroupByContactMemberId(aContactMember.ContactId).SmallGroupName);
                // 取得要被指派的小組
                Entity aAssignedSmallGroupEntity = this.m_ToolUtilityClass.RetrieveListEntityByName(SmallGroupName);
                // 要被指派的人
                Entity aUpdatedContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", new Guid(ParseParentId(aContactMember.ContactId)));

                m_ToolUtilityClass.RemoveMembersToMarketingList(aCurrentSmallGroupEntity.Id, aUpdatedContactEntity.Id);

                List<Guid> memberGuidList = new List<Guid>();
                memberGuidList.Add(aUpdatedContactEntity.Id);
                m_ToolUtilityClass.AddMembersToMarketingList(aAssignedSmallGroupEntity.Id, memberGuidList);

                if (m_ToolUtilityClass.GetEntityLookupAttribute(aUpdatedContactEntity, "new_cell_list_contact") == Guid.Empty)
                {
                    // 連絡人的主要小組與要被移除的小組是同一個，所以要把 連絡人的主要小組設定為空白
                    m_ToolUtilityClass.SetEntityLookUpAttribute(ref aUpdatedContactEntity, "new_cell_list_contact", "list", aAssignedSmallGroupEntity.Id);

                    this.m_ToolUtilityClass.UpdateEntity(aUpdatedContactEntity);
                }
                else if (m_ToolUtilityClass.GetEntityLookupAttribute(aUpdatedContactEntity, "new_cell_list_contact") == aCurrentSmallGroupEntity.Id)
                {
                    // 連絡人的主要小組與要被移除的小組是同一個，所以要把 連絡人的主要小組設定為空白
                    m_ToolUtilityClass.SetEntityLookUpAttribute(ref aUpdatedContactEntity, "new_cell_list_contact", "list", aAssignedSmallGroupEntity.Id);

                    this.m_ToolUtilityClass.UpdateEntity(aUpdatedContactEntity);
                }
                else { }

                // 換區長，所以指派負責人
                Guid aSmallGroupLeaderId = m_ToolUtilityClass.GetEntityLookupAttribute(aAssignedSmallGroupEntity, "new_contact_family_leader_list");
                Guid aOwnerId = new Guid();
                if (aSmallGroupLeaderId != null && aSmallGroupLeaderId != Guid.Empty)
                {
                    aOwnerId = this.m_ToolUtilityClass.GetOwnerId(m_ToolUtilityClass.RetrieveEntity("contact", aSmallGroupLeaderId));

                    m_ToolUtilityClass.AssignOwner("contact", aUpdatedContactEntity, aOwnerId);
                }

                // 處理出席紀錄單
                ProcessPresentRecord(aContactMember, aCurrentSmallGroupEntity, aAssignedSmallGroupEntity, aOwnerId);

                DeleteListManamement(key);

                m_AddController.EntityId = aContactMember.ContactId;
                m_AddController.Name = SmallGroupName;
                m_AddController.ParentEntityId = SearchSmallGroupByName(SmallGroupName);
                m_AddController.Status = 1;
                m_AddController.ProcessFlag = true;
                m_AddController.Result = aContactMember.FullName + " 正確換到: " + SmallGroupName;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void ProcessPresentRecord(ContactMember aContactMember, Entity aCurrentSmallGroupEntity, Entity aAssignedSmallGroupEntity, Guid aOwnerId)
        {
            try
            {
                // 處理出席紀錄單
                #region 先根據日期尋找當週主日日期
                // 依設定檔的每週第一日規則，計算今天所屬週次的主日日期。
                DateTime aSunday = ChurchReport.Services.SundayCalculator.CalculateSunday(
                    DateTime.Now,
                    ChurchReport.Services.WeeklyScheduleProvider.FirstDayOfWeek);
                #endregion

                EntityCollection aPresentRecordEntityCollection = m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate
                (
                    aContactMember.FullName, ParseParentId(aContactMember.ContactId),
                    m_ToolUtilityClass.GetEntityStringAttribute(aCurrentSmallGroupEntity, "listname"),
                    aCurrentSmallGroupEntity.Id.ToString(),
                    aSunday
                );

                if (aPresentRecordEntityCollection.Entities.Count == 1)
                {
                    // 設定區牧
                    Guid aContactGuid = m_ToolUtilityClass.GetEntityLookupAttribute(aAssignedSmallGroupEntity, "new_contact_list_arealeader");
                    if (aContactGuid != Guid.Empty)
                    {
                        m_ToolUtilityClass.SetEntityLookUpAttribute(aPresentRecordEntityCollection.Entities[0], "new_contact_arealeader_present_record", "contact", aContactGuid);
                    }
                    // 設定區長
                    aContactGuid = m_ToolUtilityClass.GetEntityLookupAttribute(aAssignedSmallGroupEntity, "new_contact_race_leager_list");
                    if (aContactGuid != Guid.Empty)
                    {
                        m_ToolUtilityClass.SetEntityLookUpAttribute(aPresentRecordEntityCollection.Entities[0], "new_race_leader_present_record", "contact", aContactGuid);
                    }
                    //設定小組長
                    aContactGuid = m_ToolUtilityClass.GetEntityLookupAttribute(aAssignedSmallGroupEntity, "new_contact_family_leader_list");
                    if (aContactGuid != Guid.Empty)
                    {
                        m_ToolUtilityClass.SetEntityLookUpAttribute(aPresentRecordEntityCollection.Entities[0], "new_groupleader_present_record", "contact", aContactGuid);
                    }

                    // 設定小組
                    m_ToolUtilityClass.SetEntityLookUpAttribute(aPresentRecordEntityCollection.Entities[0], "new_list_new_present_record", "list", aAssignedSmallGroupEntity.Id);

                    // 尋找此小組的某一個主日的週報集合
                    EntityCollection GroupWeeklyReportEntityCollection = m_ToolUtilityClass.QueryWeeklyReportBySunday(aSunday, aAssignedSmallGroupEntity.Id);

                    // 此小組的某一個主日的週報集合，應該僅有一個，也就是第0個的週報
                    Entity GroupWeeklyReportEntity = GroupWeeklyReportEntityCollection.Entities.Count == 1 ? GroupWeeklyReportEntityCollection.Entities[0] : null;

                    if (GroupWeeklyReportEntity != null)
                    {
                        // 設定周報
                        m_ToolUtilityClass.SetEntityLookUpAttribute(aPresentRecordEntityCollection.Entities[0], "new_group_present_weekly_report_prese", "new_group_present_weekly_report", GroupWeeklyReportEntity.Id);
                    }

                    // 更新出席紀錄單
                    m_ToolUtilityClass.UpdateEntity(aPresentRecordEntityCollection.Entities[0]);

                    if (aOwnerId != null && aOwnerId != Guid.Empty)
                    {
                        m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecordEntityCollection.Entities[0], aOwnerId);
                    }
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void UpdateContactMemberOnRowUpdated(String Key, String Value)
        {
            #region 換小組
            lock (m_ModifyFlagLocker)
            {
                if (m_AddController.ModifyFlag == false)
                {
                    m_AddController.ModifyFlag = true;
                }
                else
                {
                    m_AddController.ModifyFlag = false;
                }
            }
            #endregion
        }
        public SmallGroup SearchSmallGroup(string aSmallGroupKey)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            if (aSmallGroupKey == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].SmallGroupId)
                            {
                                return this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter];
                            }
                        }
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public String SearchSmallGroupByName(string aSmallGroupName)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            if (aSmallGroupName == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].SmallGroupName)
                            {
                                return this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].SmallGroupId;
                            }
                        }
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public String SearchRaceLeaderByName(string aRaceLeaderName)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        if (aRaceLeaderName == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].RaceLeaderName)
                        {
                            return this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].RaceLeaderEntityId;
                        }
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void DeleteRaceLeaderByEntityId(string aRaceLeaderId)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        if (aRaceLeaderId == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].RaceLeaderEntityId)
                        {
                            this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.RemoveAt(RaceCounter);
                            return;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void AddSmallGroupToRaceLeader(SmallGroup aSmallGroup, String aRaceLeaderName)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        if (this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].RaceLeaderName == aRaceLeaderName)
                        {
                            this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Add(aSmallGroup);

                            return;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public SmallGroup SearchSmallGroupByContactMemberId(string aContactId)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            for (int MemberCounter = 0; MemberCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList.Count; MemberCounter++)
                            {
                                if (aContactId == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList[MemberCounter].ContactId)
                                {
                                    return this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter];
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        #endregion
        #region 刪除
        public void DeleteListManamement(string Key)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        if (Key == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].RaceLeaderEntityId)
                        {
                            this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.RemoveAt(RaceCounter);
                            return;
                        }

                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            if (Key == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].SmallGroupId)
                            {
                                this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.RemoveAt(SmalllGroupCounter);
                                return;
                            }

                            for (int MemberCounter = 0; MemberCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList.Count; MemberCounter++)
                            {
                                if (Key == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList[MemberCounter].ContactId)
                                {
                                    this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList.RemoveAt(MemberCounter);
                                    return;
                                }
                            }
                        }
                    }

                }

                return;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void DeleteSmallGroupByEntityId(string aSmallGroupId)
        {
            try
            {
                Entity aSmalllGroupEntity = this.m_ToolUtilityClass.RetrieveEntity("list", new Guid(ParseParentId(aSmallGroupId)));

                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aSmalllGroupEntity, "new_app_named", false);
                //this.m_ToolUtilityClass.SetStatusToCompleted("list", aSmalllGroupEntity.Id, 0, 0);
                this.m_ToolUtilityClass.UpdateEntity(aSmalllGroupEntity);

                RemoveSmallGroupFromList(aSmallGroupId);

                return;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void RemoveSmallGroupFromList(string aSmallGroupId)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            if (aSmallGroupId == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].SmallGroupId)
                            {
                                this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.RemoveAt(SmalllGroupCounter);
                                return;
                            }
                        }
                    }
                }

                return;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void DeleteContactByEntityId(string aContactId)
        {
            try
            {
                Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", new Guid(ParseParentId(aContactId)));

                // 將連絡人從小組名單移除
                SmallGroup aSmallGroup = GetSmalllGroupByContactId(aContactId);
                if (aSmallGroup != null && aContactEntity != null)
                {
                    // 從小組名單中移除
                    m_ToolUtilityClass.RemoveMembersToMarketingList(new Guid(ParseParentId(aSmallGroup.SmallGroupId)), aContactEntity.Id);

                    if (m_ToolUtilityClass.GetEntityLookupAttribute(aContactEntity, "new_cell_list_contact") == new Guid(ParseParentId(aSmallGroup.SmallGroupId)))
                    {
                        // 連絡人的主要小組與要被移除的小組是同一個，所以要把 連絡人的主要小組設定為空白
                        m_ToolUtilityClass.SetEntityLookUpToNull(ref aContactEntity, "new_cell_list_contact");

                        this.m_ToolUtilityClass.UpdateEntity(aContactEntity);
                    }

                    #region 刪除當周、那個小組、那個連絡人的出席紀錄單

                    #region 先根據日期尋找當週主日日期
                    // 依設定檔的每週第一日規則，計算今天所屬週次的主日日期。
                    DateTime aSunday = ChurchReport.Services.SundayCalculator.CalculateSunday(
                        DateTime.Now,
                        ChurchReport.Services.WeeklyScheduleProvider.FirstDayOfWeek);
                    #endregion

                    ContactMember aContactMember = GetContactByContactId(aContactId);

                    EntityCollection aPresentRecordEntityCollection = m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate(aContactMember.FullName, ParseParentId(aContactId), aSmallGroup.SmallGroupName, ParseParentId(aSmallGroup.SmallGroupId), aSunday);

                    if (aPresentRecordEntityCollection.Entities.Count == 1)
                    {
                        m_ToolUtilityClass.DeleteEntity("new_present_record", aPresentRecordEntityCollection.Entities[0].Id);
                    }
                    #endregion

                    // 前台網頁也要被移除
                    RemoveContactFromSmallGroup(aContactId);
                }

                return;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public void RemoveContactFromSmallGroup(String aContactId)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            for (int MemberCounter = 0; MemberCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList.Count; MemberCounter++)
                            {
                                if (aContactId == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList[MemberCounter].ContactId)
                                {
                                    this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList.RemoveAt(MemberCounter);

                                    return;
                                }
                            }
                        }
                    }
                }

                return;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public SmallGroup GetSmalllGroupByContactId(String aContactId)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            for (int MemberCounter = 0; MemberCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList.Count; MemberCounter++)
                            {
                                if (aContactId == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList[MemberCounter].ContactId)
                                {
                                    return this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter];
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public ContactMember GetContactByContactId(String aContactId)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            for (int MemberCounter = 0; MemberCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList.Count; MemberCounter++)
                            {
                                if (aContactId == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList[MemberCounter].ContactId)
                                {
                                    return this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList[MemberCounter];
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }

        #endregion
        #region 工具區
        private void GetMasterDetailIndex(ref HappyGroupListClass aActiveHappyGroupListClass, string Key, ref int ListIndex, ref int MasterIndex, ref int DetailIndex)
        {
            ListIndex = MasterIndex = DetailIndex = -1;

            for (int counter = 0; counter < aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass.Count; counter++)
            {
                for (int i = 0; i < aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[counter].HappyGroupWeeklyReportList.Count; i++)
                {
                    if (Key == aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[counter].HappyGroupWeeklyReportList[i].HappyGroupWeeklyReportId)
                    {
                        ListIndex = counter;
                        MasterIndex = i;
                        DetailIndex = -1; // 修改的是週報
                        return;
                    }
                    for (int j = 0; j < aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[counter].HappyGroupWeeklyReportList[i].BestRecordList.Count; j++)
                    {
                        if (Key == aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[counter].HappyGroupWeeklyReportList[i].BestRecordList[j].BestRecordId)
                        {
                            ListIndex = counter;
                            MasterIndex = i;
                            DetailIndex = j; // 修改的是 Best
                            return;
                        }
                    }
                }
            }

        }
        private void InitialSamplData()
        {
            this.m_ChurchRoot.AreaLeaderList = new List<AreaLeader>
            {
                new AreaLeader
                {
                     AreaLeaderName = "區牧_001",
                     AreaLeaderEntityId = "001",
                     RaceLeaderList= new List<RaceLeader>
                     {
                        new RaceLeader
                        {
                            RaceLeaderName = "忠勤,區長 001-001",
                            RaceLeaderEntityId ="001-001",
                            SmallGroupList = new List<SmallGroup>
                            {
                                new SmallGroup
                                {
                                    SmallGroupName = "火熱小組-001-001-001",
                                    SmallGroupId = "001-001-001",
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
                            RaceLeaderName = "忠勤,區長 001-002",
                            RaceLeaderEntityId ="001-002",
                            SmallGroupList = new List<SmallGroup>
                            {
                                new SmallGroup
                                {
                                    SmallGroupName = "火熱小組-001-002-001",
                                    SmallGroupId = "001-002-001",
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
                //     AreaLeaderName = "區牧_002",
                //     AreaLeaderEntityId = "0002",
                //     RaceLeaderList= new List<RaceLeader>
                //     {
                //        new RaceLeader
                //        {
                //            RaceLeaderName = "忠勤,區長 003",
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
                //            RaceLeaderName = "忠勤,區長 004",
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

            m_RaceLeaderArray = new List<string>();            //換區長要用到的區長清單
            m_AreaLeaderArray = new List<string>();          //換區牧要用到的區牧清單

            m_RaceLeaderSmallGroupArray = new List<string>(); //換本區小組要用到的本區小組清單
            m_ChurchSmallGroupArray = new List<string>();    //換全教會小組要用到的全教會小組清單


            m_RaceLeaderArray.Add("忠勤,區長 001-001");
            m_RaceLeaderArray.Add("忠勤,區長 001-002");
            m_AreaLeaderArray.Add("忠勤,區長 001-001");
            m_AreaLeaderArray.Add("忠勤,區長 001-002");
            m_AreaLeaderArray.Add("忠勤,區長 002-001");
            m_AreaLeaderArray.Add("忠勤,區長 002-002");
            m_RaceLeaderSmallGroupArray.Add("火熱小組-002-001-001");
            m_RaceLeaderSmallGroupArray.Add("火熱小組-002-001-002");
            m_RaceLeaderSmallGroupArray.Add("火熱小組-002-001-003");
            m_RaceLeaderSmallGroupArray.Add("火熱小組-002-002-001");
            m_RaceLeaderSmallGroupArray.Add("火熱小組-002-002-002");
            m_RaceLeaderSmallGroupArray.Add("火熱小組-002-002-003");
            m_ChurchSmallGroupArray.Add("火熱小組-002-001-001");
            m_ChurchSmallGroupArray.Add("火熱小組-002-001-002");
            m_ChurchSmallGroupArray.Add("火熱小組-002-001-003");
            m_ChurchSmallGroupArray.Add("火熱小組-002-002-001");
            m_ChurchSmallGroupArray.Add("火熱小組-002-002-002");
            m_ChurchSmallGroupArray.Add("火熱小組-002-002-003");

        }

        public String SearchAreaLeaderIdFromRaceLeaderId(string aRaceLeaderId)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        if (aRaceLeaderId == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].RaceLeaderEntityId)
                        {
                            return this.m_ChurchRoot.AreaLeaderList[AreaCounter].AreaLeaderEntityId;
                        }
                    }
                }

                return "";
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public String SearchAreaLeaderIdFromRaceLeader(string aRaceLeaderName)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        if (aRaceLeaderName == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].RaceLeaderName)
                        {
                            return this.m_ChurchRoot.AreaLeaderList[AreaCounter].AreaLeaderEntityId;
                        }
                    }
                }

                return "";
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public String SearchRaceLeaderIdSmallGroupByName(string aSmallGroupName)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            if (aSmallGroupName == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].SmallGroupName)
                            {
                                return this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].RaceLeaderEntityId;
                            }
                        }
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }
        public String SearchSmallGroupNameByContactMemberFullName(string aContactFullName)
        {
            try
            {
                for (int AreaCounter = 0; AreaCounter < this.m_ChurchRoot.AreaLeaderList.Count; AreaCounter++)
                {
                    for (int RaceCounter = 0; RaceCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList.Count; RaceCounter++)
                    {
                        for (int SmalllGroupCounter = 0; SmalllGroupCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList.Count; SmalllGroupCounter++)
                        {
                            for (int MemberCounter = 0; MemberCounter < this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList.Count; MemberCounter++)
                            {
                                if (aContactFullName == this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].ContactMemberList[MemberCounter].FullName)
                                {
                                    return this.m_ChurchRoot.AreaLeaderList[AreaCounter].RaceLeaderList[RaceCounter].SmallGroupList[SmalllGroupCounter].SmallGroupName;
                                }
                            }
                        }
                    }
                }

                return "";
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }

        public String ParseParentId(string aParentId)
        {
            try
            {
                if (aParentId != null)
                {
                    String[] aParentIdStringArray = aParentId.Split('-');
                    if (aParentIdStringArray.Length == 6)
                    {
                        return aParentIdStringArray[0] + "-" + aParentIdStringArray[1] + "-" + aParentIdStringArray[2] + "-" + aParentIdStringArray[3] + "-" + aParentIdStringArray[4];
                    }
                    else
                    { return ""; }

                }
                else
                {
                    return "";
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }

        }

        #endregion
    }
    public class AddController
    {
        public bool ModifyFlag { get; set; } = false;
        public int Status { get; set; } = 0;
        public bool ProcessFlag { get; set; } = false;
        //public bool InsertActionModifyFlag { get; set; } = false;
        //public bool OnRowInsertingModifyFlag { get; set; } = false;
        public String Name { get; set; } = "";
        public String EntityId { get; set; } = "";
        public String ParentEntityId { get; set; } = "";
        public String Result { get; set; } = "";
    }
}
