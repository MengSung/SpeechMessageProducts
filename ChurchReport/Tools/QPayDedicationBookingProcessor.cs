using ChurchReport.Payments;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Tools;

/// <summary>
/// 舊 <c>QPayDedicationBookingProcessor</c> 名稱的相容包裝。
/// 真正的定期定額奉獻付款完成後流程已移到 <see cref="RecurringDonationPaymentProcessor"/>；
/// 此類別只能保留舊方法名稱轉交，不能再加入 CRM、LINE 或付款結果分支邏輯。
/// </summary>
[System.Obsolete("Use RecurringDonationPaymentProcessor. QPayDedicationBookingProcessor is retained only as a compatibility alias during migration.")]
public class QPayDedicationBookingProcessor : RecurringDonationPaymentProcessor
{
    /// <summary>
    /// 舊程式無參數建立處理器時的相容入口。
    /// </summary>
    public QPayDedicationBookingProcessor()
        : base()
    {
    }

    /// <summary>
    /// 舊方法名稱的相容入口；所有實際流程都轉交給中性的 <see cref="RecurringDonationPaymentProcessor.HandlePaymentReturn"/>。
    /// </summary>
    public ActionResult QPayDedicationBookingProcessorReturnUrl(
        string ShopNo,
        string PayToken,
        QPayWorkflowPaymentResult paymentResult,
        string correlationId = "",
        string requestContext = "")
    {
        return HandlePaymentReturn(ShopNo, PayToken, paymentResult, correlationId, requestContext);
    }
}