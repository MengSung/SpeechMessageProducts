using FluentAssertions;
using LineMessagingProcessor.RichMenus.Tests.Support;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Orchestration;

public sealed class RichMenuOrchestratorTests
{
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
