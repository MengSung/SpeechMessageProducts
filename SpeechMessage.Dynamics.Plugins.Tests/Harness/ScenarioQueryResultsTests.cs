/*
檔案整體說明

目的與定位：驗證 P5-02b 情境 queryResults 確實傳入單案例假服務，讓未支援查詢可使用明列的合成 seed 答案；不代表舊入口或實機黃金驗收。
主要功能與流程：合成 Plugin 連續呼叫兩次假服務不支援的 Distinct 查詢，將第一次結果依序更新，並記錄第二次筆數。測試以正式編譯、獨立 AppDomain 與假服務檢查答案次序、空集合、同名不同 ID、未覆蓋查詢拒絕及跨案例隔離；非法序號、未知或重複身分與容量故障必須在入口執行前拒絕整批。
使用邊界與維護：沒有舊來源、正式個資、憑證、D365 或 LINE 依賴；所有 JSON 與合成來源只在本次測試存在。runner 擁有並卸載每案執行域、清除 scratch 及 Dispose 假服務；測試只持有分離的結果 JSON，不建立持久檔案、連線或背景工作。移除 queryResults 傳遞時，正向案例會收到 NotSupportedException 而無預期寫入。
*/
using System;
using LegacyGolden.PluginHarness;
using Newtonsoft.Json.Linq;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Harness;

/// <summary>使用真實合成入口執行驗證查詢答案的精確範圍、身分與生命週期。</summary>
public sealed class ScenarioQueryResultsTests
{
    private static readonly string[] Sources =
    {
        """
        using System; using Microsoft.Xrm.Sdk; using Microsoft.Xrm.Sdk.Query;
        public sealed class Sample : IPlugin { public void Execute(IServiceProvider p) {
        var c=(IPluginExecutionContext)p.GetService(typeof(IPluginExecutionContext));
        var f=(IOrganizationServiceFactory)p.GetService(typeof(IOrganizationServiceFactory));
        var s=f.CreateOrganizationService(c.UserId);
        var q=new QueryExpression("contact"){Distinct=true,ColumnSet=new ColumnSet(true)};
        var first=s.RetrieveMultiple(q); var rank=0;
        foreach(var row in first.Entities) {
        s.Update(new Entity(row.LogicalName,row.Id){["name"]=(string)row["fullname"],["new_rank"]=++rank}); }
        var second=s.RetrieveMultiple(q); var target=(Entity)c.InputParameters["Target"];
        s.Update(new Entity(target.LogicalName,target.Id){["new_result_count"]=second.Entities.Count});
        } }
        """
    };

    /// <summary>兩筆同名不同正式 ID 依明列順序各自更新；第二次顯式空答案不重用第一次答案。</summary>
    [Fact]
    public void QueryResults_OrderedAliases_PreserveSameNameDifferentIdsAndExplicitEmpty()
    {
        var result = Run(Document(new JObject { ["1"] = new JArray("contact-2", "contact-1"), ["2"] = new JArray() }));
        var row = GeneratedCase(result);
        Assert.Null(row["exception"]!.Value<string>());
        var writes = (JArray)row["writes"]!;
        Assert.Equal(3, writes.Count);
        Assert.Equal("contact-2", (string?)writes[0]!["alias"]);
        Assert.Equal("contact-1", (string?)writes[1]!["alias"]);
        Assert.Equal("same-name", (string?)writes[0]!["fields"]!["name"]);
        Assert.Equal("same-name", (string?)writes[1]!["fields"]!["name"]);
        Assert.Equal(1, (int)writes[0]!["fields"]!["new_rank"]!);
        Assert.Equal(2, (int)writes[1]!["fields"]!["new_rank"]!);
        Assert.Equal(0, (int)writes[2]!["fields"]!["new_result_count"]!);
    }

    /// <summary>沒有明列的未知查詢必須保留原本拒絕行為，不能把缺答案當成空集合。</summary>
    [Fact]
    public void QueryResults_UnspecifiedQuery_RemainsUnsupported()
    {
        var result = Run(Document(null));
        var row = GeneratedCase(result);
        Assert.Equal("NotSupportedException", (string?)row["exception"]);
        Assert.Empty((JArray)row["writes"]!);
    }

    /// <summary>只有第一筆答案時，第二次未知查詢仍拒絕；前一次明列覆蓋不構成全域 fallback。</summary>
    [Fact]
    public void QueryResults_OnlyFirstCallOverridden_SecondCallStillRefused()
    {
        var row = GeneratedCase(Run(Document(new JObject { ["1"] = new JArray("contact-2") })));
        Assert.Equal("NotSupportedException", (string?)row["exception"]);
        Assert.Single((JArray)row["writes"]!);
        Assert.Equal("contact-2", (string?)row["writes"]![0]!["alias"]);
    }

