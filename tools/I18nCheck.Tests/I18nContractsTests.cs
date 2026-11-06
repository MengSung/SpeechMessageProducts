/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P0-07 原列十二個契約，以最小來源及假資源注入 CJK、忽略、棘輪與資源錯誤。 每個案例使用獨立暫存目錄，沒有共享可變狀態，斷言實際數量、錯誤或輸出位元組。
 *
 * 主要功能與流程：
 * 本檔宣告 I18nContractsTests、ResourceFixture。
 * - 注入 ILogger 樣板及一個顯示常值，排除診斷時仍必須保留顯示常值。
 * - 中文只出現在註解；若改為文字搜尋而非語法樹，零計數就會失敗。
 * - 對成員注入有理由的忽略標記，同類別未標記成員仍必須計數。
 * - 打亂資源列順序，兩次產出仍必須逐位元組相同，且鍵依 Ordinal 排序。
 * 測試案例入口包含 Scan_LiteralWithCjk_IsCounted、Scan_LoggerTemplate_IsExcluded、Scan_CommentOnly_IsNotCounted、
 * Scan_I18nIgnoreAttribute_IsExcluded、Ratchet_CountIncreases_Fails、
 * Ratchet_CountDecreases_PassesAndReportsImprovement、Generate_SameInput_ByteIdenticalOutput、
 * Generate_OutputIsUtf8NoBomCrlf；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 tools/I18nCheck.Tests/BoundaryTests.cs、tools/I18nCheck.Tests/InputIntegrityTests.cs、
 * tools/I18nCheck.Tests/ResourceVerificationTests.cs、tools/I18nCheck.Tests/TimeZoneGeneratorTests.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace I18nCheck.Tests;

/// <summary>
/// 保護 P0-07 原列十二個契約，以最小來源及假資源注入 CJK、忽略、棘輪與資源錯誤。
/// 每個案例使用獨立暫存目錄，沒有共享可變狀態，斷言實際數量、錯誤或輸出位元組。
/// </summary>
public sealed class I18nContractsTests
{
    /// <summary>注入一個中文字串，移除字串辨識會使數量斷言失敗。</summary>
    [Fact(DisplayName = "CJK 字串常值必須納入計數")]
    public void Scan_LiteralWithCjk_IsCounted() => Assert.Equal(1, SourceScanner.CountCSharp("class C { string Text = \"中文\"; }"));

    /// <summary>注入 ILogger 樣板及一個顯示常值，排除診斷時仍必須保留顯示常值。</summary>
    [Fact(DisplayName = "ILogger 訊息樣板不增加顯示文字棘輪")]
    public void Scan_LoggerTemplate_IsExcluded() => Assert.Equal(1, SourceScanner.CountCSharp("using Microsoft.Extensions.Logging; class C { void M(ILogger logger) { logger.LogInformation(\"診斷 {Id}\", 1); var text = \"顯示\"; } }"));

    /// <summary>中文只出現在註解；若改為文字搜尋而非語法樹，零計數就會失敗。</summary>
    [Fact(DisplayName = "中文註解不當成字串")]
    public void Scan_CommentOnly_IsNotCounted() => Assert.Equal(0, SourceScanner.CountCSharp("// 中文註解\nclass C { }"));

    /// <summary>對成員注入有理由的忽略標記，同類別未標記成員仍必須計數。</summary>
    [Fact(DisplayName = "有理由的 I18nIgnore 僅排除其成員")]
    public void Scan_I18nIgnoreAttribute_IsExcluded() => Assert.Equal(1, SourceScanner.CountCSharp("class C { [I18nIgnore(\"協定固定值\")] void M() { var x = \"略過\"; } string Name = \"保留\"; }"));

    /// <summary>專案增加一筆常值即失敗，避免總數相抵讓跨專案新增通過。</summary>
    [Fact(DisplayName = "專案中文字串數增加必須拒絕")]
    public void Ratchet_CountIncreases_Fails() => Assert.False(CjkRatchet.Compare(new Dictionary<string, int> { ["A"] = 2 }, new Dictionary<string, int> { ["A"] = 1 }).Passed);

    /// <summary>數量下降應通過並指出改善專案，不默默改寫基準。</summary>
    [Fact(DisplayName = "減少中文字串通過並回報改善")]
    public void Ratchet_CountDecreases_PassesAndReportsImprovement()
    {
        var result = CjkRatchet.Compare(new Dictionary<string, int> { ["A"] = 1 }, new Dictionary<string, int> { ["A"] = 2 });
        Assert.True(result.Passed);
        Assert.Contains("A", result.Improvements);
    }

