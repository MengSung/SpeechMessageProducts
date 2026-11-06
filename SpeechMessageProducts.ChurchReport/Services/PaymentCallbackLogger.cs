/*
 * 檔案整體說明
 *
 * 目的與定位：記錄共用金流核心解析 callback 後的標準化結果。 此服務只負責 ChurchReport 產品層的診斷紀錄，不做 provider 狀態碼轉換、 簽章驗證或加解密；那些銀行/金流協定細節已經由 
 * SpeechMessage.Payments 負責。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentCallbackLogger。
 * - 寫入付款 callback 的核心欄位，方便後續客服查詢訂單與工程端追蹤 provider 回傳結果。 這裡刻意只記錄訂單號、交易號、狀態、錯誤種類與金額，避免把原始 callback payload、 token、
 * 簽章、卡號或其他敏感資訊寫入應用程式 log。
 * 主要操作入口包含 LogPaymentCallbackResult。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/MyPayControllerAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/PaymentProductServiceNamingTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs、SpeechMessageProducts.ChurchReport/Startup.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
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
