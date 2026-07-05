// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class SinopacProviderTests、class StaticResponseHandler
// 主要成員：Request_mapper_maps_create_request_to_qpay_order_create_payload、Request_mapper_maps_query_provider_order_ref_to_qpay_pay_token、Crypto_builds_legacy_uppercase_aes_key、Status_mapper_maps_successful_order_pay_response_to_succeeded、Status_mapper_maps_declined_order_pay_response_to_failed、Create_result_fails_when_card_payment_success_response_has_no_payment_page_url、Create_result_preserves_provider_rejection_when_card_payment_page_url_is_missing、Create_result_preserves_atm_virtual_account_in_provider_data、Create_result_fails_when_atm_success_response_has_no_virtual_account、Create_payment_includes_route_and_response_body_when_http_status_fails
// 引用命名空間：FluentAssertions、Microsoft.Extensions.Configuration、Microsoft.Extensions.DependencyInjection、System.Net、SpeechMessage.Payments.Abstractions、SpeechMessage.Payments.Configuration、SpeechMessage.Payments.DependencyInjection、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.DependencyInjection;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Providers.Sinopac;
using Xunit;

namespace SpeechMessage.Payments.Tests.Providers.Sinopac;

/// <summary>
/// 驗證永豐 QPay provider 的 request mapping、AES key、狀態轉換、callback 與 HTTP 錯誤正規化。
/// 這些測試特別保護付款頁 URL、ATM 虛擬帳號與 legacy key derivation，避免抽離後破壞既有奉獻流程。
/// </summary>
public sealed class SinopacProviderTests
{
    [Fact]
    public void Request_mapper_maps_create_request_to_qpay_order_create_payload()
    {
        var profile = CreateProfile();
        var request = new PaymentCreateRequest
        {
            ProductOrderId = "C202606250001",
            Amount = 1200m,
            Currency = "TWD",
            Description = "Fee payment",
            PaymentMethod = "CreditCard",
            PaymentMethodSubType = "ONE",
            Callbacks = new PaymentCallbacks
            {
                ReturnUrl = "https://example.test/qpay-return",
                BackendUrl = "https://example.test/qpay-backend"
            },
            Metadata = new Dictionary<string, string>
            {
                ["Param1"] = "fee-id",
                ["Param2"] = "Jesus",
                ["Param3"] = "fee",
                ["AutoBilling"] = "Y"
            }
        };

        var payload = SinopacRequestMapper.MapCreateRequest(profile, request);

        payload.ShopNo.Should().Be("NA0149_001");
        payload.OrderNo.Should().Be("C202606250001");
        payload.Amount.Should().Be(120000);
        payload.CurrencyID.Should().Be("TWD");
        payload.PrdtName.Should().Be("Fee payment");
        payload.ReturnURL.Should().Be("https://example.test/qpay-return");
        payload.BackendURL.Should().Be("https://example.test/qpay-backend");
        payload.PayType.Should().Be("C");
        payload.Param1.Should().Be("fee-id");
        payload.Param2.Should().Be("Jesus");
        payload.Param3.Should().Be("fee");
        payload.CardParam.Should().NotBeNull();
        payload.CardParam!.AutoBilling.Should().Be("Y");
        payload.CardParam.PayTypeSub.Should().Be("ONE");
    }

    [Fact]
    public void Request_mapper_maps_query_provider_order_ref_to_qpay_pay_token()
    {
        var profile = CreateProfile();
        var request = new PaymentQueryRequest
        {
            ProductOrderId = "C202606250001",
            ProviderOrderRef = "PAYTOKEN-1234567890"
        };

        var payload = SinopacRequestMapper.MapOrderPayQuery(profile, request);

        payload.ShopNo.Should().Be("NA0149_001");
        payload.PayToken.Should().Be("PAYTOKEN-1234567890");
    }

    [Fact]
    public void Crypto_builds_legacy_uppercase_aes_key()
    {
        // 永豐 QPay AES key 來自 A1/A2、B1/B2 XOR 後的大寫 hex 字串。
        // 大小寫會影響實際 AES key bytes，錯誤時銀行端可能直接回 HTTP 400。
        var aesKey = SinopacCrypto.BuildAesKey(CreateProfile());

        aesKey.Should().Be("89C697BCC1C10908864428F5C58A068A");
    }

    [Fact]
    public void Status_mapper_maps_successful_order_pay_response_to_succeeded()
    {
        var response = new SinopacOrderPayResponse
        {
            ShopNo = "NA0149_001",
            PayToken = "PAYTOKEN-1234567890",
            Status = "S",
            Description = "S00000",
            TSResultContent = new SinopacTransactionResult
            {
                TSNo = "TS123",
                OrderNo = "C202606250001",
                ShopNo = "NA0149_001",
                Amount = "120000",
                Status = "S",
                Description = "S00000",
                Param1 = "fee-id",
                Param3 = "fee"
            }
        };

        SinopacStatusMapper.Map(response).Should().Be(PaymentStatus.Succeeded);
    }

