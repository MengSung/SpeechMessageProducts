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

            }, cancellationToken).ConfigureAwait(false);

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
