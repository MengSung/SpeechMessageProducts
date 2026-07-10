using System.Linq;
using System.Reflection;
using ChurchReport;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

public sealed class CheckSessionOutAttributeTests
{
    [Fact]
    public void CheckSessionOutAttribute_HasNoDeclaredInstanceFields()
    {
        typeof(CheckSessionOutAttribute)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void CheckSessionOutAttribute_OnActionExecuting_IsSynchronousVoidOverride()
    {
        var method = typeof(CheckSessionOutAttribute).GetMethod(
            "OnActionExecuting",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
        method.GetCustomAttributes(false).Select(a => a.GetType().Name).Should().NotContain("AsyncStateMachineAttribute");
    }
}
