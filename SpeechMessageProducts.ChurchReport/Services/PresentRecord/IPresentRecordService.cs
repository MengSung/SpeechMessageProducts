// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/PresentRecord/IPresentRecordService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IPresentRecordService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：ChurchReport.Models.CrmTransmitModule、Microsoft.Xrm.Sdk、System、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
