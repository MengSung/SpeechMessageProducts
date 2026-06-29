using System;
using ChurchReport.Tools;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 產品層付款回傳流程派送器。
/// 它只負責把已標準化的付款結果分派給 ChurchReport 內部的收費單或定期定額奉獻處理器；
/// CRM 更新、LINE 通知與結果頁面仍留在 ChurchReport 產品層，不進入可重用金流核心。
/// </summary>
public interface IDonationPaymentProductWorkflowDispatcher
{
    IActionResult HandleFeeReturn(
        string shopNo,
        string payToken,
        DonationPaymentWorkflowResult paymentResult);

    IActionResult HandleDedicationBookingReturn(
        string shopNo,
        string payToken,
        DonationPaymentWorkflowResult paymentResult);
}

/// <summary>
/// ChurchReport 付款完成後的產品流程派送器主實作。
/// 主實作使用中性的 Donation/Payment 名稱，讓未來其他 ASP.NET Core 產品能清楚看出：
/// 金流核心只回傳付款狀態，實際 CRM/LINE/頁面流程由各產品自己的 workflow 接手。
/// </summary>
public sealed class DonationPaymentProductWorkflowDispatcher : IDonationPaymentProductWorkflowDispatcher
{
    public IActionResult HandleFeeReturn(
        string shopNo,
        string payToken,
        DonationPaymentWorkflowResult paymentResult)
    {
        ArgumentNullException.ThrowIfNull(paymentResult);

        using var processor = new DonationFeePaymentProcessor();
        return processor.HandlePaymentReturn(
            shopNo,
            payToken,
            paymentResult);
    }

    public IActionResult HandleDedicationBookingReturn(
        string shopNo,
        string payToken,
        DonationPaymentWorkflowResult paymentResult)
    {
        ArgumentNullException.ThrowIfNull(paymentResult);

        using var processor = new RecurringDonationPaymentProcessor();
        return processor.HandlePaymentReturn(
            shopNo,
            payToken,
            paymentResult);
    }
}