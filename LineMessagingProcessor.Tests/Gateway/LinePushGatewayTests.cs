/*
檔案整體說明
目的與定位：F5 唯一出口的雙目標離線行為測試，以合成 SDK、HTTP handler 及原子記憶體
store 保護全名單前置核對、500 人分批、同鍵重試與不可確定結果不重送。
主要功能與流程：每個案例建立自己的 fixture，注入跨租戶、非測試、逾時、HTTP 409/500、
claim/CAS 故障與後到取消，斷言真正 Gateway 的網路次數、私有 JSON、持久狀態與原例外。
使用邊界與維護：沒有真實 Channel、Dataverse 或手機結果；替身原子性只供離線編排測試，
不能冒充資料庫唯一鍵驗收。fixture 擁有 HttpClient/handler，傳輸回應由正式 Core Dispose，
所有資料只活到案例結束，不建立 timer、queue 或無界 static 集合。
*/
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using Line.Messaging;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Gateway;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LineMessagingProcessor.Tests.Gateway;

/// <summary>使用真 Gateway 與真 Core 傳輸，依賴替身只提供合成資料及故障注入。</summary>
public sealed partial class LinePushGatewayTests
{
    /// <summary>第二租戶收件人使整案拒絕，不准先送第一個合法批次。</summary>
    [Fact]
    public async Task Send_RecipientFromOtherTenant_RejectedAndNothingSent()
    {
        using var f = new Fixture();
        f.Membership.Members = false;
        var result = await f.Send(600);
        Assert.Equal(PushRejectedReason.ForeignRecipient, result.RejectedReason);
        Assert.Empty(f.Handler.Sends);
        Assert.Contains(f.Store.Completed.Values, item => item.Outcome == SendRecordOutcome.Rejected);
    }

