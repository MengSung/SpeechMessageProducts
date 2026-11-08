/*
 * 檔案整體說明
 *
 * 目的與定位：P2-04 雙目標傳輸契約，使用真 LinePushClient 與合成 HttpMessageHandler，
 * 驗證錯教會完全無 HTTP、共享頻道仍各自 sender、alias 缺失為永久失敗。
 * 主要功能與流程：在傳輸邊界解析真正送出的 JSON 及逐請求 Authorization／Retry Key，
 * 注入 HTTP 200／404 並確認回應 Dispose；不讓 mock 回傳值取代真正產品行為。
 * 使用邊界與維護：每案 using 擁有 handler、client 與回應內容；純合成 ID／憑證不連
 * LINE 或 D365，不讀 User Secrets。並行案例用 barrier 保證請求重疊，完成後 active
 * 歸零，資料集合只在案例中保留並由 fixture 釋放，不冒稱實機收訊或隔離驗收。
 */
using System.Net;
using System.Net.Http;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LineMessagingProcessor.Tests;

/// <summary>以真 HTTP 管線驗會友教會與 sender 契約，fixture 釋放所有合成傳輸資源。</summary>
public sealed class LineMemberPushTests
{
    private const string User = "U00000000000000000000000000000000";
    private static readonly Guid ChurchA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ChurchB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Channel = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

