using FluentAssertions;
using LineMessagingProcessor.RichMenus.Tests.Support;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Assignment;

public sealed class LineRichMenuAssignmentWorkflowTests
{
    [Fact]
    public async Task AssignAsync_links_user_to_cached_rich_menu_id()
    {
        var processor = new CapturingRichMenuProcessor();
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("member-main", "rich-menu-001");
        var workflow = new LineRichMenuAssignmentWorkflow(processor, cache);

        var result = await workflow.AssignAsync("U123", "member-main");

        result.Succeeded.Should().BeTrue();
        result.RichMenuId.Should().Be("rich-menu-001");
        processor.Calls.Should().Contain("link:U123:rich-menu-001");
    }

    [Fact]
    public async Task AssignAsync_returns_validation_failure_when_menu_key_is_unknown()
    {
        var workflow = new LineRichMenuAssignmentWorkflow(
            new CapturingRichMenuProcessor(),
            new InMemoryLineRichMenuIdCache());

        var result = await workflow.AssignAsync("U123", "missing");

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(LineRichMenuStatus.ValidationFailed);
        result.ErrorCode.Should().Be("line-richmenu-menu-key-not-found");
    }

    [Fact]
    public async Task AssignAsync_resolves_online_rich_menu_when_cache_is_empty()
    {
        var definition = new LineRichMenuDefinition(
            "member-main",
            "member-main",
            RichMenuTestFactory.CreateMenu("member-main"),
            RichMenuTestFactory.CreatePngFactory());
        var processor = new CapturingRichMenuProcessor();
        var cache = new InMemoryLineRichMenuIdCache();
        var workflow = new LineRichMenuAssignmentWorkflow(
            processor,
            cache,
            new InMemoryRichMenuStateStore(),
            new StaticLineRichMenuCatalog(new[] { definition }));
        var versionedName = LineRichMenuFingerprint.BuildName(definition, RichMenuTestFactory.CreatePngBytes());
        processor.ExistingRichMenus.Add(RichMenuTestFactory.CreateMenu(versionedName).ToResponseRichMenu("rich-menu-online"));

        var result = await workflow.AssignAsync("U123", "member-main");

        result.Succeeded.Should().BeTrue();
        result.RichMenuId.Should().Be("rich-menu-online");
        processor.Calls.Should().Contain("list");
        processor.Calls.Should().Contain("link:U123:rich-menu-online");
        cache.TryGet("member-main", out var cachedRichMenuId).Should().BeTrue();
        cachedRichMenuId.Should().Be("rich-menu-online");
    }

    [Fact]
    public async Task AssignOrThrowAsync_throws_standard_exception_when_assignment_fails()
    {
        var workflow = new LineRichMenuAssignmentWorkflow(
            new CapturingRichMenuProcessor(),
            new InMemoryLineRichMenuIdCache());

        var action = () => workflow.AssignOrThrowAsync("U123", "missing");

        var exception = await action.Should().ThrowAsync<LineRichMenuException>();
        exception.Which.AssignmentResult.Should().NotBeNull();
        exception.Which.AssignmentResult!.Status.Should().Be(LineRichMenuStatus.ValidationFailed);
    }
}
