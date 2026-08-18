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

    /// <summary>在目前或新建 scope 的 Gateway lease 內建立關聯；Trace 不記錄 entity 或關聯內容。</summary>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => Run(service => { TraceOperation("Associate"); service.Associate(entityName, entityId, relationship, relatedEntities); });

    /// <summary>在目前或新建 scope 的 Gateway lease 內建立資料列；Trace 僅輸出 Create 操作種類。</summary>
    public Guid Create(Entity entity) => Run(service => { TraceOperation("Create"); return service.Create(entity); });

    /// <summary>在目前或新建 scope 的 Gateway lease 內刪除資料列；不輸出 entity 名稱或 GUID。</summary>
    public void Delete(string entityName, Guid id) => Run(service => { TraceOperation("Delete"); service.Delete(entityName, id); });

    /// <summary>在目前或新建 scope 的 Gateway lease 內移除關聯；不保留關聯資料。</summary>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => Run(service => { TraceOperation("Disassociate"); service.Disassociate(entityName, entityId, relationship, relatedEntities); });

    /// <summary>在目前或新建 scope 的 Gateway lease 內執行組織要求；不輸出要求名稱、參數或回應內容。</summary>
    public OrganizationResponse Execute(OrganizationRequest request) => Run(service => { TraceOperation("Execute"); return service.Execute(request); });

    /// <summary>在目前或新建 scope 的 Gateway lease 內讀取單一資料列；Trace 不含查詢資料。</summary>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        => Run(service => { TraceOperation("Retrieve"); return service.Retrieve(entityName, id, columnSet); });

    /// <summary>在目前或新建 scope 的 Gateway lease 內查詢資料；Trace 不含查詢條件或結果。</summary>
    public EntityCollection RetrieveMultiple(QueryBase query) => Run(service => { TraceOperation("RetrieveMultiple"); return service.RetrieveMultiple(query); });

    /// <summary>在目前或新建 scope 的 Gateway lease 內更新資料列；Trace 只輸出固定 Update 種類。</summary>
    public void Update(Entity entity) => Run(service => { TraceOperation("Update"); service.Update(entity); });

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

    /// <summary>
    /// 在 Run 所委派的 Gateway lambda 內記錄固定 CRM 操作名稱，因此無論使用現有 request scope 或臨時
    /// scope 都能讀取該次 leaseId。關閉時不建立事件，也不讀取使用者、Session 或 CRM 資料。
    /// </summary>
    private static void TraceOperation(string operation)
    {
        var trace = DataverseTrace.Current;
        if (trace?.Enabled == true)
            trace.CrmOperation(operation);
    }
}
