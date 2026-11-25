/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 F10 永豐綁卡、訂單維護、定期定額與 callback 查詢線索契約。
 * 所有請求、profile、付款識別與秘密樣式資料均為合成值；測試只使用記憶體 HttpMessageHandler，
 * 不讀取 User Secrets、不連線永豐或任何正式金流。
 *
 * 主要功能與流程：
 * CardBind_Golden_RequestSignedCorrectly 固定核對 CardBindCreate 的 API 名稱、簽章及 AES 密文；
 * OrderMaintain_Cancel_MapsCommandC 與 OrderMaintain_Capture_MapsCommandP 保護取消授權／立即請款指令；
 * Recurring_Create_MapsPayTypeSubRegular 與 Recurring_Stop_MapsCommandE 驗證定期定額建立及中止欄位；
 * CardBind_QueryReturnsOpaqueEnvelopeAndBindTokenNeverLeavesEncryptedBoundary 驗證查詢所得綁卡 token
 * 以 profile 綁定、具驗證標記的密文傳遞，只有建立銀行請求時才在 provider 內短暫解密；
 * CardBind_ProtectedTokenTamperingRejectedBeforeNetwork 與 CardBind_ProtectedTokenCannotCrossProfile
 * 注入密文篡改及跨 profile 重用，確認都在銀行 I/O 前拒絕；Sinopac_HttpEndpointRejectedBeforeNetwork
 * 確認 HTTP endpoint 在傳輸前 fail closed；
 * Logs_NoCardNumberOrBindToken 驗證 Payment core 對外 diagnostics 邊界不保留完整卡號或原始綁卡 token；
 * Recurring_Query_OversizedChunkedResponseFailsClosedAndStopsReading 與
 * Recurring_Query_ResponseBodyTimeoutReturnsFailureAndDisposesStream 驗證未知長度的回應本文受位元組上限限制、
 * headers-only 傳輸不先行緩衝、本文讀取仍有總逾時且取消／失敗後 response stream 確實釋放；
 * Callback_Duplicate_NotDoubleRecorded 與 Callback_CaptureBeforeAuthNotice_IdempotentFinalState
 * 確認重送及亂序 callback 仍只是查詢線索，不由 provider parser 直接宣告已付款。
 *
 * 使用邊界與維護：
 * 每個案例以 using 釋放自己建立的 HttpClient，連帶釋放 handler；HTTP response 由 provider 的 using 釋放，
 * 因而 StreamContent 也會 Dispose 其唯一擁有的測試 stream。假傳輸 handler 僅保留不含 nonce、簽章、
 * 密文本文或明文 token 的觀察結果；
 * 加解密結果只在單次 SendAsync 呼叫中使用，不寫入 log 或跨測試共享狀態。
 * callback 測試不模擬資料庫或跨程序冪等；最終入帳須由產品持久層以資料庫唯一鍵與原子操作保護。
 */
using System.Net;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Localization;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Providers.Sinopac;
using Xunit;

namespace SpeechMessage.Payments.Tests.Providers.Sinopac;

/// <summary>
/// 驗證 F10 永豐核心操作的 request、response 與隔離契約。
/// 合成 HTTP handler 不連外，profile 由每個測試單獨建立。
/// </summary>
public sealed class SinopacF10Tests
{
    private const string SyntheticNonce = "NONCE-F10-CARD-BIND";
    private const string SyntheticBindNo = "F10-BIND-001";
    private const string SyntheticPayToken = "F10-PAY-TOKEN-001";
    private const string SyntheticBindToken = "F10-BIND-TOKEN-ONLY";

