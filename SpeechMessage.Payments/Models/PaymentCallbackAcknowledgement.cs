namespace SpeechMessage.Payments.Models;

/// <summary>
/// provider callback 所需的回應描述。
/// 核心只描述「應回什麼」，實際 ASP.NET ContentResult/RedirectResult 由產品層轉換。
/// </summary>
public sealed record PaymentCallbackAcknowledgement
{
    public static PaymentCallbackAcknowledgement None { get; } = new();

    public PaymentAckKind Kind { get; init; } = PaymentAckKind.None;
    public string Content { get; init; } = string.Empty;
    public int StatusCode { get; init; } = 200;

    public static PaymentCallbackAcknowledgement PlainText(string content, int statusCode = 200)
    {
        return new PaymentCallbackAcknowledgement
        {
            Kind = PaymentAckKind.PlainText,
            Content = content,
            StatusCode = statusCode
        };
    }

    public static PaymentCallbackAcknowledgement Json(string content, int statusCode = 200)
    {
        return new PaymentCallbackAcknowledgement
        {
            Kind = PaymentAckKind.Json,
            Content = content,
            StatusCode = statusCode
        };
    }

    public static PaymentCallbackAcknowledgement Redirect(string url, int statusCode = 302)
    {
        return new PaymentCallbackAcknowledgement
        {
            Kind = PaymentAckKind.Redirect,
            Content = url,
            StatusCode = statusCode
        };
    }
}
