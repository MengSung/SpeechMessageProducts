using System;
using ChurchReport.Tools;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 付款完成後的產品流程派送介面。
/// 它只判斷付款結果要交給哪一種 ChurchReport 業務處理器，例如費用單或定期定額奉獻；
/// provider callback parsing、簽章驗證與查詢付款狀態都應該在 SpeechMessage.Payments 完成。
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
/// 舊 QPay 命名的產品流程派送介面。
/// 這個介面只保留給尚未改名的建構子、測試與 DI 註冊相容使用；新程式應依賴
/// <see cref="IDonationPaymentProductWorkflowDispatcher"/>。
/// </summary>
[Obsolete("Use IDonationPaymentProductWorkflowDispatcher. QPay naming is retained only for compatibility during the migration.")]
public interface IQPayProductWorkflowDispatcher
{
    IActionResult HandleFeeReturn(
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult paymentResult);

    IActionResult HandleDedicationBookingReturn(
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult paymentResult);
}

/// <summary>
/// ChurchReport 產品流程派送器。
/// 目前底層處理器仍是舊的 QPayFeeProcessor/QPayDedicationBookingProcessor，因此這裡負責做一次
/// 中性 DTO 到舊 DTO 的薄轉接；業務判斷仍集中在這個派送點，避免散落到 Controller 或 provider core。
/// </summary>
public sealed class DonationPaymentProductWorkflowDispatcher :
    IDonationPaymentProductWorkflowDispatcher,
    IQPayProductWorkflowDispatcher
{
    public IActionResult HandleFeeReturn(
        string shopNo,
        string payToken,
        DonationPaymentWorkflowResult paymentResult)
    {
        ArgumentNullException.ThrowIfNull(paymentResult);

        using var processor = new QPayFeeProcessor();
        return processor.QPayFeeProcessorReturnUrl(
            shopNo,
            payToken,
            ToLegacyResult(paymentResult));
    }

    IActionResult IQPayProductWorkflowDispatcher.HandleFeeReturn(
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult paymentResult)
    {
        return HandleFeeReturn(shopNo, payToken, paymentResult);
    }

    public IActionResult HandleDedicationBookingReturn(
        string shopNo,
        string payToken,
        DonationPaymentWorkflowResult paymentResult)
    {
        ArgumentNullException.ThrowIfNull(paymentResult);

        using var processor = new QPayDedicationBookingProcessor();
        return processor.QPayDedicationBookingProcessorReturnUrl(
            shopNo,
            payToken,
            ToLegacyResult(paymentResult));
    }

    IActionResult IQPayProductWorkflowDispatcher.HandleDedicationBookingReturn(
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult paymentResult)
    {
        return HandleDedicationBookingReturn(shopNo, payToken, paymentResult);
    }

    private static QPayWorkflowPaymentResult ToLegacyResult(DonationPaymentWorkflowResult result)
    {
        return result is QPayWorkflowPaymentResult legacyResult
            ? legacyResult
            : new QPayWorkflowPaymentResult
            {
                ShopNo = result.ShopNo,
                PayToken = result.PayToken,
                OrderNo = result.OrderNo,
                ProviderTransactionId = result.ProviderTransactionId,
                Amount = result.Amount,
                AmountMinorUnits = result.AmountMinorUnits,
                ProductEntityId = result.ProductEntityId,
                PaymentOrganization = result.PaymentOrganization,
                PaymentCategory = result.PaymentCategory,
                PayType = result.PayType,
                Status = result.Status,
                Description = result.Description,
                LeftCCNo = result.LeftCCNo,
                RightCCNo = result.RightCCNo,
                CCExpDate = result.CCExpDate,
                CCToken = result.CCToken,
                ProviderData = result.ProviderData
            };
    }
}

/// <summary>
/// 舊類別名稱的相容外殼。
/// 所有實際流程都在 <see cref="DonationPaymentProductWorkflowDispatcher"/>；此類別不可新增業務邏輯。
/// </summary>
[Obsolete("Use DonationPaymentProductWorkflowDispatcher. QPay naming is retained only for compatibility during the migration.")]
public sealed class QPayProductWorkflowDispatcher : IQPayProductWorkflowDispatcher
{
    private readonly DonationPaymentProductWorkflowDispatcher _inner = new();

    public IActionResult HandleFeeReturn(
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult paymentResult)
    {
        return _inner.HandleFeeReturn(shopNo, payToken, paymentResult);
    }

    public IActionResult HandleDedicationBookingReturn(
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult paymentResult)
    {
        return _inner.HandleDedicationBookingReturn(shopNo, payToken, paymentResult);
    }
}
