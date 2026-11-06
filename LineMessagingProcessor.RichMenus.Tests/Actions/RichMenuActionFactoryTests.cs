/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 RichMenu action factory 建出的 SDK action 符合 LINE richmenuswitch JSON contract。
 *
 * 主要功能與流程：
 * 本檔宣告 RichMenuActionFactoryTests。
 * - 確認 helper 會建立 RichMenuSwitchTemplateAction，並序列化出 LINE 需要的 richMenuAliasId 欄位。
 * 測試案例入口包含 SwitchToAlias_creates_official_richmenu_switch_action。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
