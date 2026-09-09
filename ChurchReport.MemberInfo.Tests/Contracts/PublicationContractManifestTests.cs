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
            item.GetProperty("consumer").GetString() == "WeeklyReport.SmallGroup" &&
            item.GetProperty("identity").GetString() == "PresentRecordId");
        consumers.Should().Contain(item =>
            item.GetProperty("product").GetString() == "ChurchReport" &&
            item.GetProperty("consumer").GetString() == "WeeklyReport.NewPerson" &&
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
    /// 從測試輸出目錄向上尋找 Solution root；方法只讀檔案系統且不保存路徑或檔案內容，
    /// 搜尋完成後不留下 watcher、handle 或其他資源，因此不會造成測試生命週期洩漏。
    /// </summary>
    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = relativeSegments.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, Path.Combine(relativeSegments));
    }
}
