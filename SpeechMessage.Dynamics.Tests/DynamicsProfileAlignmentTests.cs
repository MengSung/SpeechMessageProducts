// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/DynamicsProfileAlignmentTests.cs
// 目的：驗證從舊 CrmConnection 推導 ProfileAlias / Web API root 的規則。
//
// 保母教學：
// - 這些測試不連真實 CRM。
// - 重點是「Organization.svc 不得再被當 Web API root」。
// - 也確認密碼不會出現在對齊結果中。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;

namespace SpeechMessage.Dynamics.Tests;

public sealed class DynamicsProfileAlignmentTests
{
    [Theory]
    [InlineData("jesus", "prod", "jesus-prod")]
    [InlineData("jesusback", "dev", "jesusback-dev")]
    [InlineData("Jesus", "PROD", "jesus-prod")]
    public void DeriveProfileAlias_normalizes_organization_and_suffix(
        string organization,
        string suffix,
        string expected)
    {
        DynamicsProfileAlignment.DeriveProfileAlias(organization, suffix)
            .Should().Be(expected);
    }

    [Fact]
    public void TryDeriveOrganizationBaseUri_strips_organization_svc_path()
    {
        var ok = DynamicsProfileAlignment.TryDeriveOrganizationBaseUri(
            "https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc",
            out var baseUri,
            out var error);

        ok.Should().BeTrue(error);
        baseUri.Should().Be("https://jesus.speechmessage.com.tw/");
    }

    [Fact]
    public void TryDeriveOrganizationWebApiBaseUri_builds_v91_root()
    {
        var ok = DynamicsProfileAlignment.TryDeriveOrganizationWebApiBaseUri(
            "https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc",
            "9.1",
            out var webApi,
            out var error);

        ok.Should().BeTrue(error);
        webApi.Should().Be("https://jesus.speechmessage.com.tw/api/data/v9.1/");
    }

    [Fact]
    public void TryDeriveOrganizationWebApiBaseUri_builds_v82_root()
    {
        var ok = DynamicsProfileAlignment.TryDeriveOrganizationWebApiBaseUri(
            "https://crm.example.local/",
            "8.2",
            out var webApi,
            out var error);

        ok.Should().BeTrue(error);
        webApi.Should().Be("https://crm.example.local/api/data/v8.2/");
    }

    [Fact]
    public void TryAlignFromLegacyCrmConnection_matches_churchreport_cloud_profile()
    {
        var ok = DynamicsProfileAlignment.TryAlignFromLegacyCrmConnection(
            organization: "jesus",
            serverUrl: "https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc",
            ceVersion: "9.1",
            environmentSuffix: "prod",
            secretReferenceName: null,
            out var result,
            out var error);

        ok.Should().BeTrue(error);
        result.ProfileAlias.Should().Be("jesus-prod");
        result.OrganizationBaseUri.Should().Be("https://jesus.speechmessage.com.tw/");
        result.OrganizationWebApiBaseUri.Should().Be("https://jesus.speechmessage.com.tw/api/data/v9.1/");
        result.CeVersion.Should().Be("9.1");
        result.SecretReference.Should().Be("dynamics-jesus-prod-credential");

        // 對齊結果不得夾帶任何明文密碼欄位語意。
        result.SecretReference.Should().NotContain("unit-test-plaintext-password");
        result.OrganizationWebApiBaseUri.Should().NotContain("Organization.svc");
    }

    [Fact]
    public void Rejects_http_or_userinfo_server_url()
    {
        DynamicsProfileAlignment.TryDeriveOrganizationBaseUri(
            "http://jesus.speechmessage.com.tw/",
            out _,
            out var httpError).Should().BeFalse();
        httpError.Should().Contain("https");

        DynamicsProfileAlignment.TryDeriveOrganizationBaseUri(
            "https://user:pass@jesus.speechmessage.com.tw/",
            out _,
            out var userInfoError).Should().BeFalse();
        userInfoError.Should().Contain("user-info");
    }
}
