// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileRuntime.cs
// 用途：提供 Embedded 與 Dedicated Gateway 共用、但由各自 Host 唯一擁有的 Data8 runtime。
// ============================================================================

using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Configuration;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// 以不可變 profile/catalog snapshot 建立單一 Data8 host generation 的生命週期 owner。
/// 每個 Embedded 或 Dedicated Gateway host 都建立自己的 instance；因此 resolver、admission、pool、
/// client 與 permit 絕不跨 Host、Profile 或 Organization 共用。此類別不持有 HttpContext、身分、
/// token、cookie 或可變組態，也不會在 constructor 預先建立 Data8/WCF 連線。
/// </summary>
public sealed class Data8ProfileRuntime : IAsyncDisposable
{
    private readonly Data8ConnectorPoolRegistry _poolRegistry = new();
    private readonly OrganizationAdmissionManager _admissionManager;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    /// <summary>
    /// 建立指定 alias 的 Data8 runtime。
    /// 建構失敗時會以 pool → admission 的反向順序回復已取得資源；成功後 pool 只有在取得 lease
    /// 時才建立 client。組態中的 credential 只被 factory 擁有，不會被 resolver、router 或 request 保存。
    /// </summary>
    public Data8ProfileRuntime(
        IReadOnlyDictionary<string, DynamicsProfileOptions> profiles,
        IReadOnlyDictionary<string, OrganizationCatalogEntry> catalog,
        string profileAlias,
        IData8ConnectorClientFactory clientFactory,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileAlias);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ProfileResolver = new ConfigurationProfileResolver(profiles, catalog, generationId: 1);
        if (!ProfileResolver.TryResolve(profileAlias, out var profile, out var resolutionError) || profile is null)
        {
            throw new InvalidOperationException("The selected Data8 profile is invalid: " + NormalizeError(resolutionError));
        }

        if (!catalog.TryGetValue(profile.OrganizationAlias, out var organization) || organization is null)
        {
            throw new InvalidOperationException("The selected Data8 organization catalog entry is unavailable.");
        }

        _admissionManager = new OrganizationAdmissionManager(
            CreateAdmissionPlan(profile, organization),
            new InMemoryRuntimeHostSlotCoordinator(),
            loggerFactory.CreateLogger<OrganizationAdmissionManager>());

        try
        {
            _poolRegistry.Register(profile, _admissionManager, clientFactory, profile.Pool.MinSize, profile.Pool.MaxSize);
            Router = _poolRegistry;
            Executor = new Data8ProfileOperationExecutor(ProfileResolver, Router);
        }
        catch
        {
            try { _poolRegistry.Dispose(); }
            finally { _admissionManager.Dispose(); }
            throw;
        }
    }

    /// <summary>取得 runtime 的不可變 profile resolver；只能讀取建構時 snapshot，不能重載或改寫組態。</summary>
    public IProfileResolver ProfileResolver { get; }

    /// <summary>取得只接受 Data8 generation 的 router；釋放 runtime 後其所有 pool lookup 都會 fail-closed。</summary>
    public IConnectorRouter Router { get; }

    /// <summary>取得由 resolver → admission → router → lease 驅動的 SDK-free operation executor。</summary>
    public Data8ProfileOperationExecutor Executor { get; }

    /// <summary>
    /// 以 idempotent 的單一 cleanup task 釋放 runtime。
    /// pool 必先 drain/dispose，確保 active lease 回收 client 並釋放 permit；其後才停止 admission 的 host-slot
    /// renewal/CTS。多個 cleanup 失敗會被聚合，但任何一個失敗都不會阻止後續 owner 的釋放。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        List<Exception>? failures = null;
        try { await _poolRegistry.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { await _admissionManager.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { (failures ??= []).Add(exception); }

        if (failures is { Count: 1 }) throw failures[0];
        if (failures is { Count: > 1 }) throw new AggregateException("Data8 profile runtime cleanup failed.", failures);
    }

    /// <summary>
    /// 將 Organization.svc URI 正規化為含 virtual directory 的 organization root，並建立單 host 的
    /// in-memory admission plan。這是 Dedicated/Embedded 的本機 host 契約，不使用 SQL coordinator。
    /// </summary>
    private static OrganizationAdmissionPlan CreateAdmissionPlan(ResolvedProfile profile, OrganizationCatalogEntry organization)
    {
        var workSeconds = Math.Max(5, checked((int)Math.Ceiling(profile.Operation.Timeout.TotalSeconds)));
        var options = new OrganizationAdmissionOptions
        {
            ExpectedOrganizationId = organization.OrganizationId,
            AggregateMaxInFlight = profile.Pool.MaxSize,
            MaximumRuntimeHosts = 1,
            LocalQueueCapacity = profile.Pool.MaxSize,
            MaxDispatchEnvelopeBytes = 4_096,
            QueueAdmissionTimeoutSeconds = Math.Max(1, checked((int)Math.Ceiling(profile.Pool.AcquireTimeout.TotalSeconds))),
            MaxInFlightAndQueuedPerWorkload = profile.Pool.MaxSize,
            AdmissionNamespaceId = "data8.runtime." + profile.ProfileAlias,
            LeaseNamespaceId = "data8.runtime." + profile.ProfileAlias,
            AdmissionEpoch = 1,
            RuntimeHostSlotLeaseTtlSeconds = Math.Max(120, 30 + workSeconds + 10 + 1),
            RuntimeHostSlotRenewalIntervalSeconds = 30,
            RuntimeHostSlotExpiryFenceSeconds = 10,
            MaximumOutboundWorkLifetimeSeconds = workSeconds,
            ShutdownDrainTimeoutSeconds = workSeconds + 10,
            RequireDurableHostCoordinator = false
        };
        if (!OrganizationAdmissionPlan.TryCreate(GetOrganizationRoot(organization.ServiceUri), 1, 1, options, out var plan, out _) || plan is null)
        {
            throw new InvalidOperationException("The local Data8 admission plan is invalid.");
        }

        return plan;
    }

    /// <summary>從 HTTPS Organization.svc URI 取出 organization root；缺少 XRMServices 路徑時立即拒絕。</summary>
    private static string GetOrganizationRoot(string serviceUri)
    {
        if (!Uri.TryCreate(serviceUri, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("The Data8 organization service URI is invalid.");
        const string segment = "/XRMServices/";
        var index = uri.AbsolutePath.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
        if (index < 0) throw new InvalidOperationException("The Data8 organization service URI has no XRMServices path.");
        var builder = new UriBuilder(uri) { Path = uri.AbsolutePath[..(index + 1)], Query = string.Empty, Fragment = string.Empty };
        var root = builder.Uri.GetLeftPart(UriPartial.Path);
        return root.EndsWith("/", StringComparison.Ordinal) ? root : root + "/";
    }

    /// <summary>將內部 resolver 錯誤正規化，避免向 caller 洩漏 catalog 或 credential 組態細節。</summary>
    private static string NormalizeError(string? error)
        => string.Equals(error, "profile.not-found", StringComparison.Ordinal) ? "profile-not-found" : "profile-unavailable";
}
