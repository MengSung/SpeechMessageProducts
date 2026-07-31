// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs
// 用途：提供不依賴 Dynamics CRM SDK 的受控 Web API 用戶端，將上游 OData 回應在傳輸邊界投影為封閉 DTO。
//
// 安全與生命週期邊界：
// 1. 此型別只執行 registry 登錄的唯讀模板；不接受任意 CRM URL、欄位、OData、FetchXML 或呼叫端授權資料。
// 2. CRM 原始 JSON、@odata annotation、nextLink、CRM 主機名稱與 HttpResponseMessage 僅存在於本次 request scope，
//    絕不可跨入 OperationExecutionResult.Data、Gateway、ProductClient、queue、cache 或 session。
// 3. 每頁 request、response、stream、linked timeout CTS 與 ArrayPool buffer 皆由此 scope 唯一擁有，並在成功、
//    重試、取消、投影失敗與 continuation 拒絕時確定釋放；沒有 static 的 token、URI、response 或頁面集合。
// 4. continuation 必須先留在 ApprovedWebApiRoot 內並通過 cycle、page、byte、row 上限檢查，才可建立下一個
//    credential-bearing request；任何違反都不回傳 partial result。
// ============================================================================

using System.Buffers;
using System.Globalization;
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
/// 設定檔世代範圍內的不依賴 SDK Dynamics Web API 用戶端。此型別只保有由外部 profile runtime 擁有的
/// 傳輸、祕密與 token provider 參考；它不快取使用者、LINE ID、瀏覽器 Session、token、CRM URI 或
/// 上游回應。每次執行會在單一有界 scope 中完成授權、HTTP、paging、嚴格投影與資源清理，只有封閉的
/// <see cref="OperationResponseData"/> 可以離開此傳輸邊界。
/// </summary>
public sealed class DynamicsWebApiClient : IDynamicsWebApiClient
{
    private const string FormattedValueAnnotationSuffix = "@OData.Community.Display.V1.FormattedValue";

    private readonly DynamicsWebApiOptions _options;
    private readonly IDynamicsHttpTransport _transport;
    private readonly ISecretResolver _secretResolver;
    private readonly IAdfsOAuthTokenProvider _tokenProvider;
    private readonly ILogger<DynamicsWebApiClient> _logger;

    /// <summary>
    /// 建立受設定檔世代擁有的 Web API client。建構子只保存不可變的依賴參考，不啟動 HTTP、背景工作、
    /// timer 或 token refresh；因此設定替換與 runtime drain 的唯一 owner 仍可在外層精確 dispose transport
    /// 與 provider。任何 null 依賴立即失敗，避免後續 request scope 在不完整 owner 圖中遺漏 cleanup。
    /// </summary>
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

    /// <summary>
    /// 執行唯一已登錄的唯讀 WhoAmI health operation。此便利方法不自行建構 URI 或回應形狀，而是回到同一份
    /// immutable registry 與嚴格投影路徑，確保 health probe 不會變成可繞過 capability、paging、timeout、
    /// authorization 或 disposal 規則的第二條通道。
    /// </summary>
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

