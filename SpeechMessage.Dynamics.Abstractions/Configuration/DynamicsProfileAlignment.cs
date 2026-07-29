// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Configuration/DynamicsProfileAlignment.cs
// 目的：把既有產品的 CrmConnection 組織資訊，對齊成新 DynamicsAccess 可用的設定。
//
// 保母教學（為什麼需要這個？）：
// 1. 舊系統 appsettings 的 CrmConnection 通常長這樣：
//    - Organization = "jesus"
//    - ServerUrl = "https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc"
// 2. 新架構不再走 Organization.svc / SOAP / SDK。
//    它要的是：
//    - ProfileAlias（邏輯環境別名，例如 jesus-prod）
//    - OrganizationBaseUri（組織根網址）
//    - OrganizationWebApiBaseUri（.../api/data/v9.1/ 或 v8.2/）
// 3. 這裡「只推導公開連線位址與別名」，絕對不複製 Username/Password。
//    密碼必須改走秘密庫參考（SecretReference），不可搬進 DynamicsAccess。
// 4. 五大產品以後都可重用同一套推導規則，避免每個產品各寫各的字串拼接。
// ============================================================================

using System.Globalization;

namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 從舊 CrmConnection 風格欄位，推導新 DynamicsAccess / Gateway 可用設定。
/// </summary>
public static class DynamicsProfileAlignment
{
    /// <summary>
    /// 由組織名稱推導 profile alias。
    /// 例：jesus + prod => jesus-prod；jesusback + dev => jesusback-dev。
    /// </summary>
    /// <param name="organization">CrmConnection:Organization，例如 jesus / jesusback。</param>
    /// <param name="environmentSuffix">環境後綴，預設 prod。可用 dev / test / staging。</param>
    public static string DeriveProfileAlias(string? organization, string environmentSuffix = "prod")
    {
        var org = NormalizeToken(organization);
        if (string.IsNullOrWhiteSpace(org))
        {
            throw new ArgumentException("organization is required.", nameof(organization));
        }

        var suffix = NormalizeToken(environmentSuffix);
        if (string.IsNullOrWhiteSpace(suffix))
        {
            suffix = "prod";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{org}-{suffix}");
    }

    /// <summary>
    /// 從 Organization.svc URL 或組織根 URL，推導 OrganizationBaseUri。
    /// 例：
    /// https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc
    /// => https://jesus.speechmessage.com.tw/
    /// </summary>
    public static bool TryDeriveOrganizationBaseUri(
        string? serverUrlOrBaseUri,
        out string organizationBaseUri,
        out string error)
    {
        organizationBaseUri = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(serverUrlOrBaseUri))
        {
            error = "ServerUrl / OrganizationBaseUri is required.";
            return false;
        }

        if (!Uri.TryCreate(serverUrlOrBaseUri.Trim(), UriKind.Absolute, out var uri) || uri is null)
        {
            error = "ServerUrl must be an absolute URI.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "ServerUrl must use https.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "ServerUrl must not contain user-info credentials.";
            return false;
        }

        // 去掉 query/fragment，避免把暫時參數帶進正式 root。
        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        var path = builder.Path ?? "/";
        // 舊 SOAP 端點：.../XRMServices/2011/Organization.svc
        // 推導時要退回組織虛擬目錄根（通常是 host root 或 /OrgName/）。
        var soapMarker = "/XRMServices/";
        var soapIndex = path.IndexOf(soapMarker, StringComparison.OrdinalIgnoreCase);
        if (soapIndex >= 0)
        {
            path = path.Substring(0, soapIndex);
        }

        // 若路徑剛好是 Organization.svc 這種檔名，也清掉。
        if (path.EndsWith("Organization.svc", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(0, path.Length - "Organization.svc".Length);
        }

        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            path = "/";
        }
        else if (!path.EndsWith("/", StringComparison.Ordinal))
        {
            path += "/";
        }

        builder.Path = path;
        organizationBaseUri = builder.Uri.GetLeftPart(UriPartial.Authority) + path;
        return true;
    }

