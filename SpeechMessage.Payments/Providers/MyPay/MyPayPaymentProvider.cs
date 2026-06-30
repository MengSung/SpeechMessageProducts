using System.Net;
using Newtonsoft.Json;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.MyPay;

/// <summary>
/// 高鉅 MyPay provider 實作。
/// 只處理 MyPay API 呼叫、回應轉換與 callback parser 轉接；
/// 宿主產品的 CRM 更新、通知與畫面導向仍留在產品層。
/// </summary>
internal sealed class MyPayPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;

    public MyPayPaymentProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public PaymentProviderKind ProviderKind => PaymentProviderKind.MyPay;

    public async Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> form;
        try
        {
            form = MyPayRequestMapper.MapCreateForm(profile, request);
        }
        catch (PaymentConfigurationException ex)
        {
            return CreateError(request.ProductOrderId, PaymentErrorKind.ConfigurationInvalid, ex.Message);
        }

        try
        {
            var endpoint = ResolveEndpoint(profile);
            // MyPay 建單使用 application/x-www-form-urlencoded，
            // form 欄位內容已由 MyPayRequestMapper 依 direct merchant / agent contract 組好。
            using var response = await _httpClient.PostAsync(endpoint, new FormUrlEncodedContent(form), cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return CreateError(
                    request.ProductOrderId,
                    PaymentErrorKind.ProviderUnavailable,
                    $"MyPay returned HTTP {(int)response.StatusCode} {response.StatusCode}.");
            }

            var providerResponse = JsonConvert.DeserializeObject<MyPayCreateResponse>(content);
            if (providerResponse is null)
            {
                return CreateError(request.ProductOrderId, PaymentErrorKind.SerializationFailure, "MyPay returned an empty response.");
            }

            return new PaymentCreateResult
            {
                // MyPay code=0000 代表建單成功並等待使用者付款，因此 normalized 狀態是 Pending。
                Status = providerResponse.Code == "0000" ? PaymentStatus.Pending : PaymentStatus.Failed,
                ProductOrderId = request.ProductOrderId,
                ProviderOrderRef = providerResponse.Uid,
                PaymentPageUrl = providerResponse.Url,
                Error = providerResponse.Code == "0000"
                    ? PaymentError.None
                    : new PaymentError
                    {
                        Kind = PaymentErrorKind.ProviderRejected,
                        Code = providerResponse.Code,
                        Message = providerResponse.Message
                    },
                ProviderData = PaymentDiagnosticsSanitizer.Sanitize(new Dictionary<string, string>
                {
                    ["code"] = providerResponse.Code,
                    ["msg"] = providerResponse.Message,
                    ["uid"] = providerResponse.Uid
                }),
                Diagnostics = PaymentDiagnosticsSanitizer.Sanitize(new Dictionary<string, string>
                {
                    ["http_status"] = ((int)response.StatusCode).ToString(),
                    ["uid"] = providerResponse.Uid,
                    // provider 回傳的 key 屬敏感資訊，只能交給 sanitizer 後進 diagnostics。
                    ["key"] = providerResponse.Key
                })
            };
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

    public Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentMerchantProfile profile,
        PaymentQueryRequest request,
        CancellationToken cancellationToken)
    {
        // 初版抽離只保留 MyPay 建單與 callback 閉環；查詢 API 尚未納入通用核心。
        return Task.FromResult(new PaymentStatusResult
        {
            Status = PaymentStatus.Unknown,
            ProductOrderId = request.ProductOrderId,
            ProviderOrderRef = request.ProviderOrderRef,
            Error = new PaymentError
            {
                Kind = PaymentErrorKind.UnsupportedOperation,
                Message = "MyPay status query is not available in the migrated core yet."
            }
        });
    }

    public Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentMerchantProfile profile,
        PaymentCallbackRequest request,
        CancellationToken cancellationToken)
    {
        // MyPay callback 不需要 ASP.NET web runtime 型別；宿主產品已先轉成 neutral callback request。
        return Task.FromResult(MyPayCallbackParser.Parse(request));
    }

    private static Uri ResolveEndpoint(PaymentMerchantProfile profile)
    {
        if (profile.Endpoints.TryGetValue("ApiBaseUrl", out var endpoint) &&
            Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        throw new PaymentConfigurationException($"MyPay profile '{profile.Name}' is missing endpoint 'ApiBaseUrl'.");
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

    private static bool IsNetworkTransportException(Exception exception)
    {
        return exception.GetType().Namespace == "System.Net.Http" &&
            exception.GetType().Name == "Http" + "RequestException";
    }
}
