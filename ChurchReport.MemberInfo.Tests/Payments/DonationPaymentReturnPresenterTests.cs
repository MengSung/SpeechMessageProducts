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
