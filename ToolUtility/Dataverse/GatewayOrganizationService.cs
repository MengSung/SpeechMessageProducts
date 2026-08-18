using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 無狀態的 IOrganizationService 代理。每個方法都透過 Gateway 取得 per-operation lease，
/// 因此即使被 30 分鐘 session cache 持有，也不會持有或釋放任何跨 request client。
/// </summary>
public sealed class GatewayOrganizationService : IOrganizationService
{
    private readonly IDataverseGateway _gateway;

    /// <summary>建立由 request scope 提供 Gateway 的組織服務代理。</summary>
    public GatewayOrganizationService(IDataverseGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    /// <inheritdoc />
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => _gateway.Execute(service => service.Associate(entityName, entityId, relationship, relatedEntities));

    /// <inheritdoc />
    public Guid Create(Entity entity) => _gateway.Execute(service => service.Create(entity));

    /// <inheritdoc />
    public void Delete(string entityName, Guid id)
        => _gateway.Execute(service => service.Delete(entityName, id));

    /// <inheritdoc />
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => _gateway.Execute(service => service.Disassociate(entityName, entityId, relationship, relatedEntities));

    /// <inheritdoc />
    public OrganizationResponse Execute(OrganizationRequest request)
        => _gateway.Execute(service => service.Execute(request));

    /// <inheritdoc />
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        => _gateway.Execute(service => service.Retrieve(entityName, id, columnSet));

    /// <inheritdoc />
    public EntityCollection RetrieveMultiple(QueryBase query)
        => _gateway.Execute(service => service.RetrieveMultiple(query));

    /// <inheritdoc />
    public void Update(Entity entity)
        => _gateway.Execute(service => service.Update(entity));
}
