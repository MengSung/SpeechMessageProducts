/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P5-01 Plugin 離線測試使用的記憶體 IOrganizationService，
 * 以實體邏輯名稱及正式 GUID 作為識別；不是 Dataverse 權限、metadata 或交易引擎。
 * 主要功能與流程：保留既有 Handler 與 Calls；明確 callback 優先，其餘操作走本案例
 * 的 CRUD、關聯及 typed Execute。輸入與輸出均遞迴複製 SDK 可變值，更新先建候選再發布。
 * 使用邊界與維護：每個案例擁有獨立服務，最多 4096 列、4096 關聯、256 次結果覆蓋；
 * 單一鎖保護操作，設定 callback 與讀 Calls 須在呼叫排空後進行。沒有連線、背景工作、
 * 取消或逾時；using owner 的 Dispose 清除資料及回呼。未 Dispose 的舊案例只由 GC 回收。
 * queryResults 以一開始為一的 RetrieveMultiple 呼叫序號明確覆蓋未知查詢，兩側均複製；
 * 這是情境指定的離線答案，不宣稱該語法受支援。缺列、重複識別、容量及未知型別均拒絕。
 * 驗證：FakeOrganizationServiceTests 保護分離、原子失敗、查詢、關聯、隔離與排空。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fakes;

/// <summary>測試案例獨佔的有界記憶體服務；資料不跨服務實例共享，Dispose 後所有操作拒絕。</summary>
internal sealed partial class FakeOrganizationService : IOrganizationService, IDisposable
{
    private const int Capacity = 4096;
    private readonly object gate = new();
    private readonly Dictionary<(string Name, Guid Id), Entity> rows = new();
    private readonly HashSet<(string Name, Guid Id, string Relationship, string RelatedName, Guid RelatedId)> associations = new();
    private readonly Dictionary<int, EntityCollection> overrides = new();
    private bool disposed;

    /// <summary>以合成資料初始化表及依查詢序號設定的答案；建構時複製，不保留呼叫端集合。</summary>
    public FakeOrganizationService(IEnumerable<Entity>? entities = null, IReadOnlyDictionary<int, EntityCollection>? queryResults = null)
    {
        if (entities is not null)
            foreach (var entity in entities) CreateCore(entity);
        if (queryResults is not null)
            foreach (var result in queryResults) SetQueryResult(result.Key, result.Value);
    }