    /// <summary>
    /// 保護 V2.6 CardBindCreate 的實際外層服務名、簽章及加密本文。
    /// 預期值由獨立 Node.js crypto 向量計算，不呼叫 provider signer 或 mapper 產生 expected。
    /// </summary>
    [Fact]
    public async Task CardBind_Golden_RequestSignedCorrectly()
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentCardBinding)provider).CreateCardBindAsync(
            profile,
            new CardBindRequest
            {
                ProfileName = profile.Name,
                BindNo = SyntheticBindNo,
                ReturnUrl = "https://merchant.example.test/payment/bind-return"
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Pending);
        result.BindNo.Should().Be(SyntheticBindNo);
        result.PaymentPageUrl.Should().Be("https://sandbox.sinopac.test/card-bind");
        handler.CapturedApiService.Should().Be("CardBindCreate");
        handler.CapturedSignature.Should()
            .Be("D06502B1067EC11B98D07B59B9EF0F3A7B488BFF67A78BEAFD8D454132E31140");
        handler.CapturedEncryptedMessage.Should().Be(
            "CC25C012EEAC8F21A7A06464621D996275C9FC25AF9CF710C5D8DE3F96AA07839E1E97FBB4DBB1ED339F9DD35C869A9C88015193A38EBB67E3A80A1351F86A4D20470C86A50DC9F670FC6F6F29F0F6C8791101CD04A388A0A586C5CCEDC7B319F4B00929A55385AB338C4DBCF59EF25B877F085E85301E99D8BDDEC83125D56731CC20D522639BB1CFC2CEA8D47AB3F74A66673F9283A9589EB83E4AB301A058");
    }

    /// <summary>
    /// 保護取消授權只送 OrderMaintain Command=C，回應成功後才發布 Cancelled。
    /// 合成 handler 擷取解密後的命令，不發送真實金流請求。
    /// </summary>
    [Fact]
    public async Task OrderMaintain_Cancel_MapsCommandC()
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentOrderMaintenance)provider).CancelAuthorizationAsync(
            profile,
            new OrderMaintenanceRequest
            {
                ProfileName = profile.Name,
                OrderNo = "SYNTHETIC-CAPTURE-001"
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Cancelled);
        result.Error.Kind.Should().Be(PaymentErrorKind.None);
        handler.CapturedApiService.Should().Be("OrderMaintain");
        handler.CapturedCommand.Should().Be("C");
    }

    /// <summary>
    /// 保護立即請款只送 OrderMaintain Command=P，銀行回應成功時才發布 Succeeded。
    /// 測試資料為合成訂單，所有 HTTP 都由本案例的 handler 回應。
    /// </summary>
    [Fact]
    public async Task OrderMaintain_Capture_MapsCommandP()
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentOrderMaintenance)provider).CaptureNowAsync(
            profile,
            new OrderMaintenanceRequest
            {
                ProfileName = profile.Name,
                OrderNo = "SYNTHETIC-CAPTURE-002"
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Succeeded);
        result.Error.Kind.Should().Be(PaymentErrorKind.None);
        handler.CapturedApiService.Should().Be("OrderMaintain");
        handler.CapturedCommand.Should().Be("P");
    }

    /// <summary>
    /// 保護定期定額建立 payload 的 PayTypeSub、扣款期數與頻率欄位。
    /// 斷言讀取真實 provider 送出的解密本文，不只確認測試替身被呼叫。
    /// </summary>
    [Fact]
    public async Task Recurring_Create_MapsPayTypeSubRegular()
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentRecurring)provider).CreateRecurringAsync(
            profile,
            new PaymentCreateRequest
            {
                ProfileName = profile.Name,
                ProductOrderId = "SYNTHETIC-REGULAR-MASTER-001",
                Total = new Money(1200m, "TWD"),
                PaymentMethod = "C",
                PaymentMethodSubType = "REGULAR",
                Callbacks = new PaymentCallbacks
                {
                    ReturnUrl = "https://merchant.example.test/payment/return",
                    BackendUrl = "https://merchant.example.test/payment/backend"
                },
                Metadata = new Dictionary<string, string>
                {
                    ["DeductTotalNum"] = "12",
                    ["PeriodType"] = "M",
                    ["DeductFreq"] = "1"
                }
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Pending);
        handler.CapturedApiService.Should().Be("OrderCreate");
        handler.CapturedPayTypeSub.Should().Be("REGULAR");
        handler.CapturedDeductTotalNum.Should().Be(12);
        handler.CapturedPeriodType.Should().Be("M");
        handler.CapturedDeductFreq.Should().Be(1);
    }

    /// <summary>
    /// 保護定期定額中止只送 OrderMaintain Command=E，並核對成功回應只表示該次維護操作成功。
    /// 所有輸入及回應都由合成 handler 提供，不連接銀行或建立跨請求狀態。
    /// </summary>
    [Fact]
    public async Task Recurring_Stop_MapsCommandE()
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentRecurring)provider).StopRecurringAsync(
            profile,
            new OrderMaintenanceRequest
            {
                ProfileName = profile.Name,
                OrderNo = "SYNTHETIC-REGULAR-MASTER-001"
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Cancelled);
        result.Error.Kind.Should().Be(PaymentErrorKind.None);
        handler.CapturedApiService.Should().Be("OrderMaintain");
        handler.CapturedCommand.Should().Be("E");
    }

    /// <summary>
    /// 保護查詢 CardBindOut 後只將 profile 綁定的 authenticated ciphertext 交給呼叫端；
    /// 重用時 provider 驗證 profile、商店與 BindNo，短暫解密後才送入加密 OrderCreate。
    /// 決定性斷言確認 token 不出現在回傳結果或外層 HTTP JSON。
    /// </summary>
    [Fact]
    public async Task CardBind_QueryReturnsOpaqueEnvelopeAndBindTokenNeverLeavesEncryptedBoundary()
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var binding = (IPaymentCardBinding)provider;
        var bindResult = await binding.QueryCardBindAsync(
            profile,
            new CardBindQueryRequest
            {
                ProfileName = profile.Name,
                PayToken = SyntheticPayToken,
                ExpectedBindNo = SyntheticBindNo
            },
            CancellationToken.None);

        bindResult.Status.Should().Be(PaymentStatus.Succeeded);
        bindResult.ProtectedBindToken.Should().NotBeNullOrWhiteSpace();
        bindResult.ProtectedBindToken.Should().NotContain(SyntheticBindToken);
        bindResult.ProtectedBindToken.Should().NotContain("\"Message\"");

        var paymentResult = await provider.CreatePaymentAsync(
            profile,
            new PaymentCreateRequest
            {
                ProductOrderId = "SYNTHETIC-BOUND-PAYMENT-001",
                Total = new Money(1200m, "TWD"),
                PaymentMethod = "C",
                Callbacks = new PaymentCallbacks
                {
                    ReturnUrl = "https://merchant.example.test/payment/return",
                    BackendUrl = "https://merchant.example.test/payment/backend"
                },
                Metadata = new Dictionary<string, string>
                {
                    ["BindNo"] = SyntheticBindNo,
                    ["ProtectedBindToken"] = bindResult.ProtectedBindToken,
                    ["AutoBilling"] = "N",
                    ["ExpBillingDays"] = "1",
                    ["CardNumber"] = "4111111111111111"
                }
            },
            CancellationToken.None);

        paymentResult.Status.Should().Be(PaymentStatus.Pending);
        handler.CapturedApiService.Should().Be("OrderCreate");
        handler.OrderCreateContainedBindToken.Should().BeTrue();
        handler.OrderCreateOuterJsonContainedBindToken.Should().BeFalse();
        var publicResult = JsonConvert.SerializeObject(paymentResult);
        publicResult.Should().NotContain(SyntheticBindToken);
        publicResult.Should().NotContain("4111111111111111");
    }

    /// <summary>
    /// 保護產品層保存的 BindToken 密文不可被竄改；先完成一次合成查詢，再破壞封裝尾端，
    /// 確認付款操作在送出任何 Nonce／Order 請求前失敗。測試 handler 只保存請求數。
    /// </summary>
    [Fact]
    public async Task CardBind_ProtectedTokenTamperingRejectedBeforeNetwork()
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);
        var binding = (IPaymentCardBinding)provider;

        var bindResult = await binding.QueryCardBindAsync(
            profile,
            new CardBindQueryRequest
            {
                ProfileName = profile.Name,
                PayToken = SyntheticPayToken,
                ExpectedBindNo = SyntheticBindNo
            },
            CancellationToken.None);

        var protectedToken = bindResult.ProtectedBindToken;
        protectedToken.Should().NotBeNullOrWhiteSpace();
        protectedToken = protectedToken[..^1] + (protectedToken[^1] == 'A' ? "B" : "A");

        var result = await provider.CreatePaymentAsync(
            profile,
            CreateBoundPaymentRequest(protectedToken),
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Kind.Should().Be(PaymentErrorKind.SerializationFailure);
        handler.RequestCount.Should().Be(2);
    }

    /// <summary>
    /// 保護 profile A 的密文不能搬到 profile B 使用；profile 名稱、商店及金鑰皆屬封裝關聯範圍，
    /// 且跨 profile 拒絕必須發生在 profile B 的首次 Nonce 請求之前。
    /// </summary>
    [Fact]
    public async Task CardBind_ProtectedTokenCannotCrossProfile()
    {
        var profileA = CreateSyntheticProfile();
        var handlerA = new F10ProtocolHandler(profileA);
        using var clientA = new HttpClient(handlerA);
        var providerA = new SinopacPaymentProvider(clientA);

        var bindResult = await ((IPaymentCardBinding)providerA).QueryCardBindAsync(
            profileA,
            new CardBindQueryRequest
            {
                ProfileName = profileA.Name,
                PayToken = SyntheticPayToken,
                ExpectedBindNo = SyntheticBindNo
            },
            CancellationToken.None);

        var profileB = CreateSyntheticProfile();
        profileB.Name = "SyntheticOtherF10";
        profileB.Credentials["ShopNo"] = "SYNTHETIC-SHOP-002";
        var handlerB = new F10ProtocolHandler(profileB);
        using var clientB = new HttpClient(handlerB);
        var providerB = new SinopacPaymentProvider(clientB);

        var result = await providerB.CreatePaymentAsync(
            profileB,
            CreateBoundPaymentRequest(bindResult.ProtectedBindToken),
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Kind.Should().Be(PaymentErrorKind.SerializationFailure);
        handlerA.RequestCount.Should().Be(2);
        handlerB.RequestCount.Should().Be(0);
    }

    /// <summary>
    /// 保護金流憑證或綁卡 token 不得送往未加密 HTTP endpoint。
    /// 假傳輸的請求數為決定性斷言，證明 URI 驗證在任何網路 I/O 前完成。
    /// </summary>
    [Fact]
    public async Task Sinopac_HttpEndpointRejectedBeforeNetwork()
    {
        var profile = CreateSyntheticProfile();
        profile.Endpoints["ApiBaseUrl"] = "http://sandbox.sinopac.test/QPay/";
        var handler = new F10ProtocolHandler(profile);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentCardBinding)provider).CreateCardBindAsync(
            profile,
            new CardBindRequest
            {
                ProfileName = profile.Name,
                BindNo = SyntheticBindNo,
                ReturnUrl = "https://merchant.example.test/payment/bind-return"
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Kind.Should().Be(PaymentErrorKind.ConfigurationInvalid);
        handler.RequestCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 OrderPayQuery 的 CardBindOut 只能用於預期商店與 BindNo；
    /// 以有效外層簽章注入 scope／correlation 衝突，確認不能發布可重用的 protected token。
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CardBind_QueryRejectsMismatchedShopOrBindNo(
        bool mismatchShop,
        bool mismatchBindNo)
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile)
        {
            ReturnedShopNo = mismatchShop ? "SYNTHETIC-OTHER-SHOP" : null,
            ReturnedBindNo = mismatchBindNo ? "SYNTHETIC-OTHER-BIND" : null
        };
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentCardBinding)provider).QueryCardBindAsync(
            profile,
            new CardBindQueryRequest
            {
                ProfileName = profile.Name,
                PayToken = SyntheticPayToken,
                ExpectedBindNo = SyntheticBindNo
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed);
        result.ProtectedBindToken.Should().BeEmpty();
        result.Error.HasError.Should().BeTrue();
        handler.RequestCount.Should().Be(2);
    }

    /// <summary>
    /// 建立綁卡 token 已由 profile-bound authenticated envelope 保護的合成付款請求；
    /// 每個測試各自持有 DTO，避免透過共用集合保留另一個案例的秘密。
    /// </summary>
    private static PaymentCreateRequest CreateBoundPaymentRequest(string protectedBindToken)
    {
        return new PaymentCreateRequest
        {
            ProductOrderId = "SYNTHETIC-BOUND-PAYMENT-001",
            Total = new Money(1200m, "TWD"),
            PaymentMethod = "C",
            Callbacks = new PaymentCallbacks
            {
                ReturnUrl = "https://merchant.example.test/payment/return",
                BackendUrl = "https://merchant.example.test/payment/backend"
            },
            Metadata = new Dictionary<string, string>
            {
                ["BindNo"] = SyntheticBindNo,
                ["ProtectedBindToken"] = protectedBindToken,
                ["AutoBilling"] = "N",
                ["ExpBillingDays"] = "1"
            }
        };
    }

    /// <summary>
    /// 保護 RegularQuery 子單在相同狀態與日期時仍以各自穩定 OrderNo 保留為獨立資料列。
    /// 回傳集合是本次操作建立的唯讀副本，不保存於 provider 或跨查詢共用。
    /// </summary>
    [Fact]
    public async Task Recurring_Query_PreservesDistinctStableChargeOrders()
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile);
        handler.UseDuplicateRecurringOrderNo = false;
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentRecurring)provider).QueryRecurringAsync(
            profile,
            new RecurringQueryRequest
            {
                ProfileName = profile.Name,
                MasterOrderNo = "SYNTHETIC-REGULAR-MASTER-001"
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Pending);
        result.Charges.Should().HaveCount(2);
        result.Charges.Select(charge => charge.OrderNo)
            .Should().Equal("SYNTHETIC-REGULAR-001", "SYNTHETIC-REGULAR-002");
        result.Charges.Select(charge => charge.DeductStatus)
            .Should().OnlyContain(status => status == "00");
        var mutableView = result.Charges as IList<RecurringChargeResult>;
        mutableView.Should().NotBeNull();
        Assert.Throws<NotSupportedException>(() => mutableView!.Add(new RecurringChargeResult()));
    }

    /// <summary>
    /// 保護同一 RegularQuery 消費集合內不得有重複穩定訂單 ID；
    /// 遇到衝突時整份 candidate fail closed，不發布其中任一筆。
    /// </summary>
    [Fact]
    public async Task Recurring_Query_DuplicateStableOrderIdentity_FailsClosed()
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile)
        {
            UseDuplicateRecurringOrderNo = true
        };
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentRecurring)provider).QueryRecurringAsync(
            profile,
            new RecurringQueryRequest
            {
                ProfileName = profile.Name,
                MasterOrderNo = "SYNTHETIC-REGULAR-MASTER-001"
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Kind.Should().Be(PaymentErrorKind.SerializationFailure);
        result.Charges.Should().BeEmpty();
    }

    /// <summary>
    /// 保護定期查詢的本地集合上限；超過 1,000 筆時整份候選集合拒絕發布，避免無界配置與部分快照。
    /// </summary>
    [Fact]
    public async Task Recurring_Query_OverMaximumChargeCountFailsClosed()
    {
        var profile = CreateSyntheticProfile();
        var handler = new F10ProtocolHandler(profile)
        {
            UseTooManyRecurringCharges = true
        };
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentRecurring)provider).QueryRecurringAsync(
            profile,
            new RecurringQueryRequest
            {
                ProfileName = profile.Name,
                MasterOrderNo = "SYNTHETIC-REGULAR-MASTER-001"
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Code.Should().Be("RegularQueryResponseMismatch");
        result.Charges.Should().BeEmpty();
    }

    /// <summary>
    /// 保護未知 Content-Length／chunked 回應不能繞過固定本文上限；只允許讀到上限加一個偵測位元組，
    /// 隨後必須拒絕發布並由 response owner 釋放 stream，避免任意回應造成無界緩衝。
    /// </summary>
    [Fact]
    public async Task Recurring_Query_OversizedChunkedResponseFailsClosedAndStopsReading()
    {
        var profile = CreateSyntheticProfile();
        var stream = new GeneratedResponseStream(SinopacPaymentProvider.MaximumResponseBodyBytes + 4096);
        var handler = new SyntheticResponseHandler(stream);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentRecurring)provider).QueryRecurringAsync(
            profile,
            new RecurringQueryRequest
            {
                ProfileName = profile.Name,
                MasterOrderNo = "SYNTHETIC-REGULAR-MASTER-001"
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Kind.Should().Be(PaymentErrorKind.SerializationFailure);
        result.Error.Code.Should().Be("Nonce:ResponseTooLarge");
        result.Charges.Should().BeEmpty();
        handler.RequestCount.Should().Be(1);
        stream.BytesRead.Should().Be(SinopacPaymentProvider.MaximumResponseBodyBytes + 1);
        stream.IsDisposed.Should().BeTrue();
    }

    /// <summary>
    /// 保護 ResponseHeadersRead 後的本文傳輸仍受 HttpClient.Timeout 等效總逾時約束；
    /// 注入永不完成的合成 stream，確認逾時會回傳失敗且 response Dispose 釋放 stream。
    /// </summary>
    [Fact]
    public async Task Recurring_Query_ResponseBodyTimeoutReturnsFailureAndDisposesStream()
    {
        var profile = CreateSyntheticProfile();
        var stream = new GeneratedResponseStream(0, blockReads: true);
        var handler = new SyntheticResponseHandler(stream);
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(300)
        };
        var provider = new SinopacPaymentProvider(client);

        var result = await ((IPaymentRecurring)provider).QueryRecurringAsync(
            profile,
            new RecurringQueryRequest
            {
                ProfileName = profile.Name,
                MasterOrderNo = "SYNTHETIC-REGULAR-MASTER-001"
            },
            CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Kind.Should().Be(PaymentErrorKind.NetworkFailure);
        result.Error.Code.Should().Be("Nonce:Timeout");
        handler.RequestCount.Should().Be(1);
        stream.IsDisposed.Should().BeTrue();
    }

    /// <summary>
    /// 規格名稱沿用 F10 的 log 驗收；Payments core 沒有直接 logger，故以實際 diagnostics sanitizer 邊界作決定性證據。
    /// 這不是 host 的 Exception.log sink capture，報告仍會明列該實機／宿主整合邊界。
    /// </summary>
    [Fact]
    public void Logs_NoCardNumberOrBindToken()
    {
        var sanitized = PaymentDiagnosticsSanitizer.Sanitize(
            new Dictionary<string, string>
            {
                ["card_number"] = "4111111111111111",
                ["bind_token"] = SyntheticBindToken,
                ["order_no"] = "SYNTHETIC-ORDER-001"
            });

        sanitized["card_number"].Should().NotBe("4111111111111111");
        sanitized["bind_token"].Should().NotBe(SyntheticBindToken);
        sanitized["order_no"].Should().Be("SYNTHETIC-ORDER-001");
    }

    /// <summary>
    /// 保護 callback 重送不會由 provider parser 直接發布成功或付款金額。
    /// parser 無跨請求狀態；測試只證明重送仍是同一查詢線索，不代替資料庫入帳冪等測試。
    /// </summary>
    [Fact]
    public async Task Callback_Duplicate_NotDoubleRecorded()
    {
        var profile = CreateSyntheticProfile();
        using var client = new HttpClient(new NeverCalledHandler());
        var provider = new SinopacPaymentProvider(client);
        var request = CreateCallback("CaptureOut");

        var first = await provider.ParseCallbackAsync(profile, request, CancellationToken.None);
        var duplicate = await provider.ParseCallbackAsync(profile, request, CancellationToken.None);

        first.Status.Should().Be(PaymentStatus.Pending);
        duplicate.Status.Should().Be(PaymentStatus.Pending);
        first.PaidAmount.Should().BeNull();
        duplicate.PaidAmount.Should().BeNull();
        first.ProviderTransactionId.Should().Be(SyntheticPayToken);
        duplicate.ProviderTransactionId.Should().Be(SyntheticPayToken);
        first.Acknowledgement.Should().Be(PaymentCallbackAcknowledgement.None);
        duplicate.Acknowledgement.Should().Be(PaymentCallbackAcknowledgement.None);
    }

    /// <summary>
    /// 保護請款通知先於授權通知時，兩個 callback 都不被當成付款結果；
    /// 最終狀態必須由銀行查詢及產品持久層原子冪等更新決定。
    /// </summary>
    [Fact]
    public async Task Callback_CaptureBeforeAuthNotice_IdempotentFinalState()
    {
        var profile = CreateSyntheticProfile();
        using var client = new HttpClient(new NeverCalledHandler());
        var provider = new SinopacPaymentProvider(client);

        var captureNotice = await provider.ParseCallbackAsync(
            profile,
            CreateCallback("CaptureOut"),
            CancellationToken.None);
        var authNotice = await provider.ParseCallbackAsync(
            profile,
            CreateCallback("PayOut"),
            CancellationToken.None);

        captureNotice.Status.Should().Be(PaymentStatus.Pending);
        authNotice.Status.Should().Be(PaymentStatus.Pending);
        captureNotice.ProviderTransactionId.Should().Be(authNotice.ProviderTransactionId);
        captureNotice.PaidAmount.Should().BeNull();
        authNotice.PaidAmount.Should().BeNull();
    }

    /// <summary>
    /// 建立含固定合成 ShopNo、AES key fragments、X-KeyID 與 sandbox 測試 URI 的 profile。
    /// 這些值不是 User Secrets，也不能用於真實商店或正式金流。
    /// </summary>
    private static PaymentMerchantProfile CreateSyntheticProfile()
    {
        return new PaymentMerchantProfile
        {
            Name = "SyntheticF10",
            Provider = PaymentProviderKind.Sinopac,
            Credentials = new Dictionary<string, string>
            {
                ["ShopNo"] = "SYNTHETIC-SHOP-001",
                ["A1"] = "0011223344556677",
                ["A2"] = "8899AABBCCDDEEFF",
                ["B1"] = "1021324354657687",
                ["B2"] = "98A9BACBDCEDFE0F",
                ["XKeyId"] = "SYNTHETIC-XKEY"
            },
            Endpoints = new Dictionary<string, string>
            {
                ["ApiBaseUrl"] = "https://sandbox.sinopac.test/QPay/"
            }
        };
    }

    /// <summary>
    /// 建立最小 callback 欄位投影；事件類型只是合成輸入，付款狀態仍不得由 callback 直接發布。
    /// </summary>
    private static PaymentCallbackRequest CreateCallback(string apiType)
    {
        return new PaymentCallbackRequest
        {
            ProfileName = "SyntheticF10",
            ProviderHint = PaymentProviderKind.Sinopac,
            Form = new Dictionary<string, string>
            {
                ["ShopNo"] = "SYNTHETIC-SHOP-001",
                ["PayToken"] = SyntheticPayToken,
                ["APType"] = apiType
            }
        };
    }

    /// <summary>
    /// 永不回應網路的 handler，供純 callback parser 測試使用；若解析路徑誤發 HTTP，測試立即失敗。
    /// </summary>
    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        /// <summary>以測試失敗拒絕任何網路傳輸，證明 callback parser 是同步無狀態解析。</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Callback parsing must not issue HTTP requests.");
        }
    }

    /// <summary>
    /// 為本文大小與逾時測試提供單案例 response stream；handler 與 HttpClient 不共用此可變狀態。
    /// 其唯一 Dispose owner 是包裝它的 StreamContent，並由 provider Dispose response 觸發。
    /// </summary>
    private sealed class GeneratedResponseStream : Stream
    {
        private readonly bool _blockReads;
        private int _remainingBytes;

        /// <summary>建立不配置整份本文的合成串流，並選擇立即產生位元組或等待取消。</summary>
        /// <param name="length">可產生的合成本文長度。</param>
        /// <param name="blockReads">為 true 時，非同步讀取只會在取消後結束。</param>
        public GeneratedResponseStream(int length, bool blockReads = false)
        {
            _remainingBytes = length;
            _blockReads = blockReads;
        }

        /// <summary>取得讀取量，以確定性驗證消費者在上限加一個位元組時停止。</summary>
        public int BytesRead { get; private set; }

        /// <summary>取得 stream 是否已由 response／content 的 owner 確定釋放。</summary>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public override bool CanRead => !IsDisposed;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var read = Math.Min(count, _remainingBytes);
            Array.Fill(buffer, (byte)'x', offset, read);
            _remainingBytes -= read;
            BytesRead += read;
            return read;
        }

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var read = Math.Min(buffer.Length, _remainingBytes);
            buffer[..read].Fill((byte)'x');
            _remainingBytes -= read;
            BytesRead += read;
            return read;
        }

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_blockReads)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Read(buffer.Span);
        }

        /// <inheritdoc />
        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 只對本測試的假請求回傳指定 stream；不記錄 request URI、標頭或本文。
    /// response content 對 stream 的唯一 owner，測試只查詢計數而不延長跨案例資源生命週期。
    /// </summary>
    private sealed class SyntheticResponseHandler : HttpMessageHandler
    {
        private readonly Stream _responseStream;
        private int _requestCount;

        /// <summary>建立持有單一 response stream 的合成 HTTP handler。</summary>
        /// <param name="responseStream">本案例專屬、由 response content 負責釋放的串流。</param>
        public SyntheticResponseHandler(Stream responseStream)
        {
            _responseStream = responseStream;
        }

        /// <summary>取得送入假傳輸的請求數，不保留請求或任何付款識別。</summary>
        public int RequestCount => Volatile.Read(ref _requestCount);

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestCount);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(_responseStream)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// 對單一案例提供兩階段 QPay 回應：Nonce 與簽章加密的成功結果。
    /// Handler 只保存測試觀察所需的服務名、命令、排程數值與布林值，不保留 token 本文。
    /// </summary>
    private sealed class F10ProtocolHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        private readonly PaymentMerchantProfile _profile;
        private int _requestCount;

        /// <summary>建立綁定本測試 profile 的合成傳輸 owner，避免不同案例共用商店狀態。</summary>
        public F10ProtocolHandler(PaymentMerchantProfile profile)
        {
            _profile = profile;
        }

        /// <summary>是否刻意讓兩筆扣款子單具有相同 OrderNo，以注入穩定 ID 衝突。</summary>
        public bool UseDuplicateRecurringOrderNo { get; set; }

        /// <summary>是否注入超過本地 1,000 筆上限的定期扣款候選。</summary>
        public bool UseTooManyRecurringCharges { get; set; }

        /// <summary>取得本案例目前送入合成傳輸的請求數，不保留 request 或其本文。</summary>
        public int RequestCount => Volatile.Read(ref _requestCount);

        /// <summary>可選擇以有效簽章回傳錯誤商店代號，供 provider-side scope 驗證測試使用。</summary>
        public string? ReturnedShopNo { get; set; }

        /// <summary>可選擇以有效簽章回傳錯誤綁卡編號，供 provider-side correlation 驗證測試使用。</summary>
        public string? ReturnedBindNo { get; set; }

        /// <summary>最近一筆訂單封包所使用的 APIService；不保存完整外層本文。</summary>
        public string CapturedApiService { get; private set; } = string.Empty;

        /// <summary>最近一筆封包的簽章，用於固定 golden 向量比對。</summary>
        public string CapturedSignature { get; private set; } = string.Empty;

        /// <summary>最近一筆封包的密文本文，用於固定 golden 向量比對。</summary>
        public string CapturedEncryptedMessage { get; private set; } = string.Empty;

        /// <summary>最近一筆解密 request 的訂單維護命令；不保留原始 JSON。</summary>
        public string CapturedCommand { get; private set; } = string.Empty;

        /// <summary>最近一筆建立付款 request 的 PayTypeSub。</summary>
        public string CapturedPayTypeSub { get; private set; } = string.Empty;

        /// <summary>最近一筆建立付款 request 的定期扣款總期數。</summary>
        public int? CapturedDeductTotalNum { get; private set; }

        /// <summary>最近一筆建立付款 request 的定期扣款週期類型。</summary>
        public string CapturedPeriodType { get; private set; } = string.Empty;

        /// <summary>最近一筆建立付款 request 的定期扣款頻率。</summary>
        public int? CapturedDeductFreq { get; private set; }

        /// <summary>解密 request 時是否看見預期綁卡 token；不保留該 token 字串。</summary>
        public bool OrderCreateContainedBindToken { get; private set; }

        /// <summary>傳送中的外層 JSON 是否意外含明文綁卡 token。</summary>
        public bool OrderCreateOuterJsonContainedBindToken { get; private set; }

        /// <summary>
        /// 回覆固定 Nonce，解密並只投影必要欄位，再回傳簽章與 AES 加密的合成 response。
        /// Request、response 及每次 HTTP message 的實際 Dispose owner 是 HttpClient／provider 呼叫端。
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestCount);
            if (request.RequestUri!.AbsolutePath.EndsWith("/Nonce", StringComparison.Ordinal))
            {
                return JsonResponse("{\"Nonce\":\"" + SyntheticNonce + "\"}");
            }

            var wireJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            var envelope = JsonConvert.DeserializeObject<SinopacWebApiMessage>(wireJson)
                ?? throw new InvalidOperationException("Synthetic request envelope was empty.");
            CapturedApiService = envelope.APIService;
            CapturedSignature = envelope.Sign;
            CapturedEncryptedMessage = envelope.Message;

            var aesKey = SinopacCrypto.BuildAesKey(_profile);
            var requestJson = SinopacCrypto.Decrypt(aesKey, envelope.Message, envelope.Nonce);
            var requestObject = JObject.Parse(requestJson);
            CapturedCommand = (string?)requestObject["Command"] ?? string.Empty;

            if (string.Equals(envelope.APIService, "OrderCreate", StringComparison.Ordinal))
            {
                var cardParam = requestObject["CardParam"];
                CapturedPayTypeSub = (string?)cardParam?["PayTypeSub"] ?? string.Empty;
                CapturedDeductTotalNum = (int?)cardParam?["DeductTotalNum"];
                CapturedPeriodType = (string?)cardParam?["PeriodType"] ?? string.Empty;
                CapturedDeductFreq = (int?)cardParam?["DeductFreq"];
                OrderCreateContainedBindToken =
                    string.Equals((string?)cardParam?["BindToken"], SyntheticBindToken, StringComparison.Ordinal);
                OrderCreateOuterJsonContainedBindToken = wireJson.Contains(
                    SyntheticBindToken,
                    StringComparison.Ordinal);
            }

            var response = BuildResponse(envelope.APIService, requestObject);
            var responseJson = JsonConvert.SerializeObject(response, SerializerSettings);
            var responseEnvelope = new SinopacWebApiMessage
            {
                Version = "1.0.0",
                ShopNo = envelope.ShopNo,
                APIService = envelope.APIService,
                Nonce = envelope.Nonce,
                Message = SinopacCrypto.Encrypt(aesKey, responseJson, envelope.Nonce),
                Sign = SinopacSigner.GenerateSign(response, aesKey, envelope.Nonce)
            };
            return JsonResponse(JsonConvert.SerializeObject(responseEnvelope, SerializerSettings));
        }

        /// <summary>依 APIService 建立與 V2.6 欄位相符的合成 response，不向外部系統送出請求。</summary>
        private object BuildResponse(string apiService, JObject request)
        {
            return apiService switch
            {
                "CardBindCreate" => new
                {
                    ShopNo = ReturnedShopNo ?? _profile.Credentials["ShopNo"],
                    BindNo = (string?)request["BindNo"] ?? string.Empty,
                    CardBindURL = "https://sandbox.sinopac.test/card-bind",
                    Status = "S",
                    Description = string.Empty
                },
                "OrderMaintain" => new
                {
                    OrderNo = (string?)request["OrderNo"] ?? string.Empty,
                    ShopNo = _profile.Credentials["ShopNo"],
                    TSNo = "SYNTHETIC-TSNO-001",
                    Command = (string?)request["Command"] ?? string.Empty,
                    Date = "202611260200",
                    Status = "S",
                    Description = string.Empty
                },
                "OrderPayQuery" => new
                {
                    ShopNo = ReturnedShopNo ?? _profile.Credentials["ShopNo"],
                    PayToken = SyntheticPayToken,
                    Date = "202611260200",
                    Status = "S",
                    Description = string.Empty,
                    TSResultContent = new
                    {
                        APType = "CardBindOut",
                        TSNo = "SYNTHETIC-TSNO-BIND",
                        OrderNo = "SYNTHETIC-BIND-ORDER",
                        ShopNo = ReturnedShopNo ?? _profile.Credentials["ShopNo"],
                        PayType = "C",
                        Amount = "100",
                        Status = "S",
                        Description = string.Empty,
                        BindNo = ReturnedBindNo ?? SyntheticBindNo,
                        BindToken = SyntheticBindToken,
                        LeftCCNo = "411111",
                        RightCCNo = "1111"
                    }
                },
                "RegularQuery" => BuildRecurringResponse(),
                "OrderCreate" => new
                {
                    OrderNo = (string?)request["OrderNo"] ?? string.Empty,
                    ShopNo = _profile.Credentials["ShopNo"],
                    TSNo = "SYNTHETIC-TSNO-CREATE",
                    PayType = (string?)request["PayType"] ?? string.Empty,
                    Amount = (int?)request["Amount"] ?? 0,
                    Status = "S",
                    Description = string.Empty,
                    CardParam = new
                    {
                        CardPayURL = "https://sandbox.sinopac.test/payment"
                    }
                },
                _ => throw new InvalidOperationException("Unexpected synthetic APIService.")
            };
        }

        /// <summary>建立主單與兩筆扣款子單，並可注入兩筆共用 OrderNo 的資料衝突。</summary>
        private object BuildRecurringResponse()
        {
            var chargeCount = UseTooManyRecurringCharges ? 1001 : 2;
            return new
            {
                ShopNo = _profile.Credentials["ShopNo"],
                Date = "202611260200",
                Status = "S",
                Description = "S0000",
                Order = new
                {
                    MasterOrderNo = "SYNTHETIC-REGULAR-MASTER-001",
                    OrderTime = "202611250900",
                    DeductTotalNum = 12,
                    Amount = 120000,
                    ExpectDeductDate = "20261225",
                    FreqNum = 1,
                    FreqType = "M",
                    DeductNum = 2,
                    RegularStatus = "01",
                    StopDeductTime = string.Empty,
                    Remarks = string.Empty,
                    Info = Enumerable.Range(1, chargeCount)
                        .Select(number => new
                        {
                            RegularNum = number,
                            OrderNo = number == 2 && UseDuplicateRecurringOrderNo
                                ? "SYNTHETIC-REGULAR-001"
                                : $"SYNTHETIC-REGULAR-{number:000}",
                            DeductTime = number == 1
                                ? "202611250900"
                                : "202612250900",
                            DeductStatus = "00"
                        })
                        .ToArray()
                }
            };
        }

        /// <summary>建立 JSON HTTP response；呼叫端透過 HttpClient 釋放 request 與 response。</summary>
        private static HttpResponseMessage JsonResponse(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