    /// <summary>
    /// 推導 Web API root。
    /// 例：base=https://jesus.speechmessage.com.tw/ + ce=9.1
    /// => https://jesus.speechmessage.com.tw/api/data/v9.1/
    /// </summary>
    public static bool TryDeriveOrganizationWebApiBaseUri(
        string? serverUrlOrBaseUri,
        string? ceVersion,
        out string organizationWebApiBaseUri,
        out string error)
    {
        organizationWebApiBaseUri = string.Empty;
        error = string.Empty;

        if (!TryNormalizeCeVersion(ceVersion, out var normalized, out var apiSegment, out var ceError))
        {
            error = ceError;
            return false;
        }

        // 若呼叫端已經給完整 Web API root，直接驗證並回傳。
        if (!string.IsNullOrWhiteSpace(serverUrlOrBaseUri) &&
            serverUrlOrBaseUri.IndexOf("/api/data/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (!Uri.TryCreate(serverUrlOrBaseUri.Trim(), UriKind.Absolute, out var direct) || direct is null)
            {
                error = "OrganizationWebApiBaseUri must be an absolute URI.";
                return false;
            }

            if (!string.Equals(direct.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "OrganizationWebApiBaseUri must use https.";
                return false;
            }

            var path = direct.AbsolutePath.TrimEnd('/') + "/";
            var expected = $"/api/data/{apiSegment}/";
            if (!path.EndsWith(expected, StringComparison.OrdinalIgnoreCase))
            {
                error = $"OrganizationWebApiBaseUri must end with api/data/{apiSegment}/ for CeVersion {normalized}.";
                return false;
            }

            organizationWebApiBaseUri = direct.GetLeftPart(UriPartial.Authority) + path;
            return true;
        }

        if (!TryDeriveOrganizationBaseUri(serverUrlOrBaseUri, out var baseUri, out var baseError))
        {
            error = baseError;
            return false;
        }

        organizationWebApiBaseUri = baseUri.TrimEnd('/') + $"/api/data/{apiSegment}/";
        return true;
    }

    /// <summary>
    /// 一次把舊 CrmConnection 對齊成可寫入 DynamicsAccess 的結果。
    /// 注意：不包含密碼；SecretReference 只放「參考名稱」，不是秘密本體。
    /// </summary>
    public static bool TryAlignFromLegacyCrmConnection(
        string? organization,
        string? serverUrl,
        string? ceVersion,
        string environmentSuffix,
        string? secretReferenceName,
        out DynamicsAccessAlignmentResult result,
        out string error)
    {
        result = new DynamicsAccessAlignmentResult();
        error = string.Empty;

        try
        {
            result.ProfileAlias = DeriveProfileAlias(organization, environmentSuffix);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!TryDeriveOrganizationBaseUri(serverUrl, out var baseUri, out var baseError))
        {
            error = baseError;
            return false;
        }

        if (!TryDeriveOrganizationWebApiBaseUri(serverUrl, ceVersion, out var webApi, out var webApiError))
        {
            error = webApiError;
            return false;
        }

        if (!TryNormalizeCeVersion(ceVersion, out var normalized, out _, out var ceError))
        {
            error = ceError;
            return false;
        }

        result.Organization = NormalizeToken(organization);
        result.OrganizationBaseUri = baseUri;
        result.OrganizationWebApiBaseUri = webApi;
        result.CeVersion = normalized;
        result.SecretReference = string.IsNullOrWhiteSpace(secretReferenceName)
            ? $"dynamics-{result.ProfileAlias}-credential"
            : secretReferenceName.Trim();
        return true;
    }

    private static bool TryNormalizeCeVersion(
        string? ceVersion,
        out string normalized,
        out string apiSegment,
        out string error)
    {
        normalized = string.Empty;
        apiSegment = string.Empty;
        error = string.Empty;

        var value = (ceVersion ?? "9.1").Trim().TrimStart('v', 'V');
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

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // 只保留安全 token 字元，避免 profile alias 混入空白/路徑。
        var chars = value.Trim().ToLowerInvariant().Where(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.').ToArray();
        return new string(chars);
    }
}

/// <summary>
/// DynamicsAccess 對齊結果（可直接填入 Gateway/Embedded 設定）。
/// </summary>
public sealed class DynamicsAccessAlignmentResult
{
    /// <summary>正規化後的組織代碼，例如 jesus。</summary>
    public string Organization { get; set; } = string.Empty;

    /// <summary>邏輯 profile 別名，例如 jesus-prod。</summary>
    public string ProfileAlias { get; set; } = string.Empty;

    /// <summary>組織根 URI，例如 https://jesus.speechmessage.com.tw/</summary>
    public string OrganizationBaseUri { get; set; } = string.Empty;

    /// <summary>Web API root，例如 https://jesus.speechmessage.com.tw/api/data/v9.1/</summary>
    public string OrganizationWebApiBaseUri { get; set; } = string.Empty;

    /// <summary>CE 版本標籤：8.2 或 9.1。</summary>
    public string CeVersion { get; set; } = "9.1";

    /// <summary>秘密參考名稱（不是密碼本體）。</summary>
    public string SecretReference { get; set; } = string.Empty;
}
