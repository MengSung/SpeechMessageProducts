// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/RuntimeHealth/RuntimeHealthWhoAmIClient.cs
// 目的：把既有固定 runtime.health.whoami executor response 轉換成 bounded、immutable ProductClient DTO。
//
// 安全與生命週期邊界：
// - singleton 只保存 DI-owned IDynamicsOperationExecutor；不保存 request、profile、workload、GUID、response、
//   Session、principal、cookie、credential、cache、timer、subscription、Task 或 background state。
// - executor 是 HTTP/Data8 connector、lease、permit、stream、buffer、timeout/cancellation/fault cleanup 的唯一 owner；
//   此 client 不建立 transport、retry、fallback、Entity 或 IDisposable 資源。
// - 每次呼叫在 await 前驗證並複製 routing scalar，固定 operation/CE/branch，任一不一致或 partial identity 均
//   fail closed，絕不發布 raw executor error 或跨 profile 的上一筆結果。
// ============================================================================

using System.Collections.ObjectModel;
using System.Text;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ProductClient.RuntimeHealth;

/// <summary>
/// 實作 ORG-CALL-00003 的 stateless runtime health ProductClient。
/// fixed operation 的 request 沒有業務參數或 idempotency key；profile/workload 仍必須由 deployment/host
/// composition 決定。此類別不接線 ChurchReport consumer 或 feature gate，因此它只補齊產品 DTO boundary，
/// 不能被視為 CE dispatch、consumer migration、ToolUtility removal、P7.5/P8 readiness 或 traffic evidence。
/// </summary>
public sealed class RuntimeHealthWhoAmIClient : IRuntimeHealthWhoAmIClient
{
    private const string CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI;
    private const string RequiredCeVersion = "9.1";
    private const int MaximumProfileAliasBytes = 128;
    private const int MaximumWorkloadSubjectBytes = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // 此空 map 是不含使用者、profile 或 request data 的 immutable metadata；ReadOnlyDictionary 透過
    // IDictionary 亦拒絕 mutation。所有 per-call routing scalar 都另建 OperationExecutionRequest，不能放進 static。
    private static readonly IReadOnlyDictionary<string, object?> EmptyParameters =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));

    private readonly IDynamicsOperationExecutor _executor;

    /// <summary>
    /// 建立只依賴既有 executor 的無狀態 health client。建構式不解析設定、不建立 HttpClient、不取得 connector/lease、
    /// 不註冊 cancellation callback，亦不讀寫 Session/cache；composition root 與 executor 繼續擁有 transport 的
    /// bounded lifetime、fault eviction、drain 與 deterministic cleanup，因此 singleton 不會跨 request 保留身份資料。
    /// </summary>
    /// <param name="executor">唯一可派送固定 operation 並擁有所有下游資源生命週期的 executor。</param>
    public RuntimeHealthWhoAmIClient(IDynamicsOperationExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <summary>
    /// 以固定 <c>runtime.health.whoami</c> operation 將 deployment-owned routing scalar 派送給既有 executor，
    /// 並只在回應精確符合 CE 9.1、WhoAmI branch 與三個非空 GUID 時建立新的 immutable identity DTO。方法不保存
    /// profile、workload、response 或 GUID；取消 token 不建立 linked source 而原樣傳遞，timeout、fault、branch 不符或
    /// partial identity 均以去識別化的固定例外 fail closed，不 retry、不 fallback，也不取得 transport 資源所有權。
    /// </summary>
    /// <param name="profileAlias">已由 deployment composition 驗證的 profile alias；會在 dispatch 前以嚴格 UTF-8 byte budget 驗證、修剪及複製。</param>
    /// <param name="workloadSubjectId">已由 host policy 驗證的 workload subject；會在 dispatch 前以嚴格 UTF-8 byte budget 驗證、修剪及複製。</param>
    /// <param name="cancellationToken">目前呼叫生命週期的取消 token；client 不註冊 callback、不延長其生命週期，並原樣交給 executor。</param>
    /// <returns>只含本次已驗證三 GUID scalar 的新 DTO；不引用 executor response 或任何 CRM／transport graph。</returns>
    /// <exception cref="ArgumentException">profile 或 workload 空白、含無效 UTF-8 surrogate，或超出其固定 byte budget 時擲回。</exception>
    /// <exception cref="InvalidOperationException">executor 未成功或回應不符合固定 operation contract 時擲回，且不含上游原始細節。</exception>
    public async Task<RuntimeHealthWhoAmIIdentityDto> CheckAsync(
        string profileAlias,
        string workloadSubjectId,
        CancellationToken cancellationToken = default)
    {
        // 在第一個 await 前將兩個 routing scalar 驗證並複製成 request-local 值。這避免未來呼叫端若以可變
        // wrapper 建構 request，continuation 仍讀到另一個 request 的 profile/workload；client 不保存這些局部值。
        var normalizedProfileAlias = RequireBoundedRoutingValue(
            profileAlias,
            nameof(profileAlias),
            MaximumProfileAliasBytes);
        var normalizedWorkloadSubjectId = RequireBoundedRoutingValue(
            workloadSubjectId,
            nameof(workloadSubjectId),
            MaximumWorkloadSubjectBytes);

        var execution = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = normalizedProfileAlias,
            WorkloadSubjectId = normalizedWorkloadSubjectId,
            CapabilityOperationId = CapabilityOperationId,
            IdempotencyKey = null,
            Parameters = EmptyParameters
        }, cancellationToken).ConfigureAwait(false);

        if (!execution.Succeeded)
        {
            // executor 的 ErrorMessage 可能含上游 transport diagnostics；產品邊界只發布固定分類，且不 retry
            // 已知或不確定的 failure。lease/client fault eviction 與 cleanup 仍完整留在 executor owner。
            throw new InvalidOperationException("Runtime health identity check failed.");
        }

        var data = execution.Data;
        if (data is null ||
            !string.Equals(data.OperationId, CapabilityOperationId, StringComparison.Ordinal) ||
            !string.Equals(data.CeVersion, RequiredCeVersion, StringComparison.Ordinal) ||
            data.ResponseKind != OperationResponseKind.WhoAmI ||
            data.WhoAmI is null ||
            data.WhoAmI.UserId is not Guid userId || userId == Guid.Empty ||
            data.WhoAmI.BusinessUnitId is not Guid businessUnitId || businessUnitId == Guid.Empty ||
            data.WhoAmI.OrganizationId is not Guid organizationId || organizationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Runtime health identity response does not match the requested operation contract.");
        }

        // 建構式只接收三個 validated value-type scalar；它不保存 OperationResponseData 或 WhoAmIResponseData
        // reference，讓 executor response 在目前 await scope 外沒有產品端 retained graph。
        return new RuntimeHealthWhoAmIIdentityDto(userId, businessUnitId, organizationId);
    }

    /// <summary>
    /// 驗證、修剪並複製 deployment/server-owned routing scalar。空白、invalid UTF-8 surrogate 或超過 byte budget
    /// 的輸入必須在 executor、profile resolution、connector/lease allocation 或任何外部 I/O 前拒絕；例外不含
    /// 原始 scalar，避免把 profile/workload 放入 log 或回應。回傳值只屬於目前 invocation，不進 static/cache。
    /// </summary>
    /// <param name="value">待驗證的 deployment-owned profile 或 workload scalar。</param>
    /// <param name="parameterName">公開 exception 的參數名稱，不含實際值。</param>
    /// <param name="maximumBytes">嚴格 UTF-8 encoding 下允許的最大 byte 數。</param>
    /// <returns>修剪且複製的 request-local routing scalar。</returns>
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
