using System;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Payments;

/// <summary>
/// 舊 QPay 命名的產品流程派送介面。
/// 新程式應使用 <see cref="IDonationPaymentProductWorkflowDispatcher"/>；此介面只為舊測試、舊 DI 與過渡期相容而存在。
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
/// 舊 <c>QPayProductWorkflowDispatcher</c> 名稱的相容包裝。
/// 所有實際流程都在 <see cref="DonationPaymentProductWorkflowDispatcher"/>；此類別不可新增業務邏輯。
/// </summary>
[Obsolete("Use DonationPaymentProductWorkflowDispatcher. QPay naming is retained only for compatibility during the migration.")]
public sealed class QPayProductWorkflowDispatcher : IQPayProductWorkflowDispatcher
{
    private readonly IDonationPaymentProductWorkflowDispatcher _inner;

    public QPayProductWorkflowDispatcher()
        : this(new DonationPaymentProductWorkflowDispatcher())
    {
    }

    public QPayProductWorkflowDispatcher(IDonationPaymentProductWorkflowDispatcher inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

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