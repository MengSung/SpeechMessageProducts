// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/OrganizationAdmissionRegistryTests.cs
// 目的：以 RED／GREEN 測試固定 Canonical Organization 容量登錄、反向碰撞防護、
//       引用計數與確定性釋放語意，避免同一實體 D365 Organization 被錯誤切成多份預算。
//
// 重要安全原則：
// - Canonical key 只能由 ExpectedOrganizationId 與正規化 Organization Base URI 組成。
// - Profile alias、CE 版本、IP、FQDN 與 worker 數量都不能創造新的總容量。
// - 最後一個 registration 釋放後，Manager 與 Host Slot 才能被回收。
// - 所有測試資料均為假的非正式環境值，不含真實 Credential、Token 或 Session。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Organization Admission Registry 只會為一個實體 Organization 建立一份共用容量管理器，
/// 並以 GUID、Base URI、Admission Namespace 與 Lease Namespace 的雙向索引拒絕模糊或衝突設定。
/// 這些測試是 Session／Connection／Memory 隔離的前置門檻：若 Registry 可重複建立 Manager，
/// Central、Local、blue/green 與不同 Profile Generation 便可能同時放大對 CRM 的實際併發。
/// </summary>
public sealed class OrganizationAdmissionRegistryTests
{
    /// <summary>
    /// 證明相同 Canonical Organization 與相同共享容量摘要只會取得同一個 Manager。
    /// Profile alias 與 Transport 世代不參與此判斷，否則 reload 或 8.2／9.1 並存會重複放大預算。
    /// </summary>
    [Fact]
    public async Task Same_canonical_organization_and_digest_share_one_manager()
    {
        await using var registry = CreateRegistry();
        var plan = CreatePlan();

        await using var first = registry.Acquire(plan);
        await using var second = registry.Acquire(plan);

        first.Manager.Should().BeSameAs(second.Manager);
        registry.EntryCount.Should().Be(1);
    }

    /// <summary>
    /// 證明同一 Canonical Organization 若宣告不同的共享容量設定，必須在建立第二個 Manager 前失敗。
    /// 靜默接受衝突會使相同 Host Slot Namespace 同時存在不同 epoch／digest，造成超額流量或錯誤 fencing。
    /// </summary>
    [Fact]
    public async Task Same_canonical_organization_with_different_capacity_digest_fails_closed()
    {
        await using var registry = CreateRegistry();
        await using var registration = registry.Acquire(CreatePlan());
        var conflictingPlan = CreatePlan(aggregateMaxInFlight: 30);

        var act = () => registry.Acquire(conflictingPlan);

        act.Should().Throw<InvalidOperationException>();
        registry.EntryCount.Should().Be(1);
    }

    /// <summary>
    /// 證明 GUID 與 Base URI 都不同、且使用不同命名空間的兩個實體 Organization 可擁有獨立 Manager。
    /// 這是合法的容量隔離，不應因為同一個 Gateway Process 而誤共用 Token、Client 或 Semaphore。
    /// </summary>
    [Fact]
    public async Task Different_canonical_organizations_get_different_managers()
    {
        await using var registry = CreateRegistry();
        await using var first = registry.Acquire(CreatePlan());
        await using var second = registry.Acquire(CreatePlan(
            organizationId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            organizationBaseUri: "https://crm-two.example.test/Contoso/",
            admissionNamespace: "crm-two-admission",
            leaseNamespace: "crm-two-lease"));

        first.Manager.Should().NotBeSameAs(second.Manager);
        registry.EntryCount.Should().Be(2);
    }

    /// <summary>
    /// 證明相同 Organization GUID 不可指向不同 Base URI。
    /// 這種部分碰撞通常代表 virtual directory、環境或 DNS 設定錯誤，若放行會為同一 GUID 建立兩份容量。
    /// </summary>
    [Fact]
    public async Task Same_organization_id_with_different_base_uri_fails_closed()
    {
        await using var registry = CreateRegistry();
        await using var registration = registry.Acquire(CreatePlan());
        var conflictingPlan = CreatePlan(
            organizationBaseUri: "https://crm-one.example.test/OtherVirtualDirectory/",
            admissionNamespace: "other-admission",
            leaseNamespace: "other-lease");

        var act = () => registry.Acquire(conflictingPlan);

        act.Should().Throw<InvalidOperationException>();
        registry.EntryCount.Should().Be(1);
    }

    /// <summary>
    /// 證明相同正規化 Base URI 不可宣告不同 Organization GUID。
    /// 這可避免錯誤 Discovery／WhoAmI 證據在同一端點下建立互相獨立的容量與 Credential Runtime。
    /// </summary>
    [Fact]
    public async Task Same_base_uri_with_different_organization_id_fails_closed()
    {
        await using var registry = CreateRegistry();
        await using var registration = registry.Acquire(CreatePlan());
        var conflictingPlan = CreatePlan(
            organizationId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            admissionNamespace: "other-admission",
            leaseNamespace: "other-lease");

        var act = () => registry.Acquire(conflictingPlan);

        act.Should().Throw<InvalidOperationException>();
        registry.EntryCount.Should().Be(1);
    }

