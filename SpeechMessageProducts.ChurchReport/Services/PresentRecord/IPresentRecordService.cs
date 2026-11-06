/*
 * 檔案整體說明
 *
 * 目的與定位：出席記錄服務介面 負責處理個人聚會與靈修記錄的建立、更新等操作
 *
 * 主要功能與流程：
 * 本檔宣告 IPresentRecordService。
 * 資料／選項欄位包含 IPresentRecordService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs 追查目前工作樹中的合作／使用位置。
 */
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
