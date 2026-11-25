/*
檔案整體說明
目的與定位：好牧人產品層 P7 代送註冊、scope隔離與停止排空的離線驗證，只替換SDK及HTTP底層。
主要流程：真 AddGoodShepherdLineWebhook 組合root，驗嚴格開關、固定查詢allowlist；真正handler透過
MemberNotifyRelay組字與解密，交錯兩教會及停止中傳輸用決定性同步點驗scope未提早Dispose。
故障注入包含缺key及無效布林，斷言零推播、真狀態及全部SDK／client清理回零；不連外網。
資源與隔離：每案獨佔容器、記憶體組態、合成金鑰、HTTP及SDK計數，finally排空Task後Dispose。
沒有Session／跨案例static可變狀態；這些替身測試不能代替goodshepherdback實機送達。
*/
using System.Collections.Concurrent;
using System.Net;
using Line.Messaging;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessage.Hosting.Outbound;
using SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Tests.Webhooks;

/// <summary>真代送worker組合與每筆scope生命週期契約。</summary>
public sealed class MemberNotifyRelayRegistrationTests
{
    /// <summary>缺省／false均沒有代送factory或worker；對照true必須註冊一個。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void RelayWorker_Disabled_NotRegistered(string? flag)
    {
        using var f = new Fixture(flag);
        Assert.Null(f.Provider.GetService<MemberNotifyRelayHostFactory>());
        Assert.Empty(f.Provider.GetServices<IHostedService>().OfType<OutboundTaskWorker>());
        using var enabled = new Fixture("true");
        Assert.NotNull(enabled.Provider.GetRequiredService<MemberNotifyRelayHostFactory>());
        Assert.Single(enabled.Provider.GetServices<IHostedService>().OfType<OutboundTaskWorker>());
    }
    /// <summary>錯值及物件型開關不能默認false，需在組建時拒絕。</summary>
    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    public void RelayWorker_InvalidFlag_RejectsStartup(string flag) =>
        Assert.Throws<InvalidOperationException>(() => new Fixture(flag));
    /// <summary>真store只查第四類且每操作使用新SDK，回傳後scope歸零。</summary>
    [Fact]
    public void RelayWorker_ClaimsOnlyRelayType()
    {
        using var f = new Fixture("true");
        var store = f.Provider.GetRequiredService<MemberNotifyRelayWorkStore>();
        Assert.Empty(store.ReadCandidates(DateTime.UtcNow, 50, CancellationToken.None));
        Assert.Equal(100000003, Assert.Single(f.Owner.QueriedTypes)); Assert.Equal(0, f.Owner.Active);
    }
    /// <summary>缺key於推播之前失敗，finally仍釋放handler scope。</summary>
    [Fact]
    public async Task RelayHandler_MissingTokenKey_FailsClosedNoPush()
    {
        using var f = new Fixture("true", false);
        var factory = f.Provider.GetRequiredService<MemberNotifyRelayHostFactory>();
        await using (var lease = await factory.OpenAsync(f.Item(f.Owner.A), CancellationToken.None))
            await Assert.ThrowsAsync<InvalidOperationException>(() => lease.Handler.ExecuteAsync(CancellationToken.None));
        Assert.Equal(0, f.Owner.Active); Assert.Empty(f.Owner.Pushed);
    }
    /// <summary>A/B/A每筆新scope，真正fee組字的to及retry只屬當次來源。</summary>
    [Fact]
    public async Task RelayHandler_TwoChurchesInterleaved_ScopesNotShared()
    {
        using var f = new Fixture("true"); var factory = f.Provider.GetRequiredService<MemberNotifyRelayHostFactory>();
        foreach (var church in new[] { f.Owner.A, f.Owner.B, f.Owner.A })
        {
            await using (var lease = await factory.OpenAsync(f.Item(church), CancellationToken.None))
                await lease.Handler.ExecuteAsync(CancellationToken.None);
            Assert.Equal(0, f.Owner.Active);
        }
        Assert.Equal(new[] { f.Owner.A, f.Owner.B, f.Owner.A }, f.Owner.Pushed.ToArray());
        Assert.Equal(3, f.Owner.Disposed); Assert.Equal(0, f.Owner.Active);
    }
    /// <summary>worker停止時傳輸尚未完成則scope仍存活，釋放同步點後排空再Dispose且只完成一次。</summary>
    [Fact]
    public async Task RelayHandler_StopDuringSend_DrainsAndDisposesScope()
    {
        using var f = new Fixture("true"); f.Owner.Block = true;
        var store = new SingleStore(f.Item(f.Owner.A));
        using var worker = new OutboundTaskWorker(store, f.Provider.GetRequiredService<MemberNotifyRelayHostFactory>(),
            (_, _, _) => { }, options: new OutboundTaskWorkerOptions(TimeSpan.FromHours(1), 1, TimeSpan.FromSeconds(5)));
        try
        {
            await worker.StartAsync(CancellationToken.None); await f.Owner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var stopping = worker.StopAsync(CancellationToken.None); Assert.False(stopping.IsCompleted); Assert.Equal(1, f.Owner.Active);
            f.Owner.Release.TrySetResult(); await stopping.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, f.Owner.Active); Assert.Equal(1, f.Owner.Disposed); Assert.Equal(1, store.Completed);
        }
        finally { f.Owner.Release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
    }
    /// <summary>429/503及無回應故障經真handler與worker，應排重試並保留固定分類，不能假完成。</summary>
    [Theory]
    [InlineData(429, "Http429")]
    [InlineData(503, "Http5xx")]
    [InlineData(0, "Transport")]
    public async Task RelayHandler_TransientFailure_WorkerSchedulesRetry(int status, string error)
    {
        using var f = new Fixture("true"); f.Owner.Status = status;
        var store = new SingleStore(f.Item(f.Owner.A)) { ExpectedStatus = OutboundWorkStatus.Pending };
        using var worker = new OutboundTaskWorker(store, f.Provider.GetRequiredService<MemberNotifyRelayHostFactory>(), (_, _, _) => { },
            options: new OutboundTaskWorkerOptions(TimeSpan.FromHours(1), 1, TimeSpan.FromSeconds(5)));
        try
        {
            await worker.StartAsync(CancellationToken.None); await store.Settled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await worker.StopAsync(CancellationToken.None); Assert.Equal(error, store.Error); Assert.Equal(1, store.Attempts);
            Assert.Equal(0, f.Owner.Active); Assert.Equal(1, f.Owner.Disposed);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }
    /// <summary>百筆交錯通知逐筆排空，確定scope存活數與Dispose計數每輪回基線，沒有跨教會保留SDK。</summary>
    [Fact]
    public async Task RelayHandler_HundredAlternatingScopes_ReturnToBaseline()
    {
        using var f = new Fixture("true"); var factory = f.Provider.GetRequiredService<MemberNotifyRelayHostFactory>();
        for (var index = 0; index < 100; index++)
        {
            var church = index % 2 == 0 ? f.Owner.A : f.Owner.B;
            await using (var lease = await factory.OpenAsync(f.Item(church), CancellationToken.None))
                await lease.Handler.ExecuteAsync(CancellationToken.None);
            Assert.Equal(0, f.Owner.Active); Assert.Equal(index + 1, f.Owner.Disposed);
        }
        Assert.Equal(100, f.Owner.Pushed.Count);
    }
    /// <summary>每案容器與純值owner，不啟動正式Webhook SQL／queue。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly Owner Owner = new();
        internal readonly ServiceProvider Provider;
        private readonly IConfigurationRoot config;
        /// <summary>真註冊保留預設組合，僅用窄SDK及HTTP替身消除外部I/O。</summary>
        internal Fixture(string? flag, bool key = true)
        {
            config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:NotifyRelay:Enabled"] = flag,
                ["Deployment:Organizations:synthetic:TokenKey"] = key ? Convert.ToBase64String(Owner.Key) : null
            }).Build();
            try
            {
                var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(config); services.AddLogging();
                services.AddScoped<IOrganizationService>(_ => new Sdk(Owner));
                services.AddSingleton<IWebhookEventDeduplicator>(new UnusedDedupe());
                services.AddSingleton<IWebhookEventQueue>(new UnusedQueue());
                services.AddGoodShepherdLineWebhook(config);
                services.AddHttpClient("MemberNotifyRelay").ConfigurePrimaryHttpMessageHandler(() => new Http(Owner));
                Provider = services.BuildServiceProvider(validateScopes: true);
            }
            catch { ((IDisposable)config).Dispose(); Owner.Dispose(); throw; }
        }
        /// <summary>四欄payload與正式教會分區事件鍵，不以測試索引代替資料列ID。</summary>
        internal OutboundWorkItem Item(Guid church) => new(Guid.NewGuid(), "1", church, null, OutboundWorkType.MemberNotifyRelay,
            new JObject { ["kind"] = 3, ["recordId"] = church, ["message"] = 1, ["route"] = 0 }.ToString(), $"{church:N}-100000003-{church:N}", OutboundWorkStatus.Pending, 0, null, null);
        /// <summary>先容器排空與釋放client，再清合成key及owner，沒有未完成Task。</summary>
        public void Dispose() { Provider.Dispose(); ((IDisposable)config).Dispose(); Owner.Dispose(); }
    }
    /// <summary>不啟動Webhook的離線組合只排除SQLcleanup owner，任何去重呼叫直接失敗。</summary>
    private sealed class UnusedDedupe : IWebhookEventDeduplicator
    {
        /// <summary>本測試不接事件，不提供假的正式去重能力。</summary>
        public ValueTask<IWebhookEventRegistration?> TryRegisterAsync(WebhookEnvelope envelope, CancellationToken ct) => throw new NotSupportedException();
    }
    /// <summary>只排除不在本測試範圍的Webhook queue owner，任何事件交接均明確失敗。</summary>
    private sealed class UnusedQueue : IWebhookEventQueue
    {
        /// <summary>代送工作不走入站queue，意外入場直接拒絕，不靜默吞事件。</summary>
        public ValueTask<bool> TryEnqueueAsync(WebhookEnvelope envelope, IWebhookEventRegistration registration, CancellationToken ct) => throw new NotSupportedException();
    }
    /// <summary>有界案例計數與傳輸同步點，不存SDK或憑證到static。</summary>
    private sealed class Owner : IDisposable
    {
        internal readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), Org = Guid.NewGuid();
        internal readonly byte[] Key = Enumerable.Range(0, 64).Select(x => (byte)x).ToArray();
        internal int Active, Disposed; internal bool Block; internal int Status = 200;
        internal readonly ConcurrentQueue<int> QueriedTypes = new(); internal readonly ConcurrentQueue<Guid> Pushed = new();
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>所有呼叫排空後清除純值及金鑰，解開同步點避免失敗案例留Task。</summary>
        public void Dispose() { Release.TrySetResult(); Array.Clear(Key); QueriedTypes.Clear(); Pushed.Clear(); }
    }
    /// <summary>只支援真fee通知所需的最小SDK，資源計數由DI scope唯一Dispose。</summary>
    private sealed class Sdk(Owner owner) : IOrganizationService, IDisposable
    {
        private readonly Owner owner = Open(owner); private bool disposed;
        /// <summary>每次scope獨佔新SDK，計數不得借到其他scope。</summary>
        private static Owner Open(Owner owner) { Interlocked.Increment(ref owner.Active); return owner; }
        /// <summary>來源feeId與church同為合成正式ID；所有欄位由明確白名單表提供。</summary>
        public Entity Retrieve(string table, Guid id, ColumnSet cols) => table switch
        {
            "new_fee" => new Entity(table, id) { ["new_contact_new_fee"] = new EntityReference("contact", id), ["new_pay_date"] = new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc), ["new_fee_really_paid"] = new Money(100) },
            "contact" => new Entity(table, id) { ["parentcustomerid"] = new EntityReference("account", id), ["fullname"] = "合成", ["new_lineid"] = "U" + id.ToString("N") },
            "account" => new Entity(table, id) { ["name"] = "合成", ["new_linechannelid"] = new EntityReference("new_linechannel", id) },
            "new_linechannel" => new Entity(table, id) { ["new_accesstokenencrypted"] = ChannelTokenProtector.Protect("synthetic", owner.Key) },
            _ => throw new InvalidOperationException("Unexpected SDK table.")
        };
        /// <summary>只接受固定allowlist的待辦查詢，不返回假工作讓worker誤執行。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = Assert.IsType<QueryExpression>(query); Assert.Equal("new_outboundtask", q.EntityName);
            var filter = Assert.Single(q.Criteria.Conditions); Assert.Equal("new_type", filter.AttributeName);
            owner.QueriedTypes.Enqueue((int)Assert.Single(filter.Values)); return new EntityCollection();
        }
        /// <summary>server組織名稱與WhoAmI使用同正式ID，不接受payload指定組織。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveCurrentOrganizationRequest) return new RetrieveCurrentOrganizationResponse { Results = { ["Detail"] = new Microsoft.Xrm.Sdk.Organization.OrganizationDetail { OrganizationId = owner.Org, UniqueName = "synthetic" } } };
            if (request is WhoAmIRequest) return new WhoAmIResponse { Results = { ["OrganizationId"] = owner.Org } };
            throw new NotSupportedException();
        }
        /// <summary>沒有資料建立權限，誤入立即失敗。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>fee通知不寫來源資料，誤入立即失敗。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>不授刪除，避免測試清理掩蓋產品副作用。</summary>
        public void Delete(string table, Guid id) => throw new NotSupportedException();
        /// <summary>沒有關聯變更功能。</summary>
        public void Associate(string table, Guid id, Relationship rel, EntityReferenceCollection refs) => throw new NotSupportedException();
        /// <summary>沒有關聯移除功能。</summary>
        public void Disassociate(string table, Guid id, Relationship rel, EntityReferenceCollection refs) => throw new NotSupportedException();
        /// <summary>DI唯一owner冪等清理，計數回零後不得仍有執行中通知。</summary>
        public void Dispose() { if (disposed) return; disposed = true; Interlocked.Decrement(ref owner.Active); Interlocked.Increment(ref owner.Disposed); }
    }
    /// <summary>只替換LINE最底層HTTP，不替代真正組字／解密／Retry Key。</summary>
    private sealed class Http(Owner owner) : HttpMessageHandler
    {
        /// <summary>讀真to識別並由同步點控制完成，返回真200模型但沒有網路。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = JObject.Parse(await request.Content!.ReadAsStringAsync(ct)); var to = (string)json["to"]!;
            owner.Pushed.Enqueue(Guid.ParseExact(to.Substring(1), "N")); owner.Entered.TrySetResult();
            if (owner.Block) await owner.Release.Task.WaitAsync(ct);
            if (owner.Status == 0) throw new HttpRequestException("Synthetic no-response transport failure.");
            return new HttpResponseMessage((HttpStatusCode)owner.Status);
        }
    }
    /// <summary>只提供一筆已核定候選，CAS於本案鎖內一次認領，用來驅動真worker停止流程。</summary>
    private sealed class SingleStore(OutboundWorkItem item) : IOutboundWorkStore
    {
        private int claimed; internal int Completed; internal int Attempts; internal string? Error;
        internal OutboundWorkStatus ExpectedStatus = OutboundWorkStatus.Completed;
        internal readonly TaskCompletionSource Settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>完整候選只有一筆，沒有重複身分或跨教會集合。</summary>
        public IReadOnlyList<OutboundWorkItem> ReadCandidates(DateTime now, int max, CancellationToken ct) => [item];
        /// <summary>只有第一個owner可認領，版本更新為2。</summary>
        public OutboundWorkItem? TryClaim(OutboundWorkItem row, DateTime now, CancellationToken ct) => Interlocked.Exchange(ref claimed, 1) == 0 ? row with { RowVersion = "2", Status = OutboundWorkStatus.Processing, ClaimedOn = now } : null;
        /// <summary>決定性核對成功結算一次，錯誤分類不允許測試當成功。</summary>
        public bool TrySettle(OutboundWorkItem row, OutboundWorkStatus status, int attempts, DateTime now, DateTime? next, string? error, CancellationToken ct) { Assert.Equal(ExpectedStatus, status); Attempts = attempts; Error = error; Interlocked.Increment(ref Completed); Settled.TrySetResult(); return true; }
    }
}
