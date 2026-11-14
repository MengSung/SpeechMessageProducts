/*
 * 檔案整體說明
 *
 * 目的與定位：提供 Deployment 影子兩輪比較的操作區域 DTO 與 CRUD 投影入口，
 * 將新輪寫入意圖轉成最終欄位快照，供 comparer 與舊輪 observe、expected 比較。
 * 主要功能與流程：輸入是呼叫端明確建立的初始快照與完整 trace，不猜測完整
 * scenarios 或 expected 格式；初始狀態應包含 trigger 已套用、規則尚未執行的值。
 * 使用邊界與維護：所有資料只屬本次呼叫，不持有服務、連線、Session 或背景工作。
 * 呼叫端負責授權、observe 查詢與安全欄位投影，本檔不宣稱已查詢實機或驗證租戶。
 * JSON 必須 detached；不支援或不完整的操作不得發布半成品或宣稱零差異。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>
/// 已存在的一筆安全欄位快照；刪除列以整筆不存在表示，null 欄位仍保留其存在性。
/// Entity 與 Id 是正式實體及穩定身分，Fields 須沿用 writer 的安全 JSON 型別投影。
/// 不得放入 SDK 物件、秘密或原始個資；呼叫端擁有輸入，投影結果另行複製。
/// </summary>
internal sealed record ShadowRow(string Entity, string Id, JsonElement Fields);

/// <summary>
/// 單次操作的完整快照；Comparable=false 表示候選未能完整建立，Rows 必須為空。
/// 建立輸入時可用 Comparable=true，但消費入口仍驗證重複身分、欄位與容量。
/// </summary>
internal sealed record ShadowSnapshot(bool Comparable, IReadOnlyList<ShadowRow> Rows);

/// <summary>
/// 由 scenarios.observe 解析並完成關聯查詢後的明確觀察範圍。
/// Id 可以是實際 ID 或明確映射的代號；Fields 只列此列需要比較的直屬欄位。
/// 本 DTO 不自行查詢、不以顯示名稱推測身分，也不擁有任何外部資源。
/// </summary>
internal sealed record ShadowObservation(string Entity, string Id, IReadOnlyList<string> Fields);

/// <summary>
/// 純記憶體 CRUD 投影器；只有整批成功才發布最終快照，不保存跨操作狀態。
/// 呼叫端應保留逐意圖黃金比較，最終快照比較不取代推播與關聯意圖的離線驗證。
/// </summary>
internal static class ShadowProjection
{
    // 與 recorder 的 64 列、128 欄邊界一致；快照總原文與節點另行設限，
    // 避免呼叫端 DTO 繞過 8,000 字元 trace 上限而造成無界複製。
    private const int MaximumRows = 64;
    private const int MaximumFields = 128;
    private const int MaximumCharacters = 65_536;
    private const int MaximumNodes = 2_048;

    /// <summary>
    /// 依 trace 原順序套用 Create、Update、Delete，保留未改動欄位與 null。
    /// 初始快照必須涵蓋所有待更新列；新增重複身分、更新或刪除不存在列均拒絕。
    /// 非 CRUD、overflow、缺 end 或未知輸入回傳不可比較且沒有候選列。
    /// </summary>
    /// <param name="initial">同一輪 trigger 已套用、規則尚未執行的安全欄位快照。</param>
    /// <param name="trace">正式 parser 解析的完整記錄，不以最後一筆取代順序套用。</param>
    /// <returns>本次操作獨有的 detached 完整快照，或沒有部分資料的失敗結果。</returns>
    public static ShadowSnapshot Apply(ShadowSnapshot initial, ShadowTrace trace)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(trace);
        if (!initial.Comparable || !trace.Comparable) return Unavailable();
        try
        {
            var rows = Prepare(initial, null);
            var validated = ValidateTrace(trace);
            foreach (var intent in validated.Intents)
            {
                var key = (intent.Entity, intent.Id);
                switch (intent.Operation)
                {
                    case "Create":
                        if (rows.Count >= MaximumRows || !rows.TryAdd(key,
                            new ShadowRow(intent.Entity, intent.Id, intent.Projection.GetProperty("fields").Clone())))
                            throw InvalidProjection();
                        break;
                    case "Update":
                        if (!rows.TryGetValue(key, out var existing)) throw InvalidProjection();
                        var fields = existing.Fields.EnumerateObject()
                            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
                        foreach (var property in intent.Projection.GetProperty("fields").EnumerateObject())
                            fields[property.Name] = property.Value;
                        if (fields.Count > MaximumFields) throw InvalidProjection();
                        rows[key] = new ShadowRow(intent.Entity, intent.Id, JsonSerializer.SerializeToElement(fields));
                        break;
                    case "Delete":
                        if (!rows.Remove(key)) throw InvalidProjection();
                        break;
                    default:
                        // 欄位快照不能表示推播或關聯操作；禁止靜默略過後宣稱整批成功。
                        throw InvalidProjection();
                }
            }
            var candidate = new ShadowSnapshot(true, Array.AsReadOnly(rows.Values.ToArray()));
            _ = Prepare(candidate, null);
            return candidate;
        }
        catch (InvalidDataException)
        {
            return Unavailable();
        }
    }

    /// <summary>
    /// 驗證完整快照並建立操作區域字典；身分碰撞即拒絕，不依顯示內容合併。
    /// JSON 複製保留數字原文、日期 offset 與 null，所有 mutable 容器只屬本次呼叫。
    /// </summary>
    internal static Dictionary<(string Entity, string Id), ShadowRow> Prepare(
        ShadowSnapshot snapshot, IReadOnlyDictionary<string, string>? aliases)
    {
        if (!snapshot.Comparable || snapshot.Rows is null || snapshot.Rows.Count > MaximumRows)
            throw InvalidProjection();
        var result = new Dictionary<(string Entity, string Id), ShadowRow>();
        var characters = 0;
        var nodes = 0;
        foreach (var row in snapshot.Rows)
        {
            if (row is null || !ValidIdentity(row.Entity) || !ValidIdentity(row.Id) ||
                row.Fields.ValueKind != JsonValueKind.Object ||
                row.Fields.EnumerateObject().Count() > MaximumFields) throw InvalidProjection();
            var rawLength = row.Fields.GetRawText().Length;
            if (rawLength > MaximumCharacters - characters) throw InvalidProjection();
            characters += rawLength;
            var id = Alias(row.Id, aliases);
            var fields = CopyValue(row.Fields, aliases, 0, ref nodes);
            if (!result.TryAdd((row.Entity, id), new ShadowRow(row.Entity, id, fields)))
                throw InvalidProjection();
        }
        return result;
    }

    /// <summary>
    /// 將呼叫端映射凍結為 Ordinal，禁止空值、過量與多段映射，確保一次正規化可重複使用。
    /// 多個不同實際 ID 可指向同一情境代號，但同一快照的碰撞仍由 Prepare 拒絕。
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? CopyAliases(IReadOnlyDictionary<string, string>? aliases)
    {
        if (aliases is null) return null;
        if (aliases.Count > 512) throw InvalidProjection();
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in aliases)
            if (!ValidIdentity(pair.Key) || !ValidIdentity(pair.Value) || !copy.TryAdd(pair.Key, pair.Value))
                throw InvalidProjection();
        foreach (var pair in copy)
            if (copy.TryGetValue(pair.Value, out var next) && !string.Equals(pair.Value, next, StringComparison.Ordinal))
                throw InvalidProjection();
        return copy;
    }

    /// <summary>只有呼叫端明確列出的實際身分可以換成代號；未列出的值原樣保留。</summary>
    internal static string Alias(string id, IReadOnlyDictionary<string, string>? aliases) =>
        aliases is not null && aliases.TryGetValue(id, out var alias) ? alias : id;

    /// <summary>限制結構識別字長度，拒絕缺身分；不解析或改寫 GUID 大小寫。</summary>
    internal static bool ValidIdentity(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;

    /// <summary>
    /// 遞迴複製並拒絕重複屬性、深度與節點超量；只改寫 writer 的 entity/id 關聯物件。
    /// 一般字串即使與映射鍵同文也不改，避免將商業欄位文字誤當成 ID。
    /// </summary>
    private static JsonElement CopyValue(JsonElement value, IReadOnlyDictionary<string, string>? aliases,
        int depth, ref int nodes)
    {
        if (depth > 32 || ++nodes > MaximumNodes) throw InvalidProjection();
        if (value.ValueKind == JsonValueKind.Object)
        {
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            // 深度零是欄位集合，即使有名為 entity/id 的商業欄位也不是 EntityReference。
            var reference = depth > 0 && value.TryGetProperty("entity", out var entity) && entity.ValueKind == JsonValueKind.String &&
                value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String;
            foreach (var property in value.EnumerateObject())
            {
                var child = reference && property.Name == "id"
                    ? JsonSerializer.SerializeToElement(Alias(property.Value.GetString()!, aliases))
                    : CopyValue(property.Value, aliases, depth + 1, ref nodes);
                if (!fields.TryAdd(property.Name, child)) throw InvalidProjection();
            }
            return JsonSerializer.SerializeToElement(fields);
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var items = new List<JsonElement>();
            foreach (var item in value.EnumerateArray()) items.Add(CopyValue(item, aliases, depth + 1, ref nodes));
            return JsonSerializer.SerializeToElement(items);
        }
        if (value.ValueKind is JsonValueKind.Undefined) throw InvalidProjection();
        return value.Clone();
    }

    /// <summary>
    /// DTO 亦可被手動建立，因此重新經正式 parser 驗證外框、序號、必要欄位與總長。
    /// 不信任 Comparable 一個旗標；控制標記與 record metadata 必須一致才可套用。
    /// </summary>
    private static ShadowTrace ValidateTrace(ShadowTrace trace)
    {
        if (trace.Intents is null || trace.Intents.Count > 128 || trace.Total != trace.Intents.Count)
            throw InvalidProjection();
        var text = new StringBuilder();
        foreach (var intent in trace.Intents)
        {
            if (intent is null || intent.Projection.ValueKind != JsonValueKind.Object) throw InvalidProjection();
            var raw = intent.Projection.GetRawText();
            if (raw.Length > 8_000 - text.Length - 16) throw InvalidProjection();
            text.Append("SMV-SHADOW|1|").Append(raw).Append("\r\n");
        }
        text.Append("SMV-SHADOW|1|{\"end\":true,\"count\":")
            .Append(trace.Intents.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('}');
        var validated = ShadowTraceParser.Parse(text.ToString());
        for (var index = 0; index < validated.Intents.Count; index++)
        {
            var actual = validated.Intents[index];
            var supplied = trace.Intents[index];
            if (actual.Sequence != supplied.Sequence ||
                !string.Equals(actual.Operation, supplied.Operation, StringComparison.Ordinal) ||
                !string.Equals(actual.Entity, supplied.Entity, StringComparison.Ordinal) ||
                !string.Equals(actual.Id, supplied.Id, StringComparison.Ordinal)) throw InvalidProjection();
        }
        return validated;
    }

    /// <summary>不完整候選一律清空，不讓呼叫端誤用失敗前已套用的列。</summary>
    private static ShadowSnapshot Unavailable() => new(false, Array.Empty<ShadowRow>());

    /// <summary>固定安全錯誤不回顯 JSON、實際 ID、欄位值或原始例外。</summary>
    internal static InvalidDataException InvalidProjection() => new("Invalid shadow projection.");
}