    /// <summary>
    /// 執行 server-owned <paramref name="definition"/>。必要參數、封閉 response kind、response limits 與
    /// Unsupported 邊界都在建立 root、模板、request、authorization 或 transport 之前檢查；因此不受支援的
    /// metadata 或錯誤輸入不會留下 request、token、session 或 socket 資源。成功資料只能透過後續的嚴格
    /// projector 形成 <see cref="OperationResponseData"/>，不會保留原始 OData document。
    /// </summary>
    public async Task<OperationExecutionResult> ExecuteRegisteredOperationAsync(
        OperationDefinition definition,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        parameters ??= new Dictionary<string, object?>();

        var missing = definition.Parameters
            .Where(parameter => parameter.Required)
            .Where(parameter => !parameters.ContainsKey(parameter.Name) ||
                                parameters[parameter.Name] is null ||
                                (parameters[parameter.Name] is string text && string.IsNullOrWhiteSpace(text)))
            .Select(parameter => parameter.Name)
            .ToArray();

        if (missing.Length > 0)
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                $"Missing required parameters: {string.Join(", ", missing)}");
        }

        // Unsupported 是封閉 product-response boundary，而非稍後再處理的 template 種類。必須在任何可取得
        // credential、建立 URI 或送出 HTTP 的動作之前失敗，避免 raw metadata 被意外包進成功 envelope。
        if (definition.ResponseKind == OperationResponseKind.Unsupported)
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.NotImplemented,
                $"Operation '{definition.CapabilityOperationId}' has no approved typed response contract.");
        }

        if (!TryGetResponseLimits(definition, out _, out var limitError))
        {
            return OperationExecutionResult.Failure(DynamicsErrorCodes.InvalidConfiguration, limitError);
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

    /// <summary>
    /// 將已繫結的 OData relative path 解析到本 profile 的唯一 ApprovedWebApiRoot。URI 解析完成後再次驗證
    /// scheme、origin、port 與 virtual-directory root，避免可信模板日後被變更時逸出設定世代的 outbound
    /// allowlist。此方法只組合值，不取得 authorization 或建立 HTTP owner；真正 request lifecycle 由
    /// <see cref="SendJsonGetAsync"/> 在下一層唯一管理。
    /// </summary>
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

        return await SendJsonGetAsync(target, approvedRoot, definition, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 以 server-owned FetchXML template 建立受控 GET。輸入只能是已驗證且具 context-specific encoder 的 typed
    /// parameters；完成 query encoding 後仍驗證最終 URI 以防 URI normalization 或 template 變更越過 root。
    /// FetchXML 字串、request 與其後 response 都只存活在本次呼叫 scope，不進入 response DTO、cache 或
    /// session；paging 與 stream disposal 交由 <see cref="SendJsonGetAsync"/> 統一處理。
    /// </summary>
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

        if (!ApprovedWebApiRootFactory.IsUnderApprovedRoot(target, approvedRoot.Value))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "Resolved FetchXML URL escapes ApprovedWebApiRoot.");
        }

        _logger.LogInformation(
            "Executing FetchXML operation {OperationId} via template {TemplateId}",
            definition.CapabilityOperationId,
            template.TemplateId);

        return await SendJsonGetAsync(target, approvedRoot, definition, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在單一 request scope 執行 bounded GET 與 server-driven paging。linked timeout CTS、visited continuation
    /// set、aggregate typed records、byte/page/row counters 都是此方法的唯一 owner，沒有 static 或 runtime cache；
    /// 每一輪 retry 都重新建立並 dispose request/response，且 retry 不增加 page、row 或 visited 計數。下一頁
    /// 必須先在已解析的上游頁面內通過 root、cycle 與 policy 驗證，之後才建立或授權下一個 credential-bearing
    /// request；違反時回傳 sanitized failure 並丟棄所有 partial records。
    /// </summary>
    private async Task<OperationExecutionResult> SendJsonGetAsync(
        Uri initialTarget,
        ApprovedWebApiRoot approvedRoot,
        OperationDefinition definition,
        CancellationToken cancellationToken)
    {
        if (!TryGetResponseLimits(definition, out var limits, out var limitError))
        {
            return OperationExecutionResult.Failure(DynamicsErrorCodes.InvalidConfiguration, limitError);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 300)));

        var retryAttempts = Math.Clamp(_options.MaxRetryAttempts, 0, 5);
        var visitedContinuations = new HashSet<string>(StringComparer.Ordinal)
        {
            CanonicalizeUri(initialTarget)
        };
        var feeRecords = definition.ResponseKind == OperationResponseKind.Package01FeeRecords
            ? new List<Package01FeeRecord>()
            : null;
        var storLessonRecords = definition.ResponseKind == OperationResponseKind.Package01StorLessonRecords
            ? new List<Package01StorLessonRecord>()
            : null;
        WhoAmIResponseData? whoAmI = null;
        var cumulativeBytes = 0;
        var pageCount = 0;
        var target = initialTarget;

        while (true)
        {
            // pageCount 僅在成功讀取與完整投影後遞增。429/503 retry 與任何失敗頁面都不得消耗 paging
            // policy，否則短暫服務保護會錯誤改變 registry 定義的結果語意。
            for (var attempt = 0; ; attempt++)
            {
                using var request = CreateJsonGetRequest(target);
                OperationExecutionResult? authorizationError;
                try
                {
                    authorizationError = await ApplyAuthorizationAsync(request, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return OperationExecutionResult.Failure(
                        DynamicsErrorCodes.UpstreamTimeout,
                        $"Operation '{definition.CapabilityOperationId}' timed out while acquiring authorization.");
                }

                if (authorizationError is not null)
                {
                    return authorizationError;
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
                        $"Operation '{definition.CapabilityOperationId}' timed out.");
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        "Upstream transport failure for {OperationId}. ExceptionType={ExceptionType}",
                        definition.CapabilityOperationId,
                        exception.GetType().Name);
                    return OperationExecutionResult.Failure(
                        DynamicsErrorCodes.UpstreamFailure,
                        $"Operation '{definition.CapabilityOperationId}' transport failure.");
                }

                using (response)
                {
                    if (IsRetryableReadStatus(response.StatusCode) && attempt < retryAttempts)
                    {
                        var delay = ResolveRetryDelay(response, attempt);
                        _logger.LogWarning(
                            "Retrying read operation {OperationId} after HTTP {StatusCode}. Attempt={Attempt} DelayMs={DelayMs}",
                            definition.CapabilityOperationId,
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
                                $"Operation '{definition.CapabilityOperationId}' timed out while waiting to retry.");
                        }

                        continue;
                    }

                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.Unauthorized,
                            $"Operation '{definition.CapabilityOperationId}' was not authorized by upstream.");
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning(
                            "Upstream returned {StatusCode} for {OperationId}. HasLocation={HasLocation}",
                            (int)response.StatusCode,
                            definition.CapabilityOperationId,
                            response.Headers.Location is not null);

                        if ((int)response.StatusCode is >= 300 and < 400)
                        {
                            return OperationExecutionResult.Failure(
                                DynamicsErrorCodes.UpstreamFailure,
                                $"Operation '{definition.CapabilityOperationId}' failed with HTTP {(int)response.StatusCode} redirect." +
                                " This usually means IFD/claims auth. Windows credentials are not enough for Web API;" +
                                " configure AdfsOAuth bearer/service flow.");
                        }

                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.UpstreamFailure,
                            $"Operation '{definition.CapabilityOperationId}' failed with HTTP {(int)response.StatusCode}.");
                    }

                    if (response.Content.Headers.ContentEncoding.Count > 0)
                    {
                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.UpstreamFailure,
                            $"Operation '{definition.CapabilityOperationId}' returned unsupported Content-Encoding.");
                    }

                    BoundedJsonReadResult read;
                    try
                    {
                        read = await ReadBoundedJsonAsync(
                            response.Content,
                            limits.MaximumPageBytes,
                            timeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (JsonException)
                    {
                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.UpstreamFailure,
                            $"Operation '{definition.CapabilityOperationId}' returned non-JSON payload.");
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.UpstreamTimeout,
                            $"Operation '{definition.CapabilityOperationId}' timed out while reading the response.");
                    }

                    if (read.TooLarge ||
                        read.ByteCount > limits.MaximumCumulativeResponseBytes - cumulativeBytes)
                    {
                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.UpstreamFailure,
                            $"Operation '{definition.CapabilityOperationId}' response exceeded the configured byte limit.");
                    }

                    cumulativeBytes += read.ByteCount;
                    if (!TryProjectPage(definition.ResponseKind, read.Data, out var page))
                    {
                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.UpstreamFailure,
                            $"Operation '{definition.CapabilityOperationId}' returned an unapproved response shape.");
                    }

                    if (page.WhoAmI is not null)
                    {
                        whoAmI = page.WhoAmI;
                    }

                    if (page.FeeRecords is not null)
                    {
                        if (page.FeeRecords.Count > limits.MaximumResultItemCount - feeRecords!.Count)
                        {
                            return OperationExecutionResult.Failure(
                                DynamicsErrorCodes.UpstreamFailure,
                                $"Operation '{definition.CapabilityOperationId}' response exceeded the configured result-row limit.");
                        }

                        feeRecords.AddRange(page.FeeRecords);
                    }

                    if (page.StorLessonRecords is not null)
                    {
                        if (page.StorLessonRecords.Count > limits.MaximumResultItemCount - storLessonRecords!.Count)
                        {
                            return OperationExecutionResult.Failure(
                                DynamicsErrorCodes.UpstreamFailure,
                                $"Operation '{definition.CapabilityOperationId}' response exceeded the configured result-row limit.");
                        }

                        storLessonRecords.AddRange(page.StorLessonRecords);
                    }

                    pageCount++;
                    if (page.Continuation is null)
                    {
                        return CreateSuccessfulResult(definition, approvedRoot.CeVersion, whoAmI, feeRecords, storLessonRecords);
                    }

                    // Response projector 只允許 collection response 帶 nextLink；若 WhoAmI 或未知 branch 走到此處，
                    // 一律 fail-closed，避免不受控的 continuation 把認證請求延長到下一頁。
                    if (definition.ResponseKind is not (OperationResponseKind.Package01FeeRecords or OperationResponseKind.Package01StorLessonRecords) ||
                        pageCount >= limits.MaximumPageCount ||
                        !ApprovedWebApiRootFactory.TryResolveContinuation(
                            page.Continuation,
                            target,
                            approvedRoot.Value,
                            out var nextTarget) ||
                        nextTarget is null ||
                        !visitedContinuations.Add(CanonicalizeUri(nextTarget)))
                    {
                        return OperationExecutionResult.Failure(
                            DynamicsErrorCodes.UpstreamFailure,
                            $"Operation '{definition.CapabilityOperationId}' returned an unsafe or over-limit continuation.");
                    }

                    // nextTarget 在 response 尚被 using owner 管理時已完成純 URI 驗證；離開 using 後本頁
                    // response/content/stream 都被 dispose，下一個 outer loop 才會新建 request 與附加 authorization。
                    target = nextTarget;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 建立單次 GET request，所有 OData headers 都是 server-owned 且 request scoped。不得使用
    /// HttpClient.DefaultRequestHeaders，以免 authorization 或 caller state 在 profile generation、retry、session
    /// 或不同 organization 間殘留；request 的 using owner 由 paging loop 保證 dispose。
    /// </summary>
    private static HttpRequestMessage CreateJsonGetRequest(Uri target)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        request.Headers.TryAddWithoutValidation(
            "Prefer",
            "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");
        return request;
    }

    /// <summary>
    /// 判斷唯讀 GET 可安全重試的上游服務保護狀態。此判斷不保存 response 或 retry state；實際 response dispose、
    /// bounded delay 與取消傳播由呼叫端的同一 request scope 負責。
    /// </summary>
    private static bool IsRetryableReadStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    /// <summary>
    /// 將 Retry-After 或有限 exponential fallback 截斷到 deployment-owned 最大等待值。回傳值不包含 upstream
    /// URL、token、response body 或 session；呼叫端以 linked timeout CTS 等待，因此 retry 不會在 request
    /// 已取消或 runtime drain 之後留下 timer 或背景 task。
    /// </summary>
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

    /// <summary>
    /// 對單次 request 施加 profile-owned authorization。Windows 模式刻意不建立 Authorization header，讓 handler
    /// 使用已驗證的 host identity；ADFS 模式只把本次取得的 bearer token 放進目前 request，request dispose 後
    /// 即釋放 header 參考。token provider 的 cache/refresh lifecycle 屬於 profile runtime；此方法不把 token、
    /// credential、exception text 或 principal 放入 result、log、static 或 session。
    /// </summary>
    private async Task<OperationExecutionResult?> ApplyAuthorizationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_options.AuthMode == DynamicsAuthMode.Windows)
        {
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
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "AdfsOAuth token acquisition failed. ExceptionType={ExceptionType}",
                    exception.GetType().Name);
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.Unauthorized,
                    "AdfsOAuth token acquisition failed.");
            }
        }

        return OperationExecutionResult.Failure(
            DynamicsErrorCodes.InvalidConfiguration,
            $"Unsupported AuthMode '{_options.AuthMode}'.");
    }

    /// <summary>
    /// 驗證 immutable operation response policy 並將 registry per-page 上限與 deployment hard cap 取較小值。
    /// 回傳的值只屬於目前 request scope，不能寫回 options 或 definition；這避免測試、profile reload 或不同
    /// organization 的 mutable state 相互污染。任何零、負數、溢位風險或無法形成 closed branch 的 policy 在
    /// outbound URI/authorization 之前 fail-closed。
    /// </summary>
    private bool TryGetResponseLimits(
        OperationDefinition definition,
        out ResponseLimits limits,
        out string error)
    {
        limits = default;
        error = string.Empty;

        if (definition.ResponseKind is not (OperationResponseKind.WhoAmI or
                                            OperationResponseKind.Package01FeeRecords or
                                            OperationResponseKind.Package01StorLessonRecords))
        {
            error = "Operation response kind is not supported.";
            return false;
        }

        if (definition.MaximumPageCount <= 0 ||
            definition.MaximumPageBytes <= 0 ||
            definition.MaximumCumulativeResponseBytes <= 0 ||
            definition.MaximumResultItemCount <= 0 ||
            _options.MaxResponseBytes <= 0)
        {
            error = "Operation response limits must be positive.";
            return false;
        }

        var effectivePageBytes = Math.Min(definition.MaximumPageBytes, _options.MaxResponseBytes);
        if (effectivePageBytes <= 0)
        {
            error = "Effective operation page limit is invalid.";
            return false;
        }

        limits = new ResponseLimits(
            definition.MaximumPageCount,
            effectivePageBytes,
            definition.MaximumCumulativeResponseBytes,
            definition.MaximumResultItemCount);
        return true;
    }

    /// <summary>
    /// 將已完整讀取但仍屬傳輸私有範圍的 JSON 頁投影為封閉 branch。此方法是 raw OData 的唯一 decoder；
    /// <see cref="JsonElement"/> 不會由此方法返回到 public envelope。每個 object 都檢查 duplicate property、
    /// 每個欄位都檢查精確 allowlist 與型別，未知 annotation、CRM extension data 或不相符的 branch 一律拒絕，
    /// 讓 schema 漂移在 connector 停止而不是洩漏到 Gateway/ProductClient。
    /// </summary>
    private static bool TryProjectPage(
        OperationResponseKind responseKind,
        JsonElement? data,
        out ProjectedPage page)
    {
        page = null!;
        if (data is not JsonElement root || root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return responseKind switch
        {
            OperationResponseKind.WhoAmI => TryProjectWhoAmI(root, out page),
            OperationResponseKind.Package01FeeRecords => TryProjectFeeRecords(root, out page),
            OperationResponseKind.Package01StorLessonRecords => TryProjectStorLessonRecords(root, out page),
            _ => false
        };
    }

    /// <summary>
    /// 嚴格投影 WhoAmI 的三個 nullable GUID。OData context 是 connector 內部可丟棄 metadata，其他未登錄
    /// property（含 nextLink）皆拒絕；這保證 health result 不會攜帶 upstream URL、profile 資訊或 extension
    /// data。此純 parsing helper 不持有 stream、response、buffer、token 或跨 request state。
    /// </summary>
    private static bool TryProjectWhoAmI(JsonElement root, out ProjectedPage page)
    {
        page = null!;
        Guid? userId = null;
        Guid? businessUnitId = null;
        Guid? organizationId = null;
        var properties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            if (!properties.Add(property.Name))
            {
                return false;
            }

            switch (property.Name)
            {
                case "@odata.context":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    break;
                case "UserId":
                    if (!TryReadNullableGuid(property.Value, out userId))
                    {
                        return false;
                    }

                    break;
                case "BusinessUnitId":
                    if (!TryReadNullableGuid(property.Value, out businessUnitId))
                    {
                        return false;
                    }

                    break;
                case "OrganizationId":
                    if (!TryReadNullableGuid(property.Value, out organizationId))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        page = new ProjectedPage(
            new WhoAmIResponseData
            {
                UserId = userId,
                BusinessUnitId = businessUnitId,
                OrganizationId = organizationId
            },
            null,
            null,
            null);
        return true;
    }

    /// <summary>
    /// 投影 fee collection 的 top-level OData envelope 與每一列。nextLink 只暫存為本 scope 的 server-side
    /// continuation，不會放入 <see cref="Package01FeeRecord"/> 或 success response；row list 僅在 page/
    /// cumulative/row policy 驗證完成前留於目前方法，呼叫端失敗時不會回傳 partial result。
    /// </summary>
    private static bool TryProjectFeeRecords(JsonElement root, out ProjectedPage page)
    {
        page = null!;
        if (!TryReadCollectionEnvelope(root, out var values, out var continuation))
        {
            return false;
        }

        var records = new List<Package01FeeRecord>();
        foreach (var value in values.EnumerateArray())
        {
            if (!TryMapFeeRecord(value, out var record))
            {
                return false;
            }

            records.Add(record);
        }

        page = new ProjectedPage(null, records, null, continuation);
        return true;
    }

    /// <summary>
    /// 投影 stor-lesson collection 的 top-level OData envelope 與每一列。lookup compatibility aliases 與
    /// formatted labels 只轉成共享 wire record 的已登錄欄位，所有 CRM field name、etag、paging cookie 和
    /// nextLink 都在此層丟棄或內部消費，不會跨入 ProductClient DTO。
    /// </summary>
    private static bool TryProjectStorLessonRecords(JsonElement root, out ProjectedPage page)
    {
        page = null!;
        if (!TryReadCollectionEnvelope(root, out var values, out var continuation))
        {
            return false;
        }

        var records = new List<Package01StorLessonRecord>();
        foreach (var value in values.EnumerateArray())
        {
            if (!TryMapStorLessonRecord(value, out var record))
            {
                return false;
            }

            records.Add(record);
        }

        page = new ProjectedPage(null, null, records, continuation);
        return true;
    }

    /// <summary>
    /// 讀取受限 collection envelope。只允許 value array、可丟棄的 context/paging-cookie，以及由下一頁
    /// allowlist 驗證器內部消費的 nextLink；duplicate property、未知 metadata、錯誤型別與沒有 value 的
    /// success body 都 fail-closed。此 helper 不解析或保存 URL，讓 continuation 的唯一安全 owner 保持在
    /// paging loop 中。
    /// </summary>
    private static bool TryReadCollectionEnvelope(
        JsonElement root,
        out JsonElement values,
        out string? continuation)
    {
        values = default;
        continuation = null;
        var hasValues = false;
        var properties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            if (!properties.Add(property.Name))
            {
                return false;
            }

            switch (property.Name)
            {
                case "value":
                    if (hasValues || property.Value.ValueKind != JsonValueKind.Array)
                    {
                        return false;
                    }

                    values = property.Value;
                    hasValues = true;
                    break;
                case "@odata.context":
                case "@Microsoft.Dynamics.CRM.fetchxmlpagingcookie":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    break;
                case "@odata.nextLink":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    continuation = property.Value.GetString();
                    break;
                default:
                    return false;
            }
        }

        return hasValues;
    }

    /// <summary>
    /// 將一列 CRM fee JSON 映射為 Package 1 fee wire record。此 allowlist 精確反映 server-owned templates：
    /// new_fee_shoud_pay 只為相容性接受並丟棄，而 Amount 永遠取 new_fee_really_paid；formatted label 僅允許
    /// new_pay_way/new_category。unknown field、duplicate field、錯誤型別或非 allowlisted annotation 皆拒絕，
    /// 使上游 schema 漂移不會成為跨產品資料外洩。
    /// </summary>
    private static bool TryMapFeeRecord(JsonElement value, out Package01FeeRecord record)
    {
        record = null!;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        Guid? feeId = null;
        DateTimeOffset? createdOn = null;
        DateTimeOffset? payDate = null;
        decimal? amount = null;
        int? payWayOption = null;
        string? payWayLabel = null;
        string? categoryLabel = null;
        string? others = null;
        string? paidPeriod = null;
        string? name = null;
        var properties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in value.EnumerateObject())
        {
            if (!properties.Add(property.Name))
            {
                return false;
            }

            if (property.Name.EndsWith(FormattedValueAnnotationSuffix, StringComparison.Ordinal))
            {
                var sourceField = property.Name[..^FormattedValueAnnotationSuffix.Length];
                if (!TryReadNullableString(property.Value, out var formattedValue))
                {
                    return false;
                }

                switch (sourceField)
                {
                    case "new_pay_way":
                        payWayLabel = formattedValue;
                        break;
                    case "new_category":
                        categoryLabel = formattedValue;
                        break;
                    default:
                        return false;
                }

                continue;
            }

            switch (property.Name)
            {
                case "@odata.etag":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    break;
                case "new_feeid":
                    if (!TryReadNullableGuid(property.Value, out feeId))
                    {
                        return false;
                    }

                    break;
                case "new_name":
                    if (!TryReadNullableString(property.Value, out name))
                    {
                        return false;
                    }

                    break;
                case "createdon":
                    if (!TryReadNullableDateTimeOffset(property.Value, out createdOn))
                    {
                        return false;
                    }

                    break;
                case "new_pay_date":
                    if (!TryReadNullableDateTimeOffset(property.Value, out payDate))
                    {
                        return false;
                    }

                    break;
                case "new_fee_really_paid":
                case "new_fee_shoud_pay":
                    if (!TryReadNullableDecimal(property.Value, out var numericValue))
                    {
                        return false;
                    }

                    if (property.Name == "new_fee_really_paid")
                    {
                        amount = numericValue;
                    }

                    break;
                case "new_pay_way":
                case "new_category":
                    if (!TryReadNullableInt32(property.Value, out var optionValue))
                    {
                        return false;
                    }

                    if (property.Name == "new_pay_way")
                    {
                        payWayOption = optionValue;
                    }

                    break;
                case "new_others":
                    if (!TryReadNullableString(property.Value, out others))
                    {
                        return false;
                    }

                    break;
                case "new_paid_period":
                    if (!TryReadNullableString(property.Value, out paidPeriod))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        record = new Package01FeeRecord
        {
            FeeId = feeId,
            CreatedOn = createdOn,
            PayDate = payDate,
            Amount = amount ?? 0m,
            PayWayOption = payWayOption,
            PayWayLabel = payWayLabel,
            CategoryLabel = categoryLabel,
            Others = others,
            PaidPeriod = paidPeriod,
            Name = name
        };
        return true;
    }

    /// <summary>
    /// 將一列 CRM stor-lesson JSON 映射為共享 wire record。兩組 lookup raw/compatibility aliases 必須產生同一
    /// GUID，否則視為上游歧義而拒絕；lookup formatted value 只作 ContactName/DiscipleLessonName fallback。
    /// emailaddress1 與 lesson.new_name 為已知但不跨越產品邊界的欄位，僅驗證型別後丟棄。此方法不保留
    /// JsonElement、CRM logical field name、etag、response 或任何 profile/session 狀態。
    /// </summary>
    private static bool TryMapStorLessonRecord(JsonElement value, out Package01StorLessonRecord record)
    {
        record = null!;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        Guid? storLessonId = null;
        Guid? contactId = null;
        Guid? discipleLessonId = null;
        var contactIdSet = false;
        var discipleLessonIdSet = false;
        DateTimeOffset? createdOn = null;
        DateTimeOffset? payDate = null;
        bool? currentComplete = null;
        decimal? feeAmount = null;
        string? contactName = null;
        string? contactNameFormatted = null;
        string? contactMobile = null;
        string? discipleLessonNameFormatted = null;
        var properties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in value.EnumerateObject())
        {
            if (!properties.Add(property.Name))
            {
                return false;
            }

            if (property.Name.EndsWith(FormattedValueAnnotationSuffix, StringComparison.Ordinal))
            {
                var sourceField = property.Name[..^FormattedValueAnnotationSuffix.Length];
                if (!TryReadNullableString(property.Value, out var formattedValue))
                {
                    return false;
                }

                switch (sourceField)
                {
                    case "new_contact_new_stor_lessons":
                    case "_new_contact_new_stor_lessons_value":
                        contactNameFormatted = formattedValue;
                        break;
                    case "new_new_disciple_lessons_new_stor_les":
                    case "_new_new_disciple_lessons_new_stor_les_value":
                        discipleLessonNameFormatted = formattedValue;
                        break;
                    default:
                        return false;
                }

                continue;
            }

            switch (property.Name)
            {
                case "@odata.etag":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    break;
                case "new_stor_lessonsid":
                    if (!TryReadNullableGuid(property.Value, out storLessonId))
                    {
                        return false;
                    }

                    break;
                case "new_contact_new_stor_lessons":
                case "_new_contact_new_stor_lessons_value":
                    if (!TryReadNullableGuid(property.Value, out var contactCandidate) ||
                        !TryAssignGuidAlias(ref contactId, ref contactIdSet, contactCandidate))
                    {
                        return false;
                    }

                    break;
                case "new_new_disciple_lessons_new_stor_les":
                case "_new_new_disciple_lessons_new_stor_les_value":
                    if (!TryReadNullableGuid(property.Value, out var discipleLessonCandidate) ||
                        !TryAssignGuidAlias(ref discipleLessonId, ref discipleLessonIdSet, discipleLessonCandidate))
                    {
                        return false;
                    }

                    break;
                case "createdon":
                    if (!TryReadNullableDateTimeOffset(property.Value, out createdOn))
                    {
                        return false;
                    }

                    break;
                case "new_pay_date":
                    if (!TryReadNullableDateTimeOffset(property.Value, out payDate))
                    {
                        return false;
                    }

                    break;
                case "new_current_complete":
                    if (!TryReadNullableBoolean(property.Value, out currentComplete))
                    {
                        return false;
                    }

                    break;
                case "new_fee":
                    if (!TryReadNullableDecimal(property.Value, out feeAmount))
                    {
                        return false;
                    }

                    break;
                case "contact.fullname":
                    if (!TryReadNullableString(property.Value, out contactName))
                    {
                        return false;
                    }

                    break;
                case "contact.mobilephone":
                    if (!TryReadNullableString(property.Value, out contactMobile))
                    {
                        return false;
                    }

                    break;
                case "contact.emailaddress1":
                case "lesson.new_name":
                    if (!TryReadNullableString(property.Value, out _))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        record = new Package01StorLessonRecord
        {
            StorLessonId = storLessonId,
            ContactId = contactId,
            DiscipleLessonId = discipleLessonId,
            CreatedOn = createdOn,
            PayDate = payDate,
            CurrentComplete = currentComplete,
            ContactName = contactName ?? contactNameFormatted,
            ContactMobile = contactMobile,
            DiscipleLessonName = discipleLessonNameFormatted,
            FeeAmount = feeAmount
        };
        return true;
    }

    /// <summary>
    /// 對同一 logical lookup 的兩個 CRM property alias 維持一致性。alias 不會成為 cache key 或長生命週期
    /// mutable state；僅在目前 JSON row scope 比較，避免 upstream 同時回傳衝突值時任意選擇一個而造成
    /// cross-layer data ambiguity。
    /// </summary>
    private static bool TryAssignGuidAlias(ref Guid? target, ref bool assigned, Guid? candidate)
    {
        if (assigned && target != candidate)
        {
            return false;
        }

        target = candidate;
        assigned = true;
        return true;
    }

    /// <summary>
    /// 讀取 nullable GUID，只接受 JSON null 或可完整解析的 string GUID。這是封閉 DTO 的型別邊界，避免 number、
    /// object、array 或任意 JSON extension 以 implicit conversion 進入產品資料；不會保留來源 JsonElement。
    /// </summary>
    private static bool TryReadNullableGuid(JsonElement value, out Guid? result)
    {
        result = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }

    /// <summary>
    /// 讀取 nullable round-trip timestamp，只接受 JSON null 或 invariant ISO/OData 可解析的 string。時間文字不會
    /// 被寫入 log、cache 或 session；轉成 DateTimeOffset 後才能進入 bounded DTO，避免 caller 日後重新解讀
    /// upstream JSON 格式。
    /// </summary>
    private static bool TryReadNullableDateTimeOffset(JsonElement value, out DateTimeOffset? result)
    {
        result = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }

    /// <summary>
    /// 讀取 nullable decimal，僅接受 JSON number 或 null。money 欄位不接受字串/物件 coercion，避免不同 CRM
    /// serializer 或 locale 造成隱性數值語意改變；解析結果僅存於本頁投影與最終 typed record。
    /// </summary>
    private static bool TryReadNullableDecimal(JsonElement value, out decimal? result)
    {
        result = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }

    /// <summary>
    /// 讀取 nullable option-set integer，僅接受 JSON number 或 null，避免 formatted label 或任意 string 被誤當
    /// 成 option code。顯示文字由 allowlisted formatted-value annotation 的獨立 mapping 處理。
    /// </summary>
    private static bool TryReadNullableInt32(JsonElement value, out int? result)
    {
        result = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }

    /// <summary>
    /// 讀取 nullable boolean，僅接受 JSON true/false/null。這避免 upstream 字串或數字被寬鬆 coercion 後改變
    /// product 行為，也避免將原始 JSON 節點保存到 record、cache、queue 或 session。
    /// </summary>
    private static bool TryReadNullableBoolean(JsonElement value, out bool? result)
    {
        result = value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

        return value.ValueKind is JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False;
    }

    /// <summary>
    /// 讀取 nullable string，僅接受 JSON string/null；所有長度仍受本頁 byte budget 限制。此 helper 不將來源
    /// JsonElement 或其 document lifecycle 暴露給 caller，回傳值僅用於已登錄的 typed DTO 欄位。
    /// </summary>
    private static bool TryReadNullableString(JsonElement value, out string? result)
    {
        result = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = value.GetString();
        return true;
    }

    /// <summary>
    /// 從 HttpContent 讀取單頁 bounded JSON。Content-Length 超限時不開啟 body stream；chunked body 以最多
    /// limit+1 bytes 偵測超限。stream 使用 await using，ArrayPool buffer 在成功、malformed UTF-8/JSON、
    /// cancellation 或 over-limit 時都完整 zero 後歸還，避免前一 organization 回應片段被下一個 request
    /// 租用。回傳的 JsonElement 僅供本檔 projector 立即使用，絕不跨出 WebApi boundary。
    /// </summary>
    private static async Task<BoundedJsonReadResult> ReadBoundedJsonAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
        {
            return new BoundedJsonReadResult(null, 0, TooLarge: true);
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(checked(maximumBytes + 1));
        var totalRead = 0;
        try
        {
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
                        return new BoundedJsonReadResult(null, totalRead, TooLarge: false);
                    }

                    using var document = JsonDocument.Parse(buffer.AsMemory(0, totalRead));
                    return new BoundedJsonReadResult(document.RootElement.Clone(), totalRead, TooLarge: false);
                }

                totalRead += read;
            }

            return new BoundedJsonReadResult(null, totalRead, TooLarge: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 判斷空白 response payload。只有 ASCII JSON whitespace 可被視為空；其他位元組會交由 JSON parser
    /// fail-closed，避免以寬鬆文字解碼隱藏 malformed UTF-8 或非 JSON 上游回應。
    /// </summary>
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

    /// <summary>
    /// 由封閉 branch 組成成功 envelope。只有 immutable operation ID、CE version 與 DTO records 能離開本方法；
    /// 如果 projector 未產生定義要求的 branch，回傳 sanitized upstream failure 而非空泛 object/json payload，
    /// 從而維持 Gateway/ProductClient 的 discriminated-union 契約。
    /// </summary>
    private static OperationExecutionResult CreateSuccessfulResult(
        OperationDefinition definition,
        string ceVersion,
        WhoAmIResponseData? whoAmI,
        List<Package01FeeRecord>? feeRecords,
        List<Package01StorLessonRecord>? storLessonRecords)
    {
        return definition.ResponseKind switch
        {
            OperationResponseKind.WhoAmI when whoAmI is not null =>
                OperationExecutionResult.Success(
                    OperationResponseData.ForWhoAmI(definition.CapabilityOperationId, ceVersion, whoAmI)),
            OperationResponseKind.Package01FeeRecords when feeRecords is not null =>
                OperationExecutionResult.Success(
                    OperationResponseData.ForPackage01FeeRecords(
                        definition.CapabilityOperationId,
                        ceVersion,
                        feeRecords)),
            OperationResponseKind.Package01StorLessonRecords when storLessonRecords is not null =>
                OperationExecutionResult.Success(
                    OperationResponseData.ForPackage01StorLessonRecords(
                        definition.CapabilityOperationId,
                        ceVersion,
                        storLessonRecords)),
            _ => OperationExecutionResult.Failure(
                DynamicsErrorCodes.UpstreamFailure,
                $"Operation '{definition.CapabilityOperationId}' did not produce its approved response branch.")
        };
    }

    /// <summary>
    /// 將已驗證 absolute continuation URI 轉成 deterministic cycle-detection key。URI 仍只存在於本 request
    /// scope 的 HashSet，方法結束即釋放集合；不會寫入 log、DTO、cache 或 session，因此 CRM query/token 不會
    /// 透過診斷或跨要求狀態洩漏。
    /// </summary>
    private static string CanonicalizeUri(Uri uri)
        => uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);

    /// <summary>
    /// 將 OData path template 的 logical-name placeholders 以受限字元集繫結。此 helper 只處理已登錄的
    /// identifier context，不可作為一般 URL builder；未繫結 placeholder、空字串或危險字元都在 outbound
    /// request 前拒絕，且沒有 URI/token/session 的 retained state。
    /// </summary>
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

    /// <summary>
    /// 以各參數的 XML/FetchXML encoder 繫結 server-owned template。可選 display-name attribute 僅在安全編碼
    /// 且非空白時出現；required placeholders 未繫結即失敗。此純字串結果只用來建立本次 URI，不能被 cache、
    /// session 或 DTO 保存，因此不會形成長生命週期的 CRM query/identity retention。
    /// </summary>
    private static bool TryBindFetchXml(
        string template,
        IReadOnlyDictionary<string, object?> parameters,
        out string fetchXml,
        out string error)
    {
        fetchXml = string.Empty;
        error = string.Empty;

        var bound = CollapseWhitespace(template);
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

    /// <summary>
    /// 將 optional FetchXML display-name attribute 以相同的 XML encoder 安全加入或完整移除。它不能改變 XML
    /// 結構、entity、operator 或 attribute 名稱；所產生字串只屬於目前 bind scope，沒有 cache/session owner，
    /// 並會在 request lifecycle 結束後隨 URI/request disposal 失去參考。
    /// </summary>
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

    /// <summary>
    /// 驗證 OData logical-name placeholder 的有限字元集與長度。這是 identifier context 的 deny-by-default
    /// boundary，不接受 slash、quote、query、Unicode control 或 URI fragments；因此 binder 不會成為任意
    /// route/CRM schema 的輸入通道，也不建立任何長生命週期資源。
    /// </summary>
    private static bool IsSafeLogicalName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(char.IsLetterOrDigit(character) || character is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 將 server-owned FetchXML template 的 formatting whitespace 正規化，降低固定模板 query 的配置雜訊。
    /// 此方法絕不接收 caller XML，且回傳字串僅在 bind/request scope 中短暫存在；它不保存到 static、cache、
    /// queue 或 session，因此不會造成跨 profile 或跨使用者的記憶體保留。
    /// </summary>
    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWhitespace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }

                continue;
            }

            previousWhitespace = false;
            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// 單次讀取結果只由 SendJsonGetAsync 的 request scope 擁有。Data 是 private projector 的短生命週期
    /// JsonElement，ByteCount 用於 cumulative budget，TooLarge 可讓 caller 在投影前 fail-closed；record
    /// 不保存 stream、response、buffer、token、credential 或 session。
    /// </summary>
    private sealed record BoundedJsonReadResult(JsonElement? Data, int ByteCount, bool TooLarge);

    /// <summary>
    /// 已投影頁面只含封閉 DTO 分支與內部 continuation 原文；外層在驗證 continuation 前不會序列化它。
    /// record 與其 lists 僅存在於目前 request scope，失敗時不會形成 OperationExecutionResult.Data。
    /// </summary>
    private sealed record ProjectedPage(
        WhoAmIResponseData? WhoAmI,
        IReadOnlyList<Package01FeeRecord>? FeeRecords,
        IReadOnlyList<Package01StorLessonRecord>? StorLessonRecords,
        string? Continuation);

    /// <summary>
    /// registry/deployment 交集後的 immutable response limits。每次 Execute 都建立一份 value record，避免把
    /// profile option 或 operation definition 的 mutable 參考放入 retry/paging state，並使 request 結束後
    /// counters、lists 與 limit owner 一起可被回收。
    /// </summary>
    private readonly record struct ResponseLimits(
        int MaximumPageCount,
        int MaximumPageBytes,
        int MaximumCumulativeResponseBytes,
        int MaximumResultItemCount);
}
