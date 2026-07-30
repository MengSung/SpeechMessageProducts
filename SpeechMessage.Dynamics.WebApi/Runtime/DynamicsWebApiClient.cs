// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs
// 用途：提供不依賴 Dynamics CRM SDK 的 Web API 用戶端，負責 WhoAmI 與 Package 1 費用查詢等唯讀 HTTP 作業。
//
// 安全與生命週期邊界：
// 1. 不得參考舊版 CRM SDK 套件、組件或命名空間，避免重新引入 SOAP/WCF 與版本耦合。
// 2. HTTP 傳輸只能使用設定檔世代所擁有的共用 HttpClient；查詢形狀必須來自伺服器端核准範本。
// 3. 不建立或快取每位使用者的 CRM 工作階段，也不得以帳號、LINE ID、瀏覽器 Session 或 Token 作為連線池索引鍵。
// 4. 所有組合完成的 URI 都必須留在 ApprovedWebApiRoot 的 HTTPS 來源、連接埠與基底路徑之下。
// 5. 記錄與錯誤訊息不得包含密碼、權杖、授權標頭、完整回應內容、重新導向位置或可識別使用者的資料。
// 6. 要求、回應、串流、取消來源與租用緩衝區都必須有明確擁有者，並在成功、失敗、重試及取消路徑確定釋放。
// ============================================================================

