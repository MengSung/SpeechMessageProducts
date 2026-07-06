// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/HappyGroupDataManager.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class HappyGroupDataManager
// 主要成員：SetupHappyGroupData、InitialHappyGroupData、AddActiveHappyGroup、AddWeeklyReport、AddBest、GetHappyGroupWeeklyReportListClass、GetHappyGroupWeeklyReportListClassByWeeklyReportId、WeeklyReportOrBest、UpdateActiveHappyGroup、UpdateMasterActiveHappyGroup
// 引用命名空間：System、ToolUtilityNameSpace、ToolUtilityNameSpace.DependencyInjection、Newtonsoft.Json、System.Collections.Generic、ChurchReport.WebServiceConnector、Newtonsoft.Json.Linq
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;

using Newtonsoft.Json;
using System.Collections.Generic;
using ChurchReport.WebServiceConnector;
using Newtonsoft.Json.Linq;

namespace ChurchReport.Models
{
    /// <summary>
    /// 幸福小組資料管理類別
    /// 負責處理幸福小組相關的業務邏輯
    /// 使用 Dependency Injection 模式注入 ToolUtilityClass
    /// </summary>
    public class HappyGroupDataManager
    {
        #region 成員資料
        public String m_FullName = "";
        public String m_Account = "";
        public String m_Password = "";
        public DateTime m_SundayDate;

        public String HappyType = "沒幸福小組名單";

        private readonly IToolUtilityProvider _toolUtilityProvider;

        /// <summary>
        /// 工具類單例 (透過 Provider 取得)
        /// </summary>
        private ToolUtilityClass ToolUtility => _toolUtilityProvider?.GetToolUtility();

        DownloadHappyGroup m_DownloadHappyGroup = new DownloadHappyGroup();


        // 一個人有多個幸福小組
        public HappyGroupListClass m_ActiveHappyGroupListClass = new HappyGroupListClass();

        // 進行中的幸福小組
        public HappyGroupWeeklyReportListClass m_ActiveHappyGroupWeeklyReportList = new HappyGroupWeeklyReportListClass();

        // 已完成的幸福小組
        public HappyGroupWeeklyReportListClass m_CompletedHappyGroupWeeklyReportList = new HappyGroupWeeklyReportListClass();
        #endregion

        #region 建構函式

        /// <summary>
        /// 無參數建構函式 (向後相容，但不建議使用)
        /// 此建構函式為了相容舊程式碼而保留，新程式碼應使用帶參數的建構函式
        /// </summary>
        [Obsolete("請使用帶有 IToolUtilityProvider 參數的建構函式")]
        public HappyGroupDataManager()
        {
            // 向後相容：如果沒有提供 Provider，則不使用 ToolUtility
            _toolUtilityProvider = null;
        }

        /// <summary>
        /// 依賴注入建構函式 (建議使用)
        /// </summary>
        /// <param name="toolUtilityProvider">ToolUtility 提供者實例 (透過 DI 注入)</param>
        public HappyGroupDataManager(IToolUtilityProvider toolUtilityProvider)
        {
            _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
        }

        #endregion

