/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 Payments provider 的永豐撥款查詢契約；測試只涵蓋協定 mapping、Nonce／AES／簽章傳輸及可信回應轉成中立撥款批次，
 * 不執行 reconciliation、CRM 更新或任何實際付款。
 *
 * 主要功能與流程：以固定 nonce、明確合成憑證及手工固定的 request envelope 驗證 AllotQuery 服務名稱與欄位序列化；
 * response 測試僅讀取核准的 Golden/Sinopac/allot-query-response.json，經 fake HTTP handler 回應 nonce、密文與簽章；
 * 合成 APIM 狀態、缺少 Nonce、逾時與惡意本文案例鎖定階段碼分類及不洩漏契約。
 * 另以兩個不同 profile 同時呼叫同一 provider，確認 X-KeyID 僅掛在各自的 HttpRequestMessage、兩筆 Nonce 可併行，
 * HttpClient 不保留 profile 標頭；斷言並涵蓋批次日期、TWD Money 與簽章錯誤時不發布批次。
 *
 * 使用邊界與維護：憑證、nonce、商店與資料均為合成固定值，測試不連網、不讀取部署設定或 User Secrets；
 * fake handler 僅在單一測試內持有 Golden 內容，HttpClient 由 using 擁有，request／response 由 provider 的逐次呼叫範圍釋放。
 * JSON 欄位、密文或驗簽結果不符即明確失敗；不得以被測輸出重生期待值或修改核准 Golden。
 */
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Newtonsoft.Json;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.DependencyInjection;
using SpeechMessage.Localization;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Providers.Sinopac;
using Xunit;

namespace SpeechMessage.Payments.Tests.Providers.Sinopac;

/// <summary>
/// 鎖定永豐 AllotQuery 的合成請求向量、已驗簽回應映射及安全失敗診斷。
/// 假 HTTP 傳輸注入固定 nonce、APIM 狀態、逾時與合成 response envelope；斷言秘密與任意本文不離開 provider 邊界。
/// </summary>
public sealed class SinopacAllotQueryTests
{
    private const string SyntheticNonce = "NONCE-SYNTHETIC-001";
    private const string SyntheticAesKey = "00112233445566778899AABBCCDDEEFF";
    private const string SyntheticInnerRequest =
        "{\"ShopNo\":\"SYNTHETIC-SHOP-001\",\"AllotDateS\":\"20260701\",\"AllotDateE\":\"20260708\",\"PayType\":\"C\"}";
    private const string SyntheticRequestSign =
        "2848E669FC390EAD2540C9FDAEBE212D539CD5557A8A97BF91EB83EAAE4A3968";
    private const string SyntheticRequestMessage =
        "A94A869B86AA06E49A8F0F5F7E0363B619A8DC702CAA42CD28C9AFAF3ACCBF0F7DE8FB9F04C9689FA9D8127A8D0818BE6B54ABE91A73394608052740BA8462F4935865D18E953FD3B754E633F5ED56B641D5E30F889C169BC5CCD7C4ADF63559";

