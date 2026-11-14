/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-03a D5-5 的最終欄位比較契約，防止把新輪的 CRUD 意圖
 * 序列直接拿去與舊輪觀察快照比較。輸入全部是合成的記憶體資料，沒有實機結果。
 * 主要功能與流程：先確認產品具有依序套用意圖的入口，再驗證建立、更新、刪除、
 * 明確身分映射、型別精度與不完整輸入的拒絕，不使用替身重寫產品演算法。
 * 使用邊界與維護：測試不連線、不讀秘密、不推播；案例獨占輸入與結果，
 * 不保留跨案例狀態。JSON 文件由建立輸入的 helper 當場釋放，輸出必須 detached。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以離線輸入保護 CRUD 最終狀態投影，不把成功斷言當成實機驗收。</summary>
public sealed class ShadowProjectionTests
{
    /// <summary>
    /// 注入 Create→Update、多次 Update、Delete 及未更動欄位，以手算舊輪與 expected
    /// 快照驗證三組比較。若直接比較意圖或只取最後一筆，數量、欄位或刪除斷言必敗。
    /// 所有相依為正式 parser／投影器／comparer；JSON helper 當場釋放文件。
    /// </summary>
    [Fact]
    public void Shadow_TwoRounds_ComparedAfterApplyingIntents()
    {
        var initial = Snapshot(Row("new-task", """{"value":0,"retained":true}"""),
            Row("removed", """{"value":9}"""));
        var trace = Trace(
            """{"operation":"Create","entity":"task","id":"created","fields":{"value":1,"retained":"yes"}}""",
            """{"operation":"Update","entity":"task","id":"created","fields":{"value":2}}""",
            """{"operation":"Update","entity":"task","id":"new-task","fields":{"value":1}}""",
            """{"operation":"Update","entity":"task","id":"new-task","fields":{"value":3,"cleared":null}}""",
            """{"operation":"Delete","entity":"task","id":"removed"}""");
        var actual = ShadowProjection.Apply(initial, trace);
        var legacy = Snapshot(Row("old-task", """{"value":3,"retained":true,"cleared":null}"""),
            Row("old-created", """{"value":2,"retained":"yes"}"""));
        var expected = Snapshot(Row("task-1", """{"value":3,"retained":true,"cleared":null}"""),
            Row("task-2", """{"value":2,"retained":"yes"}"""));
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["new-task"] = "task-1", ["old-task"] = "task-1",
            ["created"] = "task-2", ["old-created"] = "task-2"
        };
        var observe = new[]
        {
            Observation("task-1", "value", "retained", "cleared"),
            Observation("task-2", "value", "retained"),
            Observation("removed", "value")
        };

