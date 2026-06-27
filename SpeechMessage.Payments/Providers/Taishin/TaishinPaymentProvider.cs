using System.Net;
using Newtonsoft.Json;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Diagnostics;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Taishin;

internal sealed class TaishinPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;

    public TaishinPaymentProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public PaymentProviderKind ProviderKind => PaymentProviderKind.Taishin;

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
                Amount = ParseMinorAmount(response.Params?.Amount),
                Currency = NormalizeCurrency(response.Params?.Currency),
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

    private static decimal? ParseMinorAmount(string? amount)
    {
        return decimal.TryParse(amount, out var parsed) ? parsed / 100m : null;
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
