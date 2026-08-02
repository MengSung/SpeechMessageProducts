using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Gateway `/ready` 只反映能安全容納 outbound 工作的 host-slot lease 狀態。
/// readiness 回應同時必須 NoStore，避免代理或瀏覽器快取過期的可用狀態而把流量送往已失去 lease 的主機。
/// </summary>
public sealed class GatewayReadinessTests
{
    /// <summary>
    /// 使用 Multi-Profile snapshot 同時覆蓋 Manager readiness 與任一 Profile Host Slot NotReady；
    /// 只有 Catalog 已初始化且所有 Active Profile 都在安全 Lease Window 內，Gateway 才可接收流量。
    /// </summary>
    [Theory]
    [InlineData(true, true, HttpStatusCode.OK)]
    [InlineData(false, true, HttpStatusCode.ServiceUnavailable)]
    [InlineData(true, false, HttpStatusCode.ServiceUnavailable)]
    public async Task Ready_endpoint_aggregates_manager_and_profile_host_slot_state(
        bool managerReady,
        bool allProfilesReady,
        HttpStatusCode expectedStatus)
    {
        await using var factory = CreateFactory(
            new StubRuntimeManager(managerReady, allProfilesReady));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(expectedStatus);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    /// <summary>
    /// 證明 readiness 回應只包含 Alias、Generation、狀態與 bounded 容量指標，
    /// 不輸出 Endpoint、Canonical URI、Secret Reference、Credential、Token 或 Request Identity。
    /// </summary>
    [Fact]
    public async Task Ready_endpoint_exposes_only_redacted_multi_profile_diagnostics()
    {
        await using var factory = CreateFactory(new StubRuntimeManager(true, true));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ready");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.Should().Contain("crm82");
        json.Should().Contain("crm91");
        var normalizedJson = json.ToLowerInvariant();
        normalizedJson.Should().NotContain("example.test");
        normalizedJson.Should().NotContain("secret");
        normalizedJson.Should().NotContain("credential");
        normalizedJson.Should().NotContain("token");
        document.RootElement.TryGetProperty("profiles", out var profiles).Should().BeTrue();
        profiles.GetArrayLength().Should().Be(2);
    }

    /// <summary>
    /// 建立 Testing Gateway 並以受控 Stub Runtime Manager 取代正式 Manager；Stub 不連 CRM、SQL、ADFS 或 Secret Store。
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactory(IDynamicsProfileRuntimeManager manager)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    // Readiness route 不需 workload authorization，但 startup validator 仍必須驗證一個隔離、非空的 Testing set；
                    // reserved principal 不會由任何 handler 建立，也不接觸 executor、admission、secret 或外部 socket。
                    ["DynamicsGateway:ActiveWorkloadBindingSet"] = "Testing",
                    ["DynamicsGateway:WorkloadBindingSets:Testing:0:PrincipalName"] =
                        @"SPEECHMESSAGE\ReadinessBaseline$",
                    ["DynamicsGateway:WorkloadBindingSets:Testing:0:WorkloadSubjectId"] =
                        "readiness-baseline-service",
                    ["DynamicsGateway:WorkloadBindingSets:Testing:0:ProfileAliases:0"] = "crm82",
                    ["DynamicsGateway:WorkloadBindingSets:Testing:0:CapabilityOperationIds:0"] =
                        "runtime.health.whoami"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDynamicsProfileRuntimeManager>();
                services.RemoveAll<IProfileExecutionLeaseProvider>();
                services.AddSingleton<IDynamicsProfileRuntimeManager>(manager);
                services.AddSingleton<IProfileExecutionLeaseProvider>(manager);
            });
        });

    /// <summary>
    /// 只提供不含秘密的 Multi-Profile readiness snapshot；所有執行、替換與租約取得路徑都禁止使用，
    /// 避免 readiness 測試意外接觸 Transport、Token、Credential、Admission Queue 或背景工作。
    /// </summary>
    private sealed class StubRuntimeManager : IDynamicsProfileRuntimeManager
    {
        private readonly DynamicsProfileRuntimeManagerSnapshot _snapshot;

        /// <summary>建立包含 crm82 與 crm91 的固定快照；第二個 Profile 可模擬 Host Slot NotReady。</summary>
        public StubRuntimeManager(bool managerReady, bool allProfilesReady)
        {
            _snapshot = new DynamicsProfileRuntimeManagerSnapshot
            {
                IsReady = managerReady,
                Profiles =
                [
                    CreateProfileSnapshot("crm82", 1, hostSlotReady: true),
                    CreateProfileSnapshot("crm91", 3, hostSlotReady: allProfilesReady)
                ]
            };
        }

        /// <summary>Readiness Stub 已在 Constructor 建立固定快照，因此初始化立即完成且不建立 Runtime、Host Slot 或背景 Task。</summary>
        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        /// <summary>Readiness 測試禁止替換 Runtime；若端點意外呼叫此路徑立即失敗，以揭露生命週期範圍擴張。</summary>
        public Task ReplaceAsync(
            DynamicsProfileDefinition definition,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Readiness stub does not replace runtimes.");

        /// <summary>Readiness 測試禁止執行 Dynamics 操作，避免接觸 Transport、Token、Credential 或外部網路。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Readiness stub does not execute Dynamics operations.");

        /// <summary>Stub 不提供 Admission Plan，確保 readiness 只能消費 bounded snapshot，不能繞過 Manager 查詢內部容量設定。</summary>
        public bool TryGetAdmissionPlan(
            string profileAlias,
            out OrganizationAdmissionPlan? admissionPlan)
        {
            admissionPlan = null;
            return false;
        }

        /// <summary>Stub 禁止取得 Permit 或 Runtime Lease；任何呼叫都代表 readiness 實作錯誤進入了會改變容量的路徑。</summary>
        public Task<ProfileExecutionLeaseAcquireResult> AcquireAsync(
            DispatchEnvelope envelope,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Readiness stub does not acquire execution leases.");

        /// <summary>回傳 Constructor 建立的非秘密固定快照；不暴露 Runtime、Client、Token 或可變集合 ownership。</summary>
        public DynamicsProfileRuntimeManagerSnapshot GetSnapshot() => _snapshot;

        /// <summary>Stub 沒有任何可釋放資源。</summary>
        public void Dispose()
        {
        }

        /// <summary>Stub 沒有任何非同步背景工作或資源。</summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// 建立一筆不含 Endpoint、Secret 或 Token 的 Profile readiness 快照。
        /// Canonical URI 只存在於內部 Key，端點測試會證明 HTTP 回應沒有序列化該欄位。
        /// </summary>
        private static DynamicsProfileRuntimeSnapshot CreateProfileSnapshot(
            string alias,
            long generation,
            bool hostSlotReady)
            => new()
            {
                Key = new ProfileRuntimeKey(
                    alias,
                    generation,
                    alias == "crm82" ? "8.2" : "9.1",
                    new CanonicalOrganizationCapacityKey(
                        alias == "crm82"
                            ? Guid.Parse("82828282-8282-8282-8282-828282828282")
                            : Guid.Parse("91919191-9191-9191-9191-919191919191"),
                        $"https://{alias}.example.test/")),
                State = DynamicsProfileRuntimeState.Active,
                ActiveExecutionCount = 0,
                Admission = new AdmissionMetricsSnapshot
                {
                    LocalMaxInFlight = 1,
                    InFlight = 0,
                    Queued = 0,
                    LocalQueueCapacity = 4,
                    AcceptedCount = 0,
                    RejectedCount = 0,
                    TimeoutCount = 0,
                    HostSlotReady = hostSlotReady,
                    HostFencingToken = hostSlotReady ? 1 : 0,
                    HostLeaseExpiresAtUtc = hostSlotReady
                        ? DateTimeOffset.UtcNow.AddMinutes(1)
                        : null,
                    ActivePermits = 0,
                    RenewalLoopActive = hostSlotReady,
                    TrackedWorkloadCount = 0
                }
            };
    }
}
