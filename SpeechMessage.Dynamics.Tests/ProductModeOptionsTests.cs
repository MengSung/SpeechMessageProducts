// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ProductModeOptionsTests.cs
// 目的：確認產品 JSON 模型能表達 Gateway / Embedded 二選一。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;

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
    public void Embedded_mode_options_expose_only_deferred_trust_bindings()
    {
        var options = new ProductDynamicsOptions
        {
            ExecutionMode = DynamicsExecutionMode.Embedded,
            ProfileAlias = "jesus-dev",
            Embedded = new EmbeddedModeOptions
            {
                ProductProfileBinding = "church-report-membership",
                OrganizationAdmissionCoordinatorRef = "dynamics-admission-development"
            }
        };

        options.ExecutionMode.Should().Be(DynamicsExecutionMode.Embedded);
        options.Embedded!.ProductProfileBinding.Should().Be("church-report-membership");
        typeof(EmbeddedModeOptions).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(EmbeddedModeOptions.ProductProfileBinding),
                nameof(EmbeddedModeOptions.OrganizationAdmissionCoordinatorRef));
        options.Gateway.Should().BeNull();
    }

    [Theory]
    [InlineData("https://dynamics-gateway.internal/")]
    [InlineData("https://localhost:7244/")]
    public void Gateway_mode_accepts_central_and_local_https_endpoints(string endpoint)
    {
        var options = ResolveGatewayOptions(endpoint);

        options.Gateway!.Endpoint.Should().Be(endpoint);
    }

    [Fact]
    public void Gateway_mode_exposes_a_bounded_response_size_setting()
    {
        typeof(GatewayModeOptions)
            .GetProperty("MaxResponseBytes")
            .Should()
            .NotBeNull();
    }

    [Theory]
    [InlineData("crm 91")]
    [InlineData("crm/91")]
    [InlineData("crm:91")]
    public void Gateway_mode_rejects_unsafe_profile_aliases(string profileAlias)
    {
        var act = () => ResolveGatewayOptions(
            "https://localhost:7244/",
            mutate: options => options.ProfileAlias = profileAlias);

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Gateway_mode_rejects_profile_alias_longer_than_128_characters()
    {
        var act = () => ResolveGatewayOptions(
            "https://localhost:7244/",
            mutate: options => options.ProfileAlias = new string('a', 129));

        act.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(8_388_609)]
    public void Gateway_mode_rejects_response_size_outside_the_bounded_range(int maxResponseBytes)
    {
        var act = () => ResolveGatewayOptions(
            "https://localhost:7244/",
            mutate: options => options.Gateway!.MaxResponseBytes = maxResponseBytes);

        act.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(2_097_152)]
    [InlineData(8_388_608)]
    public void Gateway_mode_accepts_response_size_inside_the_bounded_range(int maxResponseBytes)
    {
        var options = ResolveGatewayOptions(
            "https://localhost:7244/",
            mutate: configured => configured.Gateway!.MaxResponseBytes = maxResponseBytes);

        options.Gateway!.MaxResponseBytes.Should().Be(maxResponseBytes);
    }

    [Theory]
    [InlineData("http://localhost:7244/")]
    [InlineData("https://user:password@localhost:7244/")]
    [InlineData("https://localhost:7244/?target=https://crm.example/")]
    [InlineData("https://crm.example/XRMServices/2011/Organization.svc")]
    [InlineData("https://crm.example/api/data/v9.1/")]
    public void Gateway_mode_rejects_unsafe_or_raw_crm_endpoints(string endpoint)
    {
        var act = () => ResolveGatewayOptions(endpoint);

        act.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("/v1?x=1")]
    [InlineData("/v1#fragment")]
    [InlineData("/../v1")]
    [InlineData("/v1//operations")]
    public void Gateway_mode_rejects_invalid_api_prefix(string apiPrefix)
    {
        var act = () => ResolveGatewayOptions("https://localhost:7244/", apiPrefix);

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Gateway_mode_rejects_inactive_embedded_branch()
    {
        var act = () => ResolveGatewayOptions(
            "https://localhost:7244/",
            mutate: options => options.Embedded = new EmbeddedModeOptions
            {
                ProductProfileBinding = "forbidden-in-gateway-mode",
                OrganizationAdmissionCoordinatorRef = "forbidden-in-gateway-mode"
            });

        act.Should().Throw<OptionsValidationException>();
    }

    private static ProductDynamicsOptions ResolveGatewayOptions(
        string endpoint,
        string apiPrefix = "/v1",
        Action<ProductDynamicsOptions>? mutate = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSpeechMessageDynamicsGatewayProductClient(options =>
        {
            options.ExecutionMode = DynamicsExecutionMode.Gateway;
            options.ProfileAlias = "jesus-prod";
            options.Gateway = new GatewayModeOptions
            {
                Endpoint = endpoint,
                ApiPrefix = apiPrefix
            };
            mutate?.Invoke(options);
        });

        using var provider = services.BuildServiceProvider(validateScopes: true);
        return provider.GetRequiredService<IOptions<ProductDynamicsOptions>>().Value;
    }
}
