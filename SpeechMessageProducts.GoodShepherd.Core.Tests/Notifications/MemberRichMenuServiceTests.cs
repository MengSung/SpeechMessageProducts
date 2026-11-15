/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-04 會友語言換綁服務，真實語系政策、alias 命名與 LINE client
 * 使用離線合成來源及 HTTP 替身；不連 D365 或 LINE，不宣稱實機驗收。
 * 主要功能與流程：以語言改變、未綁定、非法教會代碼、跨教會／頻道錯配及壞密文注入，
 * 決定性斷言 alias、正式 Contact 身分、零傳輸及固定拒絕分類；另驗模式互斥與取消。
 * 使用邊界與維護：每案 fixture 擁有 client、handler 與合成金鑰；所有 Task 完成後 Dispose
 * 連線替身並清零 key，不跨案保存資料。HTTP 只保留有界測試路徑，回應內容有 Dispose 計數，
 * 保護完成即歸還資源與同一服務 A→B→A 不殘留會友／憑證的契約。
 */
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.LineRecipients;
using Xunit;
namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Notifications;
/// <summary>使用真實雙目標核心與合成 HTTP，保護換綁前驗證、影子無解密及資源釋放。</summary>
public sealed class MemberRichMenuServiceTests
{
    /// <summary>固定且僅供複製的合成啟用政策；不保存當案會友或教會資料。</summary>
    private static readonly string[] Cultures = { "zh-TW", "en-US" };
    /// <summary>同一服務先繁中再英文再繁中；每次解析當下偏好，確定 lookup 與綁定未沿用前次結果。</summary>
    [Fact]
    public async Task MemberLanguage_Changed_LinksAliasForNewCulture()
    {
        using var fixture = new Fixture();
        foreach (var culture in new[] { "zh-TW", "en-US", "zh-TW" })
        {
            var source = new Source(fixture.Envelope, culture);
            var result = await fixture.Link(source, fixture.Key, null);
            Assert.True(result.Success);
            Assert.Equal("/v2/bot/richmenu/alias/ch-main-home-" + culture.ToLowerInvariant(), fixture.Handler.Paths.ToArray()[fixture.Handler.Paths.Count - 2]);
            Assert.Equal("/v2/bot/user/" + Source.UserId + "/richmenu/" + Handler.MenuId, fixture.Handler.Paths.Last());
        }
        Assert.Equal(6, fixture.Handler.DisposedResponses);
        Assert.Null(fixture.Http.DefaultRequestHeaders.Authorization);
    }
    /// <summary>未綁定搭配壞密文與非法 alias；兩模式均先拒絕，連頻道都不讀，回呼與 HTTP 皆零次。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberLanguage_NotBound_NoCall(bool shadow)
    {
        using var fixture = new Fixture();
        var source = new Source("invalid") { LineId = null };
        var calls = 0;
        var result = await fixture.Link(source, shadow ? null : fixture.Key,
            shadow ? (_, _) => calls++ : null, "INVALID");
        Assert.Equal("NotBound", result.Reason);
        Assert.Equal(0, source.ChannelReads);
        Assert.Equal(0, calls);
        Assert.Empty(fixture.Handler.Paths);
    }
    /// <summary>教會代碼不小寫清洗；在壞密文之前轉為固定業務鍵，兩模式均不發布 alias 或 HTTP。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberLanguage_ChurchCodeInvalidForAlias_Rejected(bool shadow)
    {
        using var fixture = new Fixture();
        var calls = 0;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Link(new Source("invalid"),
            shadow ? null : fixture.Key, shadow ? (_, _) => calls++ : null, "CH"));
        Assert.Equal("Common.Plugin.RuleRejected", error.TextKey.Value);
        Assert.Empty(error.Arguments);
        Assert.Equal(0, calls);
        Assert.Empty(fixture.Handler.Paths);
    }
    /// <summary>來源教會、頻道教會、空頻道與指派錯配各自注入；兩模式均在語系／alias／解密之前拒絕。</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public async Task MemberLanguage_ForeignChurchOrChannel_NoCall(bool shadow, int fault)
    {
        using var fixture = new Fixture();
        var source = new Source("invalid") { Fault = fault };
        var calls = 0;
        var result = await fixture.Link(source, shadow ? null : fixture.Key,
            shadow ? (_, _) => calls++ : null, "INVALID");
        Assert.Equal("ChurchMismatch", result.Reason);
        Assert.Equal(fault == 0 ? 0 : 1, source.ChannelReads);
        Assert.Equal(0, calls);
        Assert.Empty(fixture.Handler.Paths);
    }
    /// <summary>無效密文在 Live 必定失敗；Shadow 只傳 Contact GUID 與 alias，證明影子不讀解密結果。</summary>
    [Fact]
    public async Task MemberLanguage_ShadowInvalidCiphertext_RecordsOnlyIdentityAndAlias()
    {
        using var fixture = new Fixture();
        var source = new Source("invalid", "en-US");
        var aliases = new List<(Guid Contact, string Alias)>();
        var result = await fixture.Link(source, null, (id, alias) => aliases.Add((id, alias)));
        Assert.Equal((source.Contact, "ch-main-home-en-us"), Assert.Single(aliases));
        Assert.False(result.Success);
        Assert.Equal("ShadowRecorded", result.Reason);
        Assert.Empty(fixture.Handler.Paths);
        await Assert.ThrowsAsync<ChannelTokenProtectionException>(() => fixture.Link(source, fixture.Key, null));
        Assert.Empty(fixture.Handler.Paths);
    }
    /// <summary>雙模式或無模式在讀取來源前固定拒絕；禁止以 sink 與 key 的優先序暗選模式。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberLanguage_ExactlyOneMode_Required(bool both)
    {
        using var fixture = new Fixture();
        var source = new Source("invalid");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Link(source,
            both ? fixture.Key : null, both ? (_, _) => { } : null));
        Assert.Equal(0, source.MemberReads);
        Assert.Empty(fixture.Handler.Paths);
    }
    /// <summary>呼叫端預取消不開始 SDK 查詢，亦不發布 Shadow 意圖。</summary>
    [Fact]
    public async Task MemberLanguage_Cancelled_NoReadOrCall()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new Source("invalid");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.LinkAsync(source,
            Context(source.Church), source.Contact, "ch", "main", "home", null, (_, _) => { }, cancellation.Token));
        Assert.Equal(0, source.MemberReads);
        Assert.Empty(fixture.Handler.Paths);
    }
    /// <summary>固定向量鎖定共用規則在兩宿主的文化尾碼，不用同函式呼叫互相比對冒充黃金。</summary>
    [Theory]
    [InlineData("zh-TW", "ch-main-home-zh-tw")]
    [InlineData("en-US", "ch-main-home-en-us")]
    [InlineData("QPS-PLOC", "ch-main-home-qps")]
    public void Alias_CultureSuffix_Golden(string culture, string expected) =>
        Assert.Equal(expected, RichMenuAlias.Build("ch", "main", "home", culture));
    /// <summary>業務識別三個位置均不得小寫清洗；空值、非 ASCII 與完整超長亦明確拒絕。</summary>
    [Theory]
    [InlineData("CH", "main", "home", "zh-TW")]
    [InlineData("ch", "MAIN", "home", "zh-TW")]
    [InlineData("ch", "main", "HOME", "zh-TW")]
    [InlineData("", "main", "home", "zh-TW")]
    [InlineData("教會", "main", "home", "zh-TW")]
    [InlineData("ch", "main", "home", "")]
    [InlineData("abcdefghijklmnopqrstuvwx", "main", "home", "zh-TW")]
    public void Alias_InvalidComponent_Rejects(string church, string combination, string tab, string culture) =>
        Assert.Throws<ArgumentException>(() => RichMenuAlias.Build(church, combination, tab, culture));
    /// <summary>
    /// A/B 各一百次共用服務並以真正未完成 HTTP barrier 重疊；教會、會友、token、alias
    /// 全部分離，斷言每個路徑所屬 token、400 個回應清理與零預設認證殘留。取消 owner
    /// 五秒期限避免測試死鎖，registration 在每輪 drain 後 Dispose。
    /// </summary>
    [Fact]
    public async Task MemberLanguage_ConcurrentChurches_IsolatedAndDrained()
    {
        using var fixture = new Fixture();
        var a = new Source(ChannelTokenProtector.Protect("token-a", fixture.Key));
        var b = new Source(ChannelTokenProtector.Protect("token-b", fixture.Key), "en-US")
            { LineId = "U22222222222222222222222222222222" };
        for (var index = 0; index < 100; index++)
        {
            var entered = 0;
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var registration = deadline.Token.Register(() => gate.TrySetCanceled());
            fixture.Handler.Gate = () =>
            {
                if (Interlocked.Increment(ref entered) == 2) gate.TrySetResult(true);
                return gate.Task;
            };
            var results = await Task.WhenAll(fixture.Link(a, fixture.Key, null, "a"), fixture.Link(b, fixture.Key, null, "b"));
            Assert.All(results, result => Assert.True(result.Success));
            fixture.Handler.Gate = null;
        }
        Assert.Equal(400, fixture.Handler.DisposedResponses);
        Assert.Equal(400, fixture.Handler.Requests.Count);
        foreach (var request in fixture.Handler.Requests)
        {
            var alpha = request.Path.Contains("/alias/a-", StringComparison.Ordinal) || request.Path.Contains(Source.UserId, StringComparison.Ordinal);
            Assert.Equal(alpha ? "token-a" : "token-b", request.Token);
        }
        Assert.Null(fixture.Http.DefaultRequestHeaders.Authorization);
    }
    /// <summary>伺服器已授權的固定產品與組織範圍，語言不是授權條件。</summary>
    private static TextContext Context(Guid church) => new(church, "zh-TW", "zh-TW", "SyntheticOrg", "GoodShepherd");
    /// <summary>每案唯一資源 owner；只借用真實核心策略，不使用全域 client 或秘密。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>合成加密材料僅供當案建立封包，Dispose 清零。</summary>
        internal byte[] Key { get; } = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        /// <summary>記錄當案必要的固定測試路徑並計算回應清理。</summary>
        internal Handler Handler { get; } = new();
        /// <summary>由本 fixture 擁有並連帶釋放 handler 的 HTTP client。</summary>
        internal HttpClient Http { get; }
        /// <summary>可跨當案多次操作重用且不得持有操作狀態的服務。</summary>
        internal MemberRichMenuService Service { get; }
        /// <summary>以合成 key 即時產生的有效封包，只活到 fixture 結束。</summary>
        internal string Envelope { get; }
        /// <summary>接線真實 resolver、加密與傳輸管線，網路只走合成 handler。</summary>
        internal Fixture()
        {
            Http = new HttpClient(Handler);
            Service = new MemberRichMenuService(new LinePushClient(Http),
                new MemberCultureResolver(new LocalizationOptions(Cultures)));
            Envelope = ChannelTokenProtector.Protect("synthetic-token", Key);
        }
        /// <summary>統一當案呼叫並保留測試對 key／sink 模式及教會代碼的故障注入。</summary>
        internal Task<LinePushResult> Link(Source source, byte[]? key, Action<Guid, string>? sink, string code = "ch") =>
            Service.LinkAsync(source, Context(source.Church), source.Contact, code, "main", "home", key, sink, default);
        /// <summary>所有呼叫已 await 後確定釋放 client／handler，並清零單案金鑰。</summary>
        public void Dispose() { Http.Dispose(); Array.Clear(Key, 0, Key.Length); }
    }
    /// <summary>當案純值來源；可注入所屬教會或頻道錯配，不存 SDK／解密憑證。</summary>
    private sealed class Source(string envelope, string language = "zh-TW") : ILineRecipientSource
    {
        /// <summary>固定合成且格式合法的 LINE 身分，不代表任何真人。</summary>
        internal const string UserId = "U11111111111111111111111111111111";
        /// <summary>當案來源教會正式合成主鍵。</summary>
        internal Guid Church { get; } = Guid.NewGuid();
        /// <summary>當案會友正式合成主鍵，禁止以 LINE 身分代替。</summary>
        internal Guid Contact { get; } = Guid.NewGuid();
        /// <summary>可注入未綁定資料；每次查詢建立獨立收件值。</summary>
        internal string? LineId { get; set; } = UserId;
        /// <summary>缺省無故障；零為會友錯教會，一為頻道錯教會，二為指派不符，三為空頻道。</summary>
        internal int Fault { get; set; } = -1;
        /// <summary>讀取計數證明拒絕與取消均未開始後續 I/O。</summary>
        internal int MemberReads { get; private set; }
        /// <summary>頻道讀取計數用於判斷先驗拒絕順序。</summary>
        internal int ChannelReads { get; private set; }
        /// <summary>以請求 Contact 核對正式身分，再回傳當次不可變測試值。</summary>
        public MemberRecipient ReadMember(Guid contactId, CancellationToken ct)
        {
            Assert.Equal(Contact, contactId);
            MemberReads++;
            return new MemberRecipient(Contact, LineId, Fault == 0 ? Guid.NewGuid() : Church, language);
        }
        /// <summary>以固定欄位故障注入核對 channel 指派，壞密文用來證明未提前解密。</summary>
        public ChurchChannelRecord ReadChannel(Guid sourceChurchId, CancellationToken ct)
        {
            Assert.Equal(Church, sourceChurchId);
            ChannelReads++;
            var channel = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
            return new ChurchChannelRecord(Fault == 1 ? Guid.NewGuid() : Church,
                Fault == 2 ? Guid.NewGuid() : channel, Fault == 3 ? Guid.Empty : channel,
                "Synthetic", envelope, new ChurchLocaleSettings("zh-TW", Cultures));
        }
    }
    /// <summary>單案離線 HTTP 替身；只保存有限案例的路徑，不保留 request、Token 或 response。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        /// <summary>固定合成 provider 正式識別，與 alias 不同。</summary>
        internal const string MenuId = "richmenu-11111111111111111111111111111111";
        /// <summary>已完成傳輸的測試路徑清單，Dispose 清空。</summary>
        internal ConcurrentQueue<string> Paths { get; } = new();
        /// <summary>有限合成 token 與路徑配對，用來決定性核對 A/B 隔離；Dispose 清空。</summary>
        internal ConcurrentQueue<(string Path, string Token)> Requests { get; } = new();
        /// <summary>只在當輪借用測試擁有的 barrier，所有 Task 排空後清除。</summary>
        internal Func<Task>? Gate { get; set; }
        /// <summary>由核心 response owner 觸發的確定性清理計數。</summary>
        internal int DisposedResponses;
        /// <summary>lookup 回傳同 alias 的正式 menu ID；綁定回空物件，不建立真實網路工作。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Enqueue(path);
            Requests.Enqueue((path, request.Headers.Authorization!.Parameter!));
            if (Gate is not null) await Gate();
            var body = request.Method == HttpMethod.Get
                ? "{\"richMenuAliasId\":\"" + path.Substring(path.LastIndexOf('/') + 1) + "\",\"richMenuId\":\"" + MenuId + "\"}"
                : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new Content(this, body) };
        }
        /// <summary>fixture Dispose 後移除合成路徑，沒有未觀測背景工作。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                while (Paths.TryDequeue(out _)) { }
                while (Requests.TryDequeue(out _)) { }
                Gate = null;
            }
            base.Dispose(disposing);
        }
        /// <summary>回應內容由真實 Core 的 using 唯一擁有，測試只觀測清理次數。</summary>
        private sealed class Content(Handler owner, string body) : StringContent(body)
        {
            /// <summary>確定性計數用來證明 lookup 與 link 回應都在完成前釋放。</summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing) Interlocked.Increment(ref owner.DisposedResponses);
                base.Dispose(disposing);
            }
        }
    }
}
