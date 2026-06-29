using FluentAssertions;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 callback workflow 結果投影已移到 SpeechMessage.Payments.AspNetCore，
/// 且仍能保留 ChurchReport 後續 CRM/LINE 流程需要的核心欄位。
/// </summary>
public sealed class PaymentWorkflowResultMapperTests
{
    [Fact]
    public void Map_reads_neutral_callback_result_fields()
    {
        var result = new PaymentCallbackResult
        {
            Status = PaymentStatus.Succeeded,
            ProductOrderId = "F202606250001",
            ProviderTransactionId = "provider-tx-1",
            Amount = 1200m,
            Currency = "TWD",
            ProviderData = new Dictionary<string, string>
            {
                ["provider_message"] = "approved"
            }
        };
        var mapper = new PaymentWorkflowResultMapper();

        var workflow = mapper.Map(result);

        workflow.Status.Should().Be(PaymentStatus.Succeeded);
        workflow.ProductOrderId.Should().Be("F202606250001");
        workflow.ProviderTransactionId.Should().Be("provider-tx-1");
        workflow.Amount.Should().Be(1200m);
        workflow.ProviderMessage.Should().Be("approved");
    }
}
