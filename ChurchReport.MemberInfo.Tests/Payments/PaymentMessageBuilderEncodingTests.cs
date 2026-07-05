// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/PaymentMessageBuilderEncodingTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentMessageBuilderEncodingTests
// 主要成員：Dedication_success_message_uses_readable_traditional_chinese_text
// 引用命名空間：ChurchReport.Services、FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Services;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 保護付款後 LINE 通知文案的編碼與基本語意。
/// 這些訊息會直接送給奉獻者或付款者；如果檔案被 Big5/UTF-8 來回轉碼破壞，
/// 編譯仍可能成功，但使用者收到的內容會變成亂碼，因此用測試鎖住可讀的繁體中文關鍵字。
/// </summary>
public sealed class PaymentMessageBuilderEncodingTests
{
    [Fact]
    public void Dedication_success_message_uses_readable_traditional_chinese_text()
    {
        var builder = new PaymentMessageBuilder();

        var message = builder.BuildDedicationSuccessMessage(
            "胡夢嵩",
            "ORDER-001",
            "TX-001",
            8m,
            "十一奉獻",
            new DateTime(2026, 7, 6, 12, 30, 0));

        message.Should().Contain("金流付款成功通知");
        message.Should().Contain("親愛的 胡夢嵩");
        message.Should().Contain("奉獻類別：十一奉獻");
        message.Should().Contain("付款金額：NT$ 8");
        message.Should().NotContain("嚗");
        message.Should().NotContain(((char)0xFFFD).ToString());
    }
}
