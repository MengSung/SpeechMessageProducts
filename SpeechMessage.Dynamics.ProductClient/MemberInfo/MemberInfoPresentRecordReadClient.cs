// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/MemberInfo/MemberInfoPresentRecordReadClient.cs
// 目的：將 ORG-CALL-00026 的封閉 executor response 轉換成獨立的 MemberInfo 出席紀錄 DTO 快照。
//
// 安全與生命週期邊界：
// - 此 stateless singleton 只保存 DI-owned executor/logger；不保存 request、profile、workload、contact、DTO、
//   response、token、Session、cache、timer、subscription、task 或背景工作，避免跨使用者/跨 profile 留存。
// - executor 是 HTTP、Data8 connector、lease、permit、stream、buffer、timeout/cancellation/fault cleanup 的唯一 owner；
//   client 不建立第二條 I/O 路徑、retry、fallback、Entity 或 IDisposable 資源。
// - 每次呼叫都在 await 前複製/驗證所有 routing scalar 和 contact GUID，並在 mapping 前驗證 exact response contract；
//   任一違約均 fail closed，絕不發布 partial results。
// ============================================================================

using System.Collections.ObjectModel;
using System.Text;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.MemberInfo;

/// <summary>
/// 實作 ORG-CALL-00026 的獨立、唯讀、DTO-only ProductClient。
/// 此類別不繼承或共用 <see cref="IPackage02ContactProfileClient"/> 的寫入/aggregate capability，讓 deployment
/// 的 present-read gate 只取得此固定 read surface。每次呼叫以新的 ordinal contact-only dictionary、operation
/// request、DTO list 與 read-only wrapper 完成；因此 A/B profile/workload 的資料不可能透過 singleton 的 mutable
/// state、上一次 response、cache 或 token 跨 request 泄漏。
/// </summary>
public sealed class MemberInfoPresentRecordReadClient : IMemberInfoPresentRecordReadClient
{
    private const string CapabilityOperationId = OperationIds.MemberInfoPresentRetrieveByContact;
    private const string RequiredCeVersion = "9.1";
    private const int MaximumProfileAliasBytes = 128;
    private const int MaximumWorkloadSubjectBytes = 256;
    private const int MaximumRecordTextCharacters = 512;
    private const int MaximumRecordTextBytes = MaximumRecordTextCharacters * 4;
    private const int MaximumRecords = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<MemberInfoPresentRecordReadClient> _logger;

    /// <summary>
    /// 建立不持有 request-specific 或外部資源的 ProductClient。
    /// executor/logger 的存活、dispose、handler/pool/lease ownership 均由 composition root 管理；建構式不讀取設定、
    /// 建立 HttpClient、解析 profile、啟動計時器、註冊 cancellation callback 或保存任何使用者/response 資料，故
    /// singleton 不會把一個 request 的 state 傳遞給下一個 request。
    /// </summary>
    /// <param name="executor">唯一可執行固定 operation 並擁有 transport/connector cleanup 的下游邊界。</param>
    /// <param name="logger">僅可記錄固定 operation 與安全列數；不得記錄 contact、文字、profile、token 或上游 detail。</param>
    public MemberInfoPresentRecordReadClient(
        IDynamicsOperationExecutor executor,
        ILogger<MemberInfoPresentRecordReadClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 以已授權 contact 執行唯一固定 read operation，並發佈獨立、不可變的 DTO snapshot。
    /// 所有 required scalar 都在 await 前驗證、修剪並複製；只有 <c>contactId</c> 會進入 ordinal read-only parameter
    /// map，IdempotencyKey 固定為 null。取消 token 不被捕捉、替換或註冊；fault/timeout/cancellation 時 executor 的
    /// 單一 owner 依其生命週期釋放外部資源，本 client 不 retry、不 fallback，也不留下未完成 task 或 partial list。
    /// </summary>
    /// <param name="request">包含 deployment-owned profile、server-chosen workload 與 controller-authorized contact 的 request。</param>
    /// <param name="cancellationToken">目前 request token，必須原樣傳遞給 executor。</param>
    /// <returns>由新 DTO 與 read-only wrapper 建立、不可轉型為 backing array 的當次結果。</returns>
    public async Task<IReadOnlyList<MemberInfoPresentRecordReadDto>> RetrievePresentRecordsByContactAsync(
        MemberInfoPresentRecordReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 所有 request scalar 必須在 await 前變成短命、已驗證的局部值；不可讓 client 於 continuation 讀取呼叫端
        // 可變 request/reference，也不可把 profile/workload/contact 保存到欄位、static/cache 或 background closure。
        var profileAlias = RequireBoundedRoutingValue(
            request.ProfileAlias,
            nameof(request.ProfileAlias),
            MaximumProfileAliasBytes);
        var workloadSubjectId = RequireBoundedRoutingValue(
            request.WorkloadSubjectId,
            nameof(request.WorkloadSubjectId),
            MaximumWorkloadSubjectBytes);
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
            _logger.LogWarning(
                "MemberInfo present-record read operation failed with {ErrorCode}.",
                execution.ErrorCode ?? "unknown");
            throw new InvalidOperationException("MemberInfo present-record read failed.");
        }

        var data = execution.Data;
        if (data is null ||
            !string.Equals(data.OperationId, CapabilityOperationId, StringComparison.Ordinal) ||
            !string.Equals(data.CeVersion, RequiredCeVersion, StringComparison.Ordinal) ||
            data.ResponseKind != OperationResponseKind.MemberInfoPresentRecordReadRecords ||
            data.MemberInfoPresentRecordReadRecords is null)
        {
            throw new InvalidOperationException(
                "MemberInfo present-record response does not match the requested operation contract.");
        }

        if (data.MemberInfoPresentRecordReadRecords.Count > MaximumRecords)
        {
            throw new InvalidOperationException("MemberInfo present-record response exceeds its bounded record count.");
        }

        var recordIds = new HashSet<Guid>();
        var copiedRecords = new List<MemberInfoPresentRecordReadDto>(data.MemberInfoPresentRecordReadRecords.Count);
        foreach (var record in data.MemberInfoPresentRecordReadRecords)
        {
            copiedRecords.Add(MapRecord(record, recordIds));
        }

        var publishedRecords = new ReadOnlyCollection<MemberInfoPresentRecordReadDto>(copiedRecords);
        _logger.LogInformation(
            "MemberInfo present-record read {OperationId} returned {Count} rows.",
            CapabilityOperationId,
            publishedRecords.Count);
        return publishedRecords;
    }

