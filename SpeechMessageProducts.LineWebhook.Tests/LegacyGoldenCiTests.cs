/*
檔案整體說明
目的與定位：保護 P3-01 新工具的方案、CI 篩選、測試宿主與獨立覆蓋率整合，防止工具只存在磁碟或借用其他模組報告。
主要功能與流程：讀取本工作樹已核定的方案與 workflow，核對唯一 LegacyGolden 專案與 Debug／Release 設定、slnf 包含及 Host 測試專案參考；擷取真正 coverage matrix 的宣告列，並以省略、重複、錯宿主及錯 target 反例驗證拒絕。
使用邊界與維護：這是針對本包修改範圍的離線契約測試，不執行 workflow、不讀機密、不替主會話跑固定 guard。每次只借用小型文字檔及本地 XML／JSON reader，JSON owner 在案例結束 Dispose；沒有外部 process、網路、共享可變狀態或設定寫入。
*/
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>本包 CI 接入的精確正反例，不把字串任意出現當作矩陣已採用。</summary>
public sealed class LegacyGoldenCiTests
{
    /// <summary>新增工具恰一次且兩個正式組態都可建置；CI 篩選與測試宿主同時納入。</summary>
    [Fact]
    public void Tool_IsInSolutionFilterAndHostTests_WithDebugReleaseMappings()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(LegacyGoldenFixture.Converter)!, "../.."));
        var solution = File.ReadAllText(Path.Combine(root, "SpeechMessageProducts.sln"));
        var pattern = "(?m)^Project\\([^\\r\\n]+?= \"LegacyGolden\", \"tools\\\\LegacyGolden\\\\LegacyGolden.csproj\", \"(?<guid>[^\"]+)\"";
        var match = Assert.Single(Regex.Matches(solution, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Cast<Match>());
        foreach (var mode in new[] { "Debug", "Release" })
            foreach (var property in new[] { "ActiveCfg", "Build.0" })
                Assert.Contains(match.Groups["guid"].Value + "." + mode + "|Any CPU." + property + " = " + mode + "|Any CPU", solution, StringComparison.Ordinal);
        using var filter = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "SpeechMessageProducts.CI.slnf")));
        Assert.Single(filter.RootElement.GetProperty("solution").GetProperty("projects").EnumerateArray(),
            item => item.GetString() == "tools\\LegacyGolden\\LegacyGolden.csproj");
        var project = XDocument.Load(Path.Combine(root, "SpeechMessageProducts.LineWebhook.Tests", "SpeechMessageProducts.LineWebhook.Tests.csproj"));
        Assert.Single(project.Descendants("ProjectReference"), node => (string?)node.Attribute("Include") == "..\\tools\\LegacyGolden\\LegacyGolden.csproj");
        Assert.Single(project.Descendants("None"), node => (string?)node.Attribute("Update") == "Golden\\**\\*.json" && (string?)node.Attribute("CopyToOutputDirectory") == "PreserveNewest");
    }

    /// <summary>從實際 coverage run 的矩陣擷取 LegacyGolden，必須只有一筆 net10 與既有 Host 測試宿主配對。</summary>
    [Fact]
    public void Workflow_RequiresSeparateLegacyGoldenCoverage()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(LegacyGoldenFixture.Converter)!, "../.."));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "solution-ci.yml"));
        var start = workflow.IndexOf("      - name: Coverage for each new module and target", StringComparison.Ordinal);
        var end = workflow.IndexOf("      - name:", start + 10, StringComparison.Ordinal);
        var block = workflow[start..end];
        var rows = Regex.Matches(block,
            "(?m)^\\s*@\\{ Project='(?<project>[^']+)'; Framework='(?<framework>[^']+)'; Module='LegacyGolden' \\},?\\r?$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(match => (match.Groups["project"].Value, match.Groups["framework"].Value)).ToArray();
        Assert.True(Valid(rows));
        Assert.Contains("--collect:\"XPlat Code Coverage\"", block, StringComparison.Ordinal);
        Assert.Contains("$reports.Count -ne 1", block, StringComparison.Ordinal);
        Assert.Contains("-Module $case.Module", block, StringComparison.Ordinal);
    }

    /// <summary>省略、借錯宿主或 target、重複矩陣都必須拒絕；反例只修改純合成列，不改正式 workflow。</summary>
    [Fact]
    public void CoverageContract_RejectsMissingDuplicateAndWrongPairings()
    {
        Assert.True(Valid([("SpeechMessageProducts.LineWebhook.Tests", "net10.0")]));
        Assert.False(Valid([]));
        Assert.False(Valid([("SpeechMessageProducts.LineWebhook.Tests", "net48")]));
        Assert.False(Valid([("Other.Tests", "net10.0")]));
        Assert.False(Valid([("SpeechMessageProducts.LineWebhook.Tests", "net10.0"), ("SpeechMessageProducts.LineWebhook.Tests", "net10.0")]));
    }

    /// <summary>固定的配對契約與正式 workflow 正例共用，不接受其他模組的 coverage 或重複取最好結果。</summary>
    private static bool Valid((string Project, string Framework)[] rows) => rows.Length == 1 &&
        rows[0].Project == "SpeechMessageProducts.LineWebhook.Tests" && rows[0].Framework == "net10.0";
}
