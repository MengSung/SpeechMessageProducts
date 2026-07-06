// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/FollowUp/IFollowUpService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IFollowUpService、class FollowUpInfo
// 主要成員：CurrentWeek、HistoryReport、Gender、FirstChurchDate、WelcomeRecord、IdentityType
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
