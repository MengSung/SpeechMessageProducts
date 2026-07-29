using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 SQL durable host-slot coordinator 的設定界線、schema 隔離、故障關閉與真實資料庫原子契約。
/// Live contract 只在明確提供非生產連線字串時執行，且使用唯一 namespace，避免測試彼此或生產租約互相污染。
/// </summary>
public sealed class SqlRuntimeHostSlotCoordinatorTests
{
    /// <summary>不允許零或無界 timeout/quarantine，避免資料庫故障造成忙迴圈或永久容量凍結。</summary>
    [Fact]
    public void Options_reject_unbounded_or_unsafe_values()
    {
        var options = new SqlRuntimeHostSlotCoordinatorOptions
        {
            ConnectionString = "Server=localhost;Database=SpeechMessageDynamicsControlPlane;Integrated Security=true;",
            CommandTimeoutSeconds = 0,
            QuarantineSeconds = 0
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>schema 必須位於獨立 control-plane，不得接觸 MSCRM_CONFIG 或 OrganizationBase。</summary>
    [Fact]
    public void Schema_is_scoped_to_the_standalone_control_plane_database()
    {
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("RuntimeHostSlotLease");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("RuntimeHostFencingSequence");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("RuntimeHostAdmissionEpoch");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("ConfigurationDigest");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("AdmissionEpoch");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("SYSUTCDATETIME()");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().NotContain("MSCRM_CONFIG");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().NotContain("OrganizationBase");
    }

    /// <summary>注入無法連線的 SQL endpoint，證明錯誤向上傳播且 ActiveDatabaseOperations 必定回到零。</summary>
    [Fact]
    public async Task Coordinator_outage_fails_closed_without_retained_connection_or_task()
    {
        var coordinator = new SqlRuntimeHostSlotCoordinator(
            new SqlRuntimeHostSlotCoordinatorOptions
            {
                ConnectionString = "Server=127.0.0.1,1;Database=SpeechMessageDynamicsControlPlane;Integrated Security=true;Encrypt=false;Connect Timeout=1;",
                CommandTimeoutSeconds = 1,
                QuarantineSeconds = 1
            },
            NullLogger<SqlRuntimeHostSlotCoordinator>.Instance);

        var act = async () => await coordinator.TryAcquireAsync(
            new RuntimeHostSlotLeaseNamespace("outage-test"),
            "host-1",
            1,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        await act.Should().ThrowAsync<SqlException>();
        coordinator.ActiveDatabaseOperations.Should().Be(0);
    }

    /// <summary>
    /// 在明確 live LocalDB/SQL 上證明 epoch drift 拒絕、同 namespace 槽位上限、fencing token 單調遞增、
    /// stale renew/release 無效、不同 namespace 隔離及 quarantine 到期前不可重用。
    /// </summary>
    [Fact]
    public async Task Live_sql_contract_is_atomic_fenced_quarantined_and_namespace_isolated()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new SqlRuntimeHostSlotCoordinatorOptions
        {
            ConnectionString = connectionString,
            CommandTimeoutSeconds = 5,
            QuarantineSeconds = 1
        };
        var coordinator = new SqlRuntimeHostSlotCoordinator(
            options,
            NullLogger<SqlRuntimeHostSlotCoordinator>.Instance);
        await coordinator.EnsureSchemaAsync(CancellationToken.None);

        var epochNamespace = new RuntimeHostSlotLeaseNamespace(
            "epoch-contract-" + Guid.NewGuid().ToString("N"));
        var epochLease = await coordinator.TryAcquireAsync(
            new RuntimeHostSlotLeaseRequest(
                epochNamespace,
                "epoch-host",
                MaximumRuntimeHosts: 1,
                LeaseTtl: TimeSpan.FromSeconds(5),
                AdmissionEpoch: 7,
                ConfigurationDigest: new string('A', 64)),
            CancellationToken.None);
        epochLease.Should().NotBeNull();

        var drift = async () => await coordinator.TryAcquireAsync(
            new RuntimeHostSlotLeaseRequest(
                epochNamespace,
                "drift-host",
                MaximumRuntimeHosts: 1,
                LeaseTtl: TimeSpan.FromSeconds(5),
                AdmissionEpoch: 7,
                ConfigurationDigest: new string('B', 64)),
            CancellationToken.None);
        await drift.Should().ThrowAsync<SqlException>()
            .Where(exception => exception.Number == 51003);
        await epochLease!.DisposeAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var ns = new RuntimeHostSlotLeaseNamespace("contract-" + suffix);
        var otherNs = new RuntimeHostSlotLeaseNamespace("contract-other-" + suffix);
        var ttl = TimeSpan.FromSeconds(3);

        var attempts = Enumerable.Range(0, 32)
            .Select(index => coordinator.TryAcquireAsync(
                ns,
                "host-" + index,
                maximumRuntimeHosts: 2,
                ttl,
                CancellationToken.None))
            .ToArray();
        var leases = await Task.WhenAll(attempts);
        leases.Count(lease => lease is not null).Should().Be(2);

        var first = leases.First(lease => lease is not null)!;
        var firstToken = first.FencingToken;
        (await coordinator.TryRenewAsync(first, ttl, CancellationToken.None)).Should().BeTrue();
        first.FencingToken.Should().BeGreaterThan(firstToken);

        var stale = new RuntimeHostSlotLease(
            coordinator,
            first.LeaseNamespace,
            first.HostInstanceId,
            firstToken,
            first.ExpiresAtUtc,
            first.SlotOrdinal);
        (await coordinator.TryRenewAsync(stale, ttl, CancellationToken.None)).Should().BeFalse();
        await stale.DisposeAsync();
        (await coordinator.TryRenewAsync(first, ttl, CancellationToken.None)).Should().BeTrue(
            "a stale release must not delete the newer fenced lease");

        var other = await coordinator.TryAcquireAsync(
            otherNs,
            "other-host",
            1,
            ttl,
            CancellationToken.None);
        other.Should().NotBeNull("lease namespaces have independent bounded slots");

        foreach (var lease in leases.OfType<RuntimeHostSlotLease>())
        {
            await lease.DisposeAsync();
        }
        await other!.DisposeAsync();

        var quarantined = await coordinator.TryAcquireAsync(
            ns,
            "replacement-before-quarantine",
            2,
            ttl,
            CancellationToken.None);
        quarantined.Should().BeNull();
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        var replacement = await coordinator.TryAcquireAsync(
            ns,
            "replacement-after-quarantine",
            2,
            ttl,
            CancellationToken.None);
        replacement.Should().NotBeNull();
        replacement!.FencingToken.Should().BeGreaterThan(first.FencingToken);
        await replacement.DisposeAsync();

        coordinator.ActiveDatabaseOperations.Should().Be(0);
    }
}
