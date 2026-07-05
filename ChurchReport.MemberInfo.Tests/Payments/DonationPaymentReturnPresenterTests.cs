// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnPresenterTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentReturnPresenterTests、class TestController
// 主要成員：PresentSuccess_sets_stable_view_bag_values、PresentFailure_sets_error_details
// 引用命名空間：ChurchReport.Payments、FluentAssertions、Microsoft.AspNetCore.Mvc、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Payments;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentReturnPresenterTests
{
    [Fact]
    public void PresentSuccess_sets_stable_view_bag_values()
    {
        var presenter = new DonationPaymentReturnPresenter();
        var controller = new TestController();

        var result = presenter.PresentSuccess(
            controller,
            fullName: "王小明",
            amount: "800",
            orderId: "D202606300001",
            transactionId: "TX-DONATION-001",
            dedicationCategory: "十一奉獻",
            message: "付款成功");

        result.Should().BeOfType<ViewResult>();
        ((bool)controller.ViewBag.IsSuccess).Should().BeTrue();
        ((string)controller.ViewBag.FullName).Should().Be("王小明");
        ((string)controller.ViewBag.Amount).Should().Be("800");
        ((string)controller.ViewBag.OrderId).Should().Be("D202606300001");
        ((string)controller.ViewBag.TransactionId).Should().Be("TX-DONATION-001");
        ((string)controller.ViewBag.PaymentMethod).Should().Be("信用卡");
        ((string)controller.ViewBag.DedicationCategory).Should().Be("十一奉獻");
        ((ViewResult)result).ViewName.Should().Be("~/Views/PaymentReturn/PaymentResult.cshtml");
    }

    [Fact]
    public void PresentFailure_sets_error_details()
    {
        var presenter = new DonationPaymentReturnPresenter();
        var controller = new TestController();

        var result = presenter.PresentFailure(
            controller,
            fullName: "王小明",
            orderId: "D202606300002",
            errorDetails: "銀行拒絕交易",
            message: "付款失敗");

        result.Should().BeOfType<ViewResult>();
        ((bool)controller.ViewBag.IsSuccess).Should().BeFalse();
        ((string)controller.ViewBag.FullName).Should().Be("王小明");
        ((string)controller.ViewBag.OrderId).Should().Be("D202606300002");
        ((string)controller.ViewBag.ErrorDetails).Should().Be("銀行拒絕交易");
        ((ViewResult)result).ViewName.Should().Be("~/Views/PaymentReturn/PaymentResult.cshtml");
    }

    private sealed class TestController : Controller
    {
    }
}
