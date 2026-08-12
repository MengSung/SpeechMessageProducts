using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證共用 Data8 Profile Runtime 的組態、隔離與釋放契約。
/// 此測試只使用記憶體中的 profile、catalog 與 client factory，不會建立 CRM 連線、WCF channel、
/// 計時器或背景工作；其目的是鎖定 P5 中 Embedded 與 Dedicated Gateway 都必須遵循的共同生命週期。
/// </summary>
public sealed class Data8ProfileRuntimeTests
{
    /// <summary>
    /// 保護「runtime 建立不等於建立連線」的資源界線。
    /// 故障注入為在 factory 被呼叫時立即擲出例外；若 constructor、resolver 或 router 意外預熱
    /// client，測試會失敗。決定性斷言為 runtime 釋放後 router 不可再被使用，避免已終止 host
    /// 的 pool 或 admission 狀態被下一個 host 重新借用。
    /// </summary>
    [Fact]
    public async Task Constructor_creates_an_isolated_data8_generation_without_acquiring_a_client()
    {
        var factory = new ThrowingFactory();
        await using var runtime = new Data8ProfileRuntime(
            CreateProfiles(),
            CreateCatalog(),
            "sunnyvalechback",
            factory,
            NullLoggerFactory.Instance);

        runtime.ProfileResolver.TryResolve("sunnyvalechback", out var profile, out var error)
            .Should().BeTrue(error);
        runtime.Router.Resolve(profile!).ProfileAlias.Should().Be("sunnyvalechback");
        factory.CreateCount.Should().Be(0);

        await runtime.DisposeAsync();

        var useDisposedRuntime = () => runtime.Router.Resolve(profile!);
        useDisposedRuntime.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    /// 保護 generation-owned runtime 是 metadata cache 的唯一釋放 owner。故障注入是在 runtime Dispose 前先放入一筆
    /// 已完整隔離的 server-locale metadata，隨後仍嘗試使用相同 cache；decisive assertions 是 Dispose 後讀寫皆
    /// fail closed。這證明 profile replacement／host shutdown 不會把 option label、profile/generation key 或 cache
    /// entry 留給下一個 runtime，而 constructor 仍不會預先建立 Data8 connector、WCF channel 或背景清理工作。
    /// </summary>
    [Fact]
    public async Task Dispose_async_clears_the_runtime_owned_metadata_cache()
    {
        var factory = new ThrowingFactory();
        await using var runtime = new Data8ProfileRuntime(
            CreateProfiles(),
            CreateCatalog(),
            "sunnyvalechback",
            factory,
            NullLoggerFactory.Instance);
        var key = new MetadataOptionSetCacheKey(
            "sunnyvalechback",
            generationId: 1,
            MetadataOptionSetTarget.ContactCustomerTypeCode,
            serverResolvedLocale: 1028);

        runtime.MetadataOptionSetCache.Store(
            key,
            [new OptionSetOptionRecord { Value = 1, Label = "runtime-owned", ConfiguredOrder = 0 }])
            .Should().BeTrue();

        await runtime.DisposeAsync();

        runtime.MetadataOptionSetCache.TryGet(key, out _).Should().BeFalse();
        runtime.MetadataOptionSetCache.Store(
            key,
            [new OptionSetOptionRecord { Value = 2, Label = "disposed", ConfiguredOrder = 0 }])
            .Should().BeFalse();
        factory.CreateCount.Should().Be(0);
    }

    /// <summary>
    /// 產生唯一的 Data8 profile snapshot；credential reference 只留在 host-owned factory 設定中，
    /// 不會進入 request、lease 或 pool key，因此不會形成跨 profile 或跨組織的 Session 洩漏。
    /// </summary>
    private static IReadOnlyDictionary<string, DynamicsProfileOptions> CreateProfiles()
        => new Dictionary<string, DynamicsProfileOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["sunnyvalechback"] = new()
            {
                OrganizationAlias = "sunnyvalechback",
                CeVersion = CeVersion.Ce91,
                ConnectorKind = ConnectorKind.Data8,
                CredentialReference = "test.data8",
                Pool = new PoolPolicy { MinSize = 0, MaxSize = 1, IdleTimeoutMinutes = 1, AcquireTimeoutSeconds = 1 },
                Operation = new OperationPolicy { TimeoutSeconds = 5, MaxRetries = 0, RetryBaseDelayMs = 1 }
            }
        };

    /// <summary>
    /// 建立啟用中的單一 organization catalog；ServiceUri 含 virtual directory，藉以確認 admission
    /// canonicalization 不可遺失部署路徑，否則不同 virtual directory 會錯誤共用容量預算。
    /// </summary>
    private static IReadOnlyDictionary<string, OrganizationCatalogEntry> CreateCatalog()
        => new Dictionary<string, OrganizationCatalogEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["sunnyvalechback"] = new()
            {
                FriendlyName = "測試組織",
                UniqueName = "sunnyvalechback",
                OrganizationId = Guid.Parse("bfb92ead-3705-f011-8143-00155d006608"),
                State = OrganizationState.Enabled,
                ServiceUri = "https://example.invalid/CrmApp/XRMServices/2011/Organization.svc"
            }
        };

    /// <summary>
    /// 測試專用 factory 只計數而不保留 profile 或 cancellation token。
    /// 若 runtime 在尚未取得 lease 時建立 client，此類別立即失敗，避免連線或 Session 被無界限預熱。
    /// </summary>
    private sealed class ThrowingFactory : IData8ConnectorClientFactory
    {
        private int _createCount;

        /// <summary>取得已嘗試建立 client 的次數；此測試預期為零。</summary>
        public int CreateCount => Volatile.Read(ref _createCount);

        /// <summary>任何非 lease 驅動的 client 建立都違反此測試所保護的生命週期契約。</summary>
        public Task<IConnectorClient> CreateAsync(ResolvedProfile profile, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCount);
            throw new InvalidOperationException("The runtime constructor must not create a Data8 client.");
        }
    }
}
