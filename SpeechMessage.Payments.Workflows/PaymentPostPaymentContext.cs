namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// 付款後流程共用 context。
/// 共用 workflow 只知道付款結果與產品附加資料；客戶資料物件、會員資料、維修單、
/// 發票或通知對象等具體物件，都由 host product 放在 <see cref="Items"/> 中。
/// </summary>
public sealed class PaymentPostPaymentContext
{
    public PaymentPostPaymentContext(
        PaymentWorkflowResult payment,
        IReadOnlyDictionary<string, object?>? items = null)
    {
        Payment = payment ?? throw new ArgumentNullException(nameof(payment));
        Items = items ?? new Dictionary<string, object?>();
    }

    /// <summary>金流核心正規化後、可供產品流程使用的付款結果。</summary>
    public PaymentWorkflowResult Payment { get; }

    /// <summary>產品端專用資料袋；共用層不解讀內容，只原樣傳給產品實作。</summary>
    public IReadOnlyDictionary<string, object?> Items { get; }

    /// <summary>
    /// 取得產品端放入的必要資料。若 key 不存在或型別不符，會丟出清楚例外，
    /// 讓產品實作在開發期及早發現 context 組裝錯誤。
    /// </summary>
    public T GetRequiredItem<T>(string key)
    {
        if (!Items.TryGetValue(key, out var value))
        {
            throw new KeyNotFoundException($"Payment workflow context does not contain required item '{key}'.");
        }

        if (value is not T typedValue)
        {
            throw new InvalidOperationException(
                $"Payment workflow context item '{key}' is not of expected type {typeof(T).FullName}.");
        }

        return typedValue;
    }

    /// <summary>
    /// 取得可選的產品端資料。產品可能沒有付款人聯絡資料或通知對象，
    /// 這種情境不應阻止紀錄更新流程繼續完成。
    /// </summary>
    public T? GetOptionalItem<T>(string key)
    {
        if (!Items.TryGetValue(key, out var value) || value is null)
        {
            return default;
        }

        if (value is not T typedValue)
        {
            throw new InvalidOperationException(
                $"Payment workflow context item '{key}' is not of expected type {typeof(T).FullName}.");
        }

        return typedValue;
    }
}
