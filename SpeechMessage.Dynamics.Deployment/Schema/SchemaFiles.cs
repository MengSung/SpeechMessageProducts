/*
 * 檔案整體說明
 *
 * 目的與定位：本檔是 SpeechMessage.Dynamics.Deployment 結構部署流程的檔案與序列化邊界。
 * 它把人工維護的 speechmessagecore.schema.json／審核過的 schema-plan.json 轉為可規劃的純值，
 * 並把完整 metadata 快照、計畫 JSON 與可閱讀摘要寫成可保留的本機證據；不連 D365、不執行部署。
 * SchemaDeploymentCli 負責路由、白名單與可信路徑，SchemaPlanner 負責即時組織差異與 plan 雜湊核對，
 * SchemaDeploymentEngine 只有在本檔的 before snapshot 已成功 flush 並關閉後，才可發出新增請求。
 *
 * 主要功能與流程：Load 讀取最多 1 MiB 的 schema，ReadPlan 讀取最多 4 MiB 的 plan；
 * 共用的 Read<T> 開啟檔案後再次核對大小，解析深度最多 32，拒絕未知及重複 JSON 屬性。
 * Validate 逐一確認固定 solution／publisher／new 前綴、ActivePhase範圍、精確既有實體白名單、欄位身份與唯一性、
 * 進入既有實體白名單輔助函式前先確認Fields／Keys不為null；null集合與其他結構錯誤一律轉成受控資料錯誤，
 * 型別／長度／phase、lookup 目標、選項數值與索引欄位；主要名稱與索引欄仍要求phase 1，P1時間點必須為UserLocal／DateAndTime。
 * 分層讀取先驗每檔的結構，lookup 目標與主名稱欄位存在性留到完整組合後驗證，允許同層檔案延伸新表；
 * 跨層僅精確允許 goodshepherd20 的 goodshepherd 層延伸 account 四個核定生命週期欄位；
 * 此特例不能加入未知欄位、重複平台欄位、鍵或變更實體 metadata，其餘跨層重複實體仍拒絕。
 * 單檔 Load／Hash 與合併完成後的 Validate 仍拒絕任何未宣告目標，未驗證的局部層不會公開回傳。
 * 層級檢視只合併一次，實體／檢視名稱不得重複；所有條件欄位也須存在於清單。
 * 非教會產品即使誤載了平台內的教會實體仍會拒絕，隔離不只依賴設定檔目前剛好正確。
 * Hash 與 HashValue 對已辨識的純結構計算 SHA-256；它們用於完整性與漂移檢查，不代表使用者授權。
 * WriteNew 與 WritePlanMarkdown 共用 WriteContent，以 UTF-8 無 BOM、CRLF 輸出，最多 100 MiB。
 *
 * 擁有權、隔離與失敗處理：每次讀取獨佔 FileStream／JsonDocument，離開方法即 Dispose；
 * 每次寫入先以 CreateNew 建立操作專屬暫存檔，Flush(true)、關閉，再以禁止覆寫的 File.Move 發布。
 * 舊證據不得截斷或覆蓋；失敗不回傳半份 JSON，暫存檔由同一操作 finally 清理，例外向命令共用告警入口傳遞。
 * 本檔沒有租戶結果快取、背景工作、timer 或 request 狀態；唯一共用內容是固定 JSON 規約、
 * 固定名稱文法與不可變系統文字表。部署摘要不建立假教會 context，也不讀教會覆寫或秘密。
 * 路徑的組織授權與祖先 link／junction 檢查由 CLI 完成；本檔的檔案檢查不能取代該信任邊界。
 *
 * 維護與驗證：修改 JSON 規約、預算、命名或 hash 時，必須同步重驗 SchemaDefinitionTests、
 * SchemaPlannerTests、SchemaEngineTests 與 SchemaCliTests。尤其保留未知／重複輸入拒絕、
 * plan 漂移、before snapshot 先落檔、禁止覆寫與 UTF-8／CRLF 斷言。摘要文字只改正式 resx，
 * 再由 I18nCheck 產生資源並執行 verify；不能為通過而提高棘輪或手改生成檔。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using SpeechMessage.Localization;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>單操作擁有的有界結構檔案入口；嚴格JSON與完整唯一性驗證先於任何SDK讀寫，hash只涵蓋已辨識定義。</summary>
internal static class SchemaFiles
{
    /// <summary>固定型別契約與深度，不忽略未知欄位或將大小寫錯誤靜默轉成預設值。</summary>
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { PreserveLegacyDefinitionShape } }
    };
    /// <summary>
    /// 舊結構沒有 views 欄位；僅在空集合時省略，以保留 FS 前 canonical hash。
    /// 非空檢視完整參與雜湊，讀取仍使用原嚴格未知欄位及重複屬性檢查，不省略任何有效候選。
    /// </summary>
    private static void PreserveLegacyDefinitionShape(JsonTypeInfo info)
    {
        if (info.Type == typeof(SchemaDefinition))
            info.Properties.Single(property => property.Name == "views").ShouldSerialize =
                static (_, value) => value is IReadOnlyList<SchemaView> { Count: > 0 };
    }
    /// <summary>固定資料庫名稱文法附逾時，不保存任何來源文字或caller狀態。</summary>
    private static readonly Regex Name = new("^new_[a-z][a-z0-9_]{0,79}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    /// <summary>固定生成資源的不可變系統表，無scope、request或教會覆寫，可安全共用且沒有背景資源。</summary>
    private static readonly SystemTextTable Texts = new();

    /// <summary>讀取最多1MiB的UTF-8定義，不追蹤symlink檔；stream只在此呼叫存活，解析失敗也確定釋放。</summary>
    /// <param name="path">Host選定的結構清單路徑，不是資料或credential來源。</param>
    /// <returns>已完整驗證的純結構候選，僅供本次命令使用。</returns>
    internal static SchemaDefinition Load(string path)
    {
        var definition = Read<SchemaDefinition>(path, 1_048_576);
        Validate(definition);
        return definition;
    }

    /// <summary>
    /// 讀取僅供本類別組合的局部層，驗證格式、身份與欄位約束；允許明確空產品層，
    /// 並把 lookup 目標存在性延到完整組合後確認，不能將此局部候選交給部署器。
    /// </summary>
    private static SchemaDefinition LoadLayer(string path)
    {
        var definition = Read<SchemaDefinition>(path, 1_048_576);
        Validate(definition, allowEmptyEntities: true, validateLookupTargets: false);
        return definition;
    }

    /// <summary>
    /// 依 organization 的分層清單載入並合併 schema。資料夾層可在同一 layer 分檔延伸實體，
    /// 不同 layer 重複實體、同名欄位或未列組織一律拒絕；所有檔案先讀成 operation-local
    /// 候選，完成衝突與 ownership／view 驗證後才回傳完整定義。
    /// </summary>
    internal static SchemaDefinition LoadForOrganization(string repositoryRoot, string organization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        var schemaRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "d365", "schema"));
        var layerPath = Path.Combine(schemaRoot, "layers.json");
        var layerManifest = ReadLayerManifest(layerPath);
        if (!layerManifest.Organizations.TryGetValue(organization, out var layers) || layers.Count == 0)
            throw new InvalidDataException("Organization has no approved schema layers.");

        var definitions = new List<(string Layer, bool Folder, SchemaDefinition Definition)>();
        foreach (var layer in layers)
        {
            if (!IsLayerName(layer)) throw new InvalidDataException("Schema layer name is invalid.");
            var folder = Path.Combine(schemaRoot, layer);
            var folderFiles = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.schema.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
            if (folderFiles.Length != 0)
            {
                foreach (var file in folderFiles)
                    definitions.Add((layer, true, LoadLayer(file)));
                continue;
            }

            var filePath = Path.Combine(schemaRoot, layer + ".schema.json");
            if (!File.Exists(filePath))
                throw new InvalidDataException("Approved schema layer is missing.");
            definitions.Add((layer, false, LoadLayer(filePath)));
        }

        if (definitions.Count == 0) throw new InvalidDataException("Schema layer composition is empty.");
        var first = definitions[0].Definition;
        var entities = new List<SchemaEntity>();
        var entityLayers = new Dictionary<string, string>(StringComparer.Ordinal);
        var views = new List<SchemaView>();
        var viewLayers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in definitions)
        {
            var definition = item.Definition;
            if (definition.SchemaVersion != first.SchemaVersion || definition.ActivePhase != first.ActivePhase ||
                definition.Solution != first.Solution || definition.Publisher != first.Publisher ||
                definition.Prefix != first.Prefix)
                throw new InvalidDataException("Schema layers have incompatible identities.");

            foreach (var entity in definition.Entities)
            {
                var existingIndex = entities.FindIndex(candidate => candidate.LogicalName == entity.LogicalName);
                if (existingIndex < 0)
                {
                    entityLayers.Add(entity.LogicalName, item.Layer);
                    entities.Add(entity);
                }
                else if (!item.Folder || !string.Equals(entityLayers[entity.LogicalName], item.Layer, StringComparison.Ordinal))
                {
                    if (!IsApprovedLifecycleExtension(organization, entityLayers[entity.LogicalName], item.Layer, entity))
                        throw new InvalidDataException("Schema entity is duplicated across layers.");
                    entities[existingIndex] = MergeFolderEntity(entities[existingIndex], entity);
                }
                else
                {
                    entities[existingIndex] = MergeFolderEntity(entities[existingIndex], entity);
                }
            }

            if (viewLayers.Add(item.Layer) && layerManifest.Views.TryGetValue(item.Layer, out var layerViews))
                views.AddRange(layerViews);
        }

        var merged = first with
        {
            Entities = Array.AsReadOnly(entities.ToArray()),
            Views = Array.AsReadOnly(views.ToArray())
        };
        Validate(merged);
        ValidateLayeredContract(merged, organization);
        return merged;
    }

    /// <summary>讀取有界plan，不接受未知或重複屬性；語意與hash仍須由apply依即時server重驗。</summary>
    internal static SchemaPlan ReadPlan(string path) => Read<SchemaPlan>(path, 4 * 1024 * 1024);

    /// <summary>受限接續作業讀原始完整before，最多100MiB與同一JSON深度；語意必須由原plan完整hash重新驗證，不把任意snapshot當授權。</summary>
    internal static SchemaSnapshot ReadBeforeSnapshot(string path) => Read<SchemaSnapshot>(path, 100 * 1024 * 1024);

    /// <summary>只讀repo內固定P1-12合成fixture，使用相同嚴格JSON／4KiB預算；固定key及文化不可改成其他業務資料。</summary>
    internal static string ReadAcceptanceText(string path)
    {
        var fixture = Read<SchemaAcceptanceFixture>(path, 4096);
        if (fixture.Key != "Org.Level.Group" || fixture.Culture != "*" || string.IsNullOrWhiteSpace(fixture.Text) || fixture.Text.Length > 2000)
            throw new InvalidDataException("Acceptance fixture does not match the fixed P1 scope.");
        return fixture.Text;
    }

    /// <summary>同一份嚴格JSON入口，取得獨占讀handle後再次驗大小，防止FileInfo與open之間的替換繞過預算。</summary>
    private static T Read<T>(string path, long maximumBytes) where T : class
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > maximumBytes || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Schema file is unavailable or outside its bounded budget.");
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= 0 || stream.Length > maximumBytes) throw new InvalidDataException("Schema file exceeds its bounded budget.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicateProperties(document.RootElement);
        return document.RootElement.Deserialize<T>(Json)
            ?? throw new InvalidDataException("Schema is empty.");
    }

    /// <summary>在命令獨占目錄以CreateNew寫暫存並flush到磁碟，關閉後才移成正式證據；故障不覆寫舊檔或留下半份JSON。</summary>
    /// <param name="directory">Host選定的本命令唯一目錄，不接受request輸入。</param><param name="name">程式固定檔名。</param>
    /// <param name="value">純metadata或計畫，不可含SDK/credential。</param><returns>新證據絕對路徑。</returns>
    internal static string WriteNew<T>(string directory, string name, T value)
        => WriteContent(directory, name, JsonSerializer.Serialize(value, Json));

    /// <summary>計畫以相同有界／CreateNew／flush契約輸出可人工掃讀的Markdown，不另開另一套不安全writer。</summary>
    internal static string WritePlanMarkdown(string directory, SchemaPlan plan)
    {
        var text = new StringBuilder("# " + Text("Title") + "\n\n");
        text.AppendLine("- " + Text("Organization") + ": " + plan.Organization);
        text.AppendLine("- " + Text("OrganizationId") + ": " + plan.OrganizationId.ToString("D"));
        text.AppendLine("- " + Text("SchemaHash") + ": " + plan.SchemaHash);
        text.AppendLine("- " + Text("Fingerprint") + ": " + plan.Fingerprint);
        text.AppendLine("- " + Text("PlanHash") + ": " + plan.Sha256);
        text.AppendLine("- " + Text("Safety") + "\n\n## " + Text("Operations") + "\n");
        foreach (var operation in plan.Operations) text.AppendLine("- " + operation.Kind + ": " + operation.Entity + "." + operation.Name);
        if (plan.RetainedOwnership is { } retained)
        {
            text.AppendLine("\n## Existing metadata ownership\n");
            foreach (var item in retained)
                text.AppendLine("- " + item.Entity + "; MetadataId=" + item.MetadataId.ToString("D")
                    + "; retained=" + item.Ownership + "; new-entity contract=UserOwned");
        }
        if (plan.Ui is { } ui)
        {
            text.AppendLine("\n## UI\n");
            text.AppendLine("- Solution: " + ui.Solution + "; Product: " + ui.Product + "; Version: " + ui.Version);
            text.AppendLine("- Fingerprint: " + ui.Fingerprint);
            foreach (var view in ui.Views)
                text.AppendLine("- " + (view.NeedsWrite ? view.Id == Guid.Empty ? "CreateView" : "UpdateView" : "VerifyView")
                    + ": " + view.Definition.Entity + "; " + view.Definition.Name + "; SavedQueryId=" + view.Id.ToString("D")
                    + "; Include=" + (!view.Included).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (plan.Operations.Count == 0) text.AppendLine("- " + Text("NoChanges"));
        text.AppendLine("\n## " + Text("Keys") + "\n");
        foreach (var key in plan.PendingKeys) text.AppendLine("- " + Text("Pending") + ": " + key);
        if (plan.PendingKeys.Count == 0) text.AppendLine("- " + Text("NoPending"));
        return WriteContent(directory, "schema-plan.md", text.ToString());
    }

    /// <summary>部署工具固定採繁中系統資源，不建立假的教會scope或讀任一租戶覆寫。</summary>
    private static string Text(string suffix) => Texts.GetText("zh-TW", new TextKey("Deployment.Schema.Plan." + suffix));

    /// <summary>所有格式共用一次性檔案owner，任一已存在檔案保持原樣，暫存檔不會被讀者當完整證據。</summary>
    private static string WriteContent(string directory, string name, string content)
    {
        if (name != Path.GetFileName(name) || name.Length == 0) throw new InvalidDataException("Invalid schema artifact name.");
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Artifact directory must not be a link.");
        var path = Path.GetFullPath(Path.Combine(directory, name));
        if (File.Exists(path)) throw new IOException("Schema artifact already exists.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var text = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        if (Encoding.UTF8.GetByteCount(text) > 100 * 1024 * 1024) throw new InvalidDataException("Schema artifact exceeds its bounded budget.");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: false);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>對完整已辨識定義計算canonical hash；apply必須重算，清單任何有效變更使既有plan失效。</summary>
    /// <param name="definition">已驗證的操作候選。</param>
    /// <returns>固定大寫SHA-256，不含秘密或原始檔案路徑。</returns>
    internal static string Hash(SchemaDefinition definition)
    {
        Validate(definition);
        return HashValue(definition);
    }

    /// <summary>計畫、已驗證定義及純metadata共用同一JSON規約，所有buffer只活於單次hash，不保存或記錄內容。</summary>
    internal static string HashValue<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));

    /// <summary>逐層拒絕同物件重複屬性，避免last-write-wins把人工審核內容與反序列化結果分開。</summary>
    internal static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    /// <summary>驗全部實體與欄位後才交規劃器；P8只額外允許兩張舊表的指定optional lookup，未知表或欄仍拒絕且不回顯輸入。</summary>
    internal static void Validate(SchemaDefinition definition) =>
        Validate(definition, allowEmptyEntities: false, validateLookupTargets: true);

    /// <summary>
    /// 共用 schema 驗證；僅私有 LoadLayer 可接受空層並延後跨層目標存在性。
    /// 所有公開於類別外的完整定義都必須驗目標，未知或漏載的表不能進入 planner。
    /// </summary>
    private static void Validate(SchemaDefinition definition, bool allowEmptyEntities, bool validateLookupTargets)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.SchemaVersion != 1 || definition.ActivePhase is < 1 or > 8 || definition.Solution != "SpeechMessageCore"
            || definition.Publisher != "SpeechMessagePublisher" || definition.Prefix != "new"
            || definition.Entities is null || definition.Entities.Count > 32 ||
            !allowEmptyEntities && definition.Entities.Count < 1)
            throw new InvalidDataException("Unsupported schema identity or budget.");
        var entities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in definition.Entities)
        {
            if (entity is null || !entities.Add(entity.LogicalName)
                || (entity.IsNew ? !ValidName(entity.LogicalName) : !ValidExistingEntity(entity))
                || !ValidLabel(entity.DisplayName) || !ValidLabel(entity.PluralName)
                || entity.Fields is null || entity.Fields.Count is < 1 or > 128 || entity.Keys is null || entity.Keys.Count > 8)
                throw new InvalidDataException("Invalid or duplicate schema entity.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in entity.Fields)
            {
                if (field is null || !ValidName(field.LogicalName) || !fields.Add(field.LogicalName)
                    || !ValidLabel(field.DisplayName) || field.Phase is < 1 or > 8 || field.Options is null)
                    throw new InvalidDataException("Invalid or duplicate schema field.");
                ValidateField(field);
            }
            if (entity.IsNew && (!ValidName(entity.PrimaryName)
                || !allowEmptyEntities && !entity.Fields.Any(field => field.LogicalName == entity.PrimaryName && field.Type == "string" && field.Phase == 1)))
                throw new InvalidDataException("New entity requires its declared primary name field.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in entity.Keys)
            {
                if (key is null || !ValidName(key.LogicalName) || !keys.Add(key.LogicalName)
                    || key.Attributes is null || key.Attributes.Count is < 1 or > 5
                    || key.Attributes.Distinct(StringComparer.Ordinal).Count() != key.Attributes.Count
                    || key.Attributes.Any(attribute => !entity.Fields.Any(field => field.LogicalName == attribute && field.Phase == 1)))
                    throw new InvalidDataException("Invalid or duplicate schema key.");
            }
        }
        if (validateLookupTargets)
        {
            foreach (var field in definition.Entities.SelectMany(entity => entity.Fields).Where(field => field.Type == "lookup"))
                if (!entities.Contains(field.Target)) throw new InvalidDataException("Unknown schema lookup target.");
        }
    }

    /// <summary>分層組合只允許同一 folder 檔案延伸欄位／鍵，metadata 身份不因最後一檔覆蓋。</summary>
    private static SchemaEntity MergeFolderEntity(SchemaEntity first, SchemaEntity second)
    {
        if (first.IsNew != second.IsNew || first.DisplayName != second.DisplayName ||
            first.PluralName != second.PluralName || first.PrimaryName != second.PrimaryName ||
            first.Audit != second.Audit || first.OwnershipType != second.OwnershipType ||
            first.OptimisticConcurrency != second.OptimisticConcurrency)
            throw new InvalidDataException("Schema entity metadata conflicts within a layer folder.");
        var fields = first.Fields.Concat(second.Fields).ToArray();
        if (fields.Select(field => field.LogicalName).Distinct(StringComparer.Ordinal).Count() != fields.Length)
            throw new InvalidDataException("Schema attribute is duplicated within a layer folder.");
        var keys = first.Keys.Concat(second.Keys).ToArray();
        if (keys.Select(key => key.LogicalName).Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new InvalidDataException("Schema key is duplicated within a layer folder.");
        return first with
        {
            Fields = Array.AsReadOnly(fields),
            Keys = Array.AsReadOnly(keys)
        };
    }

    /// <summary>
    /// F3 唯一核定的跨層延伸：好牧人 account 生命週期屬產品層，租戶基本資料屬平台層。
    /// 只接受四個固定欄位及型別／選項，完整合併仍驗 metadata 一致與欄位唯一；未知產品擴充不能沿此特例穿越。
    /// </summary>
    private static bool IsApprovedLifecycleExtension(string organization, string firstLayer, string secondLayer, SchemaEntity entity)
    {
        if (organization != "goodshepherd20" || firstLayer != "platform" || secondLayer != "goodshepherd"
            || entity.LogicalName != "account" || entity.IsNew || entity.Fields.Count != 4 || entity.Keys.Count != 0)
            return false;
        return entity.Fields.All(field => field.Phase == 1 && !field.Required && !field.Secured &&
            (field.LogicalName == "new_lifecyclestatus"
                ? field.Type == "choice" && field.Options.Select(option => option.Value).SequenceEqual(Enumerable.Range(100000000, 7))
                : field.LogicalName is "new_trialenddate" or "new_reserveuntil" or "new_periodend"
                    && field.Type == "datetime" && field.Behavior == "UserLocal" && field.Format == "DateAndTime"));
    }

    /// <summary>分層專用 ownership 與檢視契約；legacy goodshepherdback 保留舊 nullable ownership 以維持回歸 hash。</summary>
    private static void ValidateLayeredContract(SchemaDefinition definition, string organization)
    {
        if (organization is "construction" or "association" &&
            definition.Entities.Any(entity => entity.LogicalName is
                "new_fee" or "new_offeringcategory" or "new_dedication_booking" or "new_stor_lessons"))
            throw new InvalidDataException("Church-specific schema entity is forbidden in this organization.");
        foreach (var entity in definition.Entities.Where(entity => entity.IsNew))
        {
            if (organization != "goodshepherdback" &&
                entity.OwnershipType is not ("UserOwned" or "OrganizationOwned"))
                throw new InvalidDataException("New schema entity requires an explicit ownership type.");
            if (entity.LogicalName == "new_localizedtext" && entity.OwnershipType == "OrganizationOwned" &&
                entity.Fields.Any(field => field.Type == "lookup" && field.Target == "account"))
                throw new InvalidDataException("Tenant business entity cannot be organization-owned.");
        }

        ValidateViews(definition);
    }

    /// <summary>檢查 view 的實體／欄位白名單與每個實體唯一預設公用檢視。</summary>
    private static void ValidateViews(SchemaDefinition definition)
    {
        var entities = definition.Entities.ToDictionary(entity => entity.LogicalName, StringComparer.Ordinal);
        var defaults = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<(string Entity, string Name)>();
        foreach (var view in definition.Views)
        {
            if (view is null || !ValidViewText(view.Entity, 128) || !ValidViewText(view.Name, 300) ||
                view.Columns is null || view.Columns.Count is < 1 or > 64 ||
                view.Filter is null || view.Filter.Count > 64 || view.Order is null || view.Order.Count > 64)
                throw new InvalidDataException("Schema view definition is invalid.");
            if (!identities.Add((view.Entity, view.Name)))
                throw new InvalidDataException("Schema view definition is duplicated.");
            if (!entities.TryGetValue(view.Entity, out var entity))
                throw new InvalidDataException("Schema view references an unknown entity.");
            var fields = entity.Fields.Select(field => field.LogicalName).ToHashSet(StringComparer.Ordinal);
            if (view.Columns.Any(column => !ValidName(column) || !fields.Contains(column)) ||
                view.Columns.Distinct(StringComparer.Ordinal).Count() != view.Columns.Count ||
                view.Order.Any(column => !ValidName(column) || !fields.Contains(column)) ||
                view.Order.Distinct(StringComparer.Ordinal).Count() != view.Order.Count ||
                view.Filter.Any(condition => !ValidName(condition) || !fields.Contains(condition)))
                throw new InvalidDataException("Schema view contains an unknown or unsafe attribute.");
            if (view.IsDefault && !defaults.Add(view.Entity))
                throw new InvalidDataException("An entity has more than one default public view.");
        }
    }

    /// <summary>公開檢視名稱與固定條件只允許可見字元，避免未驗證本文進入 FetchXml。</summary>
    private static bool ValidViewText(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    /// <summary>讀取 layers.json 的嚴格純值描述，不忽略未知欄位或重複 JSON 屬性。</summary>
    private static LayerManifest ReadLayerManifest(string path)
    {
        using var document = ReadDocument(path, 1_048_576);
        var root = document.RootElement;
        RejectDuplicateProperties(root);
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject()
                .Any(property => property.Name is not ("schemaVersion" or "organizations" or "views")))
            throw new InvalidDataException("Schema layer manifest is invalid.");
        if (!root.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1 ||
            !root.TryGetProperty("organizations", out var organizations) ||
            organizations.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Schema layer manifest is invalid.");

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var item in organizations.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.Array || !item.Value.EnumerateArray().Any())
                throw new InvalidDataException("Schema organization layer list is invalid.");
            var names = item.Value.EnumerateArray().Select(value =>
                value.ValueKind == JsonValueKind.String && value.GetString() is { } name ? name : throw new InvalidDataException("Schema layer name is invalid.")).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(name => !IsLayerName(name)))
                throw new InvalidDataException("Schema layer list contains duplicates or invalid names.");
            if (!result.TryAdd(item.Name, Array.AsReadOnly(names)))
                throw new InvalidDataException("Schema organization is duplicated.");
        }

        var views = new Dictionary<string, IReadOnlyList<SchemaView>>(StringComparer.Ordinal);
        if (root.TryGetProperty("views", out var rawViews))
        {
            if (rawViews.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Schema views are invalid.");
            foreach (var item in rawViews.EnumerateObject())
            {
                if (!IsLayerName(item.Name) || item.Value.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Schema view layer is invalid.");
                var parsed = item.Value.Deserialize<SchemaView[]>(Json) ?? throw new InvalidDataException("Schema views are invalid.");
                views.Add(item.Name, Array.AsReadOnly(parsed));
            }
        }
        return new LayerManifest(result, views);
    }

    /// <summary>取得有界 JsonDocument；由呼叫端 using 擁有 document，避免 reader 跨方法存活。</summary>
    private static JsonDocument ReadDocument(string path, long maximumBytes)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > maximumBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Schema layer manifest is unavailable.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= 0 || stream.Length > maximumBytes) throw new InvalidDataException("Schema layer manifest exceeds its budget.");
        return JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
    }

    /// <summary>固定 layer 名稱只能是檔名／資料夾名，不能以路徑穿越改變讀取範圍。</summary>
    private static bool IsLayerName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private sealed record LayerManifest(
        IReadOnlyDictionary<string, IReadOnlyList<string>> Organizations,
        IReadOnlyDictionary<string, IReadOnlyList<SchemaView>> Views);

    /// <summary>既有表延伸採逐表逐欄白名單；奉獻實體只接受一個phase 8 optional lookup，不允許順帶管理其他舊欄位。</summary>
    private static bool ValidExistingEntity(SchemaEntity entity)
    {
        if (entity.LogicalName is "account" or "contact" or "new_stor_lessons")
        {
            return true;
        }

        if (entity.LogicalName is not ("new_fee" or "new_dedication_booking")
            || entity.Fields is null || entity.Keys is null
            || entity.Fields.Count != 1 || entity.Keys.Count != 0)
        {
            return false;
        }

        var field = entity.Fields[0];
        return field.LogicalName == "new_offeringcategoryid"
            && field.Type == "lookup"
            && field.Target == "new_offeringcategory"
            && field.Phase == 8
            && !field.Required;
    }

    /// <summary>每種型別只接受可明確建立與比對的約束；未知欄位、錯日期或多餘參數全部fail closed。</summary>
    private static void ValidateField(SchemaField field)
    {
        if (field.Target is null || field.Behavior is null || field.Format is null
            || field.Type is not ("string" or "memo" or "integer" or "choice" or "lookup" or "datetime")
            || (field.Type is "string" or "memo" ? field.MaxLength is < 1 or > 4000 : field.MaxLength != 0)
            || (field.Type == "lookup" ? string.IsNullOrEmpty(field.Target) : field.Target.Length != 0)
            || (field.Type == "datetime" ? field.Behavior != "UserLocal" || field.Format != "DateAndTime"
                : field.Behavior.Length != 0 || field.Format.Length != 0)
            || (field.Type == "choice" ? field.Options.Count is < 1 or > 64 : field.Options.Count != 0))
            throw new InvalidDataException("Unsupported field type or constraints.");
        var values = new HashSet<int>();
        foreach (var option in field.Options)
            if (option is null || option.Value < 100_000_000 || !values.Add(option.Value) || !ValidLabel(option.Label))
                throw new InvalidDataException("Invalid or duplicate option identity.");
    }

    /// <summary>驗正式邏輯名稱，禁止輸入成為FetchXML、metadata路徑或另一產品元件。</summary>
    internal static bool ValidName(string? name) => name is not null && Name.IsMatch(name);
    /// <summary>固定label有界且不含控制字元；它只屬定義，不包含業務列或個資。</summary>
    private static bool ValidLabel(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 300 && !value.Any(char.IsControl);
}
