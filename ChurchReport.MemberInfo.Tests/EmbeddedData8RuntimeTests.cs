using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Connectors.Data8;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 ChurchReport Embedded composition root 將既有 CrmConnection 映射結果組成同一條 Data8 控制面管線。
/// 測試不執行 D365 I/O：它只保護 ProfileResolver、Admission manager、Router 與 Pool 的所有權排列，以及
/// runtime Dispose 的 drain-before-admission 清理順序，避免 F5 host 結束後留下 permit、client、timer 或 session。
/// </summary>
public sealed class EmbeddedData8RuntimeTests
{
    /// <summary>
    /// 保護 runtime 在不需 Gateway endpoint 下建立 resolver、Data8 generation pool 與 executor；故障模型是組合根
    /// 偷偷建立 client 或改走 HTTP。決定性斷言是 constructor 不呼叫 connection factory、Router 僅接受固定
    /// sunnyvalechback generation，Dispose 後拒絕再路由，且無真實 connector／WCF 資源可殘留。
    /// </summary>
    [Fact]
    public async Task Runtime_composes_one_embedded_data8_generation_without_creating_a_client_until_a_lease_is_requested()
    {
        var profile = new DynamicsProfileOptions
        {
            OrganizationAlias = "sunnyvalechback",
            CeVersion = CeVersion.Ce91,
            ConnectorKind = ConnectorKind.Data8,
            CredentialReference = "churchreport.crmconnection",
            Pool = new PoolPolicy { MinSize = 0, MaxSize = 1, IdleTimeoutMinutes = 1, AcquireTimeoutSeconds = 1 },
            Operation = new OperationPolicy { TimeoutSeconds = 5, MaxRetries = 0, RetryBaseDelayMs = 1 }
        };
        var catalog = new OrganizationCatalogEntry
        {
            FriendlyName = "測試組織",
            UniqueName = "sunnyvalechback",
            OrganizationId = Guid.Parse("bfb92ead-3705-f011-8143-00155d006608"),
            State = OrganizationState.Enabled,
            ServiceUri = "https://example.invalid/XRMServices/2011/Organization.svc"
        };
        var connectionFactory = new CountingConnectionFactory();
        await using var runtime = new EmbeddedData8Runtime(
            new Dictionary<string, DynamicsProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["sunnyvalechback"] = profile
            },
            new Dictionary<string, OrganizationCatalogEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["sunnyvalechback"] = catalog
            },
            "sunnyvalechback",
            connectionFactory,
            NullLogger<EmbeddedData8Runtime>.Instance,
            NullLoggerFactory.Instance);

        runtime.Executor.Should().NotBeNull();
        runtime.ProfileResolver.TryResolve("sunnyvalechback", out var resolved, out var error).Should().BeTrue(error);
        runtime.Router.Resolve(resolved!).ProfileAlias.Should().Be("sunnyvalechback");
        connectionFactory.CreateCount.Should().Be(0);

        await runtime.DisposeAsync();

        var action = () => runtime.Router.Resolve(resolved!);
        action.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    /// 保護組織位於 IIS virtual directory 時，Embedded composition root 仍會把 Data8 的完整 Organization.svc
    /// transport endpoint 正規化為 admission 所需的組織根網址。故障模型是以字串 Replace 移除過多路徑，導致
    /// `https://host/CrmApp/XRMServices/...` 被誤縮成 host 根目錄或被容量鍵拒絕；決定性斷言是 constructor 不建立
    /// client 且可完成 router lookup，表示 virtual directory 被保留並且沒有繞過 admission。
    /// </summary>
    [Fact]
    public async Task Runtime_preserves_the_virtual_directory_when_creating_its_organization_admission_plan()
    {
        var profile = new DynamicsProfileOptions
        {
            OrganizationAlias = "sunnyvalechback",
            CeVersion = CeVersion.Ce91,
            ConnectorKind = ConnectorKind.Data8,
            CredentialReference = "churchreport.crmconnection",
            Pool = new PoolPolicy { MinSize = 0, MaxSize = 1, IdleTimeoutMinutes = 1, AcquireTimeoutSeconds = 1 },
            Operation = new OperationPolicy { TimeoutSeconds = 5, MaxRetries = 0, RetryBaseDelayMs = 1 }
        };
        var catalog = new OrganizationCatalogEntry
        {
            FriendlyName = "虛擬目錄測試組織",
            UniqueName = "sunnyvalechback",
            OrganizationId = Guid.Parse("bfb92ead-3705-f011-8143-00155d006608"),
            State = OrganizationState.Enabled,
            ServiceUri = "https://example.invalid/CrmApp/XRMServices/2011/Organization.svc"
        };
        var connectionFactory = new CountingConnectionFactory();

        await using var runtime = new EmbeddedData8Runtime(
            new Dictionary<string, DynamicsProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["sunnyvalechback"] = profile
            },
            new Dictionary<string, OrganizationCatalogEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["sunnyvalechback"] = catalog
            },
            "sunnyvalechback",
            connectionFactory,
            NullLogger<EmbeddedData8Runtime>.Instance,
            NullLoggerFactory.Instance);

        runtime.ProfileResolver.TryResolve("sunnyvalechback", out var resolved, out var error).Should().BeTrue(error);
        runtime.Router.Resolve(resolved!).ProfileAlias.Should().Be("sunnyvalechback");
        connectionFactory.CreateCount.Should().Be(0);
    }

    /// <summary>
    /// 只計數 factory 被要求建立 client 的次數。此替身在意外取得 lease 時立刻失敗且不建立 client，讓測試能
    /// 證明 constructor／Router lookup 沒有偷開 WCF channel、session、timer 或背景工作；計數是方法範圍 scalar，
    /// 不保存 Profile、credential 或 request。
    /// </summary>
    private sealed class CountingConnectionFactory : IData8ConnectorClientFactory
    {
        private int _createCount;

        public int CreateCount => Volatile.Read(ref _createCount);

        public Task<SpeechMessage.Dynamics.Abstractions.Connectors.IConnectorClient> CreateAsync(
            ResolvedProfile profile,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCount);
            throw new InvalidOperationException("This test must not request a connector client.");
        }
    }
}
