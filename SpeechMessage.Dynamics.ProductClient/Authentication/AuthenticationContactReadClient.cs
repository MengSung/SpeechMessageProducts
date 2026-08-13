// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/Authentication/AuthenticationContactReadClient.cs
// 用途：將 ORG-CALL-00055／00056 的唯一 lookup scalar 映射到 server-owned、DTO-only 的 Dynamics read boundary。
//
// 安全與生命週期邊界：
// 1. 類別是 stateless singleton，只保存 DI-owned executor/logger；絕不保存 lookup、contact、result、profile、
//    workload、credential、token、cookie、Session、HttpContext、cache、timer、queue 或背景工作。
// 2. input 在 executor 前嚴格 trim、UTF-8 限制；每次 dispatch 新建 ordinal dictionary/request，每次 Found
//    新建 DTO/result。executor 獨自擁有 HTTP、Data8 lease、stream、buffer、client 與取消清理責任。
// 3. 不 retry、不 fallback 至 legacy SDK。取消不攔截且原樣傳入 executor；上游 fault、mismatch 或 secret 分類
//    僅轉為固定去識別化狀態，不回顯 exception、endpoint、raw response 或任何秘密。
// ============================================================================

using System.Text;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.Authentication;

/// <summary>
/// P7.4 authentication contact read 的 stateless typed client。此類別只實作 disabled-by-default 的 local-only
/// consumer boundary；它不建立 deployment gate、host、HTTP handler、pool、Session 或登入流程，這些責任仍由
/// composition root 與未來獨立的 credential/authorization/session migration 擁有。
/// </summary>
public sealed class AuthenticationContactReadClient : IAuthenticationContactReadClient
{
    private const int MaximumProfileAliasBytes = 128;
    private const int MaximumWorkloadSubjectBytes = 256;
    private const int MaximumLookupValueBytes = 512;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<AuthenticationContactReadClient> _logger;

