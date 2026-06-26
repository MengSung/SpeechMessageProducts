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

            var status = SinopacStatusMapper.MapCreate(response);
            return new PaymentCreateResult
            {
                Status = status,
                ProductOrderId = FirstNonEmpty(response.OrderNo, request.ProductOrderId),
                ProviderOrderRef = FirstNonEmpty(response.TSNo, response.OrderNo),
                PaymentPageUrl = ResolvePaymentPageUrl(response),
                Error = SinopacStatusMapper.IsProviderRejected(response)
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
                    ["web_atm_url"] = response.ATMParam?.WebAtmURL ?? string.Empty
                })
            };
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
            _httpClient.DefaultRequestHeaders.Remove("X-KeyID");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-KeyID", SinopacRequestMapper.GetXKeyId(profile));

            using var response = await _httpClient.PostAsync(ResolveEndpoint(profile, route), requestContent, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new SinopacHttpStatusException(response.StatusCode);
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
            ["payment_category"] = response.Param3
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
        public SinopacHttpStatusException(HttpStatusCode statusCode)
            : base($"Sinopac returned HTTP {(int)statusCode} {statusCode}.")
        {
        }

        public SinopacHttpStatusException(string message)
            : base(message)
        {
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
