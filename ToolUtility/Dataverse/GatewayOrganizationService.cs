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

    /// <summary>透過 Gateway 在唯一 lease 中建立關聯；Trace 只寫入固定操作名稱，不記錄 entity 或關聯內容。</summary>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => _gateway.Execute(service => { TraceOperation("Associate"); service.Associate(entityName, entityId, relationship, relatedEntities); });

    /// <summary>透過 Gateway 建立資料列；軌跡只可識別操作種類，絕不寫入 entity 欄位或新建 ID。</summary>
    public Guid Create(Entity entity) => _gateway.Execute(service => { TraceOperation("Create"); return service.Create(entity); });

    /// <summary>透過 Gateway 刪除資料列；固定事件名稱不會暴露 entity 名稱或資料列識別碼。</summary>
    public void Delete(string entityName, Guid id)
        => _gateway.Execute(service => { TraceOperation("Delete"); service.Delete(entityName, id); });

    /// <summary>透過 Gateway 移除關聯；Trace 在 lease 內記錄操作種類而不保留關聯或使用者資料。</summary>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => _gateway.Execute(service => { TraceOperation("Disassociate"); service.Disassociate(entityName, entityId, relationship, relatedEntities); });

    /// <summary>透過 Gateway 執行組織要求；只輸出固定 Execute 種類，不記錄 request 名稱、參數或 CRM 回應。</summary>
    public OrganizationResponse Execute(OrganizationRequest request)
        => _gateway.Execute(service => { TraceOperation("Execute"); return service.Execute(request); });

    /// <summary>透過 Gateway 讀取單一資料列；Trace 不記錄 entity 名稱、GUID、欄位集合或資料內容。</summary>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        => _gateway.Execute(service => { TraceOperation("Retrieve"); return service.Retrieve(entityName, id, columnSet); });

    /// <summary>透過 Gateway 執行查詢；軌跡不輸出 QueryBase 條件、欄位或任何 CRM 查詢結果。</summary>
    public EntityCollection RetrieveMultiple(QueryBase query)
        => _gateway.Execute(service => { TraceOperation("RetrieveMultiple"); return service.RetrieveMultiple(query); });

    /// <summary>透過 Gateway 更新資料列；只記錄 Update 操作，不保留 entity 欄位或識別資料。</summary>
    public void Update(Entity entity)
        => _gateway.Execute(service => { TraceOperation("Update"); service.Update(entity); });

    /// <summary>
    /// 在 Gateway 已建立並推入 AsyncLocal 的 lease 範圍內寫入 CRM 操作種類。關閉時只讀取開關後返回，
    /// 固定字串不含呼叫端提供的識別或 CRM 資料，確保稽核檔不會成為資料外洩媒介。
    /// </summary>
    private static void TraceOperation(string operation)
    {
        var trace = DataverseTrace.Current;
        if (trace?.Enabled == true)
            trace.CrmOperation(operation);
    }
}
