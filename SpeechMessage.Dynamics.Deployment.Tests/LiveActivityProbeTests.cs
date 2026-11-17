/*
檔案整體說明
目的與定位：Live 活動測試流程的離線 SDK 所有權契約，不載入活動、不執行舊程式或連 D365。
主要流程：用停用的合成原流程建立手動複本，核新活動參照、自動觸發全關與原流程完全不變；
Create 回應遺失仍能按預配ID找回，混合其他舊活動則在任何寫入前拒絕。
資源與隔離：每案 SDK 與流程ID獨占，Probe 清理後由 fixture Dispose；沒有真正 workflow runtime。
決定性斷言：測試複本先停用再刪除、原列字串與狀態保留、無新增殘留，不冒稱實機啟用成功。
*/
using System;
using System.Linq;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Live;
using SpeechMessage.Dynamics.Deployment.Switching;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>真 SDK probe 的建立、精準改指及失敗清理。</summary>
public sealed class LiveActivityProbeTests
{
    /// <summary>複本只手動觸發，成功及送達後失敗均清本輪流程，不動原列。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActivityProbe_ManualCopyAndLostResponse_CleansOnlyOwned(bool lose)
    {
        using var f = new Fixture(); var service = new Service(f.Service) { LoseCreate = lose };
        var source = Source(); f.Service.Seed(source);
        var original = source.GetAttributeValue<string>("xaml");
        var probe = new LiveActivityProbe(service, new(source, new("Old.Assembly", "Old.Activity", "SpeechMessage.Dynamics.Workflows", "New.Activity")));
        try
        {
            if (lose) Assert.Throws<InvalidOperationException>(() => probe.Trigger(new Entity("task", Guid.NewGuid()), CancellationToken.None));
            else
            {
                probe.Trigger(new Entity("task", Guid.NewGuid()), CancellationToken.None);
                var copy = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "workflow" && row.Id != source.Id));
                Assert.True(copy.GetAttributeValue<bool>("ondemand"));
                Assert.False(copy.GetAttributeValue<bool>("triggeroncreate")); Assert.False(copy.GetAttributeValue<bool>("triggerondelete"));
                Assert.Equal("", copy.GetAttributeValue<string>("triggeronupdateattributelist"));
                Assert.DoesNotContain("Old.Activity", copy.GetAttributeValue<string>("xaml"), StringComparison.Ordinal);
                Assert.True(probe.Completed());
            }
        }
        finally { probe.Cleanup(); probe.VerifyNoRemaining(); }
        Assert.Equal(original, source.GetAttributeValue<string>("xaml"));
        Assert.Equal(0, source.GetAttributeValue<OptionSetValue>("statecode").Value);
        Assert.Equal(source.Id, Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "workflow")).Id);
    }
    /// <summary>原流程還有另一個未移植活動，不能部分改指後啟用。</summary>
    [Fact]
    public void ActivityProbe_UnportedActivity_RefusedBeforeCreate()
    {
        using var f = new Fixture(); var source = Source();
        source["xaml"] = "<Activity><Node AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/><Node AssemblyQualifiedName=\"Other.Activity, Other.Assembly\"/></Activity>";
        var probe = new LiveActivityProbe(new Service(f.Service), new(source, new("Old.Assembly", "Old.Activity", "SpeechMessage.Dynamics.Workflows", "New.Activity")));
        Assert.Throws<System.IO.InvalidDataException>(() => probe.Trigger(new Entity("task", Guid.NewGuid()), CancellationToken.None));
        probe.Cleanup(); probe.VerifyNoRemaining(); Assert.Empty(f.Service.Writes);
    }
    /// <summary>純合成停用流程，正文不含身分、秘密或真實名稱。</summary>
    private static Entity Source() => new("workflow", Guid.NewGuid()) { ["statecode"] = new OptionSetValue(0),
        ["primaryentity"] = "task", ["xaml"] = "<Activity><Node AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/></Activity>" };
    /// <summary>只模擬平台流程控制及完成回應，不執行任何 XAML。</summary>
    private sealed class Service(MemoryOrganization inner) : IOrganizationService
    {
        internal bool LoseCreate { get; init; }
        private readonly Guid operation = Guid.NewGuid();
        /// <summary>送達後故障必須由產品預配ID找回。</summary>
        public Guid Create(Entity row) { var id = inner.Create(row); if (LoseCreate) throw new InvalidOperationException("synthetic lost response"); return id; }
        /// <summary>只對本案列作平台狀態更新。</summary>
        public void Update(Entity row) => inner.Update(row);
        /// <summary>刪除正式ID，不按名稱。</summary>
        public void Delete(string table, Guid id) => inner.Delete(table, id);
        /// <summary>完成作業只回純狀態，不保留業務資料。</summary>
        public Entity Retrieve(string table, Guid id, ColumnSet columns) => table == "asyncoperation" && id == operation
            ? new(table, id) { ["statecode"] = new OptionSetValue(3), ["statuscode"] = new OptionSetValue(30) } : inner.Retrieve(table, id, columns);
        /// <summary>精確流程ID存在性查詢沿本案SDK。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => inner.RetrieveMultiple(query);
        /// <summary>手動執行只回作業ID；狀態訊息交原SDK，不執行舊程式。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => request is ExecuteWorkflowRequest
            ? new ExecuteWorkflowResponse { Results = { ["Id"] = operation } } : inner.Execute(request);
        /// <summary>無關聯能力。</summary>
        public void Associate(string name, Guid id, Relationship relationship, EntityReferenceCollection rows) => throw new NotSupportedException();
        /// <summary>無解除關聯能力。</summary>
        public void Disassociate(string name, Guid id, Relationship relationship, EntityReferenceCollection rows) => throw new NotSupportedException();
    }
}
