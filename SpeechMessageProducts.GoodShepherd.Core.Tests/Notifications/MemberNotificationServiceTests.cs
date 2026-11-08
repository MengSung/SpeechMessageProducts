/*
 * 檔案整體說明
 *
 * 目的與定位：P2-04 會友通知的雙目標離線契約，串接真實語言resolver、字典、解密
 * 及Core HTTP管線；只把資料來源與網路替換成合成資料，不連D365或LINE。
 * 主要功能與流程：未綁定／錯教會必須在讀頻道與HTTP前拒絕；實際JSON、Authorization、
 * Retry Key及sender逐項斷言。A/B各100次用雙入場barrier證明重疊，不讓同步逐一
 * 執行冒充隔離測試；最後回應Dispose數與active歸零。
 * 使用邊界與維護：每案fixture擁有HttpClient、handler及合成key，Dispose先關傳輸、
 * 清記錄再清零key；來源只存合成封包，沒有全域教會狀態。所有Task在案例結束前
 * 完整等待，所有barrier有五秒期限；這些證據不是實機驗收或手機收訊。
 */
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using ToolUtilityNameSpace.LineRecipients;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Notifications;

/// <summary>以真共用鏈驗證產品組合與隔離，fixture負責傳輸及合成秘密的確定性清理。</summary>
public sealed class MemberNotificationServiceTests
{
    private static readonly string[] Cultures = { "zh-TW", "qps-ploc" };
    private const string LineA = "U00000000000000000000000000000000";
    private const string LineB = "U11111111111111111111111111111111";
    private static readonly TextKey MessageKey = new("Common.Diagnostics.Ping");

    /// <summary>空綁定在讀頻道、解密與HTTP前回NotBound，不以空字串建立假成功。</summary>
    [Fact]
    public async Task Notify_MemberNotBound_ReturnsNotBound()
    {
        using var fixture = new Fixture();
        var source = fixture.Source(line: null);
        var result = await fixture.Service.NotifyAsync(source, Context(source.Church), source.Contact, Guid.NewGuid(),
            fixture.Key, MessageKey, default);
        Assert.Equal(LinePushOutcome.Rejected, result.Outcome);
        Assert.Equal("NotBound", result.Reason);
        Assert.Equal(0, source.ChannelReads);
        Assert.Empty(fixture.Handler.Sent);
    }

