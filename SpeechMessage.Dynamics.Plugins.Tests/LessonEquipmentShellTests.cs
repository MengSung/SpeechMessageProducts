/*
 * 檔案整體說明
 * 目的與定位：P5-07設備活動真入口的離線測試，以WorkflowInvoker執行正式活動，
 * 合成SDK提供明確已授權課程／會友與完整查詢；不連D365或讀機密，不冒稱實機驗收。
 * 主要功能與流程：同一活動先Shadow再Live，驗最高已完成階級與零真影子寫入；
 * 注入跨教會及重複正式課程ID，必須在contact更新前拒絕。以安全追蹤確認end輸出。
 * 使用邊界與維護：本測試唯一擁有mock、列集合、追蹤及Invoker，執行同步排空後
 * finally重設mock並清空集合，不保留context／SDK於static；沒有網路連線、背景工作、
 * 計時器或需GC補救的資源，字串及資料只限固定合成值。輸入字串沿舊宣告但不消費。
 */
extern alias Workflows;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Moq;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;
using EquipmentStatusActivity = Workflows::SpeechMessage.Dynamics.Workflows.Lessons.EquipmentStatusActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>設備正式活動的雙模式同案及必要拒絕，SDK由測試擁有而非產品Dispose。</summary>
public sealed class LessonEquipmentShellTests
{
    /// <summary>正常Shadow／Live、跨教會與重複ID故障；同一活動不能沿用前次結果。</summary>
    [Fact]
    public void Lessons_Equipment_ShadowMode_MatchesExpected()
    {
        RunGoldenCases();
        var church = Guid.NewGuid(); var member = Guid.NewGuid(); var record = Guid.NewGuid(); var course = Guid.NewGuid();
        var context = new FakeWorkflowContext(messageName: "ExecuteWorkflow", primaryEntityName: "new_stor_lessons", primaryEntityId: record);
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var factory = new Mock<IOrganizationServiceFactory>(MockBehavior.Strict);
        var tracing = new FakeTracingService(); var writes = new List<Entity>();
        var contact = new Entity("contact", member) { ["parentcustomerid"] = new EntityReference("account", church) };
        var stor = new Entity("new_stor_lessons", record) { ["new_contact_new_stor_lessons"] = contact.ToEntityReference(), ["new_account_new_stor_lessons"] = new EntityReference("account", church), ["new_current_complete"] = true, ["new_new_disciple_lessons_new_stor_les"] = new EntityReference("new_disciple_lessons", course) };
        var lesson = new Entity("new_disciple_lessons", course) { ["new_now_stage_name"] = "E2門徒營" };
        var rows = new EntityCollection(new[] { stor });
        try
        {
            factory.Setup(x => x.CreateOrganizationService(context.Object.UserId)).Returns(sdk.Object);
            sdk.Setup(x => x.Retrieve("new_stor_lessons", record, It.IsAny<ColumnSet>())).Returns(stor);
            sdk.Setup(x => x.Retrieve("contact", member, It.IsAny<ColumnSet>())).Returns(contact);
            sdk.Setup(x => x.Retrieve("new_disciple_lessons", course, It.IsAny<ColumnSet>())).Returns(lesson);
            sdk.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Callback<QueryBase>(q =>
            {
                var query = Assert.IsType<QueryExpression>(q);
                Assert.Equal("new_stor_lessons", query.EntityName);
                Assert.Contains(query.Criteria.Conditions, c => c.AttributeName == "new_contact_new_stor_lessons" && c.Operator == ConditionOperator.Equal && (Guid)c.Values[0] == member);
            }).Returns(() => rows);
            sdk.Setup(x => x.Update(It.IsAny<Entity>())).Callback<Entity>(e => writes.Add(e));
            var activity = new EquipmentStatusActivity();
            void Run(string mode)
            {
                var invoker = new WorkflowInvoker(activity);
                invoker.Extensions.Add<IWorkflowContext>(() => context.Object);
                invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory.Object);
                invoker.Extensions.Add<ITracingService>(() => tracing);
                invoker.Invoke(new Dictionary<string, object> { ["InputParameter"] = "synthetic", ["ExecutionMode"] = mode }, TimeSpan.FromSeconds(10));
            }
            Run("Shadow");
            Assert.Empty(writes);
            var projection = tracing.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length))).ToArray();
            Assert.Equal(2, projection.Length);
            Assert.Equal("Update", (string?)projection[0]["operation"]);
            Assert.Equal("contact", (string?)projection[0]["entity"]);
            Assert.Equal(member.ToString("D"), (string?)projection[0]["id"]);
            Assert.Equal("E2門徒營", (string?)projection[0]["fields"]!["new_equipment_status"]);
            Assert.True((bool)projection[1]["end"]!);
            Run("Live");
            var update = Assert.Single(writes); Assert.Equal(member, update.Id); Assert.Equal("contact", update.LogicalName);
            Assert.Equal("E2門徒營", update["new_equipment_status"]);
            writes.Clear(); contact["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
            Assert.ThrowsAny<Exception>(() => Run("Shadow")); Assert.Empty(writes);
            contact["parentcustomerid"] = new EntityReference("account", church);
            rows.Entities.Add(stor);
            Assert.ThrowsAny<Exception>(() => Run("Live")); Assert.Empty(writes);
        }
        finally { sdk.Reset(); factory.Reset(); context.Mock.Reset(); tracing.Entries.Clear(); rows.Entities.Clear(); writes.Clear(); }
    }
    /// <summary>同份四案輸入與期望經真活動雙模式執行；不以產品輸出反寫期望或只檢查計數。</summary>
    private static void RunGoldenCases()
    {
        var cases = Read("scenarios")["cases"]!.Children<JObject>().ToArray();
        var expected = Read("expected")["cases"]!.Children<JObject>().ToArray();
        var activity = new EquipmentStatusActivity();
        Assert.Equal(cases.Length, expected.Length);
        for (var i = 0; i < cases.Length; i++)
        {
            Assert.Equal((string?)cases[i]["name"], (string?)expected[i]["name"]);
            var definitions = cases[i]["seed"]!.Children<JObject>().ToArray();
            var ids = definitions.ToDictionary(x => (string)x["alias"]!, _ => Guid.NewGuid(), StringComparer.Ordinal);
            var aliases = ids.ToDictionary(x => x.Value, x => x.Key);
            var rows = new Dictionary<Guid, Entity>();
            foreach (var definition in definitions)
            {
                var row = new Entity((string)definition["entity"]!, ids[(string)definition["alias"]!]);
                foreach (var field in ((JObject)definition["fields"]!).Properties())
                    row[field.Name] = field.Value is JObject reference
                        ? new EntityReference((string)reference["entity"]!, ids[(string)reference["ref"]!])
                        : ((JValue)field.Value).Value;
                rows.Add(row.Id, row);
            }
            var context = new FakeWorkflowContext(messageName: "ExecuteWorkflow", primaryEntityName: "new_stor_lessons", primaryEntityId: ids["record"]);
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var factory = new Mock<IOrganizationServiceFactory>(MockBehavior.Strict);
            var tracing = new FakeTracingService(); var writes = new List<Entity>();
            try
            {
                factory.Setup(x => x.CreateOrganizationService(context.Object.UserId)).Returns(sdk.Object);
                sdk.Setup(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                    .Returns<string, Guid, ColumnSet>((table, id, _) => rows[id].LogicalName == table ? rows[id] : throw new InvalidOperationException("Wrong synthetic table."));
                sdk.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
                    .Returns(() => new EntityCollection(rows.Values.Where(x => x.LogicalName == "new_stor_lessons").ToArray()));
                sdk.Setup(x => x.Update(It.IsAny<Entity>())).Callback<Entity>(e => writes.Add(e));
                foreach (var mode in new[] { "Shadow", "Live" })
                {
                    tracing.Entries.Clear(); writes.Clear();
                    var invoker = new WorkflowInvoker(activity);
                    invoker.Extensions.Add<IWorkflowContext>(() => context.Object);
                    invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory.Object);
                    invoker.Extensions.Add<ITracingService>(() => tracing);
                    Exception? failure = null;
                    try { invoker.Invoke(new Dictionary<string, object> { ["InputParameter"] = "synthetic", ["ExecutionMode"] = mode }, TimeSpan.FromSeconds(10)); }
                    catch (Exception exception) { failure = exception; }
                    if (expected[i]["exception"]!.Type != JTokenType.Null)
                    {
                        Assert.NotNull(failure); Assert.Equal((string?)expected[i]["exception"], failure.GetBaseException().GetType().Name);
                        Assert.Empty(writes); continue;
                    }
                    Assert.Null(failure);
                    var actual = new JArray();
                    if (mode == "Shadow")
                    {
                        Assert.Empty(writes);
                        var projection = tracing.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                            .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length))).ToArray();
                        Assert.True((bool)projection.Last()["end"]!);
                        foreach (var line in projection.Where(x => x["operation"] is not null))
                            actual.Add(new JObject { ["operation"] = line["operation"], ["entity"] = line["entity"], ["alias"] = aliases[Guid.Parse((string)line["id"]!)], ["fields"] = line["fields"] });
                    }
                    else
                    {
                        foreach (var row in writes)
                            actual.Add(new JObject { ["operation"] = "Update", ["entity"] = row.LogicalName, ["alias"] = aliases[row.Id], ["fields"] = JObject.FromObject(row.Attributes.ToDictionary(x => x.Key, x => x.Value)) });
                    }
                    Assert.True(JToken.DeepEquals(expected[i]["writes"], actual), "Equipment writes differ from reading expectation.");
                }
            }
            finally { sdk.Reset(); factory.Reset(); context.Mock.Reset(); tracing.Entries.Clear(); writes.Clear(); rows.Clear(); }
        }
    }

    /// <summary>有界讀取與Core相同的嵌入情境；所有reader／stream逆序using釋放。</summary>
    private static JObject Read(string kind)
    {
        using var stream = typeof(LessonEquipmentShellTests).Assembly.GetManifestResourceStream(
            "SpeechMessage.Dynamics.Plugins.Tests.Golden.lessonsequipment." + kind + ".json") ?? throw new InvalidDataException("Missing equipment fixture.");
        if (stream.Length > 1048576) throw new InvalidDataException("Equipment fixture exceeds capacity.");
        using var text = new StreamReader(stream, new UTF8Encoding(false, true));
        using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
        return JObject.Load(reader);
    }
}
