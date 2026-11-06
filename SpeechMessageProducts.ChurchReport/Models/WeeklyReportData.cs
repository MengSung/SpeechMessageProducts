/*
 * 檔案整體說明
 *
 * 目的與定位：協調指定小組與週次的週報下載及上傳，將WeeklyReportManager取得的資料放入目前回報模型；日期變動應重新取得對應週報，不得把舊週次資料視為新週次成果。
 *
 * 主要功能與流程：
 * 本檔宣告 WeeklyReportData。
 * 主要操作入口包含 SetupWeeklyReport、UploadWeeklyReport。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs、
 * SpeechMessageProducts.ChurchReport/Models/IInMemoryDataContext.cs、
 * SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListManager.cs 追查目前工作樹中的合作／使用位置。
 */
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
