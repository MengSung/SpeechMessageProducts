// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class RichMenuProjectBoundaryTests
// 主要成員：RichMenu_project_does_not_reference_product_specific_dependencies、FindProjectRoot
// 引用命名空間：FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate solution root.");
    }
}
