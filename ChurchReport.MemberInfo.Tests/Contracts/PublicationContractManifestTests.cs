/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 Solution-level manifest 不是只存在檔名，而是可被自動化檢查的實際契約入口。
 *
 * 主要功能與流程：
 * 本檔宣告 PublicationContractManifestTests。
 * - 保護每個已登記 consumer 都有產品、端點、View、權威 ID、scope、容量與測試套件。 決勝斷言同時確認檔案存在、兩個 ChurchReport Grid 均登記且 identity 不是姓名欄位。
 * - 先從測試輸出定位最近的 SpeechMessageProducts.sln，再限定於該 worktree 查詢指定檔案。 缺少方案或檔案必須立即失敗，不得讀取父 worktree 的舊副本或退回輸出目錄使缺檔測試假通過。
 *  方法僅查詢檔案資訊，路徑只屬於當次測試，不保存內容、watcher、handle 或跨案例狀態。
 * 測試案例入口包含 Manifest_ContainsRequiredStableIdentityAndConsumerBoundaryFields、FindRepositoryFile。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
// ============================================================================
// 檔案路徑：ChurchReport.MemberInfo.Tests/Contracts/PublicationContractManifestTests.cs
// 測試責任：驗證 Solution 層級 publication manifest 確實登記每個 ChurchReport Grid consumer，
//           讓未來採購協會、建設公司與其他產品能沿用相同 stable-ID、scope 與 lifecycle 門禁。
// 故障模型：刪除必要欄位、重複 consumer key、指定不存在 view 或使用姓名作 identity 都必須失敗；
//           測試不建立 Session、HttpContext、CRM client、背景 Task 或長生命週期 cache。
// 編碼要求：本檔案必須以 UTF-8 without BOM、CRLF、final CRLF 儲存。
// ============================================================================
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Contracts;

/// <summary>
/// 驗證 Solution-level manifest 不是只存在檔名，而是可被自動化檢查的實際契約入口。
/// </summary>
public sealed class PublicationContractManifestTests
{
    /// <summary>
    /// 保護每個已登記 consumer 都有產品、端點、View、權威 ID、scope、容量與測試套件。
    /// 決勝斷言同時確認檔案存在、兩個 ChurchReport Grid 均登記且 identity 不是姓名欄位。
    /// </summary>
    [Fact]
    public void Manifest_ContainsRequiredStableIdentityAndConsumerBoundaryFields()
    {
        var manifestPath = FindRepositoryFile("docs", "publication-contracts.json");
        File.Exists(manifestPath).Should().BeTrue();

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);

        var consumers = document.RootElement.GetProperty("consumers").EnumerateArray().ToArray();
        consumers.Should().NotBeEmpty();
        consumers.Select(item =>
                $"{item.GetProperty("product").GetString()}::{item.GetProperty("consumer").GetString()}")
            .Should().OnlyHaveUniqueItems();

        consumers.Should().Contain(item =>
            item.GetProperty("product").GetString() == "ChurchReport" &&
            item.GetProperty("consumer").GetString() == "ChurchReport.WeeklyReport.SmallGroupGrid" &&
            item.GetProperty("identity").GetString() == "PresentRecordId");
        consumers.Should().Contain(item =>
            item.GetProperty("product").GetString() == "ChurchReport" &&
            item.GetProperty("consumer").GetString() == "ChurchReport.WeeklyReport.NewPersonGrid" &&
            item.GetProperty("identity").GetString() == "PresentRecordId");

        foreach (var consumer in consumers)
        {
            consumer.GetProperty("endpoint").GetString().Should().NotBeNullOrWhiteSpace();
            var view = consumer.GetProperty("view").GetString();
            view.Should().NotBeNullOrWhiteSpace();
            File.Exists(FindRepositoryFile(view!.Split('/'))).Should().BeTrue();
            consumer.GetProperty("identityKind").GetString().Should().Be("persisted-guid");
            consumer.GetProperty("scopeFields").GetArrayLength().Should().BeGreaterThan(0);
            consumer.GetProperty("maxRows").GetInt32().Should().BeGreaterThan(0);
            var contractTestSuite = consumer.GetProperty("contractTestSuite").GetString();
            contractTestSuite.Should().NotBeNullOrWhiteSpace();
            typeof(PublicationContractManifestTests).Assembly.GetTypes()
                .Should().Contain(type => type.Name == contractTestSuite,
                    "manifest 不得只填入不存在或從未執行的測試套件名稱");
        }
    }

    /// <summary>
    /// 先從測試輸出定位最近的 SpeechMessageProducts.sln，再限定於該 worktree 查詢指定檔案。
    /// 缺少方案或檔案必須立即失敗，不得讀取父 worktree 的舊副本或退回輸出目錄使缺檔測試假通過。
    /// 方法僅查詢檔案資訊，路徑只屬於當次測試，不保存內容、watcher、handle 或跨案例狀態。
    /// </summary>
    /// <param name="relativeSegments">可信任 manifest 或測試定義提供的方案相對路徑。</param>
    /// <returns>最近方案根目錄內已確認存在的檔案路徑。</returns>
    /// <exception cref="DirectoryNotFoundException">無法定位目前 worktree 的方案。</exception>
    /// <exception cref="FileNotFoundException">指定檔案不在目前 worktree 中。</exception>
    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new DirectoryNotFoundException("找不到目前 worktree 的 SpeechMessageProducts.sln。");
        }

        var candidate = Path.Combine(directory.FullName, Path.Combine(relativeSegments));
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException("目前 worktree 缺少 publication contract 檔案。", candidate);
        }

        return candidate;
    }
}
