using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

public sealed class PaymentAcknowledgementResultMapper
{
    public static IActionResult Map(PaymentCallbackAcknowledgement acknowledgement)
    {
        return acknowledgement.Kind switch
        {
            PaymentAckKind.PlainText => new ContentResult
            {
                Content = acknowledgement.Content,
                ContentType = "text/plain",
                StatusCode = acknowledgement.StatusCode
            },
            PaymentAckKind.Json => new ContentResult
            {
                Content = acknowledgement.Content,
                ContentType = "application/json",
                StatusCode = acknowledgement.StatusCode
            },
            PaymentAckKind.Redirect => new RedirectResult(acknowledgement.Content),
            _ => new StatusCodeResult(acknowledgement.StatusCode)
        };
    }

    public IActionResult ToActionResult(PaymentCallbackAcknowledgement acknowledgement)
    {
        return Map(acknowledgement);
    }
}
