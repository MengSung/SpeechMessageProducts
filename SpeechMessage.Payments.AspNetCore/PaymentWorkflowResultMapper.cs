using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.AspNetCore;

/// <summary>
/// 將金流核心解析完成的 callback 結果投影成產品流程容易使用的摘要模型。
/// 此類別只整理中立欄位；付款成功後要更新維修單、會員期限、發票收款、
/// 奉獻紀錄或發送通知，仍由各 ASP.NET Core 產品自行負責。
/// </summary>
public sealed class PaymentWorkflowResultMapper
{
    /// <summary>
    /// 從 <see cref="PaymentCallbackResult"/> 取出產品流程常用欄位。
    /// Provider 原始資料保留在 <see cref="PaymentWorkflowResult.ProviderData"/>，
    /// 供產品端在必要時做對帳、紀錄或通知訊息。
    /// </summary>
    public PaymentWorkflowResult Map(PaymentCallbackResult result)
    {
        return new PaymentWorkflowResult
        {
            Status = result.Status,
            ProductOrderId = result.ProductOrderId,
            ProviderTransactionId = result.ProviderTransactionId,
            Amount = result.Amount,
            Currency = result.Currency,
            ProviderMessage = ReadProviderMessage(result.ProviderData),
            ProviderData = result.ProviderData
        };
    }

    private static string ReadProviderMessage(IReadOnlyDictionary<string, string> providerData)
    {
        // 多數 provider parser 會把主要訊息正規化成 provider_message；
        // message 是相容部分舊 payload 或測試資料的 fallback。
        if (providerData.TryGetValue("provider_message", out var providerMessage))
        {
            return providerMessage;
        }

        return providerData.TryGetValue("message", out var message) ? message : string.Empty;
    }
}

/// <summary>
/// 產品流程友善的付款 callback 結果。
/// 共用層只負責狀態、訂單號、金額、交易流水號與 provider 訊息的正規化；
/// 各產品仍自行決定成功、失敗、待處理時要更新哪些資料。
/// </summary>
public sealed record PaymentWorkflowResult
{
    /// <summary>金流核心正規化後的付款狀態。</summary>
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    /// <summary>產品端原始訂單號。</summary>
    public string ProductOrderId { get; init; } = string.Empty;
    /// <summary>金流供應商交易流水號或授權交易識別碼。</summary>
    public string ProviderTransactionId { get; init; } = string.Empty;
    /// <summary>callback 中確認到的付款金額；部分 provider 或失敗狀態可能為空。</summary>
    public decimal? Amount { get; init; }
    /// <summary>付款幣別，預設為 TWD。</summary>
    public string Currency { get; init; } = "TWD";
    /// <summary>供產品端紀錄或通知使用的 provider 訊息。</summary>
    public string ProviderMessage { get; init; } = string.Empty;
    /// <summary>經過金流核心清理後的 provider 原始延伸欄位。</summary>
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
}
