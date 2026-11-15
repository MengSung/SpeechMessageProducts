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
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Moq;
using Newtonsoft.Json.Linq;
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
}
