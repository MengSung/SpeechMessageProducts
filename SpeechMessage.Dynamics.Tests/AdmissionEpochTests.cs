using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 AdmissionEpoch 與不可變 configuration digest 的 fencing 契約。
/// 容量或 lease 政策改變必須產生不同摘要；宣稱 durable 卻無法原子驗證 epoch 的 coordinator 必須阻擋 readiness。
/// </summary>
public sealed class AdmissionEpochTests
{
    /// <summary>
    /// 相同輸入必須產生穩定摘要，任何容量/epoch 漂移則必須產生不同摘要，避免舊主機靜默共用新容量。
    /// </summary>
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

    /// <summary>
    /// 注入只能持久化租約、卻不支援 epoch fencing 的 coordinator，預期 manager fail-closed 而不是降級上線。
    /// </summary>
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
        var admissionOptions = new OrganizationAdmissionOptions
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
        };

        OrganizationAdmissionPlan.TryCreate(
                "https://crm.example.local/Epoch/",
                workerCount: 1,
                maxInFlightPerWorker: 1,
                admissionOptions,
                out var plan,
                out var error)
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
