// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/FeeReads/Package01DedicationBookingReadClient.cs
// 用途：將封閉的 Package 1 認獻單 response branch 映射為 request-local DTO 快照。
//
// 此 singleton 不保存呼叫端資料、profile、workload、contact、response 或 DTO collection。所有可變資料都
// 在方法範圍內建立，交由 executor 完成 connector/HTTP/lease 的生命週期；operation、discriminator 與
// branch 任一不符即 fail closed，絕不將其他 capability 的資料發佈給目前呼叫端。
// ============================================================================

using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.Models;

namespace SpeechMessage.Dynamics.ProductClient.FeeReads;

/// <summary>
/// 實作 Package 1 認獻單的無狀態封閉讀取 client。
/// executor 與 logger 由 DI 擁有；本類別不建立或釋放 HTTP、CRM、Data8 connector、lease、stream、timer 或
/// cancellation registration。每次非同步呼叫只建立 request-local request、DTO 與唯讀 collection，避免跨使用者、
/// profile 或 workload 保留可變狀態。
/// </summary>
public sealed class Package01DedicationBookingReadClient : IPackage01DedicationBookingReadClient
{
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<Package01DedicationBookingReadClient> _logger;

    /// <summary>
    /// 建立認獻單讀取 client。
    /// 依賴項必須由 composition root 建立並具有明確生命週期；client 只保留 DI 擁有的無狀態 executor/logger
    /// 參考，不快取任何 request、認證、profile、資料列或 transport resource。
    /// </summary>
    /// <param name="executor">執行已登錄 Dynamics operation 並擁有 transport/lease 清理責任的元件。</param>
    /// <param name="logger">只記錄受限 operation 類別與筆數的 logger，不記錄 contact、credential 或 response 資料。</param>
    public Package01DedicationBookingReadClient(
        IDynamicsOperationExecutor executor,
        ILogger<Package01DedicationBookingReadClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 依已驗證的 contact 定位值執行唯一允許的認獻單讀取 operation。
    /// 方法只在目前呼叫建立參數字典與 DTO 快照，並原樣轉送取消權杖；executor 所擁有的 connector、lease、
    /// HTTP 與 transport 清理責任不會被本 client 接管或跨 request 保留。成功回應必須同時符合 operation ID、
    /// discriminator 與 dedicated-booking branch，否則在映射或發佈任何資料前 fail closed。
    /// </summary>
    /// <param name="profileAlias">部署端已驗證的 profile alias；空白值會在 outbound I/O 前遭拒絕。</param>
    /// <param name="workloadSubjectId">部署端已驗證的 workload subject；空白值會在 outbound I/O 前遭拒絕。</param>
    /// <param name="contactId">已由上層授權的 contact 定位值；空 GUID 不會送往 executor。</param>
    /// <param name="contactName">選用的非權威相容文字；只在非空白時附加，且不改變授權或 connector 選擇。</param>
    /// <param name="cancellationToken">呼叫端的取消權杖；不建立替代權杖、registration 或背景重試。</param>
    /// <returns>獨立 DTO 與唯讀包裝所組成的目前 request 結果快照。</returns>
    public async Task<IReadOnlyList<DedicationBookingRecordDto>> RetrieveDedicationBookingsByContactAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        string? contactName = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedProfileAlias = RequireNonEmpty(profileAlias, nameof(profileAlias));
        var normalizedWorkloadSubjectId = RequireNonEmpty(workloadSubjectId, nameof(workloadSubjectId));
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("contactId is required.", nameof(contactId));
        }

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contactId"] = contactId
        };
        if (!string.IsNullOrWhiteSpace(contactName))
        {
            parameters["contactName"] = contactName;
        }

        var execution = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = normalizedProfileAlias,
            CapabilityOperationId = OperationIds.PaymentsDedicationRetrieveByContact,
            WorkloadSubjectId = normalizedWorkloadSubjectId,
            Parameters = parameters
        }, cancellationToken).ConfigureAwait(false);

        if (!execution.Succeeded)
        {
            _logger.LogWarning(
                "Package01 dedication-booking read operation failed with {ErrorCode}.",
                execution.ErrorCode ?? "unknown");
            throw new InvalidOperationException("Package 1 dedication booking read failed.");
        }

        var data = execution.Data;
        if (data is null ||
            !string.Equals(
                data.OperationId,
                OperationIds.PaymentsDedicationRetrieveByContact,
                StringComparison.Ordinal) ||
            data.ResponseKind != OperationResponseKind.Package01DedicationBookingRecords ||
            data.DedicationBookingRecords is null)
        {
            throw new InvalidOperationException(
                "Package 1 dedication booking response does not match the requested operation contract.");
        }

        var mappedRows = data.DedicationBookingRecords
            .Select(MapRecord)
            .ToList();
        var publishedRows = new ReadOnlyCollection<DedicationBookingRecordDto>(mappedRows);

        _logger.LogInformation(
            "Package01 dedication-booking read {OperationId} returned {Count} rows for profile {ProfileAlias}.",
            OperationIds.PaymentsDedicationRetrieveByContact,
            publishedRows.Count,
            normalizedProfileAlias);
        return publishedRows;
    }

    /// <summary>
    /// 驗證並正規化 deployment-owned 路由值。
    /// 空白值在呼叫 executor、配置 connector 或建立任何外部 I/O 前拒絕；修剪後的字串只存在於目前方法範圍，
    /// 不會寫入 singleton 狀態、快取或背景工作，從而避免跨 request/profile 洩漏。
    /// </summary>
    /// <param name="value">需驗證的部署端路由值。</param>
    /// <param name="parameterName">例外中使用的原始參數名稱。</param>
    /// <returns>已修剪且非空白的 request-local 路由值。</returns>
    private static string RequireNonEmpty(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A required routing value is missing.", parameterName);
        }

        return value.Trim();
    }

    /// <summary>
    /// 將一筆封閉 wire record 防禦性複製為獨立 DTO。
    /// 這個映射不傳回來源 record，也不保留來源 collection；即使上游 envelope 或測試替身在建立回應後變動，
    /// 已發佈的 DTO 仍屬於目前 request。null wire row 視為違反 response contract 而失敗，不發佈部分結果。
    /// </summary>
    /// <param name="record">已由 dedicated-booking response branch 提供的單筆 wire record。</param>
    /// <returns>沒有 connector、Entity 或 collection 參考的獨立 DTO。</returns>
    private static DedicationBookingRecordDto MapRecord(Package01DedicationBookingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new DedicationBookingRecordDto
        {
            DedicationBookingId = record.DedicationBookingId,
            DedicationCategoryOption = record.DedicationCategoryOption,
            DedicationCategoryLabel = record.DedicationCategoryLabel,
            DedicationBookingStatusOption = record.DedicationBookingStatusOption,
            DedicationBookingStatusLabel = record.DedicationBookingStatusLabel,
            AmountPerStage = record.AmountPerStage,
            TotalStages = record.TotalStages,
            DedicationAmount = record.DedicationAmount,
            PaidPeriod = record.PaidPeriod,
            RollupPaidFee = record.RollupPaidFee,
            StartDate = record.StartDate,
            EndDate = record.EndDate
        };
    }
}
