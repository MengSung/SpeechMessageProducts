/*
檔案整體說明
目的與定位：P7部署工具的組織白名單純資料邊界，供CLI與獨占連線租約共用核定名稱與主機。
主要流程：從固定repo清單讀取完整候選，嚴格驗證名稱、主機、kind、狀態與逐筆分級決定，
成功才發布不可變資料；不讀User Secrets、不建立SDK連線或判定使用者已核定正式寫入。
資源與隔離：檔案及JSON reader只屬本次Load，完成即釋放；回傳不保存stream、credential、
request或跨組織可變狀態。容量與深度有界，未知或重複欄位拒絕，不回傳部分結果。
驗證方式：OrganizationsTests以合成清單注入重複、未知欄位及未列組織，離線測試不代表實機授權。
*/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
namespace SpeechMessage.Dynamics.Deployment;

/// <summary>命令區域不可變組織描述；純值不包含連線或帳密，不能替代ProductionGuard。</summary>
internal sealed record OrganizationDefinition(string Name, string Kind, string Host, string SwitchState)
{
    /// <summary>完整驗證且唯讀的逐筆決定，型別優先序由後續切換planner解釋，不由catalog自行升級分級。</summary>
    internal IReadOnlyList<OrganizationTierOverride> TierOverrides { get; init; } = Array.Empty<OrganizationTierOverride>();
}

/// <summary>已核定的逐筆分級純值；不包含SDK、身分憑證或可變設定。</summary>
internal sealed record OrganizationTierOverride(string Target, string Tier, string Reason, string DecidedBy, string DecidedAt);

/// <summary>完整驗證後才發布的白名單，每次命令獨立，不建立全域mutable cache。</summary>
internal sealed class OrganizationCatalog
{
    private readonly ReadOnlyDictionary<string, OrganizationDefinition> organizations;
    /// <summary>只接完整區域候選，包成唯讀dictionary後不再修改，不交出內部可變集合。</summary>
    private OrganizationCatalog(Dictionary<string, OrganizationDefinition> values) => organizations = new ReadOnlyDictionary<string, OrganizationDefinition>(values);
    /// <summary>只供舊測試專用入口取得唯一test描述；不存在即拒絕，不把production當測試後備。</summary>
    internal OrganizationDefinition TestOrganization => organizations.Values.SingleOrDefault(value => value.Kind == "test") ?? throw Invalid();
    /// <summary>沿組件所在方案根讀核定catalog，不借目前工作目錄中的測試fixture改變連線白名單；每次重讀不快取。</summary>
    internal static OrganizationCatalog LoadDefault()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return Load(directory.FullName);
        throw Invalid();
    }
    /// <summary>固定repo位置開啟後重驗容量與reparse邊界，嚴格UTF8讀取，所有handle在發布前關閉。</summary>
    internal static OrganizationCatalog Load(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "d365", "registration", "organizations.json");
        Schema.SchemaDeploymentCli.EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > 1024 * 1024) throw Invalid();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
        return Parse(reader.ReadToEnd());
    }
    /// <summary>讀取有界JSON並完整驗證後建立純值catalog；錯誤不包含原文或秘密。</summary>
    internal static OrganizationCatalog Parse(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 1024 * 1024) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            Schema.SchemaFiles.RejectDuplicateProperties(root);
            Fields(root, "schemaVersion", "organizations");
            if (!root.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1) throw Invalid();
            var rows = root.GetProperty("organizations");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() is < 1 or > 256) throw Invalid();
            var values = new Dictionary<string, OrganizationDefinition>(StringComparer.OrdinalIgnoreCase);
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.EnumerateArray())
            {
                Fields(row, "name", "kind", "host", "switchState", "tierOverrides");
                var name = Text(row, "name", 128); var kind = Text(row, "kind", 16);
                var host = Text(row, "host", 253); var state = Text(row, "switchState", 16);
                if (!name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ||
                    kind is not ("test" or "production") || state is not ("off" or "shadow" or "live") ||
                    kind == "test" && name != Shadow.ShadowTestIdentity.AllowedOrganization ||
                    Uri.CheckHostName(host) != UriHostNameType.Dns || host.Any(char.IsAsciiLetterUpper) ||
                    !hosts.Add(host)) throw Invalid();
                var overrides = row.GetProperty("tierOverrides");
                if (overrides.ValueKind != JsonValueKind.Array || overrides.GetArrayLength() > 256) throw Invalid();
                var decisions = new List<OrganizationTierOverride>();
                var targets = new HashSet<string>(StringComparer.Ordinal);
                foreach (var decision in overrides.EnumerateArray())
                {
                    Fields(decision, "target", "tier", "reason", "decidedBy", "decidedAt");
                    var target = Text(decision, "target", 512); var tier = Text(decision, "tier", 16);
                    var reason = Text(decision, "reason", 2000); var by = Text(decision, "decidedBy", 256);
                    var at = Text(decision, "decidedAt", 64);
                    if (!targets.Add(target) || tier is not ("live" or "dormant") ||
                        !DateTime.TryParseExact(at, "yyyy-MM-dd HH:mm 'Asia/Taipei (UTC+08:00)'", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                        tier == "live" && by != (kind == "test" ? "演練" : "使用者")) throw Invalid();
                    decisions.Add(new OrganizationTierOverride(target, tier, reason, by, at));
                }
                if (!values.TryAdd(name, new OrganizationDefinition(name, kind, host, state) { TierOverrides = decisions.AsReadOnly() })) throw Invalid();
            }
            return new OrganizationCatalog(values);
        }
        catch (JsonException) { throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
        catch (KeyNotFoundException) { throw Invalid(); }
    }
    /// <summary>未列組織一律拒絕，不以輸入名稱自動建立許可。</summary>
    internal OrganizationDefinition Get(string name) => name is not null && organizations.TryGetValue(name, out var value) ? value : throw Invalid();
    /// <summary>物件欄位必須完全相等；未知及缺欄不能用預設值掩蓋核定文件錯誤。</summary>
    private static void Fields(JsonElement row, params string[] names)
    {
        if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Count() != names.Length ||
            row.EnumerateObject().Any(property => !names.Contains(property.Name, StringComparer.Ordinal))) throw Invalid();
    }
    /// <summary>只取有界非空純文字；不保留JsonElement或document參照。</summary>
    private static string Text(JsonElement row, string field, int maximum)
    {
        var token = row.GetProperty(field);
        return token.ValueKind == JsonValueKind.String && token.GetString() is { } value &&
            !string.IsNullOrWhiteSpace(value) && value.Length <= maximum ? value : throw Invalid();
    }
    /// <summary>固定錯誤不含原文、名稱或host，避免將候選設定當成安全日誌內容。</summary>
    private static InvalidDataException Invalid() => new("Organization catalog is invalid or target is not allowlisted.");
}
