/*
檔案整體說明
目的與定位：測試真正 Roslyn 編譯與原 matcher 執行、觀察一致性、靜態回覆完整性及失敗拒絕，防止以新 matcher 或手寫表偽裝來源實測。
主要功能與流程：使用完全合成的舊程式形狀，刻意改變 helper 的比較語意，斷言同一字面輸入的意圖確實改變；測試語法與編譯故障、未知表結構、白名單缺漏、穩定序列化、獨立 context 與回覆來源行。
使用邊界與維護：所有編譯與 JSON 候選由單一案例擁有；context 以 using 釋放後用弱參照與有界 GC 驗證卸載。並行案例各自編譯不同來源，不共享使用者狀態；沒有真實服務或憑證。失敗斷言須對準明確原因，不能把工具環境失敗當成功紅燈。
*/
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using LegacyGolden;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>以合成來源對實際產生器施加語意、生命週期與追溯契約測試。</summary>
public sealed class LegacyGoldenGeneratorTests
{
    /// <summary>同一詞表改 helper 實作後意圖必須改變；若工具只重寫 StartsWith matcher，此斷言會精確失敗。</summary>
    [Fact]
    public void Generator_ExecutesActualMatcherBody_AndObservesOriginalComparers()
    {
        var sources = LegacyGoldenFixture.Sources();
        var original = GoldenGenerator.Generate(sources);
        Assert.Equal("Enrollment", Case(original, "Group", "literal")["actualIntent"]!.GetValue<string>());
        Assert.Equal("Unknown", Case(original, "Group", "lower-invariant")["actualIntent"]!.GetValue<string>());
        Assert.Equal("Unknown", Case(original, "Seed", "lower-invariant")["actualIntent"]!.GetValue<string>());
        Assert.Equal("Registration", Case(original, "pre", "upper-invariant")["actualIntent"]!.GetValue<string>());
        Assert.Equal("Unknown", Case(original, "pre", "full-width")["actualIntent"]!.GetValue<string>());
        Change(sources, LegacySourceReader.MatcherPath, "description.StartsWith(\"Group\")", "description.Contains(\"Group\", StringComparison.OrdinalIgnoreCase)");
        var changed = GoldenGenerator.Generate(sources);
        Assert.Equal("Enrollment", Case(changed, "Group", "lower-invariant")["actualIntent"]!.GetValue<string>());
        Assert.All(changed["cases"]!.AsArray(), item => Assert.True(item!["observationAgreesWithOriginal"]!.GetValue<bool>()));
    }

    /// <summary>固定來源重跑必須逐 byte 一致；實算規則與統計相同，沒有硬編一百零三或執行外部服務。</summary>
    [Fact]
    public void Generator_RepeatsDeterministically_AndRetainsEveryRule()
    {
        var first = GoldenGenerator.Generate(LegacyGoldenFixture.Sources());
        var second = GoldenGenerator.Generate(LegacyGoldenFixture.Sources());
        Assert.Equal(GoldenGenerator.Serialize(first), GoldenGenerator.Serialize(second));
        Assert.Equal(first["rules"]!.AsArray().Count, first["statistics"]!["decisionItems"]!.GetValue<int>());
        Assert.Equal(4, first["statistics"]!["distinctLiterals"]!.GetValue<int>());
        Assert.Equal(31, first["cases"]!.AsArray().Count);
        Assert.Empty(first["stubs"]!.AsArray());
        Assert.False(first["externalServicesExecuted"]!.GetValue<bool>());
    }

