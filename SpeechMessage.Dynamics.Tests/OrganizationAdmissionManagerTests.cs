// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs
// 目的：驗證 bounded admission、workload cap、permit 釋放、host slot 上限。
//
// 保母教學：
// - 這些測試不連真實 CRM。
// - 重點是「不會無限排隊、不會 slot 洩漏、不會被單一 workload 塞爆」。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class OrganizationAdmissionManagerTests
{
    [Fact]
    public void Plan_derives_local_max_in_flight()
    {
        var options = new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "9.1",
            MaxConnectionsPerServer = 2,
            Admission = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                AggregateMaxInFlight = 24,
                MaximumRuntimeHosts = 6,
                AdmissionNamespaceId = "a",
                LeaseNamespaceId = "b"
            }
        };

        OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out var error)
            .Should().BeTrue(error?.ErrorMessage);
        plan!.LocalMaxInFlight.Should().Be(4);
    }

    [Fact]
    public async Task Queue_full_is_rejected_without_unbounded_growth()
    {
        await using var manager = CreateManager(
            aggregate: 2,
            hosts: 2, // LocalMaxInFlight = 1
            queueCapacity: 1,
            perWorkload: 10,
            timeoutSeconds: 1);

        var first = await manager.AcquireAsync(CreateEnvelope("w1"), CancellationToken.None);
        first.Succeeded.Should().BeTrue();

        // second waits in queue (capacity 1)
        using var secondCts = new CancellationTokenSource();
        var secondTask = manager.AcquireAsync(CreateEnvelope("w2"), secondCts.Token);

        // give second a moment to enter queue
        await Task.Delay(50);

        var third = await manager.AcquireAsync(CreateEnvelope("w3"), CancellationToken.None);
        third.Succeeded.Should().BeFalse();
        third.Error!.ErrorCode.Should().Be(DynamicsErrorCodes.QueueFull);

        // cleanup
        secondCts.Cancel();
        var second = await secondTask;
        second.Succeeded.Should().BeFalse();
        await first.Permit!.DisposeAsync();

        var snap = manager.GetSnapshot();
        snap.InFlight.Should().Be(0);
        snap.Queued.Should().Be(0);
    }

    [Fact]
    public async Task Workload_cap_prevents_one_product_from_filling_queue()
    {
        await using var manager = CreateManager(
            aggregate: 10,
            hosts: 5, // LocalMaxInFlight = 2
            queueCapacity: 20,
            perWorkload: 2,
            timeoutSeconds: 1);

        var p1 = await manager.AcquireAsync(CreateEnvelope("church-report"), CancellationToken.None);
        var p2 = await manager.AcquireAsync(CreateEnvelope("church-report"), CancellationToken.None);
        p1.Succeeded.Should().BeTrue();
        p2.Succeeded.Should().BeTrue();

        var p3 = await manager.AcquireAsync(CreateEnvelope("church-report"), CancellationToken.None);
        p3.Succeeded.Should().BeFalse();
        p3.Error!.ErrorCode.Should().Be(DynamicsErrorCodes.WorkloadCapExceeded);

        // other workload can still enter
        var other = await manager.AcquireAsync(CreateEnvelope("other-product"), CancellationToken.None);
        // may queue/timeout depending on in-flight; at least should not be workload-cap for other
        if (!other.Succeeded)
        {
            other.Error!.ErrorCode.Should().NotBe(DynamicsErrorCodes.WorkloadCapExceeded);
        }
        else
        {
            await other.Permit!.DisposeAsync();
        }

        await p1.Permit!.DisposeAsync();
        await p2.Permit!.DisposeAsync();
    }

    [Fact]
    public async Task Permit_release_returns_capacity()
    {
        await using var manager = CreateManager(
            aggregate: 2,
            hosts: 2, // LocalMaxInFlight=1
            queueCapacity: 0,
            perWorkload: 5,
            timeoutSeconds: 1);

        var first = await manager.AcquireAsync(CreateEnvelope("w1"), CancellationToken.None);
        first.Succeeded.Should().BeTrue();

        var blocked = await manager.AcquireAsync(CreateEnvelope("w2"), CancellationToken.None);
        blocked.Succeeded.Should().BeFalse();
        blocked.Error!.ErrorCode.Should().BeOneOf(
            DynamicsErrorCodes.QueueFull,
            DynamicsErrorCodes.AdmissionTimeout);

        await first.Permit!.DisposeAsync();

        var second = await manager.AcquireAsync(CreateEnvelope("w2"), CancellationToken.None);
        second.Succeeded.Should().BeTrue();
        await second.Permit!.DisposeAsync();

        manager.GetSnapshot().InFlight.Should().Be(0);
    }

    [Fact]
    public async Task Host_slot_limit_is_enforced_by_in_memory_coordinator()
    {
        var coordinator = new InMemoryRuntimeHostSlotCoordinator();
        var ns = new RuntimeHostSlotLeaseNamespace("shared-lease");

        var l1 = await coordinator.TryAcquireAsync(ns, "host-1", maximumRuntimeHosts: 1, TimeSpan.FromMinutes(1), CancellationToken.None);
        var l2 = await coordinator.TryAcquireAsync(ns, "host-2", maximumRuntimeHosts: 1, TimeSpan.FromMinutes(1), CancellationToken.None);

        l1.Should().NotBeNull();
        l2.Should().BeNull();

        await l1!.DisposeAsync();
        var l3 = await coordinator.TryAcquireAsync(ns, "host-2", maximumRuntimeHosts: 1, TimeSpan.FromMinutes(1), CancellationToken.None);
        l3.Should().NotBeNull();
        await l3!.DisposeAsync();
    }

    private static OrganizationAdmissionManager CreateManager(
        int aggregate,
        int hosts,
        int queueCapacity,
        int perWorkload,
        int timeoutSeconds)
    {
        var options = new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "8.2",
            MaxConnectionsPerServer = 1,
            Admission = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                AggregateMaxInFlight = aggregate,
                MaximumRuntimeHosts = hosts,
                LocalQueueCapacity = queueCapacity,
                MaxInFlightAndQueuedPerWorkload = perWorkload,
                QueueAdmissionTimeoutSeconds = timeoutSeconds,
                MaxDispatchEnvelopeBytes = 65536,
                AdmissionNamespaceId = "unit-admission",
                LeaseNamespaceId = "unit-lease",
                RequireDurableHostCoordinator = false
            }
        };

        OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out var error)
            .Should().BeTrue(error?.ErrorMessage);

        return new OrganizationAdmissionManager(
            plan!,
            new InMemoryRuntimeHostSlotCoordinator(),
            NullLogger<OrganizationAdmissionManager>.Instance);
    }

    private static DispatchEnvelope CreateEnvelope(string workload)
        => new()
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = workload,
            TemplateId = "WhoAmI",
            TemplateHash = new string('a', 64),
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
            EstimatedEnvelopeBytes = 512
        };
}
