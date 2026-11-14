/*
檔案整體說明

目的與定位：驗證 P5-02 舊來源經正式語法改寫後，RestSharp 與 FileStream 只使用離線記錄及有界 scratch；不是實際 LINE 發送、舊入口黃金或任意敵意程式碼沙箱的證明。
主要功能與流程：合成 Plugin 使用三種 RestClient 建構式與兩種請求建構式，斷言方法、端點、訊息及收件 alias，並確認 header 與 query 不進輸出。檔案案例執行 seek、position、同步及非同步寫入，注入容量越界後驗證位置和長度未改；錯誤案例必須先有 Generated 結果，不能以編譯失敗取代執行時斷言。
使用邊界與維護：輸入只有合成路徑、固定假收件者及無效主機，改寫後不建真實網路 handler。每次 runner 獨佔 AppDomain、capture、stream 與 scratch，finally 反向關閉遺漏 stream、卸載並刪除暫存；測試不保留 SDK、憑證、連線或背景工作。斷言依結果 JSON 決定，沒有修改固定黃金檔或正式組織。
*/
using System;
using LegacyGolden.PluginHarness;
using Newtonsoft.Json.Linq;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Harness;

/// <summary>以編譯、改寫及真實隔離執行保護傳輸記錄與有界檔案副作用。</summary>
public sealed class ScenarioTransportSafetyTests
{
    private const string Scenarios = """
        {"cases":[{"name":"transport","seed":[{"alias":"contact-1","entity":"contact","fields":{}}],
        "trigger":{"type":"Sample","message":"Update","entity":"contact","target":"contact-1","fields":{},"depth":1}}]}
        """;

