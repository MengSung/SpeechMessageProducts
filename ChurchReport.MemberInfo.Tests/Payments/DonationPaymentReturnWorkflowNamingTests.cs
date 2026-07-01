using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentReturnWorkflowNamingTests
{
    [Fact]
    public void Legacy_qpay_workflow_result_should_not_remain_after_neutral_rename()
    {
        // CRM 更新與 LINE 通知流程已經改用 DonationPaymentWorkflowResult。
        // 舊 QPayWorkflowPaymentResult 不應再保留成 alias，避免未來高鉅、台新或新金流看起來也在依賴永豐名稱。
        var resultType = Type.GetType("ChurchReport.Payments.QPayWorkflowPaymentResult, ChurchReport");

        resultType.Should().BeNull();
    }

    [Fact]
    public void New_donation_payment_workflow_result_exists_after_rename()
    {
        // ChurchReport 產品流程應該使用 DonationPaymentWorkflowResult，而不是使用永豐專屬的 QPay 名稱。
        var resultType = Type.GetType("ChurchReport.Payments.DonationPaymentWorkflowResult, ChurchReport");

        resultType.Should().NotBeNull("新的產品中性 DTO 會成為 ChurchReport 付款回傳流程的主要型別");
    }
}