    /// <summary>兩個獨立案例的呼叫序號與答案均須重設；第二案缺覆蓋時不可讀到第一案答案。</summary>
    [Fact]
    public void QueryResults_SeparateCases_DoNotShareAnswersOrCallNumbers()
    {
        var document = Document(new JObject { ["1"] = new JArray(), ["2"] = new JArray() });
        var second = (JObject)Document(null)["cases"]![0]!.DeepClone();
        second["name"] = "isolated";
        ((JArray)document["cases"]!).Add(second);
        var result = Run(document);
        var first = GeneratedCase(result);
        Assert.Null(first["exception"]!.Value<string>());
        Assert.Single((JArray)first["writes"]!);
        Assert.Equal("NotSupportedException", (string?)result["cases"]![1]!["exception"]);
        Assert.Empty((JArray)result["cases"]![1]!["writes"]!);
    }

    /// <summary>非法答案不能默默忽略或執行 Plugin；整批固定拒絕且不得回傳部分黃金或輸入值。</summary>
    [Theory]
    [InlineData("unknown-alias")]
    [InlineData("duplicate-alias")]
    [InlineData("zero")]
    [InlineData("over-call-limit")]
    [InlineData("noncanonical-call")]
    [InlineData("nonarray")]
    [InlineData("nonobject")]
    [InlineData("null")]
    [InlineData("nonstrings")]
    [InlineData("over-row-limit")]
    public void QueryResults_InvalidFixture_RefusedBeforeEntryExecution(string fault)
    {
        var document = Document(new JObject { ["1"] = new JArray("contact-1"), ["2"] = new JArray() });
        var scenario = (JObject)document["cases"]![0]!;
        scenario["queryResults"] = fault switch
        {
            "unknown-alias" => new JObject { ["1"] = new JArray("unknown-private-alias") },
            "duplicate-alias" => new JObject { ["1"] = new JArray("contact-1", "contact-1") },
            "zero" => new JObject { ["0"] = new JArray() },
            "over-call-limit" => new JObject { ["257"] = new JArray() },
            "noncanonical-call" => new JObject { ["01"] = new JArray() },
            "nonarray" => new JObject { ["1"] = "contact-1" },
            "nonobject" => new JArray(),
            "null" => JValue.CreateNull(),
            "nonstrings" => new JObject { ["1"] = new JArray(1) },
            "over-row-limit" => new JObject { ["1"] = OversizedAliases() },
            _ => throw new InvalidOperationException("Unknown fixture fault.")
        };
        var result = Run(document);
        Assert.Equal("CompileFailed", (string?)result["status"]);
        Assert.Null(result["cases"]);
        Assert.DoesNotContain("unknown-private-alias", result.ToString(), StringComparison.Ordinal);
    }

    /// <summary>相同 JSON 屬性不能以最後值覆寫掩蓋情境歧義；必須在建立案例資源前拒絕。</summary>
    [Fact]
    public void QueryResults_DuplicateJsonCallKey_Refused()
    {
        var json = Document(null).ToString(Newtonsoft.Json.Formatting.None)
            .Replace("\"name\":\"query\"", "\"name\":\"query\",\"queryResults\":{\"1\":[],\"1\":[\"contact-1\"]}");
        var result = GoldenHarness.RunSources(Sources, json);
        Assert.Equal("CompileFailed", (string?)result["status"]);
        Assert.Null(result["cases"]);
    }

    /// <summary>建立新 JSON 候選；輸入答案只屬目前案例，不改寫任何固定黃金檔。</summary>
    private static JObject Document(JObject? answers)
    {
        var scenario = new JObject
        {
            ["name"] = "query",
            ["seed"] = new JArray(
                new JObject { ["alias"] = "contact-1", ["entity"] = "contact", ["fields"] = new JObject { ["fullname"] = "same-name" } },
                new JObject { ["alias"] = "contact-2", ["entity"] = "contact", ["fields"] = new JObject { ["fullname"] = "same-name" } }),
            ["trigger"] = new JObject
            {
                ["type"] = "Sample", ["message"] = "Update", ["entity"] = "contact",
                ["target"] = "contact-1", ["fields"] = new JObject(), ["depth"] = 1
            }
        };
        if (answers != null) scenario["queryResults"] = answers;
        return new JObject { ["cases"] = new JArray(scenario) };
    }

    /// <summary>注入超過單次總答案列容量的輸入，僅在測試記憶體配置有界陣列。</summary>
    private static JArray OversizedAliases()
    {
        var result = new JArray();
        for (var index = 0; index < 4097; index++) result.Add("contact-1");
        return result;
    }

    /// <summary>呼叫正式編譯與隔離執行管線；任何關閉與清理由 runner 的 finally 擁有。</summary>
    private static JObject Run(JObject document) => GoldenHarness.RunSources(Sources, document.ToString());

    /// <summary>先要求完整成功候選，再讀第一案；不能把基礎設施拒絕誤算成預期業務例外。</summary>
    private static JToken GeneratedCase(JObject result)
    {
        Assert.True((string?)result["status"] == "Generated", result.ToString());
        return result["cases"]![0]!;
    }
}
