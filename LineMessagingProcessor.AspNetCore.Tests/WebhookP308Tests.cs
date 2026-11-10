/*
檔案整體說明

目的與定位：保護 P3-08 Webhook 回覆、P3-09 回覆 API 結構化紀錄及遮罩、入站 letter、scope 語系與例外交接契約。

主要功能與流程：使用 detached JSON、固定 Core catalog、固定 UTC 時鐘、窄 CRM 替身及 dispatcher
替身驗證 reply token 不跨事件、兩個教會 scope 的 SDK Authorization 不串流、QueryUserId 只使用
resource key、回覆紀錄預設關閉且啟用時包含 event ID／訊息型別／遮罩文字／語系／真實 HTTP 狀態，
包含非 2xx 回應仍記錄狀態並保留失敗、機密與完整 userId 不進 log、letter 以事件 ID 原子 Upsert 並填入
directioncode=false 與固定時鐘 scheduledend，以及 Reply 傳輸真實 Exception 原樣離開 queue handler。
所有測試都是離線合成結果，不連 LINE、D365、IIS 或 SQL。

使用邊界與維護：每個案例獨占 Entity、HTTP handler、scope 與 exception；測試替身
不保存 token、request、Session 或跨案例 cache，Dispose 由案例的 using／Host fixture
完成。決定性斷言直接檢查穩定資料庫 ID、欄位型別、資源鍵與原始例外身分，不把離線
結果宣稱為實機驗收。
*/
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using Line.Messaging.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// 正式 Reply sender 經真 SDK 離線傳輸；以兩個 scope 與兩個 token 注入 A/B 隔離，
    /// 並斷言每筆 Reply 的 URI、Authorization、一次性 token、catalog 文案與 sender 欄位。
    /// Inject 捕獲合成 handler 與追蹤 client 的 IHttpClientFactory；sender 必須對每筆請求使用
    /// 當筆 scope credential，完成或失敗後釋放 client。測試不連 LINE、D365、IIS 或 SQL。
    /// </summary>
    [Fact]
    public async Task ReplySender_UsesCatalogTextTokenAndScopeSender()
    {
        using var handler = new CapturingReplyHandler();
        var httpClientFactory = new CapturingHttpClientFactory(handler);
        var catalog = new GoodShepherdTextCatalog(new EmptyOverrides());
        var sender = new LineWebhookReplySender(catalog, httpClientFactory);
        var firstScope = Scope(
            new EmptyCrm(),
            accessToken: "synthetic-token-A",
            userId: UserId,
            churchName: "甲教會",
            senderIconUrl: "https://example.invalid/a.png");
        var secondUserId = "U11111111111111111111111111111111";
        var secondScope = Scope(
            new EmptyCrm(),
            accessToken: "synthetic-token-B",
            userId: secondUserId,
            churchName: "乙教會",
            resolvedChurchId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            resolvedContactId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            senderIconUrl: "https://example.invalid/b.png");
        var firstResult = new WebhookBusinessResult(
            WebhookBusinessStatus.ReplyPending,
            "QueryUserIdReply",
            "Webhook.Reply.QueryUserId.V0",
            replyArguments: new[] { new WebhookReplyArgument("lineId", UserId) });
        var secondResult = new WebhookBusinessResult(
            WebhookBusinessStatus.ReplyPending,
            "QueryUserIdReply",
            "Webhook.Reply.QueryUserId.V0",
            replyArguments: new[] { new WebhookReplyArgument("lineId", secondUserId) });

        await sender.SendAsync("evt-reply-A", firstScope, "reply-token-A", firstResult, CancellationToken.None);
        await sender.SendAsync("evt-reply-B", secondScope, "reply-token-B", secondResult, CancellationToken.None);

        var firstRequest = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, firstRequest.Method);
        Assert.Equal("https://api.line.me/v2/bot/message/reply", firstRequest.Uri);
        Assert.Equal("Bearer synthetic-token-A", firstRequest.Authorization);
        AssertReplyBody(
            firstRequest.Body,
            "reply-token-A",
            "您的 LINE ID: " + UserId,
            "甲教會助手",
            "https://example.invalid/a.png");

        var secondRequest = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, secondRequest.Method);
        Assert.Equal("https://api.line.me/v2/bot/message/reply", secondRequest.Uri);
        Assert.Equal("Bearer synthetic-token-B", secondRequest.Authorization);
        AssertReplyBody(
            secondRequest.Body,
            "reply-token-B",
            "您的 LINE ID: " + secondUserId,
            "乙教會助手",
            "https://example.invalid/b.png");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, httpClientFactory.CreatedClients.Count);
        Assert.All(httpClientFactory.CreatedClients, client => Assert.True(client.IsDisposed));
    }

    /// <summary>回覆內容紀錄預設關閉時不產生事件，且傳輸 client 仍於單次操作後釋放。</summary>
    [Fact]
    public async Task ReplySender_LoggingDisabledByDefault_EmitsNothing()
    {
        using var handler = new CapturingReplyHandler();
        var factory = new CapturingHttpClientFactory(handler);
        var logger = new CapturingLogger<LineWebhookReplySender>();
        var sender = new LineWebhookReplySender(
            new GoodShepherdTextCatalog(new EmptyOverrides()),
            factory,
            logger);

        await sender.SendAsync(
            "evt-log-disabled",
            Scope(new EmptyCrm()),
            "reply-token-secret",
            QueryUserIdResult(),
            CancellationToken.None);

        Assert.Empty(logger.Entries);
        Assert.True(Assert.Single(factory.CreatedClients).IsDisposed);
    }

    /// <summary>
    /// 啟用測試紀錄時檢查固定事件與必要欄位；token、reply token、secret 及完整 LINE userId
    /// 作為哨兵不得出現在輸出中，並確認實際 HTTP status code 被記錄。
    /// </summary>
    [Fact]
    public async Task ReplySender_EnabledLogging_RecordsResponseAndMasksSensitiveValues()
    {
        const string accessToken = "channel-token-secret";
        const string replyToken = "reply-token-secret";
        var scope = Scope(new EmptyCrm(), accessToken: accessToken);
        using var handler = new CapturingReplyHandler();
        var factory = new CapturingHttpClientFactory(handler);
        var logger = new CapturingLogger<LineWebhookReplySender>();
        var sender = new LineWebhookReplySender(
            new GoodShepherdTextCatalog(new EmptyOverrides()),
            factory,
            logger,
            enableReplyContentLogging: true);

        await sender.SendAsync("evt-log-enabled", scope, replyToken, QueryUserIdResult(), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(30610, entry.EventId.Id);
        Assert.Equal("Webhook.ReplyResponse", entry.EventId.Name);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("evt-log-enabled", entry.Fields["WebhookEventId"]);
        Assert.Equal("text", entry.Fields["MessageType"]);
        Assert.Equal("您的 LINE ID: U***0000", entry.Fields["Text"]);
        Assert.Equal("zh-TW", entry.Fields["Culture"]);
        Assert.Equal((int)HttpStatusCode.OK, entry.Fields["StatusCode"]);
        var loggedValues = string.Join("|", entry.Fields.Values);
        Assert.DoesNotContain(UserId, loggedValues, StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, loggedValues, StringComparison.Ordinal);
        Assert.DoesNotContain(replyToken, loggedValues, StringComparison.Ordinal);
        Assert.DoesNotContain("channel-secret", loggedValues, StringComparison.Ordinal);
        Assert.True(Assert.Single(factory.CreatedClients).IsDisposed);
    }

    /// <summary>LINE 非成功狀態仍記錄真實狀態碼後以安全 SDK 例外向上傳遞。</summary>
    [Fact]
    public async Task ReplySender_NonSuccessResponse_LogsActualStatusAndPreservesFailure()
    {
        using var handler = new CapturingReplyHandler(HttpStatusCode.ServiceUnavailable);
        var factory = new CapturingHttpClientFactory(handler);
        var logger = new CapturingLogger<LineWebhookReplySender>();
        var sender = new LineWebhookReplySender(
            new GoodShepherdTextCatalog(new EmptyOverrides()),
            factory,
            logger,
            enableReplyContentLogging: true);

        var error = await Assert.ThrowsAsync<LineResponseException>(() =>
            sender.SendAsync("evt-http-failure", Scope(new EmptyCrm()), "synthetic-reply-token",
                QueryUserIdResult(), CancellationToken.None).AsTask());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(503, entry.Fields["StatusCode"]);
        Assert.Equal("evt-http-failure", entry.Fields["WebhookEventId"]);
        Assert.True(Assert.Single(factory.CreatedClients).IsDisposed);
    }

    /// <summary>
    /// 注入真 SDK 下層 HttpMessageHandler 傳輸故障，斷言同一個 Exception 由 sender 原樣拋出，
    /// 且唯一的 factory HttpClient 即使失敗也已 Dispose。
    /// </summary>
    [Fact]
    public async Task ReplySender_TransportFailure_DisposesClientAndPreservesException()
    {
        var expected = new HttpRequestException("synthetic reply transport failure");
        using var handler = new FailingReplyHandler(expected);
        var httpClientFactory = new CapturingHttpClientFactory(handler);
        var sender = new LineWebhookReplySender(
            new GoodShepherdTextCatalog(new EmptyOverrides()),
            httpClientFactory);
        var result = new WebhookBusinessResult(
            WebhookBusinessStatus.ReplyPending,
            "QueryUserIdReply",
            "Webhook.Reply.QueryUserId.V0",
            replyArguments: new[] { new WebhookReplyArgument("lineId", UserId) });

        var actual = await Assert.ThrowsAsync<HttpRequestException>(() =>
            sender.SendAsync("evt-transport-failure", Scope(new EmptyCrm()), "reply-token", result, CancellationToken.None).AsTask());

        Assert.Same(expected, actual);
        var client = Assert.Single(httpClientFactory.CreatedClients);
        Assert.True(client.IsDisposed);
    }

    /// <summary>逐筆斷言真 SDK 序列化出的 Reply body；只處理測試合成 token 與使用者 ID。</summary>
    private static void AssertReplyBody(
        string body,
        string expectedReplyToken,
        string expectedText,
        string expectedSenderName,
        string expectedSenderIcon)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(expectedReplyToken, root.GetProperty("replyToken").GetString());
        var message = Assert.Single(root.GetProperty("messages").EnumerateArray().ToArray());
        Assert.Equal("text", message.GetProperty("type").GetString());
        Assert.Equal(expectedText, message.GetProperty("text").GetString());
        Assert.Equal(expectedSenderName, message.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal(expectedSenderIcon, message.GetProperty("sender").GetProperty("iconUrl").GetString());
    }

    /// <summary>letter 使用相同事件 ID 重送同一正式主鍵，並驗證入站方向、actor scope 與固定 UTC 時間。</summary>
    [Fact]
    public async Task LetterRecorder_UpsertsStableInboundCandidate()
    {
        var crm = new LetterCrm();
        var scope = Scope(crm);
        var clock = new FixedClock();
        var recorder = new WebhookLetterRecorder(new GoodShepherdTextCatalog(new EmptyOverrides()), clock);
        var expectedScheduledEnd = clock.UtcNow.UtcDateTime;

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
        var scheduledEnd = letter.GetAttributeValue<DateTime>("scheduledend");
        Assert.Equal(expectedScheduledEnd, scheduledEnd);
        Assert.Equal(DateTimeKind.Utc, scheduledEnd.Kind);
        var party = Assert.Single(letter.GetAttributeValue<EntityCollection>("from").Entities);
        Assert.Equal(ContactId, party.GetAttributeValue<EntityReference>("partyid")!.Id);
    }

    /// <summary>Reply sender 的故障不被 dispatcher 吞掉，queue 可取得原始例外身份。</summary>
    [Fact]
    public async Task Dispatcher_ReplyFailure_PreservesOriginalException()
    {
        var expected = new InvalidOperationException("synthetic reply transport failure");
        var scope = Scope(new EmptyCrm());
        var replySender = new ThrowingReplySender(expected);
        var dispatcher = new WebhookDispatcher(
            new FixedScopeResolver(scope),
            new WebhookTextHandler(new FixedClock()),
            new WebhookHttpAdapter(new HttpClient(new NeverHandler())),
            new WebhookAudioHandler(new WebhookHttpAdapter(new HttpClient(new NeverHandler()))),
            NullLogger<WebhookDispatcher>.Instance,
            replySender,
            new EmptyLetterRecorder());

        using var document = JsonDocument.Parse(EventJson("evt-dispatch", "ID", "reply-token"));
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.HandleAsync(
                new WebhookEnvelope("123", document.RootElement.GetProperty("events")[0]),
                CancellationToken.None).AsTask());

        Assert.Same(expected, actual);
        Assert.Equal("evt-dispatch", replySender.EventId);
    }

    /// <summary>建立含正式 Contact 的單事件 scope；文字文化是伺服器解析後的純值。</summary>
    private static ResolvedWebhookScope Scope(
        IOrganizationService service,
        string? senderIconUrl = null,
        string accessToken = "token-A",
        string userId = UserId,
        string churchName = "測試教會",
        Guid? resolvedChurchId = null,
        Guid? resolvedContactId = null)
    {
        var actualChurchId = resolvedChurchId ?? ChurchId;
        var actualContactId = resolvedContactId ?? ContactId;
        return new ResolvedWebhookScope(
            service,
            OrganizationId,
            actualChurchId,
            "123",
            userId,
            "Asia/Taipei",
            accessToken,
            new Entity("contact", actualContactId)
            {
                ["new_lineid"] = userId,
                ["parentcustomerid"] = new EntityReference("account", actualChurchId)
            },
            organizationName: "GoodShepherd",
            churchName: churchName,
            memberCulture: "zh-TW",
            formatCulture: "zh-TW",
            senderIconUrl: senderIconUrl);
    }

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

    /// <summary>建立使用 LINE ID 查詢資源鍵的固定結果；其輸出本文供遮罩契約斷言。</summary>
    private static WebhookBusinessResult QueryUserIdResult() =>
        new(
            WebhookBusinessStatus.ReplyPending,
            "QueryUserIdReply",
            "Webhook.Reply.QueryUserId.V0",
            replyArguments: new[] { new WebhookReplyArgument("lineId", UserId) });

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
        /// <summary>dispatcher 傳入的當筆 envelope event ID；只由單一測試呼叫設定。</summary>
        internal string? EventId { get; private set; }

        /// <summary>不吞例外或建立背景工作。</summary>
        public ValueTask SendAsync(string webhookEventId, ResolvedWebhookScope scope, string? replyToken, WebhookBusinessResult result, CancellationToken cancellationToken)
        {
            EventId = webhookEventId;
            return ValueTask.FromException(error);
        }
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

    /// <summary>只捕獲本案例兩筆合成 Reply 請求並回傳成功；不建立外部連線或背景資源。</summary>
    private sealed class CapturingReplyHandler(HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        /// <summary>只保留已 detached 的短請求欄位，供兩個 scope 的隔離斷言。</summary>
        internal List<CapturedReplyRequest> Requests { get; } = [];

        /// <summary>複製本次必要欄位後立即回傳合成成功，讓 SDK 負責 request／response 清理。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedReplyRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.ToString() ?? string.Empty,
                body));
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{}")
            };
        }
    }

    /// <summary>本案例專用 logger，只保留格式化後的事件，不跨呼叫保存原始 state 或 exception。</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        /// <summary>測試案例同步產生的格式化 log 結果。</summary>
        internal List<CapturedLogEntry> Entries { get; } = [];

        /// <summary>回傳無資源 scope；測試不建立 request 或背景生命週期。</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => EmptyScope.Instance;

        /// <summary>複製格式化文字與固定事件欄位，避免保留 logger 原始狀態物件。</summary>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new CapturedLogEntry(logLevel, eventId, formatter(state, exception), fields));
        }

        /// <summary>只用於測試；此 logger 不過濾資訊層級。</summary>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <summary>固定空 scope，不持有可變或可處置資源。</summary>
        private sealed class EmptyScope : IDisposable
        {
            /// <summary>全案例共用的不可變空 scope。</summary>
            internal static EmptyScope Instance { get; } = new();

            /// <summary>沒有需清理的資源。</summary>
            public void Dispose() { }
        }
    }

    /// <summary>建立每次由 sender 擁有的短命 HttpClient，並觀察請求完成後的 Dispose。</summary>
    private sealed class CapturingHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        /// <summary>本案例 factory 建立、sender 擁有並應已釋放的 client。</summary>
        internal List<TrackingHttpClient> CreatedClients { get; } = [];

        /// <summary>只接受正式 Reply sender 的具名 client，handler 生命週期由外層測試控制。</summary>
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("LineWebhookReplySender", name);
            var client = new TrackingHttpClient(handler);
            CreatedClients.Add(client);
            return client;
        }
    }

    /// <summary>只注入預先建立的合成傳輸例外，不保存 request、token 或額外背景資源。</summary>
    private sealed class FailingReplyHandler(Exception error) : HttpMessageHandler
    {
        /// <summary>在 SDK HTTP 邊界拋出呼叫端指定的同一例外。</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(error);
    }

    /// <summary>只追蹤 HttpClient owner 是否確定釋放，不保留請求、憑證或額外資源。</summary>
    private sealed class TrackingHttpClient(HttpMessageHandler handler) : HttpClient(handler, disposeHandler: false)
    {
        /// <summary>由 sender 完成本次 Reply 後設定；測試確認每次建立的 client 均已處置。</summary>
        internal bool IsDisposed { get; private set; }

        /// <summary>記錄唯一處置，再由基底清除短期傳輸狀態。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>已 detached 的合成 Reply 欄位，只在目前測試案例保留。</summary>
    private sealed record CapturedReplyRequest(HttpMethod Method, string Uri, string Authorization, string Body);

    /// <summary>離線 logger 的純值結果，不保存 token、request 或原始 log state。</summary>
    private sealed record CapturedLogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyDictionary<string, object?> Fields);

    /// <summary>回傳本次 scope 的空覆寫快照，不保存跨案例資料。</summary>
    private sealed class EmptyOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>快照 scope 與呼叫 context 完全一致。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}
