using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Guard;
using SpeechMessage.Dynamics.Embedded;
using SpeechMessage.Dynamics.Embedded.DependencyInjection;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Embedded 主機不可因省略 HTTP 而略過既有 Dynamics 控制面。
/// 測試以可計數的受控 executor 取代下游 runtime；因此可以明確證明 RequestGuard 拒絕時，
/// admission、connector pool 與 client 尚未被接觸，防止請求覆寫演變為跨 Profile session 或 permit 洩漏。
/// </summary>
public sealed class EmbeddedHostAdapterTests
{
    /// <summary>
    /// 保護 Embedded 不依賴 Gateway endpoint 的契約。故障注入是完全不設定 Gateway；
    /// 決定性斷言為 DI 可建立並解析同一個 stateless adapter，且未建立 HTTP handler、timer 或背景工作。
    /// </summary>
    [Fact]
    public void Registration_allows_embedded_without_gateway_endpoint()
    {
        var services = new ServiceCollection();
        var controlled = new RecordingExecutor();
        var guard = new RequestGuard(["approved.read"]);

        services.AddSpeechMessageDynamicsEmbedded(
            new ProductDynamicsOptions
            {
                ConnectionMode = ConnectionMode.Embedded,
                ProfileAlias = "sunnyvalechback"
            },
            guard,
            controlled);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        provider.GetRequiredService<IDynamicsOperationExecutor>()
            .Should().BeOfType<EmbeddedHostAdapter>();
    }

    /// <summary>
    /// 保護組合根可由 DI factory 延後取得 Guard 與受控 executor，而不把 runtime、profile、credential 或
    /// connector client 直接交給產品。故障模型是 factory 在註冊期提早建立外部資源，或解析時遞迴建立第二個
    /// adapter；決定性斷言是兩個 factory 僅在 singleton adapter 首次解析時各執行一次，且通過 Guard 的操作
    /// 只委派一次。此測試全程使用純記憶體替身，不建立 Data8 client、WCF channel、HTTP、timer 或 session。
    /// </summary>
    [Fact]
    public async Task Factory_registration_lazily_composes_one_embedded_adapter_without_a_gateway_endpoint()
    {
        var services = new ServiceCollection();
        var controlled = new RecordingExecutor();
        var guardFactoryCalls = 0;
        var executorFactoryCalls = 0;

        services.AddSpeechMessageDynamicsEmbedded(
            new ProductDynamicsOptions
            {
                ConnectionMode = ConnectionMode.Embedded,
                ProfileAlias = "sunnyvalechback"
            },
            _ =>
            {
                guardFactoryCalls++;
                return new RequestGuard(["approved.read"]);
            },
            _ =>
            {
                executorFactoryCalls++;
                return controlled;
            });

        guardFactoryCalls.Should().Be(0);
        executorFactoryCalls.Should().Be(0);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var adapter = provider.GetRequiredService<IDynamicsOperationExecutor>();
        var result = await adapter.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "sunnyvalechback",
            CapabilityOperationId = "approved.read",
            WorkloadSubjectId = "churchreport-development"
        });

        result.Succeeded.Should().BeTrue();
        guardFactoryCalls.Should().Be(1);
        executorFactoryCalls.Should().Be(1);
        controlled.CallCount.Should().Be(1);
    }

    /// <summary>
    /// 保護保留 routing 參數的 fail-closed 順序。故障注入為 <c>endpoint</c>；
    /// 決定性斷言為回傳固定 guard 錯誤且下游 executor 呼叫數仍為零，表示尚未取得 permit 或 client。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_rejects_reserved_parameters_before_controlled_executor()
    {
        var controlled = new RecordingExecutor();
        var adapter = new EmbeddedHostAdapter(
            new RequestGuard(["approved.read"]),
            controlled,
            "sunnyvalechback");

        var result = await adapter.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "sunnyvalechback",
            CapabilityOperationId = "approved.read",
            WorkloadSubjectId = "churchreport-development",
            Parameters = new Dictionary<string, object?>
            {
                ["endpoint"] = "https://forbidden.example/"
            }
        });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("request.reserved-parameter");
        controlled.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護固定 Profile 的隔離契約。故障注入為另一個格式合法、也可通過共用 Guard 的 alias；
    /// 決定性斷言為 Adapter 在進入下游前拒絕，故不同 Organization 不能因同一 Embedded singleton
    /// 借到不相符的 admission permit、generation client 或既有 session。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_rejects_a_valid_but_different_profile_before_controlled_executor()
    {
        var controlled = new RecordingExecutor();
        var adapter = new EmbeddedHostAdapter(
            new RequestGuard(["approved.read"]),
            controlled,
            "sunnyvalechback");

        var result = await adapter.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "elijah",
            CapabilityOperationId = "approved.read",
            WorkloadSubjectId = "churchreport-development"
        });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("request.invalid-profile-alias");
        controlled.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護已通過 Guard 的請求只被委派一次且原始 cancellation token 不被替換。
    /// 此案例避免 adapter 保存 request 或建立自行管理的 CTS，確保下游 pool 的 deadline、drain 與 dispose
    /// 仍是唯一生命週期 owner。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_delegates_an_approved_request_once_without_retaining_it()
    {
        var controlled = new RecordingExecutor();
        var adapter = new EmbeddedHostAdapter(
            new RequestGuard(["approved.read"]),
            controlled,
            "sunnyvalechback");
        using var cancellation = new CancellationTokenSource();

        var result = await adapter.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "sunnyvalechback",
            CapabilityOperationId = "approved.read",
            WorkloadSubjectId = "churchreport-development"
        }, cancellation.Token);

        result.Succeeded.Should().BeTrue();
        controlled.CallCount.Should().Be(1);
        controlled.LastCancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// 最小化的下游受控 executor。它不建立 connection、permit、timer 或 task cache，只記錄這個測試
    /// 用來驗證的 bounded scalar，避免測試自身成為資源洩漏來源。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        /// <summary>取得已通過 Guard 而實際到達下游的呼叫數。</summary>
        public int CallCount { get; private set; }

        /// <summary>取得最後一次透傳的 cancellation token；此值不會被保存至測試結束以外。</summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <summary>以成功純值回應，模擬既有 ControlPlane executor 的受控回傳契約。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            CallCount++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(OperationExecutionResult.Success(data: null));
        }
    }
}
