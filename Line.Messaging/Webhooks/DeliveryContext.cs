namespace Line.Messaging.Webhooks;

/// <summary>單一 webhook 投遞的不可變資訊；重送不代表新業務事件，後續去重應使用相同 WebhookEventId。</summary>
public sealed class DeliveryContext
{
    /// <summary>是否為平台重送；缺少欄位的舊事件預設 false，不推測外部請求的信任程度。</summary>
    public bool IsRedelivery { get; }
    /// <summary>建立事件專屬的投遞資訊，不持有 request、來源 JSON 或身分憑證。</summary>
    /// <param name="isRedelivery">平台已驗簽內容中的重送旗標。</param>
    public DeliveryContext(bool isRedelivery) => IsRedelivery = isRedelivery;
}
