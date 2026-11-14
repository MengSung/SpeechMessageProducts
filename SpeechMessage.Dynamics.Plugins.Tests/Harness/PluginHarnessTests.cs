/*
檔案整體說明
目的與定位：保護P5-02確實編譯與執行舊入口的離線契約，防止手寫合成結果冒充黃金。
主要功能與流程：案例傳入合成IPlugin或CodeActivity來源，經正式compiler、隔離執行與傳輸替身取得寫入、LINE內容及活動輸出；注入編譯錯誤、未攔截網路及秘密，斷言拒絕且不發布。
使用邊界與維護：不依賴外部舊方案、不讀秘密、不連D365或LINE。runner擁有每次執行域與scratch並清理，測試擁有唯一輸出目錄並於finally刪除。相同來源兩次執行必須位元組相同，跨案例狀態不得保留。
*/
using System;
using System.IO;
using LegacyGolden.PluginHarness;
using Newtonsoft.Json.Linq;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Harness;

/// <summary>使用實際動態編譯及執行驗證黃金來源與安全發布。</summary>
public sealed class PluginHarnessTests
{
    private const string Plugin = """
        using System; using System.Net.Http; using System.Text; using Microsoft.Xrm.Sdk;
        public sealed class Sample : IPlugin { public void Execute(IServiceProvider p) {
        var c=(IPluginExecutionContext)p.GetService(typeof(IPluginExecutionContext));
        var f=(IOrganizationServiceFactory)p.GetService(typeof(IOrganizationServiceFactory));
        var s=f.CreateOrganizationService(c.UserId); var e=(Entity)c.InputParameters["Target"];
        s.Update(new Entity(e.LogicalName,e.Id){["name"]="changed"});
        using(var client=new HttpClient()) {
        using(var result=client.PostAsync("https://api.line.me/v2/bot/message/push",
        new StringContent("{\"to\":\"U00000000000000000000000000000001\",\"messages\":[{\"type\":\"text\",\"text\":\"hello\"}]}",
        Encoding.UTF8,"application/json")).GetAwaiter().GetResult()) {} }
        } }
        """;
    private const string Scenarios = """
        {"cases":[{"name":"normal","seed":[{"alias":"contact-1","entity":"contact","fields":{}}],
        "trigger":{"type":"Sample","message":"Update","entity":"contact","target":"contact-1","fields":{},"depth":1}}]}
        """;

    /// <summary>移除舊Execute或HTTP攔截後，真實寫入及訊息斷言必定失敗。</summary>
    [Fact]
    public void Harness_SyntheticPlugin_RecordsWritesAndLineCalls()
    {
        var result = GoldenHarness.RunSources(new[] { Plugin }, Scenarios);
        Assert.Equal("Generated", (string?)result["status"]);
        var row = result["cases"]![0]!;
        Assert.Equal("changed", (string?)row["writes"]![0]!["fields"]!["name"]);
        Assert.Equal("contact-1", (string?)row["writes"]![0]!["alias"]);
        Assert.Equal("contact-1", (string?)row["lineCalls"]![0]!["recipients"]![0]);
        Assert.Equal("hello", (string?)row["lineCalls"]![0]!["content"]![0]!["text"]);
        Assert.Null(row["exception"]!.Value<string>());
    }

    /// <summary>WorkflowInvoker執行活動輸入加七，斷言實際OutArgument而非靜態預期。</summary>
    [Fact]
    public void Harness_SyntheticActivity_RecordsOutputArguments()
    {
        const string source = """
            using System.Activities; public sealed class Sample : CodeActivity {
            public InArgument<int> Count {get;set;} public OutArgument<int> Result {get;set;}
            protected override void Execute(CodeActivityContext c) { Result.Set(c,Count.Get(c)+7); }}
            """;
        var scenarios = JObject.Parse(Scenarios);
        scenarios["cases"]![0]!["trigger"]!["activity"] = true;
        scenarios["cases"]![0]!["trigger"]!["inputs"] = new JObject { ["Count"] = 5 };
        var result = GoldenHarness.RunSources(new[] { source }, scenarios.ToString());
        Assert.Equal(12, (int)result["cases"]![0]!["outputs"]!["Result"]!);
    }

    /// <summary>兩次獨立編譯及隔離執行，不能被隨機識別或主機時鐘污染。</summary>
    [Fact]
    public void Harness_SameScenarioTwice_ByteIdenticalOutput() =>
        Assert.Equal(GoldenHarness.RunSources(new[] { Plugin }, Scenarios).ToString(),
            GoldenHarness.RunSources(new[] { Plugin }, Scenarios).ToString());

    /// <summary>編譯錯誤只回固定代碼及檔名，沒有部分案例或來源原文。</summary>
    [Fact]
    public void Harness_CompileFailure_ReportedNotThrown()
    {
        var result = GoldenHarness.RunSources(new[] { "public class Bad { invalid syntax }" }, Scenarios);
        Assert.Equal("CompileFailed", (string?)result["status"]);
        Assert.NotEmpty((JArray)result["diagnostics"]!);
        Assert.Null(result["cases"]);
    }

    /// <summary>WebClient在組件載入前被拒絕，不需要觸發或嘗試連線。</summary>
    [Fact]
    public void Harness_UninterceptedNetworkApi_RefusesToRun()
    {
        var result = GoldenHarness.RunSources(new[] {
            "public class Bad { public void Go(){new System.Net.WebClient().DownloadString(\"https://example.invalid\");} }"
        }, Scenarios);
        Assert.Equal("BlockedByNetworkApi", (string?)result["status"]);
        Assert.Null(result["cases"]);
    }

    /// <summary>秘密輸出既不能新建檔案也不能覆蓋完整舊檔，案例owner最後清除唯一目錄。</summary>
    [Theory]
    [InlineData("U12345678901234567890123456789012")]
    [InlineData("Bearer secret")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature")]
    public void Harness_OutputWithSecretPattern_NotWritten(string secret)
    {
        var directory = Path.Combine(Path.GetTempPath(), "smv-harness-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "golden.json");
        try
        {
            Assert.Throws<InvalidDataException>(() => GoldenWriter.Write(path, new JObject { ["value"] = secret }));
            Assert.False(File.Exists(path));
            File.WriteAllText(path, "previous");
            Assert.Throws<InvalidDataException>(() => GoldenWriter.Write(path, new JObject { ["value"] = secret }));
            Assert.Equal("previous", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
