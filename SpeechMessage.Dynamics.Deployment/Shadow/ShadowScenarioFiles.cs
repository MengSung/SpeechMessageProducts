/*
 * 檔案整體說明
 * 目的與定位：部署工具的固定 Golden/Legacy 模組情境讀取邊界，將 scenarios 與
 * expected 驗成完整、獨立的檔案 DTO，供 SDK runtime 接線而不直接執行任何觸發。
 * 主要功能與流程：只從 repositoryRoot 下固定測試目錄讀兩檔，拒絕連結、未知模組、
 * 超過一 MiB／三十二層 JSON、重複或未知屬性，再驗案例一對一及 seed 參照。
 * 使用邊界與維護：legacy 永不開啟；不解析 User Secrets、不猜 GUID、不查詢組織。
 * 每次 Load 獨占檔案及 JsonDocument，using 在成功或失敗都釋放；只發布 Clone 後
 * 的唯讀 JSON 與集合，保留數字原文精度，無跨請求快取或背景工作。DTO 的生命期由
 * 命令呼叫端擁有，不含連線及憑證。活動僅保存並標記，是否可執行由 runtime 明確拒絕。
 * observe 目前只接受 alias/entity/fields，關聯查詢須另行核定 schema 後才能擴充。
 * 固定錯誤不回顯檔案本文；秘密樣式掃描是輔助守門，不能代替人工合成資料審查。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>
/// 一個經完整配對的檔案情境；所有 JSON 均已 Clone，來源文件關閉後仍可讀。
/// Seed、Trigger、Observe 保留原 schema，ExpectedWrites 不先轉為執行意圖；
/// Church、Now 及 QueryResults 保留離線重播資訊，DeclaredDifferences 為整檔宣告。
/// IsActivity 只是保存格式的識別，不能作為 runtime 已支援活動的成功證據。
/// </summary>
internal sealed record ShadowFileCase(string Name, bool LiveShadow, JsonElement Seed,
    JsonElement Trigger, JsonElement Observe, IReadOnlyList<string> Volatile,
    JsonElement ExpectedWrites, JsonElement ExpectedLineCalls, JsonElement Exception,
    string Basis, string Entrypoint, JsonElement Outputs, JsonElement Church,
    string Now, JsonElement QueryResults, JsonElement DeclaredDifferences, bool IsActivity);

/// <summary>無狀態且僅讀固定來源的嚴格情境 reader；失敗不發布部分案例。</summary>
internal static class ShadowScenarioFiles
{
    private const int MaximumBytes = 1_048_576;
    private const int MaximumRows = 64;
    private const int MaximumFields = 128;
    // 與離線 harness 相同的解碼後樣式檢查；固定逾時且不保留 Match 或原文字串。
    private static readonly Regex SecretPattern = new(
        @"U[0-9a-fA-F]{32}|[A-Za-z0-9+/=_-]{101,}|[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]+|Bearer\s+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));

    /// <summary>
    /// 從固定產品測試根讀 scenarios.json 與 expected.json；只接受 05A 十個模組代號。
    /// 每份文件最多一 MiB／三十二層，每檔一至六案且名稱恰好配對；所有驗證成功才
    /// 回傳 detached 唯讀集合。呼叫端仍須驗證組織、授權、實機綁定及執行能力。
    /// </summary>
    public static IReadOnlyList<ShadowFileCase> Load(string repositoryRoot, string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (module is not ("diagnostics" or "localization" or "weeklyreports" or "attendance" or
            "lessons" or "appointments" or "qrcodes" or "care" or "fees" or "messaging")) throw Invalid();
        var directory = Path.GetFullPath(repositoryRoot);
        RejectLink(directory);
        foreach (var segment in new[] { "SpeechMessageProducts.GoodShepherd.Core.Tests", "Golden", "Legacy", module })
        {
            directory = Path.Combine(directory, segment);
            RejectLink(directory);
        }
        using var scenarios = Read(Path.Combine(directory, "scenarios.json"));
        using var expected = Read(Path.Combine(directory, "expected.json"));
        var source = scenarios.RootElement;
        var target = expected.RootElement;
        Shape(source, "module,cases");
        Shape(target, "module,basis,cases,declaredDifferences");
        if (Text(source, "module") != module || Text(target, "module") != module) throw Invalid();
        var basis = Text(target, "basis");
        if (basis is not ("legacy-live" or "legacy-golden" or "legacy-live-only" or "reading" or "deferred-to-P7" or "new")) throw Invalid();
        var input = Cases(source.GetProperty("cases"));
        var output = Cases(target.GetProperty("cases"));
        if (!input.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(output.Keys)) throw Invalid();
        Differences(target.GetProperty("declaredDifferences"), input);
        var result = new List<ShadowFileCase>(input.Count);
        foreach (var (name, item) in input)
        {
            Shape(item, "name,church,seed,trigger,now,volatile,liveShadow,observe", "queryResults");
            var aliases = Seeds(item.GetProperty("seed"));
            var church = item.GetProperty("church");
            Shape(church, "alias,organizationName,timeZone,culture");
            if (!aliases.ContainsKey(Text(church, "alias"))) throw Invalid();
            _ = Text(church, "organizationName"); _ = Text(church, "timeZone"); _ = Text(church, "culture");
            var now = Text(item, "now");
            if (!now.EndsWith('Z') || !DateTimeOffset.TryParse(now, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _)) throw Invalid();
            var live = Boolean(item.GetProperty("liveShadow"));
            var trigger = item.GetProperty("trigger");
            var activity = Trigger(trigger, aliases, live);
            Observe(item.GetProperty("observe"), aliases);
            var volatileFields = Names(item.GetProperty("volatile"), MaximumFields, schema: true);
            var queries = item.TryGetProperty("queryResults", out var query) ? query : EmptyObject();
            Queries(queries, aliases);
            var desired = output[name];
            Shape(desired, "name,entrypoint,writes,lineCalls,outputs,exception");
            var entrypoint = Text(desired, "entrypoint");
            if (entrypoint != Text(trigger, "type")) throw Invalid();
            Writes(desired.GetProperty("writes"));
            LineCalls(desired.GetProperty("lineCalls"));
            Outputs(desired.GetProperty("outputs"));
            var exception = desired.GetProperty("exception");
            if (exception.ValueKind != JsonValueKind.Null &&
                (exception.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(exception.GetString()))) throw Invalid();
            result.Add(new ShadowFileCase(name, live, item.GetProperty("seed").Clone(), trigger.Clone(),
                item.GetProperty("observe").Clone(), System.Array.AsReadOnly(volatileFields),
                desired.GetProperty("writes").Clone(), desired.GetProperty("lineCalls").Clone(), exception.Clone(),
                basis, entrypoint, desired.GetProperty("outputs").Clone(), church.Clone(), now, queries.Clone(),
                target.GetProperty("declaredDifferences").Clone(), activity));
        }
        return result.AsReadOnly();
    }

    /// <summary>以唯讀串流先驗長度並限制實讀；文件由 Load 的 using 唯一擁有。</summary>
    private static JsonDocument Read(string path)
    {
        RejectLink(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > MaximumBytes) throw Invalid();
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw Invalid();
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            Visit(document.RootElement);
            return document;
        }
        catch (Exception failure) when (failure is JsonException or InvalidDataException or RegexMatchTimeoutException)
        {
            document?.Dispose();
            throw Invalid();
        }
    }

    /// <summary>固定路徑每段都拒絕 reparse point，避免模組檔被替換為外部資料來源。</summary>
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Invalid();
    }

    /// <summary>所有物件含動態欄位均拒絕重複鍵；解碼後屬性與值同步掃描秘密樣式。</summary>
    private static void Visit(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || SecretPattern.IsMatch(property.Name)) throw Invalid();
                Visit(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) Visit(child);
        else if (element.ValueKind == JsonValueKind.String && SecretPattern.IsMatch(element.GetString()!)) throw Invalid();
    }

    /// <summary>只允許明列必要與選填屬性；缺值與未知鍵均不採寬鬆反序列化。</summary>
    private static void Shape(JsonElement value, string required, string optional = "")
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var requiredNames = required.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var allowed = requiredNames.Concat(optional.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal);
        if (value.EnumerateObject().Any(property => !allowed.Contains(property.Name)) ||
            requiredNames.Any(name => !value.TryGetProperty(name, out _))) throw Invalid();
    }

    /// <summary>建立 operation-local 案例索引，名稱不能依最後寫入覆蓋，也不按顯示內容去重。</summary>
    private static Dictionary<string, JsonElement> Cases(JsonElement value)
    {
        Array(value, 6, minimum: 1);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !result.TryAdd(Text(item, "name"), item)) throw Invalid();
        }
        return result;
    }

    /// <summary>先收齊唯一 alias 再驗欄位參照，允許顯式前向參照但不猜測 ID。</summary>
    private static Dictionary<string, string> Seeds(JsonElement value)
    {
        Array(value, MaximumRows, minimum: 1);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in value.EnumerateArray())
        {
            Shape(row, "alias,entity,fields");
            var entity = Text(row, "entity");
            if (!Schema(entity) || !Alias(Text(row, "alias")) || !aliases.TryAdd(Text(row, "alias"), entity)) throw Invalid();
        }
        foreach (var row in value.EnumerateArray()) Fields(row.GetProperty("fields"), aliases);
        return aliases;
    }

    /// <summary>驗 CRUD 目標存在且實體一致；活動只保存明確 activity/inputs，仍須驗 depth。</summary>
    private static bool Trigger(JsonElement value, Dictionary<string, string> aliases, bool live)
    {
        var activity = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("activity", out var flag) && Boolean(flag);
        if (activity)
        {
            Shape(value, "project,type,activity,inputs,depth");
            Fields(value.GetProperty("inputs"), aliases, inputParameters: true);
        }
        else
        {
            Shape(value, "project,type,message,entity,target,fields,depth", "images");
            if (Text(value, "message") is not ("Create" or "Update" or "Delete")) throw Invalid();
            Match(aliases, Text(value, "target"), Text(value, "entity"));
            Fields(value.GetProperty("fields"), aliases);
            if (value.TryGetProperty("images", out var images))
            {
                Shape(images, "", "pre,post");
                foreach (var side in images.EnumerateObject())
                {
                    if (side.Value.ValueKind != JsonValueKind.Object || side.Value.EnumerateObject().Count() > MaximumRows) throw Invalid();
                    foreach (var image in side.Value.EnumerateObject())
                    {
                        if (!Alias(image.Name)) throw Invalid();
                        Fields(image.Value, aliases);
                    }
                }
            }
        }
        _ = Text(value, "project"); _ = Text(value, "type");
        if (!Integer(value.GetProperty("depth"), out var depth) || depth < 1 || (live && depth > 1)) throw Invalid();
        return activity;
    }

    /// <summary>目前僅接受 seed 別名與同實體的明確觀察欄位，不推測未定義的關聯查詢。</summary>
    private static void Observe(JsonElement value, Dictionary<string, string> aliases)
    {
        Array(value, MaximumRows, minimum: 1);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in value.EnumerateArray())
        {
            Shape(row, "alias,entity,fields");
            var alias = Text(row, "alias");
            Match(aliases, alias, Text(row, "entity"));
            if (!seen.Add(alias)) throw Invalid();
            _ = Names(row.GetProperty("fields"), MaximumFields, schema: true, minimum: 1);
        }
    }

    /// <summary>欄位鍵必須是 schema 名稱，活動輸入則接受區分大小寫的識別字；值不轉 double。</summary>
    private static void Fields(JsonElement value, Dictionary<string, string>? aliases, bool inputParameters = false)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() > MaximumFields) throw Invalid();
        foreach (var property in value.EnumerateObject())
        {
            if (!(inputParameters ? Identifier(property.Name) : Schema(property.Name))) throw Invalid();
            if (property.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)) throw Invalid();
            if (property.Name == "new_lineid" && property.Value.ValueKind != JsonValueKind.Null &&
                (property.Value.ValueKind != JsonValueKind.Object || !property.Value.TryGetProperty("line", out _))) throw Invalid();
            Value(property.Value, aliases);
        }
    }

    /// <summary>只接受 scalar、陣列與明確型別 marker；ref 依 alias/entity 核對，絕不生成 GUID。</summary>
    private static void Value(JsonElement value, Dictionary<string, string>? aliases)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            Array(value, MaximumRows);
            foreach (var item in value.EnumerateArray()) Value(item, aliases);
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("ref", out _))
            {
                Shape(value, "ref,entity", "name");
                Reference(value, "ref", aliases);
            }
            else if (value.TryGetProperty("alias", out _))
            {
                Shape(value, "alias,entity", "name");
                Reference(value, "alias", aliases);
            }
            else if (value.TryGetProperty("reference", out var reference))
            {
                Shape(value, "reference"); Shape(reference, "alias,entity", "name");
                Reference(reference, "alias", aliases);
            }
            else if (value.TryGetProperty("line", out _))
            {
                Shape(value, "line");
                if (aliases is null || !aliases.ContainsKey(Text(value, "line"))) throw Invalid();
            }
            else if (value.TryGetProperty("money", out var money))
            {
                Shape(value, "money"); if (money.ValueKind != JsonValueKind.Number) throw Invalid();
            }
            else if (value.TryGetProperty("option", out var option))
            {
                Shape(value, "option"); if (!Integer(option, out _)) throw Invalid();
            }
            else throw Invalid();
        }
    }

    /// <summary>有 seed 時精確核對別名與實體；期望新增列尚未建立，只驗明確身分形狀。</summary>
    private static void Reference(JsonElement value, string key, Dictionary<string, string>? aliases)
    {
        var alias = Text(value, key); var entity = Text(value, "entity");
        if (!Alias(alias) || !Schema(entity)) throw Invalid();
        if (aliases is not null) Match(aliases, alias, entity);
        if (value.TryGetProperty("name", out var name) && name.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw Invalid();
    }

    /// <summary>期望寫入只保存 JSON；目前 schema 明列 CRUD，未知操作或未知鍵均拒絕。</summary>
    private static void Writes(JsonElement value)
    {
        Array(value, 128);
        foreach (var row in value.EnumerateArray())
        {
            Shape(row, "operation,entity,alias,fields");
            if (Text(row, "operation") is not ("Create" or "Update" or "Delete") ||
                !Schema(Text(row, "entity")) || !Alias(Text(row, "alias"))) throw Invalid();
            Fields(row.GetProperty("fields"), null);
        }
    }

    /// <summary>LINE 期望只保留合成 recipient 及端點投影，不執行 HTTP、不解析憑證。</summary>
    private static void LineCalls(JsonElement value)
    {
        Array(value, 128);
        foreach (var call in value.EnumerateArray())
        {
            Shape(call, "method,endpoint,recipients,messageCount", "messages");
            if (Text(call, "method") != "POST" || Text(call, "endpoint") is not
                ("/v2/bot/message/push" or "/v2/bot/message/multicast" or "/v2/bot/message/broadcast") ||
                !Integer(call.GetProperty("messageCount"), out var count) || count is < 0 or > 5) throw Invalid();
            _ = Names(call.GetProperty("recipients"), MaximumRows, schema: false);
            if (call.TryGetProperty("messages", out var messages))
            {
                Array(messages, 5);
                foreach (var message in messages.EnumerateArray())
                {
                    Shape(message, "type,text");
                    if (Text(message, "type") != "text") throw Invalid();
                    _ = Text(message, "text");
                }
                if (messages.GetArrayLength() != count) throw Invalid();
            }
        }
    }

    /// <summary>outputs 支援既有 observed 摘要與活動的純值輸出；未知巢狀物件不靜默保留。</summary>
    private static void Outputs(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() > MaximumFields) throw Invalid();
        foreach (var property in value.EnumerateObject())
        {
            if (!Identifier(property.Name)) throw Invalid();
            if (property.Name == "observed")
            {
                var observed = property.Value;
                if (observed.ValueKind != JsonValueKind.Object) throw Invalid();
                if (observed.TryGetProperty("execution", out _))
                {
                    Shape(observed, "execution,depth");
                    if (Text(observed, "execution") != "blocked-by-depth" || !Integer(observed.GetProperty("depth"), out var depth) || depth < 1) throw Invalid();
                }
                else
                {
                    Shape(observed, "entity,alias,changedFields");
                    if (!Schema(Text(observed, "entity")) || !Alias(Text(observed, "alias"))) throw Invalid();
                    _ = Names(observed.GetProperty("changedFields"), MaximumFields, schema: true);
                }
            }
            else Value(property.Value, null);
        }
    }

    /// <summary>宣告差異必須指向現存情境並列明 D5-6 類別與依據；不改寫原始內容。</summary>
    private static void Differences(JsonElement value, Dictionary<string, JsonElement> cases)
    {
        Array(value, 128);
        foreach (var difference in value.EnumerateArray())
        {
            Shape(difference, "case,position,category,reason,test");
            if (!cases.ContainsKey(Text(difference, "case")) ||
                !Integer(difference.GetProperty("category"), out var category) || category is < 1 or > 5) throw Invalid();
            _ = Text(difference, "position"); _ = Text(difference, "reason"); _ = Text(difference, "test");
        }
    }

    /// <summary>離線查詢覆蓋只允許一至二五六次序與已存在的 seed 別名，每次最多六十四列。</summary>
    private static void Queries(JsonElement value, Dictionary<string, string> aliases)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() > 256) throw Invalid();
        foreach (var property in value.EnumerateObject())
        {
            if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index is < 1 or > 256 || index.ToString(CultureInfo.InvariantCulture) != property.Name) throw Invalid();
            foreach (var alias in Names(property.Value, MaximumRows, schema: false))
                if (!aliases.ContainsKey(alias)) throw Invalid();
        }
    }

    /// <summary>驗 bounded 唯一欄位或別名清單，空清單僅於契約允許的位置成立。</summary>
    private static string[] Names(JsonElement value, int maximum, bool schema, int minimum = 0)
    {
        Array(value, maximum, minimum);
        var result = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) throw Invalid();
            var name = item.GetString()!;
            if (!(schema ? Schema(name) : Alias(name)) || !seen.Add(name)) throw Invalid();
            result.Add(name);
        }
        return result.ToArray();
    }

    /// <summary>所有固定陣列先驗型別與數量，避免錯型別被當空集合。</summary>
    private static void Array(JsonElement value, int maximum, int minimum = 0)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < minimum || value.GetArrayLength() > maximum) throw Invalid();
    }
    /// <summary>必要文字最多四千字且非空；錯誤訊息不插入原字串。</summary>
    private static string Text(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(item.GetString()) || item.GetString()!.Length > 4000) throw Invalid();
        return item.GetString()!;
    }
    /// <summary>整數先驗 JSON 型別再取值；字串、超界與小數統一拒絕而不洩漏解析內容。</summary>
    private static bool Integer(JsonElement value, out int number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number);
    }
    /// <summary>布林必須為 JSON true/false，不將字串或數字強制轉型。</summary>
    private static bool Boolean(JsonElement value) => value.ValueKind switch
    { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw Invalid() };
    /// <summary>精確核對同情境 alias 的實體，不按名稱或欄位值猜測身分。</summary>
    private static void Match(Dictionary<string, string> aliases, string alias, string entity)
    {
        if (!aliases.TryGetValue(alias, out var actual) || actual != entity) throw Invalid();
    }
    /// <summary>schema 僅接受 ASCII 小寫字母起首與數字、底線，最多一百二十八字。</summary>
    private static bool Schema(string name) => name.Length is > 0 and <= 128 && name[0] is >= 'a' and <= 'z' &&
        name.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
    /// <summary>合成別名允許英數、底線與連字號，禁止路徑、空白與真 GUID 推測。</summary>
    private static bool Alias(string name) => name.Length is > 0 and <= 128 &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    /// <summary>活動參數與映像名稱允許大小寫識別字，不接受點號及路徑語法。</summary>
    private static bool Identifier(string name) => name.Length is > 0 and <= 128 &&
        (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    /// <summary>選填 queryResults 缺省為獨立空物件，立即 Clone 後關閉短生命文件。</summary>
    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
    /// <summary>所有結構或內容錯誤使用固定訊息，不附帶可能包含秘密的解析器例外。</summary>
    private static InvalidDataException Invalid() => new("Shadow scenario files rejected by the strict safety contract.");
}
