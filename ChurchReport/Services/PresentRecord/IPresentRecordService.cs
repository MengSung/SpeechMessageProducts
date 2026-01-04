using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;

namespace ChurchReport.Services.PresentRecord
{
    /// <summary>
    /// 出席記錄服務介面
    /// 負責處理個人聚會與靈修記錄的建立、更新等操作
    /// </summary>
    public interface IPresentRecordService
    {
        /// <summary>
        /// 為新聯絡人建立出席記錄
        /// </summary>
        /// <param name="listEntity">小組名單實體</param>
        /// <param name="contactId">聯絡人 ID</param>
        /// <param name="groupName">小組名稱</param>
        /// <returns>建立的出席記錄 ID</returns>
        Task<Guid?> CreatePresentRecordAsync(Entity listEntity, Guid contactId, string groupName);

        /// <summary>
        /// 取得指定聯絡人在特定週報中的出席記錄
        /// </summary>
        /// <param name="contactId">聯絡人 ID</param>
        /// <param name="weeklyReportId">週報 ID</param>
        /// <returns>出席記錄集合</returns>
        EntityCollection GetPresentRecordsByContact(Guid contactId, Guid weeklyReportId);

        /// <summary>
        /// 設定出席記錄的"停止提醒"標記
        /// </summary>
        /// <param name="contact">聯絡人實體</param>
        /// <returns>是否成功</returns>
        Task<bool> SetNotRemindFlagAsync(Entity contact);

        /// <summary>
        /// 取得聯絡人在過去 N 週的出席次數
        /// </summary>
        /// <param name="contactId">聯絡人 ID</param>
        /// <param name="weeklyReportId">週報 ID</param>
        /// <param name="weekPeriod">週期（幾週內）</param>
        /// <param name="attendanceType">出席類型（主日/小組）</param>
        /// <returns>出席次數</returns>
        int GetPresentNumber(Guid contactId, Guid weeklyReportId, int weekPeriod, string attendanceType);
    }
}
