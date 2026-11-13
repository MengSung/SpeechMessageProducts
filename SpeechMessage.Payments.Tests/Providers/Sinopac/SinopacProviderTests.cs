/*
 * 檔案整體說明
 *
 * 目的與定位：驗證永豐 QPay provider 的 request mapping、AES key、狀態轉換、callback 與 HTTP 錯誤正規化。HTTP 失敗只公開固定階段／狀態碼，不回傳回應本文。這些測試特別保護付款頁 URL、ATM 虛擬帳號與 legacy
 * key derivation，避免抽離後破壞既有奉獻流程。
 *
 * 主要功能與流程：
 * 本檔宣告 SinopacProviderTests、StaticResponseHandler 與只擷取 envelope 服務名稱的假 HTTP handler。
 * 測試案例入口包含 Request_mapper_maps_create_request_to_qpay_order_create_payload、
 * Request_mapper_maps_query_provider_order_ref_to_qpay_pay_token、Crypto_builds_legacy_uppercase_aes_key、
 * Status_mapper_maps_successful_order_pay_response_to_succeeded、
 * Status_mapper_maps_declined_order_pay_response_to_failed、
 * Create_result_fails_when_card_payment_success_response_has_no_payment_page_url、
 * Create_result_preserves_provider_rejection_when_card_payment_page_url_is_missing、
 * Create_result_preserves_atm_virtual_account_in_provider_data、
 * Envelope_ApiService_UsesDocumentedServiceName、Envelope_ApiService_NeverNumeric、
 * Envelope_ApiService_RejectsUnknownEnumValue、
 * Allot_query_preserves_http_status_when_error_body_root_is_not_an_object；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Newtonsoft.Json;
using SpeechMessage.Localization;
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

    /// <summary>
    /// 保護 QPay 外層封包使用規格所定義的服務名稱；假 HTTP handler 回傳合成 Nonce，並在 Order 階段拒絕請求。
    /// 決定性斷言只讀取第二個 request body 的 APIService 欄位，不保存或輸出完整 envelope；HttpClient 擁有並釋放 handler。
    /// </summary>
    [Theory]
    [InlineData(1, "OrderCreate")]
    [InlineData(5, "OrderPayQuery")]
    [InlineData(7, "AllotQuery")]
    public async Task Envelope_ApiService_UsesDocumentedServiceName(
        int apiServiceValue,
        string expectedServiceName)
    {
        var apiService = (SinopacApiService)apiServiceValue;
        var handler = new ApiServiceCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(httpClient);

        await InvokeProviderOperationAsync(provider, apiService);

        handler.RequestCount.Should().Be(2);
        handler.CapturedApiService.Should().Be(expectedServiceName);
    }

    /// <summary>
    /// 保護所有已定義服務的 wire value 都不是 enum 數字；逐一走公開 provider 操作並檢查假傳輸擷取的 APIService。
    /// 測試不連外網、不保留完整 request body，且每次呼叫的 HttpClient 在迴圈內確定性 Dispose。
    /// </summary>
    [Fact]
    public async Task Envelope_ApiService_NeverNumeric()
    {
        foreach (var apiService in Enum.GetValues<SinopacApiService>())
        {
            var handler = new ApiServiceCaptureHandler();
            using var httpClient = new HttpClient(handler);
            var provider = new SinopacPaymentProvider(httpClient);

            await InvokeProviderOperationAsync(provider, apiService);

            handler.RequestCount.Should().Be(2);
            handler.CapturedApiService.Should().NotBeNullOrWhiteSpace();
            handler.CapturedApiService!.All(char.IsDigit).Should().BeFalse();
        }
    }

    /// <summary>
    /// 未知 enum 是 provider 程式錯誤，不得退化成數字 wire value 或可重試的 HTTP 呼叫。
    /// 直接驗證純對照入口拋出 ArgumentOutOfRangeException；此路徑不建立 transport 或保存任何請求資料。
    /// </summary>
    [Fact]
    public void Envelope_ApiService_RejectsUnknownEnumValue()
    {
        Action mapUnknownService = () =>
            SinopacPaymentProvider.ResolveApiServiceName((SinopacApiService)0);

        mapUnknownService.Should().Throw<ArgumentOutOfRangeException>();
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

    /// <summary>HTTP 失敗回傳固定階段與狀態碼；合成回應本文不得進入 PaymentError 或診斷訊息。</summary>
    [Fact]
    public async Task Create_payment_excludes_http_response_body_from_safe_diagnostic()
    {
        const string responseMarker = "synthetic response body must not escape";
        using var httpClient = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.BadRequest,
            responseMarker));
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
        result.Error.Kind.Should().Be(PaymentErrorKind.ProviderRejected);
        result.Error.Message.Should().Be("Nonce:400");
        result.Error.Message.Should().NotContain(responseMarker);
    }

    /// <summary>
    /// 保護 APIM 錯誤本文的根 JSON 為非物件時仍保留 Order 階段與 HTTP 狀態；
    /// 序列 handler 先回合成 Nonce，再回 500 與陣列本文，決定性斷言為固定 Order:500，
    /// 且原始本文不進入回傳錯誤。HttpClient 由本測試唯一擁有並在結束時 Dispose。
    /// </summary>
    [Fact]
    public async Task Allot_query_preserves_http_status_when_error_body_root_is_not_an_object()
    {
        const string responseBody = "[]";
        using var httpClient = new HttpClient(new SequenceResponseHandler(
            (HttpStatusCode.OK, "{\"Nonce\":\"synthetic-nonce\"}"),
            (HttpStatusCode.InternalServerError, responseBody)));
        var provider = new SinopacPaymentProvider(httpClient);

        var result = await provider.QueryAllotAsync(
            CreateProfile(),
            new PaymentAllotQueryRequest
            {
                FromDate = new DateOnly(2026, 11, 1),
                ToDate = new DateOnly(2026, 11, 2),
                PaymentType = "C"
            },
            CancellationToken.None);

        result.Error.Kind.Should().Be(PaymentErrorKind.ProviderUnavailable);
        result.Error.Code.Should().Be("Order:500");
        result.Error.Message.Should().Be("Order:500");
        result.Error.Message.Should().NotContain(responseBody);
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
                ["XKeyId"] = "1f078484-87fe-4c61-a77d-22d865eeb4f4"
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
                ["XKeyId"] = "1f078484-87fe-4c61-a77d-22d865eeb4f4"
            },
            Endpoints = new Dictionary<string, string>
            {
                ["ApiBaseUrl"] = "https://sandbox.sinopac.com/QPay.WebAPI/api/"
            }
        };
    }

    /// <summary>
    /// 依 enum 成員呼叫實際公開 provider 入口，讓假傳輸觀察真正建立的 Order envelope。
    /// 請求資料與設定均為本測試專用合成值，不會讀取 User Secrets 或發出真實付款。
    /// </summary>
    private static async Task InvokeProviderOperationAsync(
        SinopacPaymentProvider provider,
        SinopacApiService apiService)
    {
        var profile = CreateProfile();
        switch (apiService)
        {
            case SinopacApiService.OrderCreate:
                await provider.CreatePaymentAsync(
                    profile,
                    new PaymentCreateRequest
                    {
                        ProductOrderId = "SYNTHETIC-ORDER-001",
                        Total = new Money(1200m, "TWD"),
                        Description = "Synthetic envelope contract",
                        PaymentMethod = "C",
                        Callbacks = new PaymentCallbacks
                        {
                            ReturnUrl = "https://example.test/return",
                            BackendUrl = "https://example.test/backend"
                        }
                    },
                    CancellationToken.None);
                return;

            case SinopacApiService.OrderPayQuery:
                await provider.QueryPaymentAsync(
                    profile,
                    new PaymentQueryRequest
                    {
                        ProductOrderId = "SYNTHETIC-ORDER-001",
                        ProviderOrderRef = "SYNTHETIC-PAYTOKEN-001"
                    },
                    CancellationToken.None);
                return;

            case SinopacApiService.AllotQuery:
                await provider.QueryAllotAsync(
                    profile,
                    new PaymentAllotQueryRequest
                    {
                        ProfileName = profile.Name,
                        FromDate = new DateOnly(2026, 7, 1),
                        ToDate = new DateOnly(2026, 7, 8),
                        PaymentType = "C"
                    },
                    CancellationToken.None);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(apiService), apiService, null);
        }
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

    /// <summary>
    /// 依呼叫順序回傳固定合成 HTTP 結果，僅供離線協定測試使用；
    /// 隊列只保存狀態與合成本文字串，不持有 request、設定或外部資源。
    /// </summary>
    private sealed class SequenceResponseHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Content)> _responses;

        public SequenceResponseHandler(params (HttpStatusCode StatusCode, string Content)[] responses)
        {
            _responses = new Queue<(HttpStatusCode StatusCode, string Content)>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No synthetic HTTP response is configured.");
            }

            var response = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Content)
            });
        }
    }

    /// <summary>
    /// 離線擷取第二次 HTTP POST 的 APIService 欄位；只保留服務名稱，不保留 envelope 的 nonce、簽章或密文。
    /// 假 handler 回傳合成 Nonce，再以固定 HTTP 400 結束流程；HttpClient 是唯一 handler owner。
    /// </summary>
    private sealed class ApiServiceCaptureHandler : HttpMessageHandler
    {
        private int _requestCount;

        /// <summary>取得目前測試收到的請求數，預期為 Nonce 與 Order 各一筆。</summary>
        public int RequestCount => Volatile.Read(ref _requestCount);

        /// <summary>取得第二個 request body 中解析出的 APIService；不保存其他封包欄位。</summary>
        public string? CapturedApiService { get; private set; }

        /// <summary>為 Nonce 回覆固定合成資料，並僅從 Order 本文解析服務名稱。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestNumber = Interlocked.Increment(ref _requestCount);
            if (request.RequestUri!.AbsolutePath.EndsWith("/Nonce", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"Nonce\":\"SYNTHETIC-NONCE-ONLY\"}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            if (requestNumber == 2)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                CapturedApiService = JsonConvert.DeserializeObject<SinopacWebApiMessage>(body)?.APIService;
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }
    }
}
