using ChurchReport.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 的奉獻付款 UI 狀態管理器已改用中性名稱。
/// 這組測試不觸發 CRM 或 LINE，只鎖定型別邊界，確保舊 QpayManager 不再是主要實作。
/// </summary>
public sealed class DonationPaymentManagerNamingTests
{
    [Fact]
    public void New_donation_payment_manager_exists_as_primary_ui_payment_state_manager()
    {
        var managerType = Type.GetType("ChurchReport.Models.DonationPaymentManager, ChurchReport");

        managerType.Should().NotBeNull(
            "ChurchReport 奉獻付款的 UI 狀態管理應以 DonationPaymentManager 作為主要名稱");
        managerType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }

    [Fact]
    public void Legacy_qpay_manager_remains_as_compatibility_alias()
    {
        var managerType = Type.GetType("ChurchReport.Models.DonationPaymentManager, ChurchReport");
        var legacyType = typeof(QpayManager);

        managerType.Should().NotBeNull();
        legacyType.Should().BeAssignableTo(managerType!,
            "QpayManager 只能保留為舊 Controller/View 的相容入口，實際管理流程應由 DonationPaymentManager 承擔");
    }
}
