using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentNamingCompatibilityTests
{
    [Fact]
    public void Old_qpay_card_controller_remains_available_as_route_alias_during_migration()
    {
        // 舊的 QPayCardController 仍然承接既有金流回傳 URL。
        // 這是外部金流設定與舊連結的相容層，不代表新程式還應該以 QPay 命名為主。
        var legacyType = Type.GetType("ChurchReport.Controllers.QPayCardController, ChurchReport");

        legacyType.Should().NotBeNull();
        legacyType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }

    [Fact]
    public void New_payment_return_controller_exists_after_rename()
    {
        // 這個測試描述目標狀態：
        // 新的主要 controller 名稱應該描述「付款回傳端點」，而不是描述特定金流供應商。
        var newType = Type.GetType("ChurchReport.Controllers.PaymentReturnController, ChurchReport");

        newType.Should().NotBeNull("新的主要回傳 Controller 應該使用 provider-neutral 命名");
        newType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }
}
