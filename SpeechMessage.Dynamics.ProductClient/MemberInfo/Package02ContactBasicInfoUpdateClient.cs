// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/MemberInfo/Package02ContactBasicInfoUpdateClient.cs
// 目的：把 P7.2 contact basic-info operation 映射成產品安全 DTO。
//
// 生命週期與安全：
// - 本類別為 stateless singleton，可跨請求重用；只保存 executor/logger，不保存 request、response、
//   credential、token、session、cache、timer 或 stream。
// - 請求在呼叫 executor 前先複製並正規化有限 scalar；executor 是 HTTP／admission／Data8 lease 的唯一 owner。
// - 任何錯誤 operation、CE version、response discriminator 或 branch mismatch 都 fail closed，不重試寫入。
// ============================================================================

using System.Text;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.MemberInfo;

/// <summary>
/// P7.2 contact basic-info 的 stateless typed client 實作。
/// </summary>
public sealed class Package02ContactBasicInfoUpdateClient : IPackage02ContactBasicInfoUpdateClient
{
    private const int MaximumStringParameterBytes = 256;
    private const int MaximumIdempotencyKeyCharacters = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<Package02ContactBasicInfoUpdateClient> _logger;

    /// <summary>
    /// 建立不擁有下游 transport 的 typed client。executor 與 logger 由 DI composition root 擁有；本類別
    /// 不建立第二個 HttpClient、Data8 pool、timer、background task 或跨 request mutable state。
    /// </summary>
    public Package02ContactBasicInfoUpdateClient(
        IDynamicsOperationExecutor executor,
        ILogger<Package02ContactBasicInfoUpdateClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 驗證並 defensive-copy 產品 request，送出唯一的 P7.2 operation，再將封閉 response branch 投影成
    /// <see cref="ContactBasicInfoUpdateResult"/>。transport timeout 或 executor failure 不會盲目重送，
    /// 因為 contact update 沒有本契約可用的 server-side idempotency token；reconciliation 由 task-owned fixture bridge 負責。
    /// </summary>
    public async Task<ContactBasicInfoUpdateResult> UpdateAsync(
        ContactBasicInfoUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profileAlias = RequireNonEmpty(request.ProfileAlias, nameof(request.ProfileAlias));
        var workloadSubjectId = RequireNonEmpty(request.WorkloadSubjectId, nameof(request.WorkloadSubjectId));
        if (request.ContactId == Guid.Empty)
        {
            throw new ArgumentException("ContactId is required.", nameof(request.ContactId));
        }

        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contactId"] = request.ContactId
        };
        AddOptionalString(parameters, "phone", request.Phone, nameof(request.Phone));
        AddOptionalString(parameters, "address", request.Address, nameof(request.Address));

        var result = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = profileAlias,
            CapabilityOperationId = OperationIds.MemberInfoContactUpdateBasicInfo,
            WorkloadSubjectId = workloadSubjectId,
            IdempotencyKey = idempotencyKey,
            Parameters = parameters
        }, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "P7.2 contact basic-info update failed with {ErrorCode}.",
                result.ErrorCode ?? "unknown");
            throw new InvalidOperationException("Contact basic-info update failed.");
        }

        var data = result.Data;
        if (data is null ||
            !string.Equals(data.OperationId, OperationIds.MemberInfoContactUpdateBasicInfo, StringComparison.Ordinal) ||
            !string.Equals(data.CeVersion, "9.1", StringComparison.Ordinal) ||
            data.ResponseKind != OperationResponseKind.ContactBasicInfoUpdate ||
            data.ContactBasicInfoUpdate is null)
        {
            throw new InvalidOperationException(
                "Contact basic-info response does not match the requested operation contract.");
        }

        return new ContactBasicInfoUpdateResult
        {
            Disposition = data.ContactBasicInfoUpdate.Disposition,
            CorrelationCategory = data.ContactBasicInfoUpdate.CorrelationCategory
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

    /// <summary>
    /// 驗證 1-128 個 URL-safe 字元的冪等鍵。此格式與 ControlPlane canonical preparer 一致，避免 Gateway／
    /// Embedded 在不同層得到不同的 acceptance 結果，也不讓任意字串進入稽核或 queue state。
    /// </summary>
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

    /// <summary>
    /// 將 optional phone/address 複製成 bounded string；空白只代表不覆寫，不代表清除欄位。
    /// </summary>
    private static void AddOptionalString(
        IDictionary<string, object?> parameters,
        string name,
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        try
        {
            if (StrictUtf8.GetByteCount(normalized) > MaximumStringParameterBytes)
            {
                throw new ArgumentException("The contact field exceeds its bounded size.", parameterName);
            }
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("The contact field contains invalid text.", parameterName);
        }

        parameters[name] = normalized;
    }
}
