using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class RelationGoalFormatterTests
{
    [Fact]
    public void Format_PreservesPairOrderAndDeduplicatesCaseInsensitively()
    {
        var items = new[]
        {
            (Role: "妻子", TargetName: "小組長甲"),
            (Role: "門徒", TargetName: "小組長乙"),
            (Role: "妻子", TargetName: "小組長甲")
        };

        var result = RelationGoalFormatter.Format(items);

        ((object)result).Should().Be("妻子: 小組長甲、門徒: 小組長乙");
    }

    [Fact]
    public void Format_MatchesMemberDetailTextWhenRoleIsBlank()
    {
        var result = RelationGoalFormatter.Format(new[]
        {
            (Role: "", TargetName: "小組長甲"),
            (Role: "門徒", TargetName: "小組長乙"),
            (Role: "", TargetName: "")
        });

        ((object)result).Should().Be("小組長甲、門徒: 小組長乙");
    }

    [Fact]
    public void Format_NullInputReturnsEmptyStrings()
    {
        var result = RelationGoalFormatter.Format(null);

        ((object)result).Should().Be(string.Empty);
    }
}
