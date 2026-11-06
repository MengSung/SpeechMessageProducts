/*
 * 檔案整體說明
 *
 * 目的與定位：驗證共用付款後流程管線只負責編排「更新紀錄」與「通知付款者」， 實際 CRM、LINE、維修單、會員、發票等產品細節仍由各產品自己的 handler 實作。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentPostPaymentWorkflowTests、RecordingRecordUpdater、RecordingPayerNotifier。
 * 測試案例入口包含 ExecuteAsync_updates_record_before_notifying_payer_and_keeps_product_items、UpdateAsync、NotifyAsync。
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
/// 驗證共用付款後流程管線只負責編排「更新紀錄」與「通知付款者」，
/// 實際 CRM、LINE、維修單、會員、發票等產品細節仍由各產品自己的 handler 實作。
/// </summary>
public sealed class PaymentPostPaymentWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_updates_record_before_notifying_payer_and_keeps_product_items()
    {
        var calls = new List<string>();
        var updater = new RecordingRecordUpdater(calls);
        var notifier = new RecordingPayerNotifier(calls);
        var workflow = new PaymentPostPaymentWorkflow(
            new[] { updater },
            new[] { notifier });
        var result = new PaymentWorkflowResult
        {
            Status = PaymentStatus.Succeeded,
            ProductOrderId = "ORDER-001",
            ProviderTransactionId = "TX-001",
            Amount = 1200m,
            Currency = "TWD"
        };
        var context = new PaymentPostPaymentContext(
            result,
            new Dictionary<string, object?>
            {
                ["ProductRecord"] = "CRM-FEE-001"
            });

        var workflowResult = await workflow.ExecuteAsync(context);

        calls.Should().Equal("update:ORDER-001:CRM-FEE-001", "notify:ORDER-001:CRM-FEE-001");
        updater.SeenContext.Should().BeSameAs(context);
        notifier.SeenContext.Should().BeSameAs(context);
        workflowResult.RecordUpdated.Should().BeTrue();
        workflowResult.PayerNotified.Should().BeTrue();
    }

    private sealed class RecordingRecordUpdater : IPaymentRecordUpdater
    {
        private readonly List<string> _calls;

        public RecordingRecordUpdater(List<string> calls)
        {
            _calls = calls;
        }

        public PaymentPostPaymentContext? SeenContext { get; private set; }

        public Task UpdateAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default)
        {
            SeenContext = context;
            _calls.Add($"update:{context.Payment.ProductOrderId}:{context.GetRequiredItem<string>("ProductRecord")}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPayerNotifier : IPaymentPayerNotifier
    {
        private readonly List<string> _calls;

        public RecordingPayerNotifier(List<string> calls)
        {
            _calls = calls;
        }

        public PaymentPostPaymentContext? SeenContext { get; private set; }

        public Task NotifyAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default)
        {
            SeenContext = context;
            _calls.Add($"notify:{context.Payment.ProductOrderId}:{context.GetRequiredItem<string>("ProductRecord")}");
            return Task.CompletedTask;
        }
    }
}
