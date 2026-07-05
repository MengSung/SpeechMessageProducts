using FluentAssertions;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Boundary;

/// <summary>
/// 驗證共用 RichMenu 專案不依賴任何產品層、資料庫層或 ASP.NET MVC 層型別。
/// 這是架構邊界測試，確保 RichMenu 共用能力可以被未來多個產品重用。
/// </summary>
public sealed class RichMenuProjectBoundaryTests
{
    /// <summary>
    /// 掃描 RichMenu 共用專案原始碼，若出現產品名稱或上層框架關鍵字就視為邊界破壞。
    /// </summary>
    [Fact]
    public void RichMenu_project_does_not_reference_product_specific_dependencies()
    {
        var projectRoot = FindProjectRoot();
        var richMenuDirectory = Path.Combine(projectRoot, "LineMessagingProcessor.RichMenus");
        var forbiddenTerms = new[]
        {
            "ChurchReport",
            "Microsoft.Xrm",
            "IOrganizationService",
            "DbContext",
            "Controller",
            "IActionResult"
        };

        var hits = Directory
            .EnumerateFiles(richMenuDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, index }))
            .Where(item => forbiddenTerms.Any(term => item.line.Contains(term, StringComparison.Ordinal)))
            .Select(item => $"{Path.GetFileName(item.path)}:{item.index + 1}:{item.line}")
            .ToList();

        hits.Should().BeEmpty();
    }

    /// <summary>
    /// 從測試輸出目錄往上找 solution root，讓測試可在 IDE、CLI 與 CI 中穩定定位專案檔。
    /// </summary>
    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ChurchReport.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate solution root.");
    }
}
