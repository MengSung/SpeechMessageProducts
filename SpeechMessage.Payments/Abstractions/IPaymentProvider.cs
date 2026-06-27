using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 單一金流 provider 的內部合約。
/// 這個介面刻意維持 internal，避免宿主產品繞過
/// <see cref="IPaymentGateway"/> 直接呼叫 provider 實作，造成抽象邊界外漏。
/// </summary>
internal interface IPaymentProvider
{
    PaymentProviderKind ProviderKind { get; }

    Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request,
        CancellationToken cancellationToken);

    Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentMerchantProfile profile,
        PaymentQueryRequest request,
        CancellationToken cancellationToken);

    Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentMerchantProfile profile,
        PaymentCallbackRequest request,
        CancellationToken cancellationToken);
}
