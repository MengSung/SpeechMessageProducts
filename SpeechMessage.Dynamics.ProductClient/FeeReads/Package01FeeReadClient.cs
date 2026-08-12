// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/FeeReads/Package01FeeReadClient.cs
// 用途：提供 Package 1 費用與儲存課程的產品 DTO 讀取介面。
//
// 安全與生命週期界線：
// - 本類別只消費 Abstractions 定義的 OperationResponseData，不解析 CRM/OData JSON。
// - Gateway/Embedded executor 擁有 HTTP、串流、緩衝區、權杖與上游分頁資源；本類別不保存它們。
// - 每一筆非 null 回應都必須和本次 capability operation 與預期 branch 相符，否則 fail-closed。
// ============================================================================

using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.Models;

namespace SpeechMessage.Dynamics.ProductClient.FeeReads;

/// <summary>
/// 將 Package 1 已封閉的 Gateway/Embedded 回應轉換成產品公開 DTO。
/// 此類別刻意不認識 CRM 實體欄位、OData annotation、continuation URL 或原始 money/lookup 格式；
/// 這些上游相容性與資源清理責任只能由 Gateway connector 在受限請求範圍內擁有。
/// </summary>
public sealed class Package01FeeReadClient : IPackage01FeeReadClient
{
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<Package01FeeReadClient> _logger;

    /// <summary>
    /// 建立 Package 1 產品端讀取器。executor 是唯一可執行 operation 的邊界 owner，而 logger
    /// 僅接收安全的 operation ID、列數與 profile alias；建構式不快取請求、回應、權杖或連線，
    /// 因此不同產品請求不會透過此類別共享可變資料。
    /// </summary>
    public Package01FeeReadClient(
        IDynamicsOperationExecutor executor,
        ILogger<Package01FeeReadClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 讀取指定聯絡人與日期區間的奉獻費用。呼叫端只能提供 registry 定義的具型別參數；
    /// 方法不接受 FetchXML、OData filter、CRM URL 或 response projection，並把取消權原封不動
    /// 交給 executor，使 HTTP 與上游資源仍由其 request scope 依序停止與釋放。
    /// </summary>
    public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactDateRangeAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        DateTime startDate,
        DateTime endDate,
        string? contactName = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["contactId"] = contactId,
            ["startDate"] = startDate,
            ["endDate"] = endDate
        };
        if (!string.IsNullOrWhiteSpace(contactName))
        {
            parameters["contactName"] = contactName;
        }

        return ExecuteAndMapFeesAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.FeeDedicationRetrieveByContactDateRange,
            parameters,
            cancellationToken);
    }

    /// <summary>
    /// 讀取指定聯絡人的奉獻費用。所有查詢語意都由 capability operation 固定，產品端只接收
    /// 封閉 fee branch；這可避免不同 caller 透過 profile、查詢文字或 CRM 欄位選擇改變資料邊界。
    /// </summary>
    public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        string? contactName = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["contactId"] = contactId
        };
        if (!string.IsNullOrWhiteSpace(contactName))
        {
            parameters["contactName"] = contactName;
        }

        return ExecuteAndMapFeesAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.FeeDedicationRetrieveByContact,
            parameters,
            cancellationToken);
    }

    /// <summary>
    /// 讀取指定奉獻預約與已繳期間的費用。期間是 server-owned template 的具型別值，空白值會在
    /// 發送前被拒絕；這可避免建立沒有界限的查詢或讓無效輸入進入 executor 的佇列與稽核生命週期。
    /// </summary>
    public Task<IReadOnlyList<FeeRecordDto>> RetrieveFeesByDedicationPeriodAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid dedicationBookingId,
        string paidPeriod,
        string? dedicationBookingName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paidPeriod))
        {
            throw new ArgumentException("paidPeriod is required.", nameof(paidPeriod));
        }

        var parameters = new Dictionary<string, object?>
        {
            ["dedicationBookingId"] = dedicationBookingId,
            ["paidPeriod"] = paidPeriod
        };
        if (!string.IsNullOrWhiteSpace(dedicationBookingName))
        {
            parameters["dedicationBookingName"] = dedicationBookingName;
        }

        return ExecuteAndMapFeesAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.FeesRetrieveByDedicationPeriod,
            parameters,
            cancellationToken);
    }

    /// <summary>
    /// 讀取費用編輯器所需的儲存課程列。結果只可來自 stor-lesson branch，避免費用 branch 或
    /// metadata branch 被誤投影到課程 UI；呼叫端取消時不會在本類別建立背景工作或留下資源。
    /// </summary>
    public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveFeeEditorRowsByDiscipleLessonAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid discipleLessonId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["discipleLessonId"] = discipleLessonId
        };

        return ExecuteAndMapStorLessonsAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.FeesEditorLoadByDiscipleLesson,
            parameters,
            cancellationToken);
    }

    /// <summary>
    /// 讀取指定聯絡人的儲存課程列。聯絡人名稱僅作為已登錄參數，不能成為 CRM 欄位、URL 或
    /// continuation 的替代輸入；回應仍須先通過 operation/branch 對應驗證才可建立產品 DTO。
    /// </summary>
    public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByContactAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        string? contactName = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["contactId"] = contactId
        };
        if (!string.IsNullOrWhiteSpace(contactName))
        {
            parameters["contactName"] = contactName;
        }

        return ExecuteAndMapStorLessonsAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.LessonsStorRetrieveByContact,
            parameters,
            cancellationToken);
    }

    /// <summary>
    /// 讀取指定門徒課程的儲存課程列。課程名稱若存在只會以具型別參數傳遞，產品端不會自行
    /// 擴充查詢或重新解析上游 lookup；executor 完成或取消後沒有任何本類別持有的 response 資源。
    /// </summary>
    public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByDiscipleLessonAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid discipleLessonId,
        string? lessonName = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["discipleLessonId"] = discipleLessonId
        };
        if (!string.IsNullOrWhiteSpace(lessonName))
        {
            parameters["lessonName"] = lessonName;
        }

        return ExecuteAndMapStorLessonsAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.LessonsStorRetrieveByDiscipleLesson,
            parameters,
            cancellationToken);
    }

    /// <summary>
    /// 執行費用 capability 並在 executor 回傳後立即驗證封閉 branch，再建立新的短生命週期 DTO
    /// 陣列。任何 operation ID 或 response kind 不一致都會在映射前失敗，避免錯誤快取、重試
    /// 或跨租戶回應被當成費用資料；本方法本身不擁有 HTTP、stream 或 pooled buffer。
    /// </summary>
    private async Task<IReadOnlyList<FeeRecordDto>> ExecuteAndMapFeesAsync(
        string profileAlias,
        string workloadSubjectId,
        string capabilityOperationId,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(
            profileAlias,
            workloadSubjectId,
            capabilityOperationId,
            parameters,
            cancellationToken).ConfigureAwait(false);

        var rows = MapFeeRecords(result.Data, capabilityOperationId);
        _logger.LogInformation(
            "Package01 fee-read {OperationId} returned {Count} rows for profile {ProfileAlias}",
            capabilityOperationId,
            rows.Count,
            profileAlias);
        return rows;
    }

    /// <summary>
    /// 執行 stor-lesson capability 並以與費用路徑相同的 fail-closed 規則驗證回應。資料只在
    /// 封閉 branch 與請求 identity 相符時才會映射，確保課程個資不會因 Gateway 回應錯配流入
    /// 其他產品 DTO，也不會在此層建立背景重試、計時器或共享快取。
    /// </summary>
    private async Task<IReadOnlyList<StorLessonRecordDto>> ExecuteAndMapStorLessonsAsync(
        string profileAlias,
        string workloadSubjectId,
        string capabilityOperationId,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(
            profileAlias,
            workloadSubjectId,
            capabilityOperationId,
            parameters,
            cancellationToken).ConfigureAwait(false);

        var rows = MapStorLessonRecords(result.Data, capabilityOperationId);
        _logger.LogInformation(
            "Package01 stor-lesson-read {OperationId} returned {Count} rows for profile {ProfileAlias}",
            capabilityOperationId,
            rows.Count,
            profileAlias);
        return rows;
    }

    /// <summary>
    /// 驗證產品端輸入後委派給 executor。profile alias 與工作負載主體為空時立即拒絕，避免無效
    /// 請求進入 Gateway/Embedded 的認證、佇列或稽核資源；executor 回報失敗時保留既有的例外
    /// 行為，但不加入上游 payload、端點、憑證或權杖到例外訊息。
    /// </summary>
    private async Task<OperationExecutionResult> ExecuteAsync(
        string profileAlias,
        string workloadSubjectId,
        string capabilityOperationId,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            throw new ArgumentException("profileAlias is required.", nameof(profileAlias));
        }

        if (string.IsNullOrWhiteSpace(workloadSubjectId))
        {
            throw new ArgumentException("workloadSubjectId is required.", nameof(workloadSubjectId));
        }

        var result = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = profileAlias.Trim(),
            CapabilityOperationId = capabilityOperationId,
            WorkloadSubjectId = workloadSubjectId.Trim(),
            Parameters = parameters
        }, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Package 1 read failed: {result.ErrorCode} - {result.ErrorMessage}");
        }

        return result;
    }

    /// <summary>
    /// 將已選擇的 fee branch 一對一映射為公開費用 DTO。null data 是封閉契約允許的 no-data
    /// 成功並維持既有空集合語意；其餘情況必須先驗證 operation ID、response kind 與非 null
    /// branch，絕不接受或解析 value、formatted annotation、money wrapper 或其他 OData 資料。
    /// </summary>
    private static IReadOnlyList<FeeRecordDto> MapFeeRecords(
        OperationResponseData? data,
        string expectedOperationId)
    {
        if (data is null)
        {
            return Array.Empty<FeeRecordDto>();
        }

        EnsureExpectedResponse(
            data,
            expectedOperationId,
            OperationResponseKind.Package01FeeRecords,
            data.FeeRecords is not null);

        return data.FeeRecords!.Select(MapFeeRecord).ToArray();
    }

    /// <summary>
    /// 將已選擇的 stor-lesson branch 一對一映射為公開課程 DTO。null data 仍是合法空集合；
    /// 非 null 回應若宣告費用、WhoAmI、Unsupported 或缺少 stor branch，便在任何 DTO 產生前
    /// 以受控例外停止，防止資料分類與請求身分交叉污染。
    /// </summary>
    private static IReadOnlyList<StorLessonRecordDto> MapStorLessonRecords(
        OperationResponseData? data,
        string expectedOperationId)
    {
        if (data is null)
        {
            return Array.Empty<StorLessonRecordDto>();
        }

        EnsureExpectedResponse(
            data,
            expectedOperationId,
            OperationResponseKind.Package01StorLessonRecords,
            data.StorLessonRecords is not null);

        return data.StorLessonRecords!.Select(MapStorLessonRecord).ToArray();
    }

    /// <summary>
    /// 驗證回應所屬 capability、discriminator 與預期資料 branch 同時一致。這是 ProductClient
    /// 的最後一道結構性隔離閘門：即使 executor、重試或測試替身回傳有效但屬於另一操作的封閉
    /// 物件，也不得映射或保存；失敗訊息故意不含上游欄位、URL、token 或任何 response 本文。
    /// </summary>
    private static void EnsureExpectedResponse(
        OperationResponseData data,
        string expectedOperationId,
        OperationResponseKind expectedResponseKind,
        bool hasExpectedBranch)
    {
        if (!string.Equals(data.OperationId, expectedOperationId, StringComparison.Ordinal)
            || data.ResponseKind != expectedResponseKind
            || !hasExpectedBranch)
        {
            throw new InvalidOperationException(
                "Package 1 response does not match the requested operation contract.");
        }
    }

    /// <summary>
    /// 逐欄複製已由 Gateway 投影完成的費用記錄。所有 nullable 值與 Amount 的零值預設直接
    /// 沿用共用契約，避免產品端重新猜測 CRM 的日期、金額、選項集或 formatted-value 規則。
    /// </summary>
    private static FeeRecordDto MapFeeRecord(Package01FeeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new FeeRecordDto
        {
            FeeId = record.FeeId,
            CreatedOn = record.CreatedOn,
            PayDate = record.PayDate,
            Amount = record.Amount,
            PayWayOption = record.PayWayOption,
            PayWayLabel = record.PayWayLabel,
            CategoryLabel = record.CategoryLabel,
            Others = record.Others,
            PaidPeriod = record.PaidPeriod,
            Name = record.Name
        };
    }

    /// <summary>
    /// 逐欄複製已由 Gateway 投影完成的 stor-lesson 記錄。lookup、名稱與可選金額皆保持原值或
    /// null，且本方法不持有任何外部資源；完成後唯一留下的是呼叫端擁有的短生命週期產品 DTO。
    /// </summary>
    private static StorLessonRecordDto MapStorLessonRecord(Package01StorLessonRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new StorLessonRecordDto
        {
            StorLessonId = record.StorLessonId,
            ContactId = record.ContactId,
            DiscipleLessonId = record.DiscipleLessonId,
            CreatedOn = record.CreatedOn,
            PayDate = record.PayDate,
            CurrentComplete = record.CurrentComplete,
            ContactName = record.ContactName,
            ContactMobile = record.ContactMobile,
            DiscipleLessonName = record.DiscipleLessonName,
            ClassStartDate = record.ClassStartDate,
            StageName = record.StageName,
            FeeAmount = record.FeeAmount
        };
    }
}
