// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class SinopacPaymentProvider、class SinopacHttpStatusException、class SinopacSignatureException
// 主要成員：CreatePaymentAsync、QueryPaymentAsync、ParseCallbackAsync、ResolveEndpoint、ResolveCreateResult、CreateMissingCreateFieldError、ResolvePaymentPageUrl、RequiresPaymentPageUrl、RequiresAtmPayNo、ParseMinorAmount
// 引用命名空間：System.Globalization、System.Net、System.Security.Cryptography、System.Text、Newtonsoft.Json、SpeechMessage.Payments.Abstractions、SpeechMessage.Payments.Configuration、SpeechMessage.Payments.Diagnostics
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;

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

    public SinopacPaymentProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public PaymentProviderKind ProviderKind => PaymentProviderKind.Sinopac;

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
                Amount = ParseMinorAmount(transaction?.Amount),
                Currency = "TWD",
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

    private static decimal? ParseMinorAmount(string? amount)
    {
        return decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed / 100m
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
