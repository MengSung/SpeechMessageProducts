/*
 * 檔案整體說明
 *
 * 目的與定位：P5-11 收費外掛的正式平台入口回歸測試，確保防遞迴與例外處理
 * 依退役來源及 05A D5-6／D5-8，而非沿用其他模組的深度設定。
 * 主要功能與流程：透過真 FeeAllotPlugin.Execute 與合成平台 context，在第一次
 * SDK 讀取注入原始故障；應執行的訊息必須傳回同一例外，應跳過的訊息零讀取。
 * Plugin 與撥款活動正常案則完整執行讀取與每日待辦意圖，檢查 payload 精確兩欄、
 * 正式教會與初始狀態，Shadow 絕不穿透真寫入；日期由執行前後台北曆日夾住。
 * 資源與隔離：每案 using 擁有 FakeOrganizationService，成功或失敗均同步排空；
 * 工廠只借用本案使用者服務。無網路、秘密、背景工作、跨案快取或正式資料。
 * 驗證範圍：只證明離線入口的政策與原例外；打包、CAS 與實機停用部署另行驗證。
 */
extern alias Workflows;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using Moq;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Fees;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using FeeAllotActivity = Workflows::SpeechMessage.Dynamics.Workflows.Fees.FeeAllotActivity;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fees;

