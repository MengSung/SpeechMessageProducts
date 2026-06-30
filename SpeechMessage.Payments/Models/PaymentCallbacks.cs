namespace SpeechMessage.Payments.Models;

/// <summary>
/// 付款 provider 需要使用的回呼網址集合。
/// 網址由產品層提供，避免 reusable core 綁定宿主產品的部署網域。
/// </summary>
public sealed record PaymentCallbacks
{
    public string ReturnUrl { get; init; } = string.Empty;
    public string BackendUrl { get; init; } = string.Empty;
    public string SuccessUrl { get; init; } = string.Empty;
    public string FailureUrl { get; init; } = string.Empty;
}
