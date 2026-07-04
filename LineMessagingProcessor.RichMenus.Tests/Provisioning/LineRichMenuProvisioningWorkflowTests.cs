using FluentAssertions;
using LineMessagingProcessor.RichMenus.Tests.Support;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Provisioning;

public sealed class LineRichMenuProvisioningWorkflowTests
{
    [Fact]
    public async Task SyncAsync_creates_uploads_aliases_defaults_and_caches_new_menu()
    {
        var processor = new CapturingRichMenuProcessor();
        processor.EnqueueCreatedRichMenuId("rich-menu-001");
        var cache = new InMemoryLineRichMenuIdCache();
        var catalog = new StaticLineRichMenuCatalog(new[]
        {
            new LineRichMenuDefinition
            {
                Key = "member-main",
                Alias = "member-main",
                IsDefault = true,
                Layout = RichMenuTestFactory.CreateMenu("member-main"),
                PngImageStreamFactory = RichMenuTestFactory.CreatePngFactory()
            }
        });
        var workflow = new LineRichMenuProvisioningWorkflow(catalog, processor, cache);

        var report = await workflow.SyncAsync();

        report.Items.Should().ContainSingle();
        report.Items[0].Outcome.Should().Be(LineRichMenuSyncOutcome.Created);
        cache.TryGet("member-main", out var cachedRichMenuId).Should().BeTrue();
        cachedRichMenuId.Should().Be("rich-menu-001");
        processor.Aliases["member-main"].Should().Be("rich-menu-001");
        processor.DefaultRichMenuId.Should().Be("rich-menu-001");
        processor.UploadedImageCount.Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_reuses_existing_fingerprinted_menu_and_updates_alias_when_needed()
    {
        var processor = new CapturingRichMenuProcessor();
        var definition = new LineRichMenuDefinition
        {
            Key = "member-main",
            Alias = "member-main",
            IsDefault = false,
            Layout = RichMenuTestFactory.CreateMenu("member-main"),
            PngImageStreamFactory = RichMenuTestFactory.CreatePngFactory()
        };
        var fingerprintedName = LineRichMenuFingerprint.BuildName(
            definition,
            RichMenuTestFactory.CreatePngBytes());
        processor.ExistingRichMenus.Add(RichMenuTestFactory.CreateMenu(fingerprintedName).ToResponseRichMenu("rich-menu-existing"));
        processor.Aliases["member-main"] = "rich-menu-old";

        var cache = new InMemoryLineRichMenuIdCache();
        var workflow = new LineRichMenuProvisioningWorkflow(
            new StaticLineRichMenuCatalog(new[] { definition }),
            processor,
            cache);

        var report = await workflow.SyncAsync();

        report.Items[0].Outcome.Should().Be(LineRichMenuSyncOutcome.UpToDate);
        processor.UpdateAliasCount.Should().Be(1);
        processor.Aliases["member-main"].Should().Be("rich-menu-existing");
        cache.TryGet("member-main", out var richMenuId).Should().BeTrue();
        richMenuId.Should().Be("rich-menu-existing");
        processor.UploadedImageCount.Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_records_failed_item_and_continues_with_next_definition()
    {
        var processor = new CapturingRichMenuProcessor();
        processor.EnqueueCreatedRichMenuId("rich-menu-created");
        var failedDefinition = new LineRichMenuDefinition(
            "broken-menu",
            "broken-menu",
            RichMenuTestFactory.CreateMenu("broken-menu"),
            _ => Task.FromResult<Stream>(null!));
        var validDefinition = new LineRichMenuDefinition(
            "member-main",
            "member-main",
            RichMenuTestFactory.CreateMenu("member-main"),
            RichMenuTestFactory.CreatePngFactory());
        var workflow = new LineRichMenuProvisioningWorkflow(
            new StaticLineRichMenuCatalog(new[] { failedDefinition, validDefinition }),
            processor,
            new InMemoryLineRichMenuIdCache());

        var report = await workflow.SyncAsync();

        report.Items.Should().HaveCount(2);
        report.Items[0].MenuKey.Should().Be("broken-menu");
        report.Items[0].Outcome.Should().Be(LineRichMenuSyncOutcome.Failed);
        report.Items[0].ErrorMessage.Should().Contain("returned null");
        report.Items[1].MenuKey.Should().Be("member-main");
        report.Items[1].Outcome.Should().Be(LineRichMenuSyncOutcome.Created);
        processor.Calls.Should().Contain("create:member-main v" + LineRichMenuFingerprint.ShortVersion(
            LineRichMenuFingerprint.Create(validDefinition.RichMenu, RichMenuTestFactory.CreatePngBytes())));
    }
}
