// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/SpecialResources/Package03SpecialResourceClient.cs
// 用途：將 P7.3 image、metadata 與 weekly statistic typed DTO 映射為固定 Dynamics capability。
//
// 安全與生命週期：
// 1. 類別是 stateless singleton，只保存 DI-owned executor/logger；不保存 request、image bytes、metadata、cookie、
//    profile、generation、credential、token、session、stream、cache、timer 或 background task。
// 2. 所有 caller string/image 在第一次 await 前驗證、trim、限制並 defensive copy；executor 是 HTTP/Data8 lease 的唯一 owner。
// 3. response operation/branch 不符、write failure、timeout、cancel 或 unknown outcome 全部 fail closed；image write 不自動重試。
// ============================================================================

using System.Text;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.SpecialResources;

/// <summary>
/// P7.3 special-resource 的 stateless typed client。它將產品純值 DTO 收斂到五個 server-owned operation，
/// 但不實作 ChurchReport consumer cutover、feature gate、cache、CE mutation evidence 或 transport fallback；
/// 那些責任分別屬於後續 P7.4、runtime/connector 與 deployment task。
/// </summary>
public sealed class Package03SpecialResourceClient : IPackage03SpecialResourceClient
{
    private const int MaximumImagePayloadBytes = 32 * 1024;
    private const int MaximumIdempotencyKeyCharacters = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<Package03SpecialResourceClient> _logger;

    /// <summary>
    /// 建立不擁有下游 transport 的 typed client。executor 與 logger 的 singleton/HttpClient/pool lifecycle 完全由
    /// composition root 擁有；此 constructor 不讀取設定、不建立 connection、cache、timer 或可釋放資源。
    /// </summary>
    public Package03SpecialResourceClient(
        IDynamicsOperationExecutor executor,
        ILogger<Package03SpecialResourceClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ContactImageDisplayResult> RetrieveContactImageDisplayAsync(
        ContactImageDisplayRetrieveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var execution = await ExecuteAsync(
            request.ProfileAlias,
            request.WorkloadSubjectId,
            OperationIds.MemberInfoContactRetrieveImageDisplay,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = RequireNonEmptyGuid(request.ContactId, nameof(request.ContactId))
            },
            idempotencyKey: null,
            cancellationToken).ConfigureAwait(false);
        var data = RequireExpectedResponse(
            execution.Data,
            OperationIds.MemberInfoContactRetrieveImageDisplay,
            OperationResponseKind.ContactImageDisplay,
            static response => response.ContactImageDisplay is not null);
        var result = ContactImageDisplayResult.FromResponse(data.ContactImageDisplay!);
        _logger.LogInformation(
            "Package03 contact-image display retrieve completed for profile {ProfileAlias}",
            request.ProfileAlias.Trim());
        return result;
    }

