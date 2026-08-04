// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs
// 用途：把受控產品 operation 從已解析 Profile 安全地路由到 Data8 generation-owned Connector Pool。
//
// 信任、隔離與生命週期契約：
// 1. 本執行器只接受產品可見的封閉 OperationExecutionRequest；請求不能攜帶 OrganizationId、endpoint、
//    ConnectorKind 或 Credential，這些資訊只由 IProfileResolver 的不可變部署快照提供。
// 2. 執行順序固定為 ProfileResolver -> IConnectorRouter -> Data8ConnectorPool。Pool 是唯一取得與釋放
//    Organization Admission permit、local slot 與 Data8 client 的 owner；本類別不複製、快取或 Dispose 它們。
// 3. 每次 Connector lease 的最長生命週期僅限本次 ExecuteAsync，並以 await using 在成功、取消、逾時、
//    例外或投影失敗時確定性歸還。不存在跨使用者、跨 Profile、跨 Organization 或跨 request 的 Session／
//    connection state 保留，也不建立 CTS、timer、background task 或可變 static state。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// Data8 的同程序受控 operation executor。
/// 此類別位於 ControlPlane 與 Pool 之間：它將 ProfileResolver 取得的 immutable generation snapshot 交給
/// Router，並只透過 lease 執行 SDK-free ConnectorOperation。它不公開 Data8 client，也不持有任何使用者、
/// credential、token、endpoint、OrganizationId、permit、request 或結果的長生命週期參考，因此可由
/// EmbeddedHostAdapter 與未來 Gateway composition root 共用，而不產生跨租戶或跨世代連線洩漏。
/// </summary>
public sealed class Data8ProfileOperationExecutor : IDynamicsOperationExecutor
{
    private const string ProfileNotFoundErrorCode = "profile.not-found";
    private const string ConnectorNotAvailableErrorCode = "connector.not-available";
    private const string OperationNotSupportedErrorCode = "operation.not-supported";
    private const string ConnectorFailureErrorCode = "connector.operation-failed";
    private const string InvalidResponseErrorCode = "connector.invalid-response";

    private readonly IProfileResolver _profileResolver;
    private readonly IConnectorRouter _connectorRouter;

    /// <summary>
    /// 建立不擁有外部資源的 Data8 executor。Resolver 與 Router 均由 host composition root 擁有；
    /// 它們可能連到可 drain 的 generation registry，但 executor 絕不自行 Dispose，避免一個產品 scope
    /// 提早終止其他 Profile 的 Pool 或 Admission manager。
    /// </summary>
    /// <param name="profileResolver">將固定 ProfileAlias 解析為 immutable generation snapshot 的部署端 resolver。</param>
    /// <param name="connectorRouter">只接受 resolver 輸出的 Data8 Profile 並回傳同 generation Pool 的 router。</param>
    public Data8ProfileOperationExecutor(
        IProfileResolver profileResolver,
        IConnectorRouter connectorRouter)
    {
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _connectorRouter = connectorRouter ?? throw new ArgumentNullException(nameof(connectorRouter));
    }

