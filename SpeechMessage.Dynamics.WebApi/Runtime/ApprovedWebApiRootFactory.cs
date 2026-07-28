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

    public static bool IsUnderApprovedRoot(Uri candidate, Uri approvedRoot)
    {
        if (!candidate.IsAbsoluteUri || !approvedRoot.IsAbsoluteUri)
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
