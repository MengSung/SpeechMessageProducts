/*
檔案整體說明
目的與定位：FS UI 匯入的結構發布護欄。PublishXml 以實體為粒度，即使 ZIP 僅含畫面，
仍可能發布同表其他人的未審結構；因此不能以表單／檢視比對或 SchemaSnapshot 的規劃投影代替。
主要功能與流程：僅對明列的影響表送 RetrieveEntity（All），分別讀已發布與包含未發布的定義；
遞迴投影 SDK DataMember 的完整具體型別與純值，逐項核對，不比較訊息字串或 metadata JSON。
涵蓋標籤、managed property、所有欄位子型別、選項、關聯 cascade 與鍵；僅略過 MetadataBase
的 HasChanged 傳輸旗標。未知 ExtensionData、型別、缺少必要集合及重複正式身分一律拒絕。
有界與隔離：最多256表、每表2048欄／32鍵、每集合4096項、深度24；兩來源合計最多
一百萬投影節點、64MiB UTF-8 路徑／文字，單文字一 MiB。超量不截斷、不回傳部分快照。
SDK 回應只留在目前同步投影；回傳快照只含字串、CLR 純值與 Guid，最長活到本次 Import／Apply 結束。
不讀業務資料、不建快取或背景工作。每個 SDK 前後及逐節點檢查取消，呼叫返回才交外層處理。
擁有權與驗證：service 與 transport timeout／組織寫鎖由外層命令 lease 擁有，本檔不保存或
Dispose。匯入前拒絕既存 pending，匯入後同時拒絕 pending 與基準漂移；schema Apply 僅允許
寫前完整 wire 候選加正式建立 ID 的欄位／雙端關聯／鍵／局部選項增量，不忽略新欄子樹或重新信任寫後來源；本計畫正式新lookup僅接受明列、固定型別及值的伺服器補值。不能防止外部管理員在
最後一次核對與發布之間寫入，組織維護仍須保持獨占。合成 SDK 測試不代表實機發布證據。
*/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>只讀影響表的完整結構定義，拒絕 UI 發布帶出未審結構；SDK 所有權不轉移。</summary>
internal static class UiMetadataPublicationGuard
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// 完整讀取兩來源並比較；匯入後另與匯入前基準比對，防止兩來源同時變動而誤當無漂移。
    /// schema Apply 可帶入寫前純值新欄增量，分別驗兩來源的精確預期，不以來源相等取代舊基準。
    /// 回傳候選不持有 SDK 物件；任一取消、SDK 故障或完整性問題皆不送寫入、不回傳部分資料。
    /// </summary>
    internal static Snapshot Check(IOrganizationService service, IEnumerable<string> entityNames,
        CancellationToken ct, Snapshot? baseline = null, SchemaDelta? delta = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(entityNames);
        ct.ThrowIfCancellationRequested();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in entityNames)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 ||
                !name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') ||
                !names.Add(name) || names.Count > 256)
                throw Invalid();
        }
        if (names.Count == 0) throw Invalid();
        var budget = new ProjectionBudget();
        var published = new Dictionary<string, Scalar>(StringComparer.Ordinal);
        var unpublished = new Dictionary<string, Scalar>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            Read(service, name, false, published, budget, ct);
            Read(service, name, true, unpublished, budget, ct);
        }
        if (delta is { Additions.Count: > 0 })
        {
            if (baseline is null) throw Invalid();
            Equal(WithAdditions(baseline, delta.Additions, published, false, ct), published, ct);
            Equal(WithAdditions(baseline, delta.Additions, unpublished, true, ct), unpublished, ct);
        }
        else
        {
            Equal(published, unpublished, ct);
            if (baseline is not null) Equal(baseline.Values, published, ct);
        }
        ct.ThrowIfCancellationRequested();
        return new Snapshot(new ReadOnlyDictionary<string, Scalar>(published));
    }

    /// <summary>
    /// 本次 Apply 唯一擁有的寫前增量與共用容量計數；只保存純值，不持有 SDK 或取消註冊。
    /// 最多2048個候選、6144個新增原子樹；完整投影共用一百萬節點／64MiB預算，命令結束即回收。
    /// </summary>
    internal sealed class SchemaDelta
    {
        private readonly ProjectionBudget budget = new();
        private readonly List<NodeAddition> additions = new();
        private int captures;

        /// <summary>完整護欄同步消費的唯讀視圖，不能交背景工作或當下一命令的基準。</summary>
        internal IReadOnlyList<NodeAddition> Additions => additions.AsReadOnly();

        /// <summary>寫前脫離一般欄完整 wire 候選；不讀寫後來源，正式 ID 稍後由 SDK 回應綁定。</summary>
        internal AttributeAddition CaptureAttribute(string entity, AttributeMetadata attribute, CancellationToken ct)
        {
            Reserve(ct);
            return new AttributeAddition(entity, attribute.SchemaName, Guid.Empty, Capture(attribute, ct));
        }

        /// <summary>一般欄同步建立成功後只綁非空正式 ID，完整舊基準與新欄成員都繼續受檢。</summary>
        internal void Add(AttributeAddition addition, Guid id) => Append(new NodeAddition(addition.Entity,
            "Attributes", Identity(id), Bind(addition.Values, id, addition.Name), 2048));

        /// <summary>
        /// 在 CreateOneToMany 前複製查閱與雙端關聯；Targets／ReferencingAttribute只由明列請求推導。
        /// 不修改 request；新表指向舊表也捕捉，護欄只在最初已存在的端點疊加。
        /// </summary>
        internal LookupAddition CaptureLookup(string entity, LookupAttributeMetadata attribute,
            OneToManyRelationshipMetadata relationship, CancellationToken ct)
        {
            var captured = CaptureAttribute(entity, attribute, ct);
            if (relationship.ReferencingEntity != entity || string.IsNullOrWhiteSpace(relationship.ReferencedEntity)) throw Invalid();
            var lookup = new Dictionary<string, Scalar>(captured.Values, StringComparer.Ordinal);
            // 建立請求未帶 Targets 時，其唯一核准來源就是同一請求的 referenced entity。
            if (attribute.Targets is not null && (attribute.Targets.Length != 1 || attribute.Targets[0] != relationship.ReferencedEntity)) throw Invalid();
            lookup[".Targets"] = new Scalar("Collection", 1);
            lookup[".Targets[text:" + relationship.ReferencedEntity + "]"] = new Scalar(typeof(string).FullName!, relationship.ReferencedEntity);
            var relation = new Dictionary<string, Scalar>(Capture(relationship, ct), StringComparer.Ordinal);
            if (relationship.ReferencingAttribute is not null && relationship.ReferencingAttribute != attribute.SchemaName) throw Invalid();
            relation[".ReferencingAttribute"] = new Scalar(typeof(string).FullName!, attribute.SchemaName);
            return new LookupAddition(captured with { Values = new ReadOnlyDictionary<string, Scalar>(lookup) },
                relationship.ReferencedEntity, new ReadOnlyDictionary<string, Scalar>(relation));
        }

        /// <summary>
        /// 本計畫CreateLookup專用入口；只有寫前無正式ID且完整命名／目標及Merge均吻合的新關聯可啟用固定補值。
        /// 呼叫者必須已完整重驗plan；舊關聯仍使用CaptureLookup的精確契約，不能用此旗標重新信任寫後來源。
        /// </summary>
        internal LookupAddition CapturePlannedLookup(string entity, LookupAttributeMetadata attribute,
            OneToManyRelationshipMetadata relationship, CancellationToken ct)
        {
            if (relationship.MetadataId is not null || attribute.MetadataId is not null ||
                relationship.SchemaName != "new_" + entity + "_" + attribute.SchemaName ||
                relationship.CascadeConfiguration?.Merge !=
                    (relationship.ReferencedEntity is "account" or "contact" ? CascadeType.Cascade : CascadeType.NoCascade))
                throw Invalid();
            return CaptureLookup(entity, attribute, relationship, ct) with { AllowServerNormalization = true };
        }

        /// <summary>僅依正式回應綁定 lookup 與 relationship 身分；自我關聯仍保留兩個不同集合的完整原子樹。</summary>
        internal void AddLookup(LookupAddition candidate, Guid attributeId, Guid relationshipId)
        {
            _ = Identity(attributeId); _ = Identity(relationshipId);
            Add(candidate.Attribute, attributeId);
            var relation = Bind(candidate.Relationship, relationshipId, null);
            Append(new NodeAddition(candidate.Attribute.Entity, "ManyToOneRelationships", Identity(relationshipId), relation, 4096, IsNewPlannedRelationship: candidate.AllowServerNormalization));
            Append(new NodeAddition(candidate.ReferencedEntity, "OneToManyRelationships", Identity(relationshipId), relation, 4096, IsNewPlannedRelationship: candidate.AllowServerNormalization));
        }

        /// <summary>新鍵寫前複製全部欄位與標籤；沒有正式 ID 或索引結果的 request 不授權其他回應預設。</summary>
        internal KeyAddition CaptureKey(string entity, EntityKeyMetadata key, CancellationToken ct)
        {
            Reserve(ct);
            return new KeyAddition(entity, key.SchemaName, Capture(key, ct));
        }

        /// <summary>正式新鍵唯一例外為已明列的非同步索引狀態；Failed／未知值在比較時一律拒絕。</summary>
        internal void AddKey(KeyAddition candidate, Guid id) => Append(new NodeAddition(candidate.Entity,
            "Keys", Identity(id), Bind(candidate.Values, id, candidate.Name), 32, true));

        /// <summary>捕捉局部選項的穩定 Value 與完整 Label／其他 wire 成員；父欄由原基準正式身份解析。</summary>
        internal OptionAddition CaptureOption(string entity, string attribute, OptionMetadata option, CancellationToken ct)
        {
            Reserve(ct);
            if (option.Value is null) throw Invalid();
            return new OptionAddition(entity, attribute, option.Value.Value, Capture(option, ct));
        }

        /// <summary>呼叫者先核對 InsertOptionValueResponse.NewOptionValue，再加入原基準局部選項的精確增量。</summary>
        internal void AddOption(OptionAddition candidate) => Append(new NodeAddition(candidate.Entity,
            "OptionSet.Options", "option:" + candidate.Value.ToString(CultureInfo.InvariantCulture), candidate.Values, 4096,
            AttributeName: candidate.Attribute));

        /// <summary>每候選共用數量與取消邊界；不因捕捉未加入而重設記憶體預算。</summary>
        private void Reserve(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (++captures > 2048) throw Invalid();
        }

        /// <summary>SDK 物件僅同步投影，不保存來源；未知 wire／容量／取消直接保留原拒絕。</summary>
        private ReadOnlyDictionary<string, Scalar> Capture(object value, CancellationToken ct)
        {
            var values = new Dictionary<string, Scalar>(StringComparer.Ordinal);
            Project(value, "", values, budget, 0, ct);
            return new ReadOnlyDictionary<string, Scalar>(values);
        }

        /// <summary>只允許建立成功的正式身分與 unmanaged／logical name，其他 scalar 不從寫後來源採信。</summary>
        private static ReadOnlyDictionary<string, Scalar> Bind(IReadOnlyDictionary<string, Scalar> values, Guid id, string? name)
        {
            _ = Identity(id);
            var bound = new Dictionary<string, Scalar>(values, StringComparer.Ordinal)
            {
                [".MetadataId"] = new Scalar(typeof(Guid).FullName!, id),
                [".IsManaged"] = new Scalar(typeof(bool).FullName!, false)
            };
            if (name is not null)
            {
                if (string.IsNullOrWhiteSpace(name)) throw Invalid();
                bound[".LogicalName"] = new Scalar(typeof(string).FullName!, name);
            }
            return new ReadOnlyDictionary<string, Scalar>(bound);
        }

        /// <summary>原子樹數量亦有上限；未採用全域或跨組織可變狀態。</summary>
        private void Append(NodeAddition addition)
        {
            if (additions.Count >= 6144) throw Invalid();
            additions.Add(addition);
        }
    }

    /// <summary>
    /// 在原純值基準加精確新原子樹；每個舊節點仍逐項核對，不把整張寫後表當新基準。
    /// published 可缺本次新增，unpublished 必須有；不存在的基準端點是本次新表，不新增 RetrieveEntity。
    /// 父欄索引只掃基準一次，避免每個選項重新遍歷整份 metadata 的平方成本。
    /// </summary>
    private static Dictionary<string, Scalar> WithAdditions(Snapshot baseline, IReadOnlyList<NodeAddition> additions,
        Dictionary<string, Scalar> actual, bool requireAll, CancellationToken ct)
    {
        var expected = new Dictionary<string, Scalar>(baseline.Values, StringComparer.Ordinal);
        var fields = new Dictionary<(string Entity, string Attribute), string>();
        foreach (var pair in baseline.Values)
        {
            ct.ThrowIfCancellationRequested();
            const string middle = ".Attributes[id:";
            var split = pair.Key.IndexOf(middle, StringComparison.Ordinal);
            if (split > 0 && pair.Key.EndsWith("].LogicalName", StringComparison.Ordinal) && pair.Value.Value is string name)
                if (!fields.TryAdd((pair.Key[..split], name), pair.Key[..^12])) throw Invalid();
        }
        var budget = new ProjectionBudget();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var addition in additions)
        {
            ct.ThrowIfCancellationRequested();
            if (!baseline.Values.ContainsKey(addition.Entity)) continue;
            var collection = addition.Entity + "." + addition.Collection;
            if (addition.AttributeName is not null)
            {
                if (!fields.TryGetValue((addition.Entity, addition.AttributeName), out var parent)) throw Invalid();
                collection = parent + "." + addition.Collection;
            }
            var root = collection + "[" + addition.Identity + "]";
            if (!identities.Add(root) || baseline.Values.ContainsKey(root) ||
                !expected.TryGetValue(collection, out var count) || count.Type != "Collection" || count.Value is not int size || size >= addition.Maximum)
                throw Invalid();
            if (!actual.ContainsKey(root))
            {
                if (requireAll) throw Invalid();
                continue;
            }
            expected[collection] = new Scalar("Collection", size + 1);
            var plannedValues = addition.IsNewPlannedRelationship
                ? NewLookupServerDefaults.Complete(addition.Values, actual, root, ct) : addition.Values;
            foreach (var pair in plannedValues)
            {
                ct.ThrowIfCancellationRequested();
                var value = pair.Value;
                if (addition.IsKey && pair.Key == ".EntityKeyIndexStatus")
                {
                    // 官方 enum 僅 Pending=0／InProgress=1／Active=2；不是任意寫後狀態豁免。
                    if (!actual.TryGetValue(root + pair.Key, out var status) || status.Type != typeof(EntityKeyIndexStatus).FullName ||
                        status.Value is not long number || number is < 0 or > 2) throw Invalid();
                    value = status;
                }
                Put(root + pair.Key, value, expected, budget);
                if (expected.Count > 1_000_000) throw Invalid();
            }
        }
        return expected;
    }

    /// <summary>正式 metadata 原子樹身分；不允許空 ID 或暫時 request token 取代。</summary>
    private static string Identity(Guid id) => id != Guid.Empty ? "id:" + id.ToString("D") : throw Invalid();

    /// <summary>寫前一般欄純值；ID 僅可取本次正式 SDK 回應，不包含來源 SDK 參照。</summary>
    internal sealed record AttributeAddition(string Entity, string Name, Guid Id, IReadOnlyDictionary<string, Scalar> Values);
    /// <summary>查閱與兩端關聯同一次請求的純值候選；不授權未知 cascade 或額外 server 預設。</summary>
    internal sealed record LookupAddition(AttributeAddition Attribute, string ReferencedEntity, IReadOnlyDictionary<string, Scalar> Relationship,
        bool AllowServerNormalization = false);
    /// <summary>替代鍵寫前純值與明列邏輯名；正式 ID／合法非同步狀態於比較時獨立驗證。</summary>
    internal sealed record KeyAddition(string Entity, string Name, IReadOnlyDictionary<string, Scalar> Values);
    /// <summary>局部選項只認穩定 Value 與完整 wire 值，不以顯示名刪除／合併任何舊值。</summary>
    internal sealed record OptionAddition(string Entity, string Attribute, int Value, IReadOnlyDictionary<string, Scalar> Values);
    /// <summary>命令區域的封閉原子樹；父集合與身份明列，唯一動態狀態例外是本次新鍵。</summary>
    internal sealed record NodeAddition(string Entity, string Collection, string Identity, IReadOnlyDictionary<string, Scalar> Values,
        int Maximum, bool IsKey = false, string? AttributeName = null, bool IsNewPlannedRelationship = false);

    /// <summary>依實體名稱完整讀取 metadata，前後檢查 caller 取消；缺少必要集合不能視為空且一致。</summary>
    private static void Read(IOrganizationService service, string name, bool unpublished,
        Dictionary<string, Scalar> values, ProjectionBudget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = service.Execute(new RetrieveEntityRequest
        {
            LogicalName = name, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = unpublished
        });
        ct.ThrowIfCancellationRequested();
        var entity = (response as RetrieveEntityResponse)?.EntityMetadata;
        if (entity is null || entity.MetadataId is null || entity.MetadataId == Guid.Empty || entity.LogicalName != name ||
            entity.Attributes is null || entity.Attributes.Length > 2048 ||
            entity.Keys is null || entity.Keys.Length > 32 ||
            entity.OneToManyRelationships is null || entity.ManyToOneRelationships is null || entity.ManyToManyRelationships is null ||
            entity.Privileges is null)
            throw Invalid();
        Project(entity, name, values, budget, 0, ct);
    }

    /// <summary>
    /// 依 SDK DataMember 契約逐項複製具體型別及 CLR 純值，不保存可變 SDK 參照。
    /// 只允許 SDK 自有 DataContract，未知擴充拒絕；完整處理繼承鏈上的公開及非公開 DataMember，避免固定規劃欄漏驗。
    /// 集合保留數量並用正式 metadata ID／選項值／LCID 定位，重複身分拒絕，不按顯示名稱去重。
    /// </summary>
    private static void Project(object? value, string path, Dictionary<string, Scalar> values,
        ProjectionBudget budget, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > 24) throw Invalid();
        var type = value?.GetType();
        if (value is null || value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal or Guid or char)
        {
            if (value is string text) budget.Text(text);
            Put(path, new Scalar(type?.FullName ?? "null", value), values, budget);
            return;
        }
        if (value is DateTime date)
        {
            Put(path, new Scalar("DateTime", date.Ticks), values, budget);
            Put(path + ".Kind", new Scalar("DateTimeKind", (int)date.Kind), values, budget);
            return;
        }
        if (type!.IsEnum)
        {
            Put(path, new Scalar(type.FullName!, Convert.ToInt64(value, CultureInfo.InvariantCulture)), values, budget);
            return;
        }
        if (value is IEnumerable sequence)
        {
            var count = 0;
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var logicalNames = new HashSet<string>(StringComparer.Ordinal);
            var metadataIds = new HashSet<Guid>();
            foreach (var item in sequence)
            {
                ct.ThrowIfCancellationRequested();
                if (++count > 4096 || item is null) throw Invalid();
                var identity = CollectionIdentity(item, count);
                if (!identities.Add(identity)) throw Invalid();
                if (item is MetadataBase { MetadataId: { } metadataId } &&
                    (metadataId == Guid.Empty || !metadataIds.Add(metadataId)))
                    throw Invalid();
                var name = item switch
                {
                    AttributeMetadata attribute => attribute.LogicalName,
                    EntityKeyMetadata key => key.LogicalName,
                    RelationshipMetadataBase relationship => relationship.SchemaName,
                    _ => null
                };
                if (item is AttributeMetadata or EntityKeyMetadata or RelationshipMetadataBase &&
                    (string.IsNullOrWhiteSpace(name) || name.Length > 128 || !logicalNames.Add(name)))
                    throw Invalid();
                Project(item, path + "[" + identity + "]", values, budget, depth + 1, ct);
            }
            Put(path, new Scalar("Collection", count), values, budget);
            return;
        }
        if (type.Assembly != typeof(EntityMetadata).Assembly || type.GetCustomAttribute<DataContractAttribute>() is null)
            throw Invalid();
        if (value is IExtensibleDataObject { ExtensionData: { } extension }) ProjectionBudget.Extensions(extension);
        Put(path, new Scalar("Contract", type.FullName), values, budget);
        var properties = budget.Members(type);
        foreach (var property in properties)
        {
            ct.ThrowIfCancellationRequested();
            if (value is MetadataBase && property.Name == nameof(MetadataBase.HasChanged)) continue;
            if (!property.CanRead || property.GetIndexParameters().Length != 0) throw Invalid();
            Project(property.GetValue(value), path + "." + property.Name, values, budget, depth + 1, ct);
        }
    }

    /// <summary>
    /// 集合的穩定正式身分不依顯示文字；metadata 無 ID 拒絕，選項及翻譯允許官方的數值／LCID。
    /// 其他 SDK 值集合保留原順序與完整內容，順序改變保守拒絕，不靜默刪除或合併。
    /// </summary>
    private static string CollectionIdentity(object value, int index) => value switch
    {
        OptionMetadata option => option.Value is { } number ? "option:" + number.ToString(CultureInfo.InvariantCulture) : throw Invalid(),
        LocalizedLabel label => label.LanguageCode > 0 ? "lcid:" + label.LanguageCode.ToString(CultureInfo.InvariantCulture) : throw Invalid(),
        MetadataBase metadata => metadata.MetadataId is { } id && id != Guid.Empty ? "id:" + id.ToString("D") : throw Invalid(),
        string text => text.Length <= 128 ? "text:" + text : throw Invalid(),
        int number => "int:" + number.ToString(CultureInfo.InvariantCulture),
        _ => "index:" + index.ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>先計算節點與路徑預算再加入候選；路徑衝突不覆蓋，無 SDK 物件可流入 Scalar。</summary>
    private static void Put(string path, Scalar value, Dictionary<string, Scalar> values, ProjectionBudget budget)
    {
        budget.Node(path);
        if (!values.TryAdd(path, value)) throw Invalid();
    }

    /// <summary>型別與純值逐項比較，差異僅回固定分類，不把 label、XML、定義本文寫入例外或 log。</summary>
    private static void Equal(IReadOnlyDictionary<string, Scalar> left, Dictionary<string, Scalar> right, CancellationToken ct)
    {
        if (left.Count != right.Count) throw Invalid();
        foreach (var pair in left)
        {
            ct.ThrowIfCancellationRequested();
            if (!right.TryGetValue(pair.Key, out var value) || pair.Value != value) throw Invalid();
        }
    }

    /// <summary>匯入前完整基準；只屬本次 Import，字典不可交給背景工作或跨命令快取。</summary>
    internal sealed class Snapshot
    {
        /// <summary>建構前已完成兩來源驗證，僅移交純值字典，沒有 SDK 回應或連線。</summary>
        internal Snapshot(IReadOnlyDictionary<string, Scalar> values) { Values = values; }
        /// <summary>僅供護欄再次比較的操作區域基準，呼叫端不得修改。</summary>
        internal IReadOnlyDictionary<string, Scalar> Values { get; }
    }

    /// <summary>節點純值及其確切型別；Value 僅由 Project 的封閉 CLR 純值分支建立，不含 SDK 可變物件。</summary>
    internal sealed record Scalar(string Type, object? Value);

    /// <summary>本次兩來源共用的投影預算；不保存來源物件，無需外部 Dispose。</summary>
    private sealed class ProjectionBudget
    {
        private int nodes;
        private long bytes;
        private readonly Dictionary<Type, PropertyInfo[]> contracts = new();

        /// <summary>
        /// 每種 SDK 契約僅在本次比較反射一次，快取只含型別與屬性描述，沒有實例或組織資料。
        /// SDK 的 EnumAttributeMetadata 已含非公開父選項 DataMember，必須與公開屬性一起比較。
        /// 逐層用 DeclaredOnly 掃描，避免遺漏基底 private 成員；未來若增加欄位形式先拒絕，不漏投影。
        /// 最多128種契約，完整性檢查先於保存，結束比較後整份局部索引即可回收。
        /// </summary>
        internal PropertyInfo[] Members(Type type)
        {
            if (contracts.TryGetValue(type, out var properties)) return properties;
            if (contracts.Count >= 128) throw Invalid();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var members = new List<PropertyInfo>();
            var levels = 0;
            for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                if (++levels > 24 || current.Assembly != typeof(EntityMetadata).Assembly ||
                    current.GetFields(flags).Any(field => field.GetCustomAttribute<DataMemberAttribute>(false) is not null))
                    throw Invalid();
                members.AddRange(current.GetProperties(flags)
                    .Where(property => property.GetCustomAttribute<DataMemberAttribute>(false) is not null));
            }
            properties = members.OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            if (properties.Length == 0) throw Invalid();
            contracts.Add(type, properties);
            return properties;
        }

        /// <summary>
        /// 空 ExtensionData 是正常 SDK 反序列化結果，不能只以非 null 判為未知。
        /// .NET 10 的公開契約不提供成員盤點，因此僅讀取 CLR 的 Members 集合及 Count；
        /// 非空立即拒絕，絕不讀取未知本文、展開物件或保存參照。執行階段若不再提供此形狀，
        /// 固定拒絕而非默認空集合；SDK wire 往返成功與未知成員案例必須隨 runtime 升級重驗。
        /// </summary>
        internal static void Extensions(ExtensionDataObject extension)
        {
            var members = typeof(ExtensionDataObject).GetProperty("Members", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw Invalid();
            var value = members.GetValue(extension);
            if (value is not null && (value is not ICollection collection || collection.Count != 0)) throw Invalid();
        }
        /// <summary>逐節點限制總數及路徑長度；兩來源共用上限，不能靠分表繞過。</summary>
        internal void Node(string path)
        {
            if (++nodes > 1_000_000 || path.Length > 2048) throw Invalid();
            Text(path);
        }
        /// <summary>先限字數再以嚴格 UTF-8 計數，單文字一 MiB、整次64MiB；不配置編碼後的大陣列。</summary>
        internal void Text(string text)
        {
            if (text.Length > 1024 * 1024) throw Invalid();
            int size;
            try { size = StrictUtf8.GetByteCount(text); }
            catch (EncoderFallbackException) { throw Invalid(); }
            if (size > 1024 * 1024 || size > 64L * 1024 * 1024 - bytes) throw Invalid();
            bytes += size;
        }
    }

    /// <summary>固定無本文拒絕，真正 SDK fault 保持原 Exception 交由既有外層告警處理。</summary>
    private static InvalidDataException Invalid() => new("UI publication metadata is incomplete, pending, changed or exceeds its bounded budget.");
}
