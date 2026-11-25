/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P8-04b 16.4 第 2 步的正式 LineWebhook 收件、簽章、
 * bounded queue、dispatcher、GoodShepherd 核心與 reply 傳輸組合。
 *
 * 主要功能與流程：以正式 Program composition 啟動只綁 loopback ephemeral port
 * 的 Kestrel Host，使用測試專用 HMAC 簽章送入兩筆合成文字事件。scope resolver
 * 提供固定合成 Contact 與 qps-ploc scope；LINE reply 的 named HttpClient 由測試
 * handler 接收，僅擷取回覆文字，不開啟外部 socket、不保存 reply token 或憑證。
 *
 * 使用邊界與維護：signature verifier、queue、dispatcher、領域 handler 與 reply
 * sender 均由正式 Host 擁有；測試只替換不可信外部資料來源與最後的 HTTP transport。
 * Contact、事件、測試 HMAC key、Host、HTTP request/response、queue worker 及 scope
 * 均由單一案例建立，Host 停止時 drain queue 並釋放資源。失敗以 HTTP 狀態、回覆
 * 數量及 qps-ploc 括號斷言呈現；本測試不連 D365、LINE、SQL 或任何組織，也不代表
 * goodshepherdback 實機驗收。
 */
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Dataverse;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests.Localization;

/// <summary>以真實簽章與 dispatcher 驗證指定關鍵字路由及實際產生的逐段偽語系回覆。</summary>
public sealed class PseudoWebhookDispatchAcceptanceTests
{
    /// <summary>主日出席與 Report 經正式 queue 路由；Help 的實際回覆須使用本事件 qps-ploc 文字。</summary>
    [Fact]
    public async Task Pseudo_Step2_KeywordsRecognized_AllSegmentsBracketed()
    {
        var replies = new CapturedReplies();
        await using var host = new WebhookSignatureTestHost(
            configureServices: services =>
            {
                services.RemoveAll<IDataverseConnectionManager>();
                services.AddSingleton<IDataverseConnectionManager, OfflineDataverseManager>();
                services.RemoveAll<IWebhookScopeResolver>();
                services.AddScoped<IWebhookScopeResolver>(_ =>
                    new SyntheticScopeResolver(new SyntheticCrm()));
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
                    new ReplyCaptureFilter(replies));
            },
            useBoundedQueue: true);
        await host.StartAsync();

