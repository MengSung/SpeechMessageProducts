/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-03a 的影子 trace/comparer 離線契約。
 * 測試只使用固定字串與純記憶體模型，不連線、不推 LINE、不讀
 * User Secrets，也不把離線結果當成實機證據。
 *
 * 主要功能與流程：覆蓋相同欄位、不同值、單側意圖、overflow、缺 end、易變
 * 欄位存在性，以及推播、關聯、型別精度與完整性拒絕。所有輸入與
 * 結果都是案例區域值，沒有背景工作、timer、stream 或 disposable owner。
 *
 * 使用邊界與維護：測試保護的是 parser fail-closed、比較不可冒稱零差異；
 * 格式以 05A §4.3 與正式 ShadowRecordWriter 為準，變更須同步決定性斷言。
 */
using System;
using System.Collections.Generic;
using System.IO;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以合成 trace 驗證 P5-03a 的離線解析、完整性與比較契約。</summary>
public sealed class ShadowContractTests
{
    /// <summary>相同意圖與欄位值應判定為可比較且無差異。</summary>
    [Fact]
    public void Shadow_SameFields_NoDifference()
    {
        var trace = Parse(
            """
SMV-SHADOW|1|{"seq":1,"operation":"Update","entity":"task","id":"task-1","fields":{"status":"done"}}
SMV-SHADOW|1|{"end":true,"count":1}
""");

        var independent = Parse("""
SMV-SHADOW|1|{"seq":1,"fields":{"status":"done"},"id":"task-1","entity":"task","operation":"Update"}
SMV-SHADOW|1|{"end":true,"count":1}
""");
        var result = ShadowComparer.Compare(trace, independent);

        Assert.True(result.IsEqual);
        Assert.Empty(result.Differences);
    }

    /// <summary>同一位置欄位值不同必須保留差異，不得用名稱或序號靜默合併。</summary>
    [Fact]
    public void Shadow_DifferentValue_Reported()
    {
        var left = Parse(
            """
SMV-SHADOW|1|{"seq":1,"operation":"Update","entity":"task","id":"task-1","fields":{"status":"done"}}
SMV-SHADOW|1|{"end":true,"count":1}
""");
        var right = Parse(
            """
SMV-SHADOW|1|{"seq":1,"operation":"Update","entity":"task","id":"task-1","fields":{"status":"pending"}}
SMV-SHADOW|1|{"end":true,"count":1}
""");

        var result = ShadowComparer.Compare(left, right);

        Assert.False(result.IsEqual);
        Assert.Contains(result.Differences, difference => difference.Kind == "DifferentValue");
    }

    /// <summary>只有一側有意圖時必須報告單側差異。</summary>
    [Fact]
    public void Shadow_OnlyOneSide_Reported()
    {
        var left = Parse("SMV-SHADOW|1|{\"end\":true,\"count\":0}");
        var right = Parse(
            """
SMV-SHADOW|1|{"seq":1,"operation":"Create","entity":"task","id":"task-1","fields":{}}
SMV-SHADOW|1|{"end":true,"count":1}
""");

        var result = ShadowComparer.Compare(left, right);

        Assert.Contains(result.Differences, difference => difference.Kind == "OnlyOneSide");
    }

    /// <summary>overflow 代表資料不完整，不能被判成零差異。</summary>
    [Fact]
    public void Shadow_Overflow_NotComparable()
    {
        var trace = Parse("SMV-SHADOW|1|{\"overflow\":true,\"total\":42}");

        Assert.False(trace.Comparable);
        Assert.False(ShadowComparer.Compare(trace, trace).IsEqual);
    }

    /// <summary>缺少 end 標記時必須不可比較。</summary>
    [Fact]
    public void Shadow_MissingEndMarker_NotComparable()
    {
        var trace = Parse("SMV-SHADOW|1|{\"seq\":1,\"operation\":\"Update\",\"entity\":\"task\",\"id\":\"task-1\",\"fields\":{}}");

        Assert.False(trace.Comparable);
    }

    /// <summary>volatile 欄位只比較是否存在，不比較其內容。</summary>
    [Fact]
    public void Shadow_VolatileField_ComparedByPresence()
    {
        var left = Parse(
            """
SMV-SHADOW|1|{"seq":1,"operation":"Update","entity":"task","id":"task-1","fields":{"modifiedon":"a"}}
SMV-SHADOW|1|{"end":true,"count":1}
""");
        var right = Parse(
            """
SMV-SHADOW|1|{"seq":1,"operation":"Update","entity":"task","id":"task-1","fields":{"modifiedon":"b"}}
SMV-SHADOW|1|{"end":true,"count":1}
""");

        Assert.True(ShadowComparer.Compare(left, right, new HashSet<string>(StringComparer.Ordinal) { "modifiedon" }).IsEqual);
    }

    /// <summary>
    /// 注入錯誤結尾計數、重複標記、序號缺口與重複 JSON 屬性；正式 parser 必須拒絕，
    /// 避免截斷或歧義記錄被宣稱完整，不依賴替身重現產品解析邏輯。
    /// </summary>
    /// <param name="jsonLines">不含前綴的合成 JSON 行，以換行區隔。</param>
    [Theory]
    [InlineData("{\"end\":true,\"count\":1}")]
    [InlineData("{\"end\":true,\"count\":0}\n{\"end\":true,\"count\":0}")]
    [InlineData("{\"end\":true,\"count\":0}\n{\"seq\":1,\"operation\":\"Delete\",\"entity\":\"task\",\"id\":\"task-1\"}")]
    [InlineData("{\"seq\":2,\"operation\":\"Delete\",\"entity\":\"task\",\"id\":\"task-1\"}\n{\"end\":true,\"count\":1}")]
    [InlineData("{\"seq\":1,\"operation\":\"Delete\",\"entity\":\"task\",\"id\":\"a\",\"id\":\"b\"}\n{\"end\":true,\"count\":1}")]
    [InlineData("{\"overflow\":true,\"total\":0}\n{\"end\":true,\"count\":0}")]
    public void Shadow_MalformedEnvelope_Refused(string jsonLines)
    {
        var trace = "SMV-SHADOW|1|" + jsonLines.Replace("\n", "\r\nSMV-SHADOW|1|", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => Parse(trace));
    }

