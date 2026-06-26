using ChurchReport.Tools;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Payments;

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

public sealed class QPayProductWorkflowDispatcher : IQPayProductWorkflowDispatcher
{
    public IActionResult HandleFeeReturn(
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult paymentResult)
    {
        using var processor = new QPayFeeProcessor();
        return processor.QPayFeeProcessorReturnUrl(shopNo, payToken, paymentResult);
    }

    public IActionResult HandleDedicationBookingReturn(
        string shopNo,
        string payToken,
        QPayWorkflowPaymentResult paymentResult)
    {
        using var processor = new QPayDedicationBookingProcessor();
        return processor.QPayDedicationBookingProcessorReturnUrl(shopNo, payToken, paymentResult);
    }
}
