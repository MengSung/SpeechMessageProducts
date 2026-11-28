/*
檔案整體說明
目的與定位：F11 真 Gateway／DataverseSendRecordStore 整合的單案合成依賴，保護正式 ID、RowVersion、租戶名額鏈與資源歸還，不連 D365、SQL 或 LINE。
主要功能與流程：Moq 只取代 SDK 與固定憑證 executor，正式 membership／record adapter 仍執行 WhoAmI、已發布 key 查詢、Create 與 CAS。最多五百一十二筆合成紀錄，HTTP 由真 HttpClient 經觀測 handler 送入有界 barrier，保存純 correlation 與計數供決定性斷言。
使用邊界與維護：測試 owner 先排空 gateway，再關 client／transport、觀測 manager、diagnostics 與 Meter；沒有全域資料或真實 userId。假資料庫鎖只模擬資料庫原子性，不能當實機唯一索引或平台接受的證據。所有測試啟動的 Task 必須由案例 await。
*/
using System.Net;
using System.Globalization;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Gateway;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SpeechMessage.Hosting.Observability;
using SpeechMessageProducts.GoodShepherd2.Web.Observability;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>一個測試獨佔的真 F5 相依圖，只替換網路與資料庫最底層。</summary>
internal sealed class PushObservationFixture : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entity> records = new();
    private readonly HttpClient http;
    private readonly Mock<IOrganizationService> sdk = new(MockBehavior.Strict);
    internal Guid Organization { get; } = Guid.NewGuid();
    internal Guid Tenant { get; } = Guid.NewGuid();
    private Guid Slot { get; } = Guid.NewGuid();
    private Guid Channel { get; } = Guid.NewGuid();
    internal HostingMeters Meters { get; } = new();
    internal ExceptionDiagnostics Diagnostics { get; } = new(Path.GetTempPath(), outputOptions: new(false, false));
    internal ObservedDataverseConnectionManager Manager { get; }
    internal GoodShepherd2LinePushGateway Gateway { get; }
    internal ILineCredentialExecutor Credentials { get; }
    internal ControlledTransport Transport { get; } = new();
    internal int LeaseReleases;
    internal int LeaseAcquires;
    internal bool FailSdk { get; set; }
    internal PushScope Scope => new("GoodShepherd2", "2012804957", Tenant, "untrusted", null);
    internal static string User(int index) => "U" + index.ToString("x32", CultureInfo.InvariantCulture);

    /// <summary>只建立合成 SDK／transport，不建線；完整正式 metadata 讓真 store 能執行能力守門。</summary>
    internal PushObservationFixture()
    {
        var metadata = new EntityMetadata { LogicalName = "new_sendrecord" };
        metadata.GetType().GetProperty("IsOptimisticConcurrencyEnabled")!.SetValue(metadata, true);
        var key = new EntityKeyMetadata { LogicalName = "new_ak_sendrecord", KeyAttributes = ["new_batchkey"] };
        key.GetType().GetProperty("EntityKeyIndexStatus")!.SetValue(key, EntityKeyIndexStatus.Active);
        metadata.GetType().GetProperty("Keys")!.SetValue(metadata, new[] { key });
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns<OrganizationRequest>(request =>
        {
            lock (gate)
            {
                if (request is WhoAmIRequest)
                {
                    var result = new WhoAmIResponse();
                    result.Results["OrganizationId"] = Organization;
                    result.Results["UserId"] = Guid.Parse("11111111-1111-1111-1111-111111111111");
                    result.Results["BusinessUnitId"] = Guid.Parse("22222222-2222-2222-2222-222222222222");
                    return result;
                }
                if (request is RetrieveEntityRequest capability)
                {
                    if (FailSdk) throw new IOException("synthetic-sdk-failure");
                    Assert.False(capability.RetrieveAsIfPublished);
                    var result = new RetrieveEntityResponse(); result.Results["EntityMetadata"] = metadata; return result;
                }
                var update = Assert.IsType<UpdateRequest>(request);
                Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
                var row = records[update.Target.Id]; Assert.Equal(row.RowVersion, update.Target.RowVersion);
                foreach (var pair in update.Target.Attributes) row[pair.Key] = pair.Value;
                row.RowVersion = (int.Parse(row.RowVersion, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
                return new UpdateResponse();
            }
        });
        sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns<Entity>(entity =>
        {
            lock (gate)
            {
                Assert.True(records.Count < 512);
                Assert.DoesNotContain(records.Values, row => row.GetAttributeValue<string>("new_batchkey") == entity.GetAttributeValue<string>("new_batchkey"));
                var copy = Copy(entity); copy.Id = Guid.NewGuid(); copy["new_sendrecordid"] = copy.Id; copy.RowVersion = "1";
                records.Add(copy.Id, copy); return copy.Id;
            }
        });
        sdk.Setup(s => s.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
            .Returns<string, Guid, ColumnSet>((name, id, _) => Read(name, id));
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns<QueryBase>(query =>
        {
            var expression = Assert.IsType<QueryExpression>(query); Assert.Equal("contact", expression.EntityName);
            var users = expression.Criteria.Conditions.Single(c => c.AttributeName == "new_lineid").Values.Cast<string>();
            var result = new EntityCollection();
            foreach (var user in users)
            {
                var row = new Entity("contact", Guid.NewGuid()); row["contactid"] = row.Id;
                row["new_lineid"] = user; row["parentcustomerid"] = new EntityReference("account", Tenant);
                row["statecode"] = new OptionSetValue(0); result.Entities.Add(row);
            }
            return result;
        });
        var source = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        source.Setup(s => s.Acquire(It.IsAny<CancellationToken>())).Returns<CancellationToken>(ct =>
        {
            ct.ThrowIfCancellationRequested(); Interlocked.Increment(ref LeaseAcquires);
            var lease = new Mock<IClientLease>(MockBehavior.Strict); var disposed = 0;
            lease.SetupGet(l => l.Service).Returns(sdk.Object); lease.Setup(l => l.MarkFaulted());
            lease.Setup(l => l.Dispose()).Callback(() =>
            { if (Interlocked.Exchange(ref disposed, 1) == 0) Interlocked.Increment(ref LeaseReleases); });
            return lease.Object;
        });
        Manager = new(source.Object, Meters, Diagnostics);
        var credentials = new Mock<ILineCredentialExecutor>(MockBehavior.Strict);
        credentials.Setup(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<LineChannelCredential, CancellationToken, Task<LinePushResult>>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<LineChannelCredential, CancellationToken, Task<LinePushResult>>, CancellationToken>((channel, send, ct) =>
            { Assert.Equal("2012804957", channel); return send(new LineChannelCredential("synthetic-token"), ct); });
        http = new(new PushBatchTrackingHandler(Meters, Transport));
        Credentials = credentials.Object;
        Gateway = new(Organization, Manager, Credentials, new LinePushClient(http), Diagnostics);
    }

    /// <summary>返回脫離模型的列，名額與 Channel 以正式 GUID 關聯；不以 caller 品牌當權威。</summary>
    private Entity Read(string name, Guid id)
    {
        lock (gate)
        {
            if (name == "new_sendrecord") return Copy(records[id]);
            var row = new Entity(name, id); row[name + "id"] = id; row["statecode"] = new OptionSetValue(0);
            if (name == "account")
            {
                Assert.Equal(Tenant, id); row["name"] = "synthetic-tenant";
                row["new_istenant"] = new OptionSetValue(100000001); row["new_istesttenant"] = new OptionSetValue(100000001);
                row["new_channelslot"] = new EntityReference("new_channelslot", Slot);
            }
            else if (name == "new_channelslot")
            {
                Assert.Equal(Slot, id); row["new_tenant"] = new EntityReference("account", Tenant);
                row["new_channel"] = new EntityReference("new_linechannel", Channel); row["new_slotstatus"] = new OptionSetValue(100000001);
            }
            else
            {
                Assert.Equal("new_linechannel", name); Assert.Equal(Channel, id);
                row["new_product"] = "GoodShepherd2"; row["new_channelid"] = "2012804957";
                row["new_channelstatus"] = new OptionSetValue(100000001);
            }
            return row;
        }
    }

    /// <summary>測試只發布脫離字典的純字串 correlation，不讓案例修改 SDK 模型。</summary>
    internal string[] Correlations() { lock (gate) return records.Values.Select(r => r.GetAttributeValue<string>("new_correlationid")).ToArray(); }

    /// <summary>每次讀寫分離純值與 SDK mutable lookup／choice，避免替身偶然掩蓋共享狀態外洩。</summary>
    private static Entity Copy(Entity row)
    {
        var copy = new Entity(row.LogicalName, row.Id) { RowVersion = row.RowVersion };
        foreach (var pair in row.Attributes) copy[pair.Key] = pair.Value switch
        { EntityReference lookup => new EntityReference(lookup.LogicalName, lookup.Id), OptionSetValue choice => new OptionSetValue(choice.Value), _ => pair.Value };
        return copy;
    }

    /// <summary>先解除所有 barrier 並排空 gateway，再關借入依賴，最後檢查 Meter 仍能拒絕未歸還資源。</summary>
    public async ValueTask DisposeAsync()
    {
        Transport.Release.TrySetResult();
        await Gateway.DisposeAsync(); http.Dispose(); Manager.Dispose();
        await Diagnostics.DisposeAsync(); Meters.Dispose(); lock (gate) records.Clear();
    }

    /// <summary>單案無網路的 HTTP 替身；每批傳入與返回都經真觀測 handler，barrier 不建背景 worker。</summary>
    internal sealed class ControlledTransport : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Requests;
        internal int RequestDisposals;
        internal int ResponseDisposals;
        internal bool Delay { get; set; }
        internal bool Fail { get; set; }
        /// <summary>核對真 F5 固定路徑並尊重 caller 取消，不保存 request／token／body。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.line.me", request.RequestUri!.Host);
            request.Content = new RequestContent(request.Content!, () => Interlocked.Increment(ref RequestDisposals));
            Interlocked.Increment(ref Requests); Entered.TrySetResult();
            if (Delay) await Release.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new HttpRequestException("synthetic-http-failure");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ResponseContent(() => Interlocked.Increment(ref ResponseDisposals)) };
            response.Headers.Add("x-line-request-id", Guid.NewGuid().ToString("D")); return response;
        }
    }

    /// <summary>測試接管當次 request content，原 body 委派與清理仍各一次，不保存到測試結束後。</summary>
    private sealed class RequestContent(HttpContent content, Action disposed) : HttpContent
    {
        private int closed;
        /// <summary>只在真 HttpClient 需要 body 時委派，不自行讀取或記錄。</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => content.CopyToAsync(stream, context);
        /// <summary>保守回未知長度，避免測試 wrapper 在長度計算中遞迴。</summary>
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        /// <summary>唯一 request 清理釋放來源 body，再通知純計數，重入不重複 Dispose。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref closed, 1) == 0)
            { try { content.Dispose(); } finally { disposed(); } }
            base.Dispose(disposing);
        }
    }

    /// <summary>測試 response body 的唯一清理票據，只有回應確定釋放才遞增計數。</summary>
    private sealed class ResponseContent(Action disposed) : StringContent("{}")
    {
        private int closed;
        /// <summary>完成基底 body／stream 清理後才回報，重入不重複計數。</summary>
        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally { if (disposing && Interlocked.Exchange(ref closed, 1) == 0) disposed(); }
        }
    }
}