        #region 初始化幸福小組
        public void SetupHappyGroupData(String Account, String Password)
        {
            m_ActiveHappyGroupListClass = m_DownloadHappyGroup.GetHappyGroupList(Account, Password);

            if (m_ActiveHappyGroupListClass != null && m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass != null && m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass.Count > 0)
            {
                HappyType = "有幸福小組名單";
            }
            else
            {
                HappyType = "沒幸福小組名單";
            }
        }
        public void InitialHappyGroupData(ref HappyGroupListClass aActiveHappyGroupListClass)
        {
            for (int counter = 0; counter < aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass.Count; counter++)
            {
                for (int i = 0; i < aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[counter].HappyGroupWeeklyReportList.Count; i++)
                {
                    aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[counter].HappyGroupWeeklyReportList[i].WeeklyReportModifiedFlag = false;
                    aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[counter].HappyGroupWeeklyReportList[i].ModifiedFlag = false;

                    for (int j = 0; j < aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[counter].HappyGroupWeeklyReportList[i].BestRecordList.Count; j++)
                    {
                        aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[counter].HappyGroupWeeklyReportList[i].BestRecordList[j].BestModifiedFlag = false;
                        aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[counter].HappyGroupWeeklyReportList[i].BestRecordList[j].ModifiedFlag = false;
                    }
                }
            }

        }
        #endregion
        #region 新增
        public void AddActiveHappyGroup(string values)
        {
            #region 先判斷是新增週報還是BEST
            String InsertType = (String)JObject.Parse(values).GetValue("InsertType");

            if (InsertType == "WeeklyReport")
            {
                #region 新增週報
                HappyGroupWeeklyReport aToAddHappyGroupWeeklyReport = new HappyGroupWeeklyReport();
                JsonConvert.PopulateObject(values, aToAddHappyGroupWeeklyReport);

                String ParentListId = (String)JObject.Parse(values).GetValue("MasterParentID");

                aToAddHappyGroupWeeklyReport.MeetingDate = aToAddHappyGroupWeeklyReport.MeetingDate.ToLocalTime();

                // 是否有輸入週次 WeekCounter， 因為如果沒有改變整數，但是反序列之後仍然會有值，而不會是 null，所以要靠旗標來幫忙
                bool WeekCounterFlag = values.Contains("WeekCounter") ? true : false;

                // 是否有輸入主題
                bool TopicFlag = values.Contains("Topic") ? true : false;

                // 新增週報
                if (WeekCounterFlag == true && TopicFlag == true)
                {
                    // 同時輸入週次及主題才能新增幸福小組週報
                    AddWeeklyReport(ParentListId, aToAddHappyGroupWeeklyReport);
                }
                #endregion
            }
            else
            {
                #region 新增BEST

                var aNewBestRecord = new BestRecord();
                JsonConvert.PopulateObject(values, aNewBestRecord );

                String WeeklyReportId = (String)JObject.Parse(values).GetValue("MasterParentID");

                if (aNewBestRecord.FullName != null && aNewBestRecord.FullName != "")
                {
                    // 新增BEST的名字不可以是空白
                    AddBest( WeeklyReportId ,aNewBestRecord );
                }
                #endregion
            }
            #endregion

        }
        public void AddWeeklyReport(String ParentListId, HappyGroupWeeklyReport aToAddHappyGroupWeeklyReport)
        {
            HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass = GetHappyGroupWeeklyReportListClass(ParentListId);

            // 新增幸福小組週報，同時以名單成員作為初始成員
            m_DownloadHappyGroup.AddHappyGroupWeeklyReport(ref aHappyGroupWeeklyReportListClass, ref aToAddHappyGroupWeeklyReport);

            #region 前台網頁要呈現的週報資料
            // 前台網頁要呈現的週報資料，因為已經到後台把幸福小組周報相關資料(聚會時間、地點、組員名單等等)抓回來了，
            aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList.Add(aToAddHappyGroupWeeklyReport);
            #endregion

        }
        public void AddBest(String WeeklyReportId, BestRecord aBestRecord)
        {
            HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass  = GetHappyGroupWeeklyReportListClassByWeeklyReportId(WeeklyReportId);

            // 新增幸福小組BEST
            m_DownloadHappyGroup.CreateBest(ref aHappyGroupWeeklyReportListClass, WeeklyReportId, ref aBestRecord);

            #region 前台網頁要呈現的Best資料
            HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded = m_DownloadHappyGroup.GetHappyGroupWeeklyReportToBeAdded ( ref aHappyGroupWeeklyReportListClass, WeeklyReportId);
            aHappyGroupWeeklyReportToBeAdded.BestRecordList.Add(aBestRecord);
            #endregion
        }
        public HappyGroupWeeklyReportListClass GetHappyGroupWeeklyReportListClass(String ParentListId)
        {
            foreach (HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass in this.m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass)
            {
                if (aHappyGroupWeeklyReportListClass.ListEntityId == ParentListId)
                {
                    return aHappyGroupWeeklyReportListClass;
                }
            }

            return null;
        }
        public HappyGroupWeeklyReportListClass GetHappyGroupWeeklyReportListClassByWeeklyReportId(String WeeklyReportId)
        {
            foreach (HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass in this.m_ActiveHappyGroupListClass.HappyGroupWeeklyReportListClass)
            {
                foreach(HappyGroupWeeklyReport aHappyGroupWeeklyReport in aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList)
                {
                    if(aHappyGroupWeeklyReport.HappyGroupWeeklyReportId == WeeklyReportId )
                    {
                        return aHappyGroupWeeklyReportListClass;
                    }

                }
            }

            return null;
        }
        private String WeeklyReportOrBest(String values)
        {
            if (values.Contains("WeekCounter") || values.Contains("Topic") || values.Contains("MeetingDate") || values.Contains("Location") || values.Contains("StartTime") || values.Contains("EndTime") || values.Contains("HappyWeeklyReport"))
            {
                return "WeeklyReport";
            }
            else
            {
                return "Best";
            }
        }
        #endregion
        #region 修改
        public void UpdateActiveHappyGroup(string key, string values)
        {
            int ListIndex = -1;     // 哪個幸福小組?
            int MasterIndex = -1;   // 哪一週?
            int DetailIndex = -1;   // 哪個Best?
            GetMasterDetailIndex(ref m_ActiveHappyGroupListClass, key, ref ListIndex, ref MasterIndex, ref DetailIndex);

            if (MasterIndex >= 0 && DetailIndex < 0)
            {
                // 修改幸福小組週報
                HappyGroupWeeklyReport aUpdatedHappyGroupWeeklyReport = JsonConvert.DeserializeObject<HappyGroupWeeklyReport>(values);

                // 設定前端傳來週報有被修改過的旗標
                aUpdatedHappyGroupWeeklyReport.ModifiedFlag = true;

                // 從前端傳來有更改過的週報去更新網頁端的幸福小組週報內容
                bool WeekCounterFlag = values.Contains("WeekCounter") ? true : false;
                this.UpdateMasterActiveHappyGroup(ref m_ActiveHappyGroupListClass, ListIndex, MasterIndex, aUpdatedHappyGroupWeeklyReport, WeekCounterFlag);
            }
            else
            {
                // 修改幸福小組個人出席紀錄

                // 取得使用者改過的BEST資料
                BestRecord aBestRecord = JsonConvert.DeserializeObject<BestRecord>(values);
                bool PresentFlag = values.Contains("Present") ? true : false;
                bool DecisionFlag = values.Contains("Decision") ? true : false;

                // 設定幸福小組個人出席紀錄有被修改過的旗標
                aBestRecord.ModifiedFlag = true;

                // 更新網頁端的幸福小組個人出席紀錄內容
                UpdateDetailActiveHappyGroup(ref m_ActiveHappyGroupListClass, ListIndex, MasterIndex, DetailIndex, aBestRecord, PresentFlag, DecisionFlag);
            }
        }
        private void UpdateMasterActiveHappyGroup(ref HappyGroupListClass aActiveHappyGroupListClass, int ListIndex, int MasterIndex, HappyGroupWeeklyReport aUpdatedHappyGroupWeeklyReport, bool WeekCounterFlag)
        {
            // 告知週報其中某個幸福小組個人出席紀錄欄位有被修改過
            aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].DirtyFlag = true;

            aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].WeeklyReportModifiedFlag = true;

            if (WeekCounterFlag == true)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].WeekCounter = aUpdatedHappyGroupWeeklyReport.WeekCounter;
            }
            if (aUpdatedHappyGroupWeeklyReport.Topic != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].Topic = aUpdatedHappyGroupWeeklyReport.Topic;
            }
            if (aUpdatedHappyGroupWeeklyReport.MeetingDate.Year > 1)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].MeetingDate = aUpdatedHappyGroupWeeklyReport.MeetingDate;
            }
            if (aUpdatedHappyGroupWeeklyReport.Location != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].Location = aUpdatedHappyGroupWeeklyReport.Location;
            }
            if (aUpdatedHappyGroupWeeklyReport.StartTime != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].StartTime = aUpdatedHappyGroupWeeklyReport.StartTime;
            }
            if (aUpdatedHappyGroupWeeklyReport.EndTime != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].EndTime = aUpdatedHappyGroupWeeklyReport.EndTime;
            }
            if (aUpdatedHappyGroupWeeklyReport.ModifiedFlag != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].ModifiedFlag = aUpdatedHappyGroupWeeklyReport.ModifiedFlag;
            }
            if (aUpdatedHappyGroupWeeklyReport.HappyWeeklyReport != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].HappyWeeklyReport = aUpdatedHappyGroupWeeklyReport.HappyWeeklyReport;
            }

        }
        private void UpdateDetailActiveHappyGroup(ref HappyGroupListClass aActiveHappyGroupListClass, int ListIndex, int MasterIndex, int DetailIndex, BestRecord aBestRecord, bool PresentFlag, bool DecisionFlag)
        {
            // 告知週報其中某個幸福小組個人出席紀錄欄位有被修改過
            aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].DirtyFlag = true;

            aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordModifiedFlag = true;

            // 幸福小組個人出席紀錄欄位有被修改過
            aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].BestModifiedFlag = true;

            if (aBestRecord.FullName != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].FullName = aBestRecord.FullName;
            }
            if (aBestRecord.MobilePhone != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].MobilePhone = aBestRecord.MobilePhone;
            }
            if (PresentFlag == true)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].Present = aBestRecord.Present;
            }
            if (DecisionFlag == true)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].Decision = aBestRecord.Decision;
            }
            if (aBestRecord.Note != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].Note = aBestRecord.Note;
            }
            if (aBestRecord.ModifiedFlag != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].ModifiedFlag = aBestRecord.ModifiedFlag;
            }
            if (aBestRecord.BestLeader != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].BestLeader = aBestRecord.BestLeader;
            }
            if (aBestRecord.BestIntroducer != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].BestIntroducer = aBestRecord.BestIntroducer;
            }
            if (aBestRecord.BestRelationship != null)
            {
                aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].BestRelationship = aBestRecord.BestRelationship;
            }
        }
        #endregion
        #region 刪除
        public void DeleteActiveHappyGroup(string key)
        {
            int ListIndex = -1;     // 哪個幸福小組?
            int MasterIndex = -1;   // 哪一週?
            int DetailIndex = -1;   // 哪個Best?
            GetMasterDetailIndex(ref m_ActiveHappyGroupListClass, key, ref ListIndex, ref MasterIndex, ref DetailIndex);

            if (MasterIndex >= 0 && DetailIndex < 0)
            {
                // 刪除週報
                this.DeleteMasterActiveHappyGroup(ref m_ActiveHappyGroupListClass, ListIndex, MasterIndex);
            }
            else
            {
                // 刪除幸福小組個人出席紀錄
                DeleteDetailActiveHappyGroup(ref m_ActiveHappyGroupListClass, ListIndex, MasterIndex, DetailIndex);
            }
        }
        private void DeleteMasterActiveHappyGroup(ref HappyGroupListClass aActiveHappyGroupListClass, int ListIndex, int MasterIndex)
        {
            aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList.RemoveAt(MasterIndex);
        }
        private void DeleteDetailActiveHappyGroup(ref HappyGroupListClass aActiveHappyGroupListClass, int ListIndex, int MasterIndex, int DetailIndex)
        {
            // 告知週報其中某個幸福小組個人出席紀錄欄位有被修改過
            aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordModifiedFlag = true;

            // 移除幸福小組個人出席紀錄欄位有被修改過
            aActiveHappyGroupListClass.HappyGroupWeeklyReportListClass[ListIndex].HappyGroupWeeklyReportList[MasterIndex].BestRecordList.RemoveAt(DetailIndex);
        }
        #endregion
        #region 上傳儲存
        public void SaveActiveHappyGroup()
        {
            #region 上傳幸福小組週報
            m_DownloadHappyGroup.UpdateHappyGroupListClass(m_ActiveHappyGroupListClass);
            #endregion
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

        #endregion

    }
}
