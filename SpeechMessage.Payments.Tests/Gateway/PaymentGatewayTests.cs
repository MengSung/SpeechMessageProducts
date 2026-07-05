// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentGatewayTests、class StubProfileResolver、class FakePaymentProvider
// 主要成員：Create_payment_uses_default_profile_when_request_profile_is_empty、Create_payment_returns_configuration_error_when_provider_hint_mismatches_profile、Create_payment_returns_unsupported_operation_when_no_provider_matches_profile、Resolve、CreatePaymentAsync、QueryPaymentAsync、ParseCallbackAsync、ProviderKind、ReceivedProfileName
// 引用命名空間：FluentAssertions、SpeechMessage.Payments.Abstractions、SpeechMessage.Payments.Configuration、SpeechMessage.Payments.Gateway、SpeechMessage.Payments.Models、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Gateway;
using SpeechMessage.Payments.Models;
using Xunit;

namespace SpeechMessage.Payments.Tests.Gateway;

/// <summary>
/// 驗證 <see cref="PaymentGateway"/> 的 provider routing 行為。
/// Gateway 只根據 profile/provider hint 選擇 provider，不知道永豐、高鉅或台新的內部協定細節。
/// </summary>
public sealed class PaymentGatewayTests
{
    [Fact]
    public async Task Create_payment_uses_default_profile_when_request_profile_is_empty()
    {
        var provider = new FakePaymentProvider(PaymentProviderKind.MyPay);
        var gateway = new PaymentGateway(
            new StubProfileResolver(new PaymentMerchantProfile
            {
                Name = "DefaultMyPay",
                Provider = PaymentProviderKind.MyPay
            }),
            new[] { provider });

        await gateway.CreatePaymentAsync(new PaymentCreateRequest { ProductOrderId = "F1" });

        provider.ReceivedProfileName.Should().Be("DefaultMyPay");
    }

    [Fact]
    public async Task Create_payment_returns_configuration_error_when_provider_hint_mismatches_profile()
    {
        // provider hint 與 profile 不一致時必須回設定錯誤；
        // 這可以防止例如 route 明明是台新，卻誤用高鉅 profile 建立付款。
        var gateway = new PaymentGateway(
            new StubProfileResolver(new PaymentMerchantProfile
            {
                Name = "DefaultMyPay",
                Provider = PaymentProviderKind.MyPay
            }),
            new[] { new FakePaymentProvider(PaymentProviderKind.MyPay) });

        var result = await gateway.CreatePaymentAsync(new PaymentCreateRequest
        {
            ProviderHint = PaymentProviderKind.Sinopac,
            ProductOrderId = "F1"
        });

        result.Error.Kind.Should().Be(PaymentErrorKind.ConfigurationInvalid);
    }

    [Fact]
    public async Task Create_payment_returns_unsupported_operation_when_no_provider_matches_profile()
    {
        var gateway = new PaymentGateway(
            new StubProfileResolver(new PaymentMerchantProfile
            {
                Name = "SinopacProfile",
                Provider = PaymentProviderKind.Sinopac
            }),
            new[] { new FakePaymentProvider(PaymentProviderKind.MyPay) });

        var result = await gateway.CreatePaymentAsync(new PaymentCreateRequest
        {
            ProductOrderId = "F1"
        });

        result.Error.Kind.Should().Be(PaymentErrorKind.UnsupportedOperation);
    }

    private sealed class StubProfileResolver : IPaymentProfileResolver
    {
        // 測試用 resolver：只模擬 profile name resolution，不讀 appsettings，也不帶真實 credential。
        private readonly PaymentMerchantProfile _profile;

        public StubProfileResolver(PaymentMerchantProfile profile)
        {
            _profile = profile;
        }

        public PaymentMerchantProfile Resolve(string? profileName)
        {
            return _profile with { Name = string.IsNullOrWhiteSpace(profileName) ? _profile.Name : profileName };
        }
    }

    private sealed class FakePaymentProvider : IPaymentProvider
    {
        // 測試用 provider：只記錄收到的 profile name，避免 gateway routing 測試碰到真實 provider HTTP 呼叫。
        public FakePaymentProvider(PaymentProviderKind providerKind)
        {
            ProviderKind = providerKind;
        }

        public PaymentProviderKind ProviderKind { get; }
        public string ReceivedProfileName { get; private set; } = string.Empty;

        public Task<PaymentCreateResult> CreatePaymentAsync(
            PaymentMerchantProfile profile,
            PaymentCreateRequest request,
            CancellationToken cancellationToken)
        {
            ReceivedProfileName = profile.Name;
            return Task.FromResult(new PaymentCreateResult
            {
                Status = PaymentStatus.Pending,
                ProductOrderId = request.ProductOrderId,
                ProviderOrderRef = "fake-provider-order"
            });
        }

        public Task<PaymentStatusResult> QueryPaymentAsync(
            PaymentMerchantProfile profile,
            PaymentQueryRequest request,
            CancellationToken cancellationToken)
        {
            ReceivedProfileName = profile.Name;
            return Task.FromResult(new PaymentStatusResult
            {
                Status = PaymentStatus.Pending,
                ProductOrderId = request.ProductOrderId,
                ProviderOrderRef = request.ProviderOrderRef
            });
        }

        public Task<PaymentCallbackResult> ParseCallbackAsync(
            PaymentMerchantProfile profile,
            PaymentCallbackRequest request,
            CancellationToken cancellationToken)
        {
            ReceivedProfileName = profile.Name;
            return Task.FromResult(new PaymentCallbackResult
            {
                Status = PaymentStatus.Succeeded,
                ProductOrderId = request.Form.TryGetValue("order_id", out var orderId) ? orderId : string.Empty
            });
        }
    }
}
