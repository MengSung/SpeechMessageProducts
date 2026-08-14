// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListCatalog/AppNamedMembershipReadClient.cs
// 用途：將 ORG-CALL-00057 的封閉 executor response 映射為 ProductClient 的不可變 membership DTO 快照。
//
// 此 stateless singleton 只保存 DI-owned executor/logger。profile、workload、contact、OperationExecutionRequest、wire row、
// DTO 與 collection 都是方法區域資料；它不建立 HTTP endpoint、Entity、cache、retry、fallback、timer、subscription、
// cancellation registration、背景工作或第二條 transport path。executor 是所有 connector/lease/transport cleanup 的唯一 owner。
// ============================================================================

using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.ListCatalog;

/// <summary>
/// ORG-CALL-00057 的 stateless、DTO-only ProductClient implementation。
/// 每次呼叫在 await 前複製並驗證 deployment/server routing 與 contact locator，再送出唯一固定 operation/contact-only map。
/// 只有 exact operation ID、exact response kind 與 non-null membership branch 能進入 mapping；任何違約都在唯讀結果發佈
/// 前 fail closed。client 不保留 request、profile、contact、response 或 DTO，故 singleton 不會造成 session 或記憶體洩漏。
/// </summary>
public sealed class AppNamedMembershipReadClient : IAppNamedMembershipReadClient
{
    private const string CapabilityOperationId = OperationIds.ListMembershipRetrieveAppNamedByContact;
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<AppNamedMembershipReadClient> _logger;

    /// <summary>
    /// 建立不含 request-specific 狀態或外部資源的 membership client。
    /// executor/logger 由 DI composition root 擁有；建構式不讀取設定、不解析 profile、不建立 client／lease、不註冊取消、
    /// 不啟動背景工作，也不保存使用者資料。因此此類別可安全以 singleton 使用，而 cleanup 仍集中於 executor owner。
    /// </summary>
    /// <param name="executor">唯一可執行固定 operation，並擁有 transport、connector、lease 與 cleanup 的下游邊界。</param>
    /// <param name="logger">只可輸出固定去識別化事件，不得輸出 contact、profile、workload、名單資料或上游錯誤細節。</param>
    public AppNamedMembershipReadClient(
        IDynamicsOperationExecutor executor,
        ILogger<AppNamedMembershipReadClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 以已授權 contact 執行唯一 membership read，並建立獨立、不可變的 DTO collection。
    /// 所有 required input 都在 executor I/O 前正規化為短命 local scalar；只把 contact GUID 寫入 ordinal read-only map，
    /// IdempotencyKey 固定為 null。取消 token 不被捕捉、替換、linked 或註冊，因此 cancellation/fault/timeout 後的 transport
    /// 淘汰與 permit/lease 釋放仍由 executor 的單一資源 owner 決定性執行；client 既不 retry 也不 fallback。
    /// </summary>
    /// <param name="request">包含 deployment profile、server workload 和已授權 contact locator 的純量 request。</param>
    /// <param name="cancellationToken">必須原樣傳遞給 executor 的目前 request cancellation token。</param>
    /// <returns>由新 DTO 與 ReadOnlyCollection 建立、不可暴露 backing array 的目前 request 結果。</returns>
    public async Task<IReadOnlyList<AppNamedMembershipRecordDto>> RetrieveAppNamedMembershipsByContactAsync(
        AppNamedMembershipReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 不可在 await 後讀取 caller 傳入的 reference 型別；先在目前 stack 建立已驗證 local scalar，才能避免未來
        // 可變 request wrapper、singleton field 或 closure 把 A 的 routing/contact 留給 B。這些 locals 不會被快取或保存。
        var profileAlias = RequireRoutingValue(request.ProfileAlias, nameof(request.ProfileAlias));
        var workloadSubjectId = RequireRoutingValue(request.WorkloadSubjectId, nameof(request.WorkloadSubjectId));
        var contactId = request.ContactId;
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("ContactId is required.", nameof(request.ContactId));
        }

        var parameters = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId
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
            _logger.LogWarning("App-named membership read failed.");
            throw new InvalidOperationException("App-named membership read failed.");
        }

        var data = execution.Data;
        if (data is null ||
            !string.Equals(data.OperationId, CapabilityOperationId, StringComparison.Ordinal) ||
            data.ResponseKind != OperationResponseKind.AppNamedMembershipRecords ||
            data.AppNamedMembershipRecords is null)
        {
            throw new InvalidOperationException(
                "App-named membership response does not match the requested operation contract.");
        }

        var seenListIds = new HashSet<Guid>();
        var copiedRows = new List<AppNamedMembershipRecordDto>(data.AppNamedMembershipRecords.Count);
        foreach (var record in data.AppNamedMembershipRecords)
        {
            copiedRows.Add(MapRecord(record, seenListIds));
        }

        var publishedRows = new ReadOnlyCollection<AppNamedMembershipRecordDto>(copiedRows);
        _logger.LogInformation("App-named membership read completed.");
        return publishedRows;
    }

    /// <summary>
    /// 驗證並複製 deployment/server-owned routing scalar。
    /// 空白輸入在 executor、profile resolution、connector allocation 或 outbound I/O 前失敗；回傳 local string copy，
    /// 但不將其寫進 log、cache、static field、session、task 或 background closure，故下一個 request 無法重用它。
    /// </summary>
    /// <param name="value">由 deployment 或 server service 提供的 profile/workload 值。</param>
    /// <param name="parameterName">公開 API 例外所用的參數名稱，永遠不包含實際 routing 值。</param>
    /// <returns>已修剪、非空白且只屬於目前呼叫的 routing scalar。</returns>
    private static string RequireRoutingValue(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A required routing value is missing.", parameterName);
        }

        return new string(value.Trim().AsSpan());
    }

    /// <summary>
    /// 將一筆封閉 membership wire row 防禦性複製為產品 DTO。
    /// null、空 list GUID 或同一 response 的 duplicate GUID 都代表資料面違約，立即在 publish 前 fail closed；HashSet 僅在
    /// 當前方法存在，完成或例外後可回收，絕不成為跨 request identity cache。nullable name 會複製新字串，不會持有 wire row。
    /// </summary>
    /// <param name="record">由 exact membership response branch 提供的純量 wire row。</param>
    /// <param name="seenListIds">目前 request 私有、用來拒絕 duplicate identity 的暫存集合。</param>
    /// <returns>不引用 wire record 或來源 collection 的新 DTO。</returns>
    private static AppNamedMembershipRecordDto MapRecord(
        AppNamedMembershipRecord record,
        ISet<Guid> seenListIds)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ListId == Guid.Empty || !seenListIds.Add(record.ListId))
        {
            throw new InvalidOperationException("App-named membership response contains an invalid list identity.");
        }

        return new AppNamedMembershipRecordDto
        {
            ListId = record.ListId,
            ListName = record.ListName is null ? null : new string(record.ListName.AsSpan())
        };
    }
}
