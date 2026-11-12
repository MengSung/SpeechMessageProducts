/*
 * 檔案整體說明
 *
 * 目的與定位：將金流核心解析完成的 callback 結果投影成產品流程容易使用的摘要模型。 此類別只整理中立欄位；付款成功後要更新維修單、會員期限、發票收款、 奉獻紀錄或發送通知，仍由各產品自行實作。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentWorkflowResultMapper、PaymentWorkflowResult。
 * - 從 PaymentCallbackResult 取出產品流程常用欄位。 Provider 原始資料保留在 PaymentWorkflowResult.ProviderData， 供產品端在必要時做對帳、紀錄或通知訊息。
 * - callback 中確認到的付款金額使用 Money；部分 provider 或失敗狀態可能為空。
 * 主要操作入口包含 Map、ReadProviderMessage。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Workflows/PaymentPostPaymentContext.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Localization;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// 將金流核心解析完成的 callback 結果投影成產品流程容易使用的摘要模型。
/// 此類別只整理中立欄位；付款成功後要更新維修單、會員期限、發票收款、
/// 奉獻紀錄或發送通知，仍由各產品自行實作。
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
            PaidAmount = result.PaidAmount,
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
    private Money? paidAmount;
    private string legacyCurrency = "TWD";

    /// <summary>金流核心正規化後的付款狀態。</summary>
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    /// <summary>產品端原始訂單號。</summary>
    public string ProductOrderId { get; init; } = string.Empty;
    /// <summary>金流供應商交易流水號或授權交易識別碼。</summary>
    public string ProviderTransactionId { get; init; } = string.Empty;
    /// <summary>callback 中確認到的付款金額及 ISO 幣別；部分 provider 或失敗狀態可能為 null。</summary>
    public Money? PaidAmount
    {
        get => paidAmount;
        init
        {
            paidAmount = value;
            if (value.HasValue)
            {
                legacyCurrency = value.Value.Currency;
            }
        }
    }

    /// <summary>相容既有產品呼叫端的金額轉接；新程式應使用 <see cref="PaidAmount"/>。</summary>
    [Obsolete("Use PaidAmount instead.")]
    public decimal? Amount
    {
        get => paidAmount?.Amount;
        init => paidAmount = value.HasValue ? new Money(value.Value, legacyCurrency) : null;
    }

    /// <summary>相容既有產品呼叫端的幣別轉接；新程式應使用 <see cref="PaidAmount"/>。</summary>
    [Obsolete("Use PaidAmount instead.")]
    public string Currency
    {
        get => paidAmount?.Currency ?? legacyCurrency;
        init
        {
            legacyCurrency = value;
            if (paidAmount.HasValue)
            {
                paidAmount = new Money(paidAmount.Value.Amount, value);
            }
        }
    }
    /// <summary>供產品端紀錄或通知使用的 provider 訊息。</summary>
    public string ProviderMessage { get; init; } = string.Empty;
    /// <summary>經過金流核心清理後的 provider 原始延伸欄位。</summary>
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
}