    /// <summary>錯會友教會、錯指派來源教會、指派ID與實際頻道不符，各自拒絕且送出次數為零。</summary>
    [Fact]
    public async Task Push_ChurchMismatch_RejectedWithoutHttp()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var client = new LinePushClient(http);
        foreach (var mismatch in new[] {
            (new MemberRecipient(Guid.NewGuid(), User, ChurchB, null), ChannelFor(ChurchA)),
            (Member(ChurchA), ChannelFor(ChurchB)),
            (Member(ChurchA), new ChurchChannel(ChurchA, Guid.NewGuid(), Channel, new("synthetic"), "A")) })
        {
            var result = await client.PushToMemberAsync(mismatch.Item1, mismatch.Item2, ChurchA, Messages(), Guid.NewGuid(), default);
            Assert.Equal(LinePushOutcome.Rejected, result.Outcome);
            Assert.Equal("ChurchMismatch", result.Reason);
        }
        Assert.Empty(handler.Bodies);
    }

    /// <summary>同一Channel的兩教會覆寫來源訊息sender，各自真正HTTP內容及憑證互不污染，來源訊息未被修改。</summary>
    [Fact]
    public async Task Push_SharedChannel_TwoChurches_EachUsesOwnSender()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var client = new LinePushClient(http);
        var message = new TextMessage("synthetic") { Sender = new Sender("caller", "https://example.invalid/icon.png") };
        await client.PushToMemberAsync(Member(ChurchA), ChannelFor(ChurchA), ChurchA, new ISendMessage[] { message }, Guid.NewGuid(), default);
        await client.PushToMemberAsync(Member(ChurchB), ChannelFor(ChurchB), ChurchB, new ISendMessage[] { message }, Guid.NewGuid(), default);
        Assert.Equal("Alpha", JObject.Parse(handler.Bodies[0])["messages"]![0]!["sender"]!["name"]!.Value<string>());
        Assert.Equal("Beta", JObject.Parse(handler.Bodies[1])["messages"]![0]!["sender"]!["name"]!.Value<string>());
        Assert.Equal("caller", message.Sender.Name);
        Assert.Equal(2, handler.DisposedResponses);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    /// <summary>alias端點404只查一次且不執行綁定；回傳永久AliasNotFound並釋放回應。</summary>
    [Fact]
    public async Task LinkRichMenu_AliasNotFound_Permanent()
    {
        using var handler = new Handler { Status = HttpStatusCode.NotFound };
        using var http = new HttpClient(handler);
        var result = await new LinePushClient(http).LinkRichMenuToUserAsync(Member(ChurchA), ChannelFor(ChurchA),
            ChurchA, "alpha-main-top-zh-TW", default);
        Assert.Equal(LinePushOutcome.Permanent, result.Outcome);
        Assert.Equal("AliasNotFound", result.Reason);
        Assert.Single(handler.Urls);
        Assert.EndsWith("/richmenu/alias/alpha-main-top-zh-TW", handler.Urls[0]);
        Assert.Equal(1, handler.DisposedResponses);
    }

    /// <summary>合法alias須先解析正式menu ID再綁定，解除用DELETE；三份回應全釋放且沒有預設Authorization。</summary>
    [Fact]
    public async Task RichMenu_LinkAndUnlink_UseResolvedIdentityAndDispose()
    {
        const string menu = "richmenu-00000000000000000000000000000000";
        using var handler = new Handler
        {
            ResponseBody = "{\"richMenuAliasId\":\"alpha-main-top-zh-TW\",\"richMenuId\":\"" + menu + "\"}"
        };
        using var http = new HttpClient(handler);
        var client = new LinePushClient(http);
        Assert.True((await client.LinkRichMenuToUserAsync(Member(ChurchA), ChannelFor(ChurchA),
            ChurchA, "alpha-main-top-zh-TW", default)).Success);
        Assert.True((await client.UnlinkRichMenuAsync(Member(ChurchA), ChannelFor(ChurchA), ChurchA, default)).Success);
        Assert.Equal(new[] { "GET", "POST", "DELETE" }, handler.Methods);
        Assert.EndsWith("/user/" + User + "/richmenu/" + menu, handler.Urls[1]);
        Assert.EndsWith("/user/" + User + "/richmenu", handler.Urls[2]);
        Assert.Equal(3, handler.DisposedResponses);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    /// <summary>alias成功本文錯身分、重複欄位或超預算，皆不可繼續綁定且釋放回應。</summary>
    [Theory]
    [InlineData("{\"richMenuAliasId\":\"other\",\"richMenuId\":\"richmenu-00000000000000000000000000000000\"}")]
    [InlineData("{\"richMenuAliasId\":\"alpha-main-top-zh-TW\",\"richMenuAliasId\":\"alpha-main-top-zh-TW\",\"richMenuId\":\"richmenu-00000000000000000000000000000000\"}")]
    [InlineData("")]
    public async Task RichMenu_InvalidResponse_FailsBeforeBinding(string body)
    {
        using var handler = new Handler { ResponseBody = body.Length == 0 ? new string('x', 4097) : body };
        using var http = new HttpClient(handler);
        var result = await new LinePushClient(http).LinkRichMenuToUserAsync(Member(ChurchA), ChannelFor(ChurchA),
            ChurchA, "alpha-main-top-zh-TW", default);
        Assert.Equal("InvalidResponse", result.Reason);
        Assert.Equal(LinePushOutcome.Permanent, result.Outcome);
        Assert.Single(handler.Urls);
        Assert.Equal(1, handler.DisposedResponses);
    }

    /// <summary>caller取消先於序列化與HTTP；以屬性注入超限文字繞過舊模型建構截斷，集合超限也必須零HTTP。</summary>
    [Fact]
    public async Task MemberPush_CancelledOrOversize_NoHttp()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var client = new LinePushClient(http);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PushToMemberAsync(Member(ChurchA),
            ChannelFor(ChurchA), ChurchA, Messages(), Guid.NewGuid(), caller.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => client.PushToMemberAsync(Member(ChurchA), ChannelFor(ChurchA),
            ChurchA, new ISendMessage[] { new TextMessage("x") { Text = new string('x', 5001) } }, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => client.PushToMemberAsync(Member(ChurchA), ChannelFor(ChurchA),
            ChurchA, Enumerable.Range(0, 6).Select(_ => (ISendMessage)new TextMessage("x")).ToArray(), Guid.NewGuid(), default));
        Assert.Empty(handler.Urls);
    }

    /// <summary>每次建立獨立正式會友ID，沒有真實LINE帳號。</summary>
    private static MemberRecipient Member(Guid church) => new(Guid.NewGuid(), User, church, "zh-TW");
    /// <summary>共享頻道ID但每次教會指派與sender獨立，不借用其他教會快照。</summary>
    private static ChurchChannel ChannelFor(Guid church) => new(church, Channel, Channel, new("synthetic"),
        church == ChurchA ? "Alpha" : "Beta");
    /// <summary>來源集合由本次呼叫擁有，正式入口必須凍結而非保存可變模型。</summary>
    private static ISendMessage[] Messages() => new ISendMessage[] { new TextMessage("synthetic") };

    /// <summary>測試owner擁有的離線HTTP邊界，不建立背景工作或真實socket。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        internal readonly List<string> Bodies = new();
        internal readonly List<string> Urls = new();
        internal readonly List<string> Methods = new();
        internal int DisposedResponses;
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        internal string ResponseBody { get; set; } = "{}";
        /// <summary>只記錄合成請求資料，回應內容由Core確定性Dispose。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            Methods.Add(request.Method.Method);
            if (request.Content is not null) Bodies.Add(await request.Content.ReadAsStringAsync());
            return new HttpResponseMessage(Status) { Content = new Content(this, ResponseBody) };
        }
        /// <summary>測試結束清除保留的合成內容，避免測試自身造成資料留存。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Bodies.Clear(); Urls.Clear(); Methods.Clear(); }
            base.Dispose(disposing);
        }
        /// <summary>回應釋放探針，僅由Core傳輸owner擁有並Dispose。</summary>
        private sealed class Content(Handler owner, string body) : StringContent(body)
        {
            /// <summary>記錄確定性釋放，不以GC取代資源清理契約。</summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing) Interlocked.Increment(ref owner.DisposedResponses);
                base.Dispose(disposing);
            }
        }
    }
}
