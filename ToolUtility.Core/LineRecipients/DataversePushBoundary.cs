/*
檔案整體說明
目的與定位：F5 SDK adapters 的借用身分與資料邊界，避免只靠 caller 路由名稱選組織。
主要功能與流程：建構先 WhoAmI 比對主機核定 GUID 與精確產品，再提供明列 ColumnSet、
查詢及正式列身分驗證。所有同步 I/O 前後檢查 caller token，不以 Task.Run 隱藏在途工作。
使用邊界與維護：不擁有 SDK，不建立連線或快取；adapter Dispose 清除此 owner 的借用參照，
已入場同步呼叫持局部參照直到返回。主機必須提供 transport deadline、固定身分 SDK，
並先排空所有 adapter 呼叫才關閉它；本檔不承諾替無期限 SDK 強制中止遠端 I/O。
*/
using System;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.LineRecipients;

/// <summary>單個 adapter 擁有的借用參照，不 Dispose 主機提供的 SDK。</summary>
internal sealed class DataversePushBoundary : IDisposable
{
    private IOrganizationService? service;
    internal string Product { get; }

    /// <summary>真正組織 GUID 與固定產品必須先驗證；失敗不取得任何 SDK 所有權。</summary>
    internal DataversePushBoundary(IOrganizationService service, Guid organizationId, string product)
    {
#if NETFRAMEWORK
        if (service is null) throw new ArgumentNullException(nameof(service));
#else
        ArgumentNullException.ThrowIfNull(service);
#endif
        if (organizationId == Guid.Empty || product is not ("GoodShepherd2" or "GoodButler" or "GoodGuild"))
            throw new ArgumentException("A fixed three-product deployment identity is required.", nameof(product));
        var response = service.Execute(new WhoAmIRequest());
        if (response is null || !response.Results.TryGetValue("OrganizationId", out var value)
            || value is not Guid actual || actual != organizationId)
            throw new InvalidOperationException("Dataverse organization boundary mismatch.");
        this.service = service;
        Product = product;
    }

    /// <summary>返回當次同步操作的借用參照；關閉後不得再開始任何 SDK 呼叫。</summary>
    internal IOrganizationService GetService() => Volatile.Read(ref service)
        ?? throw new ObjectDisposedException(nameof(DataversePushBoundary));

    /// <summary>精確主鍵查詢後驗回 LogicalName 與 ID，不能將其他列當後備值。</summary>
    internal Entity Read(string logicalName, Guid id, ColumnSet columns, CancellationToken ct)
    {
        var client = GetService();
        ct.ThrowIfCancellationRequested();
        if (id == Guid.Empty) throw new ArgumentException("A database identity is required.", nameof(id));
        var row = client.Retrieve(logicalName, id, columns);
        ct.ThrowIfCancellationRequested();
        RequireIdentity(row, logicalName, id);
        return row;
    }

    /// <summary>有界分頁由呼叫端設定；此處不提前歸還仍執行中的 SDK 或吞掉原例外。</summary>
    internal EntityCollection Query(QueryExpression query, CancellationToken ct)
    {
        var client = GetService();
        ct.ThrowIfCancellationRequested();
        var result = client.RetrieveMultiple(query);
        ct.ThrowIfCancellationRequested();
        return result ?? throw new InvalidOperationException("Dataverse returned no query result.");
    }

    /// <summary>正式資料列身分不可缺失或被其他表／主鍵替換。</summary>
    internal static void RequireIdentity(Entity? row, string name, Guid expectedId)
    {
        if (row is null || row.Id == Guid.Empty || row.Id != expectedId
            || !string.Equals(row.LogicalName, name, StringComparison.Ordinal))
            throw new InvalidOperationException("Dataverse row identity mismatch.");
    }

    /// <summary>lookup 不完整或指向其他表時回空，呼叫端必須拒絕該範圍而非猜測。</summary>
    internal static Guid Lookup(Entity row, string attribute, string target)
    {
        var reference = row.GetAttributeValue<EntityReference>(attribute);
        return reference is not null && string.Equals(reference.LogicalName, target, StringComparison.Ordinal)
            ? reference.Id : Guid.Empty;
    }

    /// <summary>缺值或非啟用選項均 fail closed，不把 null 當成預設啟用。</summary>
    internal static bool Active(Entity row) => row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0;

    /// <summary>解除本 adapter 的參照，主機仍擁有 SDK，必須自行在全部呼叫排空後關閉。</summary>
    public void Dispose() => Interlocked.Exchange(ref service, null);
}
