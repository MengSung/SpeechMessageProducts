/*
 * 檔案整體說明
 *
 * 目的與定位：將舊同步UploadData封裝為工作池上的非同步操作，並把原ref輸出收斂到UploadResult；這是相容包裝而非可中斷的原生非同步I/O，背景作業必須使用已複製的輸入並由外層追蹤排空。
 *
 * 主要功能與流程：
 * 本檔宣告 UploadIntegrateData、UploadResult。
 * 主要操作入口包含 UploadDataAsync。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
