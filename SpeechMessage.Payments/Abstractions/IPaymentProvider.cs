/*
 * 檔案整體說明
 *
 * 目的與定位：單一金流 provider 的內部合約。 這個介面刻意維持 internal，避免宿主產品繞過 IPaymentGateway 直接呼叫 provider 實作，造成抽象邊界外漏。
 *
 * 主要功能與流程：
 * 本檔宣告 IPaymentProvider。
 * 資料／選項欄位包含 IPaymentProvider，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs、
 * SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
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
