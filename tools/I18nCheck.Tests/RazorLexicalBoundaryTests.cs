using Xunit;

namespace I18nCheck.Tests;

/// <summary>
/// 保護 Razor 程式區塊與後續顯示文字的詞法邊界。所有輸入都是合成模板，
/// 不執行 Razor；斷言實際顯示片段及 CLI 棘輪拒絕，避免引號誤判造成假綠燈。
/// </summary>
public sealed class RazorLexicalBoundaryTests
{
    /// <summary>
    /// 注入逐字、原始、插值字串及註解中的括號；程式內容不計數，
    /// 後續 HTML 必須完整保留，不能由反斜線或假括號吞掉檔案餘段。
    /// </summary>
    /// <param name="source">只含假路徑、假字串與固定顯示文字的模板。</param>
    /// <param name="expected">逐一明列的顯示片段數。</param>
    [Theory]
    [InlineData("""@{ var path = @"C:\Logs\"; }<p>顯示</p>""", 1)]
    [InlineData("""@{ var path = @"C:\A""}B\"; }<p>顯示</p>""", 1)]
    [InlineData("""@if (Model.Path == @"C:\Logs\") { <p title="提示">顯示</p> }""", 2)]
    [InlineData(""""@{ var code = """brace } and " and \ """; }<p>顯示</p>"""", 1)]
    [InlineData("""@{ var code = $"{(true ? "}" : "{")}"; }<p>顯示</p>""", 1)]
    [InlineData("""@{ /* } " */ var x = 1; }<p>顯示</p>""", 1)]
    [InlineData("@{ // } \"\r\n var x = 1; }<p>顯示</p>", 1)]
    [InlineData("""@{ var value = new { Path = @"C:\Logs\", Nested = new { X = 1 } }; }<p>顯示</p>""", 1)]
    public void Scan_RazorLexicalBoundary_KeepsFollowingDisplay(string source, int expected) =>
        Assert.Equal(expected, SourceScanner.CountRazor(source));

    /// <summary>
    /// 不完整區塊或字串不能被當成「其餘內容皆非顯示」而回傳零；
    /// 固定輸入錯誤直接失敗，不附帶模板內容或假路徑。
    /// </summary>
    /// <param name="source">刻意截斷的合成模板。</param>
    [Theory]
    [InlineData("@{ var path = @\"unterminated; }<p>中文</p>")]
    [InlineData("@{ var value = 1;")]
    [InlineData("@(1 + \"unterminated)")]
    public void Scan_RazorUnclosedCode_FailsClosed(string source) =>
        Assert.Throws<InvalidDataException>(() => SourceScanner.CountRazor(source));

    /// <summary>
    /// 從零計數基準加入「逐字字串後的中文」；真正 scan 與 verify 都必須非零失敗。
    /// 每案例獨占假方案與輸出 writer，Dispose 後立即刪除，沒有來源或計數跨案例保留。
    /// </summary>
    [Fact]
    public void Cli_RazorVerbatimThenNewDisplay_RejectsRatchetIncrease()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("App", false);
        using var baselineOutput = new StringWriter();
        Assert.Equal(0, CommandRunner.Run(repo.Root,
            ["scan", "--write-baseline", "tools/I18nCheck/baselines/cjk-literals.json"], baselineOutput));
        repo.Write("App/View.cshtml", """@{ var path = @"C:\Logs\"; }<p>新增中文</p>""");
        foreach (var command in new[] { "scan", "verify" })
        {
            using var output = new StringWriter();
            Assert.Equal(1, CommandRunner.Run(repo.Root, [command], output));
            Assert.Contains("CJK ratchet: count increased", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("新增中文", output.ToString(), StringComparison.Ordinal);
        }
    }
}
