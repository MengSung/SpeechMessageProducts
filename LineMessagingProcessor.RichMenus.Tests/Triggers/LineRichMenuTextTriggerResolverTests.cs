using FluentAssertions;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Triggers;

public sealed class LineRichMenuTextTriggerResolverTests
{
    [Fact]
    public void TryResolve_uses_trimmed_ordinal_trigger_mapping()
    {
        var resolver = new LineRichMenuTextTriggerResolver(new LineRichMenuTextTriggerOptions
        {
            ExactTextToMenuKey =
            {
                ["主選單"] = "member-main"
            }
        });

        resolver.TryResolve(" 主選單 ", out var menuKey).Should().BeTrue();
        menuKey.Should().Be("member-main");
        resolver.TryResolve("主選單", out _).Should().BeTrue();
        resolver.TryResolve("主選單1", out _).Should().BeFalse();
    }
}
