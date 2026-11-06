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
 * Validate 逐一確認固定 solution／publisher／new 前綴、實體與欄位的穩定邏輯身份、唯一性、
 * 型別／長度／phase、lookup 目標、選項數值與索引欄位；P1 時間點必須為 UserLocal／DateAndTime。
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
        WriteIndented = true
    };
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

    /// <summary>讀取有界plan，不接受未知或重複屬性；語意與hash仍須由apply依即時server重驗。</summary>
    internal static SchemaPlan ReadPlan(string path) => Read<SchemaPlan>(path, 4 * 1024 * 1024);

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
    private static void RejectDuplicateProperties(JsonElement element)
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

    /// <summary>驗全部實體與欄位後才交規劃器；所有錯誤固定訊息，不回顯清單未知值。</summary>
    internal static void Validate(SchemaDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.SchemaVersion != 1 || definition.Solution != "SpeechMessageCore"
            || definition.Publisher != "SpeechMessagePublisher" || definition.Prefix != "new"
            || definition.Entities is null || definition.Entities.Count is < 1 or > 32)
            throw new InvalidDataException("Unsupported schema identity or budget.");
        var entities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in definition.Entities)
        {
            if (entity is null || !entities.Add(entity.LogicalName)
                || (entity.IsNew ? !ValidName(entity.LogicalName) : entity.LogicalName is not ("account" or "contact"))
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
                || !entity.Fields.Any(field => field.LogicalName == entity.PrimaryName && field.Type == "string" && field.Phase == 1)))
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
        foreach (var field in definition.Entities.SelectMany(entity => entity.Fields).Where(field => field.Type == "lookup"))
            if (!entities.Contains(field.Target)) throw new InvalidDataException("Unknown schema lookup target.");
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
