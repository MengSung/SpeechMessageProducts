using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.AspNetCore;

/// <summary>
/// 將 ASP.NET Core host 收到的產品付款資料轉成通用金流核心使用的
/// <see cref="PaymentCreateRequest"/>。
/// 這個 factory 只處理 provider-neutral 欄位搬移，不知道 CRM、LINE、奉獻、
/// 發票或任何產品流程，因此可以被其他 ASP.NET Core 產品重用。
/// </summary>
public sealed class PaymentCreateRequestFactory
{
    /// <summary>
    /// 建立金流核心的付款建立請求。Host 端可以先把自己的表單或 ViewModel
    /// 投影成 <see cref="PaymentCreateRequestInput"/>，再由這裡產生核心 DTO。
    /// 真正的 provider SDK payload 仍由 <c>SpeechMessage.Payments</c> 的 provider 實作負責。
    /// </summary>
    public PaymentCreateRequest Create(PaymentCreateRequestInput input)
    {
        return new PaymentCreateRequest
        {
            ProfileName = input.ProfileName,
            ProductOrderId = input.ProductOrderId,
            Amount = input.Amount,
            Currency = input.Currency,
            Description = input.Description,
            PaymentMethod = input.PaymentMethod,
            PaymentMethodSubType = input.PaymentMethodSubType,
            Callbacks = input.Callbacks,
            Customer = input.Customer,
            Items = input.Items,
            Metadata = input.Metadata
        };
    }
}

/// <summary>
/// Host 應用程式傳入的付款建立資料。
/// 產品專屬識別，例如 CRM Id、會員 Id、發票 Id 或維修單 Id，應放在
/// <see cref="Metadata"/>，不要把產品欄位新增到通用金流核心模型。
/// </summary>
public sealed record PaymentCreateRequestInput
{
    /// <summary>要使用的金流 profile 名稱，通常對應 appsettings 的 Payment:Profiles 設定。</summary>
    public string ProfileName { get; init; } = string.Empty;

    /// <summary>產品端訂單或收費單編號。</summary>
    public string ProductOrderId { get; init; } = string.Empty;

    /// <summary>付款金額，單位由 host 與 provider profile 約定，台灣金流通常為新台幣元。</summary>
    public decimal Amount { get; init; }

    /// <summary>幣別，預設 TWD。</summary>
    public string Currency { get; init; } = "TWD";

    /// <summary>顯示給付款者或 provider 後台的付款描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>付款方式，例如信用卡、ATM、LinePay；provider 會再轉成自己的協定值。</summary>
    public string PaymentMethod { get; init; } = string.Empty;

    /// <summary>付款方式子類型，例如一次付清、定期定額或行動支付子類別。</summary>
    public string PaymentMethodSubType { get; init; } = string.Empty;

    /// <summary>付款完成、失敗、取消與 callback 使用的 URL。</summary>
    public PaymentCallbacks Callbacks { get; init; } = new();

    /// <summary>付款者資料，維持 provider-neutral，不承載產品專屬 contact 型別。</summary>
    public PaymentCustomer Customer { get; init; } = new();

    /// <summary>付款項目清單；若產品沒有明細，可由 host 建立單一明細。</summary>
    public IReadOnlyList<PaymentLineItem> Items { get; init; } = Array.Empty<PaymentLineItem>();

    /// <summary>產品端需要帶過金流核心邊界的附加資料，例如 CRM fee id 或付款者內部 id。</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
