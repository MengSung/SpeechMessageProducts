using System.Reflection;
using ChurchReport.Payments;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 確認 ChurchReport 的奉獻付款 processor 已經使用 DonationPayment 命名。
///
/// DonationPaymentProcessor 是 ChurchReport 產品層的付款後流程處理器，
/// 會處理 CRM fee、奉獻收據、LINE 通知與頁面回傳。這些都不是永豐 QPay 協定本身。
/// 因此 processor 不能再提供 QPayProcessor alias，否則未來高鉅、台新或其他產品共用流程時，
/// 會看起來像是所有付款都必須經過永豐 QPay。
/// </summary>
public sealed class DonationPaymentProcessorNamingTests
{
    [Fact]
    public void New_donation_payment_processor_exists_as_primary_product_workflow_processor()
    {
        var processorType = Type.GetType("ChurchReport.WebServiceConnector.DonationPaymentProcessor, ChurchReport");

        processorType.Should().NotBeNull(
            "ChurchReport 產品層付款後流程應由 DonationPaymentProcessor 作為主要類別名稱");
    }

    [Fact]
    public void Legacy_qpay_processor_alias_should_not_remain()
    {
        Type.GetType("ChurchReport.WebServiceConnector.QPayProcessor, ChurchReport").Should().BeNull(
            "QPayProcessor 是產品層舊 alias；重構後應直接使用 DonationPaymentProcessor，" +
            "舊外部 URL 可由 route 保留，但 C# 類別名稱不應再保留 QPay alias");
    }

    [Fact]
    public void Donation_payment_processor_constructors_require_neutral_gateway_create_adapter()
    {
        var processorType = Type.GetType("ChurchReport.WebServiceConnector.DonationPaymentProcessor, ChurchReport");

        processorType.Should().NotBeNull();
        var adapterParameters = processorType!
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => parameter.ParameterType == typeof(DonationPaymentCreateGatewayAdapter))
            .ToArray();

        adapterParameters.Should().NotBeEmpty(
            "新的主要 processor 應要求 DonationPaymentCreateGatewayAdapter，避免重新引入 QPay 命名的 create adapter");
        adapterParameters.Should().OnlyContain(parameter => !parameter.HasDefaultValue);
    }
}
