using System;
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// 舊 QPay 命名的 return workflow 介面。
/// 新程式應使用 <see cref="IDonationPaymentReturnWorkflow"/>；此介面只為舊 controller 與舊測試相容而存在。
/// </summary>
[Obsolete("Use IDonationPaymentReturnWorkflow. QPay naming is retained only for compatibility during the migration.")]
public interface IQPayReturnWorkflow : IDonationPaymentReturnWorkflow
{
}

/// <summary>
/// 舊 <c>QPayReturnWorkflow</c> 名稱的相容包裝。
/// 所有實際付款回傳流程都在 <see cref="DonationPaymentReturnWorkflow"/>；此類別不可新增業務邏輯。
/// </summary>
[Obsolete("Use DonationPaymentReturnWorkflow. QPay naming is retained only for compatibility during the migration.")]
public sealed class QPayReturnWorkflow : IQPayReturnWorkflow
{
    private readonly DonationPaymentReturnWorkflow _inner;

    public QPayReturnWorkflow(IQPayProductWorkflowDispatcher? productWorkflowDispatcher = null)
    {
        _inner = new DonationPaymentReturnWorkflow(
            productWorkflowDispatcher == null
                ? null
                : new LegacyProductWorkflowDispatcherAdapter(productWorkflowDispatcher));
    }

    public IActionResult HandleReturn(
        string shopNo,
        string payToken,
        PaymentStatusResult statusResult)
    {
        return _inner.HandleReturn(shopNo, payToken, statusResult);
    }

    private sealed class LegacyProductWorkflowDispatcherAdapter : IDonationPaymentProductWorkflowDispatcher
    {
        private readonly IQPayProductWorkflowDispatcher _legacyDispatcher;

        public LegacyProductWorkflowDispatcherAdapter(IQPayProductWorkflowDispatcher legacyDispatcher)
        {
            _legacyDispatcher = legacyDispatcher ?? throw new ArgumentNullException(nameof(legacyDispatcher));
        }

        public IActionResult HandleFeeReturn(
            string shopNo,
            string payToken,
            DonationPaymentWorkflowResult paymentResult)
        {
            return _legacyDispatcher.HandleFeeReturn(
                shopNo,
                payToken,
                ToLegacyResult(paymentResult));
        }

        public IActionResult HandleDedicationBookingReturn(
            string shopNo,
            string payToken,
            DonationPaymentWorkflowResult paymentResult)
        {
            return _legacyDispatcher.HandleDedicationBookingReturn(
                shopNo,
                payToken,
                ToLegacyResult(paymentResult));
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
}