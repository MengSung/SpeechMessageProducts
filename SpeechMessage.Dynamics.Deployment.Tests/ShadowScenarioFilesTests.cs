/*
 * 檔案整體說明
 * 目的與定位：驗證部署影子情境的固定路徑、有界嚴格解析與 detached 資料契約。
 * 主要功能與流程：以每案獨占暫存工作樹建立合成 scenarios 與 expected，注入重複、
 * 未知欄位、參照錯配、深度與容量故障，斷言完整拒絕而非發布部分資料；另讀真實
 * attendance 合成黃金檔驗證相容性，不載入 legacy、不建立 SDK 或 LINE 連線。
 * 使用邊界與維護：測試 owner 在 Dispose 遞迴刪除唯一暫存根；JSON 僅含合成別名，
 * 成功後刪除來源仍可讀 DTO，以證明文件與檔案 handle 已釋放。離線結果不是實機證據。
 */
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>針對檔案邊界及語意拒絕的離線測試；不以 runtime 補足 reader 的驗證。</summary>
public sealed class ShadowScenarioFilesTests
{
    /// <summary>成功解析保留高精度原文及所有必要資料，來源刪除後仍有效且不同載入互相隔離。</summary>
    [Fact]
    public void Shadow_Files_LoadDetachedAndPreservePrecision()
    {
        using var fixture = new Files();
        var cases = ShadowScenarioFiles.Load(fixture.Root, "attendance");
        var item = Assert.Single(cases);
        fixture.Dispose();
        Assert.Equal("sample", item.Name);
        Assert.True(item.LiveShadow);
        Assert.Equal("1234567890.1234567890123456789", item.Seed[0].GetProperty("fields").GetProperty("amount").GetRawText());
        Assert.Equal("Update", item.Trigger.GetProperty("message").GetString());
        Assert.Single(item.Observe.EnumerateArray());
        Assert.Equal("legacy-golden", item.Basis);
        Assert.Equal("Synthetic.Plugin", item.Entrypoint);
        Assert.Empty(item.ExpectedLineCalls.EnumerateArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, item.Exception.ValueKind);
        Assert.Single(item.ExpectedWrites.EnumerateArray());
        Assert.False(item.IsActivity);
    }

