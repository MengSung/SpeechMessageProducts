using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.Tests;

public sealed class SqlRuntimeHostSlotCoordinatorTests
{
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

    [Fact]
    public void Schema_is_scoped_to_the_standalone_control_plane_database()
    {
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("RuntimeHostSlotLease");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("RuntimeHostFencingSequence");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("SYSUTCDATETIME()");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().NotContain("MSCRM_CONFIG");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().NotContain("OrganizationBase");
    }

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