    /// <summary>讀取回呼優先於表，回應經複製以避免共用可變圖；僅於案例內配置。</summary>
    public Func<string, Guid, ColumnSet, Entity>? RetrieveHandler { get; set; }
    /// <summary>查詢故障／答案注入入口，優先於序號覆蓋與預設查詢引擎；回應經複製。</summary>
    public Func<QueryBase, EntityCollection>? RetrieveMultipleHandler { get; set; }
    /// <summary>建立故障注入入口；有設定時由案例決定行為，不修改內部表。</summary>
    public Func<Entity, Guid>? CreateHandler { get; set; }
    /// <summary>更新故障注入入口；設定後交由案例回呼負責，不再套用預設寫入。</summary>
    public Action<Entity>? UpdateHandler { get; set; }
    /// <summary>刪除故障注入入口；設定後不修改記憶體表。</summary>
    public Action<string, Guid>? DeleteHandler { get; set; }
    /// <summary>任意 Execute 的明確注入點；保留回呼回應型別及原例外，不嘗試預設處理。</summary>
    public Func<OrganizationRequest, OrganizationResponse>? ExecuteHandler { get; set; }
    /// <summary>關聯注入入口；由案例負責 callback 的外部狀態生命週期。</summary>
    public Action<string, Guid, Relationship, EntityReferenceCollection>? AssociateHandler { get; set; }
    /// <summary>解除關聯注入入口，設定後不修改內部關聯表。</summary>
    public Action<string, Guid, Relationship, EntityReferenceCollection>? DisassociateHandler { get; set; }
    /// <summary>固定八種服務方法的計數；為相容保留字典，僅可於沒有操作執行時讀寫。</summary>
    public Dictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);
    /// <summary>目前記憶體列數；Dispose 後為零，供案例確定性檢查排空。</summary>
    public int EntityCount { get { lock (gate) return rows.Count; } }
    /// <summary>尚保存的顯式查詢答案數；Dispose 後為零，可驗證情境狀態已排空。</summary>
    public int QueryResultCount { get { lock (gate) return overrides.Count; } }
    /// <summary>目前關聯邊數；不代表 metadata 實體或關聯查詢結果。</summary>
    public int AssociationCount { get { lock (gate) return associations.Count; } }

    /// <summary>為指定的一開始為一的外部查詢序號保存 detached 答案；未知語法僅於這次明確覆蓋。</summary>
    public void SetQueryResult(int queryCall, EntityCollection result)
    {
        lock (gate)
        {
            EnsureActive();
            if (queryCall < 1) throw new ArgumentOutOfRangeException(nameof(queryCall));
            if (!overrides.ContainsKey(queryCall) && overrides.Count >= 256) throw Unsupported("queryResults capacity");
            overrides[queryCall] = CloneCollection(result, 0);
        }
    }

    /// <summary>從本案例表按正式識別讀取欄位投影；主鍵永遠可見，缺列明確失敗。</summary>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    {
        lock (gate)
        {
            Increment(nameof(Retrieve));
            return RetrieveHandler is not null ? CloneEntity(RetrieveHandler(entityName, id, columnSet), 0) : Project(Find(entityName, id), columnSet);
        }
    }

    /// <summary>callback、情境序號答案、嚴格查詢引擎依序選一；沒有不支援語法的空集合 fallback。</summary>
    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        lock (gate)
        {
            Increment(nameof(RetrieveMultiple));
            if (RetrieveMultipleHandler is not null) return CloneCollection(RetrieveMultipleHandler(query), 0);
            if (overrides.TryGetValue(Calls[nameof(RetrieveMultiple)], out var answer)) return CloneCollection(answer, 0);
            return Query(query);
        }
    }

    /// <summary>先深層複製並檢查容量及主鍵，再新增完整記錄；不修改呼叫端 Entity.Id。</summary>
    public Guid Create(Entity entity)
    {
        lock (gate) { Increment(nameof(Create)); return CreateHandler is not null ? CreateHandler(entity) : CreateCore(entity); }
    }

    /// <summary>只合併傳入欄位；null 表示刪除欄位，任何複製失敗都保留原快照。</summary>
    public void Update(Entity entity)
    {
        lock (gate) { Increment(nameof(Update)); if (UpdateHandler is not null) UpdateHandler(entity); else UpdateCore(entity); }
    }

    /// <summary>刪除正式記錄並移除指向它的關聯；不存在時明確失敗，不模擬 cascade metadata。</summary>
    public void Delete(string entityName, Guid id)
    {
        lock (gate)
        {
            Increment(nameof(Delete));
            if (DeleteHandler is not null) { DeleteHandler(entityName, id); return; }
            DeleteCore(entityName, id);
        }
    }

    /// <summary>支援七種 CRUD／關聯 typed request 與 GUID Upsert；額外參數及未知 message 具名拒絕。</summary>
    public OrganizationResponse Execute(OrganizationRequest request)
    {
        lock (gate)
        {
            Increment(nameof(Execute));
            if (ExecuteHandler is not null) return ExecuteHandler(request);
            if (request is null) throw new ArgumentNullException(nameof(request));
            switch (request)
            {
                case CreateRequest create:
                    Parameters(request, "Target");
                    var created = new CreateResponse(); created.Results["id"] = CreateCore(create.Target); return created;
                case UpdateRequest update:
                    Parameters(request, "Target"); UpdateCore(update.Target); return new UpdateResponse();
                case DeleteRequest delete:
                    Parameters(request, "Target"); Reference(delete.Target); DeleteCore(delete.Target.LogicalName, delete.Target.Id); return new DeleteResponse();
                case RetrieveRequest retrieve:
                    Parameters(request, "Target", "ColumnSet"); Reference(retrieve.Target);
                    var single = new RetrieveResponse(); single.Results["Entity"] = Project(Find(retrieve.Target.LogicalName, retrieve.Target.Id), retrieve.ColumnSet); return single;
                case RetrieveMultipleRequest multiple:
                    Parameters(request, "Query");
                    var many = new RetrieveMultipleResponse(); many.Results["EntityCollection"] = RetrieveMultiple(multiple.Query); return many;
                case AssociateRequest associate:
                    Parameters(request, "Target", "Relationship", "RelatedEntities"); Reference(associate.Target);
                    Relate(associate.Target.LogicalName, associate.Target.Id, associate.Relationship, associate.RelatedEntities, true); return new AssociateResponse();
                case DisassociateRequest disassociate:
                    Parameters(request, "Target", "Relationship", "RelatedEntities"); Reference(disassociate.Target);
                    Relate(disassociate.Target.LogicalName, disassociate.Target.Id, disassociate.Relationship, disassociate.RelatedEntities, false); return new DisassociateResponse();
                case UpsertRequest upsert:
                    Parameters(request, "Target");
                    if (upsert.Target.KeyAttributes.Count != 0) throw Unsupported("Upsert.AlternateKeys");
                    var exists = rows.ContainsKey(Key(upsert.Target.LogicalName, upsert.Target.Id));
                    var id = upsert.Target.Id;
                    if (exists) UpdateCore(upsert.Target); else id = CreateCore(upsert.Target);
                    var response = new UpsertResponse(); response.Results["RecordCreated"] = !exists;
                    response.Results["Target"] = new EntityReference(upsert.Target.LogicalName, id); return response;
                default: throw Unsupported("Execute." + request.RequestName + "." + request.GetType().Name);
            }
        }
    }

    /// <summary>以關聯 schema 與兩端正式識別建立邊；全批驗證後發布，重複邊拒絕。</summary>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
    {
        lock (gate)
        {
            Increment(nameof(Associate));
            if (AssociateHandler is not null) AssociateHandler(entityName, entityId, relationship, relatedEntities);
            else Relate(entityName, entityId, relationship, relatedEntities, true);
        }
    }

    /// <summary>整批解除既有邊，任一邊缺失則不修改；不推測一對多 lookup metadata。</summary>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
    {
        lock (gate)
        {
            Increment(nameof(Disassociate));
            if (DisassociateHandler is not null) DisassociateHandler(entityName, entityId, relationship, relatedEntities);
            else Relate(entityName, entityId, relationship, relatedEntities, false);
        }
    }

    /// <summary>以固定 schema 與正式識別檢查本案例關聯，不把內部集合交給測試修改。</summary>
    public bool IsAssociated(string entityName, Guid id, string relationship, EntityReference related)
    {
        lock (gate) { EnsureActive(); return associations.Contains((Name(entityName), id, Name(relationship), Name(related.LogicalName), related.Id)); }
    }

    /// <summary>排空後清除所有本案例資料與捕捉回呼，重複 Dispose 安全；之後拒絕操作。</summary>
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true; rows.Clear(); associations.Clear(); overrides.Clear(); Calls.Clear();
            RetrieveHandler = null; RetrieveMultipleHandler = null; CreateHandler = null; UpdateHandler = null;
            DeleteHandler = null; ExecuteHandler = null; AssociateHandler = null; DisassociateHandler = null;
        }
    }

    /// <summary>驗證 request 參數白名單，不能悄悄忽略 concurrency、關聯讀取或抑制規則等設定。</summary>
    private static void Parameters(OrganizationRequest request, params string[] allowed)
    {
        foreach (var parameter in request.Parameters.Keys)
            if (!allowed.Contains(parameter, StringComparer.Ordinal)) throw Unsupported("Execute.Parameter." + parameter);
    }

    /// <summary>複製候選後以正式主鍵新增；容量及重複檢查均先於發布。</summary>
    private Guid CreateCore(Entity entity)
    {
        var candidate = CloneEntity(entity, 0);
        if (candidate.KeyAttributes.Count != 0 || candidate.RelatedEntities.Count != 0) throw Unsupported("Create.AlternateKeysOrRelatedEntities");
        candidate.LogicalName = Name(candidate.LogicalName);
        if (candidate.Id == Guid.Empty) candidate.Id = Guid.NewGuid();
        ValidateIdentity(candidate);
        if (rows.ContainsKey(Key(candidate.LogicalName, candidate.Id))) throw new InvalidOperationException("Duplicate identity: " + candidate.LogicalName);
        if (rows.Count >= Capacity) throw Unsupported("Entity capacity");
        candidate[candidate.LogicalName + "id"] = candidate.Id;
        rows.Add(Key(candidate.LogicalName, candidate.Id), candidate);
        return candidate.Id;
    }

    /// <summary>驗證補丁並建完整候選，失敗不發布；禁止以屬性更改正式識別。</summary>
    private void UpdateCore(Entity entity)
    {
        if (entity.KeyAttributes.Count != 0 || entity.RelatedEntities.Count != 0) throw Unsupported("Update.AlternateKeysOrRelatedEntities");
        ValidateIdentity(entity);
        var original = Find(entity.LogicalName, entity.Id);
        var patch = CloneEntity(entity, 0);
        var candidate = CloneEntity(original, 0);
        foreach (var attribute in patch.Attributes)
            if (attribute.Value is null) candidate.Attributes.Remove(attribute.Key); else candidate[attribute.Key] = attribute.Value;
        candidate[candidate.LogicalName + "id"] = candidate.Id;
        rows[Key(candidate.LogicalName, candidate.Id)] = candidate;
    }

    /// <summary>主鍵慣例只供本離線 subset；特殊主鍵名稱必須另行擴充，不能猜 metadata。</summary>
    private static void ValidateIdentity(Entity entity)
    {
        var primary = Name(entity.LogicalName) + "id";
        if (entity.Contains(primary) && (!(entity[primary] is Guid id) || id == Guid.Empty || (entity.Id != Guid.Empty && id != entity.Id)))
            throw new InvalidOperationException("Conflicting primary identity: " + primary);
    }

    /// <summary>刪除列與兩端關聯，不保存孤兒參考；呼叫前先確認記錄存在。</summary>
    private void DeleteCore(string entityName, Guid id)
    {
        Find(entityName, id);
        var name = Name(entityName);
        rows.Remove(Key(name, id));
        associations.RemoveWhere(edge => (edge.Name == name && edge.Id == id) || (edge.RelatedName == name && edge.RelatedId == id));
    }

    /// <summary>只支援明確非空 GUID 的 lookup；替代鍵與 row version 需要真實 SDK 情境覆蓋。</summary>
    private static void Reference(EntityReference reference)
    {
        if (reference.KeyAttributes.Count != 0 || !string.IsNullOrEmpty(reference.RowVersion)) throw Unsupported("EntityReference.AlternateKeysOrRowVersion");
        Name(reference.LogicalName);
        if (reference.Id == Guid.Empty) throw new InvalidOperationException("Missing identity");
    }

    /// <summary>全批建立候選邊，先驗證存在性、重複與容量，再一次發布，避免半套關聯。</summary>
    private void Relate(string name, Guid id, Relationship relationship, EntityReferenceCollection related, bool add)
    {
        Find(name, id);
        if (relationship.PrimaryEntityRole is not null) throw Unsupported("Relationship.PrimaryEntityRole");
        var candidate = new HashSet<(string, Guid, string, string, Guid)>();
        foreach (var target in related)
        {
            Reference(target); Find(target.LogicalName, target.Id);
            var edge = (Name(name), id, Name(relationship.SchemaName), Name(target.LogicalName), target.Id);
            if (!candidate.Add(edge) || associations.Contains(edge) == add) throw new InvalidOperationException("Association identity conflict");
        }
        if (add && associations.Count + candidate.Count > Capacity) throw Unsupported("Association capacity");
        foreach (var edge in candidate) if (add) associations.Add(edge); else associations.Remove(edge);
    }

    /// <summary>按實體與正式 GUID 精確查找，未找到以固定診斷失敗，不回空 Entity。</summary>
    private Entity Find(string name, Guid id) => rows.TryGetValue(Key(name, id), out var entity) ? entity : throw new InvalidOperationException("Record not found: " + Name(name));
    /// <summary>正規化 schema 名稱；不把使用者內容或顯示名稱當識別。</summary>
    private static string Name(string value) => !string.IsNullOrWhiteSpace(value) ? value.ToLowerInvariant() : throw new ArgumentException("Schema name required");
    /// <summary>單一案例內的資料表複合識別；不推測任何租戶授權。</summary>
    private static (string, Guid) Key(string name, Guid id) => (Name(name), id);
    /// <summary>先檢查生命週期再計數，失敗操作仍可在排空前斷言呼叫次數。</summary>
    private void Increment(string operation) { EnsureActive(); Calls[operation] = Calls.TryGetValue(operation, out var count) ? checked(count + 1) : 1; }
    /// <summary>Dispose 後不允許重新配置或使用，避免案例意外重用舊身份狀態。</summary>
    private void EnsureActive() { if (disposed) throw new ObjectDisposedException(nameof(FakeOrganizationService)); }
    /// <summary>只包含功能名稱的具名拒絕，不輸出合成欄位值或原查詢內容。</summary>
    private static NotSupportedException Unsupported(string feature) => new("FakeOrganizationService.Unsupported: " + feature);

    /// <summary>遞迴複製 SDK 常用值，深度與集合有上限；未知參考型別直接拒絕，絕不共用。</summary>
    private static object? CloneValue(object? value, int depth)
    {
        if (depth > 16) throw Unsupported("Entity graph depth");
        switch (value)
        {
            case null: return null;
            case string text: if (text.Length > 1048576) throw Unsupported("String capacity"); return text;
            case Guid or bool or byte or short or int or long or float or double or decimal or DateTime or DateTimeOffset: return value;
            case byte[] bytes: if (bytes.Length > 1048576) throw Unsupported("Byte array capacity"); return bytes.ToArray();
            case Money money: return new Money(money.Value);
            case OptionSetValue option: return new OptionSetValue(option.Value);
            case OptionSetValueCollection options:
                if (options.Count > Capacity) throw Unsupported("OptionSetValueCollection capacity");
                return new OptionSetValueCollection(options.Select(x => new OptionSetValue(x.Value)).ToList());
            case Entity entity: return CloneEntity(entity, depth + 1);
            case EntityCollection collection: return CloneCollection(collection, depth + 1);
            case AliasedValue alias: return new AliasedValue(alias.EntityLogicalName, alias.AttributeLogicalName, CloneValue(alias.Value, depth + 1));
            case EntityReference reference:
                var copy = new EntityReference(reference.LogicalName, reference.Id) { Name = reference.Name, RowVersion = reference.RowVersion };
                foreach (var key in reference.KeyAttributes) copy.KeyAttributes.Add(key.Key, CloneValue(key.Value, depth + 1));
                return copy;
            default: throw Unsupported("AttributeType." + value.GetType().Name);
        }
    }

    /// <summary>複製屬性、格式化顯示值及替代鍵；RelatedEntities 不屬此 subset，不能靜默遺失。</summary>
    private static Entity CloneEntity(Entity entity, int depth)
    {
        if (entity is null) throw new ArgumentNullException(nameof(entity));
        if (depth > 16 || entity.Attributes.Count > 1024) throw Unsupported("Entity graph capacity");
        if (entity.RelatedEntities.Count != 0) throw Unsupported("Entity.RelatedEntities");
        var result = new Entity(entity.LogicalName, entity.Id) { RowVersion = entity.RowVersion };
        foreach (var attribute in entity.Attributes) result.Attributes.Add(attribute.Key, CloneValue(attribute.Value, depth + 1));
        foreach (var key in entity.KeyAttributes) result.KeyAttributes.Add(key.Key, CloneValue(key.Value, depth + 1));
        foreach (var formatted in entity.FormattedValues) result.FormattedValues.Add(formatted.Key, formatted.Value);
        return result;
    }

    /// <summary>複製完整回應及 paging 診斷；顯式答案也須具有非空且唯一的正式實體識別。</summary>
    private static EntityCollection CloneCollection(EntityCollection source, int depth)
    {
        if (source.Entities.Count > Capacity || depth > 16) throw Unsupported("EntityCollection capacity");
        var result = new EntityCollection { EntityName = source.EntityName, MoreRecords = source.MoreRecords, PagingCookie = source.PagingCookie,
            TotalRecordCount = source.TotalRecordCount, TotalRecordCountLimitExceeded = source.TotalRecordCountLimitExceeded, MinActiveRowVersion = source.MinActiveRowVersion };
        var identities = new HashSet<(string, Guid)>();
        foreach (var entity in source.Entities)
        {
            if (entity.Id == Guid.Empty || !identities.Add(Key(entity.LogicalName, entity.Id))) throw new InvalidOperationException("Query result identity conflict");
            result.Entities.Add(CloneEntity(entity, depth + 1));
        }
        return result;
    }
}
