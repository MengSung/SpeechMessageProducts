/*
 * 檔案整體說明
 *
 * 目的與定位：查詢付款狀態後的通用結果。 ProviderData 用於產品 workflow；Diagnostics 用於除錯，兩者都必須由 sanitizer 清理敏感資料。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentStatusResult。
 * 資料／選項欄位包含 PaymentStatusResult、Status、ProductOrderId、ProviderOrderRef、ProviderTransactionId、PaidAmount（Money?）、
 * Error、ProviderData、Diagnostics，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * PaidAmount 是純值，不保留呼叫端、使用者、租戶或連線狀態；舊 Amount／Currency 僅作 Obsolete 相容轉接。
 * 修改欄位時須核對 provider 查詢映射與工作流程投影；此模型不執行 I/O、授權或資源管理。
 * 可沿 SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs、
 * SpeechMessage.Payments/Abstractions/IPaymentGateway.cs、SpeechMessage.Payments/Abstractions/IPaymentProvider.cs、
 * SpeechMessage.Payments/Gateway/PaymentGateway.cs、SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Localization;

namespace SpeechMessage.Payments.Models;

/// <summary>
/// 查詢付款狀態後的通用結果。
/// ProviderData 用於產品 workflow；Diagnostics 用於除錯，兩者都必須由 sanitizer 清理敏感資料。
/// </summary>
public sealed record PaymentStatusResult
{
    private Money? paidAmount;
    private string legacyCurrency = "TWD";

    /// <summary>由 provider 狀態 mapper 正規化的付款狀態。</summary>
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    /// <summary>產品端訂單識別；由查詢請求或 provider 回應投影而來。</summary>
    public string ProductOrderId { get; init; } = string.Empty;
    /// <summary>provider-neutral 交易參照，不預設其為任何一家銀行的專屬欄位。</summary>
    public string ProviderOrderRef { get; init; } = string.Empty;
    /// <summary>provider-neutral 交易流水號；沒有可信回應時為空字串。</summary>
    public string ProviderTransactionId { get; init; } = string.Empty;
    /// <summary>provider 回報的實際付款金額及 ISO 幣別；未提供可信金額時為 null。</summary>
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

    /// <summary>相容既有呼叫端的金額轉接；新程式應使用 <see cref="PaidAmount"/>。</summary>
    [Obsolete("Use PaidAmount instead.")]
    public decimal? Amount
    {
        get => paidAmount?.Amount;
        init => paidAmount = value.HasValue ? new Money(value.Value, legacyCurrency) : null;
    }

    /// <summary>相容既有呼叫端的幣別轉接；新程式應使用 <see cref="PaidAmount"/>。</summary>
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
    /// <summary>正規化查詢錯誤；訊息不得包含憑證或未清理的敏感回應。</summary>
    public PaymentError Error { get; init; } = PaymentError.None;
    /// <summary>經 provider sanitizer 處理後、供產品流程使用的延伸欄位。</summary>
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
    /// <summary>經 sanitizer 處理的診斷投影，不保存原始憑證或跨 request 狀態。</summary>
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } = new Dictionary<string, string>();
}
