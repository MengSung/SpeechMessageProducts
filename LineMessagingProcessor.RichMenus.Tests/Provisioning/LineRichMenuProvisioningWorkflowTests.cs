// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineRichMenuProvisioningWorkflowTests
// 主要成員：SyncAsync_creates_uploads_aliases_defaults_and_caches_new_menu、SyncAsync_reuses_existing_fingerprinted_menu_and_updates_alias_when_needed、SyncAsync_records_failed_item_and_continues_with_next_definition
// 引用命名空間：FluentAssertions、LineMessagingProcessor.RichMenus.Tests.Support、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using LineMessagingProcessor.RichMenus.Tests.Support;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Provisioning;

/// <summary>
/// 驗證 RichMenu catalog 佈建 workflow 與 LINE provider 狀態同步的核心行為。
/// 測試重點是 create/upload/alias/default/cache 的順序，以及失敗選單不會中斷後續選單同步。
/// </summary>
public sealed class LineRichMenuProvisioningWorkflowTests
{
    /// <summary>
    /// 新選單不存在於 LINE 時，workflow 應建立 RichMenu、上傳圖片、建立 alias、設定 default 並寫入 cache。
    /// </summary>
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

    /// <summary>
    /// 已存在相同 fingerprinted name 時，workflow 應重用 provider richMenuId，仍補齊 alias 與 cache。
    /// </summary>
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

    /// <summary>
    /// 單一 definition 失敗時應產生 Failed item 並繼續處理下一個 definition，讓管理端看到完整同步結果。
    /// </summary>
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
