/*
 * 檔案整體說明
 *
 * 目的與定位：本檔是P2-08新路徑的離線驗收，使用真實handler與可觀察的既有processor替身，
 * 不連LINE、不讀D365或秘密。八個固定產品組合／頁籤只是合成配置，不冒充正式業務菜單。
 * 主要情境：注入同scope取字覆寫，斷言每個啟用語系恰八張、未啟用為零；
 * 另注入容量不足、過長選單文字、重複正式ID及upload故障，必須在正確邊界拒絕或清理。
 * 資源擁有權：每案擁有catalog與processor；renderer回傳可追蹤stream，由handlerDispose，
 * 捕捉上傳位元組只留單案有界資料，失敗亦斷言active stream歸零，不以Mock自己驗自己。
 * 維護驗證：五個計畫同名測試保留，新增反例不放寬穩定ID／語系／資源斷言；
 * 產圖像素與PNG另由ImageSharp renderer測試保護，本檔不宣稱任何實機結果。
 */
using Line.Messaging;
using LineMessagingProcessor.RichMenus.Tests.Support;
using SpeechMessage.Localization;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Generation;

/// <summary>保護多語宮格候選、發布邊界與唯一stream owner，全部依賴皆為單案離線值。</summary>
public sealed class RichMenuGenerateHandlerTests
{
    /// <summary>ASCII格式與32字上限；非法名稱不得截斷或清洗成另一個教會alias。</summary>
    [Fact]
    public void Alias_Format_MaxLength32()
    {
        Assert.Equal("c0031-p4-pastoral-en", RichMenuGenerateHandler.BuildAlias("c0031", "p4", "pastoral", "en"));
        Assert.Equal(32, RichMenuGenerateHandler.BuildAlias(new string('c', 20), "p4", "t1", "zh-TW").Length);
        Assert.Throws<ArgumentException>(() => RichMenuGenerateHandler.BuildAlias(new string('c', 21), "p4", "t1", "zh-TW"));
        Assert.Throws<ArgumentException>(() => RichMenuGenerateHandler.BuildAlias("c/31", "p4", "t1", "zh-TW"));
    }

    /// <summary>只傳zh-TW時恰產八張；圖片與chatBar從相同完整教會範圍取覆寫，不另產英文。</summary>
    [Fact]
    public async Task Generate_OnlyEnabledCultures()
    {
        var processor = new CapturingRichMenuProcessor();
        var renderer = new Renderer();
        var catalog = new Catalog();
        var handler = Handler(processor, renderer, catalog, ["zh-TW"]);
        await handler.ExecuteAsync(default);
        Assert.Equal(8, processor.UploadedImageCount);
        Assert.Equal(8, processor.Aliases.Count);
        Assert.All(processor.Aliases.Keys, key => Assert.EndsWith("-zh-TW", key));
        Assert.All(processor.ExistingRichMenus, menu => Assert.Equal("override", menu.ChatBarText));
        Assert.All(renderer.Texts, text => Assert.Equal("church-label", text));
        Assert.Equal(8, renderer.Disposed);
        Assert.Equal(0, renderer.Active);
        Assert.All(catalog.Contexts, context => Assert.Equal(Catalog.Church, context.ChurchId));
    }

