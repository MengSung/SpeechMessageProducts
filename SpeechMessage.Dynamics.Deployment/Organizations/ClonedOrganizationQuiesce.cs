/*
檔案整體說明
目的與定位：FS Step 3b 的複製測試組織自動化停用器，僅允許對 catalog 明確標示為 test 的
單一組織盤點及停用複製帶入的流程、等待中工作與會推播或寄信的 Plugin 步驟。
主要流程：先驗證組織種類，再以固定 QueryExpression 讀取受管自動化候選；不帶 apply 時只回傳
盤點結果，帶 apply 時才逐列更新或取消，且不碰其他組織、全域非同步服務或業務資料。
信任邊界與生命週期：OrganizationDefinition 由白名單提供，不能由 caller 文字取代；SDK service
借由呼叫端持有，本檔不建立 client、task、timer、cache 或背景工作。所有查詢結果只在一次
Run 存活，寫入失敗直接傳回，沒有部分成功的假摘要。
驗證方式：MultiTestOrganizationTests 以合成 SDK 驗證 production 在任何讀取前拒絕，以及 dry-run
有讀取但零寫入；Live3P FS 才能證明實際複製組織已被停用，離線測試不代表實機結果。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Organizations;

/// <summary>一次 quiesce 的純值摘要；不保存 SDK Entity、組織列內容或可跨命令狀態。</summary>
internal sealed record ClonedOrganizationQuiesceResult(
    int PendingWorkflowRuns,
    int WorkflowDefinitionsToDisable,
    int PluginStepsToDisable,
    int MailboxConfigurationsToDisable);

/// <summary>
/// 只對指定 test 組織執行複製自動化停用；production 在任何 SDK 讀取前 fail closed。
/// </summary>
internal static class ClonedOrganizationQuiesce
{
    /// <summary>
    /// 盤點並可選擇停用指定測試組織的複製自動化。
    /// </summary>
    /// <param name="service">由呼叫端唯一擁有的 Dataverse service。</param>
    /// <param name="target">已由 OrganizationCatalog 解析的組織描述。</param>
    /// <param name="apply">false 僅盤點；true 才執行明確列出的停用動作。</param>
    /// <param name="cancellationToken">呼叫端取消訊號，不由本類保存。</param>
    /// <returns>只含數量的本次盤點摘要。</returns>
    internal static ClonedOrganizationQuiesceResult Run(
        IOrganizationService service,
        OrganizationDefinition target,
        bool apply,
        System.Threading.CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(target);
        if (!string.Equals(target.Kind, "test", StringComparison.Ordinal))
            throw new InvalidDataException("Quiesce requires a test organization.");

        cancellationToken.ThrowIfCancellationRequested();
        var pendingRuns = Query(service, "asyncoperation",
            new ColumnSet("asyncoperationid", "statecode", "statuscode", "name"),
            new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("statecode", ConditionOperator.In, 0, 1)
                }
            });
        var workflows = Query(service, "workflow",
            new ColumnSet("workflowid", "statecode", "statuscode", "name", "category"),
            new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("statecode", ConditionOperator.Equal, 1)
                }
            });
        var pluginSteps = Query(service, "sdkmessageprocessingstep",
            new ColumnSet("sdkmessageprocessingstepid", "statecode", "statuscode", "name", "configuration"),
            new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                }
            });
        var mailboxes = Query(service, "mailbox",
            new ColumnSet("mailboxid", "statecode", "statuscode", "name"),
            new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                }
            });

        // 盤點完成前不發布任何修改；dry-run 因此對合成 service 也保證零寫入。
        var result = new ClonedOrganizationQuiesceResult(
            pendingRuns.Count,
            workflows.Count,
            pluginSteps.Count,
            mailboxes.Count);
        if (!apply)
            return result;

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var row in pendingRuns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Update(new Entity(row.LogicalName, row.Id)
            {
                ["statecode"] = new OptionSetValue(3),
                ["statuscode"] = new OptionSetValue(32)
            });
        }
        foreach (var row in workflows.Concat(pluginSteps).Concat(mailboxes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Update(new Entity(row.LogicalName, row.Id)
            {
                ["statecode"] = new OptionSetValue(1)
            });
        }

        return result;
    }

    /// <summary>以固定查詢讀取一類自動化候選；不把 caller 輸入拼入 FetchXML 或跨組織快取。</summary>
    private static List<Entity> Query(
        IOrganizationService service,
        string logicalName,
        ColumnSet columns,
        FilterExpression criteria)
    {
        var query = new QueryExpression(logicalName)
        {
            ColumnSet = columns,
            Criteria = criteria,
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var rows = service.RetrieveMultiple(query).Entities.ToList();
        return rows.Count <= 5000 ? rows : throw new InvalidDataException("Quiesce candidate set exceeds its bounded size.");
    }
}
