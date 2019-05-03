using ChurchReport.WebServiceConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class ListSmallGroupWeeklyReport
    {
        #region 初始化
        public ListSmallGroupWeeklyReport()
        {
            ModifyFlag = false;

            //m_WeeklyReportViewModel = new WeeklyReportViewModel
            // {
            //     WeeklyReportData = "AAAAA",
            //     WeeklyReportAnalysis = "BBBBBB"
            // };

    }
    #endregion
        #region 資料區
        #region 參數資料
        // 個別小組長點名的畫面所需要的資料
        public bool LoadFlag { get; set; }
        public String ListEntityId { get; set; } // 小組 ID

        public String WeeklyReportEntityId;//{ get; set; } // 週報 ID
        public String ListEntityName { get; set; } // 小組名稱
        public String LoginType { get; set; } // 回報型式: 小組長 OR 個人回報
        public String SmallGroupLeaderContactId { get; set; } // 小組長 ID
        public String SmallGroupLeaderFullName { get; set; } // 小組長姓名
        public DateTime SundayPrayers { get; set; } // 小組日期
        public String SundayPeriod { get; set; }   // 提醒小組長回報的期間
        public SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();// 包含 3 個SmallGroupData ( 小組牧養、新人跟進關懷、基本資料維護)，而每個又包含一個Members陣列
        public String WeeklyReportData; //{ get; set; } // 小組日誌
        public String WeeklyReportAnalysis; //{ get; set; } // 小組分析報告
        // 長條圖表資料
        public ChartDataList m_WeeklyReportChart { get; set; }
        public bool ModifyFlag { get; set; }
        public UploadIntegrateData m_UploadIntegrateData = new UploadIntegrateData();

        //public WeeklyReportViewModel m_WeeklyReportViewModel { get; set; }
        //public WeeklyReportViewModel m_WeeklyReportViewModel = new WeeklyReportViewModel
        //{
        //    WeeklyReportData = "AAAAA",
        //    WeeklyReportAnalysis = "BBBBBB"
        //};

        #endregion
        #endregion
        public void UploadIntegrateData(String Account, String Password, SmallGroupData aSmallGroupData , String aWeeklyReportData)
        {
            //if (aSmallGroupData.ModifyFlag == true)
            {
                DateTime aUploadSmallGroupDate = new DateTime(SundayPrayers.Year, SundayPrayers.Month, SundayPrayers.Day, 0, 0, 0);

                WeeklyReportData = aWeeklyReportData;
                m_UploadIntegrateData.UploadData( Account, Password, ListEntityId, ref WeeklyReportEntityId, SundayPrayers, aSmallGroupData, ref WeeklyReportData, ref WeeklyReportAnalysis );

            }
        }
    }

    //public class WeeklyReportViewModel
    //{
    //    public String WeeklyReportData { get; set; }
    //    public String WeeklyReportAnalysis { get; set; }
    //    public bool DisplayFlag { get; set; }

    //    //static public WeeklyReportData m_WeeklyReportData = new WeeklyReportData();
    //}

}
