using System;
using ToolUtilityNameSpace;

using Newtonsoft.Json;
using System.Collections.Generic;
using ChurchReport.WebServiceConnector;

namespace ChurchReport.Models
{
    public class HappyGroupDataManager
    {
        #region 成員資料
        public String m_FullName = "";
        public String m_Account = "";
        public String m_Password = "";
        public DateTime m_SundayDate;

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");
        DownloadHappyGroup m_DownloadHappyGroup = new DownloadHappyGroup();

        // 進行中的幸福小組
        //public DataGridEmployees m_ActiveDataGridEmployees = new DataGridEmployees();
        public HappyGroupWeeklyReportListClass m_ActiveHappyGroupWeeklyReportList = new HappyGroupWeeklyReportListClass();

        // 已完成的幸福小組
        //public DataGridEmployees m_CompletedDataGridEmployees = new DataGridEmployees();
        public HappyGroupWeeklyReportListClass m_CompletedHappyGroupWeeklyReportList = new HappyGroupWeeklyReportListClass();
        #endregion
        #region 初始化幸福小組
        public void SetupHappyGroupData(String Account, String Password)
        {
            m_ActiveHappyGroupWeeklyReportList = m_DownloadHappyGroup.GetHappyGroupWeeklyReportList(Account, Password);
        }
        #endregion
        #region 新增
        public void AddActiveHappyGroup(string values)
        {
            #region 先判斷是新增週報還是BEST
            String AddType = WeeklyReportOrBest(values);

            if (AddType == "WeeklyReport")
            {
                #region 新增週報
                // 轉換(反序列)從網頁有改變的欄位成為C# Weekly Report的結構
                HappyGroupWeeklyReport aToAddHappyGroupWeeklyReport = JsonConvert.DeserializeObject<HappyGroupWeeklyReport>(values);

                // 不知為何反序列會差一天
                //aToAddHappyGroupWeeklyReport.MeetingDate = aToAddHappyGroupWeeklyReport.MeetingDate.AddDays(1);

                // 是否有修改 WeekCounter， 因為如果沒有改變整數，但是反序列之後仍然會有值，而不會是 null，所以要靠旗標來幫忙
                bool WeekCounterFlag = values.Contains("WeekCounter") ? true : false;

                AddWeeklyReport(aToAddHappyGroupWeeklyReport);
                #endregion
            }
            else
            {
                #region 新增BEST

                // 轉換(反序列)從網頁有改變的欄位成為C# Best 的結構
                BestRecord aBestRecord = JsonConvert.DeserializeObject<BestRecord>(values);

                if (aBestRecord.FullName != null && aBestRecord.FullName != "")
                {
                    // 新增BEST的名字不可以是空白
                    AddBest(aBestRecord);
                }
                #endregion
            }
            #endregion

        }
        public void AddWeeklyReport(HappyGroupWeeklyReport aToAddHappyGroupWeeklyReport)
        {
            // 新增幸福小組週報，同時以名單成員作為初始成員
            m_DownloadHappyGroup.AddHappyGroupWeeklyReport(ref m_ActiveHappyGroupWeeklyReportList, ref aToAddHappyGroupWeeklyReport);

            // 前台網頁要呈現的週報資料，因為已經到後台把幸福小組周報相關資料(聚會時間、地點、組員名單等等)抓回來了，
            m_ActiveHappyGroupWeeklyReportList.HappyGroupWeeklyReportList.Add(aToAddHappyGroupWeeklyReport);

        }
        public void AddBest(BestRecord aBestRecord)
        {

            m_DownloadHappyGroup.CreateBest(ref m_ActiveHappyGroupWeeklyReportList, ref aBestRecord);

            #region 前台網頁要呈現的Best資料
            int WeeklyReportListCount = m_ActiveHappyGroupWeeklyReportList.HappyGroupWeeklyReportList.Count;
            Guid aWeeklyReportId = new Guid(m_ActiveHappyGroupWeeklyReportList.HappyGroupWeeklyReportList[WeeklyReportListCount - 1].HappyGroupWeeklyReportId);
            HappyGroupWeeklyReport aHappyGroupWeeklyReportToBeAdded = m_ActiveHappyGroupWeeklyReportList.HappyGroupWeeklyReportList[WeeklyReportListCount - 1];
            aHappyGroupWeeklyReportToBeAdded.BestRecordList.Add(aBestRecord);
            #endregion
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

            int MasterIndex = -1;
            int DetailIndex = -1;
            GetMasterDetailIndex(ref m_ActiveHappyGroupWeeklyReportList, key, ref MasterIndex, ref DetailIndex);

            if (MasterIndex >= 0 && DetailIndex < 0)
            {
                // 修改幸福小組週報
                HappyGroupWeeklyReport aUpdatedHappyGroupWeeklyReport = JsonConvert.DeserializeObject<HappyGroupWeeklyReport>(values);

                // 設定前端傳來週報有被修改過的旗標
                aUpdatedHappyGroupWeeklyReport.ModifiedFlag = true;

                // 修改系統的幸福小組週報
                m_DownloadHappyGroup.UpdateHappyGroupWeeklyReport(key, ref aUpdatedHappyGroupWeeklyReport);

                // 從前端傳來有更改過的週報去更新網頁端的幸福小組週報內容
                bool WeekCounterFlag = values.Contains("WeekCounter") ? true : false;
                this.UpdateMasterActiveHappyGroup(ref m_ActiveHappyGroupWeeklyReportList, MasterIndex, aUpdatedHappyGroupWeeklyReport, WeekCounterFlag);
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

                // 修改系統的幸福小組個人出席紀錄
                //m_DownloadHappyGroup.UpdateBestRecord(key, ref aBestRecord, PresentFlag, DecisionFlag);

                // 更新網頁端的幸福小組個人出席紀錄內容
                UpdateDetailActiveHappyGroup(ref m_ActiveHappyGroupWeeklyReportList, MasterIndex, DetailIndex, aBestRecord, PresentFlag, DecisionFlag);
            }
        }
        private void UpdateMasterActiveHappyGroup(ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass, int MasterIndex, HappyGroupWeeklyReport aUpdatedHappyGroupWeeklyReport, bool WeekCounterFlag)
        {
            aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].WeeklyReportModifiedFlag = true;

