// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LinePayCSharp/Models/ReserveInfo.cs
// 所屬區塊：LINE Pay C# 整合封裝，處理付款 API 模型與呼叫。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class ReserveInfo
// 主要成員：TransactionId、PaymentUrl、PaymentAccessToken
// 引用命名空間：Newtonsoft.Json、System
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;
using System;

namespace Line.Pay.Models
{
    /// <summary>
    /// Reservation Information
    /// </summary>
    public class ReserveInfo
    {
        /// <summary>
        /// Transaction number (19 digits)
        /// </summary>
        [JsonProperty("transactionId")]
        public Int64 TransactionId { get; set; }

        /// <summary>
        /// URL to go to after payment request
        /// </summary>
        [JsonProperty("paymentUrl")]
        public PaymentUrl PaymentUrl { get; set; }

        /// <summary>
        /// Code Value (12 digits) using for input a code instead of using a Scanner on LINE app.
        /// (Supports a Scanner of LINE Pay app above LINE 5.1 Version)
        /// </summary>
        [JsonProperty("paymentAccessToken")]
        public string PaymentAccessToken { get; set; }
    }
}
