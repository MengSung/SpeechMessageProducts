using ChurchReport.Services.MemberInfo;
using ChurchReport.ViewModels.MemberInfoTree;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoCommitmentTypeSortTests
{
    [Fact]
    public void OrderRows_Ascending_UsesConfiguredRankThenUnknownThenEmpty()
    {
        var rows = new[]
        {
            Row("empty", "Empty", null, false),
            Row("member", "Member", 1, true),
            Row("pastor", "Pastor", 0, true),
            Row("unknown", "Unknown", null, true)
        };

        var result = MemberInfoCommitmentTypeSort.OrderRows(rows);

        MemberInfoCommitmentTypeSort.Selector.Should().Be("MembershipStatusOrder");
        result.Select(row => row.ContactId)
            .Should().Equal("pastor", "member", "unknown", "empty");
    }

    [Fact]
    public void OrderRows_Descending_ReversesConfiguredRanksOnly()
    {
        var rows = new[]
        {
            Row("empty", "Empty", null, false),
            Row("pastor", "Pastor", 0, true),
            Row("member", "Member", 1, true),
            Row("unknown", "Unknown", null, true)
        };

        var result = MemberInfoCommitmentTypeSort.OrderRows(rows, descending: true);

        result.Select(row => row.ContactId)
            .Should().Equal("member", "pastor", "unknown", "empty");
    }

    [Fact]
    public void OrderRows_SameRankUsesOrdinalNameThenCaseInsensitiveContactId()
    {
        var rows = new[]
        {
            Row("z-id", "alpha", 0, true),
            Row("B-id", "Same", 0, true),
            Row("a-id", "Same", 0, true),
            Row("beta", "Beta", 0, true)
        };

        var result = MemberInfoCommitmentTypeSort.OrderRows(rows);

        result.Select(row => row.ContactId)
            .Should().Equal("beta", "a-id", "B-id", "z-id");
    }

    [Fact]
    public void OrderRows_NullInputReturnsEmptyList()
    {
        MemberInfoCommitmentTypeSort.OrderRows(null).Should().BeEmpty();
    }

    [Fact]
    public void BuildSegments_UsesConfiguredSequenceAndKeepsUnknownAndEmptyLast()
    {
        var counts = new Dictionary<int, int>
        {
            [100000006] = 2,
            [1] = 3,
            [777] = 4
        };

        var result = MemberInfoCommitmentTypeSort.BuildSegments(
            new[] { 100000006, 100000002, 1 },
            counts,
            nullCount: 1,
            descending: false);

        result.Should().Equal(
            new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Configured, 100000006, 2),
            new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Configured, 1, 3),
            new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Unknown, null, 4),
            new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Empty, null, 1));
    }

    [Fact]
    public void BuildSegments_DescendingReversesOnlyConfiguredSegments()
    {
        var counts = new Dictionary<int, int>
        {
            [100000006] = 2,
            [1] = 3,
            [777] = 4
        };

        var result = MemberInfoCommitmentTypeSort.BuildSegments(
            new[] { 100000006, 1 },
            counts,
            nullCount: 1,
            descending: true);

        result.Select(segment => segment.Kind).Should().Equal(
            MemberInfoCommitmentTypeSegmentKind.Configured,
            MemberInfoCommitmentTypeSegmentKind.Configured,
            MemberInfoCommitmentTypeSegmentKind.Unknown,
            MemberInfoCommitmentTypeSegmentKind.Empty);
        result.Select(segment => segment.Value)
            .Should().Equal(1, 100000006, null, null);
    }

    [Fact]
    public void BuildSegments_DeduplicatesConfiguredValuesAndClampsCounts()
    {
        var counts = new Dictionary<int, int>
        {
            [100000006] = -2,
            [1] = 3,
            [2] = 0,
            [777] = 4,
            [888] = -5
        };

        var result = MemberInfoCommitmentTypeSort.BuildSegments(
            new[] { 100000006, 1, 100000006, 2 },
            counts,
            nullCount: -1);

        result.Should().Equal(
            new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Configured, 1, 3),
            new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Unknown, null, 4));
    }

    [Fact]
    public void PlanSlices_CrossesConfiguredUnknownAndEmptySegments()
    {
        var segments = new[]
        {
            new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Configured, 100000006, 3),
            new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Unknown, null, 2),
            new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Empty, null, 4)
        };

        var result = MemberInfoCommitmentTypeSort.PlanSlices(2, 5, segments);

        result.Should().Equal(
            new MemberInfoCommitmentTypeSlice(
                MemberInfoCommitmentTypeSegmentKind.Configured, 100000006, 2, 1),
            new MemberInfoCommitmentTypeSlice(
                MemberInfoCommitmentTypeSegmentKind.Unknown, null, 0, 2),
            new MemberInfoCommitmentTypeSlice(
                MemberInfoCommitmentTypeSegmentKind.Empty, null, 0, 2));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, -5)]
    [InlineData(20, 5)]
    public void PlanSlices_EmptyOrOutOfRangeRequestReturnsNoSlices(int skip, int take)
    {
        var segments = new[]
        {
            new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Configured, 1, 10)
        };

        MemberInfoCommitmentTypeSort.PlanSlices(skip, take, segments)
            .Should().BeEmpty();
    }

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
