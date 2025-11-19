using System;
using ToolUtilityNameSpace;

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

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365-9.0");
        //private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");
        DownloadEquipment m_DownloadEquipment = new DownloadEquipment();

        // 裝備樹狀根
        public EquipmentRootClass m_EquipmenRoot = new EquipmentRootClass();

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
