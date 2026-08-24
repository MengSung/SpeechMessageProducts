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

    /// <summary>透過 Gateway 在唯一 lease 中建立關聯；只記錄 entity 名稱與耗時，不含關聯內容。</summary>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => _gateway.Execute(service => CrmOperationTrace.Measure(
            "Associate", entityName, () => service.Associate(entityName, entityId, relationship, relatedEntities)));

    /// <summary>透過 Gateway 建立資料列；軌跡只記 entity 與耗時，絕不寫入欄位值或新建 ID。</summary>
    public Guid Create(Entity entity)
        => _gateway.Execute(service => CrmOperationTrace.Measure(
            "Create", entity?.LogicalName ?? string.Empty, () => service.Create(entity)));

    /// <summary>透過 Gateway 刪除資料列；只輸出 entity 名稱，不含資料列識別碼。</summary>
    public void Delete(string entityName, Guid id)
        => _gateway.Execute(service => CrmOperationTrace.Measure(
            "Delete", entityName, () => service.Delete(entityName, id)));

    /// <summary>透過 Gateway 移除關聯；只記 entity 與耗時，不保留關聯或使用者資料。</summary>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => _gateway.Execute(service => CrmOperationTrace.Measure(
            "Disassociate", entityName, () => service.Disassociate(entityName, entityId, relationship, relatedEntities)));

    /// <summary>透過 Gateway 執行組織要求；只輸出 SDK 訊息名稱，不記錄參數或 CRM 回應。</summary>
    public OrganizationResponse Execute(OrganizationRequest request)
        => _gateway.Execute(service => CrmOperationTrace.Measure(
            "Execute", CrmOperationTrace.DescribeRequest(request), () => service.Execute(request)));

    /// <summary>透過 Gateway 讀取單一資料列；Trace 不記錄 GUID、欄位集合或資料內容。</summary>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        => _gateway.Execute(service => CrmOperationTrace.Measure(
            "Retrieve", entityName, () => service.Retrieve(entityName, id, columnSet)));

    /// <summary>
    /// 透過 Gateway 執行查詢；只輸出 entity 名稱、耗時與回傳筆數，不含查詢條件或任何結果內容。
    /// 筆數是判定 N+1 與大結果集的關鍵量測值，本身不揭露任何一列資料。
    /// </summary>
    public EntityCollection RetrieveMultiple(QueryBase query)
        => _gateway.Execute(service => CrmOperationTrace.Measure(
            "RetrieveMultiple",
            CrmOperationTrace.DescribeQuery(query),
            () => service.RetrieveMultiple(query),
            result => result?.Entities?.Count ?? -1));

    /// <summary>透過 Gateway 更新資料列；只記錄 entity 與耗時，不保留欄位或識別資料。</summary>
    public void Update(Entity entity)
        => _gateway.Execute(service => CrmOperationTrace.Measure(
            "Update", entity?.LogicalName ?? string.Empty, () => service.Update(entity)));
}
