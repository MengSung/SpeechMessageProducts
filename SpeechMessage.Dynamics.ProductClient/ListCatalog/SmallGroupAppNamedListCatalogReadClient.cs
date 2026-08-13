// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListCatalog/SmallGroupAppNamedListCatalogReadClient.cs
// 用途：將 ORG-CALL-00065 的封閉 small-group response branch 映射為產品端的不可變 DTO 快照。
//
// 此 singleton 只保留 stateless executor/logger 相依性。profile、workload、OperationExecutionRequest、wire row、DTO、
// collection、取消權杖與 failure 都是方法區域資料；client 不建立 cache、retry、timer、subscription、background work、
// Entity 或第二條 transport path。executor 是 connector、HTTP、lease、permit 以及 cancellation/fault cleanup 的唯一 owner。
// ============================================================================

using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.ListCatalog;

/// <summary>
/// 實作 ORG-CALL-00065 的 stateless small-group catalog ProductClient。
/// 每次呼叫先驗證 profile/workload，接著送出唯一固定 operation 和空白 read-only parameter map；成功時同時要求 exact
/// operation ID、exact response kind 與 non-null small-group branch，然後逐列建立新的 DTO 並以 ReadOnlyCollection
/// 發佈。此設計不讓 source collection、A/B profile、上一次 response 或 leader lookup graph 跨 request 留存。
/// </summary>
public sealed class SmallGroupAppNamedListCatalogReadClient : ISmallGroupAppNamedListCatalogReadClient
{
    private const string CapabilityOperationId = OperationIds.ListCatalogRetrieveAppNamedSmallGroups;
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<SmallGroupAppNamedListCatalogReadClient> _logger;

    /// <summary>
    /// 建立不含 request/profile/response state 的 client。
    /// singleton DI 安全的條件是此類別只保存 executor/logger；每次 routing 與 DTO 都由 method scope 擁有，完成後沒有
    /// collection、closure、timer、subscription 或 cancellation registration 被保留。任何外部資源的釋放仍由 executor
    /// 在其 request scope 中確定執行。
    /// </summary>
    /// <param name="executor">唯一擁有 Gateway/Embedded transport、connector、lease 與 cleanup 的 operation executor。</param>
    /// <param name="logger">只可記錄固定 operation/列數，不能記錄 profile、row、leader、session、credential 或 upstream detail。</param>
    public SmallGroupAppNamedListCatalogReadClient(
        IDynamicsOperationExecutor executor,
        ILogger<SmallGroupAppNamedListCatalogReadClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 執行 fixed small-group catalog read，並只發佈本次 request 的 readonly DTO snapshot。
    /// routing 驗證在 executor I/O 前完成；上游 failure、錯誤 operation/kind/branch、null wire row 或 empty list ID 都會
    /// 在發布 collection 前拒絕。此方法不捕捉 <see cref="OperationCanceledException"/>、不建立 linked token、retry、
    /// fallback、cache、Entity 或背景工作，因此取消與 transport 不確定狀態完全由 executor owner 依既有生命週期處理。
    /// </summary>
    /// <param name="profileAlias">deployment-owned profile alias；空白值在 I/O 前失敗。</param>
    /// <param name="workloadSubjectId">server-derived workload subject；空白值在 I/O 前失敗。</param>
    /// <param name="cancellationToken">必須原樣傳遞到 executor 的目前 request 取消權杖。</param>
    /// <returns>新 DTO 與 read-only wrapper 構成、沒有 exposed backing array 的目前 request 結果。</returns>
    public async Task<IReadOnlyList<SmallGroupAppNamedListCatalogRecordDto>> RetrieveSmallGroupAppNamedListCatalogAsync(
        string profileAlias,
        string workloadSubjectId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProfileAlias = RequireRoutingValue(profileAlias, nameof(profileAlias));
        var normalizedWorkloadSubjectId = RequireRoutingValue(workloadSubjectId, nameof(workloadSubjectId));

        var execution = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = normalizedProfileAlias,
            CapabilityOperationId = CapabilityOperationId,
            WorkloadSubjectId = normalizedWorkloadSubjectId,
            Parameters = new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(StringComparer.Ordinal))
        }, cancellationToken).ConfigureAwait(false);

        if (!execution.Succeeded)
        {
            _logger.LogWarning(
                "Small-group app-named catalog read operation failed with {ErrorCode}.",
                execution.ErrorCode ?? "unknown");
            throw new InvalidOperationException("Small-group app-named catalog read failed.");
        }

        var data = execution.Data;
        if (data is null ||
            !string.Equals(data.OperationId, CapabilityOperationId, StringComparison.Ordinal) ||
            data.ResponseKind != OperationResponseKind.SmallGroupAppNamedListCatalogRecords ||
            data.SmallGroupAppNamedListCatalogRecords is null)
        {
            throw new InvalidOperationException(
                "Small-group app-named catalog response does not match the requested operation contract.");
        }

        var mappedRows = new List<SmallGroupAppNamedListCatalogRecordDto>(
            data.SmallGroupAppNamedListCatalogRecords.Count);
        foreach (var record in data.SmallGroupAppNamedListCatalogRecords)
        {
            mappedRows.Add(MapRecord(record));
        }

        var publishedRows = new ReadOnlyCollection<SmallGroupAppNamedListCatalogRecordDto>(mappedRows);
        _logger.LogInformation(
            "Small-group app-named catalog read {OperationId} returned {Count} rows.",
            CapabilityOperationId,
            publishedRows.Count);
        return publishedRows;
    }

    /// <summary>
    /// 驗證 deployment/server 所有的 routing scalar，確保無效值不能進入 executor。
    /// 此 helper 只回傳本次呼叫的修剪字串，不寫入 static field、cache、session 或 singleton collection；因此不會將一個
    /// request 的 profile/workload 留給另一個 request 使用，也不會建立需要 dispose 的資源。
    /// </summary>
    /// <param name="value">profile 或 workload 的 server-owned 輸入值。</param>
    /// <param name="parameterName">例外訊息中公開 API 的參數名稱。</param>
    /// <returns>修剪且非空白的 request-local routing scalar。</returns>
    private static string RequireRoutingValue(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A required routing value is missing.", parameterName);
        }

        return value.Trim();
    }

    /// <summary>
    /// 將一筆 small-group wire row 複製成無 CRM lookup graph 的產品 DTO。
    /// null row 或 empty list ID 是封閉 response contract 違約，立即 fail closed，確保不會將部分 collection、另一個
    /// profile 的資料或未驗證 list 發佈；mapper 只讀 scalar，不接觸 Entity、cache、profile、connector 或 transport。
    /// </summary>
    /// <param name="record">唯一 small-group response branch 產生的純量 wire row。</param>
    /// <returns>獨立於來源 record/collection 的新 DTO。</returns>
    private static SmallGroupAppNamedListCatalogRecordDto MapRecord(SmallGroupAppNamedListCatalogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ListId == Guid.Empty)
        {
            throw new InvalidOperationException("Small-group app-named catalog response contains an invalid list ID.");
        }

        return new SmallGroupAppNamedListCatalogRecordDto
        {
            ListId = record.ListId,
            ListName = record.ListName,
            CreatedFromCodeOption = record.CreatedFromCodeOption,
            LastUsedOn = record.LastUsedOn,
            Purpose = record.Purpose,
            RaceLeaderContactId = record.RaceLeaderContactId,
            FamilyLeaderContactId = record.FamilyLeaderContactId
        };
    }
}
