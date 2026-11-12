/*
 * 檔案整體說明
 *
 * 目的與定位：高鉅 MyPay provider 實作，宣告支援 TWD；只處理 MyPay API 呼叫、回應轉換與 callback parser 轉接。
 *
 * 主要功能與流程：
 * 本檔宣告 MyPayPaymentProvider。
 * 主要操作入口包含 CreatePaymentAsync、QueryPaymentAsync、ParseCallbackAsync、ResolveEndpoint、CreateError、
 * IsNetworkTransportException。
 *
 * 使用邊界與維護：
 * HttpClient 由 DI Host 擁有；每個 response 與 request content 由單次呼叫 using 釋放。Profile 僅在單次方法呼叫借用，不快取憑證或付款人資料。
 * Workflows 與產品端只接收 Money 正規化金額及安全結果；修改協定時須同步檢查 mapper、錯誤處理及 provider 特性測試。
 * 可沿 SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
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
    private static readonly IReadOnlyCollection<string> SupportedCurrencyCodes = Array.AsReadOnly(new[] { "TWD" });

    /// <summary>建立 MyPay provider；HttpClient 由 DI typed-client 管理，本實作不保存 profile 或付款人資料。</summary>
    /// <param name="httpClient">由 DI 提供並由 Host 管理生命週期的 HTTP client。</param>
    public MyPayPaymentProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>提供 Gateway 對應 MyPay profile 的固定 provider 種類。</summary>
    public PaymentProviderKind ProviderKind => PaymentProviderKind.MyPay;

    /// <summary>MyPay provider 目前只允許以 TWD 建立付款。</summary>
    public IReadOnlyCollection<string> SupportedCurrencies => SupportedCurrencyCodes;

    /// <summary>建立 MyPay 付款表單並送至 profile 指定端點；金額及幣別讀取 Money 欄位。</summary>
    /// <param name="profile">resolver 選定的商店設定，只在本次請求使用。</param>
    /// <param name="request">含 Money 總額與付款資料的中立請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞至 HTTP 傳輸。</param>
    /// <returns>正規化的建立結果；不把 MyPay 原始回應物件交給產品層。</returns>
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

    /// <summary>MyPay 目前沒有遷移至共用核心的查詢 API，因此回傳明確的 UnsupportedOperation。</summary>
    /// <param name="profile">resolver 選定的 profile；本方法不進行 I/O。</param>
    /// <param name="request">含產品訂單與 provider 交易參照的中立查詢請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖；目前不啟動背景工作或外部傳輸。</param>
    /// <returns>狀態為 Unknown 並帶 UnsupportedOperation 的結果。</returns>
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

    /// <summary>使用 MyPay parser 驗證並正規化中立 callback；不更新產品資料或保存跨請求資料。</summary>
    /// <param name="profile">resolver 選定的 profile；目前 MyPay parser 不需讀取其憑證。</param>
    /// <param name="request">宿主投影的 HTTP-neutral callback 欄位。</param>
    /// <param name="cancellationToken">呼叫端取消權杖；純解析路徑不執行 I/O。</param>
    /// <returns>帶 MyPay acknowledgement 描述及已清理欄位的 callback 結果。</returns>
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
