/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P5 影子兩輪及黃金檔比較的純函式契約。比較只使用已投影的
 * operation、entity、stable ID、fields 與 push 摘要，不接觸 Dataverse、LINE、
 * User Secrets 或檔案系統，因此不會跨命令保留資料或資源。
 *
 * 主要功能與流程：先拒絕 overflow／缺少 end 的 trace，再依序列及欄位做精確
 * 比較；另以 ShadowProjection 的最終快照依 observe 明列欄位比較兩輪與 expected。
 * 日期等易變欄位只有在呼叫端明確列入 volatile 時比較「存在與否」。
 * 比較結果只含固定位置與差異類型，長值不回顯；所有候選與映射只活在本次呼叫。
 *
 * 使用邊界與維護：ID 正規化由呼叫端提供明確代號映射，不能按名稱猜測。任何
 * 新投影欄位都要同步加入黃金 schema 與測試，不能以忽略欄位方式掩蓋差異。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>一筆固定位置的安全比較差異。</summary>
internal sealed record ShadowDifference(int Sequence, string Path, string Kind);

/// <summary>影子比較結果；不可比較與零差異必須可區分。</summary>
internal sealed record ShadowComparison(bool Comparable, IReadOnlyList<ShadowDifference> Differences)
{
    /// <summary>兩側都可比較且沒有差異時才是成功。</summary>
    public bool IsEqual => Comparable && Differences.Count == 0;
}

