using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 舊 session 持有者的過渡代理：每次操作解析當前 request 的 Gateway，自己不保存 scope 或 client。
/// 沒有 request services 時自建短命 scope，操作完成即 Dispose；待 session cache 重設計後可移除。
/// </summary>
public sealed class AmbientGatewayOrganizationService : IOrganizationService
{
    private readonly Func<IServiceProvider> _requestServicesAccessor;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>建立 ambient 解析代理；accessor 只回傳當前 request services，不延長其生命週期。</summary>
    public AmbientGatewayOrganizationService(
        Func<IServiceProvider> requestServicesAccessor,
        IServiceScopeFactory scopeFactory)
    {
        _requestServicesAccessor = requestServicesAccessor ?? throw new ArgumentNullException(nameof(requestServicesAccessor));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <summary>在目前或新建 scope 的 Gateway lease 內建立關聯；只記 entity 與耗時。</summary>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => Run(service => CrmOperationTrace.Measure(
            "Associate", entityName, () => service.Associate(entityName, entityId, relationship, relatedEntities)));

    /// <summary>在目前或新建 scope 的 Gateway lease 內建立資料列；不輸出欄位值。</summary>
    public Guid Create(Entity entity)
        => Run(service => CrmOperationTrace.Measure(
            "Create", entity?.LogicalName ?? string.Empty, () => service.Create(entity)));

    /// <summary>在目前或新建 scope 的 Gateway lease 內刪除資料列；不輸出 GUID。</summary>
    public void Delete(string entityName, Guid id)
        => Run(service => CrmOperationTrace.Measure(
            "Delete", entityName, () => service.Delete(entityName, id)));

    /// <summary>在目前或新建 scope 的 Gateway lease 內移除關聯；不保留關聯資料。</summary>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => Run(service => CrmOperationTrace.Measure(
            "Disassociate", entityName, () => service.Disassociate(entityName, entityId, relationship, relatedEntities)));

    /// <summary>在目前或新建 scope 的 Gateway lease 內執行組織要求；只輸出 SDK 訊息名稱。</summary>
    public OrganizationResponse Execute(OrganizationRequest request)
        => Run(service => CrmOperationTrace.Measure(
            "Execute", CrmOperationTrace.DescribeRequest(request), () => service.Execute(request)));

    /// <summary>在目前或新建 scope 的 Gateway lease 內讀取單一資料列；Trace 不含查詢資料。</summary>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        => Run(service => CrmOperationTrace.Measure(
            "Retrieve", entityName, () => service.Retrieve(entityName, id, columnSet)));

    /// <summary>在目前或新建 scope 的 Gateway lease 內查詢資料；只輸出 entity、耗時與筆數。</summary>
    public EntityCollection RetrieveMultiple(QueryBase query)
        => Run(service => CrmOperationTrace.Measure(
            "RetrieveMultiple",
            CrmOperationTrace.DescribeQuery(query),
            () => service.RetrieveMultiple(query),
            result => result?.Entities?.Count ?? -1));

    /// <summary>在目前或新建 scope 的 Gateway lease 內更新資料列；只記 entity 與耗時。</summary>
    public void Update(Entity entity)
        => Run(service => CrmOperationTrace.Measure(
            "Update", entity?.LogicalName ?? string.Empty, () => service.Update(entity)));

    private T Run<T>(Func<IOrganizationService, T> work)
    {
        var requestServices = _requestServicesAccessor();
        if (requestServices != null)
            return requestServices.GetRequiredService<IDataverseGateway>().Execute(work);

        using var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IDataverseGateway>().Execute(work);
    }

    private void Run(Action<IOrganizationService> work)
        => Run<object>(service => { work(service); return null; });
}
