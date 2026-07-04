using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Actions;

public sealed class RichMenuActionFactoryTests
{
    [Fact]
    public void SwitchToAlias_creates_official_richmenu_switch_action()
    {
        var action = RichMenuActionFactory.SwitchToAlias("member-main", "switch=member-main", "主選單");

        action.Should().BeOfType<RichMenuSwitchTemplateAction>();
        JsonConvert.SerializeObject(action).Should().Contain("\"richMenuAliasId\":\"member-main\"");
    }
}