    /// <summary>
    /// 與 provider 對齊的測試序列化規則；由測試自行擁有，避免暴露 provider 私有實作設定。
    /// </summary>
    private static readonly JsonSerializerSettings SyntheticSerializerSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore
    };

    /// <summary>
    /// 保護核准的 legacy request 欄位名稱、順序、JSON、簽章、AES 封裝及 APIService="AllotQuery"。
    /// 假 handler 固定回傳 nonce；斷言兩次 HTTP 呼叫的第二個 body 與獨立固定向量逐字相同。
    /// </summary>
    [Fact]
    public async Task AllotQuery_Request_MatchesGolden()
    {
        var handler = new CapturingNonceHandler(SyntheticNonce);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);
        var request = new PaymentAllotQueryRequest
        {
            ProfileName = "SyntheticTestOnly",
            FromDate = new DateOnly(2026, 7, 1),
            ToDate = new DateOnly(2026, 7, 8),
            PaymentType = "C"
        };
        var profile = CreateSyntheticProfile();

        var payload = SinopacRequestMapper.MapAllotQuery(profile, request);
        var result = await provider.QueryAllotAsync(profile, request, CancellationToken.None);

        payload.ShopNo.Should().Be("SYNTHETIC-SHOP-001");
        JsonConvert.SerializeObject(payload, SyntheticSerializerSettings)
            .Should()
            .Be(SyntheticInnerRequest);
        handler.RequestBodies.Should().HaveCount(2);
        JsonConvert.DeserializeObject<SinopacWebApiMessage>(handler.RequestBodies[1])
            .Should()
            .BeEquivalentTo(new SinopacWebApiMessage
            {
                Version = "1.0.0",
                ShopNo = "SYNTHETIC-SHOP-001",
                APIService = "AllotQuery",
                Sign = SyntheticRequestSign,
                Nonce = SyntheticNonce,
                Message = SyntheticRequestMessage
            });
        result.Error.Kind.Should().Be(PaymentErrorKind.SerializationFailure);
        result.Batches.Should().BeEmpty();
    }

    /// <summary>
    /// 保護核准 response Golden 經 Nonce、AES 解密與回應簽章驗證後才轉成撥款批次。
    /// 逐欄斷言日期與 12345/100、45/100 的 TWD Money；再注入錯誤簽章，確認分類為 SignatureInvalid 且批次為空。
    /// </summary>
    [Fact]
    public async Task AllotQuery_Response_MapsBatchesToMoney()
    {
        var golden = ReadAllotGolden();
        var successHandler = CreateAllotResponseHandler(golden, tamperSignature: false);
        using var successClient = new HttpClient(successHandler);
        var provider = new SinopacPaymentProvider(successClient);
        var request = CreateQueryRequest();

        var result = await provider.QueryAllotAsync(
            CreateSyntheticProfile(),
            request,
            CancellationToken.None);

        result.Error.Should().Be(PaymentError.None);
        result.ProviderStatus.Should().Be("S");
        result.ProviderDescription.Should().Be("SYNTHETIC-OK");
        result.Batches.Should().ContainSingle();
        result.Batches[0].AllotNo.Should().Be("SYN-ALLOT-001");
        result.Batches[0].AllotDate.Should().Be(new DateOnly(2026, 7, 7));
        result.Batches[0].Items.Should().ContainSingle();
        result.Batches[0].Items[0].OrderNo.Should().Be("SYN-CARD-001");
        result.Batches[0].Items[0].Amount.Should().Be(new Money(123.45m, "TWD"));
        result.Batches[0].Items[0].Fee.Should().Be(new Money(0.45m, "TWD"));

        var invalidSignatureHandler = CreateAllotResponseHandler(golden, tamperSignature: true);
        using var invalidSignatureClient = new HttpClient(invalidSignatureHandler);
        var invalidSignatureProvider = new SinopacPaymentProvider(invalidSignatureClient);
        var invalidResult = await invalidSignatureProvider.QueryAllotAsync(
            CreateSyntheticProfile(),
            request,
            CancellationToken.None);

        invalidResult.Error.Kind.Should().Be(PaymentErrorKind.SignatureInvalid);
        invalidResult.Batches.Should().BeEmpty();
    }

    /// <summary>
    /// 保護共享 provider 與 HttpClient 的跨 profile 隔離及併行能力。
    /// 假 handler 以確定性 barrier 要求兩個 profile 的 Nonce 同時抵達，並逐筆核對 request 標頭；
    /// 完成後再確認 HttpClient.DefaultRequestHeaders 沒有留下任何商店識別。
    /// </summary>
    [Fact]
    public async Task AllotQuery_ConcurrentProfiles_KeepXKeyIdsRequestScoped()
    {
        var golden = ReadAllotGolden();
        var response = CreateAllotResponseJson(golden, tamperSignature: false);
        var handler = new ConcurrentProfileHandler(SyntheticNonce, response);
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);
        var profileA = CreateSyntheticProfile("ProfileA", "SYNTHETIC-XKEY-A");
        var profileB = CreateSyntheticProfile("ProfileB", "SYNTHETIC-XKEY-B");
        using var operationCancellation = new CancellationTokenSource();
        using var completionDeadline = new CancellationTokenSource();

        var requests = Task.WhenAll(
            provider.QueryAllotAsync(profileA, CreateQueryRequest(), operationCancellation.Token),
            provider.QueryAllotAsync(profileB, CreateQueryRequest(), operationCancellation.Token));
        var reachedBothProfiles = await Task.WhenAny(
            requests,
            Task.Delay(TimeSpan.FromSeconds(5), completionDeadline.Token)) == requests;
        completionDeadline.Cancel();

        if (!reachedBothProfiles)
        {
            operationCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requests);
        }

        Assert.True(reachedBothProfiles, "Both profile requests must reach the transport concurrently.");
        var results = await requests;

        Assert.All(results, result => Assert.Equal(PaymentError.None, result.Error));
        Assert.Equal(2, handler.MaximumConcurrentNonceRequests);
        Assert.Equal(4, handler.RequestKeyIds.Length);
        Assert.Equal(2, handler.RequestKeyIds.Count(value => value == "SYNTHETIC-XKEY-A"));
        Assert.Equal(2, handler.RequestKeyIds.Count(value => value == "SYNTHETIC-XKEY-B"));
        Assert.Empty(handler.RequestsWithCookieHeader);
        Assert.False(client.DefaultRequestHeaders.Contains("X-KeyID"));
    }

    /// <summary>HTTP 401 的合成 Nonce 回應只發布階段、狀態與 APIM 代碼，並分類為不可重試設定錯誤。</summary>
    [Fact]
    public async Task AllotQuery_NonceHttp401_ReportsNonceStageAndIsNotRetryable()
    {
        var handler = new ScriptedResponseHandler(
            (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.Unauthorized, "{\"code\":\"9999\",\"message\":\"synthetic\"}")));
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await provider.QueryAllotAsync(
            CreateSyntheticProfile(),
            CreateQueryRequest(),
            CancellationToken.None);

        Assert.Equal(PaymentErrorKind.ConfigurationInvalid, result.Error.Kind);
        Assert.Equal("Nonce:401:9999", result.Error.Code);
        Assert.Equal("Nonce:401:9999", result.Error.Message);
    }

    /// <summary>HTTP 499 的合成 Order 回應保留 APIM 純數字代碼，但不保留回應本文。</summary>
    [Fact]
    public async Task AllotQuery_OrderHttp499_ReportsOrderStageAndApimCode()
    {
        var handler = new ScriptedResponseHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/Nonce", StringComparison.Ordinal)
                ? JsonResponse(HttpStatusCode.OK, "{\"Nonce\":\"NONCE-SYNTHETIC-001\"}")
                : JsonResponse(ToHttpStatusCode(499), "{\"code\":\"9998\",\"message\":\"synthetic body marker\"}")));
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await provider.QueryAllotAsync(
            CreateSyntheticProfile(),
            CreateQueryRequest(),
            CancellationToken.None);

        Assert.Equal(PaymentErrorKind.ConfigurationInvalid, result.Error.Kind);
        Assert.Equal("Order:499:9998", result.Error.Code);
        Assert.Equal("Order:499:9998", result.Error.Message);
        Assert.DoesNotContain("synthetic body marker", result.Error.Code, StringComparison.Ordinal);
    }

    /// <summary>HTTP 500 的合成 Order 回應可供 worker 重試，且診斷指出 Order 階段與狀態。</summary>
    [Fact]
    public async Task AllotQuery_OrderHttp500_IsRetryableWithStage()
    {
        var handler = new ScriptedResponseHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/Nonce", StringComparison.Ordinal)
                ? JsonResponse(HttpStatusCode.OK, "{\"Nonce\":\"NONCE-SYNTHETIC-001\"}")
                : JsonResponse(HttpStatusCode.InternalServerError, "{\"message\":\"synthetic body marker\"}")));
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await provider.QueryAllotAsync(
            CreateSyntheticProfile(),
            CreateQueryRequest(),
            CancellationToken.None);

        Assert.Equal(PaymentErrorKind.ProviderUnavailable, result.Error.Kind);
        Assert.Equal("Order:500", result.Error.Code);
        Assert.Equal("Order:500", result.Error.Message);
    }

    /// <summary>HTTP 200 但 Nonce 欄位空白須明確回報 NonceMissing，而非混成一般供應商不可用。</summary>
    [Fact]
    public async Task AllotQuery_NonceMissing_ReportsNonceMissing()
    {
        var handler = new ScriptedResponseHandler(
            (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"Nonce\":\"\"}")));
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await provider.QueryAllotAsync(
            CreateSyntheticProfile(),
            CreateQueryRequest(),
            CancellationToken.None);

        Assert.Equal(PaymentErrorKind.ProviderUnavailable, result.Error.Kind);
        Assert.Equal("Nonce:NonceMissing", result.Error.Code);
    }

    /// <summary>非呼叫端取消的合成 HTTP 等待逾時標記發生於 Order，且不攜帶例外本文。</summary>
    [Fact]
    public async Task AllotQuery_OrderTimeout_ReportsOrderTimeout()
    {
        var handler = new ScriptedResponseHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/Nonce", StringComparison.Ordinal)
                ? Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"Nonce\":\"NONCE-SYNTHETIC-001\"}"))
                : Task.FromException<HttpResponseMessage>(new TaskCanceledException("synthetic timeout detail")));
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await provider.QueryAllotAsync(
            CreateSyntheticProfile(),
            CreateQueryRequest(),
            CancellationToken.None);

        Assert.Equal(PaymentErrorKind.NetworkFailure, result.Error.Kind);
        Assert.Equal("Order:Timeout", result.Error.Code);
        Assert.Equal("Order:Timeout", result.Error.Message);
    }

    /// <summary>已驗簽的供應商 F 回應只保留文件格式代碼，不保留描述中的任意文字。</summary>
    [Fact]
    public async Task AllotQuery_ProviderStatusF_KeepsOnlyDocumentedCode()
    {
        var golden = ReadAllotGolden();
        var response = CreateAllotResponseJson(
            golden,
            tamperSignature: false,
            providerStatus: "F",
            providerDescription: "E3103 synthetic private detail");
        var handler = new ScriptedResponseHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/Nonce", StringComparison.Ordinal)
                ? JsonResponse(HttpStatusCode.OK, "{\"Nonce\":\"" + SyntheticNonce + "\"}")
                : JsonResponse(HttpStatusCode.OK, response)));
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await provider.QueryAllotAsync(
            CreateSyntheticProfile(),
            CreateQueryRequest(),
            CancellationToken.None);

        Assert.Equal(PaymentErrorKind.ProviderRejected, result.Error.Kind);
        Assert.Equal("E3103", result.ProviderDescription);
        Assert.DoesNotContain("synthetic private detail", result.ProviderDescription, StringComparison.Ordinal);
    }

    /// <summary>失敗回應含假秘密與本文時，所有公開錯誤文字均只能包含固定安全診斷碼。</summary>
    [Fact]
    public async Task AllotQuery_Diagnostics_NeverContainSecretsOrBody()
    {
        const string fakeSecret = "FAKE-XKEY-DO-NOT-LEAK";
        const string fakeNonce = "FAKE-NONCE-DO-NOT-LEAK";
        const string fakeBody = "FAKE-RESPONSE-BODY-DO-NOT-LEAK";
        var handler = new ScriptedResponseHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/Nonce", StringComparison.Ordinal)
                ? JsonResponse(HttpStatusCode.OK, "{\"Nonce\":\"" + SyntheticNonce + "\"}")
                : JsonResponse(HttpStatusCode.BadRequest,
                    "{\"code\":\"12345\",\"message\":\"" + fakeBody + fakeSecret + fakeNonce + "\"}")));
        using var client = new HttpClient(handler);
        var provider = new SinopacPaymentProvider(client);

        var result = await provider.QueryAllotAsync(
            CreateSyntheticProfile(xKeyId: fakeSecret),
            CreateQueryRequest(),
            CancellationToken.None);

        var diagnostics = string.Join("|", result.Error.Code, result.Error.Message, result.ProviderDescription);
        Assert.Equal("Order:400", result.Error.Code);
        Assert.DoesNotContain(fakeSecret, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(fakeNonce, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(fakeBody, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("12345", diagnostics, StringComparison.Ordinal);
    }

    /// <summary>
    /// 保護 DI 建立的永豐 transport 不保留 Cookie、不跟隨 redirect，也不採用主機預設認證。
    /// 測試建構正式 typed-client handler 但不送出網路請求，並由 ServiceProvider 確定性釋放 handler pool。
    /// </summary>
    [Fact]
    public void SinopacHttpHandler_DisablesCookiesAndRedirects()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var filter = new CapturingHandlerBuilderFilter();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(filter);
        services.AddSpeechMessagePayments(new ConfigurationBuilder().Build().GetSection("Payment"));

        using var serviceProvider = services.BuildServiceProvider();
        Assert.NotNull(serviceProvider.GetRequiredService<SinopacPaymentProvider>());
        var handler = Assert.IsType<HttpClientHandler>(filter.PrimaryHandler);

        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseDefaultCredentials);
    }

    /// <summary>建立固定日期及付款類型的合成撥款查詢輸入，不讀取環境設定。</summary>
    /// <returns>只供本測試使用的 provider-neutral 查詢請求。</returns>
    private static PaymentAllotQueryRequest CreateQueryRequest()
    {
        return new PaymentAllotQueryRequest
        {
            ProfileName = "SyntheticTestOnly",
            FromDate = new DateOnly(2026, 7, 1),
            ToDate = new DateOnly(2026, 7, 8),
            PaymentType = "C"
        };
    }

    /// <summary>建立不含真實憑證的單次永豐測試 profile；呼叫端不保存此 profile 至共享狀態。</summary>
    /// <returns>指向 synthetic.invalid 且使用固定合成 credential 的 profile。</returns>
    private static PaymentMerchantProfile CreateSyntheticProfile(
        string name = "SyntheticTestOnly",
        string xKeyId = "00000000-0000-0000-0000-000000000000")
    {
        return new PaymentMerchantProfile
        {
            Name = name,
            Provider = PaymentProviderKind.Sinopac,
            Credentials = new Dictionary<string, string>
            {
                ["ShopNo"] = "SYNTHETIC-SHOP-001",
                ["A1"] = "0011223344556677",
                ["A2"] = "0000000000000000",
                ["B1"] = "8899AABBCCDDEEFF",
                ["B2"] = "0000000000000000",
                ["XKeyId"] = xKeyId
            },
            Endpoints = new Dictionary<string, string>
            {
                ["ApiBaseUrl"] = "https://synthetic.invalid/QPay.WebAPI/api/"
            }
        };
    }

    /// <summary>讀取已核准的合成 response fixture；不連線 provider，也不改寫或重生 Golden。</summary>
    /// <returns>去除檔尾換行的 fixture JSON，以供固定測試向量加密。</returns>
    private static string ReadAllotGolden()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Golden",
            "Sinopac",
            "allot-query-response.json");
        return File.ReadAllText(path).TrimEnd('\r', '\n');
    }

    /// <summary>
    /// 以核准 Golden 及合成 key/nonce 建立假 provider 回應；可選擇竄改簽章以驗證 fail-closed。
    /// </summary>
    /// <param name="golden">唯讀取得的合成 response JSON。</param>
    /// <param name="tamperSignature">true 時只改變簽章首字元，注入驗簽失敗。</param>
    /// <returns>只在單一測試期間持有回應本文的 fake handler。</returns>
    private static CapturingNonceHandler CreateAllotResponseHandler(string golden, bool tamperSignature)
    {
        return new CapturingNonceHandler(SyntheticNonce, CreateAllotResponseJson(golden, tamperSignature));
    }

    /// <summary>以核准合成 Golden 產生單次假 response envelope，不讀取或輸出商店設定。</summary>
    private static string CreateAllotResponseJson(
        string golden,
        bool tamperSignature,
        string? providerStatus = null,
        string? providerDescription = null)
    {
        var response = JsonConvert.DeserializeObject<SinopacAllotQueryResponse>(golden)
            ?? throw new InvalidOperationException("Synthetic allot-query Golden is empty.");
        if (providerStatus is not null)
            response.Status = providerStatus;
        if (providerDescription is not null)
            response.Description = providerDescription;
        var aesKey = SyntheticAesKey;
        var responseJson = JsonConvert.SerializeObject(response, SyntheticSerializerSettings);
        var encrypted = SinopacCrypto.Encrypt(aesKey, responseJson, SyntheticNonce);
        var sign = SinopacSigner.GenerateSign(response, aesKey, SyntheticNonce);
        if (tamperSignature)
        {
            sign = sign[0] == '0' ? "1" + sign[1..] : "0" + sign[1..];
        }

        var envelope = new SinopacWebApiMessage
        {
            Version = "1.0.0",
            ShopNo = "SYNTHETIC-SHOP-001",
            APIService = "AllotQuery",
            Sign = sign,
            Nonce = SyntheticNonce,
            Message = encrypted
        };

        return JsonConvert.SerializeObject(envelope, SyntheticSerializerSettings);
    }

    /// <summary>
    /// 僅回覆固定 nonce 與預先構造的合成 envelope，並在本測試期間記錄 request body 以比對固定向量。
    /// Handler 不連外網；HttpClient Dispose 後 handler 及其測試資料不再被保留。
    /// </summary>
    private sealed class ScriptedResponseHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        /// <summary>依當次合成 request 回傳受控回應，不建立外網連線或保存跨測試狀態。</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpStatusCode ToHttpStatusCode(int statusCode) => (HttpStatusCode)statusCode;

    private sealed class CapturingNonceHandler : HttpMessageHandler
    {
        private readonly string _nonce;
        private readonly string? _orderResponse;

        /// <summary>建立只回傳合成 nonce 與預先建構 response 的 handler，沒有外部連線能力。</summary>
        /// <param name="nonce">測試固定 nonce。</param>
        /// <param name="orderResponse">選用的 synthetic Order response JSON。</param>
        public CapturingNonceHandler(string nonce, string? orderResponse = null)
        {
            _nonce = nonce;
            _orderResponse = orderResponse;
        }

        /// <summary>保留本測試內捕獲的 request JSON，供固定封包契約斷言。</summary>
        public List<string> RequestBodies { get; } = new();

        /// <summary>記錄單次測試 request 並回傳 synthetic nonce 或 Order response；取消依 caller token 傳遞。</summary>
        /// <param name="request">provider 建立的 HTTP request。</param>
        /// <param name="cancellationToken">由 HttpClient 傳入的測試呼叫取消權杖。</param>
        /// <returns>由此 handler 建立、交由 HttpClient/provider dispose 的 synthetic response。</returns>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);

            var responseBody = request.RequestUri!.AbsolutePath.EndsWith("/Nonce", StringComparison.Ordinal)
                ? JsonConvert.SerializeObject(new SinopacNonceResponse { Nonce = _nonce })
                : _orderResponse ?? "{\"unreadable\":\"synthetic\"}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>
    /// 只接受合成 profile 的假傳輸，透過兩個 Nonce 同時到達的 barrier 驗證 provider 未全域序列化不同商店。
    /// 每次 request 都讀取自己的標頭；不建立 socket、cookie store、背景工作或跨測試狀態。
    /// </summary>
    private sealed class ConcurrentProfileHandler : HttpMessageHandler
    {
        private readonly string _nonce;
        private readonly string _orderResponse;
        private readonly TaskCompletionSource<bool> _bothNonceRequests =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _requestKeyIds = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _requestsWithCookieHeader = new();
        private int _nonceRequests;
        private int _activeNonceRequests;
        private int _maximumConcurrentNonceRequests;

        /// <summary>建立僅回應固定 nonce 與核准合成 envelope 的傳輸替身。</summary>
        internal ConcurrentProfileHandler(string nonce, string orderResponse)
        {
            _nonce = nonce;
            _orderResponse = orderResponse;
        }

        /// <summary>取得每一筆 HTTP request 自身攜帶的 X-KeyID 值。</summary>
        internal string[] RequestKeyIds => _requestKeyIds.ToArray();

        /// <summary>取得是否有 request 收到 Cookie 標頭；profile 傳輸不得共享 cookie session。</summary>
        internal string[] RequestsWithCookieHeader => _requestsWithCookieHeader.ToArray();

        /// <summary>取得 fake transport 同時處理的 Nonce request 峰值。</summary>
        internal int MaximumConcurrentNonceRequests => Volatile.Read(ref _maximumConcurrentNonceRequests);

        /// <summary>檢查單筆 request 的 profile 標頭並在兩個 Nonce 都到達後回覆。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues("X-KeyID", out var keyIds))
            {
                foreach (var keyId in keyIds)
                {
                    _requestKeyIds.Enqueue(keyId);
                }
            }

            if (request.Headers.Contains("Cookie"))
            {
                _requestsWithCookieHeader.Enqueue("Cookie");
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/Nonce", StringComparison.Ordinal))
            {
                var active = Interlocked.Increment(ref _activeNonceRequests);
                UpdateMaximum(active);
                if (Interlocked.Increment(ref _nonceRequests) == 2)
                {
                    _bothNonceRequests.TrySetResult(true);
                }

                try
                {
                    await _bothNonceRequests.Task.WaitAsync(cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeNonceRequests);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonConvert.SerializeObject(new SinopacNonceResponse { Nonce = _nonce }),
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_orderResponse, Encoding.UTF8, "application/json")
            };
        }

        /// <summary>以原子更新保存同時進入數，不將 request 或 profile 狀態發布到其他測試。</summary>
        private void UpdateMaximum(int active)
        {
            var observed = Volatile.Read(ref _maximumConcurrentNonceRequests);
            while (active > observed)
            {
                var previous = Interlocked.CompareExchange(
                    ref _maximumConcurrentNonceRequests,
                    active,
                    observed);
                if (previous == observed)
                {
                    return;
                }

                observed = previous;
            }
        }
    }

    /// <summary>
    /// 觀察正式 IHttpClientFactory 完成配置後的 primary handler；只在單一測試 provider 中持有該參照，
    /// 不 Dispose handler，因為 HttpClientFactory／ServiceProvider 擁有其 pool 與關閉生命週期。
    /// </summary>
    private sealed class CapturingHandlerBuilderFilter : IHttpMessageHandlerBuilderFilter
    {
        /// <summary>取得 typed client 建立後的 primary handler，僅供同一案例檢查安全設定。</summary>
        internal HttpMessageHandler? PrimaryHandler { get; private set; }

        /// <summary>在既有 builder 設定鏈完成後保存當次 primary handler 的測試參照。</summary>
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        {
            return builder =>
            {
                next(builder);
                PrimaryHandler = builder.PrimaryHandler;
            };
        }
    }
}
