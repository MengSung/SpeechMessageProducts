// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListManagement/Package02ListManagementClient.cs
// 目的：將 P7.2 Slice C typed list-management DTO 映射成五個固定 Dynamics capability。
//
// 信任與生命週期：
// - 此 singleton client 只保存 DI-owned executor/logger；不保存 request、GUID collection、profile generation、
//   credential、token、session、timer、cache、stream、lease 或 background task。
// - 每個 public method 都在第一個 await 前複製／驗證 bounded input；下游 executor 才是 gateway/admission/lease owner。
// - 寫入失敗、timeout 或 response mismatch 不自動重送；只有 connector 的 fixed read-back 可建立成功結果。
// ============================================================================

using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.ListManagement;

/// <summary>
/// P7.2 Slice C 的 stateless typed client。它只把已授權產品 use case 映射到固定 operation ID，不能被用作
/// generic CRM adapter；deployment-owned profile/connector/CE/version routing 仍由 executor composition root 決定。
/// </summary>
public sealed class Package02ListManagementClient : IPackage02ListManagementClient
{
    private const int MaximumMemberIds = 1000;
    private const int MaximumIdempotencyKeyCharacters = 128;
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<Package02ListManagementClient> _logger;

    /// <summary>建立不擁有 transport/resource 的 typed client；executor/logger 的 singleton lifetime 由 DI 管理。</summary>
    public Package02ListManagementClient(
        IDynamicsOperationExecutor executor,
        ILogger<Package02ListManagementClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<StaticListMembershipMutationResult> AddMembersAsync(
        StaticListMembersAddRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var members = CopyDistinctSortedMemberIds(request.MemberIds, nameof(request.MemberIds));
        var data = await ExecuteAsync(
            OperationIds.ListMembersAddMany,
            RequireNonEmpty(request.ProfileAlias, nameof(request.ProfileAlias)),
            RequireNonEmpty(request.WorkloadSubjectId, nameof(request.WorkloadSubjectId)),
            NormalizeIdempotencyKey(request.IdempotencyKey),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = RequireGuid(request.ListId, nameof(request.ListId)),
                ["memberIds"] = members
            },
            cancellationToken).ConfigureAwait(false);

        if (data.ResponseKind != OperationResponseKind.StaticListMembershipMutation ||
            data.StaticListMembershipMutation is not { } response)
        {
            throw new InvalidOperationException("Static list membership response does not match the requested operation contract.");
        }

        return new StaticListMembershipMutationResult
        {
            Disposition = response.Disposition,
            CorrelationCategory = response.CorrelationCategory
        };
    }

    /// <inheritdoc />
    public async Task<StaticListMembershipMutationResult> RemoveMemberAsync(
        StaticListMemberRemoveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var data = await ExecuteAsync(
            OperationIds.ListMembersRemoveOne,
            RequireNonEmpty(request.ProfileAlias, nameof(request.ProfileAlias)),
            RequireNonEmpty(request.WorkloadSubjectId, nameof(request.WorkloadSubjectId)),
            NormalizeIdempotencyKey(request.IdempotencyKey),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = RequireGuid(request.ListId, nameof(request.ListId)),
                ["memberId"] = RequireGuid(request.MemberId, nameof(request.MemberId))
            },
            cancellationToken).ConfigureAwait(false);

        if (data.ResponseKind != OperationResponseKind.StaticListMembershipMutation ||
            data.StaticListMembershipMutation is not { } response)
        {
            throw new InvalidOperationException("Static list membership response does not match the requested operation contract.");
        }

