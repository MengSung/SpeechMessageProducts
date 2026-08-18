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

    /// <inheritdoc />
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => Run(service => service.Associate(entityName, entityId, relationship, relatedEntities));

    /// <inheritdoc />
    public Guid Create(Entity entity) => Run(service => service.Create(entity));

    /// <inheritdoc />
    public void Delete(string entityName, Guid id) => Run(service => service.Delete(entityName, id));

    /// <inheritdoc />
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => Run(service => service.Disassociate(entityName, entityId, relationship, relatedEntities));

    /// <inheritdoc />
    public OrganizationResponse Execute(OrganizationRequest request) => Run(service => service.Execute(request));

    /// <inheritdoc />
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        => Run(service => service.Retrieve(entityName, id, columnSet));

    /// <inheritdoc />
    public EntityCollection RetrieveMultiple(QueryBase query) => Run(service => service.RetrieveMultiple(query));

    /// <inheritdoc />
    public void Update(Entity entity) => Run(service => service.Update(entity));

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
