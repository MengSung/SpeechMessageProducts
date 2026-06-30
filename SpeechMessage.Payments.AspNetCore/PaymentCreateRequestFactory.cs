using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.AspNetCore;

/// <summary>
/// 將各 ASP.NET Core 產品整理好的付款資料轉成金流核心可理解的
/// <see cref="PaymentCreateRequest"/>。
/// 這裡只處理 provider-neutral 欄位，不引用任何產品專案、CRM、LINE、
/// 維修單、會員、發票等產品模型，讓不同產品可以重用同一個付款建立邊界。
/// </summary>
public sealed class PaymentCreateRequestFactory
{
    /// <summary>
    /// 建立金流核心的付款請求。呼叫端應先把自己的產品流程資料整理成
    /// <see cref="PaymentCreateRequestInput"/>，本方法只做欄位投影，不做
    /// provider SDK payload 組裝，也不決定付款成功後的產品流程。
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
/// Host 端建立付款時使用的中立輸入模型。
/// 維修單號、會員編號、發票號碼、奉獻 CRM Id 等產品識別資料，應透過
/// <see cref="Metadata"/> 傳遞，避免共用金流專案反向依賴任何單一產品。
/// </summary>
public sealed record PaymentCreateRequestInput
{
    /// <summary>要使用的付款 profile 名稱，對應 appsettings 的 Payment:Profiles 設定。</summary>
    public string ProfileName { get; init; } = string.Empty;
    /// <summary>產品端自己的訂單號或交易識別碼。</summary>
    public string ProductOrderId { get; init; } = string.Empty;
    /// <summary>付款金額，使用主要幣別單位，例如新台幣 100 元即為 100。</summary>
    public decimal Amount { get; init; }
    /// <summary>幣別，預設為 TWD。</summary>
    public string Currency { get; init; } = "TWD";
    /// <summary>顯示給付款人或金流平台的商品/付款描述。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>付款方式，例如信用卡、ATM、LinePay；實際值由 provider 轉換層解讀。</summary>
    public string PaymentMethod { get; init; } = string.Empty;
    /// <summary>付款方式子類型，例如一次付清或定期定額。</summary>
    public string PaymentMethodSubType { get; init; } = string.Empty;
    /// <summary>前景返回、背景通知、成功與失敗導向 URL。</summary>
    public PaymentCallbacks Callbacks { get; init; } = new();
    /// <summary>付款人資訊，保持 provider-neutral，避免帶入特定供應商 DTO。</summary>
    public PaymentCustomer Customer { get; init; } = new();
    /// <summary>付款項目明細；若產品沒有明細，可由呼叫端建立單一預設項目。</summary>
    public IReadOnlyList<PaymentLineItem> Items { get; init; } = Array.Empty<PaymentLineItem>();
    /// <summary>產品流程需要回傳保存的延伸資料，例如產品 Id、分類或對帳欄位。</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
