/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-01 NotifyComposedAsync 的雙模式通知契約，特別保護 Shadow
 * 不解密、不呼叫推播，以及 Live 只推播一次；兩模式必須共用正式會友與教會核對。
 *
 * 主要功能與流程：測試使用本檔擁有的不可變來源、合成 token、推播替身與記錄器，
 * 依序驗證訊息委派、Shadow 摘要與 Live 呼叫次數。故障情境以不同教會純值注入，
 * 決定性斷言確認沒有讀頻道或推播；所有替身都是測試案例的局部 owner，不共享
 * static、Session、token 或背景工作，測試結束即清除金鑰陣列。
 *
 * 使用邊界與維護：測試只證明離線 Core 行為，不證明 LINE 實機送達或 Dataverse
 * 授權。若修改服務簽章或訊息投影，必須仍保留兩模式相同前置檢查與資源排空契約。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using ToolUtilityNameSpace.LineRecipients;
using Newtonsoft.Json;
using LineMessagingProcessor.Core.Templates;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Notifications;

/// <summary>以合成依賴驗證 Shadow／Live 通知隔離與單次推播。</summary>
[Collection("NotifyComposed global JSON defaults")]
public sealed class NotifyComposedTests
{
    private static readonly Guid ContactId = new("11111111-2222-3333-4444-555555555555");
    private static readonly string[] Cultures = { "zh-TW" };

    /// <summary>Shadow 使用壞密文且無金鑰，仍成功記錄摘要，證明沒有解密或推播。</summary>
    [Fact]
    public async Task NotifyComposed_Shadow_RecordsWithoutDecrypt()
    {
        using var fixture = new Fixture();
        var sink = new Sink();
        var source = fixture.Source(envelope: "invalid-encrypted-envelope");
        var task = fixture.Service.NotifyComposedAsync(source, fixture.Context, ContactId, Guid.NewGuid(), "member.changed",
            _ => new ISendMessage[] { new TextMessage(string.Empty) { Text = "shadow" } }, sink, default);
        var result = await Assert.IsAssignableFrom<Task<LinePushResult>>(task);
        Assert.False(result.Success);
        Assert.Equal(LinePushOutcome.Rejected, result.Outcome);
        Assert.Equal("ShadowRecorded", result.Reason);
        Assert.Null(result.StatusCode);
        Assert.Null(result.TransportException);

        Assert.Equal(0, fixture.Push.Calls);
        var record = Assert.Single(sink.Records);
        Assert.Equal(ContactId, record.ContactId);
        Assert.Equal(1, record.MessageCount);
        Assert.Equal(64, record.ContentSha256.Length);
    }

    /// <summary>Live 使用合成有效 token 並只交給推播替身一次，金鑰不跨案例保存。</summary>
    [Fact]
    public async Task NotifyComposed_Live_PushesOnce()
    {
        using var fixture = new Fixture();
        var source = fixture.Source();
        var task = fixture.Service.NotifyComposedAsync(source, fixture.Context, ContactId, Guid.NewGuid(), "member.changed",
            _ => new ISendMessage[] { new TextMessage(string.Empty) { Text = "live" } }, fixture.Key, default);
        var result = await Assert.IsAssignableFrom<Task<LinePushResult>>(task);
        Assert.True(result.Success);
        Assert.Same(fixture.Push.Result, result);

        Assert.Equal(1, fixture.Push.Calls);
    }

    /// <summary>注入會友或頻道錯配及壞密文；兩模式都回 ChurchMismatch，不能組合、解密或推播。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotifyComposed_ChurchMismatch_SameInBothModes(bool channelMismatch)
    {
        using var fixture = new Fixture();
        var source = fixture.Source(envelope: "invalid-encrypted-envelope", channelMismatch: channelMismatch);
        var context = channelMismatch ? fixture.Context :
            new TextContext(Guid.NewGuid(), "zh-TW", "zh-TW", "AlphaOrg", "GoodShepherd");
        await AssertRejectedBothModes(fixture, source, context, "ChurchMismatch");
        Assert.Equal(channelMismatch ? 2 : 0, source.ChannelReads);
    }

