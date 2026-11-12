/*
 * 檔案整體說明
 *
 * 目的與定位：台新 TSPG provider 實作，宣告支援 TWD；集中處理建單、查詢、回應正規化與 callback parser 轉接。
 *
 * 主要功能與流程：
 * 本檔宣告 TaishinPaymentProvider、TaishinHttpStatusException。
 * 主要操作入口包含 CreatePaymentAsync、QueryPaymentAsync、ParseCallbackAsync、PostAsync、ResolveEndpoint、CreateError、
 * QueryError、ParseMinorAmount；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * HttpClient 由 DI Host 擁有；單次 request content 與 response 由 using 釋放。Profile 只在單次呼叫使用，不保留憑證或付款人狀態。
 * 金額以 Money 保存 provider 回報的 ISO 幣別；修改協定時須同步檢查 mapper、錯誤處理及 provider 特性測試。
 * 可沿 SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Globalization;
using System.Net;
using Newtonsoft.Json;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;
using SpeechMessage.Localization;

namespace SpeechMessage.Payments.Providers.Taishin;

/// <summary>
/// 台新 TSPG provider 實作。
/// 這裡只處理台新 REST API 的建單、查詢、回應正規化與 callback parser 轉接；
/// 宿主產品的收費單更新與通知不放進通用核心。
/// </summary>
internal sealed class TaishinPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private static readonly IReadOnlyCollection<string> SupportedCurrencyCodes = Array.AsReadOnly(new[] { "TWD" });

    /// <summary>建立台新 provider；HttpClient 由 DI typed-client 管理，provider 不快取商店或付款人狀態。</summary>
    /// <param name="httpClient">由 DI 提供且由 Host 管理生命週期的 HTTP client。</param>
    public TaishinPaymentProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>提供 Gateway 對應台新 profile 的固定 provider 種類。</summary>
    public PaymentProviderKind ProviderKind => PaymentProviderKind.Taishin;

    /// <summary>台新 provider 目前只允許以 TWD 建立付款。</summary>
    public IReadOnlyCollection<string> SupportedCurrencies => SupportedCurrencyCodes;

    /// <summary>建立台新付款頁請求；送出前 Gateway 已檢查 Money.Currency 是否為台新宣告支援的幣別。</summary>
    /// <param name="profile">resolver 選定的商店設定，只在本次操作讀取。</param>
    /// <param name="request">含 Money 總額與中立付款欄位的建立請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞至 HTTP 傳輸。</param>
    /// <returns>正規化的建單結果與已清理 provider 欄位。</returns>
    public async Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request,
        CancellationToken cancellationToken)
    {
        TaishinPaymentRequest payload;
        try
        {
            payload = TaishinRequestMapper.MapCreatePayload(profile, request);
        }
        catch (PaymentConfigurationException ex)
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.ConfigurationInvalid, ex.Message);
        }

        try
        {
            var response = await PostAsync(profile, "auth.ashx", payload, cancellationToken);
            var retCode = FirstNonEmpty(response.RetCode, response.Params?.RetCode ?? string.Empty);
            var retMessage = FirstNonEmpty(response.RetMessage, response.Params?.RetMessage ?? string.Empty);
            // 台新建單成功只代表取得 HPP 付款頁，付款尚未完成，所以回傳 Pending。
            var status = TaishinStatusMapper.Map(retCode, "1") == PaymentStatus.Succeeded
                ? PaymentStatus.Pending
                : PaymentStatus.Failed;
            var orderNo = FirstNonEmpty(response.OrderNo, response.Params?.OrderNo ?? string.Empty, response.Params?.OrderNoUpper ?? string.Empty, request.ProductOrderId);
            var transactionId = response.Params?.TransactionId ?? string.Empty;

            return new PaymentCreateResult
            {
                Status = status,
                ProductOrderId = request.ProductOrderId,
                ProviderOrderRef = FirstNonEmpty(transactionId, orderNo),
                PaymentPageUrl = response.Params?.PaymentPageUrl ?? string.Empty,
                Error = status == PaymentStatus.Pending
                    ? PaymentError.None
                    : new PaymentError
                    {
                        Kind = PaymentErrorKind.ProviderRejected,
                        Code = retCode,
                        Message = retMessage
                    },
                ProviderData = PaymentDiagnosticsSanitizer.Sanitize(new Dictionary<string, string>
                {
                    ["ret_code"] = retCode,
                    ["ret_msg"] = retMessage,
                    ["order_no"] = orderNo,
                    ["transaction_id"] = transactionId
                }),
                Diagnostics = PaymentDiagnosticsSanitizer.Sanitize(new Dictionary<string, string>
                {
                    ["hpp_url"] = response.Params?.PaymentPageUrl ?? string.Empty,
                    ["transaction_id"] = transactionId
                })
            };
        }
        catch (TaishinHttpStatusException ex)
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.ProviderUnavailable, ex.Message);
        }
        catch (JsonException ex)
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

    /// <summary>查詢台新交易，並將 provider 最小單位金額與有效 ISO 幣別轉成 Money。</summary>
    /// <param name="profile">resolver 選定的台新 profile，只在本次操作讀取。</param>
    /// <param name="request">含產品訂單與 provider 交易參照的中立查詢請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞至 HTTP 傳輸。</param>
    /// <returns>正規化狀態、PaidAmount 及已清理診斷欄位。</returns>
    public async Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentMerchantProfile profile,
        PaymentQueryRequest request,
        CancellationToken cancellationToken)
    {
        TaishinPaymentRequest payload;
        try
        {
            payload = TaishinRequestMapper.MapQueryPayload(profile, request);
        }
        catch (PaymentConfigurationException ex)
        {
            return QueryError(request, PaymentErrorKind.ConfigurationInvalid, ex.Message);
        }

        try
        {
            var response = await PostAsync(profile, "other.ashx", payload, cancellationToken);
            var retCode = FirstNonEmpty(response.RetCode, response.Params?.RetCode ?? string.Empty);
            var retMessage = FirstNonEmpty(response.RetMessage, response.Params?.RetMessage ?? string.Empty);
            var orderNo = FirstNonEmpty(response.OrderNo, response.Params?.OrderNo ?? string.Empty, response.Params?.OrderNoUpper ?? string.Empty, request.ProductOrderId);
            var transactionId = response.Params?.TransactionId ?? string.Empty;

            return new PaymentStatusResult
            {
                Status = TaishinStatusMapper.Map(retCode, "1"),
                ProductOrderId = request.ProductOrderId,
                ProviderOrderRef = FirstNonEmpty(request.ProviderOrderRef, orderNo),
                ProviderTransactionId = transactionId,
                PaidAmount = ParseMinorAmount(
                    response.Params?.Amount,
                    NormalizeCurrency(response.Params?.Currency)),
                Error = TaishinStatusMapper.Map(retCode, "1") == PaymentStatus.Failed
                    ? new PaymentError
                    {
                        Kind = PaymentErrorKind.ProviderRejected,
                        Code = retCode,
                        Message = retMessage
                    }
                    : PaymentError.None,
                ProviderData = PaymentDiagnosticsSanitizer.Sanitize(new Dictionary<string, string>
                {
                    ["ret_code"] = retCode,
                    ["ret_msg"] = retMessage,
                    ["order_no"] = orderNo,
                    ["transaction_id"] = transactionId
                })
            };
        }
        catch (TaishinHttpStatusException ex)
        {
            return QueryError(request, PaymentErrorKind.ProviderUnavailable, ex.Message);
        }
        catch (JsonException ex)
        {
            return QueryError(request, PaymentErrorKind.SerializationFailure, ex.Message);
        }
        catch (ArgumentException)
        {
            return QueryError(request, PaymentErrorKind.SerializationFailure, "Taishin returned an unsupported amount currency.");
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

    /// <summary>解析台新 callback，使用單次 profile 驗證簽章並回傳 HTTP-neutral acknowledgement 描述。</summary>
    /// <param name="profile">resolver 選定的台新 profile；憑證只用於此解析呼叫。</param>
    /// <param name="request">宿主投影的 callback 欄位。</param>
    /// <param name="cancellationToken">呼叫端取消權杖；此解析路徑不建立外部 I/O。</param>
    /// <returns>正規化 callback 結果與 Money 金額。</returns>
    public Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentMerchantProfile profile,
        PaymentCallbackRequest request,
        CancellationToken cancellationToken)
    {
        // 台新 callback hash 驗證需要 profile 內的 StoreKey/StoreIV，因此 profile 必須傳入 parser。
        return Task.FromResult(TaishinCallbackParser.Parse(profile, request));
    }

    private async Task<TaishinApiResponse> PostAsync(
        PaymentMerchantProfile profile,
        string endpoint,
        TaishinPaymentRequest payload,
        CancellationToken cancellationToken)
    {
        var uri = ResolveEndpoint(profile, endpoint);
        var json = JsonConvert.SerializeObject(
            payload,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            });
        // 台新 TSPG REST endpoint 使用 JSON payload；auth.ashx 建單，other.ashx 查詢。
        using var response = await _httpClient.PostAsync(
            uri,
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new TaishinHttpStatusException(response.StatusCode);
        }

        return JsonConvert.DeserializeObject<TaishinApiResponse>(content) ??
            throw new JsonException("Taishin returned an empty response.");
    }

    private static Uri ResolveEndpoint(PaymentMerchantProfile profile, string endpoint)
    {
        if (profile.Endpoints.TryGetValue("ApiBaseUrl", out var apiBaseUrl) &&
            Uri.TryCreate($"{apiBaseUrl.TrimEnd('/')}/{endpoint}", UriKind.Absolute, out var uri))
        {
            return uri;
        }

        throw new PaymentConfigurationException($"Taishin profile '{profile.Name}' is missing endpoint 'ApiBaseUrl'.");
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

    private static Money? ParseMinorAmount(string? amount, string currency)
    {
        return decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? new Money(parsed / 100m, currency)
            : null;
    }

    private static string NormalizeCurrency(string? currency)
    {
        return string.Equals(currency, "NTD", StringComparison.OrdinalIgnoreCase)
            ? "TWD"
            : FirstNonEmpty(currency ?? string.Empty, "TWD");
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

    private sealed class TaishinHttpStatusException : Exception
    {
        public TaishinHttpStatusException(HttpStatusCode statusCode)
            : base($"Taishin returned HTTP {(int)statusCode} {statusCode}.")
        {
        }
    }
}