    /// <summary>真resolver→catalog→JSON：繁中未包框、偽語系有包框，且sender使用同一決定語系。</summary>
    [Theory]
    [InlineData("zh-TW", false)]
    [InlineData("qps-ploc", true)]
    public async Task Notify_UsesMemberLanguage_ZhTwAndPseudo(string preference, bool pseudo)
    {
        using var fixture = new Fixture();
        var source = fixture.Source(language: preference);
        var result = await fixture.Service.NotifyAsync(source, Context(source.Church), source.Contact, Guid.NewGuid(),
            fixture.Key, MessageKey, default);
        Assert.True(result.Success);
        var sent = Assert.Single(fixture.Handler.Sent);
        if (pseudo)
        {
            Assert.StartsWith("［", sent.Text, StringComparison.Ordinal);
            Assert.Contains("ǂ", sent.Text, StringComparison.Ordinal);
            Assert.StartsWith("［Alpha", sent.Sender, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("連線測試成功", sent.Text);
            Assert.Equal("Alpha助手", sent.Sender);
        }
        Assert.Equal(1, fixture.Handler.Disposed);
    }

    /// <summary>診斷Flex入口於兩產品目標走真嵌入資源、語言、解密與HTTP；callback只收到選定語系。</summary>
    [Theory]
    [InlineData("zh-TW", "連線測試成功")]
    [InlineData("qps-ploc", "［連線測試成功ǂǂǂ］")]
    public async Task NotifyDiagnostic_EmbeddedTemplate_UsesResolvedCulture(string language, string expected)
    {
        using var fixture = new Fixture();
        var source = fixture.Source(language: language);
        string? resolved = null;
        var record = Guid.NewGuid();
        var result = await fixture.Service.NotifyDiagnosticAsync(source, Context(source.Church), source.Contact,
            record, fixture.Key, value => resolved = value, default);
        Assert.True(result.Success);
        var sent = Assert.Single(fixture.Handler.Sent);
        Assert.Equal(expected, sent.Text);
        Assert.Equal(language, resolved);
        Assert.Equal(record.ToString("D"), sent.Retry);
        Assert.Equal(0, fixture.Handler.Active);
        Assert.Equal(1, fixture.Handler.Disposed);
    }

    /// <summary>來源正式記錄ID原樣成為真正HTTP X-Line-Retry-Key，不重生Guid或使用Contact ID。</summary>
    [Fact]
    public async Task Notify_RetryKeyIsSourceRecordId()
    {
        using var fixture = new Fixture();
        var source = fixture.Source();
        var record = Guid.NewGuid();
        await fixture.Service.NotifyAsync(source, Context(source.Church), source.Contact, record, fixture.Key, MessageKey, default);
        Assert.Equal(record.ToString("D"), Assert.Single(fixture.Handler.Sent).Retry);
    }

    /// <summary>字典合法3000字文字不得被舊TextMessage建構式截成2000，最終JSON必須逐字相同。</summary>
    [Fact]
    public async Task Notify_LongCatalogText_NoSilentTruncation()
    {
        var text = new string('x', 3000);
        using var fixture = new Fixture(new LongCatalog(text));
        var source = fixture.Source();
        await fixture.Service.NotifyAsync(source, Context(source.Church), source.Contact, Guid.NewGuid(),
            fixture.Key, MessageKey, default);
        Assert.Equal(text, Assert.Single(fixture.Handler.Sent).Text);
    }

    /// <summary>真通知鏈遇長教會名時使用正式Brand.ShortName資源，不能只測獨立factory卻未接入傳輸。</summary>
    [Fact]
    public async Task Notify_LongChurch_UsesCatalogShortSender()
    {
        using var fixture = new Fixture();
        var source = fixture.Source(name: new string('A', 30));
        var result = await fixture.Service.NotifyAsync(source, Context(source.Church), source.Contact, Guid.NewGuid(),
            fixture.Key, MessageKey, default);
        Assert.True(result.Success);
        Assert.Equal("助手", Assert.Single(fixture.Handler.Sent).Sender);
        Assert.Equal(1, fixture.Handler.Disposed);
    }

    /// <summary>錯來源教會在讀取／解密任何頻道前拒絕，避免以語言或共享頻道繞過教會核對。</summary>
    [Fact]
    public async Task Notify_ForeignMember_RejectsBeforeChannelRead()
    {
        using var fixture = new Fixture();
        var source = fixture.Source();
        var result = await fixture.Service.NotifyAsync(source, Context(Guid.NewGuid()), source.Contact, Guid.NewGuid(),
            fixture.Key, MessageKey, default);
        Assert.Equal("ChurchMismatch", result.Reason);
        Assert.Equal(0, source.ChannelReads);
        Assert.Empty(fixture.Handler.Sent);
    }

    /// <summary>A/B各100次共享client但不同教會／Token／userId／sender；每輪真重疊，完成後資源回零。</summary>
    [Fact]
    public async Task Notify_ParallelDifferentChurches_NoCrossTalk()
    {
        using var fixture = new Fixture();
        var a = fixture.Source(line: LineA);
        var b = fixture.Source(name: "Beta", line: LineB);
        for (var index = 0; index < 100; index++)
        {
            var entered = 0;
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Handler.Gate = async () =>
            {
                if (Interlocked.Increment(ref entered) == 2) gate.TrySetResult(true);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var registration = deadline.Token.Register(() => gate.TrySetCanceled());
                await gate.Task;
            };
            var first = fixture.Service.NotifyAsync(a, Context(a.Church), a.Contact, Guid.NewGuid(), fixture.Key, MessageKey, default);
            var second = fixture.Service.NotifyAsync(b, Context(b.Church), b.Contact, Guid.NewGuid(), fixture.Key, MessageKey, default);
            await Task.WhenAll(first, second);
        }
        Assert.Equal(200, fixture.Handler.Sent.Count);
        foreach (var sent in fixture.Handler.Sent)
        {
            var alpha = sent.To == LineA;
            Assert.True(alpha || sent.To == LineB);
            Assert.Equal(alpha ? "token-Alpha" : "token-Beta", sent.Token);
            Assert.Equal(alpha ? "Alpha助手" : "Beta助手", sent.Sender);
        }
        Assert.Equal(0, fixture.Handler.Active);
        Assert.Equal(200, fixture.Handler.Disposed);
        Assert.Null(fixture.Http.DefaultRequestHeaders.Authorization);
    }

    /// <summary>完整組織／產品／教會範圍由測試伺服器決定，文化只供解析，不作授權。</summary>
    private static TextContext Context(Guid church) => new(church, "zh-TW", "zh-TW", "AlphaOrg", "GoodShepherd");

    /// <summary>單案唯一資源owner；不讓並行測試共用key、handler或紀錄。</summary>
    private sealed class Fixture : IDisposable
    {
        internal byte[] Key { get; } = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        internal Handler Handler { get; } = new();
        internal HttpClient Http { get; }
        internal MemberNotificationService Service { get; }
        /// <summary>建立真resolver／catalog／Core，HTTP只接單案合成handler。</summary>
        internal Fixture(ITextCatalog? catalog = null)
        {
            Http = new HttpClient(Handler);
            Service = new MemberNotificationService(new LinePushClient(Http),
                new MemberCultureResolver(new LocalizationOptions(Cultures, allowPseudoLocale: true)),
                catalog ?? new TextCatalog(new SystemTextTable(), new EmptyOverrides()));
        }
        /// <summary>頻道ID共享而教會ID及操作純值分離，封包使用合成key即時產生。</summary>
        internal Source Source(string name = "Alpha", string? line = LineA, string language = "zh-TW") =>
            new(name, line, language, ChannelTokenProtector.Protect("token-" + name, Key));
        /// <summary>所有案例Task已排空後才關閉client並清零自己擁有的key。</summary>
        public void Dispose() { Http.Dispose(); Array.Clear(Key, 0, Key.Length); }
    }
    /// <summary>只回傳當次合成資料，實際ColumnSet在ToolUtility雙目標測試另驗。</summary>
    private sealed class Source(string name, string? line, string language, string envelope) : ILineRecipientSource
    {
        internal Guid Church { get; } = Guid.NewGuid();
        internal Guid Contact { get; } = Guid.NewGuid();
        internal int ChannelReads;
        /// <summary>以正式Contact ID讀回同一列，沒有延後或背景查詢。</summary>
        public MemberRecipient ReadMember(Guid contactId, CancellationToken ct)
        {
            Assert.Equal(Contact, contactId);
            return new MemberRecipient(Contact, line, Church, language);
        }
        /// <summary>只允許所屬來源教會，封包與語系同次回傳，不保存解密後Token。</summary>
        public ChurchChannelRecord ReadChannel(Guid sourceChurchId, CancellationToken ct)
        {
            Assert.Equal(Church, sourceChurchId);
            Interlocked.Increment(ref ChannelReads);
            var channel = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
            return new ChurchChannelRecord(Church, channel, channel, name, envelope,
                new ChurchLocaleSettings("zh-TW", Cultures));
        }
    }
    /// <summary>真catalog使用的無覆寫來源，只建立當次完整scope空快照，不建cache。</summary>
    private sealed class EmptyOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>不保存context或快照，語言由真實資源解析器決定。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
    /// <summary>僅注入有界長字典值以鎖定傳輸內容，不替換真正Core序列化與HTTP。</summary>
    private sealed class LongCatalog(string text) : ITextCatalog
    {
        /// <summary>sender保持短名稱，只有訊息值超過舊建構式2000界限。</summary>
        public string GetText(TextContext context, TextKey key, params TextArg[] args) =>
            key.Equals(MessageKey) ? text : "Alpha助手";
    }
    /// <summary>只保留斷言所需的合成HTTP純值，不保存request／response。</summary>
    private sealed record Sent(string To, string Text, string Sender, string Token, string Retry);
    /// <summary>單案網路替身；活動數與Dispose數由真正SendAsync／Core回應owner驅動。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        internal ConcurrentQueue<Sent> Sent { get; } = new();
        internal Func<Task>? Gate { get; set; }
        internal int Active;
        internal int Disposed;
        /// <summary>解析真正序列化輸出並可在雙入場barrier等待；不連任何外部端點。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Active);
            try
            {
#if NETFRAMEWORK
                var json = JObject.Parse(await request.Content!.ReadAsStringAsync());
#else
                var json = JObject.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
#endif
                Sent.Enqueue(new Sent(json["to"]!.Value<string>()!, (json["messages"]![0]!["text"] ?? json["messages"]![0]!["altText"])!.Value<string>()!,
                    json["messages"]![0]!["sender"]!["name"]!.Value<string>()!, request.Headers.Authorization!.Parameter!,
                    request.Headers.GetValues("X-Line-Retry-Key").Single()));
                if (Gate is not null) await Gate();
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new Content(this) };
                response.Headers.Add("x-line-request-id", "synthetic-request");
                return response;
            }
            finally { Interlocked.Decrement(ref Active); }
        }
        /// <summary>測試owner清空所有合成Token與訊息參照，沒有未觀測背景工作。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { while (Sent.TryDequeue(out _)) { } Gate = null; }
            base.Dispose(disposing);
        }
        /// <summary>回應的唯一owner為Core using，不靠GC釋放。</summary>
        private sealed class Content(Handler owner) : StringContent("{}")
        {
            /// <summary>提供確定性Dispose計數，證明200個請求全部歸還資源。</summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing) Interlocked.Increment(ref owner.Disposed);
                base.Dispose(disposing);
            }
        }
    }
}
