/*
 * 檔案整體說明
 *
 * 目的與定位：驗證付款成功／失敗呈現設定穩定ViewBag欄位，讓舊Razor仍可讀取一致結果。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentReturnPresenterTests、TestController。
 * 測試案例入口包含 PresentSuccess_sets_stable_view_bag_values、PresentFailure_sets_error_details。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