    /// <summary>既有 attendance 四案例原檔不改內容即可讀取；legacy 就算無效也不讀。</summary>
    [Fact]
    public void Shadow_Files_CurrentAttendanceCompatible()
    {
        var root = Path.GetDirectoryName(SourcePath())!;
        while (!Directory.Exists(Path.Combine(root, "SpeechMessageProducts.GoodShepherd.Core.Tests", "Golden")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Repository root absent.");
        var cases = ShadowScenarioFiles.Load(root, "attendance");
        Assert.Equal(4, cases.Count);
        Assert.All(cases, item => Assert.Equal("legacy-live-only", item.Basis));
        Assert.False(cases[^1].LiveShadow);
        Assert.Equal(3, cases[^1].Trigger.GetProperty("depth").GetInt32());
    }

    /// <summary>錯誤結構、欄位與別名均 fail closed；固定錯誤不可回顯輸入內容。</summary>
    /// <param name="fault">只影響本案例的故障代號。</param>
    [Theory]
    [InlineData("unknown-root")]
    [InlineData("unknown-case")]
    [InlineData("unknown-trigger")]
    [InlineData("unknown-observe")]
    [InlineData("unknown-seed")]
    [InlineData("unknown-ref")]
    [InlineData("missing-ref")]
    [InlineData("wrong-ref-entity")]
    [InlineData("duplicate-alias")]
    [InlineData("invalid-entity")]
    [InlineData("invalid-field")]
    [InlineData("missing-target")]
    [InlineData("target-entity")]
    [InlineData("unknown-message")]
    [InlineData("live-depth")]
    [InlineData("duplicate-case")]
    [InlineData("expected-mismatch")]
    [InlineData("expected-unknown")]
    [InlineData("unknown-write")]
    [InlineData("unknown-line")]
    [InlineData("zero-cases")]
    [InlineData("seven-cases")]
    [InlineData("seed-budget")]
    [InlineData("observe-budget")]
    [InlineData("fields-budget")]
    [InlineData("raw-line")]
    [InlineData("depth-type")]
    [InlineData("option-type")]
    [InlineData("unknown-expected-root")]
    [InlineData("duplicate-expected-case")]
    [InlineData("duplicate-observe")]
    [InlineData("duplicate-volatile")]
    [InlineData("bad-query-alias")]
    [InlineData("bad-difference-case")]
    [InlineData("unknown-output")]
    [InlineData("wrong-live-type")]
    public void Shadow_Files_InvalidContractsRejected(string fault)
    {
        using var f = new Files();
        var scenario = f.Scenarios["cases"]![0]!;
        var seed = scenario["seed"]!.AsArray();
        switch (fault)
        {
            case "unknown-root": f.Scenarios["extra"] = true; break;
            case "unknown-case": scenario["extra"] = true; break;
            case "unknown-trigger": scenario["trigger"]!["extra"] = true; break;
            case "unknown-observe": scenario["observe"]![0]!["query"] = new JsonObject(); break;
            case "unknown-seed": seed[0]!["extra"] = true; break;
            case "unknown-ref": seed[1]!["fields"]!["parent"]!["extra"] = true; break;
            case "missing-ref": seed[1]!["fields"]!["parent"]!["ref"] = "absent"; break;
            case "wrong-ref-entity": seed[1]!["fields"]!["parent"]!["entity"] = "contact"; break;
            case "duplicate-alias": seed.Add(seed[0]!.DeepClone()); break;
            case "invalid-entity": seed[0]!["entity"] = "Account/unsafe"; break;
            case "invalid-field": seed[0]!["fields"]!["bad.field"] = 1; break;
            case "missing-target": scenario["trigger"]!["target"] = "missing"; break;
            case "target-entity": scenario["trigger"]!["entity"] = "contact"; break;
            case "unknown-message": scenario["trigger"]!["message"] = "Execute"; break;
            case "live-depth": scenario["trigger"]!["depth"] = 2; break;
            case "duplicate-case": f.Scenarios["cases"]!.AsArray().Add(scenario.DeepClone()); break;
            case "expected-mismatch": f.Expected["cases"]![0]!["name"] = "other"; break;
            case "expected-unknown": f.Expected["cases"]![0]!["extra"] = true; break;
            case "unknown-write": f.Expected["cases"]![0]!["writes"]![0]!["extra"] = true; break;
            case "unknown-line": f.Expected["cases"]![0]!["lineCalls"]!.AsArray().Add(JsonNode.Parse("""{"extra":true}""")); break;
            case "zero-cases": f.Scenarios["cases"] = new JsonArray(); break;
            case "seven-cases": for (var i = 0; i < 6; i++) f.Scenarios["cases"]!.AsArray().Add(scenario.DeepClone()); break;
            case "seed-budget": for (var i = 0; i < 64; i++) { var row = seed[0]!.DeepClone(); row["alias"] = "row-" + i; seed.Add(row); } break;
            case "observe-budget": for (var i = 0; i < 64; i++) scenario["observe"]!.AsArray().Add(scenario["observe"]![0]!.DeepClone()); break;
            case "fields-budget": for (var i = 0; i < 129; i++) seed[0]!["fields"]!["field" + i] = i; break;
            case "depth-type": scenario["trigger"]!["depth"] = "one"; break;
            case "option-type": seed[0]!["fields"]!["state"] = JsonNode.Parse("""{"option":"bad"}"""); break;
            case "unknown-expected-root": f.Expected["extra"] = true; break;
            case "duplicate-expected-case": f.Expected["cases"]!.AsArray().Add(f.Expected["cases"]![0]!.DeepClone()); break;
            case "duplicate-observe": scenario["observe"]!.AsArray().Add(scenario["observe"]![0]!.DeepClone()); break;
            case "duplicate-volatile": scenario["volatile"] = new JsonArray("amount", "amount"); break;
            case "bad-query-alias": scenario["queryResults"] = JsonNode.Parse("""{"1":["missing"]}"""); break;
            case "bad-difference-case": f.Expected["declaredDifferences"]!.AsArray().Add(JsonNode.Parse("""{"case":"missing","position":"$","category":1,"reason":"Synthetic","test":"SyntheticTest"}""")); break;
            case "unknown-output": f.Expected["cases"]![0]!["outputs"]!["observed"] = JsonNode.Parse("""{"unknown":true}"""); break;
            case "wrong-live-type": scenario["liveShadow"] = "true"; break;
            case "raw-line": seed[1]!["fields"]!["new_lineid"] = "unsafe-raw-recipient"; break;
        }
        f.Save();
        var failure = Assert.Throws<InvalidDataException>(() => ShadowScenarioFiles.Load(f.Root, "attendance"));
        Assert.DoesNotContain("unsafe", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>兩份檔案各自執行大小、深度與重複鍵守門；巢狀動態資料也不能覆寫。</summary>
    /// <param name="file">要破壞的固定檔案。</param>
    /// <param name="fault">大小、巢狀深度或重複屬性。</param>
    [Theory]
    [InlineData("scenarios", "size")]
    [InlineData("expected", "size")]
    [InlineData("scenarios", "depth")]
    [InlineData("expected", "depth")]
    [InlineData("scenarios", "duplicate")]
    [InlineData("expected", "duplicate")]
    public void Shadow_Files_ByteAndJsonBudgets(string file, string fault)
    {
        using var f = new Files();
        var content = fault switch
        {
            "size" => new string(' ', 1_048_577),
            "depth" => new string('[', 33) + "0" + new string(']', 33),
            _ => """{"module":"attendance","module":"attendance"}"""
        };
        File.WriteAllText(Path.Combine(f.DirectoryPath, file + ".json"), content);
        Assert.Throws<InvalidDataException>(() => ShadowScenarioFiles.Load(f.Root, "attendance"));
    }

    /// <summary>六個唯一案例完整配對可發布，後續重新載入不會沿用先前文件或案例狀態。</summary>
    [Fact]
    public void Shadow_Files_SixCasesAndReloadAreIndependent()
    {
        using var f = new Files();
        for (var index = 1; index < 6; index++)
        {
            var scenario = f.Scenarios["cases"]![0]!.DeepClone();
            var expected = f.Expected["cases"]![0]!.DeepClone();
            scenario["name"] = "sample-" + index;
            expected["name"] = "sample-" + index;
            f.Scenarios["cases"]!.AsArray().Add(scenario);
            f.Expected["cases"]!.AsArray().Add(expected);
        }
        f.Save();
        var previous = ShadowScenarioFiles.Load(f.Root, "attendance");
        f.Scenarios["cases"]![0]!["seed"]![0]!["fields"]!["amount"] = 7;
        f.Save();
        var next = ShadowScenarioFiles.Load(f.Root, "attendance");
        Assert.Equal(6, previous.Count);
        Assert.Equal(6, next.Count);
        Assert.NotEqual(previous[0].Seed.GetRawText(), next[0].Seed.GetRawText());
        Assert.Equal("1234567890.1234567890123456789", previous[0].ExpectedWrites[0].GetProperty("fields").GetProperty("amount").GetRawText());
    }

    /// <summary>活動格式可保存但不宣稱支援執行；新 DTO 只帶原始參數與明確旗標。</summary>
    [Fact]
    public void Shadow_Files_ActivityPreservedWithoutExecution()
    {
        using var f = new Files();
        f.Scenarios["cases"]![0]!["trigger"] = JsonNode.Parse("""{"project":"Synthetic","type":"Synthetic.Plugin","activity":true,"inputs":{"InputParameter":"synthetic"},"depth":1}""");
        f.Save();
        var item = Assert.Single(ShadowScenarioFiles.Load(f.Root, "attendance"));
        Assert.True(item.IsActivity);
        Assert.Equal("synthetic", item.Trigger.GetProperty("inputs").GetProperty("InputParameter").GetString());
    }

    /// <summary>解碼後秘密及任意深層重複鍵拒絕；不把機密或遭覆蓋值帶入 DTO。</summary>
    /// <param name="fault">合成秘密樣式或巢狀重複鍵。</param>
    [Theory]
    [InlineData("secret")]
    [InlineData("nested-duplicate")]
    public void Shadow_Files_DecodedSecretAndNestedDuplicates(string fault)
    {
        using var f = new Files();
        var path = Path.Combine(f.DirectoryPath, "scenarios.json");
        var text = File.ReadAllText(path);
        text = fault == "secret"
            ? text.Replace("1234567890.1234567890123456789", System.Text.Json.JsonSerializer.Serialize("U" + new string('0', 32)), StringComparison.Ordinal)
            : text.Replace("1234567890.1234567890123456789", "1, \"amount\":2", StringComparison.Ordinal);
        File.WriteAllText(path, text);
        Assert.Throws<InvalidDataException>(() => ShadowScenarioFiles.Load(f.Root, "attendance"));
    }

    /// <summary>模組僅接受固定十代號，不允許路徑逃逸或大小寫替代。</summary>
    /// <param name="module">未授權的路徑或模組輸入。</param>
    [Theory]
    [InlineData("../attendance")]
    [InlineData("Attendance")]
    [InlineData("unknown")]
    public void Shadow_Files_ModuleWhitelist(string module)
    {
        using var f = new Files();
        Assert.Throws<InvalidDataException>(() => ShadowScenarioFiles.Load(f.Root, module));
    }

    /// <summary>從編譯器提供的來源路徑定位工作樹，避免測試 runner 的暫存工作目錄影響讀檔。</summary>
    /// <param name="path">由呼叫端編譯位置自動填入；不接受 CLI 輸入或秘密。</param>
    private static string SourcePath([CallerFilePath] string path = "") => path;

    /// <summary>本案 owner 只管理唯一的合成暫存根，不共用跨測試狀態；可重複 Dispose。</summary>
    private sealed class Files : IDisposable
    {
        /// <summary>本案例唯一暫存根；只有此 owner 可遞迴刪除。</summary>
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "shadow-files-" + Guid.NewGuid().ToString("N"));
        /// <summary>重現產品固定測試路徑，不允許呼叫端提供檔名。</summary>
        internal string DirectoryPath => Path.Combine(Root, "SpeechMessageProducts.GoodShepherd.Core.Tests", "Golden", "Legacy", "attendance");
        /// <summary>僅含合成別名與高精度數字的可變故障注入候選。</summary>
        internal JsonNode Scenarios { get; } = JsonNode.Parse("""
            {"module":"attendance","cases":[{"name":"sample","church":{"alias":"church-a","organizationName":"synthetic","timeZone":"Asia/Taipei","culture":"zh-TW"},"seed":[{"alias":"church-a","entity":"account","fields":{"amount":1234567890.1234567890123456789}},{"alias":"contact-1","entity":"contact","fields":{"parent":{"ref":"church-a","entity":"account","name":"Synthetic"},"new_lineid":{"line":"contact-1"}}}],"trigger":{"project":"Synthetic","type":"Synthetic.Plugin","message":"Update","entity":"account","target":"church-a","fields":{},"depth":1},"now":"2026-11-14T00:00:00Z","volatile":[],"liveShadow":true,"observe":[{"alias":"church-a","entity":"account","fields":["amount"]}]}]}
            """)!;
        /// <summary>與單一案例配對的合成期望，保留原始 writes JSON 精度。</summary>
        internal JsonNode Expected { get; } = JsonNode.Parse("""
            {"module":"attendance","basis":"legacy-golden","cases":[{"name":"sample","entrypoint":"Synthetic.Plugin","writes":[{"operation":"Update","entity":"account","alias":"church-a","fields":{"amount":1234567890.1234567890123456789}}],"lineCalls":[],"outputs":{},"exception":null}],"declaredDifferences":[]}
            """)!;
        /// <summary>建立兩份固定來源及不可解析的 legacy，以驗證 reader 完全不讀黃金輸出。</summary>
        internal Files() { Directory.CreateDirectory(DirectoryPath); Save(); File.WriteAllText(Path.Combine(DirectoryPath, "legacy.json"), "not-json"); }
        /// <summary>保存本案例候選，不會序列化或取得任何真正秘密。</summary>
        internal void Save() { File.WriteAllText(Path.Combine(DirectoryPath, "scenarios.json"), Scenarios.ToJsonString()); File.WriteAllText(Path.Combine(DirectoryPath, "expected.json"), Expected.ToJsonString()); }
        /// <summary>所有 reader handle 必須已釋放；測試結束後刪除本案例獨有根。</summary>
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