        return new StaticListMembershipMutationResult
        {
            Disposition = response.Disposition,
            CorrelationCategory = response.CorrelationCategory
        };
    }

    /// <inheritdoc />
    public async Task<SmallGroupFixedFieldsMutationResult> UpdateSmallGroupFieldsAsync(
        SmallGroupFixedFieldsUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var data = await ExecuteAsync(
            OperationIds.ListManagementSmallGroupUpdateFields,
            RequireNonEmpty(request.ProfileAlias, nameof(request.ProfileAlias)),
            RequireNonEmpty(request.WorkloadSubjectId, nameof(request.WorkloadSubjectId)),
            NormalizeIdempotencyKey(request.IdempotencyKey),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = RequireGuid(request.ListId, nameof(request.ListId)),
                ["mode"] = ToSmallGroupMode(request.Mode),
                ["targetLeaderContactId"] = RequireGuid(request.TargetLeaderContactId, nameof(request.TargetLeaderContactId))
            },
            cancellationToken).ConfigureAwait(false);

        if (data.ResponseKind != OperationResponseKind.SmallGroupFixedFieldsMutation ||
            data.SmallGroupFixedFieldsMutation is not { } response)
        {
            throw new InvalidOperationException("Small-group fixed-fields response does not match the requested operation contract.");
        }

        return new SmallGroupFixedFieldsMutationResult
        {
            Disposition = response.Disposition,
            CorrelationCategory = response.CorrelationCategory
        };
    }

    /// <inheritdoc />
    public async Task<ContactOwnerAssignmentResult> AssignContactOwnerAsync(
        ContactOwnerAssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var data = await ExecuteAsync(
            OperationIds.ContactAssignOwner,
            RequireNonEmpty(request.ProfileAlias, nameof(request.ProfileAlias)),
            RequireNonEmpty(request.WorkloadSubjectId, nameof(request.WorkloadSubjectId)),
            NormalizeIdempotencyKey(request.IdempotencyKey),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = RequireGuid(request.ContactId, nameof(request.ContactId)),
                ["ownerSystemUserId"] = RequireGuid(request.OwnerSystemUserId, nameof(request.OwnerSystemUserId))
            },
            cancellationToken).ConfigureAwait(false);

        if (data.ResponseKind != OperationResponseKind.ContactOwnerAssignment ||
            data.ContactOwnerAssignment is not { } response)
        {
            throw new InvalidOperationException("Contact owner-assignment response does not match the requested operation contract.");
        }

        return new ContactOwnerAssignmentResult
        {
            Disposition = response.Disposition,
            CorrelationCategory = response.CorrelationCategory
        };
    }

    /// <inheritdoc />
    public async Task<ContactListTransferResult> TransferContactBetweenListsAsync(
        ContactListTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var contactId = RequireGuid(request.ContactId, nameof(request.ContactId));
        var targetListId = RequireGuid(request.TargetListId, nameof(request.TargetListId));
        var sourceListId = NormalizeOptionalGuid(request.SourceListId, nameof(request.SourceListId));
        if (sourceListId == targetListId)
        {
            throw new ArgumentException("SourceListId and TargetListId must differ when a source list is supplied.", nameof(request.SourceListId));
        }

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contactId"] = contactId,
            ["targetListId"] = targetListId,
            ["weekStartDate"] = NormalizeSundayStartUtc(request.WeekStartDate, nameof(request.WeekStartDate))
        };
        if (sourceListId is { } source)
        {
            parameters["sourceListId"] = source;
        }

        if (NormalizeOptionalGuid(request.OwnerSystemUserId, nameof(request.OwnerSystemUserId)) is { } owner)
        {
            parameters["ownerSystemUserId"] = owner;
        }

        var data = await ExecuteAsync(
            OperationIds.NewPersonContactTransferBetweenLists,
            RequireNonEmpty(request.ProfileAlias, nameof(request.ProfileAlias)),
            RequireNonEmpty(request.WorkloadSubjectId, nameof(request.WorkloadSubjectId)),
            NormalizeIdempotencyKey(request.IdempotencyKey),
            parameters,
            cancellationToken).ConfigureAwait(false);

        if (data.ResponseKind != OperationResponseKind.ContactListTransfer ||
            data.ContactListTransfer is not { } response)
        {
            throw new InvalidOperationException("Contact list-transfer response does not match the requested operation contract.");
        }

        return new ContactListTransferResult
        {
            Disposition = response.Disposition,
            CorrelationCategory = response.CorrelationCategory
        };
    }

    /// <summary>
    /// 執行一個已完整複製的 fixed capability request。這是每個 public method 的唯一 await 點；成功後同時比對
    /// operation ID 與 CE 9.1，避免 executor/transport 把另一個 profile/version 或 branch 的結果交回產品。
    /// 日誌只記固定 error code，絕不記錄 GUID、member set、baseline、endpoint、credential 或原始例外。
    /// </summary>
    private async Task<OperationResponseData> ExecuteAsync(
        string operationId,
        string profileAlias,
        string workloadSubjectId,
        string idempotencyKey,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var execution = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = profileAlias,
            CapabilityOperationId = operationId,
            WorkloadSubjectId = workloadSubjectId,
            IdempotencyKey = idempotencyKey,
            Parameters = parameters
        }, cancellationToken).ConfigureAwait(false);
        if (!execution.Succeeded)
        {
            _logger.LogWarning("P7.2 list-management operation failed with {ErrorCode}.", execution.ErrorCode ?? "unknown");
            throw new InvalidOperationException("List-management operation failed.");
        }

        var data = execution.Data;
        if (data is null ||
            !string.Equals(data.OperationId, operationId, StringComparison.Ordinal) ||
            !string.Equals(data.CeVersion, "9.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("List-management response does not match the requested operation contract.");
        }

        return data;
    }

    /// <summary>複製與排序 member IDs；空集合、empty/duplicate GUID 或超過 1,000 筆皆在 executor 前 fail closed。</summary>
    private static Guid[] CopyDistinctSortedMemberIds(IReadOnlyList<Guid>? source, string parameterName)
    {
        if (source is null || source.Count is < 1 or > MaximumMemberIds)
        {
            throw new ArgumentException("MemberIds must contain between 1 and 1000 distinct GUID values.", parameterName);
        }

        var copy = new Guid[source.Count];
        var distinct = new HashSet<Guid>();
        for (var index = 0; index < copy.Length; index++)
        {
            var value = source[index];
            if (value == Guid.Empty || !distinct.Add(value))
            {
                throw new ArgumentException("MemberIds must contain only distinct non-empty GUID values.", parameterName);
            }

            copy[index] = value;
        }

        Array.Sort(copy);
        return copy;
    }

    /// <summary>複製並 trim deployment/workload scalar；錯誤訊息不回顯其來源值。</summary>
    private static string RequireNonEmpty(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A required routing value is missing.", parameterName);
        }

        return value.Trim();
    }

    /// <summary>驗證 required GUID；不接受 empty identity，避免 connector 的固定 relationship/action 失去資料邊界。</summary>
    private static Guid RequireGuid(Guid value, string parameterName)
        => value != Guid.Empty
            ? value
            : throw new ArgumentException("A required GUID is missing.", parameterName);

    /// <summary>驗證 optional GUID；null 表示不送出該 fixed optional mutation，empty GUID 則是格式錯誤。</summary>
    private static Guid? NormalizeOptionalGuid(Guid? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Value == Guid.Empty)
        {
            throw new ArgumentException("An optional GUID cannot be empty when supplied.", parameterName);
        }

        return value.Value;
    }

    /// <summary>
    /// 將 transfer weekly-report key 正規化為 UTC Sunday 00:00。這個明確 business boundary 防止 Lenovo 時區或 DST
    /// 使同一 transfer 對應到不同週報；connector 後續只可依該純值搜尋 exactly one target-list weekly report。
    /// </summary>
    private static DateTimeOffset NormalizeSundayStartUtc(DateTimeOffset value, string parameterName)
    {
        var utc = value.ToUniversalTime();
        if (utc.TimeOfDay != TimeSpan.Zero || utc.DayOfWeek != DayOfWeek.Sunday)
        {
            throw new ArgumentException("WeekStartDate must be a UTC Sunday at 00:00.", parameterName);
        }

        return utc;
    }

    /// <summary>將 enum 轉成固定 server mode；未知數值在 executor 前 fail closed。</summary>
    private static string ToSmallGroupMode(SmallGroupFixedFieldsUpdateMode value)
        => value switch
        {
            SmallGroupFixedFieldsUpdateMode.ChangeRaceLeader => "change-race-leader",
            SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader => "change-area-leader",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported small-group fixed-fields mode.")
        };

    /// <summary>驗證固定 1-128 字元的 URL-safe idempotency key，不將原始值寫入錯誤或日誌。</summary>
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