        var body = CreateSignedEventBatch();
        using var response = await host.PostAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var messages = await replies.WaitForMessagesAsync(1, TimeSpan.FromSeconds(10));
        Assert.Single(messages);
        Assert.All(messages, message =>
        {
            Assert.StartsWith("［", message, StringComparison.Ordinal);
            Assert.EndsWith("］", message, StringComparison.Ordinal);
            Assert.Contains("ǂ", message, StringComparison.Ordinal);
        });
    }

    /// <summary>建立兩個指定命令與一個有既定 Reply 契約的 Help 事件，均只在本測試記憶體簽章。</summary>
    private static string CreateSignedEventBatch()
    {
        var events = new[]
        {
            CreateEvent("01JGFJJZ000000000000000001", "SERVICE"),
            CreateEvent("01JGFJJZ000000000000000002", "REPORT"),
            CreateEvent("01JGFJJZ000000000000000003", "??")
        };
        return JsonSerializer.Serialize(new { events });
    }

    /// <summary>使用固定來源與一次性合成 reply token 建立 LINE SDK 接受的文字 event JSON。</summary>
    private static object CreateEvent(string eventId, string text) => new
    {
        type = "message",
        mode = "active",
        timestamp = 1735689600000L,
        source = new { type = "user", userId = SyntheticCrm.UserId },
        webhookEventId = eventId,
        deliveryContext = new { isRedelivery = false },
        replyToken = "00000000000000000000000000000000",
        message = new { id = "10000000000001", type = "text", text }
    };

    /// <summary>僅接收 Reply API 回應本文中的文字欄位；不保存 bearer、reply token 或完整 payload。</summary>
    private sealed class CapturedReplies
    {
        private readonly ConcurrentQueue<string> messages = new();
        private readonly TaskCompletionSource completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>複製已完成的文字結果；尚未達預期數量時以有界等待暴露失敗。</summary>
        internal async Task<string[]> WaitForMessagesAsync(int count, TimeSpan timeout)
        {
            await completed.Task.WaitAsync(timeout);
            var result = messages.ToArray();
            Assert.Equal(count, result.Length);
            return result;
        }

        /// <summary>解析固定 Reply API schema，僅保留 messages[].text。</summary>
        internal void Capture(string json)
        {
            using var document = JsonDocument.Parse(json);
            foreach (var message in document.RootElement.GetProperty("messages").EnumerateArray())
                messages.Enqueue(message.GetProperty("text").GetString() ?? string.Empty);
            if (!messages.IsEmpty)
                completed.TrySetResult();
        }
    }

    /// <summary>在 HttpClientFactory 管線尾端替換指定 named client handler，防止任何外部 LINE 傳輸。</summary>
    private sealed class ReplyCaptureFilter(CapturedReplies replies) : IHttpMessageHandlerBuilderFilter
    {
        /// <summary>其餘 client 保留原管線；Reply sender 的最終 handler 固定為本案例記憶體接收器。</summary>
        public Action<HttpMessageHandlerBuilder> Configure(
            Action<HttpMessageHandlerBuilder> next) =>
            builder =>
            {
                next(builder);
                if (string.Equals(builder.Name, "LineWebhookReplySender", StringComparison.Ordinal))
                    builder.PrimaryHandler = new ReplyCaptureHandler(replies);
            };
    }

    /// <summary>不建 socket 的 HTTP 回覆替身；只解析文字並回傳固定成功狀態。</summary>
    private sealed class ReplyCaptureHandler(CapturedReplies replies) : HttpMessageHandler
    {
        /// <summary>接收 request 本文後即捨棄 token 欄位，不保存完整 payload 或 request。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri?.AbsoluteUri !=
                "https://api.line.me/v2/bot/message/reply")
                throw new InvalidOperationException("Unexpected synthetic HTTP destination.");
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            replies.Capture(json);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>測試專屬已授權 scope；正式 verifier 與 queue 仍先驗證簽章及事件交接。</summary>
    private sealed class SyntheticScopeResolver(SyntheticCrm crm) : IWebhookScopeResolver
    {
        private static readonly string[] EnabledCultures = ["qps-ploc", "zh-TW"];

        /// <summary>只接受本測試核准的 channel/user，回傳單一合成教會與 qps-ploc 文化。</summary>
        public ResolvedWebhookScope Resolve(
            string channelId,
            string sourceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (channelId != "123" || sourceId != SyntheticCrm.UserId)
                throw new InvalidOperationException("Unexpected synthetic webhook scope.");
            return new ResolvedWebhookScope(
                crm,
                SyntheticCrm.OrganizationId,
                SyntheticCrm.ChurchId,
                channelId,
                sourceId,
                "Asia/Taipei",
                "synthetic-access-token",
                crm.Contact,
                memberCulture: "qps-ploc",
                formatCulture: "en-US",
                enabledCultures: EnabledCultures);
        }
    }

    /// <summary>只提供本案例 Contact 與空出席集合；所有寫入及未知資料查詢皆拒絕。</summary>
    private sealed class SyntheticCrm : IOrganizationService
    {
        internal static readonly Guid OrganizationId =
            Guid.Parse("11111111-1111-4111-8111-111111111111");
        internal static readonly Guid ChurchId =
            Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid ContactId =
            Guid.Parse("33333333-3333-4333-8333-333333333333");
        internal const string UserId = "U00000000000000000000000000000000";

        /// <summary>單一 actor contact 含正式教會 lookup 與 LINE binding，不以顯示文字識別。</summary>
        internal Entity Contact { get; } = new("contact", ContactId)
        {
            ["parentcustomerid"] = new EntityReference("account", ChurchId),
            ["new_lineid"] = UserId,
            ["fullname"] = "Synthetic member",
            ["versionnumber"] = 1L
        };

        /// <summary>回傳該案例唯一 actor；未授權或非預期讀取明確失敗。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (entityName == "contact" && id == Contact.Id)
                return Contact;
            throw new InvalidOperationException("Unexpected synthetic retrieve.");
        }

        /// <summary>主日出席情境使用空集合，證明路由已到達領域查詢但不寫入任何資料。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (query is QueryExpression expression &&
                expression.EntityName == "new_present_record")
                return new EntityCollection();
            throw new InvalidOperationException("Unexpected synthetic query.");
        }

        /// <summary>
        /// 僅支援正式入站 letter recorder 所需的 Upsert；回傳穩定主鍵以驗證
        /// dispatcher 沒有因為合成 CRM 的缺省 target identity 而遺失事件。
        /// 其他 SDK 命令一律拒絕，避免測試不經意跨出本案例的離線邊界。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is UpsertRequest { Target: Entity target } &&
                target.LogicalName == "letter" &&
                target.Id != Guid.Empty)
            {
                return new UpsertResponse
                {
                    Results =
                    {
                        ["Target"] = new EntityReference(target.LogicalName, target.Id)
                    }
                };
            }

            throw new InvalidOperationException("Unexpected synthetic execute.");
        }

        /// <summary>事件測試只讀，不允許建立 CRM 資料。</summary>
        public Guid Create(Entity entity) =>
            throw new InvalidOperationException("Unexpected synthetic create.");
        /// <summary>事件測試只讀，不允許更新 CRM 資料。</summary>
        public void Update(Entity entity) =>
            throw new InvalidOperationException("Unexpected synthetic update.");
        /// <summary>事件測試只讀，不允許刪除 CRM 資料。</summary>
        public void Delete(string entityName, Guid id) =>
            throw new InvalidOperationException("Unexpected synthetic delete.");
        /// <summary>事件測試不建立關聯。</summary>
        public void Associate(string entityName, Guid id, Relationship relationship,
            EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected synthetic associate.");
        /// <summary>事件測試不解除關聯。</summary>
        public void Disassociate(string entityName, Guid id, Relationship relationship,
            EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected synthetic disassociate.");
    }
}
