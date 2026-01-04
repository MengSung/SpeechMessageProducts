using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;

namespace ChurchReport.Services.FollowUp
{
    /// <summary>
    /// 跟進服務介面
    /// 負責處理新人跟進、關懷歷程記錄等操作
    /// </summary>
    public interface IFollowUpService
    {
        /// <summary>
        /// 取得新人跟進資訊
        /// </summary>
        /// <param name="contactId">聯絡人 ID</param>
        /// <returns>跟進資訊</returns>
        Task<FollowUpInfo> GetFollowUpInfoAsync(Guid contactId);

        /// <summary>
        /// 驗證聯絡人是否為新人或未入組
        /// </summary>
        /// <param name="contact">聯絡人實體</param>
        /// <returns>true: 是新人/未入組, false: 否</returns>
        bool IsNewComer(Entity contact);

        /// <summary>
        /// 設定聯絡人的關懷週次
        /// </summary>
        /// <param name="presentRecordId">出席記錄 ID</param>
        /// <param name="weekNumber">週次編號 (1-20)</param>
        /// <returns>是否成功</returns>
        Task<bool> SetFollowUpWeekAsync(Guid presentRecordId, int weekNumber);

        /// <summary>
        /// 轉換委身類型（新朋友 → 未入組 → 未入組結案）
        /// </summary>
        /// <param name="contact">聯絡人實體</param>
        /// <param name="weekCounter">當前週次計數</param>
        /// <returns>是否有轉換發生</returns>
        Task<bool> TransferIdentityAsync(Entity contact, int weekCounter);
    }

    /// <summary>
    /// 跟進資訊
    /// </summary>
    public class FollowUpInfo
    {
        /// <summary>
        /// 當前週次（中文）
        /// </summary>
        public string CurrentWeek { get; set; }

        /// <summary>
        /// 歷程記錄報告
        /// </summary>
        public string HistoryReport { get; set; }

        /// <summary>
        /// 性別
        /// </summary>
        public string Gender { get; set; }

        /// <summary>
        /// 首次進入教會日期
        /// </summary>
        public DateTime? FirstChurchDate { get; set; }

        /// <summary>
        /// 歡迎記錄
        /// </summary>
        public string WelcomeRecord { get; set; }

        /// <summary>
        /// 委身類型（新朋友/未入組/小組組員）
        /// </summary>
        public string IdentityType { get; set; }
    }
}