    [Fact]
    public void Status_mapper_maps_declined_order_pay_response_to_failed()
    {
        var response = new SinopacOrderPayResponse
        {
            ShopNo = "NA0149_001",
            PayToken = "PAYTOKEN-1234567890",
            Status = "S",
            Description = "S00000",
            TSResultContent = new SinopacTransactionResult
            {
                OrderNo = "C202606250001",
                Status = "F",
                Description = "E2700 - declined"
            }
        };

        SinopacStatusMapper.Map(response).Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public void Create_result_fails_when_card_payment_success_response_has_no_payment_page_url()
    {
        // 信用卡、行動支付等 hosted payment 成功回應必須有付款頁 URL。
        // 若缺 URL 仍視為成功，瀏覽器會跳回原奉獻頁而不是永豐刷卡頁。
        var request = new SinopacOrderCreateRequest
        {
            OrderNo = "C202606250001",
            PayType = "C"
        };
        var response = new SinopacOrderCreateResponse
        {
            OrderNo = "C202606250001",
            Status = "S",
            Description = "S00000",
            CardParam = new SinopacOrderCreateCardResponse()
        };

        var result = SinopacPaymentProvider.ResolveCreateResult(
            request,
            response,
            "fallback-order");

        result.Status.Should().Be(PaymentStatus.Failed);
        result.PaymentPageUrl.Should().BeEmpty();
        result.Error.Kind.Should().Be(PaymentErrorKind.ProviderRejected);
        result.Error.Message.Should().Contain("payment page URL");
    }

    [Fact]
    public void Create_result_preserves_provider_rejection_when_card_payment_page_url_is_missing()
    {
        var request = new SinopacOrderCreateRequest
        {
            OrderNo = "C202606250001",
            PayType = "C"
        };
        var response = new SinopacOrderCreateResponse
        {
            OrderNo = "C202606250001",
            Status = "F",
            Description = "E5001 - invalid recurring installment count"
        };

        var result = SinopacPaymentProvider.ResolveCreateResult(
            request,
            response,
            "fallback-order");

        result.Status.Should().Be(PaymentStatus.Failed);
        result.PaymentPageUrl.Should().BeEmpty();
        result.Error.Kind.Should().Be(PaymentErrorKind.ProviderRejected);
        result.Error.Code.Should().Be("F");
        result.Error.Message.Should().Be("E5001 - invalid recurring installment count");
    }

    [Fact]
    public void Create_result_preserves_atm_virtual_account_in_provider_data()
    {
        // ATM 虛擬帳號是使用者付款指示，必須從 provider response 跨到 ProviderData，
        // 讓 ChurchReport legacy adapter 可以顯示帳號並發送 LINE/頁面通知。
        var request = new SinopacOrderCreateRequest
        {
            OrderNo = "A202606250001",
            PayType = "A"
        };
        var response = new SinopacOrderCreateResponse
        {
            OrderNo = "A202606250001",
            TSNo = "TSATM123456",
            Status = "S",
            Description = "S00000",
            ATMParam = new SinopacOrderCreateAtmResponse
            {
                AtmPayNo = "12345678901234",
                WebAtmURL = "https://sandbox.sinopac.test/atm",
                OtpURL = "https://sandbox.sinopac.test/otp"
            }
        };

        var result = SinopacPaymentProvider.ResolveCreateResult(
            request,
            response,
            "fallback-order");

        result.Status.Should().Be(PaymentStatus.Pending);
        result.PaymentPageUrl.Should().Be("https://sandbox.sinopac.test/atm");
        result.ProviderData["atm_pay_no"].Should().Be("12345678901234");
        result.ProviderData["web_atm_url"].Should().Be("https://sandbox.sinopac.test/atm");
        result.ProviderData["otp_url"].Should().Be("https://sandbox.sinopac.test/otp");
        result.Diagnostics["atm_pay_no"].Should().Be("12345678901234");
    }

    [Fact]
    public void Create_result_fails_when_atm_success_response_has_no_virtual_account()
    {
        var request = new SinopacOrderCreateRequest
        {
            OrderNo = "A202606250001",
            PayType = "A"
        };
        var response = new SinopacOrderCreateResponse
        {
            OrderNo = "A202606250001",
            TSNo = "TSATM123456",
            Status = "S",
            Description = "S00000",
            ATMParam = new SinopacOrderCreateAtmResponse
            {
                WebAtmURL = "https://sandbox.sinopac.test/atm"
            }
        };

        var result = SinopacPaymentProvider.ResolveCreateResult(
            request,
            response,
            "fallback-order");

        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Kind.Should().Be(PaymentErrorKind.ProviderRejected);
        result.Error.Message.Should().Contain("ATM virtual account");
    }

    [Fact]
    public async Task Create_payment_includes_route_and_response_body_when_http_status_fails()
    {
        // 銀行 HTTP status 失敗時，錯誤訊息要包含 route 與 sanitized response body，
        // 否則現場只會看到 BadRequest，難以判斷是 nonce、create order 或 payload 哪一段失敗。
        using var httpClient = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.BadRequest,
            "invalid request payload"));
        var provider = new SinopacPaymentProvider(httpClient);

