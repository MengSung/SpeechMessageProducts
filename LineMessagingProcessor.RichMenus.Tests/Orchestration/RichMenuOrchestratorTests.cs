// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class RichMenuOrchestratorTests
// 主要成員：ApplyAsync_assigns_menu_when_text_matches_trigger_policy、ApplyAsync_returns_no_change_when_text_has_no_mapping
// 引用命名空間：FluentAssertions、LineMessagingProcessor.RichMenus.Tests.Support、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using LineMessagingProcessor.RichMenus.Tests.Support;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Orchestration;

/// <summary>
/// 驗證 RichMenu orchestrator 如何把 policy 決策轉交給 assignment workflow。
/// 這些測試避免 orchestrator 被加入產品邏輯；它只應挑選決策並套用結果。
/// </summary>
public sealed class RichMenuOrchestratorTests
{
    /// <summary>
    /// 收到文字命中 trigger policy 時，orchestrator 應指派對應 menu key 並透過 cache link 到 provider richMenuId。
    /// </summary>
    [Fact]
    public async Task ApplyAsync_assigns_menu_when_text_matches_trigger_policy()
    {
        var processor = new CapturingRichMenuProcessor();
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("member-main", "rich-menu-001");

        var orchestrator = new RichMenuOrchestrator(
            new IRichMenuPolicy[]
            {
                new LineRichMenuTextTriggerPolicy(new LineRichMenuTextTriggerResolver(new LineRichMenuTextTriggerOptions
                {
                    ExactTextToMenuKey =
                    {
                        ["主選單"] = "member-main"
                    }
                }))
            },
            new LineRichMenuAssignmentWorkflow(processor, cache));

        var decision = await orchestrator.ApplyAsync(new RichMenuContext("U123", receivedText: " 主選單 "));

        decision.Succeeded.Should().BeTrue();
        decision.AssignedMenuKey.Should().Be("member-main");
        decision.Changed.Should().BeTrue();
        processor.LinkedUsers["U123"].Should().Be("rich-menu-001");
    }

    /// <summary>
    /// 沒有任何 policy 命中時，orchestrator 應回傳 no-change，不應誤呼叫 LINE link/unlink。
    /// </summary>
    [Fact]
    public async Task ApplyAsync_returns_no_change_when_text_has_no_mapping()
    {
        var orchestrator = new RichMenuOrchestrator(
            new IRichMenuPolicy[]
            {
                new LineRichMenuTextTriggerPolicy(new LineRichMenuTextTriggerResolver(new LineRichMenuTextTriggerOptions()))
            },
            new LineRichMenuAssignmentWorkflow(new CapturingRichMenuProcessor(), new InMemoryLineRichMenuIdCache()));

        var decision = await orchestrator.ApplyAsync(new RichMenuContext("U123", receivedText: "hello", currentMenuKey: "current"));

        decision.Succeeded.Should().BeTrue();
        decision.Changed.Should().BeFalse();
        decision.AssignedMenuKey.Should().Be("current");
    }
}