    /// <inheritdoc />
    public async Task<ContactImageResult> RetrieveContactImageAsync(
        ContactImageRetrieveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var execution = await ExecuteAsync(
            request.ProfileAlias,
            request.WorkloadSubjectId,
            OperationIds.MemberInfoContactRetrieveImage,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = RequireNonEmptyGuid(request.ContactId, nameof(request.ContactId))
            },
            idempotencyKey: null,
            cancellationToken).ConfigureAwait(false);
        var data = RequireExpectedResponse(
            execution.Data,
            OperationIds.MemberInfoContactRetrieveImage,
            OperationResponseKind.ContactImage,
            static response => response.ContactImage is not null);
        var image = data.ContactImage!;
        var bytes = image.GetImageBytes();
        _logger.LogInformation(
            "Package03 contact-image retrieve completed for profile {ProfileAlias}",
            request.ProfileAlias.Trim());
        return new ContactImageResult(bytes, image.MediaKind);
    }

    /// <inheritdoc />
    public Task<ContactImageUpdateResult> UpdateMemberInfoContactImageAsync(
        ContactImageUpdateRequest request,
        CancellationToken cancellationToken = default)
        => UpdateContactImageAsync(
            request,
            OperationIds.MemberInfoContactUpdateImage,
            cancellationToken);

    /// <inheritdoc />
    public Task<ContactImageUpdateResult> UpdateNewPersonContactImageAsync(
        ContactImageUpdateRequest request,
        CancellationToken cancellationToken = default)
        => UpdateContactImageAsync(
            request,
            OperationIds.NewPersonContactUpdateImage,
            cancellationToken);

    /// <inheritdoc />
    public async Task<OptionSetRetrieveResult> RetrieveOptionSetAsync(
        OptionSetRetrieveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Target))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Target));
        }

        var execution = await ExecuteAsync(
            request.ProfileAlias,
            request.WorkloadSubjectId,
            OperationIds.MetadataOptionSetByAttribute,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["target"] = request.Target },
            idempotencyKey: null,
            cancellationToken).ConfigureAwait(false);
        var data = RequireExpectedResponse(
            execution.Data,
            OperationIds.MetadataOptionSetByAttribute,
            OperationResponseKind.OptionSetOptions,
            static response => response.OptionSetOptions is not null);
        var options = data.OptionSetOptions!
            .Select(static option => new OptionSetOptionDto
            {
                Value = option.Value,
                Label = string.Concat(option.Label),
                ConfiguredOrder = option.ConfiguredOrder
            })
            .ToArray();
        _logger.LogInformation(
            "Package03 metadata option-set retrieve completed with {OptionCount} options for profile {ProfileAlias}",
            options.Length,
            request.ProfileAlias.Trim());
        return new OptionSetRetrieveResult { Options = options };
    }

    /// <inheritdoc />
    public async Task<MeetingStatisticsRetrieveResult> RetrieveMeetingStatisticsAsync(
        MeetingStatisticsRetrieveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sunday = RequireUtcSunday(request.SundayDate, nameof(request.SundayDate));
        var execution = await ExecuteAsync(
            request.ProfileAlias,
            request.WorkloadSubjectId,
            OperationIds.StatsMeetingRetrieveBySunday,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["sundayDate"] = sunday },
            idempotencyKey: null,
            cancellationToken).ConfigureAwait(false);
        var data = RequireExpectedResponse(
            execution.Data,
            OperationIds.StatsMeetingRetrieveBySunday,
            OperationResponseKind.MeetingStatistics,
            static response => response.MeetingStatistics is not null);
        var statistics = data.MeetingStatistics!
            .Select(static statistic => new MeetingStatisticDto
            {
                MeetingStatisticId = statistic.MeetingStatisticId,
                Name = statistic.Name is null ? null : string.Concat(statistic.Name),
                CreatedOn = statistic.CreatedOn,
                SundayDate = statistic.SundayDate
            })
            .ToArray();
        _logger.LogInformation(
            "Package03 weekly meeting-statistics retrieve completed with {StatisticCount} rows for profile {ProfileAlias}",
            statistics.Length,
            request.ProfileAlias.Trim());
        return new MeetingStatisticsRetrieveResult { Statistics = statistics };
    }

    /// <summary>
    /// 執行兩個固定 image write 之一。payload bytes 在此方法同步複製成封閉 DTO，原 caller array 隨後可被安全修改或
    /// 回收；executor 還會在 admission 前使用 decoder 重驗。成功只接受 ContactImageUpdate branch 的 read-back category，
    /// 故 timeout/ambiguous/connector error 不會在本層被轉成 success 或 background retry。
    /// </summary>
    private async Task<ContactImageUpdateResult> UpdateContactImageAsync(
        ContactImageUpdateRequest request,
        string operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payload = CreateImagePayload(request.ImageBytes, request.MediaKind, nameof(request.ImageBytes));
        var execution = await ExecuteAsync(
            request.ProfileAlias,
            request.WorkloadSubjectId,
            operationId,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = RequireNonEmptyGuid(request.ContactId, nameof(request.ContactId)),
                ["imagePayload"] = payload
            },
            RequireIdempotencyKey(request.IdempotencyKey),
            cancellationToken).ConfigureAwait(false);
        var data = RequireExpectedResponse(
            execution.Data,
            operationId,
            OperationResponseKind.ContactImageUpdate,
            static response => response.ContactImageUpdate is not null);
        var update = data.ContactImageUpdate!;
        _logger.LogInformation(
            "Package03 contact-image update completed after exact read-back for profile {ProfileAlias}",
            request.ProfileAlias.Trim());
        return new ContactImageUpdateResult
        {
            Disposition = update.Disposition,
            CorrelationCategory = update.CorrelationCategory
        };
    }

    /// <summary>
    /// 在首次 await 前建立 executor request。profile/workload 只接受 trim 後的 bounded non-empty text，parameters
    /// 是本方法新建 dictionary；executor 失敗時不回顯 response body、endpoint、credential 或 token。這個 client
    /// 不保留 task 或 cancellation token，取消後由 executor/pool 走其既有 deterministic cleanup。
    /// </summary>
    private async Task<OperationExecutionResult> ExecuteAsync(
        string profileAlias,
        string workloadSubjectId,
        string operationId,
        IReadOnlyDictionary<string, object?> parameters,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var normalizedProfileAlias = RequireBoundedText(profileAlias, nameof(profileAlias), 128);
        var normalizedWorkloadSubjectId = RequireBoundedText(workloadSubjectId, nameof(workloadSubjectId), 256);
        var result = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = normalizedProfileAlias,
            WorkloadSubjectId = normalizedWorkloadSubjectId,
            CapabilityOperationId = operationId,
            Parameters = parameters,
            IdempotencyKey = idempotencyKey
        }, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Package03 special-resource operation failed.");
        }

        return result;
    }

    /// <summary>
    /// 驗證封閉 response 的 operation ID、discriminator 與唯一 branch。它是 ProductClient 防止不同 capability
    /// response 被交叉映射的最後一層；任何 mismatch 都在 DTO/caching 前失敗，且不記錄原始 response。
    /// </summary>
    private static OperationResponseData RequireExpectedResponse(
        OperationResponseData? data,
        string expectedOperationId,
        OperationResponseKind expectedResponseKind,
        Func<OperationResponseData, bool> hasExpectedBranch)
    {
        if (data is null ||
            !string.Equals(data.OperationId, expectedOperationId, StringComparison.Ordinal) ||
            data.ResponseKind != expectedResponseKind ||
            !hasExpectedBranch(data))
        {
            throw new InvalidOperationException("Package03 response does not match the requested operation contract.");
        }

        return data;
    }

    /// <summary>建立 defensive-copy image payload，先限制 enum/byte size，再交給 executor 的 decoder 作深度驗證。</summary>
    private static ContactImageResponseData CreateImagePayload(
        byte[] imageBytes,
        ContactImageMediaKind mediaKind,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (!Enum.IsDefined(mediaKind) || imageBytes.Length is < 1 or > MaximumImagePayloadBytes)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return new ContactImageResponseData(imageBytes.ToArray(), mediaKind);
    }

    /// <summary>驗證唯一 UTC Sunday 00:00，禁止 host time-zone conversion 將相同週報查詢導向不同日期。</summary>
    private static DateTimeOffset RequireUtcSunday(DateTimeOffset value, string parameterName)
    {
        var utc = value.ToUniversalTime();
        if (utc.TimeOfDay != TimeSpan.Zero || utc.DayOfWeek != DayOfWeek.Sunday)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return utc;
    }

    /// <summary>驗證非空 Guid，避免未知/empty identity 進入下游 authorization、admission 或 connector query。</summary>
    private static Guid RequireNonEmptyGuid(Guid value, string parameterName)
        => value != Guid.Empty ? value : throw new ArgumentOutOfRangeException(parameterName);

    /// <summary>驗證 bounded profile/workload text；無效 UTF-16、空白或過長資料在 executor 前拒絕。</summary>
    private static string RequireBoundedText(string? value, string parameterName, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(parameterName + " is required.", parameterName);
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
            throw new ArgumentException(parameterName + " is invalid.", parameterName);
        }

        return normalized;
    }

    /// <summary>驗證 bounded URL-safe idempotency key；它不被寫入 cache/log，也不授權 retry uncertain write。</summary>
    private static string RequireIdempotencyKey(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumIdempotencyKeyCharacters ||
            !value.All(static character =>
                (character >= 'a' && character <= 'z') ||
                (character >= 'A' && character <= 'Z') ||
                (character >= '0' && character <= '9') ||
                character is '-' or '.' or '_' or '~'))
        {
            throw new ArgumentException("idempotencyKey is invalid.", nameof(value));
        }

        return value;
    }
}