    /// <summary>正式租戶 ID 缺失在資料查詢及 HTTP 前拋出，不建立偽租戶紀錄。</summary>
    [Fact]
    public async Task Send_WithoutTenantScope_Throws()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Send(1, scope: f.Scope with { TenantId = Guid.Empty }));
        Assert.Empty(f.Handler.Sends);
        Assert.Empty(f.Store.Claims);
    }

    /// <summary>600 人以兩個多播批次傳送；紀錄人數總和只計平台已接受。</summary>
    [Fact]
    public async Task Send_600Recipients_TwoMulticastBatches()
    {
        using var f = new Fixture();
        var result = await f.Send(600);
        var sends = f.Handler.Sends.ToArray();
        Assert.Equal(2, sends.Length);
        Assert.All(sends, item => Assert.EndsWith("/message/multicast", item.Url));
        Assert.Equal(500, ((JArray)JObject.Parse(sends[0].Body)["to"]!).Count);
        Assert.Equal(100, ((JArray)JObject.Parse(sends[1].Body)["to"]!).Count);
        Assert.Equal(600, result.AcceptedRecipientCount);
        Assert.Equal(600, f.Store.Claims.Values.Sum(item => item.Identity.RecipientCount));
        Assert.Equal(2, f.Store.Completed.Count);
        Assert.All(result.Batches, item => Assert.Equal(SendRecordOutcome.Accepted, item.Outcome));
    }

    /// <summary>多人操作即使尾批只剩一人仍必須multicast，不能切換HTTP形狀導致同事件重試內容漂移。</summary>
    [Fact]
    public async Task Send_501Recipients_TailOneRemainsMulticast()
    {
        using var fixture = new Fixture();
        var result = await fixture.Send(501);
        var sends = fixture.Handler.Sends.ToArray();
        Assert.Equal(2, sends.Length);
        Assert.All(sends, send => Assert.EndsWith("/message/multicast", send.Url));
        Assert.Equal(500, ((JArray)JObject.Parse(sends[0].Body)["to"]!).Count);
        Assert.Single((JArray)JObject.Parse(sends[1].Body)["to"]!);
        Assert.Equal(501, result.AcceptedRecipientCount);
        Assert.Equal(501, fixture.Store.Claims.Values.Sum(claim => claim.Identity.RecipientCount));
    }

    /// <summary>預設僅測試租戶保護；非測試租戶沒有任何 HTTP 並保留拒絕紀錄。</summary>
    [Fact]
    public async Task Send_NonTestTenant_RejectedWhileTestTenantsOnly()
    {
        using var f = new Fixture();
        f.Membership.TestTenant = false;
        var result = await f.Send(1);
        Assert.Equal(PushRejectedReason.NonTestTenant, result.RejectedReason);
        Assert.Empty(f.Handler.Sends);
        Assert.Single(f.Store.Completed);
        Assert.Equal(SendRecordOutcome.Rejected, f.Store.Completed.Single().Value.Outcome);
        Assert.Empty(f.Failures);
    }

    /// <summary>scope 提示可冒名但不得進入 sender；實際使用查詢讀回的權威品牌。</summary>
    [Fact]
    public async Task Send_SenderNameAndIcon_InjectedFromAuthority()
    {
        using var f = new Fixture();
        await f.Send(1, scope: f.Scope with { TenantDisplayName = "forged", TenantIconUrl = "https://wrong.test" });
        var message = JObject.Parse(Assert.Single(f.Handler.Sends).Body)["messages"]![0]!;
        Assert.Equal("權威租戶", (string?)message["sender"]?["name"]);
        Assert.Equal("https://example.test/icon", (string?)message["sender"]?["iconUrl"]);
    }

    /// <summary>逾時重試的 header/body 完全相同；一次最終接受，不重複通知恢復的例外。</summary>
    [Fact]
    public async Task Push_TimeoutRetry_SameRetryKey_SentOnce()
    {
        using var f = new Fixture();
        f.Handler.Respond = index => index == 1
            ? throw new TaskCanceledException("synthetic internal timeout")
            : new HttpResponseMessage(HttpStatusCode.OK);
        var result = await f.Send(1);
        var sends = f.Handler.Sends.ToArray();
        Assert.Equal(2, sends.Length);
        Assert.Equal(sends[0].Retry, sends[1].Retry);
        Assert.Equal(sends[0].Body, sends[1].Body);
        Assert.Equal(1, result.AcceptedRecipientCount);
        Assert.Equal(2, Assert.Single(f.Store.Completed).Value.Attempts);
        Assert.Empty(f.Failures);
    }

    /// <summary>409 是先前同鍵已接受，重入相同事件不得再次傳輸。</summary>
    [Fact]
    public async Task Send_409_AlreadyAccepted_IsNotResent()
    {
        using var f = new Fixture();
        f.Handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict);
        Assert.Equal(1, (await f.Send(1)).AcceptedRecipientCount);
        Assert.Equal(1, (await f.Send(1)).AcceptedRecipientCount);
        Assert.Single(f.Handler.Sends);
    }

    /// <summary>5xx 沒有確定未送證據，不換鍵、不自動重試；再次呼叫只回對帳。</summary>
    [Fact]
    public async Task Send_Uncertain_IsPersistentAndNeverResent()
    {
        using var f = new Fixture();
        f.Handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        Assert.True((await f.Send(1)).RequiresReconciliation);
        Assert.True((await f.Send(1)).RequiresReconciliation);
        Assert.Single(f.Handler.Sends);
        Assert.Single(f.Failures);
    }

    /// <summary>同事件更改完整名單，即使首批有相同前綴也必須在 batch0 拒絕。</summary>
    [Fact]
    public async Task Send_SameOperationDifferentPayload_ConflictsBeforeHttp()
    {
        using var f = new Fixture();
        await f.Send(600);
        var result = await f.Send(601);
        Assert.Equal(PushRejectedReason.PayloadConflict, result.RejectedReason);
        Assert.Equal(2, f.Handler.Sends.Count);
    }

    /// <summary>claim 儲存故障原樣傳出並告警，不以無紀錄 HTTP 補救。</summary>
    [Fact]
    public async Task Send_ClaimFailure_NoHttpAndOriginalException()
    {
        using var f = new Fixture();
        var failure = new InvalidOperationException("synthetic claim failure");
        f.Store.ClaimFailure = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Send(1)));
        Assert.Empty(f.Handler.Sends);
        Assert.Same(failure, Assert.Single(f.Failures));
    }

    /// <summary>HTTP 接受後 CAS 失敗仍保留 claim；再次呼叫不能把它當未送而重送。</summary>
    [Fact]
    public async Task Send_CompletionFailure_PreservesUncertainClaim()
    {
        using var f = new Fixture();
        var failure = new InvalidOperationException("synthetic completion failure");
        f.Store.CompletionFailure = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Send(1)));
        f.Store.CompletionFailure = null;
        Assert.True((await f.Send(1)).RequiresReconciliation);
        Assert.Single(f.Handler.Sends);
        Assert.Same(failure, Assert.Single(f.Failures));
    }

    /// <summary>回應已抵達後 caller 取消，不得取消已接受紀錄或回報為未送。</summary>
    [Fact]
    public async Task Send_LateCancellation_PersistsAccepted()
    {
        using var f = new Fixture();
        using var cts = new CancellationTokenSource();
        f.Handler.Respond = _ => { cts.Cancel(); return new HttpResponseMessage(HttpStatusCode.OK); };
        var result = await f.Send(1, ct: cts.Token);
        Assert.Equal(1, result.AcceptedRecipientCount);
        Assert.Single(f.Store.Completed);
        Assert.Empty(f.Failures);
    }

    /// <summary>相同 userId 的重複輸入不能用 Distinct 靜默掩蓋。</summary>
    [Fact]
    public async Task Send_DuplicateRecipient_ThrowsBeforeClaim()
    {
        using var f = new Fixture();
        var id = Users(1)[0];
        await Assert.ThrowsAsync<ArgumentException>(() => f.Gateway.SendAsync(f.Scope, new[] { id, id },
            new ISendMessage[] { new TextMessage("hello") }, MessageCategory.Operational, "event-1", default));
        Assert.Empty(f.Store.Claims);
        Assert.Empty(f.Handler.Sends);
    }

    /// <summary>會員查詢期間 caller 修改名單與 SDK 模型，真正 HTTP 仍使用進場時的私有快照。</summary>
    [Fact]
    public async Task Send_MutableInputs_AreFrozenBeforeMembership()
    {
        using var f = new Fixture();
        var users = Users(1).ToList();
        var originalUser = users[0];
        var message = new TextMessage("original");
        f.Membership.OnRead = () => { users[0] = Users(2)[1]; message.Text = "changed"; };
        await f.Gateway.SendAsync(f.Scope, users, new ISendMessage[] { message },
            MessageCategory.Operational, "mutable-input", default);
        var sent = JObject.Parse(Assert.Single(f.Handler.Sends).Body);
        Assert.Equal(originalUser, (string?)sent["to"]);
        Assert.Equal("original", (string?)sent["messages"]?[0]?["text"]);
        Assert.Equal("changed", message.Text);
    }

    /// <summary>
    /// 64 路 A/B 同時共用 Gateway／HttpClient，租戶品牌、Channel 憑證、內容及紀錄不得串線。
    /// 所有操作 await 排空後，逐一存取 CTS.Token 必須拋 disposed，且成功回應內容釋放數回到請求數。
    /// </summary>
    [Fact]
    public async Task Send_ConcurrentAB_IsolatedAndResourcesDrained()
    {
        using var f = new Fixture();
        var a = f.Scope with { ChannelKey = "10001" };
        var b = f.Scope with { ChannelKey = "10002", TenantId = new Guid("99999999-8888-7777-6666-555555555555") };
        f.Membership.Facts = scope => new TenantPushFacts(scope.ChannelKey == "10001" ? "租戶A" : "租戶B", null, true);
        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(index => Task.Run(() =>
            f.Gateway.SendAsync(index % 2 == 0 ? a : b, new[] { Users(index + 1)[index] },
                new ISendMessage[] { new TextMessage("message-" + index.ToString(CultureInfo.InvariantCulture)) },
                MessageCategory.Operational, "parallel-" + index.ToString(CultureInfo.InvariantCulture), default))));
        Assert.All(results, result => Assert.Equal(1, result.AcceptedRecipientCount));
        Assert.Equal(64, f.Store.Completed.Count);
        Assert.Equal(64, f.Handler.Sends.Count);
        foreach (var send in f.Handler.Sends)
        {
            var body = JObject.Parse(send.Body);
            var text = (string)body["messages"]![0]!["text"]!;
            var index = int.Parse(text.Substring("message-".Length), CultureInfo.InvariantCulture);
            Assert.Equal(Users(index + 1)[index], (string?)body["to"]);
            Assert.Equal(index % 2 == 0 ? "租戶A" : "租戶B", (string?)body["messages"]?[0]?["sender"]?["name"]);
            Assert.Equal(index % 2 == 0 ? "synthetic-10001" : "synthetic-10002", send.Authorization);
        }
        Assert.Equal(64, f.Handler.DisposedResponses);
        Assert.Equal(64, f.Timeouts.Count);
        foreach (var source in f.Timeouts)
            Assert.Throws<ObjectDisposedException>(() => { _ = source.Token; });
        Assert.Empty(f.Failures);
    }

    /// <summary>每個測試獨佔依賴 owner，沒有可變 static／背景資源。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly Handler Handler = new();
        internal readonly Membership Membership = new();
        internal readonly Store Store = new();
        internal readonly List<Exception> Failures = new();
        internal readonly ConcurrentBag<CancellationTokenSource> Timeouts = new();
        internal readonly PushScope Scope = new("GoodShepherd2", "1234567890",
            new Guid("11111111-2222-3333-4444-555555555555"), "scope hint", null);
        private readonly HttpClient http;
        internal readonly LinePushGateway Gateway;

        /// <summary>使用只讀合成產品來源與真 Core，不建立實體連線池以外的額外背景 owner。</summary>
        internal Fixture()
        {
            http = new HttpClient(Handler);
            var transport = new LinePushClient(http, duration =>
            {
                var source = new CancellationTokenSource(duration);
                Timeouts.Add(source);
                return source; // 所有權移交 Core，測試只觀察其已釋放狀態，不代為清理。
            });
            Gateway = new LinePushGateway("GoodShepherd2", new Credentials(), transport,
                Membership, Store, (exception, _) => Failures.Add(exception));
        }
        /// <summary>合成使用者依序產生，不代表真實 LINE ID。</summary>
        internal Task<LinePushGatewayResult> Send(int count, PushScope? scope = null, CancellationToken ct = default) =>
            Gateway.SendAsync(scope ?? Scope, Users(count), new ISendMessage[] { new TextMessage("hello") },
                MessageCategory.Operational, "event-1", ct);
        /// <summary>HttpClient 擁有 handler，操作均已 await 完成後才釋放。</summary>
        public void Dispose() => http.Dispose();
    }

    /// <summary>產生合法但合成的有序 ID，專供離線序列化及名單一致性斷言。</summary>
    private static string[] Users(int count) =>
        Enumerable.Range(1, count).Select(index => "U" + index.ToString("x32", CultureInfo.InvariantCulture)).ToArray();

    /// <summary>每次委派臨時建立合成 credential；沒有跨操作快取或真實憑證。</summary>
    private sealed class Credentials : ILineCredentialExecutor
    {
        /// <summary>合成來源只用於固定產品測試，不代表真目錄驗收。</summary>
        public bool TryGetCredential(string channelKey, out LineChannelCredential? credential)
        { credential = new LineChannelCredential("synthetic-" + channelKey); return true; }
        /// <summary>直接執行一次純值委派，401 世代行為另由 F3 adapter 測試保護。</summary>
        public Task<LinePushResult> ExecuteAsync(string channelKey,
            Func<LineChannelCredential, CancellationToken, Task<LinePushResult>> send, CancellationToken cancellationToken) =>
            send(new LineChannelCredential("synthetic-" + channelKey), cancellationToken);
    }

    /// <summary>當次權威查詢替身；只控制合法性，不實作或冒充 Dataverse。</summary>
    private sealed class Membership : IRecipientMembership
    {
        internal bool Members = true;
        internal bool TestTenant = true;
        internal Action? OnRead;
        internal Func<PushScope, TenantPushFacts?>? Facts;
        /// <summary>固定品牌與 scope 提示不同，用以抓到冒名或未查權威事實的實作。</summary>
        public Task<TenantPushFacts?> ReadTenantAsync(PushScope scope, CancellationToken cancellationToken)
        {
            OnRead?.Invoke();
            return Task.FromResult(Facts is null
                ? new TenantPushFacts("權威租戶", "https://example.test/icon", TestTenant) : Facts(scope));
        }
        /// <summary>故障點以布林控制跨租戶整案拒絕。</summary>
        public Task<bool> AreAllMembersAsync(PushScope scope, IReadOnlyList<string> recipientUserIds, CancellationToken cancellationToken) =>
            Task.FromResult(Members);
        /// <summary>與權威快照一致的測試旗標，缺省真只屬於合成案例。</summary>
        public Task<bool> IsTestTenantAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(TestTenant);
    }

    /// <summary>單案例原子記憶體 claim；資料庫唯一性仍需 adapter 專屬測試，不由此替代。</summary>
    private sealed class Store : ISendRecordStore
    {
        internal readonly ConcurrentDictionary<string, SendBatchClaim> Claims = new();
        internal readonly ConcurrentDictionary<string, SendBatchCompletion> Completed = new();
        internal Exception? ClaimFailure;
        internal Exception? CompletionFailure;
        /// <summary>相同 key 僅一個勝出，完整 identity 不同即衝突；既有占用不能接手。</summary>
        public Task<SendBatchClaim> ClaimBatchAsync(SendBatchIdentity identity, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ClaimFailure is not null) throw ClaimFailure;
            var created = new SendBatchClaim(identity, Guid.NewGuid(), "1", SendClaimDisposition.Acquired);
            if (Claims.TryAdd(identity.BatchKey, created)) return Task.FromResult(created);
            var existing = Claims[identity.BatchKey];
            var disposition = existing.Identity != identity ? SendClaimDisposition.PayloadConflict
                : Completed.TryGetValue(identity.BatchKey, out var completion)
                    ? completion.Outcome == SendRecordOutcome.Accepted ? SendClaimDisposition.AlreadyAccepted
                        : completion.Outcome == SendRecordOutcome.Uncertain ? SendClaimDisposition.Uncertain : SendClaimDisposition.AlreadyFinal
                    : SendClaimDisposition.Uncertain;
            return Task.FromResult(existing with { Disposition = disposition });
        }
        /// <summary>紀錄 caller token 狀態可抓到後到取消誤用；故障注入時保留原 claim。</summary>
        public Task CompleteBatchAsync(SendBatchClaim claim, SendBatchCompletion completion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CompletionFailure is not null) throw CompletionFailure;
            Completed[claim.Identity.BatchKey] = completion;
            return Task.CompletedTask;
        }
    }

    /// <summary>只保存合成純值而非 request／response，避免測試自身保留 HTTP 資源。</summary>
    private sealed record Captured(string Url, string Body, string Retry, string? Authorization);

    /// <summary>單案例 handler，按次數注入傳輸故障或平台狀態。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<Captured> Sends = new();
        internal Func<int, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
        internal int DisposedResponses;
        /// <summary>真正序列化主體與 retry header 被觀察後才注入故障，回應移交 Core Dispose。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync();
            Sends.Enqueue(new Captured(request.RequestUri!.ToString(), body, request.Headers.GetValues("X-Line-Retry-Key").Single(),
                request.Headers.Authorization?.Parameter));
            var response = Respond(Sends.Count);
            response.Content = new CountedContent(() => Interlocked.Increment(ref DisposedResponses));
            return response;
        }
    }

    /// <summary>回應內容由正式 Core 釋放；計數 callback 只活到當次 handler/fixture 結束。</summary>
    private sealed class CountedContent(Action disposed) : StringContent("{}")
    {
        /// <summary>標示 Dispose 呼叫；不建立額外串流或背景資源。</summary>
        protected override void Dispose(bool disposing) { if (disposing) disposed(); base.Dispose(disposing); }
    }
}
