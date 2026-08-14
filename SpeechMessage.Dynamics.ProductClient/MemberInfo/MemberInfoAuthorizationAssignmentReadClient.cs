// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/MemberInfo/MemberInfoAuthorizationAssignmentReadClient.cs
// 用途：將固定 Data8 assignment evidence response 映射為 ProductClient immutable DTO。
// ============================================================================

using System.Collections.ObjectModel;
using System.Text;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.MemberInfo;

/// <summary>
/// MemberInfo 指派證據的無狀態 ProductClient。
/// 此 singleton 僅保留 DI 擁有的 executor/logger；每個 request 的 profile、workload、subject、token、wire response
/// 與 DTO 都是區域變數。它不建立 CRM SDK graph、不快取使用者 evidence、不持有 connector/lease，也不重試或
/// fallback，因此不會將 A/B request 的授權決策、資料或資源生命週期交叉重用。
/// </summary>
public sealed class MemberInfoAuthorizationAssignmentReadClient : IMemberInfoAuthorizationAssignmentReadClient
{
    private const string CapabilityOperationId = OperationIds.MemberInfoAuthorizationAssignmentResolveBySubject;
    private const string RequiredCeVersion = "9.1";
    private const int MaximumProfileAliasBytes = 128;
    private const int MaximumWorkloadSubjectBytes = 256;
    private const int MaximumAssignedListIds = 512;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<MemberInfoAuthorizationAssignmentReadClient> _logger;

    /// <summary>
    /// 建立由 composition root 管理的 stateless client。
    /// executor 是唯一會接觸 connector/lease/transport 的相依並負責其 finally/dispose 契約；logger 只記錄固定
    /// operation、分類與 count，禁止記錄 subject、profile、list、endpoint、credential 或 upstream exception。
    /// </summary>
    /// <param name="executor">具備既有隔離、取消與資源回收責任的 operation executor。</param>
    /// <param name="logger">不保留 request detail 的結構化 logger。</param>
    public MemberInfoAuthorizationAssignmentReadClient(
        IDynamicsOperationExecutor executor,
        ILogger<MemberInfoAuthorizationAssignmentReadClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<MemberInfoAuthorizationAssignmentReadResult> ResolveBySubjectAsync(
        MemberInfoAuthorizationAssignmentReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profileAlias = RequireBoundedRoutingValue(
            request.ProfileAlias,
            nameof(request.ProfileAlias),
            MaximumProfileAliasBytes);
        var workloadSubjectId = RequireBoundedRoutingValue(
            request.WorkloadSubjectId,
            nameof(request.WorkloadSubjectId),
            MaximumWorkloadSubjectBytes);
        var subjectContactId = request.SubjectContactId;
        if (subjectContactId == Guid.Empty)
        {
            throw new ArgumentException("SubjectContactId is required.", nameof(request.SubjectContactId));
        }

        var parameters = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["subjectContactId"] = subjectContactId
            });
        var execution = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = profileAlias,
            WorkloadSubjectId = workloadSubjectId,
            CapabilityOperationId = CapabilityOperationId,
            IdempotencyKey = null,
            Parameters = parameters
        }, cancellationToken).ConfigureAwait(false);

        if (!execution.Succeeded)
        {
            _logger.LogWarning(
                "MemberInfo authorization-assignment read operation failed with {ErrorCode}.",
                execution.ErrorCode ?? "unknown");
            throw new InvalidOperationException("MemberInfo authorization-assignment read failed.");
        }

        var evidence = execution.Data;
        if (evidence is null ||
            !string.Equals(evidence.OperationId, CapabilityOperationId, StringComparison.Ordinal) ||
            !string.Equals(evidence.CeVersion, RequiredCeVersion, StringComparison.Ordinal) ||
            evidence.ResponseKind != OperationResponseKind.MemberInfoAssignmentEvidence ||
            evidence.MemberInfoAuthorizationAssignmentEvidence is null)
        {
            throw new InvalidOperationException(
                "MemberInfo authorization-assignment response does not match the requested operation contract.");
        }

        var wireEvidence = evidence.MemberInfoAuthorizationAssignmentEvidence;
        if (wireEvidence.SubjectContactId != subjectContactId ||
            !Enum.IsDefined(wireEvidence.AccessMode) ||
            wireEvidence.AssignedListIds.Count > MaximumAssignedListIds)
        {
            throw new InvalidOperationException("MemberInfo authorization-assignment evidence is invalid.");
        }

        var copiedListIds = CopyValidatedListIds(wireEvidence);
        _logger.LogInformation(
            "MemberInfo authorization-assignment read {OperationId} returned {Count} list IDs.",
            CapabilityOperationId,
            copiedListIds.Count);
        return new MemberInfoAuthorizationAssignmentReadResult(
            subjectContactId,
            wireEvidence.AccessMode,
            copiedListIds);
    }

    /// <summary>
    /// 驗證 response access mode 與 list cardinality，並建立獨立 allowlist snapshot。
    /// Church-wide 帶任一 list、未定義 mode、空 GUID 或 duplicate 都代表 query/response 不可證明；此方法會在
    /// 發布 DTO 前 fail closed。AssignedLists 的空集合是有效「目前無有效指派」結果，不能改用舊 Session/ListManager。
    /// </summary>
    /// <param name="evidence">已通過 envelope discriminator 驗證的 wire evidence。</param>
    /// <returns>不可被上游 backing collection 改寫的 request-local GUID snapshot。</returns>
    private static IReadOnlyList<Guid> CopyValidatedListIds(
        MemberInfoAuthorizationAssignmentEvidenceResponseData evidence)
    {
        if (evidence.AccessMode == MemberInfoAuthorizationAssignmentAccessMode.ChurchWide)
        {
            if (evidence.AssignedListIds.Count != 0)
            {
                throw new InvalidOperationException("Church-wide assignment evidence must not contain list IDs.");
            }

            return Array.Empty<Guid>();
        }

        var unique = new HashSet<Guid>();
        var copied = new List<Guid>(evidence.AssignedListIds.Count);
        foreach (var listId in evidence.AssignedListIds)
        {
            if (listId == Guid.Empty || !unique.Add(listId))
            {
                throw new InvalidOperationException("MemberInfo authorization-assignment evidence contains invalid list IDs.");
            }

            copied.Add(listId);
        }

        return new ReadOnlyCollection<Guid>(copied);
    }

    /// <summary>
    /// 複製並驗證 deployment/server-owned routing scalar。
    /// UTF-8 byte bound、trim 與 strict surrogate 驗證在 executor I/O 前完成，避免過大或損壞 routing 值進入
    /// profile/connector composition。此方法不保存輸入，不建立 cache、session、token 或取消註冊。
    /// </summary>
    /// <param name="value">待驗證的 routing scalar。</param>
    /// <param name="parameterName">公開 API 的錯誤參數名稱。</param>
    /// <param name="maximumBytes">固定 UTF-8 byte 上限。</param>
    /// <returns>獨立配置且符合上限的 routing scalar。</returns>
    private static string RequireBoundedRoutingValue(string? value, string parameterName, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A required routing value is missing.", parameterName);
        }

        var normalized = value.Trim();
        try
        {
            if (StrictUtf8.GetByteCount(normalized) > maximumBytes)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("A routing value contains invalid text.", parameterName);
        }

        return new string(normalized.AsSpan());
    }
}
