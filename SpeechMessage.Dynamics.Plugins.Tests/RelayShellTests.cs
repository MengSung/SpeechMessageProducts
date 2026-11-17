/*
 * 檔案整體說明
 * 目的與定位：P7真活動與Plugin的Live入列離線測試；保護不取token、不送HTTP、
 * 只建立四欄代送待辦，付款仍在查詢前拒絕。使用最小合成SDK與真WorkflowInvoker。
 * 主要流程：三活動各自求值參數及平台識別，驗證待辦類型、來源教會及payload封閉；
 * OperationId優先及CorrelationId退路分開測。外送仍沿正式活動來源，不使用收件者授權。
 * 資源與隔離：每案唯一擁有SDK、WF mock、追蹤與集合，Invoke同步排空後Dispose清除；
 * CAS另用手寫服務執行Enqueue，禁止Moq／HTTP／私有反射，父測試finally卸載受限域。
 * 本檔無秘密或實機存取，不把離線寫入替身的結果說成D365或LINE送達。
 */
extern alias Workflows;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessage.Dynamics.Plugins.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using Xunit;
using Moq;
using LessonActivity = Workflows::SpeechMessage.Dynamics.Workflows.Lessons.StorLessonSigningActivity;
using AppointmentActivity = Workflows::SpeechMessage.Dynamics.Workflows.Appointments.AppointmentSigningActivity;
using FeeActivity = Workflows::SpeechMessage.Dynamics.Workflows.Fees.FeeNotifyActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>真外殼Live只入列，來源及分派不可帶入文字或credential。</summary>
public sealed class RelayShellTests
{
    /// <summary>三活動完整執行，只寫一筆待辦，SDK白名單不允許token或頻道。</summary>
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Activity_Live_EnqueuesRelay_NoTokenNoPush(int kind) => RunActivity(kind);
    /// <summary>保留付款分支拒絕且零讀寫，不以代送掩蓋缺口。</summary>
    [Fact]
    public void LessonSigningActivity_Live_PaymentBranch_FailsClosed()
    {
        using var f = new Fixture(1);
        var failure = Assert.ThrowsAny<Exception>(() => f.Invoke("付款"));
        Assert.IsType<InvalidPluginExecutionException>(failure.GetBaseException());
        Assert.Empty(f.Sdk.Calls);
    }
    /// <summary>外送Plugin只讀活動來源、入列，不查收件者頻道或token。</summary>
    [Fact]
    public void OutboundLetterPlugin_Live_EnqueuesRelay_NoTokenNoPush()
    {
        using var f = new Fixture(4); f.Plugin(); f.Check(4);
    }
    /// <summary>最小受限域真正執行Core來源推導、Writer及SDK入列，不授網路或反射權限。</summary>
    [Fact]
    public void Relay_PartialTrust_EnqueueChainWorks()
    {
        var root = AppDomain.CurrentDomain.BaseDirectory; var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        var sdk = typeof(IPlugin).Assembly.Evidence.GetHostEvidence<StrongName>(); Assert.NotNull(sdk);
        var domain = AppDomain.CreateDomain("P7-Relay-" + Guid.NewGuid().ToString("N"),null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile },permissions,sdk);
        try
        {
            var probe = (RelayPartialTrustProbe)domain.CreateInstanceAndUnwrap(typeof(RelayPartialTrustProbe).Assembly.FullName,typeof(RelayPartialTrustProbe).FullName);
            Assert.Equal("PASS",probe.Run());
        }
        finally { AppDomain.Unload(domain); }
    }
    /// <summary>舊拒絕測試依D7-24核定改成此共用真外殼檢查，不能改其他期望。</summary>
    internal static void RunActivity(int kind, string? mode = "Live", string message = "ExecuteWorkflow", string? parameter = null)
    {
        using var f = new Fixture(kind); f.Invoke(parameter ?? (kind == 1 ? "課程簽核" : "工作流程"), message, mode); f.Check(kind);
    }
    /// <summary>操作owner只提供來源最小欄位，不建立頻道或通知器。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeOrganizationService Sdk = new();
        private readonly FakeTracingService trace = new();
        private readonly Guid record = Guid.NewGuid(), member = Guid.NewGuid(), church = Guid.NewGuid();
        private readonly int kind;
        private readonly List<Entity> writes = new();
        private readonly Moq.Mock<IOrganizationServiceFactory> factory = new(Moq.MockBehavior.Strict);
        /// <summary>來源查詢與Create白名單明確，不提供寬鬆未知資料fallback。</summary>
        internal Fixture(int kind)
        {
            this.kind = kind;
            Sdk.RetrieveHandler = (table,id,cols) =>
            {
                if (table == "contact" && id == member && cols.Columns.Count == 1 && cols.Columns[0] == "parentcustomerid")
                    return new Entity(table,id) { ["parentcustomerid"] = new EntityReference("account",church) };
                if (id != record) throw new InvalidOperationException("Unexpected source.");
                if (table == "letter") return new Entity(table,id) { ["directioncode"] = true, ["statecode"] = new OptionSetValue(0),
                    ["from"] = new EntityCollection(new[] { new Entity("activityparty", Guid.NewGuid()) { ["partyid"] = new EntityReference("account",church) } }) };
                var field = kind == 1 ? "new_contact_new_stor_lessons" : kind == 2 ? "new_applier_appointment" : "new_contact_new_fee";
                return new Entity(table,id) { [field] = new EntityReference("contact",member) };
            };
            Sdk.CreateHandler = row => { writes.Add(row); return Guid.NewGuid(); };
            factory.Setup(x => x.CreateOrganizationService(Moq.It.IsAny<Guid?>())).Returns(Sdk);
        }
        /// <summary>真WF引數於當次求值，OperationId空時沿既有CorrelationId。</summary>
        internal void Invoke(string parameter, string message = "ExecuteWorkflow", string? mode = "Live")
        {
            CodeActivity activity = kind == 1 ? new LessonActivity() : kind == 2 ? new AppointmentActivity() : new FeeActivity();
            var context = new FakeWorkflowContext(messageName:message,primaryEntityName:kind == 1 ? "new_stor_lessons" : kind == 2 ? "appointment" : "new_fee",primaryEntityId:record);
            try
            {
                var invoker = new WorkflowInvoker(activity); invoker.Extensions.Add<IWorkflowContext>(() => context.Object);
                invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory.Object); invoker.Extensions.Add<ITracingService>(() => trace);
                invoker.Invoke(new Dictionary<string,object> { ["InputParameter"] = parameter, ["ExecutionMode"] = mode! },TimeSpan.FromSeconds(10));
            }
            finally { context.Mock.Reset(); }
        }
        /// <summary>真外送Plugin沿非同步PostOperation契約執行。</summary>
        internal void Plugin()
        {
            var context = new RemoteExecutionContext { PrimaryEntityId=record,PrimaryEntityName="letter",MessageName="Create",Stage=40,Mode=1,Depth=1,UserId=Guid.NewGuid(),OrganizationId=Guid.NewGuid(),CorrelationId=Guid.NewGuid() };
            new OutboundLetterPlugin("{\"mode\":\"Live\"}","").Execute(new FakeServiceProvider().Add<IPluginExecutionContext>(context).Add<IOrganizationServiceFactory>(factory.Object).Add<ITracingService>(trace));
        }
        /// <summary>完整四欄及型別、來源scope、零Push／Update，不能只檢查有Create。</summary>
        internal void Check(int expectedKind)
        {
            var row = Assert.Single(writes); Assert.Equal("new_outboundtask",row.LogicalName);
            Assert.Equal(100000003,row.GetAttributeValue<OptionSetValue>("new_type").Value);
            Assert.Equal(church,row.GetAttributeValue<EntityReference>("new_accountid").Id);
            var payload=JObject.Parse(row.GetAttributeValue<string>("new_payload")); Assert.Equal(4,payload.Count);
            Assert.Equal(expectedKind,(int)payload["kind"]!); Assert.Equal(record,(Guid)payload["recordId"]!);
            Assert.DoesNotContain(trace.Entries,x=>x.Contains("Push")); Assert.False(Sdk.Calls.ContainsKey("Update"));
        }
        /// <summary>同步排空後清除所有捕捉參照與SDK寫入，沒有跨案例共享資料。</summary>
        public void Dispose() { Sdk.Dispose(); writes.Clear();trace.Entries.Clear();factory.Reset(); }
    }
}

