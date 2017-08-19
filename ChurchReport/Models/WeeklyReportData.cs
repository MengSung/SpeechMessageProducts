using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.ViewModels;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.WebServiceConnector;

namespace ChurchReport.Models
{
    static public class WeeklyReportData
    {
        static public WeeklyReportViewModel m_WeeklyReportViewModel = new WeeklyReportViewModel
        {
            WeeklyReportData = "耶和華必拯救",
            WeeklyReportAnalysis = "我愛嘟嘟扭扭"
        };

        static public WeeklyReport m_WeeklyReport;

        public static void SetupWeeklyReport(String FullName, String Account, String Password, DateTime SundayDate)
        {
            WeeklyReportManager aWeeklyReportManager = new WeeklyReportManager();

            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = SmallGroupDataList.m_Account,
                Password = SmallGroupDataList.m_Password
            };

            // 從雲端後台下載下來小組點名資料
            m_WeeklyReport = aWeeklyReportManager.DownloadWeeklyReport(aAccountPasswordData, SmallGroupDataList.m_SundayDate);


        }


    }
}
