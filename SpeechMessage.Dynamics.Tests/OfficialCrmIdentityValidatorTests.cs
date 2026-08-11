using FluentAssertions;
using SpeechMessage.Dynamics.WorkerHost;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Official Worker 只有在使用者、Business Unit、Organization 與 CE major/minor 全部精確相符時才會 Ready。
/// 測試以空 GUID、錯誤 Organization、缺少/錯誤版本注入 identity fault，主要斷言是所有不完整證據均 fail closed，
/// 防止錯誤 Profile、另一個 Organization 或另一個 CE 版本共用同一 Worker Session。
/// </summary>
public sealed class OfficialCrmIdentityValidatorTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BusinessUnitId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrganizationId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>證明完整且精確相符的 identity/version 投影可通過，避免安全檢查把正常 Worker 永久標成 NotReady。</summary>
    [Fact]
    public void IsValid_accepts_non_empty_identity_exact_organization_and_matching_major_minor()
    {
        OfficialCrmIdentityValidator.IsValid(
                UserId,
                BusinessUnitId,
                OrganizationId,
                OrganizationId,
                new Version(9, 1, 2, 60),
                "9.1")
            .Should().BeTrue();
    }

    /// <summary>逐一注入空 user/BU/Organization GUID，證明任何缺失都不能被其他非空欄位掩蓋。</summary>
    [Theory]
    [InlineData("user")]
    [InlineData("business-unit")]
    [InlineData("organization")]
    public void IsValid_rejects_an_empty_identity_guid(string emptyField)
    {
        OfficialCrmIdentityValidator.IsValid(
                emptyField == "user" ? Guid.Empty : UserId,
                emptyField == "business-unit" ? Guid.Empty : BusinessUnitId,
                emptyField == "organization" ? Guid.Empty : OrganizationId,
                OrganizationId,
                new Version(9, 1),
                "9.1")
            .Should().BeFalse();
    }

    /// <summary>注入不同 expected Organization，證明 Worker 不會把另一個組織的已驗證 Session 視為目前 Profile。</summary>
    [Fact]
    public void IsValid_rejects_an_unexpected_organization()
    {
        OfficialCrmIdentityValidator.IsValid(
                UserId,
                BusinessUnitId,
                OrganizationId,
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                new Version(9, 1),
                "9.1")
            .Should().BeFalse();
    }

    /// <summary>注入 CE major/minor mismatch，證明 package/worker version 不能跨 CE 版本 fallback。</summary>
    [Theory]
    [InlineData(9, 0, "9.1")]
    [InlineData(8, 2, "9.1")]
    [InlineData(9, 1, "8.2")]
    public void IsValid_rejects_a_connected_ce_version_mismatch(
        int major,
        int minor,
        string expectedCeVersion)
    {
        OfficialCrmIdentityValidator.IsValid(
                UserId,
                BusinessUnitId,
                OrganizationId,
                OrganizationId,
                new Version(major, minor),
                expectedCeVersion)
            .Should().BeFalse();
    }

    /// <summary>注入缺少 connected version，證明未知版本維持 NotReady，而不是以 expected 字串猜測成功。</summary>
    [Fact]
    public void IsValid_rejects_a_missing_connected_version()
    {
        OfficialCrmIdentityValidator.IsValid(
                UserId,
                BusinessUnitId,
                OrganizationId,
                OrganizationId,
                null,
                "9.1")
            .Should().BeFalse();
    }
}