using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 設定檔世代範圍內的不依賴 SDK Dynamics Web API 用戶端，執行 Package 0/1 已登錄且受控的即時作業。
/// 此型別不保存使用者工作階段或要求內容；可重用狀態僅限於由外部設定檔世代管理的 HTTP 傳輸。
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

        // 連線驗證沿用唯讀 WhoAmI，避免為探測另外開放任意查詢能力。
        // 記錄欄位只接受受限長度的 logicalProfileId，不保留使用者識別、權杖或認證內容。
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

        // FetchXML 參數完成編碼後仍須再次檢查最終 URI，確保 scheme、host、port 與 API 基底路徑沒有逸出。
        // 這項檢查不可只依賴範本來源可信，因為實際 URI 還包含資料繫結與 Uri 正規化結果。
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
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 300)));
        var retryAttempts = Math.Clamp(_options.MaxRetryAttempts, 0, 5);

        // 每次嘗試都建立新的 HttpRequestMessage，上一輪的要求與回應會在下一輪前確定 Dispose，
        // 避免重送已送出的要求物件、累積回應串流，或讓授權標頭跨嘗試與跨設定檔外洩。
        // 只重試唯讀 GET 的 429/503；所有等待都受同一個總逾時與呼叫端取消權杖約束。
        for (var attempt = 0; ; attempt++)
        {
            using var request = CreateJsonGetRequest(target);
            OperationExecutionResult? authError;
            try
            {
                authError = await ApplyAuthorizationAsync(request, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // linked token 已取消、但呼叫端 token 尚未取消，代表整體要求逾時；
                // 對外回傳有界 timeout 結果，同時保留呼叫端主動取消時的 OperationCanceledException。
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.UpstreamTimeout,
                    $"Operation '{operationId}' timed out while acquiring authorization.");
            }

            if (authError is not null)
            {
                return authError;
            }

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
                _logger.LogWarning(
                    "Upstream transport failure for {OperationId}. ExceptionType={ExceptionType}",
                    operationId,
                    ex.GetType().Name);
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.UpstreamFailure,
                    $"Operation '{operationId}' transport failure.");
            }

            using (response)
            {
                if (IsRetryableReadStatus(response.StatusCode) && attempt < retryAttempts)
                {
                    var delay = ResolveRetryDelay(response, attempt);
                    _logger.LogWarning(
                        "Retrying read operation {OperationId} after HTTP {StatusCode}. Attempt={Attempt} DelayMs={DelayMs}",
                        operationId,
                        (int)response.StatusCode,
                        attempt + 1,
                        (long)delay.TotalMilliseconds);
                    try
                    {
                        await Task.Delay(delay, timeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.UpstreamTimeout,
                            $"Operation '{operationId}' timed out while waiting to retry.");
                    }

                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return OperationExecutionResult.Failure(
                        DynamicsErrorCodes.Unauthorized,
                        $"Operation '{operationId}' was not authorized by upstream.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Upstream returned {StatusCode} for {OperationId}. HasLocation={HasLocation}",
                        (int)response.StatusCode,
                        operationId,
                        response.Headers.Location is not null);

                    // IFD/claims 環境常以 302 導向 ADFS 登入頁，而不是直接回傳 401。
                    // Windows NTLM 認證無法完成這條瀏覽器登入導向；此處不跟隨導向，也不記錄 Location，
                    // 避免把內部 ADFS 位址、查詢參數或可能含狀態值的 URL 洩漏到記錄與呼叫端。
                    if ((int)response.StatusCode is >= 300 and < 400)
                    {
                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.UpstreamFailure,
                            $"Operation '{operationId}' failed with HTTP {(int)response.StatusCode} redirect." +
                            " This usually means IFD/claims auth. Windows credentials are not enough for Web API;" +
                            " configure AdfsOAuth bearer/service flow.");
                    }

                    return OperationExecutionResult.Failure(
                        DynamicsErrorCodes.UpstreamFailure,
                        $"Operation '{operationId}' failed with HTTP {(int)response.StatusCode}.");
                }

                if (response.Content.Headers.ContentEncoding.Count > 0)
                {
                    return OperationExecutionResult.Failure(
                        DynamicsErrorCodes.UpstreamFailure,
                        $"Operation '{operationId}' returned unsupported Content-Encoding.");
                }

                JsonElement? data;
                try
                {
                    var read = await ReadBoundedJsonAsync(
                        response.Content,
                        Math.Clamp(_options.MaxResponseBytes, 1024, 67_108_864),
                        timeoutCts.Token).ConfigureAwait(false);
                    if (read.TooLarge)
                    {
                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.UpstreamFailure,
                            $"Operation '{operationId}' response exceeded the configured byte limit.");
                    }

                    data = read.Data;
                }
                catch (JsonException)
                {
                    return OperationExecutionResult.Failure(
                        DynamicsErrorCodes.UpstreamFailure,
                        $"Operation '{operationId}' returned non-JSON payload.");
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return OperationExecutionResult.Failure(
                        DynamicsErrorCodes.UpstreamTimeout,
                        $"Operation '{operationId}' timed out while reading the response.");
                }

                // ApprovedWebApiRoot 是設定檔世代內部的 outbound 路由與 SSRF 防護資料，唯一 owner 是
                // Dynamics Web API runtime；產品只需要 operation ID、CE 版本與受控作業資料。這裡不得把
                // hostname 或 /api/data/ 路徑放入可序列化結果，否則 Gateway 會原樣跨信任邊界回傳。
                // 移除輸出欄位不改變前面已完成的 URI allowlist、取消、重試與 response Dispose 順序，
                // 同時減少每次成功回應的一個字串配置與 JSON 傳輸成本。
                return OperationExecutionResult.Success(new
                {
                    operationId,
                    ceVersion = approvedRoot.CeVersion,
                    data
                });
            }
        }
    }

    private HttpRequestMessage CreateJsonGetRequest(Uri target)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        request.Headers.TryAddWithoutValidation("Prefer", "odata.include-annotations=\"*\"");
        return request;
    }

    private static bool IsRetryableReadStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private TimeSpan ResolveRetryDelay(HttpResponseMessage response, int attempt)
    {
        var maximumDelay = TimeSpan.FromSeconds(Math.Clamp(_options.MaxRetryDelaySeconds, 0, 30));
        var retryAfter = response.Headers.RetryAfter;
        var requestedDelay = retryAfter?.Delta ??
            (retryAfter?.Date is DateTimeOffset date
                ? date - DateTimeOffset.UtcNow
                : TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)));

        if (requestedDelay < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return requestedDelay > maximumDelay ? maximumDelay : requestedDelay;
    }

    private async Task<OperationExecutionResult?> ApplyAuthorizationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_options.AuthMode == DynamicsAuthMode.Windows)
        {
            // Windows/IWA 認證由設定檔世代擁有的 HttpMessageHandler 綁定主機身分；
            // 此處不得額外建立 Authorization 標頭，避免認證資料進入要求物件或跨要求殘留。
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 取消屬於要求生命週期訊號，不是認證失敗；保留原 CancellationToken 語意，
                // 讓外層區分呼叫端取消與整體逾時，並避免產生誤導性的 Unauthorized 記錄。
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "AdfsOAuth token acquisition failed. ExceptionType={ExceptionType}",
                    ex.GetType().Name);
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.Unauthorized,
                    "AdfsOAuth token acquisition failed.");
            }
        }

        return OperationExecutionResult.Failure(
            DynamicsErrorCodes.InvalidConfiguration,
            $"Unsupported AuthMode '{_options.AuthMode}'.");
    }

    private static async Task<BoundedJsonReadResult> ReadBoundedJsonAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
        {
            return new BoundedJsonReadResult(null, TooLarge: true);
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(maximumBytes + 1);
        var totalRead = 0;
        try
        {
            // 多租用一個位元組可在不建立第二份完整 payload 的情況下判斷是否超過上限。
            // 解析完成後只 Clone JsonElement；JsonDocument、串流與租用緩衝區均在本方法內確定釋放。
            while (totalRead <= maximumBytes)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(totalRead, maximumBytes + 1 - totalRead),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    var payload = buffer.AsSpan(0, totalRead);
                    if (payload.IsEmpty || IsAsciiWhitespaceOnly(payload))
                    {
                        return new BoundedJsonReadResult(null, TooLarge: false);
                    }

                    using var document = JsonDocument.Parse(buffer.AsMemory(0, totalRead));
                    return new BoundedJsonReadResult(document.RootElement.Clone(), TooLarge: false);
                }

                totalRead += read;
            }

            return new BoundedJsonReadResult(null, TooLarge: true);
        }
        finally
        {
            // 回傳 ArrayPool 前清除實際寫入範圍，避免下一位租用者讀到前一個組織的回應片段。
            // 僅清除已使用區段可兼顧跨租戶隔離與高吞吐量，並避免對整個大型租用陣列做不必要寫入。
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, Math.Min(totalRead, buffer.Length)));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed record BoundedJsonReadResult(JsonElement? Data, bool TooLarge);

    private static bool IsAsciiWhitespaceOnly(ReadOnlySpan<byte> value)
    {
        foreach (var character in value)
        {
            if (character is not (byte)' ' and not (byte)'\t' and not (byte)'\r' and not (byte)'\n')
            {
                return false;
            }
        }

        return true;
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

        // uiname 是選用顯示名稱屬性；只有在值通過 XML 屬性編碼且非空白時才輸出，
        // 否則移除整個屬性片段，避免留下未繫結 placeholder 或產生格式不完整的 FetchXML。
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
