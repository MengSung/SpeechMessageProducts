using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentReturnWorkflowNamingTests
{
    [Fact]
    public void Legacy_qpay_workflow_result_remains_available_during_rename()
    {
        // 這個測試保護舊程式碼與尚未改名的呼叫點：
        // 第一階段改名時，不能直接移除 QPayWorkflowPaymentResult，否則舊的 CRM/LINE 回傳流程會立刻中斷。
        var resultType = Type.GetType("ChurchReport.Payments.QPayWorkflowPaymentResult, ChurchReport");

        resultType.Should().NotBeNull("第一階段必須保留舊 QPay DTO 作為相容層");
    }

    [Fact]
    public void New_donation_payment_workflow_result_exists_after_rename()
    {
        // 這個測試描述目標狀態：
        // ChurchReport 產品流程應該使用 DonationPaymentWorkflowResult，而不是使用永豐專屬的 QPay 名稱。
        var resultType = Type.GetType("ChurchReport.Payments.DonationPaymentWorkflowResult, ChurchReport");

        resultType.Should().NotBeNull("新的產品中性 DTO 會成為 ChurchReport 付款回傳流程的主要型別");
    }
}
