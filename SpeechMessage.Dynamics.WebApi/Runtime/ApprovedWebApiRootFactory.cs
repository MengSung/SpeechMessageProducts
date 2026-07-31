// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/ApprovedWebApiRootFactory.cs
// 目的：從設定推導並驗證 ApprovedWebApiRoot。
//
// 保母教學：
// 1. 只接受 https。
// 2. 禁止 user-info、query、fragment。
// 3. 保留組織虛擬目錄 base path，不可亂砍路徑。
// 4. 最終 root 必須是 .../api/data/v8.2/ 或 .../api/data/v9.1/。
// 5. 之後所有 WhoAmI / FetchXML / nextLink 都只能落在這個 root 底下。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 已驗證的 Web API root。
/// </summary>
public sealed class ApprovedWebApiRoot
{
    public required Uri Value { get; init; }
    public required string CeVersion { get; init; }
    public required string ApiVersionSegment { get; init; }
}

/// <summary>
/// 從 DynamicsWebApiOptions 建立 ApprovedWebApiRoot。
/// </summary>
public static class ApprovedWebApiRootFactory
{
    public static bool TryCreate(DynamicsWebApiOptions options, out ApprovedWebApiRoot? root, out OperationExecutionResult? error)
    {
        root = null;
        error = null;

        if (options is null)
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "Dynamics Web API options are missing.");
            return false;
        }

        if (!TryNormalizeCeVersion(options.CeVersion, out var ceVersion, out var apiSegment, out var ceError))
        {
            error = OperationExecutionResult.Failure(DynamicsErrorCodes.InvalidConfiguration, ceError);
            return false;
        }

        // 優先使用完整 Web API root；否則用 OrganizationBaseUri 推導。
        if (!string.IsNullOrWhiteSpace(options.OrganizationWebApiBaseUri))
        {
            if (!TryValidateAbsoluteHttpsUri(options.OrganizationWebApiBaseUri, out var webApiUri, out var webApiError))
            {
                error = OperationExecutionResult.Failure(DynamicsErrorCodes.InvalidConfiguration, webApiError);
                return false;
            }

            var path = webApiUri.AbsolutePath.TrimEnd('/') + "/";
            var expectedSuffix = $"/api/data/{apiSegment}/";
            if (!path.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                error = OperationExecutionResult.Failure(
                    DynamicsErrorCodes.InvalidConfiguration,
                    $"OrganizationWebApiBaseUri must end with api/data/{apiSegment}/ for CeVersion {ceVersion}.");
                return false;
            }

            root = new ApprovedWebApiRoot
            {
                Value = new Uri(webApiUri.GetLeftPart(UriPartial.Authority) + path, UriKind.Absolute),
                CeVersion = ceVersion,
                ApiVersionSegment = apiSegment
            };
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.OrganizationBaseUri))
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "Either OrganizationBaseUri or OrganizationWebApiBaseUri is required.");
            return false;
        }

        if (!TryValidateAbsoluteHttpsUri(options.OrganizationBaseUri, out var baseUri, out var baseError))
        {
            error = OperationExecutionResult.Failure(DynamicsErrorCodes.InvalidConfiguration, baseError);
            return false;
        }

        var basePath = baseUri.AbsolutePath;
        if (!basePath.EndsWith("/", StringComparison.Ordinal))
        {
            basePath += "/";
        }

        var approved = new Uri(
            baseUri.GetLeftPart(UriPartial.Authority) + basePath + $"api/data/{apiSegment}/",
            UriKind.Absolute);

        root = new ApprovedWebApiRoot
        {
            Value = approved,
            CeVersion = ceVersion,
            ApiVersionSegment = apiSegment
        };
        return true;
    }

    /// <summary>
    /// 驗證既有的絕對 URI 是否仍位於 profile 擁有的精確 Web API root。此方法只接受 HTTPS、相同
    /// scheme/host/port 與 root path 前綴，並拒絕 user-info 與 fragment；query 僅在已核准 root
    /// 之下才能存在，供 CRM 的 server-driven paging token 使用。呼叫端仍是唯一持有 request、token
    /// 與 response lifecycle 的 owner，本方法不快取 candidate 或任何呼叫者狀態，因此不會跨 profile
    /// 或 session 保留 URI、憑證或 continuation 資料。
    /// </summary>
    public static bool IsUnderApprovedRoot(Uri candidate, Uri approvedRoot)
    {
        if (!candidate.IsAbsoluteUri || !approvedRoot.IsAbsoluteUri)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(candidate.UserInfo) || !string.IsNullOrEmpty(candidate.Fragment))
        {
            return false;
        }

        if (!string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(candidate.Scheme, approvedRoot.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.Host, approvedRoot.Host, StringComparison.OrdinalIgnoreCase) ||
            candidate.Port != approvedRoot.Port)
        {
            return false;
        }

        var candidatePath = candidate.AbsolutePath.TrimEnd('/') + "/";
        var rootPath = approvedRoot.AbsolutePath.TrimEnd('/') + "/";
        return candidatePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 將上游 OData continuation 解析成下一頁的絕對 URI，並在建立下一個可能帶有 Windows/Kerberos
    /// 或 Bearer 驗證的 request 前完成 SSRF 與 path traversal 檢查。相對連結只能以目前已核准頁面
    /// 為基底解析；絕對連結仍必須回到同一個 HTTPS origin 與精確 API-version root。結果只交給目前
    /// request scope 的 paging loop 使用，呼叫端在完成、取消、逾時或拒絕後釋放其 visited set 與
    /// response/stream/buffer，故此 helper 不保存任何 token、profile、session 或跨請求 mutable state。
    /// </summary>
    public static bool TryResolveContinuation(
        string rawContinuation,
        Uri currentPage,
        Uri approvedRoot,
        out Uri? continuation)
    {
        continuation = null;

        if (string.IsNullOrWhiteSpace(rawContinuation) ||
            rawContinuation.Length > 8_192 ||
            !string.Equals(rawContinuation, rawContinuation.Trim(), StringComparison.Ordinal) ||
            !IsUnderApprovedRoot(currentPage, approvedRoot) ||
            ContainsUnsafeContinuationPathSyntax(rawContinuation))
        {
            return false;
        }

        if (!Uri.TryCreate(currentPage, rawContinuation, out var candidate) || candidate is null)
        {
            return false;
        }

        if (!IsUnderApprovedRoot(candidate, approvedRoot))
        {
            return false;
        }

        continuation = candidate;
        return true;
    }

    /// <summary>
    /// 在 URI parser 正規化路徑前拒絕可改寫 segment 邊界的原始 continuation 語法。只檢查 query/
    /// fragment 以前的 path 區段，避免把 CRM skiptoken 的不透明查詢資料當成路徑；任何反斜線、
    /// 編碼 slash/backslash 或 dot traversal 都不會取得下一頁的 credential-bearing request。
    /// </summary>
    private static bool ContainsUnsafeContinuationPathSyntax(string rawContinuation)
    {
        var pathEnd = rawContinuation.IndexOfAny(['?', '#']);
        var path = pathEnd < 0
            ? rawContinuation.AsSpan()
            : rawContinuation.AsSpan(0, pathEnd);

        if (path.IndexOf('\\') >= 0 ||
            path.IndexOf("%2f".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("%5c".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        foreach (var segment in path.ToString().Split('/', StringSplitOptions.None))
        {
            if (segment is "." or "..")
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryNormalizeCeVersion(string? ceVersion, out string normalized, out string apiSegment, out string error)
    {
        normalized = string.Empty;
        apiSegment = string.Empty;
        error = string.Empty;

        var value = (ceVersion ?? string.Empty).Trim().TrimStart('v', 'V');
        if (value is "8.2")
        {
            normalized = "8.2";
            apiSegment = "v8.2";
            return true;
        }

        if (value is "9.1")
        {
            normalized = "9.1";
            apiSegment = "v9.1";
            return true;
        }

        error = "CeVersion must be 8.2 or 9.1.";
        return false;
    }

    private static bool TryValidateAbsoluteHttpsUri(string raw, out Uri uri, out string error)
    {
        uri = null!;
        error = string.Empty;

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var parsed) || parsed is null)
        {
            error = "URI must be an absolute URI.";
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "URI must use https.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = "URI must not contain user-info credentials.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.Query))
        {
            error = "URI must not contain a query string.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.Fragment))
        {
            error = "URI must not contain a fragment.";
            return false;
        }

        uri = parsed;
        return true;
    }
}