    /// <summary>固定回覆保留完整換行及占位，委派保存真正建構型別、方法與參數，四類媒體空方法不得冒稱 CRM 已接通。</summary>
    [Fact]
    public void Replies_KeepFullStaticText_Delegates_AndCommentOnlyNoOps()
    {
        var golden = GoldenGenerator.Generate(LegacyGoldenFixture.Sources());
        var intents = golden["replies"]!["intents"]!;
        Assert.Equal("synthetic-help\r\nend", intents["Help"]!["fixedReplies"]![0]!["textTemplate"]!.GetValue<string>());
        Assert.Equal("synthetic-id: {lineId}", intents["Registration"]!["fixedReplies"]![0]!["textTemplate"]!.GetValue<string>());
        var delegation = intents["Enrollment"]!["delegations"]![0]!;
        Assert.Equal("SyntheticData", delegation["type"]!.GetValue<string>());
        Assert.Equal("Display", delegation["method"]!.GetValue<string>());
        Assert.Equal("{description}", delegation["arguments"]![0]!.GetValue<string>());
        Assert.True(intents["Enrollment"]!["constructions"]![0]!["usingOwned"]!.GetValue<bool>());
        Assert.Equal("no-op", intents["Unknown"]!["kind"]!.GetValue<string>());
        Assert.All(golden["replies"]!["webhookMessages"]!.AsObject(), item =>
            Assert.Equal("no-op-comment-only", item.Value!["kind"]!.GetValue<string>()));
        Assert.All(golden["rules"]!.AsArray(), rule => Assert.True(rule!["source"]!["line"]!.GetValue<int>() > 0));
    }

    /// <summary>分開注入語法、缺型別與非常值錯誤，分別檢查精確分類，不用任意建置失敗代替拒絕契約。</summary>
    [Theory]
    [InlineData("syntax", "Legacy source has syntax errors.")]
    [InlineData("compilation", "Legacy compilation failed: CS0103")]
    [InlineData("predicate", "Unsupported helper predicate.")]
    public void Generator_RejectsBrokenSourceBeforePublication(string fault, string expected)
    {
        var sources = LegacyGoldenFixture.Sources();
        if (fault == "syntax") Change(sources, LegacySourceReader.MatcherPath, "return description", "return ??? description");
        else if (fault == "compilation") Change(sources, LegacySourceReader.MatcherPath, "description.StartsWith", "missing.StartsWith");
        else Change(sources, LegacySourceReader.MatcherPath, "\"Group\"", "string.Empty");
        Assert.Equal(expected, Assert.Throws<InvalidDataException>(() => GoldenGenerator.Generate(sources)).Message);
    }

    /// <summary>缺檔、重複來源與白名單外路徑都須先拒絕，不能退化成只測部分來源。</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("outside")]
    [InlineData("large")]
    public void Generator_RejectsIncompleteSourceSets(string fault)
    {
        var sources = LegacyGoldenFixture.Sources();
        if (fault == "missing") sources.RemoveAt(0);
        else if (fault == "duplicate") sources[1] = sources[0];
        else if (fault == "large") sources[0] = sources[0] with { Text = new string('x', 1024 * 1024 + 1) };
        else sources[0] = sources[0] with { Path = "appsettings.json" };
        Assert.Equal("Expected the exact source allowlist.", Assert.Throws<InvalidDataException>(() => GoldenGenerator.Generate(sources)).Message);
    }

    /// <summary>輸出不得含私人識別形狀；SHA256 中相同字母連續片段不是 ID，不可誤判導致真來源無法生成。</summary>
    [Fact]
    public void Generator_RejectsPrivateIdentifierInStaticReply()
    {
        var sources = LegacyGoldenFixture.Sources();
        Change(sources, "LineMessagePlugIn/Processors/Core/IncomingMessageProcessor.Reply.cs", "synthetic-help", "U11111111111111111111111111111111");
        Assert.Equal("Private identifier or replacement character in output.", Assert.Throws<InvalidDataException>(() => GoldenGenerator.Generate(sources)).Message);
    }

