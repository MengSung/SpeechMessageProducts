// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentWorkflowTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentPostPaymentWorkflowTests、class RecordingRecordUpdater、class RecordingPayerNotifier
// 主要成員：ExecuteAsync_updates_record_before_notifying_payer_and_keeps_product_items、UpdateAsync、NotifyAsync、SeenContext
// 引用命名空間：FluentAssertions、SpeechMessage.Payments.Models、SpeechMessage.Payments.Workflows、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
