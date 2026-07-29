// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentNamingCompatibilityTests
// 主要成員：Old_qpay_card_controller_should_not_remain_as_csharp_alias、New_payment_return_controller_exists_after_rename
// 引用命名空間：FluentAssertions、Microsoft.AspNetCore.Mvc、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentNamingCompatibilityTests
{
    [Fact]
    public void Old_qpay_card_controller_should_not_remain_as_csharp_alias()
    {
        // 舊的 QPayCardController 仍然承接既有金流回傳 URL。
        // 這是外部金流設定與舊連結的相容層，不代表新程式還應該以 QPay 命名為主。
        var legacyType = Type.GetType("ChurchReport.Controllers.QPayCardController, SpeechMessageProducts.ChurchReport");

        legacyType.Should().BeNull();
    }

    [Fact]
    public void New_payment_return_controller_exists_after_rename()
    {
        // 這個測試描述目標狀態：
        // 新的主要 controller 名稱應該描述「付款回傳端點」，而不是描述特定金流供應商。
        var newType = Type.GetType("ChurchReport.Controllers.PaymentReturnController, SpeechMessageProducts.ChurchReport");

        newType.Should().NotBeNull("新的主要回傳 Controller 應該使用 provider-neutral 命名");
        newType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }
}
