/*
檔案整體說明

目的與定位：保護 P3-08 Webhook 回覆、入站 letter、scope 語系與例外交接契約。

主要功能與流程：使用 detached JSON、固定 Core catalog、窄 CRM 替身及 dispatcher
替身驗證 reply token 不跨事件、QueryUserId 只使用資源 key、letter 以事件 ID 原子
Upsert 並填入 directioncode=false，以及 Reply sender 的真實 Exception 原樣離開 queue
handler。所有測試都是離線合成結果，不連 LINE、D365、IIS 或 SQL。

使用邊界與維護：每個案例獨占 Entity、HTTP handler、scope 與 exception；測試替身
不保存 token、request、Session 或跨案例 cache，Dispose 由案例的 using／Host fixture
完成。決定性斷言直接檢查穩定資料庫 ID、欄位型別、資源鍵與原始例外身分，不把離線
結果宣稱為實機驗收。
*/
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>以最小替身驗證 P3-08 的回覆、letter 與例外邊界。</summary>
public sealed class WebhookP308Tests
{
    private static readonly Guid OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChurchId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string UserId = "U00000000000000000000000000000000";

    /// <summary>ReplyToken 只從本事件 detached 保存，格式錯誤不能進入 queue。</summary>
    [Fact]
    public void Envelope_CopiesBoundedReplyToken_AndRejectsInvalidValue()
    {
        using var document = JsonDocument.Parse(EventJson("evt-reply", "ID", "reply-token"));
        var envelope = new WebhookEnvelope("123", document.RootElement.GetProperty("events")[0]);

        Assert.Equal("reply-token", envelope.ReplyToken);
        Assert.Null(typeof(WebhookEnvelope).GetProperty(nameof(WebhookEnvelope.ReplyToken))!.SetMethod);

        using var invalid = JsonDocument.Parse(EventJson("evt-invalid", "ID", new string('x', 513)));
        Assert.Throws<ArgumentException>(() =>
            new WebhookEnvelope("123", invalid.RootElement.GetProperty("events")[0]));
    }

    /// <summary>QueryUserId 的 LINE ID 只能作 catalog 的 lineId 具名參數，不能成為文案。</summary>
    [Fact]
    public void QueryUserId_ReturnsCatalogKeyAndDetachedArgument()
    {
        var result = new WebhookTextHandler(new FixedClock()).Handle(
            new EmptyCrm(),
            new WebhookActor(OrganizationId, "123", ChurchId, ContactId, UserId, "Asia/Taipei"),
            "ID",
            CancellationToken.None);

        Assert.Equal(WebhookBusinessStatus.ReplyPending, result.Status);
        Assert.Equal("Webhook.Reply.QueryUserId.V0", result.ReplyKey);
        var argument = Assert.Single(result.ReplyArguments);
        Assert.Equal("lineId", argument.Name);
        Assert.Equal(UserId, argument.Value);
    }

    /// <summary>letter 使用相同事件 ID 重送同一正式主鍵，方向與 actor scope 固定為入站。</summary>
    [Fact]
    public async Task LetterRecorder_UpsertsStableInboundCandidate()
    {
        var crm = new LetterCrm();
        var scope = Scope(crm);
        var recorder = new WebhookLetterRecorder(new GoodShepherdTextCatalog(new EmptyOverrides()));

        var first = await recorder.RecordAsync(scope, "evt-letter", "text", "第一則內容", CancellationToken.None);
        var second = await recorder.RecordAsync(scope, "evt-letter", "text", "第一則內容", CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(2, crm.Upserts.Count);
        var letter = crm.Upserts[0];
        Assert.Equal("letter", letter.LogicalName);
        Assert.Equal(first, letter.Id);
        Assert.False(letter.GetAttributeValue<bool>("directioncode"));
        Assert.Equal(1, letter.GetAttributeValue<int>("new_count"));
        Assert.Equal(100000000, letter.GetAttributeValue<OptionSetValue>("new_message_category")!.Value);
        Assert.Equal(ContactId, letter.GetAttributeValue<EntityReference>("regardingobjectid")!.Id);
        var party = Assert.Single(letter.GetAttributeValue<EntityCollection>("from").Entities);
        Assert.Equal(ContactId, party.GetAttributeValue<EntityReference>("partyid")!.Id);
    }

    /// <summary>Reply sender 的故障不被 dispatcher 吞掉，queue 可取得原始例外身份。</summary>
    [Fact]
    public async Task Dispatcher_ReplyFailure_PreservesOriginalException()
    {
        var expected = new InvalidOperationException("synthetic reply transport failure");
        var scope = Scope(new EmptyCrm());
        var dispatcher = new WebhookDispatcher(
            new FixedScopeResolver(scope),
            new WebhookTextHandler(new FixedClock()),
            new WebhookHttpAdapter(new HttpClient(new NeverHandler())),
            new WebhookAudioHandler(new WebhookHttpAdapter(new HttpClient(new NeverHandler()))),
            NullLogger<WebhookDispatcher>.Instance,
            new ThrowingReplySender(expected),
            new EmptyLetterRecorder());

        using var document = JsonDocument.Parse(EventJson("evt-dispatch", "ID", "reply-token"));
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.HandleAsync(
                new WebhookEnvelope("123", document.RootElement.GetProperty("events")[0]),
                CancellationToken.None).AsTask());

        Assert.Same(expected, actual);
    }

