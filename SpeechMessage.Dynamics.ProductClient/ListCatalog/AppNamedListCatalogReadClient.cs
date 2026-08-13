// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListCatalog/AppNamedListCatalogReadClient.cs
// 用途：將 ORG-CALL-00014 的封閉 executor response 映射為產品可使用的不可變目錄 DTO 快照。
//
// 本 singleton client 只保存 stateless executor/logger 相依性，不保存 profile、workload、request、DTO、回應、
// session、cache、retry state、timer、subscription、cancellation registration 或 transport resource。每一次呼叫
// 建立新的 request、DTO 與 ReadOnlyCollection；executor 是 connector、HTTP、lease 與 fault/cancellation cleanup 的
// 單一 owner，client 不建立平行資料路徑、Entity 或 background work。
// ============================================================================

using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.ListCatalog;

/// <summary>
/// ORG-CALL-00014 的 stateless ProductClient implementation。
/// 此類別強制精確 operation ID、精確 response kind 與唯一 catalog branch，並將每筆 wire scalar 複製為新的 DTO；
/// 因而 source collection、另一 profile 或先前 request 的可變狀態無法跨出 executor boundary。它沒有可釋放資源，
/// 不擁有 connector/lease，取消、逾時與 fault 時的 deterministic cleanup 保持由 executor 及其底層 owner 執行。
/// </summary>
public sealed class AppNamedListCatalogReadClient : IAppNamedListCatalogReadClient
{
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<AppNamedListCatalogReadClient> _logger;

    /// <summary>
    /// 建立不含 request 或 profile state 的 catalog client。
    /// DI 可以安全地把此類別註冊為 singleton，因為它只引用 stateless executor/logger；每次 operation 的 routing 和
    /// DTO collection 都在方法區域變數內，絕不保留至下一個使用者、tenant、profile 或 workload 呼叫。
    /// </summary>
    /// <param name="executor">唯一擁有 Gateway/Embedded transport、connector、lease 與其清理的 operation executor。</param>
    /// <param name="logger">僅記錄固定 operation/數量，不能記錄 profile、row、session、credential 或上游 detail。</param>
    public AppNamedListCatalogReadClient(
        IDynamicsOperationExecutor executor,
        ILogger<AppNamedListCatalogReadClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 以 deployment-owned routing 執行固定 catalog read，並建立新的唯讀 DTO collection。
    /// 驗證先於 executor I/O，成功 response 必須同時符合 exact operation、exact discriminator 與 non-null catalog
    /// branch；任何 fault、錯誤 branch、null row 或 empty list ID 都不發佈 partial data。方法不建立 retry/fallback、
    /// cache、Entity、stream、timer 或 cancellation registration，取消權杖只原樣交給 executor owner。
    /// </summary>
    /// <param name="profileAlias">部署端選定的 profile alias；空白值在任何 outbound I/O 前拒絕。</param>
    /// <param name="workloadSubjectId">server-derived workload subject；空白值在任何 outbound I/O 前拒絕。</param>
    /// <param name="cancellationToken">要原樣向下傳遞的目前 request 取消權杖。</param>
    /// <returns>由新 DTO 與唯讀包裝構成、不可暴露 backing array 的目前 request 快照。</returns>
    public async Task<IReadOnlyList<AppNamedListCatalogRecordDto>> RetrieveAppNamedListCatalogAsync(
        string profileAlias,
        string workloadSubjectId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProfileAlias = RequireNonEmpty(profileAlias, nameof(profileAlias));
        var normalizedWorkloadSubjectId = RequireNonEmpty(workloadSubjectId, nameof(workloadSubjectId));

        var execution = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = normalizedProfileAlias,
            CapabilityOperationId = OperationIds.ListCatalogRetrieveAppNamed,
            WorkloadSubjectId = normalizedWorkloadSubjectId,
            Parameters = new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(StringComparer.Ordinal))
        }, cancellationToken).ConfigureAwait(false);

        if (!execution.Succeeded)
        {
            _logger.LogWarning(
                "App-named list catalog read operation failed with {ErrorCode}.",
                execution.ErrorCode ?? "unknown");
            throw new InvalidOperationException("App-named list catalog read failed.");
        }

        var data = execution.Data;
        if (data is null ||
            !string.Equals(
                data.OperationId,
                OperationIds.ListCatalogRetrieveAppNamed,
                StringComparison.Ordinal) ||
            data.ResponseKind != OperationResponseKind.AppNamedListCatalogRecords ||
            data.AppNamedListCatalogRecords is null)
        {
            throw new InvalidOperationException(
                "App-named list catalog response does not match the requested operation contract.");
        }

        var mappedRows = new List<AppNamedListCatalogRecordDto>(data.AppNamedListCatalogRecords.Count);
        foreach (var record in data.AppNamedListCatalogRecords)
        {
            mappedRows.Add(MapRecord(record));
        }

        var publishedRows = new ReadOnlyCollection<AppNamedListCatalogRecordDto>(mappedRows);
        _logger.LogInformation(
            "App-named list catalog read {OperationId} returned {Count} rows.",
            OperationIds.ListCatalogRetrieveAppNamed,
            publishedRows.Count);
        return publishedRows;
    }

    /// <summary>
    /// 驗證並正規化 server-owned routing scalar。
    /// 這是 I/O 前唯一接受 routing 的位置，避免 singleton client 以空白、caller-controlled 或上一個 request 的值選擇
    /// connector/profile；方法只建立短命字串，沒有快取、session、租用、registration 或其他清理責任。
    /// </summary>
    /// <param name="value">deployment composition 傳入的 profile 或 workload 值。</param>
    /// <param name="parameterName">例外中對應的公開參數名稱。</param>
    /// <returns>已修剪且非空白的 request-local routing 值。</returns>
    private static string RequireNonEmpty(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A required routing value is missing.", parameterName);
        }

        return value.Trim();
    }

    /// <summary>
    /// 將一筆已由唯一 catalog branch 提供的 wire row 複製為產品 DTO。
    /// null row 或 empty list ID 表示 upstream envelope 違反固定 projection，因此立即拒絕且不發布任何 collection；
    /// mapper 不接觸 CRM Entity、metadata、cache、profile、connector 或 transport，也不保留 record 的可變集合參考。
    /// </summary>
    /// <param name="record">由 <see cref="OperationResponseData.AppNamedListCatalogRecords"/> 提供的純量 wire row。</param>
    /// <returns>獨立於 wire row 與來源 collection 的新的 DTO。</returns>
    private static AppNamedListCatalogRecordDto MapRecord(AppNamedListCatalogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ListId == Guid.Empty)
        {
            throw new InvalidOperationException("App-named list catalog response contains an invalid list ID.");
        }

        return new AppNamedListCatalogRecordDto
        {
            ListId = record.ListId,
            ListName = record.ListName,
            CreatedFromCodeOption = record.CreatedFromCodeOption,
            LastUsedOn = record.LastUsedOn,
            Purpose = record.Purpose
        };
    }
}
