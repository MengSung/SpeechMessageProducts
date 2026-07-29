// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeFactory.cs
// 目的：依不可變 Profile Definition 建立一個完全隔離的 Runtime Generation，
//       並在任一步驟失敗時依 ownership 反向順序確定性 rollback 所有部分資源。
// ============================================================================

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// Multi-Profile Runtime Factory 的預設實作。
/// 每次 CreateAsync 都使用新的 Options 深複本，建立新的 DynamicsHttpTransport／SocketsHttpHandler、
/// AdfsOAuthTokenProvider／Token Handler、DynamicsWebApiClient 與 Retirement State；
/// 只有透過 <see cref="IOrganizationAdmissionRegistry"/> 取得的 Canonical Admission Manager 可以共用。
/// Factory 本身只保存 Registry、Secret Resolver 與 LoggerFactory，不保存 Request、User、Session、JWT、Token 或 Credential 值。
/// </summary>
public sealed class DynamicsProfileRuntimeFactory : IDynamicsProfileRuntimeFactory
{
    private readonly IOrganizationAdmissionRegistry _admissionRegistry;
    private readonly ISecretResolver _secretResolver;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// 建立 Runtime Factory。注入的 Secret Resolver 只在 Transport／Token Provider 真正需要時依 Reference Name 解析秘密，
    /// Factory 不快取解析結果；LoggerFactory 只建立 bounded structured logger，不得輸出 Token、Credential、完整 URI Query 或 Body。
    /// </summary>
    public DynamicsProfileRuntimeFactory(
        IOrganizationAdmissionRegistry admissionRegistry,
        ISecretResolver secretResolver,
        ILoggerFactory loggerFactory)
    {
        _admissionRegistry = admissionRegistry ?? throw new ArgumentNullException(nameof(admissionRegistry));
        _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// 依不可變 Definition 與單調遞增 Generation 建立一套完全隔離的 Runtime ownership graph。
    /// 方法先驗證 Web API Root 與 Canonical Admission Plan，再依序取得 Admission Registration、建立 Transport、
    /// Token Provider、Client 與 Runtime；只有 Runtime 成功建構時 ownership 才整體轉移給呼叫端。
    /// 任一步驟失敗都會依反向順序嘗試釋放已建立資源，保留原始錯誤並彙整 cleanup failure，
    /// 因此不會因第一個 Dispose 例外而遺留 Handler、Socket、Token 狀態或 Host Slot Registration。
    /// </summary>
    /// <param name="definition">部署已驗證的 Profile Definition；Factory 會建立深複本，避免外部後續修改污染 Generation。</param>
    /// <param name="generation">Alias 內單調遞增且至少為一的 Generation 編號。</param>
    /// <param name="cancellationToken">取消尚未完成的建立與 rollback 等待；已取得資源仍由本方法確定性清理。</param>
    /// <returns>由呼叫端唯一擁有、必須 drain 並 Dispose 的隔離 Runtime。</returns>
    public async Task<IDynamicsProfileRuntime> CreateAsync(
        DynamicsProfileDefinition definition,
        long generation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (generation < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), "Runtime generation must be at least one.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var options = definition.CreateOptionsSnapshot();
        if (!ApprovedWebApiRootFactory.TryCreate(options, out var approvedRoot, out var rootError) ||
            approvedRoot is null)
        {
            throw new InvalidOperationException(
                rootError?.ErrorMessage ?? "Dynamics profile Web API root is invalid.");
        }

        if (!OrganizationAdmissionPlan.TryCreate(
                options,
                options.Admission,
                out var admissionPlan,
                out var admissionError) ||
            admissionPlan is null)
        {
            throw new InvalidOperationException(
                admissionError?.ErrorMessage ?? "Dynamics profile admission plan is invalid.");
        }

        IOrganizationAdmissionRegistration? registration = null;
        DynamicsHttpTransport? transport = null;
        AdfsOAuthTokenProvider? tokenProvider = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            registration = _admissionRegistry.Acquire(admissionPlan);

            // Main CRM Transport 與 Token Transport 必須分別由此 Generation 擁有；
            // 不傳 IHttpClientFactory 給 Token Provider，避免不同 Generation 共用 named handler pool。
            transport = new DynamicsHttpTransport(
                Options.Create(options),
                _secretResolver,
                _loggerFactory.CreateLogger<DynamicsHttpTransport>());
            tokenProvider = new AdfsOAuthTokenProvider(
                Options.Create(options),
                _secretResolver,
                _loggerFactory.CreateLogger<AdfsOAuthTokenProvider>());
            var client = new DynamicsWebApiClient(
                Options.Create(options),
                transport,
                _secretResolver,
                tokenProvider,
                _loggerFactory.CreateLogger<DynamicsWebApiClient>());
            var warmUpExecutor = new ControlledOperationExecutor(client, registration.Manager);
            var key = new ProfileRuntimeKey(
                definition.ProfileAlias,
                generation,
                approvedRoot.CeVersion,
                admissionPlan.CanonicalKey);

            return new DynamicsProfileRuntime(
                key,
                definition,
                registration,
                transport,
                tokenProvider,
                client,
                warmUpExecutor);
        }
        catch (Exception creationFailure)
        {
            // Ownership 尚未轉移給 Runtime 時，Factory 必須自行依反向順序清理；
            // 每個清理都要被嘗試，任何 cleanup failure 與原始建構失敗一起回報，不能形成沉默的 Handler／Host Slot 洩漏。
            var failures = new List<Exception> { creationFailure };
            await CaptureRollbackFailureAsync(
                tokenProvider,
                static provider => provider.DisposeAsync().AsTask(),
                failures).ConfigureAwait(false);
            await CaptureRollbackFailureAsync(
                transport,
                static ownedTransport => ownedTransport.DisposeAsync().AsTask(),
                failures).ConfigureAwait(false);
            await CaptureRollbackFailureAsync(
                registration,
                static ownedRegistration => ownedRegistration.DisposeAsync().AsTask(),
                failures).ConfigureAwait(false);

            if (failures.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(creationFailure)
                    .Throw();
            }

            throw new AggregateException(
                "Dynamics profile runtime construction and one or more rollback operations failed.",
                failures);
        }
    }

    /// <summary>
    /// 對已建立的部分資源執行 rollback 並收集例外，使後續資源仍可繼續回收。
    /// null 代表該建構步驟尚未取得 ownership，不需清理。
    /// </summary>
    private static async Task CaptureRollbackFailureAsync<T>(
        T? resource,
        Func<T, Task> disposeAsync,
        ICollection<Exception> failures)
        where T : class
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            await disposeAsync(resource).ConfigureAwait(false);
        }
        catch (Exception cleanupFailure)
        {
            failures.Add(cleanupFailure);
        }
    }
}
