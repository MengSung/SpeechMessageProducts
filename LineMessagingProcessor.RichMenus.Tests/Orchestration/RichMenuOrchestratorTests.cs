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
