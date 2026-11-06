/*
 * 檔案整體說明
 *
 * 目的與定位：表示 LINE 官方 Messaging API 的 textV2 訊息。 這個模型先承接官方 JSON 形狀，產品友善的建立方法放在 LineMessagingProcessor.Workflows。
 *
 * 主要功能與流程：
 * 本檔宣告 TextV2Message。
 * 資料／選項欄位包含 Type、QuickReply、Text、Substitution、QuoteToken，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineMessageModelTests.cs 追查目前工作樹中的合作／使用位置。
 */
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
