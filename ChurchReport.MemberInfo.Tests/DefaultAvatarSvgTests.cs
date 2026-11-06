/*
 * 檔案整體說明
 *
 * 目的與定位：驗證標準及舊性別代碼選取男性、女性或中性預設SVG，避免沒有照片的會友呈現錯誤性別圖示。
 *
 * 主要功能與流程：
 * 本檔宣告 DefaultAvatarSvgTests。
 * 測試案例入口包含 ForGender_ReturnsMaleAvatar_ForSupportedMaleCodes、
 * ForGender_ReturnsFemaleAvatar_ForSupportedFemaleCodes、ForGender_ReturnsNeutralAvatar_ForUnknownCodes。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
