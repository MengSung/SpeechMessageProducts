// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/PaymentCrmService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class PaymentCrmService
// 主要成員：UpdateFeeEntityWithPaymentResult
// 引用命名空間：System、ChurchReport.Payments、Microsoft.Extensions.Logging、Microsoft.Xrm.Sdk、SpeechMessage.Payments.Workflows、ToolUtilityNameSpace
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using ChurchReport.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// 金流 callback 成功或失敗後，負責把共用金流層整理出的
    /// <see cref="PaymentWorkflowResult"/> 寫回 ChurchReport CRM 收費單。
    /// 這個服務屬於 ChurchReport 產品層，而不是金流核心；CRM 欄位名稱、option set 值、實收金額與描述文字格式都是 ChurchReport 自己的業務規則。
    /// </summary>
    public class PaymentCrmService
    {
        private const int PaymentStatusPaid = 100000001;
        private const int PaymentMethodCreditCard = 100000001;
        private readonly ILogger<PaymentCrmService> _logger;

        public PaymentCrmService(ILogger<PaymentCrmService> logger)
        {
            _logger = logger;
        }

        public void UpdateFeeEntityWithPaymentResult(
            ToolUtilityClass toolUtility,
            Entity feeEntity,
            PaymentWorkflowResult result,
            bool isSuccess)
        {
            try
            {
                var paymentTime = DateTime.Now;

                if (isSuccess)
                {
                    // 只有產品端知道 ChurchReport CRM 的付款狀態欄位與 option set 值；
                    // 共用金流專案只提供已正規化的付款結果，不直接更新 CRM。
                    var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PaymentStatusPaid);
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
                    toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PaymentMethodCreditCard);
                }

                // 將 provider-neutral 結果附加到 CRM 描述欄位，保留後續客服查核與對帳資訊。
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? string.Empty;
                var newDescription = originalDescription + Environment.NewLine +
                    $"[金流回傳資訊 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}]" + Environment.NewLine +
                    "====== 核心欄位 ======" + Environment.NewLine +
                    $"訂單號: {result.ProductOrderId}" + Environment.NewLine +
                    $"交易流水號: {result.ProviderTransactionId}" + Environment.NewLine +
                    $"交易狀態: {result.Status}" + Environment.NewLine +
                    "====== 交易資訊 ======" + Environment.NewLine +
                    $"完成時間: {paymentTime:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"交易金額: {result.Amount?.ToString() ?? string.Empty}" + Environment.NewLine +
                    $"交易幣別: {result.Currency}" + Environment.NewLine +
                    $"金流訊息: {result.ProviderMessage}" + Environment.NewLine;

                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
                _logger.LogInformation($"[付款回傳] 收費單欄位已更新 - FeeId: {feeEntity.Id}, OrderId: {result.ProductOrderId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[付款回傳] 更新收費單失敗 - OrderId: {result.ProductOrderId}");
                throw;
            }
        }
    }
}
