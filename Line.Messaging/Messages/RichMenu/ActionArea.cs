/*
 * 檔案整體說明
 *
 * 目的與定位：RichMenu 可點擊區域。 https://developers.line.me/en/docs/messaging-api/reference/#area-object ActionArea 將
 * RichMenu 圖片上的矩形範圍綁定到一個 LINE template action。
 *
 * 主要功能與流程：
 * 本檔宣告 ActionArea。
 * - 以像素描述可點擊範圍邊界的物件。 Bounds 必須落在 RichMenu 圖片尺寸內；除非設計上刻意依賴 LINE 的區域排序判定，否則不應互相重疊。
 * - 使用者點擊此區域時執行的 action。 RichMenu action 不支援顯示 label；目前支援 message、URI、postback、datetime picker、RichMenu switch 與
 * clipboard。
 * 主要操作入口包含 CreateFrom、ParseTemplateAction。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkGoldenTests.cs、Line.Messaging/LineMessagingClient.cs、
 * Line.Messaging/LineObjects/ImagemapSize.cs、Line.Messaging/Messages/Action/TemplateActionType.cs、
 * Line.Messaging/Messages/RichMenu/ResponseRichMenu.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// RichMenu 可點擊區域。
    /// https://developers.line.me/en/docs/messaging-api/reference/#area-object
    /// ActionArea 將 RichMenu 圖片上的矩形範圍綁定到一個 LINE template action。
    /// </summary>
    public class ActionArea
    {
        /// <summary>
        /// 以像素描述可點擊範圍邊界的物件。
        /// Bounds 必須落在 RichMenu 圖片尺寸內；除非設計上刻意依賴 LINE 的區域排序判定，否則不應互相重疊。
        /// </summary>
        public ImagemapArea Bounds { get; set; }

        /// <summary>
        /// 使用者點擊此區域時執行的 action。
        /// RichMenu action 不支援顯示 label；目前支援 message、URI、postback、datetime picker、RichMenu switch 與 clipboard。
        /// </summary>
        public ITemplateAction Action { get; set; }

        internal static ActionArea CreateFrom(dynamic dynamicObject)
        {
            // LINE provider response 的結構與建立 payload 相近，但這裡是 dynamic JSON。
            // 防禦式解析座標，缺少數字欄位時預設為 0，避免 parser 直接丟例外。
            return new ActionArea()
            {
                Bounds = new ImagemapArea(
                    (int)(dynamicObject?.bounds?.x ?? 0),
                    (int)(dynamicObject?.bounds?.y ?? 0),
                    (int)(dynamicObject?.bounds?.width ?? 0),
                    (int)(dynamicObject?.bounds?.height ?? 0)),
                Action = ParseTemplateAction(dynamicObject?.action)
            };
        }

        public static ITemplateAction ParseTemplateAction(dynamic dynamicObject)
        {
            // LINE action type 字串決定要建立哪個具體 action 物件。
            // 未來 SDK 新增 action type 時，這個 switch 必須與 TemplateActionType 同步更新。
            var type = (TemplateActionType)System.Enum.Parse(typeof(TemplateActionType), (string)dynamicObject?.type, true);
            switch (type)
            {
                case TemplateActionType.Message:
                    return MessageTemplateAction.CreateFrom(dynamicObject);
                case TemplateActionType.Uri:
                    return UriTemplateAction.CreateFrom(dynamicObject);
                case TemplateActionType.Postback:
                    return PostbackTemplateAction.CreateFrom(dynamicObject);
                case TemplateActionType.Datetimepicker:
                    return DateTimePickerTemplateAction.CreateFrom(dynamicObject);
                case TemplateActionType.RichMenuSwitch:
                    return RichMenuSwitchTemplateAction.CreateFrom(dynamicObject);
                case TemplateActionType.Clipboard:
                    return ClipboardTemplateAction.CreateFrom(dynamicObject);
                default:
                    return null;
            }
        }
    }
}
