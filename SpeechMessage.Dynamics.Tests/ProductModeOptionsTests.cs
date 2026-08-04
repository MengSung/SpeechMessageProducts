// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ProductModeOptionsTests.cs
// 用途：驗證產品端三種 ConnectionMode 的設定邊界與 Gateway HTTP 參數上限。
//
// 測試僅建立 DI 設定驗證器，不會送出 HTTP、建立 CRM Session 或建立任何 Worker。using provider
// 的區塊確保 Options/HttpClientFactory 若已配置，也會隨測試結束被決定性釋放。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證產品 JSON 僅能在啟動時選擇連線模式，並確認 HTTP 型模式的 Endpoint 不可被用作
/// 原始 CRM endpoint。這是產品與 Connector/Pool/憑證隔離的第一層防線。
/// </summary>
public sealed class ProductModeOptionsTests
{
    /// <summary>
    /// 保護 Dedicated Gateway 與 Central Gateway 均可使用同一產品 HTTP client 契約；差異只
    /// 在部署位置，不會讓產品持有不同的 CRM Session 或連線池。
    /// </summary>
    [Theory]
    [InlineData(ConnectionMode.DedicatedGateway, "https://localhost:7244/")]
    [InlineData(ConnectionMode.CentralGateway, "https://dynamics-gateway.internal/")]
    public void Gateway_modes_accept_safe_https_endpoint(ConnectionMode connectionMode, string endpoint)
    {
        var options = ResolveGatewayOptions(connectionMode, endpoint);

        options.ConnectionMode.Should().Be(connectionMode);
        options.Gateway!.Endpoint.Should().Be(endpoint);
    }

    /// <summary>
    /// 保護 Embedded 不會誤用 Gateway ProductClient。Embedded 將在自己的 Host Adapter 建立
    /// 相同 Guard/Resolver/Admission 鏈；在 P4 前該 Adapter 仍 fail-closed，不能藉由 HTTP
    /// 型註冊繞過其信任與資源生命週期驗證。
    /// </summary>
    [Fact]
    public void Gateway_product_client_rejects_embedded_mode()
    {
        var act = () => ResolveGatewayOptions(ConnectionMode.Embedded, "https://localhost:7244/");

        act.Should().Throw<OptionsValidationException>();
    }

    /// <summary>保護 Alias 不可成為無界或 URI 型 routing key。</summary>
    [Theory]
    [InlineData("crm 91")]
    [InlineData("crm/91")]
    [InlineData("crm:91")]
    public void Gateway_modes_reject_unsafe_profile_aliases(string profileAlias)
    {
        var act = () => ResolveGatewayOptions(
            ConnectionMode.DedicatedGateway,
            "https://localhost:7244/",
            mutate: options => options.ProfileAlias = profileAlias);

        act.Should().Throw<OptionsValidationException>();
    }

    /// <summary>
    /// 保護產品端只允許 Gateway 自己的 HTTPS root，不能將 CRM Organization Service 或 Web API
    /// URL 當作 Endpoint，避免繞過 server-owned Profile、Credential 與 Connector 選擇。
    /// </summary>
    [Theory]
    [InlineData("http://localhost:7244/")]
    [InlineData("https://user:password@localhost:7244/")]
    [InlineData("https://localhost:7244/?target=https://crm.example/")]
    [InlineData("https://crm.example/XRMServices/2011/Organization.svc")]
    [InlineData("https://crm.example/api/data/v9.1/")]
    public void Gateway_modes_reject_unsafe_or_raw_crm_endpoints(string endpoint)
    {
        var act = () => ResolveGatewayOptions(ConnectionMode.DedicatedGateway, endpoint);

        act.Should().Throw<OptionsValidationException>();
    }

    /// <summary>
    /// 保護 bounded response 與 timeout 的限制在 DI 啟動時就 fail-closed，避免錯誤設定產生
    /// 無界 Buffer、長期 Socket 等待或無法回收的 request CTS。
    /// </summary>
    [Theory]
    [InlineData(1023, 35)]
    [InlineData(2_097_152, 0)]
    [InlineData(2_097_152, 601)]
    public void Gateway_modes_reject_response_or_timeout_outside_safe_bounds(
        int maximumResponseBytes,
        int requestTimeoutSeconds)
    {
        var act = () => ResolveGatewayOptions(
            ConnectionMode.DedicatedGateway,
            "https://localhost:7244/",
            mutate: options =>
            {
                options.Gateway!.MaxResponseBytes = maximumResponseBytes;
                options.Gateway.RequestTimeoutSeconds = requestTimeoutSeconds;
            });

        act.Should().Throw<OptionsValidationException>();
    }

    /// <summary>
    /// 以可釋放 ServiceProvider 觸發實際 options validation。helper 不快取 provider、options 或
    /// HttpClient，因此不同測試與 Profile 不會共享可變 DI/Session 狀態。
    /// </summary>
    private static ProductDynamicsOptions ResolveGatewayOptions(
        ConnectionMode connectionMode,
        string endpoint,
        Action<ProductDynamicsOptions>? mutate = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSpeechMessageDynamicsGatewayProductClient(options =>
        {
            options.ConnectionMode = connectionMode;
            options.ProfileAlias = "sunnyvalechback";
            options.Gateway = new GatewayEndpointOptions
            {
                Endpoint = endpoint,
                ApiPrefix = "/v1"
            };
            mutate?.Invoke(options);
        });

        using var provider = services.BuildServiceProvider(validateScopes: true);
        return provider.GetRequiredService<IOptions<ProductDynamicsOptions>>().Value;
    }
}
