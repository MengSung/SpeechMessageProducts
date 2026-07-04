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