    /// <summary>打亂資源列順序，兩次產出仍必須逐位元組相同，且鍵依 Ordinal 排序。</summary>
    [Fact(DisplayName = "相同資源的產生結果逐位元組相同")]
    public void Generate_SameInput_ByteIdenticalOutput()
    {
        using var files = new ResourceFixture();
        files.Write("", ("Z.Last", "末筆"), ("A.First", "首筆"));
        var first = ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample.Resources", "SampleData");
        files.Write("", ("A.First", "首筆"), ("Z.Last", "末筆"));
        Assert.Equal(first, ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample.Resources", "SampleData"));
        var text = Encoding.UTF8.GetString(first);
        Assert.True(text.IndexOf("A.First", StringComparison.Ordinal) < text.IndexOf("Z.Last", StringComparison.Ordinal));
    }

    /// <summary>包含多行資源時輸出仍為無 BOM UTF-8，所有檔案換行與結尾都固定 CRLF。</summary>
    [Fact(DisplayName = "產出符合無 BOM UTF-8 與 CRLF")]
    public void Generate_OutputIsUtf8NoBomCrlf()
    {
        using var files = new ResourceFixture();
        files.Write("", ("A.Text", "中文\n第二行"));
        var bytes = ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample.Resources", "SampleData");
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        var text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("qps-ploc", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 使用者核定七筆範例保護字數公式及參數完整性；每筆期望明列，沒有在測試重算實作公式。
    /// 改用三個固定補字、移動補字位置或把參數算入長度，都會使決定性字串斷言失敗。
    /// </summary>
    /// <param name="input">中性資源字串。</param>
    /// <param name="expected">使用者核定的完整偽語系輸出。</param>
    [Theory(DisplayName = "偽語系保留參數並依核定公式補字")]
    [InlineData("{name}，報名成功", "［{name}，報名成功ǂǂ］")]
    [InlineData("課程報名", "［課程報名ǂǂ］")]
    [InlineData("連線測試成功", "［連線測試成功ǂǂǂ］")]
    [InlineData("小組", "［小組ǂ］")]
    [InlineData("{name}", "［{name}ǂ］")]
    [InlineData("目前語系：{culture}", "［目前語系：{culture}ǂǂ］")]
    [InlineData("{churchName}小幫手", "［{churchName}小幫手ǂǂ］")]
    public void Pseudo_WrapsAndExpands_KeepsPlaceholders(string input, string expected) => Assert.Equal(expected, PseudoLocale.Transform(input));

    /// <summary>同一資源檔重複鍵必須失敗，禁止字典覆寫把衝突隱藏。</summary>
    [Fact(DisplayName = "resx 重複鍵拒絕載入")]
    public void Resx_DuplicateKey_Fails()
    {
        using var files = new ResourceFixture();
        files.Write("", ("A.Text", "甲"), ("A.Text", "乙"));
        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
    }

    /// <summary>翻譯把 name 改成 other 時拒絕，防止執行時遺失替換參數。</summary>
    [Fact(DisplayName = "跨語系參數名稱集合必須一致")]
    public void Resx_ParameterMismatchBetweenCultures_Fails()
    {
        using var files = new ResourceFixture();
        files.Write("", ("A.Text", "你好 {name}"));
        files.Write("en", ("A.Text", "Hello {other}"));
        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
    }

    /// <summary>注入沒有分層的大寫鍵，必須因格式而失敗，不能產生不可追蹤資料。</summary>
    [Fact(DisplayName = "resx 鍵必須符合分層格式")]
    public void Resx_InvalidKeyFormat_Fails()
    {
        using var files = new ResourceFixture();
        files.Write("", ("invalid", "中文"));
        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
    }
}

/// <summary>
/// 每個案例獨佔一個隨機暫存目錄，僅保存假資源；Write 的串流立即關閉，Dispose 遞迴清理自己建立的目錄。
/// 不使用真實 repo 設定、個資或服務連線，最長生命週期為單一測試，平行案例互不共享。
/// </summary>
internal sealed class ResourceFixture : IDisposable
{
    /// <summary>本案例唯一擁有的目錄；清理目標永遠由建構式建立，不接受外部路徑。</summary>
    private readonly string directory = Path.Combine(Path.GetTempPath(), "i18n-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>建立隔離目錄，之後只在此寫入測試 resx。</summary>
    internal ResourceFixture() => Directory.CreateDirectory(directory);

    /// <summary>中性資源固定路徑，衍生語系使用同一個名稱前綴。</summary>
    internal string BasePath => Path.Combine(directory, "Sample.resx");

    /// <summary>寫入假資源，保留呼叫者提供的順序與重複鍵供負向測試使用。</summary>
    /// <param name="culture">空字串為中性，否則為語系後綴。</param>
    /// <param name="rows">鍵與純假文字；不讀取實際產品機密。</param>
    internal void Write(string culture, params (string Key, string Text)[] rows)
    {
        var root = new XElement("root", rows.Select(row => new XElement("data", new XAttribute("name", row.Key), new XElement("value", row.Text))));
        // XML 會把原始 CRLF 正規化成 LF；用字元實體保留 CR，才能真正測到產生器的逸出契約。
        using var writer = XmlWriter.Create(Path.Combine(directory, "Sample" + (culture.Length == 0 ? "" : "." + culture) + ".resx"),
            new XmlWriterSettings { Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize, Indent = true });
        root.Save(writer);
    }

    /// <summary>測試結束後確定釋放自己持有的全部檔案，跨案例不保留任何可變資料或把手。</summary>
    public void Dispose() => Directory.Delete(directory, true);
}
