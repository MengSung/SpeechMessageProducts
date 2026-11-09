/*
檔案整體說明
目的與定位：保護 P3-06 真 dispatcher 與語音 adapter 的離線端到端契約；測試只使用本包已核定的 Golden 事件及合成 HTTP／CRM 依賴。
主要功能與流程：每份十二事件 fixture 先建成不可變 WebhookEnvelope，再交給正式 WebhookDispatcher；語音案例另驗固定官方 URL、逐 request Bearer、annotation 的正式 parent 與故障時零寫入。
使用邊界與維護：測試替身不連 LINE、D365 或設定，所有 Entity、HTTP response、stream 與 scope 僅由單一案例擁有並在 using／finally 釋放；斷言只證明離線 adapter 契約，不冒稱實機驗收。
*/
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Line.Messaging.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>驗證十二種 Golden 事件均經正式 dispatcher，而非只經 parser 或空 sink。</summary>
public sealed class WebhookDispatchEndToEndTests
{
    /// <summary>所有核定 fixture 逐筆交給正式分派器；未綁定的 join／leave 等事件仍須具名安全忽略且不拋例外。</summary>
    [Fact]
    public async Task TwelveGoldenEvents_ReachTheRealDispatcher()
    {
        var transport = new FakeHttpHandler();
        using var http = new HttpClient(transport);
        var crm = new DispatchCrm();
        var resolver = new FixedScopeResolver(crm, "123", "token-A");
        var logger = new RecordingLogger<WebhookDispatcher>();
        var dispatcher = new WebhookDispatcher(resolver, new SpeechMessageProducts.GoodShepherd.Core.Messaging.WebhookTextHandler(new ChurchClock()), new WebhookHttpAdapter(http), new WebhookAudioHandler(new WebhookHttpAdapter(http)), logger, new RecordingReplySender(), new RecordingLetterRecorder());

        var directory = Path.Combine(AppContext.BaseDirectory, "Golden", "Events");
        foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(static value => value, StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var item = Assert.Single(document.RootElement.GetProperty("events").EnumerateArray());
            var envelope = new WebhookEnvelope("123", item);
            await dispatcher.HandleAsync(envelope, CancellationToken.None);
        }

        Assert.Equal(9, resolver.Calls);
        Assert.Equal(3, transport.RequestCount);
        Assert.Single(crm.Reads);
        Assert.NotEmpty(crm.Updates);
        Assert.Collection(
            crm.Upserts.OrderBy(static entity => entity.LogicalName, StringComparer.Ordinal),
            annotation => Assert.Equal("annotation", annotation.LogicalName),
            contact => Assert.Equal("contact", contact.LogicalName));
        Assert.Contains(logger.EventIds, static id => id.Name == "Webhook.LegacyMediaNoOp");
        Assert.Contains(logger.EventIds, static id => id.Name == "Webhook.MemberJoinNoReplyContract");
        Assert.Contains(logger.EventIds, static id => id.Name == "Webhook.LeaveNoCrmMutation");
        Assert.Contains(logger.EventIds, static id => id.Name == "Webhook.AccountLinkNoReplyContract");
        Assert.Contains(logger.EventIds, static id => id.Name == "Webhook.Follow.Applied.FollowProfileUpdatedWelcomePendingP308");
        Assert.Contains(logger.EventIds, static id => id.Name == "Webhook.Join.Applied.GroupProfileUpdatedWelcomePendingP308");
        Assert.Contains(logger.EventIds, static id => id.Name == "Webhook.Text.Ignored.UnknownText");
        Assert.Contains(logger.EventIds, static id => id.Name == "Webhook.Postback.ReplyPending.UnknownPostbackEchoPendingP308");
    }

