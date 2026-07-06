// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/PaymentCallbackLogger.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class PaymentCallbackLogger
// 主要成員：LogPaymentCallbackResult
// 引用命名空間：System、Microsoft.Extensions.Logging、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Extensions.Logging;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Services
{
    /// <summary>
    /// 記錄共用金流核心解析 callback 後的標準化結果。
    /// 此服務只負責 ChurchReport 產品層的診斷紀錄，不做 provider 狀態碼轉換、
    /// 簽章驗證或加解密；那些銀行/金流協定細節已經由 <c>SpeechMessage.Payments</c> 負責。
    /// </summary>
    public class PaymentCallbackLogger
    {
        private readonly ILogger<PaymentCallbackLogger> _logger;

        public PaymentCallbackLogger(ILogger<PaymentCallbackLogger> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 寫入付款 callback 的核心欄位，方便後續客服查詢訂單與工程端追蹤 provider 回傳結果。
        /// 這裡刻意只記錄訂單號、交易號、狀態、錯誤種類與金額，避免把原始 callback payload、
        /// token、簽章、卡號或其他敏感資訊寫入應用程式 log。
        /// </summary>
        public void LogPaymentCallbackResult(PaymentCallbackResult result)
        {
            try
            {
                _logger.LogInformation(
                    "[付款回傳] Core callback result: OrderId={OrderId}, TransactionId={TransactionId}, Status={Status}, Error={ErrorKind}, Amount={Amount}",
                    result.ProductOrderId,
                    result.ProviderTransactionId,
                    result.Status,
                    result.Error.Kind,
                    result.Amount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[付款回傳] 記錄 core callback result 時發生錯誤");
            }
        }
    }
}
