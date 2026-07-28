// ============================================================================
// 瑼?嚗peechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs
// ?桃?嚗o-SDK Web API client嚗?鞎?WhoAmI ??Package 1 fee-read 撖阡? HTTP ?瑁???
//
// 靽??飛嚗?
// 1. 銝??其遙雿?legacy CRM SDK package / namespace??
// 2. ?芰 HttpClient + server-owned template??
// 3. 銝翰??per-user session嚗? profile 蝝?transport??
// 4. ?????URL 敹??賢 ApprovedWebApiRoot??
// 5. ?蝻箏仃??交?雿?憭望?嚗??臬?????
// ============================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 蝘? no-SDK Dynamics Web API client嚗ackage 0/1 live execution嚗?
/// </summary>
public sealed class DynamicsWebApiClient : IDynamicsWebApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DynamicsWebApiOptions _options;
    private readonly IDynamicsHttpTransport _transport;
    private readonly ISecretResolver _secretResolver;
    private readonly IAdfsOAuthTokenProvider _tokenProvider;
    private readonly ILogger<DynamicsWebApiClient> _logger;

    public DynamicsWebApiClient(
        IOptions<DynamicsWebApiOptions> options,
        IDynamicsHttpTransport transport,
        ISecretResolver secretResolver,
        IAdfsOAuthTokenProvider tokenProvider,
        ILogger<DynamicsWebApiClient> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<OperationExecutionResult> WhoAmIAsync(CancellationToken cancellationToken = default)
    {
        if (!Package01OperationRegistry.TryGet(OperationIds.RuntimeHealthWhoAmI, out var definition) || definition is null)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                DynamicsErrorCodes.UnknownOperation,
                "WhoAmI operation is not registered."));
        }

        return ExecuteRegisteredOperationAsync(definition, new Dictionary<string, object?>(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationExecutionResult> ExecuteRegisteredOperationAsync(
        OperationDefinition definition,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        parameters ??= new Dictionary<string, object?>();

        var missing = definition.Parameters
            .Where(p => p.Required)
            .Where(p => !parameters.ContainsKey(p.Name) || parameters[p.Name] is null ||
                        (parameters[p.Name] is string s && string.IsNullOrWhiteSpace(s)))
            .Select(p => p.Name)
            .ToArray();

        if (missing.Length > 0)
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                $"Missing required parameters: {string.Join(", ", missing)}");
        }

        if (!ApprovedWebApiRootFactory.TryCreate(_options, out var approvedRoot, out var rootError) || approvedRoot is null)
        {
            return rootError ?? OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "Approved Web API root is invalid.");
        }

        if (!Package01ServerOwnedTemplates.TryGetByOperation(definition, out var template) || template is null)
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.NotImplemented,
                $"No server-owned template for '{definition.CapabilityOperationId}'.");
        }

        return template.TemplateKind switch
        {
            "odata-function" => await ExecuteODataGetAsync(
                approvedRoot,
                template.ODataRelativePathTemplate ?? "WhoAmI",
                parameters,
                definition,
                cancellationToken).ConfigureAwait(false),
            "odata-route" => await ExecuteODataGetAsync(
                approvedRoot,
                BindODataPath(template.ODataRelativePathTemplate ?? string.Empty, parameters, out var pathError),
                parameters,
                definition,
                cancellationToken,
                pathError).ConfigureAwait(false),
            "fetchxml" => await ExecuteFetchXmlAsync(
                approvedRoot,
                template,
                parameters,
                definition,
                cancellationToken).ConfigureAwait(false),
            _ => OperationExecutionResult.Failure(
                DynamicsErrorCodes.NotImplemented,
                $"Template kind '{template.TemplateKind}' is not supported yet.")
        };
    }

    private async Task<OperationExecutionResult> ExecuteODataGetAsync(
        ApprovedWebApiRoot approvedRoot,
        string? relativePath,
        IReadOnlyDictionary<string, object?> parameters,
        OperationDefinition definition,
        CancellationToken cancellationToken,
        OperationExecutionResult? prebindError = null)
    {
        if (prebindError is not null)
        {
            return prebindError;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "OData relative path is empty.");
        }

        // runtime.pool.validate.connection ?閬?logicalProfileId嚗?撖阡??Ｘ葫隞 WhoAmI??
        if (definition.CapabilityOperationId == OperationIds.RuntimePoolValidateConnection)
        {
            _logger.LogDebug(
                "Validating connection for logicalProfileId={LogicalProfileId}",
                parameters.TryGetValue("logicalProfileId", out var profile) ? profile : null);
        }

        var target = new Uri(approvedRoot.Value, relativePath.TrimStart('/'));
        if (!ApprovedWebApiRootFactory.IsUnderApprovedRoot(target, approvedRoot.Value))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "Resolved OData URL escapes ApprovedWebApiRoot.");
        }

        return await SendJsonGetAsync(target, approvedRoot, definition.CapabilityOperationId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OperationExecutionResult> ExecuteFetchXmlAsync(
        ApprovedWebApiRoot approvedRoot,
        ServerOwnedTemplate template,
        IReadOnlyDictionary<string, object?> parameters,
        OperationDefinition definition,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(template.EntitySetName) || string.IsNullOrWhiteSpace(template.FetchXmlTemplate))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                $"FetchXML template '{template.TemplateId}' is incomplete.");
        }

        if (!TryBindFetchXml(template.FetchXmlTemplate, parameters, out var fetchXml, out var bindError))
        {
            return OperationExecutionResult.Failure(DynamicsErrorCodes.InvalidParameter, bindError);
        }

        var query = "fetchXml=" + FetchXmlValueEncoder.ToFetchXmlQueryParameter(fetchXml);
        var relative = template.EntitySetName.Trim('/') + "?" + query;
        var target = new Uri(approvedRoot.Value, relative);

        // Uri 撱箸?敺?瑼Ｘ host/base path嚗uery ?臬 fetchXml??
        if (!string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.Host, approvedRoot.Value.Host, StringComparison.OrdinalIgnoreCase) ||
            target.Port != approvedRoot.Value.Port ||
            !target.AbsolutePath.StartsWith(approvedRoot.Value.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "Resolved FetchXML URL escapes ApprovedWebApiRoot.");
        }

        _logger.LogInformation(
            "Executing FetchXML operation {OperationId} via template {TemplateId}",
            definition.CapabilityOperationId,
            template.TemplateId);

        return await SendJsonGetAsync(target, approvedRoot, definition.CapabilityOperationId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OperationExecutionResult> SendJsonGetAsync(
        Uri target,
        ApprovedWebApiRoot approvedRoot,
        string operationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        request.Headers.TryAddWithoutValidation("Prefer", "odata.include-annotations=\"*\"");

        var authError = await ApplyAuthorizationAsync(request, cancellationToken).ConfigureAwait(false);
        if (authError is not null)
        {
            return authError;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 300)));

        HttpResponseMessage response;
        try
        {
            response = await _transport.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.UpstreamTimeout,
                $"Operation '{operationId}' timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Upstream transport failure for {OperationId}", operationId);
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.UpstreamFailure,
                $"Operation '{operationId}' transport failure.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.Unauthorized,
                    $"Operation '{operationId}' was not authorized by upstream.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var location = response.Headers.Location?.ToString();
                _logger.LogWarning(
                    "Upstream returned {StatusCode} for {OperationId}. Location={Location}",
                    (int)response.StatusCode,
                    operationId,
                    location ?? "(none)");

                // IFD/claims environments often return 302 to ADFS instead of 401.
                // Windows NTLM credentials cannot complete that login redirect path.
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    return OperationExecutionResult.Failure(
                        DynamicsErrorCodes.UpstreamFailure,
                        $"Operation '{operationId}' failed with HTTP {(int)response.StatusCode} redirect" +
                        (string.IsNullOrWhiteSpace(location) ? "." : $" to '{location}'.") +
                        " This usually means IFD/claims auth. Windows credentials are not enough for Web API;" +
                        " configure AdfsOAuth bearer/service flow.");
                }

                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.UpstreamFailure,
                    $"Operation '{operationId}' failed with HTTP {(int)response.StatusCode}.");
            }

            object? data;
            try
            {
                data = string.IsNullOrWhiteSpace(body)
                    ? null
                    : JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.UpstreamFailure,
                    $"Operation '{operationId}' returned non-JSON payload.");
            }

            return OperationExecutionResult.Success(new
            {
                operationId,
                ceVersion = approvedRoot.CeVersion,
                approvedWebApiRoot = approvedRoot.Value.ToString(),
                data
            });
        }
    }

    private async Task<OperationExecutionResult?> ApplyAuthorizationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_options.AuthMode == DynamicsAuthMode.Windows)
        {
            // Windows auth is attached on the handler; no Authorization header.
            return null;
        }

        if (_options.AuthMode == DynamicsAuthMode.AdfsOAuth)
        {
            try
            {
                var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return OperationExecutionResult.Failure(
                        DynamicsErrorCodes.SecretResolutionFailed,
                        "AdfsOAuth token provider returned an empty access token.");
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AdfsOAuth token acquisition failed.");
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.Unauthorized,
                    $"AdfsOAuth token acquisition failed: {ex.Message}");
            }
        }

        return OperationExecutionResult.Failure(
            DynamicsErrorCodes.InvalidConfiguration,
            $"Unsupported AuthMode '{_options.AuthMode}'.");
    }
    private static string? BindODataPath(
        string template,
        IReadOnlyDictionary<string, object?> parameters,
        out OperationExecutionResult? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(template))
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "OData path template is empty.");
            return null;
        }

        var path = template;
        foreach (var pair in parameters)
        {
            var text = Convert.ToString(pair.Value)?.Trim() ?? string.Empty;
            if (!IsSafeLogicalName(text))
            {
                error = OperationExecutionResult.Failure(
                    DynamicsErrorCodes.InvalidParameter,
                    $"Parameter '{pair.Key}' contains unsafe characters for OData path.");
                return null;
            }

            path = path.Replace("{{" + pair.Key + "}}", text, StringComparison.Ordinal);
        }

        if (path.Contains("{{", StringComparison.Ordinal))
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                "OData path template still contains unbound placeholders.");
            return null;
        }

        return path;
    }

    private static bool TryBindFetchXml(
        string template,
        IReadOnlyDictionary<string, object?> parameters,
        out string fetchXml,
        out string error)
    {
        fetchXml = string.Empty;
        error = string.Empty;

        var bound = CollapseWhitespace(template);

        // optional uiname attributes
        bound = BindOptionalNameAttribute(bound, parameters, "contactName", "contactNameAttr");
        bound = BindOptionalNameAttribute(bound, parameters, "dedicationBookingName", "dedicationBookingNameAttr");
        bound = BindOptionalNameAttribute(bound, parameters, "lessonName", "lessonNameAttr");

        if (parameters.TryGetValue("contactId", out var contactId) || bound.Contains("{{contactId}}", StringComparison.Ordinal))
        {
            if (bound.Contains("{{contactId}}", StringComparison.Ordinal))
            {
                if (!FetchXmlValueEncoder.TryEncodeGuid(contactId, out var encoded, out error))
                {
                    return false;
                }

                bound = bound.Replace("{{contactId}}", encoded, StringComparison.Ordinal);
            }
        }

        if (bound.Contains("{{dedicationBookingId}}", StringComparison.Ordinal))
        {
            if (!parameters.TryGetValue("dedicationBookingId", out var bookingId) ||
                !FetchXmlValueEncoder.TryEncodeGuid(bookingId, out var encoded, out error))
            {
                error = string.IsNullOrEmpty(error) ? "dedicationBookingId is required." : error;
                return false;
            }

            bound = bound.Replace("{{dedicationBookingId}}", encoded, StringComparison.Ordinal);
        }

        if (bound.Contains("{{discipleLessonId}}", StringComparison.Ordinal))
        {
            if (!parameters.TryGetValue("discipleLessonId", out var lessonId) ||
                !FetchXmlValueEncoder.TryEncodeGuid(lessonId, out var encoded, out error))
            {
                error = string.IsNullOrEmpty(error) ? "discipleLessonId is required." : error;
                return false;
            }

            bound = bound.Replace("{{discipleLessonId}}", encoded, StringComparison.Ordinal);
        }

        if (bound.Contains("{{startDate}}", StringComparison.Ordinal))
        {
            if (!parameters.TryGetValue("startDate", out var startDate) ||
                !FetchXmlValueEncoder.TryEncodeDate(startDate, out var encoded, out error))
            {
                error = string.IsNullOrEmpty(error) ? "startDate is required." : error;
                return false;
            }

            bound = bound.Replace("{{startDate}}", encoded, StringComparison.Ordinal);
        }

        if (bound.Contains("{{endDate}}", StringComparison.Ordinal))
        {
            if (!parameters.TryGetValue("endDate", out var endDate) ||
                !FetchXmlValueEncoder.TryEncodeDate(endDate, out var encoded, out error))
            {
                error = string.IsNullOrEmpty(error) ? "endDate is required." : error;
                return false;
            }

            bound = bound.Replace("{{endDate}}", encoded, StringComparison.Ordinal);
        }

        if (bound.Contains("{{paidPeriod}}", StringComparison.Ordinal))
        {
            if (!parameters.TryGetValue("paidPeriod", out var paidPeriod) ||
                !FetchXmlValueEncoder.TryEncodeString(paidPeriod, required: true, out var encoded, out error))
            {
                error = string.IsNullOrEmpty(error) ? "paidPeriod is required." : error;
                return false;
            }

            bound = bound.Replace("{{paidPeriod}}", encoded, StringComparison.Ordinal);
        }

        if (bound.Contains("{{", StringComparison.Ordinal))
        {
            error = "FetchXML template still contains unbound placeholders.";
            return false;
        }

        fetchXml = bound;
        return true;
    }

    private static string BindOptionalNameAttribute(
        string template,
        IReadOnlyDictionary<string, object?> parameters,
        string parameterName,
        string placeholderName)
    {
        var token = "{{" + placeholderName + "}}";
        if (!template.Contains(token, StringComparison.Ordinal))
        {
            return template;
        }

        if (parameters.TryGetValue(parameterName, out var raw) &&
            FetchXmlValueEncoder.TryEncodeString(raw, required: false, out var encoded, out _) &&
            !string.IsNullOrEmpty(encoded))
        {
            return template.Replace(token, $" uiname=\"{encoded}\"", StringComparison.Ordinal);
        }

        return template.Replace(token, string.Empty, StringComparison.Ordinal);
    }

    private static bool IsSafeLogicalName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static string CollapseWhitespace(string value)
    {
        var sb = new StringBuilder(value.Length);
        var previousWhitespace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                {
                    sb.Append(' ');
                    previousWhitespace = true;
                }

                continue;
            }

            previousWhitespace = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }
}
