/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 callback workflow 結果投影已移到 SpeechMessage.Payments.AspNetCore， 且仍能保留 ChurchReport 後續 CRM/LINE 流程需要的核心欄位。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentWorkflowResultMapperTests。
 * 測試案例入口包含 Map_reads_neutral_callback_result_fields。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using FluentAssertions;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows;
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
