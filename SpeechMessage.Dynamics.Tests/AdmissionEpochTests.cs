using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class AdmissionEpochTests
{
    [Fact]
    public void Admission_configuration_digest_is_stable_and_capacity_sensitive()
    {
        var first = CreatePlan(epoch: 7, aggregate: 24, hosts: 6);
        var same = CreatePlan(epoch: 7, aggregate: 24, hosts: 6);
        var changed = CreatePlan(epoch: 8, aggregate: 20, hosts: 5);

        first.AdmissionEpoch.Should().Be(7);
        first.ConfigurationDigest.Should().MatchRegex("^[A-F0-9]{64}$");
        same.ConfigurationDigest.Should().Be(first.ConfigurationDigest);
        changed.ConfigurationDigest.Should().NotBe(first.ConfigurationDigest);
    }

    [Fact]
    public async Task Durable_readiness_rejects_coordinator_without_epoch_fencing()
    {
        var plan = CreatePlan(epoch: 1, aggregate: 2, hosts: 1, requireDurable: true);
        await using var manager = new OrganizationAdmissionManager(
            plan,
            new DurableButEpochBlindCoordinator(),
            NullLogger<OrganizationAdmissionManager>.Instance);

        var act = () => manager.EnsureHostSlotAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AdmissionEpoch*");
    }

    private static OrganizationAdmissionPlan CreatePlan(
        long epoch,
        int aggregate,
        int hosts,
        bool requireDurable = false)
    {
        var options = new DynamicsWebApiOptions
        {
            OrganizationWebApiBaseUri = "https://crm.example.local/api/data/v9.1/",
            MaxConnectionsPerServer = 1,
            Admission = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("34343434-3434-3434-3434-343434343434"),
                AggregateMaxInFlight = aggregate,
                MaximumRuntimeHosts = hosts,
                LocalQueueCapacity = 4,
                MaxInFlightAndQueuedPerWorkload = 2,
                AdmissionNamespaceId = "epoch-test-admission",
                LeaseNamespaceId = "epoch-test-lease",
                AdmissionEpoch = epoch,
                RequireDurableHostCoordinator = requireDurable
            }
        };

        OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out var error)
            .Should().BeTrue(error?.ErrorMessage);
        return plan!;
    }

    private sealed class DurableButEpochBlindCoordinator : IRuntimeHostSlotCoordinator
    {
        public bool IsDurable => true;

        public Task<RuntimeHostSlotLease?> TryAcquireAsync(
            RuntimeHostSlotLeaseNamespace leaseNamespace,
            string hostInstanceId,
            int maximumRuntimeHosts,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
            => Task.FromResult<RuntimeHostSlotLease?>(null);

        public Task<bool> TryRenewAsync(
            RuntimeHostSlotLease lease,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
            => Task.FromResult(false);

        public ValueTask ReleaseAsync(
            RuntimeHostSlotLease lease,
            CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
