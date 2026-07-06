// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/EquipmentDataManager.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class EquipmentDataManager
// 主要成員：SetupEquipmentData、InitialEquipmentData、GetEquipmenSmallGroupByContactId、GetMasterDetailIndex、InitialSamplData
// 引用命名空間：System、ToolUtilityNameSpace、ToolUtilityNameSpace.Factory、ToolUtilityNameSpace.DependencyInjection、Newtonsoft.Json、System.Collections.Generic、ChurchReport.WebServiceConnector、Newtonsoft.Json.Linq
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using ToolUtilityNameSpace.DependencyInjection;

using Newtonsoft.Json;
using System.Collections.Generic;
using ChurchReport.WebServiceConnector;
using Newtonsoft.Json.Linq;

namespace ChurchReport.Models
{
    public class EquipmentDataManager
    {
        #region 成員資料
        public String m_FullName = "";
        public String m_Account = "";
        public String m_Password = "";
        public DateTime m_SundayDate;

        public String EquipmentType = "沒裝備小組名單";

        // 透過建構函數注入取得 ToolUtilityClass
        private readonly ToolUtilityClass m_ToolUtilityClass;
        DownloadEquipment m_DownloadEquipment = new DownloadEquipment();

        // 裝備樹狀根
        public EquipmentRootClass m_EquipmenRoot = new EquipmentRootClass();

        #endregion

        #region 建構函數
        /// <summary>
        /// 預設建構函數，使用 Factory 模式獲取 ToolUtilityClass 實例
        /// </summary>
        public EquipmentDataManager()
        {
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
        }

        /// <summary>
        /// 建構函數，使用 Dependency Injection 模式
        /// </summary>
        /// <param name="toolUtilityProvider">ToolUtility 提供者</param>
        public EquipmentDataManager(IToolUtilityProvider toolUtilityProvider)
        {
            if (toolUtilityProvider == null)
                throw new ArgumentNullException(nameof(toolUtilityProvider));

            m_ToolUtilityClass = toolUtilityProvider.GetToolUtility();
        }
        #endregion