    /// <summary>
    /// 複製並驗證一筆封閉 wire record。驗證採 all-or-nothing：任何 null、空/重複 GUID、無效 UTF-8、超限文字
    /// 都在結果 collection 發佈前失敗，不能把先前已映射列回傳。HashSet 僅屬於目前方法呼叫，完成或例外後即可回收，
    /// 不成為跨 request identity cache；日期直接複製而不轉換，保留既有 legacy semantics。
    /// </summary>
    /// <param name="record">由 exact present-record response branch 提供的純量 wire row。</param>
    /// <param name="recordIds">目前 request 私有、用於偵測同一 response 重複 identity 的暫存集合。</param>
    /// <returns>不引用 wire record 或來源 collection 的新產品 DTO。</returns>
    private static MemberInfoPresentRecordReadDto MapRecord(
        MemberInfoPresentRecordReadRecord record,
        ISet<Guid> recordIds)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.PresentRecordId == Guid.Empty || !recordIds.Add(record.PresentRecordId))
        {
            throw new InvalidOperationException("MemberInfo present-record response contains an invalid record identity.");
        }

        var contactFullName = CopyBoundedOptionalText(record.ContactFullName);
        var prayItem = CopyBoundedOptionalText(record.PrayItem);
        return new MemberInfoPresentRecordReadDto
        {
            PresentRecordId = record.PresentRecordId,
            ContactFullName = contactFullName,
            SundayDate = record.SundayDate,
            Sunday = record.Sunday,
            SmallGroup = record.SmallGroup,
            PrayItem = prayItem
        };
    }

    /// <summary>
    /// 驗證並複製 deployment/server-owned routing scalar。空白、無效 surrogate 或超過固定 UTF-8 byte 上限的值
    /// 一律在 executor 前拒絕；回傳新的 string reference，避免一個 mutable/string-like
    /// caller wrapper 或未來 request 物件被 client continuation 重用。此 helper 不記錄值、不建立 cache 或資源 owner。
    /// </summary>
    /// <param name="value">由 deployment 或 server service 提供的 profile/workload scalar。</param>
    /// <param name="parameterName">公開 API 例外中的參數名稱，不含實際值。</param>
    /// <param name="maximumBytes">此 routing scalar 的嚴格 UTF-8 上限。</param>
    /// <returns>修剪、嚴格驗證且只屬於目前呼叫的 routing 字串。</returns>
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

    /// <summary>
    /// 驗證並複製 response 內可選的 bounded text scalar。null 不變；非 null 值不得被靜默 trim 或截斷，因為那會
    /// 改寫上游資料/legacy display 語意並可能掩蓋 connector schema/limit 缺陷。失敗關閉可確保沒有不受限文字
    /// 進入 DTO、JSON buffer、log 或另一個 request；此 helper 不保存或輸出文字內容。
    /// </summary>
    /// <param name="value">從 wire record 接收的可選文字。</param>
    /// <returns>同值的新字串 reference，或 null。</returns>
    private static string? CopyBoundedOptionalText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length > MaximumRecordTextCharacters)
        {
            throw new InvalidOperationException("MemberInfo present-record response contains oversized text.");
        }

        try
        {
            if (StrictUtf8.GetByteCount(value) > MaximumRecordTextBytes)
            {
                throw new InvalidOperationException("MemberInfo present-record response contains oversized text.");
            }
        }
        catch (EncoderFallbackException)
        {
            throw new InvalidOperationException("MemberInfo present-record response contains invalid text.");
        }

        return new string(value.AsSpan());
    }
}
