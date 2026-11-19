/*
檔案整體說明
目的與定位：Live 活動演練的手動測試流程 owner，複製已停用原流程並精確改指新活動；
目前 CLI 因輸入繫結契約未經證實而在準備階段拒絕活動，故此 owner 尚未由 CLI 呼叫。
主要流程：首個寫入前驗參照及模式，預配 GUID 並登錄 attempted，再建立 SMV-TEST 流程；
所有自動觸發關閉，只能對本輪 owned 記錄 ExecuteWorkflow。原流程永不啟用或修改。
安全邊界：只接受包含單一核定自訂活動的流程；含其他自訂活動或Shadow就拒絕，
不能把舊程式帶入演練，也不能猜測Dataverse XAML參數格式。目前CLI對所有非dormant
Live活動都在seed前fail-closed；本owner尚不可由CLI到達。
資源生命週期：SDK 借用，流程先停用再刪除；故障／取消仍由 round cleanup 呼叫，
遠端流程的正式 ID 持有到殘留查詢完成。原 XAML 不輸出、不進 repo，沒有背景工作。
驗證方式：SDK 離線故障注入；R6 實機須先取得可驗證的 XAML 參數繫結契約，未實測不能列為 Live 證據。
*/
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Switching;
namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>經 caller 核對的停用原流程及唯一活動對照，原始 XAML 只在本命令存活。</summary>
internal sealed record LiveActivityTemplate(Entity Source, WorkflowActivityReplacement Replacement);
/// <summary>單輪流程 owner，所有寫入 GUID 都在 Create 前確定。</summary>
internal sealed class LiveActivityProbe(IOrganizationService service, LiveActivityTemplate template)
{
    private readonly Guid id = Guid.NewGuid();
    private bool attempted;
    private Guid operation;
    /// <summary>只從核定停用流程複製，禁止任何自動觸發，記錄必為 round 提供的 owned ID。</summary>
    internal void Trigger(Entity record, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var source = template.Source;
        var xaml = source.GetAttributeValue<string>("xaml");
        if (source.Id == Guid.Empty || source.LogicalName != "workflow" ||
            source.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
            source.GetAttributeValue<string>("primaryentity") != record.LogicalName || string.IsNullOrWhiteSpace(xaml)) throw Invalid();
        var references = WorkflowRepointer.ReadReferences(xaml);
        var replacement = template.Replacement;
        if (references.Count == 0 || references.Any(reference => !WorkflowRepointer.SameReference(reference, replacement.OldAssembly, replacement.OldType))) throw Invalid();
        var rewritten = WorkflowRepointer.Rewrite(xaml, new[] { replacement });
        // Shadow 參數必須由受審樣板清楚移除，不能在此猜測或忽略平台的參數表示法。
        if (rewritten.Contains("Shadow", StringComparison.Ordinal) ||
            WorkflowRepointer.ReadReferences(rewritten).Any(reference => reference.Type != replacement.NewType)) throw Invalid();
        var candidate = new Entity("workflow", id)
        {
            ["name"] = "SMV-TEST-" + id.ToString("N"), ["primaryentity"] = record.LogicalName,
            ["category"] = new OptionSetValue(0), ["type"] = new OptionSetValue(1), ["mode"] = new OptionSetValue(0),
            ["scope"] = new OptionSetValue(4), ["ondemand"] = true, ["subprocess"] = false,
            ["triggeroncreate"] = false, ["triggerondelete"] = false, ["triggeronupdateattributelist"] = "", ["xaml"] = rewritten
        };
        attempted = true;
        if (service.Create(candidate) != id) throw Invalid();
        token.ThrowIfCancellationRequested();
        service.Execute(new SetStateRequest { EntityMoniker = candidate.ToEntityReference(), State = new OptionSetValue(1), Status = new OptionSetValue(2) });
        token.ThrowIfCancellationRequested();
        if (service.Execute(new ExecuteWorkflowRequest { WorkflowId = id, EntityId = record.Id }) is not ExecuteWorkflowResponse response ||
            response.Id == Guid.Empty) throw Invalid();
        operation = response.Id;
    }
    /// <summary>只認 ExecuteWorkflow 回傳的正式作業ID，未知／失敗不當完成。</summary>
    internal bool Completed()
    {
        if (operation == Guid.Empty) throw Invalid();
        var row = service.Retrieve("asyncoperation", operation, new ColumnSet("statecode", "statuscode", "errorcode"));
        if (row.Id != operation || row.GetAttributeValue<OptionSetValue>("statecode")?.Value != 3) return false;
        if (row.GetAttributeValue<OptionSetValue>("statuscode")?.Value != 30 || row.GetAttributeValue<int?>("errorcode") is not (null or 0)) throw Invalid();
        return true;
    }
    /// <summary>取消後仍只停用及刪除本輪正式流程ID，存在性查詢避免回應不明遺留。</summary>
    internal void Cleanup()
    {
        if (!attempted || Find() is null) return;
        service.Execute(new SetStateRequest { EntityMoniker = new EntityReference("workflow", id), State = new OptionSetValue(0), Status = new OptionSetValue(1) });
        service.Delete("workflow", id);
    }
    /// <summary>遠端流程必須不存在才可通過。</summary>
    internal void VerifyNoRemaining() { if (attempted && Find() is not null) throw Invalid(); }
    /// <summary>有界正式ID查詢，不按名稱刪除他人流程。</summary>
    private Entity? Find()
    {
        var query = new QueryExpression("workflow") { ColumnSet = new ColumnSet(false), TopCount = 2 };
        query.Criteria.AddCondition("workflowid", ConditionOperator.Equal, id);
        var rows = service.RetrieveMultiple(query);
        if (rows.MoreRecords || rows.Entities.Count > 1 || rows.Entities.Any(row => row.Id != id)) throw Invalid();
        return rows.Entities.SingleOrDefault();
    }
    /// <summary>固定安全錯誤不回顯 XAML 或流程名稱。</summary>
    private static InvalidDataException Invalid() => new("Live activity probe could not be prepared or completed safely.");

}
