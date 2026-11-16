/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-11 FeeNotifyActivity 在 P5-PROP-006 Shadow 邊界使用的重試鍵與
 * 通知內容摘要契約。測試以真 CodeActivity runtime 與合成 SDK 執行，不連 D365 或 LINE，
 * 也不讀取 token；內容測試使用真 WorkflowInvoker 執行 Shadow 活動。
 *
 * 主要功能與流程：以固定 fee GUID 驗證 Retry Key 原樣等於收費單正式 ID，並以固定舊
 * 通知原文建立獨立預期 JSON，核對活動寫入 Shadow recorder 的摘要值。空 GUID 必須在
 * 任何外部 I/O 前拒絕，避免跨案例或跨教會重用錯誤的通知身分。
 *
 * 使用邊界與維護：每案 using 擁有 SDK，Invoke 同步排空後重設 Moq 與釋放回呼；
 * 沒有共享可變狀態、背景工作、連線或 timer。通過只證明離線意圖摘要與重試鍵，
 * 不代表 Live 推播或實機送達。
 */
extern alias Workflows;

using System;
using System.Activities;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using Moq;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using FeeNotifyActivity = Workflows::SpeechMessage.Dynamics.Workflows.Fees.FeeNotifyActivity;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fees;

/// <summary>保護收費通知的正式重試鍵及舊範本摘要。</summary>
public sealed class FeeNotifyActivityTests
{
    private static readonly Guid FeeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>通知重試鍵必須直接使用 feeId，不能改用 contact 或活動 instance。</summary>
    [Fact]
    public void Fees_Notify_RetryKeyIsFeeId()
    {
        Assert.Equal(FeeId, FeeNotifyActivity.RetryKey(FeeId));
        Assert.Throws<ArgumentException>(() => FeeNotifyActivity.RetryKey(Guid.Empty));
    }

    /// <summary>真活動須雜湊舊原文與伺服器 sender，不能用範本代號冒充內容。</summary>
    [Fact]
    public void Fees_Notify_ContentMatchesLegacy()
    {
        using var sdk = new FakeOrganizationService();
        var contactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var churchId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var channelId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        sdk.RetrieveHandler = (table, id, columns) =>
        {
            Assert.False(columns.AllColumns);
            Assert.DoesNotContain(columns.Columns, column => column.Contains("token"));
            if (table == "new_fee" && id == FeeId)
                return new Entity(table, id)
                {
                    ["new_contact_new_fee"] = new EntityReference("contact", contactId),
                    ["new_pay_date"] = new DateTime(2026, 11, 15, 16, 30, 0, DateTimeKind.Utc),
                    ["new_category"] = new OptionSetValue(100000002),
                    ["new_pay_way"] = new OptionSetValue(100000002),
                    ["new_others"] = "測試",
                    ["new_fee_really_paid"] = new Money(1000.5m)
                };
            if (table == "contact" && id == contactId)
                return new Entity(table, id)
                {
                    ["fullname"] = "合成會友",
                    ["parentcustomerid"] = new EntityReference("account", churchId),
                    ["new_lineid"] = "U" + new string('a', 32)
                };
            if (table == "account" && id == churchId)
                return new Entity(table, id)
                {
                    ["name"] = "合成教會", ["new_timezone"] = "Asia/Taipei",
                    ["new_linechannelid"] = new EntityReference("new_linechannel", channelId)
                };
            if (table == "new_linechannel" && id == channelId) return new Entity(table, id);
            throw new InvalidOperationException("Unexpected synthetic fee retrieve.");
        };
        var context = new FakeWorkflowContext(messageName: "ExecuteWorkflow", primaryEntityName: "new_fee",
            primaryEntityId: FeeId);
        var trace = new FakeTracingService();
        var invoker = new WorkflowInvoker(new FeeNotifyActivity());
        invoker.Extensions.Add<IWorkflowContext>(() => context.Object);
        invoker.Extensions.Add<ITracingService>(() => trace);
        invoker.Extensions.Add<IOrganizationServiceFactory>(() => new Factory(sdk, context.Object.UserId));
        invoker.Invoke(new System.Collections.Generic.Dictionary<string, object>
            { ["InputParameter"] = "", ["ExecutionMode"] = "Shadow" });
        const string json = "[{\"type\":\"text\",\"text\":\"敬收 合成會友 奉獻\\r\\n日期 : 2026/11/16\\r\\n類別 : 感恩獻金  測試\\r\\n付款方式: ATM轉帳\\r\\n金額 : 1000\",\"sender\":{\"name\":\"合成教會助手\"}}]";
        using var hash = SHA256.Create();
        var expected = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(json)))
            .Replace("-", string.Empty).ToLowerInvariant();
        Assert.Contains(trace.Entries, line => line.Contains(expected));
        Assert.DoesNotContain(trace.Entries, line => line.Contains("合成會友"));
        Assert.Empty(sdk.WriteRecords);
        context.Mock.Reset();
    }

    /// <summary>限制活動只能借用該案例的平台使用者服務，不讓 null 提升權限。</summary>
    private sealed class Factory(IOrganizationService sdk, Guid userId) : IOrganizationServiceFactory
    {
        /// <summary>錯誤使用者立即拒絕，服務生命週期仍由測試 using 擁有。</summary>
        public IOrganizationService CreateOrganizationService(Guid? requestedUser) =>
            requestedUser == userId ? sdk : throw new InvalidOperationException("Unexpected synthetic fee user.");
    }
}
