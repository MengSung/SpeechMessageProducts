/*
 * 檔案整體說明
 *
 * 目的與定位：執行共用 QPay 回呼驗證管線，將 ASP.NET adapter 的正規化欄位送入 provider-neutral gateway，並以產品 lookup 與付款後 workflow 完成授權邊界及付款狀態檢查。
 * 主要流程：以 callback ShopNo 精確選出唯一伺服器設定的永豐 profile；ParseCallback 後以 PayToken 查詢並驗簽；只接受 Succeeded 狀態，再查產品訂單、依序檢查 profile／結清／金額幣別／訂單號，最後呼叫既有 workflow。
 * 使用邊界與維護：本服務為 scoped，所有 callback、profile 與產品訂單資料僅用於目前 request，不保存於 static、singleton 或跨 request 快取。HTTP request 的取消權杖會傳入 gateway、lookup 與 workflow；provider 或產品資源由其 DI owner 管理。錯誤回應使用固定文字；真實非預期例外交 ExceptionDiagnostics，預期 409 只落安全診斷、不觸發 LINE。此檔不註冊產品掛勾、不執行產品資料寫回，透過 PaymentCallbackHostTests 驗證各拒絕關卡及呼叫次數。
 */
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpeechMessage.Hosting;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows;
using SpeechMessage.Payments.Workflows.Callbacks;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Payments.AspNetCore.Callbacks;

/// <summary>
/// 將已受限解析的回呼欄位依序交由 gateway 與產品掛勾驗證，僅在付款成功且訂單條件全部成立時執行付款後 workflow。
/// </summary>
public sealed class PaymentCallbackProcessor
{
    private readonly PaymentOptions _options;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentCallbackOrderLookup[] _orderLookups;
    private readonly PaymentPostPaymentWorkflow _postPaymentWorkflow;
    private readonly IServiceProviderIsService _serviceProviderIsService;
    private readonly ExceptionDiagnostics _diagnostics;

    /// <summary>
    /// 建立 scoped 回呼處理器；它只借用目前 DI scope 的 gateway、lookup 與 workflow，不自行建立或保存外部資源。
    /// </summary>
    /// <param name="options">Host 啟動設定中的付款 profile 集合；ShopNo 只用來選定唯一永豐 profile。</param>
    /// <param name="gateway">由 Payments DI 擁有的 provider-neutral gateway。</param>
    /// <param name="orderLookups">產品端訂單 lookup；必須恰有一個實作才能繼續處理。</param>
    /// <param name="postPaymentWorkflow">先更新產品紀錄、再通知付款者的共用付款後流程。</param>
    /// <param name="serviceProviderIsService">由 Host 容器提供的服務存在性檢查，不建立額外 updater instance。</param>
    /// <param name="diagnostics">Host 唯一擁有的安全例外診斷服務。</param>
    /// <exception cref="ArgumentNullException">必要依賴為 null。</exception>
    public PaymentCallbackProcessor(
        IOptions<PaymentOptions> options,
        IPaymentGateway gateway,
        IEnumerable<IPaymentCallbackOrderLookup> orderLookups,
        PaymentPostPaymentWorkflow postPaymentWorkflow,
        IServiceProviderIsService serviceProviderIsService,
        ExceptionDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentException("Payment options are unavailable.", nameof(options));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        ArgumentNullException.ThrowIfNull(orderLookups);
        _orderLookups = orderLookups.Take(2).ToArray();
        _postPaymentWorkflow = postPaymentWorkflow ?? throw new ArgumentNullException(nameof(postPaymentWorkflow));
        _serviceProviderIsService = serviceProviderIsService
            ?? throw new ArgumentNullException(nameof(serviceProviderIsService));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>處理目前 request 的 callback；回傳值只描述固定 HTTP outcome，不含 provider 或訂單敏感資料。</summary>
    /// <param name="request">adapter 嚴格解析後只含 ShopNo 與 PayToken 的 callback DTO。</param>
    /// <param name="cancellationToken">目前 HTTP request 的取消權杖。</param>
    /// <returns>adapter 用來映射安全 HTTP 狀態碼及固定成功回應的 outcome。</returns>
    internal async Task<PaymentCallbackOutcome> ProcessAsync(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadCallbackField(request.Form, "ShopNo", out var shopNo) ||
            !TryReadCallbackField(request.Form, "PayToken", out var payToken))
        {
            return PaymentCallbackOutcome.BadRequest;
        }

        var selectedProfiles = _options.Profiles
            .Where(profile =>
                profile.Value.Provider == PaymentProviderKind.Sinopac &&
                profile.Value.Credentials.TryGetValue("ShopNo", out var configuredShopNo) &&
                string.Equals(configuredShopNo, shopNo, StringComparison.Ordinal))
            .Take(2)
            .ToArray();

        if (selectedProfiles.Length != 1)
        {
            return PaymentCallbackOutcome.BadRequest;
        }

        var profileName = selectedProfiles[0].Key;
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return PaymentCallbackOutcome.BadRequest;
        }

