/*
檔案整體說明
目的與定位：P7 observe的SDK唯讀能力邊界，借用命令lease但拒絕所有寫入API與未列出的Execute。
主要流程：Retrieve／RetrieveMultiple直接委派，Execute只接受WhoAmI／RetrieveCurrentOrganization／RetrieveEntity；其他固定拒絕。
資源與隔離：不擁有client，不建立連線、快取或背景工作，wrapper只活到單次命令，不保存呼叫資料或回應。
失敗與驗證：固定錯誤不含request內容；ObservationReaderTests反向注入Create與SetState，確認觀察能力無法寫入。
*/
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>唯讀能力轉接，不授權任意組織；呼叫端須先通過ProductionGuard與server核對。</summary>
internal sealed class ReadOnlyOrganizationService(IOrganizationService inner) : IOrganizationService
{
    /// <summary>唯讀精確列查詢，保留底層錯誤與所有權。</summary>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => inner.Retrieve(entityName, id, columnSet);
    /// <summary>查詢上限及完整性由消費端驗證，不過濾或靜默去重。</summary>
    public EntityCollection RetrieveMultiple(QueryBase query) => inner.RetrieveMultiple(query);
    /// <summary>只放行固定唯讀metadata訊息，不能因名稱類似而放行任意請求。</summary>
    public OrganizationResponse Execute(OrganizationRequest request) => request.RequestName is "WhoAmI" or "RetrieveCurrentOrganization" or "RetrieveEntity"
        ? inner.Execute(request) : throw Rejected();
    /// <summary>觀察路徑沒有建立權限。</summary>
    public Guid Create(Entity entity) => throw Rejected();
    /// <summary>觀察路徑沒有更新權限。</summary>
    public void Update(Entity entity) => throw Rejected();
    /// <summary>觀察路徑沒有刪除權限。</summary>
    public void Delete(string entityName, Guid id) => throw Rejected();
    /// <summary>觀察路徑沒有關聯權限。</summary>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw Rejected();
    /// <summary>觀察路徑沒有解除關聯權限。</summary>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw Rejected();
    /// <summary>固定錯誤不回顯任意SDK資料。</summary>
    private static InvalidOperationException Rejected() => new("Observation service cannot perform this operation.");
}
