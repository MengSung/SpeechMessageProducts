/*
 * 檔案整體說明
 *
 * 目的與定位：provider callback 解析後的通用結果。 Acknowledgement 描述 provider 需要的回應格式，宿主產品只負責轉成實際 web response。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentCallbackResult。
 * 資料／選項欄位包含 PaymentCallbackResult、Status、ProductOrderId、ProviderTransactionId、PaidAmount（Money?）、Acknowledgement、
 * Error、ProviderData、Diagnostics，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * PaidAmount 是純值，不保留呼叫端、使用者、租戶或連線狀態；舊 Amount／Currency 僅作 Obsolete 相容轉接。
 * 修改欄位時須核對 provider callback parser 與工作流程投影；此模型不執行 I/O、授權或資源管理。
 * 可沿 SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs、
 * SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments/Abstractions/IPaymentGateway.cs、SpeechMessage.Payments/Abstractions/IPaymentProvider.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Localization;

namespace SpeechMessage.Payments.Models;

/// <summary>
/// provider callback 解析後的通用結果。
/// Acknowledgement 描述 provider 需要的回應格式，宿主產品只負責轉成實際 web response。
/// </summary>
public sealed record PaymentCallbackResult
{
    private Money? paidAmount;
    private string legacyCurrency = "TWD";

    /// <summary>callback parser 正規化的付款狀態；外部回呼本身不直接授權 CRM 回寫。</summary>
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    /// <summary>provider 回呼中解析出的產品訂單識別。</summary>
    public string ProductOrderId { get; init; } = string.Empty;
    /// <summary>provider 交易流水識別；不得當作使用者或商店授權。</summary>
    public string ProviderTransactionId { get; init; } = string.Empty;
    /// <summary>callback 確認的付款金額及 ISO 幣別；callback 未提供可信金額時為 null。</summary>
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
    // 不同 provider 可能要回 plain text、JSON 或 redirect，規則由核心 parser 決定。
    /// <summary>provider 所需回應形狀的純資料描述，由宿主轉為實際 HTTP response。</summary>
    public PaymentCallbackAcknowledgement Acknowledgement { get; init; } = PaymentCallbackAcknowledgement.None;
    /// <summary>正規化 callback 錯誤；診斷不得洩漏 token、金鑰或原始個資。</summary>
    public PaymentError Error { get; init; } = PaymentError.None;
    /// <summary>已清理的 provider 延伸欄位，只供產品 workflow 讀取。</summary>
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
    /// <summary>已清理的回呼診斷投影；不保留 request 物件或原始秘密。</summary>
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } = new Dictionary<string, string>();
}
