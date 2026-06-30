namespace SpeechMessage.Payments.Models;

/// <summary>
/// 建立付款的通用請求。
/// 此 DTO 不使用任何 provider 專屬欄位名稱；必要的 provider 相容參數放在 Metadata，
/// 由各 provider mapper 解讀，避免宿主產品綁死在 QPay/MyPay/TSPG 模型上。
/// </summary>
public sealed record PaymentCreateRequest
{
    public string ProfileName { get; init; } = string.Empty;
    // ProviderHint 用於防呆：指定 provider 時，gateway 會檢查 profile.Provider 是否一致。
    public PaymentProviderKind? ProviderHint { get; init; }
    public string ProductOrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string Description { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string PaymentMethodSubType { get; init; } = string.Empty;
    public PaymentCallbacks Callbacks { get; init; } = new();
    public PaymentCustomer Customer { get; init; } = new();
    public IReadOnlyList<PaymentLineItem> Items { get; init; } = Array.Empty<PaymentLineItem>();
    // Metadata 承載舊流程必要但非通用的欄位，例如 QPay Param1/Param2 或 MyPay PFN。
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
