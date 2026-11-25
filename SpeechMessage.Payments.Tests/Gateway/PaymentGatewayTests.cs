/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 PaymentGateway 的 provider routing 行為。 Gateway 只根據 profile/provider hint 選擇 provider，不知道永豐、高鉅或台新的內部協定細節。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentGatewayTests、StubProfileResolver、FakePaymentProvider。
 * 測試案例入口包含 Create_payment_uses_default_profile_when_request_profile_is_empty、
 * Create_payment_returns_configuration_error_when_provider_hint_mismatches_profile、
 * Create_payment_returns_unsupported_operation_when_no_provider_matches_profile、Resolve、CreatePaymentAsync、
 * QueryPaymentAsync、ParseCallbackAsync、MyPay_CardBind_NotSupported、
 * Create_recurring_rejects_unsupported_currency_before_provider_io。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using FluentAssertions;
using SpeechMessage.Localization;
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

    /// <summary>
    /// 保護沒有綁卡 capability 的 MyPay profile 以 UnsupportedOperation 失敗，且不呼叫任何 provider I/O。
    /// 測試 provider 只記錄 profile routing，不建立 HTTP 或背景資源。
    /// </summary>
    [Fact]
    public async Task MyPay_CardBind_NotSupported()
    {
        var provider = new FakePaymentProvider(PaymentProviderKind.MyPay);
        var gateway = new PaymentGateway(
            new StubProfileResolver(new PaymentMerchantProfile
            {
                Name = "SyntheticMyPay",
                Provider = PaymentProviderKind.MyPay
            }),
            new[] { provider });

        var result = await gateway.CreateCardBindAsync(new CardBindRequest
        {
            ProfileName = "SyntheticMyPay",
            BindNo = "SYNTHETIC-BIND-001",
            ReturnUrl = "https://example.test/bind-return"
        });

        result.Status.Should().Be(PaymentStatus.Unknown);
        result.Error.Kind.Should().Be(PaymentErrorKind.UnsupportedOperation);
        provider.ReceivedProfileName.Should().BeEmpty();
    }

    /// <summary>
    /// 保護定期扣款入口與一般建單使用相同的 provider 幣別邊界；
    /// 合成 provider 記錄 capability 呼叫次數，HKD 請求必須於任何 provider 操作前拒絕。
    /// </summary>
    [Fact]
    public async Task Create_recurring_rejects_unsupported_currency_before_provider_io()
    {
        var provider = new FakeRecurringProvider();
        var gateway = new PaymentGateway(
            new StubProfileResolver(new PaymentMerchantProfile
            {
                Name = "SyntheticSinopac",
                Provider = PaymentProviderKind.Sinopac
            }),
            new[] { provider });

        var result = await gateway.CreateRecurringAsync(new PaymentCreateRequest
        {
            ProfileName = "SyntheticSinopac",
            ProductOrderId = "SYNTHETIC-REGULAR-001",
            Total = new Money(1200m, "HKD")
        });

        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Kind.Should().Be(PaymentErrorKind.RequestInvalid);
        provider.RecurringCreateCalls.Should().Be(0);
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
        private static readonly IReadOnlyCollection<string> CurrencyCodes = Array.AsReadOnly(new[] { "TWD" });

        public FakePaymentProvider(PaymentProviderKind providerKind)
        {
            ProviderKind = providerKind;
        }

        public PaymentProviderKind ProviderKind { get; }

        /// <summary>測試替身只宣告 TWD，與專案內三家 provider 的建立能力一致。</summary>
        public IReadOnlyCollection<string> SupportedCurrencies => CurrencyCodes;
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

    /// <summary>
    /// 只用於 Gateway 幣別守門測試的 provider/capability 替身；不建立網路或背景資源，
    /// 以呼叫計數確認非法幣別在 capability 執行前已被拒絕。
    /// </summary>
    private sealed class FakeRecurringProvider : IPaymentProvider, IPaymentRecurring
    {
        private static readonly IReadOnlyCollection<string> CurrencyCodes =
            Array.AsReadOnly(new[] { "TWD" });

        /// <summary>取得 Gateway 實際呼叫定期建立 capability 的次數。</summary>
        public int RecurringCreateCalls { get; private set; }

        /// <summary>宣告替身 provider 只接受 TWD。</summary>
        public PaymentProviderKind ProviderKind => PaymentProviderKind.Sinopac;

        /// <summary>提供不可變的 provider 支援幣別快照。</summary>
        public IReadOnlyCollection<string> SupportedCurrencies => CurrencyCodes;

        /// <summary>未使用的一般建單入口；回傳固定失敗且不建立外部資源。</summary>
        public Task<PaymentCreateResult> CreatePaymentAsync(
            PaymentMerchantProfile profile,
            PaymentCreateRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PaymentCreateResult
            {
                Status = PaymentStatus.Failed,
                ProductOrderId = request.ProductOrderId
            });
        }

        /// <summary>未使用的付款查詢入口；回傳固定失敗且不建立外部資源。</summary>
        public Task<PaymentStatusResult> QueryPaymentAsync(
            PaymentMerchantProfile profile,
            PaymentQueryRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PaymentStatusResult
            {
                Status = PaymentStatus.Failed,
                ProductOrderId = request.ProductOrderId
            });
        }

        /// <summary>未使用的 callback 入口；回傳空結果且不建立外部資源。</summary>
        public Task<PaymentCallbackResult> ParseCallbackAsync(
            PaymentMerchantProfile profile,
            PaymentCallbackRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PaymentCallbackResult());
        }

        /// <summary>記錄定期建立是否被 Gateway 放行，供測試作決定性斷言。</summary>
        public Task<RecurringResult> CreateRecurringAsync(
            PaymentMerchantProfile profile,
            PaymentCreateRequest request,
            CancellationToken cancellationToken)
        {
            RecurringCreateCalls++;
            return Task.FromResult(new RecurringResult { Status = PaymentStatus.Pending });
        }

        /// <summary>未使用的定期查詢入口；回傳空候選集合。</summary>
        public Task<RecurringResult> QueryRecurringAsync(
            PaymentMerchantProfile profile,
            RecurringQueryRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new RecurringResult());
        }

        /// <summary>未使用的定期停止入口；回傳固定失敗。</summary>
        public Task<PaymentStatusResult> StopRecurringAsync(
            PaymentMerchantProfile profile,
            OrderMaintenanceRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PaymentStatusResult { Status = PaymentStatus.Failed });
        }
    }
}
