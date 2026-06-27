using ChurchReport.Tools;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Payments;

/// <summary>
/// 將 QPay return 結果派送到 ChurchReport 既有費用或定期奉獻 workflow。
/// 這個介面存在的原因是隔離產品流程，讓金流核心不用引用 <c>QPayFeeProcessor</c> 或 CRM/LINE 類別。
/// </summary>
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
/// ChurchReport 端的 QPay 產品流程 dispatcher。
/// 它只負責呼叫既有 processor；provider status normalization 已在 <c>SpeechMessage.Payments</c> 完成。
/// </summary>
public sealed class QPayProductWorkflowDispatcher : IQPayProductWorkflowDispatcher
{
    public IActionResult HandleFeeReturn(
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult paymentResult)
    {
        // QPayFeeProcessor 仍擁有 ChurchReport 費用 entity 更新與結果頁邏輯，因此不能搬進通用金流核心。
        using var processor = new QPayFeeProcessor();
        return processor.QPayFeeProcessorReturnUrl(shopNo, payToken, paymentResult);
    }

    public IActionResult HandleDedicationBookingReturn(
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult paymentResult)
    {
        // 定期奉獻/認獻預約是 ChurchReport 產品概念，不是金流 provider protocol。
        using var processor = new QPayDedicationBookingProcessor();
        return processor.QPayDedicationBookingProcessorReturnUrl(shopNo, payToken, paymentResult);
    }
}
