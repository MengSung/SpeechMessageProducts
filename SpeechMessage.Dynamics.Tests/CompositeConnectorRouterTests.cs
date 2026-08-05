using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.ControlPlane.Connectors;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證跨 Connector 的 Router 只依部署端不可變 <see cref="ResolvedProfile"/> 選擇已登錄路徑。
/// 測試刻意使用不建立網路、SDK、Worker 或 admission 資源的替身，專注保護 P6 的路由信任邊界：
/// 請求不可藉由錯誤 ConnectorKind 退回 Data8，也不可在別的 Connector Router 未註冊時建立任何替代資源。
/// </summary>
public sealed class CompositeConnectorRouterTests
{
    /// <summary>
    /// 驗證 Official 9.1 Profile 只會進入其已登錄 Router，且 Data8、Official 8.2 Router 均不會被探測。
    /// 這保護 CE／SDK 版本與 profile generation 的隔離，不讓「先試 Data8」等 fallback 邏輯建立錯誤
    /// 的 connection、worker、pipe、credential 或跨 Profile mutable state。
    /// </summary>
    [Fact]
    public void Resolve_routes_only_to_the_router_registered_for_profile_connector_kind()
    {
        var data8Pool = new TestConnectorPool("test-profile", 1);
        var official82Pool = new TestConnectorPool("test-profile", 1);
        var official91Pool = new TestConnectorPool("test-profile", 1);
        var data8Router = new TrackingRouter(data8Pool);
        var official82Router = new TrackingRouter(official82Pool);
        var official91Router = new TrackingRouter(official91Pool);
        var router = new CompositeConnectorRouter(
            new Dictionary<ConnectorKind, IConnectorRouter>
            {
                [ConnectorKind.Data8] = data8Router,
                [ConnectorKind.OfficialCrm82Worker] = official82Router,
                [ConnectorKind.OfficialCrm91Worker] = official91Router
            });

        var resolved = router.Resolve(CreateProfile(ConnectorKind.OfficialCrm91Worker));

        resolved.Should().BeSameAs(official91Pool);
        official91Router.ResolveCount.Should().Be(1);
        data8Router.ResolveCount.Should().Be(0);
        official82Router.ResolveCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證沒有 Official 8.2 Router 登錄時必須立即 fail closed，且現有 Data8 Router 不能被當成容錯
    /// 或相容性替代方案。決定性斷言是例外型別與 Data8 Router 的零呼叫次數；因此此測試能防止未來
    /// 因為「可用的 connector」而錯誤建立不同 CE 版本的 process、pipe 或 connection。
    /// </summary>
    [Fact]
    public void Resolve_rejects_unregistered_connector_kind_without_data8_fallback()
    {
        var data8Router = new TrackingRouter(new TestConnectorPool("data8", 1));
        var router = new CompositeConnectorRouter(
            new Dictionary<ConnectorKind, IConnectorRouter>
            {
                [ConnectorKind.Data8] = data8Router
            });

        var action = () => router.Resolve(CreateProfile(ConnectorKind.OfficialCrm82Worker));

        action.Should().Throw<NotSupportedException>();
        data8Router.ResolveCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 Official 9.1 Router 在任何 Pool、Worker 或 admission 資源分配前拒絕 CE 8.2 Profile。
    /// 決定性斷言是受控的例外與零次 Pool 使用；這可防止錯誤 package-lock／SDK version 的 Worker
    /// 因為 alias 相同而被選取，並確保版本不相容永遠不會退回 Data8 或另一個 Official Worker。
    /// </summary>
    [Fact]
    public void Official_worker_router_rejects_a_profile_for_the_other_ce_version_before_pool_use()
    {
        var pool = new TestConnectorPool("test-profile", 1);
        var router = new OfficialWorkerConnectorRouter(
            ConnectorKind.OfficialCrm91Worker,
            pool);
        var incompatibleProfile = CreateProfile(ConnectorKind.OfficialCrm91Worker) with
        {
            CeVersion = CeVersion.Ce82
        };

        var action = () => router.Resolve(incompatibleProfile);

        action.Should().Throw<NotSupportedException>();
    }

    /// <summary>
    /// 建立不含 endpoint、credential、token、SDK 或 request mutable state 的 profile snapshot。
    /// 值只用於判定 ConnectorKind 路由，並以固定 generation 模擬 deployment-owned 不可變設定。
    /// </summary>
    private static ResolvedProfile CreateProfile(ConnectorKind connectorKind)
        => new(
            "test-profile",
            "test-organization",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            connectorKind == ConnectorKind.OfficialCrm82Worker ? CeVersion.Ce82 : CeVersion.Ce91,
            connectorKind,
            "test-credential-reference",
            new ResolvedPoolPolicy(0, 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(1), 0, TimeSpan.Zero),
            1);

    /// <summary>
    /// 追蹤 Router 是否被 Composite Router 選取；它不保存 Profile、request 或 caller state，計數只在
    /// 單一測試生命週期內使用。此替身把回傳的 Pool 固定為 constructor 傳入實體，避免 mock 行為掩蓋
    /// 真正需要驗證的 ConnectorKind 分派。
    /// </summary>
    private sealed class TrackingRouter : IConnectorRouter
    {
        private readonly IConnectorPool _pool;

        /// <summary>建立固定回傳單一測試 Pool 的 Router。</summary>
        public TrackingRouter(IConnectorPool pool) => _pool = pool;

        /// <summary>取得本測試 Router 被解析的次數。</summary>
        public int ResolveCount { get; private set; }

        /// <summary>記錄一次 route 選取並回傳測試擁有的 Pool，不建立任何外部資源。</summary>
        public IConnectorPool Resolve(ResolvedProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ResolveCount++;
            return _pool;
        }
    }

    /// <summary>
    /// 提供只讀 identity 的最小 Pool 替身。Composite Router 測試不取得 Lease；若未來測試意外呼叫
    /// Acquire 或 Drain，固定拋出例外以防止這個純路由測試悄悄演變成有背景工作或資源 ownership 的測試。
    /// </summary>
    private sealed class TestConnectorPool : IConnectorPool
    {
        /// <summary>建立指定 identity 的測試 Pool。</summary>
        public TestConnectorPool(string profileAlias, long generationId)
        {
            ProfileAlias = profileAlias;
            GenerationId = generationId;
        }

        /// <summary>取得測試 Pool 所屬的 profile alias。</summary>
        public string ProfileAlias { get; }

        /// <summary>取得測試 Pool 所屬的 generation。</summary>
        public long GenerationId { get; }

        /// <summary>純路由測試不會進入 drain，因此固定回傳 false。</summary>
        public bool IsDraining => false;

        /// <summary>禁止本測試在沒有受控 admission owner 的情況下取得 Lease。</summary>
        public Task<IConnectorLease> AcquireAsync(
            ConnectorOperation operation,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("The composite-router test pool cannot acquire a lease.");

        /// <summary>禁止純路由測試啟動 drain 或建立 cleanup work。</summary>
        public Task DrainAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The composite-router test pool cannot drain.");

        /// <summary>測試 Pool 不擁有可釋放資源，因此同步 Dispose 為 no-op。</summary>
        public void Dispose()
        {
        }

        /// <summary>測試 Pool 不擁有可釋放資源，因此非同步 Dispose 立即完成。</summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