        var result = await provider.CreatePaymentAsync(
            CreateProfile(),
            new PaymentCreateRequest
            {
                ProductOrderId = "C202606250001",
                Amount = 1200m,
                Description = "Fee payment",
                PaymentMethod = "C",
                Callbacks = new PaymentCallbacks
                {
                    ReturnUrl = "https://example.test/qpay-return",
                    BackendUrl = "https://example.test/qpay-backend"
                }
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Kind.Should().Be(PaymentErrorKind.ProviderUnavailable);
        result.Error.Message.Should().Contain("Sinopac Nonce returned HTTP 400 BadRequest");
        result.Error.Message.Should().Contain("invalid request payload");
    }

    [Theory]
    [InlineData("", "PAYTOKEN-1234567890")]
    [InlineData("NA0149_001", "")]
    public void Callback_parser_rejects_missing_shop_no_or_pay_token(string shopNo, string payToken)
    {
        var request = new PaymentCallbackRequest
        {
            ProfileName = "JesusTest",
            ProviderHint = PaymentProviderKind.Sinopac,
            Query = new Dictionary<string, string>
            {
                ["ShopNo"] = shopNo,
                ["PayToken"] = payToken
            }
        };

        var result = SinopacCallbackParser.Parse(request);

        result.Error.Kind.Should().Be(PaymentErrorKind.CallbackInvalid);
        result.Acknowledgement.Should().Be(PaymentCallbackAcknowledgement.None);
    }

    [Fact]
    public void Callback_parser_maps_return_query_and_sanitizes_sensitive_values()
    {
        // Return query 只提供 PayToken/ShopNo 供後續查詢，PayToken 與密鑰類欄位在 diagnostics 中必須遮蔽。
        var request = new PaymentCallbackRequest
        {
            ProfileName = "JesusTest",
            ProviderHint = PaymentProviderKind.Sinopac,
            Query = new Dictionary<string, string>
            {
                ["ShopNo"] = "NA0149_001",
                ["PayToken"] = "1234567890abcdef1234567890abcdef",
                ["HashCode"] = "full-hash-value",
                ["A1"] = "5E854757C751413F",
                ["A2"] = "D743D0EB06904837",
                ["B1"] = "08169D5445644513",
                ["B2"] = "8E52B5A180EE4399",
                ["XKeyId"] = "b5e6986d-8636-4aa0-8c93-441ad14b2098"
            }
        };

        var result = SinopacCallbackParser.Parse(request);

        result.Status.Should().Be(PaymentStatus.Pending);
        result.ProviderTransactionId.Should().Be("1234567890abcdef1234567890abcdef");
        result.ProviderData["shop_no"].Should().Be("NA0149_001");
        result.ProviderData["pay_token"].Should().Be("1234...cdef");
        result.Diagnostics["HashCode"].Should().Be("***");
        result.Diagnostics["A1"].Should().Be("***");
        result.Diagnostics["A2"].Should().Be("***");
        result.Diagnostics["B1"].Should().Be("***");
        result.Diagnostics["B2"].Should().Be("***");
        result.Diagnostics["XKeyId"].Should().Be("***");
    }

    [Fact]
    public void Service_registration_adds_sinopac_provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DefaultProfile"] = "JesusTest",
                ["Profiles:JesusTest:Provider"] = "Sinopac"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddSpeechMessagePayments(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IPaymentProvider>()
            .Should()
            .Contain(paymentProvider => paymentProvider.ProviderKind == PaymentProviderKind.Sinopac);
    }

    private static PaymentMerchantProfile CreateProfile()
    {
        return new PaymentMerchantProfile
        {
            Name = "JesusTest",
            Provider = PaymentProviderKind.Sinopac,
            Credentials = new Dictionary<string, string>
            {
                ["ShopNo"] = "NA0149_001",
                ["A1"] = "5E854757C751413F",
                ["A2"] = "D743D0EB06904837",
                ["B1"] = "08169D5445644513",
                ["B2"] = "8E52B5A180EE4399",
                ["XKeyId"] = "b5e6986d-8636-4aa0-8c93-441ad14b2098"
            },
            Endpoints = new Dictionary<string, string>
            {
                ["ApiBaseUrl"] = "https://sandbox.sinopac.com/QPay.WebAPI/api/"
            }
        };
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public StaticResponseHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content)
            });
        }
    }
}
