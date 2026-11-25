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
using SpeechMessage.Dynamics.Deployment.Offerings;
namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 命令區域不可變組織描述；純值不包含連線或帳密，不能替代ProductionGuard。
/// Host 可是既有清單使用的 DNS 主機，也可是核定的 HTTPS 基址；測試組織在端點尚未核定
/// 時以空字串表示待補狀態，connector 必須在讀取秘密與建立 client 前拒絕。
/// </summary>
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
    /// <summary>只供相容舊入口取得唯一 test 描述；多個或零個 test 一律拒絕，避免隱含選錯組織。</summary>
    internal OrganizationDefinition TestOrganization => organizations.Values.SingleOrDefault(value => value.Kind == "test") ?? throw Invalid();
    /// <summary>依明確名稱取得 test 組織；production、未列名稱或空白名稱都拒絕。</summary>
    internal OrganizationDefinition GetTest(string name)
    {
        var organization = Get(name);
        return organization.Kind == "test" ? organization : throw Invalid();
    }
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
    [I18nIgnore("演練與使用者是組織核定紀錄的固定欄位值，不是執行期顯示資源。")]
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
                var host = HostText(row, "host", 253); var state = Text(row, "switchState", 16);
                if (!name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ||
                    kind is not ("test" or "production") || state is not ("off" or "shadow" or "live") ||
                    !ValidTestName(name, kind) || !ValidHost(host, kind) ||
                    !string.IsNullOrEmpty(host) && !hosts.Add(CanonicalHost(host))) throw Invalid();
                host = CanonicalEndpoint(host);
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
    /// <summary>允許本階段核定的三個新測試組織與既有回歸組織，拒絕把任意輸入標成 test。</summary>
    private static bool ValidTestName(string name, string kind) =>
        kind != "test" || name is "goodshepherdback" or "goodshepherd20" or "construction" or "association";
    /// <summary>
    /// 生產組織必須有 DNS 主機；測試組織可暫留空字串。非空值可使用既有 DNS 格式或 HTTPS 基址，
    /// 但不得帶查詢、片段、路徑或大寫主機，避免 connector 被設定值擴權。
    /// </summary>
    private static bool ValidHost(string host, string kind)
    {
        if (host.Length == 0) return kind == "test";
        if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
            return uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
                string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
                (uri.AbsolutePath is "" or "/") && uri.IdnHost.Length <= 253 &&
                string.Equals(uri.IdnHost, uri.IdnHost.ToLowerInvariant(), StringComparison.Ordinal);
        return Uri.CheckHostName(host) == UriHostNameType.Dns &&
            host.Length <= 253 && !host.Any(character => character is >= 'A' and <= 'Z');
    }
    /// <summary>以不含秘密的 canonical host 作唯一性比較；空值不參與比較，因為它代表待補而非端點。</summary>
    private static string CanonicalHost(string host) =>
        Uri.TryCreate(host, UriKind.Absolute, out var uri) ? uri.IdnHost : host;
    /// <summary>將既有 DNS 清單值轉成穩定 HTTPS 基址；空值維持待補標記。</summary>
    private static string CanonicalEndpoint(string host) =>
        string.IsNullOrEmpty(host) ? host :
        Uri.TryCreate(host, UriKind.Absolute, out _) ? host :
        "https://" + host;
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
    /// <summary>讀取可代表待核定測試端點的 host；只有 test 可使用空字串，production 仍由 ValidHost 拒絕。</summary>
    private static string HostText(JsonElement row, string field, int maximum)
    {
        var token = row.GetProperty(field);
        return token.ValueKind == JsonValueKind.String && token.GetString() is { } value &&
            value.Length <= maximum && value.Trim() == value ? value : throw Invalid();
    }
    /// <summary>固定錯誤不含原文、名稱或host，避免將候選設定當成安全日誌內容。</summary>
    private static InvalidDataException Invalid() => new("Organization catalog is invalid or target is not allowlisted.");
}