/// <summary>比較兩個完整影子 trace，支援 D5-5 的 ID 與易變欄位有限正規化。</summary>
internal static class ShadowComparer
{
    /// <summary>
    /// 比較兩個最終欄位快照，依 observe 已解析的身分與欄位選取後精確比較。
    /// 實際 ID 只經呼叫端的明確映射正規化；不以姓名、順序或內容配對記錄。
    /// 呼叫端須分別比較舊輪與新輪、舊輪與 expected、新輪與 expected。
    /// 新輪與 expected 的寫入清單均應先經 ShadowProjection.Apply，不可直接傳意圖。
    /// </summary>
    /// <param name="left">舊輪 observe 或另一個已套用完成的快照。</param>
    /// <param name="right">新輪或 expected 的最終快照。</param>
    /// <param name="observe">已解析出穩定身分的觀察欄位，不在此執行關聯查詢。</param>
    /// <param name="idAliases">實際 ID 到情境代號的明確映射；只套用列身分及 entity/id 關聯。</param>
    /// <param name="volatileFields">明列的直屬欄位只比存在性，不轉換日期或數字。</param>
    /// <returns>只含位置與固定差異種類，不回顯欄位值或身分。</returns>
    public static ShadowComparison CompareFinalStates(
        ShadowSnapshot left, ShadowSnapshot right, IReadOnlyList<ShadowObservation> observe,
        IReadOnlyDictionary<string, string>? idAliases = null, IReadOnlySet<string>? volatileFields = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(observe);
        try
        {
            var aliases = ShadowProjection.CopyAliases(idAliases);
            var first = ShadowProjection.Prepare(left, aliases);
            var second = ShadowProjection.Prepare(right, aliases);
            if (observe.Count is 0 or > 64) throw ShadowProjection.InvalidProjection();
            var selected = new HashSet<(string Entity, string Id)>();
            var differences = new List<ShadowDifference>();
            for (var index = 0; index < observe.Count; index++)
            {
                var item = observe[index];
                if (item is null || !ShadowProjection.ValidIdentity(item.Entity) ||
                    !ShadowProjection.ValidIdentity(item.Id) || item.Fields is null || item.Fields.Count is 0 or > 128)
                    throw ShadowProjection.InvalidProjection();
                var key = (item.Entity, ShadowProjection.Alias(item.Id, aliases));
                if (!selected.Add(key)) throw ShadowProjection.InvalidProjection();
                var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in item.Fields)
                    if (!ShadowProjection.ValidIdentity(field) || !fieldNames.Add(field))
                        throw ShadowProjection.InvalidProjection();
                var hasFirst = first.TryGetValue(key, out var firstRow);
                var hasSecond = second.TryGetValue(key, out var secondRow);
                if (hasFirst != hasSecond)
                {
                    differences.Add(new ShadowDifference(index + 1, "row", "OnlyOneSide"));
                    continue;
                }
                if (!hasFirst) continue;
                // 只選 observe 指定欄位；未知／欠缺欄位不補 null，保留 presence 差異。
                var firstFields = firstRow!.Fields.EnumerateObject().Where(property => fieldNames.Contains(property.Name))
                    .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
                var secondFields = secondRow!.Fields.EnumerateObject().Where(property => fieldNames.Contains(property.Name))
                    .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
                CompareObject(index + 1, JsonSerializer.SerializeToElement(firstFields),
                    JsonSerializer.SerializeToElement(secondFields), "fields", volatileFields, differences);
            }
            return new ShadowComparison(true, differences.AsReadOnly());
        }
        catch (InvalidDataException)
        {
            return new ShadowComparison(false, Array.Empty<ShadowDifference>());
        }
    }

    /// <summary>逐序列比較兩側意圖；任何結構差異都保留為診斷項。</summary>
    /// <param name="left">第一輪或黃金側。</param>
    /// <param name="right">第二輪或實際側。</param>
    /// <param name="volatileFields">只比存在性的易變欄位名稱。</param>
    /// <returns>不含原始長值的 detached 結果。</returns>
    public static ShadowComparison Compare(
        ShadowTrace left,
        ShadowTrace right,
        IReadOnlySet<string>? volatileFields = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (!left.Comparable || !right.Comparable)
        {
            return new ShadowComparison(false, Array.Empty<ShadowDifference>());
        }

        var differences = new List<ShadowDifference>();
        var max = Math.Max(left.Intents.Count, right.Intents.Count);
        for (var index = 0; index < max; index++)
        {
            if (index >= left.Intents.Count || index >= right.Intents.Count)
            {
                differences.Add(new ShadowDifference(index + 1, "intent", "OnlyOneSide"));
                continue;
            }

            var expected = left.Intents[index];
            var actual = right.Intents[index];
            if (!string.Equals(expected.Operation, actual.Operation, StringComparison.Ordinal) ||
                !string.Equals(expected.Entity, actual.Entity, StringComparison.Ordinal) ||
                !string.Equals(expected.Id, actual.Id, StringComparison.Ordinal))
            {
                differences.Add(new ShadowDifference(expected.Sequence, "intent", "DifferentValue"));
                continue;
            }

            CompareObject(expected.Sequence, expected.Projection, actual.Projection, "", volatileFields, differences);
        }

        return new ShadowComparison(true, differences.AsReadOnly());
    }
    /// <summary>
    /// 逐欄位比較唯讀 JSON 投影，忽略物件屬性順序但保留所有值型別及陣列順序。
    /// 只在 fields 直屬欄位明列 volatile 時比存在性；其他位置一律精確比較。
    /// </summary>
    private static void CompareObject(int sequence, JsonElement left, JsonElement right, string path,
        IReadOnlySet<string>? volatileFields, List<ShadowDifference> differences)
    {
        var keys = left.EnumerateObject().Select(property => property.Name)
            .Concat(right.EnumerateObject().Select(property => property.Name))
            .Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (path.Length == 0 && key == "seq") continue;
            var hasLeft = left.TryGetProperty(key, out var leftValue);
            var hasRight = right.TryGetProperty(key, out var rightValue);
            var fieldPath = path.Length == 0 ? key : path + "." + key;
            if (path == "fields" && (volatileFields?.Contains(key) == true || volatileFields?.Contains(fieldPath) == true))
            {
                if (hasLeft != hasRight) differences.Add(new ShadowDifference(sequence, fieldPath, "DifferentPresence"));
                continue;
            }
            if (!hasLeft || !hasRight)
            {
                differences.Add(new ShadowDifference(sequence, fieldPath, "OnlyOneSide"));
                continue;
            }
            if (leftValue.ValueKind == JsonValueKind.Object && rightValue.ValueKind == JsonValueKind.Object)
                CompareObject(sequence, leftValue, rightValue, fieldPath, volatileFields, differences);
            else if (!SameValue(leftValue, rightValue))
                differences.Add(new ShadowDifference(sequence, fieldPath, "DifferentValue"));
        }
    }

    /// <summary>
    /// 數字比較 JSON 原文而不經 decimal／double 捨入；只正規化 ID 與易變欄位，
    /// 不讓超出 decimal 精度的不同數字變成相同值。
    /// 字串保持日期 offset 與大小寫，陣列保留每個穩定身分及原始順序。
    /// </summary>
    private static bool SameValue(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch
        {
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength() &&
                left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => SameValue(pair.First, pair.Second)),
            JsonValueKind.Object => left.EnumerateObject().Count() == right.EnumerateObject().Count() &&
                left.EnumerateObject().All(property => right.TryGetProperty(property.Name, out var value) && SameValue(property.Value, value)),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => false
        };
    }

}
