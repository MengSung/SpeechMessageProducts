using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

    /// <summary>讀取最多1MiB的UTF-8定義，不追蹤symlink檔；stream只在此呼叫存活，解析失敗也確定釋放。</summary>
    /// <param name="path">Host選定的結構清單路徑，不是資料或credential來源。</param>
    /// <returns>已完整驗證的純結構候選，僅供本次命令使用。</returns>
    internal static SchemaDefinition Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > 1_048_576 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Schema file is unavailable or outside its bounded budget.");
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicateProperties(document.RootElement);
        var definition = document.RootElement.Deserialize<SchemaDefinition>(Json)
            ?? throw new InvalidDataException("Schema is empty.");
        Validate(definition);
        return definition;
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