    /// <summary>注入未綁定及壞密文，兩模式在頻道查詢、委派與解密前回 NotBound。</summary>
    [Fact]
    public async Task NotifyComposed_NotBound_SameInBothModes()
    {
        using var fixture = new Fixture();
        var source = fixture.Source(envelope: "invalid-encrypted-envelope", line: null);
        await AssertRejectedBothModes(fixture, source, fixture.Context, "NotBound");
        Assert.Equal(0, source.ChannelReads);
    }

    /// <summary>格式錯誤的非空 LINE ID 在兩模式都先拒絕，不得讓 Shadow 發布 Live 不接受的意圖。</summary>
    [Fact]
    public async Task NotifyComposed_InvalidRecipient_SameInBothModes()
    {
        using var fixture = new Fixture();
        var source = fixture.Source(envelope: "invalid-encrypted-envelope", line: "invalid-recipient");
        var sink = new Sink();
        var composed = 0;
        IReadOnlyList<ISendMessage> Compose(TextContext _)
        {
            composed++;
            return new ISendMessage[] { new TextMessage("synthetic") };
        }

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.NotifyComposedAsync(source, fixture.Context,
            ContactId, Guid.NewGuid(), "member.changed", Compose, sink, default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.NotifyComposedAsync(source, fixture.Context,
            ContactId, Guid.NewGuid(), "member.changed", Compose, fixture.Key, default));

        Assert.Equal(0, composed);
        Assert.Equal(0, source.ChannelReads);
        Assert.Empty(sink.Records);
        Assert.Equal(0, fixture.Push.Calls);
    }

    /// <summary>注入會拋錯的全域預設 callback，Shadow 仍可摘要真 Flex；finally 恢復程序設定。</summary>
    [Fact]
    public async Task NotifyComposed_Shadow_ExplicitSerializerIgnoresGlobalDefaults()
    {
        using var fixture = new Fixture();
        var original = JsonConvert.DefaultSettings;
        try
        {
            JsonConvert.DefaultSettings = () => throw new InvalidOperationException("Global defaults must not be read.");
            var source = fixture.Source(envelope: "invalid-encrypted-envelope");
            var sink = new Sink();
            var result = await Assert.IsAssignableFrom<Task<LinePushResult>>(fixture.Service.NotifyComposedAsync(
                source, fixture.Context, ContactId, Guid.NewGuid(), "member.flex", context =>
                    new ISendMessage[] { new FlexTemplateEngine(new Catalog()).Fill(
                        "{\"type\":\"flex\",\"altText\":\"{{t:Common.Diagnostics.Ping}}\",\"contents\":{\"type\":\"bubble\",\"body\":{\"type\":\"box\",\"layout\":\"vertical\",\"contents\":[]}}}",
                        context, new Dictionary<string, string>(StringComparer.Ordinal)) }, sink, default));
            Assert.False(result.Success);
            Assert.Equal("ShadowRecorded", result.Reason);
            Assert.Equal(64, Assert.Single(sink.Records).ContentSha256.Length);
            Assert.Equal(0, fixture.Push.Calls);
        }
        finally { JsonConvert.DefaultSettings = original; }
    }

