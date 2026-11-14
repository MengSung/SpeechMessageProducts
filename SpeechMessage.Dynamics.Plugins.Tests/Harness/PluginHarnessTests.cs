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
        Assert.True((string?)result["status"] == "Generated", result.ToString());
        var row = result["cases"]![0]!;
        Assert.True(((JArray)row["writes"]!).Count > 0, result.ToString());
        Assert.True(((JArray)row["lineCalls"]!).Count > 0, result.ToString());
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
        Assert.True((string?)result["status"] == "Generated", result.ToString());
        Assert.Equal(12, (int)result["cases"]![0]!["outputs"]!["Result"]!);
    }

    /// <summary>兩次獨立編譯及隔離執行，不能被隨機識別或主機時鐘污染。</summary>
    [Fact]
    public void Harness_SameScenarioTwice_ByteIdenticalOutput() =>
        Assert.Equal(GoldenHarness.RunSources(new[] { Plugin }, Scenarios).ToString(),
            GoldenHarness.RunSources(new[] { Plugin }, Scenarios).ToString());

    /// <summary>舊外掛的 static 計數於兩個案例各自從零開始，證明卸載域後不保留跨案例狀態。</summary>
    [Fact]
    public void Harness_StaticState_IsolatedBetweenCases()
    {
        const string source = """
            using System; using Microsoft.Xrm.Sdk;
            public sealed class Sample:IPlugin { private static int count;
            public void Execute(IServiceProvider p) {
            var c=(IPluginExecutionContext)p.GetService(typeof(IPluginExecutionContext));
            var f=(IOrganizationServiceFactory)p.GetService(typeof(IOrganizationServiceFactory));
            var e=(Entity)c.InputParameters["Target"];
            f.CreateOrganizationService(c.UserId).Update(new Entity(e.LogicalName,e.Id){["name"]=(++count).ToString()}); }}
            """;
        var scenarios = JObject.Parse(Scenarios);
        var second = scenarios["cases"]![0]!.DeepClone();
        second["name"] = "second";
        ((JArray)scenarios["cases"]!).Add(second);
        var result = GoldenHarness.RunSources(new[] { source }, scenarios.ToString());
        Assert.True((string?)result["status"] == "Generated", result.ToString());
        Assert.Equal("1", (string?)result["cases"]![0]!["writes"]![0]!["fields"]!["name"]);
        Assert.Equal("1", (string?)result["cases"]![1]!["writes"]![0]!["fields"]!["name"]);
    }

    /// <summary>使用05A情境的ref與具名映像格式，驗證lookup身分和前映像欄位實際送達舊入口。</summary>
    [Fact]
    public void Harness_ScenarioReferencesAndNamedImages_ReachPlugin()
    {
        const string source = """
            using System; using Microsoft.Xrm.Sdk;
            public sealed class Sample:IPlugin { public void Execute(IServiceProvider p) {
            var c=(IPluginExecutionContext)p.GetService(typeof(IPluginExecutionContext));
            var f=(IOrganizationServiceFactory)p.GetService(typeof(IOrganizationServiceFactory));
            var e=(Entity)c.InputParameters["Target"];
            var r=(EntityReference)e["ownerid"];
            f.CreateOrganizationService(c.UserId).Update(new Entity(e.LogicalName,e.Id){
            ["ownerid"]=r,["name"]=(string)c.PreEntityImages["before"]["name"]}); }}
            """;
        var scenarios = JObject.Parse(Scenarios);
        var trigger = scenarios["cases"]![0]!["trigger"]!;
        trigger["fields"] = new JObject
        {
            ["ownerid"] = new JObject { ["ref"] = "contact-1", ["entity"] = "contact", ["name"] = "same-name" }
        };
        trigger["images"] = new JObject
        {
            ["pre"] = new JObject { ["before"] = new JObject { ["name"] = "old-value" } }
        };
        var result = GoldenHarness.RunSources(new[] { source }, scenarios.ToString());
        Assert.True((string?)result["status"] == "Generated", result.ToString());
        Assert.True(((JArray)result["cases"]![0]!["writes"]!).Count == 1, result.ToString());
        var fields = result["cases"]![0]!["writes"]![0]!["fields"]!;
        Assert.Equal("contact-1", (string?)fields["ownerid"]!["alias"]);
        Assert.Equal("old-value", (string?)fields["name"]);
    }

    /// <summary>把實際不應建立的路徑寫入舊外掛，斷言File API被改寫且原始路徑未寫入。</summary>
    [Fact]
    public void Harness_FileWrite_IsRedirectedAndScratchRemoved()
    {
        var path = Path.Combine(Path.GetTempPath(), "smv-must-not-write-" + Guid.NewGuid().ToString("N"));
        var literal = Newtonsoft.Json.JsonConvert.SerializeObject(path);
        var source = "using System; using System.IO; using Microsoft.Xrm.Sdk; "
            + "public sealed class Sample:IPlugin { public void Execute(IServiceProvider p) { "
            + "File.WriteAllText(" + literal + ",\"fixture\"); var c=(IPluginExecutionContext)p.GetService(typeof(IPluginExecutionContext)); "
            + "var f=(IOrganizationServiceFactory)p.GetService(typeof(IOrganizationServiceFactory)); "
            + "var e=(Entity)c.InputParameters[\"Target\"]; "
            + "f.CreateOrganizationService(c.UserId).Update(new Entity(e.LogicalName,e.Id){[\"name\"]=File.ReadAllText(" + literal + ")}); }}";
        var result = GoldenHarness.RunSources(new[] { source }, Scenarios);
        Assert.True((string?)result["status"] == "Generated", result.ToString());
        Assert.False(File.Exists(path));
        Assert.Null(result["cases"]![0]!["exception"]!.Value<string>());
        Assert.Equal("fixture", (string?)result["cases"]![0]!["writes"]![0]!["fields"]!["name"]);
    }

    /// <summary>舊FileStream漏Dispose並要求超過一MiB時必須拒絕，案例owner仍需關閉handle並移除scratch。</summary>
    [Fact]
    public void Harness_FileStreamCapacityFailure_CleansUndisposedStream()
    {
        const string source = """
            using System; using System.IO; using Microsoft.Xrm.Sdk;
            public sealed class Sample:IPlugin { public void Execute(IServiceProvider p) {
            var stream=new FileStream("fixture",FileMode.Create); stream.SetLength(1048577); }}
            """;
        var result = GoldenHarness.RunSources(new[] { source }, Scenarios);
        Assert.True((string?)result["status"] == "Generated", result.ToString());
        Assert.Equal("InvalidDataException", (string?)result["cases"]![0]!["exception"]);
        Assert.Empty((JArray)result["cases"]![0]!["writes"]!);
        Assert.Equal(result.ToString(), GoldenHarness.RunSources(new[] { source }, Scenarios).ToString());
    }

    /// <summary>Directory.Exists應反映映射目錄，Write/ReadAllLines保留逐列內容且不觸碰原始路徑。</summary>
    [Fact]
    public void Harness_DirectoryAndLines_PreserveMappedSemantics()
    {
        const string source = """
            using System; using System.IO; using Microsoft.Xrm.Sdk;
            public sealed class Sample:IPlugin { public void Execute(IServiceProvider p) {
            Directory.CreateDirectory("fixture-directory");
            if(!Directory.Exists("fixture-directory"))throw new InvalidOperationException();
            File.WriteAllLines("fixture-lines",new[]{"a","b"}); File.AppendAllLines("fixture-lines",new[]{"c"});
            var c=(IPluginExecutionContext)p.GetService(typeof(IPluginExecutionContext));
            var f=(IOrganizationServiceFactory)p.GetService(typeof(IOrganizationServiceFactory));
            var e=(Entity)c.InputParameters["Target"];
            f.CreateOrganizationService(c.UserId).Update(new Entity(e.LogicalName,e.Id){["name"]=string.Join(",",File.ReadAllLines("fixture-lines"))}); }}
            """;
        var result = GoldenHarness.RunSources(new[] { source }, Scenarios);
        Assert.True((string?)result["status"] == "Generated", result.ToString());
        Assert.Null(result["cases"]![0]!["exception"]!.Value<string>());
        Assert.Equal("a,b,c", (string?)result["cases"]![0]!["writes"]![0]!["fields"]!["name"]);
    }

    /// <summary>編譯錯誤只回固定代碼及檔名，沒有部分案例或來源原文。</summary>
    [Fact]
    public void Harness_CompileFailure_ReportedNotThrown()
    {
        var result = GoldenHarness.RunSources(Sources("public class Bad { invalid syntax }"), Scenarios);
        Assert.Equal("CompileFailed", (string?)result["status"]);
        Assert.NotEmpty((JArray)result["diagnostics"]!);
        Assert.Null(result["cases"]);
    }

    /// <summary>WebClient在組件載入前被拒絕，不需要觸發或嘗試連線。</summary>
    [Fact]
    public void Harness_UninterceptedNetworkApi_RefusesToRun()
    {
        var result = GoldenHarness.RunSources(Sources(
            "public class Bad { public void Go(){new System.Net.WebClient().DownloadString(\"https://example.invalid\");} }"
        ), Scenarios);
        Assert.Equal("BlockedByNetworkApi", (string?)result["status"]);
        Assert.Null(result["cases"]);
    }

    /// <summary>單純網址常值不是網路呼叫，HttpClient 建構則必須被語意改寫後才可編譯。</summary>
    [Fact]
    public void Compiler_UrlLiteralIsAllowedAndHttpClientIsRewritten()
    {
        using var urlOnly = CompileSource(
            "public static class Endpoint { public const string Value = \"https://api.line.me/v2/bot/message/push\"; }");
        Assert.Equal("Generated", urlOnly.Status);

        using var client = CompileSource(
            "using System.Net.Http; public sealed class Sender { public HttpClient Create() => new HttpClient(); }");
        Assert.Equal("Generated", client.Status);

        using var rest = CompileSource(
            "using RestSharp; public sealed class RestSender { public RestClient Create() => new RestClient(\"https://api.line.me\"); }");
        Assert.Equal("Generated", rest.Status);

        using var tlsConfiguration = CompileSource(
            "using System.Net; public static class Tls { public static void Configure() => ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }");
        Assert.Equal("Generated", tlsConfiguration.Status);
    }

    /// <summary>無法經已核准語法替身攔截的 WebRequest.Create 必須在執行前被拒絕。</summary>
    [Fact]
    public void Compiler_WebRequestCreateIsBlocked()
    {
        using var result = CompileSource(
            "using System.Net; public sealed class Sender { public object Create() => WebRequest.Create(\"https://example.invalid\"); }");
        Assert.Equal("BlockedByNetworkApi", result.Status);
    }

    /// <summary>其他列明不可改寫的傳輸建構也必須在 emit／載入舊組件前拒絕。</summary>
    [Theory]
    [InlineData("new System.Net.WebClient()")]
    [InlineData("new System.Net.Sockets.TcpClient()")]
    [InlineData("new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)")]
    public void Compiler_UninterceptedNetworkConstructorsAreBlocked(string expression)
    {
        using var result = CompileSource("public sealed class Sender { public object Create() => " + expression + "; }");
        Assert.Equal("BlockedByNetworkApi", result.Status);
    }

    /// <summary>純網路資料型別不是傳輸入口，不得因 namespace 命中而整批拒絕專案。</summary>
    [Fact]
    public void Compiler_NonTransportNetTypesRemainAllowed()
    {
        using var result = CompileSource(
            "using System.Net; public static class Address { public static IPAddress Loopback => IPAddress.Loopback; }");
        Assert.Equal("Generated", result.Status);
    }

    /// <summary>不可靜態證明的動態程式碼是編譯拒絕，不得污染網路阻擋分類。</summary>
    [Fact]
    public void Compiler_NonNetworkSafetyRefusalIsCompileFailed()
    {
        using var result = CompileSource("public sealed class DynamicValue { public dynamic Value; }");
        Assert.Equal("CompileFailed", result.Status);
    }

    /// <summary>不受信任舊來源不得藉公開工具型別直接發布檔案，亦不得排入超過案例的背景工作。</summary>
    [Theory]
    [InlineData("LegacyGolden.PluginHarness.GoldenWriter.Write(\"escape.json\",new Newtonsoft.Json.Linq.JObject());")]
    [InlineData("System.Threading.Tasks.Task.Run(()=>{});")]
    [InlineData("new System.Threading.Thread(()=>{}).Start();")]
    public void Compiler_HarnessEscapeOrBackgroundWork_IsRejected(string statement)
    {
        using var result = CompileSource("public sealed class Escape { public void Go() { " + statement + " } }");
        Assert.Equal("CompileFailed", result.Status);
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

    /// <summary>建立案例獨占來源陣列，不共享可變輸入。</summary>
    private static string[] Sources(string source) => new[] { source };

    /// <summary>以正式受限compiler編譯一段合成來源，回傳物件由測試以using釋放。</summary>
    private static CompilationResult CompileSource(string source) => LegacyProjectCompiler.CompileSources(Sources(source));
}
