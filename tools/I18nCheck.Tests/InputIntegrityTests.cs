/*
 * 檔案整體說明
 *
 * 目的與定位：以假檔案保護產出可編譯性、舊編碼唯讀盤點與 XML／方案輸入邊界，不接觸外部服務。
 *
 * 主要功能與流程：
 * 本檔宣告 InputIntegrityTests。
 * - 把包含引號、反斜線、多行及參數的真實產出交給 C# 編譯器，避免只比字串的假綠燈。
 * - 專案 XML 的 DTD 即使只有內部假實體也必須拒絕，避免隱含實體展開或外部解析。
 * - 方案的父路徑參照在開檔前失敗，不因父 worktree 剛好有同名專案而讀到舊來源。
 * - 重複檔案身分不能靠字典覆寫或總數相抵隱藏；讀取基準時必須直接拒絕。
 * 測試案例入口包含 Generate_EscapedResourceRows_CompileWithoutErrors、Cli_ProjectDtd_IsRejectedBeforeScan、
 * Cli_ExternalProjectPath_IsRejected、Cli_DuplicateBaselineFileIdentity_IsRejected、
 * Scan_Cp950Source_DecodesWithoutMutation、Resx_InvalidKeyBoundaries_AreRejected。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace I18nCheck.Tests;

/// <summary>以假檔案保護產出可編譯性、舊編碼唯讀盤點與 XML／方案輸入邊界，不接觸外部服務。</summary>
public sealed class InputIntegrityTests
{
    /// <summary>把包含引號、反斜線、多行及參數的真實產出交給 C# 編譯器，避免只比字串的假綠燈。</summary>
    [Fact(DisplayName = "產生的 C# 必須可被編譯器接受")]
    public void Generate_EscapedResourceRows_CompileWithoutErrors()
    {
        using var files = new ResourceFixture();
        files.Write("", ("A.Text", "中文\"\\\r\n{name}"));
        var bytes = ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample.Resources", "SampleData");
        var compilation = CSharpCompilation.Create("GeneratedProbe", [CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(bytes))],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>專案 XML 的 DTD 即使只有內部假實體也必須拒絕，避免隱含實體展開或外部解析。</summary>
    [Fact(DisplayName = "專案 XML 不允許 DTD")]
    public void Cli_ProjectDtd_IsRejectedBeforeScan()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        repo.Write("App/App.csproj", "<!DOCTYPE Project [<!ENTITY sdk 'Microsoft.NET.Sdk'>]><Project Sdk='&sdk;' />");
        using var output = new StringWriter();
        Assert.Equal(2, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
        Assert.False(File.Exists(Path.Combine(repo.Root, "baseline.json")));
    }

    /// <summary>方案的父路徑參照在開檔前失敗，不因父 worktree 剛好有同名專案而讀到舊來源。</summary>
    [Fact(DisplayName = "方案不能參照 repo 以外的專案")]
    public void Cli_ExternalProjectPath_IsRejected()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        File.AppendAllText(Path.Combine(repo.Root, "SpeechMessageProducts.sln"), "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"External\", \"../External.csproj\", \"{00000000-0000-0000-0000-000000000002}\"\r\nEndProject\r\n");
        using var output = new StringWriter();
        Assert.Equal(2, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
    }

    /// <summary>重複檔案身分不能靠字典覆寫或總數相抵隱藏；讀取基準時必須直接拒絕。</summary>
    [Fact(DisplayName = "基準重複檔案身分必須拒絕")]
    public void Cli_DuplicateBaselineFileIdentity_IsRejected()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        repo.Write("baseline.json", "{\"version\":1,\"projects\":[{\"project\":\"App/App.csproj\",\"count\":0,\"files\":[{\"path\":\"App/Code.cs\",\"count\":0},{\"path\":\"App/Code.cs\",\"count\":0}]}]}");
        using var output = new StringWriter();
        Assert.Equal(2, CommandRunner.Run(repo.Root, ["scan", "--baseline", "baseline.json"], output));
    }

    /// <summary>CP950 是既有來源盤點的只讀後備，解碼後必須保留常值且來源位元組完全不變。</summary>
    [Fact(DisplayName = "舊 CP950 來源唯讀盤點且不改寫")]
    public void Scan_Cp950Source_DecodesWithoutMutation()
    {
        using var repo = new RepositoryFixture();
        var path = Path.Combine(repo.Root, "Legacy.cs");
        const string source = "class C { string X = \"中文測試\"; }";
        var bytes = CodePagesEncodingProvider.Instance.GetEncoding(950)!.GetBytes(source);
        File.WriteAllBytes(path, bytes);
        Assert.Equal(source, LocalFiles.ReadSource(path));
        Assert.Equal(1, SourceScanner.CountCSharp(LocalFiles.ReadSource(path)));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>鍵必須完整符合格式，不能利用 Regex 的行尾語意讓最後一個換行穿過驗證。</summary>
    /// <param name="key">帶換行、空段、空白或非允許字元的鍵。</param>
    [Theory(DisplayName = "resx 鍵格式必須完整匹配整個名稱")]
    [InlineData("A.Text\n")]
    [InlineData("A.Text\r")]
    [InlineData("A..Text")]
    [InlineData(".A.Text")]
    [InlineData("A.")]
    [InlineData("a.Text")]
    [InlineData("A.文字")]
    [InlineData("A.X Y")]
    public void Resx_InvalidKeyBoundaries_AreRejected(string key)
    {
        using var files = new ResourceFixture();
        files.Write("", (key, "假資料"));
        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
    }
}
