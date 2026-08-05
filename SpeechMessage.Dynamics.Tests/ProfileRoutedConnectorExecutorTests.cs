using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 保護 connector-oriented executor 的跨層契約：部署 Profile 決定 ConnectorKind，
/// executor 只透過 Router 取得對應 generation Pool，再以單一 Lease 執行一次操作。
/// 測試使用不持有外部連線的 test-owned lease，並驗證 lease 在執行完成後確定性釋放。
/// </summary>
public sealed class ProfileRoutedConnectorExecutorTests
{
    /// <summary>
    /// 驗證 Official Worker 操作不會繞過 Router/Pool，也不會讓 caller-owned operation
    /// dictionary 或 transport-specific state 穿越 connector lease 邊界。
    /// </summary>
    [Fact]
    public async Task Connector_path_resolves_profile_then_acquires_and_releases_one_lease()
    {
        var profile = CreateProfile();
        var lease = new TrackingLease(profile.ProfileAlias, profile.GenerationId);
        var pool = new TrackingPool(profile.ProfileAlias, profile.GenerationId, lease);
        var resolver = new StaticProfileResolver(profile);
        var router = new TrackingRouter(pool);
        var executor = new ProfileRoutedOperationExecutor(resolver, router);

        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = profile.ProfileAlias,
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "connector-executor-test"
        });

        result.Succeeded.Should().BeTrue();
        router.ResolveCount.Should().Be(1);
        pool.AcquireCount.Should().Be(1);
        lease.ExecuteCount.Should().Be(1);
        lease.DisposeCount.Should().Be(1);
    }

    private static ResolvedProfile CreateProfile()
        => new(
            "crm91",
            "crm91",
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CeVersion.Ce91,
            ConnectorKind.OfficialCrm91Worker,
            "credential-reference",
            new ResolvedPoolPolicy(0, 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(5), 0, TimeSpan.Zero),
            9);

    private sealed class StaticProfileResolver : IProfileResolver
    {
        private readonly ResolvedProfile _profile;

        public StaticProfileResolver(ResolvedProfile profile) => _profile = profile;

        public bool TryResolve(string profileAlias, out ResolvedProfile? profile, out string error)
        {
            if (string.Equals(profileAlias, _profile.ProfileAlias, StringComparison.OrdinalIgnoreCase))
            {
                profile = _profile;
                error = string.Empty;
                return true;
            }

            profile = null;
            error = "profile.not-found";
            return false;
        }
    }

    private sealed class TrackingRouter : IConnectorRouter
    {
        private readonly IConnectorPool _pool;

        public TrackingRouter(IConnectorPool pool) => _pool = pool;

        public int ResolveCount { get; private set; }

        public IConnectorPool Resolve(ResolvedProfile profile)
        {
            ResolveCount++;
            return _pool;
        }
    }

    private sealed class TrackingPool : IConnectorPool
    {
        private readonly IConnectorLease _lease;

        public TrackingPool(string profileAlias, long generationId, IConnectorLease lease)
        {
            ProfileAlias = profileAlias;
            GenerationId = generationId;
            _lease = lease;
        }

        public string ProfileAlias { get; }

        public long GenerationId { get; }

        public bool IsDraining => false;

        public int AcquireCount { get; private set; }

        public Task<IConnectorLease> AcquireAsync(
            ConnectorOperation operation,
            CancellationToken cancellationToken)
        {
            AcquireCount++;
            return Task.FromResult(_lease);
        }

        public Task DrainAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TrackingLease : IConnectorLease
    {
        public TrackingLease(string profileAlias, long generationId)
        {
            ProfileAlias = profileAlias;
            GenerationId = generationId;
        }

        public string ProfileAlias { get; }

        public long GenerationId { get; }

        public int ExecuteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task<ConnectorOperationResult> ExecuteAsync(
            ConnectorOperation operation,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            return Task.FromResult(new ConnectorOperationResult(true));
        }

        public void MarkFaulted(Exception? cause = null)
        {
        }

        public void Dispose() => DisposeCount++;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
