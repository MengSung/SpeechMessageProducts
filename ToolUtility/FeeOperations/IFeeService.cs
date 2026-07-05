// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/FeeOperations/IFeeService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IFeeService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：Microsoft.Xrm.Sdk、System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtilityNameSpace.FeeOperations
{
    public interface IFeeService
    {
        EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod);
        EntityCollection RetrieveDedicationBooking(string contactName, string contactId);

        /// <summary>
        /// 根據連絡人查詢奉獻收費單
        /// </summary>
        EntityCollection RetrieveDedicationFee(string contactName, string contactId);

        /// <summary>
        /// 根據連絡人和日期範圍查詢奉獻收費單
        /// </summary>
        EntityCollection RetrieveDedicationFeeByDateRange(string contactName, string contactId, DateTime startDate, DateTime endDate);
    }
}