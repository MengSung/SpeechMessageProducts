// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ApprovedWebApiRootFactoryTests.cs
// 目的：驗證 ApprovedWebApiRoot 推導與 URI 安全規則。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class ApprovedWebApiRootFactoryTests
{
    [Fact]
    public void Derives_root_from_organization_base_uri_and_ce_version()
    {
        var ok = ApprovedWebApiRootFactory.TryCreate(new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/Contoso",
            CeVersion = "9.1"
        }, out var root, out var error);

        ok.Should().BeTrue(error?.ErrorMessage);
        root!.Value.ToString().Should().Be("https://crm.example.local/Contoso/api/data/v9.1/");
        root.CeVersion.Should().Be("9.1");
    }

    [Theory]
    [InlineData("http://crm.example.local/org/")]
    [InlineData("https://user:pass@crm.example.local/org/")]
    [InlineData("https://crm.example.local/org/?x=1")]
    [InlineData("https://crm.example.local/org/#frag")]
    public void Rejects_unsafe_uri_shapes(string uri)
    {
        var ok = ApprovedWebApiRootFactory.TryCreate(new DynamicsWebApiOptions
        {
            OrganizationBaseUri = uri,
            CeVersion = "8.2"
        }, out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("dynamics.config.invalid");
    }

    [Fact]
    public void Rejects_webapi_root_that_does_not_match_ce_version()
    {
        var ok = ApprovedWebApiRootFactory.TryCreate(new DynamicsWebApiOptions
        {
            OrganizationWebApiBaseUri = "https://crm.example.local/api/data/v8.2/",
            CeVersion = "9.1"
        }, out _, out var error);

        ok.Should().BeFalse();
        error!.ErrorMessage.Should().Contain("api/data/v9.1/");
    }
}
