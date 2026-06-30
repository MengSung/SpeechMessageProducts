namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// 付款後流程管線。它只負責編排共用步驟：先更新付款紀錄，再通知付款者。
/// 實際怎麼更新產品資料庫、怎麼發即時訊息、Email 或簡訊，全部由產品端 handler 實作。
/// </summary>
public sealed class PaymentPostPaymentWorkflow
{
    private readonly IReadOnlyList<IPaymentRecordUpdater> _recordUpdaters;
    private readonly IReadOnlyList<IPaymentPayerNotifier> _payerNotifiers;

    public PaymentPostPaymentWorkflow(
        IEnumerable<IPaymentRecordUpdater> recordUpdaters,
        IEnumerable<IPaymentPayerNotifier> payerNotifiers)
    {
        _recordUpdaters = recordUpdaters?.ToArray() ?? throw new ArgumentNullException(nameof(recordUpdaters));
        _payerNotifiers = payerNotifiers?.ToArray() ?? throw new ArgumentNullException(nameof(payerNotifiers));
    }

    /// <summary>
    /// 執行付款後流程。更新紀錄永遠早於通知付款者，避免使用者收到成功通知時，
    /// 產品系統尚未完成收款狀態更新。
    /// </summary>
    public async Task<PaymentPostPaymentWorkflowResult> ExecuteAsync(
        PaymentPostPaymentContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var updater in _recordUpdaters)
        {
            await updater.UpdateAsync(context, cancellationToken);
        }

        foreach (var notifier in _payerNotifiers)
        {
            await notifier.NotifyAsync(context, cancellationToken);
        }

        return new PaymentPostPaymentWorkflowResult
        {
            RecordUpdated = _recordUpdaters.Count > 0,
            PayerNotified = _payerNotifiers.Count > 0
        };
    }
}

/// <summary>
/// 產品端付款紀錄更新介面，例如更新收費單、維修單、會員效期或發票收款狀態。
/// </summary>
public interface IPaymentRecordUpdater
{
    Task UpdateAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 付款者通知介面，例如即時訊息、Email、簡訊或產品內通知。
/// </summary>
public interface IPaymentPayerNotifier
{
    Task NotifyAsync(PaymentPostPaymentContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 付款後流程執行結果，用於測試、記錄或後續擴充錯誤處理策略。
/// </summary>
public sealed record PaymentPostPaymentWorkflowResult
{
    public bool RecordUpdated { get; init; }
    public bool PayerNotified { get; init; }
}