    /// <summary>已有993張合法不同ID時八張會超容量；不得render/create/upload，例外交worker告警。</summary>
    [Fact]
    public async Task Generate_CapacityInsufficient_FailsBeforeUpload()
    {
        var processor = new CapturingRichMenuProcessor();
        for (var i = 0; i < 993; i++) processor.ExistingRichMenus.Add(new RichMenu { Name = "same" }.ToResponseRichMenu("richmenu-" + i.ToString("x32")));
        var renderer = new Renderer();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(processor, renderer, new Catalog(), ["zh-TW"]).ExecuteAsync(default));
        Assert.Equal(0, processor.UploadedImageCount);
        Assert.Equal(0, renderer.Active);
        Assert.Empty(renderer.Texts);
        Assert.DoesNotContain(processor.Calls, call => call.StartsWith("create:"));
    }

    /// <summary>SDK會靜默截斷chatBar；必須在設定SDK屬性之前拒絕15字，不可送出14字假成功。</summary>
    [Fact]
    public async Task ChatBarText_TooLong_Rejected()
    {
        var processor = new CapturingRichMenuProcessor();
        await Assert.ThrowsAsync<ArgumentException>(() => Handler(processor, new Renderer(),
            new Catalog { Chat = new string('x', 15) }, ["zh-TW"]).ExecuteAsync(default));
        Assert.Empty(processor.Calls);
    }

    /// <summary>偽語系只縮alias尾碼為qps，圖面仍使用qps-ploc取字且保留完整scope。</summary>
    [Fact]
    public async Task Generate_PseudoLocale_AliasUsesQps()
    {
        var processor = new CapturingRichMenuProcessor();
        var renderer = new Renderer();
        await Handler(processor, renderer, new Catalog(), ["qps-ploc"]).ExecuteAsync(default);
        Assert.Equal(8, processor.Aliases.Count);
        Assert.All(processor.Aliases.Keys, key => Assert.EndsWith("-qps", key));
        Assert.All(renderer.Texts, text => Assert.Equal("[pseudo]", text));
    }

    /// <summary>重複正式ID即使名稱不同仍拒絕；同名不同ID在容量測試完整保留，不按Name去重。</summary>
    [Fact]
    public async Task Generate_DuplicateProviderIdentity_RejectsBeforeRender()
    {
        var processor = new CapturingRichMenuProcessor();
        processor.ExistingRichMenus.Add(new RichMenu { Name = "a" }.ToResponseRichMenu("richmenu-" + new string('1', 32)));
        processor.ExistingRichMenus.Add(new RichMenu { Name = "b" }.ToResponseRichMenu("richmenu-" + new string('1', 32)));
        var renderer = new Renderer();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(processor, renderer, new Catalog(), ["zh-TW"]).ExecuteAsync(default));
        Assert.Empty(renderer.Texts);
        Assert.Equal(0, processor.UploadedImageCount);
    }

    /// <summary>upload真正失敗保留同一例外、刪本次metadata、釋放stream；重試不受部分候選污染。</summary>
    [Fact]
    public async Task Generate_UploadFailure_CleansCandidateAndStream_AndRetrySucceeds()
    {
        var failure = new IOException("synthetic upload fault");
        var processor = new CapturingRichMenuProcessor { UploadException = failure };
        var renderer = new Renderer();
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() =>
            Handler(processor, renderer, new Catalog(), ["zh-TW"]).ExecuteAsync(default)));
        Assert.Empty(processor.ExistingRichMenus);
        Assert.Empty(processor.Aliases);
        Assert.Equal(0, renderer.Active);
        Assert.Equal(1, renderer.Disposed);
        processor.UploadException = null;
        var handler = Handler(processor, renderer, new Catalog(), ["zh-TW"]);
        await handler.ExecuteAsync(default);
        var count = processor.UploadedImageCount;
        await handler.ExecuteAsync(default);
        Assert.Equal(count, processor.UploadedImageCount);
        Assert.Equal(8, processor.ExistingRichMenus.Count);
        Assert.Equal(8, processor.Aliases.Count);
        Assert.Equal(0, renderer.Active);
    }

    /// <summary>
    /// A/B各100筆單案真handler在renderer屏障重疊，完整scope、alias與圖面文字不得互串；
    /// 所有工作await完才核對stream歸零，屏障有十秒上限而非背景fire-and-forget。
    /// </summary>
    [Fact]
    public async Task Generate_ParallelChurchScopes_NoCrossTalkAndStreamsDrain()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = 0;
            var a = new Renderer { BeforeRender = async () =>
            {
                if (Interlocked.Increment(ref entered) == 2) gate.TrySetResult();
                await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
            } };
            var b = new Renderer { BeforeRender = a.BeforeRender };
            var processorA = new CapturingRichMenuProcessor();
            var processorB = new CapturingRichMenuProcessor();
            for (var i = 0; i < 8; i++)
            {
                processorA.EnqueueCreatedRichMenuId("richmenu-" + (i + 1000).ToString("x32"));
                processorB.EnqueueCreatedRichMenuId("richmenu-" + (i + 2000).ToString("x32"));
            }
            var contextA = new TextContext(Guid.NewGuid(), "zh-TW", "zh-TW", "org-a", "product");
            var contextB = new TextContext(Guid.NewGuid(), "zh-TW", "zh-TW", "org-b", "product");
            var catalogA = new Catalog { Label = "only-A" };
            var catalogB = new Catalog { Label = "only-B" };
            await Task.WhenAll(
                new RichMenuGenerateHandler(contextA, "ca", ["zh-TW"], Pages(), catalogA, processorA, a).ExecuteAsync(default),
                new RichMenuGenerateHandler(contextB, "cb", ["zh-TW"], Pages(), catalogB, processorB, b).ExecuteAsync(default));
            Assert.All(a.Texts, value => Assert.Equal("only-A", value));
            Assert.All(b.Texts, value => Assert.Equal("only-B", value));
            Assert.All(catalogA.Contexts, context => Assert.True(contextA.HasSameScope(context)));
            Assert.All(catalogB.Contexts, context => Assert.True(contextB.HasSameScope(context)));
            Assert.All(processorA.Aliases.Keys, alias => Assert.StartsWith("ca-", alias));
            Assert.All(processorB.Aliases.Keys, alias => Assert.StartsWith("cb-", alias));
            Assert.Equal(0, a.Active + b.Active);
            Assert.Equal(16, a.Disposed + b.Disposed);
        }
    }

    /// <summary>建立合成工作；每種組合兩頁，固定八個不同穩定alias，不由集合索引充當正式資料庫ID。</summary>
    private static RichMenuGenerateHandler Handler(CapturingRichMenuProcessor processor, Renderer renderer,
        Catalog catalog, string[] cultures)
    {
        for (var i = 0; i < cultures.Length * 8; i++)
            processor.EnqueueCreatedRichMenuId("richmenu-" + (i + 2000).ToString("x32"));
        return new(new TextContext(Catalog.Church, "zh-TW", "zh-TW", "org", "product"),
            "c31", cultures, Pages(), catalog, processor, renderer);
    }

    /// <summary>合成產品版面由各案例獨占，文字工廠不讀網路或保存context。</summary>
    private static RichMenuPageTemplate[] Pages() =>
        Enumerable.Range(1, 4).SelectMany(p => new[] { "a", "b" }.Select(tab =>
            new RichMenuPageTemplate("p" + p, tab, new TextKey("Menu.Chat"), false,
                [new RichMenuCellTemplate(0, 0, 2500, 843, new TextKey("Menu.Label"),
                    text => new PostbackTemplateAction(string.Empty, "operation"))]))).ToArray();

    /// <summary>取字邊界注入固定覆寫與偽語系；單案清單只用來驗證handler傳入的真正scope。</summary>
    private sealed class Catalog : ITextCatalog
    {
        internal static readonly Guid Church = Guid.Parse("f604efb2-5aeb-4f29-b5ed-c6e76da57323");
        internal string Chat { get; init; } = "override";
        internal string Label { get; init; } = "church-label";
        internal List<TextContext> Contexts { get; } = [];
        /// <summary>回單案合成文字，保護取字鍵與完整文化選擇，不冒充正式字典資源。</summary>
        public string GetText(TextContext context, TextKey key, params TextArg[] args)
        {
            Contexts.Add(context);
            return key.Value == "Menu.Chat" ? Chat : context.TextCulture == "qps-ploc" ? "[pseudo]" : Label;
        }
    }

    /// <summary>回傳受追蹤的合成PNG stream；測試驗證真正handler的Dispose，不測此替身自身邏輯。</summary>
    private sealed class Renderer : IRichMenuImageRenderer
    {
        /// <summary>合成PNG的固定版本，不包含本案文字或scope。</summary>
        public string ProfileKey => "synthetic-v1";
        internal int Active;
        internal int Disposed;
        internal List<string> Texts { get; } = [];
        internal Func<Task>? BeforeRender { get; init; }
        /// <summary>複製本次文字後交出單一owner的stream，沒有背景工作或外部資源。</summary>
        public async Task<Stream> RenderAsync(ImagemapSize size, IReadOnlyList<RichMenuImageLabel> labels, CancellationToken cancellationToken)
        {
            if (BeforeRender is not null) await BeforeRender();
            Texts.AddRange(labels.Select(label => label.Text));
            Interlocked.Increment(ref Active);
            return new TrackedStream(this);
        }
        /// <summary>每個stream僅計算一次Dispose，失敗或成功均可驗證active回零。</summary>
        private sealed class TrackedStream(Renderer owner) : MemoryStream(new byte[] { 137, 80, 78, 71 })
        {
            private bool _disposed;
            /// <summary>由handler呼叫的唯一釋放路徑；不依賴finalizer補償。</summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing && !_disposed)
                {
                    _disposed = true;
                    Interlocked.Decrement(ref owner.Active);
                    Interlocked.Increment(ref owner.Disposed);
                }
                base.Dispose(disposing);
            }
        }
    }
}