    /// <summary>語音成功必須帶逐 request Bearer、固定 message URL 並以正式 contact parent 寫入 annotation。</summary>
    [Fact]
    public async Task Audio_UsesBearerAndWritesAnnotationForAuthorizedContact()
    {
        var transport = new FakeHttpHandler();
        using var http = new HttpClient(transport);
        var crm = new DispatchCrm();
        var scope = new ResolvedWebhookScope(crm, crm.OrganizationId, crm.ChurchId, "123", crm.UserId, "Asia/Taipei", "token-A", crm.Contact);
        var handler = new WebhookAudioHandler(new WebhookHttpAdapter(http));

        var result = await handler.HandleAsync(scope, "01JGFJJZ000000000000000003", "10000000000002", CancellationToken.None);

        Assert.Equal(SpeechMessageProducts.GoodShepherd.Core.Messaging.WebhookBusinessStatus.Applied, result.Status);
        Assert.Equal("Bearer", transport.Authorization?.Scheme);
        Assert.Equal("token-A", transport.Authorization?.Parameter);
        Assert.Equal("https://api-data.line.me/v2/bot/message/10000000000002/content", transport.RequestUri?.ToString());
        var annotation = Assert.Single(crm.Upserts);
        Assert.Equal("annotation", annotation.LogicalName);
        Assert.Equal(crm.Contact.Id, annotation.GetAttributeValue<EntityReference>("objectid")?.Id);
        Assert.Equal(Convert.ToBase64String(FakeHttpHandler.AudioBytes), annotation.GetAttributeValue<string>("documentbody"));
    }

    /// <summary>HTTP 故障須保留故障且在 annotation 發布前停止，不可以空附件或假成功收尾。</summary>
    [Fact]
    public async Task AudioHttpFailure_DoesNotWriteAnnotation()
    {
        using var http = new HttpClient(new FakeHttpHandler(status: HttpStatusCode.BadGateway));
        var crm = new DispatchCrm();
        var scope = new ResolvedWebhookScope(crm, crm.OrganizationId, crm.ChurchId, "123", crm.UserId, "Asia/Taipei", "token-A", crm.Contact);
        var handler = new WebhookAudioHandler(new WebhookHttpAdapter(http));

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.HandleAsync(scope, "01JGFJJZ000000000000000003", "10000000000002", CancellationToken.None));

