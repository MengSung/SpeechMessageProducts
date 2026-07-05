// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class MyPayPaymentProvider
// 主要成員：CreatePaymentAsync、QueryPaymentAsync、ParseCallbackAsync、ResolveEndpoint、CreateError、IsNetworkTransportException
// 引用命名空間：System.Net、Newtonsoft.Json、SpeechMessage.Payments.Abstractions、SpeechMessage.Payments.Configuration、SpeechMessage.Payments.Diagnostics、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
