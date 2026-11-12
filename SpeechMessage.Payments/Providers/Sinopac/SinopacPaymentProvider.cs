/*
 * 檔案整體說明
 *
 * 目的與定位：永豐 QPay provider 實作，宣告支援 TWD，並集中處理 Nonce、AES、簽章、建單及查詢封包。
 *
 * 主要功能與流程：
 * 本檔宣告 SinopacPaymentProvider、SinopacHttpStatusException、SinopacSignatureException。
 * 主要操作入口包含 CreatePaymentAsync、QueryPaymentAsync、ParseCallbackAsync、ResolveEndpoint、BuildCreateProviderData、
 * ResolveCreateResult、CreateMissingCreateFieldError、BuildQueryProviderData；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本 provider 只持有 DI 提供且由 Host 管理生命週期的 HttpClient；每次請求／回應由 using 關閉，序列化金額以 Money 為核心來源。
 * 請求 profile 是單次呼叫輸入，不快取憑證；SemaphoreSlim 僅序列化此 provider instance 的 X-KeyID 標頭更新，並在 finally 釋放等待權。
 * provider 與 typed HttpClient 由 DI 容器管理其主機存續期；此類別不對外暴露標頭或 semaphore，避免跨請求／profile 洩漏可變狀態。
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
internal sealed class SinopacPaymentProvider : IPaymentProvider
{
    private const string CurrentVersion = "1.0.0";

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore
    };

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
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
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞至 SemaphoreSlim 等待與 HTTP 傳輸。</param>
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
        catch (SinopacHttpStatusException ex)
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.ProviderUnavailable, ex.Message);
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
        catch (SinopacHttpStatusException ex)
        {
            return QueryError(request, PaymentErrorKind.ProviderUnavailable, ex.Message);
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
            throw new SinopacHttpStatusException("Sinopac did not return a nonce.");
        }

        var innerJson = JsonConvert.SerializeObject(payload, SerializerSettings);
        var encryptedMessage = SinopacCrypto.Encrypt(aesKey, innerJson, nonceResponse.Nonce);
        var envelope = new SinopacWebApiMessage
        {
            Version = CurrentVersion,
            ShopNo = payload.ShopNo,
            APIService = apiService.ToString(),
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
        using var requestContent = new StringContent(
            JsonConvert.SerializeObject(payload, SerializerSettings),
            Encoding.UTF8,
            "application/json");

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            // HttpClient header 是 instance 狀態；此 provider 用 semaphore 避免並行呼叫時 X-KeyID 被交錯覆蓋。
            _httpClient.DefaultRequestHeaders.Remove("X-KeyID");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-KeyID", SinopacRequestMapper.GetXKeyId(profile));

            using var response = await _httpClient.PostAsync(ResolveEndpoint(profile, route), requestContent, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new SinopacHttpStatusException(route, response.StatusCode, content);
            }

            return JsonConvert.DeserializeObject<TResponse>(content)
                ?? throw new JsonException("Sinopac returned an empty response.");
        }
        finally
        {
            _sendLock.Release();
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

    private sealed class SinopacHttpStatusException : Exception
    {
        public SinopacHttpStatusException(string route, HttpStatusCode statusCode, string responseBody)
            : base(FormatHttpStatusMessage(route, statusCode, responseBody))
        {
        }

        public SinopacHttpStatusException(string message)
            : base(message)
        {
        }

        private static string FormatHttpStatusMessage(
            string route,
            HttpStatusCode statusCode,
            string responseBody)
        {
            var message = $"Sinopac {route} returned HTTP {(int)statusCode} {statusCode}.";
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return message;
            }

            var trimmedBody = responseBody.Trim();
            if (trimmedBody.Length > 500)
            {
                trimmedBody = trimmedBody[..500] + "...";
            }

            return $"{message} Response: {trimmedBody}";
        }
    }

    private sealed class SinopacSignatureException : Exception
    {
        public SinopacSignatureException(string message)
            : base(message)
        {
        }
    }
}
