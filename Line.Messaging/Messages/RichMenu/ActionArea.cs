// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/RichMenu/ActionArea.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class ActionArea
// 主要成員：CreateFrom、ParseTemplateAction、Bounds、Action
// 引用命名空間：System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
