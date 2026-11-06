/*
 * 檔案整體說明
 *
 * 目的與定位：驗證共用 RichMenu 專案不依賴任何產品層、資料庫層或 ASP.NET MVC 層型別。 這是架構邊界測試，確保 RichMenu 共用能力可以被未來多個產品重用。
 *
 * 主要功能與流程：
 * 本檔宣告 RichMenuProjectBoundaryTests。
 * - 保護 RichMenu 不依賴產品、CRM 或 MVC 層；掃描最近 worktree 方案下的真實來源。 注入的退化情境是重新加入任一禁止依賴；決定性斷言是完整命中集合為空，不能因找錯方案而漏掃。
 * - 從測試輸出目錄尋找最近的 SpeechMessageProducts.sln，界定本次 worktree 的來源範圍。 找不到方案就明確失敗；不使用其他名稱或目前目錄作為後備，以免驗證到另一份程式。
 * 測試案例入口包含 RichMenu_project_does_not_reference_product_specific_dependencies、FindProjectRoot。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
    /// 保護 RichMenu 不依賴產品、CRM 或 MVC 層；掃描最近 worktree 方案下的真實來源。
    /// 注入的退化情境是重新加入任一禁止依賴；決定性斷言是完整命中集合為空，不能因找錯方案而漏掃。
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
    /// 從測試輸出目錄尋找最近的 SpeechMessageProducts.sln，界定本次 worktree 的來源範圍。
    /// 找不到方案就明確失敗；不使用其他名稱或目前目錄作為後備，以免驗證到另一份程式。
    /// </summary>
    /// <returns>含目前方案標記的最近上層目錄。</returns>
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