        #region 初始化幸福小組
        public void SetupEquipmentData(String Account, String Password)
        {
            //待完成....
            if (m_EquipmenRoot.EquipmenSmallGroupList == null)
            {
                m_EquipmenRoot = m_DownloadEquipment.GetEquipmentList(Account, Password, ref m_EquipmenRoot);
            }
            else
            {
                // 已經有資料就不需要再處理什麼了
            }
            //InitialSamplData();

            if (m_EquipmenRoot != null && m_EquipmenRoot.EquipmenSmallGroupList != null && m_EquipmenRoot.EquipmenSmallGroupList.Count > 0)
            {
                EquipmentType = "有裝備小組名單";
            }
            else
            {
                EquipmentType = "沒裝備小組名單";
            }
        }
        public void InitialEquipmentData(ref EquipmentRootClass aActiveEquipmentListClass)
        {
            for (int counter = 0; counter < aActiveEquipmentListClass.EquipmenSmallGroupList.Count; counter++)
            {
                for (int i = 0; i < aActiveEquipmentListClass.EquipmenSmallGroupList[counter].EquipmentContactList.Count; i++)
                {
                    aActiveEquipmentListClass.EquipmenSmallGroupList[counter].EquipmentContactList[i].ModifiedFlag = false;
                    aActiveEquipmentListClass.EquipmenSmallGroupList[counter].EquipmentContactList[i].ModifiedFlag = false;

                    for (int j = 0; j < aActiveEquipmentListClass.EquipmenSmallGroupList[counter].EquipmentContactList[i].StorLessonsList.Count; j++)
                    {
                        aActiveEquipmentListClass.EquipmenSmallGroupList[counter].EquipmentContactList[i].StorLessonsList[j].ModifiedFlag = false;
                    }
                }
            }

        }
        #endregion
        #region 工具區
        public EquipmenSmallGroup GetEquipmenSmallGroupByContactId(String aContactId)
        {
            foreach (EquipmenSmallGroup aEquipmenSmallGroup in this.m_EquipmenRoot.EquipmenSmallGroupList)
            {
                foreach (EquipmentContact aEquipmentContact in aEquipmenSmallGroup.EquipmentContactList)
                {
                    if (aEquipmentContact.EquipmentContactId == aContactId)
                    {
                        return aEquipmenSmallGroup;
                    }

                }
            }

            return null;
        }
        private void GetMasterDetailIndex(ref EquipmentRootClass aActiveEquipmentListClass, string Key, ref int ListIndex, ref int MasterIndex, ref int DetailIndex)
        {
            ListIndex = MasterIndex = DetailIndex = -1;

            for (int counter = 0; counter < aActiveEquipmentListClass.EquipmenSmallGroupList.Count; counter++)
            {
                for (int i = 0; i < aActiveEquipmentListClass.EquipmenSmallGroupList[counter].EquipmentContactList.Count; i++)
                {
                    if (Key == aActiveEquipmentListClass.EquipmenSmallGroupList[counter].EquipmentContactList[i].EquipmentContactId)
                    {
                        ListIndex = counter;
                        MasterIndex = i;
                        DetailIndex = -1; // 修改的是週報
                        return;
                    }
                    for (int j = 0; j < aActiveEquipmentListClass.EquipmenSmallGroupList[counter].EquipmentContactList[i].StorLessonsList.Count; j++)
                    {
                        if (Key == aActiveEquipmentListClass.EquipmenSmallGroupList[counter].EquipmentContactList[i].StorLessonsList[j].StorLessonsEntityId)
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
            if (m_EquipmenRoot.EquipmenSmallGroupList != null)
            {
                // 已經有資料就不需要再處理什麼了
            }
            else
            {
                m_EquipmenRoot.EquipmenSmallGroupList = new List<EquipmenSmallGroup>
            {
                new EquipmenSmallGroup
                {
                     SmallGroupListEntityId = "001",
                     SmallGroupName= "火熱小組",
                     EquipmentContactList = new List<EquipmentContact>
                     {
                         new EquipmentContact
                         {
                            SmallGroupName= "火熱小組",
                            ContactFullName ="胡夢嵩",
                            EquipmentStatus = "門徒001",
                            SmallGroupListEntityId = "001",
                            EquipmentContactId = "001-001",
                            StorLessonsList = new  List<EquipmentStorLessons>
                               {
                                      new EquipmentStorLessons
                                      {
                                           SmallGroupListEntityId = "001",
                                           EquipmentContactId = "001-001",
                                           StorLessonsEntityId = "001-001-001",
                                           StorLessonsName ="77777",
                                            DiscipleLessonsName = "幸福001",
                                            CurrentComplete = true,
                                            DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                      },
                                      new EquipmentStorLessons
                                      {
                                            SmallGroupListEntityId = "001",
                                            EquipmentContactId = "001-001",
                                            StorLessonsEntityId = "001-001-002",
                                            StorLessonsName ="999999",
                                            DiscipleLessonsName = "幸福001",
                                            CurrentComplete = false,
                                            DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                    },
                                      new EquipmentStorLessons
                                      {
                                           SmallGroupListEntityId = "001",
                                           EquipmentContactId = "001-001",
                                           StorLessonsEntityId = "001-001-001",
                                           StorLessonsName ="77777",
                                            DiscipleLessonsName = "幸福001",
                                            CurrentComplete = false,
                                            DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                      },
                                      new EquipmentStorLessons
                                      {
                                            SmallGroupListEntityId = "001",
                                            EquipmentContactId = "001-001",
                                            StorLessonsEntityId = "001-001-002",
                                            StorLessonsName ="999999",
                                            DiscipleLessonsName = "幸福001",
                                            CurrentComplete = true,
                                            DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                    },
                                      new EquipmentStorLessons
                                      {
                                            SmallGroupListEntityId = "001",
                                            EquipmentContactId = "001-001",
                                            StorLessonsEntityId = "001-001-002",
                                            StorLessonsName ="999999",
                                            DiscipleLessonsName = "幸福001",
                                            CurrentComplete = true,
                                            DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                    },
                               }
                         }
                     }
                },
                new EquipmenSmallGroup
                {
                     SmallGroupListEntityId = "002",
                     SmallGroupName= "渴慕小組",
                     EquipmentContactList = new List<EquipmentContact>
                     {
                         new EquipmentContact
                         {
                            SmallGroupName= "渴慕小組",
                            ContactFullName ="吳連碧",
                            EquipmentStatus = "門徒002",
                            SmallGroupListEntityId = "002",
                            EquipmentContactId = "002-001",
                            StorLessonsList = new  List<EquipmentStorLessons>
                               {
                                      new EquipmentStorLessons
                                      {
                                           SmallGroupListEntityId = "002",
                                           EquipmentContactId = "002-001",
                                           StorLessonsEntityId = "002-001-001",
                                           StorLessonsName ="77777",
                                           DiscipleLessonsName = "精兵002",
                                           CurrentComplete = true,
                                           DiscipleLessonsDateTime = DateTime.Now.AddDays(-25)
                                      },
                                      new EquipmentStorLessons
                                      {
                                            SmallGroupListEntityId = "002",
                                            EquipmentContactId = "002-001",
                                            StorLessonsEntityId = "002-001-002",
                                            StorLessonsName ="999999",
                                            DiscipleLessonsName = "幸福002",
                                            CurrentComplete = false,
                                            DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                      },
                                      new EquipmentStorLessons
                                      {
                                           SmallGroupListEntityId = "002",
                                           EquipmentContactId = "002-001",
                                           StorLessonsEntityId = "002-001-001",
                                           StorLessonsName ="77777",
                                           DiscipleLessonsName = "精兵002",
                                           CurrentComplete = true,
                                           DiscipleLessonsDateTime = DateTime.Now.AddDays(-25)
                                      },
                                      new EquipmentStorLessons
                                      {
                                            SmallGroupListEntityId = "002",
                                            EquipmentContactId = "002-001",
                                            StorLessonsEntityId = "002-001-002",
                                            StorLessonsName ="999999",
                                            DiscipleLessonsName = "幸福002",
                                            CurrentComplete = false,
                                            DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                      },
                                      new EquipmentStorLessons
                                      {
                                           SmallGroupListEntityId = "002",
                                           EquipmentContactId = "002-001",
                                           StorLessonsEntityId = "002-001-001",
                                           StorLessonsName ="77777",
                                           DiscipleLessonsName = "精兵002",
                                           CurrentComplete = true,
                                           DiscipleLessonsDateTime = DateTime.Now.AddDays(-25)
                                      },
                                      new EquipmentStorLessons
                                      {
                                            SmallGroupListEntityId = "002",
                                            EquipmentContactId = "002-001",
                                            StorLessonsEntityId = "002-001-002",
                                            StorLessonsName ="999999",
                                            DiscipleLessonsName = "幸福002",
                                            CurrentComplete = false,
                                            DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                      },
                                      new EquipmentStorLessons
                                      {
                                           SmallGroupListEntityId = "002",
                                           EquipmentContactId = "002-001",
                                           StorLessonsEntityId = "002-001-001",
                                           StorLessonsName ="77777",
                                           DiscipleLessonsName = "精兵002",
                                           CurrentComplete = true,
                                           DiscipleLessonsDateTime = DateTime.Now.AddDays(-25)
                                      },
                                      new EquipmentStorLessons
                                      {
                                            SmallGroupListEntityId = "002",
                                            EquipmentContactId = "002-001",
                                            StorLessonsEntityId = "002-001-002",
                                            StorLessonsName ="999999",
                                            DiscipleLessonsName = "幸福002",
                                            CurrentComplete = false,
                                            DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                      },
                               }
                         }
                     }
                },
                new EquipmenSmallGroup
                {
                     SmallGroupListEntityId = "003",
                     SmallGroupName= "恩膏小組",
                     EquipmentContactList = new List<EquipmentContact>
                     {
                         new EquipmentContact
                         {
                            SmallGroupName= "恩膏小組",
                            ContactFullName ="胡逸凡",
                            EquipmentStatus = "門徒003",
                            SmallGroupListEntityId = "003",
                            EquipmentContactId = "003-001",
                            StorLessonsList = new  List<EquipmentStorLessons>
                               {
                                      new EquipmentStorLessons
                                      {
                                           SmallGroupListEntityId = "003",
                                           EquipmentContactId = "003-001",
                                           StorLessonsEntityId = "003-001-001",
                                           StorLessonsName ="77777",
                                           DiscipleLessonsName = "幸福001",
                                           CurrentComplete = true,
                                           DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                      },
                                      new EquipmentStorLessons
                                      {
                                           SmallGroupListEntityId = "003",
                                           EquipmentContactId = "003-001",
                                           StorLessonsEntityId = "003-001-002",
                                           StorLessonsName ="999999",
                                           DiscipleLessonsName = "幸福001",
                                           CurrentComplete = true,
                                           DiscipleLessonsDateTime = DateTime.Now.AddDays(-35)
                                    },
                               }
                         }
                     }
                }
            };
            }
        }
        #endregion
    }
}