            if (WeekCounterFlag == true)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].WeekCounter = aUpdatedHappyGroupWeeklyReport.WeekCounter;
            }
            if (aUpdatedHappyGroupWeeklyReport.Topic != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].Topic = aUpdatedHappyGroupWeeklyReport.Topic;
            }
            if (aUpdatedHappyGroupWeeklyReport.MeetingDate.Year > 1)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].MeetingDate = aUpdatedHappyGroupWeeklyReport.MeetingDate;
            }
            if (aUpdatedHappyGroupWeeklyReport.Location != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].Location = aUpdatedHappyGroupWeeklyReport.Location;
            }
            if (aUpdatedHappyGroupWeeklyReport.StartTime != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].StartTime = aUpdatedHappyGroupWeeklyReport.StartTime;
            }
            if (aUpdatedHappyGroupWeeklyReport.EndTime != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].EndTime = aUpdatedHappyGroupWeeklyReport.EndTime;
            }
            if (aUpdatedHappyGroupWeeklyReport.ModifiedFlag != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].ModifiedFlag = aUpdatedHappyGroupWeeklyReport.ModifiedFlag;
            }

            if (aUpdatedHappyGroupWeeklyReport.HappyWeeklyReport != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].HappyWeeklyReport = aUpdatedHappyGroupWeeklyReport.HappyWeeklyReport;
            }

        }
        private void UpdateDetailActiveHappyGroup(ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass, int MasterIndex, int DetailIndex, BestRecord aBestRecord, bool PresentFlag, bool DecisionFlag)
        {
            // 告知週報其中某個幸福小組個人出席紀錄欄位有被修改過
            aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordModifiedFlag = true;

            // 幸福小組個人出席紀錄欄位有被修改過
            aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].BestModifiedFlag = true;

            if (aBestRecord.FullName != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].FullName = aBestRecord.FullName;
            }
            if (aBestRecord.MobilePhone != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].MobilePhone = aBestRecord.MobilePhone;
            }
            if (PresentFlag == true)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].Present = aBestRecord.Present;
            }
            if (DecisionFlag == true)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].Decision = aBestRecord.Decision;
            }
            if (aBestRecord.Note != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].Note = aBestRecord.Note;
            }
            if (aBestRecord.ModifiedFlag != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].ModifiedFlag = aBestRecord.ModifiedFlag;
            }
            if (aBestRecord.BestLeader != null)
            {
                aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordList[DetailIndex].BestLeader = aBestRecord.BestLeader;
            }
        }
        #endregion
        #region 刪除
        public void DeleteActiveHappyGroup(string key)
        {

            int MasterIndex = -1;
            int DetailIndex = -1;
            GetMasterDetailIndex(ref m_ActiveHappyGroupWeeklyReportList, key, ref MasterIndex, ref DetailIndex);

            if (MasterIndex >= 0 && DetailIndex < 0)
            {
                // 刪除週報
                this.DeleteMasterActiveHappyGroup(ref m_ActiveHappyGroupWeeklyReportList, MasterIndex);
            }
            else
            {
                // 刪除幸福小組個人出席紀錄
                DeleteDetailActiveHappyGroup(ref m_ActiveHappyGroupWeeklyReportList, MasterIndex, DetailIndex);
            }
        }
        private void DeleteMasterActiveHappyGroup(ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass, int MasterIndex)
        {
            aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList.RemoveAt(MasterIndex);
        }
        private void DeleteDetailActiveHappyGroup(ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass, int MasterIndex, int DetailIndex)
        {
            // 告知週報其中某個幸福小組個人出席紀錄欄位有被修改過
            aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordModifiedFlag = true;

            // 移除幸福小組個人出席紀錄欄位有被修改過
            aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[MasterIndex].BestRecordList.RemoveAt(DetailIndex);
        }
        #endregion
        #region 上傳儲存
        public void SaveActiveHappyGroup(string key)
        {

            m_DownloadHappyGroup.UpdateHappyGroupWeeklyReportList(m_ActiveHappyGroupWeeklyReportList);

        }
        #endregion
        #region 工具區
        private void GetMasterDetailIndex(ref HappyGroupWeeklyReportListClass aHappyGroupWeeklyReportListClass, string Key, ref int MasterIndex, ref int DetailIndex)
        {
            MasterIndex = DetailIndex = -1;

            for (int i = 0; i < aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList.Count; i++)
            {
                if (Key == aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[i].HappyGroupWeeklyReportId)
                {
                    MasterIndex = i;
                    DetailIndex = -1;
                    return;
                }
                for (int j = 0; j < aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[i].BestRecordList.Count; j++)
                {
                    if (Key == aHappyGroupWeeklyReportListClass.HappyGroupWeeklyReportList[i].BestRecordList[j].BestRecordId)
                    {
                        MasterIndex = i;
                        DetailIndex = j;
                        return;
                    }
                }
            }

        }
        //private void GetMasterDetailIndex(string Key, ref int MasterIndex, ref int DetailIndex)
        //{
        //    String[] KeyArray = Key.Split('-');
        //    if (KeyArray.Length == 1)
        //    {
        //        // 修改週報
        //        MasterIndex = Convert.ToInt32(KeyArray[0]);
        //        DetailIndex = -1;
        //    }
        //    else if (KeyArray.Length == 2)
        //    {
        //        // 修改BEST
        //        MasterIndex = Convert.ToInt32(KeyArray[0]);
        //        DetailIndex = Convert.ToInt32(KeyArray[1]);
        //    }
        //    else
        //    {
        //        MasterIndex = -1;
        //        DetailIndex = -1;
        //    }
        //}
        #endregion

    }
}
