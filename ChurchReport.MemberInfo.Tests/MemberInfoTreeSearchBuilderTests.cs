using ChurchReport.Services.MemberInfo;
using ChurchReport.ViewModels.MemberInfoTree;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoTreeSearchBuilderTests
{
    [Fact]
    public void Build_ReturnsDistinctListsContainingMatchingCurrentContacts()
    {
        var memberships = new[]
        {
            Member("L2", "C1"),
            Member("L1", "c1"),
            Member("L3", "C2", false),
            Member("L4", "C3")
        };

        var result = MemberInfoTreeSearchBuilder.Build(memberships, new[] { "C1", "C2" }, true);

        result.MatchingListIds.Should().ContainInOrder("L1", "L2");
        result.HasUngrouped.Should().BeTrue();
    }

    [Fact]
    public void Build_ShepherdNeverReturnsUngrouped()
    {
        var result = MemberInfoTreeSearchBuilder.Build(
            new[] { Member("L1", "C1") },
            new[] { "C2" },
            false);

        result.MatchingListIds.Should().BeEmpty();
        result.HasUngrouped.Should().BeFalse();
    }

    [Fact]
    public void Build_ToleratesNullInputs()
    {
        var result = MemberInfoTreeSearchBuilder.Build(null, null, true);

        result.MatchingListIds.Should().BeEmpty();
        result.HasUngrouped.Should().BeFalse();
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Build_ReturnsDistinctAuthorizedRowsSortedByCommitmentTypeThenName()
    {
        var memberships = new[] { Member("L1", "C1"), Member("L2", "C2") };
        var rows = new[]
        {
            Row("C2", "會友甲", 1, true),
            Row("c1", "會友乙", 0, true),
            Row("C1", "重複列", 0, true),
            Row("C9", "不可見", null, false)
        };

        var result = MemberInfoTreeSearchBuilder.Build(
            memberships,
            new[] { "C1", "C2" },
            true,
            rows);

        result.Rows.Select(row => row.ContactId).Should().ContainInOrder("c1", "C2");
        result.Rows.Select(row => row.FullName).Should().ContainInOrder("會友乙", "會友甲");
    }

    private static GroupMembershipRow Member(string listId, string contactId, bool current = true) => new()
    {
        ListId = listId,
        ContactId = contactId,
        IsCurrent = current
    };

    private static GroupMemberRowViewModel Row(
        string contactId,
        string fullName,
        int? membershipStatusOrder,
        bool hasMembershipStatusValue) => new()
    {
        ContactId = contactId,
        FullName = fullName,
        MembershipStatusOrder = membershipStatusOrder,
        HasMembershipStatusValue = hasMembershipStatusValue
    };
}
