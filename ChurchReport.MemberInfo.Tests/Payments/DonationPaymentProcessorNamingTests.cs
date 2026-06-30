using System.Reflection;
using ChurchReport.Payments;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 產品層的金流處理器命名已改為中性名稱。
/// 這組測試只鎖定型別與建構子契約，不觸發 CRM、LINE 或實際金流呼叫，
/// 目的是保證重構後主要業務流程不再以永豐 QPay 名稱作為入口。
/// </summary>
public sealed class DonationPaymentProcessorNamingTests
{
    [Fact]
    public void New_donation_payment_processor_exists_as_primary_product_workflow_processor()
    {
        var processorType = Type.GetType("ChurchReport.WebServiceConnector.DonationPaymentProcessor, ChurchReport");

        processorType.Should().NotBeNull(
            "ChurchReport 的奉獻付款產品流程應以 DonationPaymentProcessor 作為主要類別名稱");
    }

    [Fact]
    public void Legacy_qpay_processor_remains_as_compatibility_alias()
    {
        var newProcessorType = Type.GetType("ChurchReport.WebServiceConnector.DonationPaymentProcessor, ChurchReport");
        var legacyProcessorType = typeof(QPayProcessor);

        newProcessorType.Should().NotBeNull();
        legacyProcessorType.Should().BeAssignableTo(newProcessorType!,
            "舊 QPayProcessor 只能作為相容包裝，實際流程應由 DonationPaymentProcessor 承擔");
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
            "新的主要 processor 不應再要求 QPayCreatePaymentGatewayAdapter 這種永豐命名的相容 adapter");
        adapterParameters.Should().OnlyContain(parameter => !parameter.HasDefaultValue);
    }
}
