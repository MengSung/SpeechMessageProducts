/*
 * 檔案整體說明
 * 目的與定位：P5-12 外送 letter 的雙目標規則測試，以真正通知服務、Token 保護器
 * 與 LINE HTTP 組裝管線，驗證單人、正式活動重試鍵、原文、sender 及失敗分類。
 * 主要流程：案例只替換最底層 HttpMessageHandler 與純值收件來源；200／409／403／
 * 429／503 與傳輸例外由 owner 注入，決定性斷言檢查真正送出的 JSON、標頭及回傳
 * 分類。另掃描本模組產品來源，拒絕群送端點；只讀本工作樹、不遍歷父方案。快速行銷測試依一百個正式活動 ID 分開執行，不以姓名或內容去重。
 * 資源與隔離：每案 using fixture 唯一擁有 HttpClient／handler 與合成金鑰；Task
 * 全部 await 後清除 key、本文與重試集合。Shadow 不觸網，沒有真 D365、LINE、
 * Secrets、Session、Timer 或背景資源；離線 HTTP 替身不代表平台實機送達驗收。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using ToolUtilityNameSpace.LineRecipients;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Messaging;

/// <summary>保護單活動單收件者與無跨教會資料沿用的離線契約。</summary>
public sealed class OutboundLetterRuleTests
{
    /// <summary>入站即使內容無效、來源會拋錯也不得讀來源、送 HTTP 或回寫完成。</summary>
    [Fact]
    public async Task Letter_Inbound_Ignored()
    {
        using var f = new Fixture { ReadFailure = new InvalidOperationException("synthetic") };
        var result = await f.Send(outbound: false);
        Assert.Equal(OutboundLetterStatus.Ignored, result.Status);
        Assert.Equal(0, f.Reads);
        Assert.Equal(0, f.Calls);
    }

    /// <summary>透過真通知及 HTTP 組裝，to 為單一字串，重試標頭必須逐字等於正式活動 ID。</summary>
    [Fact]
    public async Task Letter_Outbound_PushOneRecipient_RetryKeyIsActivityId()
    {
        using var f = new Fixture();
        var result = await f.Send();
        Assert.Equal(OutboundLetterStatus.Completed, result.Status);
        Assert.Equal(1, f.Calls);
        Assert.Equal(f.ActivityId.ToString("D"), f.RetryKeys.Single());
        Assert.Equal("U00000000000000000000000000000000", (string?)f.Body!["to"]);
        Assert.Single((JArray)f.Body["messages"]!);
    }

    /// <summary>來源教會由已驗 context 固定；收件者改屬另一間教會必須零 HTTP、零頻道讀取。</summary>
    [Fact]
    public async Task Letter_RecipientOtherChurch_FailedReasonChurchMismatch_NoHttp()
    {
        using var f = new Fixture { OtherChurch = true };
        var result = await f.Send();
        Assert.Equal(OutboundLetterStatus.Failed, result.Status);
        Assert.Equal("Messaging.Failure.ChurchMismatch", result.FailureKey);
        Assert.Equal(0, f.Calls);
        Assert.Equal(0, f.ChannelReads);
    }

    /// <summary>未綁定是正常業務拒絕，不解密、不發送且不以重試例外掩蓋固定原因。</summary>
    [Fact]
    public async Task Letter_NotBound_FailedReasonNotBound_NoThrow()
    {
        using var f = new Fixture { Bound = false };
        var result = await f.Send();
        Assert.Equal(OutboundLetterStatus.Failed, result.Status);
        Assert.Equal("Messaging.Failure.NotBound", result.FailureKey);
        Assert.Equal(0, f.Calls);
        Assert.Equal(0, f.ChannelReads);
    }

    /// <summary>永久 4xx 回固定 Blocked 原因；不讀供應商回應本文、不拋系統重試例外。</summary>
    [Fact]
    public async Task Letter_Blocked4xx_FailedReasonBlocked_NoThrow()
    {
        using var f = new Fixture { ResponseStatus = HttpStatusCode.Forbidden };
        var result = await f.Send();
        Assert.Equal(OutboundLetterStatus.Failed, result.Status);
        Assert.Equal("Messaging.Failure.Blocked", result.FailureKey);
        Assert.Equal(1, f.Calls);
    }

    /// <summary>429 及 5xx 必須讓真正呼叫者失敗而非假完成；當次 HTTP 已由 Core 排空。</summary>
    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Letter_429_ThrowsForSystemJobRetry(int status)
    {
        using var f = new Fixture { ResponseStatus = (HttpStatusCode)status };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Send());
        Assert.Equal(1, f.Calls);
    }

    /// <summary>未知例外保留原實例，傳輸故障保留原 inner，不能吞錯或展開敏感訊息。</summary>
    [Fact]
    public async Task Messaging_UnexpectedError_PreservesOriginal()
    {
        var original = new InvalidOperationException("synthetic");
        using var first = new Fixture { ReadFailure = original };
        Assert.Same(original, await Record.ExceptionAsync(() => first.Send()));
        var transport = new HttpRequestException("synthetic transport");
        using var second = new Fixture { TransportFailure = transport };
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => second.Send());
        Assert.Same(transport, failure.InnerException);
    }

    /// <summary>自由文字的空白及 CRLF 原樣送出；唯一 sender 經字典且由伺服器組裝。</summary>
    [Fact]
    public async Task Letter_ContentKeptAsIs_FrameTextLocalized()
    {
        using var f = new Fixture();
        const string content = "  原文，不翻譯\r\nsecond line  ";
        _ = await f.Send(content: content);
        var message = f.Body!["messages"]![0]!;
        Assert.Equal(content, (string?)message["text"]);
        Assert.Equal("Alpha assistant", (string?)message["sender"]!["name"]);
    }

    /// <summary>一百筆不同活動即使收件者／內容相同，仍為一百次獨立單人請求。</summary>
    [Fact]
    public async Task QuickCampaign_100Activities_100IndependentSends()
    {
        using var f = new Fixture();
        for (var index = 1; index <= 100; index++)
        {
            var id = new Guid(index, 0, 0, new byte[8]);
            Assert.Equal(OutboundLetterStatus.Completed, (await f.Send(activityId: id)).Status);
        }
        Assert.Equal(100, f.Calls);
        Assert.Equal(100, f.RetryKeys.Count);
        Assert.Equal(100, f.Accepted);
    }

    /// <summary>HTTP 替身模擬供應商同鍵 409；真 Core 與規則兩次都完成而只接受一次。</summary>
    [Fact]
    public async Task QuickCampaign_SystemJobRerun_NoDuplicateDelivery()
    {
        using var f = new Fixture();
        Assert.Equal(OutboundLetterStatus.Completed, (await f.Send()).Status);
        Assert.Equal(OutboundLetterStatus.Completed, (await f.Send()).Status);
        Assert.Equal(2, f.Calls);
        Assert.Equal(1, f.Accepted);
        Assert.Single(f.RetryKeys);
    }

    /// <summary>Shadow 使用無效密文仍記正式事件意圖；不冒稱已完成且沒有 HTTP。</summary>
    [Fact]
    public async Task Messaging_ShadowMode_NoRealWritesOrHttp()
    {
        using var f = new Fixture { InvalidEnvelope = true };
        var sink = new Sink();
        var result = await f.Rule.RecordAsync(true, f, f.Context, f.ActivityId, f.ContactId,
            "synthetic content", sink, CancellationToken.None);
        Assert.Equal(OutboundLetterStatus.IntentRecorded, result.Status);
        Assert.Equal(f.ActivityId, sink.RetryKey);
        Assert.Equal(f.ContactId, sink.ContactId);
        Assert.Equal(1, sink.Count);
        Assert.Equal(0, f.Calls);
    }

    /// <summary>真正生成字典必須能以固定失敗鍵解析中英文，不用自由文字代替可診斷原因。</summary>
    [Theory]
    [InlineData("NotBound", "未綁定 LINE", "LINE is not linked")]
    [InlineData("ChurchMismatch", "收件者教會不符", "Recipient church does not match")]
    [InlineData("Blocked", "已封鎖或無法接收", "Blocked or unable to receive")]
    public void Messaging_FailureReason_UsesGeneratedDictionary(string suffix, string chinese, string english)
    {
        using var f = new Fixture();
        var catalog = new GoodShepherdTextCatalog(new NoOverrides());
        var key = new TextKey("Messaging.Failure." + suffix);
        Assert.Equal(chinese, catalog.GetText(f.Context, key));
        Assert.Equal(english, catalog.GetText(f.Context.WithCultures("en", "en"), key));
    }

    /// <summary>已取消 caller 不開始讀取，無效身分／空白內容也不允許送出請求。</summary>
    [Fact]
    public async Task Messaging_InvalidInputAndCancellation_NoHttp()
    {
        using var f = new Fixture();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            f.Rule.SendAsync(true, f, f.Context, f.ActivityId, f.ContactId, "content", f.Key, cancel.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Send(activityId: Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Send(content: ""));
        Assert.Equal(0, f.Calls);
    }

    /// <summary>掃描本模組的實際產品來源，禁止新增群送端點；共用 SDK 的其他功能不在此範圍。</summary>
    [Fact]
    public void Source_NoMulticastOrBroadcast()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "SpeechMessageProducts.sln")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var paths = new[] { "SpeechMessageProducts.GoodShepherd.Core", "SpeechMessage.Dynamics.Plugins" };
        var count = 0;
        foreach (var project in paths)
            foreach (var file in Directory.EnumerateFiles(Path.Combine(repository!.FullName, project, "Messaging"), "*.cs"))
            {
                count++;
                var source = File.ReadAllText(file);
                Assert.DoesNotContain("multicast", source, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("broadcast", source, StringComparison.OrdinalIgnoreCase);
            }
        Assert.True(count >= 3);
    }

    /// <summary>來源、HTTP 與金鑰同屬當案 owner；沒有可供下一案例借用的 static 狀態。</summary>
    private sealed class Fixture : HttpMessageHandler, ILineRecipientSource
    {
        internal readonly Guid ActivityId = new("11111111-0000-0000-0000-000000000001");
        internal readonly Guid ContactId = new("11111111-0000-0000-0000-000000000002");
        private readonly Guid church = new("11111111-0000-0000-0000-000000000003");
        internal readonly byte[] Key = Enumerable.Range(0, 64).Select(x => (byte)x).ToArray();
        private readonly HttpClient client;
        private readonly string[] cultures = { "zh-TW" };
        internal readonly OutboundLetterRule Rule;
        internal readonly TextContext Context;
        internal readonly HashSet<string> RetryKeys = new(StringComparer.Ordinal);
        internal bool OtherChurch, InvalidEnvelope, Bound = true;
        internal Exception? ReadFailure, TransportFailure;
        internal HttpStatusCode ResponseStatus = HttpStatusCode.OK;
        internal int Calls, Accepted, Reads, ChannelReads;
        internal JObject? Body;

        /// <summary>真傳輸只透過本案 handler；client 不接管 handler，以免 Dispose 循環。</summary>
        internal Fixture()
        {
            client = new HttpClient(this, disposeHandler: false);
            var catalog = new Catalog();
            Rule = new OutboundLetterRule(new MemberNotificationService(new LinePushClient(client),
                new MemberCultureResolver(new LocalizationOptions()), catalog));
            Context = new TextContext(church, "zh-TW", "zh-TW", "SyntheticOrg", "GoodShepherd");
        }

        /// <summary>每次都走真正產品入口，重跑保留同一正式活動 ID 而不重新產鍵。</summary>
        internal Task<OutboundLetterResult> Send(bool outbound = true, string content = "synthetic",
            Guid? activityId = null) =>
            Rule.SendAsync(outbound, this, Context, activityId ?? ActivityId, ContactId, content, Key, CancellationToken.None);

        /// <summary>可注入來源故障與跨教會，驗證產品在頻道／HTTP 前拒絕。</summary>
        public MemberRecipient ReadMember(Guid id, CancellationToken ct)
        {
            Reads++;
            if (ReadFailure is not null) throw ReadFailure;
            return new MemberRecipient(id, Bound ? "U00000000000000000000000000000000" : null,
                OtherChurch ? Guid.NewGuid() : church, "zh-TW");
        }

        /// <summary>Shadow 故意用壞密文證明不解密；Live 使用僅限本案的合成 token。</summary>
        public ChurchChannelRecord ReadChannel(Guid id, CancellationToken ct)
        {
            ChannelReads++;
            return new ChurchChannelRecord(church, church, church, "Alpha",
                InvalidEnvelope ? "invalid-envelope" : ChannelTokenProtector.Protect("synthetic", Key),
                new ChurchLocaleSettings("zh-TW", cultures));
        }

        /// <summary>模擬 LINE 最底層狀態，不替代真序列化、教會檢查或重試標頭產生。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (TransportFailure is not null) throw TransportFailure;
            Assert.Equal("/v2/bot/message/push", request.RequestUri!.AbsolutePath);
            var key = request.Headers.GetValues("X-Line-Retry-Key").Single();
#if NETFRAMEWORK
            ct.ThrowIfCancellationRequested();
            Body = JObject.Parse(await request.Content!.ReadAsStringAsync());
#else
            Body = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
#endif
            var first = RetryKeys.Add(key);
            if (first && ResponseStatus == HttpStatusCode.OK) Accepted++;
            return new HttpResponseMessage(first ? ResponseStatus : HttpStatusCode.Conflict);
        }

        /// <summary>等待呼叫完成後關閉 client、清除本案資料，再交基底釋放 handler。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                client.Dispose();
                Array.Clear(Key, 0, Key.Length);
                RetryKeys.Clear();
                Body = null;
                ReadFailure = TransportFailure = null;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>只保存單一摘要的 sink，沒有訊息本文、憑證或背景資源。</summary>
    private sealed class Sink : IShadowPushSink
    {
        internal Guid RetryKey, ContactId;
        internal int Count;
        /// <summary>決定性核對真服務傳入的摘要與單人數量，非由測試自行產生產品意圖。</summary>
        public void Record(Guid contactId, string templateKey, Guid retryKey, int messageCount, string contentSha256)
        {
            Assert.Equal("Messaging.OutboundLetter", templateKey);
            Assert.Equal(1, messageCount);
            Assert.Matches("^[a-f0-9]{64}$", contentSha256);
            RetryKey = retryKey; ContactId = contactId; Count++;
        }
    }

    /// <summary>只替換文字來源，不介入原文；固定 sender 明確驗證文字經 catalog。</summary>
    private sealed class Catalog : ITextCatalog
    {
        /// <summary>回傳本案固定框架文字，不持有來源 context 或個資。</summary>
        public string GetText(TextContext context, TextKey key, params TextArg[] args) => "Alpha assistant";
    }

    /// <summary>真字典驗證不使用教會覆寫，回傳目前範圍的空快照且不快取。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>只建立當次純值快照，不持有 SDK、Session 或其他教會設定。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}