        Assert.True(actual.Comparable);
        Assert.Equal(2, actual.Rows.Count);
        Assert.True(ShadowComparer.CompareFinalStates(legacy, actual, observe, aliases).IsEqual);
        Assert.True(ShadowComparer.CompareFinalStates(legacy, expected, observe, aliases).IsEqual);
        Assert.True(ShadowComparer.CompareFinalStates(actual, expected, observe, aliases).IsEqual);
        Assert.Equal(0, initial.Rows[0].Fields.GetProperty("value").GetInt32());
        var wrong = Snapshot(Row("task-1", """{"value":1,"retained":true,"cleared":null}"""),
            Row("task-2", """{"value":2,"retained":"yes"}"""));
        Assert.False(ShadowComparer.CompareFinalStates(actual, wrong, observe, aliases).IsEqual);
    }

    /// <summary>
    /// 以有效外框注入不適用欄位快照的關聯操作，並注入更新／刪除未知列、重複建立。
    /// 任一筆失敗都須不可比較且沒有部分列；成功重試不能沿用失敗候選。
    /// </summary>
    /// <param name="operation">由正式 parser 接受、但投影器必須拒絕的合成意圖。</param>
    [Theory]
    [InlineData("""{"operation":"Update","entity":"task","id":"missing","fields":{"value":1}}""")]
    [InlineData("""{"operation":"Delete","entity":"task","id":"missing"}""")]
    [InlineData("""{"operation":"Create","entity":"task","id":"existing","fields":{}}""")]
    [InlineData("""{"operation":"Associate","entity":"task","id":"existing","relationship":"task_contacts","related":[]}""")]
    public void Shadow_ProjectionInvalidTransition_DiscardsEntireCandidate(string operation)
    {
        var initial = Snapshot(Row("existing", """{"value":0}"""));
        var result = ShadowProjection.Apply(initial, Trace(
            """{"operation":"Update","entity":"task","id":"existing","fields":{"value":2}}""", operation));
        Assert.False(result.Comparable);
        Assert.Empty(result.Rows);
        var retry = ShadowProjection.Apply(initial, Trace());
        Assert.True(retry.Comparable);
        Assert.Equal(0, retry.Rows[0].Fields.GetProperty("value").GetInt32());
    }

    /// <summary>
    /// 注入 overflow、缺 end、偽造總數、未知操作與 metadata 不一致；即使外部直接
    /// 建立 record 並聲稱完整，投影仍須拒絕，不能僅信任 Comparable 旗標。
    /// </summary>
    [Fact]
    public void Shadow_ProjectionIncompleteOrForgedTrace_FailsClosed()
    {
        var valid = Trace("""{"operation":"Update","entity":"task","id":"existing","fields":{"value":2}}""");
        var intent = valid.Intents[0];
        using var unknown = JsonDocument.Parse(
            """{"seq":1,"operation":"Upsert","entity":"task","id":"existing","fields":{}}""");
        var traces = new[]
        {
            valid with { Overflow = true },
            valid with { HasEnd = false },
            valid with { Total = 2 },
            valid with { Intents = new[] { intent with { Id = "forged" } } },
            valid with { Intents = new[] { intent with { Operation = "Upsert", Projection = unknown.RootElement.Clone() } } }
        };
        foreach (var trace in traces)
        {
            var result = ShadowProjection.Apply(Snapshot(Row("existing", "{}")), trace);
            Assert.False(result.Comparable);
            Assert.Empty(result.Rows);
        }
    }

    /// <summary>手寫兩個不同精度／型別／日期值，投影後仍須有差異；不得經 double 或時區轉換。</summary>
    /// <param name="first">新輪安全 JSON 值。</param>
    /// <param name="second">預先手算的不同 expected 值。</param>
    [Theory]
    [InlineData("1", "\"1\"")]
    [InlineData("0.12345678901234567890123456781", "0.12345678901234567890123456782")]
    [InlineData("\"2026-11-14T00:00:00+08:00\"", "\"2026-11-13T16:00:00Z\"")]
    public void Shadow_FinalValues_PreservePrecisionTypeAndDate(string first, string second)
    {
        var actual = ShadowProjection.Apply(Snapshot(Row("existing", "{}")), Trace(
            "{\"operation\":\"Update\",\"entity\":\"task\",\"id\":\"existing\",\"fields\":{\"value\":" + first + "}}"));
        var expected = Snapshot(Row("existing", "{\"value\":" + second + "}"));
        var comparison = ShadowComparer.CompareFinalStates(actual, expected, new[] { Observation("existing", "value") });
        Assert.True(comparison.Comparable);
        Assert.False(comparison.IsEqual);
        Assert.Contains(comparison.Differences, difference => difference.Path == "fields.value");
        var projectedValue = actual.Rows[0].Fields.GetProperty("value");
        if (projectedValue.ValueKind == JsonValueKind.String)
            Assert.Equal(first[1..^1], projectedValue.GetString());
        else
            Assert.Equal(first, projectedValue.GetRawText());
    }

    /// <summary>
    /// 易變日期只比存在性；一側缺少與一側 null 仍不同。未列入 volatile 的 null／缺欄
    /// 同樣不同，確保「清空欄位」不被當成從未寫入。
    /// </summary>
    [Fact]
    public void Shadow_FinalVolatile_OnlyIgnoresValueWhenBothPresent()
    {
        var first = Snapshot(Row("existing", """{"modifiedon":"2026-11-14T00:00:00Z","value":null}"""));
        var second = Snapshot(Row("existing", """{"modifiedon":null,"value":null,"unobserved":"ignored"}"""));
        var observe = new[] { Observation("existing", "modifiedon", "value") };
        var volatileFields = new HashSet<string>(StringComparer.Ordinal) { "fields.modifiedon" };
        Assert.True(ShadowComparer.CompareFinalStates(first, second, observe, volatileFields: volatileFields).IsEqual);
        var missing = Snapshot(Row("existing", """{"value":null}"""));
        Assert.Contains(ShadowComparer.CompareFinalStates(first, missing, observe, volatileFields: volatileFields).Differences,
            difference => difference.Kind == "DifferentPresence");
        var missingNull = Snapshot(Row("existing", """{"modifiedon":null}"""));
        Assert.False(ShadowComparer.CompareFinalStates(second, missingNull, observe, volatileFields: volatileFields).IsEqual);
    }

    /// <summary>
    /// 只有列身分與 writer 的 entity/id 關聯可以套明確映射；同文的一般文字保持不變。
    /// 無映射時實際 ID 與代號不能自動配對，也不能按相同欄位值猜測身分。
    /// </summary>
    [Fact]
    public void Shadow_FinalAliases_ExplicitIdentityOnly()
    {
        var first = Snapshot(Row("actual", """{"owner":{"entity":"contact","id":"actual-contact"},"label":"actual-contact"}"""));
        var second = Snapshot(Row("task-1", """{"owner":{"entity":"contact","id":"contact-1"},"label":"actual-contact"}"""));
        var observe = new[] { Observation("task-1", "owner", "label") };
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["actual"] = "task-1", ["actual-contact"] = "contact-1"
        };
        Assert.False(ShadowComparer.CompareFinalStates(first, second, observe).IsEqual);
        Assert.True(ShadowComparer.CompareFinalStates(first, second, observe, aliases).IsEqual);
        var changedLabel = Snapshot(Row("task-1", """{"owner":{"entity":"contact","id":"contact-1"},"label":"contact-1"}"""));
        Assert.False(ShadowComparer.CompareFinalStates(first, changedLabel, observe, aliases).IsEqual);
        var ordinaryFields = Snapshot(Row("actual", """{"entity":"contact","id":"actual-contact"}"""));
        var differentOrdinaryFields = Snapshot(Row("task-1", """{"entity":"contact","id":"contact-1"}"""));
        Assert.False(ShadowComparer.CompareFinalStates(ordinaryFields, differentOrdinaryFields,
            new[] { Observation("task-1", "entity", "id") }, aliases).IsEqual);
    }

    /// <summary>
    /// 合法同名異 ID 列必須保留；重複原始 ID、正規化碰撞或重複 observe ID 都不可比較。
    /// 不得以字典覆寫／去重掩蓋來源衝突，診斷也不得帶出欄位內容。
    /// </summary>
    [Fact]
    public void Shadow_FinalDuplicateIdentities_RefusedWithoutMergingSameNames()
    {
        var sameName = Snapshot(Row("a", """{"label":"same"}"""), Row("b", """{"label":"same"}"""));
        var projected = ShadowProjection.Apply(sameName, Trace());
        Assert.True(projected.Comparable);
        Assert.Equal(2, projected.Rows.Count);
        var duplicate = Snapshot(Row("a", "{}"), Row("a", "{}"));
        Assert.False(ShadowProjection.Apply(duplicate, Trace()).Comparable);
        var observe = new[] { Observation("shared", "label") };
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "shared", ["b"] = "shared" };
        Assert.False(ShadowComparer.CompareFinalStates(projected, projected, observe, aliases).Comparable);
        Assert.False(ShadowComparer.CompareFinalStates(sameName, sameName,
            new[] { Observation("a", "label"), Observation("a", "label") }).Comparable);
    }

    /// <summary>
    /// 缺 observe、重複 JSON 欄位、過量列及不可比較快照必須拒絕；容量拒絕發生後不發布候選。
    /// 使用純記憶體故障輸入，不保留 reader 或 disposable 參照。
    /// </summary>
    [Fact]
    public void Shadow_FinalMalformedOrOversizedSnapshot_FailsClosed()
    {
        var valid = Snapshot(Row("a", "{}"));
        Assert.False(ShadowComparer.CompareFinalStates(valid, valid, Array.Empty<ShadowObservation>()).Comparable);
        Assert.False(ShadowProjection.Apply(Snapshot(Row("a", """{"value":1,"value":2}""")), Trace()).Comparable);
        Assert.False(ShadowProjection.Apply(Snapshot(Enumerable.Range(0, 65)
            .Select(index => Row(index.ToString(System.Globalization.CultureInfo.InvariantCulture), "{}")).ToArray()), Trace()).Comparable);
        var unavailable = new ShadowSnapshot(false, Array.Empty<ShadowRow>());
        Assert.False(ShadowComparer.CompareFinalStates(unavailable, valid, new[] { Observation("a", "value") }).Comparable);
    }

    /// <summary>
    /// A/B 案例交錯並行使用同一穩定 ID 但不同欄位，所有候選必須保留各自值且初始輸入不變。
    /// await 等待全部工作排空，沒有背景工作逸出測試 owner；離線斷言不代表實機 soak。
    /// </summary>
    [Fact]
    public async Task Shadow_ProjectionConcurrentOperations_NoSharedMutableState()
    {
        var initial = Snapshot(Row("same-id", """{"value":0}"""));
        var operations = Enumerable.Range(1, 64).Select(index => Task.Run(() =>
        {
            var value = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var result = ShadowProjection.Apply(initial, Trace(
                "{\"operation\":\"Update\",\"entity\":\"task\",\"id\":\"same-id\",\"fields\":{\"value\":" + value + "}}"));
            Assert.True(result.Comparable);
            Assert.Equal(index, result.Rows[0].Fields.GetProperty("value").GetInt32());
            return result;
        })).ToArray();
        var snapshots = await Task.WhenAll(operations);
        Assert.Equal(64, snapshots.Select(snapshot => snapshot.Rows[0].Fields.GetProperty("value").GetInt32()).Distinct().Count());
        Assert.Equal(0, initial.Rows[0].Fields.GetProperty("value").GetInt32());
    }

    /// <summary>以安全欄位 JSON 建立 detached 測試列，文件在 helper 返回前確定釋放。</summary>
    private static ShadowRow Row(string id, string fields)
    {
        using var document = JsonDocument.Parse(fields);
        return new ShadowRow("task", id, document.RootElement.Clone());
    }

    /// <summary>保留呼叫端指定順序建立輸入；正式入口仍須自行驗證重複身分。</summary>
    private static ShadowSnapshot Snapshot(params ShadowRow[] rows) => new(true, rows);

    /// <summary>明確列出 observe 欄位與穩定 ID，不按資料內容猜測對應。</summary>
    private static ShadowObservation Observation(string id, params string[] fields) => new("task", id, fields);

    /// <summary>只補固定外框，意圖驗證全部由正式 parser 執行，不複製產品解析邏輯。</summary>
    private static ShadowTrace Trace(params string[] intents)
    {
        var lines = new List<string>();
        for (var index = 0; index < intents.Length; index++)
            lines.Add("SMV-SHADOW|1|{\"seq\":" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "," + intents[index][1..]);
        lines.Add("SMV-SHADOW|1|{\"end\":true,\"count\":" +
            intents.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
        return ShadowTraceParser.Parse(string.Join("\r\n", lines));
    }
}
