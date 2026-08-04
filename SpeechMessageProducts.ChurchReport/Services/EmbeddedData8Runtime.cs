// ============================================================================
// 檔案：SpeechMessageProducts.ChurchReport/Services/EmbeddedData8Runtime.cs
// 用途：ChurchReport 的 Embedded Data8 composition root，將啟動期設定組成 ProfileResolver、Admission、Router
//       與 generation-owned Pool，再提供不含 HTTP 的受控 executor。
//
// 生命週期、隔離與效能契約：
// 1. 每個 runtime 只服務一個固定 ProfileAlias／Organization／Generation；產品 request 無法更換 connector、
//    endpoint、OrganizationId 或 credential。ProfileResolver 與 Router 共同防止跨 Profile／組織混用。
// 2. runtime 是 Data8ConnectorPoolRegistry 與 OrganizationAdmissionManager 的唯一 owner。Dispose 必先 drain／
//    dispose Pool（回收 client 與 lease），再停止 Admission（回收 permit、CTS、renewal task、host slot、semaphore）。
// 3. 建構不建立 Data8 client、WCF channel、HTTP、timer 或背景 task；這些只有首次 Pool lease 在 admission 後建立。
//    因而無流量的 Visual Studio F5 不會保留 D365 Session，且 hot path 沒有 HTTP 序列化額外成本。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Connectors.Data8;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Configuration;

namespace ChurchReport.Services;

/// <summary>
/// ChurchReport Embedded 模式的一個固定 Data8 Profile generation runtime。
/// 此型別是產品啟動期的 composition root，不是 controller、Session 或 request service。它只保存不可變的
/// resolver snapshot 與受控資源 owner，並公開 SDK-free executor／router 供 DI 組成 Adapter；Data8 client、
/// WCF channel、credential、request 和結果都不會以欄位形式保存，避免跨使用者、跨租戶或跨 host generation
/// 狀態洩漏。
/// </summary>
public sealed class EmbeddedData8Runtime : IAsyncDisposable
{
    private readonly Data8ConnectorPoolRegistry _poolRegistry;
    private readonly OrganizationAdmissionManager _admissionManager;
    private readonly ILogger<EmbeddedData8Runtime> _logger;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    /// <summary>
    /// 從 mapper 產生的唯一 Profile／Catalog snapshot 建立 Embedded runtime。
    /// constructor 只建構有界、程序內控制面物件；Data8 client factory 必須延後到 Pool 已取得 Admission permit
    /// 與 local slot 之後才被呼叫。若任何設定或註冊失敗，已建立的 manager／pool registry 會在 constructor
    /// 退出前同步 rollback，避免半完成的 semaphore、CTS 或可續租 host slot 逃出 owner。
    /// </summary>
    /// <param name="profiles">由既有 CrmConnection mapper 產生、僅含 selected alias 的啟動期 profile snapshot。</param>
    /// <param name="catalog">由既有 CrmConnection mapper 產生、僅含 selected organization 的 catalog snapshot。</param>
    /// <param name="profileAlias">host 固定的 Embedded ProfileAlias；不得由 request 覆寫。</param>
    /// <param name="clientFactory">只在 Pool 建新 client 時使用的 Data8 factory；runtime 不直接持有其 client。</param>
    /// <param name="logger">只記錄 bounded lifecycle 分類，不記錄 endpoint、credential 或 request 內容。</param>
    /// <param name="loggerFactory">建立既有 admission manager 所需的無秘密 logger。</param>
    public EmbeddedData8Runtime(
        IReadOnlyDictionary<string, DynamicsProfileOptions> profiles,
        IReadOnlyDictionary<string, OrganizationCatalogEntry> catalog,
        string profileAlias,
        IData8ConnectorClientFactory clientFactory,
        ILogger<EmbeddedData8Runtime> logger,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileAlias);
        ArgumentNullException.ThrowIfNull(clientFactory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // ConfigurationProfileResolver 在建構時會製作 immutable snapshots；輸入字典在此後即使被呼叫端改寫，
        // 也不會改變同一 runtime generation 的 Organization／Connector 選擇。
        ProfileResolver = new ConfigurationProfileResolver(profiles, catalog, generationId: 1);
        if (!ProfileResolver.TryResolve(profileAlias, out var resolvedProfile, out var resolutionError) ||
            resolvedProfile is null)
        {
            throw new InvalidOperationException(
                "The selected Embedded Dynamics profile is invalid: " + NormalizeResolutionError(resolutionError));
        }

        if (!catalog.TryGetValue(resolvedProfile.OrganizationAlias, out var organization) || organization is null)
        {
            throw new InvalidOperationException("The selected Embedded Dynamics organization catalog entry is unavailable.");
        }

        var admissionPlan = CreateLocalEmbeddedAdmissionPlan(resolvedProfile, organization);
        _admissionManager = new OrganizationAdmissionManager(
            admissionPlan,
            new InMemoryRuntimeHostSlotCoordinator(),
            loggerFactory.CreateLogger<OrganizationAdmissionManager>());
        _poolRegistry = new Data8ConnectorPoolRegistry();

        try
        {
            _poolRegistry.Register(
                resolvedProfile,
                _admissionManager,
                clientFactory,
                resolvedProfile.Pool.MinSize,
                resolvedProfile.Pool.MaxSize);
            Router = _poolRegistry;
            Executor = new Data8ProfileOperationExecutor(ProfileResolver, Router);
        }
        catch
        {
            // constructor 發生例外時尚未發佈 runtime；此同步 rollback 是唯一 owner，避免 caller 無法取得物件
            // 卻仍遺留 Pool semaphore 或 Admission CTS。Pool 先於 Admission 清理，與正常 Dispose 順序一致。
            try
            {
                _poolRegistry.Dispose();
            }
            finally
            {
                _admissionManager.Dispose();
            }

            throw;
        }
    }

