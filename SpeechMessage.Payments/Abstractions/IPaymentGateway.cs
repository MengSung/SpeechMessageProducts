using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 金流通用核心對外公開的主要入口。
/// 宿主產品與未來其他產品只能透過這個 provider-neutral 介面建立付款、
/// 查詢付款狀態與解析回呼；不得直接依賴永豐、高鉅、台新自己的 SDK 或封包格式。
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken = default);
}
