/*
 * 檔案整體說明
 *
 * 目的與定位：永豐 QPay provider 實作，宣告支援 TWD，並集中處理 Nonce、AES、簽章、建單、付款查詢及撥款查詢封包。
 *
 * 主要功能與流程：
 * 本檔宣告 SinopacPaymentProvider、SinopacRequestException、SinopacSignatureException。
 * 主要操作入口包含 CreatePaymentAsync、QueryPaymentAsync、ParseCallbackAsync、ResolveEndpoint、BuildCreateProviderData、
 * QueryAllotAsync、ResolveCreateResult、CreateMissingCreateFieldError、BuildQueryProviderData；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本 provider 只借用 DI 提供且由 Host 管理生命週期的 HttpClient；每次請求／回應由 using 關閉，序列化金額以 Money 為核心來源。
 * 撥款查詢只將固定階段、數字 HTTP 狀態與符合格式的短 APIM code 暴露給呼叫端；任何回應本文都不進例外訊息或診斷欄位。
 * 請求 profile 是單次呼叫輸入，不快取憑證；X-KeyID 僅寫入該次 HttpRequestMessage，不使用共享 DefaultRequestHeaders 或跨呼叫鎖。
 * typed HttpClient 的共用 handler 關閉 cookie 與自動轉址，避免商店 session 被保留或敏感標頭跟隨轉址；DI 是 handler 的唯一 owner。
 * 可沿 SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;
using SpeechMessage.Localization;

namespace SpeechMessage.Payments.Providers.Sinopac;

/// <summary>
/// 永豐 QPay provider 實作。
/// 這裡集中處理 Nonce、AES 加解密、簽章、建單與查詢封包；
/// 宿主產品端只拿 normalized result，不再自行組永豐 protocol。
/// </summary>
internal sealed class SinopacPaymentProvider : IPaymentProvider, IPaymentAllotProvider
{
    private const string CurrentVersion = "1.0.0";

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore
    };

    private readonly HttpClient _httpClient;
    private static readonly IReadOnlyCollection<string> SupportedCurrencyCodes = Array.AsReadOnly(new[] { "TWD" });

    /// <summary>
    /// 建立永豐 provider；HttpClient 由 DI typed-client 註冊管理，本類別只借用該 client，
    /// profile 憑證只在各方法呼叫期間使用。
    /// </summary>
    /// <param name="httpClient">由 DI 提供且由 Host 管理生命週期的 HTTP client。</param>
    public SinopacPaymentProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>提供 Gateway 對應永豐 profile 的固定 provider 種類。</summary>
    public PaymentProviderKind ProviderKind => PaymentProviderKind.Sinopac;

    /// <summary>永豐 QPay 建單僅接受 TWD；回應金額也以 TWD Money 正規化。</summary>
    public IReadOnlyCollection<string> SupportedCurrencies => SupportedCurrencyCodes;

    /// <summary>建立永豐付款；Money 金額由 mapper 依 TWD 金額契約轉成整數分，所有 HTTP I/O 都傳遞呼叫端取消權杖。</summary>
    /// <param name="profile">resolver 選定的單商店設定，只供此操作使用，不會快取憑證。</param>
    /// <param name="request">含 Money 總額及中立付款欄位的建立請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞至各次 HTTP 傳輸。</param>
    /// <returns>永豐回應正規化後的建立結果；預期 provider 錯誤轉為 PaymentError。</returns>
    public async Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request,
        CancellationToken cancellationToken)
    {
        SinopacOrderCreateRequest payload;
        try
        {
            payload = SinopacRequestMapper.MapCreateRequest(profile, request);
            var response = await SendOrderAsync<SinopacOrderCreateRequest, SinopacOrderCreateResponse>(
                profile,
                payload,
                SinopacApiService.OrderCreate,
                cancellationToken);

            return ResolveCreateResult(payload, response, request.ProductOrderId);
        }
        catch (PaymentConfigurationException ex)
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.ConfigurationInvalid, ex.Message);
        }
        catch (SinopacRequestException ex)
        {
            return CreateError(request.ProductOrderId, ex.ErrorKind, ex.DiagnosticCode);
        }
        catch (SinopacSignatureException ex)
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.SignatureInvalid, ex.Message);
        }
        catch (JsonException ex)
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.SerializationFailure, ex.Message);
        }
        catch (CryptographicException ex)
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.SerializationFailure, ex.Message);
        }
        catch (FormatException ex)
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.SerializationFailure, ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.NetworkFailure, ex.Message);
        }
        catch (Exception ex) when (IsNetworkTransportException(ex))
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.NetworkFailure, ex.Message);
        }
    }

    /// <summary>查詢永豐付款狀態，將最小單位金額解析成 TWD Money，並驗證回應簽章後才發布結果。</summary>
    /// <param name="profile">resolver 選定的單商店設定，只供此操作使用。</param>
    /// <param name="request">含 provider 交易參照的中立查詢請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞至 HTTP 傳輸。</param>
    /// <returns>已正規化且簽章驗證完成的狀態結果。</returns>
    public async Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentMerchantProfile profile,
        PaymentQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderOrderRef))
        {
            return QueryError(request, PaymentErrorKind.RequestInvalid, "Sinopac payment query requires ProviderOrderRef.");
        }

        try
        {
            var payload = SinopacRequestMapper.MapOrderPayQuery(profile, request);
            var response = await SendOrderAsync<SinopacOrderPayQueryRequest, SinopacOrderPayResponse>(
                profile,
                payload,
                SinopacApiService.OrderPayQuery,
                cancellationToken);
            var transaction = response.TSResultContent;
            var status = SinopacStatusMapper.Map(response);

            return new PaymentStatusResult
            {
                Status = status,
                ProductOrderId = FirstNonEmpty(transaction?.OrderNo ?? string.Empty, request.ProductOrderId),
                ProviderOrderRef = FirstNonEmpty(response.PayToken, request.ProviderOrderRef),
                ProviderTransactionId = transaction?.TSNo ?? string.Empty,
                PaidAmount = ParseMinorAmount(transaction?.Amount),
                Error = SinopacStatusMapper.IsProviderRejected(response)
                    ? new PaymentError
                    {
                        Kind = PaymentErrorKind.ProviderRejected,
                        Code = FirstNonEmpty(transaction?.Status ?? string.Empty, response.Status),
                        Message = FirstNonEmpty(transaction?.Description ?? string.Empty, response.Description)
                    }
                    : PaymentError.None,
                ProviderData = PaymentDiagnosticsSanitizer.Sanitize(BuildQueryProviderData(response)),
                Diagnostics = PaymentDiagnosticsSanitizer.Sanitize(BuildQueryDiagnostics(response))
            };
        }
        catch (PaymentConfigurationException ex)
        {
            return QueryError(request, PaymentErrorKind.ConfigurationInvalid, ex.Message);
        }
        catch (SinopacRequestException ex)
        {
            return QueryError(request, ex.ErrorKind, ex.DiagnosticCode);
        }
        catch (SinopacSignatureException ex)
        {
            return QueryError(request, PaymentErrorKind.SignatureInvalid, ex.Message);
        }
        catch (JsonException ex)
        {
            return QueryError(request, PaymentErrorKind.SerializationFailure, ex.Message);
        }
        catch (CryptographicException ex)
        {
            return QueryError(request, PaymentErrorKind.SerializationFailure, ex.Message);
        }
        catch (FormatException ex)
        {
            return QueryError(request, PaymentErrorKind.SerializationFailure, ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            return QueryError(request, PaymentErrorKind.NetworkFailure, ex.Message);
        }
        catch (Exception ex) when (IsNetworkTransportException(ex))
        {
            return QueryError(request, PaymentErrorKind.NetworkFailure, ex.Message);
        }
    }

    /// <summary>
    /// 查詢永豐撥款批次；使用同一 Nonce→AES→Sign 封包流程，只有 response 驗簽完成且狀態成功才建立批次結果。
    /// provider 錯誤轉為固定分類與安全訊息，避免把回應本文、憑證或 transport 例外文字帶入結果。
    /// </summary>
    /// <param name="profile">由 gateway resolver 選定的單一商店 profile，僅供本次操作使用。</param>
    /// <param name="request">含查詢日期及 C/M/A 付款類型的中立請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞至 Nonce 與 Order HTTP 傳輸。</param>
    /// <returns>已完成驗簽的撥款批次，或標準化的錯誤結果；簽章錯誤時批次為空。</returns>
    public async Task<PaymentAllotQueryResult> QueryAllotAsync(
        PaymentMerchantProfile profile,
        PaymentAllotQueryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = SinopacRequestMapper.MapAllotQuery(profile, request);
            var response = await SendOrderAsync<SinopacAllotQueryRequest, SinopacAllotQueryResponse>(
                profile,
                payload,
                SinopacApiService.AllotQuery,
                cancellationToken);

            if (!string.Equals(response.Status, "S", StringComparison.OrdinalIgnoreCase))
            {
                var providerDescription = SafeProviderDescription(response.Status, response.Description);
                return new PaymentAllotQueryResult
                {
                    ProviderStatus = response.Status,
                    ProviderDescription = providerDescription,
                    Error = new PaymentError
                    {
                        Kind = PaymentErrorKind.ProviderRejected,
                        Code = providerDescription,
                        Message = providerDescription
                    }
                };
            }

            return new PaymentAllotQueryResult
            {
                ProviderStatus = response.Status,
                ProviderDescription = SafeProviderDescription(response.Status, response.Description),
                Batches = MapAllotBatches(response.Allot)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PaymentConfigurationException)
        {
            return AllotQueryError(PaymentErrorKind.ConfigurationInvalid, "ConfigurationInvalid");
        }
        catch (ArgumentException)
        {
            return AllotQueryError(PaymentErrorKind.RequestInvalid, "RequestInvalid");
        }
        catch (SinopacRequestException ex)
        {
            return AllotQueryError(ex.ErrorKind, ex.DiagnosticCode);
        }
        catch (SinopacSignatureException)
        {
            return AllotQueryError(PaymentErrorKind.SignatureInvalid, "SignatureInvalid");
        }
        catch (JsonException)
        {
            return AllotQueryError(PaymentErrorKind.SerializationFailure, "Order:Unreadable");
        }
        catch (CryptographicException)
        {
            return AllotQueryError(PaymentErrorKind.SerializationFailure, "Order:Unreadable");
        }
        catch (FormatException)
        {
            return AllotQueryError(PaymentErrorKind.SerializationFailure, "Order:Unreadable");
        }
        catch (OverflowException)
        {
            return AllotQueryError(PaymentErrorKind.SerializationFailure, "Order:Unreadable");
        }
        catch (OperationCanceledException)
        {
            return AllotQueryError(PaymentErrorKind.NetworkFailure, "Order:Timeout");
        }
        catch (Exception ex) when (IsNetworkTransportException(ex))
        {
            return AllotQueryError(PaymentErrorKind.NetworkFailure, "Order:Transport");
        }
    }

    /// <summary>
    /// 解析永豐 callback 的 ShopNo／PayToken 作為後續查詢線索；callback 欄位不直接代表已付款，
    /// 也不觸發產品資料更新。
    /// </summary>
    /// <param name="profile">resolver 選定的商店設定，用來比對 callback ShopNo。</param>
    /// <param name="request">宿主投影成 HTTP-neutral 型別的 callback 欄位。</param>
    /// <param name="cancellationToken">呼叫端取消權杖；此純解析路徑不建立網路 I/O。</param>
    /// <returns>中立 callback 結果與已清理的診斷欄位。</returns>
    public Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentMerchantProfile profile,
        PaymentCallbackRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(SinopacCallbackParser.Parse(request, profile));
    }

    private async Task<TResult> SendOrderAsync<TRequest, TResult>(
        PaymentMerchantProfile profile,
        TRequest payload,
        SinopacApiService apiService,
        CancellationToken cancellationToken)
        where TRequest : ISinopacRequest
    {
        // 永豐 QPay 每次呼叫都先取 Nonce，再用 A1/A2/B1/B2 推導出的 AES key 加密 Message。
        // AES key 的大小寫與位元組內容必須完全符合舊 QPay Toolkit，否則銀行會回 HTTP 400。
        var aesKey = SinopacCrypto.BuildAesKey(profile);
        var nonceResponse = await PostJsonAsync<SinopacNonceResponse>(
            profile,
            "Nonce",
            new SinopacNonceRequest(payload.ShopNo),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(nonceResponse.Nonce))
        {
            throw new SinopacRequestException("Nonce", "NonceMissing", 200);
        }

        var innerJson = JsonConvert.SerializeObject(payload, SerializerSettings);
        var encryptedMessage = SinopacCrypto.Encrypt(aesKey, innerJson, nonceResponse.Nonce);
        var envelope = new SinopacWebApiMessage
        {
            Version = CurrentVersion,
            ShopNo = payload.ShopNo,
            // 永豐協定要求數字服務碼；Enum.ToString() 會輸出名稱而非固定代碼。
            APIService = ((int)apiService).ToString(CultureInfo.InvariantCulture),
            Nonce = nonceResponse.Nonce,
            Message = encryptedMessage,
            // 簽章由 provider core 產生，產品層不需要也不應知道永豐簽章欄位規則。
            Sign = SinopacSigner.GenerateSign(payload, aesKey, nonceResponse.Nonce)
        };

        var responseEnvelope = await PostJsonAsync<SinopacWebApiMessage>(
            profile,
            "Order",
            envelope,
            cancellationToken);
        var decryptedMessage = SinopacCrypto.Decrypt(aesKey, responseEnvelope.Message, responseEnvelope.Nonce);
        var innerResult = JsonConvert.DeserializeObject<TResult>(decryptedMessage)
            ?? throw new JsonException("Sinopac returned an empty response message.");
        var expectedSign = SinopacSigner.GenerateSign(innerResult, aesKey, responseEnvelope.Nonce);

        // 回應簽章不符時 fail closed，避免宿主產品根據未驗證資料更新 CRM 或顯示付款成功。
        if (!string.Equals(expectedSign, responseEnvelope.Sign, StringComparison.OrdinalIgnoreCase))
        {
            throw new SinopacSignatureException("Sinopac response signature validation failed.");
        }

        return innerResult;
    }

    private async Task<TResponse> PostJsonAsync<TResponse>(
        PaymentMerchantProfile profile,
        string route,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ResolveEndpoint(profile, route))
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(payload, SerializerSettings),
                Encoding.UTF8,
                "application/json")
        };

        // Profile-specific 商店識別只隨目前 request 傳送，不能留在可能重用的 HttpClient instance 上。
        if (!request.Headers.TryAddWithoutValidation(
                "X-KeyID",
                SinopacRequestMapper.GetXKeyId(profile)))
        {
            throw new PaymentConfigurationException("Sinopac X-KeyID could not be added to the request.");
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new SinopacRequestException(
                    route,
                    (int)response.StatusCode,
                    ExtractApimCode(content));
            }

            try
            {
                return JsonConvert.DeserializeObject<TResponse>(content)
                    ?? throw new SinopacRequestException(route, "Unreadable", 200);
            }
            catch (JsonException)
            {
                throw new SinopacRequestException(route, "Unreadable", 200);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new SinopacRequestException(route, "Timeout");
        }
        catch (HttpRequestException)
        {
            throw new SinopacRequestException(route, "Transport");
        }
    }

    private static Uri ResolveEndpoint(PaymentMerchantProfile profile, string route)
    {
        if (profile.Endpoints.TryGetValue("ApiBaseUrl", out var apiBaseUrl) &&
            Uri.TryCreate($"{apiBaseUrl.TrimEnd('/')}/{route}", UriKind.Absolute, out var uri))
        {
            return uri;
        }

        throw new PaymentConfigurationException($"Sinopac profile '{profile.Name}' is missing endpoint 'ApiBaseUrl'.");
    }

    private static IReadOnlyDictionary<string, string> BuildCreateProviderData(SinopacOrderCreateResponse response)
    {
        return new Dictionary<string, string>
        {
            ["shop_no"] = response.ShopNo,
            ["order_no"] = response.OrderNo,
            ["ts_no"] = response.TSNo,
            ["pay_type"] = response.PayType,
            ["amount"] = response.Amount.ToString(CultureInfo.InvariantCulture),
            ["status"] = response.Status,
            ["provider_message"] = response.Description,
            ["param1"] = response.Param1,
            ["param2"] = response.Param2,
            ["param3"] = response.Param3,
            ["product_entity_id"] = response.Param1,
            ["payment_organization"] = response.Param2,
            ["payment_category"] = response.Param3,
            // ATM 虛擬帳號是使用者付款所需資訊，必須穿越核心邊界給宿主產品顯示與通知。
            ["atm_pay_no"] = response.ATMParam?.AtmPayNo ?? string.Empty,
            ["web_atm_url"] = response.ATMParam?.WebAtmURL ?? string.Empty,
            ["otp_url"] = response.ATMParam?.OtpURL ?? string.Empty
        };
    }

    /// <summary>將永豐建單回應轉成中立結果，並檢查付款頁或 ATM 付款資訊等必要欄位。</summary>
    /// <param name="request">實際送出的永豐建單 payload。</param>
    /// <param name="response">經解密與簽章驗證的永豐建單回應。</param>
    /// <param name="fallbackOrderId">用於缺少回應訂單號時保留的產品訂單識別。</param>
    /// <returns>不含 provider SDK 物件的建立結果。</returns>
    internal static PaymentCreateResult ResolveCreateResult(
        SinopacOrderCreateRequest payload,
        SinopacOrderCreateResponse response,
        string fallbackProductOrderId)
    {
        var paymentPageUrl = ResolvePaymentPageUrl(response);
        // 信用卡、行動支付、LinePay 等 hosted flow 必須有付款頁網址；
        // 若 provider 回成功但 URL 空白，宿主產品不可導回原頁造成假成功。
        var missingPaymentPageUrl = RequiresPaymentPageUrl(payload.PayType) &&
            string.IsNullOrWhiteSpace(paymentPageUrl);
        // ATM/匯款 flow 必須有虛擬帳號；沒有帳號就不能產生付款指示。
        var missingAtmPayNo = RequiresAtmPayNo(payload.PayType) &&
            string.IsNullOrWhiteSpace(response.ATMParam?.AtmPayNo);
        var providerRejected = SinopacStatusMapper.IsProviderRejected(response);
        var status = missingPaymentPageUrl || missingAtmPayNo
            ? PaymentStatus.Failed
            : SinopacStatusMapper.MapCreate(response);

        return new PaymentCreateResult
        {
            Status = status,
            ProductOrderId = FirstNonEmpty(response.OrderNo, fallbackProductOrderId),
            ProviderOrderRef = FirstNonEmpty(response.TSNo, response.OrderNo),
            PaymentPageUrl = paymentPageUrl,
            Error = missingPaymentPageUrl || missingAtmPayNo
                ? CreateMissingCreateFieldError(response, missingAtmPayNo)
                : providerRejected
                    ? new PaymentError
                    {
                        Kind = PaymentErrorKind.ProviderRejected,
                        Code = response.Status,
                        Message = response.Description
                    }
                    : PaymentError.None,
            ProviderData = PaymentDiagnosticsSanitizer.Sanitize(BuildCreateProviderData(response)),
            Diagnostics = PaymentDiagnosticsSanitizer.Sanitize(new Dictionary<string, string>
            {
                ["shop_no"] = response.ShopNo,
                ["status"] = response.Status,
                ["description"] = response.Description,
                ["card_pay_url"] = response.CardParam?.CardPayURL ?? string.Empty,
                ["mobile_pay_url"] = response.MobileParam?.MobilePayURL ?? string.Empty,
                ["atm_pay_no"] = response.ATMParam?.AtmPayNo ?? string.Empty,
                ["web_atm_url"] = response.ATMParam?.WebAtmURL ?? string.Empty,
                ["otp_url"] = response.ATMParam?.OtpURL ?? string.Empty
            })
        };
    }

    private static PaymentError CreateMissingCreateFieldError(
        SinopacOrderCreateResponse response,
        bool missingAtmPayNo)
    {
        if (SinopacStatusMapper.IsProviderRejected(response))
        {
            // Provider 已經明確拒絕時，保留銀行原始 status/description，避免被 generic missing URL 訊息遮蔽。
            return new PaymentError
            {
                Kind = PaymentErrorKind.ProviderRejected,
                Code = response.Status,
                Message = FirstNonEmpty(response.Description, response.Status)
            };
        }

        return new PaymentError
        {
            Kind = PaymentErrorKind.ProviderRejected,
            Code = FirstNonEmpty(
                response.Status,
                missingAtmPayNo ? "MissingAtmPayNo" : "MissingPaymentPageUrl"),
            Message = missingAtmPayNo
                ? "Sinopac did not return an ATM virtual account number."
                : "Sinopac did not return a payment page URL."
        };
    }

    private static IReadOnlyDictionary<string, string> BuildQueryProviderData(SinopacOrderPayResponse response)
    {
        var transaction = response.TSResultContent;
        return new Dictionary<string, string>
        {
            ["shop_no"] = FirstNonEmpty(transaction?.ShopNo ?? string.Empty, response.ShopNo),
            ["pay_token"] = response.PayToken,
            ["order_no"] = transaction?.OrderNo ?? string.Empty,
            ["ts_no"] = transaction?.TSNo ?? string.Empty,
            ["pay_type"] = transaction?.PayType ?? string.Empty,
            ["amount"] = transaction?.Amount ?? string.Empty,
            ["status"] = FirstNonEmpty(transaction?.Status ?? string.Empty, response.Status),
            ["provider_message"] = FirstNonEmpty(transaction?.Description ?? string.Empty, response.Description),
            ["param1"] = transaction?.Param1 ?? string.Empty,
            ["param2"] = transaction?.Param2 ?? string.Empty,
            ["param3"] = transaction?.Param3 ?? string.Empty,
            ["product_entity_id"] = transaction?.Param1 ?? string.Empty,
            ["payment_organization"] = transaction?.Param2 ?? string.Empty,
            ["payment_category"] = transaction?.Param3 ?? string.Empty,
            ["left_cc_no"] = transaction?.LeftCCNo ?? string.Empty,
            ["right_cc_no"] = transaction?.RightCCNo ?? string.Empty
        };
    }

    private static IReadOnlyDictionary<string, string> BuildQueryDiagnostics(SinopacOrderPayResponse response)
    {
        var transaction = response.TSResultContent;
        return new Dictionary<string, string>
        {
            ["shop_no"] = response.ShopNo,
            ["pay_token"] = response.PayToken,
            ["api_status"] = response.Status,
            ["api_description"] = response.Description,
            ["transaction_status"] = transaction?.Status ?? string.Empty,
            ["transaction_description"] = transaction?.Description ?? string.Empty,
            ["cc_token"] = transaction?.CCToken ?? string.Empty
        };
    }

    /// <summary>
    /// 將已驗簽且成功的 provider 批次投影成唯讀中立集合；遇到不合法日期或非整數分值時整體拒絕發布。
    /// </summary>
    /// <param name="allotments">已驗簽 response 中的批次清單。</param>
    /// <returns>每筆批次及其 TWD 明細的唯讀副本。</returns>
    private static IReadOnlyList<PaymentAllotBatch> MapAllotBatches(
        IReadOnlyCollection<SinopacAllotMain>? allotments)
    {
        var batches = (allotments ?? Array.Empty<SinopacAllotMain>())
            .Select(allotment => new PaymentAllotBatch
            {
                AllotNo = allotment.AllotNo,
                AllotDate = ParseAllotDate(allotment.AllotDate),
                Items = Array.AsReadOnly((allotment.List ?? new List<SinopacAllotDetail>())
                    .Select(detail => new PaymentAllotItem
                    {
                        OrderNo = detail.OrderNO,
                        Amount = ConvertMinorUnit(detail.Amount),
                        Fee = ConvertMinorUnit(detail.Fee)
                    })
                    .ToArray())
            })
            .ToArray();

        return Array.AsReadOnly(batches);
    }

    /// <summary>將永豐 yyyyMMdd 整數轉成純日曆日期，格式或日期無效時拒絕該 response。</summary>
    /// <param name="providerDate">永豐批次日期整數。</param>
    /// <returns>沒有時區或時間部分的撥款日期。</returns>
    private static DateOnly ParseAllotDate(int providerDate)
    {
        var value = providerDate.ToString(CultureInfo.InvariantCulture);
        return DateOnly.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : throw new JsonException("Sinopac allot date is invalid.");
    }

    /// <summary>驗證 provider 回報整數分值並以 decimal / 100 建立 TWD Money，不經浮點或格式化字串。</summary>
    /// <param name="minorUnits">永豐回報的整數最小貨幣單位。</param>
    /// <returns>主要貨幣單位的 TWD Money。</returns>
    private static Money ConvertMinorUnit(decimal minorUnits)
    {
        if (minorUnits != decimal.Truncate(minorUnits))
        {
            throw new JsonException("Sinopac allot amount is not an integer minor unit.");
        }

        return new Money(minorUnits / 100m, "TWD");
    }

    /// <summary>建立只含穩定分類碼與固定訊息的撥款錯誤，不回傳例外文字或 HTTP response 本文。</summary>
    /// <param name="kind">標準付款錯誤分類。</param>
    /// <param name="code">不包含設定值或 provider payload 的穩定分類碼。</param>
    /// <returns>不含撥款批次的失敗結果。</returns>
    private static PaymentAllotQueryResult AllotQueryError(PaymentErrorKind kind, string code)
    {
        return new PaymentAllotQueryResult
        {
            Error = new PaymentError
            {
                Kind = kind,
                Code = code,
                Message = code
            }
        };
    }

    private static string ResolvePaymentPageUrl(SinopacOrderCreateResponse response)
    {
        return FirstNonEmpty(
            response.CardParam?.CardPayURL ?? string.Empty,
            response.MobileParam?.MobilePayURL ?? string.Empty,
            response.ATMParam?.WebAtmURL ?? string.Empty,
            response.ATMParam?.OtpURL ?? string.Empty);
    }

    private static bool RequiresPaymentPageUrl(string? payType)
    {
        var normalizedPayType = payType?.Trim().ToUpperInvariant();
        return normalizedPayType is "C" or "M" or "L" or "" or null;
    }

    private static bool RequiresAtmPayNo(string? payType)
    {
        return string.Equals(payType?.Trim(), "A", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>以 invariant decimal 解析永豐最小單位，除以 100 後建立 TWD Money；無效輸入回傳 null。</summary>
    /// <param name="amount">永豐以字串提供的整數分。</param>
    /// <returns>主要單位的 TWD 金額，或無法解析時的 null。</returns>
    internal static Money? ParseMinorAmount(string? amount)
    {
        return decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? new Money(parsed / 100m, "TWD")
            : null;
    }

    private static PaymentCreateResult CreateError(string productOrderId, PaymentErrorKind kind, string message)
    {
        return new PaymentCreateResult
        {
            Status = PaymentStatus.Failed,
            ProductOrderId = productOrderId,
            Error = new PaymentError
            {
                Kind = kind,
                Message = message
            }
        };
    }

    private static PaymentStatusResult QueryError(
        PaymentQueryRequest request,
        PaymentErrorKind kind,
        string message)
    {
        return new PaymentStatusResult
        {
            Status = PaymentStatus.Failed,
            ProductOrderId = request.ProductOrderId,
            ProviderOrderRef = request.ProviderOrderRef,
            Error = new PaymentError
            {
                Kind = kind,
                Message = message
            }
        };
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static bool IsNetworkTransportException(Exception exception)
    {
        return exception.GetType().Namespace == "System.Net.Http" &&
            exception.GetType().Name == "Http" + "RequestException";
    }

    private static PaymentErrorKind ClassifyHttpFailure(int statusCode)
    {
        if (statusCode == 0)
        {
            return PaymentErrorKind.NetworkFailure;
        }

        if (statusCode is 401 or 403 or 404 or 405 or 415 or 499)
        {
            return PaymentErrorKind.ConfigurationInvalid;
        }

        if (statusCode is 429 or >= 500)
        {
            return PaymentErrorKind.ProviderUnavailable;
        }

        return PaymentErrorKind.ProviderRejected;
    }

    private static string ExtractApimCode(string responseBody)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (!document.RootElement.TryGetProperty("code", out var codeElement))
            {
                return string.Empty;
            }

            var code = codeElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => codeElement.GetString(),
                System.Text.Json.JsonValueKind.Number => codeElement.GetRawText(),
                _ => null
            };
            return code is { Length: > 0 and <= 4 } &&
                code.All(character => character is >= '0' and <= '9')
                    ? code
                    : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }

    private static string SafeProviderDescription(string? status, string? description)
    {
        if (!string.Equals(status, "F", StringComparison.OrdinalIgnoreCase))
        {
            return description ?? string.Empty;
        }

        if (description is { Length: >= 5 }
            && description[0] == 'E'
            && description.AsSpan(1, 4).IndexOfAnyExceptInRange('0', '9') < 0)
        {
            return description[..5];
        }

        return string.Empty;
    }

    private sealed class SinopacRequestException : Exception
    {
        public SinopacRequestException(string stage, int statusCode, string apimCode)
            : base(BuildDiagnosticCode(stage, statusCode, apimCode))
        {
            Stage = stage;
            StatusCode = statusCode;
            ErrorKind = ClassifyHttpFailure(statusCode);
            DiagnosticCode = BuildDiagnosticCode(stage, statusCode, apimCode);
        }

        public SinopacRequestException(string stage, string failureCode, int statusCode = 0)
            : base(BuildDiagnosticCode(stage, failureCode))
        {
            Stage = stage;
            StatusCode = statusCode;
            ErrorKind = failureCode switch
            {
                "NonceMissing" => PaymentErrorKind.ProviderUnavailable,
                "Unreadable" => PaymentErrorKind.SerializationFailure,
                _ when statusCode != 0 => ClassifyHttpFailure(statusCode),
                _ => PaymentErrorKind.NetworkFailure
            };
            DiagnosticCode = BuildDiagnosticCode(stage, failureCode);
        }

        public string Stage { get; }
        public int StatusCode { get; }
        public PaymentErrorKind ErrorKind { get; }
        public string DiagnosticCode { get; }

        private static string BuildDiagnosticCode(string stage, int statusCode, string apimCode) =>
            string.IsNullOrEmpty(apimCode)
                ? $"{stage}:{statusCode.ToString(CultureInfo.InvariantCulture)}"
                : $"{stage}:{statusCode.ToString(CultureInfo.InvariantCulture)}:{apimCode}";

        private static string BuildDiagnosticCode(string stage, string failureCode) =>
            $"{stage}:{failureCode}";
    }

    private sealed class SinopacSignatureException : Exception
    {
        public SinopacSignatureException(string message)
            : base(message)
        {
        }
    }
}