    /// <summary>兩個獨立來源同時生成後互不污染；修改回傳 JSON 也不得影響另一次產生的快照。</summary>
    [Fact]
    public async Task Generator_ParallelSourcesRemainIsolated_AndUnload()
    {
        var a = LegacyGoldenFixture.Sources();
        var b = LegacyGoldenFixture.Sources();
        Change(b, LegacySourceReader.RoutingPath, "\"Seed\"", "\"Other\"");
        var results = await Task.WhenAll(Task.Run(() => GoldenGenerator.Generate(a)), Task.Run(() => GoldenGenerator.Generate(b)));
        Assert.Contains(results[0]["cases"]!.AsArray(), row => row!["keyword"]?.GetValue<string>() == "Seed");
        Assert.DoesNotContain(results[1]["cases"]!.AsArray(), row => row!["keyword"]?.GetValue<string>() == "Seed");
        results[0]["statistics"]!["decisionItems"] = -1;
        Assert.Equal(4, results[1]["statistics"]!["decisionItems"]!.GetValue<int>());
        var lifetime = ExecuteAndDispose();
        for (var attempt = 0; attempt < 10 && lifetime.IsAlive; attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.False(lifetime.IsAlive, "Collectible legacy context must return to the unloaded baseline.");
    }

    /// <summary>獨立方法避免 JIT 區域變數延長 context 存活，並驗證 Dispose 之後拒絕執行。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExecuteAndDispose()
    {
        var compilation = LegacyCompiler.Compile(LegacyGoldenFixture.Sources());
        var harness = LegacyCompiler.OpenOriginal(compilation);
        Assert.Equal("Help", harness.Run("Seed")[0]);
        var reference = harness.Lifetime;
        harness.Dispose(); harness.Dispose();
        Assert.Throws<ObjectDisposedException>(() => harness.Run("Seed"));
        return reference;
    }

    /// <summary>連續建立及釋放八輪原版與觀察版 context，排空後十六個弱參照全失效，防止反射委派或 static 保留來源組件。</summary>
    [Fact]
    public void Harness_RepeatedOriginalAndTraceOwners_ReturnToUnloadedBaseline()
    {
        var references = ExerciseOwners();
        Assert.Equal(16, references.Count);
        for (var attempt = 0; attempt < 10 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>不內嵌的有界生命週期迴圈只回弱參照，消除測試自身 JIT 區域參照造成的假洩漏。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> ExerciseOwners()
    {
        var references = new List<WeakReference>();
        for (var index = 0; index < 8; index++)
        {
            var compilation = LegacyCompiler.Compile(LegacyGoldenFixture.Sources());
            using var original = LegacyCompiler.OpenOriginal(compilation);
            using var observed = LegacyCompiler.OpenTraced(compilation, RuleCatalog.Extract(compilation));
            Assert.Equal(original.Run("Group")[0], observed.Run("Group")[0]);
            Assert.Equal("Enrollment", original.Run("Group")[0]);
            references.Add(original.Lifetime); references.Add(observed.Lifetime);
        }
        return references;
    }

    /// <summary>逐詞與變形定位唯一案例，禁止以第一筆或空集合掩蓋缺漏。</summary>
    private static JsonNode Case(JsonObject golden, string keyword, string variant) => Assert.Single(golden["cases"]!.AsArray(),
        item => item!["keyword"]?.GetValue<string>() == keyword && item["variant"]!.GetValue<string>() == variant)!;

    /// <summary>故障只改指定合成來源；來源 hash 隨修改更新，不能讓舊 hash 冒充已改來源。</summary>
    internal static void Change(List<SourceDocument> sources, string path, string before, string after)
    {
        var index = sources.FindIndex(source => source.Path == path);
        var text = sources[index].Text.Replace(before, after, StringComparison.Ordinal);
        var encoding = System.Text.CodePagesEncodingProvider.Instance.GetEncoding(950, System.Text.EncoderFallback.ExceptionFallback, System.Text.DecoderFallback.ExceptionFallback)!;
        sources[index] = sources[index] with { Text = text, OriginalSha256 = LegacySourceReader.Hash(encoding.GetBytes(text)),
            ConvertedSha256 = LegacySourceReader.Hash(System.Text.Encoding.UTF8.GetBytes(text)), LineCount = LegacySourceReader.CountLines(text) };
    }
}