    /// <summary>三種建構都只能進入 capture；請求 header 及 query 不保存，回應為明確合成成功值。</summary>
    [Theory]
    [InlineData("new RestClient()", true)]
    [InlineData("new RestClient(\"https://example.invalid\")", false)]
    [InlineData("new RestClient(new Uri(\"https://example.invalid\"))", false)]
    public void Harness_RestTransport_RecordsBodyAndOmitsHeadersAndQuery(string constructor, bool assignUrl)
    {
        var body = "var client=" + constructor + ";"
            + (assignUrl ? "client.BaseUrl=new Uri(\"https://example.invalid\");" : "")
            + """
              var request=new RestRequest("/v2/bot/message/push?private-query=hidden",Method.POST);
              request.AddHeader("private-header","hidden-header-value");
              request.AddParameter("application/json","{\"to\":\"U00000000000000000000000000000001\",\"messages\":[{\"type\":\"text\",\"text\":\"synthetic\"}]}",ParameterType.RequestBody);
              var response=client.Execute(request);
              if(response.Content!="{}"||response.StatusCode!=System.Net.HttpStatusCode.OK)throw new InvalidOperationException();
              var get=new RestRequest(Method.GET);client.Get(get);
              """;
        var row = Run(body);
        var calls = (JArray)row["lineCalls"]!;
        Assert.Equal(2, calls.Count);
        Assert.Equal("POST", (string?)calls[0]!["method"]);
        Assert.Equal("/v2/bot/message/push", (string?)calls[0]!["endpoint"]);
        Assert.Equal("contact-1", (string?)calls[0]!["recipients"]![0]);
        Assert.Equal("synthetic", (string?)calls[0]!["content"]![0]!["text"]);
        Assert.Equal("GET", (string?)calls[1]!["method"]);
        Assert.Empty((JArray)calls[1]!["recipients"]!);
        Assert.DoesNotContain("private-query", row.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-header-value", row.ToString(), StringComparison.Ordinal);
    }

    /// <summary>非本文參數與缺端點必須明確拒絕，不能記錄或假稱成功。</summary>
    [Theory]
    [InlineData("var client=new RestClient();client.Execute(new RestRequest(Method.GET));", "InvalidOperationException")]
    [InlineData("new RestRequest(Method.POST).AddParameter(\"x\",\"synthetic\",ParameterType.HttpHeader);", "NotSupportedException")]
    public void Harness_RestInvalidRequest_ProducesNoTransportRecord(string body, string exception)
    {
        var row = Run(body, exception);
        Assert.Empty((JArray)row["lineCalls"]!);
    }

    /// <summary>放行 Rest 相容型別不授權原始來源直接使用記錄或 scratch API；兩者仍須由 rewriter 插入。</summary>
    [Theory]
    [InlineData("new LegacyGolden.PluginHarness.RecordingRestClient();")]
    [InlineData("LegacyGolden.PluginHarness.ScratchFiles.WriteAllText(\"escape\",\"synthetic\");")]
    public void Harness_RestCompatibility_DoesNotAuthorizeDirectHarnessApis(string body)
    {
        var result = GoldenHarness.RunSources(new[] { "public class Sample { public void Go() {" + body + "}}" }, Scenarios);
        Assert.Equal("CompileFailed", (string?)result["status"]);
        Assert.Null(result["cases"]);
        Assert.Contains("HarnessApiRefused", result.ToString(), StringComparison.Ordinal);
    }

    /// <summary>seek、position、長度和寫入共用一 MiB 界線；失敗不部分寫入，正常非同步寫入已完成且可讀回。</summary>
    [Fact]
    public void Harness_ScratchStream_BoundedWritesPreservePositionAfterFailure()
    {
        var row = Run("""
            using(var stream=new FileStream("synthetic",FileMode.Create,FileAccess.ReadWrite,FileShare.None)){
            stream.Write(new byte[]{1,2},0,2);stream.WriteByte(3);
            stream.WriteAsync(new byte[]{4},0,1,System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            if(stream.Position!=4||stream.Length!=4)throw new InvalidOperationException();
            stream.Seek(-1,SeekOrigin.End);if(stream.ReadByte()!=4)throw new InvalidOperationException();
            stream.Position=1048576;var rejected=false;
            try{stream.WriteByte(5);}catch(InvalidDataException){rejected=true;}
            if(!rejected||stream.Position!=1048576||stream.Length!=4)throw new InvalidOperationException();
            rejected=false;try{stream.Seek(1,SeekOrigin.Current);}catch(InvalidDataException){rejected=true;}
            if(!rejected||stream.Position!=1048576)throw new InvalidOperationException();
            rejected=false;try{stream.Position=-1;}catch(InvalidDataException){rejected=true;}
            if(!rejected||stream.Position!=1048576)throw new InvalidOperationException();
            stream.SetLength(4);stream.Seek(0,SeekOrigin.Begin);
            if(stream.ReadByte()!=1)throw new InvalidOperationException();}
            if(!File.Exists("synthetic"))throw new InvalidOperationException();
            File.Delete("synthetic");if(File.Exists("synthetic"))throw new InvalidOperationException();
            s.Update(new Entity("contact",target.Id){["name"]="bounded-stream-verified"});
            """);
        Assert.Equal("bounded-stream-verified", (string?)Assert.Single((JArray)row["writes"]!)["fields"]!["name"]);
    }

    /// <summary>有界文字追加和位元組讀取使用同案映射，刪除後不可再讀到；編碼 overload 不可寫回原路徑。</summary>
    [Fact]
    public void Harness_ScratchConvenienceApis_RoundTripOnlyInsideCase()
    {
        var row = Run("""
            File.WriteAllBytes("bytes",new byte[]{1,2});
            if(File.ReadAllBytes("bytes")[1]!=2)throw new InvalidOperationException();
            File.WriteAllText("text","a",System.Text.Encoding.UTF8);
            File.AppendAllText("text","b");File.AppendAllText("text","c",System.Text.Encoding.UTF8);
            if(File.ReadAllText("text",System.Text.Encoding.UTF8)!="abc")throw new InvalidOperationException();
            File.WriteAllLines("lines",new[]{"a"},System.Text.Encoding.UTF8);
            File.AppendAllLines("lines",new[]{"b"},System.Text.Encoding.UTF8);
            if(File.ReadAllLines("lines",System.Text.Encoding.UTF8).Length!=2)throw new InvalidOperationException();
            s.Update(new Entity("contact",target.Id){["name"]="scratch-roundtrip"});
            """);
        Assert.Equal("scratch-roundtrip", (string?)Assert.Single((JArray)row["writes"]!)["fields"]!["name"]);
    }

    /// <summary>所有來源均經正式安全分析，基礎設施失敗先拒絕測試；結果僅保留 detached JSON。</summary>
    private static JToken Run(string body, string? exception = null)
    {
        var source = """
            using System;using System.IO;using RestSharp;using Microsoft.Xrm.Sdk;
            public sealed class Sample:IPlugin{public void Execute(IServiceProvider p){
            var context=(IPluginExecutionContext)p.GetService(typeof(IPluginExecutionContext));
            var factory=(IOrganizationServiceFactory)p.GetService(typeof(IOrganizationServiceFactory));
            var s=factory.CreateOrganizationService(context.UserId);var target=(Entity)context.InputParameters["Target"];
            """ + body + "}}";
        var result = GoldenHarness.RunSources(new[] { source }, Scenarios);
        Assert.True((string?)result["status"] == "Generated", result.ToString());
        var row = result["cases"]![0]!;
        Assert.Equal(exception, row["exception"]!.Value<string>());
        return row;
    }
}
