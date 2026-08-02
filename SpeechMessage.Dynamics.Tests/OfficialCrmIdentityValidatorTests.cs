using FluentAssertions;
using SpeechMessage.Dynamics.WorkerHost;

namespace SpeechMessage.Dynamics.Tests;

public sealed class OfficialCrmIdentityValidatorTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BusinessUnitId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrganizationId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

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
