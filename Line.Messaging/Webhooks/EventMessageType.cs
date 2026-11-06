/*
 * 檔案整體說明
 *
 * 目的與定位：本檔列出webhook訊息內容型別，供解析器挑選資料模型。
 *
 * 主要功能與流程：
 * 本檔宣告 EventMessageType。
 * 資料／選項欄位包含 EventMessageType，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/EventMessage.cs、Line.Messaging/Webhooks/FileEventMessage.cs、
 * Line.Messaging/Webhooks/LocationEventMessage.cs、Line.Messaging/Webhooks/MidiaEventMessage.cs、
 * Line.Messaging/Webhooks/StickerEventMessage.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Webhook event message types.
    /// </summary>
    public enum EventMessageType
    {
        Text,
        Image,
        Video,
        Audio,
        Location,
        Sticker,
        File
    }
}
