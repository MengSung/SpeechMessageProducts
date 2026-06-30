using System.Reflection;
using ChurchReport.Controllers;
using ChurchReport.Payments;
using ChurchReport.Tools;
using FluentAssertions;
using SpeechMessage.Payments.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class PaymentPostPaymentArchitectureTests
{
    [Fact]
    public void Tspg_controller_depends_on_common_post_payment_workflow()
    {
        typeof(TSPGController)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(PaymentPostPaymentWorkflow));
    }

    [Fact]
    public void Donation_fee_payment_processor_depends_on_common_post_payment_workflow()
    {
        typeof(DonationFeePaymentProcessor)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(PaymentPostPaymentWorkflow));
    }

    [Fact]
    public void Donation_fee_payment_processor_keeps_mvc_presentation_in_churchreport()
    {
        typeof(DonationFeePaymentProcessor)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(DonationPaymentReturnPresenter));
    }

    [Fact]
    public void ChurchReport_specific_handlers_do_not_move_to_reusable_workflow_project()
    {
        typeof(ChurchReportPaymentRecordUpdater).Assembly.GetName().Name.Should().Be("ChurchReport");
        typeof(ChurchReportPaymentPayerNotifier).Assembly.GetName().Name.Should().Be("ChurchReport");
    }
}
