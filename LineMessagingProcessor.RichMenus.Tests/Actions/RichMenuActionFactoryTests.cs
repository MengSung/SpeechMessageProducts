using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Actions;

/// <summary>
/// 驗證 RichMenu action factory 建出的 SDK action 符合 LINE richmenuswitch JSON contract。
/// </summary>
public sealed class RichMenuActionFactoryTests
{
    /// <summary>
    /// 確認 helper 會建立 RichMenuSwitchTemplateAction，並序列化出 LINE 需要的 richMenuAliasId 欄位。
    /// </summary>
    [Fact]
    public void SwitchToAlias_creates_official_richmenu_switch_action()
    {
        var action = RichMenuActionFactory.SwitchToAlias("member-main", "switch=member-main", "主選單");

        action.Should().BeOfType<RichMenuSwitchTemplateAction>();
        JsonConvert.SerializeObject(action).Should().Contain("\"richMenuAliasId\":\"member-main\"");
    }
}
