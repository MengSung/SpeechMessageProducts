/*
 * 檔案整體說明
 * 目的與定位：P5-07課程連結真Plugin入口的離線驗證，平台context與來源列均為
 * 合成值，不連D365、LINE或UNC。經真正外殼、SDK協調及待辦store，不能只驗純規則。
 * 主要流程：Create／Update影子輸出須完整且無overflow，真服務不得寫入；Live合成
 * store重送須核對唯一鍵而不重設既有狀態。故障注入空來源歸屬與SDK故障，驗零寫入
 * 及原例外；同外殼重用不同來源，不能殘留會友／教會或執行結果。
 * 使用邊界與維護：每案唯一擁有mock、正式合成列、追蹤與待辦集合，finally清空並
 * Reset全部mock。SDK同步執行，沒有timer、Task、檔案或背景工作；測試Live只指
 * 合成服務，不代表啟用任何實機新步驟或完成實機影子驗收。
 */
extern alias Workflows;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Moq;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Lessons;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;
using LessonQrCodeActivity = Workflows::SpeechMessage.Dynamics.Workflows.Lessons.LessonQrCodeActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>真課程連結外殼與SDK待辦鏈，期望沿用閱讀的15堂、報名及活動問卷差異。</summary>
public sealed class LessonLinkShellTests
{
    /// <summary>正常Create／Update影子須完整且零真寫；深度與授權錯誤必須拒絕產生意圖。</summary>
    [Fact]
    public void Lessons_Link_ShadowMode_MatchesExpected()
    {
        var church = Guid.NewGuid(); var lessonId = Guid.NewGuid(); var contactId = Guid.NewGuid();
        var lesson = new Entity("new_disciple_lessons", lessonId) { ["new_contact_new_disciple_lessons"] = new EntityReference("contact", contactId) };
        var contact = new Entity("contact", contactId) { ["parentcustomerid"] = new EntityReference("account", church) };
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var factory = new Mock<IOrganizationServiceFactory>(MockBehavior.Strict);
        var tracing = new FakeTracingService(); var tasks = new List<Entity>();
        var context = new RemoteExecutionContext
        {
            MessageName = "Create", PrimaryEntityName = "new_disciple_lessons", PrimaryEntityId = lessonId,
            Depth = 1, Stage = 40, Mode = 0, UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
            OrganizationName = "synthetic", CorrelationId = Guid.NewGuid()
        };
        try
        {
            factory.Setup(x => x.CreateOrganizationService(context.UserId)).Returns(sdk.Object);
            sdk.Setup(x => x.Retrieve("new_disciple_lessons", lessonId, It.IsAny<ColumnSet>())).Returns(lesson);
            sdk.Setup(x => x.Retrieve("contact", contactId, It.IsAny<ColumnSet>())).Returns(contact);
            sdk.Setup(x => x.Create(It.IsAny<Entity>())).Returns<Entity>(row =>
            {
                if (tasks.Any(x => Equals(x["new_idempotencykey"], row["new_idempotencykey"])))
                    throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040237) });
                var copy = new Entity(row.LogicalName, Guid.NewGuid());
                foreach (var field in row.Attributes) copy[field.Key] = field.Value;
                tasks.Add(copy); return copy.Id;
            });
            sdk.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Returns<QueryBase>(q =>
            {
                var query = Assert.IsType<QueryExpression>(q);
                Assert.Equal("new_outboundtask", query.EntityName);
                var key = Assert.Single(query.Criteria.Conditions).Values[0];
                return new EntityCollection(tasks.Where(x => Equals(x["new_idempotencykey"], key)).ToArray());
            });
            context.InputParameters["Target"] = new Entity("new_disciple_lessons", lessonId);
            var provider = new FakeServiceProvider().Add<IPluginExecutionContext>(context)
                .Add<IOrganizationServiceFactory>(factory.Object).Add<ITracingService>(tracing);
            var shell = new LessonLinkPlugin("{\"mode\":\"Shadow\"}", "");
            foreach (var message in new[] { "Create", "Update" })
            {
                context.MessageName = message; tracing.Entries.Clear();
                shell.Execute(provider); Assert.Empty(tasks);
                var records = tracing.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                    .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length))).ToArray();
                Assert.True((bool)records.Last()["end"]!);
                Assert.DoesNotContain(records, x => (bool?)x["overflow"] == true);
                Assert.Single(records.Where(x => (string?)x["operation"] == "Create"));
            }
            var live = new LessonLinkPlugin("{\"mode\":\"Live\"}", "");
            tracing.Entries.Clear(); live.Execute(provider); live.Execute(provider);
            Assert.Single(tasks);
            Assert.All(tasks, row => Assert.Equal(church, row.GetAttributeValue<EntityReference>("new_accountid").Id));
            tasks.Clear(); contact["parentcustomerid"] = null; tracing.Entries.Clear();
            Assert.Throws<InvalidOperationException>(() => shell.Execute(provider)); Assert.Empty(tasks);
            contact["parentcustomerid"] = new EntityReference("account", church);
            context.Depth = 2; tracing.Entries.Clear(); shell.Execute(provider);
            Assert.Empty(tasks); Assert.Empty(tracing.Entries);
            context.Depth = 1;
            foreach (var scope in new[] { church, Guid.NewGuid(), church })
            {
                contact["parentcustomerid"] = new EntityReference("account", scope);
                tracing.Entries.Clear(); shell.Execute(provider); Assert.Empty(tasks);
                var record = tracing.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                    .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length)))
                    .Single(x => (string?)x["operation"] == "Create");
                Assert.Equal(scope.ToString("D"), (string?)record["fields"]!["new_accountid"]!["id"]);
            }
            var workflow = new FakeWorkflowContext(messageName: "ExecuteWorkflow",
                primaryEntityName: "new_disciple_lessons", primaryEntityId: lessonId);
            factory.Setup(x => x.CreateOrganizationService(workflow.Object.UserId)).Returns(sdk.Object);
            var activity = new LessonQrCodeActivity();
            void InvokeActivity(string mode)
            {
                var invoker = new WorkflowInvoker(activity);
                invoker.Extensions.Add<IWorkflowContext>(() => workflow.Object);
                invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory.Object);
                invoker.Extensions.Add<ITracingService>(() => tracing);
                invoker.Invoke(new Dictionary<string, object> { ["InputParameter"] = "synthetic", ["ExecutionMode"] = mode }, TimeSpan.FromSeconds(10));
            }
            tracing.Entries.Clear(); InvokeActivity("Shadow"); Assert.Empty(tasks);
            var activityRecords = tracing.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length))).ToArray();
            Assert.True((bool)activityRecords.Last()["end"]!);
            Assert.DoesNotContain(activityRecords, x => (bool?)x["overflow"] == true);
            Assert.Single(activityRecords.Where(x => (string?)x["operation"] == "Create"));
            InvokeActivity("Live");
            var activityPayload = JObject.Parse(Assert.Single(tasks).GetAttributeValue<string>("new_payload"));
            Assert.Equal(32, (int)activityPayload["imageCount"]!); Assert.Equal(1, (int)activityPayload["includePoll"]!);
            tasks.Clear(); workflow.Mock.Reset();
            var failure = new InvalidOperationException("Synthetic SDK failure.");
            sdk.Setup(x => x.Retrieve("new_disciple_lessons", lessonId, It.IsAny<ColumnSet>())).Throws(failure);
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => shell.Execute(provider)));
            Assert.Empty(tasks);
        }
        finally { sdk.Reset(); factory.Reset(); tasks.Clear(); tracing.Entries.Clear(); }
    }
}
