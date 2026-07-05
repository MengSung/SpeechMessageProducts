// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/UploadIntegrateData.AsyncWrapper.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class UploadIntegrateData、class UploadResult
// 主要成員：UploadDataAsync、WeeklyReportEntityId、WeeklyReportData、WeeklyReportAnalysis
// 引用命名空間：System、System.Threading、System.Threading.Tasks、ChurchReport.Models
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Models;

namespace ChurchReport.WebServiceConnector
{
    public partial class UploadIntegrateData
    {
        /// <summary>
        /// Minimal async wrapper that runs the existing synchronous UploadData on a thread-pool thread.
        /// Returns an UploadResult containing values that would otherwise be returned via ref parameters.
        /// </summary>
        public async Task<UploadResult> UploadDataAsync(
            DateTime aSelectedDate,
            string Account,
            string Password,
            string LoginType,
            string GroupType,
            string ListEntityId,
            DateTime aSmallGroupDate,
            SmallGroupData aSmallGroupData,
            string weeklyReportData,
            string weeklyReportAnalysis,
            string HappyWeekIndex,
            string HappyWeekTopic,
            bool PauseCheckBox,
            string currentWeeklyReportEntityId = null,
            CancellationToken cancellationToken = default)
        {
            // local copies for ref parameters
            var localWeeklyReportEntityId = !string.IsNullOrEmpty(currentWeeklyReportEntityId)
                ? currentWeeklyReportEntityId
                : (this.m_WeeklyReportEntity != null ? this.m_WeeklyReportEntity.Id.ToString() : string.Empty);
            var localWeeklyReportData = weeklyReportData;
            var localWeeklyReportAnalysis = weeklyReportAnalysis;

            await Task.Run(() =>
            {
                // cooperative cancellation
                cancellationToken.ThrowIfCancellationRequested();

                UploadData(
                    aSelectedDate,
                    Account,
                    Password,
                    LoginType,
                    GroupType,
                    ListEntityId,
                    ref localWeeklyReportEntityId,
                    aSmallGroupDate,
                    aSmallGroupData,
                    ref localWeeklyReportData,
                    ref localWeeklyReportAnalysis,
                    HappyWeekIndex,
                    HappyWeekTopic,
                    PauseCheckBox);

            }).ConfigureAwait(false);

            return new UploadResult
            {
                WeeklyReportEntityId = localWeeklyReportEntityId,
                WeeklyReportData = localWeeklyReportData,
                WeeklyReportAnalysis = localWeeklyReportAnalysis
            };
        }

        public class UploadResult
        {
            public string WeeklyReportEntityId { get; set; }
            public string WeeklyReportData { get; set; }
            public string WeeklyReportAnalysis { get; set; }
        }
    }
}