    /// <summary>超過 Live 同款 256 Ki 字元序列化預算必須在記錄前失敗，不先配置無界 JSON。</summary>
    [Fact]
    public async Task NotifyComposed_Shadow_OverBudget_NoPublication()
    {
        using var fixture = new Fixture();
        var sink = new Sink();
        var source = fixture.Source(envelope: "invalid-encrypted-envelope");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.NotifyComposedAsync(source, fixture.Context,
            ContactId, Guid.NewGuid(), "member.large", _ =>
                new ISendMessage[] { new TextMessage(string.Empty) { Text = new string('x', 262145) } }, sink, default));
        Assert.Empty(sink.Records);
        Assert.Equal(0, fixture.Push.Calls);
    }

    /// <summary>Shadow 須與 Live 同樣拒絕超限文字與非 SDK 模型，不可發布只在影子有效的摘要。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public async Task NotifyComposed_Shadow_InvalidText_NoPublication(int length)
    {
        using var fixture = new Fixture();
        var sink = new Sink();
        var source = fixture.Source(envelope: "invalid-encrypted-envelope");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.NotifyComposedAsync(source, fixture.Context,
            ContactId, Guid.NewGuid(), "member.invalid", _ =>
                new ISendMessage[] { new TextMessage(string.Empty) { Text = new string('x', length) } }, sink, default));
        Assert.Empty(sink.Records);
        Assert.Equal(0, fixture.Push.Calls);
    }

    /// <summary>同內容摘要忽略 caller sender、保留模型原值，並以正式 sender 區分不同教會投影。</summary>
    [Fact]
    public void NotifyComposed_Shadow_DigestUsesServerSenderWithoutMutation()
    {
        var message = new TextMessage("content") { Sender = new Sender("caller", "https://example.invalid/icon.png") };
        var messages = new ISendMessage[] { message };
        var first = LinePushClient.ComputeMemberContentSha256(messages, "Alpha助手");
        var second = LinePushClient.ComputeMemberContentSha256(messages, "Alpha助手");
        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.Equal("caller", message.Sender.Name);
        message.Sender = new Sender("changed", "https://example.invalid/changed.png");
        Assert.Equal(first, LinePushClient.ComputeMemberContentSha256(messages, "Alpha助手"));
        Assert.NotEqual(first, LinePushClient.ComputeMemberContentSha256(messages, "Beta助手"));
        message.Text = "different";
        Assert.NotEqual(first, LinePushClient.ComputeMemberContentSha256(messages, "Alpha助手"));
        Assert.Throws<ArgumentException>(() => LinePushClient.ComputeMemberContentSha256(messages, ""));
        Assert.Throws<ArgumentException>(() => LinePushClient.ComputeMemberContentSha256(messages, new string('x', 21)));
    }

    /// <summary>以會拋錯的組合委派與壞密文保護拒絕短路；所有 Task 都在斷言前排空。</summary>
    private static async Task AssertRejectedBothModes(Fixture fixture, Source source, TextContext context, string reason)
    {
        var sink = new Sink();
        var composed = 0;
        IReadOnlyList<ISendMessage> Compose(TextContext _)
        {
            composed++;
            throw new InvalidOperationException("Rejected notifications must not compose.");
        }
        var shadowTask = fixture.Service.NotifyComposedAsync(source, context, ContactId, Guid.NewGuid(), "member.changed", Compose, sink, default);
        var shadowResult = await Assert.IsAssignableFrom<Task<LinePushResult>>(shadowTask);
        var liveTask = fixture.Service.NotifyComposedAsync(source, context, ContactId, Guid.NewGuid(), "member.changed", Compose, fixture.Key, default);
        var liveResult = await Assert.IsAssignableFrom<Task<LinePushResult>>(liveTask);
        Assert.Equal(LinePushOutcome.Rejected, shadowResult.Outcome);
        Assert.Equal(LinePushOutcome.Rejected, liveResult.Outcome);
        Assert.Equal(reason, shadowResult.Reason);
        Assert.Equal(reason, liveResult.Reason);
        Assert.False(shadowResult.Success);
        Assert.False(liveResult.Success);
        Assert.Equal(0, composed);
        Assert.Empty(sink.Records);
        Assert.Equal(0, fixture.Push.Calls);
    }

    /// <summary>每個 fixture 擁有自己的 key、來源與推播替身，Dispose 時確定清理秘密陣列。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly byte[] Key = new byte[64];
        internal readonly FakePush Push = new();
        internal readonly TextContext Context;
        internal readonly MemberNotificationService Service;
        internal readonly Guid ChurchId = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        /// <summary>建立單案例純值策略與依賴，不讀主機秘密或全域字典狀態。</summary>
        internal Fixture()
        {
            for (var i = 0; i < Key.Length; i++) Key[i] = (byte)i;
            Context = new TextContext(ChurchId, "zh-TW", "zh-TW", "AlphaOrg", "GoodShepherd");
            Service = new MemberNotificationService(Push,
                new MemberCultureResolver(new LocalizationOptions(Cultures, false)), new Catalog());
        }

        /// <summary>每次建立合成純值來源；可注入壞封包與錯誤 lookup 以保護先驗拒絕。</summary>
        internal Source Source(string? envelope = null, string? line = "U00000000000000000000000000000000", bool channelMismatch = false) =>
            new(ChurchId, envelope ?? ChannelTokenProtector.Protect("token", Key), line, channelMismatch);
        /// <summary>所有通知已 await 後清空合成 key；替身不持有外部資源。</summary>
        public void Dispose() { Array.Clear(Key, 0, Key.Length); }
    }

    /// <summary>只回傳固定純值，不執行 SDK I/O；ChannelReads 用來判定 mismatch fail-closed。</summary>
    private sealed class Source(Guid churchId, string envelope, string? line, bool channelMismatch) : ILineRecipientSource
    {
        internal int ChannelReads;
        /// <summary>只回傳操作內的綁定與正式教會身分。</summary>
        public MemberRecipient ReadMember(Guid contactId, CancellationToken ct) => new(contactId, line, churchId, "zh-TW");
        /// <summary>返回同次來源純值，可刻意使正式頻道身分錯配以驗證拒絕。</summary>
        public ChurchChannelRecord ReadChannel(Guid sourceChurchId, CancellationToken ct)
        {
            Interlocked.Increment(ref ChannelReads);
            return new ChurchChannelRecord(churchId, churchId, channelMismatch ? Guid.Empty : churchId, "Alpha", envelope,
                new ChurchLocaleSettings("zh-TW", Cultures));
        }
    }

    /// <summary>記錄器只保留本案例必要摘要，不保存訊息本文或秘密。</summary>
    private sealed class Sink : IShadowPushSink
    {
        internal readonly List<ShadowRecord> Records = new();
        /// <summary>只保存摘要到案例擁有的清單，不捕捉訊息或金鑰。</summary>
        public void Record(Guid contactId, string templateKey, Guid retryKey, int messageCount, string contentSha256) =>
            Records.Add(new ShadowRecord(contactId, templateKey, retryKey, messageCount, contentSha256));
        /// <summary>單案擁有的不可變摘要，不持有訊息物件或秘密。</summary>
        internal sealed record ShadowRecord(Guid ContactId, string TemplateKey, Guid RetryKey, int MessageCount, string ContentSha256);
    }

    /// <summary>只計數 Live 入口，使用測試友元建立固定成功純值，不建立 HTTP 或背景工作。</summary>
    private sealed class FakePush : ILineMemberPushClient
    {
        internal int Calls;
        internal readonly LinePushResult Result = new(LinePushOutcome.Success, null, null, null);
        /// <summary>保留同一成功實例，以驗證服務原樣轉交傳輸結果。</summary>
        public Task<LinePushResult> PushToMemberAsync(MemberRecipient recipient, ChurchChannel channel, Guid sourceChurchId,
            IReadOnlyList<ISendMessage> messages, Guid retryKey, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Result);
        }
    }

    /// <summary>提供 sender 所需固定文字，避免測試依賴檔案、Session 或全域字典。</summary>
    private sealed class Catalog : ITextCatalog
    {
        /// <summary>以固定短文字供 sender 與 Flex altText 消費，不保存 context。</summary>
        public string GetText(TextContext context, TextKey key, params TextArg[] args) => "Alpha助手";
    }
}

/// <summary>暫時注入全域 JSON callback 的案例不可與其他 collection 同時執行，結束一定恢復原設定。</summary>
[CollectionDefinition("NotifyComposed global JSON defaults", DisableParallelization = true)]
public sealed class NotifyComposedJsonIsolation { }