    /// <summary>建立含正式 Contact 的單事件 scope；文字文化是伺服器解析後的純值。</summary>
    private static ResolvedWebhookScope Scope(IOrganizationService service) =>
        new(service, OrganizationId, ChurchId, "123", UserId, "Asia/Taipei", "token-A",
            new Entity("contact", ContactId)
            {
                ["new_lineid"] = UserId,
                ["parentcustomerid"] = new EntityReference("account", ChurchId)
            },
            organizationName: "GoodShepherd",
            churchName: "測試教會",
            memberCulture: "zh-TW",
            formatCulture: "zh-TW");

    /// <summary>建立最小 message JSON；測試只使用合成 event id、文字與 token。</summary>
    private static string EventJson(string eventId, string text, string? replyToken) =>
        $$"""
        {
          "events": [
            {
              "type": "message",
              "source": { "type": "user", "userId": "{{UserId}}" },
              "webhookEventId": "{{eventId}}",
              "timestamp": 1735689600000,
              "deliveryContext": { "isRedelivery": false },
              "replyToken": "{{replyToken}}",
              "message": { "id": "10000000000001", "type": "text", "text": "{{text}}" }
            }
          ]
        }
        """;

    /// <summary>固定時間替身不讀主機 local 時區，也不擁有背景資源。</summary>
    private sealed class FixedClock : IChurchClock
    {
        /// <summary>固定 UTC 時間。</summary>
        public DateTimeOffset UtcNow => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        /// <summary>測試不使用轉換。</summary>
        public DateTime ToChurchTime(DateTime utcNow, string timeZone) => utcNow;
        /// <summary>固定教會日期。</summary>
        public DateTime TodayInChurch(string timeZone) => new(2026, 1, 1);
        /// <summary>測試不使用 local 轉 UTC。</summary>
        public DateTime ToUtc(LocalDateTimeOfChurch local) => throw new InvalidOperationException();
    }

    /// <summary>不執行 CRM I/O 的文字測試替身；QueryUserId 在 CRM 讀取前即完成。</summary>
    private class EmptyCrm : IOrganizationService
    {
        /// <summary>拒絕非預期讀取。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new InvalidOperationException();
        /// <summary>拒絕非預期查詢。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new InvalidOperationException();
        /// <summary>拒絕非預期命令。</summary>
        public virtual OrganizationResponse Execute(OrganizationRequest request) => throw new InvalidOperationException();
        /// <summary>拒絕直接建立。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException();
        /// <summary>拒絕直接更新。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException();
        /// <summary>拒絕刪除。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException();
        /// <summary>拒絕關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException();
        /// <summary>拒絕解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException();
    }

    /// <summary>只接收 letter Upsert 的 CRM 替身，保存本案例 candidate 供決定性斷言。</summary>
    private sealed class LetterCrm : EmptyCrm
    {
        internal List<Entity> Upserts { get; } = [];

        /// <summary>回傳與 candidate 相同的正式 ID，模擬原子 Upsert。</summary>
        public override OrganizationResponse Execute(OrganizationRequest request)
        {
            var upsert = Assert.IsType<UpsertRequest>(request);
            Upserts.Add(upsert.Target);
            var response = new UpsertResponse();
            response.Results["Target"] = new EntityReference(upsert.Target.LogicalName, upsert.Target.Id);
            return response;
        }
    }

    /// <summary>每次回覆都拋出同一個原例外的替身。</summary>
    private sealed class ThrowingReplySender(Exception error) : IWebhookReplySender
    {
        /// <summary>不吞例外或建立背景工作。</summary>
        public ValueTask SendAsync(ResolvedWebhookScope scope, string? replyToken, WebhookBusinessResult result, CancellationToken cancellationToken) =>
            ValueTask.FromException(error);
    }

    /// <summary>letter 交接替身，不連 CRM。</summary>
    private sealed class EmptyLetterRecorder : IWebhookLetterRecorder
    {
        /// <summary>返回固定空 ID；本案例只驗證 Reply 例外先離開。</summary>
        public ValueTask<Guid> RecordAsync(ResolvedWebhookScope scope, string webhookEventId, string messageKind, string description, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Guid.Empty);
    }

    /// <summary>直接回傳同一 scope 的 resolver，沒有跨事件狀態。</summary>
    private sealed class FixedScopeResolver(ResolvedWebhookScope scope) : IWebhookScopeResolver
    {
        /// <summary>拒絕不一致的 channel 或 source。</summary>
        public ResolvedWebhookScope Resolve(string channelId, string sourceId, CancellationToken cancellationToken)
        {
            Assert.Equal("123", channelId);
            Assert.Equal(UserId, sourceId);
            cancellationToken.ThrowIfCancellationRequested();
            return scope;
        }
    }

    /// <summary>不應被使用的 HTTP handler；測試只覆蓋文字 reply 例外。</summary>
    private sealed class NeverHandler : HttpMessageHandler
    {
        /// <summary>任何網路嘗試都立即失敗。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new InvalidOperationException("Unexpected HTTP call."));
    }

    /// <summary>回傳本次 scope 的空覆寫快照，不保存跨案例資料。</summary>
    private sealed class EmptyOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>快照 scope 與呼叫 context 完全一致。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}
