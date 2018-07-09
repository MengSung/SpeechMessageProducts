using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.ViewModels;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.WebServiceConnector;

namespace ChurchReport.Models
{
    public class WeeklyReportData
    {
        public WeeklyReportViewModel m_WeeklyReportViewModel = new WeeklyReportViewModel
        {
            WeeklyReportData = "耶和華必拯救",
            WeeklyReportAnalysis = "我愛嘟嘟扭扭"
        };

        public WeeklyReport m_WeeklyReport = new WeeklyReport();

        public void SetupWeeklyReport( String Account, String Password, DateTime SundayDate )
        {
            WeeklyReportManager aWeeklyReportManager = new WeeklyReportManager();

            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = Account,
                Password = Password
            };

            // 從雲端後台下載下來小組日誌
            m_WeeklyReport = aWeeklyReportManager.DownloadWeeklyReport(aAccountPasswordData, SundayDate);


            m_WeeklyReportViewModel.WeeklyReportData = m_WeeklyReport.WeeklyReportContent;
            m_WeeklyReportViewModel.WeeklyReportAnalysis = m_WeeklyReport.PresentContent;
            m_WeeklyReportViewModel.DisplayFlag = false;

        }

        public void UploadWeeklyReport(String Account, String Password, DateTime SundayDate, WeeklyReport aWeeklyReport)
        {
            WeeklyReportManager aWeeklyReportManager = new WeeklyReportManager();

            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = Account,
                Password = Password
            };

            // 上傳小組日誌到雲端後台
            m_WeeklyReport = aWeeklyReportManager.UploadWeeklyReport(aAccountPasswordData, SundayDate, aWeeklyReport );



        }

    }



}
