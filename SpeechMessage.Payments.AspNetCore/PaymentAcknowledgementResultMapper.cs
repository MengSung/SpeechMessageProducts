using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.AspNetCore;

/// <summary>
/// Converts payment-core acknowledgement descriptors into ASP.NET MVC results.
/// Provider acknowledgement rules remain in <c>SpeechMessage.Payments</c>; this
/// class only performs the host framework response mapping.
/// </summary>
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