    /// <summary>
    /// 證明一個 Admission 或 Runtime Host Lease Namespace 不可同時綁定兩個 Canonical Organization。
    /// Namespace 是跨 Host 協調的 Durable Identity；重複使用會讓不同 CRM Organization 互相取消或搶占租約。
    /// </summary>
    [Fact]
    public async Task Same_admission_or_lease_namespace_cannot_bind_different_canonical_organizations()
    {
        await using var registry = CreateRegistry();
        await using var registration = registry.Acquire(CreatePlan());
        var conflictingAdmission = CreatePlan(
            organizationId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            organizationBaseUri: "https://crm-four.example.test/",
            admissionNamespace: "crm-one-admission",
            leaseNamespace: "crm-four-lease");
        var conflictingLease = CreatePlan(
            organizationId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            organizationBaseUri: "https://crm-five.example.test/",
            admissionNamespace: "crm-five-admission",
            leaseNamespace: "crm-one-lease");

        var admissionAct = () => registry.Acquire(conflictingAdmission);
        var leaseAct = () => registry.Acquire(conflictingLease);

        admissionAct.Should().Throw<InvalidOperationException>();
        leaseAct.Should().Throw<InvalidOperationException>();
        registry.EntryCount.Should().Be(1);
    }

    /// <summary>
    /// 證明每個 runtime host 的 worker 數量不屬於共享 Organization Capacity Digest。
    /// crm82 與 crm91 可以使用不同 worker pool 大小，但兩者都不得超過同一份 LocalMaxInFlight。
    /// </summary>
    [Fact]
    public void Different_profile_worker_counts_keep_one_shared_capacity_digest()
    {
        var first = CreatePlan(workerCount: 1);
        var second = CreatePlan(workerCount: 3);

        first.ConfigurationDigest.Should().Be(second.ConfigurationDigest);
        first.LocalMaxInFlight.Should().Be(4);
        second.MaximumWorkerInFlightPerHost.Should().BeLessOrEqualTo(second.LocalMaxInFlight);
    }

    /// <summary>
    /// 證明只有最後一個 Registration 釋放後才移除 Registry Entry。
    /// 這可避免一個 Generation 完成 drain 時，錯誤 Dispose 仍被另一個 Generation 使用的 Manager 與 Host Slot。
    /// </summary>
    [Fact]
    public async Task Last_registration_disposes_manager_and_removes_registry_entry()
    {
        await using var registry = CreateRegistry();
        var first = registry.Acquire(CreatePlan());
        var second = registry.Acquire(CreatePlan());

        await first.DisposeAsync();
        registry.EntryCount.Should().Be(1);

        await second.DisposeAsync();
        registry.EntryCount.Should().Be(0);
    }

    /// <summary>
    /// 證明 Registry Shutdown 可安全重複呼叫，且會清除所有 Entry 並拒絕新的 Registration。
    /// 重複 Dispose 不可再次釋放 Manager、Semaphore 或 Host Slot，以免產生 use-after-dispose 與例外遺失。
    /// </summary>
    [Fact]
    public async Task Registry_shutdown_is_idempotent_and_disposes_every_remaining_manager_once()
    {
        var registry = CreateRegistry();
        _ = registry.Acquire(CreatePlan());
        _ = registry.Acquire(CreatePlan(
            organizationId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            organizationBaseUri: "https://crm-six.example.test/",
            admissionNamespace: "crm-six-admission",
            leaseNamespace: "crm-six-lease"));

        await registry.DisposeAsync();
        await registry.DisposeAsync();

        registry.EntryCount.Should().Be(0);
        var act = () => registry.Acquire(CreatePlan());
        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    /// 建立測試用 Registry。In-memory coordinator 只模擬 Host Slot，不建立網路連線；
    /// NullLogger 也不保存要求、Token 或 Credential，確保測試只觀察容量與生命週期契約。
    /// </summary>
    private static OrganizationAdmissionRegistry CreateRegistry()
        => new(
            new InMemoryRuntimeHostSlotCoordinator(),
            NullLogger<OrganizationAdmissionRegistry>.Instance,
            NullLogger<OrganizationAdmissionManager>.Instance);

    /// <summary>
    /// 建立一份完整有效的 Admission Plan，允許單一測試精確改變 Canonical Identity、Namespace、
    /// 共享容量或 Profile-local Socket 上限，而不把其他無關欄位帶入斷言。
    /// </summary>
    private static OrganizationAdmissionPlan CreatePlan(
        Guid? organizationId = null,
        string organizationBaseUri = "https://crm-one.example.test/Contoso/",
        string admissionNamespace = "crm-one-admission",
        string leaseNamespace = "crm-one-lease",
        int aggregateMaxInFlight = 24,
        int workerCount = 2)
    {
        var admissionOptions = new OrganizationAdmissionOptions
        {
            ExpectedOrganizationId = organizationId
                ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
            AggregateMaxInFlight = aggregateMaxInFlight,
            MaximumRuntimeHosts = 6,
            LocalQueueCapacity = 12,
            MaxInFlightAndQueuedPerWorkload = 4,
            QueueAdmissionTimeoutSeconds = 5,
            MaxDispatchEnvelopeBytes = 65_536,
            AdmissionNamespaceId = admissionNamespace,
            LeaseNamespaceId = leaseNamespace,
            AdmissionEpoch = 1,
            RuntimeHostSlotLeaseTtlSeconds = 120,
            RuntimeHostSlotRenewalIntervalSeconds = 30,
            RuntimeHostSlotExpiryFenceSeconds = 10,
            MaximumOutboundWorkLifetimeSeconds = 35,
            ShutdownDrainTimeoutSeconds = 45,
            RequireDurableHostCoordinator = false
        };

        OrganizationAdmissionPlan.TryCreate(
                organizationBaseUri,
                workerCount,
                maxInFlightPerWorker: 1,
                admissionOptions,
                out var plan,
                out var error)
            .Should().BeTrue(error?.ErrorMessage);
        return plan!;
    }
}
