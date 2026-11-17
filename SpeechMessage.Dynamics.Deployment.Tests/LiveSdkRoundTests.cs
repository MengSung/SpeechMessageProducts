/*
檔案整體說明
目的與定位：Live SDK round 的離線整合測試，直接操作真 owner 及案例獨占記憶體 SDK，沒有真連線。
主要流程：同步 UpdateRequest 完成後讀實際欄位，證明不查 trace、不切步驟；trigger 建立
待辦時以本輪 GUID payload 配對，驗證其他同教會待辦不被清除。Create 回應不明仍清理。
資源與隔離：每案 fixture、GUID 及 round 完全獨立，finally 清理 owned 列後 Dispose；
沒有 HTTP、秘密或程序背景工作。processing 待辦故障須保留父列而不是破壞工作者資料。
決定性斷言：觀察讀回 score、正式待辦數、cleanup 後 owned=0、無關待辦仍在，不以替身比較結果自證。
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>真 Live SDK 能力接線及所有權測試，不冒稱遠端演練。</summary>
public sealed class LiveSdkRoundTests
{
    private static readonly string[] Types = { "Synthetic.NewPlugin" };
    private static readonly string[] ScoreFields = { "score" };
    /// <summary>同步返回即可觀察，不依賴 trace；cleanup 真正刪除本輪資料。</summary>
    [Fact]
    public void LiveSdk_SynchronousCompletion_DoesNotReadTraceOrSwitch()
    {
        using var fixture = new Fixture();
        var service = new Service(fixture.Service);
        var (runtime, prepared) = Prepare(service, fixture.Clock);
        using var round = runtime.CreateLiveRound(prepared);
        try
        {
            round.Seed(CancellationToken.None); round.Trigger(CancellationToken.None);
            var observed = round.Observe(TimeSpan.FromSeconds(1), CancellationToken.None);
            Assert.Equal(2, Assert.Single(observed.Actual.Rows).Fields.GetProperty("score").GetInt32());
            Assert.Equal(0, service.TraceReads); Assert.Equal(0, observed.TaskEvidenceCount);
        }
        finally { round.Cleanup(CancellationToken.None); round.VerifyNoRemaining(CancellationToken.None); }
        Assert.DoesNotContain(fixture.Service.Rows, row => row.LogicalName == "region");
    }
    /// <summary>只納管 payload 指到本輪 GUID 的待辦；相同教會其他待辦完整保留。</summary>
    [Fact]
    public void LiveSdk_TaskOwnership_ExactGuidAndCleanup()
    {
        using var fixture = new Fixture();
        var service = new Service(fixture.Service) { AddTask = true };
        var unrelated = service.Task(Guid.NewGuid(), 100000000);
        fixture.Service.Seed(unrelated);
        var (runtime, prepared) = Prepare(service, fixture.Clock);
        using var round = runtime.CreateLiveRound(prepared);
        try
        {
            round.Seed(CancellationToken.None); round.Trigger(CancellationToken.None);
            var result = round.Observe(TimeSpan.FromSeconds(1), CancellationToken.None);
            Assert.Equal(1, result.TaskEvidenceCount);
            Assert.False(result.Actual.Comparable); // 未預期的待辦絕不能報通過。
        }
        finally { round.Cleanup(CancellationToken.None); round.VerifyNoRemaining(CancellationToken.None); }
        Assert.Equal(unrelated.Id, Assert.Single(fixture.Service.Rows.Where(row => row.LogicalName == "new_outboundtask")).Id);
    }
    /// <summary>待辦 payload 的 GUID 模板由明確別名解析；內容相同通過，任一數字不同必報不可比較。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveSdk_TaskPayload_UsesExactExpectedFields(bool wrong)
    {
        using var fixture = new Fixture(); var service = new Service(fixture.Service) { AddTask = true };
        var (runtime, prepared) = Prepare(service, fixture.Clock);
        var metadata = new ShadowSdkMetadata(service, TaskEntities, CancellationToken.None);
        var fields = JsonSerializer.SerializeToElement(new { new_payload = "{\"recordId\":\"${record}\"}" + (wrong ? " " : "") });
        prepared = prepared with { Source = prepared.Source with { LiveTasks = new[] { new LiveTaskExpectation("task-result", fields) }, LiveMetadata = metadata } };
        using var round = runtime.CreateLiveRound(prepared);
        try
        {
            round.Seed(CancellationToken.None); round.Trigger(CancellationToken.None);
            var observed = round.Observe(TimeSpan.FromSeconds(1), CancellationToken.None);
            Assert.Equal(!wrong, observed.Actual.Comparable);
            Assert.Equal(1, observed.TaskEvidenceCount);
        }
        finally { round.Cleanup(CancellationToken.None); round.VerifyNoRemaining(CancellationToken.None); }
    }
    private static readonly string[] TaskEntities = { "new_outboundtask" };

    /// <summary>伺服器建立種子但回應丟失仍有預配 GUID，清理不按名稱也不留下列。</summary>
    [Fact]
    public void LiveSdk_LostSeedResponse_StillCleansExactId()
    {
        using var fixture = new Fixture();
        var service = new Service(fixture.Service) { LoseCreate = true };
        var (runtime, prepared) = Prepare(service, fixture.Clock);
        using var round = runtime.CreateLiveRound(prepared);
        Assert.Throws<InvalidOperationException>(() => round.Seed(CancellationToken.None));
        round.Cleanup(CancellationToken.None); round.VerifyNoRemaining(CancellationToken.None);
        Assert.DoesNotContain(fixture.Service.Rows, row => row.LogicalName == "region");
    }
    /// <summary>prepared 只含合成 region 與一個同步新步驟，沒有舊步驟或秘密。</summary>
    private static (ShadowSdkRuntime Runtime, ShadowSdkPreparedScenario Prepared) Prepare(Service service, TimeProvider clock)
    {
        var step = Guid.NewGuid();
        var definition = new ShadowScenarioDefinition(ShadowTraceParser.Parse("SMV-SHADOW|1|{\"end\":true,\"count\":0}"),
            new[] { new ShadowObservation("region", "record", ScoreFields) }, new HashSet<string>());
        var source = new ShadowSdkScenario(new[] { new ShadowSdkSeed("record", new Entity("region") { ["score"] = 1 }) },
            new("Update", "record", new Entity("region") { ["score"] = 2 }), Array.Empty<Guid>(), new[] { step },
            Types, Array.Empty<ShadowSdkTokenField>(), false)
        {
            PrimaryIdAttributes = new Dictionary<string, string> { ["region"] = "regionid" },
            SynchronousStepTypes = new Dictionary<Guid, string> { [step] = "Synthetic.NewPlugin" }, TokenNotRequired = true
        };
        return (new(service, Guid.NewGuid(), service.Church, Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = source }, () => Assert.Fail("Live must not switch"), clock), new(definition, source));
    }
    /// <summary>只提供本案同步 Update 及有限查詢，無網路、trace 或切換能力。</summary>
    private sealed class Service(MemoryOrganization inner) : IOrganizationService
    {
        internal Guid Church { get; } = Guid.NewGuid();
        internal bool AddTask { get; init; }
        internal bool LoseCreate { get; init; }
        internal int TraceReads { get; private set; }
        /// <summary>同教會合成待辦；正式ID與payload事件ID分開，狀態受測試明確指定。</summary>
        internal Entity Task(Guid record, int status) => new("new_outboundtask", Guid.NewGuid())
        {
            ["new_accountid"] = new EntityReference("account", Church), ["new_type"] = new OptionSetValue(100000003),
            ["new_taskstatus"] = new OptionSetValue(status), ["new_payload"] = JsonSerializer.Serialize(new { recordId = record }),
            ["new_idempotencykey"] = "synthetic", ["createdon"] = DateTime.UtcNow
        };
        /// <summary>送達後故障是真實資料已存在，產品必須以 attempted 清冊找回。</summary>
        public Guid Create(Entity row) { var id = inner.Create(row); if (LoseCreate) throw new InvalidOperationException("synthetic lost response"); return id; }
        /// <summary>寫入本案資料，不代產品組結果。</summary>
        public void Update(Entity row) => inner.Update(row);
        /// <summary>只刪正式ID。</summary>
        public void Delete(string entity, Guid id) => inner.Delete(entity, id);
        /// <summary>直接讀案例正式列。</summary>
        public Entity Retrieve(string entity, Guid id, ColumnSet columns) => inner.Retrieve(entity, id, columns);
        /// <summary>待辦查詢驗權威教會及時間條件；精確ID查詢沿原SDK，trace意外讀取直接失敗。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = Assert.IsType<QueryExpression>(query);
            if (q.EntityName == "plugintracelog") { TraceReads++; throw new InvalidOperationException("trace unavailable"); }
            if (q.EntityName == "new_outboundtask" && q.Criteria.Conditions.Any(condition => condition.AttributeName == "createdon"))
            {
                Assert.Equal(Church, q.Criteria.Conditions.Single(condition => condition.AttributeName == "new_accountid").Values[0]);
                Assert.Contains(q.Criteria.Conditions, condition => condition.AttributeName == "createdon" && condition.Operator == ConditionOperator.OnOrAfter);
                return new EntityCollection(inner.Rows.Where(row => row.LogicalName == "new_outboundtask").ToList());
            }
            return inner.RetrieveMultiple(query);
        }
        /// <summary>同步 UpdateRequest 回應後才可能產待辦；未知寫入拒絕。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveEntityRequest)
            {
                var entity = new EntityMetadata { LogicalName = "new_outboundtask" };
                typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.PrimaryIdAttribute))!.SetValue(entity, "new_outboundtaskid");
                var primary = new UniqueIdentifierAttributeMetadata { LogicalName = "new_outboundtaskid" };
                var payload = new StringAttributeMetadata { LogicalName = "new_payload", MaxLength = 4000 };
                typeof(AttributeMetadata).GetProperty(nameof(AttributeMetadata.IsValidForCreate))!.SetValue(payload, true);
                typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.Attributes))!.SetValue(entity, new AttributeMetadata[] { primary, payload });
                return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = entity } };
            }
            var update = Assert.IsType<UpdateRequest>(request); inner.Update(update.Target);
            if (AddTask) inner.Seed(Task(update.Target.Id, 100000000));
            return new UpdateResponse();
        }
        /// <summary>無關聯能力，意外呼叫即失敗。</summary>
        public void Associate(string entity, Guid id, Relationship relationship, EntityReferenceCollection rows) => throw new NotSupportedException();
        /// <summary>無解除關聯能力，意外呼叫即失敗。</summary>
        public void Disassociate(string entity, Guid id, Relationship relationship, EntityReferenceCollection rows) => throw new NotSupportedException();
    }
}
