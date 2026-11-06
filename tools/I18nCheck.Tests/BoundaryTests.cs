/*
 * 檔案整體說明
 *
 * 目的與定位：以假原始碼、假資源與獨佔目錄保護掃描準確性、Unicode、檔案所有權與命令列棘輪。 測試失敗時指出真正的語法、資料或生命週期契約，不依賴網路、主機文化或真實組織。
 *
 * 主要功能與流程：
 * 本檔宣告 BoundaryTests、RepositoryFixture。
 * - 注入不同 C# 字串語法與範圍邊界；以明列數量保護 Roslyn 語法計數。
 * - 相似方法名稱不得造成假排除；真正診斷只排除模板或 Trace／Debug 引數。
 * - 僅計算可見節點與四種指定屬性；Razor、註解與腳本字串不得被假算。
 * - 補充平面字元必須按 Rune 計數，空白與標點不可被修剪，參數重複不增加字數。
 * 測試案例入口包含 Scan_SyntaxAndRanges_UsesLiteralValues、Scan_Diagnostics_ExcludesOnlyProvenTemplates、
 * Scan_Razor_OnlyCountsDisplayFragments、Pseudo_UnicodeAndWhitespace_CountsRunes、
 * Pseudo_UnicodeOrNumericPlaceholder_IsNotCounted、Ratchet_NewProjectAndOffsettingDecrease_StillFails、
 * Generate_EnglishShellAndEscaping_PreservesValues、Resx_RepeatedAndReorderedParameters_ValidatesAsSet；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Text;
using System.Text.Json;
using System.Xml;
using Xunit;

namespace I18nCheck.Tests;

/// <summary>
/// 以假原始碼、假資源與獨佔目錄保護掃描準確性、Unicode、檔案所有權與命令列棘輪。
/// 測試失敗時指出真正的語法、資料或生命週期契約，不依賴網路、主機文化或真實組織。
/// </summary>
public sealed class BoundaryTests
{
    /// <summary>注入不同 C# 字串語法與範圍邊界；以明列數量保護 Roslyn 語法計數。</summary>
    /// <param name="source">完整假來源。</param>
    /// <param name="count">預期常值數量。</param>
    [Theory(DisplayName = "注入不同 C# 字串語法與範圍邊界")]
    [InlineData("class C { string X = @\"中文\"; }", 1)]
    [InlineData("class C { string X = \"\\u4e2d文\"; }", 1)]
    [InlineData("class C { string X = \"\"\"中文\"\"\"; }", 1)]
    [InlineData("class C { string X = $\"中文{1}中文\"; }", 1)]
    [InlineData("class C { string X = $\"English{\"中文\"}\"; }", 1)]
    [InlineData("class C { string X = \"㐀\" + \"鿿\" + \"豈\" + \"﫿\"; }", 4)]
    [InlineData("class C { char X = '中'; string Y = \"English\"; }", 0)]
    [InlineData("class C { [I18nIgnore(\"\")] string X = \"中文\"; }", 1)]
    [InlineData("class C { [I18nIgnore(\"協定\")] string X = \"中文\"; }", 0)]
    public void Scan_SyntaxAndRanges_UsesLiteralValues(string source, int count) => Assert.Equal(count, SourceScanner.CountCSharp(source));

    /// <summary>相似方法名稱不得造成假排除；真正診斷只排除模板或 Trace／Debug 引數。</summary>
    /// <param name="source">含同名干擾或真實診斷型別的來源。</param>
    /// <param name="count">仍應納入的文字數。</param>
    [Theory(DisplayName = "相似方法名稱不得造成假排除")]
    [InlineData("class C { void M(dynamic logger) { logger.LogInformation(\"顯示\"); } }", 1)]
    [InlineData("using System.Diagnostics; class C { void M() { Trace.WriteLine(\"診斷\"); Debug.WriteLine(\"診斷\"); } }", 0)]
    [InlineData("class Trace { public static void WriteLine(string x) {} } class C { void M() { Trace.WriteLine(\"顯示\"); } }", 1)]
    [InlineData("using Microsoft.Extensions.Logging; class C { void M(ILogger log) { log.LogWarning(\"診斷 {Value}\", \"顯示值\"); } }", 1)]
    [InlineData("using Microsoft.Extensions.Logging; class C { void M(ILogger log) { log.LogError(message: \"診斷\"); } }", 0)]
    public void Scan_Diagnostics_ExcludesOnlyProvenTemplates(string source, int count) => Assert.Equal(count, SourceScanner.CountCSharp(source));

    /// <summary>僅計算可見節點與四種指定屬性；Razor、註解與腳本字串不得被假算。</summary>
    /// <param name="source">最小 Razor 片段。</param>
    /// <param name="count">CJK 顯示片段數。</param>
    [Theory(DisplayName = "僅計算可見節點與四種指定屬性")]
    [InlineData("<p>中文</p><p>中文</p>", 2)]
    [InlineData("<input title='標題' placeholder=\"提示\" alt='替代' value='值' data-note='忽略' />", 4)]
    [InlineData("<!-- 中文 -- > -->@* 中文 *@<script>const x='中文';</script><style>中文</style>", 0)]
    [InlineData("@{ var x = \"中文\"; }<p>顯示 @Model.Name</p>", 1)]
    [InlineData("@if (Model.Ready) { <p title='提示'>中文</p> }", 2)]
    [InlineData("<input value='@Model.Name' /><p>@(\"程式字串\")</p>", 0)]
    public void Scan_Razor_OnlyCountsDisplayFragments(string source, int count) => Assert.Equal(count, SourceScanner.CountRazor(source));

    /// <summary>補充平面字元必須按 Rune 計數，空白與標點不可被修剪，參數重複不增加字數。</summary>
    /// <param name="text">Unicode 與參數邊界輸入。</param>
    /// <param name="expected">獨立列出的完整答案。</param>
    [Theory(DisplayName = "補充平面字元必須按 Rune 計數")]
    [InlineData("😀😀😀", "［😀😀😀ǂǂ］")]
    [InlineData("   ", "［   ǂǂ］")]
    [InlineData("", "［ǂ］")]
    [InlineData("{name}{name}", "［{name}{name}ǂ］")]
    [InlineData("{a}一{b}二", "［{a}一{b}二ǂ］")]
    public void Pseudo_UnicodeAndWhitespace_CountsRunes(string text, string expected) => Assert.Equal(expected, PseudoLocale.Transform(text));

    /// <summary>參數辨識不得任意限縮為 ASCII 名稱；Unicode 及數字名稱都由同一解析器排除。</summary>
    /// <param name="text">不含非參數文字的輸入。</param>
    [Theory(DisplayName = "參數辨識不得任意限縮為 ASCII 名稱")]
    [InlineData("{這是參數名稱}")]
    [InlineData("{1234567890}")]
    public void Pseudo_UnicodeOrNumericPlaceholder_IsNotCounted(string text) => Assert.Equal("［" + text + "ǂ］", PseudoLocale.Transform(text));

    /// <summary>新專案以零為基準，各專案分別比較，禁止另一專案改善抵銷新增。</summary>
    [Fact(DisplayName = "新專案以零為基準")]
    public void Ratchet_NewProjectAndOffsettingDecrease_StillFails()
    {
        Assert.False(CjkRatchet.Compare(new Dictionary<string, int> { ["New"] = 1 }, new Dictionary<string, int>()).Passed);
        Assert.False(CjkRatchet.Compare(new Dictionary<string, int> { ["A"] = 0, ["B"] = 2 }, new Dictionary<string, int> { ["A"] = 2, ["B"] = 1 }).Passed);
    }

    /// <summary>英文空殼允許缺字；產出必須保留 null，偽語系依中性來源生成。</summary>
    [Fact(DisplayName = "英文空殼允許缺字")]
    public void Generate_EnglishShellAndEscaping_PreservesValues()
    {
        using var files = new ResourceFixture();
        files.Write("", ("A.Text", "中文\"\\\r\n{name}"), ("B.Text", "第二筆"));
        files.Write("en", ("A.Text", "English\"\\\r\n{name}"));
        var text = Encoding.UTF8.GetString(ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample.Resources", "SampleData"));
        Assert.Contains("null", text, StringComparison.Ordinal);
        Assert.Contains("\\\"", text, StringComparison.Ordinal);
        Assert.Contains("\\\\", text, StringComparison.Ordinal);
        Assert.Contains("\\r\\n", text, StringComparison.Ordinal);
        Assert.Contains("internal static class SampleData", text, StringComparison.Ordinal);
    }

    /// <summary>參數集合忽略次數及出現順序；同一解析器也必須排除偽語系的參數長度。</summary>
    [Fact(DisplayName = "參數集合忽略次數及出現順序")]
    public void Resx_RepeatedAndReorderedParameters_ValidatesAsSet()
    {
        using var files = new ResourceFixture();
        files.Write("", ("A.Text", "{first}{second}{first}"));
        files.Write("en", ("A.Text", "{second}{first}"));
        Assert.NotEmpty(ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample", "Data"));
        Assert.Equal("［{first}{second}{first}ǂ］", PseudoLocale.Transform("{first}{second}{first}"));
    }

    /// <summary>XML DTD 不得解析外部檔案，注入純假實體宣告即必須拒絕。</summary>
    [Fact(DisplayName = "XML DTD 不得解析外部檔案")]
    public void Resx_Dtd_IsRejectedWithoutResolution()
    {
        using var files = new ResourceFixture();
        File.WriteAllText(files.BasePath, "<!DOCTYPE root [<!ENTITY x 'fake'>]><root><data name='A.Text'><value>&x;</value></data></root>");
        Assert.Throws<XmlException>(() => ResourceCatalog.Load(files.BasePath));
    }

    /// <summary>翻譯資源的重複鍵與沒有中性來源的鍵也必須拒絕，不能由字典覆寫。</summary>
    [Fact(DisplayName = "翻譯資源的重複鍵與沒有中性來源的鍵也必須拒絕")]
    public void Resx_TranslationDuplicateOrUnknownKey_IsRejected()
    {
        using var files = new ResourceFixture();
        files.Write("", ("A.Text", "中文"));
        files.Write("en", ("A.Text", "One"), ("A.Text", "Two"));
        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
        files.Write("en", ("B.Other", "Other"));
        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
    }

    /// <summary>實際 CLI 建立基準後通過；加入一筆文字即失敗，且輸出不含原始字串。</summary>
    [Fact(DisplayName = "實際 CLI 建立基準後通過")]
    public void Cli_ScanBaselineAndVerify_ProtectsProjectRatchet()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        repo.Write("App/Code.cs", "class C { string X = \"假機密中文\"; }");
        using var output = new StringWriter();
        Assert.Equal(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "tools/I18nCheck/baselines/cjk-literals.json"], output));
        Assert.Equal(0, CommandRunner.Run(repo.Root, ["verify"], output));
        repo.Write("App/Code.cs", "class C { string X = \"假機密中文\", Y = \"新增\"; }");
        Assert.Equal(1, CommandRunner.Run(repo.Root, ["scan", "--baseline", "tools/I18nCheck/baselines/cjk-literals.json"], output));
        Assert.DoesNotContain("假機密中文", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("假機密中文", File.ReadAllText(Path.Combine(repo.Root, "tools/I18nCheck/baselines/cjk-literals.json")), StringComparison.Ordinal);
    }

    /// <summary>只讀方案非測試專案；父 worktree、未列專案、生成檔與輸出目錄均不能增加數量。</summary>
    [Fact(DisplayName = "只讀方案非測試專案")]
    public void Cli_SolutionBoundary_ExcludesTestsGeneratedAndParentFiles()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        repo.AddProject("App.Tests", true);
        repo.Write("App/Code.cs", "class C { string X = \"一\"; }");
        foreach (var path in new[] { "App.Tests/Code.cs", "Outside/Code.cs", "App/obj/Code.cs", "App/bin/Code.cs", "App/.worktrees/Code.cs", "App/.其他解決方案/Code.cs", "App/Generated.g.cs" })
            repo.Write(path, "class D { string X = \"不可掃到\"; }");
        using var output = new StringWriter();
        Assert.Equal(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo.Root, "baseline.json")));
        var projects = json.RootElement.GetProperty("projects");
        Assert.Single(projects.EnumerateArray());
        Assert.Equal(1, projects[0].GetProperty("count").GetInt32());
        var nested = Path.Combine(repo.Root, ".worktrees", "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "SpeechMessageProducts.sln"), "Microsoft Visual Studio Solution File, Format Version 12.00\r\n");
        Assert.Equal(nested, RepositoryScanner.FindRoot(nested));
        Assert.NotEqual(0, CommandRunner.Run(nested, ["scan", "--write-baseline", "baseline.json"], output));
    }

    /// <summary>Windows 副檔名大小寫不影響掃描，否則改成大寫即可繞過文字棘輪。</summary>
    [Fact(DisplayName = "Windows 副檔名大小寫不影響掃描")]
    public void Cli_UppercaseSourceExtensions_AreCounted()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        repo.Write("App/Upper.CS", "class C { string X = \"中文\"; }");
        repo.Write("App/View.CSHTML", "<p>中文</p>");
        using var output = new StringWriter();
        Assert.Equal(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo.Root, "baseline.json")));
        Assert.Equal(2, baseline.RootElement.GetProperty("projects")[0].GetProperty("count").GetInt32());
    }

    /// <summary>即使方案誤列排除目錄內的專案，也必須在讀取來源前拒絕，不能走訪子 worktree。</summary>
    [Fact(DisplayName = "即使方案誤列排除目錄內的專案")]
    public void Cli_ExcludedProjectDirectory_IsRejected()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        repo.Write(".worktrees/Hidden/Hidden.csproj", "<Project Sdk='Microsoft.NET.Sdk' />");
        File.AppendAllText(Path.Combine(repo.Root, "SpeechMessageProducts.sln"), "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Hidden\", \".worktrees/Hidden/Hidden.csproj\", \"{00000000-0000-0000-0000-000000000001}\"\r\nEndProject\r\n");
        using var output = new StringWriter();
        Assert.NotEqual(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
        Assert.False(File.Exists(Path.Combine(repo.Root, "baseline.json")));
    }

    /// <summary>產生、唯讀檢查、竄改後拒絕；檢查不能覆寫既有檔案，命令後可獨佔開檔。</summary>
    [Fact(DisplayName = "產生、唯讀檢查、竄改後拒絕")]
    public void Cli_GenerateCheck_IsReadOnlyAndClosesHandles()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        repo.Write("App/Resources/Text.resx", "<root><data name='A.Text'><value>中文</value></data></root>");
        var args = new[] { "generate", "--resx", "App/Resources/Text.resx", "--namespace", "App.Resources", "--class", "TextData", "--out", "App/Resources/Text.g.cs" };
        using var output = new StringWriter();
        Assert.Equal(0, CommandRunner.Run(repo.Root, args, output));
        Assert.Equal(0, CommandRunner.Run(repo.Root, ["generate", "--check"], output));
        var path = Path.Combine(repo.Root, "App/Resources/Text.g.cs");
        File.AppendAllText(path, "// stale\r\n");
        var original = File.ReadAllBytes(path);
        Assert.Equal(1, CommandRunner.Run(repo.Root, [.. args, "--check"], output));
        Assert.Equal(original, File.ReadAllBytes(path));
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(stream.Length > 0);
        Assert.Empty(Directory.GetFiles(repo.Root, "*.tmp", SearchOption.AllDirectories));
    }

    /// <summary>缺參數、未知命令、缺基準與非法輸出均必須非零結束，不能假稱驗證成功。</summary>
    /// <param name="command">以分隔符號列出的獨立負向命令列。</param>
    [Theory(DisplayName = "缺參數、未知命令、缺基準與非法輸出均必須非零結束")]
    [InlineData("invalid")]
    [InlineData("scan|--baseline|missing.json")]
    [InlineData("generate")]
    [InlineData("generate|--check|--unknown")]
    [InlineData("scan|--write-baseline|../outside.json")]
    public void Cli_InvalidArgumentsOrInputs_Fails(string command)
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        using var output = new StringWriter();
        Assert.NotEqual(0, CommandRunner.Run(repo.Root, command.Split('|'), output));
    }

    /// <summary>基準重複專案身分必須拒絕，不能由 JSON 字典覆寫繞過發布契約。</summary>
    [Fact(DisplayName = "基準重複專案身分必須拒絕")]
    public void Cli_DuplicateBaselineIdentity_FailsClosed()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        repo.Write("baseline.json", "{\"version\":1,\"projects\":[{\"project\":\"App/App.csproj\",\"count\":0,\"files\":[]},{\"project\":\"App/App.csproj\",\"count\":0,\"files\":[]}]}");
        using var output = new StringWriter();
        Assert.NotEqual(0, CommandRunner.Run(repo.Root, ["scan", "--baseline", "baseline.json"], output));
    }

    /// <summary>兩個並行掃描不得共享來源、計數或輸出，結束後所有目錄可被立即清理。</summary>
    [Fact(DisplayName = "兩個並行掃描不得共享來源、計數或輸出")]
    public async Task Cli_ParallelRepositories_DoNotShareState()
    {
        using var first = new RepositoryFixture();
        using var second = new RepositoryFixture();
        first.AddProject("First", false);
        second.AddProject("Second", false);
        first.Write("First/Code.cs", "class C { string X = \"一\"; }");
        second.Write("Second/Code.cs", "class C { string X = \"一\", Y = \"二\"; }");
        await Task.WhenAll(new[] { first, second }.Select(repo => Task.Run(() =>
        {
            using var output = new StringWriter();
            Assert.Equal(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
        })));
        using var a = JsonDocument.Parse(File.ReadAllText(Path.Combine(first.Root, "baseline.json")));
        using var b = JsonDocument.Parse(File.ReadAllText(Path.Combine(second.Root, "baseline.json")));
        Assert.Equal(1, a.RootElement.GetProperty("projects")[0].GetProperty("count").GetInt32());
        Assert.Equal(2, b.RootElement.GetProperty("projects")[0].GetProperty("count").GetInt32());
    }
}

/// <summary>建立完全離線的假方案，根目錄由單一案例擁有；不讀取父 repo、帳密或產品設定。</summary>
internal sealed class RepositoryFixture : IDisposable
{
    /// <summary>本案例專屬根，最長生命週期到 Dispose，沒有任何共享靜態來源清單。</summary>
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "i18n-repo-" + Guid.NewGuid().ToString("N"));

    /// <summary>建立有效標頭，專案由 AddProject 明確列入，避免工具走訪未列目錄。</summary>
    internal RepositoryFixture() => Write("SpeechMessageProducts.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\r\n");

    /// <summary>將假 SDK 專案加入方案；測試標記用於驗證正式掃描範圍。</summary>
    /// <param name="name">本案例的簡單專案名稱。</param>
    /// <param name="isTest">是否為應排除的測試專案。</param>
    internal void AddProject(string name, bool isTest)
    {
        Write(name + "/" + name + ".csproj", "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>" + (isTest ? "true" : "false") + "</IsTestProject></PropertyGroup></Project>");
        File.AppendAllText(Path.Combine(Root, "SpeechMessageProducts.sln"), "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"" + name + "\", \"" + name + "/" + name + ".csproj\", \"{" + Guid.NewGuid().ToString("D") + "}\"\r\nEndProject\r\n");
    }

    /// <summary>只在自己建立的目錄寫入假資料，每次寫入立即關閉檔案把手。</summary>
    /// <param name="relative">受測案例固定提供的相對路徑。</param>
    /// <param name="text">不含真實個資的假內容。</param>
    internal void Write(string relative, string text)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>刪除本案例自建的根及所有假資料；沒有外部路徑輸入或保留背景工作。</summary>
    public void Dispose() => Directory.Delete(Root, true);
}
