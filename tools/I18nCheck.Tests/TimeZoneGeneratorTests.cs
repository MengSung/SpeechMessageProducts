/*
 * 檔案整體說明
 *
 * 目的與定位：對實際 CLI 注入最小 CLDR 候選，驗證排序、衝突、版本、XML 安全與寫入所有權。 每案使用獨佔假資源目錄，不下載內容、不連外；測完由原 fixture owner 關閉與清理。
 *
 * 主要功能與流程：
 * 本檔宣告 TimeZoneGeneratorTests。
 * - 打亂列序與重複相同 territory 映射，輸出仍逐位元組相同；check 不得改寫目的地。
 * - 同一 IANA 指向不同 Windows ID 必須拒絕，既有輸出完整保留，不可最後寫入者勝出。
 * - 上游固定外部 DTD 宣告只略過、不解析；有實體引用的 XXE 則必須拒絕且不建立輸出。
 * - 錯誤根、空映射、缺欄、注入識別及超預算皆須拒絕；版本選項不允許插入程式。
 * 測試案例入口包含 GenerateTimeZones_ReorderedMappings_IsDeterministic、
 * GenerateTimeZones_ConflictingIdentity_KeepsOldOutput、GenerateTimeZones_DtdIsIgnored_EntitiesAreNotExpanded、
 * GenerateTimeZones_InvalidSchema_Rejects、Verify_LocalizationWithoutCldr_Rejects、
 * GenerateTimeZones_BudgetOrVersion_Rejects、Args、Xml。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Text;
using Xunit;

namespace I18nCheck.Tests;

/// <summary>
/// 對實際 CLI 注入最小 CLDR 候選，驗證排序、衝突、版本、XML 安全與寫入所有權。
/// 每案使用獨佔假資源目錄，不下載內容、不連外；測完由原 fixture owner 關閉與清理。
/// </summary>
public sealed class TimeZoneGeneratorTests
{
    /// <summary>打亂列序與重複相同 territory 映射，輸出仍逐位元組相同；check 不得改寫目的地。</summary>
    [Fact(DisplayName = "時區CLI固定排序與唯讀check")]
    public void GenerateTimeZones_ReorderedMappings_IsDeterministic()
    {
        using var files = new ResourceFixture();
        var root = Path.GetDirectoryName(files.BasePath)!;
        var source = Path.Combine(root, "windowsZones.xml");
        var destination = Path.Combine(root, "IanaWindowsZones.g.cs");
        File.WriteAllText(source, Xml("<mapZone other='Taipei Standard Time' territory='TW' type='Asia/Taipei'/><mapZone other='UTC' territory='001' type='Etc/UTC'/><mapZone other='Taipei Standard Time' territory='001' type='Asia/Taipei'/>"));
        using var output = new StringWriter();
        Assert.Equal(0, CommandRunner.Run(root, Args(), output));
        var first = File.ReadAllBytes(destination);
        var text = new UTF8Encoding(false, true).GetString(first);
        Assert.Contains("CLDR 49", text, StringComparison.Ordinal);
        Assert.Contains("\"Asia/Taipei\" => \"Taipei Standard Time\"", text, StringComparison.Ordinal);
        Assert.False(first.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        File.WriteAllText(source, Xml("<mapZone other='UTC' territory='001' type='Etc/UTC'/><mapZone other='Taipei Standard Time' territory='001' type='Asia/Taipei'/>"));
        Assert.Equal(0, CommandRunner.Run(root, Args(), output));
        Assert.Equal(first, File.ReadAllBytes(destination));
        Assert.Equal(0, CommandRunner.Run(root, [.. Args(), "--check"], output));
        File.WriteAllText(destination, "synthetic stale output");
        var stale = File.ReadAllBytes(destination);
        Assert.Equal(1, CommandRunner.Run(root, [.. Args(), "--check"], output));
        Assert.Equal(stale, File.ReadAllBytes(destination));
    }

    /// <summary>同一 IANA 指向不同 Windows ID 必須拒絕，既有輸出完整保留，不可最後寫入者勝出。</summary>
    [Fact(DisplayName = "時區衝突拒絕且不覆寫既有輸出")]
    public void GenerateTimeZones_ConflictingIdentity_KeepsOldOutput()
    {
        using var files = new ResourceFixture();
        var root = Path.GetDirectoryName(files.BasePath)!;
        File.WriteAllText(Path.Combine(root, "windowsZones.xml"), Xml("<mapZone other='A' territory='001' type='Asia/Taipei'/><mapZone other='B' territory='TW' type='Asia/Taipei'/>"));
        var destination = Path.Combine(root, "IanaWindowsZones.g.cs");
        File.WriteAllText(destination, "previous complete output");
        using var output = new StringWriter();
        Assert.Equal(2, CommandRunner.Run(root, Args(), output));
        Assert.Equal("previous complete output", File.ReadAllText(destination));
        Assert.DoesNotContain("Asia/Taipei", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>上游固定外部 DTD 宣告只略過、不解析；有實體引用的 XXE 則必須拒絕且不建立輸出。</summary>
    [Fact(DisplayName = "CLDR外部DTD不載入且XXE拒絕")]
    public void GenerateTimeZones_DtdIsIgnored_EntitiesAreNotExpanded()
    {
        using var files = new ResourceFixture();
        var root = Path.GetDirectoryName(files.BasePath)!;
        var source = Path.Combine(root, "windowsZones.xml");
        using var output = new StringWriter();
        var body = Xml("<mapZone other='UTC' territory='001' type='Etc/UTC'/>");
        File.WriteAllText(source, "<!DOCTYPE supplementalData SYSTEM 'file:///not-read.dtd'>" + body);
        Assert.Equal(0, CommandRunner.Run(root, Args(), output));
        var previous = File.ReadAllBytes(Path.Combine(root, "IanaWindowsZones.g.cs"));
        File.WriteAllText(source, "<!DOCTYPE supplementalData [<!ENTITY x SYSTEM 'file:///not-read.txt'>]>" + Xml("<mapZone other='&x;' territory='001' type='Etc/UTC'/>"));
        Assert.Equal(2, CommandRunner.Run(root, Args(), output));
        Assert.Equal(previous, File.ReadAllBytes(Path.Combine(root, "IanaWindowsZones.g.cs")));
    }

    /// <summary>錯誤根、空映射、缺欄、注入識別及超預算皆須拒絕；版本選項不允許插入程式。</summary>
    /// <param name="body">整份合成 XML。</param>
    [Theory(DisplayName = "時區不完整或非法資料拒絕")]
    [InlineData("<root/>")]
    [InlineData("<supplementalData><windowsZones><mapTimezones/></windowsZones></supplementalData>")]
    [InlineData("<supplementalData><windowsZones><mapTimezones><mapZone other='UTC' territory='001'/></mapTimezones></windowsZones></supplementalData>")]
    [InlineData("<supplementalData><windowsZones><mapTimezones><mapZone other='UTC' territory='001' type='bad&quot;code'/></mapTimezones></windowsZones></supplementalData>")]
    public void GenerateTimeZones_InvalidSchema_Rejects(string body)
    {
        using var files = new ResourceFixture();
        var root = Path.GetDirectoryName(files.BasePath)!;
        File.WriteAllText(Path.Combine(root, "windowsZones.xml"), body);
        using var output = new StringWriter();
        Assert.Equal(2, CommandRunner.Run(root, Args(), output));
        Assert.False(File.Exists(Path.Combine(root, "IanaWindowsZones.g.cs")));
    }

    /// <summary>正式 Localization 存在時，刪除 CLDR 不得讓 verify 靜默跳過；最小其他 repo 仍相容。</summary>
    [Fact(DisplayName = "正式驗證缺CLDR來源不得假通過")]
    public void Verify_LocalizationWithoutCldr_Rejects()
    {
        using var repo = new RepositoryFixture();
        repo.AddProject("SpeechMessage.Localization", false);
        using var output = new StringWriter();
        Assert.Equal(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "tools/I18nCheck/baselines/cjk-literals.json"], output));
        Assert.Equal(2, CommandRunner.Run(repo.Root, ["verify"], output));
    }

    /// <summary>任一生成預算或版本輸入不合法都須在寫檔前拒絕；不是只靠單檔 XML 大小限制。</summary>
    /// <param name="mode">來源列、唯一映射或版本故障。</param>
    [Theory(DisplayName = "時區生成的來源列與唯一映射皆有界")]
    [InlineData("rows")]
    [InlineData("identities")]
    [InlineData("version")]
    public void GenerateTimeZones_BudgetOrVersion_Rejects(string mode)
    {
        using var files = new ResourceFixture();
        var root = Path.GetDirectoryName(files.BasePath)!;
        var rows = mode == "rows" ? string.Concat(Enumerable.Repeat("<mapZone other='UTC' territory='001' type='Etc/UTC'/>", 4097)) :
            mode == "identities" ? string.Concat(Enumerable.Range(0, 2049).Select(index => "<mapZone other='UTC' territory='001' type='Etc/Test" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "'/>")) :
            "<mapZone other='UTC' territory='001' type='Etc/UTC'/>";
        File.WriteAllText(Path.Combine(root, "windowsZones.xml"), Xml(rows));
        var args = Args();
        if (mode == "version") args[4] = "49\"injection";
        using var output = new StringWriter();
        Assert.Equal(2, CommandRunner.Run(root, args, output));
        Assert.False(File.Exists(Path.Combine(root, "IanaWindowsZones.g.cs")));
    }

    /// <summary>固定完整選項，讓測試驗證同一個正式命令入口，不以另一套測試產生器冒充。</summary>
    private static string[] Args() => ["generate-timezones", "--cldr", "windowsZones.xml", "--cldr-version", "49", "--out", "IanaWindowsZones.g.cs"];

    /// <summary>只有 mapZone 片段可變，其餘固定結構與上游一致；不引入外部解析器。</summary>
    private static string Xml(string rows) => "<supplementalData><windowsZones><mapTimezones>" + rows + "</mapTimezones></windowsZones></supplementalData>";
}
