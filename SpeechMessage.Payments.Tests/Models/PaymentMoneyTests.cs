/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P4-02 付款核心使用 SpeechMessage.Localization.Money 的精確金額契約與 provider 幣別邊界。
 * 測試只建立合成訂單、合成 sandbox profile 及案例私有替身，不讀取 User Secrets、環境變數或真實商店資料。
 *
 * 主要功能與流程：
 * Money_TwdRoundTrip_NoDecimalLoss 將 Money 經永豐 mapper 轉為最小單位，再由永豐回應金額解析路徑轉回 Money，
 * 斷言金額為 123456 分且結果仍精確等於 1234.56 TWD。
 * CreateOrder_UnsupportedCurrency_Rejected、CreateOrder_LineItemCurrencyMismatch_RejectedBeforeHttp 與
 * CreateOrder_DefaultMoneyLineItem_RejectedBeforeHttp 使用實際 Gateway 與 provider，在呼叫 provider 前拒絕不支援或不一致的幣別，
 * 並由計數型假 HTTP handler 證明沒有送出網路請求；後兩案各覆蓋永豐與 MyPay。
 *
 * 使用邊界與維護：
 * 本檔不啟動產品 Host、不連線永豐或 CRM。profile 與 handler 只由單一測試建立；HTTP 測試以 using 擁有 HttpClient，
 * 由 HttpClient Dispose 關閉其 handler；profile resolver 僅持有合成設定，與案例同壽命。測試失敗以 Money 值、錯誤種類及
 * handler 呼叫次數判定，不輸出任何憑證。
 */
using System.Globalization;
using System.Net;
using SpeechMessage.Localization;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Gateway;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Providers.MyPay;
using SpeechMessage.Payments.Providers.Sinopac;
using Xunit;

namespace SpeechMessage.Payments.Tests.Models;