/// <summary>一次性受限域探針；只回固定結果，不交換SDK、來源或憑證。</summary>
public sealed class RelayPartialTrustProbe : MarshalByRefObject
{
    private bool ran;
    /// <summary>確認產品仍受限後使用手寫SDK執行真正入列全鏈，沒有動態代理。</summary>
    public string Run()
    {
        if (ran) return "REUSED";
        ran = true;
        if (AppDomain.CurrentDomain.IsFullyTrusted || typeof(MemberNotifyRelay).Assembly.IsFullyTrusted) return "UNRESTRICTED";
        using var sdk = new Fakes.FakeOrganizationService();
        var record=Guid.NewGuid();var church=Guid.NewGuid(); Entity? created=null;
        sdk.RetrieveHandler=(table,id,cols)=>new Entity("letter",id) { ["directioncode"]=true,["statecode"]=new OptionSetValue(0),["from"]=new EntityCollection(new[] { new Entity("activityparty", Guid.NewGuid()) { ["partyid"]=new EntityReference("account",church) } }) };
        sdk.CreateHandler=row=> { created=row;return Guid.NewGuid(); };
        MemberNotifyRelay.Enqueue(sdk,MemberNotifyRelayKind.OutboundLetter,record,2,0,Guid.Empty);
        return created is not null && created.GetAttributeValue<OptionSetValue>("new_type").Value==100000003 ? "PASS" : "NO_TASK";
    }
}
