// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ProductModeOptionsTests.cs
// 目的：確認產品 JSON 模型能表達 Gateway / Embedded 二選一。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.Tests;

public sealed class ProductModeOptionsTests
{
    [Fact]
    public void Gateway_mode_options_only_require_endpoint_and_alias()
    {
        var options = new ProductDynamicsOptions
        {
            ExecutionMode = DynamicsExecutionMode.Gateway,
            ProfileAlias = "jesus-prod",
            Gateway = new GatewayModeOptions
            {
                Endpoint = "https://dynamics-gateway.internal/"
            }
        };

        options.ExecutionMode.Should().Be(DynamicsExecutionMode.Gateway);
        options.Gateway!.Endpoint.Should().StartWith("https://");
        options.Embedded.Should().BeNull();
    }

    [Fact]
    public void Embedded_mode_options_require_webapi_and_secret_reference()
    {
        var options = new ProductDynamicsOptions
        {
            ExecutionMode = DynamicsExecutionMode.Embedded,
            ProfileAlias = "jesus-dev",
            Embedded = new EmbeddedModeOptions
            {
                OrganizationWebApiBaseUri = "https://crm.example.local/api/data/v8.2/",
                CeVersion = "8.2",
                SecretReference = "kv-dynamics-dev",
                ManifestOrRegistrySource = "dev-manifest.json"
            }
        };

        options.ExecutionMode.Should().Be(DynamicsExecutionMode.Embedded);
        options.Embedded!.CeVersion.Should().Be("8.2");
        options.Gateway.Should().BeNull();
    }
}