    /// <summary>
    /// 建立不擁有 transport 的 typed client。executor 與 logger 的 lifecycle 均由 DI composition root 擁有；
    /// constructor 不讀取設定、建立 provider/handler/client/pool，亦不配置可釋放資源或保留跨使用者資料。
    /// </summary>
    /// <param name="executor">唯一可執行 server-owned operation 的下游邊界。</param>
    /// <param name="logger">只記錄固定 operation/status，絕不寫入 lookup、contact、秘密或 transport detail。</param>
    public AuthenticationContactReadClient(
        IDynamicsOperationExecutor executor,
        ILogger<AuthenticationContactReadClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<AuthenticationContactReadResult> RetrieveByAccountAsync(
        string profileAlias,
        string workloadSubjectId,
        string accountLookupValue,
        CancellationToken cancellationToken = default)
        => RetrieveAsync(
            profileAlias,
            workloadSubjectId,
            accountLookupValue,
            OperationIds.AuthenticationContactRetrieveByAccount,
            "accountLookupValue",
            cancellationToken);

    /// <inheritdoc />
    public Task<AuthenticationContactReadResult> RetrieveByLineIdAsync(
        string profileAlias,
        string workloadSubjectId,
        string lineIdLookupValue,
        CancellationToken cancellationToken = default)
        => RetrieveAsync(
            profileAlias,
            workloadSubjectId,
            lineIdLookupValue,
            OperationIds.AuthenticationContactRetrieveByLineId,
            "lineIdLookupValue",
            cancellationToken);

    /// <summary>
    /// 執行其中一個 compile-time 固定 authentication capability。lookup 的 validation 必須早於 profile/workload
    /// normalization 和 executor dispatch，讓空白、malformed 或 oversized input 不會觸發 host、connector、pool、
    /// lease 或 CE I/O。每次結果只在 method stack 內持有，完成後不會被 singleton 保留。
    /// </summary>
    /// <param name="profileAlias">由 deployment/authorization 層提供的 profile alias。</param>
    /// <param name="workloadSubjectId">由伺服器推導的 workload subject。</param>
    /// <param name="lookupValue">唯一的 caller lookup scalar。</param>
    /// <param name="operationId">僅由兩個 public wrapper 固定選擇的 server-owned capability。</param>
    /// <param name="lookupParameterName">與 capability 一對一對應的 registry parameter 名稱。</param>
    /// <param name="cancellationToken">原樣傳遞給 executor，且不在此 client 註冊或保存。</param>
    /// <returns>新的 immutable found 或 fail-closed result。</returns>
    private async Task<AuthenticationContactReadResult> RetrieveAsync(
        string profileAlias,
        string workloadSubjectId,
        string? lookupValue,
        string operationId,
        string lookupParameterName,
        CancellationToken cancellationToken)
    {
        var normalizedLookupValue = NormalizeLookupValue(lookupValue);
        if (normalizedLookupValue is null)
        {
            return AuthenticationContactReadResult.Failure(AuthenticationContactReadStatus.InvalidInput);
        }

        var normalizedProfileAlias = NormalizeRequiredText(
            profileAlias,
            nameof(profileAlias),
            MaximumProfileAliasBytes);
        var normalizedWorkloadSubjectId = NormalizeRequiredText(
            workloadSubjectId,
            nameof(workloadSubjectId),
            MaximumWorkloadSubjectBytes);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [lookupParameterName] = normalizedLookupValue
        };

        OperationExecutionResult execution;
        try
        {
            execution = await _executor.ExecuteAsync(new OperationExecutionRequest
            {
                ProfileAlias = normalizedProfileAlias,
                WorkloadSubjectId = normalizedWorkloadSubjectId,
                CapabilityOperationId = operationId,
                IdempotencyKey = null,
                Parameters = parameters
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消必須維持原例外與 token 語意；不可轉為 success/failure result、重試或借用另一 request 的狀態。
            throw;
        }

        var result = MapExecutionResult(execution, operationId);
        _logger.LogInformation(
            "Authentication contact read {OperationId} completed with {Status}.",
            operationId,
            result.Status);
        return result;
    }

    /// <summary>
    /// 將受控 executor outcome 映射為固定 classification。任何 failed executor response、null data、operation/
    /// discriminator mismatch 或未預期 classification 都不攜帶上游內容，而是安全地映射為 ProfileUnavailable；
    /// 唯一 Safe branch 依列數精確分成 NotFound、Found、Ambiguous，絕不猜選第一筆。
    /// </summary>
    /// <param name="execution">只在當次呼叫堆疊使用的封閉 executor result。</param>
    /// <param name="expectedOperationId">目前 public method 所固定的 capability。</param>
    /// <returns>新建、去識別化且不含 raw fault 的 immutable result。</returns>
    private static AuthenticationContactReadResult MapExecutionResult(
        OperationExecutionResult execution,
        string expectedOperationId)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (!execution.Succeeded || execution.Data is null)
        {
            return AuthenticationContactReadResult.Failure(AuthenticationContactReadStatus.ProfileUnavailable);
        }

        var data = execution.Data;
        if (data.ResponseKind != OperationResponseKind.AuthenticationContactReadRecords)
        {
            return AuthenticationContactReadResult.Failure(AuthenticationContactReadStatus.ProfileUnavailable);
        }

        if (data.AuthenticationContactReadSafetyClassification ==
            AuthenticationContactReadSafetyClassification.SecretPresent)
        {
            return AuthenticationContactReadResult.Failure(AuthenticationContactReadStatus.SecretPresent);
        }

        if (data.AuthenticationContactReadSafetyClassification != AuthenticationContactReadSafetyClassification.Safe ||
            data.AuthenticationContactReadRecords is null)
        {
            return AuthenticationContactReadResult.Failure(AuthenticationContactReadStatus.ProfileUnavailable);
        }

        // 先確認 capability correlation，再解讀資料列基數。若另一 operation 的空集合被映射為 NotFound，或其
        // 多筆集合被映射為 Ambiguous，呼叫端就能從錯配 transport 回應取得不屬於本 operation 的資料品質訊號。
        // correlation 不符一律收斂成 ProfileUnavailable；只有已驗證屬於此固定 operation 的封閉 branch 才能
        // 發布 zero／one／many 分類，且整個判斷僅使用目前 request 的 immutable envelope，不保存任何結果。
        if (!string.Equals(data.OperationId, expectedOperationId, StringComparison.Ordinal))
        {
            return AuthenticationContactReadResult.Failure(AuthenticationContactReadStatus.ProfileUnavailable);
        }

        return data.AuthenticationContactReadRecords.Count switch
        {
            0 => AuthenticationContactReadResult.Failure(AuthenticationContactReadStatus.NotFound),
            1 => AuthenticationContactReadResult.Found(MapContact(data.AuthenticationContactReadRecords[0])),
            _ => AuthenticationContactReadResult.Failure(AuthenticationContactReadStatus.Ambiguous)
        };
    }

    /// <summary>
    /// 從已驗證的 immutable wire record 建立新的 DTO。這個顯式欄位 copy 防止 result 與 wire record reference
    /// 被另一層重用，也使 ProductClient API 永遠不暴露 CRM Entity、秘密、安全分類以外的資料或 transport owner。
    /// </summary>
    /// <param name="record">已由 <see cref="OperationResponseData"/> 建構時驗證的唯一安全 record。</param>
    /// <returns>僅由目前 result 擁有的新 immutable DTO。</returns>
    private static AuthenticationContactReadDto MapContact(AuthenticationContactReadRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new AuthenticationContactReadDto
        {
            ContactId = record.ContactId,
            AccountLocator = string.Concat(record.AccountLocator),
            DisplayName = string.Concat(record.DisplayName),
            IsActive = record.IsActive
        };
    }

    /// <summary>
    /// 驗證並正規化唯一 lookup value。null/blank、無效 surrogate 或超過 512 UTF-8 bytes 的值一律回傳 null，
    /// 讓 public path 在 executor 前發布 InvalidInput；不記錄原值、不將其放入 static/cache，亦不嘗試 fallback。
    /// </summary>
    /// <param name="value">呼叫端提供的單一 lookup scalar。</param>
    /// <returns>trim 後的 bounded UTF-8 lookup，或 null 表示 invalid input。</returns>
    private static string? NormalizeLookupValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        try
        {
            return StrictUtf8.GetByteCount(normalized) <= MaximumLookupValueBytes ? normalized : null;
        }
        catch (EncoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// 驗證 deployment-owned profile/workload scalar。這兩個值不是 browser authority；當組合根遺漏或提供
    /// 無效設定時，呼叫在 executor 前明確失敗，不會解析其他 profile、建立 fallback client 或保留錯誤內容。
    /// </summary>
    /// <param name="value">由 composition/authorization 層傳入的 routing scalar。</param>
    /// <param name="parameterName">僅供不含實際值的固定錯誤訊息使用。</param>
    /// <param name="maximumBytes">對應 scalar 的最大嚴格 UTF-8 byte 數。</param>
    /// <returns>trim 後的 bounded routing scalar。</returns>
    private static string NormalizeRequiredText(string? value, string parameterName, int maximumBytes)
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
}
