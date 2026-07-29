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

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.Gateway;

/// <summary>
/// Gateway HTTP 版受控操作執行器。
/// </summary>
public sealed class GatewayDynamicsOperationExecutor : IDynamicsOperationExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

    /// <inheritdoc />
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

        var alias = string.IsNullOrWhiteSpace(request.ProfileAlias)
            ? _options.ProfileAlias
            : request.ProfileAlias;

        if (string.IsNullOrWhiteSpace(alias))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                "ProfileAlias is required.");
        }

        var prefix = string.IsNullOrWhiteSpace(_options.Gateway.ApiPrefix)
            ? "/v1"
            : _options.Gateway.ApiPrefix.TrimEnd('/');

        var baseUri = _options.Gateway.Endpoint.TrimEnd('/') + "/";
        var relative = $"{prefix.TrimStart('/')}/organizations/{Uri.EscapeDataString(alias)}/operations/{Uri.EscapeDataString(request.CapabilityOperationId)}";
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
            response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gateway transport failure for {OperationId}", request.CapabilityOperationId);
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.UpstreamFailure,
                "Gateway transport failure.");
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Gateway returned non-JSON body for {OperationId}", request.CapabilityOperationId);
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.UpstreamFailure,
                    "Gateway returned non-JSON body.");
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
        public bool Succeeded { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public JsonElement? Data { get; set; }

        public OperationExecutionResult ToResult()
        {
            if (Succeeded)
            {
                object? data = Data.HasValue ? Data.Value : null;
                return OperationExecutionResult.Success(data);
            }

            return OperationExecutionResult.Failure(
                ErrorCode ?? DynamicsErrorCodes.UpstreamFailure,
                ErrorMessage ?? "Gateway operation failed.");
        }
    }
}
