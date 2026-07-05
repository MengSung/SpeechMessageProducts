// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus.Tests/Actions/RichMenuActionFactoryTests.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class RichMenuActionFactoryTests
// 主要成員：SwitchToAlias_creates_official_richmenu_switch_action
// 引用命名空間：FluentAssertions、Line.Messaging、Newtonsoft.Json、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
