// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/DefaultAvatarSvgTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DefaultAvatarSvgTests
// 主要成員：ForGender_ReturnsMaleAvatar_ForSupportedMaleCodes、ForGender_ReturnsFemaleAvatar_ForSupportedFemaleCodes、ForGender_ReturnsNeutralAvatar_ForUnknownCodes
// 引用命名空間：ChurchReport.Services.ContactAvatar、FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Services.ContactAvatar;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class DefaultAvatarSvgTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(200000)]
    public void ForGender_ReturnsMaleAvatar_ForSupportedMaleCodes(int genderCode)
    {
        DefaultAvatarSvg.ForGender(genderCode).Should().Be(DefaultAvatarSvg.Male);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(200001)]
    public void ForGender_ReturnsFemaleAvatar_ForSupportedFemaleCodes(int genderCode)
    {
        DefaultAvatarSvg.ForGender(genderCode).Should().Be(DefaultAvatarSvg.Female);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(100000000)]
    [InlineData(0)]
    public void ForGender_ReturnsNeutralAvatar_ForUnknownCodes(int? genderCode)
    {
        DefaultAvatarSvg.ForGender(genderCode).Should().Be(DefaultAvatarSvg.Neutral);
    }
}