    /// <summary>
    /// 執行目前可由 Data8 Pool 安全投影的 WhoAmI capability。
    /// 在任何 await 前，本方法只讀取並投影 request 的有限 scalar，且解析部署端 Profile；返回的非同步路徑
    /// 不捕捉原始 request，避免 queue／pool wait 將 HttpContext、Session 或大型參數圖保留到請求範圍之外。
    /// Profile 不存在、ConnectorKind 非 Data8、Pool generation 未登錄、未知 capability 或非空 WhoAmI 參數
    /// 均 fail closed，並在取得 Permit 或 Client 前結束。
    /// </summary>
    /// <param name="request">已通過上游 RequestGuard 的封閉產品 operation；不得包含連線或認證資訊。</param>
    /// <param name="cancellationToken">由 request scope 擁有的取消訊號；只向 Pool／Client 傳遞且不被保存。</param>
    /// <returns>僅含安全 WhoAmI 純值投影的成功結果，或不含 transport detail 的固定錯誤分類。</returns>
    public Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_profileResolver.TryResolve(request.ProfileAlias, out var profile, out var resolutionError) ||
            profile is null)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                NormalizeProfileResolutionError(resolutionError),
                "The requested Dynamics profile is unavailable."));
        }

        if (profile.ConnectorKind != ConnectorKind.Data8)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                ConnectorNotAvailableErrorCode,
                "The resolved Dynamics profile does not use the Data8 connector."));
        }

        if (!IsSupportedWhoAmIRequest(request))
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                OperationNotSupportedErrorCode,
                "The requested Dynamics operation is not supported by the Data8 embedded executor."));
        }

        IConnectorPool pool;
        try
        {
            pool = _connectorRouter.Resolve(profile);
        }
        catch (NotSupportedException)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                ConnectorNotAvailableErrorCode,
                "No compatible Data8 connector is available for the resolved profile."));
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                ConnectorNotAvailableErrorCode,
                "No active Data8 pool is available for the resolved profile generation."));
        }
        catch (ObjectDisposedException)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                ConnectorNotAvailableErrorCode,
                "The resolved Data8 profile generation is draining."));
        }

        // 只把有界 scalar 複製到本次 connector operation；此後的 async state machine 不再捕捉原始 request。
        // Deadline 的唯一後續 owner 是 Pool／Lease，它們會建立並 Dispose 方法範圍的 deadline CTS。
        var operation = new ConnectorOperation
        {
            OperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = request.WorkloadSubjectId.Trim(),
            DeadlineUtc = DateTimeOffset.UtcNow.Add(profile.Operation.Timeout),
            EstimatedBytes = 256
        };
        return ExecuteWhoAmIAsync(pool, profile, operation, cancellationToken);
    }

    /// <summary>
    /// 取得單次 lease、執行 connector 並立即投影成封閉回應。
    /// <c>await using</c> 是 Client 與 Permit 的唯一歸還點；若 connector 回傳無效 identity，先標記 faulted
    /// 再退出區塊，使 Pool Dispose 可疑 Client 而非放回 idle queue。此方法不快取結果或例外，因此結果與
    /// 失敗鏈不會延長到後續 Session／Profile。
    /// </summary>
    private static async Task<OperationExecutionResult> ExecuteWhoAmIAsync(
        IConnectorPool pool,
        ResolvedProfile profile,
        ConnectorOperation operation,
        CancellationToken cancellationToken)
    {
        await using var lease = await pool.AcquireAsync(operation, cancellationToken).ConfigureAwait(false);
        var connectorResult = await lease.ExecuteAsync(operation, cancellationToken).ConfigureAwait(false);
        if (!connectorResult.Succeeded)
        {
            return OperationExecutionResult.Failure(
                ConnectorFailureErrorCode,
                "The Data8 connector did not complete the requested operation.");
        }

        if (!TryProjectWhoAmI(operation.OperationId, profile, connectorResult, out var data))
        {
            // 回應若不符合固定 WhoAmI contract，client 健康與 session 狀態均不可證明；必須淘汰。
            lease.MarkFaulted();
            return OperationExecutionResult.Failure(
                InvalidResponseErrorCode,
                "The Data8 connector returned an invalid identity response.");
        }

        return OperationExecutionResult.Success(data);
    }

    /// <summary>
    /// 檢查 Data8 executor 的最小可驗證 capability。P4 只開放無參數 WhoAmI；其餘 registry capability
    /// 尚未具備 Data8 的安全 template／projection 實作，必須在任何 Pool 資源取得前拒絕，不能以 generic
    /// FetchXML、endpoint 或 raw response 作為暫時 fallback。
    /// </summary>
    private static bool IsSupportedWhoAmIRequest(OperationExecutionRequest request)
        => string.Equals(
                request.CapabilityOperationId,
                OperationIds.RuntimeHealthWhoAmI,
                StringComparison.Ordinal) &&
           request.Parameters is { Count: 0 } &&
           !string.IsNullOrWhiteSpace(request.WorkloadSubjectId) &&
           Package01OperationRegistry.TryGet(request.CapabilityOperationId, out _);

    /// <summary>
    /// 從 SDK-free connector scalar map 建構嚴格的 WhoAmI 投影。
    /// 三個 GUID 都必須存在且有效，且 organizationId 必須與 resolver 的 immutable Profile snapshot 相同；
    /// 此比較是防止 Factory／Client 混接到其他 Organization 的最後防線。投影只建立純值 DTO，絕不回傳
    /// 原始 dictionary、connector client 或 Profile 的 credential／endpoint 資訊。
    /// </summary>
    private static bool TryProjectWhoAmI(
        string operationId,
        ResolvedProfile profile,
        ConnectorOperationResult result,
        out OperationResponseData? data)
    {
        data = null;
        if (!TryReadNonEmptyGuid(result.Values, "userId", out var userId) ||
            !TryReadNonEmptyGuid(result.Values, "businessUnitId", out var businessUnitId) ||
            !TryReadNonEmptyGuid(result.Values, "organizationId", out var organizationId) ||
            organizationId != profile.OrganizationId)
        {
            return false;
        }

        data = OperationResponseData.ForWhoAmI(
            operationId,
            ToCeVersionString(profile.CeVersion),
            new WhoAmIResponseData
            {
                UserId = userId,
                BusinessUnitId = businessUnitId,
                OrganizationId = organizationId
            });
        return true;
    }

    /// <summary>
    /// 讀取固定名稱的非空 GUID，避免寬鬆解析把缺欄、空字串或其他 connector 回應種類誤判為成功。
    /// 任何失敗都只回傳 false，讓 caller 在 lease 還在作用域內標記 faulted 並確定性 dispose Client。
    /// </summary>
    private static bool TryReadNonEmptyGuid(
        IReadOnlyDictionary<string, string?> values,
        string key,
        out Guid value)
    {
        value = Guid.Empty;
        return values.TryGetValue(key, out var scalar) &&
               Guid.TryParse(scalar, out value) &&
               value != Guid.Empty;
    }

    /// <summary>
    /// 將部署端 CE enum 映射為公共回應合約的固定版本字串。未知 enum 代表已損毀或未受支援設定，
    /// 以例外拒絕而非偷偷標記為另一版本，避免跨版本 connector 結果被產品錯誤重用。
    /// </summary>
    private static string ToCeVersionString(CeVersion ceVersion)
        => ceVersion switch
        {
            CeVersion.Ce82 => "8.2",
            CeVersion.Ce91 => "9.1",
            _ => throw new ArgumentOutOfRangeException(nameof(ceVersion), ceVersion, "Unsupported CE version.")
        };

    /// <summary>
    /// 將 resolver 的內部錯誤限制為已審查的安全分類，避免錯誤實作把 endpoint、Organization GUID 或
    /// credential reference 帶入產品回應。未知分類仍使用 profile.not-found，保持 fail-closed 語意。
    /// </summary>
    private static string NormalizeProfileResolutionError(string? resolutionError)
        => string.Equals(resolutionError, ProfileNotFoundErrorCode, StringComparison.Ordinal)
            ? ProfileNotFoundErrorCode
            : ProfileNotFoundErrorCode;
}
