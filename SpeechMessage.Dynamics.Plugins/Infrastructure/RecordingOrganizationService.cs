/*
 * 檔案整體說明
 *
 * 目的與定位：單次 Plugin Execute 的 Shadow 組織服務，借用授權後的真服務讀取，
 * 將寫入轉成安全不可變投影，不執行實際寫入。Create 按建立序號回傳固定 GUID。
 * 主要功能與流程：同次新建列的 Update／Retrieve 使用深複製 overlay，Delete 留墓碑；
 * RetrieveMultiple 永遠只讀真服務，不合併 overlay。Execute 只轉送明列的唯讀訊息，
 * 已知 CRUD／關聯寫入走相同記錄路徑，未知、批次及缺參數訊息均拒絕且不回顯輸入。
 * 讀取結果允許經逐段驗證的 alias.field；寫入仍只接受實際 schema 欄位。
 * RelatedEntities 尚未支援，任何複製路徑都明確拒絕，不能靜默遺失相關寫入。
 * 使用邊界與維護：每次 Execute 獨占實例，不支援同實例平行使用；overlay 最多 64 列，
 * 每列最多 128 欄位、2048 值節點與 65536 字元／位元組，超量直接失敗，不靜默逐出。
 * 安全摘要最多 128 筆及 16000 字元，超量只累計總數並標記 Overflow。原始資料只供
 * 本次讀回，Clear／Dispose 確定清除，借用的 inner 不釋放；沒有可變 static、背景工作
 * 或連線所有權。錯誤收尾只可輸出安全投影，不讀原始 overlay。離線測試驗證隔離、
 * 隱私、白名單、容量及排空，不宣稱已驗證真實 Dataverse 的授權或保存政策。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpeechMessage.Dynamics.Plugins.Infrastructure;

/// <summary>不可變寫入意圖，只保存已過濾 JSON，不持有原始 Entity／可變值。</summary>
internal sealed record ShadowOrganizationWrite(string Operation, string LogicalName, Guid Id, string ProjectionJson);

/// <summary>由單次 Execute 外殼擁有、借用讀取服務並攔截全部寫入的記錄器。</summary>
internal sealed class RecordingOrganizationService : IOrganizationService, IDisposable
{
    private readonly IOrganizationService inner;
    private readonly List<ShadowOrganizationWrite> writes = new();
    private readonly Dictionary<string, Entity?> localRows = new(StringComparer.Ordinal);
    private long createSequence;
    private int projectionCharacters;
    private bool disposed;

    /// <summary>借用平台授權的 service，不取得其 Dispose 所有權。</summary>
    public RecordingOrganizationService(IOrganizationService inner) => this.inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>不可變投影的 detached 陣列，呼叫端無法改變內部清單。</summary>
    public IReadOnlyList<ShadowOrganizationWrite> Writes => new ReadOnlyCollection<ShadowOrganizationWrite>(writes.ToArray());
    /// <summary>包括未保留摘要的有效寫入意圖總數，供 overflow 診斷。</summary>
    public long TotalCount { get; private set; }
    /// <summary>摘要是否因容量不足而不完整，不得將此狀態判成零差異。</summary>
    public bool Overflow { get; private set; }

    /// <summary>新建列以 overlay 依 ColumnSet 回應；其他列借用真服務並回傳深複製。</summary>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    {
        EnsureActive();
        if (columnSet is null) throw new ArgumentNullException(nameof(columnSet));
        if (!localRows.TryGetValue(Key(entityName, id), out var row)) return CloneEntity(inner.Retrieve(entityName, id, columnSet));
        if (row is null) throw new InvalidOperationException("Shadow row was deleted.");
        var result = CloneEntity(row);
        if (!columnSet.AllColumns)
            foreach (var field in result.Attributes.Keys.Where(field => !columnSet.Columns.Contains(field)).ToArray()) result.Attributes.Remove(field);
        return result;
    }

    /// <summary>永遠不合併 overlay；回傳深複製，避免 provider 的可變 Entity 被規則修改。</summary>
    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        EnsureActive();
        if (query is null) throw new ArgumentNullException(nameof(query));
        var source = inner.RetrieveMultiple(query);
        var result = new EntityCollection
        {
            EntityName = source.EntityName, MoreRecords = source.MoreRecords, PagingCookie = source.PagingCookie,
            TotalRecordCount = source.TotalRecordCount, TotalRecordCountLimitExceeded = source.TotalRecordCountLimitExceeded,
            MinActiveRowVersion = source.MinActiveRowVersion
        };
        foreach (var entity in source.Entities) result.Entities.Add(CloneEntity(entity));
        return result;
    }

    /// <summary>依 Create 序號產生 ID，完整驗證候選及安全投影後才發布，與其他寫入數無關。</summary>
    public Guid Create(Entity entity)
    {
        EnsureActive();
        var candidate = CloneEntity(entity, allowAliases: false);
        var sequence = checked(createSequence + 1);
        var id = entity.Id == Guid.Empty
            ? SpeechMessageProducts.GoodShepherd.Core.Rules.DeterministicGuid.Create("shadow-create", sequence, candidate.LogicalName) : entity.Id;
        candidate.Id = id;
        var key = Key(candidate.LogicalName, id);
        if (localRows.ContainsKey(key)) throw new InvalidOperationException("Duplicate shadow row identity.");
        if (localRows.Count >= 64) throw new InvalidOperationException("Shadow overlay capacity exceeded.");
        var projection = Project("Create", candidate);
        localRows.Add(key, candidate);
        createSequence = sequence;
        Add("Create", candidate.LogicalName, id, projection);
        return id;
    }

    /// <summary>同次新建列合併 patch；既有列僅記錄，不額外讀取或假造完整資料。</summary>
    public void Update(Entity entity)
    {
        EnsureActive();
        var patch = CloneEntity(entity, allowAliases: false);
        var key = Key(patch.LogicalName, patch.Id);
        Entity? candidate = null;
        if (localRows.TryGetValue(key, out var previous))
        {
            if (previous is null) throw new InvalidOperationException("Shadow row was deleted.");
            candidate = CloneEntity(previous);
            foreach (var pair in patch.Attributes) candidate[pair.Key] = pair.Value;
            candidate = CloneEntity(candidate);
        }
        var projection = Project("Update", patch);
        if (candidate is not null) localRows[key] = candidate;
        Add("Update", patch.LogicalName, patch.Id, projection);
    }

    /// <summary>只記錄刪除；新建列留下有界墓碑，後續回讀不穿透到真服務。</summary>
    public void Delete(string entityName, Guid id)
    {
        EnsureActive();
        var key = Key(entityName, id);
        var projection = Project("Delete", new Entity(entityName, id));
        if (localRows.ContainsKey(key)) localRows[key] = null;
        Add("Delete", entityName, id, projection);
    }

    /// <summary>記錄 schema 與實際 related references，不執行真實關聯操作。</summary>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
        RecordRelationship("Associate", entityName, entityId, relationship, relatedEntities);
    /// <summary>記錄解除關聯的 reference 身分，不輸出顯示姓名。</summary>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
        RecordRelationship("Disassociate", entityName, entityId, relationship, relatedEntities);

    /// <summary>明確白名單分類讀寫；未知／複合批次／缺參數訊息固定拒絕且不回顯輸入。</summary>
    public OrganizationResponse Execute(OrganizationRequest request)
    {
        EnsureActive();
        if (request is null) throw new ArgumentNullException(nameof(request));
        switch (request.RequestName)
        {
            case "WhoAmI": case "RetrieveCurrentOrganization": case "RetrievePrincipalAccess":
            case "RetrieveEntity": case "RetrieveAttribute": case "RetrieveAllEntities":
            case "RetrieveRelationship": case "RetrieveMetadataChanges":
                return inner.Execute(request);
            case "Retrieve":
                var target = Parameter<EntityReference>(request, "Target");
                var retrieved = new RetrieveResponse();
                retrieved.Results["Entity"] = Retrieve(target.LogicalName, target.Id, Parameter<ColumnSet>(request, "ColumnSet"));
                return retrieved;
            case "RetrieveMultiple":
                var collection = new RetrieveMultipleResponse();
                collection.Results["EntityCollection"] = RetrieveMultiple(Parameter<QueryBase>(request, "Query"));
                return collection;
            case "Create":
                var created = new CreateResponse();
                created.Results["id"] = Create(Parameter<Entity>(request, "Target"));
                return created;
            case "Update":
                Update(Parameter<Entity>(request, "Target")); return new UpdateResponse();
            case "Delete":
                var deleted = Parameter<EntityReference>(request, "Target");
                Delete(deleted.LogicalName, deleted.Id); return new DeleteResponse();
            case "Associate": case "Disassociate":
                var primary = Parameter<EntityReference>(request, "Target");
                RecordRelationship(request.RequestName, primary.LogicalName, primary.Id,
                    Parameter<Relationship>(request, "Relationship"), Parameter<EntityReferenceCollection>(request, "RelatedEntities"));
                return request.RequestName == "Associate" ? new AssociateResponse() : new DisassociateResponse();
            default: throw new NotSupportedException("Unsupported organization request.");
        }
    }

    /// <summary>清除原始 overlay、安全摘要及計數；由本次 Execute 的唯一 owner 呼叫。</summary>
    public void Clear()
    {
        localRows.Clear(); writes.Clear(); createSequence = 0; projectionCharacters = 0; TotalCount = 0; Overflow = false;
    }
    /// <summary>確定排空並永久停用；不 Dispose 借用的真服務。</summary>
    public void Dispose() { Clear(); disposed = true; }

    /// <summary>提供 PluginInput 共用的有界深複製，未知可變值、循環與超量圖形固定拒絕。</summary>
    internal static Entity CloneEntity(Entity source, bool allowAliases = true)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return (Entity)CloneValue(source, new CloneBudget(allowAliases), 0)!;
    }

    /// <summary>每次 Clone 獨立的容量預算，阻止無界遞迴及單列保留。</summary>
    private sealed class CloneBudget
    {
        /// <summary>只有讀取快照可以保存 alias.field；寫入不能用查詢別名冒充 schema。</summary>
        internal readonly bool AllowAliases;
        /// <summary>以本次操作角色建立不共享的容量與欄位政策。</summary>
        internal CloneBudget(bool allowAliases) => AllowAliases = allowAliases;
        /// <summary>整個單列圖形剩餘值節點，包含集合及 reference。</summary>
        internal int Nodes = 2048;
        /// <summary>原文及二進位總容量，單列最多 65536 單位。</summary>
        internal int Units = 65536;
    }

    /// <summary>複製明列 SDK 資料值與不可變 scalar；永不將未知物件交給 ToString 或序列化器。</summary>
    private static object? CloneValue(object? value, CloneBudget budget, int depth)
    {
        if (--budget.Nodes < 0 || depth > 8) throw new NotSupportedException("Shadow value graph capacity exceeded.");
        if (value is null) return null;
        if (value is string text)
        {
            budget.Units -= text.Length;
            if (budget.Units < 0) throw new NotSupportedException("Shadow value size exceeded.");
            return text;
        }
        if (value is bool or char or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or Guid or DateTime or DateTimeOffset or TimeSpan) return value;
        if (value is byte[] bytes)
        {
            budget.Units -= bytes.Length;
            if (budget.Units < 0) throw new NotSupportedException("Shadow value size exceeded.");
            return bytes.Clone();
        }
        if (value is Entity entity)
        {
            ValidateName(entity.LogicalName);
            if (entity.RelatedEntities.Count != 0) throw new NotSupportedException("Shadow.Unsupported.RelatedEntities");
            if (entity.Attributes.Count > 128 || entity.KeyAttributes.Count > 128 || entity.FormattedValues.Count > 128)
                throw new NotSupportedException("Shadow field capacity exceeded.");
            var copy = new Entity(entity.LogicalName, entity.Id) { RowVersion = (string?)CloneValue(entity.RowVersion, budget, depth + 1) };
            foreach (var pair in entity.Attributes)
            {
                ValidateAttributeName(pair.Key, budget.AllowAliases && pair.Value is AliasedValue);
                copy[pair.Key] = CloneValue(pair.Value, budget, depth + 1);
            }
            foreach (var pair in entity.KeyAttributes) { ValidateName(pair.Key); copy.KeyAttributes[pair.Key] = CloneValue(pair.Value, budget, depth + 1); }
            foreach (var pair in entity.FormattedValues)
            {
                ValidateAttributeName(pair.Key, budget.AllowAliases);
                copy.FormattedValues.Add(pair.Key, (string)CloneValue(pair.Value, budget, depth + 1)!);
            }
            return copy;
        }
        if (value is EntityReference reference)
        {
            ValidateName(reference.LogicalName);
            if (reference.KeyAttributes.Count > 128) throw new NotSupportedException("Shadow key capacity exceeded.");
            var copy = new EntityReference(reference.LogicalName, reference.Id)
            {
                Name = (string?)CloneValue(reference.Name, budget, depth + 1), RowVersion = (string?)CloneValue(reference.RowVersion, budget, depth + 1)
            };
            foreach (var pair in reference.KeyAttributes) { ValidateName(pair.Key); copy.KeyAttributes[pair.Key] = CloneValue(pair.Value, budget, depth + 1); }
            return copy;
        }
        if (value is OptionSetValue option) return new OptionSetValue(option.Value);
        if (value is Money money) return new Money(money.Value);
        if (value is AliasedValue alias)
            return new AliasedValue((string)CloneValue(alias.EntityLogicalName, budget, depth + 1)!,
                (string)CloneValue(alias.AttributeLogicalName, budget, depth + 1)!, CloneValue(alias.Value, budget, depth + 1));
        if (value is EntityCollection collection)
        {
            if (collection.Entities.Count > 128) throw new NotSupportedException("Shadow collection capacity exceeded.");
            var copy = new EntityCollection
            {
                EntityName = (string?)CloneValue(collection.EntityName, budget, depth + 1), MoreRecords = collection.MoreRecords,
                PagingCookie = (string?)CloneValue(collection.PagingCookie, budget, depth + 1), TotalRecordCount = collection.TotalRecordCount,
                TotalRecordCountLimitExceeded = collection.TotalRecordCountLimitExceeded,
                MinActiveRowVersion = (string?)CloneValue(collection.MinActiveRowVersion, budget, depth + 1)
            };
            foreach (var item in collection.Entities) copy.Entities.Add((Entity)CloneValue(item, budget, depth + 1)!);
            return copy;
        }
        if (value is EntityReferenceCollection references)
        {
            if (references.Count > 128) throw new NotSupportedException("Shadow collection capacity exceeded.");
            var copy = new EntityReferenceCollection();
            foreach (var item in references) copy.Add((EntityReference)CloneValue(item, budget, depth + 1)!);
            return copy;
        }
        if (value is OptionSetValueCollection options)
        {
            if (options.Count > 128) throw new NotSupportedException("Shadow collection capacity exceeded.");
            var copy = new OptionSetValueCollection();
            foreach (var item in options) copy.Add((OptionSetValue)CloneValue(item, budget, depth + 1)!);
            return copy;
        }
        if (value is Entity[] entities)
        {
            if (entities.Length > 128) throw new NotSupportedException("Shadow collection capacity exceeded.");
            return entities.Select(item => (Entity)CloneValue(item, budget, depth + 1)!).ToArray();
        }
        throw new NotSupportedException("Unsupported mutable shadow value.");
    }

    /// <summary>檢查 schema 名稱的長度及 ASCII 識別字元，禁止任意內容成為 JSON 屬性。</summary>
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(character => !(char.IsLetterOrDigit(character) && character < 128) && character != '_'))
            throw new ArgumentException("Invalid schema name.");
    }
    /// <summary>讀取別名恰為兩段合法 schema；其他路徑沿用嚴格實際欄位驗證。</summary>
    private static void ValidateAttributeName(string name, bool allowAlias)
    {
        var separator = name.IndexOf('.');
        if (!allowAlias || separator < 0)
        {
            ValidateName(name);
            return;
        }
        ValidateName(name.Substring(0, separator));
        ValidateName(name.Substring(separator + 1));
    }
    /// <summary>以字串邊界建立 key，避免 net462 引入 ValueTuple 相依。</summary>
    private static string Key(string name, Guid id)
    {
        ValidateName(name);
        if (id == Guid.Empty) throw new ArgumentException("Row identity is required.");
        return name + ":" + id.ToString("D", CultureInfo.InvariantCulture);
    }
    /// <summary>取得白名單 request 的必要具型別參數，失敗不回顯輸入。</summary>
    private static T Parameter<T>(OrganizationRequest request, string name) where T : class =>
        request.Parameters.TryGetValue(name, out var value) && value is T typed ? typed : throw new NotSupportedException("Unsupported organization request parameters.");

    /// <summary>完整快照及投影後才記錄關聯，保留每個 related reference 的順序及身分。</summary>
    private void RecordRelationship(string operation, string name, Guid id, Relationship relationship, EntityReferenceCollection related)
    {
        EnsureActive();
        if (relationship is null) throw new ArgumentNullException(nameof(relationship));
        if (related is null) throw new ArgumentNullException(nameof(related));
        ValidateName(relationship.SchemaName);
        _ = Key(name, id);
        var holder = CloneEntity(new Entity(name, id) { ["related"] = related });
        var projection = BaseProjection(operation, name, id);
        projection["relationship"] = relationship.SchemaName;
        if (relationship.PrimaryEntityRole.HasValue) projection["role"] = relationship.PrimaryEntityRole.Value.ToString();
        projection["related"] = ProjectValue(holder["related"]);
        Add(operation, name, id, projection.ToString(Formatting.None));
    }
    /// <summary>超量後停止保留新摘要而只累計總數，防止規則大量寫入造成無界保留。</summary>
    private void Add(string operation, string name, Guid id, string projection)
    {
        TotalCount = checked(TotalCount + 1);
        if (Overflow || writes.Count >= 128 || projection.Length > 16000 - projectionCharacters) { Overflow = true; return; }
        writes.Add(new ShadowOrganizationWrite(operation, name, id, projection));
        projectionCharacters += projection.Length;
    }
    /// <summary>拒絕已排空的 owner 被重新用於另一個執行。</summary>
    private void EnsureActive()
    {
        if (disposed) throw new ObjectDisposedException(nameof(RecordingOrganizationService));
    }
    /// <summary>建立完整欄位投影，不對 SDK Entity 或 record 直接 ToString／序列化。</summary>
    private static string Project(string operation, Entity entity)
    {
        var result = BaseProjection(operation, entity.LogicalName, entity.Id);
        result["fields"] = ProjectFields(entity.Attributes);
        if (entity.KeyAttributes.Count > 0) result["keys"] = ProjectFields(entity.KeyAttributes);
        return result.ToString(Formatting.None);
    }

    /// <summary>安全摘要往返保留 decimal 精度與日期字串，避免依主機時區重寫業務值。</summary>
    internal static JObject ReadProjection(string json)
    {
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text)
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
            MaxDepth = 32
        };
        return JObject.Load(reader);
    }
    /// <summary>固定版本一操作外框，writer 再加入輸出序號；不假造巢狀未存檔 Entity 的 ID。</summary>
    private static JObject BaseProjection(string operation, string name, Guid id) => new()
    {
        ["operation"] = operation, ["entity"] = name, ["id"] = id.ToString("D", CultureInfo.InvariantCulture)
    };
    /// <summary>依 Ordinal 排序欄位；省略機密及訊息原文，個資及長值只保留長度與雜湊。</summary>
    private static JObject ProjectFields(IEnumerable<KeyValuePair<string, object>> fields)
    {
        var result = new JObject();
        foreach (var pair in fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (IsExcluded(pair.Key)) continue;
            var projected = ProjectValue(pair.Value);
            if (projected is null) continue;
            if (IsPersonal(pair.Key) && pair.Value is not null) projected = Hash(pair.Value as string ?? projected.ToString(Formatting.None));
            result[pair.Key] = projected;
        }
        return result;
    }
    /// <summary>投影已深複製的已知 SDK 值；reference 不輸出顯示名稱，alias 依真正欄位再過濾。</summary>
    private static JToken? ProjectValue(object? value)
    {
        switch (value)
        {
            case null: return JValue.CreateNull();
            case string text:
                if (ContainsLineId(text) || text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
                return text.Length > 120 ? Hash(text) : new JValue(text);
            case EntityReference reference:
                var target = new JObject { ["entity"] = reference.LogicalName, ["id"] = reference.Id.ToString("D", CultureInfo.InvariantCulture) };
                if (reference.KeyAttributes.Count > 0) target["keys"] = ProjectFields(reference.KeyAttributes);
                return target;
            case OptionSetValue option: return new JValue(option.Value);
            case Money money: return new JValue(money.Value);
            case byte[] bytes: return Hash(bytes);
            case AliasedValue alias:
                if (IsExcluded(alias.AttributeLogicalName)) return null;
                var projected = ProjectValue(alias.Value);
                return IsPersonal(alias.AttributeLogicalName) && projected is not null ? Hash(alias.Value as string ?? projected.ToString(Formatting.None)) : projected;
            case Entity entity: return ReadProjection(Project("Entity", entity));
            case EntityCollection collection: return new JArray(collection.Entities.Select(ProjectValue));
            case EntityReferenceCollection references: return new JArray(references.Select(ProjectValue));
            case OptionSetValueCollection options: return new JArray(options.Select(ProjectValue));
            case Entity[] entities: return new JArray(entities.Select(ProjectValue));
            default: return new JValue(value);
        }
    }
    /// <summary>集中列出不得進入追蹤的機密及訊息原文字段，涵蓋帶前綴的自訂 schema。</summary>
    private static bool IsExcluded(string field) =>
        Contains(field, "token") || Contains(field, "secret") || Contains(field, "password") || Contains(field, "credential") ||
        Contains(field, "cookie") || Contains(field, "lineid") || Contains(field, "line_id") || Contains(field, "content") ||
        Contains(field, "body") || Contains(field, "message") || Contains(field, "description") || Contains(field, "subject") || Contains(field, "text");
    /// <summary>個資欄位即使短字串也只記雜湊，保守涵蓋常見自訂名稱。</summary>
    private static bool IsPersonal(string field) =>
        string.Equals(field, "new_personal_id", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "new_cell_hpone", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "company", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "new_last_six_digit", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "new_fb_account", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "new_ig_account", StringComparison.OrdinalIgnoreCase) ||
        Contains(field, "name") || Contains(field, "phone") || Contains(field, "tel") ||
        Contains(field, "mobile") || Contains(field, "email") || Contains(field, "address") || Contains(field, "birth");
    /// <summary>不依賴 regex 或全域文化的欄位片段比對。</summary>
    private static bool Contains(string value, string fragment) => value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    /// <summary>偵測內嵌 LINE user／group／room ID，欄位命名錯誤時也不可輸出原值。</summary>
    internal static bool ContainsLineId(string text)
    {
        for (var index = 0; index + 32 < text.Length; index++)
        {
            if (text[index] is not ('U' or 'C' or 'R')) continue;
            var matches = true;
            for (var digit = 1; digit <= 32; digit++)
            {
                var c = text[index + digit];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) { matches = false; break; }
            }
            if (matches) return true;
        }
        return false;
    }
    /// <summary>以固定 UTF-8 區塊計算字串雜湊，不配置與完整訊息等長的 byte 陣列。</summary>
    private static JObject Hash(string text)
    {
        using var sha = SHA256.Create();
        var encoder = Encoding.UTF8.GetEncoder();
        var chars = new char[1024];
        var bytes = new byte[4096];
        for (var offset = 0; offset < text.Length;)
        {
            var length = Math.Min(chars.Length, text.Length - offset);
            text.CopyTo(offset, chars, 0, length); offset += length;
            var count = encoder.GetBytes(chars, 0, length, bytes, 0, offset == text.Length);
            sha.TransformBlock(bytes, 0, count, bytes, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return new JObject { ["length"] = text.Length, ["sha256"] = Hex(sha.Hash!) };
    }
    /// <summary>二進位值只輸出長度及雜湊，SHA-256 owner 在呼叫結束確定釋放。</summary>
    private static JObject Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return new JObject { ["length"] = bytes.Length, ["sha256"] = Hex(sha.ComputeHash(bytes)) };
    }
    /// <summary>固定小寫十六進位輸出，不依賴新版框架 API 或全域文化。</summary>
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
}
