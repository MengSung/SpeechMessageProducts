// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs
// 目的：產品端透過 HTTP 呼叫集中式 Gateway，實作 IDynamicsOperationExecutor。
//
// 保母教學：
// - 產品程式仍只依賴 IDynamicsOperationExecutor / IPackage01FeeReadClient。
// - 這個類別負責把 request 轉成 Gateway REST 呼叫。
// - 不要在這裡放 CRM 密碼；Gateway 自己持有 profile 秘密。
// - WorkloadSubjectId 應來自部署設定/服務身分，不是終端使用者任意輸入。
// ============================================================================

using System.Buffers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.Configuration;

namespace SpeechMessage.Dynamics.ProductClient.Gateway;

/// <summary>
/// Gateway HTTP 版受控操作執行器。
/// </summary>
public sealed class GatewayDynamicsOperationExecutor : IDynamicsOperationExecutor
{
    private const int MaximumReadBufferBytes = 16 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly HttpClient _httpClient;
    private readonly ProductDynamicsOptions _options;
    private readonly ILogger<GatewayDynamicsOperationExecutor> _logger;

    public GatewayDynamicsOperationExecutor(
        HttpClient httpClient,
        IOptions<ProductDynamicsOptions> options,
        ILogger<GatewayDynamicsOperationExecutor> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 將產品已登錄的 capability operation 送往設定的 Gateway，並在同一個 HTTP 回應生命週期中
    /// 以有限位元組讀取、嚴格 UTF-8 與封閉 JSON 契約處理結果。此方法不接受請求指定 profile
    /// 路由，也不把工作負載主體、CRM URL、權杖、接續連結或未登錄 JSON 成員帶入回傳值；任何
    /// 傳輸、大小、格式或 branch 驗證失敗都會轉換為已淨化的 fail-closed 上游失敗。
    /// </summary>
    public async Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_options.Gateway is null || string.IsNullOrWhiteSpace(_options.Gateway.Endpoint))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "Gateway endpoint is not configured.");
        }

        var configuredAlias = _options.ProfileAlias?.Trim();
        if (string.IsNullOrWhiteSpace(configuredAlias))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                "ProfileAlias is required.");
        }

        var requestedAlias = request.ProfileAlias?.Trim();
        if (!string.IsNullOrWhiteSpace(requestedAlias)
            && !string.Equals(requestedAlias, configuredAlias, StringComparison.OrdinalIgnoreCase))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                "The request ProfileAlias cannot override the configured Gateway profile.");
        }

        var maximumResponseBytes = _options.Gateway.MaxResponseBytes;
        if (maximumResponseBytes is
            < GatewayProductClientLimits.MinimumResponseBytes or
            > GatewayProductClientLimits.MaximumResponseBytes)
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                $"Gateway MaxResponseBytes must be between " +
                $"{GatewayProductClientLimits.MinimumResponseBytes} and " +
                $"{GatewayProductClientLimits.MaximumResponseBytes}.");
        }

        var prefix = string.IsNullOrWhiteSpace(_options.Gateway.ApiPrefix)
            ? "/v1"
            : _options.Gateway.ApiPrefix.TrimEnd('/');

        var baseUri = _options.Gateway.Endpoint.TrimEnd('/') + "/";
        var relative = $"{prefix.TrimStart('/')}/organizations/{Uri.EscapeDataString(configuredAlias)}/operations/{Uri.EscapeDataString(request.CapabilityOperationId)}";
        var target = new Uri(new Uri(baseUri, UriKind.Absolute), relative);

        var body = new GatewayOperationHttpBody
        {
            IdempotencyKey = request.IdempotencyKey,
            Parameters = request.Parameters.ToDictionary(x => x.Key, x => x.Value)
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Gateway transport failure for {OperationId}. ExceptionType={ExceptionType}",
                request.CapabilityOperationId,
                ex.GetType().Name);
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.UpstreamFailure,
                "Gateway transport failure.");
        }

        using (response)
        {
            BoundedPayloadReadResult read;
            try
            {
                read = await ReadBoundedPayloadAsync(
                    response.Content,
                    maximumResponseBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Gateway response read failure for {OperationId}. ExceptionType={ExceptionType}",
                    request.CapabilityOperationId,
                    ex.GetType().Name);
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.UpstreamFailure,
                    "Gateway response read failure.");
            }

            if (read.TooLarge)
            {
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.UpstreamFailure,
                    "Gateway response exceeded the configured byte limit.");
            }

            var payload = read.Payload;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.UpstreamFailure,
                    $"Gateway returned empty body with HTTP {(int)response.StatusCode}.");
            }

            OperationExecutionResult? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<OperationExecutionResultDto>(payload, JsonOptions)
                    ?.ToResult();
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                _logger.LogWarning(
                    "Gateway returned an invalid response contract for {OperationId}. ExceptionType={ExceptionType}",
                    request.CapabilityOperationId,
                    ex.GetType().Name);
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.UpstreamFailure,
                    "Gateway returned an invalid response contract.");
            }

            if (parsed is null)
            {
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.UpstreamFailure,
                    "Gateway response could not be parsed.");
            }

            // Gateway 可能用 400 包 Failure；仍以 body.Succeeded 為準。
            return parsed;
        }
    }

    private static async Task<BoundedPayloadReadResult> ReadBoundedPayloadAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
        {
            return new BoundedPayloadReadResult(null, TooLarge: true);
        }

        var initialCapacity = content.Headers.ContentLength is > 0 and <= int.MaxValue
            ? (int)content.Headers.ContentLength.Value
            : Math.Min(maximumBytes, MaximumReadBufferBytes);
        using var payloadBuffer = new MemoryStream(initialCapacity);
        await using var contentStream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var rentedBuffer = ArrayPool<byte>.Shared.Rent(
            Math.Min(MaximumReadBufferBytes, maximumBytes + 1));
        try
        {
            while (true)
            {
                var remaining = maximumBytes - checked((int)payloadBuffer.Length);
                var requestedRead = Math.Min(rentedBuffer.Length, remaining + 1);
                var bytesRead = await contentStream.ReadAsync(
                    rentedBuffer.AsMemory(0, requestedRead),
                    cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    var payloadBytes = payloadBuffer.GetBuffer()
                        .AsSpan(0, checked((int)payloadBuffer.Length));
                    return new BoundedPayloadReadResult(
                        StrictUtf8.GetString(payloadBytes),
                        TooLarge: false);
                }

                if (bytesRead > remaining)
                {
                    return new BoundedPayloadReadResult(null, TooLarge: true);
                }

                await payloadBuffer.WriteAsync(
                    rentedBuffer.AsMemory(0, bytesRead),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Gateway 回應可能含有受保護資料；陣列池與暫存區在釋放前都必須清除，
            // 避免後續租用者或較長的 GC 生命週期保留前一個回應內容。
            CryptographicOperations.ZeroMemory(rentedBuffer.AsSpan());
            ArrayPool<byte>.Shared.Return(rentedBuffer);

            if (payloadBuffer.TryGetBuffer(out var payloadSegment)
                && payloadBuffer.Length > 0)
            {
                CryptographicOperations.ZeroMemory(
                    payloadSegment.AsSpan(0, checked((int)payloadBuffer.Length)));
            }
        }
    }

    private sealed record BoundedPayloadReadResult(string? Payload, bool TooLarge);

    private sealed class GatewayOperationHttpBody
    {
        public string? IdempotencyKey { get; set; }
        public Dictionary<string, object?>? Parameters { get; set; }
    }

    /// <summary>
    /// JSON DTO：因為 OperationExecutionResult 使用 init/required，反序列化用中間模型較穩。
    /// </summary>
    private sealed class OperationExecutionResultDto
    {
        [JsonPropertyName("succeeded")]
        public bool Succeeded { get; set; }

        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("data")]
        public OperationResponseData? Data { get; set; }

        /// <summary>
        /// 將已完成嚴格 JSON 驗證的線路 DTO 轉為產品共用結果。成功但沒有 data 的情況保留為
        /// 合法 no-data 成功；非 null data 則已由 <see cref="OperationResponseData"/> 建構式
        /// 驗證 discriminator 只選擇一個相符 branch。此方法不重新序列化或保存 payload，確保
        /// response、stream 與 pooled buffer 仍由外層請求 scope 按既有順序釋放。
        /// </summary>
        public OperationExecutionResult ToResult()
        {
            if (Succeeded)
            {
                return OperationExecutionResult.Success(Data);
            }

            return OperationExecutionResult.Failure(
                ErrorCode ?? DynamicsErrorCodes.UpstreamFailure,
                ErrorMessage ?? "Gateway operation failed.");
        }
    }
}