    /// <summary>取得 immutable profile resolver；只可解析此 runtime 啟動時的 generation，不可讀取 credential。</summary>
    public IProfileResolver ProfileResolver { get; }

    /// <summary>取得 Data8-only router；它只能以 resolver snapshot 的 Alias／Generation 路由到來源 Pool。</summary>
    public IConnectorRouter Router { get; }

    /// <summary>取得可交給 EmbeddedHostAdapter 的 SDK-free executor；Adapter 與 executor 均不擁有 runtime。</summary>
    public Data8ProfileOperationExecutor Executor { get; }

    /// <summary>
    /// 終止這個 host generation。多個 Generic Host shutdown、DI disposal 或測試 cleanup 呼叫會等待同一 Task；
    /// 第一步先拒絕新 Pool lease 並等待既有 lease 釋放／Dispose client，第二步才停止 Admission 的 renew loop、
    /// lease token、host slot 與 semaphore。所有 cleanup 失敗仍繼續嘗試後續 owner，最後以 AggregateException
    /// 回報，避免 permit 或 background renewal 由第一個例外遮蔽而洩漏。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    /// <summary>按 pool-before-admission 的唯一所有權順序執行所有非同步 cleanup。</summary>
    private async Task DisposeCoreAsync()
    {
        List<Exception>? failures = null;
        try
        {
            await _poolRegistry.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            await _admissionManager.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (failures is { Count: 1 })
        {
            throw failures[0];
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException("Embedded Data8 runtime cleanup failed.", failures);
        }

        _logger.LogDebug("ChurchReport Embedded Data8 runtime was drained and disposed.");
    }

    /// <summary>
    /// 建立僅限單一程序 Embedded F5 的 bounded admission policy。P4 明確不實作 Central Gateway 或 SQL
    /// coordinator，因此 MaximumRuntimeHosts=1 與 RequireDurableHostCoordinator=false 是本機開發範圍的顯式
    /// 限制，不能被誤當成正式多主機容量設定。Pool 最大 size 同時作為本 runtime 的 aggregate ceiling，
    /// 避免 local client slot 高於 Organization admission。
    /// </summary>
    private static OrganizationAdmissionPlan CreateLocalEmbeddedAdmissionPlan(
        ResolvedProfile profile,
        OrganizationCatalogEntry organization)
    {
        var outboundLifetimeSeconds = Math.Max(
            5,
            checked((int)Math.Ceiling(profile.Operation.Timeout.TotalSeconds)));
        var options = new OrganizationAdmissionOptions
        {
            ExpectedOrganizationId = organization.OrganizationId,
            AggregateMaxInFlight = profile.Pool.MaxSize,
            MaximumRuntimeHosts = 1,
            LocalQueueCapacity = profile.Pool.MaxSize,
            MaxDispatchEnvelopeBytes = 4_096,
            QueueAdmissionTimeoutSeconds = Math.Max(
                1,
                checked((int)Math.Ceiling(profile.Pool.AcquireTimeout.TotalSeconds))),
            MaxInFlightAndQueuedPerWorkload = profile.Pool.MaxSize,
            // 本機 alias 是固定 host deployment key；正式跨 host 共用 namespace 是 P6/P7 以 durable coordinator
            // 驗證的工作，P4 不能假裝 in-memory value 能跨 process 保證總容量。
            AdmissionNamespaceId = "churchreport.embedded." + profile.ProfileAlias,
            LeaseNamespaceId = "churchreport.embedded." + profile.ProfileAlias,
            AdmissionEpoch = 1,
            RuntimeHostSlotLeaseTtlSeconds = Math.Max(120, 30 + outboundLifetimeSeconds + 10 + 1),
            RuntimeHostSlotRenewalIntervalSeconds = 30,
            RuntimeHostSlotExpiryFenceSeconds = 10,
            MaximumOutboundWorkLifetimeSeconds = outboundLifetimeSeconds,
            ShutdownDrainTimeoutSeconds = outboundLifetimeSeconds + 10,
            RequireDurableHostCoordinator = false
        };

        if (!OrganizationAdmissionPlan.TryCreate(
                GetOrganizationRoot(organization.ServiceUri),
                workerCount: 1,
                maxInFlightPerWorker: 1,
                options,
                out var plan,
                out _)
            || plan is null)
        {
            throw new InvalidOperationException("The local Embedded Dynamics admission plan is invalid.");
        }

        return plan;
    }

    /// <summary>
    /// 從 Data8 必須使用的完整 Organization.svc endpoint 取得 admission 唯一接受的組織根網址。
    /// 容量鍵刻意拒絕 <c>/XRMServices/</c> 之後的 transport path，避免把同一 Organization 的不同 SDK
    /// endpoint 誤當成不同容量預算；但 virtual directory 是部署身分的一部分，不能用 Replace 或全路徑小寫
    /// 而遺失。此方法只切除第一個 <c>/XRMServices/</c> path segment 與其後內容，保留 scheme、host、port
    /// 與前綴 virtual directory。它不快取 URI、credential、request 或 session，回傳的 string 只存活到
    /// OrganizationAdmissionPlan 建構完成。
    /// </summary>
    /// <param name="serviceUri">已由 ProfileResolver 驗證為 HTTPS Organization.svc 的完整服務網址。</param>
    /// <returns>含結尾斜線的 HTTPS 組織根網址，供 canonical capacity key 使用。</returns>
    /// <exception cref="InvalidOperationException">完整服務網址缺少受支援 XRMServices path 時 fail closed。</exception>
    private static string GetOrganizationRoot(string serviceUri)
    {
        if (!Uri.TryCreate(serviceUri, UriKind.Absolute, out var parsedServiceUri))
        {
            throw new InvalidOperationException("The Embedded Dynamics organization service URI is invalid.");
        }

        const string xrmServicesPathSegment = "/XRMServices/";
        var xrmServicesIndex = parsedServiceUri.AbsolutePath.IndexOf(
            xrmServicesPathSegment,
            StringComparison.OrdinalIgnoreCase);
        if (xrmServicesIndex < 0)
        {
            throw new InvalidOperationException("The Embedded Dynamics organization service URI has no XRMServices path.");
        }

        // xrmServicesIndex 指向前導 '/'；保留它能確保 host root 變為 "/"，virtual directory 則保留為
        // "/CrmApp/"。UriBuilder 負責 IPv6 與非預設 port，不以手動字串串接破壞 URI 語意。
        var builder = new UriBuilder(parsedServiceUri)
        {
            Path = parsedServiceUri.AbsolutePath[..(xrmServicesIndex + 1)],
            Query = string.Empty,
            Fragment = string.Empty
        };
        var organizationRoot = builder.Uri.GetLeftPart(UriPartial.Path);
        return organizationRoot.EndsWith("/", StringComparison.Ordinal)
            ? organizationRoot
            : organizationRoot + "/";
    }

    /// <summary>
    /// 將 resolver 的內部錯誤分類縮小為不含 deployment detail 的固定文字，避免啟動例外洩漏 catalog 或
    /// credential metadata。此 runtime 只接受 profile.not-found；其餘情況同樣 fail closed。
    /// </summary>
    private static string NormalizeResolutionError(string? error)
        => string.Equals(error, "profile.not-found", StringComparison.Ordinal)
            ? "profile-not-found"
            : "profile-unavailable";
}
