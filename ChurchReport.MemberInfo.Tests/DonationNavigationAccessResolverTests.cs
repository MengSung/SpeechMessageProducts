// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/DonationNavigationAccessResolverTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationNavigationAccessResolverTests
// 主要成員：CanAccessDonationManagement_returns_true_for_accounting_roles、CanAccessDonationManagement_returns_false_for_non_accounting_roles
// 引用命名空間：ChurchReport.Services.Donation、FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Services.Donation;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public sealed class DonationNavigationAccessResolverTests
{
    [Theory]
    [InlineData("會計")]
    [InlineData("主責會計")]
    [InlineData("  會計同工  ")]
    public void CanAccessDonationManagement_returns_true_for_accounting_roles(string jobTitle)
    {
        DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("小組長")]
    [InlineData("牧養主任")]
    [InlineData("行政同工")]
    public void CanAccessDonationManagement_returns_false_for_non_accounting_roles(string? jobTitle)
    {
        DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle)
            .Should().BeFalse();
    }
}