        try
        {
            var normalizedRequest = new PaymentCallbackRequest
            {
                ProfileName = profileName,
                ProviderHint = PaymentProviderKind.Sinopac,
                HttpMethod = request.HttpMethod,
                ContentType = request.ContentType,
                Form = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ShopNo"] = shopNo,
                        ["PayToken"] = payToken
                    })
            };

            var callback = await _gateway.ParseCallbackAsync(normalizedRequest, cancellationToken)
                .ConfigureAwait(false);
            if (callback.Error.HasError ||
                !string.Equals(callback.ProviderTransactionId, payToken, StringComparison.Ordinal))
            {
                return PaymentCallbackOutcome.BadRequest;
            }

            var status = await _gateway.QueryPaymentAsync(
                    new PaymentQueryRequest
                    {
                        ProfileName = profileName,
                        ProviderHint = PaymentProviderKind.Sinopac,
                        ProductOrderId = callback.ProductOrderId,
                        ProviderOrderRef = callback.ProviderTransactionId
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (status.Error.HasError)
            {
                return PaymentCallbackOutcome.ServiceUnavailable;
            }

            if (status.Status != PaymentStatus.Succeeded)
            {
                return Reject("PaymentCallbackProcessor.ProviderStatusRejected");
            }

            if (_orderLookups.Length != 1)
            {
                return PaymentCallbackOutcome.ServiceUnavailable;
            }

            var order = await _orderLookups[0].FindAsync(status, cancellationToken).ConfigureAwait(false);
            if (order is null)
            {
                return PaymentCallbackOutcome.Accepted;
            }

            if (!string.Equals(order.ProfileName, profileName, StringComparison.Ordinal))
            {
                return Reject("PaymentCallbackProcessor.ProfileMismatch");
            }

            if (order.AlreadySettled)
            {
                return PaymentCallbackOutcome.Accepted;
            }

            if (!HasMatchingPayment(status, order))
            {
                return Reject("PaymentCallbackProcessor.PaymentMismatch");
            }

            if (!_serviceProviderIsService.IsService(typeof(IPaymentRecordUpdater)))
            {
                return PaymentCallbackOutcome.ServiceUnavailable;
            }

            var payment = new PaymentWorkflowResult
            {
                Status = status.Status,
                ProductOrderId = status.ProductOrderId,
                ProviderTransactionId = status.ProviderTransactionId,
                PaidAmount = status.PaidAmount,
                ProviderMessage = status.ProviderData.TryGetValue("provider_message", out var providerMessage)
                    ? providerMessage
                    : string.Empty,
                ProviderData = status.ProviderData
            };
            var items = new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(order.Items, StringComparer.Ordinal));
            var workflowResult = await _postPaymentWorkflow.ExecuteAsync(
                    new PaymentPostPaymentContext(payment, items),
                    cancellationToken)
                .ConfigureAwait(false);

            return workflowResult.RecordUpdated
                ? PaymentCallbackOutcome.Accepted
                : PaymentCallbackOutcome.ServiceUnavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics.Report(
                exception,
                "PaymentCallbackProcessor.ProcessAsync",
                cancellationToken);
            return PaymentCallbackOutcome.ServiceUnavailable;
        }
    }

    /// <summary>只接受非空、無控制字元且已由 adapter 去重的必要欄位。</summary>
    private static bool TryReadCallbackField(
        IReadOnlyDictionary<string, string> fields,
        string key,
        out string value)
    {
        if (fields.TryGetValue(key, out var rawValue))
        {
            value = rawValue.Trim();
            return value.Length > 0 && !value.Any(char.IsControl);
        }

        value = string.Empty;
        return false;
    }

    /// <summary>驗證已查詢付款金額與幣別，以及非空 provider 訂單號是否屬於 lookup 回傳的訂單集合。</summary>
    private static bool HasMatchingPayment(PaymentStatusResult status, PaymentCallbackOrder order)
    {
        if (!status.PaidAmount.HasValue || status.PaidAmount.Value.Amount <= 0 ||
            string.IsNullOrWhiteSpace(status.ProductOrderId) || order.OrderNumbers is null)
        {
            return false;
        }

        string paidCurrency;
        try
        {
            paidCurrency = status.PaidAmount.Value.Currency;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (!string.Equals(paidCurrency, order.Currency, StringComparison.Ordinal))
        {
            return false;
        }

        return order.OrderNumbers.Any(orderNumber =>
            !string.IsNullOrWhiteSpace(orderNumber) &&
            string.Equals(orderNumber, status.ProductOrderId, StringComparison.Ordinal));
    }

    /// <summary>以固定 operation 記錄預期拒絕，不觸發 LINE；例外文字不含 callback 或產品資料。</summary>
    private PaymentCallbackOutcome Reject(string operation)
    {
        _diagnostics.Report(
            new InvalidOperationException("Payment callback validation failed."),
            operation,
            notify: false);
        return PaymentCallbackOutcome.Conflict;
    }
}

/// <summary>供 callback HTTP adapter 使用的固定結果分類，不含 request、provider 或訂單資料。</summary>
internal enum PaymentCallbackOutcome
{
    Accepted,
    BadRequest,
    Conflict,
    ServiceUnavailable
}
