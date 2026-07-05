// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class PaymentPostPaymentWorkflow、interface IPaymentRecordUpdater、interface IPaymentPayerNotifier、record PaymentPostPaymentWorkflowResult
// 主要成員：ExecuteAsync、RecordUpdated、PayerNotified
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
