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

        public static void SetupWeeklyReport()
        {
            WeeklyReportManager aWeeklyReportManager = new WeeklyReportManager();

            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = SmallGroupDataList.m_Account,
                Password = SmallGroupDataList.m_Password
            };

            // 從雲端後台下載下來小組日誌
            m_WeeklyReport = aWeeklyReportManager.DownloadWeeklyReport(aAccountPasswordData, SmallGroupDataList.m_SundayDate);


            m_WeeklyReportViewModel.WeeklyReportData = m_WeeklyReport.WeeklyReportContent;
            m_WeeklyReportViewModel.WeeklyReportAnalysis = m_WeeklyReport.PresentContent;

        }

        public static void UploadWeeklyReport()
        {
            WeeklyReportManager aWeeklyReportManager = new WeeklyReportManager();

            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = SmallGroupDataList.m_Account,
                Password = SmallGroupDataList.m_Password
            };

            // 從雲端後台下載下來小組日誌
            m_WeeklyReport = aWeeklyReportManager.UploadWeeklyReport(aAccountPasswordData, SmallGroupDataList.m_SundayDate, m_WeeklyReport);



        }

    }



}