    /// <summary>推播摘要沿用正式 writer 的欄位；只改訊息雜湊必須產生差異，不得遺失推播內容。</summary>
    [Fact]
    public void Shadow_PushHashDifference_Reported()
    {
        const string first = """
SMV-SHADOW|1|{"seq":1,"operation":"Push","contactId":"11111111-1111-1111-1111-111111111111","templateKey":"fees.paid","retryKey":"22222222-2222-2222-2222-222222222222","messageCount":1,"contentSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}
SMV-SHADOW|1|{"end":true,"count":1}
""";
        var second = first.Replace(new string('a', 64), new string('b', 64), StringComparison.Ordinal);
        var result = ShadowComparer.Compare(Parse(first), Parse(second));

        Assert.True(result.Comparable);
        Assert.Contains(result.Differences, difference => difference.Path == "contentSha256");
    }

    /// <summary>關聯對象 ID 改變必須報差異；不能只比較不存在的 fields。</summary>
    [Fact]
    public void Shadow_RelationshipDifference_Reported()
    {
        const string first = """
SMV-SHADOW|1|{"seq":1,"operation":"Associate","entity":"account","id":"account-1","relationship":"account_contacts","related":[{"entity":"contact","id":"contact-1"}]}
SMV-SHADOW|1|{"end":true,"count":1}
""";
        var second = first.Replace("contact-1", "contact-2", StringComparison.Ordinal);

        Assert.False(ShadowComparer.Compare(Parse(first), Parse(second)).IsEqual);
    }

    /// <summary>JSON 數字與字串不能混同；decimal 尾端差異及日期 offset 原文必須完整保留。</summary>
    /// <param name="first">手動給定的第一個欄位值。</param>
    /// <param name="second">只在值或型別不同的第二個欄位值。</param>
    [Theory]
    [InlineData("1", "\"1\"")]
    [InlineData("0.1234567890123456789012345678", "0.1234567890123456789012345679")]
    [InlineData("0.12345678901234567890123456781", "0.12345678901234567890123456782")]
    [InlineData("\"2026-11-14T00:00:00+08:00\"", "\"2026-11-13T16:00:00Z\"")]
    public void Shadow_TypedValues_ComparedWithoutPrecisionLoss(string first, string second)
    {
        const string template = "SMV-SHADOW|1|{\"seq\":1,\"operation\":\"Update\",\"entity\":\"task\",\"id\":\"task-1\",\"fields\":{\"value\":VALUE}}\r\nSMV-SHADOW|1|{\"end\":true,\"count\":1}";
        var result = ShadowComparer.Compare(Parse(template.Replace("VALUE", first, StringComparison.Ordinal)),
            Parse(template.Replace("VALUE", second, StringComparison.Ordinal)));

        Assert.False(result.IsEqual);
    }

    /// <summary>完整 trace 加 overflow/end 仍不可比對；total 是原始總數，count 是保留下來的意圖數。</summary>
    [Fact]
    public void Shadow_OverflowWithEnd_NotComparable()
    {
        var trace = Parse("""
SMV-SHADOW|1|{"seq":1,"operation":"Delete","entity":"task","id":"task-1"}
SMV-SHADOW|1|{"overflow":true,"total":20}
SMV-SHADOW|1|{"end":true,"count":1}
""");
        Assert.True(trace.HasEnd);
        Assert.Equal(20, trace.Total);
        Assert.False(trace.Comparable);
    }

    /// <summary>超量輸入須在解析前拒絕，避免保留超出單次 Execute 預算的資料。</summary>
    [Fact]
    public void Shadow_AboveCapacity_RefusedWithoutEcho()
    {
        var exception = Assert.Throws<InvalidDataException>(() => Parse(new string('x', 8_001)));
        Assert.DoesNotContain(new string('x', 50), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>跨兩個案例及多次回收後，先前唯讀 JSON 仍有效且不混入後續案例的欄位。</summary>
    [Fact]
    public void Shadow_ParsedValues_DetachedAcrossOperations()
    {
        var first = Parse("SMV-SHADOW|1|{\"seq\":1,\"operation\":\"Update\",\"entity\":\"task\",\"id\":\"task-a\",\"fields\":{\"value\":\"a\"}}\r\nSMV-SHADOW|1|{\"end\":true,\"count\":1}");
        for (var index = 0; index < 100; index++)
        {
            var second = Parse("SMV-SHADOW|1|{\"seq\":1,\"operation\":\"Update\",\"entity\":\"task\",\"id\":\"task-b\",\"fields\":{\"value\":\"b\"}}\r\nSMV-SHADOW|1|{\"end\":true,\"count\":1}");
            Assert.Equal("b", second.Intents[0].Projection.GetProperty("fields").GetProperty("value").GetString());
        }
        Assert.Equal("a", first.Intents[0].Projection.GetProperty("fields").GetProperty("value").GetString());
    }

    /// <summary>由正式 parser 建立 detached 結果，不自行解析 JSON 模擬產品行為。</summary>
    private static ShadowTrace Parse(string text) => ShadowTraceParser.Parse(text);
}
