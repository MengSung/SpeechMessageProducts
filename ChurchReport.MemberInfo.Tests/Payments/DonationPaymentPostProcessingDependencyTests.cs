using ChurchReport.Payments;
using ChurchReport.Tools;
using FluentAssertions;
using SpeechMessage.Payments.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentPostProcessingDependencyTests
{
    [Fact]
    public void Donation_fee_payment_processor_accepts_common_workflow_and_presenter()
    {
        var constructorParameters = typeof(DonationFeePaymentProcessor)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameters.Should().Contain(typeof(PaymentPostPaymentWorkflow));
        constructorParameters.Should().Contain(typeof(ChurchReportPaymentContextBuilder));
        constructorParameters.Should().Contain(typeof(DonationPaymentReturnPresenter));
    }
}