        Assert.Empty(crm.Upserts);
    }

    /// <summary>超過 5 MiB 的音訊必須在配置附件前拒絕，避免 CRM 出現空白或部分 annotation。</summary>
    [Fact]
    public async Task AudioContentTooLarge_DoesNotWriteAnnotation()
    {
        using var http = new HttpClient(new OversizedAudioHandler());
        var crm = new DispatchCrm();
        var scope = new ResolvedWebhookScope(crm, crm.OrganizationId, crm.ChurchId, "123", crm.UserId, "Asia/Taipei", "token-A", crm.Contact);
        var handler = new WebhookAudioHandler(new WebhookHttpAdapter(http));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(scope, "01JGFJJZ000000000000000003", "10000000000002", CancellationToken.None));

        Assert.Empty(crm.Upserts);
    }

    /// <summary>下載串流取消必須排空 HTTP 資源、保留取消分類，且在 CRM 寫入前沒有 annotation。</summary>
    [Fact]
    public async Task AudioCancellationDuringDownload_DoesNotWriteAnnotation()
    {
        var transport = new BlockingAudioHandler();
        using var http = new HttpClient(transport);
        using var cancellation = new CancellationTokenSource();
        var crm = new DispatchCrm();
        var scope = new ResolvedWebhookScope(crm, crm.OrganizationId, crm.ChurchId, "123", crm.UserId, "Asia/Taipei", "token-A", crm.Contact);
        var handler = new WebhookAudioHandler(new WebhookHttpAdapter(http));

        var operation = handler.HandleAsync(scope, "01JGFJJZ000000000000000003", "10000000000002", cancellation.Token);
        await transport.ReadStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Empty(crm.Upserts);
    }

    /// <summary>兩個 scope 同時下載時，各自的 Bearer 與 annotation parent 必須保持隔離。</summary>
    [Fact]
    public async Task AudioConcurrentScopes_KeepBearerAndAnnotationParentsIsolated()
    {
        var transport = new ConcurrentAudioHandler();
        using var http = new HttpClient(transport);
        var crmA = new DispatchCrm();
        var crmB = new DispatchCrm();
        var scopeA = new ResolvedWebhookScope(crmA, crmA.OrganizationId, crmA.ChurchId, "123", crmA.UserId, "Asia/Taipei", "token-A", crmA.Contact);
        var scopeB = new ResolvedWebhookScope(crmB, crmB.OrganizationId, crmB.ChurchId, "123", crmB.UserId, "Asia/Taipei", "token-B", crmB.Contact);
        var handler = new WebhookAudioHandler(new WebhookHttpAdapter(http));

        await Task.WhenAll(
            handler.HandleAsync(scopeA, "01JGFJJZ00000000000000000A", "10000000000001", CancellationToken.None),
            handler.HandleAsync(scopeB, "01JGFJJZ00000000000000000B", "10000000000002", CancellationToken.None));

        Assert.Equal(new[] { "token-A", "token-B" }, transport.Tokens.OrderBy(static token => token, StringComparer.Ordinal));
        Assert.Equal(crmA.Contact.Id, Assert.Single(crmA.Upserts).GetAttributeValue<EntityReference>("objectid")?.Id);
        Assert.Equal(crmB.Contact.Id, Assert.Single(crmB.Upserts).GetAttributeValue<EntityReference>("objectid")?.Id);
        Assert.NotEqual(Assert.Single(crmA.Upserts).Id, Assert.Single(crmB.Upserts).Id);
    }

    /// <summary>Join 必須讀取群組摘要並建立舊來源要求的 Contact 欄位，不能以空返回冒稱已移植。</summary>
    [Fact]
    public async Task Join_CreatesGroupContactFromProfile()
    {
        var transport = new FakeHttpHandler();
        using var http = new HttpClient(transport);
        var crm = new DispatchCrm();
        var groupId = "C00000000000000000000000000000000";
        var dispatcher = new WebhookDispatcher(
            new FixedScopeResolver(crm, "123", "token-A"),
            new SpeechMessageProducts.GoodShepherd.Core.Messaging.WebhookTextHandler(new ChurchClock()),
            new WebhookHttpAdapter(http),
            new WebhookAudioHandler(new WebhookHttpAdapter(http)),
            new RecordingLogger<WebhookDispatcher>(),
            new RecordingReplySender(),
            new RecordingLetterRecorder());

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Golden", "Events", "synthetic-join.json")));
        var item = Assert.Single(document.RootElement.GetProperty("events").EnumerateArray());

        await dispatcher.HandleAsync(new WebhookEnvelope("123", item), CancellationToken.None);

        var contact = Assert.Single(crm.Upserts.Where(static entity => entity.LogicalName == "contact"));
        Assert.Equal(groupId, contact.GetAttributeValue<string>("new_lineid"));
        Assert.Equal(groupId, contact.GetAttributeValue<string>("new_lineid_backup"));
        Assert.Equal("群組摘要", contact.GetAttributeValue<string>("lastname"));
        Assert.Equal("群組摘要", contact.GetAttributeValue<string>("new_line_displayname"));
        Assert.Equal("https://example.test/group.png", contact.GetAttributeValue<string>("new_line_picture_url"));
        Assert.Equal("群組", contact.GetAttributeValue<string>("new_line_type"));
        Assert.False(contact.GetAttributeValue<bool>("new_line_register"));
        Assert.Equal(100000001, contact.GetAttributeValue<OptionSetValue>("new_line_status")?.Value);
        Assert.Equal(crm.ChurchId, contact.GetAttributeValue<EntityReference>("parentcustomerid")?.Id);
        Assert.NotEqual(Guid.Empty, contact.Id);
        Assert.Equal("Bearer", transport.Authorization?.Scheme);
        Assert.Equal("token-A", transport.Authorization?.Parameter);
        Assert.Equal("https://api.line.me/v2/bot/group/" + groupId + "/summary", transport.RequestUri?.ToString());
    }

    /// <summary>
    /// 群組文字事件若缺少 LINE userId，只能具名忽略；不得把 C 群組 ID
    /// 當成 actor 交給 scope resolver，也不得讀寫 CRM。
    /// </summary>
    [Fact]
    public async Task GroupTextWithoutUserId_IsIgnoredWithoutUsingGroupAsActor()
    {
        var transport = new FakeHttpHandler();
        using var http = new HttpClient(transport);
        var crm = new DispatchCrm();
        var resolver = new FixedScopeResolver(crm, "123", "token-A");
        var logger = new RecordingLogger<WebhookDispatcher>();
        var dispatcher = new WebhookDispatcher(
            resolver,
            new SpeechMessageProducts.GoodShepherd.Core.Messaging.WebhookTextHandler(new ChurchClock()),
            new WebhookHttpAdapter(http),
            new WebhookAudioHandler(new WebhookHttpAdapter(http)),
            logger,
            new RecordingReplySender(),
            new RecordingLetterRecorder());
        using var document = JsonDocument.Parse(
            """
            {
              "type": "message",
              "mode": "active",
              "timestamp": 1735689600000,
              "source": {
                "type": "group",
                "groupId": "C00000000000000000000000000000000"
              },
              "webhookEventId": "01JGFJJZ000000000000000013",
              "deliveryContext": { "isRedelivery": false },
              "replyToken": "00000000000000000000000000000000",
              "message": {
                "id": "10000000000005",
                "type": "text",
                "text": "第一課簽到"
              }
            }
            """);

        await dispatcher.HandleAsync(
            new WebhookEnvelope("123", document.RootElement),
            CancellationToken.None);

        Assert.Equal(0, resolver.Calls);
        Assert.Empty(crm.Reads);
        Assert.Empty(crm.Updates);
        Assert.Empty(crm.Upserts);
        Assert.Contains(logger.EventIds, static id => id.Name == "Webhook.MissingUserSourceIgnored");
    }

    /// <summary>固定範圍 resolver 只借用單一案例的 scope，不保存跨測試事件狀態。</summary>
    private sealed class FixedScopeResolver(DispatchCrm crm, string channelId, string accessToken) : IWebhookScopeResolver
    {
        internal int Calls { get; private set; }

        /// <summary>只允許單一 synthetic channel，依真事件 source 建立隔離 scope，不把 U scope 借給群組。</summary>
        public ResolvedWebhookScope Resolve(string requestedChannelId, string sourceId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal(channelId, requestedChannelId);
            Calls++;
            return new ResolvedWebhookScope(
                crm,
                crm.OrganizationId,
                crm.ChurchId,
                channelId,
                sourceId,
                "Asia/Taipei",
                accessToken,
                sourceId == crm.UserId ? crm.Contact : null);
        }
    }

    /// <summary>只記錄 reply 呼叫數的離線替身；不連 LINE、不保存 token 或訊息正文。</summary>
    private sealed class RecordingReplySender : IWebhookReplySender
    {
        public ValueTask SendAsync(
            ResolvedWebhookScope scope,
            string? replyToken,
            SpeechMessageProducts.GoodShepherd.Core.Messaging.WebhookBusinessResult result,
            CancellationToken cancellationToken)
        {
            Assert.False(string.IsNullOrWhiteSpace(replyToken));
            Assert.NotNull(result.ReplyKey);
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }
    }

    /// <summary>只驗證 letter 交接存在的離線替身；CRM candidate 契約由 P3-08 專用測試直接驗證。</summary>
    private sealed class RecordingLetterRecorder : IWebhookLetterRecorder
    {
        public ValueTask<Guid> RecordAsync(
            ResolvedWebhookScope scope,
            string webhookEventId,
            string messageKind,
            string description,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(SpeechMessageProducts.GoodShepherd.Core.Messaging.WebhookRecordOperations.BusinessId(
                scope.OrganizationId, scope.ChurchId, "test-letter", webhookEventId));
        }
    }

    /// <summary>只保留固定事件識別碼，驗證已知未移植事件有安全診斷且不擷取個資或例外。</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        internal List<EventId> EventIds { get; } = [];

        /// <summary>測試不建立 scope、subscription 或背景資源。</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>允許擷取資訊層級的固定 dispatcher disposition。</summary>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <summary>僅記錄 EventId；不保存格式化本文、狀態物件或例外參照。</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            EventIds.Add(eventId);
        }
    }

    /// <summary>離線 HTTP 替身記錄請求形狀並回傳 profile 或有界音訊，不建立 socket 或背景工作。</summary>
    private sealed class FakeHttpHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        /// <summary>固定小型資料供 Base64 annotation 斷言，避免測試配置無界內容。</summary>
        internal static readonly byte[] AudioBytes = [1, 2, 3, 4];
        internal AuthenticationHeaderValue? Authorization { get; private set; }
        internal Uri? RequestUri { get; private set; }
        internal int RequestCount { get; private set; }

        /// <summary>同步記錄本次 request 後回傳可釋放 response；不使用共用 DefaultRequestHeaders。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Authorization = request.Headers.Authorization;
            RequestUri = request.RequestUri;
            RequestCount++;
            var audio = request.RequestUri?.AbsolutePath.EndsWith("/content", StringComparison.Ordinal) == true;
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.OK)
            {
                var isGroup = request.RequestUri?.AbsolutePath.Contains("/group/", StringComparison.Ordinal) == true;
                response.Content = audio
                    ? new ByteArrayContent(AudioBytes)
                    : isGroup
                        ? new StringContent("{\"groupId\":\"C00000000000000000000000000000000\",\"groupName\":\"群組摘要\",\"pictureUrl\":\"https://example.test/group.png\"}")
                        : new StringContent("{\"userId\":\"U00000000000000000000000000000000\",\"displayName\":\"Synthetic user\",\"pictureUrl\":\"\",\"statusMessage\":\"\"}");
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(audio ? "audio/x-m4a" : "application/json");
            }
            return Task.FromResult(response);
        }
    }

    /// <summary>回傳已知超限 Content-Length 的音訊，不配置完整內容，驗證 adapter 先拒絕再觸碰 CRM。</summary>
    private sealed class OversizedAudioHandler : HttpMessageHandler
    {
        /// <summary>建立帶有超過本包容量的串流回應；回應由 HttpClient caller 釋放。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new OversizedAudioContent()
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/x-m4a");
            return Task.FromResult(response);
        }
    }

    /// <summary>只宣告超限長度的內容，避免測試為了驗證 header 先配置超過預算的 byte[]。</summary>
    private sealed class OversizedAudioContent : HttpContent
    {
        /// <summary>回報一個超過 5 MiB 的長度，adapter 應在讀取 stream 前拒絕。</summary>
        protected override bool TryComputeLength(out long length)
        {
            length = (long)WebhookHttpAdapter.MaximumAudioBytes + 1;
            return true;
        }

        /// <summary>此測試預期不會讀取內容；若誤讀則以固定錯誤暴露行為。</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("Oversized audio content must be rejected before reading.");
    }

    /// <summary>在第一個串流讀取點停住，供測試注入 caller cancellation 並驗證零 CRM 寫入。</summary>
    private sealed class BlockingAudioHandler : HttpMessageHandler
    {
        internal TaskCompletionSource<bool> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>回傳可取消的有界串流，所有 owner 由 HttpClient／adapter 關閉。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingReadStream(ReadStarted))
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/x-m4a");
            return Task.FromResult(response);
        }
    }

    /// <summary>以 cancellation token 結束讀取，不建立背景工作或保存 caller scope。</summary>
    private sealed class BlockingReadStream(TaskCompletionSource<bool> readStarted) : Stream
    {
        /// <summary>測試串流只允許讀取。</summary>
        public override bool CanRead => true;
        /// <summary>測試串流不可定位。</summary>
        public override bool CanSeek => false;
        /// <summary>測試串流不可寫入。</summary>
        public override bool CanWrite => false;

        /// <summary>通知測試已進入真實串流讀取。</summary>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            readStarted.TrySetResult(true);
            return new ValueTask<int>(WaitForCancellationAsync(cancellationToken));
        }

        /// <summary>相容 StreamContent 在舊 overload 上的呼叫路徑，同樣只等待明確取消。</summary>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WaitForCancellationAsync(cancellationToken);

        /// <summary>只在測試 cancellation path 使用；沒有成功同步讀取語義。</summary>
        private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        /// <summary>測試不允許同步讀取。</summary>
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous reads are not allowed.");
        /// <summary>此測試串流不可寫入。</summary>
        public override void Flush() => throw new NotSupportedException();
        /// <summary>此測試串流不可設定長度。</summary>
        public override long Length => throw new NotSupportedException();
        /// <summary>此測試串流不可定位。</summary>
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        /// <summary>此測試串流不可設定長度。</summary>
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <summary>此測試串流不可寫入。</summary>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <summary>此測試串流不可定位。</summary>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }

    /// <summary>記錄兩個併行 scope 的 Bearer，回傳固定小音訊，不共享任何產品狀態。</summary>
    private sealed class ConcurrentAudioHandler : HttpMessageHandler
    {
        internal ConcurrentBag<string> Tokens { get; } = [];

        /// <summary>每個 request 只讀自己的 Authorization，回應由 caller 完成 Dispose。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Tokens.Add(request.Headers.Authorization?.Parameter ?? string.Empty);
            await Task.Yield();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/x-m4a");
            return response;
        }
    }

    /// <summary>窄 CRM 替身只允許本人讀取及 annotation Upsert，其他操作明確失敗。</summary>
    private sealed class DispatchCrm : IOrganizationService
    {
        internal readonly Guid OrganizationId = Guid.NewGuid();
        internal readonly Guid ChurchId = Guid.NewGuid();
        internal readonly Guid ContactId = Guid.NewGuid();
        internal readonly string UserId = "U00000000000000000000000000000000";
        internal readonly Entity Contact;
        internal readonly List<Guid> Reads = [];
        internal readonly List<Entity> Upserts = [];

        /// <summary>建立與 actor 同一教會的正式 contact，禁止以顯示名稱取代 ID。</summary>
        internal DispatchCrm()
        {
            Contact = new Entity("contact", ContactId)
            {
                ["parentcustomerid"] = new EntityReference("account", ChurchId),
                ["new_lineid"] = UserId,
                RowVersion = "7"
            };
        }

        /// <summary>只回傳本案例 Contact，記錄讀取以證明 handler 確實複核 actor。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (entityName == "contact" && id == ContactId) { Reads.Add(id); return Contact; }
            throw new InvalidOperationException("Unexpected retrieve.");
        }

        /// <summary>只接受 annotation Upsert，回傳相同正式 ID；不提供任意 query 或刪除能力。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is UpsertRequest upsert && upsert.Target.LogicalName is "annotation" or "contact")
            {
                Upserts.Add(upsert.Target);
                var response = new UpsertResponse();
                response.Results["Target"] = new EntityReference(upsert.Target.LogicalName, upsert.Target.Id);
                return response;
            }
            if (request is UpdateRequest update)
            {
                Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
                Assert.Equal("7", update.Target.RowVersion);
                Updates.Add(update.Target);
                return new UpdateResponse();
            }
            throw new InvalidOperationException("Unexpected CRM operation.");
        }

        internal readonly List<Entity> Updates = [];

        /// <summary>所有 Entity 建立都必須先經上方明確的型別化原子 Upsert；拒絕非預期直接建立。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException("Unexpected create.");
        /// <summary>annotation 以窄 adapter 轉 Upsert，其他 Update 拒絕。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Unexpected update.");
        /// <summary>本測試沒有刪除契約。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected delete.");
        /// <summary>本測試沒有關聯契約。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException("Unexpected associate.");
        /// <summary>本測試沒有解除關聯契約。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException("Unexpected disassociate.");
        /// <summary>本測試不提供跨記錄查詢，避免把 fake 當成完整 CRM。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new InvalidOperationException("Unexpected query.");
    }

    /// <summary>固定 UTC 時鐘供文字事件使用；不讀主機 local 時區或共享狀態。</summary>
    private sealed class ChurchClock : IChurchClock
    {
        /// <summary>測試固定時間，讓雙目標結果一致。</summary>
        public DateTimeOffset UtcNow => new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        /// <summary>使用固定時區轉換介面，避免依賴主機設定。</summary>
        public DateTime ToChurchTime(DateTime utcNow, string timeZone) => utcNow;
        /// <summary>測試固定日期，不讀主機 local 時區。</summary>
        public DateTime TodayInChurch(string timeZone) => new(2025, 1, 1);
        /// <summary>此 dispatcher 測試不建立牆上時間輸入，拒絕未使用轉換。</summary>
        public DateTime ToUtc(LocalDateTimeOfChurch local) => throw new InvalidOperationException("Unexpected local-time conversion.");
    }
}