/// <summary>驗證真 Plugin 入口的深度與未知例外契約，不以獨立政策函式代替外殼。</summary>
public sealed class FeesShellTests
{
    /// <summary>待辦初始欄位的固定契約名稱；不包含教會或案例資料，測試不修改。</summary>
    private static readonly string[] TaskFields =
    {
        "new_accountid", "new_attempts", "new_idempotencykey", "new_name",
        "new_payload", "new_taskstatus", "new_type"
    };
    /// <summary>兩個正式撥款入口都須只產生每日 AllotQuery Create，且不能真寫入或連外。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fees_AllotShell_RecordsTask(bool workflow)
    {
        using var sdk = new FakeOrganizationService();
        var feeId = Guid.NewGuid(); var contactId = Guid.NewGuid(); var churchId = Guid.NewGuid();
        sdk.RetrieveHandler = (table, id, columns) =>
        {
            Assert.False(columns.AllColumns);
            if (table == "new_fee" && id == feeId)
                return new Entity(table, id)
                {
                    RowVersion = "1", ["new_contact_new_fee"] = new EntityReference("contact", contactId),
                    ["new_pay_way"] = new OptionSetValue(100000002),
                    ["new_pay_date"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                };
            if (table == "contact" && id == contactId)
                return new Entity(table, id) { ["parentcustomerid"] = new EntityReference("account", churchId) };
            if (table == "account" && id == churchId)
                return new Entity(table, id) { ["new_timezone"] = "Asia/Taipei" };
            throw new InvalidOperationException("Unexpected synthetic allot read.");
        };
        var trace = new FakeTracingService();
        var before = DateTime.UtcNow.AddHours(8).Date;
        if (workflow)
        {
            var context = new FakeWorkflowContext(depth: 99, messageName: "ExecuteWorkflow",
                primaryEntityName: "new_fee", primaryEntityId: feeId);
            try
            {
                var invoker = new WorkflowInvoker(new FeeAllotActivity());
                invoker.Extensions.Add<IWorkflowContext>(() => context.Object);
                invoker.Extensions.Add<ITracingService>(() => trace);
                invoker.Extensions.Add<IOrganizationServiceFactory>(() => new Factory(sdk, context.Object.UserId));
                invoker.Invoke(new Dictionary<string, object> { ["InputParameter"] = "", ["ExecutionMode"] = "Shadow" });
            }
            finally { context.Mock.Reset(); }
        }
        else
        {
            var context = new RemoteExecutionContext
            {
                MessageName = "Create", Depth = 1, Stage = 40, Mode = 0,
                PrimaryEntityName = "new_fee", PrimaryEntityId = feeId,
                UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid()
            };
            context.InputParameters["Target"] = new Entity("new_fee", feeId);
            new FeeAllotPlugin("{\"mode\":\"Shadow\"}", "").Execute(new FakeServiceProvider()
                .Add<IPluginExecutionContext>(context).Add<ITracingService>(trace)
                .Add<IOrganizationServiceFactory>(new Factory(sdk, context.UserId)));
        }
        var after = DateTime.UtcNow.AddHours(8).Date;
        var lines = trace.Entries.Where(line => line.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
            .Select(line => JObject.Parse(line.Substring("SMV-SHADOW|1|".Length))).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.True(JToken.DeepEquals(new JObject { ["end"] = true, ["count"] = 1 }, lines[1]));
        Assert.Equal("Create", (string?)lines[0]["operation"]);
        Assert.Equal("new_outboundtask", (string?)lines[0]["entity"]);
        var fields = (JObject)lines[0]["fields"]!;
        Assert.Equal(TaskFields, fields.Properties().Select(field => field.Name));
        Assert.Equal(churchId.ToString("D"), (string?)fields["new_accountid"]!["id"]);
        Assert.Equal("account", (string?)fields["new_accountid"]!["entity"]);
        Assert.Equal(100000002, (int)fields["new_type"]!);
        Assert.Equal(100000000, (int)fields["new_taskstatus"]!);
        Assert.Equal(0, (int)fields["new_attempts"]!);
        Assert.StartsWith(churchId.ToString("N") + "-100000002-", (string)fields["new_idempotencykey"]!, StringComparison.Ordinal);
        var payload = JObject.Parse((string)fields["new_payload"]!);
        Assert.Equal(2, payload.Count);
        Assert.Equal(feeId.ToString("D"), (string?)payload["feeId"]);
        var actualDay = DateTime.ParseExact(payload["queryDate"]!.ToString(), "yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(actualDay, before, after);
        Assert.Empty(sdk.WriteRecords);
        Assert.Single(sdk.Calls);
        Assert.Equal(5, sdk.Calls[nameof(IOrganizationService.Retrieve)]);
    }

    /// <summary>Create 深度不限，Update 深度二執行、三跳過；錯誤若被吞掉亦會失敗。</summary>
    [Theory]
    [InlineData("Create", 1, true)]
    [InlineData("Create", 99, true)]
    [InlineData("Update", 2, true)]
    [InlineData("Update", 3, false)]
    public void Fees_DepthPolicy_MatchesLegacy(string message, int depth, bool executes)
    {
        using var sdk = new FakeOrganizationService();
        var failure = new InvalidOperationException("Synthetic fee read failure.");
        sdk.RetrieveHandler = (_, _, _) => throw failure;
        var context = new RemoteExecutionContext
        {
            MessageName = message, Depth = depth, Stage = 40, Mode = 0,
            PrimaryEntityName = "new_fee", PrimaryEntityId = Guid.NewGuid(),
            UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid()
        };
        context.InputParameters["Target"] = new Entity("new_fee", context.PrimaryEntityId);
        var provider = new FakeServiceProvider().Add<IPluginExecutionContext>(context)
            .Add<ITracingService>(new FakeTracingService())
            .Add<IOrganizationServiceFactory>(new Factory(sdk, context.UserId));
        var plugin = new FeeAllotPlugin("{\"mode\":\"Shadow\"}", "");
        if (executes)
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => plugin.Execute(provider)));
            Assert.Equal(1, sdk.Calls[nameof(IOrganizationService.Retrieve)]);
        }
        else
        {
            plugin.Execute(provider);
            Assert.Empty(sdk.Calls);
        }
        Assert.Empty(sdk.WriteRecords);
    }

    /// <summary>只接受平台 UserId 的單案服務；外殼不擁有、不 Dispose 此服務。</summary>
    private sealed class Factory(IOrganizationService service, Guid user) : IOrganizationServiceFactory
    {
        /// <summary>錯誤使用者立即拒絕，不能以 null 或 initiating user 提升權限。</summary>
        public IOrganizationService CreateOrganizationService(Guid? userId) =>
            userId == user ? service : throw new InvalidOperationException("Unexpected synthetic service user.");
    }
}