/// <summary>
/// 驗證金流公開模型以不可變 Money 傳遞金額，並保護 provider 幣別能力在網路邊界前生效。
/// 所有輸入均為合成值；測試不建立共用 Host、資料庫連線或外部商店依賴。
/// </summary>
public sealed class PaymentMoneyTests
{
    /// <summary>
    /// 保護十進位 TWD 金額經永豐整數最小單位往返後不受浮點誤差影響。
    /// 決定性斷言同時檢查 mapper 產生 123456 及回應解析後 Money 與原始值相等。
    /// </summary>
    [Fact]
    public void Money_TwdRoundTrip_NoDecimalLoss()
    {
        var original = new Money(1234.56m, "TWD");
        var request = new PaymentCreateRequest
        {
            ProductOrderId = "synthetic-order",
            Description = "Synthetic payment",
            PaymentMethod = "CreditCard",
            Total = original
        };

        var payload = SinopacRequestMapper.MapCreateRequest(CreateSinopacProfile(), request);
        var roundTrip = SinopacPaymentProvider.ParseMinorAmount(
            payload.Amount.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(123456, payload.Amount);
        Assert.Equal<Money?>(original, roundTrip);
    }

    /// <summary>
    /// 保護 provider 宣告的幣別能力在任何外部傳輸前套用。
    /// 使用永豐 Sandbox 合成 profile 與實際 Gateway/provider，注入計數型假 HTTP handler；
    /// USD 必須回 Failed／RequestInvalid 且 handler 呼叫數為零。
    /// </summary>
    [Fact]
    public async Task CreateOrder_UnsupportedCurrency_Rejected()
    {
        var handler = new CountingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var profile = CreateSinopacProfile();
        var gateway = new PaymentGateway(
            new StaticPaymentProfileResolver(profile),
            new[] { new SinopacPaymentProvider(httpClient) });

        var result = await gateway.CreatePaymentAsync(new PaymentCreateRequest
        {
            ProfileName = profile.Name,
            ProductOrderId = "synthetic-order",
            Total = new Money(10m, "USD")
        });

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(PaymentStatus.Failed, result.Status);
        Assert.Equal(PaymentErrorKind.RequestInvalid, result.Error.Kind);
    }

    /// <summary>
    /// 保護建單前逐筆檢查明細幣別，避免把非總額幣別的數值送進 provider。
    /// 同一個合成請求分別通過永豐與 MyPay；兩個計數型 handler 都必須維持零次呼叫。
    /// </summary>
    [Fact]
    public async Task CreateOrder_LineItemCurrencyMismatch_RejectedBeforeHttp()
    {
        var request = new PaymentCreateRequest
        {
            ProductOrderId = "synthetic-order",
            Total = new Money(10m, "TWD"),
            Items = new[]
            {
                new PaymentLineItem
                {
                    Name = "Synthetic item",
                    Quantity = 1,
                    Price = new Money(10m, "USD")
                }
            }
        };

        await AssertRejectedBeforeHttp(
            CreateSinopacProfile(),
            client => new SinopacPaymentProvider(client),
            request);
        await AssertRejectedBeforeHttp(
            CreateMyPayProfile(),
            client => new MyPayPaymentProvider(client),
            request);
    }

    /// <summary>
    /// 保護明細使用沒有合法幣別的 default Money 時 fail closed。
    /// 永豐與 MyPay 都必須在 provider 建立 HTTP request 前回傳輸入錯誤。
    /// </summary>
    [Fact]
    public async Task CreateOrder_DefaultMoneyLineItem_RejectedBeforeHttp()
    {
        var request = new PaymentCreateRequest
        {
            ProductOrderId = "synthetic-order",
            Total = new Money(10m, "TWD"),
            Items = new[]
            {
                new PaymentLineItem
                {
                    Name = "Synthetic item",
                    Quantity = 1,
                    Price = default
                }
            }
        };

        await AssertRejectedBeforeHttp(
            CreateSinopacProfile(),
            client => new SinopacPaymentProvider(client),
            request);
        await AssertRejectedBeforeHttp(
            CreateMyPayProfile(),
            client => new MyPayPaymentProvider(client),
            request);
    }

    /// <summary>
    /// 建立完整但完全合成的 sandbox profile，確保若 Gateway 幣別檢查被移除，
    /// 永豐 provider 會走到計數型假 HTTP handler，而不會在憑證驗證處提前停止。
    /// profile 由呼叫測試獨占，回傳後沒有跨測試保存或重用。
    /// </summary>
    private static PaymentMerchantProfile CreateSinopacProfile()
    {
        return new PaymentMerchantProfile
        {
            Name = "SyntheticSandbox",
            Provider = PaymentProviderKind.Sinopac,
            Environment = PaymentEnvironment.Sandbox,
            Credentials = new Dictionary<string, string>
            {
                ["ShopNo"] = "synthetic-shop",
                ["A1"] = "1111111111111111",
                ["A2"] = "2222222222222222",
                ["B1"] = "3333333333333333",
                ["B2"] = "4444444444444444",
                ["XKeyId"] = "synthetic-key-id"
            },
            Endpoints = new Dictionary<string, string>
            {
                ["ApiBaseUrl"] = "https://sandbox.example.test/"
            }
        };
    }

    /// <summary>
    /// 建立完全合成的 MyPay profile；只供拒絕前 HTTP 測試使用，不讀取部署設定或秘密。
    /// </summary>
    private static PaymentMerchantProfile CreateMyPayProfile()
    {
        return new PaymentMerchantProfile
        {
            Name = "SyntheticMyPay",
            Provider = PaymentProviderKind.MyPay,
            Environment = PaymentEnvironment.Sandbox,
            Credentials = new Dictionary<string, string>
            {
                ["StoreId"] = "synthetic-store",
                ["Key"] = "synthetic-key"
            },
            Endpoints = new Dictionary<string, string>
            {
                ["ApiBaseUrl"] = "https://sandbox.example.test/api/init"
            }
        };
    }

    /// <summary>
    /// 使用單一案例擁有的 HttpClient 驗證 Gateway 在輸入關卡拒絕請求。
    /// provider 與 handler 均限於本次呼叫生命週期，回傳前由 using 確定性釋放。
    /// </summary>
    private static async Task AssertRejectedBeforeHttp(
        PaymentMerchantProfile profile,
        Func<HttpClient, IPaymentProvider> providerFactory,
        PaymentCreateRequest request)
    {
        var handler = new CountingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var gateway = new PaymentGateway(
            new StaticPaymentProfileResolver(profile),
            new[] { providerFactory(httpClient) });

        var result = await gateway.CreatePaymentAsync(request with { ProfileName = profile.Name });

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(PaymentStatus.Failed, result.Status);
        Assert.Equal(PaymentErrorKind.RequestInvalid, result.Error.Kind);
    }

    /// <summary>
    /// 只回傳本案例建立的合成商店設定，不讀取共用設定、秘密或外部狀態。
    /// </summary>
    private sealed class StaticPaymentProfileResolver : IPaymentProfileResolver
    {
        private readonly PaymentMerchantProfile _profile;

        /// <summary>由此測試獨占 resolver 與 profile，兩者不跨案例保存。</summary>
        public StaticPaymentProfileResolver(PaymentMerchantProfile profile)
        {
            _profile = profile;
        }

        /// <summary>依 Gateway 選取結果回傳此案例的合成設定。</summary>
        public PaymentMerchantProfile Resolve(string? profileName)
        {
            return _profile with { Name = profileName ?? _profile.Name };
        }
    }

    /// <summary>
    /// 計算 Gateway 是否越過幣別關卡呼叫 HTTP；此替身不建立連線，且由測試擁有的 HttpClient Dispose。
    /// </summary>
    private sealed class CountingHttpMessageHandler : HttpMessageHandler
    {
        /// <summary>記錄呼叫次數，僅供目前案例判斷傳輸是否發生。</summary>
        public int CallCount { get; private set; }

        /// <summary>若 Gateway 錯誤放行則回傳固定合成回應，避免測試對外傳輸。</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
