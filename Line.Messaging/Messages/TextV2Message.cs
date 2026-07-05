// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/TextV2Message.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class TextV2Message
// 主要成員：Type、QuickReply、Text、QuoteToken
// 引用命名空間：System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// 表示 LINE 官方 Messaging API 的 textV2 訊息。
    /// 這個模型先承接官方 JSON 形狀，產品友善的建立方法放在 LineMessagingProcessor.Workflows。
    /// </summary>
    public class TextV2Message : ISendMessage
    {
        public MessageType Type { get; } = MessageType.TextV2;

        public QuickReply QuickReply { get; set; }

        public string Text { get; }

        public IDictionary<string, object> Substitution { get; }

        public string QuoteToken { get; }

        public TextV2Message(
            string text,
            IDictionary<string, object> substitution = null,
            string quoteToken = null,
            QuickReply quickReply = null)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            Text = text.Substring(0, Math.Min(text.Length, 5000));
            Substitution = substitution;
            QuoteToken = quoteToken;
            QuickReply = quickReply;
        }
    }
}
