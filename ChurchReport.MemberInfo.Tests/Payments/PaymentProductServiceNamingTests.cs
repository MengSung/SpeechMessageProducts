using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 的付款後產品流程服務不再以單一 provider 命名。
/// CRM 更新、LINE 通知、付款訊息組裝都屬於 ChurchReport 產品流程，
/// 服務名稱應表達「產品付款後處理」而不是誤導成 MyPay 金流協定實作。
/// </summary>
public sealed class PaymentProductServiceNamingTests
{
    [Fact]
    public void Product_payment_services_use_provider_neutral_names()
    {
        var expectedServiceNames = new[]
        {
            "ChurchReport.Services.PaymentCrmService, ChurchReport",
            "ChurchReport.Services.PaymentFeeTypeHelper, ChurchReport",
            "ChurchReport.Services.PaymentCallbackLogger, ChurchReport",
            "ChurchReport.Services.PaymentMessageBuilder, ChurchReport",
            "ChurchReport.Services.PaymentNotificationService, ChurchReport"
        };

        foreach (var expectedServiceName in expectedServiceNames)
        {
            Type.GetType(expectedServiceName).Should().NotBeNull(
                "ChurchReport 付款後流程服務應以 provider-neutral 名稱呈現，避免把 CRM/LINE/收費單流程誤認為 MyPay 協定實作");
        }
    }

    [Fact]
    public void Legacy_mypay_product_service_names_are_removed()
    {
        var legacyServiceNames = new[]
        {
            "ChurchReport.Services.MyPayCrmService, ChurchReport",
            "ChurchReport.Services.MyPayFeeTypeHelper, ChurchReport",
            "ChurchReport.Services.MyPayLogger, ChurchReport",
            "ChurchReport.Services.MyPayMessageBuilder, ChurchReport",
            "ChurchReport.Services.MyPayNotificationService, ChurchReport"
        };

        foreach (var legacyServiceName in legacyServiceNames)
        {
            Type.GetType(legacyServiceName).Should().BeNull(
                "這些服務已經只消費 provider-neutral payment result，不應保留 MyPay 開頭的產品流程型別名稱");
        }
    }
}
