/*
 * 檔案整體說明
 *
 * 目的與定位：單一 webhook 投遞的不可變資訊；重送不代表新業務事件，後續去重應使用相同 WebhookEventId。
 *
 * 主要功能與流程：
 * 本檔宣告 DeliveryContext。
 * - 是否為平台重送；缺少欄位的舊事件預設 false，不推測外部請求的信任程度。
 * - 建立事件專屬的投遞資訊，不持有 request、來源 JSON 或身分憑證。
 * 資料／選項欄位包含 DeliveryContext、IsRedelivery，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkFoundationTests.cs、Line.Messaging/Webhooks/WebhookEvent.cs 追查目前工作樹中的合作／使用位置。
 */
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
