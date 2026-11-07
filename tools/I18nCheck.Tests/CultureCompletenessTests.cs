/*
檔案整體說明

目的與定位：保護 P2-01 由全部原始 resx 產生語系完成度的正式 CLI 契約，避免回退文字、
空白翻譯或單一資源組的完成狀態讓主機誤開放未完成語系。所有輸入均為獨立合成方案，
不讀產品設定、秘密、CRM 或網路；中性鍵是穩定身分，不按顯示文字去重。

主要功能與流程：透過真實 CommandRunner 生成、編譯並呼叫產物，驗證跨組分母、
空白與缺譯、大小寫、未知及偽語系、重複鍵拒絕、固定排序、編碼與唯讀 stale 檢查。
改動翻譯後 check 必須失敗且原檔不變，再生成才可恢復；兩個方案並行不能串用候選。

使用邊界與維護：每案借用 VerificationFixture 的唯一暫存 owner，所有 task 必須 await。
編譯探針使用可回收 AssemblyLoadContext，串流確定釋放並在 finally Unload；
不修改程序文化、環境變數或長生命靜態集合。此為離線生成及執行證據，不代替實機。
*/
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace I18nCheck.Tests;

/// <summary>從原始資源到可執行完成度產物的離線契約，測試 owner 統一清理全部檔案。</summary>
public sealed class CultureCompletenessTests
{
    /// <summary>缺少第二資源組或只把空白算已譯，都必須使英文保持未完成；相同顯示文字不合併。</summary>
    [Theory]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("translated", 2)]
    public void Generate_UsesAllOriginalKeysAndNonblankTranslations(string secondTranslation, int expected)
    {
        using var files = new VerificationFixture();
        files.Write("First", "", ("A.First", "same"));
        files.Write("First", "en", ("A.First", "translated"));
        files.Write("Second", "", ("A.Second", "same"));
        if (secondTranslation.Length != 0) files.Write("Second", "en", ("A.Second", secondTranslation));
        Assert.Equal(0, Run(files.Root));
        var bytes = File.ReadAllBytes(Path.Combine(files.Root, "CultureCompleteness.g.cs"));
        Probe(bytes, "zh-TW", 2, true);
        Probe(bytes, "EN", expected, expected == 2);
        Probe(bytes, "qps-ploc", 0, false);
        Probe(bytes, "unknown-private-value", 0, false);
        Probe(bytes, null, 0, false);
        var source = new UTF8Encoding(false, true).GetString(bytes);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        Assert.EndsWith("\r\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", source.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("檔案整體說明", source, StringComparison.Ordinal);
        Assert.DoesNotContain("translated", source, StringComparison.Ordinal);
    }

    /// <summary>空集合不能以零除零或零等於零宣稱完整；空白中性值也必須阻止該語系發布。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_EmptyOrBlankNeutral_IsNeverComplete(bool blank)
    {
        using var files = new VerificationFixture();
        if (blank) files.Write("Text", "", ("A.Text", " "));
        Assert.Equal(0, Run(files.Root));
        Probe(File.ReadAllBytes(Path.Combine(files.Root, "CultureCompleteness.g.cs")), "zh-TW", 0, false);
    }

    /// <summary>翻譯修改後唯讀 check 要拒絕舊產物並保留位元組；重生後再檢查才可通過。</summary>
    [Fact]
    public void Check_StaleInput_DoesNotWriteAndRegenerationRecovers()
    {
        using var files = new VerificationFixture();
        files.Write("Text", "", ("A.Text", "source"));
        Assert.Equal(0, Run(files.Root));
        var path = Path.Combine(files.Root, "CultureCompleteness.g.cs");
        var before = File.ReadAllBytes(path);
        Assert.Equal(0, Run(files.Root, check: true));
        files.Write("Text", "en", ("A.Text", "translation"));
        Assert.Equal(1, Run(files.Root, check: true));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(0, Run(files.Root));
        Assert.Equal(0, Run(files.Root, check: true));
        Assert.Empty(Directory.GetFiles(files.Root, "*.tmp", SearchOption.AllDirectories));
    }

    /// <summary>跨組同鍵是來源衝突，必須在發布前拒絕，不得覆寫既有完整輸出或洩漏資源本文。</summary>
    [Fact]
    public void Generate_DuplicateKey_PreservesPreviousOutput()
    {
        using var files = new VerificationFixture();
        files.Write("First", "", ("A.Text", "private-source-value"));
        Assert.Equal(0, Run(files.Root));
        var path = Path.Combine(files.Root, "CultureCompleteness.g.cs");
        var before = File.ReadAllBytes(path);
        files.Write("Second", "", ("A.Text", "private-source-value"));
        using var output = new StringWriter();
        Assert.Equal(2, CommandRunner.Run(files.Root,
            ["generate-completeness", "--out", "CultureCompleteness.g.cs"], output));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.DoesNotContain("private-source-value", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>正式 Localization 專案的 generate --check 必須包含完成度產物，刪除產物不能跳過守門。</summary>
    [Fact]
    public void RepositoryCheck_LocalizationProjectRequiresCompleteness()
    {
        using var files = new RepositoryFixture();
        files.AddProject("SpeechMessage.Localization", false);
        using var output = new StringWriter();
        Assert.Equal(1, CommandRunner.Run(files.Root, ["generate", "--check"], output));
        Assert.Equal(0, CommandRunner.Run(files.Root, ["generate-completeness"], output));
        Assert.Equal(0, CommandRunner.Run(files.Root, ["generate", "--check"], output));
    }

    /// <summary>兩個方案共用相同鍵但不同翻譯率，並行生成各自輸出並排空，不保留跨次快照。</summary>
    [Fact]
    public async Task Generate_ConcurrentRepositories_RemainIndependent()
    {
        using var first = new VerificationFixture();
        using var second = new VerificationFixture();
        first.Write("Text", "", ("A.Text", "source"));
        second.Write("Text", "", ("A.Text", "source"));
        second.Write("Text", "en", ("A.Text", "translation"));
        await Task.WhenAll(Task.Run(() => Assert.Equal(0, Run(first.Root))),
            Task.Run(() => Assert.Equal(0, Run(second.Root))));
        Probe(File.ReadAllBytes(Path.Combine(first.Root, "CultureCompleteness.g.cs")), "en", 0, false);
        Probe(File.ReadAllBytes(Path.Combine(second.Root, "CultureCompleteness.g.cs")), "en", 1, true);
    }

    /// <summary>呼叫正式 CLI 並確定關閉 writer，只允許本案根內的固定生成位置。</summary>
    private static int Run(string root, bool check = false)
    {
        using var output = new StringWriter();
        string[] args = ["generate-completeness", "--out", "CultureCompleteness.g.cs"];
        return CommandRunner.Run(root, check ? [.. args, "--check"] : args, output);
    }

    /// <summary>編譯並執行真正生成碼；所有反射物件只活於本次呼叫，離開時卸載測試組件。</summary>
    private static void Probe(byte[] bytes, string? culture, int translated, bool complete)
    {
        var compilation = CSharpCompilation.Create("CompletenessProbe",
            [CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(bytes))],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("completeness-probe", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(stream).GetType("SpeechMessage.Localization.CultureCompleteness", throwOnError: true)!;
            Assert.Equal(translated, type.GetMethod("GetTranslatedKeyCount")!.Invoke(null, [culture]));
            Assert.Equal(complete, type.GetMethod("IsComplete")!.Invoke(null, [culture]));
        }
        finally { context.Unload(); }
    }
}
