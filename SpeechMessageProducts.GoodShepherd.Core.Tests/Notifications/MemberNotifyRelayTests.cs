/*
 * 檔案整體說明
 * 目的與定位：P7 代送的雙目標離線契約，直接執行產品 Relay、通知與 HTTP 序列化。
 * 主要流程：合成 SDK 提供正式來源關聯及加密測試頻道，資料庫唯一鍵衝突由替身明確
 * 回報；驗證同執行個體防重、不同執行個體分離、來源教會、完整訊息與既有 Live 相同。
 * 故障注入涵蓋未知欄位／種類、錯教會、429、交錯教會，決定性斷言核對零解密前置、
 * 真請求 JSON、Retry Key 與外送狀態；未連線 D365 或 LINE，不冒稱實機送達。
 * 資源與隔離：每案 Fixture 唯一擁有 HTTP client、handler、合成金鑰與 SDK 集合，
 * await 返回後 Dispose 清空 key／本文／資料；無共用可變狀態或背景工作。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessageProducts.GoodShepherd.Core.Appointments;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Notifications;

/// <summary>以操作局部替身保護代送入列、重新授權與傳輸分類。</summary>
public sealed class MemberNotifyRelayTests
{
    /// <summary>資料庫唯一鍵重跑回原正式待辦，不覆寫已有狀態。</summary>
    [Fact]
    public void Relay_Enqueue_SameInstanceTwice_OneTask()
    {
        using var f = new Fixture(); var instance = Guid.NewGuid();
        var a = f.Enqueue(instance); var b = f.Enqueue(instance);
        Assert.Equal(a, b); Assert.Single(f.Tasks);
    }
    /// <summary>不同流程實例即使來源相同也保留兩筆事件。</summary>
    [Fact]
    public void Relay_Enqueue_DifferentInstance_TwoTasks()
    {
        using var f = new Fixture(); f.Enqueue(Guid.NewGuid()); f.Enqueue(Guid.NewGuid());
        Assert.Equal(2, f.Tasks.Count);
    }
    /// <summary>來源申請人是A，簽核者是B；入列仍屬A且不讀頻道。</summary>
    [Fact]
    public void Relay_Enqueue_ChurchFromSourceRecord_NotFromRecipient()
    {
        using var f = new Fixture(); f.Rows[("contact", f.Signer)]["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
        f.Enqueue(Guid.NewGuid());
        Assert.Equal(f.Church, Assert.Single(f.Tasks).Value.GetAttributeValue<EntityReference>("new_accountid").Id);
        Assert.Equal(0, f.ChannelReads);
    }
    /// <summary>合法控制組完成，再注入未知種類／多欄，應於任何SDK或HTTP之前拒絕。</summary>
    [Theory]
    [InlineData("kind")]
    [InlineData("extra")]
    public async Task Relay_Execute_UnknownKindOrExtraField_FailsClosed(string fault)
    {
        using var f = new Fixture(); var payload = JObject.Parse(f.Payload(2));
        if (fault == "kind") payload["kind"] = 99; else payload["text"] = "forbidden";
        await Assert.ThrowsAnyAsync<ArgumentException>(() => MemberNotifyRelay.ExecuteAsync(f, f.Push, f.Church, payload.ToString(), f.Key, CancellationToken.None));
        Assert.Equal(0, f.Reads); Assert.Empty(f.Bodies);
    }
    /// <summary>來源教會與待辦不同時不讀頻道、不解密，即使key無效也正常略過。</summary>
    [Fact]
    public async Task Relay_Execute_RecordOtherChurch_NoPushNoDecrypt()
    {
        using var f = new Fixture();
        await MemberNotifyRelay.ExecuteAsync(f, f.Push, Guid.NewGuid(), f.Payload(2), Array.Empty<byte>(), CancellationToken.None);
        Assert.Equal(0, f.ChannelReads); Assert.Empty(f.Bodies);
    }
    /// <summary>同合成來源經原入口與代送路徑，完整HTTP JSON及重試鍵相同。</summary>
    [Fact]
    public async Task Relay_Execute_AppointmentSigning_SameMessageAsPluginLive()
    {
        using var f = new Fixture();
        await AppointmentSigningRule.NotifyAsync(f, f.Push, f.Record, f.Organization, "ExecuteWorkflow", "工作流程", null, f.Key, CancellationToken.None);
        await f.Execute(2);
        Assert.Equal(2, f.Bodies.Count); Assert.Equal(f.Bodies[0], f.Bodies[1]); Assert.Equal(f.Keys[0], f.Keys[1]);
    }
    /// <summary>課程分派與原Live共用訊息、收件人及事件鍵，不能另造第二套格式化。</summary>
    [Fact]
    public async Task Relay_Execute_LessonSigning_SameMessageAsPluginLive()
    {
        using var f = new Fixture();
        await LessonSigningDispatch.NotifyAsync(f, f.Push, f.Record, f.Organization, "Create", LessonSigningDispatch.DefaultParameter("Create"), null, f.Key, CancellationToken.None);
        await f.Execute(1, 2, 1);
        Assert.Equal(2, f.Bodies.Count); Assert.Equal(f.Bodies[0], f.Bodies[1]); Assert.Equal(f.Keys[0], f.Keys[1]);
    }
    /// <summary>繳費通知直接用feeId，不以待辦或執行個體作推播重試鍵。</summary>
    [Fact]
    public async Task Relay_Execute_FeeNotify_RetryKeyIsFeeId()
    {
        using var f = new Fixture(); await f.Execute(3, 1, 0);
        Assert.Equal(f.Record.ToString("D"), Assert.Single(f.Keys)); Assert.Single(f.Bodies);
    }
    /// <summary>外送用activityId，成功回寫完成1/4且保留原description。</summary>
    [Fact]
    public async Task Relay_Execute_OutboundLetter_RetryKeyIsActivityId_StatusWrittenBack()
    {
        using var f = new Fixture(); await f.Execute(4, 2, 0);
        Assert.Equal(f.Record.ToString("D"), Assert.Single(f.Keys));
        var patch = Assert.Single(f.Updates); Assert.Equal(1, patch.GetAttributeValue<OptionSetValue>("statecode").Value);
        Assert.Equal(4, patch.GetAttributeValue<OptionSetValue>("statuscode").Value); Assert.False(patch.Contains("description"));
    }
    /// <summary>429不能當永久成功，須丟可供工作者重試的真HTTP例外。</summary>
    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Relay_Execute_Retryable429_Throws(int status)
    {
        using var f = new Fixture { Status = (HttpStatusCode)status };
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => f.Execute(2)); Assert.Single(f.Bodies);
    }
    /// <summary>A/B/A交錯完成，每次的收件者與加密頻道僅屬該案，A完整結果一致。</summary>
    [Fact]
    public async Task Relay_Execute_InterleavedChurches_NoStateShared()
    {
        using var a = new Fixture(); using var b = new Fixture();
        await a.Execute(2); await b.Execute(2); await a.Execute(2);
        Assert.Equal(a.Bodies[0], a.Bodies[1]); Assert.NotEqual(a.Keys[0], b.Keys[0]);
        Assert.Equal(2, a.ChannelReads); Assert.Equal(1, b.ChannelReads);
    }

    /// <summary>單案SDK與HTTP owner；唯一鍵衝突用正式SDK fault，不在產品外模擬Relay邏輯。</summary>
    private sealed class Fixture : HttpMessageHandler, IOrganizationService
    {
        internal readonly Guid Record = Guid.NewGuid(), Church = Guid.NewGuid(), Member = Guid.NewGuid(), Signer = Guid.NewGuid(), Channel = Guid.NewGuid(), Organization = Guid.NewGuid();
        internal readonly byte[] Key = Enumerable.Range(0,64).Select(x => (byte)x).ToArray();
        internal readonly Dictionary<(string, Guid), Entity> Rows = new();
        internal readonly Dictionary<string, Entity> Tasks = new(StringComparer.Ordinal);
        internal readonly List<Entity> Updates = new();
        internal readonly List<string> Bodies = new(), Keys = new();
        internal int Reads, ChannelReads;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        private readonly HttpClient client;
        internal readonly LinePushClient Push;
        /// <summary>合成資料保留正式lookup與不同簽核者，頻道只用測試加密封包。</summary>
        internal Fixture()
        {
            client = new HttpClient(this, false); Push = new LinePushClient(client);
            Rows[("contact", Member)] = new Entity("contact", Member) { ["parentcustomerid"] = new EntityReference("account", Church), ["fullname"] = "學員", ["new_lineid"] = "U00000000000000000000000000000000", ["new_preferredlanguage"] = "zh-TW" };
            Rows[("contact", Signer)] = new Entity("contact", Signer) { ["parentcustomerid"] = new EntityReference("account", Church), ["fullname"] = "簽核者", ["new_lineid"] = "U11111111111111111111111111111111", ["new_preferredlanguage"] = "zh-TW" };
            Rows[("account", Church)] = new Entity("account", Church) { ["name"] = "合成範圍", ["new_linechannelid"] = new EntityReference("new_linechannel", Channel), ["new_defaultlanguage"] = "zh-TW", ["new_enabledlanguages"] = "zh-TW", ["new_timezone"] = "Asia/Taipei" };
            Rows[("new_linechannel", Channel)] = new Entity("new_linechannel", Channel) { ["new_accesstokenencrypted"] = ChannelTokenProtector.Protect("synthetic", Key) };
            Rows[("appointment", Record)] = new Entity("appointment", Record) { ["new_signing_contact"] = new EntityReference("contact", Signer), ["new_applier_appointment"] = new EntityReference("contact", Member), ["new_signing_content"] = "合成簽核", ["new_meeting_kind"] = new OptionSetValue(4) };
            Rows[("new_stor_lessons", Record)] = new Entity("new_stor_lessons", Record) { ["new_contact_new_stor_lessons"] = new EntityReference("contact", Member), ["new_new_disciple_lessons_new_stor_les"] = new EntityReference("new_disciple_lessons", Record) };
            Rows[("new_disciple_lessons", Record)] = new Entity("new_disciple_lessons", Record) { ["new_name"] = "課程", ["new_picture_link"] = "https://example.invalid/image.png" };
            Rows[("new_fee", Record)] = new Entity("new_fee", Record) { ["new_contact_new_fee"] = new EntityReference("contact", Member), ["new_pay_date"] = new DateTime(2026,11,17,0,0,0,DateTimeKind.Utc), ["new_fee_really_paid"] = new Money(100) };
            Rows[("letter", Record)] = new Entity("letter", Record) { ["directioncode"] = true, ["statecode"] = new OptionSetValue(0), ["description"] = "合成原文", ["from"] = Parties("account",Church), ["to"] = Parties("contact",Member) };
        }
        /// <summary>每次產生正式四欄payload，測試不保存任何秘密。</summary>
        internal string Payload(int kind, int message = 1, int route = 1) => new JObject { ["kind"] = kind, ["recordId"] = Record, ["message"] = message, ["route"] = route }.ToString();
        /// <summary>直接執行產品入口並等待所有處理排空。</summary>
        internal Task Execute(int kind, int message = 1, int route = 1) => MemberNotifyRelay.ExecuteAsync(this, Push, Church, Payload(kind,message,route), Key, CancellationToken.None);
        /// <summary>直接入列同來源的預約通知，資料庫防重由Create fault測試。</summary>
        internal Guid? Enqueue(Guid instance) => MemberNotifyRelay.Enqueue(this, MemberNotifyRelayKind.AppointmentSigning, Record, 1, 1, instance);
        /// <summary>活動party只包含正式lookup，沒有姓名去重。</summary>
        private static EntityCollection Parties(string table, Guid id) => new(new[] { new Entity("activityparty") { ["partyid"] = new EntityReference(table,id) } });
        /// <summary>按精確主鍵及欄位投影，計數證明拒絕時沒有頻道解密前讀取。</summary>
        public Entity Retrieve(string table, Guid id, ColumnSet columns)
        {
            Reads++; if (table == "new_linechannel") ChannelReads++;
            var source = Rows[(table,id)]; var copy = new Entity(table,id);
            foreach (var col in columns.Columns) if (source.Contains(col)) copy[col] = source[col];
            return copy;
        }
        /// <summary>替代鍵重複回正式fault，產品必須讀回核對，不能用每次新ID迴避。</summary>
        public Guid Create(Entity row)
        {
            Assert.Equal("new_outboundtask",row.LogicalName); var key = row.GetAttributeValue<string>("new_idempotencykey");
            if (Tasks.ContainsKey(key)) throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040237) });
            row.Id = Guid.NewGuid(); Tasks.Add(key,row); return row.Id;
        }
        /// <summary>只允許精確待辦鍵查回，其他query均視為未預期存取。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = Assert.IsType<QueryExpression>(query); Assert.Equal("new_outboundtask",q.EntityName);
            return new EntityCollection(new[] { Tasks[(string)Assert.Single(q.Criteria.Conditions).Values[0]] });
        }
        /// <summary>只回server組織ID；不讓payload充當組織授權。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Assert.IsType<WhoAmIRequest>(request); var result = new WhoAmIResponse(); result.Results["OrganizationId"] = Organization; return result;
        }
        /// <summary>記錄真狀態patch，不修改輸入來源掩蓋重讀差異。</summary>
        public void Update(Entity entity) => Updates.Add(entity);
        /// <summary>本案不授權刪資料，意外呼叫直接失敗。</summary>
        public void Delete(string table, Guid id) => throw new NotSupportedException();
        /// <summary>本案沒有關聯寫入，意外呼叫直接失敗。</summary>
        public void Associate(string table, Guid id, Relationship relationship, EntityReferenceCollection related) => throw new NotSupportedException();
        /// <summary>本案沒有關聯移除，意外呼叫直接失敗。</summary>
        public void Disassociate(string table, Guid id, Relationship relationship, EntityReferenceCollection related) => throw new NotSupportedException();
        /// <summary>只替換HTTP最底層，完整JSON及正式重試標頭仍由產品生成。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            #if NETFRAMEWORK
            ct.ThrowIfCancellationRequested();
            Bodies.Add(await request.Content!.ReadAsStringAsync());
#else
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
#endif
            Keys.Add(request.Headers.GetValues("X-Line-Retry-Key").Single()); return new HttpResponseMessage(Status);
        }
        /// <summary>呼叫排空後清除金鑰及操作資料，client不回頭Dispose同handler。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { client.Dispose(); Array.Clear(Key,0,Key.Length); Rows.Clear(); Tasks.Clear(); Updates.Clear(); Bodies.Clear(); Keys.Clear(); }
            base.Dispose(disposing);
        }
    }
}
