// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/WeeklyReportData.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class WeeklyReportData
// 主要成員：SetupWeeklyReport、UploadWeeklyReport
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks、ChurchReport.ViewModels、ChurchReport.Models.CrmTransmitModule、ChurchReport.WebServiceConnector
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
