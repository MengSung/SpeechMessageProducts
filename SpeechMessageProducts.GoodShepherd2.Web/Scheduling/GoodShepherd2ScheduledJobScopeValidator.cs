/*
檔案整體說明
目的與定位：F4好牧人2.0排程的scoped執行前權威驗證器，只放行固定產品、部署組織與當次仍可執行的租戶，不保存角色、token或登入身分。
主要功能與流程：先拒絕不符的JobScope，再以同一短期CRM租約WhoAmI核對組織。租戶工作只讀一個account的new_istenant、statecode及new_lifecyclestatus，並讀已發布choice metadata映射計畫七種繁中狀態；數值不依顯示順序推測，停用、保留中、已結束及不明資料一律跳過。null租戶只允許已驗同組織的明確組織級工作，不自動擴張為全租戶授權。
使用邊界與維護：租戶旗標依平台00-core清單，只接受OptionSetValue的「是」100000001，不把bool、裸整數或未知選項當權威資料。計畫與目前repo沒有生命週期數值，因此每次工作向CRM已發布metadata確認；缺少1028繁中label、重複值、未知label或欄位未部署均fail closed並記固定原因。所有query、列、metadata及集合只在單次呼叫存活，沒有授權或schema cache，不增加一般HTTP成本。資料庫ID是account唯一身分，重複列拒絕而非去重。SDK同步操作與取消由CrmLease完整排空、fault及歸還；真正故障保留給核心或主機共用診斷，預期拒絕只記不含識別值的資訊。
*/
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Scheduling;
using ToolUtilityNameSpace.Dataverse;

namespace SpeechMessageProducts.GoodShepherd2.Web.Scheduling;

/// <summary>單次工作scope的無狀態重驗器；任何成功結果只供當次handler使用，不形成權限快照cache。</summary>
/// <param name="manager">既有Host擁有的固定產品manager，只借用短期lease。</param>
/// <param name="options">啟動部署快照；組織GUID仍須每次以WhoAmI驗證。</param>
/// <param name="logger">共用Host記錄器，只接收固定拒絕原因，不傳租戶或組織識別。</param>
internal sealed class GoodShepherd2ScheduledJobScopeValidator(
    IDataverseConnectionManager manager, SchedulingOptions options,
    ILogger<GoodShepherd2ScheduledJobScopeValidator> logger) : IScheduledJobScopeValidator
{
    /// <summary>無使用者資料的固定訊息委派；避免逐次建立logger格式剖析狀態或記錄權威列。</summary>
    private static readonly Action<ILogger, string, Exception?> LogSkipped = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(4001, "SchedulingScopeRejected"), "Scheduled job skipped: {Reason}.");

    /// <summary>逐次驗產品、WhoAmI及租戶；同步SDK不拆為逃離租約的背景工作，失敗不轉成false。</summary>
    /// <param name="scope">持久化工作中的未重新授權純值，不可直接信任。</param>
    /// <param name="ct">執行worker原取消訊號；借入SDK返回且租約釋放後才傳遞。</param>
    /// <returns>所有查詢與歸還成功且當次狀態允許時為true，其餘預期拒絕為false。</returns>
    public Task<bool> CanExecuteAsync(JobScope scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(options.Product, GoodShepherd2Product.Name, StringComparison.Ordinal) ||
            !string.Equals(scope.Product, GoodShepherd2Product.Name, StringComparison.Ordinal) ||
            options.OrganizationId == Guid.Empty || scope.OrganizationId != options.OrganizationId ||
            scope.TenantId == Guid.Empty)
            return Task.FromResult(Reject("DeploymentScopeMismatch"));
        var result = GoodShepherd2SchedulingCrmLease.Execute(manager, options.OrganizationId,
            service => scope.TenantId is not { } tenantId || CheckTenant(service, tenantId, ct), ct);
        return Task.FromResult(result);
    }

    /// <summary>只查指定權威ID，完整檢查回應型別與列身分，不以default補缺欄或以名字合併。</summary>
    private bool CheckTenant(IOrganizationService service, Guid tenantId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var query = new QueryExpression("account")
        {
            TopCount = 2,
            ColumnSet = new ColumnSet("accountid", "new_istenant", "statecode", "new_lifecyclestatus")
        };
        query.Criteria.AddCondition("accountid", ConditionOperator.Equal, tenantId);
        var rows = service.RetrieveMultiple(query);
        ct.ThrowIfCancellationRequested();
        if (rows is null || rows.MoreRecords || rows.Entities.Count > 1)
            throw new InvalidOperationException("Scheduling tenant identity projection is invalid.");
        if (rows.Entities.Count == 0) return Reject("TenantMissing");
        var row = rows.Entities[0];
        if (!string.Equals(row.LogicalName, "account", StringComparison.Ordinal) || row.Id != tenantId ||
            !row.Attributes.TryGetValue("new_istenant", out var tenant) ||
            tenant is not OptionSetValue { Value: 100000001 } ||
            !row.Attributes.TryGetValue("statecode", out var state) || state is not OptionSetValue { Value: 0 } ||
            !row.Attributes.TryGetValue("new_lifecyclestatus", out var status) || status is not OptionSetValue lifecycle)
            return Reject("TenantDisabledOrIncomplete");
        return CheckLifecycle(service, lifecycle.Value, ct);
    }

    /// <summary>只以已發布schema讀取數值；未核定或不能唯一對應計畫七種狀態時維持拒絕。</summary>
    private bool CheckLifecycle(IOrganizationService service, int lifecycle, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = service.Execute(new RetrieveAttributeRequest
        {
            EntityLogicalName = "account", LogicalName = "new_lifecyclestatus", RetrieveAsIfPublished = false
        });
        ct.ThrowIfCancellationRequested();
        if (response is not RetrieveAttributeResponse { AttributeMetadata: PicklistAttributeMetadata metadata } ||
            !string.Equals(metadata.LogicalName, "new_lifecyclestatus", StringComparison.Ordinal) ||
            metadata.OptionSet?.Options is not { Count: 7 } choices)
            return Reject("LifecyclePublishedMetadataUnavailable");

        // 計畫只有名稱，沒有choice數值。完整核對七種唯一繁中label與數值，禁止依索引／預設數字猜測。
        var values = new HashSet<int>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        string? current = null;
        foreach (var choice in choices)
        {
            var localized = choice.Label?.LocalizedLabels.Where(value => value.LanguageCode == 1028).Take(2).ToArray();
            if (choice.Value is not { } value || localized is not { Length: 1 } ||
                localized[0].Label is not ("申請中" or "待啟用" or "試用中" or "進行中" or "待配發名額" or "保留中" or "已結束") ||
                !values.Add(value) || !labels.Add(localized[0].Label))
                return Reject("LifecyclePublishedMetadataAmbiguous");
            if (value == lifecycle) current = localized[0].Label;
        }
        return current is "申請中" or "待啟用" or "試用中" or "進行中" or "待配發名額" ||
            Reject("TenantRetainedEndedOrLifecycleUnknown");
    }

    /// <summary>預期拒絕只記固定理由，不記產品輸入、資料列、GUID、角色或任何秘密。</summary>
    private bool Reject(string reason)
    {
        LogSkipped(logger, reason, null);
        return false;
    }
}
