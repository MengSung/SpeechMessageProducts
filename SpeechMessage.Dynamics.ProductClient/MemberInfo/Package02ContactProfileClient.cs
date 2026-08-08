// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/MemberInfo/Package02ContactProfileClient.cs
// 目的：將 P7.2 Slice B1/B2 typed DTO 映射到固定 Dynamics operations。
//
// 生命週期與安全：
// - 類別是 stateless singleton，只保存 DI-owned executor/logger；不保存 request、credential、token、session、cache、timer 或 stream。
// - 所有 caller 字串在第一次 await 前 trim、限制並複製；executor 獨自擁有 HTTP/admission/lease lifecycle。
// - write failure 或 ambiguous timeout 絕不自動重送；錯配 operation/CE/discriminator 一律 fail closed。
// ============================================================================

using System.Text;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.MemberInfo;

/// <summary>P7.2 contact profile write 與 aggregate function 的 stateless typed client。</summary>
public sealed class Package02ContactProfileClient : IPackage02ContactProfileClient
{
    private const int MaximumPictureUrlBytes = 1024;
    private const int MaximumProfileTextBytes = 512;
    private const int MaximumSearchBytes = 256;
    private const int MaximumIdempotencyKeyCharacters = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<Package02ContactProfileClient> _logger;

    /// <summary>
    /// 建立不擁有下游 transport 的 typed client。executor/logger 由 composition root 擁有；本類別沒有 dispose owner。
    /// </summary>
    public Package02ContactProfileClient(
        IDynamicsOperationExecutor executor,
        ILogger<Package02ContactProfileClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 將 ChurchReport 已完成授權的 LINE profile mutation 映射成固定 operation request，交給共用 executor
    /// 執行一次 bounded Data8 lease。方法只在本次 request scope 建立參數 dictionary；第一次 await 前已完成
    /// mode、URL、文字、contact ID 與冪等鍵的複製和限制，回傳前也只保留已 read-back-confirmed 的 enum。
    /// </summary>
    /// <param name="request">不含 LINE token、user ID、endpoint、credential 或 CRM SDK graph 的有限產品 DTO。</param>
    /// <param name="cancellationToken">由呼叫端持有且只向下游傳遞的 request cancellation；client 不會保存它。</param>
    /// <returns>只含 Changed 與 ReadBackConfirmed 的封閉結果；timeout 或錯配 response 會 fail closed。</returns>
    public async Task<ContactLineProfileUpdateResult> UpdateLineProfileAsync(
        ContactLineProfileUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profileAlias = RequireNonEmpty(request.ProfileAlias, nameof(request.ProfileAlias));
        var workloadSubjectId = RequireNonEmpty(request.WorkloadSubjectId, nameof(request.WorkloadSubjectId));
        if (request.ContactId == Guid.Empty)
        {
            throw new ArgumentException("ContactId is required.", nameof(request.ContactId));
        }

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contactId"] = request.ContactId
        };
        AddNullableTextMutation(
            parameters,
            "pictureMode",
            "pictureUrl",
            request.PictureMode,
            request.PictureUrl,
            MaximumPictureUrlBytes,
            validateHttpsUrl: true);
        AddNullableTextMutation(
            parameters,
            "statusMode",
            "statusMessage",
            request.StatusMode,
            request.StatusMessage,
            MaximumProfileTextBytes,
            validateHttpsUrl: false);
        AddDisplayNameMutation(parameters, request.DisplayNameMode, request.DisplayName);

        var execution = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = profileAlias,
            CapabilityOperationId = OperationIds.MemberInfoContactUpdateLineProfile,
            WorkloadSubjectId = workloadSubjectId,
            IdempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey),
            Parameters = parameters
        }, cancellationToken).ConfigureAwait(false);

        if (!execution.Succeeded)
        {
            _logger.LogWarning(
                "P7.2 contact LINE profile update failed with {ErrorCode}.",
                execution.ErrorCode ?? "unknown");
            throw new InvalidOperationException("Contact LINE profile update failed.");
        }

        var data = execution.Data;
        if (data is null ||
            !string.Equals(data.OperationId, OperationIds.MemberInfoContactUpdateLineProfile, StringComparison.Ordinal) ||
            !string.Equals(data.CeVersion, "9.1", StringComparison.Ordinal) ||
            data.ResponseKind != OperationResponseKind.ContactLineProfileUpdate ||
            data.ContactLineProfileUpdate is null)
        {
            throw new InvalidOperationException("Contact LINE profile response does not match the requested operation contract.");
        }

        return new ContactLineProfileUpdateResult
        {
            Disposition = data.ContactLineProfileUpdate.Disposition,
            CorrelationCategory = data.ContactLineProfileUpdate.CorrelationCategory
        };
    }

    /// <summary>
    /// 執行 server-owned ungrouped commitment aggregate function。caller 只能提供 optional bounded search；
    /// closed-status metadata、active app-named list scope、membership 排除與 FetchXML 都由 Data8 connector 固定
    /// 建立，避免任意 query 或 grouped contact graph 穿越 ProductClient 邊界。結果集合會在本方法 scope 內映射
    /// 成新的 bounded DTO，不保存 Entity、metadata、session、cache 或下游 lease。
    /// </summary>
    /// <param name="request">只含 deployment/workload scalar 與 optional bounded search 的產品輸入。</param>
    /// <param name="cancellationToken">由呼叫端持有且不被此 stateless client 保存的取消訊號。</param>
    /// <returns>固定 operation 投影的 raw OptionSet value/count 集合；錯誤或錯配 response 會 fail closed。</returns>
    public async Task<UngroupedCommitmentCountResult> CountUngroupedCommitmentAsync(
        UngroupedCommitmentCountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var search = NormalizeOptionalText(request.Search, MaximumSearchBytes, nameof(request.Search));
        if (search is not null)
        {
            parameters["search"] = search;
        }

        var execution = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = RequireNonEmpty(request.ProfileAlias, nameof(request.ProfileAlias)),
            CapabilityOperationId = OperationIds.MemberInfoContactCountUngroupedCommitment,
            WorkloadSubjectId = RequireNonEmpty(request.WorkloadSubjectId, nameof(request.WorkloadSubjectId)),
            IdempotencyKey = null,
            Parameters = parameters
        }, cancellationToken).ConfigureAwait(false);

        if (!execution.Succeeded)
        {
            _logger.LogWarning(
                "P7.2 ungrouped commitment count failed with {ErrorCode}.",
                execution.ErrorCode ?? "unknown");
            throw new InvalidOperationException("Ungrouped commitment count failed.");
        }

        var data = execution.Data;
        if (data is null ||
            !string.Equals(data.OperationId, OperationIds.MemberInfoContactCountUngroupedCommitment, StringComparison.Ordinal) ||
            !string.Equals(data.CeVersion, "9.1", StringComparison.Ordinal) ||
            data.ResponseKind != OperationResponseKind.UngroupedCommitmentCounts ||
            data.UngroupedCommitmentCounts is null)
        {
            throw new InvalidOperationException("Ungrouped commitment response does not match the requested operation contract.");
        }

        return new UngroupedCommitmentCountResult
        {
            Counts = data.UngroupedCommitmentCounts
                .Select(static row => new UngroupedCommitmentCountDto { Value = row.Value, Count = row.Count })
                .ToArray()
        };
    }

    /// <summary>複製並 trim 非空 routing scalar；錯誤訊息不回顯原始值。</summary>
    private static string RequireNonEmpty(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A required routing value is missing.", parameterName);
        }

        return value.Trim();
    }

    /// <summary>加入 picture/status 的固定 set/clear scalar，mode/value 不一致時在 executor 前失敗。</summary>
    private static void AddNullableTextMutation(
        IDictionary<string, object?> parameters,
        string modeName,
        string valueName,
        ContactLineProfileNullableTextMode mode,
        string? value,
        int maximumBytes,
        bool validateHttpsUrl)
    {
        switch (mode)
        {
            case ContactLineProfileNullableTextMode.Set:
                var normalized = NormalizeRequiredText(value, maximumBytes, valueName);
                if (validateHttpsUrl)
                {
                    ValidateHttpsPictureUrl(normalized, valueName);
                }
                parameters[modeName] = "set";
                parameters[valueName] = normalized;
                return;
            case ContactLineProfileNullableTextMode.Clear when value is null:
                parameters[modeName] = "clear";
                return;
            case ContactLineProfileNullableTextMode.Clear:
                throw new ArgumentException("Clear mode cannot include a value.", valueName);
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported LINE profile mutation mode.");
        }
    }

    /// <summary>加入 display name 的固定 set/preserve scalar；legacy preserve 禁止夾帶空白或其他值。</summary>
    private static void AddDisplayNameMutation(
        IDictionary<string, object?> parameters,
        ContactLineProfileDisplayNameMode mode,
        string? value)
    {
        switch (mode)
        {
            case ContactLineProfileDisplayNameMode.Set:
                parameters["displayNameMode"] = "set";
                parameters["displayName"] = NormalizeRequiredText(value, MaximumProfileTextBytes, nameof(value));
                return;
            case ContactLineProfileDisplayNameMode.Preserve when value is null:
                parameters["displayNameMode"] = "preserve";
                return;
            case ContactLineProfileDisplayNameMode.Preserve:
                throw new ArgumentException("Preserve mode cannot include a value.", nameof(value));
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported display-name mutation mode.");
        }
    }

    /// <summary>正規化 required bounded UTF-8 純文字；不使用 ToString 或寬鬆 Unicode fallback。</summary>
    private static string NormalizeRequiredText(string? value, int maximumBytes, string parameterName)
        => NormalizeOptionalText(value, maximumBytes, parameterName)
           ?? throw new ArgumentException("A required profile value is missing.", parameterName);

    /// <summary>正規化 optional bounded UTF-8 純文字；空白表示沒有值。</summary>
    private static string? NormalizeOptionalText(string? value, int maximumBytes, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        try
        {
            if (StrictUtf8.GetByteCount(normalized) > maximumBytes)
            {
                throw new ArgumentException("A profile value exceeds its bounded size.", parameterName);
            }
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("A profile value contains invalid text.", parameterName);
        }

        return normalized;
    }

    /// <summary>驗證圖片 URL 為 absolute HTTPS、無 user-info/fragment 且使用預設 port，避免 credential 或任意通道值。</summary>
    private static void ValidateHttpsPictureUrl(string value, string parameterName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.IsDefaultPort)
        {
            throw new ArgumentException("PictureUrl must be an absolute HTTPS URL without user-info, fragment or custom port.", parameterName);
        }
    }

    /// <summary>驗證 1-128 個 RFC 3986 unreserved ASCII 字元的冪等鍵，不將原始值寫入錯誤或 log。</summary>
    private static string NormalizeIdempotencyKey(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length > MaximumIdempotencyKeyCharacters ||
            value.Any(static character =>
                !((character >= 'a' && character <= 'z') ||
                  (character >= 'A' && character <= 'Z') ||
                  (character >= '0' && character <= '9') ||
                  character is '-' or '.' or '_' or '~')))
        {
            throw new ArgumentException("IdempotencyKey must be a 1-128 character URL-safe value.", nameof(value));
        }

        return value;
    }
}
